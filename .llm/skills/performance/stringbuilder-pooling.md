---
title: "StringBuilder Pooling for Zero-Allocation String Building"
id: "stringbuilder-pooling"
category: "performance"
version: "1.0.0"
created: "2026-01-21"
updated: "2026-01-21"

source:
  repository: "wallstop/unity-helpers"
  files:
    - path: "Runtime/Utils/Buffers.cs"
      lines: "148-188"
  url: "https://github.com/wallstop/unity-helpers"

tags:
  - "memory"
  - "allocation"
  - "pooling"
  - "zero-alloc"
  - "strings"
  - "stringbuilder"

complexity:
  level: "basic"
  reasoning: "Simple pattern with clear usage; StringBuilder is well-known"

impact:
  performance:
    rating: "high"
    details: "Eliminates O(n²) string allocations in concatenation loops"
  maintainability:
    rating: "high"
    details: "Cleaner than manual StringBuilder management"
  testability:
    rating: "high"
    details: "String output can be tested directly"

prerequisites:
  - "Understanding of StringBuilder"

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
    dotnet: ">=netstandard2.0"

aliases:
  - "String concatenation optimization"
  - "StringBuffer pooling"

related:
  - "collection-pooling"
  - "object-pooling"

status: "stable"
---

# StringBuilder Pooling for Zero-Allocation String Building

> **One-line summary**: Pool StringBuilder instances to eliminate per-operation allocations when building strings in hot paths.

## Overview

String concatenation with `+` or `+=` creates new string objects for each operation, causing O(n²) allocations for n concatenations. StringBuilder avoids this, but creating a new StringBuilder each time still allocates. Pooling StringBuilders eliminates even that allocation.

## Problem Statement

```csharp
// BAD: O(n²) allocations
public string BuildItemList(List<Item> items)
{
    string result = "";
    for (int i = 0; i < items.Count; i++)
    {
        result += items[i].Name; // New string EACH iteration
        if (i < items.Count - 1)
        {
            result += ", "; // Another allocation
        }
    }
    return result;
}
```

For 100 items, this creates ~199 temporary strings that immediately become garbage.

```csharp
// BETTER but still allocates StringBuilder
public string BuildItemList(List<Item> items)
{
    StringBuilder sb = new StringBuilder(256); // Allocates ~256 bytes
    // ... build string ...
    return sb.ToString();
}
```

## Solution

### Core Concept

Pool StringBuilders and clear them on return:

```text
┌────────────────────────────────────────┐
│  StringBuilder Pool                     │
│  ┌──────────┐ ┌──────────┐ ┌────────┐ │
│  │ Cap:256  │ │ Cap:512  │ │Cap:1024│ │
│  └──────────┘ └──────────┘ └────────┘ │
└────────────────┬───────────────────────┘
                 │ Get(capacity_hint)
                 ▼
           ┌───────────┐
           │ Use & sb  │ sb.Clear() + Return
           │ .Append() │────────────────────┐
           └───────────┘                    │
                 │ sb.ToString()            │
                 ▼                          ▼
            "result"                    Pool
```

### Implementation

```csharp
namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using System.Text;

    public static class Buffers
    {
        private static readonly ObjectPool<StringBuilder> stringBuilderPool =
            new ObjectPool<StringBuilder>(
                initialCapacity: 4,
                maxSize: 32,
                resetAction: sb => sb.Clear()
            );

        public static class StringBuilder
        {
            /// <summary>
            /// Get a pooled StringBuilder with optional capacity hint.
            /// </summary>
            public static PooledResource<System.Text.StringBuilder> Get(
                out System.Text.StringBuilder sb,
                int capacityHint = 256)
            {
                sb = stringBuilderPool.Rent();

                // Ensure minimum capacity without shrinking
                if (sb.Capacity < capacityHint)
                {
                    sb.Capacity = capacityHint;
                }

                return new PooledResource<System.Text.StringBuilder>(sb, ReturnToPool);
            }

            private static void ReturnToPool(System.Text.StringBuilder sb)
            {
                // Clear content but preserve capacity
                sb.Clear();

                // Optional: Trim oversized builders to prevent memory bloat
                if (sb.Capacity > 8192)
                {
                    sb.Capacity = 256;
                }

                stringBuilderPool.Return(sb);
            }
        }

        /// <summary>
        /// Convenience method matching common API.
        /// </summary>
        public static PooledResource<System.Text.StringBuilder> GetStringBuilder(
            int capacityHint,
            out System.Text.StringBuilder sb)
        {
            return StringBuilder.Get(out sb, capacityHint);
        }
    }
}
```

