#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+f\d+$')]
    [string]$UnityVersion,

    [Parameter(Mandatory = $true)]
    [ValidateSet('editmode', 'playmode', 'standalone')]
    [string]$TestMode,

    [Parameter(Mandatory = $true)]
    [string]$AssemblyNames,

    [Parameter(Mandatory = $true)]
    [string]$ArtifactsPath,

    [string]$RepoRoot = $(if ($env:GITHUB_WORKSPACE) { $env:GITHUB_WORKSPACE } else { (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path }),

    [string]$ProjectPath,

    [string]$UnityEditorPath = $env:UNITY_EDITOR_PATH,

    [string]$UnityInstallRoot = $(if ($env:UNITY_EDITOR_INSTALL_ROOT) { $env:UNITY_EDITOR_INSTALL_ROOT } else { 'C:\Unity\Editors' }),

    [switch]$GenerateOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# PowerShell 7.4 introduced $PSNativeCommandUseErrorActionPreference (stabilizing
# the native-error experimental feature). Its default is $false on current builds,
# so `& <native>` does NOT throw on a non-zero exit and our explicit checks run as
# written. However, a host profile or a future/different build could enable it,
# which would make `& <native>` THROW on a non-zero exit BEFORE our explicit
# `$LASTEXITCODE` check runs -- short-circuiting Invoke-UnityEditor's exit-code
# diagnostic and making the best-effort license return rely on its catch block
# instead of finishing. Pinning it $false makes LASTEXITCODE-based handling
# authoritative and identical across hosts/versions. (PS 5.1 lacks this variable;
# assigning it there is harmless, and the assignment is StrictMode-safe.)
$PSNativeCommandUseErrorActionPreference = $false

$PackageName = 'com.wallstop-studios.dxmessaging'
$TestFrameworkVersion = '1.4.5'
$PerformanceFrameworkVersion = '3.4.2'
$DxMessagingAnalyzerDllNames = @(
    'WallstopStudios.DxMessaging.SourceGenerators.dll',
    'WallstopStudios.DxMessaging.Analyzer.dll',
    'Microsoft.CodeAnalysis.dll',
    'Microsoft.CodeAnalysis.CSharp.dll',
    'System.Reflection.Metadata.dll',
    'System.Runtime.CompilerServices.Unsafe.dll',
    'System.Collections.Immutable.dll'
)
$RequiredDxMessagingAnalyzerDllNames = @(
    'WallstopStudios.DxMessaging.SourceGenerators.dll',
    'WallstopStudios.DxMessaging.Analyzer.dll'
)

function Write-CiError {
    param([Parameter(Mandatory = $true)][string]$Message)
    Write-Host "::error::$Message"
}

function Write-CiNotice {
    param([Parameter(Mandatory = $true)][string]$Message)
    Write-Host "::notice::$Message"
}

function Resolve-FullPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    $executionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
}

function Assert-RepoRoot {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath (Join-Path $Path 'package.json') -PathType Leaf)) {
        throw "Repo root '$Path' does not contain package.json."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $Path 'Runtime') -PathType Container)) {
        throw "Repo root '$Path' does not contain Runtime/."
    }
}

function ConvertTo-UnityFileUriPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    return ($Path -replace '\\', '/')
}

function Initialize-UnityCacheEnvironment {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Version
    )

    $cacheRoot = Join-Path $Root ".artifacts\unity\cache\$Version"
    $upmRoot = Join-Path $cacheRoot 'upm'
    $npmRoot = Join-Path $cacheRoot 'npm'
    $gitLfsRoot = Join-Path $cacheRoot 'git-lfs'
    $localUnityCaches = if ($env:LOCALAPPDATA) {
        Join-Path $env:LOCALAPPDATA 'Unity\Caches'
    } else {
        Join-Path $cacheRoot 'localappdata\Unity\Caches'
    }

    foreach ($path in @($cacheRoot, $upmRoot, $npmRoot, $gitLfsRoot, $localUnityCaches)) {
        New-Item -ItemType Directory -Force -Path $path | Out-Null
    }

    $env:UPM_CACHE_ROOT = $upmRoot
    $env:UPM_NPM_CACHE_PATH = $npmRoot
    $env:UPM_GIT_LFS_CACHE_PATH = $gitLfsRoot
    $env:UPM_ENABLE_GIT_LFS_CACHE = 'true'

    Write-Host "::group::Unity cache environment"
    Write-Host "LOCALAPPDATA Unity caches: $localUnityCaches"
    Write-Host "UPM_CACHE_ROOT: $env:UPM_CACHE_ROOT"
    Write-Host "UPM_NPM_CACHE_PATH: $env:UPM_NPM_CACHE_PATH"
    Write-Host "UPM_GIT_LFS_CACHE_PATH: $env:UPM_GIT_LFS_CACHE_PATH"
    Write-Host "::endgroup::"
}

