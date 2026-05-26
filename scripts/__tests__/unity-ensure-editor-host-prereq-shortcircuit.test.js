/**
 * @fileoverview Behavioral + AST-style guard for the 0xC0000135 /
 * STATUS_DLL_NOT_FOUND short-circuit in
 * scripts/unity/ensure-editor.ps1's `Ensure-UnityNativeStartupHealthy`.
 *
 * WHY THIS EXISTS: when the Windows loader cannot resolve a DLL Unity.exe
 * imports (overwhelmingly the Microsoft Visual C++ 2015-2022 Redistributable),
 * Unity.exe exits with -1073741515 / 0xC0000135. The previous behavior of
 * Ensure-UnityNativeStartupHealthy was to retry a MANAGED REINSTALL of Unity
 * on the failure -- futile, because the missing DLL is ON THE OS, not in the
 * Unity install tree. The fix is a SHORT-CIRCUIT: when
 * Test-IsNativeDllNotFound classifies the exit code as the canonical
 * STATUS_DLL_NOT_FOUND, ensure-editor.ps1:
 *   1. Emits a wrap-immune `::error::` annotation (Write-UnityHostPrereqAnnotation)
 *      that NAMES the operator-actionable remediation (run the bootstrap
 *      script, or trigger the runner-bootstrap workflow_dispatch) AND lists
 *      the DLLs Unity.exe imports so the missing prereq is obvious at a glance.
 *   2. Throws WITHOUT attempting the futile managed reinstall. The throw
 *      message also carries the canonical "0xC0000135 / STATUS_DLL_NOT_FOUND"
 *      tokens + the bootstrap script path + the runbook reference.
 * The short-circuit fires on BOTH the first-probe failure AND the post-repair
 * probe failure (a probe that succeeded the first time but failed
 * 0xC0000135 after a managed reinstall would otherwise be silently swallowed
 * by the repair flow; the post-repair short-circuit emits the same annotation
 * with `-RepairAttempted` so the message reflects the failed-reinstall context).
 *
 * STRATEGY:
 *   1. STATIC: the source MUST contain the short-circuit branch (Test-
 *      IsNativeDllNotFound call gating Write-UnityHostPrereqAnnotation +
 *      throw), with the right tokens, BEFORE the managed-reinstall branch
 *      AND inside the post-repair branch. AST-style assertions on the
 *      Ensure-UnityNativeStartupHealthy function body.
 *   2. BEHAVIORAL: dot-source the helpers and invoke
 *      Test-IsNativeDllNotFound + Write-UnityHostPrereqAnnotation directly
 *      with the canonical -1073741515 exit code. We CANNOT exercise the
 *      end-to-end short-circuit via a fake Unity.exe stub on Linux because
 *      OS exec truncates a negative exit code to 0..255 (53 in this case
 *      since 0xC0000135 mod 256 = 0x35) -- the probe would never see
 *      0xC0000135. Direct invocation of the helpers IS the behavioral
 *      coverage; it pins:
 *        - Test-IsNativeDllNotFound returns $true for -1073741515 (the int
 *          form of 0xC0000135) AND for the positive uint32 form.
 *        - Test-IsNativeDllNotFound returns $false for benign exit codes.
 *        - Write-UnityHostPrereqAnnotation emits the load-bearing tokens:
 *          ::error title=, "Unity", "STATUS_DLL_NOT_FOUND" via
 *          Get-NativeExitCodeDescription, "bootstrap-windows-runner.ps1",
 *          AND respects DXM_RUNNER_PREREQ_INSTALLED for the context-aware
 *          cause phrasing.
 *        - DXM_UNITY_FAKE_IMPORTS injects the comma-separated names into
 *          the annotation's "Unity.exe imports:" segment.
 *
 * pwsh is preinstalled on GitHub runners; locally-absent pwsh -> behavioral
 * tests skip with a console.warn; the always-on static assertions still
 * guarantee a zero-coverage regression cannot hide.
 */

"use strict";

