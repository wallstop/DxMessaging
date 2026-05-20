<#
    .SYNOPSIS
        Headless Unity Test Runner driver (PowerShell parity).

    .DESCRIPTION
        Mirror of scripts/unity/run-tests.sh — keep behavior in sync.
        Spawns an ephemeral unityci/editor container via the host docker
        socket and streams Unity's log to stdout in realtime.

        Default behavior excludes Benchmarks/Allocations/Comparisons asmdefs
        per .llm/context.md line 114 (perf isolation). Use -IncludePerf to
        include them.

        Default behavior also excludes the DI integration suites
        (VContainer/Zenject/Reflex) because their backing packages are not
        present in the test project's manifest.json. Use
        -IncludeIntegrations to opt in.

        Bind-mount path translation (docker-outside-of-docker): the docker
        daemon runs on the HOST, so the -v source must be a HOST path, not
        a path inside this devcontainer. Resolution priority:
            1) $env:DXM_HOST_REPO_ROOT  (absolute manual override)
            2) docker inspect of the current devcontainer bind mount
            3) $env:LOCAL_WORKSPACE_FOLDER  (absolute path from VS Code Dev Containers;
               used only outside a container or after inspect fails)
            4) the in-script $RepoRoot  (only when NOT inside a container)

    .PARAMETER Platform
        editmode | playmode | standalone (required for normal runs; omit
        when passing -Help).

    .PARAMETER UnityVersion
        Unity Editor version tag (default: 2022.3.45f1 or $env:UNITY_VERSION).

    .PARAMETER Filter
        Forwarded to Unity's -testFilter.

    .PARAMETER IncludePerf
        Include Benchmarks/Allocations asmdefs that do not require external
        comparison packages.

    .PARAMETER IncludeComparisons
        Include comparison benchmarks against MessagePipe, UniRx, UniTask, and
        Zenject. Requires those packages in .unity-test-project/Packages/manifest.json.

    .PARAMETER IncludeIntegrations
        Include VContainer/Zenject/Reflex integration asmdefs (default:
        excluded). Requires the corresponding DI packages to be present in
        .unity-test-project/Packages/manifest.json.

    .PARAMETER Results
        Path to write the NUnit XML results
        (default: .artifacts/unity/results.xml). Must be within the repo
        root (the docker bind-mount only exposes the repo root).

    .PARAMETER Runner
        auto | docker | local. auto uses local Unity on Windows for editmode
        and playmode, and docker elsewhere. standalone always uses docker.

    .EXAMPLE
        pwsh -NoProfile -File scripts/unity/run-tests.ps1 -Platform editmode

    .EXAMPLE
        pwsh -NoProfile -File scripts/unity/run-tests.ps1 -Platform playmode `
            -Filter 'MessageBus.*'

    .EXAMPLE
        pwsh -NoProfile -File scripts/unity/run-tests.ps1 -Platform standalone

    .NOTES
        Mirror of scripts/unity/run-tests.sh — when changing CLI surface or
        docker invocation, update both files in the same change. The repo's
        .llm/context.md mandates synchronized JS+PS dual implementations.
#>

[CmdletBinding()]
param(
    # M1: Platform must NOT be Mandatory — that blocks `-Help` in non-
    # interactive shells. We validate manually below.
    [ValidateSet('editmode', 'playmode', 'standalone')]
    [string]$Platform,

    [string]$UnityVersion,

    [string]$Filter,

    [switch]$IncludePerf,

    [switch]$IncludeIntegrations,

    [switch]$IncludeComparisons,

    [string]$Results,

    [ValidateSet('auto', 'docker', 'local')]
    [string]$Runner = 'auto',

    [switch]$Help
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# Help short-circuit (must come before -Platform validation so `-Help` works
# even when no platform is provided).
# ---------------------------------------------------------------------------
if ($Help) {
    Get-Help $PSCommandPath -Detailed
    exit 0
}

if ([string]::IsNullOrWhiteSpace($Platform)) {
    Write-Host @"
ERROR: -Platform is required (editmode | playmode | standalone).

Usage:
  pwsh -NoProfile -File scripts/unity/run-tests.ps1 -Platform <mode> [options]

For full help, run with -Help.
"@ -ForegroundColor Red
    exit 2
}

# ---------------------------------------------------------------------------
# Resolve repo root + paths
# ---------------------------------------------------------------------------
$ScriptDir = Split-Path -Parent $PSCommandPath
$RepoRoot  = (Resolve-Path (Join-Path $ScriptDir '..\..')).Path
$ArtifactsDir = Join-Path $RepoRoot '.artifacts/unity'

if (-not $UnityVersion -or [string]::IsNullOrWhiteSpace($UnityVersion)) {
    $envVersion = $env:UNITY_VERSION
    if ([string]::IsNullOrWhiteSpace($envVersion)) {
        $UnityVersion = '2022.3.45f1'
    } else {
        $UnityVersion = $envVersion
    }
}

if (-not $Results -or [string]::IsNullOrWhiteSpace($Results)) {
    $Results = Join-Path $ArtifactsDir 'results.xml'
}
if (-not [System.IO.Path]::IsPathRooted($Results)) {
    $Results = Join-Path $RepoRoot $Results
}
$ResultsDir = Split-Path -Parent $Results
$ResultsLeaf = Split-Path -Leaf $Results
if ([string]::IsNullOrWhiteSpace($ResultsLeaf) -or $ResultsLeaf -eq '.' -or $ResultsLeaf -eq '..') {
    Write-Host @"
ERROR: -Results path must be a file within the repo (got '$Results').
"@ -ForegroundColor Red
    exit 2
}
if (-not (Test-Path $ResultsDir)) {
    New-Item -ItemType Directory -Path $ResultsDir -Force | Out-Null
}
$RepoRootReal = (Resolve-Path $RepoRoot).Path
$ResultsDirReal = (Resolve-Path $ResultsDir).Path
$Results = Join-Path $ResultsDirReal $ResultsLeaf

# ---------------------------------------------------------------------------
# Resolve image tag
# ---------------------------------------------------------------------------
if ($Platform -eq 'standalone') {
    $ImageTag = "$UnityVersion-linux-il2cpp-3"
} else {
    $ImageTag = "$UnityVersion-base-3"
}
$ImageRef = "unityci/editor:$ImageTag"
$UnityLibraryCacheSource = "dxm-unity-library-$ImageTag-$Platform" -replace '[^A-Za-z0-9_.-]', '-'

# ---------------------------------------------------------------------------
# Build assembly include list via shared discovery library
# ---------------------------------------------------------------------------
function Get-AssemblyList {
    param(
        [bool]$IncludePerfFlag,
        [bool]$IncludeIntegrationsFlag,
        [bool]$IncludeComparisonsFlag
    )

    # Single source of truth: defaultIncludeAssemblies in
    # scripts/unity/lib/asmdef-discovery.js. Pass both opt-in flags through.
    $perfBool = if ($IncludePerfFlag) { 'true' } else { 'false' }
    $integBool = if ($IncludeIntegrationsFlag) { 'true' } else { 'false' }
    $comparisonsBool = if ($IncludeComparisonsFlag) { 'true' } else { 'false' }
    $opts = "{ includePerf: $perfBool, includeIntegrations: $integBool, includeComparisons: $comparisonsBool }"
    $nodeScript = "const m=require('./scripts/unity/lib/asmdef-discovery.js');console.log(m.defaultIncludeAssemblies(process.cwd(), $opts).join(';'));"

    Push-Location $RepoRoot
    try {
        $output = & node -e $nodeScript
        if ($LASTEXITCODE -ne 0) {
            throw "asmdef discovery failed (exit code $LASTEXITCODE)"
        }
        return ($output -join "`n").Trim()
    }
    finally {
        Pop-Location
    }
}

