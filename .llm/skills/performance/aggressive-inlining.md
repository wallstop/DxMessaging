---
title: "AggressiveInlining for Hot Path Optimization"
id: "aggressive-inlining"
category: "performance"
version: "1.0.0"
created: "2026-01-21"
updated: "2026-01-21"

source:
  repository: "wallstop/unity-helpers"
  files:
    - path: "Runtime/Core/DataStructure/ImmutableBitSet.cs"
      lines: "55-99"
    - path: "Runtime/Core/DataStructure/BitSet.cs"
      lines: "589-613"
    - path: "Runtime/Core/OneOf/FastOneOf.cs"
      lines: "129-196"
  url: "https://github.com/wallstop/unity-helpers"

tags:
  - "performance"
  - "inlining"
  - "optimization"
  - "hot-path"
  - "methodimpl"

complexity:
  level: "intermediate"
  reasoning: "Requires understanding of JIT behavior and when inlining helps vs hurts"

impact:
  performance:
    rating: "medium"
    details: "Eliminates method call overhead for small, frequently called methods"
  maintainability:
    rating: "high"
    details: "Attribute is self-documenting; doesn't change behavior"
  testability:
    rating: "high"
    details: "No impact on testability"

prerequisites:
  - "Understanding of JIT compilation"
  - "Knowledge of method call overhead"

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
  - "Method inlining"
  - "Inline hint"
  - "JIT optimization"

related:
  - "readonly-struct-cached-hash"
  - "aggressive-inlining-performance-notes"

status: "stable"
---

# AggressiveInlining for Hot Path Optimization

> **One-line summary**: Use `[MethodImpl(MethodImplOptions.AggressiveInlining)]` to hint the JIT compiler to inline small, hot methods, eliminating call overhead.

## Overview

Method calls have overhead: push arguments, call, pop return value. For very small methods called millions of times (e.g., property getters, math operations), this overhead can be significant. `AggressiveInlining` tells the JIT to strongly prefer inlining, replacing the call with the method body directly.

## Problem Statement

```csharp
// Without inlining hint, JIT may not inline this
public int GetValue()
{
    return _value;
}

// In a hot loop, call overhead accumulates
for (int i = 0; i < 1000000; i++)
{
    sum += obj.GetValue(); // Potential call overhead each iteration
}
```

## Solution

### Core Concept

```text
Without Inlining:                    With Inlining:
─────────────────                    ────────────────
call GetValue()  ─┐                  sum += _value;
  push this       │ ~3-5 cycles      (directly inline)
  push return     │
  pop result     ─┘                  ~1 cycle
```

### Implementation

```csharp
namespace WallstopStudios.UnityHelpers.Core
{
    using System.Runtime.CompilerServices;

    public readonly struct BitSet
    {
        private readonly ulong[] bits;
        private readonly int count;

        /// <summary>
        /// Gets the number of bits that can be stored.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Capacity => bits.Length * 64;

        /// <summary>
        /// Gets the number of bits currently set to true.
        /// </summary>
        public int Count => count;

        /// <summary>
        /// Checks if a bit is set.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Get(int index)
        {
            int wordIndex = index >> 6;  // index / 64
            int bitIndex = index & 63;   // index % 64
            return (bits[wordIndex] & (1UL << bitIndex)) != 0;
        }

        /// <summary>
        /// Checks if this set contains all bits from another set.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ContainsAll(BitSet other)
        {
            for (int i = 0; i < bits.Length; i++)
            {
                if ((bits[i] & other.bits[i]) != other.bits[i])
                    return false;
            }
            return true;
        }
    }
}
```

### Pattern Matching with Inline

```csharp
public readonly struct FastOneOf<T0, T1, T2>
{
    private readonly byte index;
    private readonly T0 value0;
    private readonly T1 value1;
    private readonly T2 value2;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Is<T>(out T value)
    {
        if (typeof(T) == typeof(T0) && index == 0)
        {
            value = (T)(object)value0;
            return true;
        }
        if (typeof(T) == typeof(T1) && index == 1)
        {
            value = (T)(object)value1;
            return true;
        }
        if (typeof(T) == typeof(T2) && index == 2)
        {
            value = (T)(object)value2;
            return true;
        }
        value = default;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TResult Match<TResult>(
        Func<T0, TResult> f0,
        Func<T1, TResult> f1,
        Func<T2, TResult> f2)
    {
        return index switch
        {
            0 => f0(value0),
            1 => f1(value1),
            2 => f2(value2),
            _ => throw new InvalidOperationException()
        };
    }
}
```

### Equality Operations

```csharp
public readonly struct FastVector2Int : IEquatable<FastVector2Int>
{
    public readonly int x;
    public readonly int y;
    private readonly int _hash;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() => _hash;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(FastVector2Int other)
    {
        return _hash == other._hash && x == other.x && y == other.y;
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
}
```

## Usage Guidelines

### Good Candidates for AggressiveInlining

```csharp
// ✅ Property getters (trivial)
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public int Count => _count;

// ✅ Simple arithmetic/logic
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static int Clamp(int value, int min, int max)
{
    return value < min ? min : (value > max ? max : value);
}

// ✅ Hash/equality operations
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public override int GetHashCode() => _cachedHash;

// ✅ Type checks
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public bool IsValid => _data != null;

// ✅ Forwarding calls
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public T Get(int index) => _array[index];
```

### Poor Candidates

```csharp
// ❌ Large method bodies
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public void ProcessData()
{
    // 50+ lines of code
    // Inlining this everywhere bloats code size
}

// ❌ Virtual methods (can't inline)
[MethodImpl(MethodImplOptions.AggressiveInlining)] // Ignored
public virtual void Update() { }

// ❌ Methods with try-catch
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public void DoSomething()
{
    try { /* ... */ }
    catch { /* ... */ }
}

// ❌ Recursive methods
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public int Factorial(int n) => n <= 1 ? 1 : n * Factorial(n - 1);

// ❌ Cold paths (rarely called)
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public void HandleError() { /* called 0.01% of time */ }
```

## Best Practices

### Do

- Apply to property getters and simple accessors
- Apply to equality/hash methods on value types
- Apply to frequently called math utilities
- Apply to forwarding methods (thin wrappers)
- Measure before and after

### Don't

- Don't apply to virtual methods (ignored)
- Don't apply to methods with try-catch blocks
- Don't apply to large methods (code bloat)
- Don't apply to cold paths (no benefit)
- Don't assume it always helps (measure!)

### Combining with readonly struct

```csharp
// Best combo: readonly struct + AggressiveInlining
public readonly struct Point
{
    public readonly float x;
    public readonly float y;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float DistanceSquared(Point other)
    {
        float dx = x - other.x;
        float dy = y - other.y;
        return dx * dx + dy * dy;
    }
}
```

## Related Patterns

- [Readonly Struct with Cached Hash](./readonly-struct-cached-hash.md) - Uses AggressiveInlining
- [AggressiveInlining Performance Notes](./aggressive-inlining-performance-notes.md) - Benchmarks and JIT behavior
