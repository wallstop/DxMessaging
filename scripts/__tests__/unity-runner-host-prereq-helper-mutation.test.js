/**
 * @fileoverview Mutation-resistance behavioral tests for the host-prereq
 * helper functions in scripts/unity/bootstrap-windows-runner.ps1.
 *
 * WHY: per the adversarial-review-loop working style, every guard must be
 * MUTATION-TESTED -- a refactor that swaps `return $true` for `return $false`
 * (or that drops the regex anchor that rejects single-drive roots) must fail
 * loudly. The static-contract test pins the SOURCE shape; this file pins the
 * RUNTIME BEHAVIOR by dot-sourcing the bootstrap script into a hermetic pwsh
 * session and invoking each helper with adversarial inputs.
 *
 * Mirrors the AST-extract-then-dot-source pattern used by
 * unity-ensure-editor-il2cpp-idempotency.test.js (which also targets
 * top-level-side-effect-free function inspection), but we go one step lighter
 * here: the bootstrap script DECLARES its dot-source-safety contract
 * explicitly (see scripts/unity/bootstrap-windows-runner.ps1's "Import
 * contract" section in the .DESCRIPTION block) and the companion suite
 * unity-runner-host-prereq-dot-source-safety.test.js verifies that contract
 * holds. So we can dot-source the WHOLE file rather than AST-extracting one
 * function at a time -- the contract makes that side-effect-free.
 *
 * Each test runs in one pwsh process (a single dot-source + a sequence of
 * Write-Host outcomes) so the total wall-clock cost is one spawn per test
 * description, not one per assertion. The single-process pattern mirrors the
 * style used elsewhere in this directory and keeps the suite fast.
 */

"use strict";

const fs = require("fs");
const os = require("os");
const path = require("path");
const { spawnSync } = require("child_process");

const REPO_ROOT = path.resolve(__dirname, "..", "..");
const BOOTSTRAP_SCRIPT = path.join(REPO_ROOT, "scripts", "unity", "bootstrap-windows-runner.ps1");

function pwshAvailable() {
  const probe = spawnSync("pwsh", ["-NoProfile", "-NonInteractive", "-Command", "exit 0"], {
    encoding: "utf8"
  });
  return probe.status === 0;
}

const PWSH_PRESENT = pwshAvailable();

if (!PWSH_PRESENT) {
  // eslint-disable-next-line no-console
  console.warn(
    "[unity-runner-host-prereq-helper-mutation] pwsh not found on PATH; skipping behavioral assertions. CI runners always have pwsh."
  );
}

const workspaces = [];

afterAll(() => {
  for (const ws of workspaces) {
    try {
      fs.rmSync(ws, { recursive: true, force: true });
    } catch {
      // best-effort cleanup; never fail the suite on teardown.
    }
  }
});

function makeWorkspace() {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "dxm-bootstrap-helper-mutation-"));
  workspaces.push(dir);
  return dir;
}

function combined(result) {
  return ((result.stdout || "") + "\n" + (result.stderr || "")).replace(/\r\n/g, "\n");
}