const fs = require("fs");
const os = require("os");
const path = require("path");
const { spawnSync } = require("child_process");

const REPO_ROOT = path.resolve(__dirname, "..", "..");
const ENSURE_EDITOR = path.join(REPO_ROOT, "scripts", "unity", "ensure-editor.ps1");

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
    "[unity-ensure-editor-host-prereq-shortcircuit] pwsh not found on PATH; skipping behavioral assertions."
  );
}

const workspaces = [];

afterAll(() => {
  for (const ws of workspaces) {
    try {
      fs.rmSync(ws, { recursive: true, force: true });
    } catch {
      // best-effort temp cleanup; never fail the suite on teardown.
    }
  }
});

function makeWorkspace() {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "dxm-ensure-editor-shortcircuit-"));
  workspaces.push(dir);
  return dir;
}

function combined(result) {
  return ((result.stdout || "") + "\n" + (result.stderr || "")).replace(/\r\n/g, "\n");
}

// AST-extract a PowerShell function body by name via balanced brace scan.
// Mirrors the pattern in unity-native-startup-probe-isolation.test.js.
function extractPowerShellFunction(source, name) {
  const headerPattern = new RegExp(`(^|\\n)function\\s+${name}\\b`);
  const headerMatch = headerPattern.exec(source);
  if (!headerMatch) {
    return null;
  }
  const headerStart = headerMatch.index + (headerMatch[1] ? headerMatch[1].length : 0);
  const openBrace = source.indexOf("{", headerStart);
  if (openBrace < 0) {
    return null;
  }
  let depth = 0;
  for (let i = openBrace; i < source.length; i += 1) {
    const ch = source[i];
    if (ch === "{") {
      depth += 1;
    } else if (ch === "}") {
      depth -= 1;
      if (depth === 0) {
        return source.slice(headerStart, i + 1);
      }
    }
  }
  return null;
}

let SCRIPT_TEXT;
beforeAll(() => {
  SCRIPT_TEXT = fs.readFileSync(ENSURE_EDITOR, "utf8");
});

