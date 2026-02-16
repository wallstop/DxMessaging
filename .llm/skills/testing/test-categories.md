---
title: "Test Categories for Selective Execution"
id: "test-categories"
category: "testing"
version: "1.0.0"
created: "2026-01-21"
updated: "2026-01-21"

source:
  repository: "wallstop/unity-helpers"
  files:
    - path: "Tests/Runtime/SpriteSettingsTests.cs"
    - path: "Tests/Runtime/ParabolaTests.cs"
  url: "https://github.com/wallstop/unity-helpers"

tags:
  - "testing"
  - "organization"
  - "categories"
  - "nunit"
  - "ci"

complexity:
  level: "basic"
  reasoning: "Simple NUnit attribute usage"

impact:
  performance:
    rating: "high"
    details: "Enables fast feedback by running only fast tests locally"
  maintainability:
    rating: "high"
    details: "Clear test organization"
  testability:
    rating: "high"
    details: "Selective test runs improve workflow"

prerequisites:
  - "Understanding of NUnit attributes"

dependencies:
  packages: []
  skills: []

applies_to:
  languages:
    - "C#"
  frameworks:
    - "Unity"
    - ".NET"
  versions:
    unity: ">=2021.3"

aliases:
  - "NUnit categories"
  - "Test filtering"
  - "Test organization"

related:
  - "test-base-class-cleanup"
  - "data-driven-tests"
  - "test-categories-execution"

status: "stable"
---

# Test Categories for Selective Execution

> **One-line summary**: Use NUnit `[Category]` attributes to organize tests by speed, type, and purpose, enabling selective test runs for faster development feedback.

## Overview

Not all tests should run all the time:

- **Fast unit tests** (~100ms): Run constantly during development
- **Slow integration tests** (~5s): Run before commits
- **Flaky tests**: Quarantine and fix separately

This pattern uses NUnit's `[Category]` attribute to enable selective execution.

## Problem Statement

```csharp
// BAD: All tests look the same
[Test]
public void QuickCalculation() { } // 1ms

[Test]
public void LoadAssetBundle() { } // 3000ms

[Test]
public void NetworkCall() { } // Sometimes fails

// Running all tests takes forever
// Developer skips testing because it's slow
// Flaky test breaks CI randomly
```

## Solution

### Core Categories

```csharp
// Category definitions (can be constants or a static class)
public static class TestCategories
{
    public const string Fast = "Fast";           // <100ms, no I/O
    public const string Slow = "Slow";           // >100ms or I/O
    public const string Integration = "Integration"; // External dependencies
    public const string Flaky = "Flaky";         // Known intermittent failures
    public const string EditorOnly = "EditorOnly"; // Requires Editor mode
    public const string PlayMode = "PlayMode";   // Requires Play mode
}
```

### Implementation

```csharp
namespace WallstopStudios.UnityHelpers.Tests
{
    using NUnit.Framework;

    // Fast unit tests - pure logic, no Unity APIs
    [TestFixture]
    [Category(TestCategories.Fast)]
    public sealed class MathUtilsTests
    {
        [Test]
        public void ClampReturnsValueInRange()
        {
            Assert.AreEqual(5, MathUtils.Clamp(5, 0, 10));
        }

        [Test]
        public void ClampReturnsMinWhenBelowRange()
        {
            Assert.AreEqual(0, MathUtils.Clamp(-5, 0, 10));
        }
    }

    // Slow tests - creates Unity objects
    [TestFixture]
    [Category(TestCategories.Slow)]
    public sealed class TextureProcessingTests : CommonTestBase
    {
        [Test]
        public void ProcessLargeTexture()
        {
            Texture2D texture = CreateTexture(2048, 2048);
            Track(texture);

            processor.Process(texture);

            Assert.IsTrue(processor.IsComplete);
        }
    }

    // Integration tests - requires external resources
    [TestFixture]
    [Category(TestCategories.Slow)]
    [Category(TestCategories.Integration)]
    public sealed class AssetBundleTests : CommonTestBase
    {
        [Test]
        public void LoadBundleFromDisk()
        {
            // Requires asset bundle on disk
            AssetBundle bundle = AssetBundle.LoadFromFile(bundlePath);
            Track(bundle);

            Assert.IsNotNull(bundle);
        }
    }

    // Mixed fixture - some fast, some slow
    [TestFixture]
    public sealed class CacheTests
    {
        [Test]
        [Category(TestCategories.Fast)]
        public void GetReturnsStoredValue()
        {
            // Pure logic test
        }

        [Test]
        [Category(TestCategories.Slow)]
        public void CacheEvictsUnderMemoryPressure()
        {
            // Allocates lots of memory, takes time
        }
    }

    // Flaky tests - quarantine until fixed
    [TestFixture]
    [Category(TestCategories.Flaky)]
    [Explicit("Network timing issues - see issue #123")]
    public sealed class NetworkTests
    {
        [Test]
        public void ServerRespondsWithinTimeout()
        {
            // Sometimes fails due to network latency
        }
    }
}
```

### Per-Test Category Override

```csharp
[TestFixture]
[Category(TestCategories.Fast)] // Default for fixture
public sealed class StringExtensionTests
{
    [Test]
    public void TrimRemovesWhitespace()
    {
        // Fast test
    }

    [Test]
    [Category(TestCategories.Slow)] // Override for this test
    public void FormatLargeString()
    {
        // Slow test in otherwise fast fixture
    }
}
```

## Performance Notes

### Recommended Time Budgets

| Category    | Max Time | Description                 |
| ----------- | -------- | --------------------------- |
| Fast        | <100ms   | No I/O, no Unity lifecycle  |
| Slow        | <5s      | Unity objects, moderate I/O |
| Integration | <30s     | External resources, network |

### Execution Strategy

| Context              | Categories to Run   |
| -------------------- | ------------------- |
| On save (IDE plugin) | Fast only           |
| Pre-commit hook      | Fast                |
| PR checks            | Fast + Slow         |
| Nightly build        | All including Flaky |

## Best Practices

### Do

- Mark all tests with at least one category
- Use `[Explicit]` with reason for quarantined tests
- Run Fast tests constantly during development
- Keep Fast tests under 100ms total per file
- Document why tests are categorized as Slow/Flaky

### Don't

- Don't leave tests uncategorized (defaults vary)
- Don't mark slow tests as Fast (hurts feedback loop)
- Don't leave Flaky tests in main categories (breaks CI)
- Don't use too many categories (hard to manage)

### Category Hygiene

```csharp
// Good: Clear reason for Slow category
[Test]
[Category(TestCategories.Slow)]
// This test creates 1000 GameObjects
public void StressTestSpawning() { }

// Good: Explicit with reason for quarantine
[Test]
[Category(TestCategories.Flaky)]
[Explicit("Race condition on first frame - issue #456")]
public void InitializationOrder() { }
```

## Related Patterns

- [Test Base Class Cleanup](./test-base-class-cleanup.md) - Test infrastructure
- [Data-Driven Tests](./data-driven-tests.md) - Parameterized testing
- [Test Category Execution](./test-categories-execution.md) - Running categories