// Run a pwsh script that dot-sources the bootstrap script, then executes the
// given body. Returns the spawn result.
function runHarness(body) {
  const escapedScript = BOOTSTRAP_SCRIPT.replace(/'/g, "''");
  const workspace = makeWorkspace();
  const harness = path.join(workspace, "harness.ps1");
  fs.writeFileSync(
    harness,
    [
      "# Dot-source contract is verified by unity-runner-host-prereq-dot-source-safety.test.js;",
      "# we rely on it here so we can invoke individual helpers without AST extraction.",
      `. '${escapedScript}'`,
      body
    ].join("\n"),
    "utf8"
  );
  return spawnSync(
    "pwsh",
    ["-NoProfile", "-NonInteractive", "-File", harness],
    { encoding: "utf8", maxBuffer: 16 * 1024 * 1024 }
  );
}

describe("scripts/unity/bootstrap-windows-runner.ps1 helper mutation resistance", () => {
  test("the script under test exists", () => {
    expect(fs.existsSync(BOOTSTRAP_SCRIPT)).toBe(true);
  });

  if (!PWSH_PRESENT) {
    test.skip("Test-IsWindowsHost on Linux returns $false", () => {});
    test.skip("Test-IsAdministrator on Linux returns $false (no throw)", () => {});
    test.skip("Test-IsAccessDeniedException returns $false for benign exceptions", () => {});
    test.skip("Test-DefenderExclusionPathAllowed rejects 'C:\\'", () => {});
    test.skip("Test-DefenderExclusionPathAllowed rejects empty string", () => {});
    test.skip("Test-DefenderExclusionPathAllowed accepts 'C:\\Unity\\Editors'", () => {});
    test.skip("Test-DefenderExclusionPathAllowed accepts a runner _work fragment", () => {});
    test.skip("Test-DefenderExclusionPathAllowed rejects 'C:\\WINDOWS'", () => {});
    test.skip("Test-VcRedistFilesOnDisk on Linux returns @{present=$true; missing=@()}", () => {});
    return;
  }

  const ON_NON_WINDOWS = process.platform !== "win32";

  test("Test-IsWindowsHost on Linux returns $false", () => {
    if (!ON_NON_WINDOWS) {
      // On a real Windows host this MUST return $true; assert it positively
      // so the test still catches a regression that flipped the boolean.
      const result = runHarness("Write-Output ([bool](Test-IsWindowsHost))");
      expect(result.status).toBe(0);
      expect((result.stdout || "").trim()).toBe("True");
      return;
    }
    const result = runHarness("Write-Output ([bool](Test-IsWindowsHost))");
    expect(result.status).toBe(0);
    expect((result.stdout || "").trim()).toBe("False");
  });

  test("Test-IsAdministrator on Linux returns $false without throwing", () => {
    if (!ON_NON_WINDOWS) {
      // On Windows, the function may return $true or $false depending on
      // elevation; we cannot assert the value, only that it does NOT throw
      // and the exit code is 0. We still assert it returned a boolean so a
      // mutation that returned a non-bool is caught.
      const result = runHarness(
        "$r = Test-IsAdministrator; Write-Output ($r -is [bool])"
      );
      expect(result.status).toBe(0);
      expect((result.stdout || "").trim()).toBe("True");
      return;
    }
    // On Linux: $false. Crucially MUST NOT throw -- WindowsIdentity::GetCurrent
    // is .NET Core safe but the WindowsBuiltInRole check requires the Windows
    // identity stack; an unguarded call throws PlatformNotSupportedException.
    const result = runHarness("Write-Output ([bool](Test-IsAdministrator))");
    expect(result.status).toBe(0);
    expect((result.stdout || "").trim()).toBe("False");
  });

  test("Test-IsAccessDeniedException returns $false for benign exceptions", () => {
    // Construct a benign ArgumentException error record and feed it through
    // the classifier. It must NOT match access-denied (the helper exists
    // specifically to avoid false-positive English regex matches on
    // unrelated error text).
    const result = runHarness(
      [
        "$ex = New-Object System.ArgumentException('benign argument exception')",
        "$er = New-Object System.Management.Automation.ErrorRecord(",
        "  $ex, 'BenignErr', [System.Management.Automation.ErrorCategory]::InvalidArgument, $null)",
        "Write-Output ([bool](Test-IsAccessDeniedException -ErrorRecord $er))"
      ].join("\n")
    );
    expect(result.status).toBe(0);
    expect((result.stdout || "").trim()).toBe("False");
  });

  test("Test-IsAccessDeniedException returns $true for UnauthorizedAccessException", () => {
    // Symmetric mutation guard: the classifier MUST positively classify the
    // canonical .NET shape. A mutation that always returned $false (the
    // simplest sabotage) would pass the previous test but fail this one.
    const result = runHarness(
      [
        "$ex = New-Object System.UnauthorizedAccessException('access denied (test)')",
        "$er = New-Object System.Management.Automation.ErrorRecord(",
        "  $ex, 'UnauthorizedAccess', [System.Management.Automation.ErrorCategory]::PermissionDenied, $null)",
        "Write-Output ([bool](Test-IsAccessDeniedException -ErrorRecord $er))"
      ].join("\n")
    );
    expect(result.status).toBe(0);
    expect((result.stdout || "").trim()).toBe("True");
  });

  // ---------------------------------------------------------------------
  // Test-DefenderExclusionPathAllowed: the load-bearing allow-list guard
  // against a hostile/misconfigured RUNNER_WORKSPACE that would otherwise
  // exclude `C:\` from Defender on-access scanning.
  // ---------------------------------------------------------------------
  test("Test-DefenderExclusionPathAllowed rejects 'C:\\' (single drive root)", () => {
    const result = runHarness(
      "Write-Output ([bool](Test-DefenderExclusionPathAllowed -Path 'C:\\' -UnityInstallRoot 'C:\\Unity\\Editors'))"
    );
    expect(result.status).toBe(0);
    expect((result.stdout || "").trim()).toBe("False");
  });

  test("Test-DefenderExclusionPathAllowed rejects 'C:' (no separator)", () => {
    const result = runHarness(
      "Write-Output ([bool](Test-DefenderExclusionPathAllowed -Path 'C:' -UnityInstallRoot 'C:\\Unity\\Editors'))"
    );
    expect(result.status).toBe(0);
    expect((result.stdout || "").trim()).toBe("False");
  });

  test("Test-DefenderExclusionPathAllowed rejects whitespace-only path", () => {
    // The function declares its $Path parameter as Mandatory; pwsh 7's
    // cmdlet binder ALREADY rejects an empty string at param-binding time
    // (the inner `IsNullOrWhiteSpace($Path)` guard handles whitespace-only
    // input that DOES bind). A whitespace-only string is the input that
    // EXERCISES the inner guard, which is what we are mutation-testing.
    const result = runHarness(
      "Write-Output ([bool](Test-DefenderExclusionPathAllowed -Path '   ' -UnityInstallRoot 'C:\\Unity\\Editors'))"
    );
    expect(result.status).toBe(0);
    expect((result.stdout || "").trim()).toBe("False");
  });

  test("Test-DefenderExclusionPathAllowed accepts 'C:\\Unity\\Editors' (matches UnityInstallRoot prefix)", () => {
    const result = runHarness(
      "Write-Output ([bool](Test-DefenderExclusionPathAllowed -Path 'C:\\Unity\\Editors' -UnityInstallRoot 'C:\\Unity\\Editors'))"
    );
    expect(result.status).toBe(0);
    expect((result.stdout || "").trim()).toBe("True");
  });

  test("Test-DefenderExclusionPathAllowed accepts 'D:\\actions-runner\\_work\\foo' (workspace fragment)", () => {
    const result = runHarness(
      "Write-Output ([bool](Test-DefenderExclusionPathAllowed -Path 'D:\\actions-runner\\_work\\foo' -UnityInstallRoot 'C:\\Unity\\Editors'))"
    );
    expect(result.status).toBe(0);
    expect((result.stdout || "").trim()).toBe("True");
  });

  test("Test-DefenderExclusionPathAllowed rejects 'C:\\WINDOWS' (not under UnityInstallRoot, no _work fragment)", () => {
    const result = runHarness(
      "Write-Output ([bool](Test-DefenderExclusionPathAllowed -Path 'C:\\WINDOWS' -UnityInstallRoot 'C:\\Unity\\Editors'))"
    );
    expect(result.status).toBe(0);
    expect((result.stdout || "").trim()).toBe("False");
  });

  test("Test-DefenderExclusionPathAllowed rejects '\\\\server\\share' (UNC root without subpath)", () => {
    // UNC roots like `\\server\share` would exclude an entire network share;
    // mirroring the single-drive reject for the UNC case. A `\\server\share\sub`
    // path is OK because the third segment proves a subpath was supplied.
    const result = runHarness(
      "Write-Output ([bool](Test-DefenderExclusionPathAllowed -Path '\\\\server\\share' -UnityInstallRoot 'C:\\Unity\\Editors'))"
    );
    expect(result.status).toBe(0);
    expect((result.stdout || "").trim()).toBe("False");
  });

  test("Test-VcRedistFilesOnDisk on Linux returns the vacuous-OK shape (present=$true)", () => {
    if (!ON_NON_WINDOWS) {
      // On Windows this depends on the host install; not asserted here.
      return;
    }
    // Non-Windows hosts have no System32 dir; the helper SHORT-CIRCUITS with
    // `present = $true; missing = @()` so callers can compose it without
    // crashing on Linux (this same pattern is exercised by the production-
    // contract dot-source check). The early-return is load-bearing for the
    // dot-source test harness on Linux pwsh.
    const result = runHarness(
      [
        "$r = Test-VcRedistFilesOnDisk",
        "Write-Output ('present=' + [bool]$r.present)",
        "Write-Output ('missing.count=' + ($r.missing.Count))"
      ].join("\n")
    );
    expect(result.status).toBe(0);
    const out = (result.stdout || "").trim();
    expect(out).toContain("present=True");
    expect(out).toContain("missing.count=0");
  });

  test("Test-LongPathsEnabled honors DXM_UNITY_FAKE_LONGPATHS_ENABLED on every OS", () => {
    // The test override is symmetric with the override in ensure-editor.ps1;
    // exercising it here lets a future Linux-side test verify a Windows-only
    // branch deterministically. We MUST scope the env-var mutation so the
    // suite's other tests do not see it leak (we pass the env to spawnSync).
    const escapedScript = BOOTSTRAP_SCRIPT.replace(/'/g, "''");
    const workspace = makeWorkspace();
    const harness = path.join(workspace, "harness.ps1");
    fs.writeFileSync(
      harness,
      [
        `. '${escapedScript}'`,
        "Write-Output ([bool](Test-LongPathsEnabled))"
      ].join("\n"),
      "utf8"
    );

    // Override = '1' -> $true (regardless of OS).
    const trueResult = spawnSync(
      "pwsh",
      ["-NoProfile", "-NonInteractive", "-File", harness],
      {
        encoding: "utf8",
        maxBuffer: 16 * 1024 * 1024,
        env: { ...process.env, DXM_UNITY_FAKE_LONGPATHS_ENABLED: "1" }
      }
    );
    expect(trueResult.status).toBe(0);
    expect((trueResult.stdout || "").trim()).toBe("True");

    // Override = '0' -> $false (regardless of OS).
    const falseResult = spawnSync(
      "pwsh",
      ["-NoProfile", "-NonInteractive", "-File", harness],
      {
        encoding: "utf8",
        maxBuffer: 16 * 1024 * 1024,
        env: { ...process.env, DXM_UNITY_FAKE_LONGPATHS_ENABLED: "0" }
      }
    );
    expect(falseResult.status).toBe(0);
    expect((falseResult.stdout || "").trim()).toBe("False");
  });
});
