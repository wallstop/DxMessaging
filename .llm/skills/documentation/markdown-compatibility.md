---
title: "Markdown Compatibility Guidelines"
id: "markdown-compatibility"
category: "documentation"
version: "1.0.0"
created: "2026-01-29"
updated: "2026-01-29"

source:
  repository: "wallstop/DxMessaging"
  files:
    - path: "docs/"
    - path: "README.md"
  url: "https://github.com/wallstop/DxMessaging"

tags:
  - "documentation"
  - "markdown"
  - "compatibility"
  - "mkdocs"
  - "portability"

complexity:
  level: "basic"
  reasoning: "Simple syntax substitutions for cross-platform compatibility"

impact:
  performance:
    rating: "none"
    details: "Documentation only"
  maintainability:
    rating: "high"
    details: "Ensures docs render correctly in all viewers without maintenance burden"
  testability:
    rating: "none"
    details: "Documentation does not affect testability"

prerequisites:
  - "Understanding of Markdown"

dependencies:
  packages: []
  skills:
    - "documentation-style-guide"
    - "documentation-updates"

applies_to:
  languages:
    - "Markdown"
  frameworks:
    - "MkDocs"
    - "GitHub"
    - "VS Code"

aliases:
  - "Portable markdown"
  - "Cross-platform docs"
  - "MkDocs alternatives"

related:
  - "documentation-style-guide"
  - "documentation-updates"

status: "stable"
---

# Markdown Compatibility Guidelines

> **One-line summary**: Use only standard markdown syntax that renders correctly in GitHub, VS Code, and MkDocs simultaneously.

## Overview

This project's documentation must render correctly across multiple platforms:

- **GitHub's markdown viewer** (README, docs in repo)
- **VS Code markdown preview** (local development)
- **Standard markdown renderers** (third-party tools, npm packages)
- **MkDocs** (docs site build)

Using MkDocs-specific syntax breaks rendering in standard viewers, creating a poor experience for users who browse documentation on GitHub or in their editor.

## Problem Statement

MkDocs and its Material theme provide powerful extensions like admonitions, tabs, and buttons. However, these extensions use non-standard syntax that renders as broken text in standard markdown viewers:

```markdown
!!! warning "Caution"
This will not render correctly on GitHub.
```

On GitHub, this displays as literal text: `!!! warning "Caution"` followed by an indented paragraph—not as a styled warning box.

## Solution

Use only standard markdown syntax with Unicode emoji for visual emphasis. This ensures consistent rendering across all platforms.

---

## Forbidden Syntax and Alternatives

### 1. Admonitions

Admonitions (callout boxes) are a common MkDocs extension that use `!!!` syntax.

#### ❌ Forbidden: MkDocs Admonitions

```markdown
!!! note "Important"
This is a note admonition that only renders in MkDocs.

!!! warning "Caution"
This warning will appear as broken text on GitHub.

!!! danger "Critical"
This danger callout is MkDocs-specific.

!!! tip "Pro Tip"
This tip syntax is not standard markdown.

!!! info
Admonitions without titles are also forbidden.
```

#### ✅ Correct: Blockquotes with Emoji

```markdown
> ℹ️ **Note**: This is a note that renders everywhere.

> ⚠️ **Caution**: This warning displays correctly on all platforms.

> 🚨 **Critical**: This danger callout works universally.

> 💡 **Tip**: This tip renders in any markdown viewer.

> 📝 **Info**: Use descriptive emoji to convey meaning.
```

**Emoji reference for common admonition types**:

| Type     | Emoji | Example                  |
| -------- | ----- | ------------------------ |
| Note     | ℹ️    | `> ℹ️ **Note**: ...`     |
| Warning  | ⚠️    | `> ⚠️ **Warning**: ...`  |
| Danger   | 🚨    | `> 🚨 **Danger**: ...`   |
| Tip      | 💡    | `> 💡 **Tip**: ...`      |
| Info     | 📝    | `> 📝 **Info**: ...`     |
| Success  | ✅    | `> ✅ **Success**: ...`  |
| Error    | ❌    | `> ❌ **Error**: ...`    |
| Example  | 📌    | `> 📌 **Example**: ...`  |
| See Also | 🔗    | `> 🔗 **See Also**: ...` |

---

### 2. Collapsible Admonitions

MkDocs supports collapsible sections with `???` syntax.

#### ❌ Forbidden: MkDocs Collapsibles

```markdown
??? note "Click to expand"
This content is hidden by default in MkDocs.
It appears as broken syntax elsewhere.

???+ warning "Expanded by default"
The plus makes it expanded initially.
```

#### ✅ Correct: Use Details/Summary HTML

```markdown
<details>
<summary>Click to expand</summary>

This content is hidden by default and works in GitHub and VS Code.

Note: Leave a blank line after `<summary>` for proper markdown rendering inside.

</details>
```

Or simply use a regular section if collapsibility is not essential:

```markdown
### Additional Details

This content is always visible, which is often clearer for users.
```

---

### 3. Content Tabs

MkDocs Material provides tabbed content with `===` syntax.

#### ❌ Forbidden: MkDocs Tabs

```markdown
=== "Python"
`python
    print("Hello")
    `

=== "JavaScript"
`javascript
    console.log("Hello");
    `

=== "C#"
`csharp
    Console.WriteLine("Hello");
    `
```

#### ✅ Correct: Use Headers

````markdown
### Python

```python
print("Hello")
```

### JavaScript

```javascript
console.log("Hello");
```

### C#

```csharp
Console.WriteLine("Hello");
```
````

For installation instructions or platform-specific content, headers provide clear navigation and work universally.

---

### 4. Button Attributes

