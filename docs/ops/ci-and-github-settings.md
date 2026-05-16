---
title: CI and GitHub Settings
description: Runner, environment, secret, and branch protection setup for trusted releases
---

# CI and GitHub Settings

This repository splits trust domains:

- Licensed Unity jobs run only on Ambiguous self-hosted Windows runners.
- npm publishing runs on GitHub-hosted Ubuntu with OIDC Trusted Publishing.

## Runner Group

Create or verify this runner contract in the Ambiguous organization:

- Runner group: `ambiguous-interactive-organization-builds`
- Labels:
  - `self-hosted`
  - `Windows`
  - `RAM-64GB`

Grant the repository access to that runner group. Do not grant broader runner
groups unless a workflow explicitly needs them.

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

Trigger safe workflows after transfer:

1. `workflow_dispatch` for Unity Tests with one Unity version and one mode.
1. `workflow_dispatch` for Unity IL2CPP.
1. `workflow_dispatch` for Unity Benchmarks if runner capacity allows it.
1. A same-repository pull request to confirm licensed checks run.
1. A fork pull request dry run to confirm licensed checks skip.
