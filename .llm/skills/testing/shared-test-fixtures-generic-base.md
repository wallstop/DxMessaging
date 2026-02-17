---
title: "Shared Fixtures: Generic Base"
id: "shared-test-fixtures-generic-base"
category: "testing"
version: "1.0.0"
created: "2026-01-21"
updated: "2026-01-21"

source:
  repository: "wallstop/unity-helpers"
  files:
    - path: "Tests/Runtime/SharedTextureTestFixtures.cs"
  url: "https://github.com/wallstop/unity-helpers"

tags:
  - "testing"
  - "fixtures"
  - "generic"
  - "reference-counting"

complexity:
  level: "advanced"
  reasoning: "Uses static lifecycle hooks with a generic base pattern"

impact:
  performance:
    rating: "high"
    details: "Avoids repeated fixture construction across tests"
  maintainability:
    rating: "medium"
    details: "Provides a reusable base for multiple fixture types"
  testability:
    rating: "medium"
    details: "Shared state must be controlled carefully"

prerequisites:
  - "Understanding of NUnit fixture lifecycle"

dependencies:
  packages: []
  skills:
    - "shared-test-fixtures"

applies_to:
  languages:
    - "C#"
  frameworks:
    - "Unity"
  versions:
    unity: ">=2021.3"

aliases:
  - "Generic shared fixtures"

related:
  - "shared-test-fixtures"
  - "shared-test-fixtures-reference-counting"

status: "stable"
---

# Shared Fixtures: Generic Base

> **One-line summary**: Use a generic base class to standardize shared fixture creation and teardown.

## Overview

When multiple shared fixtures exist, a generic base class avoids repeating reference counting logic.

## Problem Statement

Duplicated reference counting logic across fixtures increases the risk of bugs and inconsistent behavior.

## Solution

### Implementation

```csharp
namespace WallstopStudios.UnityHelpers.Tests
{
    using UnityEngine.SceneManagement;

    public abstract class SharedFixtures<T> where T : SharedFixtures<T>, new()
    {
        private static readonly object syncLock = new object();
        private static int refCount;
        private static T instance;

        public static T Instance
        {
            get
            {
                lock (syncLock)
                {
                    if (instance == null)
                    {
                        UnityEngine.Debug.LogWarning($"[{typeof(T).Name}] Accessing without Acquire()");
                    }
                    return instance;
                }
            }
        }

        public static void Acquire()
        {
            lock (syncLock)
            {
                if (refCount == 0)
                {
                    instance = new T();
                    instance.Create();
                }
                refCount++;
            }
        }

        public static void Release()
        {
            lock (syncLock)
            {
                refCount--;
                if (refCount == 0)
                {
                    instance.Destroy();
                    instance = default;
                }
            }
        }

        protected abstract void Create();
        protected abstract void Destroy();
    }

    public sealed class SharedSceneFixtures : SharedFixtures<SharedSceneFixtures>
    {
        public Scene TestScene { get; private set; }

        protected override void Create()
        {
            TestScene = SceneManager.CreateScene("SharedTestScene");
        }

        protected override void Destroy()
        {
            if (TestScene.isLoaded)
            {
                SceneManager.UnloadSceneAsync(TestScene);
            }
        }
    }
}
```

## Usage Example

```csharp
[TestFixture]
public sealed class SceneTests : CommonTestBase
{
    [OneTimeSetUp]
    public override void CommonOneTimeSetUp()
    {
        base.CommonOneTimeSetUp();
        SharedSceneFixtures.Acquire();
    }

    [OneTimeTearDown]
    public override IEnumerator CommonOneTimeTearDown()
    {
        SharedSceneFixtures.Release();
        yield return base.CommonOneTimeTearDown();
    }
}
```

## See Also

- [Shared Test Fixtures](shared-test-fixtures.md)
- [Shared Fixtures: Reference Counting](shared-test-fixtures-reference-counting.md)

## Changelog

| Version | Date       | Changes         |
| ------- | ---------- | --------------- |
| 1.0.0   | 2026-01-21 | Initial version |
