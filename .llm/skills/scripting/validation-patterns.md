---
title: "Validation Patterns and Duplicate Warning Prevention"
id: "validation-patterns"
category: "scripting"
version: "1.0.0"
created: "2026-01-30"
updated: "2026-01-30"

source:
  repository: "wallstop/DxMessaging"
  files:
    - path: "scripts/validate-skills.js"
    - path: "scripts/__tests__/validate-skills-optional-fields.test.js"
  url: "https://github.com/wallstop/DxMessaging"

tags:
  - "validation"
  - "javascript"
  - "error-handling"
  - "duplicate-warnings"
  - "enum-validation"
  - "optional-fields"
  - "testing"

complexity:
  level: "intermediate"
  reasoning: "Requires understanding of validation flow and conditional logic ordering"

impact:
  performance:
    rating: "none"
    details: "Validation patterns only; no runtime performance impact"
  maintainability:
    rating: "high"
    details: "Prevents confusing duplicate warnings that obscure actual issues"
  testability:
    rating: "high"
    details: "Clear validation logic is easier to test exhaustively"

prerequisites:
  - "JavaScript fundamentals"
  - "Understanding of null/undefined distinction"
  - "Basic validation concepts"

dependencies:
  skills:
    - "javascript-code-quality"

applies_to:
  languages:
    - "JavaScript"
    - "TypeScript"
  frameworks:
    - "Node.js"

aliases:
  - "duplicate warning prevention"
  - "validation deduplication"
  - "enum validation"

related:
  - "javascript-code-quality"
  - "comprehensive-test-coverage"

status: "stable"
---

# Validation Patterns and Duplicate Warning Prevention

> **One-line summary**: Structure validation logic to produce exactly one warning per issue,
> not duplicates from multiple overlapping checks.

## Overview

When validating fields that have multiple possible error conditions (missing, empty, invalid enum
value), careless ordering can produce duplicate warnings for the same underlying issue. This
confuses users and makes debugging harder.

## Solution

1. **Order validation checks from most specific to most general** - check for missing/empty before
   checking for invalid values
1. **Use else-if chains** to ensure only one warning per field
1. **Exclude empty/null values from enum validation** - an empty string is "empty," not "invalid enum"
1. **Write integration tests** that verify exactly one warning per field condition

## The Duplicate Warning Problem

### Anti-Pattern: Overlapping Validation Checks

```javascript
// BAD: Two separate if statements can both trigger for the same issue
function validateField(value, fieldName) {
  const warnings = [];

  // Check 1: Is the value invalid according to enum?
  if (!VALID_VALUES.includes(value)) {
    warnings.push(`Invalid ${fieldName}: '${value}'`);
  }

  // Check 2: Is the value missing or empty?
  if (value == null || value === "") {
    warnings.push(`Missing ${fieldName}`);
  }

  return warnings;
}

// When called with empty string:
validateField("", "complexity.level");
// Returns TWO warnings:
// - "Invalid complexity.level: ''"  (because '' is not in VALID_VALUES)
// - "Missing complexity.level"       (because '' === "")
```

### Correct Pattern: Mutually Exclusive Checks

```javascript
// GOOD: Ordered checks ensure exactly one warning
function validateField(value, fieldName) {
  const warnings = [];

  // Check 1: Is the value missing?
  if (value == null) {
    warnings.push(`Missing ${fieldName}`);
  }
  // Check 2: Is the value empty string?
  else if (value === "") {
    warnings.push(`Empty ${fieldName}`);
  }
  // Check 3: Is the value invalid? (Only check if value is present and non-empty)
  else if (!VALID_VALUES.includes(value)) {
    warnings.push(`Invalid ${fieldName}: '${value}'`);
  }

  return warnings;
}

// When called with empty string:
validateField("", "complexity.level");
// Returns exactly ONE warning:
// - "Empty complexity.level"
```

## Real-World Example: Skill File Validation

This pattern is implemented in [validate-skills.js](../../scripts/validate-skills.js):