$Assemblies = Get-AssemblyList `
    -IncludePerfFlag:$IncludePerf.IsPresent `
    -IncludeIntegrationsFlag:$IncludeIntegrations.IsPresent `
    -IncludeComparisonsFlag:$IncludeComparisons.IsPresent
if ([string]::IsNullOrWhiteSpace($Assemblies)) {
    Write-Error 'Assembly include list is empty (asmdef discovery failed).'
    exit 1
}

# ---------------------------------------------------------------------------
# Summary tail (B2: delegate parsing to scripts/unity/lib/parse-test-results.py
# so this script, the IL2CPP workflow, and run-tests.sh all share one parser
# implementation. The helper emits "OK total=.. passed=.. failed=.. skipped=.."
# on success and "PARSE_ERROR:<reason>" on failure with exit code 2.)
#
# Defined here (before any runner dispatch) so BOTH the local and docker paths
# can route their results validation through the same function. The contract is
# uniform: a runner must NEVER report success without a results.xml proving
# tests actually ran (total > 0). Under CI we additionally emit GitHub Actions
# annotations so failures surface in the Actions UI (annotations only -- they
# do NOT change control flow or skip work).
# ---------------------------------------------------------------------------
function Write-ResultsSummary {
    param([string]$ResultsXml)

    if (-not (Test-Path $ResultsXml)) {
        Write-Host "No results.xml at $ResultsXml" -ForegroundColor Yellow
        if ($env:CI -eq 'true') {
            Write-Host "::error::run-tests: no results.xml produced for $Platform/$UnityVersion -- tests did not run"
        }
        return 2
    }

    $parser = Join-Path $RepoRoot 'scripts/unity/lib/parse-test-results.py'
    $summary = & python3 $parser $ResultsXml
    if ($LASTEXITCODE -ne 0 -or -not ($summary -match '^OK ')) {
        Write-Host "Could not parse results summary: $summary" -ForegroundColor Yellow
        if ($env:CI -eq 'true') {
            Write-Host "::error::run-tests: could not parse results.xml for $Platform/$UnityVersion -- $summary"
        }
        return 2
    }

    $kvLine = $summary -replace '^OK ', ''
    $kvs = @{}
    foreach ($pair in ($kvLine -split '\s+')) {
        if ($pair -match '^(\w+)=(.*)$') {
            $kvs[$Matches[1]] = $Matches[2]
        }
    }
    $total   = if ($kvs.ContainsKey('total'))   { $kvs['total']   } else { '0' }
    $passed  = if ($kvs.ContainsKey('passed'))  { $kvs['passed']  } else { '0' }
    $failed  = if ($kvs.ContainsKey('failed'))  { $kvs['failed']  } else { '0' }
    $skipped = if ($kvs.ContainsKey('skipped')) { $kvs['skipped'] } else { '0' }

    if ($total -eq '0') {
        Write-Host 'ERROR: 0 tests ran. Check filter / assembly list.' -ForegroundColor Red
        Write-Host "  failed=$failed passed=$passed skipped=$skipped" -ForegroundColor Red
        if ($env:CI -eq 'true') {
            Write-Host "::error::run-tests: 0 tests ran (total=0) for $Platform/$UnityVersion -- check assembly list / filter"
        }
        return 2
    }

    if ($failed -eq '0') {
        Write-Host "PASS $passed passed (total=$total skipped=$skipped)" -ForegroundColor Green
        if ($env:CI -eq 'true') {
            Write-Host "::notice::run-tests: $passed passed (total=$total skipped=$skipped) for $Platform/$UnityVersion"
        }
        return 0
    }

    Write-Host "FAIL $failed failed of $total (passed=$passed skipped=$skipped)" -ForegroundColor Red
    if ($env:CI -eq 'true') {
        Write-Host "::error::run-tests: $failed failed of $total for $Platform/$UnityVersion (passed=$passed skipped=$skipped)"
    }
    return 1
}

function Test-ActivationFailureLog {
    param([string]$LogPath)
    if (-not (Test-Path $LogPath)) { return $false }
    $needle = '2FA|two-factor|verification code|License client failed|LICENSE SYSTEM .* (Failed|invalid)|com\.unity\.editor\.headless|No valid Unity Editor license found'
    return (Select-String -Path $LogPath -Pattern $needle -Quiet -ErrorAction SilentlyContinue) -eq $true
}

function Find-UnityEditorPath {
    param([string]$Version)

    foreach ($envName in @('UNITY_EDITOR_PATH', 'UNITY_PATH')) {
        $envValue = [Environment]::GetEnvironmentVariable($envName)
        if (-not [string]::IsNullOrWhiteSpace($envValue) -and
            (Test-Path -LiteralPath $envValue -PathType Leaf)) {
            return (Resolve-Path -LiteralPath $envValue).Path
        }
    }

    $candidates = @()
    foreach ($root in @($env:ProgramFiles, ${env:ProgramFiles(x86)})) {
        if ([string]::IsNullOrWhiteSpace($root)) {
            continue
        }

        if (-not [string]::IsNullOrWhiteSpace($Version)) {
            $candidates += (Join-Path $root "Unity/Hub/Editor/$Version/Editor/Unity.exe")
        }

        $hubRoot = Join-Path $root 'Unity/Hub/Editor'
        if (Test-Path -LiteralPath $hubRoot -PathType Container) {
            $candidates += Get-ChildItem -LiteralPath $hubRoot -Directory -ErrorAction SilentlyContinue |
                Sort-Object Name -Descending |
                ForEach-Object { Join-Path $_.FullName 'Editor/Unity.exe' }
        }
    }

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    return ''
}

function Invoke-LocalUnityTests {
    $unityPath = Find-UnityEditorPath $UnityVersion
    if ([string]::IsNullOrWhiteSpace($unityPath)) {
        Write-Host @"
ERROR: Could not find a local Unity Editor.
Set UNITY_EDITOR_PATH to your Unity.exe path, for example:
  `$env:UNITY_EDITOR_PATH = 'C:\Program Files\Unity\Hub\Editor\$UnityVersion\Editor\Unity.exe'
