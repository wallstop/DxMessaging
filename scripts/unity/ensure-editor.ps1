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

# pwsh 7.4+ defaults $PSNativeCommandUseErrorActionPreference to $true, which makes
# `& <native>` THROW on a non-zero exit BEFORE our `if ($LASTEXITCODE -ne 0)` check
# runs. That would make Invoke-UnityCli's custom path+args diagnostic dead code, and
# would make the best-effort invokers rely on their catch block instead of the
# explicit exit check. Pinning it $false makes LASTEXITCODE-based handling
# authoritative and identical across pwsh versions. (PS 5.1 lacks this variable;
# assigning it there is harmless, and the assignment is StrictMode-safe.)
$PSNativeCommandUseErrorActionPreference = $false

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

function Invoke-UnityCli {
    # THROWING invoker: use only for commands whose failure is fatal to the
    # bootstrap (the editor install itself). Echoes the resolved CLI path so
    # logs reflect the source that actually ran (PATH entry or absolute
    # fallback), not a misleading literal `unity`.
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    Write-Host "$script:UnityCliPath $($Arguments -join ' ')"
    & $script:UnityCliPath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Unity CLI command failed with exit code ${LASTEXITCODE}: $script:UnityCliPath $($Arguments -join ' ')"
    }
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

function Add-WindowsIl2CppModule {
    # IL2CPP module resilience for standalone. Consult
    # `unity install-modules -e <version> -l` (the documented list flag) before
    # installing as a best-effort sanity check only: the authoritative
    # `unity install-modules -e <v> -m windows-il2cpp` below is the source of
    # truth (and is fatal on real failure). The listing format/display name on a
    # beta CLI may not contain the literal module id, so a mismatch must NOT abort
    # a standalone run on its own -- emit a ::warning:: and CONTINUE to the
    # install. If the listing is unavailable (beta flag drift), proceed
    # optimistically with the standard Unity Hub module id. The install MUST
    # succeed for standalone, so its failure throws.
    param([Parameter(Mandatory = $true)][string]$Version)

    $moduleId = 'windows-il2cpp'

    $listLines = Get-UnityCliOutput -Arguments @('install-modules', '-e', $Version, '-l')
    if ($listLines) {
        $listText = ($listLines -join "`n")
        if ($listText -notmatch [regex]::Escape($moduleId)) {
            Write-Host "::warning::Unity $Version module listing did not contain the literal id '$moduleId' (the beta CLI may use a different listing format/display name). Proceeding because the install-modules call below is authoritative and fatal on real failure."
        }
    } else {
        Write-CiNotice "Could not list installable modules for Unity $Version (best-effort); proceeding with the standard module id '$moduleId'."
    }

    # Fatal on failure: standalone requires IL2CPP. Use the THROWING invoker so a
    # genuine inability to install the module fails the job loudly.
    Invoke-UnityCli -Arguments @('install-modules', '-e', $Version, '-m', $moduleId)
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
    Add-WindowsIl2CppModule -Version $UnityVersion
}

Write-CiNotice "Unity editor resolved: $editor"
Write-Output $editor