function New-ManifestJson {
    param([Parameter(Mandatory = $true)][string]$Root)

    $packagePath = ConvertTo-UnityFileUriPath -Path $Root
    $manifest = [ordered]@{
        dependencies = [ordered]@{
            'com.unity.test-framework' = $TestFrameworkVersion
            'com.unity.test-framework.performance' = $PerformanceFrameworkVersion
            $PackageName = "file:$packagePath"
        }
        testables = @($PackageName)
    }

    return ($manifest | ConvertTo-Json -Depth 8)
}

function New-ConfiguratorSource {
    @'
using UnityEditor;

public static class DxmCiTestConfigurator
{
    public static void Apply()
    {
        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.IL2CPP);
        PlayerSettings.SetApiCompatibilityLevel(BuildTargetGroup.Standalone, ApiCompatibilityLevel.NET_Standard_2_0);
    }
}
'@
}

function New-CscRspContent {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Project
    )

    $lines = New-Object System.Collections.Generic.List[string]
    $missingRequired = New-Object System.Collections.Generic.List[string]
    foreach ($dllName in $DxMessagingAnalyzerDllNames) {
        $sourcePath = Join-Path $Root "Editor\Analyzers\$dllName"
        if (Test-Path -LiteralPath $sourcePath -PathType Leaf) {
            $analyzerPath = ConvertTo-UnityFileUriPath -Path (Resolve-FullPath -Path $sourcePath)
            $lines.Add("-a:`"$analyzerPath`"")
        } elseif ($RequiredDxMessagingAnalyzerDllNames -contains $dllName) {
            $missingRequired.Add($sourcePath)
        }
    }

    if ($missingRequired.Count -gt 0) {
        throw "Missing required DxMessaging analyzer DLL(s) for generated csc.rsp:`n$($missingRequired.ToArray() -join "`n")"
    }

    $ignoreSidecarRelativePath = 'Assets/Editor/DxMessaging.BaseCallIgnore.txt'
    $ignoreSidecarPath = Join-Path $Project $ignoreSidecarRelativePath
    if (Test-Path -LiteralPath $ignoreSidecarPath -PathType Leaf) {
        $lines.Add("-additionalfile:`"$ignoreSidecarRelativePath`"")
    }

    if ($lines.Count -eq 0) {
        return ''
    }

    return (($lines.ToArray() -join [Environment]::NewLine) + [Environment]::NewLine)
}

function Write-CscRspDiagnostics {
    param(
        [Parameter(Mandatory = $true)][string]$Project,
        [string]$LogPath,
        [Parameter(Mandatory = $true)][string]$Label
    )

    $rspPath = Join-Path $Project 'Assets\csc.rsp'
    $rspExists = Test-Path -LiteralPath $rspPath -PathType Leaf
    $rspText = if ($rspExists) { Get-Content -LiteralPath $rspPath -Raw } else { '' }
    $rspHasSourceGenerator = $rspText -match 'WallstopStudios\.DxMessaging\.SourceGenerators\.dll'
    $rspHasAnalyzer = $rspText -match 'WallstopStudios\.DxMessaging\.Analyzer\.dll'
    $logHasSourceGeneratorArg = $false
    $logHasAnalyzerArg = $false
    if ($LogPath -and (Test-Path -LiteralPath $LogPath -PathType Leaf)) {
        $logText = Get-Content -LiteralPath $LogPath -Raw
        $logHasSourceGeneratorArg = $logText -match 'WallstopStudios\.DxMessaging\.SourceGenerators\.dll'
        $logHasAnalyzerArg = $logText -match 'WallstopStudios\.DxMessaging\.Analyzer\.dll'
    }

    Write-Host "::group::DxMessaging compiler response diagnostics ($Label)"
    Write-Host "csc.rsp exists: $rspExists"
    Write-Host "csc.rsp has source generator: $rspHasSourceGenerator"
    Write-Host "csc.rsp has analyzer: $rspHasAnalyzer"
    Write-Host "Unity compile log mentioned DxMessaging source-generator arg: $logHasSourceGeneratorArg"
    Write-Host "Unity compile log mentioned DxMessaging analyzer arg: $logHasAnalyzerArg"
    if ($rspExists) {
        Write-Host "csc.rsp:"
        Get-Content -LiteralPath $rspPath
    }
    Write-Host "::endgroup::"

    if (-not ($rspExists -and $rspHasSourceGenerator -and $rspHasAnalyzer)) {
        throw "Generated Assets/csc.rsp is missing required DxMessaging source-generator/analyzer entries."
    }
}

function Initialize-EphemeralProject {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$Mode,
        [string]$Path
    )

    $project = if ($Path) {
        Resolve-FullPath -Path $Path
    } else {
        Join-Path $Root ".artifacts\unity\projects\$Version-$Mode"
    }

    New-Item -ItemType Directory -Force -Path (Join-Path $project 'Packages') | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $project 'ProjectSettings') | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $project 'Assets\Editor') | Out-Null

    New-ManifestJson -Root $Root |
        Set-Content -LiteralPath (Join-Path $project 'Packages\manifest.json') -Encoding UTF8
    "m_EditorVersion: $Version`n" |
        Set-Content -LiteralPath (Join-Path $project 'ProjectSettings\ProjectVersion.txt') -Encoding UTF8
    New-ConfiguratorSource |
        Set-Content -LiteralPath (Join-Path $project 'Assets\Editor\DxmCiTestConfigurator.cs') -Encoding UTF8
    New-CscRspContent -Root $Root -Project $project |
        Set-Content -LiteralPath (Join-Path $project 'Assets\csc.rsp') -Encoding UTF8

    return $project
}

function Get-AcceleratorArguments {
    param(
        [string]$Endpoint,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$Mode
    )

    if (-not $Endpoint -or $Endpoint.Trim().Length -eq 0) {
        return @()
    }

    $trimmed = $Endpoint.Trim()
    if ($trimmed -match '^[a-zA-Z][a-zA-Z0-9+.-]*://') {
        throw "UNITY_ACCELERATOR_ENDPOINT must be host:port, not a URL with a scheme: '$trimmed'."
    }
    if ($trimmed -notmatch '^[^:\s/]+:\d+$') {
        throw "UNITY_ACCELERATOR_ENDPOINT must be host:port, for example 127.0.0.1:10080. Got '$trimmed'."
    }

    return @(
        '-EnableCacheServer',
        '-cacheServerEndpoint', $trimmed,
        '-cacheServerNamespacePrefix', "dxmessaging-$Version-$Mode",
        '-cacheServerEnableDownload', 'true',
        '-cacheServerEnableUpload', 'true'
    )
}

function Invoke-UnityLicenseActivate {
    param(
        [Parameter(Mandatory = $true)][string]$EditorPath,
        [Parameter(Mandatory = $true)][string]$Serial,
        [Parameter(Mandatory = $true)][string]$Email,
        [Parameter(Mandatory = $true)][string]$Password,
        [Parameter(Mandatory = $true)][string]$LogPath
    )

    # Classic SERIAL activation: a single editor invocation that activates the
    # paid Unity seat and immediately quits. This MUST succeed before the test
    # run, so unlike the return path it THROWS on a non-zero exit -- a failed
    # activation means the test editor would launch unlicensed and fail opaquely.
    $logDir = Split-Path -Parent $LogPath
    if ($logDir -and -not (Test-Path -LiteralPath $logDir -PathType Container)) {
        New-Item -ItemType Directory -Force -Path $logDir | Out-Null
    }

    # SECURITY: the serial/email/password ride in the argument array, so this site
    # must NEVER echo the args (no "...$activateArgs..." Write-Host). The caller
    # passes a $LogPath that lives under a NON-uploaded temp dir (RUNNER_TEMP /
    # system temp), never under $ArtifactsPath, so the credentials cannot leak into
    # an uploaded artifact.
    $activateArgs = @(
        '-quit',
        '-batchmode',
        '-nographics',
        '-serial', $Serial,
        '-username', $Email,
        '-password', $Password,
        '-logFile', '-'
    )

    Write-Host "::group::Activate Unity license (serial)"
    # Unity.exe is a Windows GUI-subsystem binary: PowerShell's `&` does NOT wait
    # for it or set $LASTEXITCODE unless its stdout is consumed via the pipeline.
    # `-logFile -` puts the Unity log on stdout and `| Tee-Object` forces the wait,
    # sets $LASTEXITCODE, and persists the (non-uploaded) temp log. (Proven idiom;
    # see Invoke-UnityEditor.)
    & $EditorPath @activateArgs 2>&1 | Tee-Object -FilePath $LogPath
    $exitCode = $LASTEXITCODE
    Write-Host "::endgroup::"
    if ($exitCode -ne 0) {
        # The message names the failure and the (non-uploaded) log path ONLY -- it
        # must never embed the serial/email/password values.
        throw "Unity license activation failed with exit code $exitCode. See the activation log at $LogPath (not uploaded as an artifact)."
    }

    Write-CiNotice 'Activated the Unity license (serial).'
}

function Invoke-UnityLicenseReturn {
    param(
        [Parameter(Mandatory = $true)][string]$EditorPath,
        [Parameter(Mandatory = $true)][string]$Email,
        [Parameter(Mandatory = $true)][string]$Password,
        [Parameter(Mandatory = $true)][string]$LogPath
    )

    # Best-effort, defense-in-depth: this MUST NEVER throw. The license is also
    # returned by the workflow if:always() step (a backstop for a hard-killed
    # editor that never reaches this finally) and by the NEXT run's
    # return-at-start (which reclaims a seat leaked by a prior force-killed run on
    # this persistent self-hosted runner).
    try {
        $logDir = Split-Path -Parent $LogPath
        if ($logDir -and -not (Test-Path -LiteralPath $logDir -PathType Container)) {
            New-Item -ItemType Directory -Force -Path $logDir | Out-Null
        }

        # SECURITY: email/password ride in the argument array; never echo the args
        # and keep the return log in the NON-uploaded temp dir, never under
        # $ArtifactsPath.
        $returnArgs = @(
            '-quit',
            '-batchmode',
            '-nographics',
            '-returnlicense',
            '-username', $Email,
            '-password', $Password,
            '-logFile', '-'
        )

        Write-Host "::group::Return Unity license (serial)"
        # Same Tee-Object wait + $LASTEXITCODE idiom as Invoke-UnityLicenseActivate
        # / Invoke-UnityEditor (a bare `&` would not wait for the GUI-subsystem
        # binary). `-logFile -` puts the log on stdout; Tee-Object DOES persist it
        # to $LogPath, but the caller keeps $LogPath under the NON-uploaded temp dir
        # (RUNNER_TEMP / system temp), so it stays out of any UPLOADED ARTIFACT and
        # the account fragments Unity may print cannot leak into uploads.
        & $EditorPath @returnArgs 2>&1 | Tee-Object -FilePath $LogPath
        $exitCode = $LASTEXITCODE
        Write-Host "::endgroup::"

        if ($exitCode -ne 0) {
            Write-Host "::warning::Unity license return exited with code $exitCode; the workflow if:always() return step and the next run's return-at-start are the backstops for the leaked seat."
        } else {
            Write-CiNotice 'Returned the Unity license (serial).'
        }
    } catch {
        Write-Host "::warning::Unity license return failed: $($_.Exception.Message). The workflow if:always() return step and the next run's return-at-start are the backstops."
    }
}

function Invoke-UnityEditor {
    param(
        [Parameter(Mandatory = $true)][string]$EditorPath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string]$LogPath
    )

    # Unity.exe is a Windows GUI-subsystem binary. PowerShell's `&` launches such
    # executables ASYNCHRONOUSLY: it does NOT wait for them and does NOT set
    # $LASTEXITCODE. Callers therefore pass `-logFile -` (Unity logs to stdout) so
    # that consuming the process's stdout via the pipeline forces PowerShell to
    # BLOCK until the process exits AND reliably sets $LASTEXITCODE. Tee-Object both
    # streams the log live to the CI console and persists it to $LogPath. This is
    # the proven idiom from scripts/unity/run-tests.ps1.
    $logDir = Split-Path -Parent $LogPath
    if ($logDir -and -not (Test-Path -LiteralPath $logDir -PathType Container)) {
        New-Item -ItemType Directory -Force -Path $logDir | Out-Null
    }

    Write-Host "::group::$Label"
    Write-Host "`"$EditorPath`" $($Arguments -join ' ')"
    & $EditorPath @Arguments 2>&1 | Tee-Object -FilePath $LogPath
    $exitCode = $LASTEXITCODE
    Write-Host "::endgroup::"
    if ($exitCode -ne 0) {
        throw "$Label failed with exit code $exitCode. See the streamed Unity log above (also saved to $LogPath)."
    }
}

function Get-NativeExitCodeDescription {
    param([Parameter(Mandatory = $true)][int]$ExitCode)

    $normalized = if ($ExitCode -lt 0) {
        [uint32]($ExitCode + 4294967296)
    } else {
        [uint32]$ExitCode
    }
    $hex = "0x$($normalized.ToString('X8'))"
    if ($normalized -eq 0xC0000135) {
        return "$hex / STATUS_DLL_NOT_FOUND"
    }

    return $hex
}

function Invoke-UnityNativeStartupProbe {
    param(
        [Parameter(Mandatory = $true)][string]$EditorPath,
        [Parameter(Mandatory = $true)][string]$LogPath
    )

    $logDir = Split-Path -Parent $LogPath
    if ($logDir -and -not (Test-Path -LiteralPath $logDir -PathType Container)) {
        New-Item -ItemType Directory -Force -Path $logDir | Out-Null
    }

    Write-Host "::group::Unity native startup diagnostics"
    Write-Host "Runner name: $env:RUNNER_NAME"
    Write-Host "Runner OS: $env:RUNNER_OS"
    Write-Host "Runner architecture: $env:RUNNER_ARCH"
    Write-Host "Unity editor path: $EditorPath"
    try {
        $editorItem = Get-Item -LiteralPath $EditorPath
        Write-Host "Unity editor file version: $($editorItem.VersionInfo.FileVersion)"
        Write-Host "Unity editor product version: $($editorItem.VersionInfo.ProductVersion)"
    } catch {
        Write-Host "::notice::Could not read Unity editor version info: $($_.Exception.Message)"
    }

    Write-Host "Unity licensing client inventory:"
    $licensingClientCandidates = New-Object System.Collections.Generic.List[string]
    foreach ($root in @(${env:ProgramFiles}, ${env:ProgramFiles(x86)})) {
        if ($root -and $root.Trim().Length -gt 0) {
            $licensingClientCandidates.Add(
                (Join-Path $root 'Common Files\Unity\UnityLicensingClient\Unity.Licensing.Client.exe')
            )
        }
    }
    if ($env:LOCALAPPDATA -and $env:LOCALAPPDATA.Trim().Length -gt 0) {
        $licensingClientCandidates.Add(
            (Join-Path $env:LOCALAPPDATA 'Unity\Unity.Licensing.Client\Unity.Licensing.Client.exe')
        )
    }
    foreach ($candidate in $licensingClientCandidates) {
        $exists = Test-Path -LiteralPath $candidate -PathType Leaf
        Write-Host "  [$exists] $candidate"
    }

    $probeArgs = @(
        '-version',
        '-batchmode',
        '-nographics',
        '-quit',
        '-logFile', '-'
    )

    Write-Host "`"$EditorPath`" $($probeArgs -join ' ')"
    & $EditorPath @probeArgs 2>&1 | Tee-Object -FilePath $LogPath
    $exitCode = $LASTEXITCODE
    $description = Get-NativeExitCodeDescription -ExitCode $exitCode
    Write-Host "Unity native startup probe exit code: $exitCode ($description)"
    Write-Host "::endgroup::"

    if ($exitCode -ne 0) {
        throw "Unity native startup probe failed with exit code $exitCode ($description) after pre-lock editor provisioning. ensure-editor.ps1 already attempted managed repair/reinstall before this job acquired the organization Unity license lock; this in-lock failure indicates host OS/runtime prerequisite damage rather than a Unity package/test issue. See the streamed probe log above (also saved to $LogPath)."
    }
}

function Write-UnityResultFailureDiagnostics {
    param(
        [string]$LogPath,
        [string]$Project,
        [Parameter(Mandatory = $true)][string]$Label
    )

    Write-Host "::group::Unity result failure diagnostics ($Label)"
    try {
        if ($LogPath -and (Test-Path -LiteralPath $LogPath -PathType Leaf)) {
            Write-Host "Unity log path: $LogPath"
            $diagnosticPatterns = @(
                'warning CS8032',
                'error CS\d+',
                'Aborting batchmode',
                'Exiting batchmode successfully',
                'No tests',
                'TestRunner',
                'results\.xml',
                'assemblyNames'
            )
            $matches = @(
                Select-String -LiteralPath $LogPath -Pattern $diagnosticPatterns -ErrorAction SilentlyContinue |
                    Select-Object -First 80
            )
            if ($matches.Count -gt 0) {
                Write-Host "Selected Unity log lines:"
                foreach ($match in $matches) {
                    Write-Host ("  line {0}: {1}" -f $match.LineNumber, $match.Line.Trim())
                }
            } else {
                Write-Host "No targeted diagnostic lines matched in the Unity log."
            }

            $logText = Get-Content -LiteralPath $LogPath -Raw
            if ($logText -match 'warning CS8032') {
                Write-CiError "Unity could not instantiate one or more DxMessaging analyzers/source generators (CS8032). Check that Editor/Analyzers DLLs target the Roslyn version supported by this Unity editor."
            }
            if ($logText -match 'error CS0315' -and $logText -match 'Simple(?:Untargeted|Targeted|Broadcast)Message') {
                Write-CiError "Message fixture compile errors followed missing generated interfaces. This usually means the DxMessaging source generator did not load."
            }
            if ($logText -match 'Exiting batchmode successfully') {
                Write-CiError "Unity exited with code 0 but did not write NUnit results. Check the selected assembly list, test platform, and TestRunner log lines above."
            }
        } else {
            Write-Host "Unity log path unavailable or missing: $LogPath"
        }

        if ($Project) {
            $rspPath = Join-Path $Project 'Assets\csc.rsp'
            Write-Host "Generated csc.rsp exists: $(Test-Path -LiteralPath $rspPath -PathType Leaf)"
            $scriptAssemblies = Join-Path $Project 'Library\ScriptAssemblies'
            if (Test-Path -LiteralPath $scriptAssemblies -PathType Container) {
                Write-Host "Script assemblies present:"
                Get-ChildItem -LiteralPath $scriptAssemblies -Filter '*.dll' -ErrorAction SilentlyContinue |
                    Select-Object -ExpandProperty Name |
                    Sort-Object |
                    ForEach-Object { Write-Host "  $_" }
            } else {
                Write-Host "Script assemblies directory missing: $scriptAssemblies"
            }
        }
    } catch {
        Write-Host "::warning::Could not collect Unity result failure diagnostics: $($_.Exception.Message)"
    }
    Write-Host "::endgroup::"
}

function Invoke-UnityEditorWithFailureDiagnostics {
    param(
        [Parameter(Mandatory = $true)][string]$EditorPath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string]$LogPath,
        [Parameter(Mandatory = $true)][string]$Project,
        [Parameter(Mandatory = $true)][string]$CscLabel,
        [Parameter(Mandatory = $true)][string]$DiagnosticsLabel
    )

    try {
        Invoke-UnityEditor -EditorPath $EditorPath -Arguments $Arguments -Label $Label -LogPath $LogPath
    } catch {
        Write-CscRspDiagnostics -Project $Project -LogPath $LogPath -Label $CscLabel
        Write-UnityResultFailureDiagnostics -LogPath $LogPath -Project $Project -Label $DiagnosticsLabel
        throw
    }
}

function Test-NUnitResults {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Label,
        [string]$LogPath,
        [string]$Project
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        Write-CiError "No NUnit results XML exists at $Path for $Label."
        Write-UnityResultFailureDiagnostics -LogPath $LogPath -Project $Project -Label $Label
        throw "Unity did not produce NUnit results for $Label."
    }

    [xml]$xml = Get-Content -LiteralPath $Path -Raw
    $run = $xml.SelectSingleNode('//test-run')
    if (-not $run) {
        Write-CiError "NUnit results at $Path do not contain a <test-run> element."
        Write-UnityResultFailureDiagnostics -LogPath $LogPath -Project $Project -Label $Label
        throw "Invalid NUnit results for $Label."
    }

    $total = [int]$run.total
    $passed = [int]$run.passed
    $failed = [int]$run.failed
    $skipped = [int]$run.skipped

    Write-Host "Results: total=$total passed=$passed failed=$failed skipped=$skipped"
    if ($total -lt 1) {
        Write-CiError "0 tests ran for $Label -- check assembly selection and package testables."
        throw "0 tests ran for $Label."
    }
    if ($failed -gt 0) {
        Write-CiError "$failed tests failed for $Label."
        throw "$failed tests failed for $Label."
    }

        Write-CiNotice "${Label}: total=$total passed=$passed failed=$failed skipped=$skipped"
}

