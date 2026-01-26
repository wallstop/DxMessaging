# Repository Guidelines

## Project Structure & Module Organization

- [Runtime](../Runtime/) — core library (`DxMessaging.Core`) and Unity components (`DxMessaging.Unity`).
- [Editor](../Editor/) — editor utilities, analyzers, and setup (`DxMessaging.Editor`).
- [SourceGenerators](../SourceGenerators/) — Roslyn source generators (`netstandard2.0`).
- [Tests/Runtime](../Tests/Runtime/) — NUnit/Unity Test Framework tests (e.g., [Core Nominal tests](../Tests/Runtime/Core/NominalTests.cs)).
- [Docs](../Docs/) — usage patterns and examples.
- Package manifest: [the package manifest](../package.json) (published to NPM/UPM).

## Build, Test, and Development Commands

- Format: `dotnet tool restore` then `dotnet tool run csharpier format`.
- Build generators: `dotnet build SourceGenerators/WallstopStudios.DxMessaging.SourceGenerators/WallstopStudios.DxMessaging.SourceGenerators.csproj`.
- Unity tests: open a Unity 2021.3+ project that references this package, then Window > Test Runner > PlayMode. CLI example: `Unity -batchmode -nographics -quit -projectPath <your_project> -runTests -testPlatform PlayMode -testResults ./TestResults.xml`.
- Actionlint: Runs in CI on PRs/pushes to validate GitHub Actions workflows. Available as a pre-push hook (requires `actionlint` installed locally).
- Spellcheck: Runs in CI on PRs/pushes via `cspell` to check spelling in Markdown, C#, JSON, YAML, and script files. Dictionary is maintained in [.cspell.json](../.cspell.json). Available as a pre-push hook via `npx cspell`.

## Coding Style & Naming Conventions

- Indent with 4 spaces for `.cs` (JSON/YAML: 2). CRLF, UTF-8 (no BOM) (see [the EditorConfig](../.editorconfig)).
- Prefer explicit types over `var`. Braces required. `using` directives inside the namespace.
- Naming: `PascalCase` for types/methods/properties; interfaces `I*`; type parameters `T*`; events prefixed `On*`; public fields lowerCamelCase (matches examples in the [README](../README.md)).
- Place code under `DxMessaging.Core`, `DxMessaging.Unity`, or `DxMessaging.Editor` as appropriate.
- Do not use underscores in function names, especially test function names.
- Do not use regions, anywhere, ever.
- Avoid `var` wherever possible, use expressive types.
- Do not use nullable reference types.

## Scripting Guidelines

Scripts in `scripts/` may be PowerShell (`.ps1`) or JavaScript (`.js`). Follow these practices:

- When parsing structured command output (git, build tools, etc.), prefer pattern matching over fixed positional indices. For example, use `Where-Object { $_ -like 'i/*' }` instead of assuming `[0]`.
- Validate array/collection length before accessing by index.
- Keep PowerShell and JavaScript implementations in sync when both exist for the same task.
- Add comments explaining the expected format of external command output.

### Shell Pattern Matching

- Use `grep -F` for literal string matching (paths, filenames with special characters like `.`).
- Use `grep -E` only when regex features are explicitly needed.
- Remember that `.` in a regex matches any character, not just a literal dot.
- Always quote variable expansions in patterns: `grep -F "$PATH_VAR/"` not `grep -F $PATH_VAR/`.

### Forbidden Commands

- **NEVER** run `exec bash`, `exec /bin/bash`, or any variant of `exec` that replaces the current shell. This breaks terminal sessions and causes command failures.
- If you need a new shell environment, spawn a subshell with `bash -c "command"` or simply run commands directly.

### JavaScript/Node.js Practices

- All declared constants and variables must be used or removed.
- Prefer `const` over `let`; use `let` only when reassignment is necessary.
- When adding validation constants (e.g., `VALID_X`), ensure corresponding validation logic uses them.

## PR Review Feedback Handling

Before acting on PR review feedback:

1. **Verify factual claims**: If a reviewer says a value is "incorrect," verify against the source of truth (e.g., run `git remote get-url origin` for repository URLs).
1. **Test assertions**: If a reviewer claims code is broken, reproduce the issue before fixing.
1. **Question assumptions**: Reviewers (including AI reviewers) can be mistaken. Respectfully verify before making changes.
1. **Document verification**: When rejecting feedback, document why (e.g., "Verified against git remote: correct URL is X").

## Testing Guidelines

- Frameworks: NUnit + Unity Test Framework. Use `[Test]`/`[UnityTest]` as needed.
- Location: add files under [Tests/Runtime](../Tests/Runtime/) `<Area>/` named `*Tests.cs` with classes `*Tests`.
- Keep tests independent: prefer a local `MessageBus` and explicit `MessageRegistrationToken` lifecycles.
- Do not use underscores in test function names.
- Prefer expressive assertions and failure messages so it is clear what exactly is failing when a test fails.
- Do not use regions.
- Try to use minimal comments and instead rely on expressive naming conventions and assertions.
- Do not use Description annotations for tests.
- Do not create `async Task` test methods - the Unity test runner does not support this. Make do with `IEnumerator` based UnityTestMethods.
- Do not use `Assert.ThrowsAsync`, it does not exist.
- When asserting that UnityEngine.Objects are null or not null, please check for null directly (thing != null, thing == null), to properly adhere to Unity Object existence checks.

### Comprehensive Test Coverage

