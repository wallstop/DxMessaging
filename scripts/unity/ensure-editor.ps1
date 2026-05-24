#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+f\d+$')]
    [string]$UnityVersion,

    [string]$InstallRoot = $(if ($env:UNITY_EDITOR_INSTALL_ROOT) { $env:UNITY_EDITOR_INSTALL_ROOT } else { 'C:\Unity\Editors' }),

    [switch]$CiManagedOnly = $($env:GITHUB_ACTIONS -eq 'true'),

    [switch]$WithWindowsIl2Cpp
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:UnityCliPath = 'unity'
$script:UnityInstallLockDepth = 0

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

function Get-EnsureEditorRetryDelaySeconds {
    # Single source of truth for the Invoke-WithRetry backoff delay. Honors the
    # DXM_ENSURE_EDITOR_RETRY_DELAY_SECONDS override (tests set it to 0 to avoid
    # real sleeps; CI leaves it unset for the production 15s backoff). A
    # non-integer or negative override is ignored with a ::warning:: and the
    # default is used. StrictMode-safe: no collection reads.
    param([int]$Default = 15)

    if ($env:DXM_ENSURE_EDITOR_RETRY_DELAY_SECONDS) {
        $parsed = 0
        if (
            [int]::TryParse($env:DXM_ENSURE_EDITOR_RETRY_DELAY_SECONDS, [ref]$parsed) -and
            $parsed -ge 0
        ) {
            return $parsed
        }
        Write-Host "::warning::Ignoring invalid DXM_ENSURE_EDITOR_RETRY_DELAY_SECONDS='$env:DXM_ENSURE_EDITOR_RETRY_DELAY_SECONDS'; using $Default second(s)."
    }
    return $Default
}

function Invoke-WithUnityInstallLock {
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$InstallRoot,
        [Parameter(Mandatory = $true)][scriptblock]$Action,
        [int]$TimeoutMinutes = 180
    )

    if ($script:UnityInstallLockDepth -gt 0) {
        return & $Action
    }

    $lockRoot = Join-Path $InstallRoot '_locks'
    New-Item -ItemType Directory -Force -Path $lockRoot | Out-Null
    $lockPath = Join-Path $lockRoot "$Version-ci-modules.lock"
    $deadline = [DateTime]::UtcNow.AddMinutes($TimeoutMinutes)
    $stream = $null

    while ($null -eq $stream) {
        try {
            $stream = [System.IO.File]::Open(
                $lockPath,
                [System.IO.FileMode]::OpenOrCreate,
                [System.IO.FileAccess]::ReadWrite,
                [System.IO.FileShare]::None
            )
        } catch {
            if ([DateTime]::UtcNow -ge $deadline) {
                throw "Timed out waiting for Unity install lock '$lockPath' after $TimeoutMinutes minute(s)."
            }
            Write-Host "::notice::Waiting for Unity install lock: $lockPath"
            Start-Sleep -Seconds 10
        }
    }

    try {
        $script:UnityInstallLockDepth++
        return & $Action
    } finally {
        $script:UnityInstallLockDepth--
        if ($stream) {
            $stream.Dispose()
        }
    }
}

function Get-UnityEditorCandidates {
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$Root,
        [switch]$IncludeHostInstalls
    )

    $candidates = New-Object System.Collections.Generic.List[string]
    $candidates.Add((Join-Path $Root "$Version\Editor\Unity.exe"))
    $candidates.Add((Join-Path $Root "$Version\Unity.exe"))

    if ($IncludeHostInstalls -and ${env:ProgramFiles} -and ${env:ProgramFiles}.Trim().Length -gt 0) {
        $candidates.Add((Join-Path ${env:ProgramFiles} "Unity\Hub\Editor\$Version\Editor\Unity.exe"))
        $candidates.Add((Join-Path ${env:ProgramFiles} "Unity\$Version\Editor\Unity.exe"))
    }
    if ($IncludeHostInstalls -and ${env:ProgramFiles(x86)} -and ${env:ProgramFiles(x86)}.Trim().Length -gt 0) {
        $candidates.Add(
            (Join-Path ${env:ProgramFiles(x86)} "Unity\Hub\Editor\$Version\Editor\Unity.exe")
        )
    }

    return @($candidates.ToArray() | Where-Object { $_ -and $_.Trim().Length -gt 0 })
}

