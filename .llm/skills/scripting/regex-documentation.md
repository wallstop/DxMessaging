---
title: "Regex Pattern Documentation"
id: "regex-documentation"
category: "scripting"
version: "1.0.0"
created: "2026-01-29"
updated: "2026-01-29"

source:
  repository: "wallstop/DxMessaging"
  files:
    - path: "scripts/"
    - path: "docs/"
  url: "https://github.com/wallstop/DxMessaging"

tags:
  - "regex"
  - "documentation"
  - "scripting"
  - "patterns"
  - "comments"
  - "maintenance"

complexity:
  level: "intermediate"
  reasoning: "Requires regex knowledge to understand flag behaviors and documentation implications"

impact:
  performance:
    rating: "none"
    details: "Documentation only; no runtime performance impact"
  maintainability:
    rating: "high"
    details: "Improves code clarity and prevents maintenance bugs from misleading comments"
  testability:
    rating: "medium"
    details: "Helps verify regex behavior matches documented expectations"

prerequisites:
  - "Basic regex syntax"
  - "Understanding of regex flags"

dependencies:
  packages: []
  skills:
    - "shell-best-practices"

applies_to:
  languages:
    - "JavaScript"
    - "PowerShell"
    - "C#"
    - "Bash"
  frameworks: []
  versions: {}

aliases:
  - "Regex comments"
  - "Pattern documentation"
  - "Regular expression documentation"

related:
  - shell-best-practices
  - cross-platform-compatibility

status: "stable"
---

# Regex Pattern Documentation

> **One-line summary**: Document regex patterns accurately by describing what they actually match,
> including all flag-dependent behavior.

## Overview

This skill covers best practices for documenting regular expression patterns. Accurate comments are critical because regex behavior changes dramatically based on flags, and misleading comments cause maintenance bugs. Many subtle bugs arise when comments describe intended behavior rather than actual behavior.

## Solution

1. **Describe actual behavior** - Comments must answer "What will this pattern match?" not "What do I hope it matches?"
1. **Document flag effects** - Global, case-insensitive, multiline, and dotall flags change matching behavior significantly
1. **Use precise language** - Say "matches all occurrences" (global) vs "matches first occurrence" (non-global)
1. **Include character class details** - Specify whether patterns match Unicode, newlines, or specific character ranges

## Core Principle

### Comments must describe actual behavior, not intended behavior

A regex comment should answer: "What will this pattern actually match?" not "What do I hope it matches?"

## Whitespace Character Classes

### `\s` vs `[ \t]` - A Critical Distinction

The `\s` shorthand matches **all whitespace characters including newlines**. This is a frequent source of bugs when you only want to match spaces and tabs.

| Pattern     | Matches                                                                     | Use When                                                         |
| ----------- | --------------------------------------------------------------------------- | ---------------------------------------------------------------- |
| `\s`        | Space, tab, newline (`\n`), carriage return (`\r`), form feed, vertical tab | You want to match ANY whitespace including line breaks           |
| `[ \t]`     | Only space and tab                                                          | You want horizontal whitespace only, preserving newlines         |
| `[\r\n]`    | Only newline characters                                                     | You want to match line breaks specifically                       |
| `[^\S\r\n]` | Whitespace except newlines (double negative trick)                          | Alternative to `[ \t]` that includes other horizontal whitespace |

#### Wrong Pattern

```javascript
// WRONG: \s* consumes newlines, concatenating lines when replaced
const DIRECTIVE_PATTERN = /^%%\{init:.*?\}%%\s*/gm;
// Intended: Strip directive and trailing spaces
// Actual: Also consumes the newline, joining this line with the next
```

#### Correct Pattern

```javascript
// CORRECT: [ \t]* only matches horizontal whitespace
const DIRECTIVE_PATTERN = /^[ \t]*%%\{init:.*?\}%%[ \t]*\r?\n?/gm;
// Strips directive, surrounding spaces/tabs, and just its own line ending
// Preserves separation between other lines
```

#### Practical Impact

```javascript
const text = "line1\n%%{init:...}%%\nline2";

// Bug: \s* consumes the newline AFTER the directive
text.replace(/%%\{init:.*?\}%%\s*/g, "");
// Result: "line1\nline2" with missing newline before line2

// Fixed: [ \t]* preserves newlines
text.replace(/%%\{init:.*?\}%%[ \t]*/g, "");
// Result: "line1\n\nline2" - newlines preserved
```

## Flag Reference Table

