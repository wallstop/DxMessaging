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
- Actionlint: Runs in CI on PRs/pushes to validate GitHub Actions workflows. Available as a pre-push hook (requires `actionlint` installed locally). See the [Workflow Consistency skill](./skills/github-actions/workflow-consistency.md) for required workflow structure.
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

### Filename Case Sensitivity

Linux filesystems are case-sensitive; Windows and macOS are not. Scripts that work locally may fail in CI:

- **Verify exact case**: Use `git ls-files | grep -i filename` or `ls -la` to confirm actual filenames.
- **Test on Linux**: Run scripts in Docker, WSL, or Linux before committing.
- **Common mistake**: Using `dxmessaging-banner.svg` when the file is `DxMessaging-banner.svg`.

See the [Cross-Platform Compatibility skill](./skills/scripting/cross-platform-compatibility.md) for detailed patterns.

### Script Test Coverage

All scripts in `scripts/` must have corresponding test coverage in `scripts/__tests__/`:

- **Naming**: `script-name.test.js` for each script.
- **Coverage**: Test core logic, input validation, edge cases, and error handling.
- **File paths**: Include tests that verify referenced file paths exist with correct case.
- **PowerShell logic**: Implement equivalent JavaScript functions to test PowerShell script logic.

See the [Cross-Platform Compatibility skill](./skills/scripting/cross-platform-compatibility.md) for testing patterns.

### Script Configuration Synchronization

When scripts exist in both PowerShell and JavaScript, keep these values synchronized:

- **Extension lists**: File extensions for CRLF, LF, or other validation must match exactly.
- **Exclude patterns**: Directory exclusion patterns (`.git`, `node_modules`, `Library`, etc.) must match.
- **Validation logic**: Error categories and reporting must use the same structure.

When modifying one script, search for and update the corresponding script. Use comments to mark synchronized sections:

```powershell
# SYNC: Keep in sync with check-eol.js crlfExts
$extensions = @('.cs', '.csproj', '.sln', ...)
```

```javascript
// SYNC: Keep in sync with check-eol.ps1 $extensions
const crlfExts = new Set(['.cs', '.csproj', '.sln', ...]);
```

### SYNC Note Best Practices

- **Always bidirectional**: When code A has a SYNC note referencing code B, code B must also have a SYNC note referencing code A. One-way SYNC notes are easily missed during updates.
- **Never use line numbers**: Reference function names, class names, or named anchors instead. Line numbers become stale as code evolves.
- **Use descriptive identifiers**: `SYNC: Keep logic in sync with validate-skills.js validateSkill() tags validation` is better than `SYNC: Keep in sync with validate-skills.js lines 224-250`.
- **Verify references exist**: Before adding a SYNC note, confirm the referenced function, variable, or block actually exists with the exact name.

### Shell Pattern Matching

- Use `grep -F` for literal string matching (paths, filenames with special characters like `.`).
- Use `grep -E` only when regex features are explicitly needed.
- Remember that `.` in a regex matches any character, not just a literal dot.
- **Count escape sequences carefully**: Each `\.` matches exactly one literal dot. The pattern `^\.\.$` matches the literal string `..` (two consecutive dots), not "any string with dots at each end."
- **Dotfile matching**: To match dotfiles (files starting with `.`), use `^\.` which is sufficient for `git ls-files` output since `..` directory entries are never listed.
- Always quote variable expansions in patterns: `grep -F "$PATH_VAR/"` not `grep -F $PATH_VAR/`.
- **`grep` exit codes**: `grep` returns exit code 1 when no matches are found, which fails CI pipelines. Use `|| true`, `|| echo "0"`, or pipe to `wc -l` instead of `grep -c` when zero matches is acceptable.

### EditorConfig Glob Patterns

- **Recursive matching requires `**`**: `[*.sh]`only matches files in the root directory. Use`[**/*.sh]` for recursive matching across all directories.
- **Brace expansion**: Use `[**/*.{sh,bash,zsh}]` to match multiple extensions in one section.
- **Test patterns**: Verify EditorConfig patterns by checking files in subdirectories, not just the root.

### Mixed Line Ending Policies

This project uses CRLF for most files but LF for shell scripts (`.sh`, `.bash`, `.zsh`, `.ksh`, `.fish`). When working with line endings:

