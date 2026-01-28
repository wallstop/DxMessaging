---
title: "PowerShell Scripting Best Practices"
id: "powershell-best-practices"
category: "scripting"
version: "1.0.0"
created: "2026-01-27"
updated: "2026-01-27"

source:
  repository: "wallstop/DxMessaging"
  files:
    - path: "scripts/"
    - path: ".github/workflows/"
  url: "https://github.com/wallstop/DxMessaging"

tags:
  - "powershell"
  - "scripting"
  - "regex"
  - "here-strings"
  - "encoding"
  - "ci-cd"
  - "pre-commit"

complexity:
  level: "intermediate"
  reasoning: "Requires understanding of PowerShell-specific behaviors that differ from other languages"

impact:
  performance:
    rating: "none"
    details: "Script patterns only; no runtime performance impact"
  maintainability:
    rating: "high"
    details: "Proper scripting patterns prevent subtle bugs and improve reliability"
  testability:
    rating: "medium"
    details: "Scripts should be tested but testing infrastructure varies"

prerequisites:
  - "Basic PowerShell syntax"
  - "Understanding of regular expressions"
  - "Familiarity with file encoding concepts"

dependencies:
  packages: []
  skills:
    - "git-workflow-robustness"

applies_to:
  languages:
    - "PowerShell"
  frameworks:
    - ".NET"
  versions:
    powershell: ">=5.1"
    dotnet: ">=netstandard2.0"

aliases:
  - "PowerShell patterns"
  - "PS1 best practices"
  - "Script reliability"

related:
  - "git-workflow-robustness"
  - "documentation-updates"

status: "stable"
---

# PowerShell Scripting Best Practices

> **One-line summary**: Avoid common PowerShell pitfalls involving regex patterns, here-strings,
> file encoding, and terminology precision.

## Overview

PowerShell has unique behaviors that differ from other scripting languages and even other .NET
contexts. This skill documents lessons learned from real PR feedback cycles to help avoid
repeated mistakes involving regex patterns, here-string quoting, file encoding, and precise
terminology for git hooks.

## Regex Pattern Pitfalls

### Character Class Exclusion vs Non-Greedy Matching

Using `[^x]*` to match content that might legitimately contain character `x` causes failures.

#### Problem: Matching HTML/XML Comments

```powershell
# BROKEN: Fails when comment contains > character
$pattern = '<!--[^>]*-->'
$content = '<!-- This has > inside the comment -->'
$content -match $pattern  # Fails to match correctly!
```

The pattern `[^>]*` means "match any character except `>`". When the content itself contains `>`,
the match terminates prematurely or fails entirely.

#### Solution: Use Non-Greedy Quantifiers

```powershell
# CORRECT: Non-greedy .*? matches minimal content
$pattern = '<!--.*?-->'
$content = '<!-- This has > inside the comment -->'
$content -match $pattern  # Correctly matches the entire comment
```

The `.*?` pattern matches any characters (including `>`) but stops at the first occurrence of the
closing delimiter `-->`.

#### When to Use Each Pattern

| Pattern  | Use Case                              | Example                   |
| -------- | ------------------------------------- | ------------------------- |
| `[^x]*`  | Content guaranteed not to contain `x` | `[^"]*` for simple quotes |
| `.*?`    | Content may contain any characters    | XML/HTML comments         |
| `[^x]*?` | Rarely needed; non-greedy exclusion   | Edge cases only           |

#### When `[^>]*` IS Appropriate: XML Tag Attributes

While `[^>]*` fails for XML/HTML **comments** (where `>` is allowed), it is safe for matching
XML **tag attributes** in well-formed XML. The `>` character IS allowed unescaped in attribute
values per XML 1.0 section 2.3's `AttValue` grammar, but the closing `>` of a tag is always
outside the quoted attribute values (since quotes must be properly matched):

```powershell
# SAFE: Matching XML tag attributes in well-formed XML
# The closing '>' is always outside quoted attribute values due to grammar constraints
$pattern = '<rect[^>]*/>'  # Safe: closing '>' is outside any attribute quotes
$pattern = '<g[^>]*>'      # Safe: same reasoning (malformed XML would fail parsing anyway)

# UNSAFE: Matching XML comments (where '>' IS allowed)
$pattern = '<!--[^>]*-->'  # Broken: comments can contain '>'
$pattern = '<!--.*?-->'    # Correct: use non-greedy for comments
```

