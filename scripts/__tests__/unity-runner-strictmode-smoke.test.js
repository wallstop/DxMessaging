/**
 * @fileoverview End-to-end StrictMode smoke test for scripts/unity/run-ci-tests.ps1.
 *
 * WHY THIS EXISTS: run-ci-tests.ps1 runs under `Set-StrictMode -Version Latest`.
 * `Get-UnityLicenseArguments` and `Get-AcceleratorArguments` each `return @()`
 * on their empty paths. A PowerShell function returning an EMPTY array emits
 * ZERO objects, so a bare capture (`$x = Get-Foo`) assigns AutomationNull (it
 * compares equal to $null) -- the empty array unwraps to nothing. The script
 * then evaluated `$acceleratorArgs.Count`, which under StrictMode Latest (and
 * 2.0+, verified on pwsh 7.6.1, the CI runtime) THROWS "The property 'Count'
 * cannot be found on this object." That property-access throw is the real and
 * only bug -- the later `... + $licenseArgs + $acceleratorArgs` concatenation was
 * NOT broken, because `+` DROPS the empty/AutomationNull capture instead of
 * adding an element (a LITERAL $null operand would instead add a spurious one).
 *
 * The fix `@()`-wraps both captures at the source. This test ACTUALLY RUNS the
 * real script via pwsh under its real StrictMode, exercising the
 * empty-license/empty-accelerator path that triggered the failing CI run, and
 * asserts the script reaches a clean exit and produces results.xml. A STUB
 * editor stands in for Unity.exe (cross-platform pwsh) so the run never touches
 * a real Unity install. Everything lives in OS temp dirs; the repo tree is never
 * polluted.
 *
 * pwsh is preinstalled on GitHub's runners (the script-tests.yml matrix), so
 * this runs in CI. When pwsh is absent locally the run assertions are skipped,
 * but an always-on sanity assertion still runs so a zero-coverage regression
 * (the test silently validating nothing) cannot hide.
 */

"use strict";

const fs = require("fs");
const os = require("os");
const path = require("path");
const { spawnSync } = require("child_process");

const REPO_ROOT = path.resolve(__dirname, "..", "..");
const RUN_CI_TESTS = path.join(REPO_ROOT, "scripts", "unity", "run-ci-tests.ps1");

// Env vars whose presence would route the script DOWN a non-empty path. We must
// delete every one so the empty-array branches (the ones that triggered the
// bug) are the ones exercised. GITHUB_WORKSPACE is deleted too so the default
// -RepoRoot does not point at the real repo (we pass an explicit temp RepoRoot).
// LOCALAPPDATA is deleted so Initialize-UnityCacheEnvironment falls back to the
// temp cacheRoot on Windows too (otherwise it would create %LOCALAPPDATA%\Unity\
// Caches outside the temp workspace) -- keeping the run fully hermetic.
const ENV_TO_DELETE = [
  "UNITY_ACCELERATOR_ENDPOINT",
  "UNITY_SERIAL",
  "UNITY_LICENSE",
  "UNITY_EMAIL",
  "UNITY_PASSWORD",
  "GITHUB_WORKSPACE",
  "LOCALAPPDATA"
];

// Cross-platform STUB editor: writes a passing NUnit results.xml to whatever
// path follows -testResults, and exits 0. The configure step (standalone) has no
// -testResults, so the stub writes nothing and still exits 0.
const STUB_EDITOR = [
  "[CmdletBinding()] param([Parameter(ValueFromRemainingArguments=$true)][string[]]$Rest)",
  "$Rest = @($Rest)",
  "$i = [array]::IndexOf($Rest, '-testResults')",
  "if ($i -ge 0 -and ($i + 1) -lt $Rest.Count) {",
  "  $out = $Rest[$i + 1]; $d = Split-Path -Parent $out",
  "  if ($d) { New-Item -ItemType Directory -Force -Path $d | Out-Null }",
  "  '<?xml version=\"1.0\" encoding=\"utf-8\"?><test-run total=\"1\" passed=\"1\" failed=\"0\" skipped=\"0\" result=\"Passed\"></test-run>' | Set-Content -LiteralPath $out -Encoding UTF8",
  "}",
  "exit 0",
  ""
].join("\n");