"@ -ForegroundColor Red
        exit 1
    }

    $projectPath = Join-Path $RepoRoot '.unity-test-project'
    $unityArgs = @(
        '-batchmode',
        '-nographics',
        '-projectPath', $projectPath,
        '-runTests',
        '-testPlatform', $Platform,
        '-testResults', $Results,
        '-assemblyNames', $Assemblies
    )
    if (-not [string]::IsNullOrWhiteSpace($Filter)) {
        $unityArgs += @('-testFilter', $Filter)
    }
    $unityArgs += @('-logFile', '-')

    Write-Host "Launching local Unity: $unityPath" -ForegroundColor Cyan
    Write-Host "  platform=$Platform assemblies=$Assemblies"
    Write-Host "  results=$Results log=$ArtifactsDir/log.txt"
    Write-Host "  perf=$($IncludePerf.IsPresent) comparisons=$($IncludeComparisons.IsPresent) integrations=$($IncludeIntegrations.IsPresent) filter=$Filter"

    & $unityPath @unityArgs 2>&1 | Tee-Object -FilePath (Join-Path $ArtifactsDir 'log.txt')
    $UnityExit = $LASTEXITCODE
    if ($UnityExit -ne 0) {
        if (-not (Test-Path $Results)) {
            Write-Host "No results.xml at $Results" -ForegroundColor Yellow
        }
        Write-Host "Unity exited with code $UnityExit." -ForegroundColor Red
        exit $UnityExit
    }

    # Unity exited 0; route through the shared validator so the local path
    # fails loudly on a missing results.xml or zero tests (total=0), exactly
    # like the docker path. Write-ResultsSummary returns 2 for those cases.
    $summaryExit = Write-ResultsSummary -ResultsXml $Results
    exit $summaryExit
}

$ResolvedRunner = $Runner
if ($ResolvedRunner -eq 'auto') {
    if ($IsWindows -and $Platform -ne 'standalone') {
        $ResolvedRunner = 'local'
    } else {
        $ResolvedRunner = 'docker'
    }
}

