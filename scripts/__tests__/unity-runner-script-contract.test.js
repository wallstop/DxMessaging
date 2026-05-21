/**
 * @fileoverview Contract tests for the Unity headless runner CLI surface.
 *
 * scripts/unity/run-tests.sh, run-tests.ps1, and activate-license.sh are the
 * canonical local entry points for the headless Unity workflow. They are
 * invoked from documentation, the .claude allowlist, the devcontainer test
 * workflow, and (transitively) the activate-license skill page.
 *
 * Renaming a flag or dropping the docker-outside-of-docker (DooD) path
 * translation contract would silently break those callers, so we lock the
 * surface here with text-grep assertions. We deliberately avoid invoking
 * `bash` / `pwsh` on these scripts: they would try to talk to Docker and
 * Unity and the grep approach is sub-millisecond.
 */

"use strict";

const fs = require("fs");
const path = require("path");
const childProcess = require("child_process");

const REPO_ROOT = path.resolve(__dirname, "..", "..");
const UNITY_SCRIPTS = path.join(REPO_ROOT, "scripts", "unity");

function readScript(relPath) {
  const abs = path.join(REPO_ROOT, relPath);
  expect(fs.existsSync(abs)).toBe(true);
  return fs.readFileSync(abs, "utf8");
}

function hasExecutableBit(absPath) {
  const relativePath = path.relative(REPO_ROOT, absPath).split(path.sep).join("/");
  const result = childProcess.spawnSync("git", ["ls-files", "--stage", "--", relativePath], {
    cwd: REPO_ROOT,
    encoding: "utf8"
  });

  expect(result.status).toBe(0);
  const [mode] = result.stdout.trim().split(/\s+/, 1);
  expect(mode).toBe("100755");
  return true;
}

