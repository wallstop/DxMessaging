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
| Unity                                      | 2,594,000           | Yes          |
| DxMessaging (GameObject) - Normal          | 8,606,000           | No           |
| DxMessaging (Component) - Normal           | 8,584,000           | No           |
| DxMessaging (GameObject) - No-Copy         | 9,496,000           | No           |
| DxMessaging (Component) - No-Copy          | 9,574,000           | No           |
| DxMessaging (Untargeted) - No-Copy         | 15,828,000          | No           |
| DxMessaging (Untargeted) - Interceptors    | 7,428,000           | No           |
| DxMessaging (Untargeted) - Post-Processors | 5,400,000           | No           |
| Reflexive (One Argument)                   | 2,848,000           | No           |
| Reflexive (Two Arguments)                  | 2,328,000           | No           |
| Reflexive (Three Arguments)                | 2,346,000           | No           |

## macOS

Run the PlayMode benchmarks on macOS to populate this section.

## Linux

Run the PlayMode benchmarks on Linux to populate this section.
