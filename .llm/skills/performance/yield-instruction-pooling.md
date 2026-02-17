---
title: "WaitForSeconds and Yield Instruction Pooling"
id: "yield-instruction-pooling"
category: "performance"
version: "1.0.0"
created: "2026-01-21"
updated: "2026-01-21"

source:
  repository: "wallstop/unity-helpers"
  files:
    - path: "Runtime/Utils/Buffers.cs"
      lines: "148-467"
  url: "https://github.com/wallstop/unity-helpers"

tags:
  - "performance"
  - "unity"
  - "coroutines"
  - "pooling"
  - "zero-alloc"
  - "yield"

complexity:
  level: "basic"
  reasoning: "Simple caching pattern with straightforward API"

impact:
  performance:
    rating: "high"
    details: "Eliminates per-coroutine allocations for common wait instructions"
  maintainability:
    rating: "high"
    details: "Drop-in replacement for standard yield instructions"
  testability:
    rating: "high"
    details: "Coroutine behavior unchanged; only allocation differs"

prerequisites:
  - "Understanding of Unity coroutines"
  - "Knowledge of yield instructions"

dependencies:
  packages: []
  skills: []

applies_to:
  languages:
    - "C#"
  frameworks:
    - "Unity"
  versions:
    unity: ">=2021.3"

aliases:
  - "Cached WaitForSeconds"
  - "Coroutine optimization"
  - "Yield caching"

related:
  - "object-pooling"
  - "collection-pooling"

status: "stable"
---

# WaitForSeconds and Yield Instruction Pooling

> **One-line summary**: Cache and reuse Unity yield instructions like `WaitForSeconds` to eliminate per-coroutine allocations.

## Overview

Unity coroutines frequently use `yield return new WaitForSeconds(x)`, creating a new object each time. Since `WaitForSeconds` is reusable after completion, we can cache instances by duration to achieve zero-allocation coroutines.

## Problem Statement

```csharp
// BAD: Allocates 20 bytes every call
private IEnumerator SpawnEnemies()
{
    while (true)
    {
        SpawnEnemy();
        yield return new WaitForSeconds(2f); // New allocation!
    }
}
```

With 100 coroutines at 1 yield/second = 2KB/second = 7.2MB/hour of garbage.

## Solution

### Core Concept

```text
┌─────────────────────────────────────────────────────────────────┐
│                 WaitForSeconds Cache                             │
├─────────────────────────────────────────────────────────────────┤
│  Key (quantized seconds) │ Value (WaitForSeconds instance)      │
│  ───────────────────────────────────────────────────────────    │
│  0.0f                    │ WaitForSeconds(0.0f)                 │
│  0.1f                    │ WaitForSeconds(0.1f)                 │
│  0.5f                    │ WaitForSeconds(0.5f)                 │
│  1.0f                    │ WaitForSeconds(1.0f)                 │
│  2.0f                    │ WaitForSeconds(2.0f)                 │
│  ...                     │ ...                                   │
└─────────────────────────────────────────────────────────────────┘

GetWaitForSeconds(1.5f) → Quantize to 1.5f → Return cached instance
```

### Implementation

