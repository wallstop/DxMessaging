---
title: "Auto-Load Singleton Attribute"
id: "singleton-autoload"
category: "performance"
version: "1.0.0"
created: "2026-01-21"
updated: "2026-01-21"

source:
  repository: "wallstop/unity-helpers"
  files:
    - path: "Runtime/Core/Singletons/RuntimeSingleton.cs"
    - path: "Runtime/Core/Singletons/ScriptableObjectSingleton.cs"
  url: "https://github.com/wallstop/unity-helpers"

tags:
  - "unity"
  - "singleton"
  - "patterns"

complexity:
  level: "intermediate"
  reasoning: "Covers Unity-specific lifecycle and global access patterns"

impact:
  performance:
    rating: "low"
    details: "Singletons are about lifecycle management, not raw speed"
  maintainability:
    rating: "medium"
    details: "Global state needs discipline to avoid coupling"
  testability:
    rating: "medium"
    details: "Requires reset hooks or abstraction for tests"

prerequisites:
  - "Understanding of Unity lifecycle"

dependencies:
  packages: []
  skills:
    - "singleton-patterns"

applies_to:
  languages:
    - "C#"
  frameworks:
    - "Unity"
  versions:
    unity: ">=2021.3"

aliases:
  - "Unity singleton patterns"

related:
  - "singleton-patterns"

status: "stable"
---

# Auto-Load Singleton Attribute

> **One-line summary**: Auto-loading singleton bootstrapping for Unity scenes.

## Overview

This skill explains auto-load singleton bootstrapping in Unity.

## Solution

Use the attribute and loader pattern below to ensure early initialization.

### AutoLoadSingleton Attribute

```csharp
namespace WallstopStudios.UnityHelpers.Utils
{
    using System;

    /// <summary>
    /// Apply to RuntimeSingleton to auto-create on game start.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class AutoLoadSingletonAttribute : Attribute
    {
    }
}

// RuntimeInitializeOnLoad handler
public static class SingletonAutoLoader
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void AutoLoadSingletons()
    {
        // Find all types with AutoLoadSingleton attribute
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            foreach (var type in assembly.GetTypes())
            {
                if (type.GetCustomAttributes(typeof(AutoLoadSingletonAttribute), true).Length > 0)
                {
                    // Access Instance property to trigger creation
                    var instanceProperty = type.GetProperty("Instance",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    instanceProperty?.GetValue(null);
                }
            }
        }
    }
}
```
