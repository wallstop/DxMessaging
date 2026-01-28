---
title: "Cross-Platform Script Compatibility"
id: "cross-platform-compatibility"
category: "scripting"
version: "1.0.0"
created: "2026-01-28"
updated: "2026-01-28"

source:
  repository: "wallstop/DxMessaging"
  files:
    - path: "scripts/"
    - path: ".github/workflows/"
  url: "https://github.com/wallstop/DxMessaging"

tags:
  - "cross-platform"
  - "case-sensitivity"
  - "testing"
  - "powershell"
  - "javascript"
  - "ci-cd"
  - "linux"
  - "windows"
  - "macos"

complexity:
  level: "intermediate"
  reasoning: "Requires understanding of filesystem differences across platforms"

impact:
  performance:
    rating: "none"
    details: "Script patterns only; no runtime performance impact"
  maintainability:
    rating: "critical"
    details: "Cross-platform issues cause CI failures that work locally"
  testability:
    rating: "high"
    details: "Proper testing catches platform-specific issues before CI"

prerequisites:
  - "Understanding of filesystem case sensitivity"
  - "Familiarity with Jest testing framework"
  - "Knowledge of CI/CD pipeline concepts"

dependencies:
  packages: []
  skills:
    - "shell-best-practices"
    - "powershell-best-practices"

applies_to:
  languages:
    - "PowerShell"
    - "JavaScript"
    - "Bash"
  frameworks:
    - "Jest"
    - "GitHub Actions"
  versions:
    node: ">=18.0"
    powershell: ">=5.1"

aliases:
  - "Case sensitivity"
  - "Platform compatibility"
  - "Script testing"

related:
  - "shell-best-practices"
  - "powershell-best-practices"
  - "comprehensive-test-coverage"

status: "stable"
---

# Cross-Platform Script Compatibility

> **One-line summary**: Ensure scripts work correctly across Windows, macOS, and Linux by handling
> case-sensitive paths and maintaining comprehensive test coverage.

## Overview

Scripts that work locally on Windows or macOS often fail in CI/CD environments running Linux.
The most common cause is **filename case sensitivity**—Linux filesystems are case-sensitive while
Windows and macOS are case-insensitive by default. This skill documents patterns to prevent these
issues and ensure all scripts have proper test coverage.

## Solution

1. **Verify exact file path case** before committing using `git ls-files` or `ls -la`
1. **Test in case-sensitive environments** (Docker, WSL, or Linux) before pushing
1. **Derive paths from source of truth** instead of hardcoding filenames
1. **Maintain test coverage** for all scripts (see [Script Test Coverage](../testing/script-test-coverage.md))
1. **Add case validation in CI** to catch issues automatically

## Filename Case Sensitivity

### The Problem

Linux filesystems (ext4, XFS, etc.) distinguish between `File.txt` and `file.txt` as completely
different files. Windows (NTFS) and macOS (APFS) treat them as the same file.

#### Real-World Example from PR #144

```powershell
# BROKEN: Wrong case for the actual file
$bannerPath = Join-Path $repoRoot "docs/images/dxmessaging-banner.svg"
# Actual filename: DxMessaging-banner.svg

# This works on Windows/macOS but fails on Linux CI!
```

The script used `dxmessaging-banner.svg` but the actual file was `DxMessaging-banner.svg`.
This passed all local tests but failed in the GitHub Actions CI environment.

### Prevention Strategies

#### 1. Verify Exact Case Before Committing

Always verify the exact case of files referenced in scripts:

```bash
# Use git to find the canonical name
git ls-files | grep -i banner

# Use ls to verify exact case
ls -la docs/images/

# Use tab-completion to get exact case
```

#### 2. Use Variables from Source of Truth

Instead of hardcoding filenames, derive them from existing known-good sources:

```powershell
# BETTER: Derive from a source of truth
$files = Get-ChildItem -Path $imagesDir -Filter "*banner*"
if ($files.Count -eq 1) {
    $bannerPath = $files[0].FullName
}

# OR: Store the canonical name in configuration
$config = Get-Content "scripts/config.json" | ConvertFrom-Json
$bannerPath = Join-Path $repoRoot $config.bannerPath
```

#### 3. Add Case Validation in CI

Create a CI check that validates case consistency:

```bash
#!/bin/bash
# Validate that all script file references match actual case

# Find all file path strings in PowerShell scripts
grep -roh '"[^"]*\.[a-z]*"' scripts/*.ps1 | sort -u | while read -r path; do
    # Strip quotes and check if file exists with exact case
    clean_path="${path//\"/}"
    if [[ -e "$clean_path" ]]; then
        actual=$(ls -d "$clean_path" 2>/dev/null)
        if [[ "$clean_path" != "$actual" ]]; then
            echo "Case mismatch: script uses '$clean_path' but actual is '$actual'"
            exit 1
        fi
    fi
done
```

### Cross-Platform Path Comparison Table

| Platform       | Filesystem | Case-Sensitive | CI Environment |
| -------------- | ---------- | -------------- | -------------- |
| Linux          | ext4, XFS  | Yes            | GitHub Actions |
| macOS          | APFS       | No (default)   | Local dev      |
| Windows        | NTFS       | No             | Local dev      |
| WSL            | ext4       | Yes            | Local dev      |
| Docker (Linux) | ext4       | Yes            | Local CI       |

### Testing for Case Sensitivity Issues

Run your scripts in a case-sensitive environment before committing:

```bash
# Option 1: Use Docker with Linux
docker run --rm -v "$PWD:/workspace" -w /workspace mcr.microsoft.com/powershell:latest \
    pwsh -File scripts/sync-banner-version.ps1

# Option 2: Use WSL on Windows
wsl pwsh -File scripts/sync-banner-version.ps1

# Option 3: Create a case-sensitive volume on macOS
# (requires disk utility to create APFS case-sensitive volume)
```

## Validation Checklist

Before merging scripts:

### Case Sensitivity

- [ ] All file path references verified against actual filesystem
- [ ] Tested in case-sensitive environment (Linux, Docker, or WSL)
- [ ] File extensions use correct case (`.svg` not `.SVG`)
- [ ] Directory names use correct case (`docs/images/` not `Docs/Images/`)

### Test Coverage

- [ ] Test coverage exists for all scripts (see [Script Test Coverage](../testing/script-test-coverage.md))

## See Also

- [Script Test Coverage](../testing/script-test-coverage.md) - Test coverage requirements for scripts
- [Shell Best Practices](./shell-best-practices.md) - Shell-specific case sensitivity patterns
- [PowerShell Best Practices](./powershell-best-practices.md) - PowerShell scripting patterns
- [Comprehensive Test Coverage](../testing/comprehensive-test-coverage.md) - General test coverage requirements

## Changelog

| Version | Date       | Changes                               |
| ------- | ---------- | ------------------------------------- |
| 1.0.0   | 2026-01-28 | Initial version from PR #144 feedback |
