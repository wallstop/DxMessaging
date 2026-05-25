# Unity Runners After Repository Transfer Runbook

This runbook explains how to restore self-hosted Unity runner access after a repository is transferred between GitHub organizations. Keep execution notes local. Do not paste secrets, screenshots of organization settings, or other private account metadata into this file or any tracked follow-up.

## Symptom

- A queued Unity workflow run (for example `Unity Tests`, `Unity IL2CPP`, `Unity Benchmarks`, or the `unity-checks` job in `Release`) stays queued indefinitely.
- The GitHub Actions UI shows the job waiting for a runner. There is no error, no warning, and the run never starts.
- The organization's self-hosted runners report Online and Idle in the GitHub UI, with labels that exactly match the workflow's `runs-on` request (`self-hosted`, `Windows`, `RAM-64GB`).
- The watchdog defined in `.github/workflows/stuck-job-watchdog.yml` does not recover the run because no idle runner is visible to the repository, which means the watchdog's matching rule does not fire.

## Root cause

After a repository transfer between GitHub organizations, the destination organization's runner groups do not automatically include the transferred repository in their repository-access list. When a runner group is configured as "Selected repositories", any repository that is not explicitly listed cannot dispatch jobs to that group's runners. The dispatcher does not log an error in this state; the job simply stays queued.

