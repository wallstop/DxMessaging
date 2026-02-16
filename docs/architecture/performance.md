# Performance Benchmarks

This page is auto-updated by the Unity PlayMode benchmark tests in the [Performance PlayMode benchmark suite](https://github.com/wallstop/DxMessaging/blob/master/Tests/Runtime/Benchmarks/PerformanceTests.cs).

How it works:

- Run PlayMode tests locally in your Unity project that references this package.
- The benchmark test writes an OS-specific section below with a markdown table.
- CI runs skip writing to avoid noisy diffs.

See also: [Performance optimizations](./design-and-architecture.md#performance-optimizations) for design details.

## Benchmark Methodology and Caveats

These benchmarks measure raw message dispatch throughput using a simple counter-increment handler. Each test runs for 5 seconds, dispatching messages in batches of 10,000 operations per iteration with a pre-warm phase to avoid cold-start effects.

### Important considerations

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
| Unity                                      | 2,576,000           | Yes          |
| DxMessaging (GameObject) - Normal          | 10,264,000          | No           |
| DxMessaging (Component) - Normal           | 10,086,000          | No           |
| DxMessaging (GameObject) - No-Copy         | 11,552,000          | No           |
| DxMessaging (Component) - No-Copy          | 11,266,000          | No           |
| DxMessaging (Untargeted) - No-Copy         | 16,892,000          | No           |
| DxMessaging (Untargeted) - Interceptors    | 7,628,000           | No           |
| DxMessaging (Untargeted) - Post-Processors | 6,562,000           | No           |
| Reflexive (One Argument)                   | 2,868,000           | No           |
| Reflexive (Two Arguments)                  | 2,386,000           | No           |
| Reflexive (Three Arguments)                | 2,372,000           | No           |

## macOS

Run the PlayMode benchmarks on macOS to populate this section.

## Linux

Run the PlayMode benchmarks on Linux to populate this section.
