# GitHub Copilot Instructions

Run targeted preflight checks during editing so git hooks stay last-resort safeguards.

## Workflow Edits (`.github/workflows/*.yml`, `.github/workflows/*.yaml`)

1. Run `npm run check:workflow-cspell`.
1. Run `npm run validate:workflows`.
1. Run `npm run check:yaml`.

## Test And Banner-Impacting Edits (`Tests/**`, `SourceGenerators/**`, `scripts/**/*.test.js`, `scripts/**/*.spec.js`)

1. Run `npm run check:banner-sync`.
1. If validation reports drift, run `npm run sync:banner` and then run `npm run check:banner-sync` again.

`sync:banner` uses `scripts/sync-banner-version.sh`, which prefers PowerShell and falls back to Node for cross-platform consistency.

## Hook Tooling Edits (`.pre-commit-config.yaml`, `scripts/*`, `package.json` hook scripts)

1. Run `npm run validate:pre-commit-tooling`.
1. Run `npm run preflight:pre-commit`.

## Managed Jest Tooling Edits (`scripts/run-managed-jest.js`, `scripts/verify-managed-jest-fallback.js`, `scripts/validate-node-tooling.js`)

1. Run `npm run validate:node-tooling`.
1. Run `node scripts/run-managed-jest.js --runTestsByPath scripts/__tests__/run-managed-jest.test.js scripts/__tests__/verify-managed-jest-fallback.test.js scripts/__tests__/validate-node-tooling.test.js`.
1. Run `pre-commit run --hook-stage pre-push script-tests --all-files`.

For full project guidance, see [AI Agent Guidelines](../.llm/context.md).