function Find-UnityEditor {
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$Root,
        [switch]$IncludeHostInstalls
    )

    foreach ($candidate in Get-UnityEditorCandidates -Version $Version -Root $Root -IncludeHostInstalls:$IncludeHostInstalls) {
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

function Write-UnityCliInstallFailureAnnotation {
    # HIGH-SIGNAL, additive CI annotation for a FAILED module install. Scans the
    # captured CLI output for the two failure SIGNATURES this script has been bitten
    # by and emits a targeted ::error::/::warning:: that NAMES the remediation, so a
    # future regression of this exact class is obvious at a glance in the CI log
    # instead of buried in a generic exit-code dump. Additive only: callers still
    # throw/log their full message + arg vector + exit code separately. Best-effort
    # and StrictMode-safe: never throws, @()-wraps the output capture.
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [string[]]$Output,
        [int]$ExitCode = -1,
        [string[]]$Arguments
    )

    $text = (@($Output) -join "`n")
    $argLine = if ($Arguments) { ($Arguments -join ' ') } else { '(unavailable)' }

    # Match only the ACTUAL EULA-rejection phrasing, never a bare `--accept-eula`.
    # The real failing log line is "Error: One or more modules require license
    # acceptance. Pass --accept-eula to accept all module license terms and
    # proceed." -- matching the remediation phrase `Pass --accept-eula` (or the
    # "require[s] license acceptance" cause) avoids a self-false-positive if the CLI
    # ever echoes our own invoked args (which contain `--accept-eula`) back to stdout.
    if ($text -match '(?i)require[s]? license acceptance|Pass\s+--accept-eula') {
        # The fix is structural (Get-UnityCliModuleInstallArguments injects
        # --accept-eula for every module install); if this still fires, the flag is
        # no longer being honored by this CLI build for this verb.
        Write-Host "::error::Unity $Version module install was rejected for missing EULA acceptance (exit $ExitCode). Every module install in this script must pass --accept-eula via Get-UnityCliModuleInstallArguments. Args: $argLine"
    }
    if ($text -match "(?i)couldn't find module|could not find module|missing module|did you mean") {
        # A requested -m id is unknown to this CLI build (e.g. a version-pinned id
        # drifted). OpenJDK is intentionally NOT requested (it arrives as an
        # android-sdk-ndk-tools dependency); any other id here needs correcting in
        # Get-UnityCiModuleIds.
        Write-Host "::warning::Unity $Version module install reported an unknown module id (exit $ExitCode). Check the 'Did you mean:' hint in the CLI output above and reconcile Get-UnityCiModuleIds. Args: $argLine"
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
    if ($trimmed.StartsWith('/')) {
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

function Confirm-UnityCliManagedInstallRoot {
    param([Parameter(Mandatory = $true)][string]$Root)

    $cliRoot = Get-UnityCliInstallRoot
    if (-not $cliRoot) {
        # Emit a wrap-IMMUNE CI annotation BEFORE the throw. PowerShell's
        # ConciseView formatter word-wraps a thrown message at the console width
        # (splitting phrases across a `     | ` gutter), so the throw text alone is
        # an unreliable single-line annotation; Write-Host output is never wrapped,
        # giving CI a clean ::error:: line AND a stable assertion target. Additive
        # only -- the throw below still aborts with identical semantics.
        Write-Host "::error::CI-managed Unity provisioning cannot mutate editors because the Unity CLI did not report an install root after setting '$Root'."
        throw "CI-managed Unity provisioning cannot mutate editors because the Unity CLI did not report an install root after setting '$Root'."
    }
    if (-not (Test-IsPathInsideDirectory -Path $cliRoot -Directory $Root)) {
        # Wrap-immune CI annotation before the throw (see the note above): the
        # "outside the managed root" phrase is exactly the one PowerShell's
        # word-wrap was observed to split on the narrower Windows runner.
        Write-Host "::error::CI-managed Unity provisioning cannot mutate editors because the Unity CLI install root is outside the managed root. CLI root: '$cliRoot'. Managed root: '$Root'."
        throw "CI-managed Unity provisioning cannot mutate editors because the Unity CLI install root is outside the managed root. CLI root: '$cliRoot'. Managed root: '$Root'."
    }
    return $cliRoot
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
        [Parameter(Mandatory = $true)][string]$Root,
        [switch]$ManagedOnly
    )

    $cliRoot = Get-UnityCliInstallRoot
    if ($cliRoot) {
        foreach ($candidate in @(
                (Join-Path $cliRoot "$Version\Editor\Unity.exe"),
                (Join-Path $cliRoot "$Version\Unity.exe"))) {
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                if ($ManagedOnly -and -not (Test-IsPathInsideDirectory -Path $candidate -Directory $Root)) {
                    Write-CiNotice "Ignoring Unity $Version from CLI install root because it is outside the managed install root: $candidate"
                    continue
                }
                return (Resolve-Path -LiteralPath $candidate).Path
            }
        }
    }

    $byCandidate = Find-UnityEditor -Version $Version -Root $Root -IncludeHostInstalls:(-not $ManagedOnly)
    if ($byCandidate) {
        return $byCandidate
    }

    $byJson = Resolve-EditorFromCliJson -Version $Version
    if ($byJson) {
        if ($ManagedOnly -and -not (Test-IsPathInsideDirectory -Path $byJson -Directory $Root)) {
            Write-CiNotice "Ignoring Unity $Version from CLI editor inventory because it is outside the managed install root: $byJson"
            return $null
        }
        return $byJson
    }

    return $null
}

function Test-Il2CppModulePresent {
    # Disk-authoritative, best-effort probe for whether Windows IL2CPP support is
    # already installed for a resolved editor. The standalone CLI's
    # `install-modules` output is not a reliable success source by itself. Disk
    # evidence is the success proof we accept after a non-zero module install.
    #
    # IMPORTANT: the exact on-disk layout VARIES BY UNITY VERSION (and has shifted
    # across the 2020->6000 lineage), so this probes concrete STANDALONE-SPECIFIC
    # player leaves under known Windows IL2CPP variations. Empty directories are
    # not enough proof: failed or partial module installs have left folder shells
    # behind without a usable player/toolchain payload.
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

        $variationNames = @(
            'win64_player_development_il2cpp',
            'win64_player_nondevelopment_il2cpp'
        )
        foreach ($variationName in $variationNames) {
            $variation = Join-Path $standaloneVariations $variationName
            $leafCandidates = @(
                (Join-Path $variation 'WindowsPlayer.exe'),
                (Join-Path $variation 'UnityPlayer.dll'),
                (Join-Path $variation 'GameAssembly.dll')
            )
            foreach ($candidate in $leafCandidates) {
                if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                    return $true
                }
            }
            $payloadLeaves = @(
                Get-ChildItem -LiteralPath $variation -File -ErrorAction SilentlyContinue |
                    Where-Object { $_.Name -match '(?i)\.(dll|exe)$' }
            )
            if ($payloadLeaves.Count -gt 0) {
                return $true
            }
        }
    } catch {
        # Any unexpected probe error is non-fatal: treat as "inconclusive" (false).
        return $false
    }

    return $false
}

function Test-AnyUnityLeafPresent {
    param([Parameter(Mandatory = $true)][string[]]$Paths)

    foreach ($path in $Paths) {
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            return $true
        }
    }

    return $false
}

function Test-IsPathInsideDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Directory
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $fullDirectory = [System.IO.Path]::GetFullPath($Directory).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $isWindowsHost = [System.IO.Path]::DirectorySeparatorChar -eq '\'
    $comparison = if ($isWindowsHost -or $PSVersionTable.PSEdition -eq 'Desktop') {
        [System.StringComparison]::OrdinalIgnoreCase
    } else {
        [System.StringComparison]::Ordinal
    }

    return $fullPath.Equals($fullDirectory, $comparison) -or
        $fullPath.StartsWith($fullDirectory + [System.IO.Path]::DirectorySeparatorChar, $comparison) -or
        $fullPath.StartsWith($fullDirectory + [System.IO.Path]::AltDirectorySeparatorChar, $comparison)
}

