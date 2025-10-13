# Performance Benchmarks

This page is auto-updated by the Unity PlayMode benchmark tests in `Tests/Runtime/Benchmarks/PerformanceTests.cs`.

How it works:

- Run PlayMode tests locally in your Unity project that references this package.
- The benchmark test writes an OS-specific section below with a markdown table.
- CI runs skip writing to avoid noisy diffs.

See also: `Docs/DesignAndArchitecture.md#performance-optimizations` for design details.

## Windows

| Message Tech                       | Operations / Second | Allocations? |
| ---------------------------------- | ------------------- | ------------ |
| Unity                              | 2,328,200           | Yes          |
| DxMessaging (GameObject) - Normal  | 8,288,600           | No           |
| DxMessaging (Component) - Normal   | 8,298,200           | No           |
| DxMessaging (GameObject) - No-Copy | 9,329,200           | No           |
| DxMessaging (Component) - No-Copy  | 9,350,800           | No           |
| DxMessaging (Untargeted) - No-Copy | 14,705,000          | No           |
| Reflexive (One Argument)           | 2,806,800           | No           |
| Reflexive (Two Arguments)          | 2,325,800           | No           |
| Reflexive (Three Arguments)        | 2,322,200           | No           |

## macOS

Run the PlayMode benchmarks on macOS to populate this section.

## Linux

Run the PlayMode benchmarks on Linux to populate this section.
