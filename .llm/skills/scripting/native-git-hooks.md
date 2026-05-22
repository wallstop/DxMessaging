---
title: "Native Git Hook Bootstrap"
id: "native-git-hooks"
category: "scripting"
version: "1.0.0"
created: "2026-05-22"
updated: "2026-05-22"

source:
  repository: "Ambiguous-Interactive/DxMessaging"
  files:
    - path: "scripts/hooks/"
    - path: "scripts/install-git-hooks.js"
  url: "https://github.com/Ambiguous-Interactive/DxMessaging"

tags:
  - "git-hooks"
  - "cross-platform"
  - "automation"
  - "pre-push"

complexity:
  level: "intermediate"
  reasoning: "Requires native Git hook behavior plus cross-platform Node and npm shim handling"

impact:
  performance:
    rating: "low"
    details: "Runs only at Git hook boundaries and delegates to existing preflight commands"
  maintainability:
    rating: "high"
    details: "Makes hook installation versioned and automatic instead of a local manual step"
  testability:
    rating: "high"
    details: "The native hook wrapper and installer are covered by Jest contract tests"

prerequisites:
  - "Git installed locally"
  - "Node installed locally"

dependencies:
  packages: []
  skills:
    - "cross-platform-compatibility"
    - "integrity-gate-robustness"

applies_to:
  languages:
    - "JavaScript"
  frameworks:
    - "Git"
  versions:
    node: ">=18.0"

aliases:
  - "core.hooksPath"
  - "pre-push hook"
  - "hook bootstrap"

related:
  - "cross-platform-compatibility"
  - "integrity-gate-robustness"
  - "jest-hook-robustness"

status: "stable"
---

# Native Git Hook Bootstrap

> **One-line summary**: Keep Git hooks versioned under `scripts/hooks`, install them automatically with `core.hooksPath`, and make pre-push run the same preflight agents should run before hooks fire.

## Overview

The repository uses `pre-commit` for hook orchestration, but the Git hook entrypoints themselves are versioned under `scripts/hooks`. `scripts/install-git-hooks.js` sets local `core.hooksPath` to that directory during `postinstall`, so contributors and agents do not need a manual hook install step.

The native hook wrappers are intentionally small Node scripts. They avoid Bash, PowerShell, Husky, and shell-string composition at the Git boundary. Substantive cross-platform automation can still use PowerShell 7+ when that is the right tool, but the hook bootstrap must only require Git, Node, npm, and the repository files.

## Rules

1. Put native Git hook entrypoints in `scripts/hooks` with extensionless names (`pre-commit`, `pre-push`).
1. Use `#!/usr/bin/env node` and argument-array process launches.
1. Route npm and npx through `spawnPlatformCommandSync` so Windows shims do not depend on ad hoc shell behavior.
1. Keep dependency repair automatic. Managed Node tools should run the integrity gate and recover with `npm ci` when safe.
1. `pre-push` must run `node scripts/repair-node-tooling.js`, `npm run doctor`, then `npm run preflight:pre-push`.
1. `postinstall` may warn, but must not make `npm install` fatal when hook installation cannot run outside a Git worktree.

## Agent Workflow

For hook-gated tooling, workflow, script, or `.llm` edits:

1. Run focused tests while editing.
1. Run `npm run repair:node-tooling`.
1. Run `npm run doctor`.
1. Run `npm run preflight:pre-push`.
1. Treat the Git hook as a final backstop only.

## Verification

- `node scripts/run-managed-jest.js --runTestsByPath scripts/__tests__/native-git-hooks.test.js`
- `npm run doctor`
- `npm run preflight:pre-push`

## See Also

- [Cross-Platform Script Compatibility](./cross-platform-compatibility.md)
- [Integrity Gate Robustness](./integrity-gate-robustness.md)
- [Jest Hook Robustness](./jest-hook-robustness.md)