function Get-UnityEditorInstallDirectory {
    param([Parameter(Mandatory = $true)][string]$EditorPath)

    $editorDir = Split-Path -Parent $EditorPath
    if (-not $editorDir) {
        return $null
    }

    $leaf = Split-Path -Leaf $editorDir
    if ($leaf -eq 'Editor') {
        return (Split-Path -Parent $editorDir)
    }

    return $editorDir
}

function Get-UnityCiModuleIds {
    # REQUESTED module ids passed to the standalone Unity CLI's `-m` install list.
    #
    # NOTE: this is intentionally DECOUPLED from Get-UnityCiVerifiedModuleGroups
    # (the on-disk verification switch). The two lists answer different questions:
    # "what do we ASK the CLI to install" vs. "what must we PROVE is on disk".
    #
    # OpenJDK is deliberately ABSENT here even though we verify it on disk. The
    # standalone beta CLI does not accept the bare id 'android-open-jdk' -- it
    # emits "Couldn't find module \"android-open-jdk\". Did you mean:
    # android-open-jdk-11.0.14.1+1" because its real id is VERSION-PINNED, and that
    # exact suffix drifts across Unity versions (hardcoding it would be brittle and
    # re-break on the next bump). OpenJDK is auto-added as a DEPENDENCY of
    # 'android-sdk-ndk-tools', so requesting that group brings OpenJDK along; we
    # then PROVE it landed via the 'android-open-jdk' disk group in
    # Get-UnityCiVerifiedModuleGroups. This removes the silent "Couldn't find
    # module" warning while keeping robust disk verification of OpenJDK.
    return @(
        'windows-il2cpp',
        'webgl',
        'android',
        'android-sdk-ndk-tools',
        'linux-mono',
        'linux-il2cpp'
    )
}

function Get-UnityCiVerifiedModuleGroups {
    # VERIFIED-on-disk module groups (the on-disk truth we require after any
    # install/repair). Iterated by Get-MissingUnityCiModuleGroups /
    # Test-UnityCiModuleGroupPresent. Decoupled from Get-UnityCiModuleIds (see the
    # note there): we verify 'android-open-jdk' on disk even though we never pass
    # that id to the CLI, because OpenJDK arrives as a dependency of
    # 'android-sdk-ndk-tools' and must be PROVEN present, not assumed.
    return @(
        'windows-il2cpp',
        'webgl',
        'android',
        'android-sdk-ndk-tools',
        'android-open-jdk',
        'linux-mono',
        'linux-il2cpp'
    )
}

function Get-UnityCliModuleInstallArguments {
    # SINGLE SOURCE OF TRUTH for the Unity-CLI module-install argument vector.
    # ALL THREE module-install call sites (top-level `install`, the repair-path
    # `install`, and the `install-modules` module-add) route through this so it is
    # structurally impossible for one to carry `--accept-eula` while another omits
    # it -- the exact drift that broke every CI cell.
    #
    # `--accept-eula` is MANDATORY (never optional): the Android SDK/NDK/OpenJDK
    # modules carry license terms, and without the flag the standalone CLI aborts
    # the ENTIRE install with "One or more modules require license acceptance. Pass
    # --accept-eula ...". The failing CI log proved the `install` verb emits that
    # message too, so the flag is valid for BOTH verbs handled here.
    #
    # Verb handling (preserving the resilient beta-CLI arg shapes already in use):
    #   install          -> @('install', <version>, '--accept-eula', '-m', <ids...>)
    #   install-modules  -> @('install-modules', '-e', <version>, '--accept-eula', '-m', <ids...>)
    # NOTE: this builder is for the EULA-bearing module INSTALL only. The `-l`
    # listing diagnostic and `editors`/`install-path` getters are NOT module
    # installs and must keep their own (EULA-free) shapes; do not route them here.
    #
    # StrictMode-safe: @()-wraps the module-id capture so an empty list never
    # collapses to AutomationNull, and uses array `+` concatenation only.
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('install', 'install-modules')]
        [string]$Verb,

        [Parameter(Mandatory = $true)]
        [string]$Version
    )

    $moduleIds = @(Get-UnityCiModuleIds)

    if ($Verb -eq 'install-modules') {
        # `install-modules` targets an EXISTING editor, so it needs `-e <version>`.
        return @('install-modules', '-e', $Version, '--accept-eula', '-m') + $moduleIds
    }

    # `install` provisions a fresh editor; the version is positional (no `-e`).
    return @('install', $Version, '--accept-eula', '-m') + $moduleIds
}

