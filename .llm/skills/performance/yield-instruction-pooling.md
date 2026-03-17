---
title: "WaitForSeconds and Yield Instruction Pooling"
id: "yield-instruction-pooling"
category: "performance"
version: "1.0.0"
created: "2026-01-21"
updated: "2026-01-21"

source:
  repository: "wallstop/unity-helpers"
  files:
    - path: "Runtime/Utils/Buffers.cs"
      lines: "148-467"
  url: "https://github.com/wallstop/unity-helpers"

tags:
  - "performance"
  - "unity"
  - "coroutines"
  - "pooling"
  - "zero-alloc"
  - "yield"

complexity:
  level: "basic"
  reasoning: "Simple caching pattern with straightforward API"

impact:
  performance:
    rating: "high"
    details: "Eliminates per-coroutine allocations for common wait instructions"
  maintainability:
    rating: "high"
    details: "Drop-in replacement for standard yield instructions"
  testability:
    rating: "high"
    details: "Coroutine behavior unchanged; only allocation differs"

prerequisites:
  - "Understanding of Unity coroutines"
  - "Knowledge of yield instructions"

dependencies:
  packages: []
  skills: []

applies_to:
  languages:
    - "C#"
  frameworks:
    - "Unity"
  versions:
    unity: ">=2021.3"

aliases:
  - "Cached WaitForSeconds"
  - "Coroutine optimization"
  - "Yield caching"

related:
  - "object-pooling"
  - "collection-pooling"

status: "stable"
---

# WaitForSeconds and Yield Instruction Pooling

> **One-line summary**: Cache and reuse Unity yield instructions like `WaitForSeconds` to eliminate per-coroutine allocations.

## Overview

Unity coroutines frequently use `yield return new WaitForSeconds(x)`, creating a new object each time. Since `WaitForSeconds` is reusable after completion, we can cache instances by duration to achieve zero-allocation coroutines.

## Problem Statement

```csharp
// BAD: Allocates 20 bytes every call
private IEnumerator SpawnEnemies()
{
    while (true)
    {
        SpawnEnemy();
        yield return new WaitForSeconds(2f); // New allocation!
    }
}
```

With 100 coroutines at 1 yield/second = 2KB/second = 7.2MB/hour of garbage.

## Solution

Refer to the detailed implementation guides linked below, which cover:

- implementation strategy and data structures
- code examples with patterns and variations
- usage examples and testing considerations
- performance notes and anti-patterns

## See Also

- [yield instruction pooling part 1](./yield-instruction-pooling-part-1.md)
- [yield instruction pooling part 2](./yield-instruction-pooling-part-2.md)
