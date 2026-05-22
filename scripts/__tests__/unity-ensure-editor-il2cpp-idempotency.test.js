/**
 * @fileoverview Focused behavioral test for ensure-editor.ps1's IL2CPP module
 * idempotency fix.
 *
 * WHY THIS EXISTS: the standalone Unity beta CLI's `install-modules` returns "No
 * modules found to install." with exit code 6 when the Windows IL2CPP module is
 * ALREADY present -- an idempotent no-op, NOT a failure. The previous code routed
 * the module install through the THROWING `Invoke-UnityCli`, so that benign
 * non-zero exit aborted the whole bootstrap on any re-run of an editor that
 * already had IL2CPP. The fix added `Test-Il2CppModulePresent` (a disk-
 * authoritative probe) and made `Add-WindowsIl2CppModule` classify the install
 * result against the disk via the NON-throwing capturing invoker, so an
 * already-installed module is treated as success instead of a fatal error.
 *
 * This test pins the core of that fix: the `Test-Il2CppModulePresent` PROBE. We
 * extract just that function from ensure-editor.ps1 via the PowerShell AST
 * (`Parser::ParseFile`) into a throwaway temp .ps1 and dot-source it -- the
 * script has top-level executable code (it creates the install root and runs the
 * install flow on load), so dot-sourcing the whole file would have side effects;
 * AST-extracting only the function avoids that entirely. We then assert the probe
 * returns $false for a non-existent editor and $true for a fabricated on-disk
 * IL2CPP variations tree, exactly the disk evidence the idempotency branch relies
 * on. Everything lives under os.tmpdir and is cleaned up.
 *
 * A cheap static corroboration also runs (always, even without pwsh): it proves
 * Add-WindowsIl2CppModule no longer routes the module install through the
 * throwing Invoke-UnityCli and DOES classify against Test-Il2CppModulePresent, so
 * a refactor that reintroduces the throwing path is caught even where the
 * behavioral run is skipped.
 *
 * pwsh is preinstalled on GitHub's runners; when it is absent locally the
 * behavioral assertions are skipped, but an always-on sanity assertion still runs
 * so a zero-coverage regression cannot hide.
 */

"use strict";

const fs = require("fs");
const os = require("os");
const path = require("path");
const { spawnSync } = require("child_process");

const REPO_ROOT = path.resolve(__dirname, "..", "..");
const ENSURE_EDITOR = path.join(REPO_ROOT, "scripts", "unity", "ensure-editor.ps1");

// The IL2CPP variations leaf the probe treats as conclusive evidence (the exact
// path called out in the task: a fabricated win64_player_development_il2cpp dir).
const IL2CPP_VARIATION_REL = path.join(
  "Editor",
  "Data",
  "PlaybackEngines",
  "windowsstandalonesupport",
  "Variations",
  "win64_player_development_il2cpp"
);

function pwshAvailable() {
  const probe = spawnSync("pwsh", ["-NoProfile", "-NonInteractive", "-Command", "exit 0"], {
    encoding: "utf8"
  });
  return probe.status === 0;
}

const PWSH_PRESENT = pwshAvailable();

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

// AST-extract a named function body from ensure-editor.ps1 into a temp .ps1,
// dot-source it, invoke it against $EditorPath, and print the boolean result.
// Returns the trimmed stdout (e.g. "True" / "False") plus exit status.
function probeIl2Cpp(editorPath) {
  const base = fs.mkdtempSync(path.join(os.tmpdir(), "dxm-il2cpp-probe-"));
  workspaces.push(base);
  const harnessPath = path.join(base, "harness.ps1");

  // The harness extracts ONLY Test-Il2CppModulePresent (it depends solely on
  // built-in cmdlets, so no other local function is needed), writes it to a temp
  // file, dot-sources it, and prints the probe result for the given editor path.
  const harness = [
    "Set-StrictMode -Version Latest",
    "$ErrorActionPreference = 'Stop'",
    `$src = '${ENSURE_EDITOR.replace(/'/g, "''")}'`,
    "$tokens = $null; $errs = $null",
    "$ast = [System.Management.Automation.Language.Parser]::ParseFile($src, [ref]$tokens, [ref]$errs)",
    "$fn = $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Test-Il2CppModulePresent' }, $true)",
    "if (-not $fn -or $fn.Count -lt 1) { Write-Error 'Test-Il2CppModulePresent not found'; exit 3 }",
    `$tmpFn = Join-Path '${base.replace(/'/g, "''")}' 'fn.ps1'`,
    "Set-Content -LiteralPath $tmpFn -Value $fn[0].Extent.Text -Encoding UTF8",
    ". $tmpFn",
    `$result = Test-Il2CppModulePresent -EditorPath '${editorPath.replace(/'/g, "''")}'`,
    "Write-Output ([bool]$result)"
  ].join("\n");

  fs.writeFileSync(harnessPath, harness, "utf8");

  const run = spawnSync("pwsh", ["-NoProfile", "-NonInteractive", "-File", harnessPath], {
    encoding: "utf8",
    maxBuffer: 16 * 1024 * 1024
  });
  return { stdout: (run.stdout || "").trim(), stderr: run.stderr || "", status: run.status };
}