function Test-UnityCiModuleGroupPresent {
    param(
        [Parameter(Mandatory = $true)][string]$EditorPath,
        [Parameter(Mandatory = $true)][string]$Group
    )

    if (-not $EditorPath) {
        return $false
    }

    try {
        $editorDir = Split-Path -Parent $EditorPath
        if (-not $editorDir) {
            return $false
        }

        $dataRoot = Join-Path $editorDir 'Data'
        switch ($Group) {
            'windows-il2cpp' {
                return Test-Il2CppModulePresent -EditorPath $EditorPath
            }
            'webgl' {
                $webGlRoot = Join-Path $dataRoot 'PlaybackEngines\WebGLSupport'
                $hasEditorExtension = Test-Path -LiteralPath (Join-Path $webGlRoot 'UnityEditor.WebGL.Extensions.dll') -PathType Leaf
                $hasEmscriptenToolchain = Test-AnyUnityLeafPresent -Paths @(
                    (Join-Path $webGlRoot 'BuildTools\Emscripten\emscripten\emscripten-version.txt'),
                    (Join-Path $webGlRoot 'BuildTools\Emscripten\emscripten\emcc.py'),
                    (Join-Path $webGlRoot 'BuildTools\Emscripten\emscripten-version.txt'),
                    (Join-Path $webGlRoot 'BuildTools\Emscripten\emcc.py')
                )
                return $hasEditorExtension -and $hasEmscriptenToolchain
            }
            'android' {
                $androidRoot = Join-Path $dataRoot 'PlaybackEngines\AndroidPlayer'
                return Test-AnyUnityLeafPresent -Paths @(
                    (Join-Path $androidRoot 'UnityEditor.Android.Extensions.dll'),
                    (Join-Path $androidRoot 'Tools\Source.properties')
                )
            }
            'android-sdk-ndk-tools' {
                $androidRoot = Join-Path $dataRoot 'PlaybackEngines\AndroidPlayer'
                $sdk = Join-Path $androidRoot 'SDK'
                $ndk = Join-Path $androidRoot 'NDK'
                $hasAdb = Test-AnyUnityLeafPresent -Paths @(
                    (Join-Path $sdk 'platform-tools\adb.exe'),
                    (Join-Path $sdk 'platform-tools\adb')
                )
                $hasNdkProperties = Test-Path -LiteralPath (Join-Path $ndk 'source.properties') -PathType Leaf
                $llvmRoot = Join-Path $ndk 'toolchains\llvm\prebuilt'
                $hasLlvmClang = $false
                if (Test-Path -LiteralPath $llvmRoot -PathType Container) {
                    $clangLeaves = @(
                        Get-ChildItem -LiteralPath $llvmRoot -Recurse -File -ErrorAction SilentlyContinue |
                            Where-Object { $_.Name -in @('clang++', 'clang++.exe') } |
                            Select-Object -First 1
                    )
                    $hasLlvmClang = $clangLeaves.Count -gt 0
                }
                return $hasAdb -and $hasNdkProperties -and $hasLlvmClang
            }
            'android-open-jdk' {
                $androidRoot = Join-Path $dataRoot 'PlaybackEngines\AndroidPlayer'
                return Test-AnyUnityLeafPresent -Paths @(
                    (Join-Path $androidRoot 'OpenJDK\bin\java.exe'),
                    (Join-Path $androidRoot 'OpenJDK\bin\java')
                )
            }
            'linux-mono' {
                $linuxRoot = Join-Path $dataRoot 'PlaybackEngines\LinuxStandaloneSupport'
                $variationRoot = Join-Path $linuxRoot 'Variations'
                return Test-AnyUnityLeafPresent -Paths @(
                    (Join-Path $variationRoot 'linux64_player_development_mono\LinuxPlayer'),
                    (Join-Path $variationRoot 'linux64_player_development_mono\UnityPlayer.so'),
                    (Join-Path $variationRoot 'linux64_player_nondevelopment_mono\LinuxPlayer'),
                    (Join-Path $variationRoot 'linux64_player_nondevelopment_mono\UnityPlayer.so')
                )
            }
            'linux-il2cpp' {
                $linuxRoot = Join-Path $dataRoot 'PlaybackEngines\LinuxStandaloneSupport'
                $variationRoot = Join-Path $linuxRoot 'Variations'
                return Test-AnyUnityLeafPresent -Paths @(
                    (Join-Path $variationRoot 'linux64_player_development_il2cpp\LinuxPlayer'),
                    (Join-Path $variationRoot 'linux64_player_development_il2cpp\UnityPlayer.so'),
                    (Join-Path $variationRoot 'linux64_player_nondevelopment_il2cpp\LinuxPlayer'),
                    (Join-Path $variationRoot 'linux64_player_nondevelopment_il2cpp\UnityPlayer.so')
                )
            }
            default {
                throw "Unknown Unity CI module group '$Group'."
            }
        }
    } catch {
        return $false
    }
}

function Get-MissingUnityCiModuleGroups {
    param([Parameter(Mandatory = $true)][string]$EditorPath)

    $missing = New-Object System.Collections.Generic.List[string]
    foreach ($group in @(Get-UnityCiVerifiedModuleGroups)) {
        if (-not (Test-UnityCiModuleGroupPresent -EditorPath $EditorPath -Group $group)) {
            $missing.Add($group)
        }
    }

    return @($missing.ToArray())
}

function Test-UnityCiModulesPresent {
    param([Parameter(Mandatory = $true)][string]$EditorPath)

    $missing = @(Get-MissingUnityCiModuleGroups -EditorPath $EditorPath)
    return ($missing.Count -eq 0)
}

function Move-UnityInstallDirectoryToQuarantine {
    param(
        [Parameter(Mandatory = $true)][string]$InstallDirectory,
        [Parameter(Mandatory = $true)][string]$InstallRoot,
        [Parameter(Mandatory = $true)][string]$Version
    )

    if (-not $InstallDirectory -or -not (Test-Path -LiteralPath $InstallDirectory -PathType Container)) {
        return
    }

    if (-not (Test-IsPathInsideDirectory -Path $InstallDirectory -Directory $InstallRoot)) {
        throw "Refusing to auto-repair Unity $Version because the resolved editor install '$InstallDirectory' is outside the configured managed install root '$InstallRoot'. Remove or reinstall that editor manually, or set UNITY_EDITOR_INSTALL_ROOT to a CI-owned directory."
    }

    $quarantineRoot = Join-Path $InstallRoot '_quarantine'
    New-Item -ItemType Directory -Force -Path $quarantineRoot | Out-Null
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $suffix = [Guid]::NewGuid().ToString('N').Substring(0, 8)
    $destination = Join-Path $quarantineRoot "$Version-$stamp-$suffix"

    Write-Host "::warning::Quarantining unmanaged or partial Unity $Version install before repair: $InstallDirectory -> $destination"
    # Move-Item against a Unity editor directory can fail transiently on Windows
    # with "The process cannot access the file '...' because it is being used by
    # another process." when Unity, an antivirus scanner, or the Windows indexer
    # still holds a handle on a file under the tree. Retry the move with backoff
    # so a momentary lock does not abort the whole repair; Invoke-WithRetry emits
    # a per-attempt ::warning:: and RETHROWS the last error if every attempt
    # fails, so a genuinely stuck directory still aborts loudly. Class rule: any
    # destructive dir op (Move/Remove/Rename) on a transiently-lockable Unity
    # editor directory on Windows goes through this retry helper.
    Invoke-WithRetry -MaxAttempts 3 -DelaySeconds (Get-EnsureEditorRetryDelaySeconds) -Action {
        Move-Item -LiteralPath $InstallDirectory -Destination $destination -Force
    } | Out-Null
}

