# Repository Guidelines

This file is intentionally concise. It contains only critical, high-signal guidance for agentic work.

## Start Here

- Read the generated skill catalog first: [Skills Index](./skills/index.md)
- Prefer focused skills over adding large instruction blocks here.
- Keep this file under 300 lines at all times.

## Project Structure

- `Runtime/` - Core runtime and Unity-facing messaging components.
- `Editor/` - Editor tooling and analyzers.
- `SourceGenerators/` - Roslyn source generation.
- `Tests/` - Runtime and integration test coverage.
- `scripts/` - Validation, formatting, and repository automation scripts.
- `docs/` - User-facing package documentation and examples.

## Core Delivery Rules

- Implement complete solutions in one pass whenever feasible.
- When changing behavior, add or update tests in the same change.
- Prefer small focused edits over broad refactors unless required.
- Preserve existing naming and architectural patterns.
- Never commit repository settings that auto-approve chat-invoked terminal commands.
- Ensure fenced markdown examples are closed and do not swallow real sections (for example `## See Also`).
- Run file-scoped validation during editing; do not treat git hooks as the first signal of quality issues.
- When editing `.cs`, `.md`, `.json`, `.yml`, `.yaml`, `.ps1`, or `.js` files, run file-scoped cspell on touched files and update `.cspell.json` in the same change for legitimate domain terms.
- For Node child-process calls in `scripts/*.js`, prefer argument-array invocations (`spawnSync` / `execFileSync`) and `stdio` options instead of shell redirection.
- When editing `.pre-commit-config.yaml`, `scripts/*` hook tooling, `.github/workflows/*.yml`, or hook-related scripts in `package.json`, run `npm run preflight:pre-commit` before finishing.

## Build and Test Commands

- Restore tools: `dotnet tool restore`
- Format C#: `dotnet tool run csharpier format`
- Script tests: `npm run test:scripts`
- Validate pre-commit Node tooling policy: `npm run validate:pre-commit-tooling`
- Pre-commit Node tooling preflight: `npm run preflight:pre-commit`
- Run parser hook suite exactly as pre-commit executes it: `pre-commit run script-parser-tests --all-files`
- Check package.json format explicitly: `npm run check:package-json-format`
- Check hook-managed Prettier targets: `npm run check:prettier:hooks`
- Validate YAML formatting and lint policy: `npm run check:yaml`
- Validate npm package meta integrity: `npm run validate:npm-meta`
- Check C# method naming (no underscores): `node scripts/fix-csharp-underscore-methods.js --check --all`
- Auto-fix C# method naming on selected files: `node scripts/fix-csharp-underscore-methods.js <changed-files...>`
- File-scoped spellcheck: `npx --yes cspell@9 --no-progress --no-summary <changed-files...>`
- Script-wide spellcheck preflight: `npm run check:cspell:scripts`
- Note: Prettier does not auto-wrap long YAML lines; yamllint enforces the 200-character limit.
- Auto-fix markdown fragments/lists: `node scripts/fix-md029-md051.js <changed-docs.md ...>`
- Lint markdown: `npx markdownlint-cli2 <changed-docs.md ...>`
- Validate skills + context: `node scripts/validate-skills.js`
- Regenerate skills index: `node scripts/generate-skills-index.js`
- Verify index is current: `node scripts/generate-skills-index.js --check`

## C# Conventions

- Use explicit types where practical; avoid unnecessary `var`.
- Keep braces explicit.
- Avoid regions.
- Use PascalCase for all method names with no underscores (including test methods); this is auto-enforced by the `fix-csharp-underscore-methods` pre-commit hook.
- Keep test names descriptive and readable.
- Keep public API changes intentional and backward-compatible unless planned otherwise.

## Script and Automation Conventions

