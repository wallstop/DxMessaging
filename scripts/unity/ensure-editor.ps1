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

$script:UnityCliPath = 'unity'

# PowerShell 7.4 introduced $PSNativeCommandUseErrorActionPreference (stabilizing
# the native-error experimental feature). Its default is $false on current builds,
# so `& <native>` does NOT throw on a non-zero exit and our explicit exit checks
# run as written. However, a host profile or a future/different build could enable
# it, which would make `& <native>` THROW on a non-zero exit BEFORE our
# `if ($LASTEXITCODE -ne 0)` check runs -- making the best-effort invokers rely on
# their catch block instead of the explicit exit check. Pinning it $false makes
# LASTEXITCODE-based handling authoritative and identical across hosts/versions.
# (PS 5.1 lacks this variable; assigning it there is harmless, and the assignment
# is StrictMode-safe.)
$PSNativeCommandUseErrorActionPreference = $false

function Write-CiNotice {
    param([Parameter(Mandatory = $true)][string]$Message)
    Write-Host "::notice::$Message"
}

function Invoke-WithRetry {
    # Generic retry wrapper for a TERMINATING-error-prone operation (the base
    # editor install, which has been observed to fail flakily after a long run
    # with exit code 6 and almost no diagnostic output). Runs $Action; on a
    # thrown terminating error it logs a ::warning:: with the attempt number and
    # message, sleeps with linear backoff (DelaySeconds * attempt), then retries.
    # After exhausting $MaxAttempts it RETHROWS the LAST error so a persistent
    # failure still aborts the bootstrap loudly (never silently swallowed).
    # StrictMode-safe: no collection captures, no property reads on $null.
    param(
        [Parameter(Mandatory = $true)][scriptblock]$Action,
        [int]$MaxAttempts = 2,
        [int]$DelaySeconds = 15
    )

    if ($MaxAttempts -lt 1) { $MaxAttempts = 1 }

    $lastError = $null
    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        try {
            return & $Action
        } catch {
            $lastError = $_
            $message = $_.Exception.Message
            if ($attempt -lt $MaxAttempts) {
                $sleep = $DelaySeconds * $attempt
                Write-Host "::warning::Attempt $attempt of $MaxAttempts failed: $message. Retrying in $sleep second(s)."
                Start-Sleep -Seconds $sleep
            } else {
                Write-Host "::warning::Attempt $attempt of $MaxAttempts failed: $message. No attempts remaining."
            }
        }
    }

    # Exhausted every attempt: rethrow the last terminating error verbatim so the
    # original message (which now includes captured CLI output + exit code) reaches
    # CI logs unchanged.
    if ($lastError) {
        throw $lastError
    }

    # Defensive: only reachable if $MaxAttempts somehow yielded no iteration.
    throw "Invoke-WithRetry exhausted all attempts without capturing an error."
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

function Update-SessionPathFromRegistry {
    # The standalone Unity CLI installer writes %LOCALAPPDATA%\Unity\bin\unity.exe
    # and updates only the User-scope registry PATH; it never refreshes the
    # current session's $env:PATH. Rebuild the session PATH from the persisted
    # Machine + User registry values so the freshly installed CLI resolves in
    # this process, and prepend the installer's known target in case the
    # registry write lags. CRUCIAL: this .ps1 shares the caller's process
    # environment, so the existing $env:PATH carries process-only entries (e.g.
    # node added by setup-node via $GITHUB_PATH). Append it as the FINAL segment
    # so those entries survive instead of being clobbered.
    $segments = New-Object System.Collections.Generic.List[string]

    if ($env:LOCALAPPDATA) {
        $segments.Add((Join-Path $env:LOCALAPPDATA 'Unity\bin'))
    }

    $machinePath = [System.Environment]::GetEnvironmentVariable('Path', 'Machine')
    if ($machinePath) {
        $segments.Add($machinePath)
    }

    $userPath = [System.Environment]::GetEnvironmentVariable('Path', 'User')
    if ($userPath) {
        $segments.Add($userPath)
    }

    if ($env:PATH) {
        $segments.Add($env:PATH)
    }

    $env:PATH = (($segments | Where-Object { $_ -and $_.Trim().Length -gt 0 }) -join ';')
}