function Move-UnityEditorInstallToQuarantine {
    param(
        [Parameter(Mandatory = $true)][string]$EditorPath,
        [Parameter(Mandatory = $true)][string]$InstallRoot,
        [Parameter(Mandatory = $true)][string]$Version
    )

    $installDirectory = Get-UnityEditorInstallDirectory -EditorPath $EditorPath
    if (-not $installDirectory) {
        return
    }

    Move-UnityInstallDirectoryToQuarantine -InstallDirectory $installDirectory -InstallRoot $InstallRoot -Version $Version
}

function Move-UnityVersionInstallToQuarantine {
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$InstallRoot
    )

    $candidateRoots = New-Object System.Collections.Generic.List[string]
    $candidateRoots.Add($InstallRoot)
    try {
        $cliRoot = Get-UnityCliInstallRoot
        if ($cliRoot -and (Test-IsPathInsideDirectory -Path $cliRoot -Directory $InstallRoot)) {
            $candidateRoots.Add($cliRoot)
        }
    } catch {
        Write-Host "::notice::Could not query Unity CLI install root while quarantining Unity ${Version}: $($_.Exception.Message)"
    }

    $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($root in @($candidateRoots.ToArray())) {
        if (-not $root) {
            continue
        }
        $installDirectory = Join-Path $root $Version
        $full = [System.IO.Path]::GetFullPath($installDirectory)
        if ($seen.Add($full)) {
            Move-UnityInstallDirectoryToQuarantine -InstallDirectory $full -InstallRoot $InstallRoot -Version $Version
        }
    }
}

function Invoke-UnityVersionUninstallForRepair {
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$Reason
    )

    $uninstallResult = Invoke-UnityCliCapture -Arguments @('uninstall', $Version)
    if (-not $uninstallResult.Success) {
        $uninstallLines = @($uninstallResult.Output)
        $tailCount = [Math]::Min(12, $uninstallLines.Count)
        $tail = if ($tailCount -gt 0) {
            ($uninstallLines[($uninstallLines.Count - $tailCount)..($uninstallLines.Count - 1)] -join "`n")
        } else {
            '(no output captured)'
        }
        Write-CiNotice "Unity CLI uninstall for $Version did not complete cleanly before repair (exit code $($uninstallResult.ExitCode)); quarantining the install directory instead. Reason: $Reason Output tail:`n$tail"
    }
}

function Install-UnityEditorWithCiModules {
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$InstallRoot,
        [Parameter(Mandatory = $true)][string]$Reason,
        [switch]$ManagedOnly
    )

    $moduleIds = @(Get-UnityCiModuleIds)
    if ($ManagedOnly) {
        Confirm-UnityCliManagedInstallRoot -Root $InstallRoot | Out-Null
    }
    Write-CiNotice "Repairing Unity $Version by installing a fresh CLI-managed editor with CI modules ($($moduleIds -join ', ')). Reason: $Reason"

    # Single source of truth for the (EULA-bearing) module-install arg vector.
    $installArgs = @(Get-UnityCliModuleInstallArguments -Verb 'install' -Version $Version)

    $resolved = $null
    for ($attempt = 1; $attempt -le 2; $attempt++) {
        $installResult = Invoke-UnityCliCapture -Arguments $installArgs
        if ($installResult.Success) {
            $resolved = Resolve-InstalledEditor -Version $Version -Root $InstallRoot -ManagedOnly:$ManagedOnly
            if ($resolved) {
                break
            }
            if ($attempt -lt 2) {
                Write-InstalledEditorDiagnostics -Version $Version -Root $InstallRoot -Reason "Unity repair install exited 0, but Unity.exe could not be resolved afterward."
                Invoke-UnityVersionUninstallForRepair -Version $Version -Reason "Unity repair install exited 0, but Unity.exe could not be resolved afterward."
                Move-UnityVersionInstallToQuarantine -Version $Version -InstallRoot $InstallRoot
                Write-Host "::warning::Retrying Unity $Version repair install after successful CLI install left no resolvable Unity.exe."
                continue
            }
            break
        }

        $installLines = @($installResult.Output)
        $installText = ($installLines -join "`n")
        $tailCount = [Math]::Min(40, $installLines.Count)
        $tail = if ($tailCount -gt 0) {
            ($installLines[($installLines.Count - $tailCount)..($installLines.Count - 1)] -join "`n")
        } else {
            '(no output captured)'
        }
        $resolvedAfterFailure = Resolve-InstalledEditor -Version $Version -Root $InstallRoot -ManagedOnly:$ManagedOnly
        if ($installText -match '(?i)already installed|editor already installed|is already installed') {
            if ($resolvedAfterFailure) {
                Write-CiNotice "Unity repair install for $Version reported already-installed with exit code $($installResult.ExitCode), but Unity.exe is resolvable afterward; verifying modules against disk."
                $resolved = $resolvedAfterFailure
                break
            }

            if ($attempt -lt 2) {
                Write-InstalledEditorDiagnostics -Version $Version -Root $InstallRoot -Reason "Unity repair install reported already-installed, but Unity.exe could not be resolved afterward."
                Invoke-UnityVersionUninstallForRepair -Version $Version -Reason "Unity repair install reported already-installed, but Unity.exe could not be resolved."
                Move-UnityVersionInstallToQuarantine -Version $Version -InstallRoot $InstallRoot
                Write-Host "::warning::Retrying Unity $Version repair install after clearing stale CLI metadata and quarantining the managed version directory."
                continue
            }
        }

        Write-UnityCliInstallFailureAnnotation -Version $Version -Output $installResult.Output -ExitCode $installResult.ExitCode -Arguments $installArgs
        Write-InstalledEditorDiagnostics -Version $Version -Root $InstallRoot -Reason "Unity repair install failed."
        throw "Unity $Version repair install with CI modules failed with exit code $($installResult.ExitCode). CLI output tail:`n$tail"
    }

    if (-not $resolved) {
        Write-InstalledEditorDiagnostics -Version $Version -Root $InstallRoot -Reason "Unity repair install completed, but Unity.exe could not be resolved afterward."
        throw "Unity $Version repair install completed, but Unity.exe could not be found afterward."
    }

    $missing = @(Get-MissingUnityCiModuleGroups -EditorPath $resolved)
    if ($missing.Count -gt 0) {
        throw "Unity $Version repair install completed at '$resolved', but required CI module groups are still missing on disk: $($missing -join ', ')."
    }

    return $resolved
}

