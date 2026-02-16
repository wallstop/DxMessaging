---
title: "Collection Pooling with RAII Pattern"
id: "collection-pooling"
category: "performance"
version: "1.0.0"
created: "2026-01-21"
updated: "2026-01-21"

source:
  repository: "wallstop/unity-helpers"
  files:
    - path: "Runtime/Utils/Buffers.cs"
      lines: "1-500"
  url: "https://github.com/wallstop/unity-helpers"

tags:
  - "memory"
  - "allocation"
  - "pooling"
  - "zero-alloc"
  - "collections"
  - "raii"
  - "disposable"

complexity:
  level: "intermediate"
  reasoning: "Requires understanding of IDisposable and RAII patterns, but usage is straightforward"

impact:
  performance:
    rating: "high"
    details: "Eliminates per-operation allocations for Lists, HashSets, Stacks, Queues, and StringBuilders"
  maintainability:
    rating: "high"
    details: "using statements ensure proper cleanup; pattern is self-documenting"
  testability:
    rating: "high"
    details: "Pools can be tested in isolation; behavior tests unaffected by pooling"

prerequisites:
  - "Understanding of IDisposable"
  - "Knowledge of using statements"

dependencies:
  packages: []
  skills:
    - "object-pooling"

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
  - "Buffers pattern"
  - "PooledResource"
  - "Collection reuse"

related:
  - "object-pooling"
  - "stringbuilder-pooling"
  - "array-pooling"

status: "stable"
---

# Collection Pooling with RAII Pattern

> **One-line summary**: Use PooledResource<T> with `using` statements to automatically rent and return collections, achieving zero-allocation collection usage in hot paths.

## Overview

Collection pooling provides reusable instances of common collection types (List, HashSet, Stack, Queue, StringBuilder) with automatic cleanup. The RAII (Resource Acquisition Is Initialization) pattern via `IDisposable` ensures collections are:

1. **Rented** when entering scope
1. **Cleared** when exiting scope (removing stale data)
1. **Returned** to the pool automatically

This eliminates the most common source of per-frame allocations in Unity games.

## Problem Statement

```csharp
// BAD: Allocates a new List every call
public void ProcessEnemies(Vector3 playerPos)
{
    List<Enemy> nearby = new List<Enemy>(); // 40+ byte allocation
    foreach (Enemy e in allEnemies)
    {
        if (Vector3.Distance(e.Position, playerPos) < 10f)
        {
            nearby.Add(e);
        }
    }
    // nearby becomes garbage after this method
}
```

Called 60 times per second = 2,400 List allocations per minute = GC spikes.

## Solution

### Core Concept

```text
┌─────────────────────────────────────────────────────────────┐
│  using (PooledResource<List<T>> lease = Pool.Get(out list)) │
│  {                                                           │
│      // Use list...                                          │
│  } // <-- Dispose() called: list.Clear(), return to pool    │
└─────────────────────────────────────────────────────────────┘
```

### Implementation

```csharp
namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// RAII wrapper that returns a pooled object on Dispose.
    /// </summary>
    public readonly struct PooledResource<T> : IDisposable where T : class
    {
        private readonly T value;
        private readonly Action<T> returnAction;

        public T Value => value;

        internal PooledResource(T value, Action<T> returnAction)
        {
            this.value = value;
            this.returnAction = returnAction;
        }

        public void Dispose()
        {
            returnAction?.Invoke(value);
        }
    }

    /// <summary>
    /// Generic collection pool with typed accessors.
    /// </summary>
    public static class Buffers<T>
    {
        private static readonly ObjectPool<List<T>> listPool =
            new ObjectPool<List<T>>(
                initialCapacity: 8,
                resetAction: list => list.Clear()
            );

        private static readonly ObjectPool<HashSet<T>> hashSetPool =
            new ObjectPool<HashSet<T>>(
                initialCapacity: 4,
                resetAction: set => set.Clear()
            );

        private static readonly ObjectPool<Stack<T>> stackPool =
            new ObjectPool<Stack<T>>(
                initialCapacity: 4,
                resetAction: stack => stack.Clear()
            );

        private static readonly ObjectPool<Queue<T>> queuePool =
            new ObjectPool<Queue<T>>(
                initialCapacity: 4,
                resetAction: queue => queue.Clear()
            );

        public static class List
        {
            public static PooledResource<List<T>> Get(out List<T> list)
            {
                list = listPool.Rent();
                return new PooledResource<List<T>>(list, l =>
                {
                    l.Clear();
                    listPool.Return(l);
                });
            }
        }

        public static class HashSet
        {
            public static PooledResource<HashSet<T>> Get(out HashSet<T> set)
            {
                set = hashSetPool.Rent();
                return new PooledResource<HashSet<T>>(set, s =>
                {
                    s.Clear();
                    hashSetPool.Return(s);
                });
            }
        }

        public static class Stack
        {
            public static PooledResource<Stack<T>> Get(out Stack<T> stack)
            {
                stack = stackPool.Rent();
                return new PooledResource<Stack<T>>(stack, s =>
                {
                    s.Clear();
                    stackPool.Return(s);
                });
            }
        }

        public static class Queue
        {
            public static PooledResource<Queue<T>> Get(out Queue<T> queue)
            {
                queue = queuePool.Rent();
                return new PooledResource<Queue<T>>(queue, q =>
                {
                    q.Clear();
                    queuePool.Return(q);
                });
            }
        }
    }
}
```

## Usage

### Basic List Pooling

```csharp
public void ProcessEnemies(Vector3 playerPos)
{
    using PooledResource<List<Enemy>> lease = Buffers<Enemy>.List.Get(out List<Enemy> nearby);

    foreach (Enemy e in allEnemies)
    {
        if (Vector3.Distance(e.Position, playerPos) < 10f)
        {
            nearby.Add(e);
        }
    }

    foreach (Enemy e in nearby)
    {
        e.ReactToPlayer();
    }
    // lease.Dispose() called automatically: nearby.Clear() + return to pool
}
```

### Nested Collection Usage

```csharp
public void BuildGraph()
{
    using var nodesLease = Buffers<Node>.List.Get(out List<Node> nodes);
    using var visitedLease = Buffers<Node>.HashSet.Get(out HashSet<Node> visited);
    using var pendingLease = Buffers<Node>.Queue.Get(out Queue<Node> pending);

    pending.Enqueue(rootNode);

    while (pending.Count > 0)
    {
        Node current = pending.Dequeue();
        if (visited.Add(current))
        {
            nodes.Add(current);
            foreach (Node child in current.Children)
            {
                pending.Enqueue(child);
            }
        }
    }

    ProcessNodes(nodes);
} // All three collections cleared and returned
```

## Performance Notes

- **Allocations**: Zero in steady state; one-time pool warm-up
- **Timing**: O(1) rent/return; pool operations are constant time
- **Memory**: Pool grows to high-water mark, then stabilizes
- **Thread Safety**: Consider per-thread pools for multithreaded code

## Best Practices

### Do

- Use `using var` for concise syntax (C# 8+)
- Pre-size collections if you know approximate size: `list.Capacity = expectedCount`
- Pool collections used in Update/FixedUpdate/LateUpdate
- Clear collections before returning (handled automatically)

### Don't

- Don't store references to pooled collections beyond the using scope
- Don't return collections manually if using PooledResource
- Don't pool collections with finalizers or native resources
- Don't share PooledResource across threads

## Related Patterns

- [Object Pooling](./object-pooling.md) - Base pooling pattern
- [StringBuilder Pooling](./stringbuilder-pooling.md) - Specialized string building
- [Array Pooling](./array-pooling.md) - Fixed-size buffer pooling