describe("ensure-editor.ps1 IL2CPP module idempotency", () => {
  // Always runs (even without pwsh): proves the script under test exists, so a
  // rename/move cannot silently turn this whole guard into a no-op.
  test("the script under test exists", () => {
    expect(fs.existsSync(ENSURE_EDITOR)).toBe(true);
  });

  // Static corroboration of the fix shape (no pwsh needed): the module install in
  // Add-WindowsIl2CppModule must NOT go through the throwing Invoke-UnityCli, and
  // the function MUST classify against Test-Il2CppModulePresent.
  test("Add-WindowsIl2CppModule classifies via Test-Il2CppModulePresent, not the throwing Invoke-UnityCli", () => {
    const text = fs.readFileSync(ENSURE_EDITOR, "utf8");
    const start = text.indexOf("function Add-WindowsIl2CppModule");
    expect(start).toBeGreaterThanOrEqual(0);
    // Bound the slice at the next top-level function definition.
    const after = text.indexOf("\nfunction ", start + 1);
    const body = after === -1 ? text.slice(start) : text.slice(start, after);

    // It must consult the disk probe...
    expect(body).toContain("Test-Il2CppModulePresent");
    // ...attempt the install via the non-throwing capturing invoker...
    expect(body).toContain("Invoke-UnityCliCapture");
    // ...and NEVER route the module install through the throwing invoker.
    expect(body).not.toMatch(/Invoke-UnityCli\b(?!Capture|Safe)/);
  });

  if (!PWSH_PRESENT) {
    // eslint-disable-next-line no-console
    console.warn(
      "[il2cpp-idempotency] pwsh not found on PATH; skipping behavioral probe (CI runners have pwsh)."
    );
    test.skip.each([
      ["non-existent editor path", false],
      ["fabricated IL2CPP variations tree", true]
    ])("Test-Il2CppModulePresent returns %s -> %s", () => {});
    return;
  }

  test("Test-Il2CppModulePresent returns $false for a non-existent editor path", () => {
    const fake = path.join(os.tmpdir(), "dxm-does-not-exist", "Editor", "Unity.exe");
    const out = probeIl2Cpp(fake);
    expect(out.status).toBe(0);
    expect(out.stdout).toBe("False");
  });

  test("Test-Il2CppModulePresent returns $true for a fabricated IL2CPP variations tree", () => {
    const base = fs.mkdtempSync(path.join(os.tmpdir(), "dxm-il2cpp-editor-"));
    workspaces.push(base);

    // Build ...\<version>\Editor\Data\PlaybackEngines\...\Variations\
    //   win64_player_development_il2cpp, then place a (non-existent-on-disk-as-
    //   a-file is fine) Unity.exe path whose PARENT is the Editor dir so the
    //   probe's editorDir -> Data resolution lands on the fabricated tree.
    const variationDir = path.join(base, "6000.0.32f1", IL2CPP_VARIATION_REL);
    fs.mkdirSync(variationDir, { recursive: true });
    const editorExe = path.join(base, "6000.0.32f1", "Editor", "Unity.exe");
    fs.writeFileSync(editorExe, "stub", "utf8");

    const out = probeIl2Cpp(editorExe);
    expect(out.status).toBe(0);
    expect(out.stdout).toBe("True");
  });
});