function Repair-UnityEditorWithCiModules {
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$EditorPath,
        [Parameter(Mandatory = $true)][string]$InstallRoot,
        [Parameter(Mandatory = $true)][string]$Reason,
        [switch]$ManagedOnly
    )

    return Invoke-WithUnityInstallLock -Version $Version -InstallRoot $InstallRoot -Action {
        if ($ManagedOnly) {
            Confirm-UnityCliManagedInstallRoot -Root $InstallRoot | Out-Null
        }
        Invoke-UnityVersionUninstallForRepair -Version $Version -Reason $Reason

        if (Test-Path -LiteralPath $EditorPath -PathType Leaf) {
            Move-UnityEditorInstallToQuarantine -EditorPath $EditorPath -InstallRoot $InstallRoot -Version $Version
        }
        Move-UnityVersionInstallToQuarantine -Version $Version -InstallRoot $InstallRoot

        return Install-UnityEditorWithCiModules -Version $Version -InstallRoot $InstallRoot -Reason $Reason -ManagedOnly:$ManagedOnly
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
    if ($normalized -eq 0x8007007E) {
        return "$hex / ERROR_MOD_NOT_FOUND"
    }

    return $hex
}

function Test-UnityNativeStartup {
    param(
        [Parameter(Mandatory = $true)][string]$EditorPath,
        [Parameter(Mandatory = $true)][string]$LogPath
    )

    $logDir = Split-Path -Parent $LogPath
    if ($logDir -and -not (Test-Path -LiteralPath $logDir -PathType Container)) {
        New-Item -ItemType Directory -Force -Path $logDir | Out-Null
    }

    $probeArgs = @(
        '-version',
        '-batchmode',
        '-nographics',
        '-quit',
        '-logFile', '-'
    )

    Write-Host "::group::Unity editor startup provisioning probe"
    Write-Host "`"$EditorPath`" $($probeArgs -join ' ')"
    & $EditorPath @probeArgs 2>&1 |
        Tee-Object -FilePath $LogPath |
        ForEach-Object { Write-Host ([string]$_) }
    $exitCode = $LASTEXITCODE
    $description = Get-NativeExitCodeDescription -ExitCode $exitCode
    Write-Host "Unity startup provisioning probe exit code: $exitCode ($description)"
    Write-Host "::endgroup::"

    return [pscustomobject]@{
        Success     = ($exitCode -eq 0)
        ExitCode    = $exitCode
        Description = $description
    }
}

function Ensure-UnityNativeStartupHealthy {
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$EditorPath,
        [Parameter(Mandatory = $true)][string]$InstallRoot,
        [switch]$ManagedOnly
    )

    # Test harnesses spawn ensure-editor.ps1 against a stub Unity.exe; Windows CreateProcess refuses non-PE .exe, so opt-in skip the probe.
    if ($env:DXM_UNITY_SKIP_NATIVE_STARTUP_PROBE -eq '1') {
        Write-CiNotice "Skipping Unity $Version native startup probe (DXM_UNITY_SKIP_NATIVE_STARTUP_PROBE=1)."
        return $EditorPath
    }

    $probeRoot = Join-Path $InstallRoot '_probes'
    $probeLog = Join-Path $probeRoot "$Version-startup-probe.log"
    $result = Test-UnityNativeStartup -EditorPath $EditorPath -LogPath $probeLog
    if ($result.Success) {
        return $EditorPath
    }

    if ($env:DXM_UNITY_DISABLE_EDITOR_REPAIR -eq '1') {
        throw "Unity $Version native startup probe failed with exit code $($result.ExitCode) ($($result.Description)), and DXM_UNITY_DISABLE_EDITOR_REPAIR=1 disabled auto-repair. Probe log: $probeLog"
    }

    Write-Host "::warning::Unity $Version native startup probe failed before the license lock; attempting one managed reinstall."
    $repaired = Repair-UnityEditorWithCiModules -Version $Version -EditorPath $EditorPath -InstallRoot $InstallRoot -Reason "native startup probe failed with exit code $($result.ExitCode) ($($result.Description)). Probe log: $probeLog" -ManagedOnly:$ManagedOnly
    $repairProbe = Test-UnityNativeStartup -EditorPath $repaired -LogPath $probeLog
    if (-not $repairProbe.Success) {
        throw "Unity $Version native startup probe still failed after managed reinstall with exit code $($repairProbe.ExitCode) ($($repairProbe.Description)). This indicates host OS/runtime prerequisite damage rather than a package/test issue. Probe log: $probeLog"
    }

    return $repaired
}

function Ensure-UnityCiModules {
    # IDEMPOTENT, disk-authoritative CI module install. The standalone beta CLI can
    # return "No modules found to install." with exit code 6 when modules are
    # already present, and it cannot add modules to manually installed editors.
    # Classify the result against disk proof first; if required module groups are
    # missing, repair by quarantining the managed editor and reinstalling through
    # the CLI with the full CI module set.
    #   1. install succeeded (exit 0)                              -> done.
    #   2. install failed BUT every module group is present on disk -> notice, return.
    #   3. modules are missing and repair is enabled                -> quarantine/reinstall.
    #   4. repair disabled or impossible                            -> throw with diagnostics.
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$EditorPath,
        [string]$InstallRoot,
        [switch]$ManagedOnly
    )

    $moduleIds = @(Get-UnityCiModuleIds)

    if ($ManagedOnly) {
        Confirm-UnityCliManagedInstallRoot -Root $InstallRoot | Out-Null
    }

    # Best-effort listing diagnostic (unchanged): the beta listing format may not
    # contain every literal module id, so a mismatch only warns and never aborts.
    $listLines = @(Get-UnityCliOutput -Arguments @('install-modules', '-e', $Version, '-l'))
    if ($listLines.Count -gt 0) {
        $listText = ($listLines -join "`n")
        $missingFromList = @($moduleIds | Where-Object { $listText -notmatch [regex]::Escape($_) })
        if ($missingFromList.Count -gt 0) {
            Write-Host "::warning::Unity $Version module listing did not contain every required CI module id ($($missingFromList -join ', ')). Proceeding; the install result below is classified against the on-disk module layout."
        }
    } else {
        Write-CiNotice "Could not list installable modules for Unity $Version (best-effort); proceeding with required CI module ids: $($moduleIds -join ', ')."
    }

    # Single source of truth for the (EULA-bearing) `install-modules` arg vector --
    # captured ONCE here and reused for both the install call below and the failure
    # annotation's arg echo, mirroring how the `install` paths capture $installArgs
    # once. The vector (including the MANDATORY `--accept-eula`) comes from
    # Get-UnityCliModuleInstallArguments, so this `install-modules` call can never
    # drift from the `install` call sites. `--accept-eula` is REQUIRED: the Android
    # SDK/NDK/OpenJDK modules carry license terms, and without the flag the
    # standalone CLI aborts the whole install with "One or more modules require
    # license acceptance. Pass --accept-eula ...". It applies only to this INSTALL
    # (`-m`) call, never to the `-l` listing call above.
    $installArgs = @(Get-UnityCliModuleInstallArguments -Verb 'install-modules' -Version $Version)

    # Attempt the install via the capturing (non-throwing) path so we can inspect
    # BOTH the exit code AND the output text before deciding whether it was fatal.
    $result = Invoke-UnityCliCapture -Arguments $installArgs

    # Case 1: clean success.
    if ($result.Success) {
        $missingAfterSuccess = @(Get-MissingUnityCiModuleGroups -EditorPath $EditorPath)
        if ($missingAfterSuccess.Count -eq 0) {
            return $EditorPath
        }
        Write-Host "::warning::Unity $Version 'install-modules' exited 0, but required CI module groups are still missing on disk: $($missingAfterSuccess -join ', ')."
    }

    $outputLines = @($result.Output)
    $outputText = ($outputLines -join "`n")
    # Tail of the captured output for diagnostics (last lines only, to keep the
    # thrown message readable).
    $tailCount = [Math]::Min(20, $outputLines.Count)
    $tail = if ($tailCount -gt 0) { ($outputLines[($outputLines.Count - $tailCount)..($outputLines.Count - 1)] -join "`n") } else { '(no output captured)' }

    # Case 2: install failed but every module group is demonstrably present on disk -> the
    # CLI's non-zero exit was an idempotent no-op. Treat as success.
    $missingGroups = @(Get-MissingUnityCiModuleGroups -EditorPath $EditorPath)
    if ($missingGroups.Count -eq 0) {
        Write-CiNotice "Required Unity CI modules already present on disk; treating 'install-modules' no-op as success (CLI exit code $($result.ExitCode))."
        return $EditorPath
    }

    # The install genuinely did not deliver the required modules. Emit a targeted,
    # high-signal annotation if the CLI output carries a known failure signature
    # (missing EULA / unknown module id) BEFORE we repair or throw, so the root
    # cause is obvious in the CI log.
    Write-UnityCliInstallFailureAnnotation -Version $Version -Output $result.Output -ExitCode $result.ExitCode -Arguments $installArgs

    $repairDisabled = $env:DXM_UNITY_DISABLE_EDITOR_REPAIR -eq '1'
    if ($repairDisabled) {
        throw "Unity $Version is missing required CI module groups ($($missingGroups -join ', ')), and DXM_UNITY_DISABLE_EDITOR_REPAIR=1 disabled auto-repair. CLI output tail:`n$tail"
    }

    if ($InstallRoot) {
        return Repair-UnityEditorWithCiModules -Version $Version -EditorPath $EditorPath -InstallRoot $InstallRoot -Reason "required CI module groups missing ($($missingGroups -join ', ')). CLI output tail:`n$tail" -ManagedOnly:$ManagedOnly
    }

    # Case 3: genuine, non-benign failure. Fatal -- include the captured output
    # tail and exit code so CI logs show WHY the module install failed.
    throw "Unity $Version 'install-modules' failed with exit code $($result.ExitCode), and required CI module groups are missing on disk ($($missingGroups -join ', ')). CLI output tail:`n$tail"
}

