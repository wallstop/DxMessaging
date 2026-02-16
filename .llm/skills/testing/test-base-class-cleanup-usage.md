---
title: "Test Base Class Cleanup Usage"
id: "test-base-class-cleanup-usage"
category: "testing"
version: "1.0.0"
created: "2026-01-22"
updated: "2026-01-22"

source:
  repository: "wallstop/unity-helpers"
  files:
    - path: "Tests/Runtime/CommonTestBase.cs"
  url: "https://github.com/wallstop/unity-helpers"

tags:
  - "testing"
  - "cleanup"
  - "lifecycle"
  - "unity"

complexity:
  level: "intermediate"
  reasoning: "Usage patterns depend on Unity fixture lifecycles"

impact:
  performance:
    rating: "low"
    details: "Cleanup adds minor overhead but prevents leaks"
  maintainability:
    rating: "high"
    details: "Consistent teardown reduces flaky tests"
  testability:
    rating: "high"
    details: "Encourages isolated tests"

prerequisites:
  - "Understanding of NUnit fixture lifecycle"

dependencies:
  packages: []
  skills:
    - "test-base-class-cleanup"

applies_to:
  languages:
    - "C#"
  frameworks:
    - "Unity"
    - ".NET"
  versions:
    unity: ">=2021.3"

aliases:
  - "Common test base usage"

related:
  - "test-base-class-cleanup"

status: "stable"
---

# Test Base Class Cleanup Usage

> **One-line summary**: Usage patterns, performance notes, and best practices for automatic test cleanup.

## Overview

This skill covers how to apply cleanup tracking in test fixtures.

## Solution

Use the usage patterns below to keep tests isolated.

## Usage

### Basic Test Class

```csharp
[TestFixture]
public sealed class PlayerTests : CommonTestBase
{
    [Test]
    public void PlayerTakesDamage()
    {
        GameObject go = CreateGameObject("Player");
        Player player = go.AddComponent<Player>();

        player.TakeDamage(10);

        Assert.AreEqual(90, player.Health);
    } // go automatically destroyed after test
}
```

### Fluent Tracking

```csharp
[Test]
public void EnemySpawnsCorrectly()
{
    // Track returns the object for chaining
    Enemy enemy = Track(Object.Instantiate(enemyPrefab)).GetComponent<Enemy>();

    Assert.IsNotNull(enemy);
    Assert.AreEqual(100, enemy.Health);
}
```

### Tracking Multiple Objects

```csharp
[Test]
public void BulletHitsEnemy()
{
    GameObject playerGo = CreateGameObject("Player", typeof(Rigidbody));
    GameObject enemyGo = CreateGameObject("Enemy", typeof(Rigidbody), typeof(Collider));
    GameObject bulletGo = CreateGameObject("Bullet", typeof(Rigidbody));

    // All three destroyed after test
    // ...
}
```

### Tracking Disposables

```csharp
[Test]
public void CacheEvictsOldEntries()
{
    var cache = TrackDisposable(new CacheBuilder<string, int>()
        .WithMaximumSize(10)
        .Build());

    for (int i = 0; i < 20; i++)
    {
        cache.Put($"key{i}", i);
    }

    Assert.AreEqual(10, cache.Count);
} // cache.Dispose() called automatically
```

### Shared Fixtures (Deferred Cleanup)

```csharp
[TestFixture]
public sealed class ExpensiveAssetTests : CommonTestBase
{
    protected override bool DeferAssetCleanupToOneTimeTearDown => true;

    private Texture2D sharedTexture;

    [OneTimeSetUp]
    public override void CommonOneTimeSetUp()
    {
        base.CommonOneTimeSetUp();
        sharedTexture = Track(new Texture2D(1024, 1024));
        // Expensive setup done once
    }

    [Test]
    public void Test1()
    {
        // Uses sharedTexture
    }

    [Test]
    public void Test2()
    {
        // Also uses sharedTexture
    }

    // sharedTexture destroyed in OneTimeTearDown
}
```

### Scene Tests

```csharp
[TestFixture]
public sealed class SceneLoadingTests : CommonTestBase
{
    [UnityTest]
    public IEnumerator SceneLoadsCorrectly()
    {
        Scene testScene = CreateTestScene("TestScene");
        SceneManager.SetActiveScene(testScene);

        GameObject go = CreateGameObject("TestObject");
        SceneManager.MoveGameObjectToScene(go, testScene);

        Assert.IsTrue(testScene.isLoaded);
        yield return null;
    } // testScene unloaded after test
}
```

## Performance Notes

- **Overhead**: ~1us per tracked object
- **Memory**: List allocation in OneTimeSetUp only
- **Destruction Order**: Reverse order prevents orphan issues

## Best Practices

### Do

- Always use `CreateGameObject` or `Track` for test objects
- Use `TrackDisposable` for caches, streams, etc.
- Set `DeferAssetCleanupToOneTimeTearDown = true` for expensive shared fixtures
- Destroy in reverse order (handled automatically)

### Don't

- Don't manually destroy tracked objects (double-destroy error)
- Don't forget to call base methods when overriding SetUp/TearDown
- Don't create objects in [OneTimeSetUp] without tracking
- Don't skip tracking "temporary" objects (they leak)