function pwshAvailable() {
  const probe = spawnSync("pwsh", ["-NoProfile", "-NonInteractive", "-Command", "exit 0"], {
    encoding: "utf8"
  });
  return probe.status === 0;
}

const PWSH_PRESENT = pwshAvailable();

// Build a clean env where the bug-triggering branches are guaranteed to be hit.
function cleanedEnv() {
  const env = { ...process.env };
  for (const key of ENV_TO_DELETE) {
    delete env[key];
  }
  return env;
}

// Stand up an isolated, self-contained workspace in the OS temp dir:
//   repoRoot/  -> package.json + Runtime/  (so Assert-RepoRoot passes)
//   artifacts/ -> -ArtifactsPath
//   project/   -> -ProjectPath
//   stub.ps1   -> -UnityEditorPath
function makeWorkspace() {
  const base = fs.mkdtempSync(path.join(os.tmpdir(), "dxm-strictmode-smoke-"));
  const repoRoot = path.join(base, "repo");
  const artifacts = path.join(base, "artifacts");
  const project = path.join(base, "project");

  fs.mkdirSync(path.join(repoRoot, "Runtime"), { recursive: true });
  fs.writeFileSync(path.join(repoRoot, "package.json"), "{}\n", "utf8");
  fs.mkdirSync(artifacts, { recursive: true });
  fs.mkdirSync(project, { recursive: true });

  const stubPath = path.join(base, "stub-editor.ps1");
  fs.writeFileSync(stubPath, STUB_EDITOR, "utf8");

  return { base, repoRoot, artifacts, project, stubPath };
}

function runScript(mode, ws) {
  return spawnSync(
    "pwsh",
    [
      "-NoProfile",
      "-NonInteractive",
      "-File",
      RUN_CI_TESTS,
      "-UnityVersion",
      "2021.3.45f1",
      "-TestMode",
      mode,
      "-AssemblyNames",
      "WallstopStudios.DxMessaging.Tests.Editor",
      "-ArtifactsPath",
      ws.artifacts,
      "-ProjectPath",
      ws.project,
      "-RepoRoot",
      ws.repoRoot,
      "-UnityEditorPath",
      ws.stubPath
    ],
    { env: cleanedEnv(), encoding: "utf8", maxBuffer: 32 * 1024 * 1024 }
  );
}

const workspaces = [];

afterAll(() => {
  for (const ws of workspaces) {
    try {
      fs.rmSync(ws.base, { recursive: true, force: true });
    } catch {
      // best-effort temp cleanup; never fail the suite on teardown.
    }
  }
});

describe("run-ci-tests.ps1 StrictMode collection-safety smoke test", () => {
  // Always runs (even without pwsh): proves the script under test exists, so a
  // rename/move cannot silently turn this whole guard into a no-op.
  test("the script under test exists", () => {
    expect(fs.existsSync(RUN_CI_TESTS)).toBe(true);
  });

  if (!PWSH_PRESENT) {
    // eslint-disable-next-line no-console
    console.warn(
      "[strictmode-smoke] pwsh not found on PATH; skipping run assertions (CI runners have pwsh)."
    );
    test.skip.each(["editmode", "standalone"])(
      "%s: runs under StrictMode without the empty-collection .Count throw",
      () => {}
    );
    return;
  }

  // standalone also exercises the configure-step arg concatenation (the
  // `... + $licenseArgs + $acceleratorArgs` path) before the test run, so the
  // `@()`-wrapped captures are also driven through a `+` concat under StrictMode.
  // (Even unwrapped these `+` operands would drop, being AutomationNull; the
  // wrap exists for the `.Count` read, not the concat.)
  test.each(["editmode", "standalone"])(
    "%s: runs under StrictMode without the empty-collection .Count throw",
    (mode) => {
      const ws = makeWorkspace();
      workspaces.push(ws);

      const result = runScript(mode, ws);
      const combined = `${result.stdout || ""}\n${result.stderr || ""}`;

      // The exact StrictMode failure string must NOT appear on any stream.
      expect(combined).not.toContain("cannot be found on this object");
      // The script must reach a clean exit.
      expect(result.status).toBe(0);
      // And it must have produced the NUnit results the stub wrote.
      expect(fs.existsSync(path.join(ws.artifacts, "results.xml"))).toBe(true);
    }
  );
});
