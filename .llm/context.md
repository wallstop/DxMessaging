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
- When editing `.llm/context.md` or `.llm/skills/**/*.md`, run `npm run repair:llm-policy` and `npm run validate:llm-policy` before finishing so skill size/schema, index freshness, and markdown policy fail before hook-time.
- For user-visible code edits (`Runtime/`, `Samples~/`, user-facing `Editor/`, or shipped `SourceGenerators/` code), run `npm run validate:changelog:coverage` before finishing and resolve any `W002` warnings by rewriting entries around user impact.
- When editing `.cs`, `.md`, `.json`, `.yml`, `.yaml`, `.ps1`, or `.js` files, run file-scoped cspell on touched files with `node scripts/run-managed-cspell.js --no-progress --no-summary <changed-files...>` and update `.cspell.json` in the same change for legitimate domain terms.
- Before invoking git hooks or reporting completion after documentation edits, run `npm run validate:changed-docs` so changed Markdown and C# XML doc comments hit the ASCII/code-pattern/prose validators before hook-time.
- Before validating Markdown edits, run `node scripts/normalize-docs-ascii.js <changed-md-files...>` or the hook-equivalent `node scripts/run-staged-md-pipeline.js <changed-md-files...>` so auto-fixable non-ASCII characters are repaired before validators run.
- Native Git hooks are versioned under `scripts/hooks` and installed by `scripts/install-git-hooks.js` through `core.hooksPath`. The native `pre-commit` hook repairs Node tooling, auto-installs pinned `pre-commit` when Python is available, delegates to `pre-commit run --hook-stage pre-commit`, and retries once so successful restaged auto-fixes do not require a manual second commit attempt; the native `pre-push` hook performs the same repairs before `npm run doctor` and `npm run preflight:pre-push`. For any hook-gated tooling, script, workflow, or `.llm` edit, run `npm run doctor` and then `npm run preflight:pre-push` before reporting completion; the hook should be the last signal, not the first.
- For Node child-process calls in `scripts/*.js`, prefer argument-array invocations (`spawnSync` / `execFileSync`) and `stdio` options instead of shell redirection.
- For dynamic `import()` in `scripts/*.js`, convert filesystem paths with `pathToFileURL(...).href` before importing (raw Windows drive-letter paths fail Node's ESM loader).
- When editing `.pre-commit-config.yaml`, `scripts/*` hook tooling, `.github/workflows/*.yml`, or hook-related scripts in `package.json`, run `npm run preflight:pre-commit` before finishing.
- When editing `.pre-commit-config.yaml`, `scripts/run-managed-jest.js`, `scripts/run-managed-prettier.js`, `scripts/validate-node-tooling.js`, `scripts/validate-pre-commit-tooling.js`, `.github/workflows/*.yml`, or any file gated by the `script-parser-tests`, `script-tests`, or `unity-contract-tests` pre-push hooks, run `npm run preflight:pre-push` before reporting the task complete. Editing `package.json`, `package-lock.json`, `scripts/lib/node-modules-integrity.js`, `scripts/run-managed-cspell.js`, or any `scripts/run-managed-*.js` also requires running `npm run doctor` before reporting done. On a `testRunner option was not found` or any `jest-circus` resolution error, run `npm run doctor` and consult [Jest Hook Robustness](./skills/scripting/jest-hook-robustness.md). On Windows, suspect a partial extract; `scripts/lib/node-modules-integrity.js` auto-repairs via `npm ci`, or run manually. Path-separator regressions are blocked by `scripts/__tests__/cross-platform-path-handling.test.js`; when interpolating a path into a `warnFn` / `console.warn` / `console.error` log line in the managed-Jest / integrity-gate scripts, wrap it in `toPosixPath` or `toRepoPosixRelative` from `scripts/lib/path-classifier.js`.
- When editing ANY `.yml`/`.yaml` file (including `.github/workflows/**` AND `.github/actions/**` composite-action files), run `npm run check:yaml` before finishing so format and line-length issues are surfaced before hook-time; for `.github/workflows/**` and `.github/actions/**` also run `npm run check:workflow-cspell`, and for `.github/workflows/**` also run `npm run validate:workflows` (it lints `.github/workflows/*.yml` only). For zero-touch line-length recovery, run the auto-fixers `npm run format:yaml:comments` (wraps long `#` comments) and `npm run format:yaml:lines` (rewrites long PowerShell strings in pwsh `run:` blocks); for anything else, shorten or externalize the script per [YAML Line-Length Budget](./skills/github-actions/yaml-line-length.md).
- When editing repository tests (`Tests/**`, `SourceGenerators/**`, `scripts/**/*.test.js`, `scripts/**/*.spec.js`) or banner sync tooling, run `npm run check:banner-sync` before finishing so badge drift is caught before push-time.
- `npm run check:banner-sync` validates banner freshness only. If it reports drift, run `npm run sync:banner` and re-run `npm run check:banner-sync`.
- `scripts/sync-banner-version-hook.js` prefers open-source PowerShell 7+ (`pwsh`), then legacy `powershell`, then falls back to `node scripts/sync-banner-version.js` so banner sync works on Linux, macOS, and Windows.
- When running `preflight:pre-commit` with unstaged docs changes, the markdown hook may report 'files were modified' -- stage the changes first.
- When editing `.pre-commit-config.yaml` or hook scripts, the new performance budget test (`scripts/__tests__/hook-perf-budget.test.js`) must pass; see [Git Hook Performance Budget](./skills/performance/git-hook-performance.md).
- Never introduce `PLAN.md` / `PERF-PLAN.md` / `OLD-PLAN.md` / `GH-PAGES-PLAN.md` filename references or `T0.0` / `P0.0`-style milestone tags into shipping content (under `Runtime/`, `Editor/`, `SourceGenerators/`, `Samples~/`, `docs/`, root `*.md`, `llms.txt`). The `validate:no-plan-vocabulary` hook enforces this; treat any failure as a prose rewrite, not a hook bypass. See [No PLAN Vocabulary in Shipping Content](./skills/documentation/no-plan-vocabulary.md).
- Untracked-and-unignored paths at the repo root are forbidden. The `validate:untracked-policy` hook fails if `git ls-files --others --exclude-standard` reports any path. Either commit the file or extend `.gitignore` / `.npmignore`.
- Tests that spawn host-sensitive scripts (e.g. `scripts/unity/ensure-editor.ps1`, `scripts/unity/run-ci-tests.ps1`, which probe `${env:ProgramFiles}\Unity\Hub\Editor\...` and `$env:LOCALAPPDATA\Unity\...`) MUST build the spawn env hermetically via `sandboxHostFolderEnv(baseEnv, sandboxRootDir)` from `scripts/lib/spawn-env-sandbox.js`. Never neutralize host-default folder vars (ProgramFiles, ProgramFiles(x86), ProgramW6432, CommonProgramFiles\*, LOCALAPPDATA) with `delete env.X`: Windows env-var names are case-insensitive but JS `delete` is case-sensitive, so a surviving case-variant (`PROGRAMFILES`) keeps the real folder visible and a host install leaks in (invisible on Linux, breaks only on Windows). SET them to empty sandbox dirs instead. The case-INSENSITIVE-removal proof lives in the golden unit test `scripts/lib/__tests__/spawn-env-sandbox.test.js` (it is OS-independent); end-to-end leak regressions through the real ps1 (e.g. `scripts/__tests__/unity-ensure-editor-il2cpp-idempotency.test.js`) prove removal + sandboxing and catch a TOTAL bypass on any OS, but on Linux cannot distinguish casing (Linux pwsh reads env names case-sensitively). Test the "host already has X installed" scenario on any OS by injecting a fake install. The `hermetic-host-env-policy` guard (in `script-parser-tests`) enforces this; it flags the dot (`delete env.ProgramFiles`), bracket (`delete env["ProgramFiles(x86)"]`), `Reflect.deleteProperty(env, "ProgramFiles")`, and array-driven forms where a host-folder name is a literal element of THE SAME array that drives a computed delete (`const ENV_TO_DELETE = [..., "ProgramFiles", ...]; for (const k of ENV_TO_DELETE) delete env[k]`, plus the inline, `forEach`, and direct-index/`Reflect` variants). The correlation is per-construct, so an unrelated array holding a host-folder name beside an unrelated computed delete is NOT flagged. Residual (not over-claimed): it cannot see a fully runtime-assembled name, a scalar-variable-routed literal (`const k = "ProgramFiles"; delete env[k]`), or a push-built array; the golden unit test and the use-the-helper requirement are the backstops.

## Build and Test Commands

- Restore tools: `dotnet tool restore`
- Format C#: `dotnet tool run csharpier format`
- Script tests: `npm run test:scripts`
- Validate pre-commit Node tooling policy: `npm run validate:pre-commit-tooling`
- Pre-commit Node tooling preflight: `npm run preflight:pre-commit`
- Pre-push hook parity preflight: `npm run preflight:pre-push`
- Diagnose local Node/hook environment: `npm run doctor`
- Validate local Node tool dependency health: `npm run validate:node-tooling`
- Run Unity/devcontainer contract tests: `npm run test:unity-contracts`
- Run markdown hook parity check: `npm run validate:hook-markdown`
- Repair generated `.llm` index: `npm run repair:llm-policy`
- Validate `.llm` skills, index, and markdown policy: `npm run validate:llm-policy`
- Run parser hook suite exactly as pre-push executes it: `pre-commit run --hook-stage pre-push script-parser-tests --all-files`
- Check package.json format explicitly: `npm run check:package-json-format`
- Check hook-managed Prettier targets: `npm run check:prettier:hooks`
- Validate YAML formatting and lint policy: `npm run check:yaml`
- Auto-wrap breakable YAML comments to line-length policy: `npm run format:yaml:comments`
- Check YAML comment line-length auto-wrap drift: `npm run check:yaml:comments`
- Validate npm package meta integrity: `npm run validate:npm-meta`
- Validate changelog structure plus changed-file coverage: `npm run validate:changelog:coverage`
- Check C# method naming (no underscores): `node scripts/fix-csharp-underscore-methods.js --check --all`
- Auto-fix C# method naming on selected files: `node scripts/fix-csharp-underscore-methods.js <changed-files...>`
- File-scoped spellcheck: `node scripts/run-managed-cspell.js --no-progress --no-summary <changed-files...>`
- Script-wide spellcheck preflight: `npm run check:cspell:scripts`
- Note: Prettier does not auto-wrap long YAML lines; use `format:yaml:comments` for breakable YAML comments and rely on yamllint for non-breakable overflows.
- For long `.pre-commit-config.yaml` values (especially `description:` fields), use YAML folded scalars (`>-`) instead of single-line strings.
- For `.github/workflows/*.yml` `run:` blocks, keep shell statements multiline (`run: |` plus line breaks) instead of single long lines; `validate:workflows` enforces the same line-length ceiling early to keep hooks as a last-resort check.
- Auto-fix markdown fragments/lists: `node scripts/fix-md029-md051.js <changed-docs.md ...>`
- Lint markdown: `npx markdownlint-cli2 <changed-docs.md ...>`
- Validate skills + context only: `node scripts/validate-skills.js`

## Running Unity Tests

For Unity-side tests in `Tests/Editor/` or `Tests/Runtime/` (excludes Benchmarks/Allocations/Comparisons by default):

- EditMode: `bash scripts/unity/run-tests.sh --platform editmode`
- PlayMode: `bash scripts/unity/run-tests.sh --platform playmode`
- IL2CPP standalone: `bash scripts/unity/run-tests.sh --platform standalone`
- Filter: `--filter <regex>` (passed to `-testFilter`)
- Include perf: `--include-perf` (off by default; the active `unity-benchmarks.yml` opts in via the `compute-unity-assemblies` action's `include-perf` input)
- Include comparisons: `--include-comparisons` (off by default; requires MessagePipe/UniRx/UniTask/Zenject packages in the harness)
- Include DI integrations (Reflex/Zenject/VContainer): `--include-integrations` (off by default)
- Realtime log streams to stdout; XML written to `.artifacts/unity/results.xml` unless `--results` overrides it
- CI host project: generated under `.artifacts/unity/projects/<version>-<mode>/` by `scripts/unity/run-ci-tests.ps1` -- see [UPM Test Harness](./skills/unity/upm-test-harness.md)
- License: see [Unity License Bootstrap](./skills/unity/unity-license-bootstrap.md). CI activates Unity with a classic serial (`UNITY_SERIAL` + `UNITY_EMAIL` + `UNITY_PASSWORD`, the single, primary CI path) and guarantees a `-returnlicense` on every exit path; the retired `UNITY_LICENSING_SERVER` secret is removed. A `.ulf` (`UNITY_LICENSE` / `UNITY_LICENSE_B64`) remains only as the LOCAL fallback for `run-tests.sh` / `run-tests.ps1`.
- ARM Mac (Apple Silicon): not supported locally -- use a non-ARM local shell or Codespace, or rely on the active Unity GitHub workflows (direct Unity on self-hosted Windows)
- For source-generator tests (no Unity), use `dotnet test SourceGenerators/...Tests`

## GitHub Actions / CI Runners

- Self-hosted runner topology (org-level, group "Default"):
  - `ELI-MACHINE`: `self-hosted, X64, RAM-64GB, Windows, fast`
  - `DAD-MACHINE`: `self-hosted, X64, RAM-64GB, Windows`
  - `box-linux`: `self-hosted, Linux, X64, RAM-64GB`
  - `mac-mini`: `self-hosted, RAM-16GB, macOS, ARM64`
  - `old-linux`: `self-hosted, Linux, X64, RAM-16GB, old`
  - `ubuntu-latest-large`: GitHub-hosted large runner
- Never use a single shared `concurrency.group` across multiple matrix entries without mitigation. GitHub Actions retains only one running + one pending slot per group unless `queue: max` is declared, so unmitigated shared groups can evict pending matrix entries. Allowed escape hatches are (a) expand the group with at least one `${{ matrix.* }}` token, (b) declare `queue: max` with `cancel-in-progress: false`, or (c) declare `strategy.max-parallel: 1`.
- Do not use native GitHub `concurrency.group: wallstop-organization-builds` in workflows. GitHub native concurrency is repository-scoped and serializes whole jobs; the organization lock name belongs only in the central `Ambiguous-Interactive/ambiguous-organization-build-lock` acquire/release action inputs.
- Unity is activated with a classic serial (`UNITY_SERIAL` + `UNITY_EMAIL` + `UNITY_PASSWORD`); the floating licensing server is RETIRED (`UNITY_LICENSING_SERVER` removed). A serial has no server-side reclaim and only a small activation-seat pool (typically ~2 seats), so the org lock serializes Unity jobs to one-at-a-time. Every Unity-credential-using job (`unity-tests`, `benchmarks`, `release.unity-checks`) must run `./.github/actions/validate-unity-license` (checks the three serial secrets are present and errors if the retired `UNITY_LICENSING_SERVER` is still set), then run `scripts/unity/ensure-editor.ps1 -CiManagedOnly` in the `Provision Unity Editor` step before the org lock with an explicit `-ProvisioningProfile` and export `UNITY_EDITOR_PATH` through `$GITHUB_ENV`, then acquire `wallstop-organization-builds` through `Ambiguous-Interactive/ambiguous-organization-build-lock/.github/actions/acquire-build-lock@v1` immediately before `scripts/unity/run-ci-tests.ps1`, then release it with `if: always()` through `release-build-lock@v1`. Editor install/repair does not require the paid Unity seat and must stay outside the organization lock. `ensure-editor.ps1` treats CI editors as a profile-scoped desired state: `EditorOnly` verifies only the editor, `StandaloneWindowsIl2Cpp` verifies only `windows-il2cpp`, `Android` verifies Android SDK/NDK/OpenJDK proof, and `Full` preserves the broad manual module set for compatibility. Active CI uses `EditorOnly` for editmode/playmode/benchmarks/release checks and `StandaloneWindowsIl2Cpp` for standalone; Android provisioning is opt-in. Module presence must be proven by concrete player/toolchain leaf files, not empty broad support directories. If a managed editor is missing profile-required module groups or the CLI reports "already installed" without a resolvable `Unity.exe`, the script runs `unity uninstall <version>`, quarantines the version under `<install-root>\_quarantine\...`, and reinstalls it through the Unity CLI while preserving CI-managed-only resolution so host `ProgramFiles` installs cannot leak in. In CI-managed mode, Unity CLI version-scoped mutations are blocked unless the getter-reported CLI install root is inside the managed root, so a failed/ignored `install-path -s` cannot mutate an external editor tree. Do not rely on manually installed/archive-installed editors for CI modules. Set `DXM_UNITY_DISABLE_EDITOR_REPAIR=1` only for installer debugging. The license is returned on EVERY exit path through four redundant layers: `run-ci-tests.ps1` runs a defensive return-at-start (`Invoke-UnityLicenseReturn` reclaims a seat a prior killed run leaked on this persistent runner), activates via `Invoke-UnityLicenseActivate` (throws on failure), and returns via `Invoke-UnityLicenseReturn` in a `finally`; every Unity workflow also runs `./.github/actions/return-unity-license` as an `if: always()` step inside the org-lock window (before the lock release) so a killed process still returns the license; and the next run's return-at-start is the final backstop on the persistent runner. The serial/password are never echoed or logged, and license logs go to `RUNNER_TEMP` (never uploaded). The accepted residual risk (a scheduled reaper was declined) is both machines leaking simultaneously with zero seats free; the mitigation is to ask Unity to raise the seat count. See [Unity License Return Guarantee](./skills/unity/unity-license-return-guarantee.md). IL2CPP is the `standalone` entry in the `unity-tests.yml` `test-mode` matrix; `scripts/unity/run-ci-tests.ps1` maps it to `StandaloneWindows64` and configures IL2CPP in the generated project. Both ELI-MACHINE and DAD-MACHINE are eligible for every Unity job via the uniform `runs-on: [self-hosted, Windows, RAM-64GB]`; the `fast` label remains on ELI-MACHINE for future opt-in hotfix dispatches but no job requests it today.
- Per-runner Unity-cache safety is provided by each runner agent's exclusive workspace. CI caches the generated project's `Library` under `.artifacts/unity/projects/<version>-<mode>/Library` and Unity package caches under `.artifacts/unity/cache/<version>`, with keys including OS, architecture, Unity version, mode, and package/test inputs. Do not add broad restore keys for Unity `Library`.
- `UNITY_ACCELERATOR_ENDPOINT` is optional. If present, `scripts/unity/run-ci-tests.ps1` passes Unity cache-server CLI flags and requires `host:port` format (for example `127.0.0.1:10080`), not an `http://` URL.
- Known GitHub Actions dispatcher bug: self-hosted runners can report Online/Idle while `runner_id` stays at 0 for 7+ minutes, leaving a queued job stuck even when label sets match (see [Community Discussion #186811](https://github.com/orgs/community/discussions/186811)). Recovery paths: (a) `.github/workflows/stuck-job-watchdog.yml` auto-audits queued runs every 5 minutes (`MIN_QUEUE_AGE_SECONDS=300`) and reruns up to twice per 24h when an idle runner satisfies the queued job's labels; (b) `.github/workflows/unstick-run.yml` is a `workflow_dispatch`-only manual recovery for a single explicit `run_id` (optional `force_redispatch` + `bypass_exclusion` inputs), cancelling and optionally REST-redispatching on demand. Caveat: GitHub `schedule:` cron triggers fire only from the repo default branch, so the watchdog is INACTIVE until merged to `master`; until then, `unstick-run.yml` (or a manual `workflow_dispatch` of the watchdog from the Actions tab) is the only auto-recovery path. Manual recourse: re-run the failed job via the GitHub UI.
- Enforcement: `scripts/validate-workflows.js` (`npm run validate:workflows`) lints every workflow file for native misuse of the organization lock group, matrix-with-shared-group eviction, unsupported `game-ci/unity-test-runner@v4` inputs, missing organization lock acquire/release wrappers for GameCI experiments, missing Unity license preflight, and the self-hosted label allowlist. It also runs `findUnityLicenseReturnViolations` (requires the `if: always()` `return-unity-license` step inside the org-lock window), `findForbiddenUnityLicenseSecretViolations` (rejects any reintroduced `UNITY_LICENSING_SERVER` secret reference), and `findRequiredUnityLicenseSecretViolations` (requires the three serial secrets be wired on Unity jobs). The static guard `scripts/__tests__/unity-license-leak-safety.test.js` pins the `Invoke-UnityLicenseActivate`/`Invoke-UnityLicenseReturn` activate-and-return bracket in `run-ci-tests.ps1` (plus the `return-unity-license` action and the forbidden `UNITY_LICENSING_SERVER`), the behavioral leak-regression in `scripts/__tests__/unity-runner-strictmode-smoke.test.js` asserts the license is returned on failure paths, and the shape contract test `scripts/__tests__/unity-workflow-shape.test.js` (plus `scripts/__tests__/validate-workflows-concurrency-and-labels.test.js`) keep the Unity-credential-using jobs and the `if: always()` return-step shape honest. The validator scans `.github/workflows/*.yml` only; the `.github/workflows-disabled/*` mirror is intentionally left at the old `${{ github.workflow }}-${{ github.ref }}` group shape and is not policed.
- The workflow validator is invoked in CI by `.github/workflows/actionlint.yml` (Validate workflow patterns step) so any reintroduction of the sentinel, matrix-eviction footgun, or off-allowlist label set fails the PR before merge.

## Devcontainer Workflow

The agent runs from inside the slim devcontainer (.NET 9/10 base + docker-outside-of-docker). Local Unity tests spawn ephemeral `unityci/editor` containers via the host docker socket; the image is pulled lazily on first use, and the local `.unity-test-project/Library` cache is preserved in a named volume across runs. CI uses `scripts/unity/run-ci-tests.ps1` on self-hosted Windows instead. See [Devcontainer Cache Contract](./skills/unity/devcontainer-cache-contract.md) and [Headless Test Runner](./skills/unity/headless-test-runner.md).

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
- In Jest tests that spawn `pwsh` or `powershell`, never assert multi-word phrases against raw `stdout` / `stderr` or raw `${result.stdout}\n${result.stderr}` merges. Use `combinedText`, `stdoutText`, `normalizePwshText`, or `assertPwshContains` from `scripts/lib/pwsh-output.js` so PowerShell ConciseView wrapping on narrower Windows consoles cannot split phrases. Run `npm run fix:pwsh-output-assertions -- <changed-test-files...>` and then `node scripts/run-managed-jest.js --runTestsByPath <changed-test-files...> scripts/__tests__/pwsh-output-assertion-policy.test.js` before hook-time validation.
- Add tests for parser changes and malformed input edge cases.
- For path-exclusion logic in script CLIs, apply exclusion patterns only to repository-local paths and add paired tests for outside-repo explicit file args plus repo-internal excluded directories.
- For pre-commit hooks that operate on staged files, remember pre-commit stashes unstaged changes and runs hooks against the staged snapshot on disk; reproduce failures through commit-equivalent hook runs when validating behavior.
- For auto-fix hooks that restage files, guard restaging with `git diff --quiet -- "$@" || git add "$@"` so no-op runs do not touch the git index.
- For Jest in hooks or npm scripts, use `node scripts/run-managed-jest.js` instead of bare `jest` invocations.
- When editing `scripts/run-managed-jest.js`, `scripts/verify-managed-jest-fallback.js`, or `scripts/validate-node-tooling.js`, run `npm run validate:node-tooling` first so missing Jest runner dependencies are caught before hook-time.
- For managed Jest tooling edits, run `node scripts/run-managed-jest.js --runTestsByPath scripts/__tests__/run-managed-jest.test.js scripts/__tests__/verify-managed-jest-fallback.test.js scripts/__tests__/validate-node-tooling.test.js` and then `pre-commit run --hook-stage pre-push script-tests --all-files`.
- For Jest-driven pre-push hooks (`script-parser-tests`, `script-tests`, `unity-contract-tests`), follow [Jest Hook Robustness](./skills/scripting/jest-hook-robustness.md). The wrapper at `scripts/run-managed-jest.js` MUST NOT inject `--testRunner <abs-path>`; the policy is enforced by `scripts/__tests__/run-managed-jest-no-injected-test-runner.test.js` (narrow source-scan) and `scripts/__tests__/no-testrunner-injection-policy.test.js` (repo-wide policy). Before pushing, run `npm run preflight:pre-push`.
- For Prettier in npm scripts (`format:*`, `check:prettier:hooks`) and ad-hoc invocations, use `node scripts/run-managed-prettier.js` instead of hardcoded `prettier@X.Y.Z` commands. The managed runner resolves versions in this order: package-lock.json, package.json, then static fallback. Hook entries should call shell-neutral Node entrypoints so they run on native Linux, macOS, and Windows without Bash.
- For `npm`/`npx` child-process calls in `scripts/*.js` (`spawnSync`, `execFileSync`, `execSync`), use `spawnPlatformCommandSync()` from `scripts/lib/shell-command.js`. Do not call `spawnSync(toShellCommand(...))` directly; the helper applies Windows shell-shim execution rules consistently. On win32 it wraps shims as `<ComSpec> /d /s /c npm.cmd ...args` (`shell:false`, `windowsHide:true`); never spawn `npm`, `npx`, `*.cmd`, or `*.bat` directly via child_process.
- For spawn-shape test assertions, derive the expected command and args from `buildSpawnInvocation(command, args, options, platform)` (the single source of truth that `spawnPlatformCommandSync` itself uses), e.g. `const inv = buildSpawnInvocation("npm", ["pack", ...]); expect(spy).toHaveBeenCalledWith(inv.command, inv.args, expect.objectContaining({...}))`. Never assert a raw command name (`"npm"`, `"npm.cmd"`, `toShellCommand(...)`) as the expected spawn command; `toShellCommand("npm")` resolves to `"npm"` on Linux and `"npm.cmd"` on Windows, so a host-only assertion passes in the Linux devcontainer/CI yet fails when the pre-push hook runs on Windows.
- For platform-divergent behavior, test linux AND win32 (and darwin where relevant) regardless of host OS by overriding `process.platform` (save/restore via `Object.defineProperty(process, "platform", { value, configurable: true })`) or by passing an explicit `platform` argument. Host-only assertions rot silently and only fail when a hook runs on the other OS. The repo-wide guard `scripts/__tests__/spawn-invocation-policy.test.js` blocks both the stale-assertion and direct-spawn anti-patterns at pre-push time.
- Before pushing, run the hook test suite (`npm test`), which now exercises every platform branch of the spawn helpers; do not rely on a single host's pass.
- Cross-platform regression tests opt into fast first-failure attribution by carrying the `@cross-platform-regression` marker inside a comment anywhere in the file (conventionally the header; detection is comment-span based, so the marker is honored in any real comment and ignored inside strings/code). The coverage guard `scripts/__tests__/cross-platform-preflight-coverage.test.js` requires every marked test to be wired into the targeted regression step of `.github/workflows/cross-platform-preflight.yml` (gated on ubuntu/windows/macos) and requires every path listed there to exist and carry the marker, so a regression in one fails fast with attribution on every OS and the list cannot rot. The marker set is intentionally a curated subset, not every cross-platform test: the `preflight:pre-push` step already runs the entire suite on all three OSes, so unmarked platform-divergent tests still get cross-OS coverage; the marker only adds first-failure attribution.
- Tests that drive `scripts/unity/ensure-editor.ps1` against a fake `Unity.exe` stub MUST set `DXM_UNITY_SKIP_NATIVE_STARTUP_PROBE=1` in the spawn env; Windows `CreateProcess()` rejects shebang `.exe` files as not a valid PE binary. The production gate lives in `Ensure-UnityNativeStartupHealthy` and is enforced by `scripts/__tests__/unity-native-startup-probe-isolation.test.js`. See [Cross-Platform Script Compatibility](./skills/scripting/cross-platform-compatibility.md#stub-executables-on-windows-pe-binary-requirement).
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
- Documentation and `///` XML doc comments must be pure ASCII; see [ASCII-Only Documentation Policy](./skills/documentation/ascii-only-docs.md). Run `node scripts/normalize-docs-ascii.js <md-or-cs-files...>` first, then `node scripts/validate-docs-ascii.js` (or, for the hook-equivalent batch run, `node scripts/run-staged-md-pipeline.js <md-files>` for `.md` and `node scripts/run-staged-validators.js <cs-files>` for `.cs`) before finishing.
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
- Unity headless test workflow under `./skills/unity/` (see headless-test-runner, unity-license-bootstrap, unity-license-return-guarantee, upm-test-harness, devcontainer-cache-contract, unity-ci-matrix, unity-perf-test-isolation)

## Split File Maintenance

- Split files (for example `*-part-1.md`) are regular human-maintained docs, not generated artifacts.
- Keep `.llm/**/*.md` files in the 120-260 line target range; 261-300 is a warning, and above 300 is a hard failure.
- If a base file approaches the warning range, extract focused sections into linked companion files before the hook becomes the first signal.
- Keep base files as the canonical overview and cross-link companions via `## See Also`.

## See Also

- [Skill File Sizing Guidelines](./skills/documentation/skill-file-sizing.md)
- [Documentation Updates and Maintenance](./skills/documentation/documentation-updates.md)
- [ASCII-Only Documentation Policy](./skills/documentation/ascii-only-docs.md)
- [Code Samples Must Compile](./skills/documentation/code-samples-must-compile.md)
- [Human-Prose Documentation Policy](./skills/documentation/human-prose-policy.md)
- [Cross-Platform Script Compatibility](./skills/scripting/cross-platform-compatibility.md)
- [Integrity Gate Robustness](./skills/scripting/integrity-gate-robustness.md)
- [Jest Hook Robustness](./skills/scripting/jest-hook-robustness.md)
- [Let Tools Resolve Modules](./skills/scripting/let-tools-resolve-modules.md)
- [Native Git Hook Bootstrap](./skills/scripting/native-git-hooks.md)
- [Test Failure Investigation and Zero-Flaky Policy](./skills/testing/test-failure-investigation.md)
- [Lifecycle Edge-Case Test Coverage](./skills/testing/lifecycle-edge-coverage.md)
- [LeakWatcher: Detecting Registration Leaks in Tests](./skills/testing/leak-watcher-usage.md)
- [Memory Reclaim Coverage](./skills/testing/memory-reclaim-coverage.md)
- [DxMessaging Memory Reclamation](./skills/performance/memory-reclamation.md)
- [MessageAwareComponent Base-Call Contract](./skills/unity/base-call-contract.md)
- [Git Hook Performance Budget](./skills/performance/git-hook-performance.md)
- [Headless Unity Test Runner](./skills/unity/headless-test-runner.md)
- [Unity License Bootstrap](./skills/unity/unity-license-bootstrap.md)
- [Unity License Return Guarantee](./skills/unity/unity-license-return-guarantee.md)
- [UPM Test Harness](./skills/unity/upm-test-harness.md)
- [Devcontainer Cache Contract](./skills/unity/devcontainer-cache-contract.md)
- [Unity CI Matrix](./skills/unity/unity-ci-matrix.md)
- [Unity Perf Test Isolation](./skills/unity/unity-perf-test-isolation.md)
- [CI/CD Devcontainer Workflows](./skills/github-actions/cicd-devcontainer-workflows.md)
