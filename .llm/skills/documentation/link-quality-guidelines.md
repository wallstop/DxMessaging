---
title: "Link Quality and External URL Management"
id: "link-quality-guidelines"
category: "documentation"
version: "1.2.0"
created: "2026-01-22"
updated: "2026-01-23"

source:
  repository: "wallstop/DxMessaging"
  files:
    - path: "Docs/"
    - path: "README.md"
    - path: ".llm/"
    - path: ".github/workflows/"
  url: "https://github.com/wallstop/DxMessaging"

tags:
  - "documentation"
  - "links"
  - "urls"
  - "markdown"
  - "ci-cd"
  - "quality"
  - "accessibility"
  - "github-actions"
  - "linting"
  - "testing"

complexity:
  level: "basic"
  reasoning: "Link quality follows clear patterns but requires attention to detail and verification"

impact:
  performance:
    rating: "none"
    details: "Documentation links do not affect runtime performance"
  maintainability:
    rating: "high"
    details: "Broken or unclear links degrade documentation quality and user experience"
  testability:
    rating: "low"
    details: "Link validation can be automated but is not part of core test suite"

prerequisites:
  - "Understanding of Markdown link syntax"
  - "Familiarity with GitHub repository structure"
  - "Basic knowledge of GitHub Actions"

dependencies:
  packages: []
  skills:
    - "documentation-updates"

applies_to:
  languages:
    - "Markdown"
    - "YAML"
  frameworks:
    - "GitHub Actions"
  versions:
    unity: ">=2021.3"
    dotnet: ">=netstandard2.0"

aliases:
  - "URL management"
  - "Link validation"
  - "Markdown links"
  - "External references"

related:
  - "documentation-updates"
  - "changelog-management"
  - "documentation-style-guide"

status: "stable"
---

# Link Quality and External URL Management

> **One-line summary**: Ensure all links use human-readable text, point to correct URLs, and remain valid over time.

## Overview

Links in documentation serve two purposes: navigation and context. Poor link quality—whether through cryptic text, incorrect URLs, or broken references—damages user trust and wastes developer time investigating CI failures.

This skill covers:

- Writing human-readable link text
- Ensuring repository URL consistency in skill files
- Validating external links before committing
- Keeping GitHub Action versions consistent

## Problem Statement

Link-related issues cause preventable CI/CD failures and documentation quality problems:

| Issue Type                     | Impact                                         | Example                                                     |
| ------------------------------ | ---------------------------------------------- | ----------------------------------------------------------- |
| Non-descriptive link text      | Poor accessibility, confusing navigation       | `[README.md](../README.md)` vs `[the README](../README.md)` |
| Incorrect repository URLs      | Broken skill file validation, wrong references | Using wrong org/repo in frontmatter                         |
| Broken external URLs           | 404 errors, outdated documentation references  | Linking to deprecated Unity docs pages                      |
| Workflow version inconsistency | Unpredictable CI behavior, security issues     | Mixing `actions/checkout@v3` and `actions/checkout@v4`      |

## Solution

### Human-Readable Link Text

**Never use raw file names as link text.** Link text should describe what the user will find, not the file name.

#### Anti-patterns

```text
<!-- BAD: Raw file names as link text -->

See [README.md](../README.md) for installation instructions.
Check [CHANGELOG.md](../CHANGELOG.md) for version history.
Read [context.md](.llm/context.md) for guidelines.
```

#### Correct Patterns

```text
<!-- GOOD: Descriptive link text -->

See [the README](../README.md) for installation instructions.
Check [the changelog](../CHANGELOG.md) for version history.
Read [the AI Agent Guidelines](.llm/context.md) for guidelines.
```

#### Link Text Guidelines

| Scenario               | Bad Example                    | Good Example                                |
| ---------------------- | ------------------------------ | ------------------------------------------- |
| File reference         | `[package.json](package.json)` | `[the package manifest](package.json)`      |
| Section reference      | `\[FAQ.md\](Docs/FAQ.md)`      | `[frequently asked questions](Docs/FAQ.md)` |
| Code location          | `[Tests/](Tests/)`             | `[the test suite](Tests/)`                  |
| External documentation | `[docs.unity3d.com](url)`      | `[Unity documentation](url)`                |
| GitHub repository      | `[repo](url)`                  | `[the DxMessaging repository](url)`         |

#### Accessibility Considerations

Screen readers announce link text. Users should understand the destination without additional context:

```markdown
<!-- BAD: "Click here" pattern -->

For more information, click [here](../Docs/Advanced.md).

<!-- GOOD: Descriptive and accessible -->

For more information, see [advanced usage patterns](../Docs/Advanced.md).
```

