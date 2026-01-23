---
title: "IEquatable Usage Examples"
id: "iequatable-implementation-usage"
category: "solid"
version: "1.0.0"
created: "2026-01-21"
updated: "2026-01-21"

source:
  repository: "wallstop/unity-helpers"
  files:
    - path: "Runtime/Core/DataStructure/"
  url: "https://github.com/wallstop/unity-helpers"

tags:
  - "solid"
  - "performance"
  - "struct"
  - "equality"

complexity:
  level: "intermediate"
  reasoning: "Builds on core IEquatable implementation details"

impact:
  performance:
    rating: "medium"
    details: "Variants reduce boxing and hash recomputation"
  maintainability:
    rating: "medium"
    details: "More code, but consistent patterns"
  testability:
    rating: "high"
    details: "Equality logic can be unit tested"

prerequisites:
  - "Understanding of IEquatable"

dependencies:
  packages: []
  skills:
    - "iequatable-implementation"

applies_to:
  languages:
    - "C#"
  frameworks:
    - "Unity"
    - ".NET"

aliases:
  - "Equality variants"

related:
  - "iequatable-implementation"

status: "stable"
---

# IEquatable Usage Examples

> **One-line summary**: Using IEquatable in dictionaries, hash sets, and comparisons.

## Overview

This skill shows how to apply IEquatable in common collection scenarios.

## Solution

Use the examples below to integrate equality in dictionaries and sets.

## Usage

### In Dictionary

```csharp
var positions = new Dictionary<Point, GameObject>(256);

// All operations use typed Equals and GetHashCode - no boxing
positions[new Point(1, 2)] = playerObject;

if (positions.TryGetValue(new Point(1, 2), out GameObject go))
{
    // Found!
}
```

### In HashSet

```csharp
var visited = new HashSet<Point>(512);

// Zero allocations for these operations
visited.Add(new Point(x, y));
if (visited.Contains(currentPosition))
{
    return; // Already visited
}
```

### Equality Comparisons

```csharp
Point a = new Point(1, 2);
Point b = new Point(1, 2);
Point c = new Point(3, 4);

bool equal = a == b;     // true, uses operator ==
bool notEqual = a != c;  // true, uses operator !=
bool alsoEqual = a.Equals(b); // true, direct typed call
```