function Ensure-UnityCli {
    $command = Get-Command unity -ErrorAction SilentlyContinue
    if ($command) {
        $script:UnityCliPath = $command.Source
        return $command.Source
    }

    Write-CiNotice "Unity CLI was not found on PATH; installing the standalone Unity CLI for this runner."
    $env:UNITY_CLI_CHANNEL = if ($env:UNITY_CLI_CHANNEL) { $env:UNITY_CLI_CHANNEL } else { 'beta' }
    Invoke-Expression (Invoke-RestMethod 'https://public-cdn.cloud.unity3d.com/hub/prod/cli/install.ps1')

    $maxTries = 3
    for ($try = 1; $try -le $maxTries; $try++) {
        Update-SessionPathFromRegistry
        $command = Get-Command unity -ErrorAction SilentlyContinue
        if ($command) {
            $script:UnityCliPath = $command.Source
            return $command.Source
        }
        if ($try -lt $maxTries) {
            Start-Sleep -Seconds 2
        }
    }

    if ($env:LOCALAPPDATA) {
        $fallback = Join-Path $env:LOCALAPPDATA 'Unity\bin\unity.exe'
        if (Test-Path -LiteralPath $fallback -PathType Leaf) {
            $script:UnityCliPath = (Resolve-Path -LiteralPath $fallback).Path
            return $script:UnityCliPath
        }
    }

    throw "Unity CLI installation completed but 'unity' is still not on PATH. Reopen the runner shell or add the Unity CLI install directory to PATH."
}

function Invoke-UnityCliSafe {
    # NON-THROWING best-effort invoker. The standalone Unity CLI is a moving
    # beta surface (v0.1.0-beta.x); some flags are undocumented and may differ
    # between releases. For optional operations (setting the install path,
    # probing module ids) a non-zero exit must NOT abort the bootstrap, so this
    # variant returns $true/$false and never throws on a non-zero exit code.
    # It echoes the command and surfaces any output via Write-Host so failures
    # remain diagnosable in CI logs. Captured-output callers should use
    # Get-UnityCliOutput instead; this one is for fire-and-forget effects.
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    Write-Host "$script:UnityCliPath $($Arguments -join ' ')"
    try {
        # Merge stderr into stdout (2>&1) so a beta CLI that writes usage/errors
        # to stderr still gets echoed instead of vanishing, and so a native
        # stderr write cannot trip $ErrorActionPreference = 'Stop'.
        $output = & $script:UnityCliPath @Arguments 2>&1
        $exit = $LASTEXITCODE
    } catch {
        # Resolution/spawn failures (e.g. the CLI vanished) are non-fatal here.
        Write-Host "::notice::Unity CLI best-effort command threw and was ignored: $($_.Exception.Message)"
        return $false
    }

    if ($output) {
        $output | ForEach-Object { Write-Host $_ }
    }

    return ($exit -eq 0)
}

function Get-UnityCliOutput {
    # CAPTURING, NON-THROWING invoker for getter-style commands (install-path,
    # editors -i --format json). Returns an array of stdout lines (strings) on
    # success, or $null on any failure. Does NOT echo to the success pipeline
    # of this script: the caller (run-ci-tests.ps1) reads our LAST stdout line
    # as the resolved editor path, so getter output must never leak there.
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    Write-Host "$script:UnityCliPath $($Arguments -join ' ')"
    try {
        $raw = & $script:UnityCliPath @Arguments 2>$null
        $exit = $LASTEXITCODE
    } catch {
        Write-Host "::notice::Unity CLI getter command threw and was ignored: $($_.Exception.Message)"
        return $null
    }

    if ($exit -ne 0) {
        return $null
    }

    # Normalize to an array of strings regardless of whether 0/1/many lines came
    # back (a single line returns a scalar under the call operator).
    return @($raw | ForEach-Object { [string]$_ })
}

