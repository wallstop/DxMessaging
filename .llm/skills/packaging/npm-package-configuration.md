---
title: npm Package Configuration
id: npm-package-configuration
category: packaging
description: Best practices for configuring package.json "files" and .npmignore to create clean, minimal npm packages
version: 1.1.0
created: 2026-02-01
updated: 2026-02-01
date: 2026-02-01
author: AI Assistant
status: stable
complexity:
  level: intermediate
impact:
  performance:
    rating: low
related:
  - ../documentation/changelog-management.md
  - ../scripting/cross-platform-compatibility.md
tags:
  - npm
  - packaging
  - configuration
  - files
  - npmignore
  - unity
---

# npm Package Configuration

## Overview

This skill documents best practices for configuring npm package contents using `package.json` "files" field and `.npmignore`. Understanding how these mechanisms interact prevents bloated packages or missing files.

## Solution

npm uses two complementary mechanisms:

1. **`package.json` "files" field**: An **allowlist** specifying what TO include
1. **`.npmignore`**: An **exclusion list** for files within allowed directories

```text
All Repository Files → "files" Allowlist → .npmignore Exclusions → Final Package
```

**Key insight:** Items not matched by "files" are already excluded. However, `.npmignore` remains valuable for excluding subdirectories within included paths, handling complex patterns, and providing defense-in-depth.

## Best Practices for "files" Field

### Use Specific Patterns

For complex directory structures, use specific glob patterns rather than broad wildcards:

```json
{
  "files": [
    "Editor/**",
    "Runtime/**",
    "Samples~/**",
    "SourceGenerators/Directory.Build.props",
    "SourceGenerators/WallstopStudios.DxMessaging.SourceGenerators/*.cs",
    "SourceGenerators/WallstopStudios.DxMessaging.SourceGenerators/*.csproj",
    "CHANGELOG.md",
    "LICENSE.md",
    "README.md",
    "Third Party Notices.md"
  ]
}
```

This pattern includes only specific files within `SourceGenerators/` rather than the entire directory, avoiding the need to exclude test subdirectories.

### Avoid Negated Patterns

**Never use negated patterns (!) in the "files" field.** Use `.npmignore` for exclusions instead:

```json
// WRONG: Mixing inclusion and exclusion
{ "files": ["Runtime/", "!Runtime/**/*.Tests.cs"] }

// CORRECT: Pure allowlist + .npmignore
{ "files": ["Runtime/"] }
// .npmignore: Runtime/**/*.Tests.cs
```

### Always-Included Files

npm always includes `package.json`, `README`, `LICENSE`, and `CHANGELOG` regardless of configuration.

## When .npmignore IS Needed

While "files" handles most exclusions, `.npmignore` is valuable for:

### Subdirectories Within Included Paths

When "files" uses broad patterns like `SourceGenerators/**`, you need `.npmignore` to exclude test directories:

```text
SourceGenerators/WallstopStudios.DxMessaging.SourceGenerators.Tests/
```

### Defense-in-Depth and Build Artifacts

For complex "files" patterns, `.npmignore` provides a safety net and handles build artifacts:

```text
# Safety: exclude development files even if "files" patterns change
**/bin/
**/obj/
**/.vs/
**/*.pdb
```

## Unity Package Specifics

Unity packages require special handling for `.meta` files.

### Include .meta Files for Package Structure

Unity needs `.meta` files for directories and files that ARE included:

```json
{
  "files": ["Editor/**", "Runtime/**", "Editor.meta", "Runtime.meta", "package.json.meta"]
}
```

### Exclude .meta Files for Excluded Items

When excluding a directory via `.npmignore`, also exclude its `.meta` file:

```text
# Exclude test project AND its meta file
SourceGenerators/WallstopStudios.DxMessaging.SourceGenerators.Tests/
SourceGenerators/WallstopStudios.DxMessaging.SourceGenerators.Tests.meta

# Exclude Tests directory AND its meta file
Tests/
Tests.meta
```

### Pattern: Directory + Meta Exclusion

Always pair directory exclusions with their `.meta` companion:

```text
scripts/
scripts.meta
site/
site.meta
```

## .npmignore Best Practices

### Include Explanatory Header

