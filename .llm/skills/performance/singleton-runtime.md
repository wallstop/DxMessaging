---
title: "Runtime Singleton Pattern"
id: "singleton-runtime"
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

# Runtime Singleton Pattern

> **One-line summary**: Runtime singleton implementation with lazy instance creation.

## Overview

This skill focuses on runtime singleton patterns for MonoBehaviours.

## Solution

Apply the implementation below to enforce a single instance safely.

### RuntimeSingleton<T>

```csharp
namespace WallstopStudios.UnityHelpers.Utils
{
    using UnityEngine;

    /// <summary>
    /// Thread-safe MonoBehaviour singleton that persists across scenes.
    /// </summary>
    public abstract class RuntimeSingleton<T> : MonoBehaviour where T : RuntimeSingleton<T>
    {
        private static T instance;
        private static readonly object lockObject = new object();
        private static bool applicationIsQuitting;

        /// <summary>
        /// Whether to call DontDestroyOnLoad. Override to return false for scene-scoped singletons.
        /// </summary>
        protected virtual bool Preserve => true;

        /// <summary>
        /// Gets the singleton instance. Creates one if needed.
        /// </summary>
        public static T Instance
        {
            get
            {
                if (applicationIsQuitting)
                {
                    Debug.LogWarning($"[RuntimeSingleton] Instance of {typeof(T)} requested after application quit.");
                    return null;
                }

                lock (lockObject)
                {
                    if (instance == null)
                    {
                        instance = FindObjectOfType<T>();

                        if (instance == null)
                        {
                            GameObject singletonObject = new GameObject($"[{typeof(T).Name}]");
                            instance = singletonObject.AddComponent<T>();
                        }
                    }

                    return instance;
                }
            }
        }

        /// <summary>
        /// Returns true if an instance exists (without creating one).
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

        protected virtual void Awake()
        {
            lock (lockObject)
            {
                if (instance == null)
                {
                    instance = (T)this;

                    if (Preserve)
                    {
                        transform.SetParent(null); // Ensure not child of another object
                        DontDestroyOnLoad(gameObject);
                    }

                    OnSingletonAwake();
                }
                else if (instance != this)
                {
                    Debug.LogWarning($"[RuntimeSingleton] Duplicate {typeof(T).Name} destroyed on {gameObject.name}");
                    Destroy(gameObject);
                }
            }
        }

        /// <summary>
        /// Called when this instance becomes the singleton. Override instead of Awake().
        /// </summary>
        protected virtual void OnSingletonAwake()
        {
        }

        protected virtual void OnApplicationQuit()
        {
            applicationIsQuitting = true;
        }

        protected virtual void OnDestroy()
        {
            lock (lockObject)
            {
                if (instance == this)
                {
                    instance = null;
                }
            }
        }
    }
}
```
