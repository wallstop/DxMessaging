# Skills Repository Specification

This document defines the structure, schema, and tooling for storing code patterns (skills) extracted from repositories.

---

## 1. Directory Structure

```text
.llm/
├── skills/
│   ├── specification.md          # This file - the spec
│   ├── index.md                  # Auto-generated index of all skills
│   ├── templates/                # Skill templates
│   │   └── skill-template.md
│   │
│   ├── performance/              # Performance optimization patterns
│   │   ├── object-pooling.md
│   │   ├── cache-strategies.md
│   │   └── allocation-reduction.md
│   │
│   ├── testing/                  # Testing patterns and practices
│   │   ├── unity-test-patterns.md
│   │   ├── mock-strategies.md
│   │   └── assertion-patterns.md
│   │
│   ├── solid/                    # SOLID principles implementations
│   │   ├── dependency-injection.md
│   │   ├── interface-segregation.md
│   │   └── single-responsibility.md
│   │
│   ├── messaging/                # Messaging and event patterns
│   │   ├── pub-sub-patterns.md
│   │   ├── message-routing.md
│   │   └── broadcast-strategies.md
│   │
│   ├── unity/                    # Unity-specific patterns
│   │   ├── lifecycle-management.md
│   │   ├── component-patterns.md
│   │   └── editor-extensions.md
│   │
│   ├── concurrency/              # Threading and async patterns
│   │   ├── thread-safety.md
│   │   ├── lock-free-patterns.md
│   │   └── async-patterns.md
│   │
│   ├── architecture/             # Architectural patterns
│   │   ├── service-locator.md
│   │   ├── factory-patterns.md
│   │   └── repository-pattern.md
│   │
│   ├── error-handling/           # Error handling strategies
│   │   ├── exception-patterns.md
│   │   ├── result-types.md
│   │   └── defensive-coding.md
│   │
│   ├── code-generation/          # Source generation patterns
│   │   ├── roslyn-analyzers.md
│   │   ├── source-generators.md
│   │   └── emit-patterns.md
│   │
│   └── documentation/            # Documentation and code comments
│       └── documentation-updates.md
```

---

## 2. YAML Frontmatter Schema

Every skill file MUST include the following YAML frontmatter:

```yaml
---
# Required Fields
title: "Human-readable skill title"
id: "unique-kebab-case-identifier"
category: "performance|testing|solid|messaging|unity|concurrency|architecture|error-handling|code-generation|documentation"
version: "1.0.0"
created: "2026-01-21"
updated: "2026-01-21"

# Source Information
source:
  repository: "owner/repo-name"
  files:
    - path: "relative/path/to/file.cs"
      lines: "10-50" # Optional: specific line range
      commit: "abc123" # Optional: specific commit hash
    - path: "another/file.cs"
  url: "https://github.com/owner/repo" # Optional: direct URL

# Classification
tags:
  - "primary-tag"
  - "secondary-tag"
  - "technology-tag"

complexity:
  level: "basic|intermediate|advanced|expert"
  reasoning: "Brief explanation of complexity rating"

# Impact Assessment
impact:
  performance:
    rating: "none|low|medium|high|critical"
    details: "Brief description of performance implications"
  maintainability:
    rating: "none|low|medium|high|critical"
    details: "Brief description of maintainability implications"
  testability:
    rating: "none|low|medium|high|critical"
    details: "Brief description of testability implications"

# Dependencies and Prerequisites
prerequisites:
  - "Required knowledge or skill"
  - "Another prerequisite"

dependencies:
  packages:
    - "Package.Name >= 1.0.0"
  skills:
    - "related-skill-id"

# Applicability
applies_to:
  languages:
    - "C#"
    - "JavaScript"
  frameworks:
    - "Unity"
    - ".NET"
  versions:
    unity: ">=2021.3"
    dotnet: ">=netstandard2.0"

# Optional metadata
aliases:
  - "Alternative name 1"
  - "Alternative name 2"

related:
  - "related-skill-id-1"
  - "related-skill-id-2"

status: "draft|review|stable|deprecated"
---
```

### Field Definitions

