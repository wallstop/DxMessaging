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
- For user-visible code edits (`Runtime/`, `Samples~/`, user-facing `Editor/`, or shipped `SourceGenerators/` code), run `npm run validate:changelog:coverage` before finishing and resolve any `W002` warnings by rewriting entries around user impact.
- When editing `.cs`, `.md`, `.json`, `.yml`, `.yaml`, `.ps1`, or `.js` files, run file-scoped cspell on touched files and update `.cspell.json` in the same change for legitimate domain terms.
- For Node child-process calls in `scripts/*.js`, prefer argument-array invocations (`spawnSync` / `execFileSync`) and `stdio` options instead of shell redirection.
- For dynamic `import()` in `scripts/*.js`, convert filesystem paths with `pathToFileURL(...).href` before importing (raw Windows drive-letter paths fail Node's ESM loader).
- When editing `.pre-commit-config.yaml`, `scripts/*` hook tooling, `.github/workflows/*.yml`, or hook-related scripts in `package.json`, run `npm run preflight:pre-commit` before finishing.
- When editing `.github/workflows/*.yml` or `.github/workflows/*.yaml`, run `npm run validate:workflows` and `npm run check:yaml` before finishing so workflow policy and line-length issues are surfaced before hook-time.
- When running `preflight:pre-commit` with unstaged docs changes, the markdown hook may report 'files were modified' -- stage the changes first.
- When editing `.pre-commit-config.yaml` or hook scripts, the new performance budget test (`scripts/__tests__/hook-perf-budget.test.js`) must pass; see [Git Hook Performance Budget](./skills/performance/git-hook-performance.md).
- Never introduce `PLAN.md` / `PERF-PLAN.md` / `OLD-PLAN.md` / `GH-PAGES-PLAN.md` filename references or `T0.0` / `P0.0`-style milestone tags into shipping content (under `Runtime/`, `Editor/`, `SourceGenerators/`, `Samples~/`, `docs/`, root `*.md`, `llms.txt`). The `validate:no-plan-vocabulary` hook enforces this; treat any failure as a prose rewrite, not a hook bypass. See [No PLAN Vocabulary in Shipping Content](./skills/documentation/no-plan-vocabulary.md).
- Untracked-and-unignored paths at the repo root are forbidden. The `validate:untracked-policy` hook fails if `git ls-files --others --exclude-standard` reports any path. Either commit the file or extend `.gitignore` / `.npmignore`.

## Build and Test Commands

- Restore tools: `dotnet tool restore`
- Format C#: `dotnet tool run csharpier format`
- Script tests: `npm run test:scripts`
- Validate pre-commit Node tooling policy: `npm run validate:pre-commit-tooling`
- Pre-commit Node tooling preflight: `npm run preflight:pre-commit`
- Validate local Node tool dependency health: `npm run validate:node-tooling`
- Run Unity/devcontainer contract tests: `npm run test:unity-contracts`
- Run markdown hook parity check: `npm run validate:hook-markdown`
- Run parser hook suite exactly as pre-push executes it: `pre-commit run --hook-stage pre-push script-parser-tests --all-files`
- Check package.json format explicitly: `npm run check:package-json-format`
- Check hook-managed Prettier targets: `npm run check:prettier:hooks`
- Validate YAML formatting and lint policy: `npm run check:yaml`
- Validate npm package meta integrity: `npm run validate:npm-meta`
- Validate changelog structure plus changed-file coverage: `npm run validate:changelog:coverage`
- Check C# method naming (no underscores): `node scripts/fix-csharp-underscore-methods.js --check --all`
- Auto-fix C# method naming on selected files: `node scripts/fix-csharp-underscore-methods.js <changed-files...>`
- File-scoped spellcheck: `npx --yes cspell@10.0.0 --no-progress --no-summary <changed-files...>`
- Script-wide spellcheck preflight: `npm run check:cspell:scripts`
- Note: Prettier does not auto-wrap long YAML lines; yamllint enforces the 200-character limit.
- For long `.pre-commit-config.yaml` values (especially `description:` fields), use YAML folded scalars (`>-`) instead of single-line strings.
- For `.github/workflows/*.yml` `run:` blocks, keep shell statements multiline (`run: |` plus line breaks) instead of single long lines; `validate:workflows` enforces the same line-length ceiling early to keep hooks as a last-resort check.
- Auto-fix markdown fragments/lists: `node scripts/fix-md029-md051.js <changed-docs.md ...>`
- Lint markdown: `npx markdownlint-cli2 <changed-docs.md ...>`
- Validate skills + context: `node scripts/validate-skills.js`
- Regenerate skills index: `node scripts/generate-skills-index.js`
- Verify index is current: `node scripts/generate-skills-index.js --check`

## Running Unity Tests

For Unity-side tests in `Tests/Editor/` or `Tests/Runtime/` (excludes Benchmarks/Allocations/Comparisons by default):

- EditMode: `bash scripts/unity/run-tests.sh --platform editmode`
- PlayMode: `bash scripts/unity/run-tests.sh --platform playmode`
- IL2CPP standalone: `bash scripts/unity/run-tests.sh --platform standalone`
- Filter: `--filter <regex>` (passed to `-testFilter`)
- Include perf: `--include-perf` (off by default; GitHub benchmark workflow template is disabled)
- Include comparisons: `--include-comparisons` (off by default; requires MessagePipe/UniRx/UniTask/Zenject packages in the harness)
- Include DI integrations (Reflex/Zenject/VContainer): `--include-integrations` (off by default)
- Realtime log streams to stdout; XML written to `.artifacts/unity/results.xml` unless `--results` overrides it
- Bootstrap project: `.unity-test-project/` -- see [UPM Test Harness](./skills/unity/upm-test-harness.md)
- License: see [Unity License Bootstrap](./skills/unity/unity-license-bootstrap.md) (Personal/GameCI: raw `.ulf` in `UNITY_LICENSE` plus credentials; Professional: `UNITY_SERIAL` plus credentials; local shells may use `UNITY_LICENSE_B64`.)
- ARM Mac (Apple Silicon): not supported locally -- use a non-ARM local shell or Codespace while Unity GitHub workflows are disabled
- For source-generator tests (no Unity), use `dotnet test SourceGenerators/...Tests`

## GitHub Actions / CI Runners

- Self-hosted runner topology (org-level, group "Default"):
  - `ELI-MACHINE`: `self-hosted, X64, RAM-64GB, Windows, fast`
  - `DAD-MACHINE`: `self-hosted, X64, RAM-64GB, Windows`
  - `box-linux`: `self-hosted, Linux, X64, RAM-64GB`
  - `mac-mini`: `self-hosted, RAM-16GB, macOS, ARM64`
  - `old-linux`: `self-hosted, Linux, X64, RAM-16GB, old`
  - `ubuntu-latest-large`: GitHub-hosted large runner
- Never use a single shared `concurrency.group` across multiple matrix entries without mitigation. GitHub Actions retains only one running + one pending slot per group, so every third matrix entry to enqueue cancels the previously-queued one. The two allowed escape hatches are (a) expand the group with at least one `${{ matrix.* }}` token (for example `unity-${{ matrix.unity-version }}-${{ matrix.test-mode }}`), or (b) declare `strategy.max-parallel: 1` so matrix entries serialize internally to the workflow run and never compete for the same group slot.
- The concurrency group name `wallstop-organization-builds` is a reserved sentinel. The validator hard-fails any workflow that reintroduces it, because that group is the historical root cause of Unity matrix-eviction cancellations and runner-pickup stalls.
- Unity Pro is a single-seat license. All four Unity-credential-using jobs (`unity-tests`, `il2cpp-tests`, `benchmarks`, `release.unity-checks`) share `concurrency.group: unity-pro-license` with `cancel-in-progress: false`, and the three matrix jobs MUST set `strategy.max-parallel: 1` so matrix entries do not compete for the license lock and evict each other. The validator (`findMatrixConcurrencyEvictionViolations`) enforces this combination. Both ELI-MACHINE and DAD-MACHINE are eligible for every Unity job via the uniform `runs-on: [self-hosted, Windows, RAM-64GB]`; cross-workflow serialization comes from the shared group, within-workflow serialization from `max-parallel: 1`. The `fast` label remains on ELI-MACHINE for future opt-in hotfix dispatches but no job requests it today.
- Per-runner Unity-cache safety is provided by each runner agent's exclusive workspace (a single self-hosted agent only runs one job at a time, so `.unity-test-project/Library` directories cannot collide).
- Known GitHub Actions dispatcher bug: self-hosted runners can report Online/Idle while `runner_id` stays at 0 for 7+ minutes, leaving a queued job stuck even when label sets match (see [Community Discussion #186811](https://github.com/orgs/community/discussions/186811)). Recovery paths: (a) `.github/workflows/stuck-job-watchdog.yml` auto-audits queued runs every 5 minutes (`MIN_QUEUE_AGE_SECONDS=300`) and reruns up to twice per 24h when an idle runner satisfies the queued job's labels; (b) `.github/workflows/unstick-run.yml` is a `workflow_dispatch`-only manual recovery for a single explicit `run_id` (optional `force_redispatch` + `bypass_exclusion` inputs), cancelling and optionally REST-redispatching on demand. Caveat: GitHub `schedule:` cron triggers fire only from the repo default branch, so the watchdog is INACTIVE until merged to `master`; until then, `unstick-run.yml` (or a manual `workflow_dispatch` of the watchdog from the Actions tab) is the only auto-recovery path. Manual recourse: re-run the failed job via the GitHub UI.
- Enforcement: `scripts/validate-workflows.js` (`npm run validate:workflows`) lints every workflow file for the sentinel group, matrix-with-shared-group eviction (requiring `${{ matrix.* }}` expansion or `max-parallel: 1`), and the self-hosted label allowlist. The shape contract test `scripts/__tests__/unity-workflow-shape.test.js` plus `scripts/__tests__/validate-workflows-concurrency-and-labels.test.js` keep the Unity-credential-using jobs honest. The validator scans `.github/workflows/*.yml` only; the `.github/workflows-disabled/*` mirror is intentionally left at the old `${{ github.workflow }}-${{ github.ref }}` group shape and is not policed.
- The workflow validator is invoked in CI by `.github/workflows/actionlint.yml` (Validate workflow patterns step) so any reintroduction of the sentinel, matrix-eviction footgun, or off-allowlist label set fails the PR before merge.

## Devcontainer Workflow

The agent runs from inside the slim devcontainer (.NET 9/10 base + docker-outside-of-docker). Unity tests spawn ephemeral `unityci/editor` containers via the host docker socket; the image is pulled lazily on first use, the `.unity-test-project/Library` cache is preserved in a named volume across runs. See [Devcontainer Cache Contract](./skills/unity/devcontainer-cache-contract.md) and [Headless Test Runner](./skills/unity/headless-test-runner.md).

## C# Conventions

- Use explicit types where practical; avoid unnecessary `var`.
- Keep braces explicit.
- Avoid regions.
- Use PascalCase for all method names with no underscores (including test methods); this is auto-enforced by the `fix-csharp-underscore-methods` pre-commit hook.
- For base-call analyzer suppression parity, method-level `[DxIgnoreMissingBaseCall]` suppresses only the annotated guarded method; class-level attribute or project ignore list suppresses the entire type.
- Keep test names descriptive and readable.
- Keep public API changes intentional and backward-compatible unless planned otherwise.

## Script and Automation Conventions

- Reuse shared helpers in `scripts/lib/` before duplicating parsing logic.
- Normalize multiline text handling before line-based parsing.
- Keep JS and PowerShell behavior synchronized when dual implementations exist.
- Add tests for parser changes and malformed input edge cases.
- For path-exclusion logic in script CLIs, apply exclusion patterns only to repository-local paths and add paired tests for outside-repo explicit file args plus repo-internal excluded directories.
- For pre-commit hooks that operate on staged files, remember pre-commit stashes unstaged changes and runs hooks against the staged snapshot on disk; reproduce failures through commit-equivalent hook runs when validating behavior.
- For auto-fix hooks that restage files, guard restaging with `git diff --quiet -- "$@" || git add "$@"` so no-op runs do not touch the git index.
- For Jest in hooks or npm scripts, use `node scripts/run-managed-jest.js` instead of bare `jest` invocations.
- For Prettier in npm scripts (`format:*`, `check:prettier:hooks`) and ad-hoc invocations, use `node scripts/run-managed-prettier.js` instead of hardcoded `prettier@X.Y.Z` commands. The managed runner resolves versions in this order: package-lock.json, package.json, then static fallback. Pre-commit hook entries themselves use the inline `bash -c '[ -f node_modules/prettier/bin/prettier.cjs ] && exec node ...; else exec npx --yes --package=prettier@<pinned> prettier ...; fi'` pattern (cspell/markdownlint shape) plus the parity test at `scripts/__tests__/prettier-version-parity.test.js`.
- For `npm`/`npx` child-process calls in `scripts/*.js` (`spawnSync`, `execFileSync`, `execSync`), use `spawnPlatformCommandSync()` from `scripts/lib/shell-command.js`. Do not call `spawnSync(toShellCommand(...))` directly; the helper applies Windows shell-shim execution rules consistently.
- For validators that depend on `git` metadata (for example ignore-policy checks), treat `ENOENT`/missing-git failures as hard errors; never silently default to permissive behavior.
- When editing `scripts/validate-npm-meta.js`, `scripts/__tests__/validate-npm-meta.test.js`, or npm package metadata, run `npm run validate:npm-meta` before finishing.
- When editing `scripts/fix-csharp-underscore-methods.js` or its tests, run `node scripts/run-managed-jest.js --runTestsByPath scripts/__tests__/fix-csharp-underscore-methods.test.js` and then `npm run preflight:pre-commit` before finishing.
- For parser-script failures, verify both isolated and hook-parity execution before concluding root cause: run the focused Jest path first, then run `pre-commit run --hook-stage pre-push script-parser-tests --all-files` from the same shell used for commit operations.
- For Unity runner or perf-baseline script failures, run `npm run test:unity-contracts` before hook parity checks. On Windows, keep fake command shims platform-native (`.cmd` wrappers for PATH-resolved tools) and verify executable shell entrypoints with `git ls-files --stage` because NTFS mode bits are not the repository contract.
- For PowerShell paths exported into Docker or Unity containers, pass repo-relative paths with `/` separators; keep platform-native absolute paths only for local filesystem display and validation.
- Generated dependency lockfiles should be ignored by cspell unless the vocabulary is intentionally reviewed.
- When editing `.pre-commit-config.yaml` or `scripts/validate-pre-commit-tooling.js`, run `node scripts/run-managed-jest.js --runTestsByPath scripts/__tests__/pre-commit-hook-stage-policy.test.js scripts/__tests__/validate-pre-commit-tooling.test.js` before `npm run preflight:pre-commit`.
- On Windows, verify `npm --version` in the active shell before running hook-related checks (especially when using nvm/fnm).
- On Windows hosts, run `npm run preflight:pre-commit` in the same shell you use for `git commit` so hook PATH/init, npm version drift, package.json formatting, and yamllint issues are caught before commit.
- If a Node-backed hook reports missing packages under `node_modules`, run `npm run validate:node-tooling` before retrying the hook; it imports the same local tool graph and reports incomplete installs directly.
- For destructive test harness scripts (for example deleting files under `node_modules`), require explicit CLI opt-in flags and validate target paths defensively before mutation.
- In workflows where `package-lock.json` is gitignored, dependency install blocks must be lockfile-aware (`npm ci` when lockfile exists, `npm i --no-audit --no-fund` fallback when absent); bare install-only blocks should be treated as policy violations.
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
- Tests that exercise dispatch across more than one of `Untargeted`/`Targeted`/`Broadcast` MUST be parameterized via `MessageScenarios.AllKinds`; see [Tests Must Be Parameterized by Message Kind](./skills/testing/tests-must-be-parameterized-by-message-kind.md).
- Bus dispatch-path changes must be covered by the canonical lifecycle edge-case set (scene unload mid-dispatch, DDOL transitions, prefab pooling churn, token disable / re-enable, post-Reset emit, OnApplicationQuit drain, cross-kind reentrancy); see [Lifecycle Edge-Case Test Coverage](./skills/testing/lifecycle-edge-coverage.md).
- Tests that create and tear down message registrations should bracket the work in a `LeakWatcher` to assert no registrations survive; see [LeakWatcher: Detecting Registration Leaks in Tests](./skills/testing/leak-watcher-usage.md).
- Tests for memory holders keyed by message type or `InstanceId` must prove forced trim, idle sweep, slot-count recovery, and stale deregistration behavior; see [Memory Reclaim Coverage](./skills/testing/memory-reclaim-coverage.md).
- Benchmark and performance/allocation tests must stay isolated under `Tests/Runtime/Benchmarks` in asmdef `WallstopStudios.DxMessaging.Tests.00.Runtime.Benchmarks`; `.00` is a lexical prefix convention so the benchmark assembly sorts before peer test assemblies in Unity Test Runner. Keep `BenchmarkAssemblyContractTests` green when adding or moving perf tests.
- When adding a `MessageCache<>` storage field to `MessageBus`, update `MessageBus.ExpectedMessageCacheFieldCount`, add the field to `MessageBus.SweepableTypeCaches`, and add reclamation coverage; see [DxMessaging Memory Reclamation](./skills/performance/memory-reclamation.md).

## Documentation Expectations

- Update relevant docs after user-visible behavior changes.
- Keep examples accurate and aligned with real usage.
- Update `CHANGELOG.md` only for user-facing DxMessaging changes, not developer-only tooling/process updates.
- For `## [Unreleased]` entries, mutate existing bullets as behavior evolves; do not stack separate `Added` then `Fixed` bullets for the same unreleased change.
- When likely user-visible files change (`Runtime/`, `Samples~/`, user-facing `Editor/`, `SourceGenerators/WallstopStudios.DxMessaging.SourceGenerators/`, or `SourceGenerators/WallstopStudios.DxMessaging.Analyzer/Analyzers/`), ensure `CHANGELOG.md` is updated in the same change and run `npm run validate:changelog:coverage`.
- If changelog validation raises `W002`, rewrite the entry to foreground user impact or move internal-only details to developer docs.
- For edited Markdown files, run `node scripts/fix-md029-md051.js` and then `npx markdownlint-cli2` before finishing.
- Ordered lists must follow MD029 `one` style (`1.` for each item).
- Internal fragment links must match GitHub/markdownlint heading slugs exactly (MD051).
- Documentation and `///` XML doc comments must be pure ASCII; see [ASCII-Only Documentation Policy](./skills/documentation/ascii-only-docs.md). Run `node scripts/validate-docs-ascii.js` (or, for the hook-equivalent batch run, `node scripts/run-staged-md-pipeline.js <md-files>` for `.md` and `node scripts/run-staged-validators.js <cs-files>` for `.cs`) before finishing.
- Every C# code sample in docs - inline, fenced, and XML `<code>` blocks - must compile; see [Code Samples Must Compile](./skills/documentation/code-samples-must-compile.md). Run `node scripts/validate-doc-code-patterns.js` (or, for the hook-equivalent batch run, `node scripts/run-staged-md-pipeline.js <md-files>` for `.md` and `node scripts/run-staged-validators.js <cs-files>` for `.cs`) and the `DocsSnippetCompilationTests` suite before finishing.
- Documentation prose must avoid LLM-style filler, marketing adjectives, hedge transitions, and vague quantifiers; see [Human-Prose Documentation Policy](./skills/documentation/human-prose-policy.md). Run `node scripts/validate-docs-prose.js` (or, for the hook-equivalent batch run, `node scripts/run-staged-md-pipeline.js <md-files>` for `.md` and `node scripts/run-staged-validators.js <cs-files>` for `.cs`) before finishing.
- Subclasses of `MessageAwareComponent` MUST call `base.<method>()` from every guarded lifecycle override (`Awake`, `OnEnable`, `OnDisable`, `OnDestroy`, `RegisterMessageHandlers`); see [MessageAwareComponent Base-Call Contract](./skills/unity/base-call-contract.md). Five enforcement layers (Roslyn analyzer DXMSG006-010, IL scanner, Inspector overlay, runtime self-check, meta-test) keep the contract honest.
- When editing `Runtime/Core/Configuration/DxMessagingRuntimeSettings.cs` or its provider, run `npm run validate:runtime-settings-docs` and update `docs/reference/runtime-settings.md` and `docs/guides/memory-reclamation.md` in the same change; see [Memory Reclamation Documentation Maintenance](./skills/documentation/memory-reclamation-docs.md).

## Skills to Prefer

Use the index above and then select the most relevant skill pages. Frequently useful entries include:

- Documentation and changelog guidance under `./skills/documentation/`
- Memory reclamation guidance under `./skills/performance/memory-reclamation.md`
- Script reliability and parsing guidance under `./skills/scripting/`
- Test quality and investigation guidance under `./skills/testing/`
- Memory reclaim testing guidance under `./skills/testing/memory-reclaim-coverage.md`
- Workflow robustness under `./skills/github-actions/`
- Unity headless test workflow under `./skills/unity/` (see headless-test-runner, unity-license-bootstrap, upm-test-harness, devcontainer-cache-contract, unity-ci-matrix, unity-perf-test-isolation)

## Split File Maintenance

- Split files (for example `*-part-1.md`) are regular human-maintained docs, not generated artifacts.
- If a base file grows above 300 lines, extract focused sections into linked companion files.
- Keep base files as the canonical overview and cross-link companions via `## See Also`.

## See Also

- [Skill File Sizing Guidelines](./skills/documentation/skill-file-sizing.md)
- [Documentation Updates and Maintenance](./skills/documentation/documentation-updates.md)
- [ASCII-Only Documentation Policy](./skills/documentation/ascii-only-docs.md)
- [Code Samples Must Compile](./skills/documentation/code-samples-must-compile.md)
- [Human-Prose Documentation Policy](./skills/documentation/human-prose-policy.md)
- [Cross-Platform Script Compatibility](./skills/scripting/cross-platform-compatibility.md)
- [Test Failure Investigation and Zero-Flaky Policy](./skills/testing/test-failure-investigation.md)
- [Lifecycle Edge-Case Test Coverage](./skills/testing/lifecycle-edge-coverage.md)
- [LeakWatcher: Detecting Registration Leaks in Tests](./skills/testing/leak-watcher-usage.md)
- [Memory Reclaim Coverage](./skills/testing/memory-reclaim-coverage.md)
- [DxMessaging Memory Reclamation](./skills/performance/memory-reclamation.md)
- [MessageAwareComponent Base-Call Contract](./skills/unity/base-call-contract.md)
- [Git Hook Performance Budget](./skills/performance/git-hook-performance.md)
- [Headless Unity Test Runner](./skills/unity/headless-test-runner.md)
- [Unity License Bootstrap](./skills/unity/unity-license-bootstrap.md)
- [UPM Test Harness](./skills/unity/upm-test-harness.md)
- [Devcontainer Cache Contract](./skills/unity/devcontainer-cache-contract.md)
- [Unity CI Matrix](./skills/unity/unity-ci-matrix.md)
- [Unity Perf Test Isolation](./skills/unity/unity-perf-test-isolation.md)
- [CI/CD Devcontainer Workflows](./skills/github-actions/cicd-devcontainer-workflows.md)
