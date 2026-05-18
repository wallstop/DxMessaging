---
title: "Jest Hook Robustness"
id: "jest-hook-robustness"
category: "scripting"
version: "1.0.0"
created: "2026-05-18"
updated: "2026-05-18"

source:
  repository: "Ambiguous-Interactive/DxMessaging"
  files:
    - path: "scripts/run-managed-jest.js"
    - path: "scripts/__tests__/run-managed-jest-no-injected-test-runner.test.js"
    - path: "scripts/__tests__/no-testrunner-injection-policy.test.js"
    - path: "scripts/doctor.js"
    - path: ".pre-commit-config.yaml"
  url: "https://github.com/Ambiguous-Interactive/DxMessaging"

tags:
  - "jest"
  - "pre-commit"
  - "pre-push"
  - "windows"
  - "cross-platform"
  - "tooling"

complexity:
  level: "intermediate"
  reasoning: "Requires understanding of Jest's internal module resolver, pre-commit hook execution, and Windows-specific failure modes"

impact:
  performance:
    rating: "none"
    details: "Hook reliability only; no runtime cost"
  maintainability:
    rating: "critical"
    details: "Prevents the recurring Windows-only pre-push failure that blocked development"
  testability:
    rating: "high"
    details: "Two regression tests (narrow source-scan plus broad policy) plus doctor diagnostics"

prerequisites:
  - "Familiarity with pre-commit hook configuration"
  - "Understanding of Jest 27+ test runner architecture"

dependencies:
  packages: []
  skills:
    - "cross-platform-compatibility"
    - "git-hook-performance"

applies_to:
  languages:
    - "JavaScript"
  frameworks:
    - "Jest"
    - "pre-commit"
  versions:
    jest: ">=27.0"
    node: ">=18.0"

aliases:
  - "Managed Jest"
  - "Pre-push Jest"
  - "testRunner option was not found"

related:
  - "cross-platform-compatibility"
  - "git-hook-performance"
  - "let-tools-resolve-modules"

status: "stable"
---

# Jest Hook Robustness

> **One-line summary**: The `scripts/run-managed-jest.js` wrapper must never inject
> `--testRunner <absolute-path>`. Jest 27+ resolves `jest-circus` natively, and
> absolute-path injection breaks jest-config's runner validator on Windows.

## Overview

Pre-push hooks that run Jest in this repository go through
`scripts/run-managed-jest.js`. The wrapper validates the local install and
falls back to an isolated install if needed, but it MUST forward the resolver
decision to Jest itself. The failure that motivated this skill was a Windows
pre-push log (`pre-push.txt`) reporting `Validation Error: testRunner option
was not found` because the wrapper passed an absolute path to `jest-circus`
that jest-config's internal validator rejected on Windows drive-letter paths.

## Solution

1. Treat the contract "no `--testRunner <abs-path>` injection in
   `run-managed-jest.js`" as load-bearing. Two regression tests pin it.
1. Before reporting a hook-adjacent change complete, run
   `npm run preflight:pre-push`.
1. On `testRunner option was not found` or any `jest-circus` resolution
   error, run `npm run doctor`; the wrapper self-heals once per run, and the
   doctor surfaces the manual repair commands when self-heal fails.

## The failure mode

Symptom (from `pre-push.txt`):

```text
Validation Error:
  testRunner option was not found.
  Make sure jest-circus is installed: https://www.npmjs.com/package/jest-circus
```

The wrapper had been passing an absolute path such as
`C:\Users\...\node_modules\jest-circus\build\runner.js` via `--testRunner`.
jest-config's internal resolver rejects that path on Windows because of how it
normalizes drive letters and slashes during runner validation. Jest 27+ defaults
to `jest-circus` and resolves the bundled runner via its own resolver walking
up from the Jest binary, which is more reliable than any caller-side
pre-resolution.

## The fix invariant

`scripts/run-managed-jest.js` MUST NOT inject `--testRunner`. The contract is
enforced by two tests:

- Narrow source-scan:
  `scripts/__tests__/run-managed-jest-no-injected-test-runner.test.js`
  greps the wrapper source for any internal `--testRunner` push and fails on
  match. If the caller's `args` array contains a user-supplied `--testRunner`,
  the wrapper forwards it unchanged (documented escape hatch).
- Broad policy:
  `scripts/__tests__/no-testrunner-injection-policy.test.js`
  scans every `scripts/*.js` and every hook entry in
  `.pre-commit-config.yaml` for `--testRunner` literals. Any new script or
  hook that pre-resolves a runner path fails the test.

Both tests run under the `script-parser-tests` pre-push hook.

## Agentic workflow

Before reporting done for any change that touches `.pre-commit-config.yaml`,
`scripts/run-managed-jest.js`, `scripts/validate-pre-commit-tooling.js`,
`scripts/validate-node-tooling.js`, `.github/workflows/*.yml`, or any file
gated by `script-parser-tests`, `script-tests`, or `unity-contract-tests`,
run:

```bash
npm run preflight:pre-push
```

For triage on a fresh clone or a flaky workstation:

```bash
npm run doctor
```

The doctor prints Node, npm, `jest-circus` install state, and the isolated
cache directory; it reports the manual repair commands listed below.

## Hook self-heal protocol

On `testRunner option was not found` or any `jest-circus` resolution failure:

1. `scripts/run-managed-jest.js` auto-retries once after clearing the
   isolated managed cache or rebuilding it. The decoder reads the failing
   stderr to decide which recovery to attempt.
1. If the retry fails, the wrapper banner prints the explicit repair
   commands that match the manual repair procedure below.
1. Manual repair (only after the wrapper has already failed twice). The first
   command is cross-platform (Linux, macOS, Windows CMD, Windows PowerShell);
   run them in order:
   1. `node -e "require('fs').rmSync(require('path').join(require('os').tmpdir(), 'dxmessaging-managed-jest'), { recursive: true, force: true })"`
   1. `npm ci`
   1. `npm run preflight:pre-push`
1. Never bypass with `git commit --no-verify` or `git push --no-verify`. The
   hook is the gate, not the obstacle.

## Cross-platform note

On Windows, prefer open-source PowerShell 7+ (`pwsh`); legacy `powershell` works
but is slower. Bash on macOS and Linux works directly. Internally,
`scripts/run-managed-jest.js` uses `spawnPlatformCommandSync` from
`scripts/lib/shell-command.js` so npm and Node shims resolve through the
Windows shell-shim rules consistently. See
[Cross-Platform Script Compatibility](./cross-platform-compatibility.md) for
the shared helper and case-sensitivity rules.

## Adding a new Jest-backed hook

1. Copy the `script-tests` (or `script-parser-tests`) block shape; both already
   invoke `node scripts/run-managed-jest.js`. Do not introduce a new wrapper.
1. Add the new test file path to the `--runTestsByPath` list and to the
   `files:` regex on the same hook entry.
1. `npm run preflight:pre-push` covers the new hook automatically via
   `pre-commit run --hook-stage pre-push --all-files`. Run it before reporting
   done.

## See Also

- [Cross-Platform Script Compatibility](./cross-platform-compatibility.md)
- [Git Hook Performance Budget](../performance/git-hook-performance.md)
- [Let Tools Resolve Modules](./let-tools-resolve-modules.md)

## Changelog

| Version | Date       | Changes                                         |
| ------- | ---------- | ----------------------------------------------- |
| 1.0.0   | 2026-05-18 | Initial version after the pre-push.txt failure. |
