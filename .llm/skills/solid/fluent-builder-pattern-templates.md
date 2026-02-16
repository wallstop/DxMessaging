---
title: "Fluent Builder Templates and Factories"
id: "fluent-builder-pattern-templates"
category: "solid"
version: "1.0.0"
created: "2026-01-21"
updated: "2026-01-21"

source:
  repository: "wallstop/unity-helpers"
  files:
    - path: "Runtime/Core/Builders/"
  url: "https://github.com/wallstop/unity-helpers"

tags:
  - "solid"
  - "patterns"
  - "builder"
  - "fluent-api"

complexity:
  level: "intermediate"
  reasoning: "Builds on fluent builder structure with reusable templates"

impact:
  performance:
    rating: "medium"
    details: "Struct builders minimize allocations"
  maintainability:
    rating: "high"
    details: "Templates standardize builder APIs"
  testability:
    rating: "high"
    details: "Builder validation can be unit-tested"

prerequisites:
  - "Understanding of fluent builders"

dependencies:
  packages: []
  skills:
    - "fluent-builder-pattern"

applies_to:
  languages:
    - "C#"
  frameworks:
    - "Unity"
    - ".NET"

aliases:
  - "Builder templates"

related:
  - "fluent-builder-pattern"

status: "stable"
---

# Fluent Builder Templates and Factories

> **One-line summary**: Static factory entry points and reusable builder templates.

## Overview

This skill provides reusable templates and entry points for builders.

## Solution

Use these templates to standardize fluent builder APIs.

### Static Factory Entry Point

```csharp
/// <summary>
/// Entry point for cache creation.
/// </summary>
public static class Cache
{
    /// <summary>
    /// Creates a new cache builder.
    /// </summary>
    public static CacheBuilder<TKey, TValue> Builder<TKey, TValue>()
    {
        return new CacheBuilder<TKey, TValue>();
    }
}
```

### Generic Builder Template

```csharp
/// <summary>
/// Template for struct-based builders.
/// </summary>
public struct ObjectBuilder<T> where T : class, new()
{
    private T prototype;

    private T GetOrCreatePrototype()
    {
        return prototype ?? (prototype = new T());
    }

    public ObjectBuilder<T> With(Action<T> configure)
    {
        var copy = this;
        configure(copy.GetOrCreatePrototype());
        return copy;
    }

    public T Build()
    {
        return prototype ?? new T();
    }
}
```