$RepoRoot = Resolve-FullPath -Path $RepoRoot
Assert-RepoRoot -Path $RepoRoot
$ArtifactsPath = Resolve-FullPath -Path $ArtifactsPath
New-Item -ItemType Directory -Force -Path $ArtifactsPath | Out-Null

Initialize-UnityCacheEnvironment -Root $RepoRoot -Version $UnityVersion

$ProjectPath = Initialize-EphemeralProject -Root $RepoRoot -Version $UnityVersion -Mode $TestMode -Path $ProjectPath
$LibraryPath = Join-Path $ProjectPath 'Library'
New-Item -ItemType Directory -Force -Path $LibraryPath | Out-Null

Write-Host "::group::Ephemeral Unity project"
Write-Host "RepoRoot: $RepoRoot"
Write-Host "ProjectPath: $ProjectPath"
Write-Host "LibraryPath: $LibraryPath"
Write-Host "ArtifactsPath: $ArtifactsPath"
Write-Host "Manifest:"
Get-Content -LiteralPath (Join-Path $ProjectPath 'Packages\manifest.json')
Write-Host "Assets/csc.rsp:"
Get-Content -LiteralPath (Join-Path $ProjectPath 'Assets\csc.rsp')
Write-Host "::endgroup::"

if ($GenerateOnly) {
    Write-CiNotice "Generated ephemeral Unity project only: $ProjectPath"
    exit 0
}