function Add-WindowsIl2CppModule {
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$EditorPath,
        [string]$InstallRoot,
        [switch]$ManagedOnly
    )

    return Ensure-UnityCiModules -Version $Version -EditorPath $EditorPath -InstallRoot $InstallRoot -ManagedOnly:$ManagedOnly
}

function Write-InstalledEditorDiagnostics {
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Reason
    )

    Write-Host "::group::Unity editor resolution diagnostics"
    Write-Host "Reason: $Reason"
    Write-Host "Requested Unity version: $Version"
    Write-Host "Configured install root: $Root"
    try {
        $cliRoot = Get-UnityCliInstallRoot
        if ($cliRoot) {
            Write-Host "Unity CLI reported install root: $cliRoot"
        } else {
            Write-Host "Unity CLI reported install root: (unavailable)"
        }
    } catch {
        Write-Host "::notice::Could not query Unity CLI install root: $($_.Exception.Message)"
    }

    try {
        Write-Host "Known Unity.exe candidate paths:"
        foreach ($candidate in Get-UnityEditorCandidates -Version $Version -Root $Root) {
            $exists = Test-Path -LiteralPath $candidate -PathType Leaf
            Write-Host "  [$exists] $candidate"
        }
    } catch {
        Write-Host "::notice::Could not enumerate Unity.exe candidates: $($_.Exception.Message)"
    }

    try {
        Write-Host "Installed Unity editors reported by CLI:"
        Invoke-UnityCliSafe -Arguments @('editors', '-i') | Out-Null
    } catch {
        Write-Host "::notice::Could not query installed Unity editors: $($_.Exception.Message)"
    }
    Write-Host "::endgroup::"
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

