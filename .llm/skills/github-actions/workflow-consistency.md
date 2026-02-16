---
title: "GitHub Actions Workflow Consistency"
id: "workflow-consistency"
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
  - "ci-cd"
  - "workflow"
  - "security"
  - "consistency"
  - "yaml"

complexity:
  level: "intermediate"
  reasoning: "Requires understanding of GitHub Actions features and security best practices"

impact:
  performance:
    rating: "medium"
    details: "Concurrency controls prevent resource waste from duplicate runs"
  maintainability:
    rating: "high"
    details: "Consistent structure makes workflows easier to review and modify"
  testability:
    rating: "medium"
    details: "Proper path filters ensure workflows run when needed"

prerequisites:
  - "Understanding of GitHub Actions workflow syntax"
  - "Familiarity with YAML formatting"
  - "Knowledge of security best practices for CI/CD"

dependencies:
  packages: []
  skills:
    - "cross-platform-compatibility"

applies_to:
  languages:
    - "YAML"
  frameworks:
    - "GitHub Actions"
  versions:
    github-actions: "current"

aliases:
  - "Workflow standards"
  - "CI/CD consistency"
  - "Actions best practices"

related:
  - "cross-platform-compatibility"
  - "shell-best-practices"
  - "git-renormalize-patterns"

status: "stable"
---

# GitHub Actions Workflow Consistency

> **One-line summary**: Ensure all GitHub Actions workflows follow consistent structure,
> security practices, and formatting to maintain reliability and reviewability.

## Overview

GitHub Actions workflows in this project must follow strict conventions for structure,
security, and formatting. This skill documents the required patterns to ensure all workflows
are consistent, secure, and maintainable.

## Solution

Apply these requirements to every workflow file:

1. Use consistent property ordering: `name` → `on` → `concurrency` → `permissions` → `jobs`
1. Always include a concurrency group with `cancel-in-progress: true`
1. Declare explicit minimal permissions
1. Set `timeout-minutes` on every job
1. Use `persist-credentials: false` on checkout steps (unless pushing)
1. Include `.github/workflows/**` in path filters for self-referential workflows
1. Use double quotes for strings (Prettier default)

## Required Property Order

All workflow files MUST use this exact property ordering at the top level:

```yaml
name: Workflow Name

on:
  # triggers

concurrency:
  # concurrency settings

permissions:
  # permission declarations

jobs:
  # job definitions
```

### Why Order Matters

1. **Readability**: Consistent ordering makes workflows scannable
1. **Review efficiency**: Reviewers know where to find specific sections
1. **Prettier compatibility**: Maintains formatting after auto-formatting

## Required Elements

Every workflow MUST include these elements:

### 1. Concurrency Group

Prevents duplicate workflow runs and cancels in-progress runs when new commits are pushed:

```yaml
concurrency:
  group: ${{ github.workflow }}-${{ github.ref }}
  cancel-in-progress: true
```

### 2. Explicit Permissions

Always declare the minimum required permissions. Default to read-only:

```yaml
# Read-only access (most workflows)
permissions:
  contents: read

# For workflows that push changes
permissions:
  contents: write
  pull-requests: write
```

**Never omit permissions**—implicit permissions are overly broad.

### 3. Job Timeout

Every job MUST have a `timeout-minutes` to prevent runaway jobs:

```yaml
jobs:
  build:
    runs-on: ubuntu-latest
    timeout-minutes: 10 # Required on every job
```

Recommended timeouts:

- **Lint/format checks**: 5 minutes
- **Build/compile**: 15-30 minutes
- **Full test suites**: 30-60 minutes
- **Deployment**: 10-15 minutes

### 4. Secure Checkout

Use `persist-credentials: false` on checkout steps unless credentials are explicitly needed:

```yaml
- name: Checkout
  uses: actions/checkout@v6
  with:
    persist-credentials: false
```

Only omit this when the workflow needs to push commits (e.g., auto-fix workflows).

## Path Filters

### Self-Referential Workflows

Workflows that check formatting or linting of specific file types MUST include
`.github/workflows/**` in their path filters. This ensures the workflow runs when
its own definition changes.

```yaml
on:
  pull_request:
    paths:
      - "**/*.yml"
      - "**/*.yaml"
      - ".github/workflows/**" # Critical: include workflow files
      - ".prettierrc*"
      - "package.json"
```