if (-not $UnityEditorPath -or $UnityEditorPath.Trim().Length -eq 0) {
    $ensureEditor = Join-Path $PSScriptRoot 'ensure-editor.ps1'
    $provisioningProfile = if ($TestMode -eq 'standalone') { 'StandaloneWindowsIl2Cpp' } else { 'EditorOnly' }
    $ensureArgs = @{
        UnityVersion         = $UnityVersion
        InstallRoot          = $UnityInstallRoot
        ProvisioningProfile = $provisioningProfile
    }
    $UnityEditorPath = (& $ensureEditor @ensureArgs | Select-Object -Last 1)
}

if (-not (Test-Path -LiteralPath $UnityEditorPath -PathType Leaf)) {
    throw "Unity editor not found: $UnityEditorPath"
}

# Export the resolved editor path so a workflow if:always() step (which runs in a
# SEPARATE process after this one exits) can run `Unity.exe -returnlicense` to
# return the seat as defense-in-depth.
if ($env:GITHUB_ENV) {
    Add-Content -LiteralPath $env:GITHUB_ENV -Value "UNITY_EDITOR_PATH=$UnityEditorPath"
}

# Classic SERIAL activation: the paid seat is activated from UNITY_SERIAL +
# UNITY_EMAIL + UNITY_PASSWORD and explicitly returned on EVERY exit path so the
# seat is never leaked. All three credentials are required together; we test each
# with IsNullOrWhiteSpace so a blank-but-set secret counts as missing.
$hasLicenseCreds = (
    -not [string]::IsNullOrWhiteSpace($env:UNITY_SERIAL) -and
    -not [string]::IsNullOrWhiteSpace($env:UNITY_EMAIL) -and
    -not [string]::IsNullOrWhiteSpace($env:UNITY_PASSWORD)
)
# In CI all three credentials are MANDATORY: a missing one means the editor would
# launch unlicensed and fail opaquely. The error names the missing VARS (never
# their values). Locally, missing creds is fine -- we assume the machine is
# already licensed (Hub sign-in / a local .ulf) and simply skip activate/return.
if ($env:GITHUB_ACTIONS -eq 'true' -and -not $hasLicenseCreds) {
    $missing = @()
    if ([string]::IsNullOrWhiteSpace($env:UNITY_SERIAL)) { $missing += 'UNITY_SERIAL' }
    if ([string]::IsNullOrWhiteSpace($env:UNITY_EMAIL)) { $missing += 'UNITY_EMAIL' }
    if ([string]::IsNullOrWhiteSpace($env:UNITY_PASSWORD)) { $missing += 'UNITY_PASSWORD' }
    throw "Serial Unity activation requires UNITY_SERIAL, UNITY_EMAIL, and UNITY_PASSWORD in CI. Missing or empty: $($missing -join ', ')."
}

