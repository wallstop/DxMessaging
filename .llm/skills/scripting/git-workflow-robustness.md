---
id: git-workflow-robustness
title: "Git and Parser Robustness in CI/CD"
description: "Best practices for robust git commands, line ending normalization, and EditorConfig in workflows"
version: 1.1.0
created: 2026-01-27
updated: 2026-01-27
status: stable
category: scripting
complexity:
  level: intermediate
  prerequisites:
    - Basic shell scripting
    - Git fundamentals
    - EditorConfig syntax
    - GitHub Actions workflow syntax
impact:
  testability:
    rating: high
    description: Prevents flaky tests from git edge cases
  maintainability:
    rating: high
    description: Improves workflow reliability and prevents unintended commits
related:
  - testing/comprehensive-test-coverage
  - documentation/documentation-updates
---

# Git and Parser Robustness in CI/CD

## Overview

This skill covers best practices for writing robust git commands in CI/CD pipelines,
handling line ending normalization safely, configuring EditorConfig for mixed policies,
and using git-auto-commit-action correctly.

## Solution

## Git Command Edge Cases

### Initial Commits and Single-Commit Repositories

Git commands that reference previous commits can fail in specific repository states:

```bash
# DANGEROUS: Fails on initial commit (no HEAD~1 exists)
git diff HEAD~1 -- path/to/file

# DANGEROUS: Fails on single-commit repos
git log --oneline -2  # May return fewer than 2 commits
```

#### Solution patterns

```bash
# Check commit count before referencing ancestors
COMMIT_COUNT=$(git rev-list --count HEAD 2>/dev/null || echo "0")
if [ "$COMMIT_COUNT" -gt 1 ]; then
    git diff HEAD~1 -- path/to/file
else
    echo "No previous commit to compare against"
fi
```

```bash
# Use --first-parent and handle errors gracefully
git diff HEAD~1 -- path/to/file 2>/dev/null || true
```

### Shallow Clones in CI Environments

Many CI systems use shallow clones for speed. Commands that traverse history may fail:

```bash
# DANGEROUS: May fail with shallow clone
git log --oneline --all

# DANGEROUS: Ancestor references may not exist
git merge-base main feature-branch
```

#### Solution patterns

```bash
# Fetch sufficient depth before operations
git fetch --depth=50 origin main

# Or unshallow if needed
git fetch --unshallow 2>/dev/null || true
```

### Detached HEAD States

CI systems often check out specific commits, creating detached HEAD states:

```bash
# DANGEROUS: Branch name queries fail in detached HEAD
git branch --show-current  # Returns empty string

# DANGEROUS: Assumes branch context
git push origin HEAD:refs/heads/$(git branch --show-current)
```

#### Solution patterns

```bash
# Check for detached HEAD before branch operations
CURRENT_BRANCH=$(git branch --show-current)
if [ -z "$CURRENT_BRANCH" ]; then
    echo "In detached HEAD state, using SHA instead"
    CURRENT_REF=$(git rev-parse HEAD)
else
    CURRENT_REF=$CURRENT_BRANCH
fi
```

### Empty Repository States

Scripts must handle repositories with no commits:

```bash
# DANGEROUS: Fails on empty repos
git log --oneline -1

# DANGEROUS: HEAD doesn't exist
git rev-parse HEAD
```

#### Solution patterns

```bash
# Check if HEAD exists before using it
if git rev-parse HEAD >/dev/null 2>&1; then
    git log --oneline -1
else
    echo "Repository has no commits"
fi
```

### File Path Edge Cases

Git commands with file paths can fail unexpectedly:

```bash
# DANGEROUS: Fails if path contains spaces
git diff HEAD -- $FILE_PATH

# DANGEROUS: Glob patterns may match nothing
git diff HEAD -- *.md
```

#### Solution patterns

```bash
# Always quote variables containing paths
git diff HEAD -- "$FILE_PATH"

# Use nullglob or check pattern expansion
shopt -s nullglob
FILES=(*.md)
if [ ${#FILES[@]} -gt 0 ]; then
    git diff HEAD -- "${FILES[@]}"
fi
```

## Markdown Inline Code Parsing

### CommonMark Multi-Backtick Semantics

CommonMark defines specific rules for code spans with multiple backticks:

1. Opening and closing sequences must have the **same number of backticks**
1. Content can contain backticks with fewer characters than the delimiter
1. A single backtick inside double-backtick delimiters is literal text

#### Examples

