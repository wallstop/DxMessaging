---
title: "Unity Editor CLI Bootstrap"
id: "unity-editor-cli-bootstrap"
category: "unity"
version: "1.1.1"
created: "2026-05-20"
updated: "2026-05-21"

source:
  repository: "Ambiguous-Interactive/DxMessaging"
  files:
    - path: "scripts/unity/ensure-editor.ps1"
  url: "https://github.com/Ambiguous-Interactive/DxMessaging"

tags:
  - "unity"
  - "cli"
  - "powershell"
  - "path"
  - "bootstrap"
  - "self-hosted"

complexity:
  level: "intermediate"
  reasoning: "Requires understanding Windows registry PATH scopes plus the getter-vs-setter surface of a beta Unity CLI whose flags are not fully documented."

impact:
  performance:
    rating: "none"
    details: "One-time per-runner installer cost; does not affect steady-state run time"
  maintainability:
    rating: "high"
    details: "Centralizes session-PATH refresh, a best-effort invoker, and getter-based discovery so a beta CLI flag drift cannot break Unity bootstrap"
  testability:
    rating: "medium"
    details: "powershell-syntax.test.js reparses the script; unity-runner-script-contract.test.js pins the PATH and resilience tokens"

prerequisites:
  - "Familiarity with Windows PowerShell 5.1 and Set-StrictMode"
  - "Awareness of Windows Machine vs User registry PATH scopes"

dependencies:
  packages: []
  skills:
    - "unity-ci-matrix"

applies_to:
  languages:
    - "PowerShell"
  frameworks:
    - "Unity"
  versions:
    unity: ">=2021.3"

aliases:
  - "Unity CLI PATH refresh"
  - "ensure-editor bootstrap"

related:
  - "unity-ci-matrix"
  - "headless-test-runner"
  - "unity-license-bootstrap"

status: "stable"
---

<!-- trigger: unity, cli, path, bootstrap, ensure-editor, powershell, install-path | Standalone Unity CLI install + session PATH refresh + getter-based discovery on self-hosted runners | Core -->

# Unity Editor CLI Bootstrap

> **One-line summary**: `scripts/unity/ensure-editor.ps1` installs the standalone Unity CLI on a self-hosted Windows runner, refreshes the session `$env:PATH` from the registry, and discovers the installed editor through a best-effort SET plus a getter-based resolver so a beta CLI with uncertain flags cannot break the bootstrap.

## Overview

The active Unity workflows run directly on self-hosted Windows runners and use the standalone Unity CLI (`unity`) to install editors and modules on demand. `scripts/unity/ensure-editor.ps1` is the bootstrap: if `unity` is not already on PATH it downloads and runs the official installer, then installs the requested editor version and (for standalone IL2CPP) the `windows-il2cpp` module.

The standalone CLI is a moving beta surface (`v0.1.0-beta.x`). Some flags are undocumented and differ between releases, so the script treats every optional operation as best-effort and never lets an uncertain flag abort the install. Two failure modes drove the current design: the installer leaves `unity` off the current session PATH, and `unity install-path` was once called with a positional directory argument, which the CLI rejected.

The script is parsed cross-platform: `scripts/__tests__/powershell-syntax.test.js` reparses the `.ps1` on Linux `pwsh`, so it must stay valid PowerShell 5.1 under `Set-StrictMode -Version Latest` even though the Windows-only registry calls never execute on Linux.

## Problem Statement

Two distinct problems, both fatal to a naive bootstrap:

1. **Session PATH lag.** The installer at `https://public-cdn.cloud.unity3d.com/hub/prod/cli/install.ps1` writes `%LOCALAPPDATA%\Unity\bin\unity.exe` and appends that directory to the User-scope registry PATH only. A registry write does not mutate the running process `$env:PATH`, so `Get-Command unity` immediately after install still fails even on a perfect install. The registry write can also lag behind the binary on disk.
1. **Getter-vs-setter confusion.** `unity install-path` with NO arguments is a GETTER that prints the current editor install directory. The original code passed a positional directory to it, producing the failure `too many arguments for 'install-path'. Expected 0 arguments but got 1`. The SET form uses a flag, not a positional argument.

Under `Set-StrictMode -Version Latest`, reading an uninitialized variable is a terminating error, so module-scope state the helpers depend on must be initialized up front.

## Solution

The script initializes `$script:UnityCliPath = 'unity'` after `Set-StrictMode`, refreshes the session PATH from the registry through a bounded retry loop, and discovers the editor through layered, defensive strategies.

### Standalone CLI surface: VERIFIED vs UNCERTAIN

VERIFIED (relied on directly by the script):

- `unity install <version>` -- positional version; `-m <id>` adds a module (the editor install adds `-m windows-il2cpp` only under the `$WithWindowsIl2Cpp` switch).
- `unity install-path` with NO arguments -- a 0-arg GETTER that prints the current editor install directory. Passing a positional directory is the root cause of the "Expected 0 arguments but got 1" failure.
- `unity editors -i` (with `--format json` for parsing; bare `-i` for the diagnostic dump).
- `unity install-modules -e <version> -m <id>` (authoritative, fatal on failure); the `-l` flag lists installable module ids (best-effort sanity check only).

UNCERTAIN (treated best-effort; docs punt to `--help`):

- The install-path SET flag. The Hub CLI uses `-s <dir>`; the standalone CLI likely mirrors `-s`/`--set`. The script tries `-s` then `--set`, then continues.
- The `editors --format json` schema (field names are scanned, not assumed; a wrapper object such as `{"editors":[...]}` is flattened).
- Whether the Windows IL2CPP module id is exactly `windows-il2cpp` (the standard Hub id, very likely). The `-l` listing is a best-effort sanity check; the throwing `-m` install is the source of truth.
- `-c <changeset>` (only meaningful for non-release builds). The script never passes it, so it is NOT exercised here.