function Invoke-UnityCliCapture {
    # CAPTURING, NON-THROWING invoker that returns BOTH the exit status AND the
    # full output text, while STILL streaming output live to the console. The
    # other live invokers each give only part of this: Invoke-UnityCliSafe returns
    # a bool (no output, no exit code), and Get-UnityCliOutput captures lines but
    # returns $null on a non-zero exit (discarding the very output you need to
    # diagnose a failure). For classification logic that must inspect WHY a
    # command failed (e.g. the IL2CPP "No modules found to install" no-op) we need
    # the exit code + output together, so this helper provides both.
    #
    # Returns a StrictMode-safe hashtable with:
    #   Success  [bool]     - $true when exit code is 0
    #   ExitCode [int]      - the native exit code (-1 if the call threw/spawn failed)
    #   Output   [string[]] - @()-wrapped stdout+stderr lines (never $null)
    # Every field is always populated, so callers can read .Output.Count and
    # index .Output without the 0/1/many AutomationNull hazard.
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    Write-Host "$script:UnityCliPath $($Arguments -join ' ')"
    $lines = New-Object System.Collections.Generic.List[string]
    $exit = -1
    try {
        # Merge stderr into stdout (2>&1) so a beta CLI that writes errors/usage
        # to stderr is both echoed AND captured. Stream live (Write-Host) AND
        # accumulate so a long install is never silently buffered with a blank
        # console, yet the captured text remains available for diagnostics.
        & $script:UnityCliPath @Arguments 2>&1 | ForEach-Object {
            $line = [string]$_
            Write-Host $line
            $lines.Add($line)
        }
        $exit = $LASTEXITCODE
    } catch {
        # Resolution/spawn failures (e.g. the CLI vanished). Surface the message
        # in the captured output so a caller's diagnostic tail still shows it.
        $message = "Unity CLI capture invoker threw: $($_.Exception.Message)"
        Write-Host "::notice::$message"
        $lines.Add($message)
        $exit = -1
    }

    return @{
        Success  = ($exit -eq 0)
        ExitCode = $exit
        Output   = @($lines.ToArray())
    }
}

function Test-LooksLikeAbsolutePath {
    # True only for a Windows drive-letter path (C:\...) or a UNC path (\\...).
    # Guards the getter resolver against decorated/empty/relative output from a
    # beta CLI that might print a banner or a prompt instead of a bare path.
    param([string]$Value)

    if (-not $Value) {
        return $false
    }
    $trimmed = $Value.Trim()
    if ($trimmed.Length -lt 3) {
        return $false
    }
    if ($trimmed -match '^[A-Za-z]:[\\/]') {
        return $true
    }
    if ($trimmed.StartsWith('\\')) {
        return $true
    }

    return $false
}

function Get-UnityCliInstallRoot {
    # GETTER-based authoritative resolver. `unity install-path` with NO args is
    # a 0-arg getter that PRINTS the CLI's current editor install directory.
    # This reports the CLI's REAL install location regardless of whether our
    # best-effort SET succeeded, so discovery does not depend on the (uncertain)
    # set flag. Take the last non-empty path-like stdout line; ignore banners
    # and decorated output.
    $lines = Get-UnityCliOutput -Arguments @('install-path')
    if (-not $lines) {
        return $null
    }

    $candidate = $null
    foreach ($line in $lines) {
        if ($null -eq $line) {
            continue
        }
        $trimmed = ([string]$line).Trim()
        if ($trimmed.Length -eq 0) {
            continue
        }
        if (Test-LooksLikeAbsolutePath $trimmed) {
            $candidate = $trimmed
        }
    }

    if ($candidate) {
        Write-CiNotice "Unity CLI reports install root: $candidate"
    }

    return $candidate
}

function Set-UnityCliInstallPath {
    # BEST-EFFORT. Setting the install path is an OPTIMIZATION (it co-locates
    # editors under our chosen root), never a requirement: discovery falls back
    # to the getter-reported root and the candidate search. The SET flag for the
    # standalone CLI is NOT documented exactly (the Hub CLI uses `-s <dir>`; the
    # standalone CLI very likely mirrors `-s`/`--set`). Try `-s` first, then
    # `--set`; if both fail, emit a ::notice:: (NOT an error) and continue.
    param([Parameter(Mandatory = $true)][string]$Root)

    if (Invoke-UnityCliSafe -Arguments @('install-path', '-s', $Root)) {
        return
    }

    if (Invoke-UnityCliSafe -Arguments @('install-path', '--set', $Root)) {
        return
    }

    Write-CiNotice "Could not set the Unity CLI install path to '$Root' (best-effort; the standalone CLI set flag may differ). Continuing; discovery will use the CLI-reported install root."
}

