---
title: "Object Pooling for Zero-Allocation Messaging"
id: "object-pooling"
category: "performance"
version: "1.0.0"
created: "2026-01-21"
updated: "2026-01-21"

source:
  repository: "wallstop/DxMessaging"
  files:
    - path: "Runtime/Core/Pool/ObjectPool.cs"
      lines: "1-150"
    - path: "Runtime/Core/Messages/PooledMessage.cs"
  url: "https://github.com/wallstop/DxMessaging"

tags:
  - "memory"
  - "allocation"
  - "garbage-collection"
  - "pooling"
  - "zero-alloc"
  - "hot-path"

complexity:
  level: "intermediate"
  reasoning: "Requires understanding of object lifecycle and reference management, but implementation is straightforward"

impact:
  performance:
    rating: "high"
    details: "Eliminates per-message allocations, reducing GC pressure significantly in high-throughput scenarios"
  maintainability:
    rating: "medium"
    details: "Adds complexity around object lifecycle but follows clear patterns"
  testability:
    rating: "low"
    details: "Pools are implementation details; tests focus on behavior not pooling"

prerequisites:
  - "Understanding of C# memory management"
  - "Knowledge of garbage collection impact on games"

dependencies:
  packages: []
  skills: []

applies_to:
  languages:
    - "C#"
  frameworks:
    - "Unity"
    - ".NET"
  versions:
    unity: ">=2021.3"
    dotnet: ">=netstandard2.0"

aliases:
  - "Memory pooling"
  - "Object reuse"
  - "Flyweight pattern"

related:
  - "collection-pooling"
  - "array-pooling"
  - "object-pooling-variations"
  - "object-pooling-usage-examples"
  - "object-pooling-anti-patterns"

status: "stable"
---

# Object Pooling for Zero-Allocation Messaging

> **One-line summary**: Eliminate garbage collection spikes by reusing message objects instead of allocating new ones for each message emit.

## Overview

Object pooling is a creational pattern that pre-allocates and reuses objects instead of creating new instances each time they're needed. In messaging systems, this is critical because:

1. **High frequency**: Message systems often emit thousands of messages per frame
1. **Short-lived objects**: Messages are typically consumed immediately and discarded
1. **GC pressure**: Frequent allocations of short-lived objects trigger garbage collection
1. **Frame spikes**: GC pauses cause visible stuttering in games

By pooling message objects, we achieve near-zero allocation messaging even under heavy load.

## Problem Statement

Consider a typical messaging system that allocates a new message for each emit:

```csharp
public void EmitDamageEvent(int damage, GameObject source)
{
    // Allocates ~40 bytes per call
    DamageMessage message = new DamageMessage(damage, source);
    messageBus.Emit(message);
    // message becomes garbage immediately after handlers complete
}
```

In a game with 100 entities each emitting 10 messages per second, that's 1,000 allocations/second. Over 60 seconds, you've created 60,000 garbage objects, triggering multiple GC collections.

**Symptoms**:

- Frame rate drops every few seconds (GC spikes)
- Increasing memory usage followed by sudden drops
- Profiler shows high allocation rate in messaging code
- `GC.Collect` appearing in profiler hot path

## Solution

### Core Concept

Instead of `new`, acquire objects from a pool. Instead of letting them become garbage, return them to the pool:

```text
┌─────────────────────────────────────────────────────────┐
│                     Object Pool                          │
│  ┌─────┐ ┌─────┐ ┌─────┐ ┌─────┐ ┌─────┐              │
│  │ Msg │ │ Msg │ │ Msg │ │ Msg │ │ Msg │ ... (idle)   │
│  └─────┘ └─────┘ └─────┘ └─────┘ └─────┘              │
└────────────────────┬────────────────────────────────────┘
                     │
         ┌───────────┴───────────┐
         │ Rent()                │ Return()
         ▼                       │
    ┌─────────┐                  │
    │ Active  │──────────────────┘
    │ Message │
    └─────────┘
```

### Implementation