| Field                           | Type   | Required | Description                                                          |
| ------------------------------- | ------ | -------- | -------------------------------------------------------------------- |
| `title`                         | string | ✅       | Human-readable title for the skill                                   |
| `id`                            | string | ✅       | Unique kebab-case identifier (must match filename without extension) |
| `category`                      | enum   | ✅       | Primary category (must match parent folder name)                     |
| `version`                       | semver | ✅       | Semantic version of this skill document                              |
| `created`                       | date   | ✅       | ISO 8601 date when skill was first documented                        |
| `updated`                       | date   | ✅       | ISO 8601 date when skill was last modified                           |
| `source.repository`             | string | ✅       | Source repository in `owner/repo` format                             |
| `source.files`                  | array  | ✅       | Array of source file references                                      |
| `source.files[].path`           | string | ✅       | Relative path within source repository                               |
| `source.files[].lines`          | string | ❌       | Line range in format `start-end`                                     |
| `source.files[].commit`         | string | ❌       | Git commit SHA for version pinning                                   |
| `source.url`                    | string | ❌       | Direct URL to repository or file                                     |
| `tags`                          | array  | ✅       | Array of descriptive tags for discovery                              |
| `complexity.level`              | enum   | ✅       | One of: basic, intermediate, advanced, expert                        |
| `complexity.reasoning`          | string | ❌       | Explanation of complexity rating                                     |
| `impact.performance.rating`     | enum   | ✅       | Performance impact rating                                            |
| `impact.performance.details`    | string | ❌       | Performance impact explanation                                       |
| `impact.maintainability.rating` | enum   | ✅       | Maintainability impact rating                                        |
| `impact.testability.rating`     | enum   | ✅       | Testability impact rating                                            |
| `prerequisites`                 | array  | ❌       | Required knowledge or skills                                         |
| `dependencies.packages`         | array  | ❌       | Required NuGet/npm packages                                          |
| `dependencies.skills`           | array  | ❌       | Related skill IDs that should be learned first                       |
| `applies_to.languages`          | array  | ✅       | Programming languages this applies to                                |
| `applies_to.frameworks`         | array  | ❌       | Frameworks this applies to                                           |
| `applies_to.versions`           | object | ❌       | Version constraints                                                  |
| `aliases`                       | array  | ❌       | Alternative names for search                                         |
| `related`                       | array  | ❌       | IDs of related skills                                                |
| `status`                        | enum   | ✅       | Document status: draft, review, stable, deprecated                   |

---

## 3. Markdown Body Format

After the frontmatter, skills follow this structure:

````markdown
---
[YAML Frontmatter]
---

# {title}

> **One-line summary**: Brief description of what this pattern accomplishes.

## Overview

2-4 paragraphs explaining:

- What problem this pattern solves
- When to use it
- Key benefits and trade-offs

## Problem Statement

Describe the specific problem this pattern addresses. Include:

- Common symptoms of the problem
- Why naive solutions don't work
- Real-world scenarios where this matters

## Solution

### Core Concept

Explain the fundamental idea behind the solution.

### Implementation

```csharp
// Primary code example with detailed comments
// This should be the canonical implementation
```

### Step-by-Step Breakdown

1. **Step 1**: Explanation
1. **Step 2**: Explanation
1. **Step 3**: Explanation

## Variations

### Variation A: {Name}

Brief description and code example for a common variation.

```csharp
// Variation code
```

### Variation B: {Name}

Brief description and code example for another variation.

## Usage Examples

### Example 1: {Scenario Name}

```csharp
// Complete, runnable example
```

### Example 2: {Another Scenario}

```csharp
// Another complete example
```

## Anti-Patterns

### ❌ Don't Do This

```csharp
// Example of what NOT to do
```

**Why it's wrong**: Explanation of the problems with this approach.

### ❌ Another Anti-Pattern

```csharp
// Another bad example
```

## Testing Considerations

- How to unit test code using this pattern
- Mocking strategies
- Edge cases to cover

```csharp
// Test example
```

## Performance Notes

- Benchmarking results (if applicable)
- Memory allocation characteristics
- CPU usage patterns
- When performance matters vs. when it doesn't

## Migration Guide

If replacing an existing pattern:

1. Step-by-step migration instructions
1. Common pitfalls during migration
1. Rollback strategy

## See Also