function Resolve-EditorFromCliJson {
    # DEFENSIVE parse of `unity editors -i --format json`. The JSON schema is
    # NOT documented for the standalone CLI, so this scans every object for an
    # entry whose version matches $Version and pulls ANY plausible path-like
    # field. ConvertFrom-Json is wrapped in try/catch: malformed/non-JSON output
    # (e.g. a banner-prefixed beta response) returns $null instead of throwing.
    param(
        [Parameter(Mandatory = $true)][string]$Version
    )

    $lines = Get-UnityCliOutput -Arguments @('editors', '-i', '--format', 'json')
    if (-not $lines) {
        return $null
    }

    $jsonText = ($lines -join "`n").Trim()
    if ($jsonText.Length -eq 0) {
        return $null
    }

    try {
        $parsed = $jsonText | ConvertFrom-Json
    } catch {
        Write-Host "::notice::Could not parse 'unity editors -i --format json' output as JSON; continuing with candidate search."
        return $null
    }

    if ($null -eq $parsed) {
        return $null
    }

    # Normalize to a flat list of objects whether the CLI returned a top-level
    # array, a single object, or the most-likely real schema: a single object
    # that WRAPS the editor array under a property (e.g. {"editors":[...]}).
    # For the wrapped case we keep the wrapper object itself as a candidate AND
    # descend into any property whose value is a non-string IEnumerable, flattening
    # those items into $entries so the version-field scan below sees the real
    # editor records instead of mis-treating the wrapper as the lone entry.
    $entries = New-Object System.Collections.Generic.List[object]
    if ($parsed -is [System.Collections.IEnumerable] -and $parsed -isnot [string]) {
        foreach ($item in $parsed) { $entries.Add($item) }
    } else {
        $entries.Add($parsed)
        foreach ($prop in $parsed.PSObject.Properties) {
            if ($prop.Value -is [System.Collections.IEnumerable] -and $prop.Value -isnot [string]) {
                foreach ($item in $prop.Value) { $entries.Add($item) }
            }
        }
    }

    $versionFields = @('version', 'editorVersion', 'unityVersion', 'name')
    $pathFields = @('path', 'location', 'installPath', 'executable', 'installation', 'editorPath')

    foreach ($entry in $entries) {
        if ($null -eq $entry) { continue }

        $matchesVersion = $false
        foreach ($vf in $versionFields) {
            $vv = $null
            try { $vv = $entry.$vf } catch { $vv = $null }
            if ($vv -and ([string]$vv).Trim() -eq $Version) {
                $matchesVersion = $true
                break
            }
        }
        if (-not $matchesVersion) { continue }

        foreach ($pf in $pathFields) {
            $pv = $null
            try { $pv = $entry.$pf } catch { $pv = $null }
            if (-not $pv) { continue }
            $pathValue = ([string]$pv).Trim()
            if ($pathValue.Length -eq 0) { continue }

            # The field may already be Unity.exe, or a directory we must probe.
            if ((Test-Path -LiteralPath $pathValue -PathType Leaf) -and
                $pathValue.ToLowerInvariant().EndsWith('unity.exe')) {
                return (Resolve-Path -LiteralPath $pathValue).Path
            }

            $exeProbe = @(
                (Join-Path $pathValue 'Editor\Unity.exe'),
                (Join-Path $pathValue 'Unity.exe')
            )
            foreach ($probe in $exeProbe) {
                if (Test-Path -LiteralPath $probe -PathType Leaf) {
                    return (Resolve-Path -LiteralPath $probe).Path
                }
            }
        }
    }

    return $null
}

