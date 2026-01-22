---
title: "Fluent Builder Pattern with Struct Builders"
id: "fluent-builder-pattern"
category: "solid"
version: "1.0.0"
created: "2026-01-21"
updated: "2026-01-21"

source:
  repository: "wallstop/unity-helpers"
  files:
    - path: "Runtime/Core/Cache/CacheBuilder.cs"
  url: "https://github.com/wallstop/unity-helpers"

tags:
  - "solid"
  - "patterns"
  - "builder"
  - "fluent-api"
  - "zero-alloc"

complexity:
  level: "intermediate"
  reasoning: "Requires understanding of builder pattern and struct semantics"

impact:
  performance:
    rating: "medium"
    details: "Struct builder eliminates allocation for configuration object"
  maintainability:
    rating: "high"
    details: "Self-documenting API with method chaining"
  testability:
    rating: "high"
    details: "Easy to test different configurations"

prerequisites:
  - "Understanding of builder pattern"
  - "Knowledge of struct vs class tradeoffs"

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

aliases:
  - "Method chaining"
  - "Configuration builder"
  - "Fluent interface"

related:
  - "collection-extensions"
  - "try-pattern-apis"
  - "fluent-builder-pattern-templates"
  - "fluent-builder-pattern-usage-examples"

status: "stable"
---

# Fluent Builder Pattern with Struct Builders

> **One-line summary**: Implement the builder pattern using structs to provide a zero-allocation fluent API for complex object construction.

## Overview

The builder pattern separates object construction from its representation. Using a struct builder:

1. **Zero allocation** for the builder itself
1. **Fluent API** with method chaining
1. **Immutable result** - builder returns configured object
1. **Validation** in Build() method

## Problem Statement

```csharp
// BAD: Constructor with many parameters
var cache = new Cache<string, int>(
    1000,           // What is this?
    EvictionPolicy.Lru,
    TimeSpan.FromMinutes(5),
    TimeSpan.FromMinutes(1),
    true,           // What does true mean?
    null);

// BAD: Class-based builder allocates
var builder = new CacheBuilder<string, int>(); // Heap allocation!
builder.WithMaxSize(1000);
var cache = builder.Build();
```

## Solution

### Struct-Based Fluent Builder