describe("scripts/unity/run-tests.sh contract", () => {
  const shPath = path.join(UNITY_SCRIPTS, "run-tests.sh");
  let content;

  beforeAll(() => {
    content = readScript("scripts/unity/run-tests.sh");
  });

  test("file exists and is executable", () => {
    expect(fs.existsSync(shPath)).toBe(true);
    expect(hasExecutableBit(shPath)).toBe(true);
  });

  test.each([
    ["--platform"],
    ["--unity-version"],
    ["--filter"],
    ["--include-perf"],
    ["--include-integrations"],
    ["--include-comparisons"],
    ["--results"],
    ["--help"]
  ])("help text references %s", (flag) => {
    expect(content).toContain(flag);
  });

  test("references LOCAL_WORKSPACE_FOLDER (DooD path translation)", () => {
    expect(content).toContain("LOCAL_WORKSPACE_FOLDER");
  });

  test("references DXM_HOST_REPO_ROOT override (DooD path translation)", () => {
    expect(content).toContain("DXM_HOST_REPO_ROOT");
  });

  test("prefers docker-inspected devcontainer mount before LOCAL_WORKSPACE_FOLDER", () => {
    expect(content.indexOf("if is_container_runtime")).toBeGreaterThan(
      content.indexOf("DXM_HOST_REPO_ROOT")
    );
    expect(content.indexOf("detect_host_repo_root_from_container")).toBeLessThan(
      content.indexOf("Ignoring LOCAL_WORKSPACE_FOLDER=")
    );
  });

  test("license contract supports ULF, local base64 ULF, and serial paths", () => {
    expect(content).toContain("UNITY_LICENSE");
    expect(content).toContain("UNITY_LICENSE_B64");
    expect(content).toContain("UNITY_SERIAL");
    expect(content).toContain("UNITY_EMAIL");
    expect(content).toContain("UNITY_PASSWORD");
    expect(content).not.toContain('-serial ""');
    expect(content).not.toContain("personal-email");
  });

  test("auto-loads common local Unity license files before failing", () => {
    expect(content).toContain("UNITY_LICENSE_FILE");
    expect(content).toContain("ProgramData");
    expect(content).toContain("LOCALAPPDATA");
    expect(content).toContain(".local/share/unity3d/Unity/Unity_lic.ulf");
    expect(content).toContain("Library/Application Support/Unity/Unity_lic.ulf");
    expect(content).toContain('UNITY_LICENSE="$(cat "${license_path}")"');
  });

  test("forwards the perf commit environment into Unity containers", () => {
    expect(content).toContain("-e DX_PERF_COMMIT");
    expect(content).toContain("-e DX_PERF_BASELINE");
    expect(content).toContain("-e DX_PERF_BASELINE_MODE");
  });

  test("standalone runs natively via the single editor command path (no two-pass)", () => {
    // standalone now shares the same single inner command as editmode/playmode,
    // mapping -testPlatform to StandaloneLinux64 (Unity builds AND runs the
    // IL2CPP player in one pass; IL2CPP backend from ProjectSettings).
    expect(content).toContain('test_platform="StandaloneLinux64"');
    expect(content).toContain("-runTests -testPlatform ${test_platform}");
    expect(content).toContain("-assemblyNames");
    expect(content).toContain("-testFilter");
    // The runtime-only assembly list is threaded in for standalone.
    expect(content).toContain("runtimeOnly: ${runtime_only}");
    expect(content).toContain('RUNTIME_ONLY="true"');
  });

  test("does NOT contain the deleted two-pass standalone build/run symbols", () => {
    expect(content).not.toContain("build_standalone_build_cmd_inner");
    expect(content).not.toContain("build_standalone_run_cmd_inner");
    expect(content).not.toContain("DXM_IL2CPP_BUILD_PATH");
    expect(content).not.toContain("BuildIL2CPPTestPlayer");
    expect(content).not.toContain("-buildTarget StandaloneLinux64");
    expect(content).not.toContain("-executeMethod");
  });

  test("normalizes relative --results paths under the repo before validation", () => {
    expect(content).toMatch(/RESULTS_PATH=.*REPO_ROOT/);
    expect(content).toContain("${RESULTS_PATH#./}");
    expect(content).toContain("RESULTS_DIR_REAL=");
    expect(content).toContain("REPO_ROOT_REAL=");
    expect(content).toContain('${RESULTS_PATH#"${REPO_ROOT_REAL}/"}');
  });

  test("quotes caller-controlled inner bash arguments", () => {
    expect(content).toContain("printf '%q'");
    expect(content).toContain("filter_q=");
    expect(content).toContain("results_q=");
    expect(content).toContain("assemblies_q=");
  });

  test("returns Unity Library cache ownership to the invoking user", () => {
    expect(content).toContain("trap cleanup_ownership EXIT");
    expect(content).toContain("UNITY_LIBRARY_CACHE_SOURCE=");
    expect(content).toContain("dxm-unity-library-%s-%s");
    expect(content).toContain('chown -R "${USER_UID}:${USER_GID}" /workspace/.artifacts || true');
    expect(content).toContain('baseline_path="${DX_PERF_BASELINE}"');
    expect(content).toContain('baseline_path="/workspace/${baseline_path}"');
    expect(content).toContain('chown "${USER_UID}:${USER_GID}" "${baseline_path}"');
    expect(content).toContain('baseline_dir="$(dirname "${baseline_path}")"');
    expect(content).toContain(
      'chown -R "${USER_UID}:${USER_GID}" /workspace/.unity-test-project/Library || true'
    );
    expect(content).toContain(
      '-v "${UNITY_LIBRARY_CACHE_SOURCE}:/workspace/.unity-test-project/Library"'
    );
    expect(content).not.toContain("dxm-unity-library-cache:/workspace/.unity-test-project/Library");
  });
});

