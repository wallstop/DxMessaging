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

Refer to the detailed implementation guides linked below, which cover:

- implementation strategy and data structures
- code examples with patterns and variations
- usage examples and testing considerations
- performance notes and anti-patterns

## See Also

- [fluent builder pattern part 1](./fluent-builder-pattern-part-1.md)
- [fluent builder pattern part 2](./fluent-builder-pattern-part-2.md)