# Array-wrap the capture so it is ALWAYS an array under Set-StrictMode -Version
# Latest. Get-AcceleratorArguments `return @()` on its empty path emits ZERO
# objects, so a bare `$x = Get-Foo` assigns AutomationNull (the empty array
# unwraps to nothing). Then reading `$x.Count` THROWS "property 'Count' cannot be
# found on this object" under StrictMode 2.0+ (verified on pwsh 7.6.1). @(...)
# forces Count 0 when empty so the read is safe. (The later `... + $x` concat was
# fine either way: `+` DROPS the empty/AutomationNull capture rather than adding
# it -- only a LITERAL $null operand would add a spurious element.)
$acceleratorArgs = @(Get-AcceleratorArguments -Endpoint $env:UNITY_ACCELERATOR_ENDPOINT -Version $UnityVersion -Mode $TestMode)
if ($acceleratorArgs.Count -gt 0) {
    Write-CiNotice "Unity Accelerator enabled for namespace dxmessaging-$UnityVersion-$TestMode."
} else {
    Write-CiNotice "Unity Accelerator disabled; UNITY_ACCELERATOR_ENDPOINT is unset."
}

$testPlatform = switch ($TestMode) {
    'editmode' { 'EditMode' }
    'playmode' { 'PlayMode' }
    'standalone' { 'StandaloneWindows64' }
}

