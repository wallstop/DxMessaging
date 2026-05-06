---
title: "UPM Test Harness"
id: "upm-test-harness"
category: "unity"
version: "1.0.0"
created: "2026-05-05"
updated: "2026-05-05"

source:
  repository: "wallstop/DxMessaging"
  files:
    - path: ".unity-test-project/Packages/manifest.json"
    - path: ".unity-test-project/Packages/packages-lock.json"
    - path: ".unity-test-project/ProjectSettings/ProjectVersion.txt"
    - path: ".unity-test-project/Assets/Editor/TestRunnerBuilder.cs"
    - path: ".unity-test-project/Assets/Editor/WallstopStudios.DxMessaging.TestHarness.Editor.asmdef"
  url: "https://github.com/wallstop/DxMessaging"

tags:
  - "unity"
  - "upm"
  - "test-harness"
  - "manifest"
  - "testables"

complexity:
  level: "basic"
  reasoning: "Five committed files; standard UPM testables semantics."

impact:
  performance:
    rating: "none"
    details: "Test infrastructure only"
  maintainability:
    rating: "high"
    details: "Pinned manifest + lock + ProjectVersion guarantees reproducible test runs across machines and CI"
  testability:
    rating: "high"
    details: "Without this harness, the package has no Unity surface to execute against"

prerequisites:
  - "Familiarity with Unity Package Manager (UPM)"
  - "Awareness of asmdef structure"

dependencies:
  packages: []
  skills:
    - "headless-test-runner"

applies_to:
  languages:
    - "JSON"
    - "C#"
  frameworks:
    - "Unity"
  versions:
    unity: ">=2021.3"

aliases:
  - "Unity test project"
  - ".unity-test-project"
  - "Test harness"

related:
  - "headless-test-runner"
  - "unity-ci-matrix"
  - "unity-perf-test-isolation"

status: "stable"
---

<!-- trigger: unity, upm, manifest, testables, harness, test-project | UPM test harness for executing the package's Tests asmdefs | Core -->

# UPM Test Harness

> **One-line summary**: `.unity-test-project/` is a thin Unity host project whose only job is to import the package via `file:../..` and expose its `Tests/` asmdefs through the UPM `testables` field; everything else is gitignored.

## When to Use

- Adding a new `.asmdef` under `Tests/` and verifying it shows up in Test Runner.
- Reproducing a CI failure that needs a working Unity project on disk.
- Adding a UPM dependency that the test asmdefs need (e.g., a future Reflex DI integration).
- Diagnosing "Test framework not found" or empty test-run failures.

## When NOT to Use

- Editing source files for the package itself. Those live at the repo root (`Runtime/`, `Editor/`, `Tests/`); this harness only references them.
- Adding regular `Assets/` content. The harness intentionally ships exactly one `Assets/Editor` file and no scenes, sprites, or prefabs.

## Architecture

```text
repo-root/
+-- package.json                  # the DxMessaging UPM package manifest
+-- Runtime/                      # package source (asmdefs)
+-- Editor/                       # package source (asmdefs)
+-- Tests/
|   +-- Editor/                   # NUnit + UTF tests (asmdefs)
|   +-- Runtime/                  # PlayMode tests (asmdefs)
+-- .unity-test-project/          # thin host that imports the package
    +-- Packages/
    |   +-- manifest.json         # "com.wallstop-studios.dxmessaging": "file:../..",
    |   |                         # plus "testables" exposing the package's Tests
    |   +-- packages-lock.json    # committed for deterministic resolution
    +-- ProjectSettings/
    |   +-- ProjectVersion.txt    # pinned to 2022.3.45f1
    +-- Assets/
    |   +-- Editor/
    |       +-- TestRunnerBuilder.cs            # IL2CPP build entry point
    |       +-- WallstopStudios.DxMessaging.TestHarness.Editor.asmdef
    +-- Library/                  # gitignored, populated on first run
    +-- Temp/                     # gitignored
    +-- Logs/                     # gitignored
    +-- Builds/                   # gitignored, IL2CPP outputs land here
```

The shape is deliberate. UPM resolves `file:../..` to the repo root, the package surfaces its asmdefs, and the `testables` array tells Unity Test Framework to scan that package's Tests assemblies. The harness has zero application code.

## Key Files