| Flag | Name                 | Effect                                                    | Comment Implications                                            |
| ---- | -------------------- | --------------------------------------------------------- | --------------------------------------------------------------- |
| `g`  | Global               | Match all occurrences, not just the first                 | Comments must use plural ("matches all X" not "matches X")      |
| `i`  | Case-insensitive     | `[A-Za-z]` matches regardless of case                     | Comments must mention "case-insensitive" explicitly             |
| `m`  | Multiline            | `^` and `$` match start/end of each line, not just string | Comments must say "at start/end of any line" not "at start/end" |
| `s`  | DotAll (Single-line) | `.` matches newline characters too                        | Comments must mention "including newlines" when using `.`       |
| `u`  | Unicode              | Enables full Unicode support, `\p{}` escapes              | Comments should note Unicode-aware matching if relevant         |

## Flag-Specific Documentation Rules

### Global Flag (`g`)

The global flag changes whether a pattern finds one or all matches. Comments must reflect this.

#### Wrong Comments

```javascript
// WRONG: Comment implies single match, but /g finds all
const pattern = /\d+/g;
// Matches a number in the string
```

```javascript
// WRONG: Singular language with global flag
const emailPattern = /\w+@\w+\.\w+/g;
// Matches an email address
```

#### Correct Comments

```javascript
// CORRECT: Plural language matches global behavior
const pattern = /\d+/g;
// Matches all numbers in the string

const emailPattern = /\w+@\w+\.\w+/g;
// Matches all email addresses in the string
```

### Multiline Flag (`m`)

The multiline flag changes the meaning of `^` and `$` anchors. This is one of the most commonly misdocumented behaviors.

#### Wrong Comments

```javascript
// WRONG: Says "at start" but /m means "at start of ANY line"
const pattern = /^function/m;
// Matches 'function' at start of string
```

```javascript
// WRONG: Omits multiline behavior entirely
const endPattern = /;$/m;
// Matches semicolon at end
```

#### Correct Comments

```javascript
// CORRECT: Explicitly mentions line-level matching
const pattern = /^function/m;
// Matches 'function' at start of any line (not just string start)

const endPattern = /;$/m;
// Matches semicolon at end of any line (not just string end)
```

#### Multiline Behavior Demonstration

```javascript
const text = `line1
function test() {
  return 42;
}`;

// Without /m flag: ^ only matches position 0
/^function/.test(text); // false - 'function' is not at string start

// With /m flag: ^ matches after every newline too
/^function/m.test(text); // true - 'function' is at start of line 2
```

### DotAll Flag (`s`)

The DotAll flag makes `.` match newline characters. Without it, `.` matches any character except newlines.

#### Wrong Comments

```javascript
// WRONG: Says "any character" but without /s, newlines are excluded
const pattern = /start.*end/;
// Matches 'start', then any characters, then 'end'
```

```javascript
// WRONG: Implies newlines are matched when they're not
const blockPattern = /<div>.*<\/div>/;
// Matches everything between div tags
```

#### Correct Comments

```javascript
// CORRECT: Explicitly notes newline exclusion
const pattern = /start.*end/;
// Matches 'start', any characters EXCEPT newlines, then 'end'

// CORRECT: With /s flag, mentions newline inclusion
const multilinePattern = /start.*end/s;
// Matches 'start', any characters INCLUDING newlines, then 'end'

// CORRECT: Alternative using explicit character class
const explicitPattern = /start[\s\S]*end/;
// Matches 'start', any characters including newlines, then 'end'
```

#### DotAll Behavior Demonstration

```javascript
const text = `start
middle
end`;

// Without /s: . doesn't match newlines
/start.*end/.test(text); // false - can't cross newlines

// With /s: . matches everything including newlines
/start.*end/s.test(text); // true - matches across lines

// Alternative: [\s\S] matches any character including newlines
/start[\s\S]*end/.test(text); // true - explicit any-character class
```

### Case-Insensitive Flag (`i`)

#### Wrong Comments

```javascript
// WRONG: Doesn't mention case-insensitivity
const pattern = /error/i;
// Matches 'error' in the string
```

#### Correct Comments

```javascript
// CORRECT: Explicitly mentions case behavior
const pattern = /error/i;
// Matches 'error' case-insensitively (ERROR, Error, error, etc.)
```

### Unicode Flag (`u`)

#### Wrong Comments

```javascript
// WRONG: Doesn't explain Unicode implications
const emojiPattern = /\p{Emoji}/u;
// Matches emoji
```

#### Correct Comments

```javascript
// CORRECT: Notes Unicode property escapes
const emojiPattern = /\p{Emoji}/u;
// Matches any Unicode emoji character (requires /u flag for \p{} syntax)

const letterPattern = /\p{Letter}+/gu;
// Matches all sequences of Unicode letters (any script, not just ASCII)
```

