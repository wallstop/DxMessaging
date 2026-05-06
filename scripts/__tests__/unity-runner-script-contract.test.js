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

const REPO_ROOT = path.resolve(__dirname, "..", "..");
const UNITY_SCRIPTS = path.join(REPO_ROOT, "scripts", "unity");

function readScript(relPath) {
  const abs = path.join(REPO_ROOT, relPath);
  expect(fs.existsSync(abs)).toBe(true);
  return fs.readFileSync(abs, "utf8");
}

function hasExecutableBit(absPath) {
  const mode = fs.statSync(absPath).mode;
  // Any of user/group/other execute bits set is enough; chmod tooling on
  // contributor machines varies, but git+the CI runner only require one.
  return (mode & 0o111) !== 0;
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

  test("forwards the perf commit environment into Unity containers", () => {
    expect(content).toContain("-e DX_PERF_COMMIT");
  });

  test("standalone player run forwards the same assembly and filter controls", () => {
    const standaloneRun = content.slice(content.indexOf("build_standalone_run_cmd_inner"));
    expect(standaloneRun).toContain("-assemblyNames");
    expect(standaloneRun).toContain("-testFilter");
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
    expect(content).toContain(
      'chown -R "${USER_UID}:${USER_GID}" /workspace/.artifacts/unity || true'
    );
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
    ["IncludeComparisons"]
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

  test("forwards the perf commit environment into Unity containers", () => {
    expect(content).toContain("'-e', 'DX_PERF_COMMIT'");
  });

  test("standalone player run forwards the same assembly and filter controls", () => {
    const standaloneRun = content.slice(content.indexOf("Get-StandaloneRunCommandInner"));
    expect(standaloneRun).toContain("-assemblyNames");
    expect(standaloneRun).toContain("-testFilter");
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
    expect(content).toContain(
      'chown -R "${USER_UID}:${USER_GID}" /workspace/.unity-test-project/Library || true'
    );
    expect(content).toContain('"$UnityLibraryCacheSource`:/workspace/.unity-test-project/Library"');
    expect(content).not.toContain("dxm-unity-library-cache:/workspace/.unity-test-project/Library");
  });
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