function Resolve-InstalledEditor {
    # Layered discovery, in order:
    #   (a) under the getter-reported CLI install root (authoritative),
    #   (b) under the configured $InstallRoot (candidate search),
    #   (c) defensive parse of `unity editors -i --format json`.
    # Returns the absolute Unity.exe path, or $null if every strategy fails.
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$Root
    )

    $cliRoot = Get-UnityCliInstallRoot
    if ($cliRoot) {
        foreach ($candidate in @(
                (Join-Path $cliRoot "$Version\Editor\Unity.exe"),
                (Join-Path $cliRoot "$Version\Unity.exe"))) {
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                return (Resolve-Path -LiteralPath $candidate).Path
            }
        }
    }

    $byCandidate = Find-UnityEditor -Version $Version -Root $Root
    if ($byCandidate) {
        return $byCandidate
    }

    $byJson = Resolve-EditorFromCliJson -Version $Version
    if ($byJson) {
        return $byJson
    }

    return $null
}

function Test-Il2CppModulePresent {
    # Disk-authoritative, best-effort probe for whether Windows IL2CPP support is
    # already installed for a resolved editor. The standalone CLI's
    # `install-modules` is not a reliable source of truth here: it returns "No
    # modules found to install." (exit 6) when the module is ALREADY present,
    # which is an idempotent no-op rather than a failure. The on-disk layout is
    # the real evidence.
    #
    # IMPORTANT: the exact on-disk layout VARIES BY UNITY VERSION (and has shifted
    # across the 2020->6000 lineage), so this probes two STANDALONE-SPECIFIC IL2CPP
    # signals under the Windows standalone support module and returns true if
    # EITHER exists: (a) the specific win64_player_development_il2cpp variation
    # folder, and (b) any subdirectory of the standalone Variations folder whose
    # name contains 'il2cpp'. It is best-effort CORROBORATION only -- a $false
    # result does not prove the module is absent (the layout may be one we don't
    # know), so callers must not treat $false as a hard "missing" signal. Fully
    # StrictMode-safe: every path read is guarded, missing paths yield $false, and
    # nothing throws.
    param([Parameter(Mandatory = $true)][string]$EditorPath)

    if (-not $EditorPath) {
        return $false
    }

    try {
        # $EditorPath looks like ...\<version>\Editor\Unity.exe; the editor data
        # root is its parent directory + 'Data'.
        $editorDir = Split-Path -Parent $EditorPath
        if (-not $editorDir) {
            return $false
        }
        $dataRoot = Join-Path $editorDir 'Data'
        $standaloneVariations = Join-Path $dataRoot 'PlaybackEngines\windowsstandalonesupport\Variations'

        # Strong, standalone-specific direct candidate: the development IL2CPP
        # player variation under the Windows standalone support module. (We
        # deliberately do NOT probe a bare Data\il2cpp folder: that il2cpp
        # toolchain directory can exist on Mono-only editors too, so it is a
        # false-positive risk for *Windows standalone* IL2CPP presence.)
        $developmentVariation = Join-Path $standaloneVariations 'win64_player_development_il2cpp'
        if (Test-Path -LiteralPath $developmentVariation) {
            return $true
        }

        # Variations scan: true if the standalone Variations folder holds ANY
        # subdirectory whose name contains 'il2cpp' (case-insensitive). This
        # catches version-specific variation names (e.g. *_il2cpp suffixes) we
        # have not enumerated explicitly above.
        if (Test-Path -LiteralPath $standaloneVariations) {
            $il2cppDirs = @(
                Get-ChildItem -LiteralPath $standaloneVariations -Directory -ErrorAction SilentlyContinue |
                    Where-Object { $_.Name -match '(?i)il2cpp' }
            )
            if ($il2cppDirs.Count -gt 0) {
                return $true
            }
        }
    } catch {
        # Any unexpected probe error is non-fatal: treat as "inconclusive" (false).
        return $false
    }

    return $false
}

