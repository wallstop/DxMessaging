/**
 * @fileoverview End-to-end StrictMode + license-leak smoke test for
 * scripts/unity/run-ci-tests.ps1.
 *
 * WHY THIS EXISTS (StrictMode): run-ci-tests.ps1 runs under `Set-StrictMode
 * -Version Latest`. `Get-AcceleratorArguments` `return @()` on its empty path. A
 * PowerShell function returning an EMPTY array emits ZERO objects, so a bare
 * capture (`$x = Get-Foo`) assigns AutomationNull (it compares equal to $null) --
 * the empty array unwraps to nothing. The script then evaluated
 * `$acceleratorArgs.Count`, which under StrictMode Latest (and 2.0+, verified on
 * pwsh 7.6.1, the CI runtime) THROWS "The property 'Count' cannot be found on
 * this object." That property-access throw is the real bug. The fix `@()`-wraps
 * the capture at the source.
 *
 * WHY THIS EXISTS (license leak): the repo uses classic SERIAL activation. CI
 * activates the paid seat (`Unity.exe -serial -username -password`) and MUST
 * return it (`Unity.exe -returnlicense`) on EVERY exit path -- clean exit, throw,
 * or a killed editor that still unwinds the finally. This test is the LIVE
 * companion to unity-license-leak-safety.test.js's static finally-return guard:
 * it ACTUALLY RUNS the real script via pwsh under its real StrictMode with the
 * serial creds set, drives both a FAILING and a PASSING editor stub, and asserts
 * the seat is RETURNED in BOTH cases (the marker file the stub appends to on
 * `-returnlicense`). The return-at-start (before the try) AND the finally-return
 * are both exercised.
 *
 * A STUB editor stands in for Unity.exe (cross-platform pwsh) so the run never
 * touches a real Unity install: it branches on its args (-returnlicense ->
 * append to the marker; -runTests -> write/skip results.xml; otherwise -- serial
 * activation / configure -- a clean exit-0 no-op). Everything lives in OS temp
 * dirs; the repo tree is never polluted.
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

const { sandboxHostFolderEnv } = require("../lib/spawn-env-sandbox");
const { normalizePwshText } = require("../lib/pwsh-output");

const REPO_ROOT = path.resolve(__dirname, "..", "..");
const RUN_CI_TESTS = path.join(REPO_ROOT, "scripts", "unity", "run-ci-tests.ps1");

// Merge a pwsh run's stdout+stderr and NORMALIZE it for phrase assertions.
// run-ci-tests.ps1 surfaces failures via both wrap-immune `::error::` annotations
// AND unhandled `throw`s; the latter are word-wrapped by PowerShell's ConciseView
// formatter at the host console width, which can split an asserted phrase across a
// `\n     | ` gutter (intermittently, on the narrower Windows runner).
// normalizePwshText rejoins that gutter and strips ANSI so the assertions are
// width-independent. (StrictMode-failure substrings like "cannot be found on this
// object" are asserted as ABSENT here; normalization only makes that stricter.)
function combinedText(run) {
  return normalizePwshText(`${run.stdout || ""}\n${run.stderr || ""}`);
}

// Env vars whose presence would route the script DOWN a non-empty/divergent
// path. We delete the accelerator endpoint so the empty-array branch (the one
// that triggered the StrictMode bug) is exercised. GITHUB_WORKSPACE is deleted
// so the default -RepoRoot does not point at the real repo (we pass an explicit
// temp RepoRoot). GITHUB_ACTIONS is deleted so the in-CI creds gate cannot fire
// under the Jest-in-CI harness (Jest itself runs with GITHUB_ACTIONS=true on the
// script-tests matrix); with the serial creds SET, $hasLicenseCreds is true and
// activate/return run regardless. UNITY_LICENSING_SERVER is deleted because the
// floating-server path was removed -- it must not exist in the test env.
//
// The host-default FOLDER vars (LOCALAPPDATA, ProgramFiles, ...) are NOT in this
// list: run-ci-tests.ps1 probes `$env:LOCALAPPDATA` (Unity caches) and
// `${env:ProgramFiles}` / `${env:ProgramFiles(x86)}` / `$env:LOCALAPPDATA` (the
// Unity Licensing Client). They are neutralized hermetically via
// sandboxHostFolderEnv (empty sandbox dirs) instead of a case-sensitive delete,
// so a real install on a Windows host can never leak in -- the same Windows
// case-insensitivity gotcha that bit ensure-editor.ps1's tests.
const ENV_TO_DELETE = [
  "UNITY_ACCELERATOR_ENDPOINT",
  "UNITY_LICENSE",
  "UNITY_LICENSE_B64",
  "UNITY_LICENSE_FILE",
  "GITHUB_WORKSPACE",
  "GITHUB_ACTIONS",
  "UNITY_LICENSING_SERVER"
];

// The serial-activation credentials. SETTING all three makes
// run-ci-tests.ps1's `$hasLicenseCreds` true, so:
//   - return-at-start runs BEFORE the try (reclaims any prior leaked seat),
//   - Invoke-UnityLicenseActivate runs as the first thing inside the try,
//   - Invoke-UnityLicenseReturn runs in the finally on EVERY exit path.
// They are bogus values: the STUB editor never validates them, it only branches
// on the presence of -serial / -returnlicense / -runTests in its args.
const SERIAL_CREDS = {
  UNITY_SERIAL: "SC-TEST-SERIAL-0000",
  UNITY_EMAIL: "ci-bot@example.invalid",
  UNITY_PASSWORD: "not-a-real-password"
};

// Cross-platform STUB editor body shared by all scenarios. It branches on args:
//   - `-returnlicense` (the seat return)     -> APPEND a line to the marker file
//     named by $env:DXM_SMOKE_RETURN_MARKER, then exit 0. This is how the test
//     proves the seat was returned (on return-at-start AND in the finally).
//   - `-runTests` (the test run)             -> {RUNTESTS_BODY} (per scenario).
//   - otherwise (-serial activation, or the standalone configure -executeMethod
//     pass) -> a clean exit-0 no-op (activation "succeeds").
// {RUNTESTS_BODY} is substituted per scenario below.
function stubEditor(runTestsBody) {
  return [
    "[CmdletBinding()] param([Parameter(ValueFromRemainingArguments=$true)][string[]]$Rest)",
    "$Rest = @($Rest)",
    "if ($Rest -contains '-returnlicense') {",
    "  $marker = $env:DXM_SMOKE_RETURN_MARKER",
    "  if ($marker) {",
    "    $d = Split-Path -Parent $marker",
    "    if ($d -and -not (Test-Path -LiteralPath $d)) { New-Item -ItemType Directory -Force -Path $d | Out-Null }",
    "    Add-Content -LiteralPath $marker -Value ('returned ' + ($Rest -join ' '))",
    "  }",
    "  exit 0",
    "}",
    "if ($Rest -contains '-runTests') {",
    runTestsBody,
    "}",
    "exit 0",
    ""
  ].join("\n");
}

// PASSING test run: write a passing NUnit results.xml to the path after
// -testResults and exit 0.
const RUNTESTS_PASSING = [
  "  $i = [array]::IndexOf($Rest, '-testResults')",
  "  if ($i -ge 0 -and ($i + 1) -lt $Rest.Count) {",
  "    $out = $Rest[$i + 1]; $d = Split-Path -Parent $out",
  "    if ($d) { New-Item -ItemType Directory -Force -Path $d | Out-Null }",
  '    \'<?xml version="1.0" encoding="utf-8"?><test-run total="1" passed="1" failed="0" skipped="0" result="Passed"></test-run>\' | Set-Content -LiteralPath $out -Encoding UTF8',
  "  }",
  "  exit 0"
].join("\n");

// LYING test run: exits 0 (a clean editor exit) but writes NO results.xml --
// the "Unity quietly produced nothing" failure mode. Test-NUnitResults must
// catch the missing file and fail LOUDLY rather than reporting a false green.
const RUNTESTS_NO_RESULTS = ["  exit 0"].join("\n");

// FAILING test run: exits non-zero and writes nothing. Stands in for a Unity run
// that crashes/times out AFTER the seat was activated. The leak-regression test
// asserts the seat is STILL returned (the finally fires) even though the run
// fails.
const RUNTESTS_FAILING = ["  exit 1"].join("\n");
const RUNTESTS_FAILING_CS8032 = [
  "  Write-Output \"warning CS8032: An instance of analyzer WallstopStudios.DxMessaging.SourceGenerators.DxMessageIdGenerator cannot be created from Editor/Analyzers/WallstopStudios.DxMessaging.SourceGenerators.dll : Could not load file or assembly 'Microsoft.CodeAnalysis, Version=4.2.0.0'.\"",
  "  Write-Output \"Tests/Runtime/Scripts/Messages/SimpleUntargetedMessage.cs(1,1): error CS0315: The type 'SimpleUntargetedMessage' cannot be used as type parameter 'T'.\"",
  "  exit 1"
].join("\n");

const STUB_EDITOR = stubEditor(RUNTESTS_PASSING);
const STUB_EDITOR_NO_RESULTS = stubEditor(RUNTESTS_NO_RESULTS);
const STUB_EDITOR_FAILING = stubEditor(RUNTESTS_FAILING);
const STUB_EDITOR_FAILING_CS8032 = stubEditor(RUNTESTS_FAILING_CS8032);

function pwshAvailable() {
  const probe = spawnSync("pwsh", ["-NoProfile", "-NonInteractive", "-Command", "exit 0"], {
    encoding: "utf8"
  });
  return probe.status === 0;
}

const PWSH_PRESENT = pwshAvailable();

// Build a clean env where the bug-triggering branches are hit AND the serial
// creds are present (so activate/return run). cleanedEnv neutralizes the
// host-default FOLDER vars hermetically (sandboxHostFolderEnv -> empty sandbox
// dirs under the per-run workspace, scrubbing every Windows case-variant),
// deletes the remaining path-diverging vars, then sets the three serial
// credentials.
function cleanedEnv(sandboxRoot) {
  const env = sandboxHostFolderEnv(process.env, sandboxRoot);
  for (const key of ENV_TO_DELETE) {
    delete env[key];
  }
  Object.assign(env, SERIAL_CREDS);
  return env;
}

// Stand up an isolated, self-contained workspace in the OS temp dir:
//   repoRoot/            -> package.json + Runtime/  (so Assert-RepoRoot passes)
//   artifacts/           -> -ArtifactsPath
//   project/             -> -ProjectPath
//   stub-editor.ps1      -> the passing -UnityEditorPath (writes results.xml)
//   stub-no-results.ps1  -> a stub that exits 0 but writes NO results.xml
//   stub-editor-failing.ps1 -> a stub whose -runTests exits non-zero
//   returned.marker      -> $env:DXM_SMOKE_RETURN_MARKER (return-proof file)
function makeWorkspace() {
  const base = fs.mkdtempSync(path.join(os.tmpdir(), "dxm-strictmode-smoke-"));
  const repoRoot = path.join(base, "repo");
  const artifacts = path.join(base, "artifacts");
  const project = path.join(base, "project");

  fs.mkdirSync(path.join(repoRoot, "Runtime"), { recursive: true });
  fs.mkdirSync(path.join(repoRoot, "Editor", "Analyzers"), { recursive: true });
  fs.writeFileSync(path.join(repoRoot, "package.json"), "{}\n", "utf8");
  for (const dllName of [
    "WallstopStudios.DxMessaging.SourceGenerators.dll",
    "WallstopStudios.DxMessaging.Analyzer.dll"
  ]) {
    fs.writeFileSync(path.join(repoRoot, "Editor", "Analyzers", dllName), "stub", "utf8");
  }
  fs.mkdirSync(artifacts, { recursive: true });
  fs.mkdirSync(project, { recursive: true });

  const stubPath = path.join(base, "stub-editor.ps1");
  fs.writeFileSync(stubPath, STUB_EDITOR, "utf8");

  const noResultsStubPath = path.join(base, "stub-no-results.ps1");
  fs.writeFileSync(noResultsStubPath, STUB_EDITOR_NO_RESULTS, "utf8");

  const failingStubPath = path.join(base, "stub-editor-failing.ps1");
  fs.writeFileSync(failingStubPath, STUB_EDITOR_FAILING, "utf8");

  const failingCs8032StubPath = path.join(base, "stub-editor-failing-cs8032.ps1");
  fs.writeFileSync(failingCs8032StubPath, STUB_EDITOR_FAILING_CS8032, "utf8");

  const returnMarker = path.join(base, "returned.marker");

  return {
    base,
    repoRoot,
    artifacts,
    project,
    stubPath,
    noResultsStubPath,
    failingStubPath,
    failingCs8032StubPath,
    returnMarker
  };
}

// Run run-ci-tests.ps1 against a given workspace. `editorPath` selects which stub
// editor stands in for Unity.exe (the passing one by default). The serial creds
// (set by cleanedEnv) make the script activate at start and return in the
// finally; DXM_SMOKE_RETURN_MARKER is where the stub records each -returnlicense.
function runScript(mode, ws, editorPath = ws.stubPath) {
  const env = cleanedEnv(path.join(ws.base, "host-env-sandbox"));
  env.DXM_SMOKE_RETURN_MARKER = ws.returnMarker;

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
      editorPath
    ],
    { env, encoding: "utf8", maxBuffer: 32 * 1024 * 1024 }
  );
}

// Count the number of `-returnlicense` entries the stub recorded in the marker
// file (one line per return invocation). Zero when the file does not exist.
function countReturnEntries(ws) {
  if (!fs.existsSync(ws.returnMarker)) {
    return 0;
  }
  return fs
    .readFileSync(ws.returnMarker, "utf8")
    .split(/\r?\n/)
    .filter((line) => line.trim().length > 0).length;
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
    test.skip.each(["editmode", "standalone"])(
      "%s: fails LOUDLY when the editor exits 0 but produces no results.xml",
      () => {}
    );
    return;
  }

  // standalone also exercises the configure-step arg concatenation (the
  // `... + $acceleratorArgs` path) before the test run, so the `@()`-wrapped
  // capture is also driven through a `+` concat under StrictMode. (Even unwrapped
  // these `+` operands would drop, being AutomationNull; the wrap exists for the
  // `.Count` read, not the concat.)
  test.each(["editmode", "standalone"])(
    "%s: runs under StrictMode without the empty-collection .Count throw",
    (mode) => {
      const ws = makeWorkspace();
      workspaces.push(ws);

      const result = runScript(mode, ws);
      const combined = combinedText(result);

      // The exact StrictMode failure string must NOT appear on any stream.
      expect(combined).not.toContain("cannot be found on this object");
      // The script must reach a clean exit.
      expect(result.status).toBe(0);
      // And it must have produced the NUnit results the stub wrote.
      expect(fs.existsSync(path.join(ws.artifacts, "results.xml"))).toBe(true);
    }
  );

  // Result-verification path: prove the runner fails LOUDLY when the editor
  // exits 0 but writes NO results.xml. This is the "Unity quietly produced
  // nothing" mode that the async-no-wait bug class could otherwise mask -- a
  // false-green pass is the worst outcome, so Test-NUnitResults MUST turn a
  // missing results file into a non-zero exit with a clear diagnostic. Data-
  // driven over modes so both the editmode and the standalone (configure +
  // test) paths are covered.
  test.each(["editmode", "standalone"])(
    "%s: fails LOUDLY when the editor exits 0 but produces no results.xml",
    (mode) => {
      const ws = makeWorkspace();
      workspaces.push(ws);

      const result = runScript(mode, ws, ws.noResultsStubPath);
      const combined = combinedText(result);

      // Must NOT silently succeed: a missing results.xml is a hard failure.
      expect(result.status).not.toBe(0);
      // The runner must surface a results-missing diagnostic (Test-NUnitResults
      // emits both the "::error::No NUnit results XML" annotation and the thrown
      // "did not produce NUnit results" message).
      expect(combined).toMatch(/did not produce NUnit results|No NUnit results XML/);
      // And it must NOT have fabricated a results.xml.
      expect(fs.existsSync(path.join(ws.artifacts, "results.xml"))).toBe(false);
    }
  );

  test("surfaces targeted diagnostics when the editor exits non-zero after CS8032", () => {
    const ws = makeWorkspace();
    workspaces.push(ws);

    const result = runScript("editmode", ws, ws.failingCs8032StubPath);
    const combined = combinedText(result);

    expect(result.status).not.toBe(0);
    expect(combined).toContain("Unity result failure diagnostics");
    expect(combined).toContain("Unity could not instantiate one or more DxMessaging");
    expect(combined).toContain(
      "Message fixture compile errors followed missing generated interfaces"
    );
    expect(fs.readFileSync(path.join(ws.artifacts, "unity.log"), "utf8")).toContain(
      "warning CS8032"
    );
  });
});

// ===========================================================================
// LEAK-REGRESSION: the serial-activated seat MUST be returned on EVERY exit
// path -- including a Unity run that FAILS after the seat was activated. This is
// the live companion to unity-license-leak-safety.test.js's static finally-
// return guard. With the serial creds set, run-ci-tests.ps1 returns the seat at
// START (before the try, reclaiming any prior leak) AND in the finally. We drive
// both a FAILING and a PASSING editor stub and assert the seat is returned (the
// marker file the stub appends to on `-returnlicense`) in BOTH cases, and that
// the return-at-start produced its own entry.
// ===========================================================================
describe("run-ci-tests.ps1 serial seat is always returned (leak regression)", () => {
  // Always-on sanity (even without pwsh): proves the script under test exists,
  // so a rename/move cannot silently turn this guard into a no-op.
  test("the script under test exists", () => {
    expect(fs.existsSync(RUN_CI_TESTS)).toBe(true);
  });

  if (!PWSH_PRESENT) {
    // eslint-disable-next-line no-console
    console.warn(
      "[leak-regression] pwsh not found on PATH; skipping run assertions (CI runners have pwsh)."
    );
    test.skip("returns the seat even when the editor FAILS (the leak regression)", () => {});
    test.skip("returns the seat on the SUCCESS path too", () => {});
    return;
  }

  test("returns the seat even when the editor FAILS (the leak regression)", () => {
    const ws = makeWorkspace();
    workspaces.push(ws);

    const result = runScript("editmode", ws, ws.failingStubPath);
    const combined = combinedText(result);

    // (1) The run FAILS loudly: a failing editor must propagate a non-zero exit
    // (the finally returns the seat, then the failure re-throws). And StrictMode
    // must not have tripped.
    expect(result.status).not.toBe(0);
    expect(combined).not.toContain("cannot be found on this object");

    // (2) THE leak regression: the seat was returned even though the editor
    // failed. The stub appends to the marker on each -returnlicense, and with a
    // failing run BOTH the return-at-start (before the try) AND the finally-return
    // fire, so there must be at least TWO entries -- proving return-on-failure
    // under StrictMode and the return-at-start backstop.
    expect(fs.existsSync(ws.returnMarker)).toBe(true);
    const entries = countReturnEntries(ws);
    // Return-at-start (1) + finally-return after the failure (1) = 2.
    expect(entries).toBeGreaterThanOrEqual(2);
  });

  test("returns the seat on the SUCCESS path too", () => {
    const ws = makeWorkspace();
    workspaces.push(ws);

    const result = runScript("editmode", ws, ws.stubPath);
    const combined = combinedText(result);

    // The passing editor stub wrote results.xml, so the run succeeds...
    expect(combined).not.toContain("cannot be found on this object");
    expect(result.status).toBe(0);
    expect(fs.existsSync(path.join(ws.artifacts, "results.xml"))).toBe(true);
    // ...and the seat is STILL returned (return-at-start + the finally on the
    // clean path), so at least two return entries are recorded.
    expect(fs.existsSync(ws.returnMarker)).toBe(true);
    expect(countReturnEntries(ws)).toBeGreaterThanOrEqual(2);
  });
});