Sources: docs.unity.com/en-us/hub/unity-cli, docs.unity.com/en-us/hub/cli-overview.

### Refresh the session PATH from the registry

```powershell
function Update-SessionPathFromRegistry {
    $segments = New-Object System.Collections.Generic.List[string]

    # Guard the INPUT: Join-Path throws on a null $env:LOCALAPPDATA.
    if ($env:LOCALAPPDATA) { $segments.Add((Join-Path $env:LOCALAPPDATA 'Unity\bin')) }

    $machinePath = [System.Environment]::GetEnvironmentVariable('Path', 'Machine')
    if ($machinePath) { $segments.Add($machinePath) }

    $userPath = [System.Environment]::GetEnvironmentVariable('Path', 'User')
    if ($userPath) { $segments.Add($userPath) }

    # Preserve process-only PATH entries (e.g. node from setup-node via
    # $GITHUB_PATH) by appending the existing $env:PATH as the FINAL segment.
    if ($env:PATH) { $segments.Add($env:PATH) }

    $env:PATH = (($segments | Where-Object { $_ -and $_.Trim().Length -gt 0 }) -join ';')
}
```

Read BOTH Machine and User registry scopes, null-guard each, prepend the installer's known `%LOCALAPPDATA%\Unity\bin` target in case the registry write lags, and -- because this `.ps1` shares the caller's process environment -- append the existing `$env:PATH` LAST so process-only entries (node added by `setup-node` via `$GITHUB_PATH`) survive instead of being clobbered. `Ensure-UnityCli` re-checks `Get-Command unity` about 3 times, refreshing PATH each pass with `Start-Sleep -Seconds 2` between tries; on exhaustion it falls back to the absolute `%LOCALAPPDATA%\Unity\bin\unity.exe`, and only a genuinely missing binary throws the original message verbatim (so historical log greps keep matching).

### Three invokers with distinct contracts

- `Invoke-UnityCli` -- THROWING. Used only where failure is fatal (the editor install, the standalone module install). Echoes `$script:UnityCliPath` and throws on a non-zero `$LASTEXITCODE`.
- `Invoke-UnityCliSafe` -- NON-throwing best-effort. Returns `$true`/`$false`, merges stderr into stdout (`2>&1`) so a beta CLI writing usage to stderr cannot trip `$ErrorActionPreference = 'Stop'`. Used for optional effects such as setting the install path.
- `Get-UnityCliOutput` -- CAPTURING, non-throwing. Returns stdout lines or `$null`. It deliberately keeps getter output OFF this script's success stream because the caller (`run-ci-tests.ps1`) reads our LAST stdout line as the resolved editor path via `Select-Object -Last 1`; getter chatter must never leak there.

### Best-effort SET, getter-based authoritative discovery

`Set-UnityCliInstallPath` calls `Invoke-UnityCliSafe -Arguments @('install-path', '-s', $Root)`, then `--set` on failure, then emits a `::notice::` (NOT an error) and continues. Setting the path is an optimization, never a requirement.

`Get-UnityCliInstallRoot` runs the 0-arg GETTER `Get-UnityCliOutput -Arguments @('install-path')` and takes the last path-like line (validated by `Test-LooksLikeAbsolutePath`, which accepts only `C:\...` or `\\...`). This reports the CLI's REAL install location whether or not the SET succeeded, so discovery never depends on the uncertain set flag.

`Resolve-InstalledEditor` layers: (a) probe under the getter-reported root, (b) the candidate-path search under the configured `$InstallRoot`, (c) a defensive parse of `unity editors -i --format json`. `Resolve-EditorFromCliJson` wraps `ConvertFrom-Json` in try/catch -- malformed or banner-prefixed beta output returns `$null` instead of throwing -- and scans candidate version fields and path fields rather than assuming a schema.

### Modules only for standalone, with id verification

The editor install adds `-m windows-il2cpp` ONLY under the `$WithWindowsIl2Cpp` switch; editmode and playmode never install modules. `Add-WindowsIl2CppModule` runs `install-modules -e <version> -l` as a best-effort sanity check: if that listing is readable but omits the literal `windows-il2cpp`, it emits a `::warning::` and CONTINUES (a beta CLI may use a different listing format or display name, so a mismatch must not abort a standalone run on its own); if the listing is unavailable it proceeds optimistically with the standard id. Either way the throwing `install-modules -e <version> -m windows-il2cpp` is the authoritative source of truth and is fatal on real failure.

## Verification

- `scripts/__tests__/powershell-syntax.test.js` reparses the edited `.ps1`; keep it valid PowerShell 5.1.
- `scripts/__tests__/unity-runner-script-contract.test.js` pins both the PATH fix (`$script:UnityCliPath`, `GetEnvironmentVariable`, `Update-SessionPathFromRegistry`, the absolute-path fallback) and the resilience design: `install-path` SET uses `-s` (never a positional `$Root`), `Invoke-UnityCliSafe` exists, `Get-UnityCliInstallRoot` reads `@('install-path')`, the defensive `--format json` / `ConvertFrom-Json` discovery, and standalone-only `-m windows-il2cpp` gating.

## See Also

- [Unity CI Matrix](./unity-ci-matrix.md)
- [Headless Test Runner](./headless-test-runner.md)
- [Unity License Bootstrap](./unity-license-bootstrap.md)

## References

- Unity CLI docs: https://docs.unity.com/en-us/hub/unity-cli
- Unity CLI overview: https://docs.unity.com/en-us/hub/cli-overview
- Standalone CLI installer: https://public-cdn.cloud.unity3d.com/hub/prod/cli/install.ps1
- Bootstrap script: `scripts/unity/ensure-editor.ps1`
