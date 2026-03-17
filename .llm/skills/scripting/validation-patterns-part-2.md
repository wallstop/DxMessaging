---
title: "Validation Patterns and Duplicate Warning Prevention Part 2"
id: "validation-patterns-part-2"
category: "scripting"
version: "1.2.0"
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

Continuation material extracted from `validation-patterns.md` to keep .llm files within the 300-line budget.

## Solution

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

## Related Links

- [Validation Patterns and Duplicate Warning Prevention](./validation-patterns.md)
