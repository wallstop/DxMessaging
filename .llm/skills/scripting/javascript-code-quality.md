---
title: "JavaScript Code Quality Practices"
id: "javascript-code-quality"
category: "scripting"
version: "1.0.0"
created: "2026-01-30"
updated: "2026-01-30"

source:
  repository: "wallstop/DxMessaging"
  files:
    - path: "scripts/"
    - path: "scripts/__tests__/"
  url: "https://github.com/wallstop/DxMessaging"

tags:
  - "javascript"
  - "code-quality"
  - "linting"
  - "testing"
  - "jest"
  - "eslint"
  - "documentation"
  - "sync-notes"

complexity:
  level: "intermediate"
  reasoning: "Requires understanding of linting tools, test organization, and code documentation"

impact:
  performance:
    rating: "none"
    details: "Code quality patterns only; no runtime performance impact"
  maintainability:
    rating: "high"
    details: "Proper code organization and documentation prevents confusion and bugs"
  testability:
    rating: "high"
    details: "Well-organized tests are easier to maintain and extend"

prerequisites:
  - "JavaScript fundamentals"
  - "Jest testing framework familiarity"
  - "Understanding of linting concepts"

dependencies:
  packages:
    - "jest"
  skills:
    - "script-test-coverage"
    - "cross-platform-compatibility"

applies_to:
  languages:
    - "JavaScript"
  frameworks:
    - "Jest"
    - "Node.js"
  versions:
    node: ">=18.0"
    jest: ">=29.0"

aliases:
  - "JS code quality"
  - "JavaScript best practices"
  - "Test organization"

related:
  - "script-test-coverage"
  - "cross-platform-compatibility"
  - "comprehensive-test-coverage"
  - "validation-patterns"

status: "stable"
---

# JavaScript Code Quality Practices

> **One-line summary**: Ensure JavaScript code has accurate documentation, properly placed
> directives, correctly categorized tests, and bidirectional synchronization notes.

## Overview

JavaScript code quality extends beyond just "working code." This skill covers common pitfalls in
documentation accuracy, linter directive placement, test organization, and synchronization between
related code. Following these practices prevents confusion, reduces bugs, and improves codebase
maintainability.

## Solution

1. **Keep JSDoc accurate** - function descriptions, parameter types, and return values must match
   implementation
1. **Keep module exports documented** - `@exports` tags must list actual exports with correct types
   and values
1. **Place linter directives immediately before suppressed code** - `eslint-disable-next-line`
   only affects the next line
1. **Only use linter directives if the linter is configured** - don't add ESLint comments to
   projects that don't use ESLint
1. **Use accurate `describe` block names** - test categories must reflect actual test content
1. **Match user-facing messages to actual UI terminology** - column names, labels, and terms must
   be consistent
1. **Add bidirectional SYNC notes** - when code A references code B, code B must reference code A
1. **Test user-facing message content** - verify messages contain correct terminology

## Linter Directive Placement

### Problem

Linter disable directives (ESLint, JSHint, etc.) must be placed immediately before the line they
suppress. Placing them earlier in the file has no effect and creates confusion.

### Anti-Pattern

```javascript
// BAD: Directive is too far from the code it's meant to suppress
// eslint-disable-next-line no-unused-vars
const SOME_CONSTANT = "value";

// Many lines of other code here...

function unusedFunction() {
  // This function is NOT suppressed - the directive was consumed by SOME_CONSTANT
}
```

### Correct Pattern

```javascript
// GOOD: Directive immediately precedes the suppressed code
const SOME_CONSTANT = "value";

// Many lines of other code here...

// eslint-disable-next-line no-unused-vars
function unusedFunction() {
  // This function IS suppressed
}
```

### When to Avoid Directives Entirely

Before adding a linter directive, verify:

1. **Is the linter actually configured?** Adding ESLint directives to a project that doesn't use
   ESLint is misleading and creates maintenance burden.