# Plan banner: one line describing exactly what is about to run, emitted before
# any runner dispatch so the chosen path is visible in local + CI logs.
Write-Host "[run-tests] runner=$ResolvedRunner platform=$Platform unity=$UnityVersion ci=$($env:CI)" -ForegroundColor Cyan

if ($ResolvedRunner -eq 'local') {
    if ($Platform -eq 'standalone') {
        Write-Host 'ERROR: -Runner local does not support standalone; use -Runner docker.' -ForegroundColor Red
        exit 2
    }
    if (-not (Test-Path $ArtifactsDir)) {
        New-Item -ItemType Directory -Path $ArtifactsDir -Force | Out-Null
    }
    Invoke-LocalUnityTests
    return
}

# ---------------------------------------------------------------------------
# Argument-level path validation (m5: before docker/license checks so users
# see the most relevant error first regardless of system state).
# ---------------------------------------------------------------------------
$ResultsRel = $Results
if ($ResultsRel -eq $RepoRootReal) {
    Write-Host @"
ERROR: -Results path must be a file within the repo (got '$Results').
"@ -ForegroundColor Red
    exit 2
} elseif ($ResultsRel.StartsWith("$RepoRootReal/") -or $ResultsRel.StartsWith("$RepoRootReal\")) {
    $ResultsRel = $ResultsRel.Substring($RepoRootReal.Length).TrimStart('\','/')
} else {
    # Use Write-Host + exit instead of Write-Error so $ErrorActionPreference
    # = 'Stop' doesn't preempt our chosen exit code.
    Write-Host @"
ERROR: -Results path must be within the repo (got '$Results').
The bind-mounted /workspace inside the unityci/editor container only exposes
the repo. Either omit -Results or use a path under .artifacts/ or
.unity-test-project/.
"@ -ForegroundColor Red
    exit 2
}
# Normalize to POSIX path inside container
$ResultsContainer = "/workspace/$($ResultsRel -replace '\\','/')"

# ---------------------------------------------------------------------------
# Resolve docker executable (Windows uses docker.exe; elsewhere docker)
# ---------------------------------------------------------------------------
$DockerCommand = if ($IsWindows) { 'docker.exe' } else { 'docker' }

# Verify docker socket reachability
& $DockerCommand info *> $null
if ($LASTEXITCODE -ne 0) {
    Write-Error @"
docker socket is not reachable.
Remediation:
  1) Verify the docker-outside-of-docker devcontainer feature is enabled
     (.devcontainer/devcontainer.json).
  2) Rebuild the devcontainer (Command Palette: 'Dev Containers: Rebuild
     Container').
  3) On the host, confirm the docker daemon is running:
       $DockerCommand info
"@
    exit 1
}

# License activation: auto-detect ULF vs paid serial vs failure.
# Mirrors the bash script: current Unity/GameCI behavior does not support
# email/password-only Personal headless activation in docker.
function Get-UnityLicenseFileCandidates {
    $candidates = @()

    if (-not [string]::IsNullOrWhiteSpace($env:UNITY_LICENSE_FILE)) {
        $candidates += $env:UNITY_LICENSE_FILE
    }

    if (-not [string]::IsNullOrWhiteSpace($env:ProgramData)) {
        $candidates += (Join-Path $env:ProgramData 'Unity/Unity_lic.ulf')
    }

    if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        $candidates += (Join-Path $env:LOCALAPPDATA 'Unity/Unity_lic.ulf')
    }

    if (-not [string]::IsNullOrWhiteSpace($env:HOME)) {
        $candidates += (Join-Path $env:HOME '.local/share/unity3d/Unity/Unity_lic.ulf')
        $candidates += (Join-Path $env:HOME 'Library/Application Support/Unity/Unity_lic.ulf')
    }

    return $candidates | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique
}

if ([string]::IsNullOrEmpty($env:UNITY_LICENSE) -and [string]::IsNullOrEmpty($env:UNITY_LICENSE_B64)) {
    foreach ($licensePath in Get-UnityLicenseFileCandidates) {
        if (Test-Path -LiteralPath $licensePath -PathType Leaf) {
            $env:UNITY_LICENSE = Get-Content -LiteralPath $licensePath -Raw
            Write-Host "[run-tests] loaded UNITY_LICENSE from $licensePath"
            break
        }
    }
}

$LicenseMode = ''
if (-not [string]::IsNullOrEmpty($env:UNITY_LICENSE)) {
    $LicenseMode = 'ulf'
} elseif (-not [string]::IsNullOrEmpty($env:UNITY_LICENSE_B64)) {
    $LicenseMode = 'ulf-b64'
} elseif ((-not [string]::IsNullOrEmpty($env:UNITY_SERIAL)) -and `
          (-not [string]::IsNullOrEmpty($env:UNITY_EMAIL)) -and `
          (-not [string]::IsNullOrEmpty($env:UNITY_PASSWORD))) {
    $LicenseMode = 'serial'
} else {
    Write-Host @"
ERROR: No Unity license configured.
Set EITHER:
  UNITY_LICENSE       (raw .ulf contents; GameCI-compatible)
  UNITY_LICENSE_B64   (base64 .ulf contents; local shell convenience)
  UNITY_SERIAL + UNITY_EMAIL + UNITY_PASSWORD   (paid serial activation)

UNITY_EMAIL + UNITY_PASSWORD alone is not a supported headless container license path.
Run 'bash scripts/unity/activate-license.sh --check' for diagnostics.
"@ -ForegroundColor Red
    exit 2
}
Write-Host "[run-tests] license mode: $LicenseMode"

