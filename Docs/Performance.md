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
| Unity                                      | 2,674,000           | Yes          |
| DxMessaging (GameObject) - Normal          | 8,516,000           | No           |
| DxMessaging (Component) - Normal           | 8,512,000           | No           |
| DxMessaging (GameObject) - No-Copy         | 9,424,000           | No           |
| DxMessaging (Component) - No-Copy          | 9,524,000           | No           |
| DxMessaging (Untargeted) - No-Copy         | 14,734,000          | No           |
| DxMessaging (Untargeted) - Interceptors    | 6,538,000           | No           |
| DxMessaging (Untargeted) - Post-Processors | 5,260,000           | No           |
| Reflexive (One Argument)                   | 2,826,000           | No           |
| Reflexive (Two Arguments)                  | 2,358,000           | No           |
| Reflexive (Three Arguments)                | 2,320,000           | No           |

## macOS

Run the PlayMode benchmarks on macOS to populate this section.

## Linux

Run the PlayMode benchmarks on Linux to populate this section.
