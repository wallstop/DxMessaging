/**
 * @fileoverview Tests for validate-pre-commit-tooling.js.
 */

"use strict";

const fs = require("fs");
const os = require("os");
const path = require("path");

const {
  parseHookEntries,
  parseHookIds,
  hasRequiredParserPrecheckCommand,
  hasRequiredPackageJsonFormatCommand,
  hasRequiredNodeToolingCommand,
  hasRequiredHookMarkdownCommand,
  hasRequiredLlmMarkdownCommand,
  hasRequiredScriptsCspellCommand,
  hasRequiredWorkflowCspellCommand,
  hasRequiredWorkflowValidationCommand,
  hasRequiredBannerSyncCommand,
  hasRequiredChangelogValidationCommand,
  hasRequiredParserSuiteTestPaths,
  hasNpxInstallPolicy,
  hasManagedJestInvocation,
  hasInlinedPrettierEntry,
  hasInlinedPrettierInvocation,
  hasGuardedFixerRestagePattern,
  validateYamllintPolicy,
  validatePrettierVersionResolution,
  validatePreflightScriptPolicy,
  validatePerfBudget,
  REQUIRED_PRECHECK_PARSER_COMMAND,
  REQUIRED_NODE_TOOLING_COMMAND,
  REQUIRED_HOOK_MARKDOWN_COMMAND,
  REQUIRED_LLM_MARKDOWN_COMMAND,
  REQUIRED_PACKAGE_JSON_FORMAT_COMMAND,
  REQUIRED_SCRIPTS_CSPELL_COMMAND,
  REQUIRED_WORKFLOW_CSPELL_COMMAND,
  REQUIRED_WORKFLOW_VALIDATION_COMMAND,
  REQUIRED_BANNER_SYNC_COMMAND,
  REQUIRED_CHANGELOG_VALIDATION_COMMAND,
  REQUIRED_PARSER_SUITE_HOOK_ID,
  REQUIRED_PARSER_SUITE_TEST_PATHS,
  validateConfigContent,
  validateConfigFile
} = require("../validate-pre-commit-tooling.js");

function requiredPreflightScript({ remove = [] } = {}) {
  const requiredCommands = [
    REQUIRED_NODE_TOOLING_COMMAND,
    REQUIRED_HOOK_MARKDOWN_COMMAND,
    REQUIRED_LLM_MARKDOWN_COMMAND,
    REQUIRED_PACKAGE_JSON_FORMAT_COMMAND,
    "npm run validate:pre-commit-tooling",
    REQUIRED_SCRIPTS_CSPELL_COMMAND,
    REQUIRED_WORKFLOW_CSPELL_COMMAND,
    REQUIRED_WORKFLOW_VALIDATION_COMMAND,
    REQUIRED_BANNER_SYNC_COMMAND,
    REQUIRED_CHANGELOG_VALIDATION_COMMAND,
    REQUIRED_PRECHECK_PARSER_COMMAND
  ];

  const removedCommands = new Set(remove);
  return requiredCommands
    .filter((command) => !removedCommands.has(command))
    .join(" && ");
}