# Ensure artifacts dir exists
if (-not (Test-Path $ArtifactsDir)) {
    New-Item -ItemType Directory -Path $ArtifactsDir -Force | Out-Null
}

function Test-AbsoluteHostPath {
    param([string]$Value)
    return (-not [string]::IsNullOrWhiteSpace($Value)) -and (
        $Value.StartsWith('/') -or ($Value -match '^[A-Za-z]:[\\/]')
    )
}

function Test-ContainerRuntime {
    $InContainer = $false
    if (Test-Path '/.dockerenv') {
        $InContainer = $true
    } elseif (Test-Path '/proc/1/cgroup') {
        try {
            $cgroup = Get-Content '/proc/1/cgroup' -Raw -ErrorAction Stop
            if ($cgroup -match '(docker|containerd|kubepods)') {
                $InContainer = $true
            }
        } catch {
            # Not all hosts expose /proc/1/cgroup readably; assume not-container.
        }
    }
    return $InContainer
}

function Get-InspectedHostRepoRoot {
    $containerId = (& hostname).Trim()
    $format = "{{range .Mounts}}{{if eq .Destination `"$RepoRoot`"}}{{.Source}}{{end}}{{end}}"
    $mountSource = & $DockerCommand inspect $containerId --format $format 2>$null
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($mountSource)) {
        return ($mountSource -join "`n").Trim()
    }
    return ''
}

# B2: Resolve the HOST path that the docker daemon will see. Priority:
#   1) absolute DXM_HOST_REPO_ROOT
#   2) docker inspect of the current devcontainer bind mount
#   3) absolute LOCAL_WORKSPACE_FOLDER
#   4) $RepoRoot (only when NOT inside a container)
$HostRepoRoot = ''
if (-not [string]::IsNullOrWhiteSpace($env:DXM_HOST_REPO_ROOT)) {
    if (Test-AbsoluteHostPath $env:DXM_HOST_REPO_ROOT) {
        $HostRepoRoot = $env:DXM_HOST_REPO_ROOT
    } else {
        Write-Host "Ignoring relative DXM_HOST_REPO_ROOT=$env:DXM_HOST_REPO_ROOT; docker bind mounts require an absolute host path." -ForegroundColor Yellow
    }
}
if ([string]::IsNullOrWhiteSpace($HostRepoRoot)) {
    $InContainer = Test-ContainerRuntime

    if ($InContainer) {
        $HostRepoRoot = Get-InspectedHostRepoRoot
    }
    if ($InContainer -and [string]::IsNullOrWhiteSpace($HostRepoRoot) -and
        -not [string]::IsNullOrWhiteSpace($env:LOCAL_WORKSPACE_FOLDER) -and
        $env:LOCAL_WORKSPACE_FOLDER.StartsWith('/')) {
        $HostRepoRoot = $env:LOCAL_WORKSPACE_FOLDER
    } elseif ($InContainer -and [string]::IsNullOrWhiteSpace($HostRepoRoot) -and
        -not [string]::IsNullOrWhiteSpace($env:LOCAL_WORKSPACE_FOLDER)) {
        Write-Host "Ignoring LOCAL_WORKSPACE_FOLDER=$env:LOCAL_WORKSPACE_FOLDER inside devcontainer; docker inspect did not resolve a POSIX host path." -ForegroundColor Yellow
    }
    if ($InContainer -and [string]::IsNullOrWhiteSpace($HostRepoRoot)) {
        Write-Error @"
ERROR: Cannot determine host path for the workspace.
When running inside a devcontainer with docker-outside-of-docker, set:
  DXM_HOST_REPO_ROOT=/absolute/path/on/host
"@
        exit 1
    }

    if (-not $InContainer) {
        if (-not [string]::IsNullOrWhiteSpace($env:LOCAL_WORKSPACE_FOLDER)) {
            if (Test-AbsoluteHostPath $env:LOCAL_WORKSPACE_FOLDER) {
                $HostRepoRoot = $env:LOCAL_WORKSPACE_FOLDER
            } else {
                Write-Host "Ignoring relative LOCAL_WORKSPACE_FOLDER=$env:LOCAL_WORKSPACE_FOLDER; docker bind mounts require an absolute host path." -ForegroundColor Yellow
            }
        }
        if ([string]::IsNullOrWhiteSpace($HostRepoRoot)) {
            $HostRepoRoot = $RepoRoot
        }
    }
}

# UID/GID resolution (best-effort; non-Linux hosts won't honor chown)
if ($IsWindows) {
    $UserUid = '0'
    $UserGid = '0'
} else {
    $UserUid = (& id -u).Trim()
    $UserGid = (& id -g).Trim()
}

# ---------------------------------------------------------------------------
# Build inner Unity commands (editmode/playmode share one; standalone needs
# two passes — build, then launch).
# ---------------------------------------------------------------------------