This distinction applies when:

1. **Matching XML/SVG/HTML tag attributes**: `[^>]*` is safe—closing `>` is outside quotes
1. **The file is controlled/validated**: Project-maintained assets with known format
1. **Matching comments or CDATA**: Use `.*?` instead—these sections allow `>`

### Self-Referential Documentation Breaking Code

Comments that document regex patterns must not contain literal examples of matched content.

#### Problem: Meta-Pattern Collision

```powershell
# BROKEN: The comment itself matches the pattern being documented!
# Pattern matches: <!-- banner-version:X.Y.Z -->
$pattern = '<!--\s*banner-version:[\d.]+\s*-->'
```

If a script uses this pattern to find and replace version comments in files, and the script file
itself contains the comment showing the matched format, the script may inadvertently modify itself.

#### Solution: Abstract Documentation

```powershell
# CORRECT: Describe without literal examples
# Pattern matches HTML version comments (see docs for format details)
$pattern = '<!--\s*banner-version:[\d.]+\s*-->'
```

Or use placeholder notation:

```powershell
# Pattern matches: <!-- banner-version:{SEMVER} -->
# (where {SEMVER} represents a version number like 1.2.3)
$pattern = '<!--\s*banner-version:[\d.]+\s*-->'
```

## Here-String Quote Escaping

PowerShell here-strings have different escaping rules than regular strings.

### Double-Quote Here-Strings (@"..."@)

In `@"..."@` here-strings, double quotes do **NOT** need escaping:

```powershell
# BROKEN: Doubled quotes appear literally in output
$json = @"
{
    ""name"": ""value""
}
"@
# Output contains literal: {"name": "value"} ← Wrong!

# CORRECT: Single quotes work as-is
$json = @"
{
    "name": "value"
}
"@
# Output contains: {"name": "value"} ← Correct!
```

### Single-Quote Here-Strings (@'...'@)

In `@'...'@` here-strings, no escaping is possible—everything is literal:

```powershell
$literal = @'
This $variable won't expand
"Quotes" work 'fine'
'@
```

### Here-String Syntax Rules

| Type      | Variable Expansion | Quote Escaping Required | Use Case            |
| --------- | ------------------ | ----------------------- | ------------------- |
| `@"..."@` | Yes                | No (use single `"`)     | Dynamic content     |
| `@'...'@` | No                 | No escaping possible    | Literal/static text |

### Common Mistake Pattern

```powershell
# MISTAKE: Applying regular string escaping rules to here-strings
$bad = @"
Use ""doubled quotes"" for escaping
"@
# Result: Use "doubled quotes" for escaping ← Oops!

# CORRECT: Here-strings need no quote escaping
$good = @"
Use "single quotes" naturally
"@
# Result: Use "single quotes" naturally ← Correct!
```

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

### Manual Testing Pattern

```powershell
# Test regex patterns with edge cases
$testCases = @(
    '<!-- simple -->',
    '<!-- has > inside -->',
    '<!-- multiple >> chars -->',
    "<!-- has `n newline -->"
)

foreach ($case in $testCases) {
    $result = $case -match $pattern
    Write-Host "Input: $case"
    Write-Host "Match: $result"
    if ($result) { Write-Host "Matched: $($Matches[0])" }
}
```

### Here-String Output Verification

```powershell
# Verify here-string content matches expectations
$expected = '{"name": "value"}'
$actual = @"
{"name": "value"}
"@

if ($actual -ne $expected) {
    Write-Error "Mismatch: expected [$expected] got [$actual]"
}
```

## See Also

- [Git Workflow Robustness](../testing/git-workflow-robustness.md) - Git command patterns
- [Shell Pattern Matching](../../context.md#shell-pattern-matching) - Main context file patterns
- [Scripting Guidelines](../../context.md#scripting-guidelines) - General scripting rules

## Changelog

| Version | Date       | Changes                                |
| ------- | ---------- | -------------------------------------- |
| 1.0.0   | 2026-01-27 | Initial version from PR feedback cycle |