Every new feature and bug fix requires extensive, exhaustive tests:

- **Normal scenarios**: Expected usage patterns and typical inputs.
- **Negative scenarios**: Invalid inputs, null values, empty collections.
- **Extreme edge cases**: Boundary values, maximum/minimum limits, unusual but valid inputs.
- **Unexpected situations**: Concurrent access, out-of-order operations, resource exhaustion.
- **"The impossible"**: States that "should never happen" but might due to bugs or misuse.

Use data-driven tests to consolidate test code and cover many cases efficiently:

- Use `[TestCase]` for inline parameterized tests.
- Use `[TestCaseSource]` for complex or reusable test data sets.

See the [Comprehensive Test Coverage skill](./skills/testing/comprehensive-test-coverage.md) for detailed guidance.

### Zero-Flaky Test Policy

This project maintains a strict **zero-flaky test policy**. Every test failure indicates a real bug that must be comprehensively investigated and fixed.

- **No flaky tests**: If a test ever fails, it reveals either a production bug or a test bug. Both require full investigation.
- **No "make it pass" fixes**: Never apply superficial fixes that make a test pass without understanding and addressing the root cause.
- **No ignored tests**: Do not skip, ignore, or disable failing tests. If a test cannot pass, it must be fixed or removed with documented justification.
- **Full investigation required**: When a test fails, investigate the production code behavior thoroughly. Understand why the failure occurred before making any changes.
- **Document findings**: When fixing test failures, document what was discovered about the underlying behavior, especially if it reveals edge cases or unexpected interactions.
- **Reproduce first**: Before fixing, ensure you can reliably reproduce the failure and understand the conditions that trigger it.

See the [Test Failure Investigation skill](./skills/testing/test-failure-investigation.md) for detailed investigation patterns and procedures.

## Documentation Guidelines

After any new feature or bug fix, documentation must be updated:

- **Markdown docs**: Update relevant files in [Docs/](../Docs/) for user-facing changes.
- **Code comments**: Keep inline comments accurate and helpful.
- **XML docs**: Update `<summary>`, `<param>`, `<returns>`, and `<remarks>` for public APIs.
- **Code samples**: Ensure examples are correct, compilable, and tested.
- **README**: Update [the README](../README.md) for significant features or breaking changes.

When documenting new behavior, note the version it was introduced (e.g., "Added in v1.2.0").

See the [Documentation Updates skill](./skills/documentation/documentation-updates.md) and [Changelog Management skill](./skills/documentation/changelog-management.md) for detailed guidance.

### Markdown Formatting Conventions

- **Ordered lists**: Use lazy numbering (`1.`, `1.`, `1.`) not sequential (`1.`, `2.`, `3.`). This matches Prettier behavior and markdownlint MD029 configuration.
- **Fenced code blocks**: Use triple backticks (```), not indented blocks.
- **Line endings**: CRLF, no UTF-8 BOM (enforced by pre-commit hooks).
- **Headings**: Use ATX-style headings (`#`, `##`, `###`) not underlined style.
- **Line length**: Not enforced. Write naturally; let lines wrap as needed.

## Changelog Guidelines

Every user-facing change must be added to [the changelog](../CHANGELOG.md):

- Follow the [Keep a Changelog](https://keepachangelog.com/) format.
- Use categories: **Added**, **Changed**, **Deprecated**, **Removed**, **Fixed**, **Security**.
- Include proper version headers with dates (e.g., `## [1.2.0] - 2026-01-22`).
- Link issues and PRs in entries (e.g., "Fixed registration bug (#123)").
- Write entries from the user's perspective, focusing on impact.

See the [Changelog Management skill](./skills/documentation/changelog-management.md) for detailed guidance.

## Skill and Context File Size Limits

All files in `.llm/` must follow strict size limits to ensure effective LLM context usage:

| Range         | Status       | Action                                     |
| ------------- | ------------ | ------------------------------------------ |
| < 200 lines   | 📝 Short     | Consider adding more detail                |
| 200-350 lines | ✅ **Ideal** | Target range for skill files               |
| 351-500 lines | ⚠️ Warning   | Consider splitting into focused sub-skills |
| > 500 lines   | ❌ **Error** | Must split; blocks CI and pre-commit       |

When a file exceeds 350 lines:

1. Identify distinct concepts or variations within the file
1. Extract each into its own focused skill file
1. Use `related` frontmatter and `## See Also` sections for cross-references
1. Keep the original file as a high-level overview if needed

Validation runs automatically via pre-commit hooks and CI. Run manually with:

```bash
node scripts/validate-skills.js
```

See the [Skill File Sizing skill](./skills/documentation/skill-file-sizing.md) for detailed guidance.

## Commit & Pull Request Guidelines

- Commits: short, imperative subject; group related changes; reference issues/PRs (e.g., "Fix registration dedupe (#123)").
- PRs: include a clear description, linked issues, before/after notes for performance changes (see [Tests/Runtime/Benchmarks](../Tests/Runtime/Benchmarks/)), and tests for bug fixes/features.
- Releasing: changes to [the package manifest](../package.json) on `master` may trigger the NPM publish workflow.

## Security & Configuration Tips

- Editor analyzer DLLs are copied into the Unity project by [SetupCscRsp.cs](../Editor/SetupCscRsp.cs); do not commit generated DLLs into this repo.
- Keep public APIs minimal and consistent; avoid breaking changes without a major version bump.