- **Prefer `fix-eol.js` for working tree fixes**: Run `node scripts/fix-eol.js` to directly fix line endings in your working tree. This is the recommended approach after cloning or when files have incorrect endings.
- **`git add --renormalize` only updates the index**: This command updates the git staging area based on `.gitattributes` but does **not** modify working tree files. Use it only when you need to re-stage files with updated normalization rules.
- **`git add --renormalize` must target specific paths**: Never use `git add --renormalize .` as it stages all files. Always specify exact patterns like `git add --renormalize -- '*.md' '**/*.md'`.
- **Use per-extension loops for `git add --renormalize`**: Multi-pattern renormalize commands fail with exit code 128 if any pattern matches no files. Use a loop to process each extension separately with existence checks. Example: `for ext in md json yml; do if git ls-files "*.$ext" "**/*.$ext" | grep -q .; then git add --renormalize -- "*.$ext" "**/*.$ext"; fi; done`.
- **Dotfiles do not match glob patterns in `git add`**: The command `git ls-files "*.yaml"` matches dotfiles like `.pre-commit-config.yaml`, but `git add --renormalize -- "*.yaml"` does NOT match dotfiles. This causes the existence check to pass but the renormalize to fail. Exclude `yaml` from extension loops since the only `.yaml` files are typically dotfiles; use `yml` instead.
- **Generalized rule for dotfile-only extensions**: Exclude any extension from `git add --renormalize` loops when ALL tracked files of that extension are dotfiles (files whose names start with `.`). To check: run `git ls-files "*.$ext" "**/*.$ext"` and verify whether any results are non-dotfiles. If all matches are dotfiles, exclude that extension.
- **Error messages must be specific**: Indicate which policy was violated (e.g., "Expected LF for shell scripts" vs "Expected CRLF per project policy").
- **git-auto-commit-action `file_pattern`**: This only limits what gets newly added; previously staged files still get committed. Ensure preceding `git add` commands target the same file set.

See the [Git Workflow Robustness skill](./skills/testing/git-workflow-robustness.md) and the [Git Renormalize Patterns skill](./skills/github-actions/git-renormalize-patterns.md) for detailed patterns.

### Forbidden Commands

- **NEVER** run `exec bash`, `exec /bin/bash`, or any variant of `exec` that replaces the current shell. This breaks terminal sessions and causes command failures.
- **NEVER** run just `bash` as a standalone command. This spawns an interactive subshell that hangs indefinitely, blocking all further operations. Always run specific commands directly.
- If you need a new shell environment, spawn a subshell with `bash -c "command"` or simply run commands directly.

### JavaScript/Node.js Practices

- All declared constants and variables must be used or removed.
- Prefer `const` over `let`; use `let` only when reassignment is necessary.
- When adding validation constants (e.g., `VALID_X`), ensure corresponding validation logic uses them.

#### Presence vs Value Validation

When validating input, clearly separate "presence" checks (is the value defined?) from "value" checks (is the defined value valid?). This two-phase approach produces clearer error messages and more maintainable code.

##### Checking for missing values (undefined/null)

```javascript
// CORRECT: Explicit null/undefined check
if (frontmatter[field] === undefined || frontmatter[field] === null) {
  errors.push(`Required field '${field}' is missing`);
}

// ALSO CORRECT: Loose equality shorthand (null == undefined is true)
if (frontmatter[field] == null) {
  errors.push(`Required field '${field}' is missing`);
}

// WRONG: Falsy check conflates missing with empty/invalid
if (!frontmatter[field]) {
  errors.push(`Required field '${field}' is missing`); // Misleading: also triggers for ""
}
```

###### Two-phase validation pattern

```javascript
// Phase 1: Check presence
if (value === undefined || value === null) {
  errors.push("Value is missing");
} else if (value === "") {
  // Phase 2: Check value validity (only if present)
  errors.push("Value is empty");
} else if (!isValidFormat(value)) {
  errors.push("Value has invalid format");
}
```

###### When to use falsy checks (`!x`)

- Only when you explicitly want to catch ALL falsy values: `undefined`, `null`, `0`, `""`, `false`, `NaN`
- Common legitimate uses: `if (!array.length)` to check empty arrays, `if (!str.trim())` to check whitespace-only strings

###### Common pitfalls