```csharp
namespace WallstopStudios.UnityHelpers.Utils
{
    using System.Collections.Generic;
    using UnityEngine;

    public static partial class Buffers
    {
        /// <summary>
        /// Quantization step for WaitForSeconds caching.
        /// Smaller = more precision, larger cache.
        /// </summary>
        public static float WaitInstructionQuantizationStepSeconds { get; set; } = 0.05f;

        private static readonly Dictionary<float, WaitForSeconds> waitForSecondsCache =
            new Dictionary<float, WaitForSeconds>(64);

        private static readonly Dictionary<float, WaitForSecondsRealtime> waitForSecondsRealtimeCache =
            new Dictionary<float, WaitForSecondsRealtime>(32);

        // Static singleton instances for common instructions
        private static WaitForEndOfFrame waitForEndOfFrame;
        private static WaitForFixedUpdate waitForFixedUpdate;

        /// <summary>
        /// Gets a cached WaitForEndOfFrame instance.
        /// </summary>
        public static WaitForEndOfFrame WaitForEndOfFrame =>
            waitForEndOfFrame ?? (waitForEndOfFrame = new WaitForEndOfFrame());

        /// <summary>
        /// Gets a cached WaitForFixedUpdate instance.
        /// </summary>
        public static WaitForFixedUpdate WaitForFixedUpdate =>
            waitForFixedUpdate ?? (waitForFixedUpdate = new WaitForFixedUpdate());

        /// <summary>
        /// Gets a cached WaitForSeconds for the given duration.
        /// Duration is quantized to reduce cache entries.
        /// </summary>
        public static WaitForSeconds GetWaitForSeconds(float seconds)
        {
            float quantized = QuantizeTime(seconds);

            if (!waitForSecondsCache.TryGetValue(quantized, out WaitForSeconds cached))
            {
                cached = new WaitForSeconds(quantized);
                waitForSecondsCache[quantized] = cached;
            }

            return cached;
        }

        /// <summary>
        /// Gets a cached WaitForSecondsRealtime for the given duration.
        /// </summary>
        public static WaitForSecondsRealtime GetWaitForSecondsRealtime(float seconds)
        {
            float quantized = QuantizeTime(seconds);

            if (!waitForSecondsRealtimeCache.TryGetValue(quantized, out WaitForSecondsRealtime cached))
            {
                cached = new WaitForSecondsRealtime(quantized);
                waitForSecondsRealtimeCache[quantized] = cached;
            }

            return cached;
        }

        /// <summary>
        /// Try to get cached WaitForSeconds. Returns null if cache is at capacity.
        /// Use this in memory-constrained scenarios.
        /// </summary>
        public static WaitForSeconds TryGetWaitForSecondsPooled(
            float seconds,
            int maxCacheSize = 100)
        {
            float quantized = QuantizeTime(seconds);

            if (waitForSecondsCache.TryGetValue(quantized, out WaitForSeconds cached))
            {
                return cached;
            }

            if (waitForSecondsCache.Count >= maxCacheSize)
            {
                return null; // Caller should create new instance
            }

            cached = new WaitForSeconds(quantized);
            waitForSecondsCache[quantized] = cached;
            return cached;
        }

        private static float QuantizeTime(float seconds)
        {
            if (seconds <= 0f) return 0f;

            float step = WaitInstructionQuantizationStepSeconds;
            return Mathf.Round(seconds / step) * step;
        }

        /// <summary>
        /// Clears all cached yield instructions.
        /// Call when loading new scene if memory is tight.
        /// </summary>
        public static void ClearYieldCaches()
        {
            waitForSecondsCache.Clear();
            waitForSecondsRealtimeCache.Clear();
        }
    }
}
```

## Usage

### Basic Coroutine

```csharp
private IEnumerator SpawnEnemies()
{
    while (true)
    {
        SpawnEnemy();
        yield return Buffers.GetWaitForSeconds(2f); // Cached!
    }
}
```

### Common Yield Instructions

```csharp
private IEnumerator UpdateLoop()
{
    while (true)
    {
        // These are static singletons - always zero allocation
        yield return Buffers.WaitForEndOfFrame;
        UpdateUI();

        yield return Buffers.WaitForFixedUpdate;
        UpdatePhysics();

        // Cached by duration
        yield return Buffers.GetWaitForSeconds(0.5f);
    }
}
```

### Realtime Waits (Pause-Safe)

```csharp
private IEnumerator FadeOut()
{
    float elapsed = 0f;
    float duration = 1f;

    while (elapsed < duration)
    {
        elapsed += Time.unscaledDeltaTime;
        SetAlpha(1f - (elapsed / duration));
        yield return Buffers.GetWaitForSecondsRealtime(0.016f); // ~60fps
    }
}
```

### Memory-Constrained Fallback

```csharp
private IEnumerator DynamicDelay(float delay)
{
    WaitForSeconds wait = Buffers.TryGetWaitForSecondsPooled(delay, maxCacheSize: 50);

    if (wait != null)
    {
        yield return wait;
    }
    else
    {
        // Cache full, create temporary (will be GC'd)
        yield return new WaitForSeconds(delay);
    }
}
```

## Performance Notes

- **Allocations**: Zero after initial cache population
- **Cache Size**: ~20 bytes per cached duration
- **Quantization**: 0.05s step = max 200 entries for 0-10s range
- **Lookup**: O(1) dictionary access

### Cache Growth Behavior

| Duration Range | Quantization | Max Entries |
| -------------- | ------------ | ----------- |
| 0-1s           | 0.05s        | 20          |
| 0-5s           | 0.05s        | 100         |
| 0-10s          | 0.1s         | 100         |

## Best Practices

### Do

- Use static singleton properties for `WaitForEndOfFrame`/`WaitForFixedUpdate`
- Use `GetWaitForSeconds` for common durations
- Call `ClearYieldCaches()` on scene transitions if memory is tight
- Adjust `WaitInstructionQuantizationStepSeconds` based on precision needs

### Don't

- Don't create new `WaitForSeconds` in hot coroutines
- Don't cache unique random durations (defeats purpose)
- Don't use very small quantization steps (cache bloat)
- Don't share `WaitForSecondsRealtime` across threads (not thread-safe)

### Quantization Trade-offs

```csharp
// Precise (more cache entries)
Buffers.WaitInstructionQuantizationStepSeconds = 0.01f;

// Balanced (default)
Buffers.WaitInstructionQuantizationStepSeconds = 0.05f;

// Coarse (fewer entries, less precision)
Buffers.WaitInstructionQuantizationStepSeconds = 0.25f;
```

## Related Patterns

- [Object Pooling](./object-pooling.md) - General pooling pattern
- [Collection Pooling](./collection-pooling.md) - Pooling with RAII