function Add-WindowsIl2CppModule {
    # IDEMPOTENT, disk-authoritative IL2CPP module install for standalone. The
    # standalone beta CLI returns "No modules found to install." with exit code 6
    # when the IL2CPP module is ALREADY present -- an idempotent no-op, NOT a
    # failure -- so blindly treating any non-zero exit as fatal wrongly aborts the
    # job. We instead attempt the install via the NON-throwing capturing path and
    # CLASSIFY the result against the disk:
    #   1. install succeeded (exit 0)                              -> done.
    #   2. install failed BUT IL2CPP is present on disk            -> ::notice::, return.
    #   3. install failed, output matches a benign "nothing to do"
    #      pattern AND disk probe is inconclusive                 -> ::warning::, return.
    #   4. anything else                                          -> THROW (fatal),
    #      including the captured CLI output tail + exit code.
    # Case 4 preserves the original guarantee: a genuinely missing module with a
    # non-benign error STILL fails the job loudly. The benign-pattern branch
    # (case 3) does not mask that -- a real missing module produces no IL2CPP on
    # disk AND, when the CLI reports nothing to install, the absence will surface
    # at standalone build/test time; a non-benign error skips straight to case 4.
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$EditorPath
    )

    $moduleId = 'windows-il2cpp'

    # Best-effort listing diagnostic (unchanged): the beta listing format may not
    # contain the literal module id, so a mismatch only warns and never aborts.
    $listLines = @(Get-UnityCliOutput -Arguments @('install-modules', '-e', $Version, '-l'))
    if ($listLines.Count -gt 0) {
        $listText = ($listLines -join "`n")
        if ($listText -notmatch [regex]::Escape($moduleId)) {
            Write-Host "::warning::Unity $Version module listing did not contain the literal id '$moduleId' (the beta CLI may use a different listing format/display name). Proceeding; the install result below is classified against the on-disk module layout."
        }
    } else {
        Write-CiNotice "Could not list installable modules for Unity $Version (best-effort); proceeding with the standard module id '$moduleId'."
    }

    # Attempt the install via the capturing (non-throwing) path so we can inspect
    # BOTH the exit code AND the output text before deciding whether it was fatal.
    $result = Invoke-UnityCliCapture -Arguments @('install-modules', '-e', $Version, '-m', $moduleId)

    # Case 1: clean success.
    if ($result.Success) {
        return
    }

    $outputLines = @($result.Output)
    $outputText = ($outputLines -join "`n")
    # Tail of the captured output for diagnostics (last lines only, to keep the
    # thrown message readable).
    $tailCount = [Math]::Min(20, $outputLines.Count)
    $tail = if ($tailCount -gt 0) { ($outputLines[($outputLines.Count - $tailCount)..($outputLines.Count - 1)] -join "`n") } else { '(no output captured)' }

    # Case 2: install failed but the module is demonstrably present on disk -> the
    # CLI's non-zero exit was an idempotent no-op. Treat as success.
    if (Test-Il2CppModulePresent -EditorPath $EditorPath) {
        Write-CiNotice "Windows IL2CPP already present on disk; treating 'install-modules' no-op as success (CLI exit code $($result.ExitCode))."
        return
    }

    # Case 3: install failed with a benign "nothing to install / already
    # installed" message AND the disk probe was inconclusive (we could not
    # corroborate presence, but the CLI itself says there is nothing to do). Do
    # NOT abort: a genuinely missing module will surface in the standalone
    # build/test step. Warn so the situation is visible in CI logs.
    if ($outputText -match '(?i)no modules found to install|already installed|is already installed') {
        Write-Host "::warning::Unity $Version 'install-modules -m $moduleId' reported nothing to install (exit code $($result.ExitCode)) and IL2CPP could not be corroborated on disk. Continuing; a genuinely missing module will surface in the standalone build/test."
        return
    }

    # Case 4: genuine, non-benign failure. Fatal -- include the captured output
    # tail and exit code so CI logs show WHY the module install failed.
    throw "Unity $Version 'install-modules -m $moduleId' failed with exit code $($result.ExitCode) and Windows IL2CPP is not present on disk. CLI output tail:`n$tail"
}

