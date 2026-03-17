---
title: "JavaScript Code Quality Practices Part 2"
id: "javascript-code-quality-part-2"
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

## Related Links

- [JavaScript Code Quality Practices](./javascript-code-quality.md)