- Reuse shared helpers in `scripts/lib/` before duplicating parsing logic.
- Normalize multiline text handling before line-based parsing.
- Keep JS and PowerShell behavior synchronized when dual implementations exist.
- Add tests for parser changes and malformed input edge cases.
- For path-exclusion logic in script CLIs, apply exclusion patterns only to repository-local paths and add paired tests for outside-repo explicit file args plus repo-internal excluded directories.
- For pre-commit hooks that operate on staged files, remember pre-commit stashes unstaged changes and runs hooks against the staged snapshot on disk; reproduce failures through commit-equivalent hook runs when validating behavior.
- For Jest in hooks or npm scripts, use `node scripts/run-managed-jest.js` instead of bare `jest` invocations.
- For Prettier in hooks or npm scripts, use `node scripts/run-managed-prettier.js` instead of hardcoded `prettier@X.Y.Z` commands. The managed runner resolves versions in this order: package-lock.json, package.json, then static fallback.
- For `npm`/`npx` child-process calls in `scripts/*.js` (`spawnSync`, `execFileSync`, `execSync`), use `spawnPlatformCommandSync()` from `scripts/lib/shell-command.js`. Do not call `spawnSync(toShellCommand(...))` directly; the helper applies Windows shell-shim execution rules consistently.
- When editing `scripts/validate-npm-meta.js`, `scripts/__tests__/validate-npm-meta.test.js`, or npm package metadata, run `npm run validate:npm-meta` before finishing.
- When editing `scripts/fix-csharp-underscore-methods.js` or its tests, run `node scripts/run-managed-jest.js --runTestsByPath scripts/__tests__/fix-csharp-underscore-methods.test.js` and then `npm run preflight:pre-commit` before finishing.
- For parser-script failures, verify both isolated and hook-parity execution before concluding root cause: run the focused Jest path first, then run `pre-commit run script-parser-tests --all-files` from the same shell used for commit operations.
- On Windows, verify `npm --version` in the active shell before running hook-related checks (especially when using nvm/fnm).
- On Windows hosts, run `npm run preflight:pre-commit` in the same shell you use for `git commit` so hook PATH/init, npm version drift, package.json formatting, and yamllint issues are caught before commit.
- For command alternation regexes, avoid optional-suffix shorthands that split words into partial tokens; prefer explicit alternation forms like `(?:install|i)` to keep patterns readable and spellcheck-safe.
- For temporary test directory/file labels, prefer full descriptive words (for example `carriage-return-arguments`) over opaque abbreviations to reduce avoidable cspell failures.

## Line Ending Policy

- Mixed policy is required.
- CRLF: `.cs`, `.csproj`, `.sln`, `.props`
- LF: all other text files
- Source of truth for JS tooling: `scripts/lib/eol-policy.js`

## Testing Expectations

- Treat failing tests as real defects until proven otherwise.
- Prefer direct testing of production code rather than re-implementation in tests.
- Cover normal, negative, and edge-case scenarios for new behavior.

## Documentation Expectations

- Update relevant docs after user-visible behavior changes.
- Keep examples accurate and aligned with real usage.
- Update `CHANGELOG.md` for user-facing changes.
- For edited Markdown files, run `node scripts/fix-md029-md051.js` and then `npx markdownlint-cli2` before finishing.
- Ordered lists must follow MD029 `one` style (`1.` for each item).
- Internal fragment links must match GitHub/markdownlint heading slugs exactly (MD051).

## Skills to Prefer

Use the index above and then select the most relevant skill pages. Frequently useful entries include:

- Documentation and changelog guidance under `./skills/documentation/`
- Script reliability and parsing guidance under `./skills/scripting/`
- Test quality and investigation guidance under `./skills/testing/`
- Workflow robustness under `./skills/github-actions/`

## Split File Maintenance

- Split files (for example `*-part-1.md`) are regular human-maintained docs, not generated artifacts.
- If a base file grows above 300 lines, extract focused sections into linked companion files.
- Keep base files as the canonical overview and cross-link companions via `## See Also`.

## See Also

- [Skill File Sizing Guidelines](./skills/documentation/skill-file-sizing.md)
- [Documentation Updates and Maintenance](./skills/documentation/documentation-updates.md)
- [Cross-Platform Script Compatibility](./skills/scripting/cross-platform-compatibility.md)
- [Test Failure Investigation and Zero-Flaky Policy](./skills/testing/test-failure-investigation.md)
