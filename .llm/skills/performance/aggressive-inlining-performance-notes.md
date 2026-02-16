---
title: "AggressiveInlining Performance Notes"
id: "aggressive-inlining-performance-notes"
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
  - "benchmark"

complexity:
  level: "intermediate"
  reasoning: "Requires reading JIT behavior and benchmark results"

impact:
  performance:
    rating: "medium"
    details: "Provides context on when inlining is measurable"
  maintainability:
    rating: "low"
    details: "Informational only"
  testability:
    rating: "none"
    details: "No test impact"

prerequisites:
  - "Understanding of JIT inlining"

dependencies:
  packages: []
  skills:
    - "aggressive-inlining"

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
  - "Inlining benchmarks"

related:
  - "aggressive-inlining"

status: "stable"
---

# AggressiveInlining Performance Notes

> **One-line summary**: Benchmark and JIT behavior notes for aggressive inlining decisions.

## Overview

This skill captures benchmark results and JIT behavior that inform inlining decisions.

## Solution

Use the notes below to decide when AggressiveInlining provides measurable wins.

## Performance Notes

### Benchmark: 10M Dictionary Lookups

| Approach                   | Time    |
| -------------------------- | ------- |
| Without AggressiveInlining | 142ms   |
| With AggressiveInlining    | 98ms    |
| **Improvement**            | **31%** |

### JIT Behavior

- **Default**: JIT inlines methods < 32 IL bytes
- **AggressiveInlining**: JIT tries harder, may inline larger methods
- **Not a guarantee**: JIT can still refuse (virtual, try-catch, etc.)