````markdown
`code` → <code>code</code>
`code` → <code>code</code>
`` `code` `` → <code>`code`</code>
```a`b`` → <code>a`b</code> (opened with ``, closed with ``)
`` a ` b ``     → <code>a` b</code>
````

### Parsing Algorithm

A correct inline code parser must:

1. Detect opening backtick sequences and count their length
1. Search for a closing sequence of **exactly the same length**
1. Handle nested or unmatched backticks as literal content
1. Strip one leading and one trailing space when both are present

**Key implementation requirements:**

- Count opening backticks and search for closing sequence of **exactly the same length**
- Verify the closing sequence isn't part of a longer backtick run
- Strip one leading and trailing space when both are present (CommonMark rule)
- Treat unmatched opening sequences as literal text

### Common Parsing Pitfalls

1. **Greedy matching**: Using regex like `` `[^`]+` `` fails on nested backticks
1. **Ignoring backtick count**: Treating all backtick sequences as equivalent
1. **Missing space stripping**: Not handling `` ` code ` `` correctly
1. **Partial matches**: Matching ` `` ` inside ` ``` ` sequences

## Test Coverage for Parsers

### Edge Case Categories

When testing parsers, cover these categories:

| Category      | Examples                                       |
| ------------- | ---------------------------------------------- |
| Empty/minimal | `''`, `` ` ``, ` `` `                          |
| Boundary      | Code at start/end, only code, adjacent spans   |
| Nested        | ``` `` `nested` `` ```, unequal counts         |
| Whitespace    | Leading/trailing spaces, only spaces, newlines |
| Unicode       | `café`, emoji, zero-width characters           |

### Data-Driven Test Patterns

Use parameterized tests for comprehensive coverage:

```javascript
describe("inline code parsing", () => {
  const testCases = [
    // [input, expectedOutput, description]
    ["`code`", [{ type: "code", content: "code" }], "simple code span"],
    ["``a`b``", [{ type: "code", content: "a`b" }], "backtick in code"],
    ["` a `", [{ type: "code", content: "a" }], "space stripping"],
    [
      "`a``b`",
      [
        { type: "code", content: "a" },
        { type: "code", content: "b" }
      ],
      "adjacent spans"
    ]
  ];

  test.each(testCases)("%s → %j (%s)", (input, expected, _desc) => {
    expect(parseInlineCode(input)).toEqual(expected);
  });
});
```

### Property-Based Testing

For parser robustness, use property-based tests with libraries like `fast-check`:

- **Never throws**: Parser should handle any input without throwing
- **Content preservation**: Total output length ≤ input length (accounting for delimiters)
- **Idempotence**: Re-parsing output yields same structure

## CI/CD Integration Patterns

### Fail-Safe Git Operations

Structure git operations in CI to handle all edge cases:

```yaml
# GitHub Actions example
- name: Check for changes
  id: changes
  run: |
    # Handle initial commit
    if git rev-parse HEAD~1 >/dev/null 2>&1; then
      CHANGED_FILES=$(git diff --name-only HEAD~1 HEAD)
    else
      # First commit - all files are "new"
      CHANGED_FILES=$(git ls-tree --name-only -r HEAD)
    fi
    echo "files=$CHANGED_FILES" >> $GITHUB_OUTPUT
```

### Parser Testing in CI

Ensure parser tests run with comprehensive edge cases:

```yaml
- name: Run parser tests
  run: |
    npm test -- --coverage --coverageThreshold='{"global":{"branches":90}}'
```

### Handling `grep` Exit Codes

`grep` returns exit code 1 when no matches are found, which fails CI pipelines:

```bash
# DANGEROUS: Fails CI when no matches
grep "pattern" file.txt

# SAFE: Handle no-match case
grep "pattern" file.txt || true

# SAFE: Use count with fallback
COUNT=$(grep -c "pattern" file.txt 2>/dev/null || echo "0")
```

## Line Ending Normalization in Workflows

### The `git add --renormalize` Pitfall

`git add --renormalize .` stages ALL files in the repository for line ending normalization,
which can include unintended changes from other steps or untracked files.

```bash
# DANGEROUS: Stages ALL files, including unintended changes
git add --renormalize .

# SAFE: Specify exact file patterns to renormalize
git add --renormalize -- '*.md' '**/*.md' '*.json' '**/*.json'
```

**Why this matters**: In CI workflows that modify files, running `git add --renormalize .`
will stage everything, potentially committing tool configuration changes, cached files,
or modifications from earlier workflow steps that weren't intended for commit.

### Targeted Renormalization Pattern

Always specify explicit path patterns that match only the files you intend to commit:

```yaml
- name: Renormalize line endings
  run: |
    # Only renormalize the specific file types we're formatting
    git add --renormalize -- \
      '*.md' '**/*.md' \
      '*.json' '**/*.json' \
      '*.yml' '**/*.yml' \
      '*.yaml' '**/*.yaml'
```

### git-auto-commit-action and `file_pattern`

The `file_pattern` option in `stefanzweifel/git-auto-commit-action` only limits which
files are **added** to the commit. Previously staged files are still committed.

```yaml
# PROBLEM: If files were staged earlier, they still get committed
- uses: stefanzweifel/git-auto-commit-action@v7
  with:
    file_pattern: "**/*.md" # Only limits what's NEWLY added
```

**Safe pattern**: Use `add_options` with `--renormalize` and match `file_pattern` exactly:

```yaml
- name: Commit changes
  uses: stefanzweifel/git-auto-commit-action@v7
  with:
    commit_message: "chore(format): apply formatting"
    add_options: --renormalize
    file_pattern: |
      **/*.md
      **/*.json
      **/*.yml
      **/*.yaml
```

**Important**: The `file_pattern` and any preceding `git add` commands should target
the same file set to avoid committing unintended files.

## EditorConfig Glob Pattern Syntax

### Recursive Matching Requires `**`

EditorConfig patterns like `[*.sh]` only match files in the **root directory**.
Use `[**/*.sh]` for recursive matching:

```editorconfig
# WRONG: Only matches *.sh in root directory
[*.sh]
end_of_line = lf

# CORRECT: Matches *.sh in all directories
[**/*.sh]
end_of_line = lf
```

### Matching Both Root and Subdirectories

To match files in both the root and subdirectories, use the `**` pattern:

```editorconfig
# Matches shell scripts anywhere in the repository
[**/*.sh]
end_of_line = lf
indent_style = space
indent_size = 2
```

### Multiple Patterns in One Section

Use brace expansion for multiple extensions:

```editorconfig
# Shell scripts and related files
[**/*.{sh,bash,zsh}]
end_of_line = lf
```

## Mixed Line Ending Policies

### Clear Error Messages for Policy Violations

When a project has different line ending policies for different file types (e.g., CRLF
for most files, LF for shell scripts), error messages must clearly indicate which
policy was violated:

```powershell
# POOR: Ambiguous error message
"Line ending error in file.sh"

# GOOD: Clear policy indication
"file.sh: Expected LF line endings (shell script policy), found CRLF"
"README.md: Expected CRLF line endings (project policy), found LF"
```

### Validation Script Pattern

```bash
#!/bin/bash
# Check line endings with clear policy reporting

check_file() {
    local file="$1"
    local expected="$2"
    local policy_name="$3"

    if [[ "$expected" == "lf" ]]; then
        if grep -q $'\r' "$file"; then
            echo "ERROR: $file: Expected LF ($policy_name), found CRLF" >&2
            return 1
        fi
    elif [[ "$expected" == "crlf" ]]; then
        if ! grep -q $'\r' "$file"; then
            echo "ERROR: $file: Expected CRLF ($policy_name), found LF" >&2
            return 1
        fi
    fi
    return 0
}

# Check shell scripts (LF required)
for f in $(find . -name "*.sh" -type f); do
    check_file "$f" "lf" "shell script policy"
done

# Check other files (CRLF required)
for f in $(find . -name "*.md" -type f); do
    check_file "$f" "crlf" "project policy"
done
```

## Validation Checklist

Before merging code with git commands or parsers:

- [ ] Git commands handle initial/empty commits
- [ ] Git commands work with shallow clones
- [ ] Git commands handle detached HEAD states
- [ ] File paths are properly quoted
- [ ] `git add --renormalize` uses targeted paths, not `.` or `*`
- [ ] git-auto-commit-action `file_pattern` matches staged files exactly
- [ ] EditorConfig patterns use `**/*.ext` for recursive matching
- [ ] Line ending error messages indicate which policy was violated
- [ ] Parsers handle empty input
- [ ] Parsers handle malformed/unmatched delimiters
- [ ] Parsers handle unicode and special characters
- [ ] Tests cover boundary conditions
- [ ] Tests use data-driven patterns for edge cases
- [ ] CI pipelines handle `grep` exit codes properly

## See Also

- [Comprehensive Test Coverage](../testing/comprehensive-test-coverage.md) - Detailed testing
  strategies
- [Documentation Updates](../documentation/documentation-updates.md) - Keeping docs in sync
- [Shell Pattern Matching](../../context.md#shell-pattern-matching) - Main context file patterns
- [EditorConfig glob patterns](https://spec.editorconfig.org) - Official specification