```javascript
// Validate complexity level - exclude empty values from enum check
if (
  frontmatter.complexity != null &&
  frontmatter.complexity.level != null &&
  frontmatter.complexity.level !== "" && // <-- Key: exclude empty strings
  !VALID_COMPLEXITY_LEVELS.includes(frontmatter.complexity.level)
) {
  warnings.push(
    new ValidationError(
      skillFile.relativePath,
      "complexity.level",
      `Invalid complexity level '${frontmatter.complexity.level}'. Valid: ${VALID_COMPLEXITY_LEVELS.join(", ")}`
    )
  );
}

// Later, check for missing/empty (mutually exclusive with above)
if (frontmatter.complexity == null || frontmatter.complexity.level == null) {
  warnings.push(
    new ValidationError(
      skillFile.relativePath,
      "complexity.level",
      `Missing 'complexity.level' - will show '?' in Complexity column of skills index`
    )
  );
} else if (frontmatter.complexity.level === "") {
  warnings.push(
    new ValidationError(
      skillFile.relativePath,
      "complexity.level",
      `Empty 'complexity.level' - will show '?' in Complexity column of skills index`
    )
  );
}
```

## Integration Testing for Warning Counts

Always write integration tests that verify the exact number of warnings produced:

```javascript
// GOOD: Integration test that verifies no duplicate warnings
describe("empty string complexity.level produces exactly one warning", () => {
  test("should produce exactly 1 warning (no duplicate from invalid enum check)", () => {
    const content = createValidFrontmatter({
      complexity: { level: "" }
    });
    const skillFile = createMockSkillFile(tempDir, content);

    const result = validateSkill(skillFile);

    // Filter warnings for the specific field
    const complexityWarnings = result.warnings.filter((w) => w.field === "complexity.level");

    // Key assertion: exactly ONE warning, not two
    expect(complexityWarnings).toHaveLength(1);
    expect(complexityWarnings[0].message).toContain("Empty 'complexity.level'");
  });
});
```

## Validation Check Ordering Rules

1. **Null/undefined first** - the most fundamental check (is the value present?)
1. **Empty string second** - present but semantically empty
1. **Type checks third** - is it the right type (array, object, string)?
1. **Format/enum checks last** - only check value validity if value is present and correct type

```javascript
// Optimal ordering for comprehensive validation
function validateOptionalField(value, fieldName, validValues) {
  // 1. Missing check
  if (value == null) {
    return [{ type: "warning", message: `Missing ${fieldName}` }];
  }

  // 2. Empty string check
  if (value === "") {
    return [{ type: "warning", message: `Empty ${fieldName}` }];
  }

  // 3. Type check (if applicable)
  if (typeof value !== "string") {
    return [{ type: "warning", message: `${fieldName} must be a string` }];
  }

  // 4. Enum/format check (only reached if all above pass)
  if (!validValues.includes(value)) {
    return [{ type: "warning", message: `Invalid ${fieldName}: '${value}'` }];
  }

  return []; // Valid
}
```

## Common Mistakes Checklist

Before committing validation code, verify:

- [ ] Empty strings don't trigger both "invalid enum" and "empty field" warnings
- [ ] Null/undefined values don't trigger both "invalid enum" and "missing field" warnings
- [ ] Integration tests verify exactly one warning per field per condition
- [ ] Validation checks use else-if chains or early returns to ensure mutual exclusivity
- [ ] Filter by field name when counting warnings in tests (to isolate the field under test)

## Troubleshooting

### Duplicate Warnings Appearing in Output

**Symptom**: A field produces two similar warnings (e.g., both "Missing X" and "Invalid X: ''")

**Diagnosis**:

1. Check if the enum validation excludes empty/null values with explicit checks
1. Verify else-if chains are used instead of separate if statements
1. Run integration tests that filter by field name and verify warning count

**Solution**: Ensure enum validation includes `value !== ''` in its guard clause:

```javascript
// Add empty string exclusion to enum check
if (value != null && value !== "" && !VALID_VALUES.includes(value)) {
  // Only one warning path
}
```

### Whitespace-Only Strings Triggering Wrong Warning

**Symptom**: Input like `"   "` produces "Invalid enum" instead of "Empty field"

**Explanation**: Current validation does not trim whitespace, so `"   "` is treated as a
non-empty string that fails enum validation. This is intentional - if whitespace-only should
be treated as empty, add explicit `.trim()` checks.

### Test Counting More Warnings Than Expected

**Symptom**: Test expects 1 warning but gets 2+

**Diagnosis**:

1. Filter warnings by field name: `result.warnings.filter(w => w.field === "fieldName")`
1. Log all warnings to see what's being produced
1. Check if both enum validation and missing/empty checks are triggering

## See Also

- [JavaScript Code Quality](javascript-code-quality.md) - General JavaScript best practices
- [Comprehensive Test Coverage](../testing/comprehensive-test-coverage.md) - Test coverage requirements
