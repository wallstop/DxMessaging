---
title: "Try-Pattern API Usage Examples"
id: "try-pattern-apis-usage"
category: "solid"
version: "1.0.0"
created: "2026-01-21"
updated: "2026-01-21"

source:
  repository: "wallstop/unity-helpers"
  files:
    - path: "Runtime/Core/Utilities/"
  url: "https://github.com/wallstop/unity-helpers"

tags:
  - "solid"
  - "defensive"
  - "api-design"
  - "try-pattern"

complexity:
  level: "basic"
  reasoning: "Extends core try-pattern usage with variants"

impact:
  performance:
    rating: "medium"
    details: "Avoids exception allocations in expected failure cases"
  maintainability:
    rating: "high"
    details: "Encourages explicit error handling"
  testability:
    rating: "high"
    details: "Try-patterns are easy to test"

prerequisites:
  - "Understanding of out parameters"

dependencies:
  packages: []
  skills:
    - "try-pattern-apis"

applies_to:
  languages:
    - "C#"
  frameworks:
    - "Unity"
    - ".NET"

aliases:
  - "TryGet patterns"

related:
  - "try-pattern-apis"

status: "stable"
---

# Try-Pattern API Usage Examples

> **One-line summary**: Usage patterns for chaining, callbacks, and error handling.

## Overview

This skill focuses on Try-pattern usage in real flows.

## Solution

Apply the examples below to chain and compose Try-pattern calls.

## Usage

### Basic Try-Pattern Usage

```csharp
// Collection access
if (items.TryGet(index, out Item item))
{
    ProcessItem(item);
}
else
{
    HandleMissingItem(index);
}

// With default fallback
Item item = items.GetOrDefault(index, Item.Empty);

// Cache lookup
if (cache.TryGet(playerId, out PlayerData data))
{
    UpdateUI(data);
}
else
{
    StartDataLoad(playerId);
}
```

### Chained Try Operations

```csharp
public bool TryLoadPlayerWeapon(string playerId, out Weapon weapon)
{
    weapon = null;

    if (!playerCache.TryGet(playerId, out PlayerData player))
    {
        return false;
    }

    if (!weaponCache.TryGet(player.WeaponId, out weapon))
    {
        return false;
    }

    return true;
}
```

### Try with Action on Success

```csharp
// Extension for common pattern
public static void IfPresent<T>(this bool found, T value, Action<T> action)
{
    if (found)
    {
        action(value);
    }
}

// Usage
cache.TryGet(key, out var value).IfPresent(value, v => Process(v));
```
