---
title: "Changelog Entry Writing and Anti-Patterns Part 1"
id: "changelog-entry-writing-part-1"
category: "documentation"
version: "1.0.0"
created: "2026-01-22"
updated: "2026-03-16"
status: "stable"
tags:
  - migration
  - split
complexity:
  level: "intermediate"
impact:
  performance:
    rating: "low"
---

## Overview

Continuation extracted from `changelog-entry-writing.md` to keep files within the repository line-budget policy.

## Solution

```markdown
### Bad (vague)

## [2.1.4] - 2026-01-15

### Fixed

- Fixed a bug (added after release when user reported it wasn't in notes)
```

**Why it's wrong**: Changelog should be updated during development, not retroactively.

## See Also

- [Changelog Management](changelog-management.md)
- [Changelog Release Workflow](changelog-release-workflow.md)
- [Documentation Updates](documentation-updates.md)

## References

- [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)

## Changelog

| Version | Date       | Changes         |
| ------- | ---------- | --------------- |
| 1.0.0   | 2026-01-22 | Initial version |

## Related Links

- [Changelog Entry Writing and Anti-Patterns](./changelog-entry-writing.md)
