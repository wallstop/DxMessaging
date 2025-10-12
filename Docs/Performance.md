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
| Unity | 2,575,600 | Yes |
| DxMessaging (GameObject) - Normal | 8,957,000 | No |
| DxMessaging (Component) - Normal | 8,959,400 | No |
| DxMessaging (GameObject) - No-Copy | 10,068,000 | No |
| DxMessaging (Component) - No-Copy | 10,107,000 | No |
| DxMessaging (Untargeted) - No-Copy | 15,899,800 | No |
| Reflexive (One Argument) | 2,849,000 | No |
| Reflexive (Two Arguments) | 2,288,800 | No |
| Reflexive (Three Arguments) | 2,242,200 | No |

## macOS

Run the PlayMode benchmarks on macOS to populate this section.

## Linux

Run the PlayMode benchmarks on Linux to populate this section.