This is a configuration-state issue, not the intermittent dispatcher bug tracked upstream as [GitHub Community Discussion #186811](https://github.com/orgs/community/discussions/186811). The dispatcher bug applies when an idle matching runner is visible to the repository through the GitHub API but never receives the job. If the API does not list the runner at all for the repository, this runbook applies instead.

## Diagnose with the GitHub CLI

Run the following commands from any workstation with `gh auth login` already completed.

List the organization's runner groups, including each group's visibility setting:

```bash
gh api orgs/Ambiguous-Interactive/actions/runner-groups \
  -q '.runner_groups[] | {id, name, visibility, allows_public_repositories}'
```

For a runner group whose visibility is `selected`, list the repositories that currently have access:

```bash
gh api orgs/Ambiguous-Interactive/actions/runner-groups/<group-id>/repositories \
  -q '.repositories[] | {id, name, full_name}'
```

If the transferred repository name does not appear in that list, the dispatcher has no path to the group's runners from this repository, which matches the symptom above.

Cross-check by listing runners that the repository itself can see:

```bash
gh api repos/Ambiguous-Interactive/DxMessaging/actions/runners \
  -q '.runners[] | {id, name, status, busy, labels: [.labels[].name]}'
```

When this list is empty or omits the expected runner names while the organization-level inventory shows them online, the access list is the cause.

## Resolution

Choose one of the following resolutions inside the destination organization. Either resolution restores dispatch; pick the one that matches the organization's security model.

Add the transferred repository to the selected list:

1. Organization Settings.
1. Actions.
1. Runner groups.
1. Default.
1. Repository access.
1. Add the transferred repository to the list.
1. Save.

Change the group's visibility to all repositories:

1. Organization Settings.
1. Actions.
1. Runner groups.
1. Default.
1. Repository access.
1. Set visibility to all repositories.
1. Save.

The second resolution avoids future per-transfer maintenance but exposes the runners to every repository in the organization. Use it only when that exposure is acceptable for the runner group's security posture.

After applying the chosen resolution, re-run the queued workflow from the Actions tab. The preflight job added to each Unity workflow validates runner access from `ubuntu-latest` before any matrix entry attempts to dispatch onto self-hosted; a green preflight confirms the fix.

## Preflight diagnostic in this repository

Unity workflows in this repository run a `runner-preflight` job on `ubuntu-latest` before the self-hosted matrix. That preflight queries `gh api orgs/${OWNER}/actions/runners` first and, on 403/404 (the default `secrets.GITHUB_TOKEN` cannot list org-scoped runners under most org policies), falls back to `gh api repos/${GITHUB_REPOSITORY}/actions/runners`. If both endpoints fail (typically a 403 from each because the token is unscoped for runner administration), the preflight emits a `::warning::` and exits 0 (soft pass). The preflight must NEVER be more strict than the no-preflight baseline; its only job is to surface a fast, clear failure when it can prove the runner inventory is wrong.

### Upgrading the soft pass to a hard pass

The default `secrets.GITHUB_TOKEN` cannot list runners under a repo-level scope strict enough to reflect the runner-group ACL, so the preflight falls back to a soft pass on most installations. To upgrade the soft-pass path to a hard-pass:

1. Mint a fine-grained personal access token (or a GitHub App installation token) holding the repository-level "Administration: read" permission, scoped to `Ambiguous-Interactive/DxMessaging` only. Do NOT use a classic PAT with `admin:org`, and do NOT use the fine-grained "Organization administration: read" permission: both grant org-wide visibility, which causes `gh api orgs/<org>/actions/runners` to return the entire org runner inventory regardless of any individual repository's runner-group ACL. That would let the preflight see runners as online and silently pass even when the post-transfer ACL is broken, which is exactly the pitfall this runbook addresses.
1. Add the token as a repository secret named `RUNNER_AUDIT_PAT`.
1. Wire the workflow to prefer `RUNNER_AUDIT_PAT` over `GITHUB_TOKEN` when set, and to query the repo-scoped endpoint `repos/<owner>/<repo>/actions/runners`. That endpoint enforces the runner-group ACL: if the repository does not have access to a runner via its group, the runner is invisible there, which is the live ACL state we want the preflight to detect. The preflight retains the same soft-pass behavior if the secret is absent, so this is opt-in.

The rationale is deliberate: we want the upgrade token to FAIL when the ACL is misconfigured, not paper over it; that is why we use the repo-scoped "Administration: read" permission rather than any org admin scope. Without that property the hard-pass mode would be worse than the soft-pass mode it replaces.

This is intentionally documented but NOT enabled by default: the soft pass is the correct conservative behavior given the threat model. Operators see a `::warning::` annotation rather than a green check, and the existing watchdog + manual unstick workflows continue to recover any actually-stuck job.

Because `administration` is not a valid `permissions:` key for the workflow-scoped `GITHUB_TOKEN`, the only way to grant the preflight read access to the runner inventory under a repo-level scope is to provision an external token (PAT or app installation token) via `RUNNER_AUDIT_PAT` (see above). Without that, the preflight falls back to the soft-pass path, which is the design intent.

### Follow-up: composite action factoring

The preflight shell currently lives inline in three workflows (`unity-tests.yml`, `unity-benchmarks.yml`, `release.yml`). A composite action under `.github/actions/runner-access-preflight/` would deduplicate the block. Out of scope for the current change; track here so the next maintainer can find it.

If the preflight passes but the matrix job still stays queued, the cause is more likely the dispatcher bug (see [GitHub Community Discussion #186811](https://github.com/orgs/community/discussions/186811)) than the access list. Use the recovery workflows in this repository: [unstick-run.yml](https://github.com/Ambiguous-Interactive/DxMessaging/blob/master/.github/workflows/unstick-run.yml) for manual recovery and [stuck-job-watchdog.yml](https://github.com/Ambiguous-Interactive/DxMessaging/blob/master/.github/workflows/stuck-job-watchdog.yml) for the automated path.

## PowerShell 7 prerequisite on self-hosted runners

Self-hosted Windows Unity runners require **PowerShell 7 (`pwsh`)** in
addition to Git Bash. Every Unity workflow consumes the
`print-self-hosted-runner-diagnostics` composite action
(`.github/actions/print-self-hosted-runner-diagnostics/action.yml`) before its
own steps, and that action plus several Unity build steps run with
`shell: pwsh`. PowerShell 7 is _not_ the Windows-built-in PowerShell 5.1
(`powershell`); it is a separate install that provides the `pwsh` executable.

### Symptom

- A self-hosted Unity job fails almost immediately with
  `##[error]pwsh: command not found`.
- The failure originates from the first `shell: pwsh` step the agent reaches.
- Git Bash and the runner agent are otherwise healthy.

The diagnostics composite action now fails fast with a clear, actionable
error annotation (`pwsh missing on self-hosted runner`) when `pwsh` is absent,
so this state no longer surfaces only as the cryptic
`pwsh: command not found`. The preflight step that emits that error runs under
Windows PowerShell 5.1, which is always present, so it executes even when
PowerShell 7 is missing.

### Install PowerShell 7

On a machine with winget:

```powershell
winget install --id Microsoft.PowerShell --source winget
```

For machines without winget, download and run the latest MSI installer from
the official releases page:
<https://github.com/PowerShell/PowerShell/releases>.

### Verify

Open a **new** shell (so the updated PATH is picked up) and confirm:

```powershell
pwsh -v
Get-Command pwsh
```

`pwsh -v` should print the installed PowerShell 7 version, and
`Get-Command pwsh` should resolve to the installed executable's path.

### Restart the runner agent

After installing PowerShell 7, restart the self-hosted runner service/agent
(or refresh the machine's PATH and restart the runner) so the agent process
sees `pwsh` on its PATH. The runner agent inherits its environment at start
time; until it is restarted it will keep reporting `pwsh: command not found`
even though a fresh interactive shell can find `pwsh`. Re-run the queued
Unity workflow once the agent is back online.

## Android NDK install failures and Windows long-path (MAX_PATH) enablement

The dedicated Android module install (`android` + `android-sdk-ndk-tools`) is a
multi-GB Google download whose **NDK unpack** phase fails flakily on Windows.

### Symptom

- The base editor and all core modules provision fine, but the run fails on the
  Android tier with a message like
  `Unity <version> Android CI module install FAILED after N attempt(s)`.
- The Unity CLI reaches roughly **93%** of the install and then dies, frequently
  with **exit code 6**, while the NDK extraction is in progress.
- `ensure-editor.ps1`'s post-mortem (printed automatically on Android exhaustion)
  reports the **deepest NDK absolute path length** and the **`LongPathsEnabled`
  state**, and emits a `::warning::` pointing here when the deepest NDK path is at
  or beyond ~240 characters while Windows long-path support is not enabled. The
  editor is deliberately **not** quarantined or re-downloaded in this case.

### Root cause

The Android NDK tree contains very deeply nested toolchain paths. When Windows
long-path support is disabled, the NDK extraction hits the legacy **MAX_PATH
(260-character)** limit mid-unpack and the install fails. Antivirus file-locking
on the freshly written files can also interrupt the unpack.

### Fix on the runner

1. **Enable long paths** (`LongPathsEnabled = 1`). Via the registry:

   ```powershell
   New-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem' `
     -Name 'LongPathsEnabled' -Value 1 -PropertyType DWord -Force
   ```

   Or via Group Policy: **Computer Configuration -> Administrative Templates ->
   System -> Filesystem -> Enable Win32 long paths -> Enabled**. Restart the
   self-hosted runner agent (and ideally reboot) so the change takes effect.

1. **(Optional) Add a Windows Defender exclusion** for the Unity install root
   (`C:\Unity\Editors`) so Defender does not transiently lock NDK files during
   extraction:

   ```powershell
   Add-MpPreference -ExclusionPath 'C:\Unity\Editors'
   ```

After enabling long paths, re-run the workflow. The post-mortem's
`LongPathsEnabled` line should now read `True`, and the deep-path `::warning::`
should no longer fire.

## Local documentation validation

To reproduce the strict-mode mkdocs build that runs in CI:

```bash
npm run validate:docs:strict
```

That command installs the pinned `requirements-docs.txt` and runs `mkdocs build --strict --site-dir _site`. Use `npm run validate:docs` for the much faster out-of-tree link guard alone (no mkdocs install required).

## Audit log

- Record the date, operator initials, and the resolution chosen in the operator log only.
- Do not paste organization settings screenshots, repository identifiers from other organizations, or runner registration tokens.
- Note follow-ups in the team's private operator log, not in tracked files.
