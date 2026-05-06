<#
    .SYNOPSIS
        Capture a Unity performance baseline for DispatchThroughputBenchmarks.

    .DESCRIPTION
        Runs the explicit DispatchThroughputBenchmarks baseline update test
        for a commit. The Unity test writes the normalized baseline CSV.

    .PARAMETER Commit
        Commit/ref value to expose to Unity as DX_PERF_COMMIT. When omitted,
        the script prompts interactively.

    .PARAMETER Output
        Baseline CSV output path. Defaults to .artifacts/perf-baseline.csv.

    .PARAMETER Append
        Kept for compatibility. Baseline updates add missing rows and replace
        matching rows by default.

    .PARAMETER Replace
        Replace the full baseline CSV instead of updating matching rows.

    .PARAMETER Help
        Show detailed help and exit.

    .EXAMPLE
        pwsh -NoProfile -File scripts/unity/capture-perf-baseline.ps1 `
            -Commit bf448fe84872022343260cb636409a9d3831bdec

    .EXAMPLE
        pwsh -NoProfile -File scripts/unity/capture-perf-baseline.ps1 -Replace
#>

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Commit,

    [string]$Output = '.artifacts/perf-baseline.csv',

    [switch]$Append,

    [switch]$Replace,

    [switch]$Help
)

$ErrorActionPreference = 'Stop'

# cspell:ignore PSHOME

if ($Help) {
    Get-Help $PSCommandPath -Detailed
    exit 0
}

if ([string]::IsNullOrWhiteSpace($Commit)) {
    $Commit = Read-Host 'Commit/ref for DX_PERF_COMMIT'
}
$Commit = $Commit.Trim()

if ([string]::IsNullOrWhiteSpace($Commit)) {
    Write-Host 'ERROR: -Commit is required.' -ForegroundColor Red
    exit 2
}

$ScriptDir = Split-Path -Parent $PSCommandPath
$RepoRoot = (Resolve-Path (Join-Path $ScriptDir '..\..')).Path
$ArtifactsDir = Join-Path $RepoRoot '.artifacts'

if (-not (Test-Path $ArtifactsDir)) {
    New-Item -ItemType Directory -Path $ArtifactsDir -Force | Out-Null
}

$artifactToken = $Commit -replace '[^A-Za-z0-9_.-]', '-'
$resultsPath = Join-Path $ArtifactsDir "perf-$artifactToken-results.xml"
$logPath = Join-Path $ArtifactsDir "perf-$artifactToken-unity-log.txt"
$runnerPath = Join-Path $ScriptDir 'run-tests.ps1'
$filter = 'DxMessaging.Tests.Runtime.Benchmarks.DispatchThroughputBenchmarks.UpdateDispatchThroughputBaseline'

function ConvertTo-RepoRelativePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if ([System.IO.Path]::IsPathRooted($Path)) {
        $fullPath = [System.IO.Path]::GetFullPath($Path)
    } else {
        $fullPath = [System.IO.Path]::GetFullPath($Path, $RepoRoot)
    }

    $relativePath = [System.IO.Path]::GetRelativePath($RepoRoot, $fullPath)
    if ([System.IO.Path]::IsPathRooted($relativePath) -or $relativePath -eq '..' -or $relativePath.StartsWith("..$([System.IO.Path]::DirectorySeparatorChar)") -or $relativePath.StartsWith("..$([System.IO.Path]::AltDirectorySeparatorChar)")) {
        return $null
    }

    return $relativePath
}

$BaselinePathForUnity = ConvertTo-RepoRelativePath $Output
if ([string]::IsNullOrWhiteSpace($BaselinePathForUnity)) {
    Write-Host @"
ERROR: -Output must be relative to the repo or under the repo root.
Paths outside the repo are not visible to Docker Unity runs.
"@ -ForegroundColor Red
    exit 2
}

if ([System.IO.Path]::IsPathRooted($Output)) {
    $BaselineDisplayPath = [System.IO.Path]::GetFullPath($Output)
} else {
    $BaselineDisplayPath = [System.IO.Path]::GetFullPath($Output, $RepoRoot)
}
$outputDir = Split-Path -Parent $BaselineDisplayPath
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

function Find-ExecutableOnPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$CommandName
    )

    $pathValue = [Environment]::GetEnvironmentVariable('PATH')
    if ([string]::IsNullOrWhiteSpace($pathValue)) {
        return $null
    }

    $extensions = @('')
    if ($IsWindows) {
        $pathExtValue = [Environment]::GetEnvironmentVariable('PATHEXT')
        if ([string]::IsNullOrWhiteSpace($pathExtValue)) {
            $extensions = @('.exe', '.cmd', '.bat', '')
        } else {
            $extensions = $pathExtValue.Split([System.IO.Path]::PathSeparator) + ''
        }
    }

    $fallbackCandidate = $null
    foreach ($pathEntry in $pathValue.Split([System.IO.Path]::PathSeparator)) {
        if ([string]::IsNullOrWhiteSpace($pathEntry)) {
            continue
        }

        foreach ($extension in $extensions) {
            $candidate = Join-Path $pathEntry "$CommandName$extension"
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                $resolvedCandidate = (Resolve-Path -LiteralPath $candidate).Path
                if (-not $fallbackCandidate) {
                    $fallbackCandidate = $resolvedCandidate
                }

                $candidateDir = Split-Path -Parent $resolvedCandidate
                if (-not $PSHOME -or $candidateDir -ne $PSHOME) {
                    return $resolvedCandidate
                }
            }
        }
    }

    return $fallbackCandidate
}

$pwshPath = Find-ExecutableOnPath 'pwsh'
if (-not $pwshPath) {
    Write-Host 'ERROR: pwsh is required to run the Unity PowerShell test runner.' -ForegroundColor Red
    exit 1
}

$previousDxPerfCommit = $env:DX_PERF_COMMIT
$hadDxPerfCommit = Test-Path Env:DX_PERF_COMMIT
$previousBaseline = $env:DX_PERF_BASELINE
$hadBaseline = Test-Path Env:DX_PERF_BASELINE
$previousBaselineMode = $env:DX_PERF_BASELINE_MODE
$hadBaselineMode = Test-Path Env:DX_PERF_BASELINE_MODE
$env:DX_PERF_COMMIT = $Commit
$env:DX_PERF_BASELINE = $BaselinePathForUnity
if ($Replace) {
    $env:DX_PERF_BASELINE_MODE = 'replace'
} else {
    Remove-Item Env:DX_PERF_BASELINE_MODE -ErrorAction SilentlyContinue
}

try {
    Write-Host "Updating DispatchThroughputBenchmarks baseline for $Commit"
    Write-Host "Unity results: $resultsPath"
    Write-Host "Unity log:     $logPath"
    Write-Host "Baseline CSV:  $BaselineDisplayPath"

    $baselineTimestampBeforeRun = $null
    if (Test-Path -LiteralPath $BaselineDisplayPath -PathType Leaf) {
        $baselineTimestampBeforeRun = (Get-Item -LiteralPath $BaselineDisplayPath).LastWriteTimeUtc
    }

    & $pwshPath -NoProfile -File $runnerPath `
        -Platform playmode `
        -IncludePerf `
        -Filter $filter `
        -Results $resultsPath 2>&1 |
        Tee-Object -FilePath $logPath

    $unityExitCode = $LASTEXITCODE
    if ($unityExitCode -ne 0) {
        Write-Host "ERROR: Unity perf run failed with exit code $unityExitCode." -ForegroundColor Red
        exit $unityExitCode
    }

    if (-not (Test-Path -LiteralPath $BaselineDisplayPath -PathType Leaf)) {
        Write-Host "ERROR: Unity perf run completed but did not write baseline CSV: $BaselineDisplayPath" -ForegroundColor Red
        exit 1
    }

    $baselineTimestampAfterRun = (Get-Item -LiteralPath $BaselineDisplayPath).LastWriteTimeUtc
    if ($baselineTimestampBeforeRun -and $baselineTimestampAfterRun -le $baselineTimestampBeforeRun) {
        Write-Host "ERROR: Unity perf run completed but did not update baseline CSV: $BaselineDisplayPath" -ForegroundColor Red
        exit 1
    }
}
finally {
    if ($hadDxPerfCommit) {
        $env:DX_PERF_COMMIT = $previousDxPerfCommit
    } else {
        Remove-Item Env:DX_PERF_COMMIT -ErrorAction SilentlyContinue
    }

    if ($hadBaseline) {
        $env:DX_PERF_BASELINE = $previousBaseline
    } else {
        Remove-Item Env:DX_PERF_BASELINE -ErrorAction SilentlyContinue
    }

    if ($hadBaselineMode) {
        $env:DX_PERF_BASELINE_MODE = $previousBaselineMode
    } else {
        Remove-Item Env:DX_PERF_BASELINE_MODE -ErrorAction SilentlyContinue
    }
}