## Combined Flag Documentation

When patterns use multiple flags, document the combined effect.

### Wrong Comments

```javascript
// WRONG: Doesn't explain combined behavior
const pattern = /^todo:/gim;
// Matches TODO markers
```

### Correct Comments

```javascript
// CORRECT: Documents each flag's contribution
const pattern = /^todo:/gim;
// Matches all 'todo:' markers at start of any line, case-insensitively
// - /g: finds all occurrences
// - /i: matches TODO, Todo, todo, etc.
// - /m: ^ matches start of each line, not just string start
```

## Anti-Patterns to Avoid

### 1. Copy-Paste Comments

Never copy comments from a similar regex without verifying accuracy.

```javascript
// ANTI-PATTERN: Comment copied from non-global version
const pattern = /\bword\b/g;
// Matches 'word' as a whole word  // WRONG: should say "all occurrences"
```

### 2. Intent-Based Comments

Comments should describe behavior, not intent.

```javascript
// ANTI-PATTERN: Describes intent, not behavior
const pattern = /^#.*/m;
// For matching comment lines  // WRONG: doesn't explain what it actually does

// CORRECT: Describes actual matching behavior
const pattern = /^#.*/m;
// Matches lines starting with # (from # to end of line, on any line)
```

### 3. Incomplete Anchor Documentation

Always specify what anchors match in context.

```javascript
// ANTI-PATTERN: Vague anchor description
const pattern = /^start/;
// Matches 'start' at the beginning

// CORRECT: Specific anchor behavior
const pattern = /^start/;
// Matches 'start' at string start only (not line start; no /m flag)
```

### 4. Omitting Quantifier Behavior

Document what quantifiers actually capture.

```javascript
// ANTI-PATTERN: Doesn't explain greedy vs lazy
const pattern = /".+"/;
// Matches quoted strings

// CORRECT: Explains greedy matching implications
const pattern = /".+"/;
// Matches from first " to LAST " (greedy); may span multiple quoted sections
// Use /"[^"]+"/g or /".+?"/g for individual quoted strings
```

### 5. Ignoring Edge Cases

Document known limitations.

```javascript
// ANTI-PATTERN: No edge case documentation
const urlPattern = /https?:\/\/\S+/g;
// Matches URLs

// CORRECT: Documents limitations
const urlPattern = /https?:\/\/\S+/g;
// Matches all http/https URLs (simple pattern)
// Note: May include trailing punctuation; doesn't validate URL structure
```

## Validation Checklist

Before committing any regex pattern with comments, verify:

### Flag Verification

- [ ] **Global (`g`)**: Comments use plural forms ("all X" not "X")
- [ ] **Multiline (`m`)**: Comments say "any line" for `^` and `$` anchors
- [ ] **DotAll (`s`)**: Comments mention newline matching for `.`
- [ ] **Case-insensitive (`i`)**: Comments explicitly note case behavior
- [ ] **Unicode (`u`)**: Comments note Unicode-specific features if used

### Anchor Verification

- [ ] `^` comment specifies: string start, line start, or both (based on `/m`)
- [ ] `$` comment specifies: string end, line end, or both (based on `/m`)
- [ ] `\b` and `\B` behavior is correctly described

### Quantifier Verification

- [ ] Greedy vs lazy behavior is documented when it matters
- [ ] `*` vs `+` implications are clear (zero-or-more vs one-or-more)

### Behavioral Verification

- [ ] Comment describes actual behavior, not intended behavior
- [ ] Edge cases and limitations are documented
- [ ] Combined flag effects are explained

### Testing Verification

- [ ] Pattern has been tested with expected matches
- [ ] Pattern has been tested with expected non-matches
- [ ] Edge cases have been verified

## Language-Specific Notes

### JavaScript

- Use template literals for patterns with many escapes: ``new RegExp(`pattern`, 'flags')``
- Remember that string escaping doubles backslashes: `new RegExp('\\d+')` vs `/\d+/`

### Shell (grep, sed, awk)

- BRE (Basic Regular Expressions): `grep 'pattern'`
- ERE (Extended Regular Expressions): `grep -E 'pattern'`
- Different flag syntax: `-i` for case-insensitive, not `/i`
- See [Shell Best Practices](shell-best-practices.md) for grep-specific guidance

### Python

- Use raw strings: `r'\d+'` to avoid double-escaping
- `re.MULTILINE` corresponds to `/m`, `re.DOTALL` to `/s`

## See Also

- [Shell Best Practices](shell-best-practices.md) - grep pattern matching guidelines
- [Cross-Platform Compatibility](cross-platform-compatibility.md) - regex differences across platforms