describe("scripts/unity/run-tests.ps1 contract", () => {
  const ps1Path = path.join(UNITY_SCRIPTS, "run-tests.ps1");
  let content;

  beforeAll(() => {
    content = readScript("scripts/unity/run-tests.ps1");
  });

  test("file exists", () => {
    expect(fs.existsSync(ps1Path)).toBe(true);
  });

  test("ValidateSet pins editmode/playmode/standalone (whitespace-tolerant)", () => {
    // Allow optional whitespace between members so contributors can run
    // PowerShell formatters without invalidating the contract test.
    expect(content).toMatch(
      /\[ValidateSet\(\s*'editmode'\s*,\s*'playmode'\s*,\s*'standalone'\s*\)\]/
    );
  });

  test.each([
    ["Platform"],
    ["UnityVersion"],
    ["IncludePerf"],
    ["IncludeIntegrations"],
    ["IncludeComparisons"],
    ["Runner"]
  ])("declares parameter %s (as $Name and -Name)", (paramName) => {
    // PowerShell convention: `param([type]$Name)` declares the parameter,
    // callers pass it as `-Name`. We require BOTH forms: the variable
    // declaration AND at least one caller-side reference (or .PARAMETER
    // doc) so a typo in either half fails loudly.
    const variableForm = new RegExp(`\\$${paramName}\\b`);
    const callerForm = new RegExp(`-${paramName}\\b`);
    const docForm = new RegExp(`\\.PARAMETER\\s+${paramName}\\b`);
    expect(content).toMatch(variableForm);
    // Either a caller-side `-Name` reference (in examples / Write-Host
    // help text) or a `.PARAMETER Name` doc block satisfies the
    // discoverability half of the contract.
    expect(callerForm.test(content) || docForm.test(content)).toBe(true);
  });

  test("references LOCAL_WORKSPACE_FOLDER (DooD path translation)", () => {
    expect(content).toContain("LOCAL_WORKSPACE_FOLDER");
  });

  test("references DXM_HOST_REPO_ROOT override (DooD path translation)", () => {
    expect(content).toContain("DXM_HOST_REPO_ROOT");
  });

  test("prefers docker-inspected devcontainer mount before LOCAL_WORKSPACE_FOLDER", () => {
    expect(content.indexOf("$InContainer = Test-ContainerRuntime")).toBeGreaterThan(
      content.indexOf("$env:DXM_HOST_REPO_ROOT")
    );
    expect(content.indexOf("$HostRepoRoot = Get-InspectedHostRepoRoot")).toBeLessThan(
      content.indexOf("Ignoring LOCAL_WORKSPACE_FOLDER=")
    );
  });

  test("license contract supports ULF, local base64 ULF, and serial paths", () => {
    expect(content).toContain("UNITY_LICENSE");
    expect(content).toContain("UNITY_LICENSE_B64");
    expect(content).toContain("UNITY_SERIAL");
    expect(content).not.toContain('-serial ""');
    expect(content).not.toContain("personal-email");
  });

  test("auto-loads common local Unity license files before failing", () => {
    expect(content).toContain("UNITY_LICENSE_FILE");
    expect(content).toContain("ProgramData");
    expect(content).toContain("LOCALAPPDATA");
    expect(content).toContain(".local/share/unity3d/Unity/Unity_lic.ulf");
    expect(content).toContain("Library/Application Support/Unity/Unity_lic.ulf");
    expect(content).toContain("Get-Content -LiteralPath $licensePath -Raw");
  });

  test("normalizes generated bash payloads to LF for docker bash", () => {
    expect(content).toContain("function ConvertTo-BashScriptText");
    expect(content).toContain('return $Value.Replace("`r`n", "`n")');
    expect(content).toContain("return ConvertTo-BashScriptText $sb.ToString()");
  });

  test("supports local Windows Unity execution for editmode and playmode", () => {
    expect(content).toMatch(/\[ValidateSet\(\s*'auto'\s*,\s*'docker'\s*,\s*'local'\s*\)\]/);
    expect(content).toContain("function Find-UnityEditorPath");
    expect(content).toContain("UNITY_EDITOR_PATH");
    expect(content).toContain("UNITY_PATH");
    expect(content).toContain("Unity/Hub/Editor/$Version/Editor/Unity.exe");
    expect(content).toContain("function Invoke-LocalUnityTests");
    expect(content).toContain("Launching local Unity");
    expect(content).toContain("$IsWindows -and $Platform -ne 'standalone'");
    expect(content).toContain("$ResolvedRunner = 'local'");
    expect(content).toContain("-Runner local does not support standalone");
  });

  test("forwards the perf commit environment into Unity containers", () => {
    expect(content).toContain("'-e', 'DX_PERF_COMMIT'");
    expect(content).toContain("'-e', 'DX_PERF_BASELINE'");
    expect(content).toContain("'-e', 'DX_PERF_BASELINE_MODE'");
  });

  test("standalone runs natively via the single editor command path (no two-pass)", () => {
    // Parity with run-tests.sh: standalone shares Get-EditorCommandInner and
    // maps -testPlatform to StandaloneLinux64 (single build+run pass; IL2CPP
    // backend from ProjectSettings).
    expect(content).toContain("StandaloneLinux64");
    expect(content).toContain("-assemblyNames");
    expect(content).toContain("-testFilter");
    // The runtime-only assembly list is threaded in for standalone.
    expect(content).toContain("runtimeOnly: $runtimeOnlyBool");
    expect(content).toContain("-RuntimeOnlyFlag:($Platform -eq 'standalone')");
  });

  test("does NOT contain the deleted two-pass standalone build/run symbols", () => {
    expect(content).not.toContain("Get-StandaloneBuildCommandInner");
    expect(content).not.toContain("Get-StandaloneRunCommandInner");
    expect(content).not.toContain("DXM_IL2CPP_BUILD_PATH");
    expect(content).not.toContain("BuildIL2CPPTestPlayer");
    expect(content).not.toContain("-buildTarget StandaloneLinux64");
    expect(content).not.toContain("-executeMethod");
  });

  test("uses boundary-aware Results path validation", () => {
    expect(content).toContain("$RepoRootReal");
    expect(content).toContain("$ResultsDirReal");
    expect(content).toContain("$ResultsRel -eq $RepoRootReal");
    expect(content).toContain('$ResultsRel.StartsWith("$RepoRootReal/")');
    expect(content).toContain('$ResultsRel.StartsWith("$RepoRootReal\\")');
    expect(content).toContain("$Results = Join-Path $RepoRoot $Results");
    expect(content).not.toContain("TrimStart('.', '/', '\\')");
  });

  test("quotes caller-controlled inner bash arguments", () => {
    expect(content).toContain("ConvertTo-BashSingleQuotedString");
    expect(content).toContain("$filterQ");
    expect(content).toContain("$resultsQ");
    expect(content).toContain("$assembliesQ");
  });

  test("returns Unity Library cache ownership to the invoking user", () => {
    expect(content).toContain("trap cleanup_ownership EXIT");
    expect(content).toContain("$UnityLibraryCacheSource");
    expect(content).toContain("dxm-unity-library-$ImageTag-$Platform");
    expect(content).toContain('chown -R "${USER_UID}:${USER_GID}" /workspace/.artifacts || true');
    expect(content).toContain('baseline_path="${DX_PERF_BASELINE}"');
    expect(content).toContain('baseline_path="/workspace/${baseline_path}"');
    expect(content).toContain('chown "${USER_UID}:${USER_GID}" "${baseline_path}"');
    expect(content).toContain('baseline_dir="$(dirname "${baseline_path}")"');
    expect(content).toContain(
      'chown -R "${USER_UID}:${USER_GID}" /workspace/.unity-test-project/Library || true'
    );
    expect(content).toContain('"$UnityLibraryCacheSource`:/workspace/.unity-test-project/Library"');
    expect(content).not.toContain("dxm-unity-library-cache:/workspace/.unity-test-project/Library");
  });
});