1. **Can the underlying issue be fixed?** Unused variables should usually be removed, not
   suppressed.
1. **Is the suppression documented?** If you must suppress, add a comment explaining why.

```javascript
// GOOD: If ESLint is not used in the project, don't add ESLint directives
// Just remove or use the code properly instead of suppressing warnings
```

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

## User-Facing Message Consistency

### Problem

Warning messages, error messages, and other user-facing text must match the actual UI, column
names, or output they reference. Inconsistent naming causes confusion.

### Anti-Pattern

```javascript
// BAD: Message says "Difficulty column" but the actual table header is "Complexity"
console.warn(`Warning: ${file} has missing Difficulty column value`);

// The actual table in the output/UI:
// | Title | Complexity | Performance |
// |-------|------------|-------------|
```

### Correct Pattern

```javascript
// GOOD: Message matches actual column name
console.warn(`Warning: ${file} has missing Complexity column value`);

// Matches the actual table:
// | Title | Complexity | Performance |
// |-------|------------|-------------|
```

### Test Coverage for Messages

When code produces user-facing messages, tests should verify the message content:

```javascript
// GOOD: Test verifies message matches actual column names
describe("Warning Messages", () => {
  test("should use correct column name in warning", () => {
    const result = validate(invalidData);

    // Verify the warning uses the actual column name from the UI
    expect(result.warnings[0]).toContain("Complexity");
    expect(result.warnings[0]).not.toContain("Difficulty"); // Old incorrect name
  });
});
```

## Bidirectional SYNC Notes

### Problem

SYNC notes that only exist in one location are easily missed. When code A references code B, code B
should also reference code A.

### Anti-Pattern: One-Way SYNC

```javascript
// File: validate-skills.js
// SYNC: Keep column names in sync with format-table.js formatHeader()
const COLUMNS = ["Complexity", "Performance"];

// File: format-table.js
// No SYNC note here - easy to forget to update validate-skills.js
function formatHeader() {
  return ["Complexity", "Performance"];
}
```

### Correct Pattern: Bidirectional SYNC

```javascript
// File: validate-skills.js
// SYNC: Keep column names in sync with format-table.js formatHeader()
const COLUMNS = ["Complexity", "Performance"];

// File: format-table.js
// SYNC: Keep column names in sync with validate-skills.js COLUMNS
function formatHeader() {
  return ["Complexity", "Performance"];
}
```

### SYNC Note Checklist

When adding a SYNC note:

1. **Add notes in BOTH locations** - never just one direction
1. **Reference by function/variable name** - not line numbers
1. **Verify the referenced code exists** - don't reference non-existent functions
1. **Update tests** - if synced values are used in tests, add SYNC notes there too
1. **Cross-environment code** - browser/Node.js boundaries require SYNC notes since modules cannot
   be shared; the test file replicates logic and the SYNC notes ensure they stay aligned

## Common Mistakes Checklist

Before committing JavaScript code, verify:

- [ ] JSDoc function descriptions match implementation behavior
- [ ] JSDoc `@param` and `@returns` types are accurate
- [ ] Module `@exports` documentation lists actual exports with correct values
- [ ] Linter directives are immediately before the suppressed line
- [ ] Linter directives are only used if the linter is actually configured
- [ ] Test `describe` blocks accurately categorize the tests they contain
- [ ] File headers accurately describe file contents
- [ ] Comments about JavaScript behavior are factually correct
- [ ] User-facing messages match actual UI/output terminology
- [ ] SYNC notes exist in both directions between related code
- [ ] Tests verify user-facing message content

## See Also

- [Script Test Coverage](../testing/script-test-coverage.md) - Test file structure and naming
- [Cross-Platform Compatibility](cross-platform-compatibility.md) - Platform-specific considerations
- [Comprehensive Test Coverage](../testing/comprehensive-test-coverage.md) - Test coverage requirements
