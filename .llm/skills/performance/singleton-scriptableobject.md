---
title: "ScriptableObject Singleton Pattern"
id: "singleton-scriptableobject"
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

# ScriptableObject Singleton Pattern

> **One-line summary**: ScriptableObject-backed singleton with asset-based configuration.

## Overview

This skill focuses on ScriptableObject-backed singleton patterns.

## Solution

Use the asset-based singleton below for global configuration.

### ScriptableObjectSingleton<T>

```csharp
namespace WallstopStudios.UnityHelpers.Utils
{
    using UnityEngine;

    /// <summary>
    /// Attribute to specify the Resources path for a ScriptableObjectSingleton.
    /// </summary>
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public class ScriptableSingletonPathAttribute : System.Attribute
    {
        public string Path { get; }

        public ScriptableSingletonPathAttribute(string path)
        {
            Path = path;
        }
    }

    /// <summary>
    /// ScriptableObject that loads itself from Resources folder on first access.
    /// </summary>
    public abstract class ScriptableObjectSingleton<T> : ScriptableObject where T : ScriptableObjectSingleton<T>
    {
        private static T instance;
        private static readonly object lockObject = new object();

        /// <summary>
        /// Gets the singleton instance, loading from Resources if needed.
        /// </summary>
        public static T Instance
        {
            get
            {
                lock (lockObject)
                {
                    if (instance == null)
                    {
                        instance = LoadInstance();
                    }
                    return instance;
                }
            }
        }

        /// <summary>
        /// Returns true if an instance exists (without loading one).
        /// </summary>
        public static bool HasInstance
        {
            get
            {
                lock (lockObject)
                {
                    return instance != null;
                }
            }
        }

        private static T LoadInstance()
        {
            string path = GetResourcePath();
            T loaded = Resources.Load<T>(path);

            if (loaded == null)
            {
                Debug.LogError($"[ScriptableObjectSingleton] Failed to load {typeof(T).Name} from Resources/{path}");

                // Create runtime instance as fallback
                loaded = CreateInstance<T>();
                loaded.name = typeof(T).Name;
            }

            return loaded;
        }

        private static string GetResourcePath()
        {
            // Check for attribute
            object[] attributes = typeof(T).GetCustomAttributes(typeof(ScriptableSingletonPathAttribute), true);

            if (attributes.Length > 0)
            {
                return ((ScriptableSingletonPathAttribute)attributes[0]).Path;
            }

            // Default path
            return $"Config/{typeof(T).Name}";
        }

        /// <summary>
        /// Called after loading. Override to perform initialization.
        /// </summary>
        protected virtual void OnEnable()
        {
            if (instance == null)
            {
                instance = (T)this;
            }
        }
    }
}
```
