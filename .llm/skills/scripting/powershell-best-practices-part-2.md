---
title: "PowerShell Scripting Best Practices Part 2"
id: "powershell-best-practices-part-2"
category: "scripting"
version: "1.0.0"
created: "2026-01-27"
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

Continuation material extracted from `powershell-best-practices.md` to keep .llm files within the 300-line budget.

## Solution

## Pre-Commit Hook Terminology

Precise terminology prevents confusion about when hooks execute.

### Problem: Misleading "On Every Commit" Phrasing

```powershell
# MISLEADING comment:
# This script runs on every commit to validate line endings
```

This phrasing suggests the script runs _after_ commits are created, which is incorrect for
pre-commit hooks.

### Solution: Precise Timing Language

```powershell
# CORRECT: Precise timing
# This script runs before each commit is created (pre-commit hook)

# ALSO CORRECT: Alternative phrasing
# This pre-commit hook validates line endings before allowing commits
```

### Terminology Reference

| Phrase                               | Meaning                            | Correct For       |
| ------------------------------------ | ---------------------------------- | ----------------- |
| "runs before each commit is created" | Executes prior to commit creation  | pre-commit hooks  |
| "runs as a pre-commit hook"          | Explicitly names the hook type     | pre-commit hooks  |
| "runs after each commit"             | Executes after commit is finalized | post-commit hooks |
| "runs on every commit"               | Ambiguous—avoid this phrasing      | Neither           |
| "runs when commits are pushed"       | Executes during push operation     | pre-push hooks    |

## .NET File Encoding Behaviors

PowerShell's file operations have different encoding defaults than older cmdlets.

### WriteAllText Default Encoding

The .NET method `[System.IO.File]::WriteAllText()` uses UTF-8 **without BOM** by default:

```powershell
# This writes UTF-8 WITHOUT BOM (no byte order mark)
[System.IO.File]::WriteAllText($path, $content)

# Explicitly equivalent:
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($path, $content, $utf8NoBom)
```

### Common Confusion Sources

| Method                                   | Default Encoding | BOM     |
| ---------------------------------------- | ---------------- | ------- |
| `[System.IO.File]::WriteAllText()`       | UTF-8            | No BOM  |
| `Set-Content` (PS 5.1)                   | System default   | Varies  |
| `Set-Content -Encoding UTF8` (PS 5.1)    | UTF-8            | Has BOM |
| `Set-Content -Encoding utf8NoBOM` (PS 7) | UTF-8            | No BOM  |
| `Out-File` (PS 5.1)                      | UTF-16 LE        | Has BOM |

### Anti-Pattern: "Fixing" Correct Code

```powershell
# ALREADY CORRECT: Writes UTF-8 without BOM
[System.IO.File]::WriteAllText($path, $content)

# WRONG "FIX": Adding BOM when not needed
$utf8WithBom = New-Object System.Text.UTF8Encoding($true)
[System.IO.File]::WriteAllText($path, $content, $utf8WithBom)
```

Before "fixing" encoding code, verify the actual behavior matches requirements.

## Validation Checklist

Before merging PowerShell scripts:

- [ ] Regex patterns use `.*?` not `[^x]*` when content may contain excluded character
- [ ] Documentation comments don't contain literal matched content
- [ ] Here-strings use single `"` not doubled `""`
- [ ] Pre-commit hooks use precise timing terminology
- [ ] File encoding matches project requirements (usually UTF-8 no BOM)
- [ ] Variable expansions in here-strings are intentional

## Testing PowerShell Scripts

Test regex patterns with edge cases before committing:

```powershell
$testCases = @('<!-- simple -->', '<!-- has > inside -->', '<!-- multiple >> chars -->')
foreach ($case in $testCases) {
    Write-Host "Input: $case - Match: $($case -match $pattern)"
}
```

For comprehensive test coverage patterns, see [Script Test Coverage](../testing/script-test-coverage.md).

## See Also

- [Cross-Platform Compatibility](./cross-platform-compatibility.md) - Case sensitivity patterns
- [Script Test Coverage](../testing/script-test-coverage.md) - Script test coverage requirements
- [Git Workflow Robustness](../testing/git-workflow-robustness.md) - Git command patterns
- [Shell Pattern Matching](../../context.md#shell-pattern-matching) - Main context file patterns

## Changelog

| Version | Date       | Changes                                |
| ------- | ---------- | -------------------------------------- |
| 1.0.0   | 2026-01-27 | Initial version from PR feedback cycle |

## Related Links

- [PowerShell Scripting Best Practices](./powershell-best-practices.md)
