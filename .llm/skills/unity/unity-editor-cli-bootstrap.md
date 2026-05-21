---
title: "Unity Editor CLI Bootstrap"
id: "unity-editor-cli-bootstrap"
category: "unity"
version: "1.0.0"
created: "2026-05-20"
updated: "2026-05-20"

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
  reasoning: "Requires understanding Windows registry PATH scopes and the difference between the persisted environment and the current process environment."

impact:
  performance:
    rating: "none"
    details: "One-time per-runner installer cost; does not affect steady-state run time"
  maintainability:
    rating: "high"
    details: "A single helper centralizes session-PATH refresh and absolute-path fallback so the installer's PATH lag cannot break Unity bootstrap"
  testability:
    rating: "medium"
    details: "powershell-syntax.test.js reparses the script; unity-runner-script-contract.test.js pins the PATH-fix tokens"

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

<!-- trigger: unity, cli, path, bootstrap, ensure-editor, powershell | Standalone Unity CLI install + session PATH refresh on self-hosted runners | Core -->

# Unity Editor CLI Bootstrap

> **One-line summary**: `scripts/unity/ensure-editor.ps1` installs the standalone Unity CLI on a self-hosted Windows runner and must refresh the current session's `$env:PATH` from the registry (with an absolute-path fallback) because the installer updates only the User-scope registry PATH, never the running process.

## Overview

The active Unity workflows run directly on self-hosted Windows runners and rely on the standalone Unity CLI (`unity`) to install editors and modules on demand. `scripts/unity/ensure-editor.ps1` is the bootstrap: if `unity` is not already on PATH, it downloads and runs the official installer, then uses the CLI to install the requested editor version and (for standalone IL2CPP) the `windows-il2cpp` module.

The installer at `https://public-cdn.cloud.unity3d.com/hub/prod/cli/install.ps1` writes the binary to `%LOCALAPPDATA%\Unity\bin\unity.exe` and persists that directory onto the User-scope registry PATH via `[Environment]::SetEnvironmentVariable("PATH", ..., "User")`. The trap: a registry PATH write does NOT mutate the already-running process's `$env:PATH`. A child process inherits the environment that existed when it was spawned, so an in-session `Get-Command unity` immediately after install still fails. The historical symptom was a hard failure with "Unity CLI installation completed but 'unity' is still not on PATH. Reopen the runner shell...", which is unactionable inside a CI step that cannot reopen its shell.

This bootstrap matters because the script is parsed cross-platform: `scripts/__tests__/powershell-syntax.test.js` reparses the `.ps1` on Linux `pwsh`, so the script must stay syntactically valid and PowerShell 5.1 + `Set-StrictMode -Version Latest` safe even though the Windows-only registry calls never execute on Linux.

## Problem Statement

After `Invoke-Expression (Invoke-RestMethod '.../cli/install.ps1')`, the installer:

- Drops `unity.exe` at `%LOCALAPPDATA%\Unity\bin\unity.exe` (a known, fixed target).
- Appends that directory to the User-scope registry PATH only.
- Does NOT touch the Machine-scope registry PATH.
- Does NOT update `$env:PATH` in the current PowerShell process.

So a naive re-check throws even on a perfectly successful install. Two compounding hazards:

1. The registry write can lag slightly behind the binary appearing on disk, so even reading the registry back immediately is not guaranteed to show the new entry on the first try.
1. Under `Set-StrictMode -Version Latest`, reading an uninitialized variable is a terminating error, so any module-scope state the helpers depend on must be initialized up front.

## Solution

The script defines a script-scope default and a session-PATH refresh helper, then resolves the CLI through a bounded retry loop with an absolute-path fallback. Only when every path fails does it throw the original message verbatim (so historical log greps keep matching).

### Script-scope default

Initialize `$script:UnityCliPath = 'unity'` near the top, right after `Set-StrictMode`. StrictMode then never reads it uninitialized, and callers that resolve the CLI later overwrite it with the concrete source.

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

Read BOTH Machine and User registry scopes (the persisted PATH after the installer's write), null-guard each so a missing scope cannot break the join, prepend the installer's known `%LOCALAPPDATA%\Unity\bin` target in case the registry write lags, and -- crucially, because this `.ps1` shares the caller's process environment -- append the existing `$env:PATH` LAST so process-only entries (such as node added by `setup-node` via `$GITHUB_PATH`) are preserved rather than clobbered. Filter empty/null segments before the `;` join. Note the LOCALAPPDATA guard wraps the INPUT to `Join-Path`, which throws on null before any output guard could run.

### Bounded retry, then absolute-path fallback

`Ensure-UnityCli` re-checks `Get-Command unity` in a short bounded loop (about 3 tries, refreshing the session PATH each pass, `Start-Sleep -Seconds 2` between tries with no trailing sleep on the last try). On success it sets `$script:UnityCliPath = $command.Source` and returns. If the loop exhausts, it falls back to the absolute path:

```powershell
$fallback = Join-Path $env:LOCALAPPDATA 'Unity\bin\unity.exe'
if (Test-Path -LiteralPath $fallback -PathType Leaf) {
    $script:UnityCliPath = (Resolve-Path -LiteralPath $fallback).Path
    return $script:UnityCliPath
}

throw "Unity CLI installation completed but 'unity' is still not on PATH. Reopen the runner shell or add the Unity CLI install directory to PATH."
```

The throw message is preserved verbatim so historical log greps still match; it now fires only when the binary is genuinely absent.

### Invoke through the resolved path

`Invoke-UnityCli` calls `& $script:UnityCliPath @Arguments` (and echoes `$script:UnityCliPath`) instead of a bare `& unity`, so commands run against whichever source `Ensure-UnityCli` resolved -- PATH entry or absolute fallback. Its command-FAILED `throw` interpolates `$script:UnityCliPath` too, so the error reports the path that actually ran (not a misleading literal `unity`). The `$LASTEXITCODE` handling is unchanged.

## Verification

- `scripts/__tests__/powershell-syntax.test.js` reparses the edited `.ps1`; keep it valid PowerShell 5.1.
- `scripts/__tests__/unity-runner-script-contract.test.js` asserts the script contains `$script:UnityCliPath` and `GetEnvironmentVariable` as a regression guard for the PATH fix, alongside the existing tokens (`Ensure-UnityCli`, `Set-UnityCliInstallPath`, `install-path`, `install-modules`, `windows-il2cpp`, `Unity.exe`).

## See Also

- [Unity CI Matrix](./unity-ci-matrix.md)
- [Headless Test Runner](./headless-test-runner.md)
- [Unity License Bootstrap](./unity-license-bootstrap.md)

## References

- Unity CLI docs: https://docs.unity.com/en-us/hub/unity-cli
- Standalone CLI installer: https://public-cdn.cloud.unity3d.com/hub/prod/cli/install.ps1
- Bootstrap script: `scripts/unity/ensure-editor.ps1`
