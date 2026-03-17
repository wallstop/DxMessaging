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

Refer to the detailed implementation guides linked below, which cover:

- implementation strategy and data structures
- code examples with patterns and variations
- usage examples and testing considerations
- performance notes and anti-patterns

## See Also

- [aggressive inlining part 1](./aggressive-inlining-part-1.md)
- [aggressive inlining part 2](./aggressive-inlining-part-2.md)