- **Empty array is truthy**: `[]` is truthy, so `![]` is `false`. Use `!array.length` instead.
- **Zero is falsy**: `0` is falsy, so `!count` fails when count is legitimately zero.
- **Empty string vs missing**: `!value` treats `""` the same as `undefined`, but they often need different error messages.

### Jest Test Style

For JavaScript tests using Jest:

- **Use `test()` not `it()`**: All tests must use `test()` for consistency with existing tests.
- **Descriptive names**: Test descriptions should clearly state what is being tested.
- **Group with `describe()`**: Use `describe()` blocks to group related tests logically.
- **Accurate categorization**: `describe()` block names must accurately reflect the tests they contain. Do not group "undefined/null" checks under "falsy" if falsy wrong-type values (empty string, 0, false) are tested separately.
- **Test accuracy**: Test names, descriptions, and comments must be factually correct. Never include statements that contradict how JavaScript actually behaves (e.g., claiming "empty array is falsy" when it is truthy).
- **Message content tests**: When code produces user-facing messages, add tests that verify message content matches actual UI/output terminology.

```javascript
// CORRECT
describe("Version Extraction", () => {
  test("should extract valid semver version", () => {
    // ...
  });
});

// WRONG: Do not use it()
it("should extract valid semver version", () => {
  // ...
});
```

### Validation and Error Reporting

- **Separate violation tracking**: When validating multiple policies (e.g., CRLF vs LF line endings, BOM vs encoding), track each violation type in separate collections. This enables clear, specific error messages.
- **Specific error messages**: Error messages must indicate which policy was violated. Instead of "Line ending error in file.sh", report "file.sh: Expected LF line endings (shell script policy), found CRLF".
- **Aggregate reporting**: Report all violations at the end rather than failing on the first error. This gives users a complete picture of what needs fixing.

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
- **MkDocs navigation**: When adding new pages to `docs/`, always add corresponding entries to `mkdocs.yml` nav section.

When documenting new behavior, note the version it was introduced (e.g., "Added in v1.2.0").

See the [Documentation Updates skill](./skills/documentation/documentation-updates.md), [Changelog Management skill](./skills/documentation/changelog-management.md), and [MkDocs Navigation skill](./skills/documentation/mkdocs-navigation.md) for detailed guidance.

### Markdown Formatting Conventions

- **Ordered lists**: Use lazy numbering (`1.`, `1.`, `1.`) not sequential (`1.`, `2.`, `3.`). This matches Prettier behavior and markdownlint MD029 configuration.
- **Fenced code blocks**: Use triple backticks (```), not indented blocks.
- **Nested fences**: When showing code blocks inside code blocks (e.g., documenting markdown), the outer fence must have MORE backticks than inner fences. Use ``for outer when inner uses` ```.
- **Line endings**: CRLF, no UTF-8 BOM (enforced by pre-commit hooks).
- **Headings**: Use ATX-style headings (`#`, `##`, `###`) not underlined style.
- **Line length**: Not enforced. Write naturally; let lines wrap as needed.
- **Inline code spacing**: Always include a space before and after inline code when adjacent to text. Write ``the `code` here`` not ``the`code`here``. This improves readability and matches CommonMark best practices.

See the [Markdown Compatibility skill](./skills/documentation/markdown-compatibility.md) for MkDocs-specific syntax to avoid.

## Changelog Guidelines

Every user-facing change must be added to [the changelog](../CHANGELOG.md):

- Follow the [Keep a Changelog](https://keepachangelog.com/) format.
- Use categories: **Added**, **Changed**, **Deprecated**, **Removed**, **Fixed**, **Security**.
- Include proper version headers with dates (e.g., `## [1.2.0] - 2026-01-22`).
- Link issues and PRs in entries (e.g., "Fixed registration bug (#123)").
- Write entries from the user's perspective, focusing on impact.
- Entries must describe **user-visible** changes only; do not document internal tooling, AI agent guidance, or developer-only changes like `.llm/` skill files.
- Use accurate scope: if only some files/assemblies were changed, name them specifically rather than implying all were changed.

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

## Security & Configuration Tips

- Editor analyzer DLLs are copied into the Unity project by [SetupCscRsp.cs](../Editor/SetupCscRsp.cs); do not commit generated DLLs into this repo.
- Keep public APIs minimal and consistent; avoid breaking changes without a major version bump.