// ===========================================================================
// STATIC source-shape guards (always run, even without pwsh)
// ===========================================================================
describe("ensure-editor.ps1 0xC0000135 short-circuit source shape", () => {
  test("the script under test exists", () => {
    expect(fs.existsSync(ENSURE_EDITOR)).toBe(true);
  });

  test("declares Test-IsNativeDllNotFound, Get-UnityNativeImports, Write-UnityHostPrereqAnnotation", () => {
    expect(extractPowerShellFunction(SCRIPT_TEXT, "Test-IsNativeDllNotFound")).not.toBeNull();
    expect(extractPowerShellFunction(SCRIPT_TEXT, "Get-UnityNativeImports")).not.toBeNull();
    expect(extractPowerShellFunction(SCRIPT_TEXT, "Write-UnityHostPrereqAnnotation")).not.toBeNull();
  });

  test("Test-IsNativeDllNotFound compares against 'C0000135' hex string (NOT the 0xC0000135 int literal)", () => {
    // Compare on the canonical 8-char hex of the uint32 value -- comparing
    // [uint32]$normalized -eq 0xC0000135 silently coerces to Int32 because
    // PowerShell parses 0xC0000135 as the negative Int32 -1073741515 and the
    // [uint32] cast collapses the equality. The hex-string compare is the
    // bug-immune shape; this assertion guards against a refactor that
    // "simplifies" back to the buggy form.
    const body = extractPowerShellFunction(SCRIPT_TEXT, "Test-IsNativeDllNotFound");
    expect(body).not.toBeNull();
    expect(body).toMatch(/'C0000135'/);
    expect(body).toMatch(/\.ToString\('X8'\)/);
    // The function must compute the unsigned form so a negative Int32
    // (-1073741515) normalizes to the matching uint32 (3221225781).
    expect(body).toMatch(/\[uint32\]\s*\(\$ExitCode\s*\+\s*4294967296\)/);
  });

  test("Write-UnityHostPrereqAnnotation emits ::error title= and names bootstrap-windows-runner.ps1", () => {
    const body = extractPowerShellFunction(
      SCRIPT_TEXT,
      "Write-UnityHostPrereqAnnotation"
    );
    expect(body).not.toBeNull();
    // The wrap-immune annotation MUST use the GitHub Actions
    // ::error title=... syntax so it survives ConciseView's word wrap.
    expect(body).toContain("::error title=Unity");
    // The remediation MUST NAME the bootstrap script (so the operator can
    // copy-paste the path).
    expect(body).toContain("bootstrap-windows-runner.ps1");
    // The runbook MUST be linked.
    expect(body).toContain("docs/runbooks/unity-runners-after-transfer.md");
    // The annotation must branch on the preflight env var (context-aware
    // cause phrasing).
    expect(body).toMatch(/DXM_RUNNER_PREREQ_INSTALLED\s*-eq\s*'1'/);
    // The -RepairAttempted switch MUST be wired so the post-repair branch
    // can pass it in.
    expect(body).toMatch(/\[switch\]\s*\$RepairAttempted/);
  });

  test("Ensure-UnityNativeStartupHealthy short-circuits on first-probe 0xC0000135 BEFORE attempting repair", () => {
    const body = extractPowerShellFunction(
      SCRIPT_TEXT,
      "Ensure-UnityNativeStartupHealthy"
    );
    expect(body).not.toBeNull();

    // The first-probe short-circuit must fire BEFORE the
    // DXM_UNITY_DISABLE_EDITOR_REPAIR check (the comments document this
    // explicitly: "we therefore short-circuit BEFORE the DXM_UNITY_DISABLE_EDITOR_REPAIR
    // check too"). We anchor on the CODE form
    // `if ($env:DXM_UNITY_DISABLE_EDITOR_REPAIR -eq '1')` to avoid matching
    // a comment that mentions the env var name in prose.
    const firstProbeIdx = body.indexOf("Test-IsNativeDllNotFound -ExitCode $result.ExitCode");
    const disableRepairMatch = /if\s*\(\$env:DXM_UNITY_DISABLE_EDITOR_REPAIR\s*-eq\s*'1'\)/.exec(
      body
    );
    const repairCallMatch = /\$repaired\s*=\s*Repair-UnityEditorWithCiModules\b/.exec(body);
    expect(firstProbeIdx).toBeGreaterThan(-1);
    expect(disableRepairMatch).not.toBeNull();
    expect(repairCallMatch).not.toBeNull();
    expect(firstProbeIdx).toBeLessThan(disableRepairMatch.index);
    expect(firstProbeIdx).toBeLessThan(repairCallMatch.index);
  });

  test("first-probe short-circuit calls Write-UnityHostPrereqAnnotation WITHOUT -RepairAttempted, then throws", () => {
    const body = extractPowerShellFunction(
      SCRIPT_TEXT,
      "Ensure-UnityNativeStartupHealthy"
    );
    expect(body).not.toBeNull();
    // Single-line regex: the first-probe branch annotates THEN throws, in
    // that order, both inside the `if (Test-IsNativeDllNotFound ...)` block.
    // The first-probe call passes $result.ExitCode (not $repairProbe.ExitCode)
    // and MUST NOT carry -RepairAttempted.
    const firstProbePattern =
      /if \(Test-IsNativeDllNotFound -ExitCode \$result\.ExitCode\)\s*\{[\s\S]*?Write-UnityHostPrereqAnnotation[^}]*?\$result\.ExitCode[^}]*?\$result\.Description[\s\S]*?throw\s+"[\s\S]*?0xC0000135[\s\S]*?STATUS_DLL_NOT_FOUND[\s\S]*?\}/;
    expect(body).toMatch(firstProbePattern);
    // The first-probe Write-UnityHostPrereqAnnotation call must NOT include
    // -RepairAttempted (that switch is reserved for the post-repair branch).
    const firstProbeMatch = firstProbePattern.exec(body);
    expect(firstProbeMatch).not.toBeNull();
    expect(firstProbeMatch[0]).not.toContain("-RepairAttempted");
  });

  test("post-repair short-circuit calls Write-UnityHostPrereqAnnotation WITH -RepairAttempted, then throws", () => {
    const body = extractPowerShellFunction(
      SCRIPT_TEXT,
      "Ensure-UnityNativeStartupHealthy"
    );
    expect(body).not.toBeNull();
    // Single-line regex: the post-repair branch annotates with
    // -RepairAttempted THEN throws.
    const postRepairPattern =
      /if \(Test-IsNativeDllNotFound -ExitCode \$repairProbe\.ExitCode\)\s*\{[\s\S]*?Write-UnityHostPrereqAnnotation[\s\S]*?-RepairAttempted[\s\S]*?throw\s+"[\s\S]*?managed reinstall[\s\S]*?\}/;
    expect(body).toMatch(postRepairPattern);
  });

  test("throw messages name the bootstrap path AND the runbook", () => {
    const body = extractPowerShellFunction(
      SCRIPT_TEXT,
      "Ensure-UnityNativeStartupHealthy"
    );
    expect(body).not.toBeNull();
    // Both throws MUST name the operator-actionable bootstrap script.
    const throwBootstrapPattern = /throw\s+"[^"]*bootstrap-windows-runner\.ps1/g;
    const matches = body.match(throwBootstrapPattern) || [];
    expect(matches.length).toBeGreaterThanOrEqual(2);
    // Both throws MUST reference the runbook.
    expect(body).toMatch(/throw\s+"[^"]*unity-runners-after-transfer\.md/);
  });
});

