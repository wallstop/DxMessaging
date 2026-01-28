---
title: "Git Renormalize Pattern Validation"
id: "git-renormalize-patterns"
category: "github-actions"
version: "1.0.0"
created: "2026-01-28"
updated: "2026-01-28"

source:
  repository: "wallstop/DxMessaging"
  files:
    - path: ".github/workflows/"
  url: "https://github.com/wallstop/DxMessaging"

tags:
  - "github-actions"
  - "git"
  - "ci-cd"
  - "line-endings"
  - "gitattributes"

complexity:
  level: "intermediate"
  reasoning: "Requires understanding of git pathspecs, gitattributes, and CI failure modes"

impact:
  performance:
    rating: "low"
    details: "No performance impact; this is about correctness"
  maintainability:
    rating: "high"
    details: "Prevents CI failures and ensures pattern synchronization"
  testability:
    rating: "low"
    details: "Validated by actionlint and manual file existence checks"

prerequisites:
  - "Understanding of git pathspec patterns"
  - "Familiarity with .gitattributes line ending normalization"
  - "Knowledge of GitHub Actions workflow structure"

dependencies:
  packages: []
  skills:
    - "workflow-consistency"
    - "cross-platform-compatibility"

applies_to:
  languages:
    - "YAML"
  frameworks:
    - "GitHub Actions"
  versions:
    github-actions: "current"

aliases:
  - "git add renormalize"
  - "pathspec validation"
  - "line ending normalization"

related:
  - "workflow-consistency"
  - "cross-platform-compatibility"
  - "git-workflow-robustness"

status: "stable"
---

# Git Renormalize Pattern Validation

> **One-line summary**: Ensure `git add --renormalize` patterns match actual repository
> files to prevent CI failures from unmatched pathspecs.

## Overview

The `git add --renormalize` command applies `.gitattributes` normalization rules to files
matching specified patterns. This command **fails with exit code 128** when a pathspec
pattern matches zero files. This skill documents best practices for pattern validation
and synchronization across workflow sections.

## Solution

Apply these requirements when using `git add --renormalize` in CI:

1. **Validate every pattern** matches at least one file in the repository
1. **Remove patterns for non-existent file types** (e.g., `*.markdown` when only `*.md` exists)
1. **Synchronize patterns** between `git add --renormalize`, `file_pattern`, and path triggers
1. **Add missing patterns** for file types that are formatted (e.g., add YAML patterns if Prettier formats YAML)

## The Problem

When `git add --renormalize` is given a pattern that matches no files, it fails:

```bash
Run git add --renormalize -- '*.md' '*.markdown' '*.json'
fatal: pathspec '*.markdown' did not match any files
Error: Process completed with exit code 128.
```

This causes CI failures when:

1. **Pattern drift**: File extensions in workflow triggers diverge from actual repository contents
1. **Premature patterns**: Patterns are added for file types that don't yet exist
1. **Removed file types**: Previously-existing file types are removed but patterns remain

## Command Behavior

### How `git add --renormalize` Works

```bash
# Renormalize specific file types
git add --renormalize -- '*.md' '**/*.md'

# What happens:
# 1. Git finds all files matching the patterns
# 2. Re-applies .gitattributes rules (line endings, filters)
# 3. Stages changes if normalization differs from current state
# 4. FAILS if any pattern matches zero files
```

### Key Behaviors

| Scenario                     | Behavior                | Exit Code |
| ---------------------------- | ----------------------- | --------- |
| All patterns match files     | Renormalizes and stages | 0         |
| Any pattern matches no files | Fatal error             | 128       |
| Invalid pattern syntax       | Error message           | Non-zero  |
| No `.gitattributes` rules    | No changes              | 0         |

## Best Practices

### 1. Validate Patterns Match Actual Files

Before adding file patterns to workflows, verify they match repository contents:

```bash
# Check if any .markdown files exist
git ls-files '*.markdown' '**/*.markdown' | head -5

# Count files by extension
git ls-files | sed 's/.*\.//' | sort | uniq -c | sort -rn | head -20

# Find all unique extensions in repository
git ls-files | grep -E '\.[^/]+$' | sed 's/.*\./\./' | sort -u
```

### 2. Keep Workflow Triggers Synchronized

When workflows have path triggers, ensure patterns reflect actual file types:

```yaml
# GOOD: Only patterns that match existing files
on:
  pull_request:
    paths:
      - "**/*.md"      # Verified: 45 files exist
      - "**/*.json"    # Verified: 12 files exist
      - "**/*.yml"     # Verified: 8 files exist

# BAD: Patterns for non-existent file types
on:
  pull_request:
    paths:
      - "**/*.markdown"  # No .markdown files in repo
      - "**/*.asmref"    # No .asmref files in repo
```

### 3. Document Pattern Sources

Add comments explaining why patterns are included:

```yaml
paths:
  # Unity assembly definitions (Editor/, Runtime/, Tests/)
  - "**/*.asmdef"
  # Markdown documentation (docs/, README.md, CHANGELOG.md)
  - "**/*.md"
```

### 4. Periodic Validation

Create a validation script or CI job to detect pattern drift:

```bash
#!/bin/bash
# validate-workflow-patterns.sh

# Extract patterns from workflow file
patterns=$(grep -E '^\s+-\s+".*"' .github/workflows/prettier-autofix.yml | \
           sed 's/.*"\(.*\)".*/\1/' | \
           grep -E '^\*\*/')

# Check each pattern
for pattern in $patterns; do
  count=$(git ls-files "$pattern" 2>/dev/null | wc -l)
  if [ "$count" -eq 0 ]; then
    echo "WARNING: Pattern '$pattern' matches no files"
  fi
done
```

### 5. Use Consistent Pattern Syntax

Prefer consistent glob patterns across all workflow files:

```yaml
# Preferred: Double-star for recursive matching
- "**/*.md"
- "**/*.json"

# Also valid but less common
- "*.md" # Root directory only
- "docs/**/*.md" # Specific directory tree
```

## Common Mistakes

### Mistake 1: Assuming Errors on No Match

```yaml
# This step succeeds even if no .markdown files exist
- name: Renormalize markdown
  run: git add --renormalize -- '*.markdown' '**/*.markdown'
```

### Mistake 2: Pattern Case Sensitivity

```bash
# Linux filesystems are case-sensitive
git ls-files '*.MD'        # May find different files than
git ls-files '*.md'        # this pattern
```

### Mistake 3: Over-Broad Patterns

```yaml
# Dangerous: Stages ALL files
- run: git add --renormalize .

# Better: Specific file types
- run: git add --renormalize -- '*.md' '**/*.md' '*.json' '**/*.json'
```

## Integration with Workflow Consistency

This skill complements the [Workflow Consistency skill](./workflow-consistency.md) by focusing specifically on:

- Pattern validation for `git add --renormalize` commands
- Path trigger synchronization across duplicate workflow sections
- Detecting and removing patterns for non-existent file types

## Validation Checklist

Before merging workflow changes:

- [ ] All path patterns match at least one file (`git ls-files '<pattern>'`)
- [ ] Patterns are consistent between `pull_request` and `pull_request_target`
- [ ] No deprecated or removed file extensions are referenced
- [ ] Comments document the purpose of non-obvious patterns

## See Also

- [Workflow Consistency skill](./workflow-consistency.md) - General workflow structure requirements
- [Cross-Platform Compatibility skill](../scripting/cross-platform-compatibility.md) - Path handling across OSes
- [Git Workflow Robustness skill](../testing/git-workflow-robustness.md) - Git operations in CI
