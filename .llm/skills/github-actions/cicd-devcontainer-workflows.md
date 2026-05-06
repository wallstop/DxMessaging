---
title: "CI/CD Devcontainer Workflows"
id: "cicd-devcontainer-workflows"
category: "github-actions"
version: "1.0.0"
created: "2026-05-05"
updated: "2026-05-05"

source:
  repository: "wallstop/DxMessaging"
  files:
    - path: ".github/workflows/devcontainer-prebuild.yml"
    - path: ".github/workflows/devcontainer-test.yml"
    - path: ".devcontainer/Dockerfile"
    - path: ".devcontainer/devcontainer.json"
  url: "https://github.com/wallstop/DxMessaging"

tags:
  - "github-actions"
  - "ci-cd"
  - "devcontainer"
  - "ghcr"
  - "docker"
  - "prebuild"

complexity:
  level: "intermediate"
  reasoning: "Requires understanding the devcontainers/ci action's event-filter default and GHCR push semantics."

impact:
  performance:
    rating: "high"
    details: "Pre-built images cut new-contributor onboarding from 15 min to under 2 min"
  maintainability:
    rating: "high"
    details: "Single skill captures the silent-failure gotcha so it does not bite the next contributor"
  testability:
    rating: "low"
    details: "Verified by the explicit pull-back step in devcontainer-prebuild.yml"

prerequisites:
  - "Familiarity with GitHub Actions workflow YAML"
  - "Awareness of GHCR authentication and image naming"

dependencies:
  packages: []
  skills:
    - "devcontainer-cache-contract"

applies_to:
  languages:
    - "YAML"
  frameworks:
    - "GitHub Actions"
  versions: {}

aliases:
  - "Devcontainer CI"
  - "GHCR prebuild"
  - "eventFilterForPush"

related:
  - "devcontainer-cache-contract"
  - "headless-test-runner"
  - "unity-ci-matrix"

status: "stable"
---

<!-- trigger: cicd, ci, cd, workflow, devcontainer, ghcr, docker, prebuild | Devcontainer CI/CD workflow patterns and pitfalls | Core -->

# CI/CD Devcontainer Workflows

> **One-line summary**: When using `devcontainers/ci@v0.3` to push images to GHCR, set `eventFilterForPush: ""` explicitly; the default value silently skips pushes on `schedule` and `workflow_dispatch` triggers, breaking pre-build and dispatch flows without any error in the log.

## When to Use

- Creating or modifying devcontainer CI/CD workflows.
- Debugging image push failures to GHCR.
- Setting up GHCR caching for devcontainer builds.
- Investigating CI failures related to Docker image operations.

## When NOT to Use

- Unity-specific CI (build, test, deploy Unity projects). See [unity-ci-matrix](../unity/unity-ci-matrix.md).
- General Docker operations unrelated to devcontainers.
- Local devcontainer development (no CI involved).

## Critical: `devcontainers/ci@v0.3` Event Filter Gotcha

The `devcontainers/ci` action has a subtle and dangerous default that silently skips image pushes.

### The Problem

`eventFilterForPush` defaults to `"push"`, which acts as a universal gate on ALL push decisions, including `push: always`. This means:

| Trigger             | `github.event_name` | Matches default filter? | Push happens? |
| ------------------- | ------------------- | ----------------------- | ------------- |
| `push` to branch    | `push`              | Yes                     | Yes           |
| `schedule` (cron)   | `schedule`          | No                      | No            |
| `workflow_dispatch` | `workflow_dispatch` | No                      | No            |

### The Fix

Always set `eventFilterForPush: ""` to disable the event gate:

```yaml
- uses: devcontainers/ci@v0.3
  with:
    imageName: ghcr.io/${{ steps.repo.outputs.repository_lowercase }}/devcontainer
    cacheFrom: ghcr.io/${{ steps.repo.outputs.repository_lowercase }}/devcontainer
    push: always
    # Required: devcontainers/ci defaults eventFilterForPush to "push",
    # which silently skips push on schedule/workflow_dispatch triggers
    eventFilterForPush: ""
```

