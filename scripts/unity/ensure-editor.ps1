#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+f\d+$')]
    [string]$UnityVersion,

    [string]$InstallRoot = $(if ($env:UNITY_EDITOR_INSTALL_ROOT) { $env:UNITY_EDITOR_INSTALL_ROOT } else { 'C:\Unity\Editors' }),

    [switch]$WithWindowsIl2Cpp
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-CiNotice {
    param([Parameter(Mandatory = $true)][string]$Message)
    Write-Host "::notice::$Message"
}

function Get-UnityEditorCandidates {
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$Root
    )

    @(
        (Join-Path $Root "$Version\Editor\Unity.exe"),
        (Join-Path $Root "$Version\Unity.exe"),
        (Join-Path ${env:ProgramFiles} "Unity\Hub\Editor\$Version\Editor\Unity.exe"),
        (Join-Path ${env:ProgramFiles} "Unity\$Version\Editor\Unity.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Unity\Hub\Editor\$Version\Editor\Unity.exe")
    ) | Where-Object { $_ -and $_.Trim().Length -gt 0 }
}

function Find-UnityEditor {
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$Root
    )

    foreach ($candidate in Get-UnityEditorCandidates -Version $Version -Root $Root) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    return $null
}

function Set-UnityCliInstallPath {
    param([Parameter(Mandatory = $true)][string]$Root)

    Invoke-UnityCli -Arguments @('install-path', $Root)
}

function Ensure-UnityCli {
    $command = Get-Command unity -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    Write-CiNotice "Unity CLI was not found on PATH; installing the standalone Unity CLI for this runner."
    $env:UNITY_CLI_CHANNEL = if ($env:UNITY_CLI_CHANNEL) { $env:UNITY_CLI_CHANNEL } else { 'beta' }
    Invoke-Expression (Invoke-RestMethod 'https://public-cdn.cloud.unity3d.com/hub/prod/cli/install.ps1')

    $command = Get-Command unity -ErrorAction SilentlyContinue
    if (-not $command) {
        throw "Unity CLI installation completed but 'unity' is still not on PATH. Reopen the runner shell or add the Unity CLI install directory to PATH."
    }

    return $command.Source
}

function Invoke-UnityCli {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    Write-Host "unity $($Arguments -join ' ')"
    & unity @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Unity CLI command failed with exit code ${LASTEXITCODE}: unity $($Arguments -join ' ')"
    }
}

New-Item -ItemType Directory -Force -Path $InstallRoot | Out-Null

$editor = Find-UnityEditor -Version $UnityVersion -Root $InstallRoot
if (-not $editor) {
    Ensure-UnityCli | Out-Null
    Set-UnityCliInstallPath -Root $InstallRoot

    Write-CiNotice "Installing Unity Editor $UnityVersion on the self-hosted Windows runner."
    $installArgs = @('install', $UnityVersion)
    if ($WithWindowsIl2Cpp) {
        $installArgs += @('-m', 'windows-il2cpp')
    }
    Invoke-UnityCli -Arguments $installArgs

    $editor = Find-UnityEditor -Version $UnityVersion -Root $InstallRoot
    if (-not $editor) {
        Write-Host "::group::Installed Unity Editors"
        Invoke-UnityCli -Arguments @('editors', '-i')
        Write-Host "::endgroup::"
        throw "Unity $UnityVersion was installed or already present, but Unity.exe could not be found in known locations. Set UNITY_EDITOR_INSTALL_ROOT or UNITY_EDITOR_PATH."
    }
} elseif ($WithWindowsIl2Cpp) {
    Ensure-UnityCli | Out-Null
    Set-UnityCliInstallPath -Root $InstallRoot
    Write-CiNotice "Ensuring Windows IL2CPP module is installed for Unity $UnityVersion."
    Invoke-UnityCli -Arguments @('install-modules', '-e', $UnityVersion, '-m', 'windows-il2cpp')
}

Write-CiNotice "Unity editor resolved: $editor"
Write-Output $editor
