---
title: "Array Pooling with ArrayPool and Custom Pools"
id: "array-pooling"
category: "performance"
version: "1.0.0"
created: "2026-01-21"
updated: "2026-01-21"

source:
  repository: "wallstop/unity-helpers"
  files:
    - path: "Runtime/Utils/Buffers.cs"
      lines: "2865-3682"
  url: "https://github.com/wallstop/unity-helpers"

tags:
  - "memory"
  - "allocation"
  - "pooling"
  - "zero-alloc"
  - "arrays"
  - "buffers"
  - "arraypool"

complexity:
  level: "intermediate"
  reasoning: "Requires understanding of exact vs variable sizing and when to use each pool type"

impact:
  performance:
    rating: "critical"
    details: "Critical for serialization, networking, and any code processing raw buffers"
  maintainability:
    rating: "medium"
    details: "Must remember to return arrays; PooledArray wrapper helps"
  testability:
    rating: "high"
    details: "Buffer behavior easily testable"

prerequisites:
  - "Understanding of ArrayPool<T>"
  - "Knowledge of unmanaged types"

dependencies:
  packages: []
  skills:
    - "collection-pooling"

applies_to:
  languages:
    - "C#"
  frameworks:
    - "Unity"
    - ".NET"
  versions:
    unity: ">=2021.3"
    dotnet: ">=netstandard2.1"

aliases:
  - "Buffer pooling"
  - "Byte array pooling"
  - "Temporary buffers"

related:
  - "collection-pooling"
  - "array-pooling-usage-examples"
  - "object-pooling"

status: "stable"
---

# Array Pooling with ArrayPool and Custom Pools

> **One-line summary**: Use specialized array pools for temporary buffers to eliminate allocations in serialization, networking, and data processing.

## Overview

Arrays are frequently allocated for temporary operations like:

- Serialization buffers
- Network packet buffers
- Image/texture data
- Computational scratch space

Three pool types serve different needs:

| Pool Type                  | Exact Size | Clears Data | Best For                      |
| -------------------------- | ---------- | ----------- | ----------------------------- |
| `WallstopArrayPool<T>`     | ✅ Yes     | ✅ Yes      | Security-sensitive, exact-fit |
| `WallstopFastArrayPool<T>` | ✅ Yes     | ❌ No       | Unmanaged types, max speed    |
| `SystemArrayPool<T>`       | ❌ No      | ❌ No       | Variable sizes, standard .NET |

## Problem Statement

```csharp
// BAD: Allocates 4KB every call
public void ProcessPacket(NetworkPacket packet)
{
    byte[] buffer = new byte[4096]; // Large Object Heap if > 85KB!
    int bytesRead = packet.ReadInto(buffer);
    ProcessBytes(buffer, 0, bytesRead);
    // buffer becomes garbage
}
```

For networking at 60 packets/second = 14.4 MB/minute of garbage.

## Solution

### Core Concept

```text
┌─────────────────────────────────────────────────────────────┐
│                    Array Pool Hierarchy                      │
├─────────────────────────────────────────────────────────────┤
│  WallstopArrayPool<T>     │ Exact size, cleared, safe       │
│  WallstopFastArrayPool<T> │ Exact size, not cleared, fast   │
│  SystemArrayPool<T>       │ May be larger, wraps Shared     │
└─────────────────────────────────────────────────────────────┘

Usage:
┌──────────────────────────────────────────────────────────────┐
│ using PooledArray<T> lease = Pool.Get(size, out T[] arr);   │
│ // arr.Length == size (Wallstop) or >= size (System)        │
│ // Use arr...                                                │
│ // Dispose returns to pool                                   │
└──────────────────────────────────────────────────────────────┘
```

### Implementation