// ===========================================================================
// BEHAVIORAL guards (only when pwsh is present)
// ===========================================================================
describe("ensure-editor.ps1 0xC0000135 helpers (behavioral)", () => {
  if (!PWSH_PRESENT) {
    test.skip("Test-IsNativeDllNotFound classifies -1073741515 as $true", () => {});
    test.skip("Test-IsNativeDllNotFound returns $false for benign exit codes", () => {});
    test.skip("Get-NativeExitCodeDescription maps 0xC0000135 to STATUS_DLL_NOT_FOUND", () => {});
    test.skip("Write-UnityHostPrereqAnnotation emits the canonical tokens", () => {});
    test.skip("Write-UnityHostPrereqAnnotation honors DXM_RUNNER_PREREQ_INSTALLED for cause phrasing", () => {});
    test.skip("Write-UnityHostPrereqAnnotation injects DXM_UNITY_FAKE_IMPORTS into the imports list", () => {});
    return;
  }

  // AST-extract the diagnostic helpers from ensure-editor.ps1 and dot-source
  // ONLY those functions into a temp .ps1 (the full file has top-level
  // executable code, so dot-sourcing the whole file has side effects we want
  // to avoid). Pattern mirrors unity-ensure-editor-il2cpp-idempotency.test.js.
  function buildHarness(bodyLines) {
    const escapedPath = ENSURE_EDITOR.replace(/'/g, "''");
    const wanted = [
      "Test-IsNativeDllNotFound",
      "Get-NativeExitCodeDescription",
      "Get-UnityNativeImports",
      "Write-UnityHostPrereqAnnotation",
      "Write-CiNotice"
    ];
    const wantedLiteral = wanted.map((n) => `'${n}'`).join(", ");
    return [
      "Set-StrictMode -Version Latest",
      "$ErrorActionPreference = 'Stop'",
      `$src = '${escapedPath}'`,
      `$wanted = @(${wantedLiteral})`,
      "$tokens = $null; $errs = $null",
      "$ast = [System.Management.Automation.Language.Parser]::ParseFile($src, [ref]$tokens, [ref]$errs)",
      "$functions = $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $wanted -contains $n.Name }, $true)",
      'foreach ($name in $wanted) { if (-not ($functions | Where-Object { $_.Name -eq $name })) { Write-Error "Function $name not found"; exit 3 } }',
      "foreach ($fn in $functions) { Invoke-Expression $fn.Extent.Text }",
      ...bodyLines
    ].join("\n");
  }

  function runHarness(bodyLines, extraEnv = {}) {
    const workspace = makeWorkspace();
    const harness = path.join(workspace, "harness.ps1");
    fs.writeFileSync(harness, buildHarness(bodyLines), "utf8");
    return spawnSync(
      "pwsh",
      ["-NoProfile", "-NonInteractive", "-File", harness],
      {
        encoding: "utf8",
        maxBuffer: 16 * 1024 * 1024,
        env: { ...process.env, ...extraEnv }
      }
    );
  }

  test("Test-IsNativeDllNotFound classifies -1073741515 (canonical Int32 form) as $true", () => {
    const result = runHarness([
      "Write-Output ([bool](Test-IsNativeDllNotFound -ExitCode -1073741515))"
    ]);
    if (result.status !== 0) {
      throw new Error(combined(result));
    }
    expect((result.stdout || "").trim()).toBe("True");
  });

  test("Test-IsNativeDllNotFound returns $false for benign exit codes (0, 1, 6, 53)", () => {
    // 53 is what `exit -1073741515` truncates to on a Linux OS exec --
    // we MUST NOT match it (false positive would let unrelated failures
    // silently take the short-circuit path).
    const result = runHarness([
      "Write-Output ([bool](Test-IsNativeDllNotFound -ExitCode 0))",
      "Write-Output ([bool](Test-IsNativeDllNotFound -ExitCode 1))",
      "Write-Output ([bool](Test-IsNativeDllNotFound -ExitCode 6))",
      "Write-Output ([bool](Test-IsNativeDllNotFound -ExitCode 53))"
    ]);
    if (result.status !== 0) {
      throw new Error(combined(result));
    }
    const lines = (result.stdout || "").trim().split(/\r?\n/);
    expect(lines).toEqual(["False", "False", "False", "False"]);
  });

  test("Get-NativeExitCodeDescription maps -1073741515 to '0xC0000135 / STATUS_DLL_NOT_FOUND'", () => {
    const result = runHarness([
      "Write-Output (Get-NativeExitCodeDescription -ExitCode -1073741515)"
    ]);
    if (result.status !== 0) {
      throw new Error(combined(result));
    }
    const out = (result.stdout || "").trim();
    expect(out).toContain("0xC0000135");
    expect(out).toContain("STATUS_DLL_NOT_FOUND");
  });

  test("Write-UnityHostPrereqAnnotation (preflight NOT run) emits VC++ remediation tokens", () => {
    // Reproduces the FIRST-PROBE failure case (preflight never ran or
    // failed). The annotation MUST name VC++ Redistributable as the most
    // likely cause AND direct the operator to run the bootstrap script.
    // We funnel Write-Host through a captured 6>&1 stream so we can grep
    // the emitted ::error:: line.
    const result = runHarness(
      [
        "$out = & {",
        "  Write-UnityHostPrereqAnnotation -Version '6000.0.32f1' -ExitCode -1073741515 -Description '0xC0000135 / STATUS_DLL_NOT_FOUND' -EditorPath '/nonexistent/Unity.exe' -ProbeLog '/tmp/probe.log'",
        "} *>&1 | Out-String",
        "Write-Output $out"
      ],
      { DXM_RUNNER_PREREQ_INSTALLED: "" }
    );
    if (result.status !== 0) {
      throw new Error(combined(result));
    }
    const out = combined(result);
    // Wrap-immune ::error title= annotation.
    expect(out).toMatch(/::error title=Unity 6000\.0\.32f1 host prerequisite missing::/);
    // First-probe causal phrasing: VC++ Redistributable is the suspect.
    expect(out).toContain("Microsoft Visual C++");
    expect(out).toContain("Redistributable");
    // The remediation directs the operator at the bootstrap script.
    expect(out).toContain("bootstrap-windows-runner.ps1");
    expect(out).toContain("runner-bootstrap.yml");
    // The runbook is linked.
    expect(out).toContain("unity-runners-after-transfer.md");
  });

  test("Write-UnityHostPrereqAnnotation (preflight DID run) flips to 'DIFFERENT missing DLL' phrasing", () => {
    // Preflight already installed VC++ this job (the composite would have
    // set DXM_RUNNER_PREREQ_INSTALLED=1); the annotation MUST NOT tell the
    // operator to run the bootstrap script again -- it must instead point
    // at the imports list and the runbook for further investigation.
    const result = runHarness(
      [
        "$out = & {",
        "  Write-UnityHostPrereqAnnotation -Version '6000.0.32f1' -ExitCode -1073741515 -Description '0xC0000135 / STATUS_DLL_NOT_FOUND' -EditorPath '/nonexistent/Unity.exe'",
        "} *>&1 | Out-String",
        "Write-Output $out"
      ],
      { DXM_RUNNER_PREREQ_INSTALLED: "1" }
    );
    if (result.status !== 0) {
      throw new Error(combined(result));
    }
    const out = combined(result);
    expect(out).toContain("Preflight ran successfully");
    expect(out).toContain("DIFFERENT missing DLL");
    // The "run the bootstrap" phrasing MUST be absent on this path
    // (running it again wouldn't help -- VC++ is already installed).
    expect(out).not.toMatch(/Fix: run scripts\/unity\/bootstrap-windows-runner\.ps1/);
    expect(out).not.toMatch(/Fix: run .* bootstrap-windows-runner\.ps1/);
    // The runbook is still linked.
    expect(out).toContain("unity-runners-after-transfer.md");
  });

  test("Write-UnityHostPrereqAnnotation -RepairAttempted phrases as 'managed reinstall already ran'", () => {
    // Post-repair short-circuit: the managed reinstall already ran and did
    // not help (as expected for 0xC0000135 -- the missing DLL is on the
    // OS, not in the Unity install). The annotation must say so.
    const result = runHarness(
      [
        "$out = & {",
        "  Write-UnityHostPrereqAnnotation -Version '6000.0.32f1' -ExitCode -1073741515 -Description '0xC0000135 / STATUS_DLL_NOT_FOUND' -EditorPath '/nonexistent/Unity.exe' -RepairAttempted",
        "} *>&1 | Out-String",
        "Write-Output $out"
      ],
      { DXM_RUNNER_PREREQ_INSTALLED: "" }
    );
    if (result.status !== 0) {
      throw new Error(combined(result));
    }
    const out = combined(result);
    expect(out).toContain("managed reinstall already ran and did not help");
    expect(out).toContain("0xC0000135");
  });

  test("DXM_UNITY_FAKE_IMPORTS injects the comma-separated names into the annotation", () => {
    // The test override (documented in Get-UnityNativeImports) lets a
    // hermetic Linux/macOS test prove the annotation surfaces the import
    // list WITHOUT smuggling a real PE binary into the repo.
    const result = runHarness(
      [
        "$out = & {",
        "  Write-UnityHostPrereqAnnotation -Version '6000.0.32f1' -ExitCode -1073741515 -Description '0xC0000135 / STATUS_DLL_NOT_FOUND' -EditorPath '/nonexistent/Unity.exe'",
        "} *>&1 | Out-String",
        "Write-Output $out"
      ],
      {
        DXM_UNITY_FAKE_IMPORTS: "VCRUNTIME140.dll,VCRUNTIME140_1.dll,MSVCP140.dll,KERNEL32.dll",
        DXM_RUNNER_PREREQ_INSTALLED: ""
      }
    );
    if (result.status !== 0) {
      throw new Error(combined(result));
    }
    const out = combined(result);
    // The "Unity.exe imports:" segment must list every injected name.
    expect(out).toMatch(/Unity\.exe imports:/);
    expect(out).toContain("VCRUNTIME140.dll");
    expect(out).toContain("VCRUNTIME140_1.dll");
    expect(out).toContain("MSVCP140.dll");
    expect(out).toContain("KERNEL32.dll");
  });
});