```text
# npm package exclusions
#
# This file works with package.json "files" to control packaging:
# - "files" is an ALLOWLIST of what to include
# - This file EXCLUDES items from within those allowed directories
#
# To verify: npm pack --dry-run
```

### Organize with Sections and Document Intent

```text
# =============================================================================
# Build Artifacts
# =============================================================================
**/.vs/
**/bin/
**/obj/

# =============================================================================
# SourceGenerator Exclusions
# =============================================================================
SourceGenerators/WallstopStudios.DxMessaging.SourceGenerators.Tests/
SourceGenerators/WallstopStudios.DxMessaging.SourceGenerators.Tests.meta
```

Some entries may be technically redundant if "files" already excludes them, but provide value as documentation, safety net against "files" changes, and audit clarity.

## Common Mistakes

### Mistake 1: Copying Entire .gitignore

`.npmignore` should not mirror `.gitignore`. Most `.gitignore` entries are for files outside the "files" allowlist anyway.

### Mistake 2: Forgetting Unity .meta Files

When excluding directories, always exclude companion `.meta` files to prevent orphaned metadata.

### Mistake 3: Using Broad Wildcards Without Exclusions

If using `SourceGenerators/**` in "files", you MUST add `.npmignore` entries for test subdirectories.

## Verification

### Always Run npm pack --dry-run

```bash
npm pack --dry-run
```

### Verify Specific Exclusions

```bash
# Verify Tests/ is NOT included
npm pack --dry-run 2>&1 | grep "Tests/" || echo "Tests/ correctly excluded"

# Verify .meta files for excluded dirs are not included
npm pack --dry-run 2>&1 | grep "Tests\.meta" || echo "Tests.meta correctly excluded"
```

## Complete Example (This Project)

### package.json "files"

```json
{
  "files": [
    "Editor/**",
    "Runtime/**",
    "Samples~/**",
    "SourceGenerators/Directory.Build.props",
    "SourceGenerators/Directory.Build.props.meta",
    "SourceGenerators/WallstopStudios.DxMessaging.SourceGenerators/*.cs",
    "SourceGenerators/WallstopStudios.DxMessaging.SourceGenerators/*.cs.meta",
    "SourceGenerators/WallstopStudios.DxMessaging.SourceGenerators/*.csproj",
    "SourceGenerators/WallstopStudios.DxMessaging.SourceGenerators/*.csproj.meta",
    "SourceGenerators/WallstopStudios.DxMessaging.SourceGenerators.meta",
    "CHANGELOG.md",
    "CHANGELOG.md.meta",
    "LICENSE.md",
    "LICENSE.md.meta",
    "README.md",
    "README.md.meta",
    "Third Party Notices.md",
    "Third Party Notices.md.meta",
    "package.json.meta",
    "Editor.meta",
    "Runtime.meta",
    "SourceGenerators.meta"
  ]
}
```

### .npmignore

```text
# npm package exclusions for com.wallstop-studios.dxmessaging
#
# This file works with package.json "files" to control packaging.
# To verify: npm pack --dry-run

# Build artifacts
**/.vs/
**/bin/
**/obj/
**/*.pdb

# SourceGenerator test project
SourceGenerators/WallstopStudios.DxMessaging.SourceGenerators.Tests/
SourceGenerators/WallstopStudios.DxMessaging.SourceGenerators.Tests.meta

# Tests (defense-in-depth)
Tests/
Tests.meta

# Development files
scripts/
scripts.meta
.github/
.llm/
```

## Summary

| Mechanism    | Purpose    | Use For                                    |
| ------------ | ---------- | ------------------------------------------ |
| `"files"`    | Allowlist  | Directories/files TO include               |
| `.npmignore` | Exclusions | Subdirs, build artifacts, defense-in-depth |

### Key principles

1. Use "files" as a pure allowlist—no negated patterns
1. Use specific patterns for complex structures (e.g., `SourceGenerators/Foo/*.cs`)
1. Use `.npmignore` for subdirectory exclusions and defense-in-depth
1. For Unity: include `.meta` files for included items, exclude for excluded items
1. Always verify with `npm pack --dry-run`

## See Also

- [npm documentation: files](https://docs.npmjs.com/cli/v10/configuring-npm/package-json#files)
- [npm documentation: .npmignore](https://docs.npmjs.com/cli/v10/using-npm/developers#keeping-files-out-of-your-package)
