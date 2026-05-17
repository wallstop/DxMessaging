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

There is no shared job-level concurrency group on Unity jobs. The previous
single-slot `wallstop-organization-builds` group caused matrix-eviction
cancellations and runner-pickup stalls; that group name is now a reserved
sentinel that the workflow validator hard-rejects anywhere it appears.

Per-runner Unity-cache safety is provided by each runner agent's exclusive
workspace - a single self-hosted agent only ever runs one job at a time, so
`.unity-test-project/Library` directories cannot collide. No additional
concurrency group is required for cache isolation.

Event-aware runner routing is emitted from each Unity workflow's
`matrix-config` job through a `runner-labels` output and consumed by
`runs-on: ${{ fromJSON(needs.matrix-config.outputs.runner-labels) }}`:

- Pull-request events route to `[self-hosted, Windows, RAM-64GB, fast]`
  (ELI-MACHINE only) for interactive feedback isolation.
- `push`, `schedule`, `workflow_dispatch`, and release-tag events route to
  `[self-hosted, Windows, RAM-64GB]` so either Windows machine can pick
  them up.

Lightweight matrix configuration jobs run on `ubuntu-latest` and remain
parallelizable.

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

1. Confirm none of the four Unity-credential-using jobs (`unity-tests`,
   `il2cpp-tests`, `benchmarks`, `unity-checks`) declares a job-level
   `concurrency:` block.
1. Confirm the active Unity workflows (`unity-tests.yml`, `unity-il2cpp.yml`)
   expose a `runner-labels` output on their `matrix-config` job that the
   licensed job consumes through
   `runs-on: ${{ fromJSON(needs.matrix-config.outputs.runner-labels) }}`.
1. Confirm `wallstop-organization-builds` does not appear anywhere under
   `.github/workflows/*.yml` (sentinel guard).

Trigger safe workflows after transfer:

1. `workflow_dispatch` for Unity Tests with one Unity version and one mode.
1. `workflow_dispatch` for Unity IL2CPP.
1. `workflow_dispatch` for Unity Benchmarks if runner capacity allows it.
1. A same-repository pull request to confirm licensed checks run on the
   `fast` ELI-MACHINE runner.
1. A fork pull request dry run to confirm licensed checks skip.
