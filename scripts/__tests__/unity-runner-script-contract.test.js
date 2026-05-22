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

  test("license contract supports serial activation plus ULF / local base64 ULF fallback", () => {
    expect(content).toContain("UNITY_LICENSE");
    expect(content).toContain("UNITY_LICENSE_B64");
    expect(content).toContain("UNITY_SERIAL");
    expect(content).toContain("UNITY_EMAIL");
    expect(content).toContain("UNITY_PASSWORD");
    expect(content).not.toContain('-serial ""');
    expect(content).not.toContain("personal-email");
  });

  test("is serial-FIRST with the .ulf fallback retained", () => {
    // The repo removed the self-hosted Unity Licensing Server and switched to
    // classic SERIAL activation. The local script prefers UNITY_SERIAL +
    // UNITY_EMAIL + UNITY_PASSWORD (-> -serial -username -password), then falls
    // back to a .ulf in UNITY_LICENSE (raw) or UNITY_LICENSE_B64 (base64). The
    // floating-server surface must be GONE.
    expect(content).toContain("-serial");
    expect(content).toContain("-username");
    expect(content).toContain("-password");
    expect(content).not.toContain("UNITY_LICENSING_SERVER");
    expect(content).not.toContain("services-config.json");
  });

  test("returns the serial seat on EVERY exit via the EXIT trap (no leaked seat)", () => {
    // Serial activation consumes the single shared seat, so a local run MUST
    // return it on EVERY exit path. The return runs inside the same EXIT trap as
    // the chown cleanup (gated to serial mode) so it fires even when the editor
    // failed, and is best-effort (|| true) so it cannot mask the real exit code.
    expect(content).toContain("trap cleanup_ownership EXIT");
    expect(content).toMatch(/-returnlicense[\s\S]*?-username "\$\{UNITY_EMAIL\}"/);
    // The return is gated to the serial license mode (not run for the .ulf paths).
    expect(content).toMatch(/LICENSE_MODE.*==.*"serial"/);
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

  test("license contract supports serial activation plus ULF / local base64 ULF fallback", () => {
    expect(content).toContain("UNITY_LICENSE");
    expect(content).toContain("UNITY_LICENSE_B64");
    expect(content).toContain("UNITY_SERIAL");
    expect(content).toContain("UNITY_EMAIL");
    expect(content).toContain("UNITY_PASSWORD");
    expect(content).not.toContain('-serial ""');
    expect(content).not.toContain("personal-email");
  });

  test("is serial-FIRST with the .ulf fallback retained", () => {
    // Parity with run-tests.sh: classic SERIAL activation (UNITY_SERIAL +
    // UNITY_EMAIL + UNITY_PASSWORD -> -serial -username -password), with the
    // .ulf fallback (UNITY_LICENSE / UNITY_LICENSE_B64) retained for offline use.
    // The floating-server surface must be GONE.
    expect(content).toContain("-serial");
    expect(content).toContain("-username");
    expect(content).toContain("-password");
    expect(content).not.toContain("UNITY_LICENSING_SERVER");
    expect(content).not.toContain("services-config.json");
  });

  test("returns the serial seat on EVERY exit via the EXIT trap (no leaked seat)", () => {
    // Parity with run-tests.sh: the generated docker bash payload returns the
    // serial seat inside its EXIT trap so a local run never leaks the single
    // shared seat.
    expect(content).toContain("trap cleanup_ownership EXIT");
    expect(content).toMatch(/-returnlicense[\s\S]*?-username "\$\{UNITY_EMAIL\}"/);
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
    // NOTE: a bare `toContain("install-path")` was intentionally removed -- it
    // matched both the old positional bug shape and the new getter/`-s` form, so
    // it was tautological. The form-specific `'install-path', '-s'` SET and
    // `@('install-path')` getter assertions below cover the surface precisely.
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

  test("ensure-editor is resilient to the unstable standalone Unity CLI surface", () => {
    // ----------------------------------------------------------------------
    // Regression guards for the "wrong argument counts" CI failure. The
    // standalone Unity CLI v0.1.0-beta.x is a moving surface: `install-path`
    // with NO args is a GETTER, and the SET flag is undocumented. The script
    // must (a) never pass a positional dir to install-path, (b) set the path
    // best-effort via -s, (c) resolve the install root from the getter, (d)
    // discover editors defensively, and (e) gate module installs to standalone.
    // ----------------------------------------------------------------------

    // (a) install-path SET must use the -s flag, NOT a positional directory.
    // The original bug was `Invoke-UnityCli -Arguments @('install-path', $Root)`
    // (positional dir). That exact shape must never reappear in ANY form.
    expect(ensureEditor).toContain("'install-path', '-s'");
    expect(ensureEditor).not.toMatch(/'install-path',\s*\$Root\b/);
    expect(ensureEditor).not.toMatch(/'install-path',\s*\$InstallRoot\b/);
    // Catch the un-quoted positional shapes too: `install-path $Root` and
    // `'install-path' $Root` (no comma) -- either would re-introduce the
    // "Expected 0 arguments but got 1" failure the getter design eliminated.
    expect(ensureEditor).not.toMatch(/'?install-path'?\s+\$Root\b/);
    expect(ensureEditor).not.toMatch(/'?install-path'?\s+\$InstallRoot\b/);
    // A SET fallback exists: try -s first, then --set, before giving up.
    expect(ensureEditor).toContain("'install-path', '--set'");
    // The throwing invoker must NOT be used for setting the install path
    // (set is an optimization, not a hard requirement). The \s after
    // Invoke-UnityCli excludes the legitimate Invoke-UnityCliSafe calls.
    expect(ensureEditor).not.toMatch(/Invoke-UnityCli\s+-Arguments\s+@\('install-path'/);

    // (b) A non-throwing best-effort invoker exists and is distinct from the
    // throwing Invoke-UnityCli, so optional CLI ops cannot abort the bootstrap.
    // Both the best-effort invoker AND the capturing getter invoker must exist;
    // the design depends on getter output staying OFF the success stream that
    // run-ci-tests.ps1 reads via `Select-Object -Last 1`.
    expect(ensureEditor).toContain("function Invoke-UnityCliSafe");
    expect(ensureEditor).toContain("function Get-UnityCliOutput");
    // Invoke-UnityCliSafe is non-throwing: it returns a boolean and swallows
    // non-zero exits (it never re-throws on $LASTEXITCODE). Pin the boolean
    // return contract so a refactor cannot quietly make it throw.
    expect(ensureEditor).toMatch(/function Invoke-UnityCliSafe[\s\S]*?return \(\$exit -eq 0\)/);
    // Set-UnityCliInstallPath routes through the best-effort invoker. Constrain
    // the gap with `[^}]*?` (no brace, so the match cannot cross out of the
    // function body) so a stray `Invoke-UnityCliSafe ... '-s' ...` in an unrelated
    // later function could not satisfy this assertion.
    expect(ensureEditor).toMatch(
      /Set-UnityCliInstallPath[^}]*?Invoke-UnityCliSafe -Arguments @\('install-path', '-s', \$Root\)/
    );
    // Failure to set the path emits a notice (not an error) and continues. Pin
    // the ACTUAL emit (the Write-CiNotice fallback line), not a bare `::notice::`
    // token: the old loose form was satisfied by the function's own comment, so
    // it stayed green even if the real notice-on-failure line were deleted.
    expect(ensureEditor).toMatch(
      /Set-UnityCliInstallPath[\s\S]*?Write-CiNotice "Could not set the Unity CLI install path/
    );

    // (c) Getter-based authoritative resolver: runs `unity install-path` with
    // NO extra args via the CAPTURING (non-throwing) invoker and uses path-like
    // output as the root. It must route through Get-UnityCliOutput, never the
    // throwing/echoing invokers, so getter chatter cannot leak to the success
    // stream or abort the bootstrap on a non-zero beta exit.
    expect(ensureEditor).toContain("function Get-UnityCliInstallRoot");
    expect(ensureEditor).toContain("Get-UnityCliOutput -Arguments @('install-path')");
    expect(ensureEditor).not.toMatch(/Invoke-UnityCli(Safe)?\s+-Arguments\s+@\('install-path'\)/);
    expect(ensureEditor).toContain("Test-LooksLikeAbsolutePath");
    // The path-like guard accepts a Windows drive-letter or UNC path only, so a
    // beta banner line cannot be mistaken for the install root.
    expect(ensureEditor).toMatch(/function Test-LooksLikeAbsolutePath/);
    expect(ensureEditor).toMatch(/\^\[A-Za-z\]:\[\\\\\/\]/);

    // (d) Defensive JSON discovery: editors -i --format json read through the
    // capturing invoker and parsed inside a try/catch with ConvertFrom-Json so
    // malformed beta output returns $null (continues) instead of throwing.
    expect(ensureEditor).toContain("'editors', '-i', '--format', 'json'");
    expect(ensureEditor).toMatch(
      /Get-UnityCliOutput -Arguments @\('editors', '-i', '--format', 'json'\)/
    );
    expect(ensureEditor).toContain("ConvertFrom-Json");
    // The ConvertFrom-Json call sits in a try whose catch returns (does not
    // re-throw), proving malformed JSON is non-fatal. Constrain the catch-body
    // gap with `[^}]*?` so the `return` must live INSIDE the catch block (cannot
    // be satisfied by an empty catch followed by a `return` in a later function).
    expect(ensureEditor).toMatch(/try\s*\{[^}]*ConvertFrom-Json[\s\S]*?\}\s*catch\s*\{[^}]*?return/);

    // (e) Module gating: the install `-m windows-il2cpp` and the install-modules
    // path are reachable ONLY under the $WithWindowsIl2Cpp switch. editmode /
    // playmode (the switch absent) must never install a module. Pin `-m
    // windows-il2cpp` to the `$installArgs +=` line INSIDE the if block so a loose
    // `[\s\S]*?` match cannot be satisfied by the literal appearing in an
    // unrelated later function (e.g. Add-WindowsIl2CppModule's install call).
    expect(ensureEditor).toMatch(
      /if \(\$WithWindowsIl2Cpp\)\s*\{\s*\$installArgs \+= @\('-m', 'windows-il2cpp'\)/
    );
    expect(ensureEditor).toMatch(/elseif \(\$WithWindowsIl2Cpp\)/);
    // IL2CPP module install verifies the id via the -l listing before installing.
    expect(ensureEditor).toContain("'install-modules', '-e', $Version, '-l'");
    expect(ensureEditor).toContain("'install-modules', '-e', $Version, '-m'");
    // The -l listing is read non-throwing (best-effort verification). The module
    // install now runs through the CAPTURING (non-throwing) invoker so its exit
    // code AND output can be classified against the on-disk module layout: the
    // standalone beta CLI returns "No modules found to install." (exit 6) when
    // IL2CPP is ALREADY present, which is an idempotent no-op rather than a
    // failure. A genuine, non-benign failure with no on-disk corroboration STILL
    // throws (case 4 below), preserving the "real failure fails loudly" guarantee.
    expect(ensureEditor).toMatch(
      /Get-UnityCliOutput -Arguments @\('install-modules', '-e', \$Version, '-l'\)/
    );
    expect(ensureEditor).toMatch(
      /Invoke-UnityCliCapture -Arguments @\('install-modules', '-e', \$Version, '-m', \$moduleId\)/
    );
    // The OLD unconditional throwing install of the module must NOT reappear: it
    // wrongly aborted standalone on the idempotent "No modules found to install"
    // no-op. (Invoke-UnityCli is the throwing invoker; Invoke-UnityCliCapture is
    // its capturing, classify-then-decide replacement for the module install.)
    expect(ensureEditor).not.toMatch(
      /Invoke-UnityCli -Arguments @\('install-modules', '-e', \$Version, '-m', \$moduleId\)/
    );
    // Idempotency classification surface: a disk-authoritative IL2CPP probe gates
    // the "treat the CLI no-op as success" path, and a benign "nothing to
    // install / already installed" message is recognized. Both must be present so
    // the idempotent contract cannot silently regress to a hard failure.
    expect(ensureEditor).toContain("function Test-Il2CppModulePresent");
    expect(ensureEditor).toMatch(/Test-Il2CppModulePresent -EditorPath/);
    expect(ensureEditor).toMatch(/no modules found to install\|already installed/i);
    // The module installer receives the resolved editor path so it can probe disk.
    expect(ensureEditor).toMatch(
      /Add-WindowsIl2CppModule -Version \$UnityVersion -EditorPath \$editor/
    );
    // The base editor install is RETRIED (it has failed flakily after a long run
    // with exit 6) and routed through the capturing invoker so a final failure
    // throws WITH the CLI output tail + exit code for diagnosis.
    expect(ensureEditor).toContain("function Invoke-WithRetry");
    expect(ensureEditor).toMatch(
      /Invoke-WithRetry -MaxAttempts 2 -DelaySeconds 15 -Action \{\s*\$installResult = Invoke-UnityCliCapture -Arguments \$installArgs/
    );

    // Layered discovery wiring: install branch resolves through the layered
    // resolver, not the bare candidate search alone. Resolve-InstalledEditor
    // must consult the getter root, the candidate search, AND the JSON parse.
    expect(ensureEditor).toContain("Resolve-InstalledEditor");
    expect(ensureEditor).toMatch(
      /function Resolve-InstalledEditor[\s\S]*?Get-UnityCliInstallRoot[\s\S]*?Find-UnityEditor[\s\S]*?Resolve-EditorFromCliJson/
    );
    // Pin the MAIN-FLOW call site (not just the function definition): the install
    // branch must actually invoke the layered resolver with the requested version
    // and configured root. A definition that nothing calls would be dead code.
    expect(ensureEditor).toMatch(
      /\$editor = Resolve-InstalledEditor -Version \$UnityVersion -Root \$InstallRoot/
    );
    // Pin the module call site: the IL2CPP-only elseif branch must invoke the
    // module installer with the requested version.
    expect(ensureEditor).toMatch(/Add-WindowsIl2CppModule -Version \$UnityVersion/);
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

  test("run-ci-tests uses classic serial activation with a guaranteed license return", () => {
    // The direct CI runner uses classic SERIAL activation: it activates the paid
    // seat from UNITY_SERIAL/UNITY_EMAIL/UNITY_PASSWORD (-serial -username
    // -password) via Invoke-UnityLicenseActivate (which THROWS on failure) and
    // returns it via Invoke-UnityLicenseReturn (best-effort, never throws,
    // `-returnlicense`). These reads see the RAW file text, so the in-array flag
    // literals (-serial / -returnlicense) are assertable directly here.
    expect(runCi).toContain("Invoke-UnityLicenseActivate");
    expect(runCi).toContain("Invoke-UnityLicenseReturn");
    expect(runCi).toContain("-returnlicense");
    expect(runCi).toContain("-serial");
    expect(runCi).toContain("-username");
    expect(runCi).toContain("-password");
    // The three serial credentials gate activation (all required in CI).
    expect(runCi).toContain("UNITY_SERIAL");
    expect(runCi).toContain("UNITY_EMAIL");
    expect(runCi).toContain("UNITY_PASSWORD");
    // The OLD floating-license-server surface must be GONE from the CI runner.
    expect(runCi).not.toContain("--acquire-floating");
    expect(runCi).not.toContain("--return-floating");
    expect(runCi).not.toContain("UNITY_LICENSING_SERVER");
    expect(runCi).not.toContain("services-config.json");
    expect(runCi).not.toContain("Write-UnityLicensingServerConfig");
    expect(runCi).not.toContain("Resolve-UnityLicensingClient");
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