$resultsPath = Join-Path $ArtifactsPath 'results.xml'
$logPath = Join-Path $ArtifactsPath 'unity.log'
$configureLogPath = Join-Path $ArtifactsPath 'configure.log'
$startupProbeLogPath = Join-Path $ArtifactsPath 'unity-startup-probe.log'

# Activation/return carry the serial/email/password in their argument arrays and
# Unity may echo account/serial fragments into the activation log, so these logs
# MUST NOT live under $ArtifactsPath (the workflow uploads that as an artifact and
# the credentials would leak). Write them to a NON-uploaded temp dir instead.
$licenseLogDir = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [System.IO.Path]::GetTempPath() }
$activateLogPath = Join-Path $licenseLogDir "unity-activate-$UnityVersion-$TestMode.log"
$returnLogPath = Join-Path $licenseLogDir "unity-return-$UnityVersion-$TestMode.log"

# Return-at-start (defense-in-depth): reclaim a seat that a PRIOR force-killed run
# on this persistent self-hosted runner may have leaked before its own finally /
# the workflow if:always() step could run. Best-effort and never throws; if no
# seat is held this is a harmless no-op. Done BEFORE the activate so we start each
# run from a clean licensing state.
if ($hasLicenseCreds) {
    Invoke-UnityLicenseReturn -EditorPath $UnityEditorPath -Email $env:UNITY_EMAIL -Password $env:UNITY_PASSWORD -LogPath $returnLogPath
}

