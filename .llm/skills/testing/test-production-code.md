---
title: "Test Production Code Directly"
id: "test-production-code"
category: "testing"
version: "1.0.0"
created: "2026-01-30"
updated: "2026-01-30"

source:
  repository: "wallstop/DxMessaging"
  files:
    - path: "scripts/__tests__/"
    - path: "scripts/"
  url: "https://github.com/wallstop/DxMessaging"

tags:
  - "testing"
  - "anti-patterns"
  - "code-quality"
  - "javascript"
  - "maintainability"
  - "testability"
  - "best-practices"

complexity:
  level: "intermediate"
  reasoning: "Requires understanding of test design principles and code architecture"

impact:
  performance:
    rating: "none"
    details: "Testing patterns only; no runtime performance impact"
  maintainability:
    rating: "critical"
    details: "Prevents tests from diverging from production behavior"
  testability:
    rating: "critical"
    details: "Ensures tests actually verify production code correctness"

prerequisites:
  - "Understanding of module exports and imports"
  - "Familiarity with test isolation principles"

dependencies:
  packages: []
  skills:
    - "comprehensive-test-coverage"
    - "script-test-coverage"

applies_to:
  languages:
    - "JavaScript"
    - "TypeScript"
    - "C#"
  frameworks:
    - "Jest"
    - "Node.js"
    - "NUnit"
  versions:
    node: ">=18.0"
    jest: ">=29.0"

aliases:
  - "Test real code"
  - "Don't re-implement in tests"
  - "Test production directly"
  - "Avoid test duplication"

related:
  - "comprehensive-test-coverage"
  - "script-test-coverage"
  - "test-code-quality"

status: "stable"
---

# Test Production Code Directly

> **One-line summary**: Tests must import and use production code, never re-implement production logic locally.

## Overview

A common anti-pattern in testing is re-implementing production validation logic inside test files. When tests maintain their own copies of validation rules, they can pass even when production code regresses. This skill documents how to structure code for testability and avoid this dangerous pattern.

## Problem Statement

### The Anti-Pattern

Tests that re-implement production logic create a false sense of security:

```javascript
// PRODUCTION: scripts/validate-data.js
function validateRecord(record) {
  const errors = [];
  if (!record.name || record.name.length < 3) {
    errors.push("Name must be at least 3 characters");
  }
  if (!record.email || !record.email.includes("@")) {
    errors.push("Email must be valid");
  }
  return errors;
}

// TEST: scripts/__tests__/validate-data.test.js
// WRONG: Re-implementing validation locally
function localValidateRecord(record) {
  const errors = [];
  if (!record.name || record.name.length < 3) {
    // Duplicated logic!
    errors.push("Name must be at least 3 characters");
  }
  if (!record.email || !record.email.includes("@")) {
    // Duplicated logic!
    errors.push("Email must be valid");
  }
  return errors;
}

test("should validate record correctly", () => {
  const result = localValidateRecord({ name: "Jo", email: "bad" });
  expect(result).toHaveLength(2); // Tests pass, but production untested!
});
```

### Why This Is Dangerous

1. **Production regressions go undetected**: If someone changes `< 3` to `< 2` in production, tests still pass because they use the local copy.
1. **Maintenance burden doubles**: Every production change requires updating test copies.
1. **Divergence over time**: Local test copies gradually drift from production reality.
1. **False confidence**: 100% test coverage means nothing if tests don't exercise production code.
1. **Bug duplication**: If the same bug exists in both copies, tests won't catch it.

## Solution

### Structure Production Code for Testability

Export validation functions and helper logic so tests can import them:

```javascript
// PRODUCTION: scripts/validate-data.js
/**
 * Validates a record and returns any errors.
 * @param {Object} record - The record to validate
 * @returns {string[]} Array of error messages
 */
function validateRecord(record) {
  const errors = [];
  if (!isValidName(record.name)) {
    errors.push("Name must be at least 3 characters");
  }
  if (!isValidEmail(record.email)) {
    errors.push("Email must be valid");
  }
  return errors;
}

/**
 * Validates name length.
 * @param {string} name - The name to validate
 * @returns {boolean} True if valid
 */
function isValidName(name) {
  return name && name.length >= 3;
}

/**
 * Validates email format.
 * @param {string} email - The email to validate
 * @returns {boolean} True if valid
 */
function isValidEmail(email) {
  return email && email.includes("@");
}

// Export everything tests need
module.exports = {
  validateRecord,
  isValidName,
  isValidEmail
};
```

### Test Production Code Directly

Import and test the actual production functions:

```javascript
// TEST: scripts/__tests__/validate-data.test.js
// CORRECT: Import production code
const { validateRecord, isValidName, isValidEmail } = require("../validate-data");

describe("validateRecord", () => {
  test("should return no errors for valid record", () => {
    const result = validateRecord({ name: "John", email: "john@example.com" });
    expect(result).toHaveLength(0);
  });

  test("should return error for short name", () => {
    const result = validateRecord({ name: "Jo", email: "john@example.com" });
    expect(result).toContain("Name must be at least 3 characters");
  });

  test("should return error for invalid email", () => {
    const result = validateRecord({ name: "John", email: "invalid" });
    expect(result).toContain("Email must be valid");
  });
});

describe("isValidName", () => {
  test("should accept names with 3 or more characters", () => {
    expect(isValidName("Joe")).toBe(true);
    expect(isValidName("John")).toBe(true);
  });

  test("should reject names with fewer than 3 characters", () => {
    expect(isValidName("Jo")).toBe(false);
    expect(isValidName("J")).toBe(false);
  });

  test("should reject null and undefined", () => {
    expect(isValidName(null)).toBe(false);
    expect(isValidName(undefined)).toBe(false);
  });
});
```

## When Local Test Helpers Are Acceptable

Not all code in test files is duplication. These are legitimate uses:

### Thin Wrappers for Test Convenience

```javascript
// ACCEPTABLE: Thin wrapper that delegates to production
function validateAndExpectErrors(record, expectedCount) {
  const result = validateRecord(record); // Calls production!
  expect(result).toHaveLength(expectedCount);
  return result;
}
```

### Test-Only Utilities

```javascript
// ACCEPTABLE: Test data generators
function createValidRecord(overrides = {}) {
  return {
    name: "Default Name",
    email: "default@example.com",
    ...overrides
  };
}

// ACCEPTABLE: Custom matchers
function expectValidationError(result, expectedMessage) {
  expect(result.some((msg) => msg.includes(expectedMessage))).toBe(true);
}

// ACCEPTABLE: Factories for test data
const TestRecords = {
  valid: { name: "John Doe", email: "john@example.com" },
  invalidName: { name: "X", email: "john@example.com" }
};
```

## Handling Unavoidable Duplication

Some situations require maintaining parallel implementations. Use SYNC notes to keep them aligned.

### PowerShell Scripts with JavaScript Tests

When the production script is PowerShell but tests are JavaScript:

```powershell
# PRODUCTION: scripts/validate-data.ps1
# SYNC: Keep validation logic in sync with validate-data.test.js validateRecord()
function Test-Record {
    param([hashtable]$Record)
    $errors = @()
    if (-not $Record.name -or $Record.name.Length -lt 3) {
        $errors += "Name must be at least 3 characters"
    }
    return $errors
}
```

```javascript
// TEST: scripts/__tests__/validate-data.test.js
// SYNC: Keep validation logic in sync with validate-data.ps1 Test-Record
function validateRecord(record) {
  const errors = [];
  if (!record.name || record.name.length < 3) {
    errors += "Name must be at least 3 characters";
  }
  return errors;
}
```

### Browser-Only Code

When code runs only in browsers and cannot be tested in Node.js:

```javascript
// PRODUCTION: browser-only.js (uses DOM APIs)
// SYNC: Core logic duplicated for testing in browser-only.test.js

// TEST: browser-only.test.js
// SYNC: Keep validation logic in sync with browser-only.js computeLayout()
// This is a test-only implementation because production uses DOM APIs
```

## SYNC Note Best Practices

When duplication is unavoidable, follow these SYNC note guidelines:

1. **Always bidirectional**: Both files must reference each other
1. **Never use line numbers**: Reference function names, not line numbers that change
1. **Use descriptive identifiers**: `SYNC: Keep in sync with validate.js isValidEmail()` not `SYNC: Keep in sync with validate.js line 42`
1. **Verify references exist**: Confirm the referenced function exists before adding the note
1. **Update both on changes**: When modifying synced code, update both locations

## Red Flags to Watch For

These patterns suggest tests may not be exercising production code:

| Red Flag                                               | Why It's Suspicious           |
| ------------------------------------------------------ | ----------------------------- |
| Test file defines validation constants                 | Should import from production |
| Test file has utility functions that mirror production | Should import instead         |
| Test never imports the module it's supposedly testing  | Tests itself, not production  |
| Test file size rivals production file size             | Too much duplicated logic     |
| Same bug exists in production and tests                | Copied code with copied bugs  |

## Verification Checklist

Before merging, verify tests exercise production code:

1. **Check imports**: Does the test file import from the production module?
1. **Check coverage**: Does production file show coverage when tests run?
1. **Mutation test**: Change production code slightly - do tests fail?
1. **Review test functions**: Are they calling production code or local copies?
1. **Search for duplication**: Do test and production files have similar function bodies?

## See Also

- [Comprehensive Test Coverage skill](comprehensive-test-coverage.md) - What to test
- [Script Test Coverage skill](script-test-coverage.md) - Testing scripts specifically
- [Test Code Quality skill](test-code-quality.md) - Test documentation accuracy