describe("scripts/unity direct CI runner contract", () => {
  const ensureEditor = readScript("scripts/unity/ensure-editor.ps1");
  const runCi = readScript("scripts/unity/run-ci-tests.ps1");

  test("ensure-editor installs through the standalone Unity CLI and resolves Unity.exe", () => {
    expect(ensureEditor).toContain("UnityVersion");
    expect(ensureEditor).toContain("Ensure-UnityCli");
    expect(ensureEditor).toContain("Set-UnityCliInstallPath");
    expect(ensureEditor).toContain("install-path");
    expect(ensureEditor).toContain("$installArgs = @('install', $UnityVersion)");
    expect(ensureEditor).toContain("install-modules");
    expect(ensureEditor).toContain("windows-il2cpp");
    expect(ensureEditor).toContain("Unity.exe");
    // Regression guard for the Unity CLI PATH fix: the installer updates only
    // the User-scope registry PATH, so the script must refresh the session
    // PATH from the registry and resolve the CLI via $script:UnityCliPath
    // (absolute-path fallback) instead of a bare `unity` on PATH.
    expect(ensureEditor).toContain("$script:UnityCliPath");
    expect(ensureEditor).toContain("GetEnvironmentVariable");
    // Structural guards (beyond token tripwires): the registry-refresh helper
    // must exist, the post-install loop must sleep AND re-probe `unity` after a
    // refresh, and an absolute-path fallback must resolve the installer's known
    // %LOCALAPPDATA%\Unity\bin\unity.exe target when the CLI never lands on PATH.
    expect(ensureEditor).toContain("Update-SessionPathFromRegistry");
    expect(ensureEditor).toContain("Start-Sleep");
    // Post-install retry: a `Get-Command unity` must appear AFTER the standalone
    // installer download so the script re-probes once the registry/PATH lands.
    const installIndex = ensureEditor.indexOf("install.ps1");
    expect(installIndex).toBeGreaterThan(-1);
    expect(ensureEditor.indexOf("Get-Command unity", installIndex)).toBeGreaterThan(installIndex);
    // Absolute-path fallback literal.
    expect(ensureEditor).toMatch(/Unity\\bin\\unity\.exe/);
  });

  test("run-ci-tests exposes the required CI surface", () => {
    for (const token of [
      "UnityVersion",
      "TestMode",
      "AssemblyNames",
      "ArtifactsPath",
      "ProjectPath",
      "GenerateOnly"
    ]) {
      expect(runCi).toContain(token);
    }
    expect(runCi).toMatch(
      /\[ValidateSet\(\s*'editmode'\s*,\s*'playmode'\s*,\s*'standalone'\s*\)\]/
    );
  });

  test("run-ci-tests creates an ephemeral package host project", () => {
    expect(runCi).toContain("Initialize-EphemeralProject");
    expect(runCi).toContain("Packages\\manifest.json");
    expect(runCi).toContain("ProjectSettings\\ProjectVersion.txt");
    expect(runCi).toContain("testables = @($PackageName)");
    expect(runCi).toContain(".artifacts\\unity\\projects\\$Version-$Mode");
    expect(runCi).not.toContain("projectPath: .unity-test-project");
  });

  test("run-ci-tests owns cache, Accelerator, and result validation diagnostics", () => {
    expect(runCi).toContain("Initialize-UnityCacheEnvironment");
    expect(runCi).toContain("UPM_CACHE_ROOT");
    expect(runCi).toContain("UNITY_ACCELERATOR_ENDPOINT");
    expect(runCi).toContain("-EnableCacheServer");
    expect(runCi).toContain("Test-NUnitResults");
    expect(runCi).toContain("0 tests ran");
  });
});