describe("validate-pre-commit-tooling", () => {
  test("parseHookEntries reads folded and inline entry styles", () => {
    const content = [
      "repos:",
      "  - repo: local",
      "    hooks:",
      "      - id: alpha",
      "        entry: node scripts/alpha.js",
      "      - id: beta",
      "        entry: >-",
      "          npx --yes jest --runTestsByPath scripts/__tests__/beta.test.js",
      "          scripts/__tests__/gamma.test.js"
    ].join("\n");

    const hooks = parseHookEntries(content);

    expect(hooks).toHaveLength(2);
    expect(hooks[0]).toEqual(
      expect.objectContaining({
        id: "alpha",
        entry: "node scripts/alpha.js"
      })
    );
    expect(hooks[1]).toEqual(
      expect.objectContaining({
        id: "beta",
        entry:
          "npx --yes jest --runTestsByPath scripts/__tests__/beta.test.js scripts/__tests__/gamma.test.js"
      })
    );
  });

  test("parseHookEntries handles consecutive folded entries", () => {
    const content = [
      "repos:",
      "  - repo: local",
      "    hooks:",
      "      - id: alpha",
      "        entry: >-",
      "          node scripts/run-managed-jest.js --runTestsByPath",
      "          scripts/__tests__/alpha.test.js",
      "      - id: beta",
      "        entry: >-",
      "          node scripts/run-managed-jest.js --runTestsByPath",
      "          scripts/__tests__/beta.test.js"
    ].join("\n");

    const hooks = parseHookEntries(content);

    expect(hooks).toHaveLength(2);
    expect(hooks[0]).toEqual(
      expect.objectContaining({
        id: "alpha",
        entry: "node scripts/run-managed-jest.js --runTestsByPath scripts/__tests__/alpha.test.js"
      })
    );
    expect(hooks[1]).toEqual(
      expect.objectContaining({
        id: "beta",
        entry: "node scripts/run-managed-jest.js --runTestsByPath scripts/__tests__/beta.test.js"
      })
    );
  });

  test("parseHookIds captures hook ids across repos", () => {
    const content = [
      "repos:",
      "  - repo: https://github.com/adrienverge/yamllint",
      "    rev: v1.38.0",
      "    hooks:",
      "      - id: yamllint",
      "  - repo: local",
      "    hooks:",
      "      - id: alpha",
      "        entry: node scripts/alpha.js"
    ].join("\n");

    const ids = parseHookIds(content);

    expect(ids).toEqual(
      expect.arrayContaining([
        expect.objectContaining({ id: "yamllint" }),
        expect.objectContaining({ id: "alpha" })
      ])
    );
  });

  test("hasNpxInstallPolicy rejects npx without explicit policy", () => {
    const okWithYes = hasNpxInstallPolicy("npx --yes jest --runTestsByPath foo.test.js");
    const okWithNo = hasNpxInstallPolicy("npx --no jest --runTestsByPath foo.test.js");
    const bad = hasNpxInstallPolicy("npx jest --runTestsByPath foo.test.js");

    expect(okWithYes).toBe(true);
    expect(okWithNo).toBe(true);
    expect(bad).toBe(false);
  });

  test("hasRequiredParserPrecheckCommand detects parser command as chained step", () => {
    const script = [
      "npm run validate:pre-commit-tooling",
      "npm run check:prettier:hooks",
      REQUIRED_PRECHECK_PARSER_COMMAND
    ].join(" && ");

    expect(hasRequiredParserPrecheckCommand(script)).toBe(true);
  });

  test("hasRequiredParserPrecheckCommand rejects substring-only matches", () => {
    const script = `npm run validate:pre-commit-tooling && echo ${REQUIRED_PRECHECK_PARSER_COMMAND}`;

    expect(hasRequiredParserPrecheckCommand(script)).toBe(false);
  });

  test("hasRequiredPackageJsonFormatCommand detects package.json format precheck step", () => {
    const script = [
      REQUIRED_NODE_TOOLING_COMMAND,
      REQUIRED_HOOK_MARKDOWN_COMMAND,
      REQUIRED_PACKAGE_JSON_FORMAT_COMMAND,
      "npm run check:prettier:hooks",
      REQUIRED_PRECHECK_PARSER_COMMAND
    ].join(" && ");

    expect(hasRequiredPackageJsonFormatCommand(script)).toBe(true);
  });

  test("hasRequiredPackageJsonFormatCommand rejects substring-only matches", () => {
    const script = "npm run validate:pre-commit-tooling && echo npm run check:package-json-format";

    expect(hasRequiredPackageJsonFormatCommand(script)).toBe(false);
  });

  test("hasRequiredNodeToolingCommand detects node tooling health precheck step", () => {
    const script = [
      REQUIRED_NODE_TOOLING_COMMAND,
      REQUIRED_HOOK_MARKDOWN_COMMAND,
      REQUIRED_PACKAGE_JSON_FORMAT_COMMAND,
      REQUIRED_PRECHECK_PARSER_COMMAND
    ].join(" && ");

    expect(hasRequiredNodeToolingCommand(script)).toBe(true);
  });

  test("hasRequiredNodeToolingCommand rejects substring-only matches", () => {
    const script = "npm run validate:pre-commit-tooling && echo npm run validate:node-tooling";

    expect(hasRequiredNodeToolingCommand(script)).toBe(false);
  });

  test("hasRequiredHookMarkdownCommand detects markdown hook parity precheck step", () => {
    const script = [
      REQUIRED_NODE_TOOLING_COMMAND,
      REQUIRED_HOOK_MARKDOWN_COMMAND,
      REQUIRED_PACKAGE_JSON_FORMAT_COMMAND,
      REQUIRED_PRECHECK_PARSER_COMMAND
    ].join(" && ");

    expect(hasRequiredHookMarkdownCommand(script)).toBe(true);
  });

  test("hasRequiredHookMarkdownCommand rejects substring-only matches", () => {
    const script = "npm run validate:pre-commit-tooling && echo npm run validate:hook-markdown";

    expect(hasRequiredHookMarkdownCommand(script)).toBe(false);
  });

  test("hasRequiredLlmMarkdownCommand detects .llm markdown precheck step", () => {
    const script = [
      REQUIRED_NODE_TOOLING_COMMAND,
      REQUIRED_HOOK_MARKDOWN_COMMAND,
      REQUIRED_LLM_MARKDOWN_COMMAND,
      REQUIRED_PACKAGE_JSON_FORMAT_COMMAND,
      REQUIRED_PRECHECK_PARSER_COMMAND
    ].join(" && ");

    expect(hasRequiredLlmMarkdownCommand(script)).toBe(true);
  });

  test("hasRequiredLlmMarkdownCommand rejects substring-only matches", () => {
    const script = "npm run validate:pre-commit-tooling && echo npm run validate:llm-markdown";

    expect(hasRequiredLlmMarkdownCommand(script)).toBe(false);
  });

  test("hasRequiredScriptsCspellCommand detects script cspell command as chained step", () => {
    const script = [
      REQUIRED_NODE_TOOLING_COMMAND,
      REQUIRED_HOOK_MARKDOWN_COMMAND,
      REQUIRED_PACKAGE_JSON_FORMAT_COMMAND,
      REQUIRED_SCRIPTS_CSPELL_COMMAND,
      REQUIRED_PRECHECK_PARSER_COMMAND
    ].join(" && ");

    expect(hasRequiredScriptsCspellCommand(script)).toBe(true);
  });

  test("hasRequiredScriptsCspellCommand rejects substring-only matches", () => {
    const script = "npm run validate:pre-commit-tooling && echo npm run check:cspell:scripts";

    expect(hasRequiredScriptsCspellCommand(script)).toBe(false);
  });

  test("hasRequiredWorkflowCspellCommand detects workflow cspell command as chained step", () => {
    const script = [
      REQUIRED_NODE_TOOLING_COMMAND,
      REQUIRED_HOOK_MARKDOWN_COMMAND,
      REQUIRED_PACKAGE_JSON_FORMAT_COMMAND,
      REQUIRED_SCRIPTS_CSPELL_COMMAND,
      REQUIRED_WORKFLOW_CSPELL_COMMAND,
      REQUIRED_PRECHECK_PARSER_COMMAND
    ].join(" && ");

    expect(hasRequiredWorkflowCspellCommand(script)).toBe(true);
  });

  test("hasRequiredWorkflowCspellCommand rejects substring-only matches", () => {
    const script = "npm run validate:pre-commit-tooling && echo npm run check:workflow-cspell";

    expect(hasRequiredWorkflowCspellCommand(script)).toBe(false);
  });

  test("hasRequiredWorkflowValidationCommand detects workflow validation command", () => {
    const script = [
      REQUIRED_NODE_TOOLING_COMMAND,
      REQUIRED_HOOK_MARKDOWN_COMMAND,
      REQUIRED_PACKAGE_JSON_FORMAT_COMMAND,
      REQUIRED_WORKFLOW_VALIDATION_COMMAND,
      REQUIRED_PRECHECK_PARSER_COMMAND
    ].join(" && ");

    expect(hasRequiredWorkflowValidationCommand(script)).toBe(true);
  });

  test("hasRequiredWorkflowValidationCommand rejects substring-only matches", () => {
    const script = "npm run validate:pre-commit-tooling && echo npm run validate:workflows";

    expect(hasRequiredWorkflowValidationCommand(script)).toBe(false);
  });

  test("hasRequiredBannerSyncCommand detects banner sync command", () => {
    const script = [
      REQUIRED_NODE_TOOLING_COMMAND,
      REQUIRED_HOOK_MARKDOWN_COMMAND,
      REQUIRED_PACKAGE_JSON_FORMAT_COMMAND,
      REQUIRED_BANNER_SYNC_COMMAND,
      REQUIRED_PRECHECK_PARSER_COMMAND
    ].join(" && ");

    expect(hasRequiredBannerSyncCommand(script)).toBe(true);
  });

  test("hasRequiredBannerSyncCommand rejects substring-only matches", () => {
    const script = "npm run validate:pre-commit-tooling && echo npm run check:banner-sync";

    expect(hasRequiredBannerSyncCommand(script)).toBe(false);
  });

  test("hasRequiredChangelogValidationCommand detects changelog validation step", () => {
    const script = [
      REQUIRED_NODE_TOOLING_COMMAND,
      REQUIRED_HOOK_MARKDOWN_COMMAND,
      REQUIRED_PACKAGE_JSON_FORMAT_COMMAND,
      REQUIRED_SCRIPTS_CSPELL_COMMAND,
      REQUIRED_CHANGELOG_VALIDATION_COMMAND,
      REQUIRED_PRECHECK_PARSER_COMMAND
    ].join(" && ");

    expect(hasRequiredChangelogValidationCommand(script)).toBe(true);
  });

  test("hasRequiredChangelogValidationCommand rejects substring-only matches", () => {
    const script =
      "npm run validate:pre-commit-tooling && echo npm run validate:changelog:coverage";

    expect(hasRequiredChangelogValidationCommand(script)).toBe(false);
  });

  test("hasRequiredParserSuiteTestPaths detects required parser regression test path", () => {
    const requiredParserSuitePaths = REQUIRED_PARSER_SUITE_TEST_PATHS;
    const content = [
      "repos:",
      "  - repo: local",
      "    hooks:",
      `      - id: ${REQUIRED_PARSER_SUITE_HOOK_ID}`,
      "        entry: >-",
      "          node scripts/run-managed-jest.js --runTestsByPath scripts/__tests__/generate-skills-index.test.js",
      `          ${requiredParserSuitePaths.join(" ")}`
    ].join("\n");

    expect(hasRequiredParserSuiteTestPaths(content)).toBe(true);
  });

  test("hasRequiredParserSuiteTestPaths rejects missing required parser regression test path", () => {
    const content = [
      "repos:",
      "  - repo: local",
      "    hooks:",
      `      - id: ${REQUIRED_PARSER_SUITE_HOOK_ID}`,
      "        entry: node scripts/run-managed-jest.js --runTestsByPath scripts/__tests__/generate-skills-index.test.js"
    ].join("\n");

    expect(hasRequiredParserSuiteTestPaths(content)).toBe(false);
  });

  test("hasManagedJestInvocation detects unmanaged bare jest command", () => {
    expect(hasManagedJestInvocation("jest --runTestsByPath foo.test.js")).toBe(false);
    expect(
      hasManagedJestInvocation("node scripts/run-managed-jest.js --runTestsByPath foo.test.js")
    ).toBe(true);
    expect(hasManagedJestInvocation("script-tests", "npm run test:scripts")).toBe(false);
    expect(hasManagedJestInvocation("script-tests", "node scripts/run-managed-jest.js")).toBe(true);
  });

  test("hasInlinedPrettierEntry detects the inlined local-bin + pinned-npx-fallback pattern", () => {
    // Round-4: the prettier hook no longer routes through a managed wrapper
    // and instead matches the cspell/markdownlint pattern. The validator
    // checks BOTH that the local bin is referenced (fast path) AND that the
    // npx fallback pins a concrete `prettier@<semver>` (cold-cache parity).
    const inlined =
      "bash -c 'if [ -f node_modules/prettier/bin/prettier.cjs ]; then " +
      'exec node node_modules/prettier/bin/prettier.cjs --write "$@"; ' +
      'else exec npx --yes --package=prettier@3.8.3 prettier --write "$@"; fi\' --';
    expect(hasInlinedPrettierEntry(inlined)).toBe(true);
    // Missing the local bin reference -> cold-cache only -> regression.
    expect(
      hasInlinedPrettierEntry(
        "bash -c 'exec npx --yes --package=prettier@3.8.3 prettier --write \"$@\"' --"
      )
    ).toBe(false);
    // Missing the pinned fallback -> drift risk.
    expect(
      hasInlinedPrettierEntry(
        "bash -c 'exec node node_modules/prettier/bin/prettier.cjs --write \"$@\"' --"
      )
    ).toBe(false);
    // Hook entry that does not invoke prettier at all -> N/A.
    expect(hasInlinedPrettierEntry("node scripts/run-managed-jest.js")).toBe(false);
  });

  test("hasInlinedPrettierEntry also accepts the managed-runner shape (integrity-gate phase)", () => {
    // The integrity-gate phase moved the prettier hook entry from the
    // inlined `bash -c` form to `node scripts/run-managed-prettier.js`.
    // Both forms must be accepted; the managed-runner form performs the
    // same local-vs-npx dispatch internally AND gates on node_modules
    // integrity ahead of either branch.
    expect(
      hasInlinedPrettierEntry("node scripts/run-managed-prettier.js --write")
    ).toBe(true);
    // Wrong invocation - missing the `node` token - is not accepted.
    expect(hasInlinedPrettierEntry("scripts/run-managed-prettier.js --write")).toBe(false);
  });

  test("hasInlinedPrettierInvocation only enforces inlining for the prettier hook id", () => {
    const inlined =
      "bash -c 'if [ -f node_modules/prettier/bin/prettier.cjs ]; then " +
      'exec node node_modules/prettier/bin/prettier.cjs --write "$@"; ' +
      'else exec npx --yes --package=prettier@3.8.3 prettier --write "$@"; fi\' --';
    // Wrong shape on the prettier hook -> rejected.
    expect(hasInlinedPrettierInvocation("prettier", "npx --yes prettier@3.8.3 --write")).toBe(
      false
    );
    // Correct shape on the prettier hook -> accepted.
    expect(hasInlinedPrettierInvocation("prettier", inlined)).toBe(true);
    // Other hooks are not constrained by this rule.
    expect(hasInlinedPrettierInvocation("other-hook", "npx --yes prettier@3.8.3 --write")).toBe(
      true
    );
  });

  // Note: cspell/markdownlint version parity lives in
  // cspell-version-parity.test.js; prettier version parity lives in
  // prettier-version-parity.test.js. The previous "managed wrapper required"
  // assertions were removed in round-3 (cspell, markdownlint) and round-4
  // (prettier) because the wrappers added wall-clock overhead that exceeded
  // the small drift-detection benefit they provided. The pinned
  // `<package>@<version>` literals in the hook entries are validated
  // against package.json directly.

  test("hasGuardedFixerRestagePattern requires diff-guarded git add for C# fixer hook", () => {
    expect(
      hasGuardedFixerRestagePattern(
        "fix-csharp-underscore-methods",
        'bash -c \'node scripts/fix-csharp-underscore-methods.js "$@" && git add "$@"\' --'
      )
    ).toBe(false);

    expect(
      hasGuardedFixerRestagePattern(
        "fix-csharp-underscore-methods",
        'bash -c \'node scripts/fix-csharp-underscore-methods.js "$@" && { git diff --quiet -- "$@" || git add "$@"; }\' --'
      )
    ).toBe(true);

    expect(hasGuardedFixerRestagePattern("another-hook", 'git add "$@"')).toBe(true);
  });

  test("hasGuardedFixerRestagePattern rejects single-quoted $@ variants", () => {
    expect(
      hasGuardedFixerRestagePattern(
        "fix-csharp-underscore-methods",
        "bash -c 'node scripts/fix-csharp-underscore-methods.js \"$@\" && { git diff --quiet -- '\''$@'\'' || git add '\''$@'\''; }' --"
      )
    ).toBe(false);
  });

  test("validateConfigContent reports missing npx policy and unmanaged jest", () => {
    const content = [
      "repos:",
      "  - repo: https://github.com/adrienverge/yamllint",
      "    rev: v1.38.0",
      "    hooks:",
      "      - id: yamllint",
      "        args: [-c, .yamllint.yaml]",
      "  - repo: local",
      "    hooks:",
      "      - id: bad-npx",
      "        entry: npx jest --runTestsByPath scripts/__tests__/a.test.js",
      "      - id: bad-jest",
      "        entry: jest --runTestsByPath scripts/__tests__/b.test.js",
      "      - id: good",
      "        entry: node scripts/run-managed-jest.js --runTestsByPath scripts/__tests__/c.test.js"
    ].join("\n");

    const readFileSyncMock = jest.fn((filePath) => {
      if (filePath === "/tmp/package.json") {
        return JSON.stringify({
          scripts: {
            "preflight:pre-commit": requiredPreflightScript()
          }
        });
      }

      if (filePath === "/tmp/pre-commit.yaml") {
        return [
          "repos:",
          "  - repo: local",
          "    hooks:",
          `      - id: ${REQUIRED_PARSER_SUITE_HOOK_ID}`,
          "        entry: node scripts/run-managed-jest.js --runTestsByPath scripts/__tests__/generate-skills-index.test.js scripts/__tests__/fix-csharp-underscore-methods.test.js scripts/__tests__/validate-changelog.test.js scripts/__tests__/pre-commit-hook-stage-policy.test.js"
        ].join("\n");
      }

      return "";
    });

    const violations = validateConfigContent(content, {
      readFileSyncImpl: readFileSyncMock,
      packageJsonPath: "/tmp/package.json",
      preCommitConfigPath: "/tmp/pre-commit.yaml",
      getConfiguredPrettierSpecFn: () => "prettier@3.8.3",
      getPinnedPrettierSpecFn: () => "prettier@3.8.3"
    });

    // Decoupled assertions so future budget tweaks do not silently change a
    // count that is unrelated to the entry-validation behavior under test.
    const entryLevelViolations = violations.filter(
      (violation) => violation.hookId !== "perf-budget"
    );
    const perfBudgetViolations = violations.filter(
      (violation) => violation.hookId === "perf-budget"
    );

    expect(entryLevelViolations).toHaveLength(3);
    expect(violations.filter((violation) => violation.hookId === "bad-npx")).toHaveLength(2);
    expect(violations.filter((violation) => violation.hookId === "bad-jest")).toHaveLength(1);

    // Round-5: validatePerfBudget reports the total-budget breach AND one
    // per-hook-ceiling violation per offending hook. This synthetic config
    // has three hooks each scoring 5 (jest-at-pre-commit), each above the
    // ceiling (3), so we get 1 total-budget + 3 per-hook = 4 perf-budget
    // entries. Assert the total-budget message is present at least once.
    expect(perfBudgetViolations.length).toBeGreaterThanOrEqual(1);
    expect(perfBudgetViolations.some((v) => /exceeds total budget/.test(v.message))).toBe(true);
    const perHookEntries = perfBudgetViolations.filter((v) =>
      /exceeds per-hook ceiling/.test(v.message)
    );
    expect(perHookEntries).toHaveLength(3);
  });

  test("validatePerfBudget passes for clean config", () => {
    const content = [
      "repos:",
      "  - repo: local",
      "    hooks:",
      "      - id: tiny",
      "        entry: node scripts/tiny.js",
      "        language: system",
      "        files: '\\.js$'"
    ].join("\n");

    const violations = validatePerfBudget(content);
    expect(violations).toHaveLength(0);
  });

  test("validatePerfBudget reports total-budget AND per-hook-ceiling violations when both are exceeded", () => {
    const content = [
      "repos:",
      "  - repo: local",
      "    hooks:",
      "      - id: heavy-a",
      "        entry: node scripts/run-managed-jest.js",
      "        language: system",
      "        files: '\\.js$'",
      "        stages:",
      "          - pre-commit",
      "      - id: heavy-b",
      "        entry: npm pack --dry-run",
      "        language: system",
      "        files: '\\.json$'",
      "        stages:",
      "          - pre-commit",
      "      - id: heavy-c",
      "        entry: dotnet tool run csharpier format",
      "        language: system",
      "        types:",
      "          - c#",
      "        stages:",
      "          - pre-commit"
    ].join("\n");

    const violations = validatePerfBudget(content);
    // 1 total-budget breach + 1 per-hook-ceiling per hook (each at 5 > 3) = 4.
    expect(violations).toHaveLength(4);
    for (const v of violations) {
      expect(v.hookId).toBe("perf-budget");
      expect(v.message).toContain(".llm/skills/performance/git-hook-performance.md");
    }
    expect(violations.filter((v) => /exceeds total budget/.test(v.message))).toHaveLength(1);
    expect(violations.filter((v) => /exceeds per-hook ceiling/.test(v.message))).toHaveLength(3);
  });

  test("validatePerfBudget reports per-hook-ceiling violations even when total budget is met", () => {
    // One hook scoring 5 (npm-spawn) -- the only contributor. Total = 5,
    // under PERF_BUDGET (10), but above PER_HOOK_CEILING (3). The
    // per-hook ceiling MUST fire on its own.
    const content = [
      "repos:",
      "  - repo: local",
      "    hooks:",
      "      - id: solo-npm",
      "        entry: npm install --no-save",
      "        language: system",
      "        files: '\\.json$'",
      "        stages:",
      "          - pre-commit"
    ].join("\n");

    const violations = validatePerfBudget(content);
    // Total budget NOT breached; per-hook ceiling IS.
    expect(violations).toHaveLength(1);
    expect(violations[0].hookId).toBe("perf-budget");
    expect(violations[0].message).toMatch(/exceeds per-hook ceiling/);
    expect(violations[0].message).toContain("solo-npm");
  });

  test("validateYamllintPolicy reports missing yamllint hook", () => {
    const content = [
      "repos:",
      "  - repo: local",
      "    hooks:",
      "      - id: alpha",
      "        entry: node scripts/alpha.js"
    ].join("\n");

    const violations = validateYamllintPolicy(content);

    expect(violations).toHaveLength(1);
    expect(violations[0].message).toContain("Missing required yamllint hook");
  });

  test("validateYamllintPolicy rejects conditional skip pattern", () => {
    const content = [
      "repos:",
      "  - repo: local",
      "    hooks:",
      "      - id: yamllint",
      '        entry: bash -c \'if command -v yamllint >/dev/null 2>&1; then yamllint -c .yamllint.yaml "$@"; else echo "yamllint not installed; skipping"; fi\' --'
    ].join("\n");

    const violations = validateYamllintPolicy(content);

    expect(violations.length).toBeGreaterThanOrEqual(1);
    expect(
      violations.some((violation) =>
        violation.message.includes("must not be conditionally skipped")
      )
    ).toBe(true);
  });

  test("validatePrettierVersionResolution passes when configured and resolved specs match", () => {
    const violations = validatePrettierVersionResolution(
      () => "prettier@3.8.3",
      () => "prettier@3.8.3"
    );

    expect(violations).toHaveLength(0);
  });

  test("validatePrettierVersionResolution reports mismatch between configured and resolved specs", () => {
    const violations = validatePrettierVersionResolution(
      () => "prettier@3.8.3",
      () => "prettier@3.9.0"
    );

    expect(violations).toHaveLength(1);
    expect(violations[0].hookId).toBe("prettier-version");
    expect(violations[0].message).toContain("must match package.json");
  });

  test("validatePrettierVersionResolution reports missing configured spec", () => {
    const violations = validatePrettierVersionResolution(
      () => null,
      () => "prettier@3.8.3"
    );

    expect(violations).toHaveLength(1);
    expect(violations[0].hookId).toBe("prettier-version");
    expect(violations[0].message).toContain("Missing pinned prettier version");
  });

  test("validateConfigFile passes for repository pre-commit config", () => {
    const repoConfigPath = path.resolve(__dirname, "../../.pre-commit-config.yaml");
    const configContent = fs.readFileSync(repoConfigPath, "utf8");
    const hooks = parseHookEntries(configContent);
    const violations = validateConfigFile(repoConfigPath);

    expect(hooks.length).toBeGreaterThan(0);
    expect(violations).toHaveLength(0);
  });

  test("validateConfigContent reports prettier hook missing inlined bin/fallback pair", () => {
    // Round-4: enforcement is "inline the local bin AND pin the npx
    // fallback", not "use the managed wrapper". The hook below names
    // prettier via npx but skips the local-bin fast path, so it should be
    // flagged.
    const content = [
      "repos:",
      "  - repo: https://github.com/adrienverge/yamllint",
      "    rev: v1.38.0",
      "    hooks:",
      "      - id: yamllint",
      "        args: [-c, .yamllint.yaml]",
      "  - repo: local",
      "    hooks:",
      "      - id: prettier",
      "        entry: npx --yes --package=prettier@3.8.3 prettier --write"
    ].join("\n");

    const readFileSyncMock = jest.fn((filePath) => {
      if (filePath === "/tmp/package.json") {
        return JSON.stringify({
          scripts: {
            "preflight:pre-commit": requiredPreflightScript()
          }
        });
      }

      if (filePath === "/tmp/pre-commit.yaml") {
        return [
          "repos:",
          "  - repo: local",
          "    hooks:",
          `      - id: ${REQUIRED_PARSER_SUITE_HOOK_ID}`,
          "        entry: node scripts/run-managed-jest.js --runTestsByPath scripts/__tests__/generate-skills-index.test.js scripts/__tests__/fix-csharp-underscore-methods.test.js scripts/__tests__/validate-changelog.test.js scripts/__tests__/pre-commit-hook-stage-policy.test.js"
        ].join("\n");
      }

      return "";
    });

    const violations = validateConfigContent(content, {
      readFileSyncImpl: readFileSyncMock,
      packageJsonPath: "/tmp/package.json",
      preCommitConfigPath: "/tmp/pre-commit.yaml",
      getConfiguredPrettierSpecFn: () => "prettier@3.8.3",
      getPinnedPrettierSpecFn: () => "prettier@3.8.3"
    });

    expect(violations).toHaveLength(1);
    expect(violations[0].hookId).toBe("prettier");
    expect(violations[0].message).toContain("inline node_modules/prettier/bin/prettier.cjs");
    expect(violations[0].message).toContain("npx --yes --package=prettier@<version>");
  });

  test("package preflight script includes YAML, runtime, and portability gates", () => {
    const packageJsonPath = path.resolve(__dirname, "../../package.json");
    const packageJson = JSON.parse(fs.readFileSync(packageJsonPath, "utf8"));
    const preflightScript = packageJson.scripts["preflight:pre-commit"];

    expect(packageJson.scripts["check:prettier:hooks"]).toContain(
      "node scripts/run-managed-prettier.js --check"
    );
    expect(preflightScript).toContain(REQUIRED_NODE_TOOLING_COMMAND);
    expect(preflightScript).toContain("npm run validate:hook-markdown");
    expect(preflightScript).toContain(REQUIRED_LLM_MARKDOWN_COMMAND);
    expect(preflightScript).toContain(REQUIRED_PACKAGE_JSON_FORMAT_COMMAND);
    expect(preflightScript).toContain("npm run check:prettier:hooks");
    expect(preflightScript).toContain(REQUIRED_SCRIPTS_CSPELL_COMMAND);
    expect(preflightScript).toContain(REQUIRED_WORKFLOW_CSPELL_COMMAND);
    expect(preflightScript).toContain(REQUIRED_WORKFLOW_VALIDATION_COMMAND);
    expect(preflightScript).toContain(REQUIRED_BANNER_SYNC_COMMAND);
    expect(preflightScript).toContain(REQUIRED_CHANGELOG_VALIDATION_COMMAND);
    expect(packageJson.scripts["check:workflow-cspell"]).toContain(
      "cspell@10.0.0"
    );
    expect(packageJson.scripts["check:banner-sync"]).toBe("node scripts/validate-banner.js");
    expect(packageJson.scripts["check:yaml"]).toContain("pre-commit run yamllint --all-files");
    expect(preflightScript).toContain("npm run check:yaml");
    expect(preflightScript).toContain("node scripts/generate-skills-index.js --check");
    expect(preflightScript).toContain("npm run validate:npm-meta");
    expect(preflightScript).toContain(REQUIRED_PRECHECK_PARSER_COMMAND);
    expect(preflightScript).not.toContain("node scripts/run-managed-jest.js --runTestsByPath");
  });

  test("validatePreflightScriptPolicy passes when parser precheck command exists", () => {
    const readFileSyncMock = jest.fn((filePath) => {
      if (filePath === "/tmp/package.json") {
        return JSON.stringify({
          scripts: {
            "preflight:pre-commit": requiredPreflightScript()
          }
        });
      }

      if (filePath === "/tmp/pre-commit.yaml") {
        return [
          "repos:",
          "  - repo: local",
          "    hooks:",
          `      - id: ${REQUIRED_PARSER_SUITE_HOOK_ID}`,
          "        entry: node scripts/run-managed-jest.js --runTestsByPath scripts/__tests__/generate-skills-index.test.js scripts/__tests__/fix-csharp-underscore-methods.test.js scripts/__tests__/validate-changelog.test.js scripts/__tests__/pre-commit-hook-stage-policy.test.js"
        ].join("\n");
      }

      return "";
    });

    const violations = validatePreflightScriptPolicy(
      readFileSyncMock,
      "/tmp/package.json",
      "/tmp/pre-commit.yaml"
    );

    expect(violations).toHaveLength(0);
    expect(readFileSyncMock).toHaveBeenCalledWith("/tmp/package.json", "utf8");
    expect(readFileSyncMock).toHaveBeenCalledWith("/tmp/pre-commit.yaml", "utf8");
  });

  test("validatePreflightScriptPolicy reports missing parser precheck command", () => {
    const readFileSyncMock = jest.fn((filePath) => {
      if (filePath === "/tmp/package.json") {
        return JSON.stringify({
          scripts: {
            "preflight:pre-commit": requiredPreflightScript({
              remove: [REQUIRED_PRECHECK_PARSER_COMMAND]
            })
          }
        });
      }

      if (filePath === "/tmp/pre-commit.yaml") {
        return [
          "repos:",
          "  - repo: local",
          "    hooks:",
          `      - id: ${REQUIRED_PARSER_SUITE_HOOK_ID}`,
          "        entry: node scripts/run-managed-jest.js --runTestsByPath scripts/__tests__/generate-skills-index.test.js scripts/__tests__/fix-csharp-underscore-methods.test.js scripts/__tests__/validate-changelog.test.js scripts/__tests__/pre-commit-hook-stage-policy.test.js"
        ].join("\n");
      }

      return "";
    });

    const violations = validatePreflightScriptPolicy(
      readFileSyncMock,
      "/tmp/package.json",
      "/tmp/pre-commit.yaml"
    );

    expect(violations).toHaveLength(1);
    expect(violations[0].hookId).toBe("preflight-script");
    expect(violations[0].message).toContain(REQUIRED_PRECHECK_PARSER_COMMAND);
  });

  test("validatePreflightScriptPolicy reports missing package.json format precheck command", () => {
    const readFileSyncMock = jest.fn((filePath) => {
      if (filePath === "/tmp/package.json") {
        return JSON.stringify({
          scripts: {
            "preflight:pre-commit": requiredPreflightScript({
              remove: [REQUIRED_PACKAGE_JSON_FORMAT_COMMAND]
            })
          }
        });
      }

      if (filePath === "/tmp/pre-commit.yaml") {
        return [
          "repos:",
          "  - repo: local",
          "    hooks:",
          `      - id: ${REQUIRED_PARSER_SUITE_HOOK_ID}`,
          "        entry: node scripts/run-managed-jest.js --runTestsByPath scripts/__tests__/generate-skills-index.test.js scripts/__tests__/fix-csharp-underscore-methods.test.js scripts/__tests__/validate-changelog.test.js scripts/__tests__/pre-commit-hook-stage-policy.test.js"
        ].join("\n");
      }

      return "";
    });

    const violations = validatePreflightScriptPolicy(
      readFileSyncMock,
      "/tmp/package.json",
      "/tmp/pre-commit.yaml"
    );

    expect(violations).toHaveLength(1);
    expect(violations[0].hookId).toBe("preflight-script");
    expect(violations[0].message).toContain(REQUIRED_PACKAGE_JSON_FORMAT_COMMAND);
  });

  test("validatePreflightScriptPolicy reports missing markdown hook parity precheck command", () => {
    const readFileSyncMock = jest.fn((filePath) => {
      if (filePath === "/tmp/package.json") {
        return JSON.stringify({
          scripts: {
            "preflight:pre-commit": requiredPreflightScript({
              remove: [REQUIRED_HOOK_MARKDOWN_COMMAND]
            })
          }
        });
      }

      if (filePath === "/tmp/pre-commit.yaml") {
        return [
          "repos:",
          "  - repo: local",
          "    hooks:",
          `      - id: ${REQUIRED_PARSER_SUITE_HOOK_ID}`,
          "        entry: node scripts/run-managed-jest.js --runTestsByPath scripts/__tests__/generate-skills-index.test.js scripts/__tests__/fix-csharp-underscore-methods.test.js scripts/__tests__/validate-changelog.test.js scripts/__tests__/pre-commit-hook-stage-policy.test.js"
        ].join("\n");
      }

      return "";
    });

    const violations = validatePreflightScriptPolicy(
      readFileSyncMock,
      "/tmp/package.json",
      "/tmp/pre-commit.yaml"
    );

    expect(violations).toHaveLength(1);
    expect(violations[0].hookId).toBe("preflight-script");
    expect(violations[0].message).toContain(REQUIRED_HOOK_MARKDOWN_COMMAND);
  });

  test("validatePreflightScriptPolicy reports missing .llm markdown precheck command", () => {
    const readFileSyncMock = jest.fn((filePath) => {
      if (filePath === "/tmp/package.json") {
        return JSON.stringify({
          scripts: {
            "preflight:pre-commit": requiredPreflightScript({
              remove: [REQUIRED_LLM_MARKDOWN_COMMAND]
            })
          }
        });
      }

      if (filePath === "/tmp/pre-commit.yaml") {
        return [
          "repos:",
          "  - repo: local",
          "    hooks:",
          `      - id: ${REQUIRED_PARSER_SUITE_HOOK_ID}`,
          "        entry: node scripts/run-managed-jest.js --runTestsByPath scripts/__tests__/generate-skills-index.test.js scripts/__tests__/fix-csharp-underscore-methods.test.js scripts/__tests__/validate-changelog.test.js scripts/__tests__/pre-commit-hook-stage-policy.test.js"
        ].join("\n");
      }

      return "";
    });

    const violations = validatePreflightScriptPolicy(
      readFileSyncMock,
      "/tmp/package.json",
      "/tmp/pre-commit.yaml"
    );

    expect(violations).toHaveLength(1);
    expect(violations[0].hookId).toBe("preflight-script");
    expect(violations[0].message).toContain(REQUIRED_LLM_MARKDOWN_COMMAND);
  });

  test("validatePreflightScriptPolicy reports missing scripts cspell precheck command", () => {
    const readFileSyncMock = jest.fn((filePath) => {
      if (filePath === "/tmp/package.json") {
        return JSON.stringify({
          scripts: {
            "preflight:pre-commit": requiredPreflightScript({
              remove: [REQUIRED_SCRIPTS_CSPELL_COMMAND]
            })
          }
        });
      }

      if (filePath === "/tmp/pre-commit.yaml") {
        return [
          "repos:",
          "  - repo: local",
          "    hooks:",
          `      - id: ${REQUIRED_PARSER_SUITE_HOOK_ID}`,
          "        entry: node scripts/run-managed-jest.js --runTestsByPath scripts/__tests__/generate-skills-index.test.js scripts/__tests__/fix-csharp-underscore-methods.test.js scripts/__tests__/validate-changelog.test.js scripts/__tests__/pre-commit-hook-stage-policy.test.js"
        ].join("\n");
      }

      return "";
    });

    const violations = validatePreflightScriptPolicy(
      readFileSyncMock,
      "/tmp/package.json",
      "/tmp/pre-commit.yaml"
    );

    expect(violations).toHaveLength(1);
    expect(violations[0].hookId).toBe("preflight-script");
    expect(violations[0].message).toContain(REQUIRED_SCRIPTS_CSPELL_COMMAND);
  });

  test("validatePreflightScriptPolicy reports missing workflow cspell precheck command", () => {
    const readFileSyncMock = jest.fn((filePath) => {
      if (filePath === "/tmp/package.json") {
        return JSON.stringify({
          scripts: {
            "preflight:pre-commit": requiredPreflightScript({
              remove: [REQUIRED_WORKFLOW_CSPELL_COMMAND]
            })
          }
        });
      }

      if (filePath === "/tmp/pre-commit.yaml") {
        return [
          "repos:",
          "  - repo: local",
          "    hooks:",
          `      - id: ${REQUIRED_PARSER_SUITE_HOOK_ID}`,
          "        entry: node scripts/run-managed-jest.js --runTestsByPath scripts/__tests__/generate-skills-index.test.js scripts/__tests__/fix-csharp-underscore-methods.test.js scripts/__tests__/validate-changelog.test.js scripts/__tests__/pre-commit-hook-stage-policy.test.js"
        ].join("\n");
      }

      return "";
    });

    const violations = validatePreflightScriptPolicy(
      readFileSyncMock,
      "/tmp/package.json",
      "/tmp/pre-commit.yaml"
    );

    expect(violations).toHaveLength(1);
    expect(violations[0].hookId).toBe("preflight-script");
    expect(violations[0].message).toContain(REQUIRED_WORKFLOW_CSPELL_COMMAND);
  });

  test("validatePreflightScriptPolicy reports missing workflow validation command", () => {
    const readFileSyncMock = jest.fn((filePath) => {
      if (filePath === "/tmp/package.json") {
        return JSON.stringify({
          scripts: {
            "preflight:pre-commit": requiredPreflightScript({
              remove: [REQUIRED_WORKFLOW_VALIDATION_COMMAND]
            })
          }
        });
      }

      if (filePath === "/tmp/pre-commit.yaml") {
        return [
          "repos:",
          "  - repo: local",
          "    hooks:",
          `      - id: ${REQUIRED_PARSER_SUITE_HOOK_ID}`,
          "        entry: node scripts/run-managed-jest.js --runTestsByPath scripts/__tests__/generate-skills-index.test.js scripts/__tests__/fix-csharp-underscore-methods.test.js scripts/__tests__/validate-changelog.test.js scripts/__tests__/pre-commit-hook-stage-policy.test.js"
        ].join("\n");
      }

      return "";
    });

    const violations = validatePreflightScriptPolicy(
      readFileSyncMock,
      "/tmp/package.json",
      "/tmp/pre-commit.yaml"
    );

    expect(violations).toHaveLength(1);
    expect(violations[0].hookId).toBe("preflight-script");
    expect(violations[0].message).toContain(REQUIRED_WORKFLOW_VALIDATION_COMMAND);
  });

  test("validatePreflightScriptPolicy reports missing banner sync command", () => {
    const readFileSyncMock = jest.fn((filePath) => {
      if (filePath === "/tmp/package.json") {
        return JSON.stringify({
          scripts: {
            "preflight:pre-commit": requiredPreflightScript({
              remove: [REQUIRED_BANNER_SYNC_COMMAND]
            })
          }
        });
      }

      if (filePath === "/tmp/pre-commit.yaml") {
        return [
          "repos:",
          "  - repo: local",
          "    hooks:",
          `      - id: ${REQUIRED_PARSER_SUITE_HOOK_ID}`,
          "        entry: node scripts/run-managed-jest.js --runTestsByPath scripts/__tests__/generate-skills-index.test.js scripts/__tests__/fix-csharp-underscore-methods.test.js scripts/__tests__/validate-changelog.test.js scripts/__tests__/pre-commit-hook-stage-policy.test.js"
        ].join("\n");
      }

      return "";
    });

    const violations = validatePreflightScriptPolicy(
      readFileSyncMock,
      "/tmp/package.json",
      "/tmp/pre-commit.yaml"
    );

    expect(violations).toHaveLength(1);
    expect(violations[0].hookId).toBe("preflight-script");
    expect(violations[0].message).toContain(REQUIRED_BANNER_SYNC_COMMAND);
  });

  test("validatePreflightScriptPolicy reports missing changelog validation command", () => {
    const readFileSyncMock = jest.fn((filePath) => {
      if (filePath === "/tmp/package.json") {
        return JSON.stringify({
          scripts: {
            "preflight:pre-commit": requiredPreflightScript({
              remove: [REQUIRED_CHANGELOG_VALIDATION_COMMAND]
            })
          }
        });
      }

      if (filePath === "/tmp/pre-commit.yaml") {
        return [
          "repos:",
          "  - repo: local",
          "    hooks:",
          `      - id: ${REQUIRED_PARSER_SUITE_HOOK_ID}`,
          "        entry: node scripts/run-managed-jest.js --runTestsByPath scripts/__tests__/generate-skills-index.test.js scripts/__tests__/fix-csharp-underscore-methods.test.js scripts/__tests__/validate-changelog.test.js scripts/__tests__/pre-commit-hook-stage-policy.test.js"
        ].join("\n");
      }

      return "";
    });

    const violations = validatePreflightScriptPolicy(
      readFileSyncMock,
      "/tmp/package.json",
      "/tmp/pre-commit.yaml"
    );

    expect(violations).toHaveLength(1);
    expect(violations[0].hookId).toBe("preflight-script");
    expect(violations[0].message).toContain(REQUIRED_CHANGELOG_VALIDATION_COMMAND);
  });

  test("validatePreflightScriptPolicy reports missing parser suite hook", () => {
    const readFileSyncMock = jest.fn((filePath) => {
      if (filePath === "/tmp/package.json") {
        return JSON.stringify({
          scripts: {
            "preflight:pre-commit": requiredPreflightScript()
          }
        });
      }

      if (filePath === "/tmp/pre-commit.yaml") {
        return [
          "repos:",
          "  - repo: local",
          "    hooks:",
          "      - id: alpha",
          "        entry: node scripts/alpha.js"
        ].join("\n");
      }

      return "";
    });

    const violations = validatePreflightScriptPolicy(
      readFileSyncMock,
      "/tmp/package.json",
      "/tmp/pre-commit.yaml"
    );

    expect(violations).toHaveLength(1);
    expect(violations[0].hookId).toBe("preflight-script");
    expect(violations[0].message).toContain(REQUIRED_PARSER_SUITE_HOOK_ID);
  });

  test("validatePreflightScriptPolicy reports missing required parser regression test path", () => {
    const readFileSyncMock = jest.fn((filePath) => {
      if (filePath === "/tmp/package.json") {
        return JSON.stringify({
          scripts: {
            "preflight:pre-commit": requiredPreflightScript()
          }
        });
      }

      if (filePath === "/tmp/pre-commit.yaml") {
        return [
          "repos:",
          "  - repo: local",
          "    hooks:",
          `      - id: ${REQUIRED_PARSER_SUITE_HOOK_ID}`,
          "        entry: node scripts/run-managed-jest.js --runTestsByPath scripts/__tests__/generate-skills-index.test.js"
        ].join("\n");
      }

      return "";
    });

    const violations = validatePreflightScriptPolicy(
      readFileSyncMock,
      "/tmp/package.json",
      "/tmp/pre-commit.yaml"
    );

    expect(violations).toHaveLength(1);
    expect(violations[0].hookId).toBe("preflight-script");
    expect(violations[0].message).toContain(REQUIRED_PARSER_SUITE_TEST_PATHS[0]);
  });

  test("validateConfigFile handles CRLF and lone CR line endings", () => {
    const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), "pre-commit-tooling-"));
    const filePath = path.join(tempDir, ".pre-commit-config.yaml");

    try {
      const content = [
        "repos:",
        "  - repo: local",
        "    hooks:",
        "      - id: bad",
        "        entry: npx jest --runTestsByPath scripts/__tests__/a.test.js"
      ].join("\r");

      fs.writeFileSync(filePath, content, "utf8");
      const violations = validateConfigFile(filePath);

      // 2 entry-level on `bad` (missing npx policy + jest at pre-commit) +
      // 1 yamllint (missing yamllint hook) + 1 preflight-script (missing
      // package.json scripts) + 1 perf-budget per-hook-ceiling (the `bad`
      // hook scores 5 from jest-at-pre-commit, above the per-hook ceiling
      // of 3 even though the total stays under PERF_BUDGET).
      expect(violations).toHaveLength(5);
      expect(violations.filter((violation) => violation.hookId === "bad")).toHaveLength(2);
      expect(violations.some((violation) => violation.hookId === "yamllint")).toBe(true);
      expect(violations.some((violation) => violation.hookId === "preflight-script")).toBe(true);
      const perfBudget = violations.filter((v) => v.hookId === "perf-budget");
      expect(perfBudget).toHaveLength(1);
      expect(perfBudget[0].message).toMatch(/exceeds per-hook ceiling/);
    } finally {
      fs.rmSync(tempDir, { recursive: true, force: true });
    }
  });
});
