---
title: "Unity CI Matrix"
id: "unity-ci-matrix"
category: "unity"
version: "1.0.0"
created: "2026-05-05"
updated: "2026-05-05"

source:
  repository: "Ambiguous-Interactive/DxMessaging"
  files:
    - path: ".github/workflows-disabled/unity-tests.yml"
    - path: ".github/workflows-disabled/unity-benchmarks.yml"
  url: "https://github.com/Ambiguous-Interactive/DxMessaging"

tags:
  - "unity"
  - "ci"
  - "matrix"
  - "il2cpp"
  - "lts"
  - "game-ci"

complexity:
  level: "intermediate"
  reasoning: "Requires understanding game-ci action shapes, IL2CPP-specific failure modes, and Unity LTS cadence."

impact:
  performance:
    rating: "low"
    details: "Each matrix cell costs about 5 minutes of runner time"
  maintainability:
    rating: "high"
    details: "One workflow per concern; the matrix is computed from a single dispatch input"
  testability:
    rating: "high"
    details: "Phase 4 contract test pins the workflow shape and asmdef discovery source-of-truth"

prerequisites:
  - "Familiarity with GitHub Actions matrix expansion"
  - "Awareness of Unity LTS release cadence"

dependencies:
  packages: []
  skills:
    - "headless-test-runner"
    - "unity-perf-test-isolation"

applies_to:
  languages:
    - "YAML"
  frameworks:
    - "GitHub Actions"
    - "Unity"
  versions:
    unity: ">=2021.3"

aliases:
  - "Unity matrix"
  - "IL2CPP gate"

related:
  - "headless-test-runner"
  - "unity-license-bootstrap"
  - "unity-perf-test-isolation"
  - "cicd-devcontainer-workflows"

status: "stable"
---

<!-- trigger: unity, ci, matrix, il2cpp, lts, game-ci, version | Unity version matrix and IL2CPP-only failure patterns | Core -->

# Unity CI Matrix

> **One-line summary**: The active Unity workflows under `.github/workflows/` run game-ci on self-hosted Windows runners: `unity-tests.yml` is one unified matrix of three Unity versions x {editmode, playmode, standalone} = 9 jobs, where `standalone` is the native game-ci `testMode: standalone` that builds and runs the AOT-compiled IL2CPP player (IL2CPP via ProjectSettings, runtime-only assemblies). The `.github/workflows-disabled/*` files remain the ubuntu reference mirrors.

## When to Use

- Adding a new Unity LTS release to the supported set.
- Triaging an IL2CPP-only test failure that does not reproduce in EditMode.
- Investigating a game-ci log that fails before any test prints output.
- Deciding whether to expand or contract the matrix to balance signal vs runtime.

## When NOT to Use

- Tweaking which assemblies run. That is the asmdef-discovery module's responsibility (see [unity-perf-test-isolation](./unity-perf-test-isolation.md)).
- Adjusting cache keys. Those live in the workflow's `actions/cache@v4` block; they hash `manifest.json` + `packages-lock.json` + `ProjectVersion.txt`.

## Current Matrix

`unity-tests.yml` (active; game-ci on self-hosted Windows; one unified matrix):

| Axis            | Values                                      |
| --------------- | ------------------------------------------- |
| `unity-version` | `2021.3.45f1`, `2022.3.45f1`, `6000.0.32f1` |
| `test-mode`     | `editmode`, `playmode`, `standalone`        |

Nine matrix cells. `editmode`/`playmode` run in-editor on Mono; `standalone` builds and runs the IL2CPP player natively via game-ci `testMode: standalone` (IL2CPP backend pinned in `.unity-test-project/ProjectSettings/ProjectSettings.asset` as `scriptingBackend: { Standalone: 1 }`, runtime-only assemblies because EditMode tests cannot run in a player). Workflow_dispatch inputs let you pin a single version or single mode for triage.

**Operator note (standalone IL2CPP image):** the `standalone` cells build a Windows IL2CPP player, which REQUIRES VS C++ Build Tools INSIDE the game-ci container. Host-installed Build Tools do NOT reach the container. Set the repo variable `UNITY_IL2CPP_WINDOWS_IMAGE` to a game-ci windows-il2cpp image that bundles them; `unity-tests.yml` wires it into the game-ci step's `customImage`. If `UNITY_IL2CPP_WINDOWS_IMAGE` is unset, `customImage` is empty and game-ci uses its stock image, so the IL2CPP build will FAIL LOUDLY (no Build Tools). `editmode`/`playmode` run in-editor and need no custom image.

`unity-benchmarks.yml` (active; manual/nightly, NEVER on PRs):

| Axis            | Values                 |
| --------------- | ---------------------- |
| `unity-version` | `2022.3.45f1`          |
| `test-mode`     | `editmode`, `playmode` |

The active `unity-benchmarks.yml` explicitly omits `pull_request` and `push` per the perf isolation rule.

## When to Add a Unity Version

Add a version to `unity-tests.yml`'s `unity-versions` JSON array when one of the following is true:

