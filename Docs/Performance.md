# Performance Benchmarks

This page is auto-updated by the Unity PlayMode benchmark tests in `Tests/Runtime/Benchmarks/PerformanceTests.cs`.

How it works:

- Run PlayMode tests locally in your Unity project that references this package.
- The benchmark test writes an OS-specific section below with a markdown table.
- CI runs skip writing to avoid noisy diffs.

See also: `Docs/DesignAndArchitecture.md#performance-optimizations` for design details.

## Windows

| Message Tech | Operations / Second | Allocations? |
| ------------ | ------------------- | ------------ |
| Unity | 2,569,400 | Yes |
| DxMessaging (GameObject) - Normal | 8,955,600 | No |
| DxMessaging (Component) - Normal | 8,961,400 | No |
| DxMessaging (GameObject) - No-Copy | 9,745,000 | No |
| DxMessaging (Component) - No-Copy | 9,832,800 | No |
| DxMessaging (Untargeted) - No-Copy | 15,540,800 | No |
| Reflexive (One Argument) | 2,846,600 | No |
| Reflexive (Two Arguments) | 2,297,200 | No |
| Reflexive (Three Arguments) | 2,292,600 | No |

## macOS

Run the PlayMode benchmarks on macOS to populate this section.

## Linux

Run the PlayMode benchmarks on Linux to populate this section.