The DxMessaging workflows that need this:

- `.github/workflows/devcontainer-prebuild.yml` (weekly cron + dispatch).
- `.github/workflows/devcontainer-test.yml` (uses `push: filter` plus `refFilterForPush: refs/heads/master`; the empty `eventFilterForPush` makes the ref filter the single source of truth).

### Why This Is Dangerous

- The push failure is silent. No error, no warning in logs.
- The build succeeds, the image exists locally, but never reaches the registry.
- Downstream steps that `docker pull` the image fail with a misleading "not found" error.
- The bug only manifests on `schedule` / `workflow_dispatch` triggers, making it hard to catch on regular PR runs.

## Required Workflow Patterns

### GHCR Lowercase Repository Name

GHCR requires lowercase image names. Always convert:

```yaml
- name: Set lowercase repository name
  id: repo
  run: |
    set -euo pipefail
    repo_lower=$(echo "${{ github.repository }}" | tr '[:upper:]' '[:lower:]')
    echo "repository_lowercase=${repo_lower}" >> "${GITHUB_OUTPUT}"
```

Then use `${{ steps.repo.outputs.repository_lowercase }}` in every `imageName` and `cacheFrom`.

### Debug Context Step

Include a debug step early in the workflow:

```yaml
- name: Debug workflow context
  run: |
    echo "Event name: ${{ github.event_name }}"
    echo "Ref: ${{ github.ref }}"
    echo "Actor: ${{ github.actor }}"
```

The repo's `wallstop-studios/com.wallstop-studios.dxmessaging` slug forces a lowercase conversion every time, so the debug step also surfaces a wrong-actor or wrong-ref problem before it consumes 15 minutes of runner time.

### Diagnostic Verification

When verifying a pushed image, include diagnostics:

```yaml
- name: Verify image
  run: |
    set -euo pipefail
    IMAGE="ghcr.io/${{ steps.repo.outputs.repository_lowercase }}/devcontainer:latest"
    echo "=== Local Docker images ==="
    docker images | grep devcontainer || echo "No local images found"
    echo "=== Pulling from GHCR ==="
    if docker pull "${IMAGE}"; then
        echo "Image successfully pulled from GHCR"
    else
        echo "ERROR: Failed to pull from GHCR. Push may have failed." >&2
        exit 1
    fi
```

`devcontainer-prebuild.yml` already includes this step. It is the only check that catches a silently-failed push when `eventFilterForPush` was forgotten.

## Common Pitfalls

| Pitfall                                                  | Impact                                                 | Prevention                                            |
| -------------------------------------------------------- | ------------------------------------------------------ | ----------------------------------------------------- |
| Missing `eventFilterForPush: ""`                         | Silent push failure on schedule / dispatch             | Always set it explicitly                              |
| Using `${{ github.repository }}` directly in image names | Case mismatch breaks GHCR                              | Always lowercase convert via the `set lowercase` step |
| `cacheFrom` on first build                               | Cache miss warnings (non-fatal)                        | Expected on bootstrap; no action needed               |
| Double `devcontainers/ci` steps building same image      | Wasted CI time                                         | Use `cacheFrom` for the second step                   |
| No debug context step                                    | Hard to diagnose trigger-related issues                | Always add the debug step                             |
| Referencing gitignored files in workflow `paths:`        | CI-only failure: file exists locally but not on runner | Run the workflow path linter before merging           |
| `permissions: packages: write` missing                   | Push fails with 403                                    | Add it at the job level for prebuild workflows        |

## Related Skills

- [Devcontainer Cache Contract](../unity/devcontainer-cache-contract.md)
- [Headless Test Runner](../unity/headless-test-runner.md)
- [Unity CI Matrix](../unity/unity-ci-matrix.md)

## References

- `devcontainers/ci` action: https://github.com/devcontainers/ci
- GHCR docs: https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-container-registry
- Source: `.github/workflows/devcontainer-prebuild.yml`, `.github/workflows/devcontainer-test.yml`