- A new LTS reaches general availability (e.g., when 2024.3 LTS or 7000.0 LTS ships) and the package's `package.json` `unity` field still permits it.
- A user files an issue reproducing only on a specific Editor version.
- Unity publishes a security patch on a currently-supported channel that the maintainer wants the gate to track.

## How to Add a Unity Version

1. Edit the active `.github/workflows/unity-tests.yml`. The matrix is computed in the `matrix-config` job:

   ```yaml
   versions='["2021.3.45f1","2022.3.45f1","6000.0.32f1"]'
   ```

   Append the new tag to the JSON array. Use the `unityci/editor` tag format (e.g., `2024.3.10f1`).

1. Verify the corresponding `unityci/editor:<tag>-base-3` image exists at `https://hub.docker.com/r/unityci/editor/tags` (the local `--platform standalone` driver uses `-linux-il2cpp-3`). game-ci publishes images shortly after Unity ships; if the tag is missing, wait or pick the nearest released patch.

1. Run the runner locally to validate the new version:

   ```bash
   bash scripts/unity/run-tests.sh --platform editmode --unity-version <new-version>
   ```

1. Push the workflow change. The first CI run will pull the new image (slow); subsequent runs hit the cache.

The `actions/cache@v4` keys include `${{ matrix.unity-version }}`, mode, manifest hash, lockfile hash, and `ProjectVersion.txt`. Do not add broad `restore-keys` for `Library/`; restoring a Library from a different Unity version or package graph can corrupt domain reloads and make failures nondeterministic. A new version should start cold and warm on the next exact-key run.

## IL2CPP-Only Failure Patterns

IL2CPP exercises an AOT-compiled path that EditMode/PlayMode under Mono cannot. These regressions historically slip past the Mono gate and only surface when a downstream consumer builds a player. The catalog:

| Pattern                                       | Signature in log                                                                   | Remediation                                                                                       |
| --------------------------------------------- | ---------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------- |
| Generic virtual method (GVM) call             | `ExecutionEngineException: Attempting to call method 'X' for which no AOT code...` | Add a non-generic forwarder, mark with `[Preserve]`, or instantiate the generic at compile time.  |
| Code stripping                                | `MissingMethodException` or `TypeLoadException` for a reflected type               | Add the type to `link.xml`, or annotate with `[Preserve]`. See Unity managed-code-stripping docs. |
| Reflection over open generics                 | Tests pass under Mono, fail under IL2CPP with reflection-related null returns      | Avoid open-generic reflection on the hot path; use the source generator instead.                  |
| Incremental Mono / IL2CPP serialization drift | `Library/` cache is stale and the build hangs at "Domain Reload"                   | Delete the Library cache (or bump the cache key prefix in the workflow); rebuild.                 |
| PInvoke / native-callable mismatch            | `EntryPointNotFoundException` or `MarshalAs` complaints unique to IL2CPP           | Audit `[DllImport]` signatures; verify calling convention.                                        |

The `avoid-reflection-on-hot-paths` skill (see Performance section of the index) covers reflection-related cases in detail. The DxMessaging codebase uses the source generator precisely to avoid most reflection at runtime.

## Reading game-ci Logs

A game-ci job log is structured. To diagnose a failure, scan in this order:

1. **Pre-Unity setup**: `Setup Node.js`, `Cache Unity Library`, `Compute test assembly list`. Failures here are infrastructure, not test logic.
1. **License activation**: search for `LICENSE SYSTEM` or `License client failed`. See [unity-license-bootstrap](./unity-license-bootstrap.md).
1. **Editor startup**: search for `[Licensing]` or `Loading native plugins`. A timeout here usually means a corrupted Library cache.
1. **Domain reload**: search for `Reloading assemblies`. A hang here typically means a circular asmdef reference or a missing dependency.
1. **Test execution**: search for `Run tests on platform`. NUnit failures appear as `[Test Failed]` lines with stack traces.
1. **Result emission**: search for `Test results saved at`. Missing results XML almost always means the player crashed before tests completed.

For `standalone` runs, game-ci's `testMode: standalone` builds the IL2CPP player and runs it in one step. Build-stage failures are AOT or stripping; run-stage failures are runtime AOT or test-logic. The shared `verify-unity-results` composite asserts `total > 0` for every mode (including standalone), so a crash mid-run that emits no results cannot look green.

## See Also

- [Headless Test Runner](./headless-test-runner.md)
- [Unity License Bootstrap](./unity-license-bootstrap.md)
- [Unity Perf Test Isolation](./unity-perf-test-isolation.md)
- [CI/CD Devcontainer Workflows](../github-actions/cicd-devcontainer-workflows.md)

## References

- game-ci docs: https://game.ci/docs/
- Unity LTS roadmap: https://unity.com/releases/lts
- Unity managed code stripping: https://docs.unity3d.com/Manual/ManagedCodeStripping.html
- Active workflows: `.github/workflows/unity-tests.yml` (game-ci on self-hosted Windows; editmode/playmode/standalone), `.github/workflows/unity-benchmarks.yml`; ubuntu reference mirrors: `.github/workflows-disabled/`
