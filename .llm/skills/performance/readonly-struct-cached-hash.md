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

Refer to the detailed implementation guides linked below, which cover:

- implementation strategy and data structures
- code examples with patterns and variations
- usage examples and testing considerations
- performance notes and anti-patterns

## See Also

- [readonly struct cached hash part 1](./readonly-struct-cached-hash-part-1.md)
- [readonly struct cached hash part 2](./readonly-struct-cached-hash-part-2.md)