### Repository URL Consistency

Skill files include repository metadata in the YAML frontmatter. These URLs must match the actual repository.

#### Frontmatter URL Fields

```yaml
source:
  repository: "wallstop/DxMessaging" # Format: "owner/repo"
  url: "https://github.com/wallstop/DxMessaging" # Full HTTPS URL
```

#### Common Mistakes

| Mistake               | Incorrect Value                             | Correct Value                             |
| --------------------- | ------------------------------------------- | ----------------------------------------- |
| Wrong organization    | `wallstop-studios/DxMessaging`              | `wallstop/DxMessaging`                    |
| Wrong repository name | `wallstop/com.wallstop-studios.dxmessaging` | `wallstop/DxMessaging`                    |
| Missing `https://`    | `github.com/wallstop/DxMessaging`           | `https://github.com/wallstop/DxMessaging` |
| Trailing slash        | `https://github.com/wallstop/DxMessaging/`  | `https://github.com/wallstop/DxMessaging` |
| SSH URL format        | `git@github.com:wallstop/DxMessaging.git`   | `https://github.com/wallstop/DxMessaging` |

#### Verification Steps

1. **Check the remote URL**: Run `git remote get-url origin` to confirm the actual repository
1. **Visit the URL**: Before committing, open the URL in a browser to verify it resolves
1. **Compare with existing files**: Check other skill files for consistent formatting

### External Link Validation

External URLs can break without warning. Follow these practices to minimize broken link risk.

#### Before Adding External Links

1. **Verify the URL loads**: Open in a browser, check for redirects
1. **Prefer stable URLs**: Use versioned documentation when available
1. **Check for canonical URLs**: Some sites redirect to preferred formats

#### Unity Documentation URLs

Unity documentation frequently reorganizes. Use current URL patterns:

```markdown
<!-- Current Unity documentation format -->

https://docs.unity3d.com/Manual/PageName.html
https://docs.unity3d.com/ScriptReference/ClassName.html

<!-- Versioned documentation (more stable) -->

https://docs.unity3d.com/2021.3/Documentation/Manual/PageName.html
```

#### External Link Checklist

| Check                       | Action                                               |
| --------------------------- | ---------------------------------------------------- |
| URL returns 200             | Open in browser, verify no 404/redirect chain        |
| Content matches expectation | Confirm the page contains the referenced information |
| URL is HTTPS                | Avoid HTTP links; use HTTPS for security             |
| No URL shorteners           | Use full URLs, not bit.ly or similar                 |
| Versioned when possible     | Prefer `/v1.2.3/` over `/latest/` for stability      |

#### High-Risk External Domains

Some domains change URL structures frequently. Extra verification is needed:

- Unity documentation (`docs.unity3d.com`) - Check version-specific URLs
- Microsoft documentation (`docs.microsoft.com`, `learn.microsoft.com`) - Reorganized in 2023
- Stack Overflow - Answers can be deleted; quote key information

### External URL Fragment Validation

URL fragments (the `#section-name` portion after the main URL) are particularly fragile for external links. The target page's heading structure can change without notice, breaking fragment references.

#### Common Fragment Issues

| Issue                          | Example                                                   | Risk                                                        |
| ------------------------------ | --------------------------------------------------------- | ----------------------------------------------------------- |
| Incorrect fragment guessing    | `#links-1` instead of `#links`                            | CI failure when lychee validates with `--include-fragments` |
| Heading renumbered             | `#step-3-install` becomes `#step-4-install`               | Silent breakage, confusing users                            |
| Heading text changed           | `#getting-started` becomes `#quick-start`                 | 404-like behavior on the page                               |
| Auto-generated fragment suffix | `#links` vs `#links-1` (duplicate heading disambiguation) | Wrong section targeted                                      |

#### Fragment Validation Process

1. **Navigate to the exact URL**: Open `https://example.com/page#fragment` in a browser
1. **Verify scroll position**: Confirm the page scrolls to the expected section
1. **Inspect the heading ID**: Right-click the heading → Inspect → Check the `id` attribute
1. **Test with link checker**: Run `lychee --include-fragments "URL"` locally

#### Fragment ID Discovery

Different sites generate fragment IDs differently:

```bash
# GitHub generates IDs from heading text (lowercase, hyphens for spaces)
## Getting Started → #getting-started

# Some sites use custom IDs
<h2 id="quick-start">Getting Started</h2> → #quick-start

# Duplicate headings get suffixes
## Links → #links
## Links → #links-1  (second occurrence)
```

#### Best Practices for Fragment URLs

- **Prefer linking without fragments** when the page is short enough to scan
- **Quote key information** rather than relying solely on the fragment link
- **Use versioned documentation** when fragments must remain stable
- **Add comments** near fragile external links explaining what they reference

