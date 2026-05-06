<#
    .SYNOPSIS
        Capture a Unity performance baseline for DispatchThroughputBenchmarks.

    .DESCRIPTION
        Runs the playmode DispatchThroughputBenchmarks perf suite for a commit,
        tees the Unity output to a commit-specific log file, and extracts the
        normalized baseline CSV via scripts/unity/extract-perf-baseline.js.

    .PARAMETER Commit
        Commit/ref value to expose to Unity as DX_PERF_COMMIT. When omitted,
        the script prompts interactively.

    .PARAMETER Output
        Baseline CSV output path. Defaults to .artifacts/perf-baseline.csv.

    .PARAMETER Append
        Append extracted rows to an existing baseline CSV.

    .PARAMETER Replace
        Replace an existing baseline CSV.

    .PARAMETER Help
        Show detailed help and exit.

    .EXAMPLE
        pwsh -NoProfile -File scripts/unity/capture-perf-baseline.ps1 `
            -Commit bf448fe84872022343260cb636409a9d3831bdec

    .EXAMPLE
        pwsh -NoProfile -File scripts/unity/capture-perf-baseline.ps1 -Append
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

if ($Append -and $Replace) {
    Write-Host 'ERROR: -Append and -Replace cannot both be specified.' -ForegroundColor Red
    exit 2
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
$extractorPath = Join-Path $ScriptDir 'extract-perf-baseline.js'
$filter = 'DxMessaging.Tests.Runtime.Benchmarks.DispatchThroughputBenchmarks.*'

if (-not [System.IO.Path]::IsPathRooted($Output)) {
    $Output = Join-Path $RepoRoot $Output
}
$outputDir = Split-Path -Parent $Output
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

if ((Test-Path -LiteralPath $Output) -and -not $Append -and -not $Replace) {
    Write-Host "ERROR: Output already exists: $Output. Specify -Append or -Replace." -ForegroundColor Red
    exit 2
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
$env:DX_PERF_COMMIT = $Commit

try {
    Write-Host "Running DispatchThroughputBenchmarks for $Commit"
    Write-Host "Unity results: $resultsPath"
    Write-Host "Unity log:     $logPath"

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

    $extractArgs = @(
        $extractorPath,
        '--input', $logPath,
        '--input', $resultsPath,
        '--output', $Output
    )
    if ($Append) {
        $extractArgs += '--append'
    } elseif ($Replace) {
        $extractArgs += '--replace'
    }

    & node @extractArgs
    $extractExitCode = $LASTEXITCODE
    if ($extractExitCode -ne 0) {
        Write-Host "ERROR: Baseline extraction failed with exit code $extractExitCode." -ForegroundColor Red
        exit $extractExitCode
    }

    Write-Host "Baseline CSV:  $Output"
}
finally {
    if ($hadDxPerfCommit) {
        $env:DX_PERF_COMMIT = $previousDxPerfCommit
    } else {
        Remove-Item Env:DX_PERF_COMMIT -ErrorAction SilentlyContinue
    }
}
