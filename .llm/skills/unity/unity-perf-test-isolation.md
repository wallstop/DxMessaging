---
title: "Unity Perf Test Isolation"
id: "unity-perf-test-isolation"
category: "unity"
version: "1.0.0"
created: "2026-05-05"
updated: "2026-05-05"

source:
  repository: "wallstop/DxMessaging"
  files:
    - path: "scripts/unity/lib/asmdef-discovery.js"
    - path: ".github/workflows-disabled/unity-tests.yml"
    - path: ".github/workflows-disabled/unity-benchmarks.yml"
    - path: "scripts/unity/run-tests.sh"
    - path: ".llm/context.md"
  url: "https://github.com/wallstop/DxMessaging"

tags:
  - "unity"
  - "performance"
  - "benchmarks"
  - "isolation"
  - "asmdef"
  - "ci"

complexity:
  level: "intermediate"
  reasoning: "Requires understanding the asmdef classification regex and the cross-cutting workflow / runner / context split."

impact:
  performance:
    rating: "high"
    details: "Keeps the default local run small by excluding perf suites that would otherwise dominate the runtime"
  maintainability:
    rating: "high"
    details: "Single regex governs classification across runner, CI, and contract test"
  testability:
    rating: "high"
    details: "Phase 4 contract test exercises every asmdef and asserts correct classification"

prerequisites:
  - "Familiarity with Unity asmdef files"
  - "Awareness of the package's perf isolation rule (.llm/context.md)"

dependencies:
  packages: []
  skills:
    - "headless-test-runner"
    - "unity-ci-matrix"

applies_to:
  languages:
    - "JavaScript"
    - "YAML"
    - "JSON"
  frameworks:
    - "Unity"
  versions:
    unity: ">=2021.3"

aliases:
  - "Perf isolation"
  - "Benchmark exclusion"
  - "asmdef classification"

related:
  - "headless-test-runner"
  - "unity-ci-matrix"
  - "upm-test-harness"

status: "stable"
---

<!-- trigger: unity, perf, benchmark, allocation, comparison, isolation, asmdef | Perf-asmdef classification and default-run exclusion contract | Core -->

# Unity Perf Test Isolation

> **One-line summary**: Asmdefs whose name matches `Benchmarks|Allocations` are classified as `perf`; `Comparisons` assemblies are a separate external-package opt-in. Both are excluded from default local Unity runs by `scripts/unity/lib/asmdef-discovery.js`.

## When to Use

- Adding a new benchmark, allocation-counting, or library-comparison test suite.
- Investigating why a perf-looking asmdef does or does not run on a PR.
- Debugging a "0 tests ran" CI failure when the suite name pattern is suspect.
- Verifying the default Unity run still excludes perf after a refactor.

## When NOT to Use

- Adding a regular correctness test. Those are `core` and run by default; no isolation work is needed.
- Adding a DI integration suite (VContainer / Zenject / Reflex). Those have their own classification (`integration`) and opt-in flag.

## The Rule

Source-of-truth is `.llm/context.md` line 114:

> Benchmark and performance/allocation tests must stay isolated from the standard test suite.

Operationally, this is enforced by classification in `scripts/unity/lib/asmdef-discovery.js`:

```js
const PERF_NAME_REGEX = /(?:Benchmarks|Allocations)/;
const COMPARISON_NAME_REGEX = /(?:Comparisons)/;
```

Any asmdef under `Tests/` whose `name` field contains `Benchmarks` or `Allocations` is classified as `perf`; `Comparisons` is classified as `comparison` because those suites depend on external comparison packages that are not in the default harness manifest. The perf path assumes `.unity-test-project/Packages/manifest.json` includes `com.unity.test-framework.performance`, because benchmark and allocation asmdefs reference `Unity.PerformanceTesting`. Three things have to be true for the isolation to hold:

1. Perf assemblies live under `Tests/Editor/Benchmarks`, `Tests/Editor/Allocations`, `Tests/Editor/Comparisons`, or `Tests/Runtime/Benchmarks`.
1. Their asmdef `name` field contains `Benchmarks`, `Allocations`, or `Comparisons` so classification matches.
1. They are NOT mentioned by name in any workflow's `customParameters`. The workflow reads its assembly list from `defaultIncludeAssemblies()`, never from a hand-edited list.

## How Exclusion Works

`scripts/unity/lib/asmdef-discovery.js` exports `defaultIncludeAssemblies(repoRoot, options)`. The behaviour:

| Asmdef Class  | Default Include? | Opt-in Flag                                                 |
| ------------- | ---------------- | ----------------------------------------------------------- |
| `core`        | Yes              | (always on)                                                 |
| `perf`        | No               | `{ includePerf: true }` or `--include-perf`                 |
| `comparison`  | No               | `{ includeComparisons: true }` or `--include-comparisons`   |
| `integration` | No               | `{ includeIntegrations: true }` or `--include-integrations` |

