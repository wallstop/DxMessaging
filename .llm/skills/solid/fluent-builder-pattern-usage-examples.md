---
title: "Fluent Builder Usage Examples"
id: "fluent-builder-pattern-usage-examples"
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

# Fluent Builder Usage Examples

> **One-line summary**: Usage patterns for builder configuration and validation.

## Overview

This skill covers usage patterns for fluent builders and validation.

## Solution

Use the examples below to configure and validate builders.

## Usage

### Basic Builder Usage

```csharp
// Clear, self-documenting configuration
using Cache<string, GameData> cache = new CacheBuilder<string, GameData>()
    .WithMaximumSize(1000)
    .WithPolicy(EvictionPolicy.Lru)
    .WithExpireAfterWrite(TimeSpan.FromMinutes(5))
    .WithExpireAfterAccess(TimeSpan.FromMinutes(1))
    .WithRecordStats()
    .Build();
```

### Using Static Factory

```csharp
using var cache = Cache.Builder<string, int>()
    .WithMaximumSize(500)
    .Build();
```

### Partial Configuration

```csharp
// Store partial configuration for reuse
CacheBuilder<string, PlayerData> baseBuilder = new CacheBuilder<string, PlayerData>()
    .WithMaximumSize(1000)
    .WithPolicy(EvictionPolicy.Lru);

// Extend for different use cases
using var shortLivedCache = baseBuilder
    .WithExpireAfterWrite(TimeSpan.FromMinutes(1))
    .Build();

using var longLivedCache = baseBuilder
    .WithExpireAfterWrite(TimeSpan.FromHours(1))
    .WithRecordStats()
    .Build();
```

### With Validation

```csharp
// Invalid configuration fails at Build() time
try
{
    var cache = new CacheBuilder<string, int>()
        .WithExpirationJitter(0.1f) // Jitter without expiration!
        .Build();
}
catch (InvalidOperationException ex)
{
    Debug.Log(ex.Message); // "Jitter requires expiration to be set..."
}
```