// ---------------------------------------------------------------------------
// Regression guards (data-driven over BOTH runner scripts).
//
// These lock in the fix for the "CI mode short-circuit" bug: both run-tests.sh
// and run-tests.ps1 used to detect CI=true, print would-be
// game-ci/unity-test-runner@v4 parameters, and exit 0 WITHOUT running Unity.
// Every CI job went green having run ZERO tests. The principle (.llm/context.md:
// never silently default to permissive behavior) is: a runner must NEVER report
// success without a results.xml proving tests ran (total > 0), on EVERY path.
//
// We assert against BOTH scripts at once so a regression in either half — or a
// reintroduction of the no-op stub — fails loudly. Assertions stay simple and
// string-based to match this file's existing grep style.
// ---------------------------------------------------------------------------
describe("Unity runner scripts never short-circuit success without running tests", () => {
  const RUNNER_SCRIPTS = [
    { name: "run-tests.sh", content: readScript("scripts/unity/run-tests.sh") },
    { name: "run-tests.ps1", content: readScript("scripts/unity/run-tests.ps1") }
  ];

  test.each(RUNNER_SCRIPTS)(
    "$name does NOT contain the no-op CI short-circuit stub strings",
    ({ content }) => {
      // The old stub printed these exact strings before exiting 0 without
      // spawning Unity. None may ever reappear.
      expect(content).not.toContain("skipping local docker invocation");
      expect(content).not.toContain("CI mode detected");
      expect(content).not.toContain("game-ci/unity-test-runner@v4 parameters");
      // A case-insensitive belt-and-suspenders check for the most distinctive
      // fragment (catches re-wordings like "Skipping local docker run").
      expect(content).not.toMatch(/skipping local docker/i);
    }
  );

  test.each(RUNNER_SCRIPTS)(
    "$name routes results through the shared parse-test-results.py validator",
    ({ content }) => {
      // Success can only be reported via Write-ResultsSummary /
      // print_results_summary, which delegate to the shared parser.
      expect(content).toContain("parse-test-results.py");
      // The total==0 guard text is the load-bearing "tests actually ran"
      // assertion; it must be present in both scripts.
      expect(content).toContain("0 tests ran");
    }
  );

  test.each(RUNNER_SCRIPTS)(
    "$name emits CI annotations via the CI env var (annotations, not control flow)",
    ({ content }) => {
      // CI is still referenced — but ONLY to emit ::error::/::notice::
      // GitHub Actions annotations, never to gate execution.
      expect(content).toContain("::error::");
      expect(content).toContain("::notice::");
    }
  );
});

