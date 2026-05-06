# Performance Benchmarks

This page documents the T0 throughput benchmark policy and keeps the older
PlayMode benchmark tables for broad historical context.

See also: [Performance optimizations](./design-and-architecture.md#performance-optimizations)
for design details.

## T0 Benchmark Methodology

The T0 harness measures raw dispatch throughput before any hot-path runtime
changes land. It is intentionally narrow: warm up each scenario, measure a
1-second window, repeat five times, and compare the median. Each row records:

- Scenario name.
- Platform identity, including editor/player target, scripting backend,
  architecture, build configuration, Unity platform, and Unity version. Common
  cells include Editor Mono x64, Standalone Mono x64, and Standalone IL2CPP
  x64.
- Commit SHA.
- Run index.
- Emits per second for dispatch scenarios.
- `GC.GetAllocatedBytesForCurrentThread()` delta.
- Wall-clock milliseconds for registration scenarios.

Run the runtime benchmark category from a Unity 2021.3 LTS or newer editor
checkout:

```bash
unity -batchmode -runTests -testPlatform playmode -testCategory "PerfBench"
```

Use the project-specific Unity executable path if `unity` is not on `PATH`.
Keep the editor version, scripting backend, CPU governor, and machine load
stable across before/after runs. Close the Unity editor UI before batchmode
runs so the benchmark process owns the editor session.

The T0 scenarios cover these paths:

| Scenario                                      | What it measures                                         |
| --------------------------------------------- | -------------------------------------------------------- |
| `UntargetedFlood_OneHandler`                  | One untargeted handler on one message type.              |
| `UntargetedFlood_FourHandlers_OnePriority`    | Four untargeted handlers sharing priority 0.             |
| `UntargetedFlood_FourHandlers_FourPriorities` | Four untargeted handlers across priorities 0-3.          |
| `TargetedFlood_OneListener`                   | One targeted listener on one target.                     |
| `TargetedFlood_SixteenListeners`              | Sixteen targeted listeners on one target.                |
| `BroadcastFlood_OneHandler`                   | One broadcast handler.                                   |
| `InterceptorHeavy_FourInterceptors`           | Four interceptors plus one handler.                      |
| `PostProcessingHeavy_FourPostProcessors`      | Four post-processors plus one handler.                   |
| `RegistrationFlood_1000Types_FromColdBus`     | Registering 1000 distinct message types from a cold bus. |

## Baseline Capture

Capture baselines into `progress/perf-baseline-2026-05-05.csv`. The baseline
file should be updated in a dedicated measurement commit, separate from runtime
changes.

Required commit cells:

| Commit    | Purpose                                |
| --------- | -------------------------------------- |
| `25a4dcc` | Pre-GC parent baseline.                |
| `29a5338` | First-pass garbage-collection landing. |
| `HEAD`    | Current branch result.                 |

Required configuration cells:

| Configuration         | Requirement                                         |
| --------------------- | --------------------------------------------------- |
| Editor Mono           | Required.                                           |
| Standalone Mono x64   | Required when a Mono build host is available.       |
| Standalone IL2CPP x64 | Stretch, required when CI has an IL2CPP build host. |

For each commit and configuration:

- Keep the T0 harness/worktree available; older runtime commits do not contain
  the benchmark harness.
- Measure the older runtime with a harness-preserving flow. Use a throwaway
  branch that cherry-picks the T0 harness onto the measured runtime commit, or
  keep the harness branch checked out and swap only the runtime files being
  measured.
- Set `DX_PERF_COMMIT=<measured-runtime-commit>` for every benchmark run so
  CSV rows identify the runtime commit under test. `DX_PERF_COMMIT` overrides
  CI's `GITHUB_SHA` when both are present.
- Run the PlayMode `PerfBench` category in batchmode.
- Append the structured output to `progress/perf-baseline-2026-05-05.csv`.
- Record the exact commit, platform, Unity version, and scripting backend.

Do not mix methodology changes with baseline updates. If the harness changes,
capture a new baseline and make the old/new methodology boundary explicit in
the PR description.

## Budget Interpretation

Dispatch budgets are interpreted in per-emit terms. Convert throughput to
nanoseconds per emit with:

```text
ns_per_emit = 1,000,000,000 / emits_per_second
```

Compare both throughput and per-emit nanoseconds. Throughput is easier to scan,
but per-emit nanoseconds makes fixed overhead visible. A 10 ns increase is
material on handlers whose work is only 10-20 ns.

Allocation budgets are interpreted as bytes allocated during the measured
window. Dispatch scenarios should stay at zero measured bytes after warmup.
Any non-zero allocation delta on a hot-path dispatch scenario requires an
explanation, a fix, or an explicit reviewer-approved exception.

The opt-in smoke gate uses `progress/perf-baseline-2026-05-05.csv`, requires an
exact `25a4dcc` row for the current scenario and platform identity, and fails
when a within-platform regression exceeds the configured threshold. Enable it
with:

```bash
DX_PERF_GATE=1 unity -batchmode -runTests -testPlatform editmode -testCategory "PerfGate"
```

The smoke gate is an EditMode test category. The T1 clock-read scaffold remains
`[Test, Explicit]` before T1.1, but it is not tagged `PerfGate` until the
sample-not-call sweep gate lands.
Before T0.3 baseline capture creates `progress/perf-baseline-2026-05-05.csv`,
the smoke gate reports an inconclusive skip instead of failing the suite for a
missing baseline file.

## Hot-Path PR Rule

Any pull request that touches one of these paths must include before/after T0
numbers in the PR description under `### Performance numbers`:

- `Runtime/Core/MessageBus/MessageBus.cs`
- `Runtime/Core/MessageHandler.cs`
- `Runtime/Core/Pooling/**`

Use this shape:

```markdown
### Performance numbers

| Scenario                                 | Baseline (commit 25a4dcc) | This PR          | Delta |
| ---------------------------------------- | ------------------------- | ---------------- | ----- |
| UntargetedFlood_OneHandler (Mono Editor) | X.XX M emits/sec          | Y.YY M emits/sec | +Z.Z% |
```

The workflow accepts either the table shape above with at least one populated
data row or one of these one-line `N/A` forms:

```text
N/A - refactor only <one-line justification>
N/A - non-hot-path edit only <one-line description>
```

The justification or description must be on the same line as the `N/A` marker.
Bare `N/A`, empty sections, and template-only comments do not satisfy the
workflow gate.

## Historical PlayMode Benchmarks

The sections below are auto-updated by the Unity PlayMode benchmark tests in
the [Performance PlayMode benchmark suite](https://github.com/wallstop/DxMessaging/blob/master/Tests/Editor/Benchmarks/PerformanceTests.cs).

How it works:

- Run PlayMode tests locally in your Unity project that references this
  package.
- The benchmark test writes an OS-specific section below with a markdown table.
- CI runs skip writing to avoid noisy diffs.

### Benchmark Methodology and Caveats

These older benchmarks measure raw message dispatch throughput using a simple
counter-increment handler. Each test runs for 5 seconds, dispatching messages in
batches of 10,000 operations per iteration with a pre-warm phase to avoid
cold-start effects.

#### Important considerations

- Results will vary based on your hardware, Unity version, and runtime environment.
- The benchmarks test isolated message dispatch with minimal handler logic. Real-world performance depends heavily on what your handlers actually do.
- The "Unity" baseline uses `GameObject.SendMessage()`, which performs string-based method lookup and allocates memory. Direct method calls would be faster than any messaging system.
- "Allocations?" indicates whether the test detected GC allocations during message dispatch under test conditions.

#### Performance tradeoffs to be aware of

- Interceptors and post-processors add overhead. With 8 interceptors registered, throughput drops to roughly 45% of the no-interceptor baseline. With 8 post-processors, throughput drops to roughly 38%. This is an expected tradeoff for the additional flexibility these features provide.
- Reflexive messaging (dynamic method invocation) is slower than direct handler registration due to the reflection overhead.

You can run these benchmarks yourself to get results specific to your environment. The source code is available in the test suite linked above.

## Windows

| Message Tech                               | Operations / Second | Allocations? |
| ------------------------------------------ | ------------------- | ------------ |
| Unity                                      | 2,387,729           | Yes          |
| DxMessaging (GameObject) - Normal          | 10,069,781          | No           |
| DxMessaging (Component) - Normal           | 9,958,399           | No           |
| DxMessaging (GameObject) - No-Copy         | 11,369,437          | No           |
| DxMessaging (Component) - No-Copy          | 8,576,809           | No           |
| DxMessaging (Untargeted) - No-Copy         | 17,393,604          | No           |
| DxMessaging (Untargeted) - Interceptors    | 7,055,588           | No           |
| DxMessaging (Untargeted) - Post-Processors | 6,534,681           | No           |
| Reflexive (One Argument)                   | 2,749,645           | No           |
| Reflexive (Two Arguments)                  | 2,311,295           | No           |
| Reflexive (Three Arguments)                | 2,300,900           | No           |

## macOS

Run the PlayMode benchmarks on macOS to populate this section.

## Linux

Run the PlayMode benchmarks on Linux to populate this section.
