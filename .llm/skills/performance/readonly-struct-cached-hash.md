---
title: "Readonly Struct with Cached Hash for Dictionary Keys"
id: "readonly-struct-cached-hash"
category: "performance"
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
  - "performance"
  - "struct"
  - "hashcode"
  - "dictionary"
  - "zero-alloc"
  - "iequatable"

complexity:
  level: "intermediate"
  reasoning: "Requires understanding of value types, hashing, and IEquatable<T>"

impact:
  performance:
    rating: "high"
    details: "Eliminates boxing and reduces hash computation in dictionary operations"
  maintainability:
    rating: "high"
    details: "Standard pattern that's easy to recognize and extend"
  testability:
    rating: "high"
    details: "Equality and hashing easily unit tested"

prerequisites:
  - "Understanding of structs vs classes"
  - "Knowledge of GetHashCode contract"
  - "Familiarity with dictionary internals"

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
  - "Fast struct key"
  - "Pre-computed hash"
  - "Value type dictionary key"

related:
  - "iequatable-implementation"
  - "aggressive-inlining"
  - "readonly-struct-cached-hash-performance-notes"

status: "stable"
---

# Readonly Struct with Cached Hash for Dictionary Keys

> **One-line summary**: Pre-compute hash codes at construction time for value types used as dictionary keys, eliminating repeated hash calculations and enabling hash-based early-out in equality checks.

## Overview

When using structs as dictionary keys, each lookup calls `GetHashCode()` and potentially `Equals()`. By:

1. Computing the hash once at construction
1. Storing it in a readonly field
1. Using it as an early-out in `Equals()`

We achieve optimal dictionary performance with zero allocations.

## Problem Statement

```csharp
// BAD: Unity's Vector2Int recomputes hash every call
public struct Vector2Int
{
    public int x, y;

    public override int GetHashCode()
    {
        // Computed every dictionary operation
        return x.GetHashCode() ^ (y.GetHashCode() << 2);
    }

    public override bool Equals(object obj)
    {
        // Boxing! Creates garbage for struct comparison
        if (obj is Vector2Int other)
            return x == other.x && y == other.y;
        return false;
    }
}
```

For a dictionary with 10,000 lookups/frame:

- 10,000 hash computations (unnecessary work)
- Potential boxing if `Equals(object)` is called
- No early-out optimization

## Solution

### Core Concept

```text
┌──────────────────────────────────────────────────────────────┐
│  FastVector2Int (readonly struct)                            │
├──────────────────────────────────────────────────────────────┤
│  readonly int x = 5                                          │
│  readonly int y = 10                                         │
│  readonly int _hash = 0x7A3B2C1D  ← Computed ONCE in ctor   │
├──────────────────────────────────────────────────────────────┤
│  GetHashCode() → return _hash     ← O(1), no computation    │
│  Equals(other) → if (_hash != other._hash) return false;    │
│                   return x == other.x && y == other.y;       │
│                   ↑ Early-out on hash mismatch              │
└──────────────────────────────────────────────────────────────┘
```

### Implementation

```csharp
namespace WallstopStudios.UnityHelpers.Core.DataStructure
{
    using System;
    using System.Runtime.CompilerServices;

    /// <summary>
    /// High-performance 2D integer coordinate for use as dictionary key.
    /// Pre-computes hash at construction; implements IEquatable to avoid boxing.
    /// </summary>
    public readonly struct FastVector2Int : IEquatable<FastVector2Int>
    {
        public readonly int x;
        public readonly int y;
        private readonly int _hash;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FastVector2Int(int x, int y)
        {
            this.x = x;
            this.y = y;
            _hash = ComputeHash(x, y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ComputeHash(int x, int y)
        {
            // High-quality hash combining
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + x;
                hash = hash * 31 + y;
                return hash;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode() => _hash;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(FastVector2Int other)
        {
            // Early-out: different hash = definitely not equal
            // Same hash = probably equal, verify with full comparison
            return _hash == other._hash && x == other.x && y == other.y;
        }

        public override bool Equals(object obj)
        {
            return obj is FastVector2Int other && Equals(other);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(FastVector2Int left, FastVector2Int right)
        {
            return left.Equals(right);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(FastVector2Int left, FastVector2Int right)
        {
            return !left.Equals(right);
        }

        public override string ToString() => $"({x}, {y})";

        // Implicit conversion from Unity's Vector2Int
        public static implicit operator FastVector2Int(UnityEngine.Vector2Int v)
        {
            return new FastVector2Int(v.x, v.y);
        }

        public static implicit operator UnityEngine.Vector2Int(FastVector2Int v)
        {
            return new UnityEngine.Vector2Int(v.x, v.y);
        }
    }
}
```

### 3D Variant

```csharp
public readonly struct FastVector3Int : IEquatable<FastVector3Int>
{
    public readonly int x;
    public readonly int y;
    public readonly int z;
    private readonly int _hash;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FastVector3Int(int x, int y, int z)
    {
        this.x = x;
        this.y = y;
        this.z = z;

        unchecked
        {
            int hash = 17;
            hash = hash * 31 + x;
            hash = hash * 31 + y;
            hash = hash * 31 + z;
            _hash = hash;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() => _hash;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(FastVector3Int other)
    {
        return _hash == other._hash
            && x == other.x
            && y == other.y
            && z == other.z;
    }

    // ... operators, Equals(object), etc.
}
```

## Usage

### As Dictionary Key

```csharp
// Tile lookup by position
private readonly Dictionary<FastVector2Int, Tile> tiles =
    new Dictionary<FastVector2Int, Tile>(1024);

public Tile GetTile(int x, int y)
{
    FastVector2Int key = new FastVector2Int(x, y);
    return tiles.TryGetValue(key, out Tile tile) ? tile : null;
}

public void SetTile(int x, int y, Tile tile)
{
    tiles[new FastVector2Int(x, y)] = tile;
}
```

### With HashSet

```csharp
private readonly HashSet<FastVector2Int> visitedCells =
    new HashSet<FastVector2Int>(256);

public bool TryVisit(int x, int y)
{
    return visitedCells.Add(new FastVector2Int(x, y));
}
```

### Conversion from Unity Types

```csharp
// Implicit conversion makes usage transparent
FastVector2Int fast = someVector2Int; // No explicit cast needed
UnityEngine.Vector2Int unity = fast;  // And back
```

## Best Practices

### Do

- Always implement `IEquatable<T>` for struct dictionary keys
- Use `readonly struct` to prevent defensive copies
- Apply `[MethodImpl(MethodImplOptions.AggressiveInlining)]` to hot methods
- Provide implicit conversions for interop with framework types
- Use `unchecked` for hash computation (overflow is fine)

### Don't

- Don't rely on default struct equality (uses reflection)
- Don't forget to implement both `==` and `!=` operators
- Don't use mutable fields in hash computation
- Don't use this pattern for rarely-hashed types (overhead not worth it)

### Hash Function Quality

For good distribution:

```csharp
// Good: Prime multiplication
hash = hash * 31 + field;

// Also good: HashCode.Combine (C# 8+)
_hash = HashCode.Combine(x, y);

// Also good: Unity-style XOR with shift
_hash = x ^ (y << 2);
```

## Related Patterns

- [IEquatable Implementation](./iequatable-implementation.md) - Full equality pattern
- [Aggressive Inlining](./aggressive-inlining.md) - When to use MethodImpl
- [Readonly Struct Cached Hash Performance Notes](./readonly-struct-cached-hash-performance-notes.md) - Benchmarks and rationale