$editor = Find-UnityEditor -Version $UnityVersion -Root $InstallRoot -IncludeHostInstalls:(-not $CiManagedOnly)
if (-not $editor) {
    Ensure-UnityCli | Out-Null
    Set-UnityCliInstallPath -Root $InstallRoot
    if ($CiManagedOnly) {
        Confirm-UnityCliManagedInstallRoot -Root $InstallRoot | Out-Null
    }

    Write-CiNotice "Installing Unity Editor $UnityVersion on the self-hosted Windows runner."
    # Single source of truth for the (EULA-bearing) module-install arg vector --
    # the SAME builder the repair-path `install` and the `install-modules` add use,
    # so this primary install can never silently drop `--accept-eula` again (the
    # exact drift that broke every CI cell).
    $installArgs = @(Get-UnityCliModuleInstallArguments -Verb 'install' -Version $UnityVersion)

    # Emit diagnostics BEFORE the (potentially 30+ minute) install so the logs
    # carry the CLI path/version + disk headroom even if the install then stalls.
    Write-InstallDiagnostics -Root $InstallRoot

    # The base install has been observed to fail flakily (exit 6 after a long run
    # with almost no output). Retry once via Invoke-WithRetry (two attempts fit
    # inside the 120-minute step budget even for a slow install), and use the
    # CAPTURING invoker so a final failure THROWS with the CLI output tail + exit
    # code -- the previous failure surfaced no actionable diagnostics. Output is
    # still streamed live by Invoke-UnityCliCapture, never silently buffered.
    $retryDelaySeconds = Get-EnsureEditorRetryDelaySeconds

    $recoveredEditor = Invoke-WithUnityInstallLock -Version $UnityVersion -InstallRoot $InstallRoot -Action {
        Invoke-WithRetry -MaxAttempts 2 -DelaySeconds $retryDelaySeconds -Action {
            $installResult = Invoke-UnityCliCapture -Arguments $installArgs
            if (-not $installResult.Success) {
                $installLines = @($installResult.Output)
                $installText = ($installLines -join "`n")
                $installTailCount = [Math]::Min(40, $installLines.Count)
                $installTail = if ($installTailCount -gt 0) {
                    ($installLines[($installLines.Count - $installTailCount)..($installLines.Count - 1)] -join "`n")
                } else {
                    '(no output captured)'
                }

                $resolvedAfterFailure = Resolve-InstalledEditor -Version $UnityVersion -Root $InstallRoot -ManagedOnly:$CiManagedOnly
                if ($resolvedAfterFailure) {
                    Write-CiNotice "Unity CLI '$($installArgs -join ' ')' failed with exit code $($installResult.ExitCode), but Unity.exe is resolvable afterward; treating the install as already present."
                    Write-CiNotice "Verifying required CI modules after recovered editor install."
                    $resolvedAfterFailure = Ensure-UnityCiModules -Version $UnityVersion -EditorPath $resolvedAfterFailure -InstallRoot $InstallRoot -ManagedOnly:$CiManagedOnly
                    return $resolvedAfterFailure
                }

                if ($installText -match '(?i)already installed|editor already installed|is already installed') {
                    Write-InstalledEditorDiagnostics -Version $UnityVersion -Root $InstallRoot -Reason "Unity CLI reported the editor is already installed, but Unity.exe could not be resolved afterward."
                    Invoke-UnityVersionUninstallForRepair -Version $UnityVersion -Reason "Unity CLI reported an already-installed editor, but Unity.exe could not be resolved."
                    Move-UnityVersionInstallToQuarantine -Version $UnityVersion -InstallRoot $InstallRoot
                    throw "Unity CLI '$($installArgs -join ' ')' reported an already-installed editor with exit code $($installResult.ExitCode), but Unity.exe could not be found. Uninstalled any CLI metadata and quarantined the managed version directory as partial or corrupt before retry. CLI output tail:`n$installTail"
                }

                Write-UnityCliInstallFailureAnnotation -Version $UnityVersion -Output $installResult.Output -ExitCode $installResult.ExitCode -Arguments $installArgs
                Write-InstalledEditorDiagnostics -Version $UnityVersion -Root $InstallRoot -Reason "Unity CLI install failed and Unity.exe could not be resolved afterward."
                throw "Unity CLI '$($installArgs -join ' ')' failed with exit code $($installResult.ExitCode). CLI output tail:`n$installTail"
            }

            return $null
        }
    }

    if ($recoveredEditor) {
        $editor = $recoveredEditor
    } else {
        $editor = Resolve-InstalledEditor -Version $UnityVersion -Root $InstallRoot -ManagedOnly:$CiManagedOnly
    }
    if (-not $editor) {
        Write-InstalledEditorDiagnostics -Version $UnityVersion -Root $InstallRoot -Reason "Unity CLI install completed, but Unity.exe could not be resolved afterward."
        Move-UnityVersionInstallToQuarantine -Version $UnityVersion -InstallRoot $InstallRoot
        $editor = Install-UnityEditorWithCiModules -Version $UnityVersion -InstallRoot $InstallRoot -Reason "Unity CLI install completed, but Unity.exe could not be resolved afterward; quarantined the managed version directory and retrying with a fresh install." -ManagedOnly:$CiManagedOnly
    }
    $editor = Ensure-UnityCiModules -Version $UnityVersion -EditorPath $editor -InstallRoot $InstallRoot -ManagedOnly:$CiManagedOnly
} else {
    Ensure-UnityCli | Out-Null
    Set-UnityCliInstallPath -Root $InstallRoot
    if ($CiManagedOnly) {
        Confirm-UnityCliManagedInstallRoot -Root $InstallRoot | Out-Null
    }
    Write-CiNotice "Ensuring required CI modules are installed for Unity $UnityVersion."
    $editor = Ensure-UnityCiModules -Version $UnityVersion -EditorPath $editor -InstallRoot $InstallRoot -ManagedOnly:$CiManagedOnly
}

$editor = Ensure-UnityNativeStartupHealthy -Version $UnityVersion -EditorPath $editor -InstallRoot $InstallRoot -ManagedOnly:$CiManagedOnly
Write-CiNotice "Unity editor resolved: $editor"
Write-Output $editor
