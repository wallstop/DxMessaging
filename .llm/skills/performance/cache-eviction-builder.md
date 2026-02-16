---
title: "Cache Builder Configuration"
id: "cache-eviction-builder"
category: "performance"
version: "1.0.0"
created: "2026-01-21"
updated: "2026-01-21"

source:
  repository: "wallstop/unity-helpers"
  files:
    - path: "Runtime/Core/Cache/CacheBuilder.cs"
  url: "https://github.com/wallstop/unity-helpers"

tags:
  - "caching"
  - "builder"
  - "configuration"
  - "performance"

complexity:
  level: "intermediate"
  reasoning: "Uses a struct-based fluent builder for cache configuration"

impact:
  performance:
    rating: "high"
    details: "Struct builder avoids allocations and keeps configuration explicit"
  maintainability:
    rating: "high"
    details: "Fluent configuration reduces misconfiguration"
  testability:
    rating: "high"
    details: "Explicit options improve test clarity"

prerequisites:
  - "Understanding of cache policies"

dependencies:
  packages: []
  skills:
    - "cache-eviction-policies"

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
  - "Cache options builder"

related:
  - "cache-eviction-policies"
  - "cache-eviction-implementation"
  - "fluent-builder-pattern"

status: "stable"
---

# Cache Builder Configuration

> **One-line summary**: Use a struct-based fluent builder to configure cache size, eviction policy, and expiration without allocations.

## Overview

The builder encapsulates cache configuration in a single fluent flow, preventing partially configured caches and keeping defaults consistent.

## Problem Statement

Ad-hoc cache configuration often leads to inconsistent defaults or hidden allocations. A builder makes configuration explicit and easy to audit.

## Solution

### Core Concept

Use a struct-based builder to avoid heap allocations while keeping configuration readable.

### Implementation

```csharp
namespace WallstopStudios.UnityHelpers.Core.Cache
{
    using System;
    using System.Collections.Generic;

    public struct CacheBuilder<TKey, TValue>
    {
        private int maximumSize;
        private EvictionPolicy policy;
        private TimeSpan? expireAfterWrite;
        private TimeSpan? expireAfterAccess;
        private bool recordStats;
        private IEqualityComparer<TKey> keyComparer;

        public CacheBuilder<TKey, TValue> WithMaximumSize(int size)
        {
            maximumSize = size;
            return this;
        }

        public CacheBuilder<TKey, TValue> WithPolicy(EvictionPolicy evictionPolicy)
        {
            policy = evictionPolicy;
            return this;
        }

        public CacheBuilder<TKey, TValue> WithExpireAfterWrite(TimeSpan duration)
        {
            expireAfterWrite = duration;
            return this;
        }

        public CacheBuilder<TKey, TValue> WithExpireAfterAccess(TimeSpan duration)
        {
            expireAfterAccess = duration;
            return this;
        }

        public CacheBuilder<TKey, TValue> WithRecordStats()
        {
            recordStats = true;
            return this;
        }

        public CacheBuilder<TKey, TValue> WithKeyComparer(IEqualityComparer<TKey> comparer)
        {
            keyComparer = comparer;
            return this;
        }

        public Cache<TKey, TValue> Build()
        {
            return new Cache<TKey, TValue>(new CacheOptions<TKey, TValue>
            {
                MaximumSize = maximumSize > 0 ? maximumSize : 1000,
                Policy = policy,
                ExpireAfterWriteSeconds = (float?)expireAfterWrite?.TotalSeconds,
                ExpireAfterAccessSeconds = (float?)expireAfterAccess?.TotalSeconds,
                RecordStats = recordStats,
                KeyComparer = keyComparer
            });
        }
    }
}
```

## Usage Examples

### Basic LRU Cache

```csharp
using Cache<string, GameData> cache = new CacheBuilder<string, GameData>()
    .WithMaximumSize(1000)
    .WithPolicy(EvictionPolicy.Lru)
    .Build();
```

### Cache with TTL

```csharp
using Cache<string, LeaderboardEntry> cache = new CacheBuilder<string, LeaderboardEntry>()
    .WithMaximumSize(500)
    .WithExpireAfterWrite(TimeSpan.FromMinutes(5))
    .WithExpireAfterAccess(TimeSpan.FromMinutes(1))
    .WithRecordStats()
    .Build();
```

### Cache with Statistics

```csharp
CacheStats stats = cache.GetStats();
Debug.Log($"Cache hit rate: {stats.HitRate:P2}");
Debug.Log($"Evictions: {stats.EvictionCount}");
```

## Performance Notes

- **Builder**: Zero allocations (struct)
- **Get/Put**: O(1) average case
- **Eviction**: O(1) for LRU/FIFO, O(n) for Random
- **Memory**: Overhead per entry from linked list and metadata

## See Also

- [High-Performance Cache with Eviction Policies](cache-eviction-policies.md)
- [Cache Eviction Implementation](cache-eviction-implementation.md)
- [Fluent Builder Pattern](../solid/fluent-builder-pattern.md)

## References

- [Cache replacement policies](https://en.wikipedia.org/wiki/Cache_replacement_policies)

## Changelog

| Version | Date       | Changes         |
| ------- | ---------- | --------------- |
| 1.0.0   | 2026-01-21 | Initial version |