describe("scripts/unity/activate-license.sh contract", () => {
  const licPath = path.join(UNITY_SCRIPTS, "activate-license.sh");
  let content;

  beforeAll(() => {
    content = readScript("scripts/unity/activate-license.sh");
  });

  test("file exists and is executable", () => {
    expect(fs.existsSync(licPath)).toBe(true);
    expect(hasExecutableBit(licPath)).toBe(true);
  });

  test("exposes --check mode (diagnostic / default)", () => {
    expect(content).toContain("--check");
  });

  test("exposes --apply mode (Pro .ulf encoder)", () => {
    expect(content).toContain("--apply");
  });

  test("references LOCAL_WORKSPACE_FOLDER (DooD path translation)", () => {
    expect(content).toContain("LOCAL_WORKSPACE_FOLDER");
  });

  test("references DXM_HOST_REPO_ROOT override (DooD path translation)", () => {
    expect(content).toContain("DXM_HOST_REPO_ROOT");
  });

  test("prefers docker-inspected devcontainer mount before LOCAL_WORKSPACE_FOLDER", () => {
    expect(content.indexOf("if is_container_runtime")).toBeGreaterThan(
      content.indexOf("DXM_HOST_REPO_ROOT")
    );
    expect(content.indexOf("detect_host_repo_root_from_container")).toBeLessThan(
      content.indexOf("Ignoring LOCAL_WORKSPACE_FOLDER=")
    );
  });

  test("does not advertise email/password-only Personal activation", () => {
    expect(content).toContain("UNITY_LICENSE_B64");
    expect(content).toContain("UNITY_SERIAL");
    expect(content).not.toContain('-serial ""');
    expect(content).not.toContain("Personal activation succeeded");
  });
});
