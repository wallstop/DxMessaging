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
| Unity                                      | 2,498,000           | Yes          |
| DxMessaging (GameObject) - Normal          | 10,260,000          | No           |
| DxMessaging (Component) - Normal           | 10,172,000          | No           |
| DxMessaging (GameObject) - No-Copy         | 11,600,000          | No           |
| DxMessaging (Component) - No-Copy          | 11,598,000          | No           |
| DxMessaging (Untargeted) - No-Copy         | 19,596,000          | No           |
| DxMessaging (Untargeted) - Interceptors    | 7,716,000           | No           |
| DxMessaging (Untargeted) - Post-Processors | 5,622,000           | No           |
| Reflexive (One Argument)                   | 2,862,000           | No           |
| Reflexive (Two Arguments)                  | 2,300,000           | No           |
| Reflexive (Three Arguments)                | 2,328,000           | No           |

## macOS

Run the PlayMode benchmarks on macOS to populate this section.

## Linux

Run the PlayMode benchmarks on Linux to populate this section.