```markdown
<!-- Good: Includes context in case fragment breaks -->

See the [Markdown links syntax](https://www.markdownguide.org/basic-syntax/#links)
section for details on inline and reference-style links.

<!-- Risky: Fragment-only reference with no context -->

See [here](https://www.markdownguide.org/basic-syntax/#links).
```

### GitHub Actions Version Consistency

Workflow files should use consistent action versions across all workflows.

#### Version Format Standards

```yaml
# GOOD: Use the same major version consistently
- uses: actions/checkout@v4
- uses: actions/setup-dotnet@v4
- uses: actions/upload-artifact@v4

# BAD: Mixed versions across workflows
- uses: actions/checkout@v3 # One workflow
- uses: actions/checkout@v4 # Another workflow
```

#### Version Update Process

1. **Audit all workflows**: Find all action uses across `.github/workflows/`
1. **Identify inconsistencies**: List actions with different versions
1. **Update together**: Change all instances in a single PR
1. **Test thoroughly**: Run all affected workflows before merging

#### Common Actions to Monitor

| Action                      | Current Recommended | Notes                              |
| --------------------------- | ------------------- | ---------------------------------- |
| `actions/checkout`          | `v4`                | Breaking changes from v3           |
| `actions/setup-dotnet`      | `v4`                | .NET SDK setup                     |
| `actions/upload-artifact`   | `v4`                | Breaking changes from v3           |
| `actions/download-artifact` | `v4`                | Must match upload-artifact version |
| `actions/cache`             | `v4`                | Caching for dependencies           |

#### Audit Command

```bash
# Find all action versions in workflows
grep -rh "uses:" .github/workflows/ | sort | uniq
```

## Documentation Linting Scripts

Automated link validation prevents broken links from reaching production. However, these scripts require careful implementation and testing.

### Linting Scripts Must Skip Code Blocks

Documentation linters that check for raw file names or other patterns **must skip content inside code blocks**:

- **Fenced code blocks**: Content between ` ``` ` markers
- **Inline code**: Content between single backticks

Without this, examples showing anti-patterns will trigger false positives:

```markdown
<!-- This anti-pattern example would trigger a linter without code block handling -->

Bad: `See [README.md](../README.md)` <- Inline code, should be skipped
```

### Linting Scripts Need Unit Tests

Documentation linting scripts are code and need tests like any other code:

| Test Category        | Examples                                      |
| -------------------- | --------------------------------------------- |
| Normal patterns      | Valid links with good text                    |
| Anti-patterns        | Raw file names that should be flagged         |
| Edge cases           | Inline code, fenced blocks, nested structures |
| False positive cases | Anti-pattern examples in documentation        |
| Boundary conditions  | Empty files, single-line files, no links      |

Without comprehensive tests, regressions can cause CI failures on valid documentation.

### Script Implementation Checklist

When creating or modifying documentation linters:

- [ ] Skip content in fenced code blocks (` ``` `)
- [ ] Skip content in inline code (backticks)
- [ ] Handle nested structures correctly
- [ ] Include unit tests covering edge cases
- [ ] Test against existing documentation files
- [ ] Document expected behavior in script comments

## Validation Checklist

Before committing documentation or skill files:

- [ ] All link text is human-readable (no raw file names)
- [ ] Repository URLs in frontmatter match `git remote get-url origin`
- [ ] External URLs have been visited and return 200
- [ ] External URL fragments have been verified to scroll to the correct section
- [ ] GitHub Action versions are consistent across all workflows
- [ ] No `http://` links (use `https://` instead)
- [ ] No URL shorteners or tracking parameters
- [ ] Documentation linting scripts have adequate test coverage

## See Also

- [Documentation Updates](documentation-updates.md)
- [Changelog Management](changelog-management.md)
- [Documentation Style Guide](documentation-style-guide.md)

## References

- [Markdown Guide - Links](https://www.markdownguide.org/basic-syntax/#links)
- [WebAIM - Links and Hypertext](https://webaim.org/techniques/hypertext/)
- [GitHub Actions - Using Actions](https://docs.github.com/en/actions/using-workflows/workflow-syntax-for-github-actions#jobsjob_idstepsuses)

## Changelog

| Version | Date       | Changes                                                                     |
| ------- | ---------- | --------------------------------------------------------------------------- |
| 1.2.0   | 2026-01-23 | Added external URL fragment validation section based on CI failure analysis |
| 1.1.0   | 2026-01-22 | Added documentation linting scripts section with testing guidance           |
| 1.0.0   | 2026-01-22 | Initial version covering link quality fundamentals                          |
