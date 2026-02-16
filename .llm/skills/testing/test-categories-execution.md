---
title: "Test Category Execution"
id: "test-categories-execution"
category: "testing"
version: "1.0.0"
created: "2026-01-22"
updated: "2026-01-22"

source:
  repository: "wallstop/unity-helpers"
  files:
    - path: "Tests/Runtime/"
  url: "https://github.com/wallstop/unity-helpers"

tags:
  - "testing"
  - "organization"
  - "categories"
  - "ci"

complexity:
  level: "basic"
  reasoning: "Focuses on executing categories in runners and CI"

impact:
  performance:
    rating: "none"
    details: "Execution guidance only"
  maintainability:
    rating: "high"
    details: "Helps keep test suites organized"
  testability:
    rating: "high"
    details: "Ensures correct selection of tests"

prerequisites:
  - "Understanding of NUnit categories"

dependencies:
  packages: []
  skills:
    - "test-categories"

applies_to:
  languages:
    - "C#"
  frameworks:
    - "Unity"
    - ".NET"
    - "NUnit"

aliases:
  - "Test category execution"

related:
  - "test-categories"

status: "stable"
---

# Test Category Execution

> **One-line summary**: How to run test categories locally, in the Unity Test Runner, and in CI.

## Overview

This skill shows how to execute tests by category in different runners.

## Solution

Use the commands below to run targeted test subsets.

## Usage

### Unity Test Runner

In Unity's Test Runner window:

1. Click the filter dropdown
1. Select "Category"
1. Choose categories to include/exclude

### Command Line

```bash
# Run only fast tests
Unity -batchmode -runTests -testFilter "cat==Fast"

# Exclude slow tests
Unity -batchmode -runTests -testFilter "cat!=Slow"

# Run integration tests only
Unity -batchmode -runTests -testFilter "cat==Integration"

# Combine filters
Unity -batchmode -runTests -testFilter "cat==Fast && cat!=Flaky"
```

### CI/CD Pipeline

```yaml
# .github/workflows/test.yml
jobs:
  fast-tests:
    name: Fast Tests
    runs-on: ubuntu-latest
    steps:
      - name: Run fast tests
        run: |
          Unity -batchmode -nographics -runTests \
            -testFilter "cat==Fast" \
            -testResults fast-results.xml

  integration-tests:
    name: Integration Tests
    runs-on: ubuntu-latest
    needs: fast-tests # Only run if fast tests pass
    steps:
      - name: Run integration tests
        run: |
          Unity -batchmode -nographics -runTests \
            -testFilter "cat==Integration" \
            -testResults integration-results.xml
```

### Local Development Workflow

```csharp
// Create a menu item for quick fast test runs
#if UNITY_EDITOR
public static class TestMenus
{
    [MenuItem("Tests/Run Fast Tests")]
    public static void RunFastTests()
    {
        var filter = new Filter { categoryNames = new[] { "Fast" } };
        var testRunnerApi = ScriptableObject.CreateInstance<TestRunnerApi>();
        testRunnerApi.Execute(new ExecutionSettings(filter));
    }
}
#endif
```