```csharp
namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using System.Buffers;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;

    /// <summary>
    /// RAII wrapper for pooled arrays.
    /// </summary>
    public readonly struct PooledArray<T> : IDisposable
    {
        private readonly T[] array;
        private readonly Action<T[]> returnAction;

        public T[] Array => array;
        public int Length => array?.Length ?? 0;

        internal PooledArray(T[] array, Action<T[]> returnAction)
        {
            this.array = array;
            this.returnAction = returnAction;
        }

        public void Dispose()
        {
            if (array != null)
            {
                returnAction?.Invoke(array);
            }
        }
    }

    /// <summary>
    /// Exact-size array pool that clears arrays on return.
    /// Safe for sensitive data.
    /// </summary>
    public static class WallstopArrayPool<T>
    {
        private static readonly Dictionary<int, Stack<T[]>> pools =
            new Dictionary<int, Stack<T[]>>();
        private static readonly object syncLock = new object();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PooledArray<T> Get(int exactSize, out T[] array)
        {
            lock (syncLock)
            {
                if (pools.TryGetValue(exactSize, out Stack<T[]> pool) && pool.Count > 0)
                {
                    array = pool.Pop();
                    return new PooledArray<T>(array, Return);
                }
            }

            array = new T[exactSize];
            return new PooledArray<T>(array, Return);
        }

        private static void Return(T[] array)
        {
            if (array == null) return;

            // Clear for security
            System.Array.Clear(array, 0, array.Length);

            lock (syncLock)
            {
                if (!pools.TryGetValue(array.Length, out Stack<T[]> pool))
                {
                    pool = new Stack<T[]>(4);
                    pools[array.Length] = pool;
                }

                if (pool.Count < 16) // Max per size
                {
                    pool.Push(array);
                }
            }
        }
    }

    /// <summary>
    /// Exact-size array pool that does NOT clear arrays.
    /// Fastest option for unmanaged types.
    /// </summary>
    public static class WallstopFastArrayPool<T> where T : unmanaged
    {
        private static readonly Dictionary<int, Stack<T[]>> pools =
            new Dictionary<int, Stack<T[]>>();
        private static readonly object syncLock = new object();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PooledArray<T> Get(int exactSize, out T[] array)
        {
            lock (syncLock)
            {
                if (pools.TryGetValue(exactSize, out Stack<T[]> pool) && pool.Count > 0)
                {
                    array = pool.Pop();
                    return new PooledArray<T>(array, Return);
                }
            }

            array = new T[exactSize];
            return new PooledArray<T>(array, Return);
        }

        private static void Return(T[] array)
        {
            if (array == null) return;

            // No clearing - faster but caller must not rely on zeroed data

            lock (syncLock)
            {
                if (!pools.TryGetValue(array.Length, out Stack<T[]> pool))
                {
                    pool = new Stack<T[]>(4);
                    pools[array.Length] = pool;
                }

                if (pool.Count < 16)
                {
                    pool.Push(array);
                }
            }
        }
    }

    /// <summary>
    /// Wraps ArrayPool<T>.Shared for variable-size needs.
    /// Returns arrays that may be larger than requested.
    /// </summary>
    public static class SystemArrayPool<T>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PooledArray<T> Get(int minimumSize, out T[] array)
        {
            array = ArrayPool<T>.Shared.Rent(minimumSize);
            return new PooledArray<T>(array, Return);
        }

        private static void Return(T[] array)
        {
            if (array != null)
            {
                ArrayPool<T>.Shared.Return(array, clearArray: false);
            }
        }
    }
}
```

## Performance Notes

- **Allocations**: Zero in steady state after pool warm-up
- **WallstopArrayPool**: ~50ns overhead for clear on return
- **WallstopFastArrayPool**: ~10ns overhead (no clear)
- **SystemArrayPool**: May return larger arrays (power of 2 sizing)
- **LOH**: Arrays > 85KB go on Large Object Heap; pool to avoid LOH fragmentation

## Best Practices

### Do

- Use `WallstopFastArrayPool` for unmanaged types when you don't need zeroed data
- Use `WallstopArrayPool` for sensitive data (passwords, keys)
- Use `SystemArrayPool` when exact size doesn't matter
- Check `array.Length` vs requested size when using SystemArrayPool

### Don't

- Don't hold PooledArray beyond the using scope
- Don't mix pool types (get from one, return to another)
- Don't assume SystemArrayPool arrays are exactly the requested size
- Don't pool tiny arrays (overhead exceeds benefit for <64 bytes)

## Related Patterns

- [Collection Pooling](./collection-pooling.md) - List/HashSet/etc. pooling
- [Span/Memory Streams](./span-memory-streams.md) - Zero-copy stream access
- [Array Pooling Usage Examples](./array-pooling-usage-examples.md) - Applied pooling scenarios
