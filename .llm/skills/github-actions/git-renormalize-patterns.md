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

1. **Use per-extension loops** (REQUIRED) - Process each extension separately with existence checks
1. **Validate every pattern** matches at least one file in the repository
1. **Remove patterns for non-existent file types** (e.g., `*.markdown` when only `*.md` exists)
1. **Synchronize patterns** between `git add --renormalize`, `file_pattern`, and path triggers
1. **Add missing patterns** for file types that are formatted (e.g., add YAML patterns if Prettier formats YAML)

### Required Pattern: Per-Extension Loop

**ALWAYS** use this pattern when renormalizing multiple file types:

```bash
# REQUIRED: Per-extension loop with existence check
# yaml excluded: dotfiles match git ls-files but not git add globs
for ext in cs md json asmdef yml; do
  if git ls-files "*.$ext" "**/*.$ext" | grep -q .; then
    git add --renormalize -- "*.$ext" "**/*.$ext"
  fi
done
```

This pattern:

- Processes each extension individually
- Checks if files exist before attempting to renormalize
- Prevents "pathspec did not match" failures (exit code 128)
- Works correctly even when some file types are missing

### Forbidden Patterns

**NEVER** use single-line multi-pattern commands:

```bash
# FORBIDDEN: Single command with multiple patterns
# Fails with exit code 128 if ANY pattern matches no files
git add --renormalize -- '*.md' '**/*.md' '*.json' '**/*.json' '*.yml' '**/*.yml'

# FORBIDDEN: Unguarded multiple extensions
git add --renormalize -- '*.md' '*.markdown' '*.json'  # Fails if no .markdown files
```

## The Problem

When `git add --renormalize` is given a pattern that matches no files, it fails:

```bash
Run git add --renormalize -- '*.md' '*.markdown' '*.json'
fatal: pathspec '*.markdown' did not match any files
Error: Process completed with exit code 128.
```

### Dotfile Glob Behavior Difference

**CRITICAL**: `git ls-files` and `git add` handle dotfiles differently:

```bash
# git ls-files MATCHES dotfiles
git ls-files "*.yaml"              # Returns: .pre-commit-config.yaml

# git add does NOT match dotfiles with globs
git add --renormalize -- "*.yaml"  # FAILS: pathspec did not match any files
```

This causes a subtle failure mode: the existence check passes (because `git ls-files`
finds the dotfile), but the actual `git add --renormalize` fails (because it doesn't
match dotfiles with glob patterns).

**Solution**: Exclude `yaml` from extension loops since `.yaml` files in most repositories
are dotfiles (e.g., `.pre-commit-config.yaml`). Use `yml` extension for non-dotfile YAML files.

**Generalized rule**: Exclude any extension from `git add --renormalize` loops when ALL
tracked files of that extension are dotfiles (files whose names start with `.`). To verify:

```bash
# Check if ALL files of an extension are dotfiles
git ls-files "*.$ext" "**/*.$ext" | while read f; do
  basename "$f" | grep -q '^\.' || echo "non-dotfile: $f"
done
# If no output, all files are dotfiles → exclude this extension
```

```bash
# CORRECT: Exclude yaml, dotfiles won't cause issues
for ext in md json asmdef yml; do

# WRONG: yaml causes failures when only dotfiles exist
for ext in md json asmdef yml yaml; do
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

### Mistake 1: Assuming Success on No Match

```yaml
# This step fails (exit code 128) if no .markdown files exist
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

# Better: Use per-extension loop pattern (see Required Pattern above)
- run: |
    for ext in md json; do
      if git ls-files "*.$ext" "**/*.$ext" | grep -q .; then
        git add --renormalize -- "*.$ext" "**/*.$ext"
      fi
    done
```

## Integration with Workflow Consistency

This skill complements the [Workflow Consistency skill](./workflow-consistency.md) by focusing specifically on:

- Pattern validation for `git add --renormalize` commands
- Path trigger synchronization across duplicate workflow sections
- Detecting and removing patterns for non-existent file types

## Validation Checklist

Before merging workflow changes:

- [ ] Uses per-extension loop pattern with existence checks (REQUIRED)
- [ ] All path patterns match at least one file (`git ls-files '<pattern>'`)
- [ ] Patterns are consistent between `pull_request` and `pull_request_target`
- [ ] No deprecated or removed file extensions are referenced
- [ ] Comments document the purpose of non-obvious patterns

## Verification

### Manual Verification

Test your renormalize patterns locally before committing:

```bash
# Verify file types exist
# yaml excluded: dotfiles match git ls-files but not git add globs
for ext in cs md json asmdef yml; do
  count=$(git ls-files "*.$ext" "**/*.$ext" | wc -l)
  echo "$ext: $count files"
done

# Test the full loop pattern (dry run)
# yaml excluded: dotfiles match git ls-files but not git add globs
for ext in cs md json asmdef yml; do
  if git ls-files "*.$ext" "**/*.$ext" | grep -q .; then
    echo "Would renormalize: *.$ext"
  else
    echo "SKIP (no files): *.$ext"
  fi
done
```

### Automated Validation

Run the workflow validation script to detect problematic patterns:

```bash
node scripts/validate-workflows.js
```

This script scans all workflow files for:

- Single-line multi-pattern `git add --renormalize` commands (FORBIDDEN)
- Missing existence checks before renormalize commands
- Patterns that may not match any files

### CI Integration

The validation script runs as part of the CI pipeline (via the actionlint workflow) to catch
problematic patterns before they cause failures. Note: This validation does not run in
pre-commit hooks—only in CI.

## See Also

- [Workflow Consistency skill](./workflow-consistency.md) - General workflow structure requirements
- [Cross-Platform Compatibility skill](../scripting/cross-platform-compatibility.md) - Path handling across OSes
- [Git Workflow Robustness skill](../testing/git-workflow-robustness.md) - Git operations in CI