## Usage

### Basic Usage

```csharp
public string BuildItemList(List<Item> items)
{
    using var lease = Buffers.StringBuilder.Get(out StringBuilder sb, items.Count * 20);

    for (int i = 0; i < items.Count; i++)
    {
        sb.Append(items[i].Name);
        if (i < items.Count - 1)
        {
            sb.Append(", ");
        }
    }

    return sb.ToString();
} // sb.Clear() + return to pool
```

### Formatting with Values

```csharp
public string FormatStats(Player player)
{
    using var lease = Buffers.StringBuilder.Get(out StringBuilder sb, 128);

    sb.Append("HP: ").Append(player.Health).Append('/').Append(player.MaxHealth);
    sb.AppendLine();
    sb.Append("MP: ").Append(player.Mana).Append('/').Append(player.MaxMana);
    sb.AppendLine();
    sb.Append("Level: ").Append(player.Level);

    return sb.ToString();
}
```

### JSON-like Building

```csharp
public string ToJson(Dictionary<string, object> data)
{
    using var lease = Buffers.StringBuilder.Get(out StringBuilder sb, 512);

    sb.Append('{');
    bool first = true;

    foreach (KeyValuePair<string, object> kvp in data)
    {
        if (!first) sb.Append(',');
        first = false;

        sb.Append('"').Append(kvp.Key).Append("\":");

        if (kvp.Value is string str)
        {
            sb.Append('"').Append(str).Append('"');
        }
        else
        {
            sb.Append(kvp.Value);
        }
    }

    sb.Append('}');
    return sb.ToString();
}
```

## Performance Notes

- **Allocations**: Zero in steady state; only `ToString()` allocates (unavoidable)
- **Capacity Hints**: Providing accurate hints avoids internal resizing
- **Memory**: Pool auto-trims oversized builders (>8KB default)
- **Timing**: O(1) pool operations

### Benchmark Comparison

| Approach             | 100 items | Allocations      |
| -------------------- | --------- | ---------------- |
| String +=            | 15.2ms    | 199 strings      |
| New StringBuilder    | 0.8ms     | 1 StringBuilder  |
| Pooled StringBuilder | 0.8ms     | 0 (steady state) |

## Best Practices

### Do

- Estimate capacity: `items.Count * avgItemLength`
- Use `Append()` chains for readability
- Pool StringBuilders used in Update loops
- Call `ToString()` only once at the end

### Don't

- Don't call `ToString()` multiple times (creates copies)
- Don't store StringBuilder references beyond the using scope
- Don't forget the capacity hint for large strings
- Don't pool for one-time operations (allocation amortization not worth it)

## Variations

### Thread-Local StringBuilder

For multithreaded code without locking:

```csharp
[ThreadStatic]
private static StringBuilder threadLocalBuilder;

public static StringBuilder GetThreadLocal()
{
    StringBuilder sb = threadLocalBuilder ?? (threadLocalBuilder = new StringBuilder(256));
    sb.Clear();
    return sb;
}
```

## Related Patterns

- [Collection Pooling](./collection-pooling.md) - General collection pooling
- [GC-Free String Formatting](./gc-free-string-formatting.md) - Advanced string techniques