| File                                                                  | Role                                                                                 |
| --------------------------------------------------------------------- | ------------------------------------------------------------------------------------ |
| `Packages/manifest.json`                                              | Declares the package via `file:../..` and lists it under `testables`.                |
| `Packages/packages-lock.json`                                         | Committed so the test environment resolves identically across machines and CI.       |
| `ProjectSettings/ProjectVersion.txt`                                  | Pinned Editor version. CI cache keys hash this; bumping it busts the Library cache.  |
| `Assets/Editor/TestRunnerBuilder.cs`                                  | `BuildPipeline.BuildPlayer` entry point invoked by `-executeMethod` for IL2CPP runs. |
| `Assets/Editor/WallstopStudios.DxMessaging.TestHarness.Editor.asmdef` | Editor-only asmdef that owns the `TestRunnerBuilder` class.                          |

The current `manifest.json` (verbatim):

```json
{
  "dependencies": {
    "com.unity.test-framework": "1.4.5",
    "com.unity.test-framework.performance": "3.4.2",
    "com.unity.ide.rider": "3.0.31",
    "com.unity.ide.visualstudio": "2.0.22",
    "com.wallstop-studios.dxmessaging": "file:../.."
  },
  "scopedRegistries": [],
  "testables": ["com.wallstop-studios.dxmessaging"]
}
```

## What to Commit vs Gitignore

Committed:

- `Packages/manifest.json`
- `Packages/packages-lock.json`
- `ProjectSettings/ProjectVersion.txt`
- `Assets/Editor/TestRunnerBuilder.cs` and its `.meta`
- `Assets/Editor/WallstopStudios.DxMessaging.TestHarness.Editor.asmdef` and its `.meta`

Gitignored (the repo `.gitignore` already covers these):

- `.unity-test-project/Library/`
- `.unity-test-project/Temp/`
- `.unity-test-project/Logs/`
- `.unity-test-project/Builds/`
- `.unity-test-project/UserSettings/`
- `.unity-test-project/obj/`

`Library/` is shared with the runner via a Docker volume whose name is derived from the Unity image tag and test mode (for example, `dxm-unity-library-2022.3.45f1-base-3-editmode`). This keeps local caches warm without allowing one Unity version or IL2CPP/editor mode to reuse another mode's `Library/`.

## Adding a New Test Asmdef

The `testables` field exposes every asmdef under the package's `Tests/` directory automatically; no harness change is needed for typical additions:

1. Create the asmdef under `Tests/Editor/<NewSuite>/` (or `Tests/Runtime/<NewSuite>/`).
1. Set its `name` to a stable, descriptive identifier (e.g., `WallstopStudios.DxMessaging.Tests.Editor.NewSuite`).
1. Verify it shows up in the runner's discovery:

   ```bash
   node scripts/unity/lib/asmdef-discovery.js
   ```

1. Re-run the headless runner to confirm the new tests execute:

   ```bash
   bash scripts/unity/run-tests.sh --platform editmode
   ```

If the new asmdef is a perf or DI-integration suite, name it accordingly (`*Benchmarks*`, `*Allocations*`, `*Comparisons*`, `*VContainer*`, `*Zenject*`, `*Reflex*`). The classification regex in `scripts/unity/lib/asmdef-discovery.js` will excluded it from the default include list. See [unity-perf-test-isolation](./unity-perf-test-isolation.md).

## Adding a Test Dependency

When a new test suite needs an additional UPM package (a DI container, a third-party assertion library, etc.):

1. Add it to `.unity-test-project/Packages/manifest.json` `dependencies` block.
1. Open the harness in Unity once locally so UPM can resolve and write `packages-lock.json`. Commit the regenerated lock.
1. Re-run the headless runner to confirm the new dependency loads cleanly.

Avoid adding heavyweight runtime dependencies unless the corresponding tests can opt-in via the `--include-integrations` flag. The default suite stays lean so the PR gate stays under ~5 minutes.

## See Also

- [Headless Test Runner](./headless-test-runner.md)
- [Unity CI Matrix](./unity-ci-matrix.md)
- [Unity Perf Test Isolation](./unity-perf-test-isolation.md)

## References

- Unity Package Manager testables: https://docs.unity3d.com/Manual/cus-tests.html
- Unity Test Framework: https://docs.unity3d.com/Packages/com.unity.test-framework@1.4/manual/index.html
- Source: `.unity-test-project/Packages/manifest.json`
