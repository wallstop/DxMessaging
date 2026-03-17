---
title: "JavaScript Code Quality Practices Part 1"
id: "javascript-code-quality-part-1"
category: "scripting"
version: "1.0.0"
created: "2026-01-30"
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

Continuation material extracted from `javascript-code-quality.md` to keep .llm files within the 300-line budget.

## Solution

## JSDoc Accuracy

### Problem

JSDoc comments that don't match implementation create dangerous misunderstandings. Tests written
against inaccurate JSDoc verify incorrect behavior.

### Anti-Pattern: Misleading Function Description

```javascript
// BAD: JSDoc says "excludes arrays" but implementation accepts them
/**
 * Validates that value is a non-null object (excludes arrays).
 * @param {*} value - Value to check
 * @returns {boolean} True if value is a non-null object
 */
function isValidObject(value) {
  // Implementation actually accepts arrays!
  return value !== null && typeof value === "object";
}
```

### Correct Pattern: Documentation Matches Implementation

```javascript
// GOOD: JSDoc accurately describes behavior
/**
 * Validates that value is a non-null object (arrays are objects in JavaScript).
 * @param {*} value - Value to check
 * @returns {boolean} True if value is non-null and typeof is 'object'
 */
function isValidObject(value) {
  return value !== null && typeof value === "object";
}

// OR: Update implementation to match documented intent
/**
 * Validates that value is a non-null, non-array object.
 * @param {*} value - Value to check
 * @returns {boolean} True if value is a non-null, non-array object
 */
function isValidObject(value) {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}
```

### Module Export Documentation

When using `@exports` tags to document module exports, keep them synchronized with actual exports:

```javascript
// BAD: @exports lists wrong values or missing exports
/**
 * @exports {Array<string>} VALID_LEVELS - Valid values: 'low', 'high'
 */
module.exports = {
  VALID_LEVELS, // Actual values: ['basic', 'intermediate', 'advanced']
  helperFunction // Not documented!
};

// GOOD: @exports matches actual export values
/**
 * @exports {Array<string>} VALID_LEVELS - Valid values: 'basic', 'intermediate', 'advanced'
 * @exports {Function} helperFunction - Validates input and returns errors
 */
module.exports = {
  VALID_LEVELS,
  helperFunction
};
```

### Verification Checklist

1. **Read the implementation** - Don't just copy JSDoc from similar functions
1. **Test edge cases mentioned in JSDoc** - If it says "excludes arrays", test with arrays
1. **Update JSDoc when changing implementation** - Don't leave stale documentation
1. **Avoid vague descriptions** - "Validates value" doesn't tell readers what validation occurs

## Test Documentation Accuracy

### Problem

Test file headers, `describe` block names, and comments must accurately describe what the tests
actually verify. Inaccurate documentation causes confusion and hides the test's true purpose.

### Anti-Pattern: Incorrect Value Categorization

```javascript
// BAD: Header says "falsy/undefined" but tests actually check "undefined/null"
/**
 * @fileoverview Tests for frontmatter validation - falsy and undefined values.
 */

describe("Falsy/Undefined Value Handling", () => {
  // But the tests below actually check undefined/null (missing values),
  // not other falsy values like empty string, 0, or false
  test("should warn when value is undefined", () => {
    validate({ field: undefined });
  });
  test("should warn when value is null", () => {
    validate({ field: null });
  });
});
```

### Correct Pattern: Precise Documentation

```javascript
// GOOD: Documentation accurately describes what is tested
/**
 * @fileoverview Tests for frontmatter validation - undefined and null values.
 */

describe("Undefined/Null Value Handling", () => {
  test("should warn when value is undefined", () => {
    validate({ field: undefined });
  });
  test("should warn when value is null", () => {
    validate({ field: null });
  });
});

// Separate describe block for falsy wrong-type values
describe("Wrong Type Value Handling", () => {
  test("should warn when value is empty string (wrong type)", () => {
    validate({ field: "" });
  });
  test("should warn when value is zero (wrong type)", () => {
    validate({ field: 0 });
  });
  test("should warn when value is false (wrong type)", () => {
    validate({ field: false });
  });
});
```

### JavaScript Truthiness Reference

When documenting tests, use accurate terminology:

| Value       | Truthy/Falsy | Correct Term for Missing Check |
| ----------- | ------------ | ------------------------------ |
| `undefined` | Falsy        | "missing" or "undefined"       |
| `null`      | Falsy        | "missing" or "null"            |
| `""`        | Falsy        | "wrong type" (not "missing")   |
| `0`         | Falsy        | "wrong type" (not "missing")   |
| `false`     | Falsy        | "wrong type" (not "missing")   |
| `[]`        | **Truthy**   | "empty array" (not falsy!)     |
| `{}`        | **Truthy**   | "empty object" (not falsy!)    |

## See Also

- [JavaScript Code Quality Practices](./javascript-code-quality.md)