- [Related Skill 1](../category/related-skill-1.md)
- [Related Skill 2](../category/related-skill-2.md)
- [External Resource](https://example.com)

## References

- Original source: [Link to source]
- Related documentation: [Link]
- Further reading: [Link]

## Changelog

| Version | Date       | Changes         |
| ------- | ---------- | --------------- |
| 1.0.0   | 2026-01-21 | Initial version |
````

---

## 4. Example Complete Skill File

See [the skill template](./templates/skill-template.md) for the template.

See the example skill file: [Object Pooling](./performance/object-pooling.md)

---

## 5. Index Generation

The index is auto-generated by scripts:

```bash
node scripts/generate-skills-index.js
```

To validate the index without modifying files:

```bash
node scripts/generate-skills-index.js --check
```

The generator formats the index via Prettier and normalizes CRLF/no-BOM output.

### Line Endings

All `.llm/` markdown files (including `index.md`) must use CRLF and no UTF-8 BOM.
If you generate or edit skills on non-Windows environments, normalize line endings:

```bash
node scripts/fix-eol.js .llm/skills/index.md
```

### Index Format

The generated `index.md` contains:

- Table of contents by category
- Full skill listing with metadata
- Tag cloud for discovery
- Statistics summary

---

## 6. Git Hooks

Install the pre-commit hook to auto-regenerate the index:

```bash
# From repository root
cp scripts/hooks/pre-commit .git/hooks/pre-commit
chmod +x .git/hooks/pre-commit
```

---

## 7. Validation

Skills are validated for:

1. Required frontmatter fields present
1. `id` matches filename
1. `category` matches parent folder
1. All referenced files exist (when source is local)
1. All `dependencies.skills` reference valid skill IDs
1. Proper markdown structure
1. **File size limits** (see below)

Run validation:

```bash
node scripts/validate-skills.js
```

---

## 8. File Size Requirements

All skill files and `context.md` must adhere to line count limits:

| Range         | Status       | Action                                     |
| ------------- | ------------ | ------------------------------------------ |
| < 200 lines   | 📝 Short     | Consider adding more examples or detail    |
| 200-350 lines | ✅ **Ideal** | Target range for all skill files           |
| 351-500 lines | ⚠️ Warning   | Consider splitting into focused sub-skills |
| > 500 lines   | ❌ **Error** | Must split; blocks CI and pre-commit       |

### Why These Limits?

- **LLM context efficiency**: Smaller, focused files fit better in context windows
- **Maintainability**: Easier to review, update, and keep accurate
- **Discoverability**: Focused topics are easier to find and reference
- **Cognitive load**: Both humans and LLMs handle focused content better

### When to Split

Split a skill file when:

1. Line count exceeds 350 lines
1. Multiple distinct variations could stand alone
1. Different complexity levels (basic vs. advanced)
1. Different platform targets (Unity vs. general .NET)

### How to Split

1. Identify the core concept and each major variation
1. Create separate files: `{concept}.md`, `{concept}-{variation}.md`
1. Use `related` frontmatter to link related skills
1. Add `## See Also` sections for cross-references

See the [Skill File Sizing skill](./documentation/skill-file-sizing.md) for detailed guidance.

---

## 9. Contributing New Skills

1. Copy `templates/skill-template.md` to the appropriate category folder
1. Rename to `{skill-id}.md` (kebab-case)
1. Fill in all required frontmatter fields
1. Write the skill content following the body format
1. Run validation
1. Commit (index auto-regenerates via hook)

---

## Category Definitions

| Category          | Description                            | Example Skills                                |
| ----------------- | -------------------------------------- | --------------------------------------------- |
| `performance`     | Optimization patterns for speed/memory | Object pooling, caching, allocation reduction |
| `testing`         | Testing strategies and patterns        | Mocking, assertions, test organization        |
| `solid`           | SOLID principle implementations        | DI, ISP, SRP patterns                         |
| `messaging`       | Event and messaging patterns           | Pub/sub, routing, broadcasting                |
| `unity`           | Unity-specific patterns                | Lifecycle, components, editor tools           |
| `concurrency`     | Threading and async patterns           | Thread safety, locks, async/await             |
| `architecture`    | Structural design patterns             | Factories, repositories, services             |
| `error-handling`  | Error and exception patterns           | Result types, defensive coding                |
| `code-generation` | Metaprogramming patterns               | Roslyn, source generators                     |
| `documentation`   | Documentation patterns and maintenance | Changelog management, API docs, code comments |
