---
title: CI and GitHub Settings
description: Runner, environment, secret, and branch protection setup for trusted releases
---

# CI and GitHub Settings

This repository splits trust domains:

- Licensed Unity jobs run only on Ambiguous self-hosted Windows runners.
- npm publishing runs on GitHub-hosted Ubuntu with OIDC Trusted Publishing.

## Self-Hosted Unity Runners

Licensed Unity jobs target self-hosted Windows runners by labels only. No
custom runner group is required; runners may live in the organization's
default runner group.

- Labels (all required on each Unity runner):
  - `self-hosted`
  - `Windows`
  - `RAM-64GB`
- Speed marker applied only to `ELI-MACHINE`:
  - `fast`

Unity Pro is a single-seat license: only one machine can be activated at
a time. All four Unity-credential-using jobs (`unity-tests`,
`il2cpp-tests`, `benchmarks`, and the `unity-checks` job in
`release.yml`) declare:

```yaml
concurrency:
  group: unity-pro-license
  cancel-in-progress: false
```

so two licensed jobs cannot run simultaneously across the two Windows
machines and fight for the license. The previous single-slot
`wallstop-organization-builds` group is a reserved sentinel that the
workflow validator hard-rejects anywhere it appears; the new
`unity-pro-license` group serves the same serialization purpose under a
non-overloaded name.

The three Unity matrix jobs (`unity-tests`, `il2cpp-tests`, `benchmarks`)
additionally declare `strategy.max-parallel: 1` so matrix entries serialize
internally to the workflow run and do not compete for the single
concurrency slot (the validator's `findMatrixConcurrencyEvictionViolations`
check enforces this combination).

Per-runner Unity-cache safety is provided by each runner agent's exclusive
workspace - a single self-hosted agent only ever runs one job at a time, so
`.unity-test-project/Library` directories cannot collide.

Runner routing is uniform across all four Unity-credential-using jobs:

```yaml
runs-on: [self-hosted, Windows, RAM-64GB]
```

Both ELI-MACHINE and DAD-MACHINE are eligible to pick up any Unity job;
the `fast` label remains on ELI-MACHINE only for future opt-in hotfix
dispatch but no job requests it today.

Lightweight matrix configuration jobs run on `ubuntu-latest` and remain
parallelizable.

### Workflow-level vs job-level concurrency interaction

The Unity workflows declare workflow-level `concurrency: { group:
${{ github.workflow }}-${{ github.ref }}, cancel-in-progress: true }`
so rapid same-branch pushes supersede the older workflow run. The
licensed Unity jobs inside each workflow separately declare a job-level
`concurrency: { group: unity-pro-license, cancel-in-progress: false }`.
The two groups operate at different scopes and do not interact directly.

On rapid same-branch pushes, the workflow-level group cancels the
_older_ workflow run; any of its jobs that have not yet started will
never start, and any running step is sent SIGTERM. However, the
job-level `cancel-in-progress: false` on the license group prevents the
license-holding job itself from being preempted, so a job that has
already entered the `unity-pro-license` slot keeps running until it
completes; the new workflow's `unity-checks`/`unity-tests`/etc. job
then waits on the license slot until the old job releases it. This is
the intended behavior - it protects in-flight Unity activation and
asset import state from being torn down mid-run - but operators should
expect a brief gap between "newer push" and "newer Unity job actually
starts" while the older job drains.

## Stuck-Job Recovery

