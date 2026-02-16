---
title: "IEquatable Implementation for Value Types"
id: "iequatable-implementation"
category: "solid"
version: "1.0.0"
created: "2026-01-21"
updated: "2026-01-21"

source:
  repository: "wallstop/unity-helpers"
  files:
    - path: "Runtime/Core/DataStructure/FastVector2Int.cs"
    - path: "Runtime/Core/DataStructure/FastVector3Int.cs"
  url: "https://github.com/wallstop/unity-helpers"

tags:
  - "solid"
  - "performance"
  - "struct"
  - "equality"
  - "boxing"
  - "iequatable"

complexity:
  level: "intermediate"
  reasoning: "Requires understanding of boxing and equality contracts"

impact:
  performance:
    rating: "high"
    details: "Eliminates boxing in collection operations"
  maintainability:
    rating: "high"
    details: "Standard pattern with clear implementation"
  testability:
    rating: "high"
    details: "Equality easily unit tested"

prerequisites:
  - "Understanding of boxing"
  - "Knowledge of equality contract"

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
  - "Struct equality"
  - "Value type equality"
  - "Boxing avoidance"

related:
  - "readonly-struct-cached-hash"
  - "collection-extensions"
  - "iequatable-implementation-variants"
  - "iequatable-implementation-usage"

status: "stable"
---

# IEquatable<T> Implementation for Value Types

> **One-line summary**: Implement `IEquatable<T>` on structs to avoid boxing allocations when used in collections, providing 10-100x faster equality comparisons.

## Overview

When structs are compared via `object.Equals()`, they are boxed (heap-allocated). Implementing `IEquatable<T>`:

1. **Avoids boxing** - direct typed comparison
1. **Enables optimization** - collections use generic `Equals(T)`
1. **Clear contract** - explicit equality semantics

## Problem Statement

```csharp
// BAD: Default struct equality uses reflection and boxes
public struct BadPoint
{
    public int X, Y;
}

var dict = new Dictionary<BadPoint, string>();
var point = new BadPoint { X = 1, Y = 2 };

// Each operation boxes the struct!
dict.Add(point, "A");     // Box for GetHashCode
dict.ContainsKey(point);  // Box for Equals

// Default Equals uses reflection - SLOW
bool equal = point.Equals(otherPoint); // Reflection-based comparison
```

### Boxing Cost

For 128,000 `HashSet.Contains` calls:

- Without IEquatable: 4MB allocations (boxing)
- With IEquatable: 0 allocations

## Solution

### Complete IEquatable Implementation

```csharp
namespace WallstopStudios.UnityHelpers.Core.DataStructure
{
    using System;
    using System.Runtime.CompilerServices;

    /// <summary>
    /// Properly implemented struct with IEquatable for optimal collection performance.
    /// </summary>
    public readonly struct Point : IEquatable<Point>
    {
        public readonly int X;
        public readonly int Y;

        public Point(int x, int y)
        {
            X = x;
            Y = y;
        }

        /// <summary>
        /// Typed equality comparison - no boxing.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(Point other)
        {
            return X == other.X && Y == other.Y;
        }

        /// <summary>
        /// Object equality - boxes if other is Point, but necessary for interface.
        /// </summary>
        public override bool Equals(object obj)
        {
            return obj is Point other && Equals(other);
        }

        /// <summary>
        /// Hash code for dictionary/hashset operations.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + X;
                hash = hash * 31 + Y;
                return hash;
            }
        }

        /// <summary>
        /// Equality operator for natural syntax.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(Point left, Point right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Inequality operator.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(Point left, Point right)
        {
            return !left.Equals(right);
        }

        public override string ToString() => $"({X}, {Y})";
    }
}
```

## Performance Notes

### Boxing Cost

| Operation         | Without IEquatable | With IEquatable |
| ----------------- | ------------------ | --------------- |
| Dictionary lookup | ~50ns + 24B alloc  | ~10ns, 0 alloc  |
| HashSet.Contains  | ~30ns + 24B alloc  | ~5ns, 0 alloc   |
| List.Contains     | ~20ns + 24B alloc  | ~5ns, 0 alloc   |

### Memory Impact (128K operations)

| Without IEquatable | With IEquatable |
| ------------------ | --------------- |
| 4MB allocations    | 0 allocations   |
| GC pauses          | No GC pressure  |

## Best Practices

### Do

- Always implement `IEquatable<T>` for structs used in collections
- Override `Equals(object)` for compatibility
- Override `GetHashCode()` with consistent hash
- Implement `==` and `!=` operators
- Use `readonly struct` to prevent mutation
- Use `[MethodImpl(AggressiveInlining)]` for hot paths

### Don't

- Don't rely on default struct equality (uses reflection)
- Don't forget any of the four members (Equals, GetHashCode, ==, !=)
- Don't mutate fields used in hash calculation
- Don't ignore nullable fields in equality/hash

### Equality Contract

```text
1. Reflexive: a.Equals(a) == true
2. Symmetric: a.Equals(b) == b.Equals(a)
3. Transitive: if a.Equals(b) && b.Equals(c) then a.Equals(c)
4. Consistent: multiple calls return same result
5. Null: a.Equals(null) == false (for reference types)
6. Hash: if a.Equals(b) then a.GetHashCode() == b.GetHashCode()
```

## Related Patterns

- [Readonly Struct with Cached Hash](../performance/readonly-struct-cached-hash.md) - Advanced hashing
- [Collection Extensions](./collection-extensions.md) - Collection utilities
- [IEquatable Implementation Variants](./iequatable-implementation-variants.md) - Cached hash and nullable patterns
- [IEquatable Usage Examples](./iequatable-implementation-usage.md) - Collections and comparisons
