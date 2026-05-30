---
title: "Jest Hook Robustness"
id: "jest-hook-robustness"
category: "scripting"
version: "1.3.0"
created: "2026-05-18"
updated: "2026-05-30"

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
   error, run `npm run repair:node-tooling` (the bootstrap auto-heals the
   isolated cache) then `npm run doctor`. The doctor is read-only and reports a
   corrupt regenerable cache as WARN (never a hard FAIL); it never prints a
   manual `rm`.

## The failure mode

Symptom (from `pre-push.txt`):

```text
Validation Error:
  testRunner option was not found.
  Make sure jest-circus is installed: https://www.npmjs.com/package/jest-circus
```

The wrapper had been passing an absolute path such as
`C:\Users\...\node_modules\jest-circus\build\runner.js` via `--testRunner`.
jest-config's internal resolver rejects that path on Windows because of
how it normalizes drive letters and slashes. Jest 27+ defaults to
`jest-circus` and resolves the bundled runner via its own resolver,
which is more reliable than any caller-side pre-resolution.

## Partial-extract failure class

The Windows `testRunner option was not found` error has a second root cause
that the `--testRunner`-injection-avoidance contract alone does not fix:
the repository's `node_modules/jest-circus/build/runner.js` (and similar
critical files) can be missing or zero-byte on disk after a partial
extract. The integrity gate
(`scripts/lib/node-modules-integrity.js`) closes that loop by probing
the on-disk critical-file list BEFORE Jest is invoked and calling
`npm ci` when a partial extract is detected. See
[Integrity Gate Robustness](./integrity-gate-robustness.md) for the
full state diagram, refusal rules, env-var matrix, and the path-classifier
dispatch that decides between `npm ci` and isolated cache reset.

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

Before reporting done for any change that touches
`.pre-commit-config.yaml`, `scripts/run-managed-jest.js`,
`scripts/validate-pre-commit-tooling.js`,
`scripts/validate-node-tooling.js`, `.github/workflows/*.yml`, or any
file gated by `script-parser-tests`, `script-tests`, or
`unity-contract-tests`, run `npm run preflight:pre-push`. For triage on
a fresh clone or a flaky workstation, run `npm run doctor` -- it prints
Node, npm, `jest-circus` install state, and the isolated cache
directory. A corrupt/partial isolated cache is auto-cleared by
`npm run repair:node-tooling` (it runs before the doctor in the native hook);
the doctor only reports it as WARN.

## Hook self-heal protocol (zero manual touch)

The isolated managed-Jest cache is a REGENERABLE tmpdir fallback
(`os.tmpdir()/dxmessaging-managed-jest`), consulted only when the local
`node_modules` Jest is unhealthy. A corrupt/partial cache is auto-cleared:

1. PROACTIVE: `scripts/repair-node-tooling.js` (run by the native `pre-commit`
   and `pre-push` hooks, the advisory Stop hook, and `npm run preflight`) calls
   `healRegenerableCaches`, which path-guard-purges every corrupt install dir
   under the cache root -- under a distinct cross-process lock, with bounded
   EPERM/EBUSY retry -- BEFORE the cache is ever consulted. The next
   managed-Jest run rebuilds it. This runs before `npm run doctor` in the
   native hook, so the doctor sees a clean (or absent) cache. Corruption shapes
   it heals: a partial install (missing `jest-circus/build/runner.js`), a
   zero-byte runner (antivirus/Disk-Cleanup mid-write -- size 0 is treated as a
   miss, mirroring the integrity gate's empty-file rule), and a stray FILE
   sitting where the cache directory belongs (an `ENOTDIR` root, cleared via a
   strict-equality-guarded delete since the strict-descendant guard cannot).
   HAPPY-PATH COST IS ~0 (cache root absent -> no readdir, no lock). The only
   non-zero cost is the rare corrupt-AND-locked path, bounded in TWO parts so it
   stays inside the <1s git-hook budget: lock acquisition is capped at
   `REGENERABLE_CACHE_HEAL_LOCK_TIMEOUT_MS` (250ms -- best-effort, a wedged peer
   is abandoned and the reactive tier below clears the cache), and the in-lock
   per-dir EPERM/EBUSY retry sleep is capped IN AGGREGATE by
   `REGENERABLE_CACHE_HEAL_PURGE_BUDGET_MS` (600ms shared across all corrupt dirs
   and the stray-file branch via one budgeted sleep) so N persistently-locked
   dirs can never stack into N\*backoff. Worst case ~= 250ms + 600ms < 1s; the
   heal backoff `[200, 400]` is deliberately tighter than the npm-ci
   `[750, 2000]`.
1. REACTIVE: if a real Jest run still emits a `jest-circus` resolution failure,
   `scripts/run-managed-jest.js` decodes the stderr and resets the isolated
   cache once before retrying (see
   [Integrity Gate Robustness](./integrity-gate-robustness.md) for the routing).
1. Never bypass with `git commit --no-verify` or `git push --no-verify`. The
   hook is the gate, not the obstacle.

### Anti-pattern: detection without wired-in automated remediation

A checker that can FAIL on a regenerable/transient artifact while offering only
a manual command is a defect -- the generalized fixer/checker-divergence
category: a checker that can fail on a state with no automated path to a clean
state. Auto-heal-or-downgrade; never a hard gate. The healer MUST clear EVERY
state the checker can flag (no fixer/checker scope divergence): the
regenerable-cache healer iterates the exact set of install dirs the doctor
walks. The doctor stays read-only and reports WARN; the heal lives in
`repair-node-tooling.js`. See
[Integrity Gate Robustness](./integrity-gate-robustness.md) for the reactive
routing.

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

- [Integrity Gate Robustness](./integrity-gate-robustness.md)
- [Cross-Platform Script Compatibility](./cross-platform-compatibility.md)
- [Git Hook Performance Budget](../performance/git-hook-performance.md)
- [Let Tools Resolve Modules](./let-tools-resolve-modules.md)

## Changelog

| Version | Date       | Changes                                                                                                                                                                                                                                                                                                  |
| ------- | ---------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 1.0.0   | 2026-05-18 | Initial version after the pre-push.txt failure.                                                                                                                                                                                                                                                          |
| 1.1.0   | 2026-05-18 | Added integrity-gate auto-repair flow, state diagram, refusal rules, and environment-var matrix.                                                                                                                                                                                                         |
| 1.2.0   | 2026-05-30 | Proactive zero-touch isolated-cache heal (registry-driven); doctor downgraded to WARN; removed the manual `rm` repair step; added the detection-without-remediation anti-pattern.                                                                                                                        |
| 1.3.0   | 2026-05-30 | Documented the heal's two-part wall-time bound: 250ms lock-acquire cap plus a 600ms cumulative in-lock purge-sleep budget (shared across all corrupt dirs) so a persistent Windows lock on N dirs cannot exceed the <1s git-hook target; heal backoff `[200, 400]` is tighter than npm-ci `[750, 2000]`. |
| 1.3.0   | 2026-05-30 | Heal/checker now cover the zero-byte runner (empty-file rule) and the stray-FILE (`ENOTDIR`) cache root; the readdir host-fault FAIL is gated on fallback relevance.                                                                                                                                     |