### Common Path Filter Patterns

| Workflow Type | Required Paths                                                                    |
| ------------- | --------------------------------------------------------------------------------- |
| YAML lint     | `**/*.yml`, `**/*.yaml`, `.github/workflows/**`, `.yamllint.yaml`, `.prettierrc*` |
| Markdown lint | `**/*.md`, `**/*.markdown`, `.markdownlint*`, `package.json`                      |
| JSON format   | `**/*.json`, `**/*.asmdef`, `**/*.asmref`, `.prettierrc*`                         |
| C# build      | `**/*.cs`, `**/*.csproj`, `**/*.sln`, `Directory.Build.props`                     |
| Tests         | Source paths + test paths + workflow config                                       |

### Trigger Best Practices

Include both `pull_request` and `push` triggers for validation workflows:

```yaml
on:
  pull_request:
    paths:
      # file patterns
  push:
    branches:
      - main
      - master
    paths:
      # same file patterns as pull_request
  workflow_dispatch: # Allow manual triggering
```

## Formatting Requirements

### Quote Style

Use **double quotes** for all strings (matches Prettier YAML defaults):

```yaml
# Correct
node-version: "20"
cache: "npm"

# Incorrect
node-version: '20'
cache: 'npm'
```

### Indentation

Use **2 spaces** for YAML indentation (not 4):

```yaml
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - name: Checkout
        uses: actions/checkout@v6
```

## Complete Workflow Template

```yaml
name: Example Workflow

on:
  pull_request:
    paths:
      - "**/*.ext"
      - ".github/workflows/**"
      - "config-file"
  push:
    branches:
      - main
      - master
    paths:
      - "**/*.ext"
      - ".github/workflows/**"
      - "config-file"
  workflow_dispatch:

concurrency:
  group: ${{ github.workflow }}-${{ github.ref }}
  cancel-in-progress: true

permissions:
  contents: read

jobs:
  example-job:
    name: Descriptive job name
    runs-on: ubuntu-latest
    timeout-minutes: 10
    steps:
      - name: Checkout
        uses: actions/checkout@v6
        with:
          persist-credentials: false

      - name: Setup Node
        uses: actions/setup-node@v6
        with:
          node-version: "20"
          cache: "npm"
          cache-dependency-path: package.json

      - name: Install dependencies
        run: |
          if [ -f package-lock.json ]; then
            npm ci
          else
            npm i --no-audit --no-fund
          fi

      - name: Run checks
        run: npm run check
```

## Common Mistakes

| Mistake                                 | Problem                                      | Fix                                               |
| --------------------------------------- | -------------------------------------------- | ------------------------------------------------- |
| Missing `.github/workflows/**` in paths | Workflow won't run when its own file changes | Add to path filters                               |
| Missing concurrency group               | Duplicate runs waste resources               | Add concurrency block                             |
| Missing permissions block               | Implicit permissions are too broad           | Declare explicit minimal permissions              |
| Missing `persist-credentials: false`    | Git credentials persist unnecessarily        | Add to checkout step                              |
| Missing `timeout-minutes`               | Jobs can run indefinitely                    | Add timeout to every job                          |
| Single quotes for strings               | Inconsistent with Prettier                   | Use double quotes                                 |
| Wrong property order                    | Hard to review, fails formatting             | Use: name → on → concurrency → permissions → jobs |

## Validation Checklist

Before committing a workflow, verify:

- [ ] Properties ordered: `name` → `on` → `concurrency` → `permissions` → `jobs`
- [ ] Concurrency group defined with `cancel-in-progress: true`
- [ ] Explicit `permissions` block with minimal required permissions
- [ ] Every job has `timeout-minutes`
- [ ] Checkout steps use `persist-credentials: false` (unless pushing)
- [ ] Path filters include `.github/workflows/**` for self-referential checks
- [ ] Double quotes used for strings
- [ ] 2-space indentation throughout

## See Also

- [Git Renormalize Pattern Validation](./git-renormalize-patterns.md) — ensuring pathspec patterns
  match actual repository files to prevent CI failures
- [Cross-Platform Compatibility](../scripting/cross-platform-compatibility.md) — handling platform
  differences in CI scripts
- [Shell Best Practices](../scripting/shell-best-practices.md) — patterns for shell commands in
  workflow steps