# Container-side path (relative to .unity-test-project/) for the IL2CPP
# binary. Kept in sync with TestRunnerBuilder.cs DefaultBuildPathRelative
# and run-tests.sh.
$StandaloneBuildRel       = 'Builds/IL2CPPTests/Tests.x86_64'
$StandaloneBuildHost      = Join-Path $RepoRoot ".unity-test-project/$StandaloneBuildRel"
$StandaloneBuildContainer = "/workspace/.unity-test-project/$StandaloneBuildRel"

function ConvertTo-BashSingleQuotedString {
    param([string]$Value)
    return "'" + ($Value -replace "'", "'\''") + "'"
}

function ConvertTo-BashScriptText {
    param([string]$Value)
    return $Value.Replace("`r`n", "`n")
}

function Get-EditorCommandInner {
    $sb = [System.Text.StringBuilder]::new()
    $projectPathQ = ConvertTo-BashSingleQuotedString '/workspace/.unity-test-project'
    $resultsQ = ConvertTo-BashSingleQuotedString $ResultsContainer
    $assembliesQ = ConvertTo-BashSingleQuotedString $Assemblies
    [void]$sb.AppendLine('set -euo pipefail')
    [void]$sb.AppendLine('cleanup_ownership() {')
    [void]$sb.AppendLine('    chown -R "${USER_UID}:${USER_GID}" /workspace/.artifacts || true')
    [void]$sb.AppendLine('    if [[ -n "${DX_PERF_BASELINE:-}" ]]; then')
    [void]$sb.AppendLine('        baseline_path="${DX_PERF_BASELINE}"')
    [void]$sb.AppendLine('        [[ "${baseline_path}" = /* ]] || baseline_path="/workspace/${baseline_path}"')
    [void]$sb.AppendLine('        if [[ "${baseline_path}" == /workspace/* ]]; then')
    [void]$sb.AppendLine('            chown "${USER_UID}:${USER_GID}" "${baseline_path}" 2>/dev/null || true')
    [void]$sb.AppendLine('            baseline_dir="$(dirname "${baseline_path}")"')
    [void]$sb.AppendLine('            [[ "${baseline_dir}" == "/workspace" ]] || chown -R "${USER_UID}:${USER_GID}" "${baseline_dir}" 2>/dev/null || true')
    [void]$sb.AppendLine('        fi')
    [void]$sb.AppendLine('    fi')
    [void]$sb.AppendLine('    chown -R "${USER_UID}:${USER_GID}" /workspace/.unity-test-project/Library || true')
    [void]$sb.AppendLine('}')
    [void]$sb.AppendLine('trap cleanup_ownership EXIT')
    [void]$sb.AppendLine('mkdir -p /root/.cache/unity3d')
    if ($LicenseMode -eq 'ulf' -or $LicenseMode -eq 'ulf-b64') {
        [void]$sb.AppendLine('mkdir -p /root/.local/share/unity3d/Unity')
        if ($LicenseMode -eq 'ulf-b64') {
            [void]$sb.AppendLine('printf "%s" "${UNITY_LICENSE_B64}" | base64 -d > /root/.local/share/unity3d/Unity/Unity_lic.ulf')
        } else {
            [void]$sb.AppendLine('printf "%s" "${UNITY_LICENSE}" > /root/.local/share/unity3d/Unity/Unity_lic.ulf')
        }
        [void]$sb.AppendLine('chmod 644 /root/.local/share/unity3d/Unity/Unity_lic.ulf')
    }
    [void]$sb.AppendLine('/opt/unity/Editor/Unity \')
    [void]$sb.AppendLine('  -batchmode -nographics \')
    [void]$sb.AppendLine("  -projectPath $projectPathQ \")
    [void]$sb.AppendLine("  -runTests -testPlatform $Platform \")
    [void]$sb.AppendLine("  -testResults $resultsQ \")
    [void]$sb.AppendLine("  -assemblyNames $assembliesQ \")
    if (-not [string]::IsNullOrWhiteSpace($Filter)) {
        $filterQ = ConvertTo-BashSingleQuotedString $Filter
        [void]$sb.AppendLine("  -testFilter $filterQ \")
    }
    if ($LicenseMode -eq 'serial') {
        [void]$sb.AppendLine('  -username "${UNITY_EMAIL}" -password "${UNITY_PASSWORD}" -serial "${UNITY_SERIAL}" \')
    }
    [void]$sb.AppendLine('  -logFile - 2>&1 | tee /workspace/.artifacts/unity/log.txt')
    return ConvertTo-BashScriptText $sb.ToString()
}

function Get-StandaloneBuildCommandInner {
    # B1: export DXM_IL2CPP_BUILD_PATH so TestRunnerBuilder.BuildIL2CPPTestPlayer
    # writes to the same path we read from below. Local + CI use the same
    # env-var contract; the value differs but the mechanism is identical.
    $sb = [System.Text.StringBuilder]::new()
    $projectPathQ = ConvertTo-BashSingleQuotedString '/workspace/.unity-test-project'
    $buildPathQ = ConvertTo-BashSingleQuotedString $StandaloneBuildContainer
    [void]$sb.AppendLine('set -euo pipefail')
    [void]$sb.AppendLine('cleanup_ownership() {')
    [void]$sb.AppendLine('    chown -R "${USER_UID}:${USER_GID}" /workspace/.artifacts || true')
    [void]$sb.AppendLine('    if [[ -n "${DX_PERF_BASELINE:-}" ]]; then')
    [void]$sb.AppendLine('        baseline_path="${DX_PERF_BASELINE}"')
    [void]$sb.AppendLine('        [[ "${baseline_path}" = /* ]] || baseline_path="/workspace/${baseline_path}"')
    [void]$sb.AppendLine('        if [[ "${baseline_path}" == /workspace/* ]]; then')
    [void]$sb.AppendLine('            chown "${USER_UID}:${USER_GID}" "${baseline_path}" 2>/dev/null || true')
    [void]$sb.AppendLine('            baseline_dir="$(dirname "${baseline_path}")"')
    [void]$sb.AppendLine('            [[ "${baseline_dir}" == "/workspace" ]] || chown -R "${USER_UID}:${USER_GID}" "${baseline_dir}" 2>/dev/null || true')
    [void]$sb.AppendLine('        fi')
    [void]$sb.AppendLine('    fi')
    [void]$sb.AppendLine('    chown -R "${USER_UID}:${USER_GID}" /workspace/.unity-test-project/Builds || true')
    [void]$sb.AppendLine('    chown -R "${USER_UID}:${USER_GID}" /workspace/.unity-test-project/Library || true')
    [void]$sb.AppendLine('}')
    [void]$sb.AppendLine('trap cleanup_ownership EXIT')
    [void]$sb.AppendLine('mkdir -p /root/.cache/unity3d')
    if ($LicenseMode -eq 'ulf' -or $LicenseMode -eq 'ulf-b64') {
        [void]$sb.AppendLine('mkdir -p /root/.local/share/unity3d/Unity')
        if ($LicenseMode -eq 'ulf-b64') {
            [void]$sb.AppendLine('printf "%s" "${UNITY_LICENSE_B64}" | base64 -d > /root/.local/share/unity3d/Unity/Unity_lic.ulf')
        } else {
            [void]$sb.AppendLine('printf "%s" "${UNITY_LICENSE}" > /root/.local/share/unity3d/Unity/Unity_lic.ulf')
        }
        [void]$sb.AppendLine('chmod 644 /root/.local/share/unity3d/Unity/Unity_lic.ulf')
    }
    [void]$sb.AppendLine('mkdir -p /workspace/.unity-test-project/Builds/IL2CPPTests')
    [void]$sb.AppendLine("export DXM_IL2CPP_BUILD_PATH=$buildPathQ")
    [void]$sb.AppendLine('/opt/unity/Editor/Unity \')
    [void]$sb.AppendLine('  -batchmode -nographics \')
    [void]$sb.AppendLine("  -projectPath $projectPathQ \")
    [void]$sb.AppendLine('  -buildTarget StandaloneLinux64 \')
    [void]$sb.AppendLine('  -executeMethod WallstopStudios.DxMessaging.TestHarness.Editor.TestRunnerBuilder.BuildIL2CPPTestPlayer \')
    if ($LicenseMode -eq 'serial') {
        [void]$sb.AppendLine('  -username "${UNITY_EMAIL}" -password "${UNITY_PASSWORD}" -serial "${UNITY_SERIAL}" \')
    }
    [void]$sb.AppendLine('  -logFile - 2>&1 | tee /workspace/.artifacts/unity/build-log.txt')
    return ConvertTo-BashScriptText $sb.ToString()
}

function Get-StandaloneRunCommandInner {
    $sb = [System.Text.StringBuilder]::new()
    $buildPathQ = ConvertTo-BashSingleQuotedString $StandaloneBuildContainer
    $resultsQ = ConvertTo-BashSingleQuotedString $ResultsContainer
    $assembliesQ = ConvertTo-BashSingleQuotedString $Assemblies
    [void]$sb.AppendLine('set -euo pipefail')
    [void]$sb.AppendLine('cleanup_ownership() {')
    [void]$sb.AppendLine('    chown -R "${USER_UID}:${USER_GID}" /workspace/.artifacts || true')
    [void]$sb.AppendLine('    if [[ -n "${DX_PERF_BASELINE:-}" ]]; then')
    [void]$sb.AppendLine('        baseline_path="${DX_PERF_BASELINE}"')
    [void]$sb.AppendLine('        [[ "${baseline_path}" = /* ]] || baseline_path="/workspace/${baseline_path}"')
    [void]$sb.AppendLine('        if [[ "${baseline_path}" == /workspace/* ]]; then')
    [void]$sb.AppendLine('            chown "${USER_UID}:${USER_GID}" "${baseline_path}" 2>/dev/null || true')
    [void]$sb.AppendLine('            baseline_dir="$(dirname "${baseline_path}")"')
    [void]$sb.AppendLine('            [[ "${baseline_dir}" == "/workspace" ]] || chown -R "${USER_UID}:${USER_GID}" "${baseline_dir}" 2>/dev/null || true')
    [void]$sb.AppendLine('        fi')
    [void]$sb.AppendLine('    fi')
    [void]$sb.AppendLine('    chown -R "${USER_UID}:${USER_GID}" /workspace/.unity-test-project/Library || true')
    [void]$sb.AppendLine('}')
    [void]$sb.AppendLine('trap cleanup_ownership EXIT')
    [void]$sb.AppendLine("if [[ ! -x $buildPathQ ]]; then")
    [void]$sb.AppendLine("    echo `"[run-tests] ERROR: built test player not found at $StandaloneBuildContainer`" >&2")
    [void]$sb.AppendLine('    exit 1')
    [void]$sb.AppendLine('fi')
    [void]$sb.AppendLine("$buildPathQ \")
    [void]$sb.AppendLine('  -batchmode -nographics \')
    [void]$sb.AppendLine('  -runTests \')
    [void]$sb.AppendLine("  -testResults $resultsQ \")
    [void]$sb.AppendLine("  -assemblyNames $assembliesQ \")
    if (-not [string]::IsNullOrWhiteSpace($Filter)) {
        $filterQ = ConvertTo-BashSingleQuotedString $Filter
        [void]$sb.AppendLine("  -testFilter $filterQ \")
    }
    [void]$sb.AppendLine('  -logFile - 2>&1 | tee /workspace/.artifacts/unity/log.txt')
    return ConvertTo-BashScriptText $sb.ToString()
}

# ---------------------------------------------------------------------------
# Invoke docker
# ---------------------------------------------------------------------------
Write-Host "Launching $ImageRef" -ForegroundColor Cyan
Write-Host "  platform=$Platform assemblies=$Assemblies"
Write-Host "  results=$Results log=$ArtifactsDir/log.txt"
Write-Host "  perf=$($IncludePerf.IsPresent) comparisons=$($IncludeComparisons.IsPresent) integrations=$($IncludeIntegrations.IsPresent) filter=$Filter"
Write-Host "  host_repo_root=$HostRepoRoot"
Write-Host "  library_cache=$UnityLibraryCacheSource"

$dockerBaseArgs = @(
    'run', '--rm',
    '-v', "$HostRepoRoot`:/workspace:rw",
    '-v', "$UnityLibraryCacheSource`:/workspace/.unity-test-project/Library",
    '-e', 'UNITY_LICENSE',
    '-e', 'UNITY_LICENSE_B64',
    '-e', 'UNITY_SERIAL',
    '-e', 'UNITY_EMAIL',
    '-e', 'UNITY_PASSWORD',
    '-e', 'DX_PERF_COMMIT',
    '-e', 'DX_PERF_BASELINE',
    '-e', 'DX_PERF_BASELINE_MODE',
    '-e', "USER_UID=$UserUid",
    '-e', "USER_GID=$UserGid"
)

if ($Platform -eq 'standalone') {
    Write-Host 'Step 1/2: building IL2CPP test player...' -ForegroundColor Cyan
    $buildInner = Get-StandaloneBuildCommandInner
    & $DockerCommand @dockerBaseArgs $ImageRef bash -c $buildInner
    $BuildExit = $LASTEXITCODE
    if ($BuildExit -ne 0) {
        Write-Host "IL2CPP build failed (exit $BuildExit)." -ForegroundColor Red
        exit $BuildExit
    }
    if (-not (Test-Path $StandaloneBuildHost)) {
        Write-Host "IL2CPP build reported success but binary missing at $StandaloneBuildHost." -ForegroundColor Red
        exit 1
    }

    Write-Host 'Step 2/2: running IL2CPP test player...' -ForegroundColor Cyan
    $runInner = Get-StandaloneRunCommandInner
    & $DockerCommand @dockerBaseArgs $ImageRef bash -c $runInner
    $ExitCode = $LASTEXITCODE
} else {
    $UnityCmdInner = Get-EditorCommandInner
    & $DockerCommand @dockerBaseArgs $ImageRef bash -c $UnityCmdInner
    $ExitCode = $LASTEXITCODE
}

# ---------------------------------------------------------------------------
# Summary tail: route the docker results through the shared Write-ResultsSummary
# defined near the top of the file (same validator the local path uses). A
# non-zero return (missing results.xml, parse error, or total=0) fails the run.
# ---------------------------------------------------------------------------
$SummaryExit = Write-ResultsSummary -ResultsXml $Results

if ($ExitCode -ne 0) {
    Write-Host "Unity exited with code $ExitCode." -ForegroundColor Red
    $logFile = Join-Path $ArtifactsDir 'log.txt'
    if (Test-ActivationFailureLog -LogPath $logFile) {
        Write-Host '' -ForegroundColor Red
        Write-Host 'ERROR: Unity license activation failed; common causes:' -ForegroundColor Red
        Write-Host '  1. UNITY_LICENSE is not raw .ulf contents, or UNITY_LICENSE_B64 is not valid base64 .ulf contents.' -ForegroundColor Red
        Write-Host '  2. UNITY_SERIAL is missing, expired, or does not match the Unity account.' -ForegroundColor Red
        Write-Host '  3. Wrong UNITY_EMAIL/UNITY_PASSWORD (check for typos, especially trailing' -ForegroundColor Red
        Write-Host '     newlines from `cat`).' -ForegroundColor Red
        Write-Host '  4. Activation rate limit (Unity throttles repeated activations from one IP).' -ForegroundColor Red
        Write-Host '     Wait 1 hour and retry.' -ForegroundColor Red
        Write-Host 'See .llm/skills/unity/unity-license-bootstrap.md for details.' -ForegroundColor Red
    }
}

# Surface a non-zero exit when either Unity OR the summary check failed.
if ($ExitCode -ne 0) {
    exit $ExitCode
}
exit $SummaryExit
