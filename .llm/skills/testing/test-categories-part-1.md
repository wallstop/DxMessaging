---
title: "Test Categories for Selective Execution Part 1"
id: "test-categories-part-1"
category: "testing"
version: "1.0.0"
created: "2026-01-21"
updated: "2026-03-16"
status: "stable"
tags:
  - migration
  - split
complexity:
  level: "intermediate"
impact:
  performance:
    rating: "low"
---

## Overview

Continuation extracted from `test-categories.md` to keep files within the repository line-budget policy.

## Solution

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

## See Also

- [Test Categories for Selective Execution](./test-categories.md)