```csharp
namespace DxMessaging.Core.Pool
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Thread-safe object pool with automatic growth.
    /// </summary>
    /// <typeparam name="T">Type of pooled objects.</typeparam>
    public sealed class ObjectPool<T> where T : class, new()
    {
        private readonly Stack<T> pool;
        private readonly Action<T> resetAction;
        private readonly object syncLock = new object();
        private readonly int maxSize;

        public int CountInactive
        {
            get
            {
                lock (syncLock)
                {
                    return pool.Count;
                }
            }
        }

        public ObjectPool(int initialCapacity = 16, int maxSize = 1024, Action<T> resetAction = null)
        {
            this.maxSize = maxSize;
            this.resetAction = resetAction;
            this.pool = new Stack<T>(initialCapacity);

            // Pre-warm the pool
            for (int i = 0; i < initialCapacity; i++)
            {
                pool.Push(new T());
            }
        }

        /// <summary>
        /// Rent an object from the pool. Creates new if pool is empty.
        /// </summary>
        public T Rent()
        {
            lock (syncLock)
            {
                if (pool.Count > 0)
                {
                    return pool.Pop();
                }
            }
            // Pool exhausted, create new (will be returned to pool later)
            return new T();
        }

        /// <summary>
        /// Return an object to the pool for reuse.
        /// </summary>
        public void Return(T item)
        {
            if (item == null)
            {
                return;
            }

            // Reset state before returning to pool
            resetAction?.Invoke(item);

            lock (syncLock)
            {
                // Don't exceed max size - let excess become garbage
                if (pool.Count < maxSize)
                {
                    pool.Push(item);
                }
            }
        }

        /// <summary>
        /// Clear the pool, releasing all objects.
        /// </summary>
        public void Clear()
        {
            lock (syncLock)
            {
                pool.Clear();
            }
        }
    }
}
```

### Step-by-Step Breakdown

1. **Pre-warm on construction**: Allocate objects upfront during loading, not during gameplay
1. **Rent instead of new**: `Rent()` returns a pooled instance or creates one if pool is empty
1. **Reset on return**: Clear object state to prevent data leakage between uses
1. **Cap pool size**: Prevent unbounded memory growth during usage spikes
1. **Thread safety**: Lock-based synchronization for multi-threaded access

## Testing Considerations

- Test pool growth under load (pool expands correctly)
- Test max size cap (excess objects become garbage, not pooled)
- Test reset behavior (no data leakage between rentals)
- Test thread safety (concurrent rent/return doesn't corrupt state)

```csharp
[Test]
public void Pool_RentAndReturn_ReusesObjects()
{
    ObjectPool<TestMessage> pool = new ObjectPool<TestMessage>(initialCapacity: 1);

    TestMessage first = pool.Rent();
    pool.Return(first);

    TestMessage second = pool.Rent();

    Assert.That(ReferenceEquals(first, second), Is.True, "Pool should reuse returned objects");
}

[Test]
public void Pool_ExceedingMaxSize_DoesNotGrow()
{
    ObjectPool<TestMessage> pool = new ObjectPool<TestMessage>(initialCapacity: 2, maxSize: 2);

    TestMessage a = pool.Rent();
    TestMessage b = pool.Rent();
    TestMessage c = pool.Rent(); // Created, not from pool

    pool.Return(a);
    pool.Return(b);
    pool.Return(c); // Should be dropped, pool at max

    Assert.That(pool.CountInactive, Is.EqualTo(2), "Pool should not exceed max size");
}
```

## Performance Notes

**Benchmarks** (10,000 messages/frame, Unity 2021.3, IL2CPP):

| Approach        | Allocations/Frame | GC Pressure | Frame Time |
| --------------- | ----------------- | ----------- | ---------- |
| `new` each time | 10,000            | 400 KB      | 2.1 ms     |
| Object pool     | 0\*               | 0 KB        | 0.8 ms     |
| Struct messages | 0                 | 0 KB        | 0.5 ms     |

\* After warm-up; initial frames allocate to fill pool

**When pooling matters**:

- High-frequency events (>100/second)
- Mobile/console targets with memory constraints
- Games targeting consistent 60+ FPS

**When pooling is overkill**:

- Editor tools
- One-shot events (level start, game over)
- Low-frequency UI events

## See Also

- [Allocation Reduction Strategies](./allocation-reduction.md)
- [Cache Strategies](./cache-strategies.md)
- [Thread Safety Patterns](../concurrency/thread-safety.md)
- [Object Pooling Variations](./object-pooling-variations.md)
- [Object Pooling Usage Examples](./object-pooling-usage-examples.md)
- [Object Pooling Anti-Patterns](./object-pooling-anti-patterns.md)

## References

- [Unity Performance Best Practices](https://docs.unity3d.com/Manual/BestPracticeUnderstandingPerformanceInUnity.html)
- [.NET Object Pooling](https://docs.microsoft.com/en-us/dotnet/api/microsoft.extensions.objectpool)
- [Game Programming Patterns: Object Pool](https://gameprogrammingpatterns.com/object-pool.html)

## Changelog

| Version | Date       | Changes                                          |
| ------- | ---------- | ------------------------------------------------ |
| 1.0.0   | 2026-01-21 | Initial version with core pattern and variations |