```csharp
namespace WallstopStudios.UnityHelpers.Core.Cache
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Zero-allocation fluent builder for Cache configuration.
    /// </summary>
    /// <remarks>
    /// This is a struct to avoid heap allocation. Each With* method returns
    /// a new struct by value, enabling method chaining.
    /// </remarks>
    public struct CacheBuilder<TKey, TValue>
    {
        private int maximumSize;
        private EvictionPolicy policy;
        private TimeSpan? expireAfterWrite;
        private TimeSpan? expireAfterAccess;
        private bool recordStats;
        private IEqualityComparer<TKey> keyComparer;
        private float? jitterFactor;
        private bool initialized;

        /// <summary>
        /// Sets the maximum number of entries in the cache.
        /// </summary>
        /// <param name="size">Maximum entry count. Must be positive.</param>
        public CacheBuilder<TKey, TValue> WithMaximumSize(int size)
        {
            if (size <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size), "Size must be positive");
            }

            var copy = this;
            copy.maximumSize = size;
            copy.initialized = true;
            return copy;
        }

        /// <summary>
        /// Sets the eviction policy when cache is full.
        /// </summary>
        /// <param name="evictionPolicy">Policy to use (LRU, LFU, etc.).</param>
        public CacheBuilder<TKey, TValue> WithPolicy(EvictionPolicy evictionPolicy)
        {
            var copy = this;
            copy.policy = evictionPolicy;
            copy.initialized = true;
            return copy;
        }

        /// <summary>
        /// Sets expiration time after an entry is written.
        /// </summary>
        public CacheBuilder<TKey, TValue> WithExpireAfterWrite(TimeSpan duration)
        {
            if (duration <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(duration), "Duration must be positive");
            }

            var copy = this;
            copy.expireAfterWrite = duration;
            copy.initialized = true;
            return copy;
        }

        /// <summary>
        /// Sets expiration time after an entry is accessed.
        /// </summary>
        public CacheBuilder<TKey, TValue> WithExpireAfterAccess(TimeSpan duration)
        {
            if (duration <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(duration), "Duration must be positive");
            }

            var copy = this;
            copy.expireAfterAccess = duration;
            copy.initialized = true;
            return copy;
        }

        /// <summary>
        /// Enables hit/miss statistics recording.
        /// </summary>
        public CacheBuilder<TKey, TValue> WithRecordStats()
        {
            var copy = this;
            copy.recordStats = true;
            copy.initialized = true;
            return copy;
        }

        /// <summary>
        /// Sets a custom key comparer for lookups.
        /// </summary>
        public CacheBuilder<TKey, TValue> WithKeyComparer(IEqualityComparer<TKey> comparer)
        {
            var copy = this;
            copy.keyComparer = comparer ?? throw new ArgumentNullException(nameof(comparer));
            copy.initialized = true;
            return copy;
        }

        /// <summary>
        /// Adds random jitter to expiration times to prevent thundering herd.
        /// </summary>
        /// <param name="factor">Jitter factor (0.0-1.0). E.g., 0.1 = ±10% variation.</param>
        public CacheBuilder<TKey, TValue> WithExpirationJitter(float factor)
        {
            if (factor < 0f || factor > 1f)
            {
                throw new ArgumentOutOfRangeException(nameof(factor), "Factor must be between 0 and 1");
            }

            var copy = this;
            copy.jitterFactor = factor;
            copy.initialized = true;
            return copy;
        }

        /// <summary>
        /// Builds the cache with the configured options.
        /// </summary>
        /// <returns>A new Cache instance.</returns>
        /// <exception cref="InvalidOperationException">If configuration is invalid.</exception>
        public Cache<TKey, TValue> Build()
        {
            // Apply defaults
            int size = maximumSize > 0 ? maximumSize : 1000;
            EvictionPolicy pol = initialized ? policy : EvictionPolicy.Lru;

            // Validate combinations
            if (jitterFactor.HasValue && !expireAfterWrite.HasValue && !expireAfterAccess.HasValue)
            {
                throw new InvalidOperationException(
                    "Jitter requires expiration to be set. Call WithExpireAfterWrite or WithExpireAfterAccess first.");
            }

            var options = new CacheOptions<TKey, TValue>
            {
                MaximumSize = size,
                Policy = pol,
                ExpireAfterWriteSeconds = (float?)expireAfterWrite?.TotalSeconds,
                ExpireAfterAccessSeconds = (float?)expireAfterAccess?.TotalSeconds,
                RecordStats = recordStats,
                KeyComparer = keyComparer,
                JitterFactor = jitterFactor
            };

            return new Cache<TKey, TValue>(options);
        }
    }
}
```

## Performance Notes

### Struct vs Class Builder

| Aspect        | Struct Builder | Class Builder     |
| ------------- | -------------- | ----------------- |
| Allocation    | 0 bytes        | ~24 bytes minimum |
| GC pressure   | None           | Creates garbage   |
| Copy behavior | Value copy     | Reference copy    |

### Method Chaining Cost

Each `With*` method copies the struct (~50-100 bytes typically). This is stack-allocated and very fast (~1ns).

## Best Practices

### Do

- Use struct for builders with few fields (<10)
- Return `this` by value (`var copy = this; ... return copy;`)
- Validate early in With\* methods where possible
- Validate configuration combinations in Build()
- Provide sensible defaults

### Don't

- Don't use struct for large builders (>200 bytes)
- Don't store reference types that might be mutated after Build()
- Don't make builders mutable (always return new copy)
- Don't forget the `initialized` flag pattern for distinguishing "not set" from "set to default"

### Initialization Pattern

```csharp
public struct Builder
{
    private int value;
    private bool valueSet; // Distinguishes "0" from "not set"

    public Builder WithValue(int v)
    {
        var copy = this;
        copy.value = v;
        copy.valueSet = true;
        return copy;
    }

    public Result Build()
    {
        int finalValue = valueSet ? value : 100; // Default is 100
        return new Result(finalValue);
    }
}
```

## Related Patterns

- [Collection Extensions](./collection-extensions.md) - Fluent collection API
- [Try-Pattern APIs](./try-pattern-apis.md) - Safe configuration validation
- [Fluent Builder Templates and Factories](./fluent-builder-pattern-templates.md) - Reusable patterns
- [Fluent Builder Usage Examples](./fluent-builder-pattern-usage-examples.md) - Common flows