try {
    Invoke-UnityNativeStartupProbe -EditorPath $UnityEditorPath -LogPath $startupProbeLogPath

    # Activate the paid seat BEFORE configure/run so the test editor launches
    # licensed. Activation THROWS on failure (caught by this try's finally, which
    # still returns the seat). Skipped locally when creds are absent (the machine
    # is assumed already licensed).
    if ($hasLicenseCreds) {
        Invoke-UnityLicenseActivate -EditorPath $UnityEditorPath -Serial $env:UNITY_SERIAL -Email $env:UNITY_EMAIL -Password $env:UNITY_PASSWORD -LogPath $activateLogPath
    }

    if ($TestMode -eq 'standalone') {
        $configureArgs = @(
            '-quit',
            '-batchmode',
            '-nographics',
            '-projectPath', $ProjectPath,
            '-buildTarget', 'StandaloneWindows64',
            '-executeMethod', 'DxmCiTestConfigurator.Apply',
            '-logFile', '-'
        ) + $acceleratorArgs
        Invoke-UnityEditorWithFailureDiagnostics `
            -EditorPath $UnityEditorPath `
            -Arguments $configureArgs `
            -Label 'Configure standalone IL2CPP project' `
            -LogPath $configureLogPath `
            -Project $ProjectPath `
            -CscLabel 'standalone configure' `
            -DiagnosticsLabel 'Unity standalone configure'
        Write-CscRspDiagnostics -Project $ProjectPath -LogPath $configureLogPath -Label 'standalone configure'
    }

    $testArgs = @(
        '-quit',
        '-batchmode',
        '-nographics',
        '-projectPath', $ProjectPath,
        '-runTests',
        '-testPlatform', $testPlatform,
        '-testResults', $resultsPath,
        '-assemblyNames', $AssemblyNames,
        '-logFile', '-'
    ) + $acceleratorArgs

    if ($TestMode -eq 'standalone') {
        $testArgs += @('-buildTarget', 'StandaloneWindows64')
    }

    Invoke-UnityEditorWithFailureDiagnostics `
        -EditorPath $UnityEditorPath `
        -Arguments $testArgs `
        -Label "Run Unity $UnityVersion $TestMode tests" `
        -LogPath $logPath `
        -Project $ProjectPath `
        -CscLabel "$UnityVersion $TestMode test compile" `
        -DiagnosticsLabel "Unity $UnityVersion $TestMode"
    Write-CscRspDiagnostics -Project $ProjectPath -LogPath $logPath -Label "$UnityVersion $TestMode test compile"
    Test-NUnitResults -Path $resultsPath -Label "Unity $UnityVersion $TestMode" -LogPath $logPath -Project $ProjectPath
} finally {
    # Deterministic RETURN of the seat on EVERY exit path (clean exit, throw, or a
    # kill that still unwinds this finally). The workflow if:always() step is the
    # additional backstop for a hard-killed process that never reaches this finally,
    # and the NEXT run's return-at-start reclaims anything still leaked. Best-effort
    # and never throws, so it cannot mask a real test failure.
    if ($hasLicenseCreds) {
        Invoke-UnityLicenseReturn -EditorPath $UnityEditorPath -Email $env:UNITY_EMAIL -Password $env:UNITY_PASSWORD -LogPath $returnLogPath
    }
}
