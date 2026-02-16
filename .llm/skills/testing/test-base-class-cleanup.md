---
title: "Test Base Class with Automatic Resource Cleanup"
id: "test-base-class-cleanup"
category: "testing"
version: "1.0.0"
created: "2026-01-21"
updated: "2026-01-21"

source:
  repository: "wallstop/unity-helpers"
  files:
    - path: "Tests/Runtime/CommonTestBase.cs"
      lines: "185-309"
  url: "https://github.com/wallstop/unity-helpers"

tags:
  - "testing"
  - "cleanup"
  - "lifecycle"
  - "fixtures"
  - "unity"
  - "nunit"

complexity:
  level: "intermediate"
  reasoning: "Requires understanding of NUnit lifecycle and Unity object management"

impact:
  performance:
    rating: "low"
    details: "Cleanup overhead is minimal and only in tests"
  maintainability:
    rating: "high"
    details: "Eliminates boilerplate cleanup code in every test"
  testability:
    rating: "high"
    details: "Ensures clean state between tests"

prerequisites:
  - "Understanding of NUnit test lifecycle"
  - "Knowledge of Unity object destruction"

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
  - "CommonTestBase"
  - "Test fixture base"
  - "Auto cleanup tests"

related:
  - "shared-test-fixtures"
  - "test-categories"
  - "test-base-class-cleanup-usage"

status: "stable"
---

# Test Base Class with Automatic Resource Cleanup

> **One-line summary**: Create an abstract test base class that automatically tracks and destroys GameObjects, Components, and other resources created during tests.

## Overview

Unity tests often create GameObjects and components that must be cleaned up to avoid test pollution. This pattern provides:

1. **Automatic tracking** via `Track<T>()` method
1. **Automatic destruction** in TearDown
1. **Scene cleanup** for integration tests
1. **Disposable tracking** for non-Unity resources

## Problem Statement

```csharp
// BAD: Manual cleanup is error-prone
[Test]
public void TestSomething()
{
    GameObject go = new GameObject("Test");
    var component = go.AddComponent<MyComponent>();

    // Test code...

    Object.DestroyImmediate(go); // Easy to forget!
}

// BAD: Cleanup in finally block is verbose
[Test]
public void TestSomething()
{
    GameObject go = null;
    try
    {
        go = new GameObject("Test");
        // Test...
    }
    finally
    {
        if (go != null) Object.DestroyImmediate(go);
    }
}
```

## Solution

### Core Concept

```text
┌─────────────────────────────────────────────────────────────────┐
│  CommonTestBase                                                  │
├─────────────────────────────────────────────────────────────────┤
│  List<Object> _trackedObjects                                   │
│  List<IDisposable> _trackedDisposables                          │
├─────────────────────────────────────────────────────────────────┤
│  Track<T>(T obj) → adds to list, returns obj                    │
│  TrackDisposable<T>(T d) → adds to list, returns d              │
├─────────────────────────────────────────────────────────────────┤
│  [TearDown] → Destroys all tracked objects                      │
│  [UnityTearDown] → Yields for async destruction                 │
└─────────────────────────────────────────────────────────────────┘
```

### Implementation

```csharp
namespace WallstopStudios.UnityHelpers.Tests
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.SceneManagement;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Base class for Unity tests with automatic resource cleanup.
    /// </summary>
    public abstract class CommonTestBase
    {
        private List<Object> _trackedObjects;
        private List<IDisposable> _trackedDisposables;
        private List<Scene> _trackedScenes;

        /// <summary>
        /// Set to true to defer asset cleanup to OneTimeTearDown.
        /// Useful when tests share expensive assets.
        /// </summary>
        protected virtual bool DeferAssetCleanupToOneTimeTearDown => false;

        [OneTimeSetUp]
        public virtual void CommonOneTimeSetUp()
        {
            _trackedObjects = new List<Object>(16);
            _trackedDisposables = new List<IDisposable>(4);
            _trackedScenes = new List<Scene>(2);
        }

        [SetUp]
        public virtual void CommonSetUp()
        {
            // Clear per-test tracking if not deferring
            if (!DeferAssetCleanupToOneTimeTearDown)
            {
                _trackedObjects.Clear();
                _trackedDisposables.Clear();
            }
        }

        /// <summary>
        /// Track a Unity Object for automatic destruction.
        /// Returns the object for fluent usage.
        /// </summary>
        protected T Track<T>(T obj) where T : Object
        {
            if (obj != null)
            {
                _trackedObjects.Add(obj);
            }
            return obj;
        }

        /// <summary>
        /// Track a disposable for automatic disposal.
        /// </summary>
        protected T TrackDisposable<T>(T disposable) where T : IDisposable
        {
            if (disposable != null)
            {
                _trackedDisposables.Add(disposable);
            }
            return disposable;
        }

        /// <summary>
        /// Create and track a GameObject.
        /// </summary>
        protected GameObject CreateGameObject(string name = "TestObject")
        {
            return Track(new GameObject(name));
        }

        /// <summary>
        /// Create and track a GameObject with components.
        /// </summary>
        protected GameObject CreateGameObject(string name, params Type[] components)
        {
            return Track(new GameObject(name, components));
        }

        /// <summary>
        /// Create and track a new test scene.
        /// </summary>
        protected Scene CreateTestScene(string name = "TestScene")
        {
            Scene scene = SceneManager.CreateScene(name);
            _trackedScenes.Add(scene);
            return scene;
        }

        [TearDown]
        public virtual void TearDown()
        {
            if (DeferAssetCleanupToOneTimeTearDown)
            {
                return;
            }

            CleanupDisposables();
        }

        [UnityTearDown]
        public virtual IEnumerator UnityTearDown()
        {
            if (DeferAssetCleanupToOneTimeTearDown)
            {
                yield break;
            }

            yield return CleanupTrackedObjects();
        }

        [OneTimeTearDown]
        public virtual IEnumerator CommonOneTimeTearDown()
        {
            if (DeferAssetCleanupToOneTimeTearDown)
            {
                CleanupDisposables();
                yield return CleanupTrackedObjects();
            }

            yield return CleanupScenes();
        }

        private void CleanupDisposables()
        {
            foreach (IDisposable disposable in _trackedDisposables)
            {
                try
                {
                    disposable?.Dispose();
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[CommonTestBase] Dispose failed: {e.Message}");
                }
            }
            _trackedDisposables.Clear();
        }

        private IEnumerator CleanupTrackedObjects()
        {
            // Destroy in reverse order (children before parents)
            for (int i = _trackedObjects.Count - 1; i >= 0; i--)
            {
                Object obj = _trackedObjects[i];
                if (obj != null)
                {
                    if (Application.isPlaying)
                    {
                        Object.Destroy(obj);
                    }
                    else
                    {
                        Object.DestroyImmediate(obj);
                    }
                }
            }
            _trackedObjects.Clear();

            // Yield to allow destruction to complete
            yield return null;
        }

        private IEnumerator CleanupScenes()
        {
            foreach (Scene scene in _trackedScenes)
            {
                if (scene.isLoaded)
                {
                    yield return SceneManager.UnloadSceneAsync(scene);
                }
            }
            _trackedScenes.Clear();
        }
    }
}
```

## Related Patterns

- [Shared Test Fixtures](./shared-test-fixtures.md) - Reference-counted fixtures
- [Test Categories](./test-categories.md) - Organizing tests
- [Test Base Class Cleanup Usage](./test-base-class-cleanup-usage.md) - Usage patterns and best practices