MkDocs Material supports styled buttons via attribute syntax.

#### ❌ Forbidden: Button Attributes

```markdown
[Get Started](getting-started.md){ .md-button }

[Download](https://example.com/download){ .md-button .md-button--primary }

[:fontawesome-brands-github: View on GitHub](https://github.com/example){ .md-button }
```

#### ✅ Correct: Standard Links

```markdown
[Get Started](getting-started.md)

[Download](https://example.com/download)

[View on GitHub](https://github.com/example)
```

If you need visual emphasis, use bold or place links prominently:

```markdown
**[Get Started →](getting-started.md)**
```

---

### 5. Emoji Shortcodes

MkDocs uses `:emoji_name:` shortcode syntax from the `pymdownx.emoji` extension.

#### ❌ Forbidden: Emoji Shortcodes

```markdown
:warning: This is a warning.

:rocket: Fast performance!

:white_check_mark: Test passed.

:fontawesome-brands-github: GitHub integration.

:material-code-braces: Code example.
```

> ⚠️ **Note**: Material for MkDocs provides icon shortcodes that must also be avoided:
>
> - `:material-*:` patterns (e.g., `:material-code-braces:`, `:material-check:`)
> - `:octicons-*:` patterns (e.g., `:octicons-git-branch-16:`)
> - `:fontawesome-*:` patterns (e.g., `:fontawesome-brands-github:`)
> - `:simple-*:` patterns (e.g., `:simple-python:`)
>
> These render as literal text in standard markdown viewers.

#### ✅ Correct: Unicode Emoji Directly

```markdown
⚠️ This is a warning.

🚀 Fast performance!

✅ Test passed.

GitHub integration (use text, not icon).

Code example (describe with words).
```

**Common emoji substitutions**:

| Shortcode              | Unicode | Copy-paste |
| ---------------------- | ------- | ---------- |
| `:warning:`            | ⚠️      | ⚠️         |
| `:rocket:`             | 🚀      | 🚀         |
| `:white_check_mark:`   | ✅      | ✅         |
| `:x:`                  | ❌      | ❌         |
| `:bulb:`               | 💡      | 💡         |
| `:information_source:` | ℹ️      | ℹ️         |
| `:fire:`               | 🔥      | 🔥         |
| `:star:`               | ⭐      | ⭐         |
| `:book:`               | 📖      | 📖         |
| `:wrench:`             | 🔧      | 🔧         |

---

### 6. Other MkDocs-Specific Syntax

#### ❌ Forbidden: Annotations

```markdown
Some code (1)
{ .annotate }

1.  This is an annotation that appears on hover.
```

#### ❌ Forbidden: Keys Extension

```markdown
Press ++ctrl+alt+del++ to restart.
```

#### ❌ Forbidden: Critic Markup

```markdown
{--deleted text--}
{++inserted text++}
{~~old~>new~~}
```

#### ✅ Correct: Use Plain Descriptions

```markdown
Some code <!-- Explanation in a comment or below -->

Press `Ctrl+Alt+Del` to restart.

Changed "old text" to "new text".
```

---

## Quick Reference

| Feature     | MkDocs Syntax               | Standard Alternative               |
| ----------- | --------------------------- | ---------------------------------- |
| Note        | `!!! note`                  | `> ℹ️ **Note**: ...`               |
| Warning     | `!!! warning`               | `> ⚠️ **Warning**: ...`            |
| Danger      | `!!! danger`                | `> 🚨 **Danger**: ...`             |
| Tip         | `!!! tip`                   | `> 💡 **Tip**: ...`                |
| Collapsible | `??? note`                  | `<details><summary>...</summary>`  |
| Tabs        | `=== "Tab"`                 | `### Tab` headers                  |
| Button      | `[text](url){ .md-button }` | `[text](url)` or `**[text](url)**` |
| Emoji       | `:emoji:`                   | Unicode emoji: ⚠️ 🚀 ✅            |

---

## Validation

Before committing documentation changes:

1. **Preview in VS Code**: Use `Ctrl+Shift+V` to open markdown preview
1. **Check on GitHub**: View the file in GitHub's web interface after pushing
1. **Search for forbidden patterns**: `grep -E '^(!!!|\?\?\?|===)' docs/`

### Automated Check (Optional)

Add to your review process:

```bash
# Find MkDocs-specific syntax in markdown files
grep -rn --include='*.md' -E '^(!!!|\?\?\?|===)' docs/
grep -rn --include='*.md' '{ *\.md-button' docs/
grep -rn --include='*.md' ':[a-z_]+:' docs/ | grep -v 'https://'
```

---

## Why This Matters

1. **GitHub is the primary documentation surface**: Most users first encounter docs in the repository, not the built site.

1. **Editor preview during development**: Contributors use VS Code preview while editing; broken syntax slows them down.

1. **Third-party integrations**: Tools like npm, documentation aggregators, and AI assistants read raw markdown.

1. **Graceful degradation**: Even if MkDocs adds features, the docs remain readable everywhere.

1. **Reduced maintenance burden**: No need to maintain two versions or worry about platform-specific rendering bugs.

---

## See Also

- [Documentation Style Guide](documentation-style-guide.md)
- [Documentation Updates](documentation-updates.md)

## References

- [CommonMark Specification](https://spec.commonmark.org/)
- [GitHub Flavored Markdown](https://github.github.com/gfm/)
- [MkDocs Material Extensions](https://squidfunk.github.io/mkdocs-material/reference/)

## Changelog

| Version | Date       | Changes                                             |
| ------- | ---------- | --------------------------------------------------- |
| 1.0.0   | 2026-01-29 | Initial version with forbidden syntax documentation |