A known GitHub Actions dispatcher bug ([Community Discussion #186811](https://github.com/orgs/community/discussions/186811))
causes self-hosted runners to report Online/Idle while `runner_id` stays
at 0 for 7+ minutes, leaving a queued job indefinitely stuck even when an
idle runner's labels are a superset of the job's requested labels.

Two workflows together provide recovery: an auto-watchdog that audits
the queue on a cron schedule, and a manual one-click recovery workflow
for a single run id.

### Auto-watchdog: `.github/workflows/stuck-job-watchdog.yml`

Runs every 5 minutes on `ubuntu-latest`, lists queued workflow runs
older than 5 minutes (`MIN_QUEUE_AGE_SECONDS=300`), fetches the org
runner inventory (falling back to repo runners on 403), and identifies
the subset that are genuinely dispatcher-stuck. A run is considered
stuck only when ALL of the following hold: the run is `status: queued`,
no job in the run is `in_progress` (a run with an in-progress job is by
definition holding/using a runner, not dispatcher-stuck), at least one
job is queued, at least one idle runner's labels satisfy a queued job's
label requirements, the run's workflow file is not in the exclusion
list, and the run is not the watchdog's own run.

Worst-case time-to-recover under the tightened thresholds is roughly
10 minutes (5-min cron interval + 300s queue-age threshold + cancel /
redispatch latency).

For each genuinely-stuck run the watchdog `gh run cancel`s the run
(the documented recovery for a queued-only run; `gh run rerun --failed`
cannot rerun a run that never reached `failed` status - see cli/cli
issue #9221). For runs triggered by `push`, `schedule`, or
`workflow_dispatch` on a workflow that declares `workflow_dispatch:`
the watchdog then re-dispatches the workflow on the same `ref` via the
REST API. For `pull_request`-triggered runs, the watchdog only cancels
and writes a clear `GITHUB_STEP_SUMMARY` instruction asking the
operator to click "Re-run all jobs" in the GitHub UI - there is no
safe API path to re-trigger a `pull_request` run without pushing a
commit, so the watchdog does not attempt it.

`release.yml` is excluded by default (`EXCLUDED_WORKFLOW_FILES=
("release.yml")`) so a spurious cancel cannot double-publish or break
attestation. Additional exclusions may be set via the
`WATCHDOG_EXCLUDED_WORKFLOWS` repository variable (whitespace-separated
list of workflow filenames).

Cancel attempts are capped at 2 per run-id per 24 hours via a small
state file on the `watchdog-state` orphan branch.

GitHub `schedule:` cron triggers only fire from the repository default
branch. Until `stuck-job-watchdog.yml` is on `master`, the cron is
INACTIVE and only manual `workflow_dispatch` from the Actions tab
works. After merge to `master` the cron resumes automatically and runs
every 5 minutes.

### Manual one-click recovery: `.github/workflows/unstick-run.yml`

`workflow_dispatch`-only workflow that targets a single explicit run id
rather than auto-scanning. Use this when:

1. The watchdog is not yet on the default branch (see the cron caveat
   above) and a job is stuck right now.
1. You want immediate recovery and do not want to wait for the next
   cron tick or the queue-age threshold.

Inputs:

- `run_id` (required, string of digits): the GitHub Actions run id to
  recover. Find it in the URL of the stuck run.
- `force_redispatch` (optional, boolean, default `false`): when true,
  attempt REST `actions/workflows/{id}/dispatches` after cancel. Only
  valid for `push` / `schedule` / `workflow_dispatch` events on a
  branch where the workflow file declares `workflow_dispatch:`.
- `bypass_exclusion` (optional, boolean, default `false`): operate on a
  run whose workflow file is in the exclusion list (e.g. `release.yml`).
  Use deliberately.

Behavior mirrors the watchdog's per-run logic: validates the run id is
a positive integer, confirms the run exists and is `queued` and older
than `MIN_AGE_SECONDS=30` (guards against accidental cancellation of
fresh runs), honors the same exclusion list unless `bypass_exclusion`
is true, then `gh run cancel`s and optionally REST-redispatches. It
does NOT touch the watchdog state branch and does NOT count against the
watchdog's per-run cancel cap.

To invoke: repo Actions tab -> "Unstick Run" workflow -> "Run workflow"
dropdown -> select branch -> enter the run id.

## Unity Workflows

Active Unity workflows:

- `.github/workflows/unity-tests.yml`
- `.github/workflows/unity-il2cpp.yml`
- `.github/workflows/unity-benchmarks.yml`
- `.github/workflows/release.yml` (`unity-checks` job)

Unity test matrix:

- `2021.3.45f1`
- `2022.3.45f1`
- `6000.0.32f1`
- `editmode`
- `playmode`

IL2CPP and release checks default to `2022.3.45f1`. Benchmarks run on schedule
or manual dispatch only.

## Licensed Job Guardrails

Licensed Unity jobs intentionally skip:

- pull requests from forks
- pushes to unprotected branches

This is expected. Fork PRs should still run GitHub-hosted checks that do not
need Unity licenses or self-hosted runners.

The workflows must not use `pull_request_target` to check out untrusted fork
code.

## Required Unity Secrets

Set secret names without documenting values:

- `UNITY_LICENSE`
- `UNITY_SERIAL`
- `UNITY_EMAIL`
- `UNITY_PASSWORD`

Personal/GameCI license flow uses `UNITY_LICENSE` plus account credentials.
Professional serial activation uses `UNITY_SERIAL` plus account credentials.
Do not record secret existence, rotation status, or account credential state in
tracked files or the local ignored runbook. Keep that security status in GitHub
environment settings or the approved organization password manager.

## GitHub Environments

Use environments if the organization wants release approvals. The current
workflow can run without an environment, but npm Trusted Publishing may be
configured with one. If an environment is used, configure npm with the exact
environment name.

For each environment, verify:

1. Required reviewers.
1. Wait timers.
1. Deployment branch rules.
1. Environment secret access through GitHub settings.
1. Whether the release workflow can request the environment from tag builds.

## Branch and Tag Protection

Protect the default branch and release tags:

1. Require pull requests for `master` and `main` if both are active.
1. Require status checks for script tests, docs checks, package metadata, and
   workflow validation.
1. Require signed tags or limit tag creation to release maintainers if the
   organization supports it.
1. Protect `v*` tags from deletion or force updates.
1. Confirm release maintainers can create `vX.Y.Z` tags through the intended
   process.

## Cache Contract

Unity Library caches must include:

- `.unity-test-project/Packages/manifest.json`
- `.unity-test-project/Packages/packages-lock.json`
- `.unity-test-project/ProjectSettings/ProjectVersion.txt`

Do not add broad `restore-keys` for Unity Library caches.

## Verification

Run:

```bash
npm run test:unity-contracts
node scripts/validate-workflows.js
```

The validator hard-fails if any workflow reintroduces the reserved
`wallstop-organization-builds` group name (workflow-level or job-level, in
multi-line mapping, inline mapping, or scalar-shorthand form).

Workflow-shape contract checklist:

1. Confirm each of the four Unity-credential-using jobs (`unity-tests`,
   `il2cpp-tests`, `benchmarks`, `unity-checks`) declares the job-level
   `concurrency:` block with `group: unity-pro-license` and
   `cancel-in-progress: false`.
1. Confirm the three Unity matrix jobs (`unity-tests`, `il2cpp-tests`,
   `benchmarks`) declare `strategy.max-parallel: 1`. The `unity-checks`
   release job has no matrix and therefore no `max-parallel`.
1. Confirm each of the four jobs declares the uniform static label set
   `runs-on: [self-hosted, Windows, RAM-64GB]` so either Windows machine
   can pick up any Unity job.
1. Confirm `wallstop-organization-builds` does not appear anywhere under
   `.github/workflows/*.yml` (sentinel guard).
1. Confirm `.github/workflows/stuck-job-watchdog.yml` exists and is
   enabled (queue auto-recovery for the GitHub Actions dispatcher bug).
   Once merged to the default branch, the 5-minute cron fires
   automatically.
1. Confirm `.github/workflows/unstick-run.yml` exists for manual
   one-click recovery of a single run id (operator dispatches it from
   the Actions tab with the stuck run's id). The workflow is
   `workflow_dispatch`-only -- there is no cron, push, or PR trigger.
   (See "Stuck-Job Recovery" subsection above for the operator runbook.)

Trigger safe workflows after transfer:

1. `workflow_dispatch` for Unity Tests with one Unity version and one mode.
1. `workflow_dispatch` for Unity IL2CPP.
1. `workflow_dispatch` for Unity Benchmarks if runner capacity allows it.
1. A same-repository pull request to confirm licensed checks land on a
   Windows runner and serialize correctly (only one matrix entry running
   at a time, no eviction messages).
1. A fork pull request dry run to confirm licensed checks skip.
