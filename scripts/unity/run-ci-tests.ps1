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

$PackageName = 'com.wallstop-studios.dxmessaging'
$TestFrameworkVersion = '1.4.5'
$PerformanceFrameworkVersion = '3.4.2'

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

function Install-UnityLicenseFile {
    if (-not $env:UNITY_LICENSE -or $env:UNITY_LICENSE.Trim().Length -eq 0) {
        return
    }

    $licenseDir = Join-Path $env:ProgramData 'Unity'
    New-Item -ItemType Directory -Force -Path $licenseDir | Out-Null
    $licensePath = Join-Path $licenseDir 'Unity_lic.ulf'
    $env:UNITY_LICENSE | Set-Content -LiteralPath $licensePath -Encoding UTF8
    Write-CiNotice "Installed UNITY_LICENSE into ProgramData for direct Unity execution."
}

function Get-UnityLicenseArguments {
    if ($env:UNITY_SERIAL -and $env:UNITY_SERIAL.Trim().Length -gt 0) {
        if (-not $env:UNITY_EMAIL -or -not $env:UNITY_PASSWORD) {
            throw 'UNITY_EMAIL and UNITY_PASSWORD are required when UNITY_SERIAL is used.'
        }
        return @('-serial', $env:UNITY_SERIAL, '-username', $env:UNITY_EMAIL, '-password', $env:UNITY_PASSWORD)
    }

    return @()
}

function Invoke-UnityEditor {
    param(
        [Parameter(Mandatory = $true)][string]$EditorPath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$Label
    )

    Write-Host "::group::$Label"
    Write-Host "`"$EditorPath`" $($Arguments -join ' ')"
    & $EditorPath @Arguments
    $exitCode = $LASTEXITCODE
    Write-Host "::endgroup::"
    if ($exitCode -ne 0) {
        throw "$Label failed with exit code $exitCode."
    }
}

function Test-NUnitResults {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        Write-CiError "No NUnit results XML exists at $Path for $Label."
        throw "Unity did not produce NUnit results for $Label."
    }

    [xml]$xml = Get-Content -LiteralPath $Path -Raw
    $run = $xml.SelectSingleNode('//test-run')
    if (-not $run) {
        Write-CiError "NUnit results at $Path do not contain a <test-run> element."
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
Write-Host "::endgroup::"

if ($GenerateOnly) {
    Write-CiNotice "Generated ephemeral Unity project only: $ProjectPath"
    exit 0
}

if (-not $UnityEditorPath -or $UnityEditorPath.Trim().Length -eq 0) {
    $ensureEditor = Join-Path $PSScriptRoot 'ensure-editor.ps1'
    $ensureArgs = @{
        UnityVersion = $UnityVersion
        InstallRoot = $UnityInstallRoot
    }
    if ($TestMode -eq 'standalone') {
        $ensureArgs.WithWindowsIl2Cpp = $true
    }
    $UnityEditorPath = (& $ensureEditor @ensureArgs | Select-Object -Last 1)
}

if (-not (Test-Path -LiteralPath $UnityEditorPath -PathType Leaf)) {
    throw "Unity editor not found: $UnityEditorPath"
}

Install-UnityLicenseFile
$licenseArgs = Get-UnityLicenseArguments
$acceleratorArgs = Get-AcceleratorArguments -Endpoint $env:UNITY_ACCELERATOR_ENDPOINT -Version $UnityVersion -Mode $TestMode
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

try {
    if ($TestMode -eq 'standalone') {
        $configureArgs = @(
            '-quit',
            '-batchmode',
            '-nographics',
            '-projectPath', $ProjectPath,
            '-buildTarget', 'StandaloneWindows64',
            '-executeMethod', 'DxmCiTestConfigurator.Apply',
            '-logFile', $configureLogPath
        ) + $licenseArgs + $acceleratorArgs
        Invoke-UnityEditor -EditorPath $UnityEditorPath -Arguments $configureArgs -Label 'Configure standalone IL2CPP project'
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
        '-logFile', $logPath
    ) + $licenseArgs + $acceleratorArgs

    if ($TestMode -eq 'standalone') {
        $testArgs += @('-buildTarget', 'StandaloneWindows64')
    }

    Invoke-UnityEditor -EditorPath $UnityEditorPath -Arguments $testArgs -Label "Run Unity $UnityVersion $TestMode tests"
    Test-NUnitResults -Path $resultsPath -Label "Unity $UnityVersion $TestMode"
} finally {
    if ($env:UNITY_SERIAL -and $env:UNITY_SERIAL.Trim().Length -gt 0 -and (Test-Path -LiteralPath $UnityEditorPath -PathType Leaf)) {
        try {
            $returnLog = Join-Path $ArtifactsPath 'return-license.log'
            & $UnityEditorPath -quit -batchmode -nographics -returnlicense -logFile $returnLog | Out-Host
        } catch {
            Write-Host "::warning::Unity license return failed: $($_.Exception.Message)"
        }
    }
}
