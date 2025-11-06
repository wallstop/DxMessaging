# Performance Benchmarks

This page is auto-updated by the Unity PlayMode benchmark tests in the [Performance PlayMode benchmark suite](../Tests/Runtime/Benchmarks/PerformanceTests.cs).

How it works:

- Run PlayMode tests locally in your Unity project that references this package.
- The benchmark test writes an OS-specific section below with a markdown table.
- CI runs skip writing to avoid noisy diffs.

See also: [Performance optimizations](./DesignAndArchitecture.md#performance-optimizations) for design details.

## Windows

| Message Tech                               | Operations / Second | Allocations? |
| ------------------------------------------ | ------------------- | ------------ |
| Unity                                      | 2,520,000           | Yes          |
| DxMessaging (GameObject) - Normal          | 8,570,000           | No           |
| DxMessaging (Component) - Normal           | 8,556,000           | No           |
| DxMessaging (GameObject) - No-Copy         | 9,464,000           | No           |
| DxMessaging (Component) - No-Copy          | 9,600,000           | No           |
| DxMessaging (Untargeted) - No-Copy         | 25,394,000          | No           |
| DxMessaging (Untargeted) - Interceptors    | 8,404,000           | No           |
| DxMessaging (Untargeted) - Post-Processors | 5,988,000           | No           |
| Reflexive (One Argument)                   | 2,844,000           | No           |
| Reflexive (Two Arguments)                  | 2,368,000           | No           |
| Reflexive (Three Arguments)                | 2,372,000           | No           |

## macOS

Run the PlayMode benchmarks on macOS to populate this section.

## Linux

Run the PlayMode benchmarks on Linux to populate this section.