Three callers consume this module:

- `scripts/unity/run-tests.sh` builds its assembly list at startup and passes it to Unity via `-assemblyNames`.
- `scripts/unity/run-tests.ps1` does the same on Windows.
- Disabled workflow templates under `.github/workflows-disabled/` still shell out to the same asmdef-discovery module so they can be re-enabled without hand-maintained lists.
- The disabled `unity-benchmarks.yml` template calls `defaultIncludeAssemblies(process.cwd(), { includePerf: true })` and skips integrations plus external comparisons.

Because every caller goes through the same module, adding a new perf asmdef requires no edits to the workflows or runner scripts.

## Adding a New Perf Asmdef

1. Place the asmdef under `Tests/Editor/Benchmarks/`, `Tests/Editor/Allocations/`, or `Tests/Runtime/Benchmarks/`.
1. Set its `name` field to include one of the magic substrings. Examples that match:
   - `WallstopStudios.DxMessaging.Tests.Editor.Benchmarks.Dispatch`
   - `WallstopStudios.DxMessaging.Tests.Runtime.Allocations.Pooling`
1. Verify classification:

   ```bash
   node scripts/unity/lib/asmdef-discovery.js
   ```

   The output groups asmdefs by category. Confirm the new entry shows `[perf]`.

1. Confirm the default run excludes it:

   ```bash
   bash scripts/unity/run-tests.sh --platform editmode
   ```

   The runner echoes the resolved assembly list at startup; the new asmdef should NOT appear.

1. Confirm the benchmark workflow includes it:

   ```bash
   bash scripts/unity/run-tests.sh --platform editmode --include-perf
   ```

   The new asmdef should now appear in the resolved list.

If the asmdef ends up in the `core` bucket instead, the most common cause is the `name` field missing the magic substring. Rename the asmdef (and its file) so the substring is present.

## Where Perf Actually Runs

| Workflow               | Triggers                         | Includes Perf? |
| ---------------------- | -------------------------------- | -------------- |
| `unity-tests.yml`      | Moved out of `.github/workflows` | NO             |
| `unity-il2cpp.yml`     | Moved out of `.github/workflows` | NO             |
| `unity-benchmarks.yml` | Moved out of `.github/workflows` | YES            |

Unity game-ci jobs are temporarily disabled in GitHub by moving them to
`.github/workflows-disabled/` and kept local-only via `scripts/unity/run-tests.sh`.
Verify any time you edit the workflow templates:

```bash
test ! -e .github/workflows/unity-tests.yml
test ! -e .github/workflows/unity-il2cpp.yml
test ! -e .github/workflows/unity-benchmarks.yml
```

## Comparison Suites

Comparison asmdefs live under `Tests/Editor/Comparisons/` and benchmark against external libraries such as MessagePipe, UniRx, UniTask, and Zenject. They are excluded from `--include-perf` because the default `.unity-test-project/Packages/manifest.json` does not install those packages. To run them locally, add the external packages to the harness manifest and pass:

```bash
bash scripts/unity/run-tests.sh --platform editmode --include-comparisons
```

The runner should print `comparisons=true` and include the comparison asmdef in the resolved assembly list. Unity will only compile that asmdef when the external comparison packages are installed, because its package-driven define constraints guard the whole assembly.

## Phase 4 Contract Test

`scripts/__tests__/unity-perf-isolation.test.js` (Phase 4B) enumerates every asmdef under `Tests/` and asserts:

- Every asmdef matching the perf regex is classified as `perf`.
- Every asmdef NOT matching the perf or integration regex is classified as `core` and appears in `defaultIncludeAssemblies(repo)`.
- Disabled `unity-tests.yml` and `unity-il2cpp.yml` templates resolve their assembly lists via `defaultIncludeAssemblies` rather than hand-rolled YAML.
- Disabled `unity-benchmarks.yml` template opts into perf via `{ includePerf: true }`.

The test catches the silent regression "I added a new perf asmdef and forgot to update the exclusion list" because the exclusion list is computed, not hand-maintained.

## See Also

- [Headless Test Runner](./headless-test-runner.md)
- [Unity CI Matrix](./unity-ci-matrix.md)
- [UPM Test Harness](./upm-test-harness.md)
- [Devcontainer Cache Contract](./devcontainer-cache-contract.md)

## References

- Source: `scripts/unity/lib/asmdef-discovery.js`
- Source-of-truth: `.llm/context.md`
- Workflow templates: `.github/workflows-disabled/unity-tests.yml`, `.github/workflows-disabled/unity-benchmarks.yml`