function Write-InstallDiagnostics {
    # Pre-install diagnostic dump so the NEXT base-install failure is debuggable.
    # The observed failure (6000.0.32f1, ~34 minutes, exit 6, almost no output)
    # gave us nothing to act on. This emits, inside a collapsible ::group:::
    #   * the resolved CLI path actually being invoked,
    #   * the CLI version (best-effort), and
    #   * free disk space on the install drive (a likely culprit for a slow/failed
    #     multi-GB editor download).
    # Every probe is wrapped in try/catch and StrictMode-safe: a diagnostic must
    # NEVER abort the bootstrap it is meant to help debug.
    param([Parameter(Mandatory = $true)][string]$Root)

    Write-Host "::group::Unity CLI install diagnostics"
    try {
        Write-Host "Resolved Unity CLI path: $script:UnityCliPath"
    } catch {
        Write-Host "::notice::Could not report the resolved Unity CLI path: $($_.Exception.Message)"
    }

    try {
        $versionLines = @(Get-UnityCliOutput -Arguments @('--version'))
        if ($versionLines.Count -gt 0) {
            Write-Host "Unity CLI version: $($versionLines -join ' ')"
        } else {
            Write-Host "::notice::Unity CLI version was not reported by '--version' (best-effort)."
        }
    } catch {
        Write-Host "::notice::Could not query the Unity CLI version: $($_.Exception.Message)"
    }

    try {
        # Free space on the drive of the install root. A multi-GB editor download
        # that runs out of disk would explain a long, output-starved failure.
        $rootFull = [System.IO.Path]::GetFullPath($Root)
        $drive = [System.IO.Path]::GetPathRoot($rootFull)
        if ($drive) {
            $driveInfo = New-Object System.IO.DriveInfo($drive)
            $freeGb = [Math]::Round($driveInfo.AvailableFreeSpace / 1GB, 2)
            $totalGb = [Math]::Round($driveInfo.TotalSize / 1GB, 2)
            Write-Host "Install drive $drive free space: $freeGb GB free of $totalGb GB total."
        } else {
            Write-Host "::notice::Could not determine the install drive for '$Root'."
        }
    } catch {
        Write-Host "::notice::Could not query install drive free space: $($_.Exception.Message)"
    }
    Write-Host "::endgroup::"
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

    # Emit diagnostics BEFORE the (potentially 30+ minute) install so the logs
    # carry the CLI path/version + disk headroom even if the install then stalls.
    Write-InstallDiagnostics -Root $InstallRoot

    # The base install has been observed to fail flakily (exit 6 after a long run
    # with almost no output). Retry once via Invoke-WithRetry (two attempts fit
    # inside the 120-minute step budget even for a slow install), and use the
    # CAPTURING invoker so a final failure THROWS with the CLI output tail + exit
    # code -- the previous failure surfaced no actionable diagnostics. Output is
    # still streamed live by Invoke-UnityCliCapture, never silently buffered.
    Invoke-WithRetry -MaxAttempts 2 -DelaySeconds 15 -Action {
        $installResult = Invoke-UnityCliCapture -Arguments $installArgs
        if (-not $installResult.Success) {
            $installLines = @($installResult.Output)
            $installTailCount = [Math]::Min(40, $installLines.Count)
            $installTail = if ($installTailCount -gt 0) {
                ($installLines[($installLines.Count - $installTailCount)..($installLines.Count - 1)] -join "`n")
            } else {
                '(no output captured)'
            }
            throw "Unity CLI '$($installArgs -join ' ')' failed with exit code $($installResult.ExitCode). CLI output tail:`n$installTail"
        }
    }

    $editor = Resolve-InstalledEditor -Version $UnityVersion -Root $InstallRoot
    if (-not $editor) {
        Write-Host "::group::Installed Unity Editors"
        Invoke-UnityCliSafe -Arguments @('editors', '-i') | Out-Null
        Write-Host "::endgroup::"
        throw "Unity $UnityVersion was installed or already present, but Unity.exe could not be found in known locations. Set UNITY_EDITOR_INSTALL_ROOT or UNITY_EDITOR_PATH."
    }
} elseif ($WithWindowsIl2Cpp) {
    Ensure-UnityCli | Out-Null
    Set-UnityCliInstallPath -Root $InstallRoot
    Write-CiNotice "Ensuring Windows IL2CPP module is installed for Unity $UnityVersion."
    Add-WindowsIl2CppModule -Version $UnityVersion -EditorPath $editor
}

Write-CiNotice "Unity editor resolved: $editor"
Write-Output $editor
