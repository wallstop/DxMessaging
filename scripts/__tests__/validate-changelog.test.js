/**
 * @fileoverview Tests for validate-changelog.js policy rules.
 */

"use strict";

const fs = require("fs");
const path = require("path");

const {
  parseArgs,
  parsePackageVersion,
  parseChangelog,
  validateStructuralRules,
  isLikelyInternalOnlyEntry,
  detectCategoryMismatch,
  areLikelyMutationPair,
  validateHeuristicRules,
  isLikelyUserVisiblePath,
  parseChangedFilesOutput,
  getChangedFilesFromGitDetails,
  getChangedFilesFromGit,
  validateChangedFilesDiscovery,
  validateCoverageRule,
  validateChangelogPolicy
} = require("../validate-changelog.js");

function buildValidChangelog(version = "2.2.0") {
  return [
    "# Changelog",
    "",
    "## [Unreleased]",
    "",
    "### Added",
    "",
    "- Added message diagnostics in the inspector for users",
    "",
    `## [${version}]`,
    "",
    "### Fixed",
    "",
    "- Fixed a crash when listeners are disposed during dispatch",
    ""
  ].join("\n");
}

function formatViolations(violations) {
  if (violations.length === 0) {
    return "(none)";
  }

  return violations.map((violation) => violation.toString()).join("\n");
}

function expectNoPolicyErrors(result) {
  if (result.errors.length === 0) {
    return;
  }

  throw new Error(
    [
      "Expected changelog policy to produce no errors.",
      `Package version: ${result.packageVersion}`,
      `Sections: ${result.parsedChangelog.sections
        .map((section) => `[${section.version}] at line ${section.line}`)
        .join(", ")}`,
      "Errors:",
      formatViolations(result.errors),
      "Warnings:",
      formatViolations(result.warnings)
    ].join("\n")
  );
}

function expectNoPolicyWarnings(result) {
  if (result.warnings.length === 0) {
    return;
  }

  throw new Error(
    [
      "Expected changelog policy to produce no warnings.",
      `Package version: ${result.packageVersion}`,
      `Unreleased entries: ${
        result.parsedChangelog.entries
          .filter((entry) => entry.version === "Unreleased")
          .map((entry) => `${entry.category} line ${entry.line}: ${entry.text}`)
          .join(" | ") || "(none)"
      }`,
      "Warnings:",
      formatViolations(result.warnings)
    ].join("\n")
  );
}

describe("validate-changelog", () => {
  describe("parseArgs", () => {
    test("parses coverage and changed-file arguments", () => {
      const options = parseArgs([
        "--check-coverage",
        "--changed-file",
        "Runtime/Core/File.cs",
        "Editor\\CustomEditors\\View.cs"
      ]);

      expect(options.checkCoverage).toBe(true);
      expect(options.changedFiles).toEqual([
        "Runtime/Core/File.cs",
        "Editor/CustomEditors/View.cs"
      ]);
    });

    test("throws on unknown flag", () => {
      expect(() => parseArgs(["--does-not-exist"])).toThrow("Unknown argument");
    });
  });

  describe("parsePackageVersion", () => {
    test("returns package version", () => {
      const version = parsePackageVersion('{"name":"pkg","version":"2.2.0"}');
      expect(version).toBe("2.2.0");
    });

    test("throws when package version is missing", () => {
      expect(() => parsePackageVersion('{"name":"pkg"}')).toThrow("missing a non-empty version");
    });
  });

  describe("parseChangelog", () => {
    test("parses sections, categories, and entries", () => {
      const parsed = parseChangelog(buildValidChangelog());

      expect(parsed.sections.map((section) => section.version)).toEqual(["Unreleased", "2.2.0"]);
      expect(parsed.entries).toHaveLength(2);
      expect(parsed.entries[0]).toEqual(
        expect.objectContaining({
          version: "Unreleased",
          category: "Added"
        })
      );
    });

    test("parses wrapped list item lines as one entry", () => {
      const changelog = [
        "# Changelog",
        "",
        "## [Unreleased]",
        "",
        "### Added",
        "",
        "- Inspector overlay now shows cached analyzer report immediately",
        "  and refreshes after domain reload with a status label.",
        "",
        "## [2.2.0]"
      ].join("\n");

      const parsed = parseChangelog(changelog);
      expect(parsed.entries).toHaveLength(1);
      expect(parsed.entries[0].text).toContain("cached analyzer report immediately and refreshes");
    });
  });

  describe("validateStructuralRules", () => {
    test("detects missing Unreleased section", () => {
      const parsed = parseChangelog(
        ["# Changelog", "", "## [2.2.0]", "", "### Added", "", "- Added feature"].join("\n")
      );

      const errors = validateStructuralRules(parsed, "2.2.0");
      expect(errors.some((error) => error.code === "E001")).toBe(true);
    });

    test("detects missing package version section", () => {
      const parsed = parseChangelog(buildValidChangelog("2.1.9"));
      const errors = validateStructuralRules(parsed, "2.2.0");

      expect(errors.some((error) => error.code === "E002")).toBe(true);
    });

    test("detects invalid category", () => {
      const parsed = parseChangelog(
        [
          "# Changelog",
          "",
          "## [Unreleased]",
          "",
          "### Additional",
          "",
          "- typo category",
          "",
          "## [2.2.0]"
        ].join("\n")
      );

      const errors = validateStructuralRules(parsed, "2.2.0");
      expect(errors.some((error) => error.code === "E003")).toBe(true);
    });
  });

  describe("heuristics", () => {
    test("flags likely internal-only entry", () => {
      expect(isLikelyInternalOnlyEntry("Regenerated corrupted meta files in scripts/wiki")).toBe(
        true
      );
      expect(
        isLikelyInternalOnlyEntry("Inspector overlay now shows cached analyzer report to users")
      ).toBe(false);
    });

    test("flags automation and agent phrasing as likely internal-only", () => {
      expect(
        isLikelyInternalOnlyEntry("Added automation instructions for agent prompt routing")
      ).toBe(true);
      expect(isLikelyInternalOnlyEntry("Added large language model context scaffolding")).toBe(
        true
      );
    });

    test("detects category mismatch using entry prefix", () => {
      const mismatch = detectCategoryMismatch({
        category: "Fixed",
        text: "Added npmignore for proper npm publishing"
      });

      expect(mismatch).toBe(true);
    });

    test("detects likely mutation pair via shared symbol", () => {
      const addedEntry = {
        text: "Added `MessageAwareComponent` fallback diagnostics in the inspector."
      };
      const fixedEntry = {
        text: "Fixed `MessageAwareComponent` fallback diagnostics not appearing after reload."
      };

      expect(areLikelyMutationPair(addedEntry, fixedEntry)).toBe(true);
    });

    test("does not detect mutation for unrelated entries", () => {
      const addedEntry = {
        text: "Added dependency injection sample scenes for container integration."
      };
      const fixedEntry = {
        text: "Fixed typo in changelog markdown header ordering."
      };

      expect(areLikelyMutationPair(addedEntry, fixedEntry)).toBe(false);
    });

    test("returns warnings for empty Unreleased section", () => {
      const parsed = parseChangelog(
        [
          "# Changelog",
          "",
          "## [Unreleased]",
          "",
          "## [2.2.0]",
          "",
          "### Added",
          "",
          "- Added feature"
        ].join("\n")
      );

      const warnings = validateHeuristicRules(parsed);
      expect(warnings.some((warning) => warning.code === "W001")).toBe(true);
    });

    test("returns mismatch warning and mutation error for Added+Fixed split in Unreleased", () => {
      const parsed = parseChangelog(
        [
          "# Changelog",
          "",
          "## [Unreleased]",
          "",
          "### Added",
          "",
          "- Added `FooFeature` support for routed dispatch.",
          "",
          "### Fixed",
          "",
          "- Added `FooFeature` null-guard for routed dispatch.",
          "",
          "## [2.2.0]"
        ].join("\n")
      );

      const violations = validateHeuristicRules(parsed);
      expect(violations.some((violation) => violation.code === "W003")).toBe(true);
      expect(violations.some((violation) => violation.code === "E005")).toBe(true);
      expect(
        violations.some((violation) => violation.code === "E005" && violation.severity === "ERROR")
      ).toBe(true);
    });
  });

  describe("coverage checks", () => {
    test.each([
      ["Runtime/Core/MessageBus.cs", true],
      ["Editor/CustomEditors/FallbackEditor.cs", true],
      [
        "SourceGenerators/WallstopStudios.DxMessaging.SourceGenerators/MessageBusEmitterGenerator.cs",
        true
      ],
      [
        "SourceGenerators/WallstopStudios.DxMessaging.Analyzer/Analyzers/MessageAwareComponentBaseCallAnalyzer.cs",
        true
      ],
      ["Samples~/BasicUsage/Example.cs", true],
      ["Runtime/Core/MessageBus.cs.meta", false],
      ["Editor/Analyzers/Analyzer.cs", false],
      ["Editor/Testing/TestHarness.cs", false],
      [
        "SourceGenerators/WallstopStudios.DxMessaging.SourceGenerators.Tests/BaseCallScannerTests.cs",
        false
      ],
      ["SourceGenerators/Directory.Build.props", false],
      ["SourceGenerators/WallstopStudios.DxMessaging.SourceGenerators/bin/Debug/File.dll", false],
      ["scripts/validate-changelog.js", false],
      ["docs/reference/runtime-settings.md", false],
      [".github/workflows/changelog-policy-check.yml", false],
      [".llm/context.md", false],
      ["CHANGELOG.md", false]
    ])("classifies %s as user-visible: %s", (filePath, expected) => {
      expect(isLikelyUserVisiblePath(filePath)).toBe(expected);
    });

    test("parses git output with mixed line endings", () => {
      const output = "Runtime/Core/A.cs\r\nEditor\\CustomEditors\\B.cs\n\n";
      expect(parseChangedFilesOutput(output)).toEqual([
        "Runtime/Core/A.cs",
        "Editor/CustomEditors/B.cs"
      ]);
    });

    test("prefers staged files when staged changes exist", () => {
      const execFileSyncMock = jest.fn((_command, args) => {
        if (args.join(" ") === "diff -M --name-only --cached") {
          return "Runtime/Core/MessageBus.cs\n";
        }

        throw new Error(`Unexpected git command: ${args.join(" ")}`);
      });

      const result = getChangedFilesFromGit(execFileSyncMock, {});
      expect(result).toEqual(["Runtime/Core/MessageBus.cs"]);
    });

    test("uses local unstaged and untracked files outside CI when no staged changes exist", () => {
      const execFileSyncMock = jest.fn((_command, args) => {
        const joined = args.join(" ");

        if (joined === "diff -M --name-only --cached") {
          return "";
        }

        if (joined === "diff -M --name-only") {
          return "Runtime/Core/MessageBus.cs\nEditor/CustomEditors/MessagingComponentEditor.cs\n";
        }

        if (joined === "ls-files --others --exclude-standard") {
          return "SourceGenerators/WallstopStudios.DxMessaging.SourceGenerators.Tests/NewTest.cs\n";
        }

        throw new Error(`Unexpected git command: ${joined}`);
      });

      const result = getChangedFilesFromGit(execFileSyncMock, { CI: "false" });
      expect(result).toEqual([
        "Runtime/Core/MessageBus.cs",
        "Editor/CustomEditors/MessagingComponentEditor.cs",
        "SourceGenerators/WallstopStudios.DxMessaging.SourceGenerators.Tests/NewTest.cs"
      ]);
    });

    test("uses rename-aware PR diff range in CI when staged files are empty", () => {
      const execFileSyncMock = jest.fn((_command, args) => {
        const joined = args.join(" ");

        if (joined === "diff -M --name-only --cached") {
          return "";
        }

        if (joined === "diff -M --name-only origin/main...HEAD") {
          return "Runtime/Core/RenamedMessageBus.cs\n";
        }

        throw new Error(`Unexpected git command: ${joined}`);
      });

      const result = getChangedFilesFromGit(execFileSyncMock, {
        CI: "true",
        GITHUB_ACTIONS: "true",
        GITHUB_EVENT_NAME: "pull_request",
        GITHUB_BASE_REF: "main"
      });

      expect(result).toEqual(["Runtime/Core/RenamedMessageBus.cs"]);
    });

    test.each([
      [
        "pull_request",
        {
          CI: "true",
          GITHUB_ACTIONS: "true",
          GITHUB_EVENT_NAME: "pull_request",
          GITHUB_BASE_REF: "main"
        },
        "diff -M --name-only origin/main...HEAD",
        "pull-request"
      ],
      [
        "push",
        {
          CI: "true",
          GITHUB_ACTIONS: "true",
          GITHUB_EVENT_NAME: "push",
          GITHUB_EVENT_BEFORE: "abc123"
        },
        "diff -M --name-only abc123...HEAD",
        "push"
      ],
      [
        "fallback",
        {
          CI: "true",
          GITHUB_ACTIONS: "true",
          GITHUB_EVENT_NAME: "workflow_dispatch"
        },
        "diff -M --name-only HEAD~1...HEAD",
        "head-fallback"
      ]
    ])("reports %s changed-file source diagnostics", (_name, env, expectedCommand, source) => {
      const execFileSyncMock = jest.fn((_command, args) => {
        const joined = args.join(" ");

        if (joined === "diff -M --name-only --cached") {
          return "";
        }

        if (joined === expectedCommand) {
          return "Runtime/Core/MessageBus.cs\n";
        }

        throw new Error(`Unexpected git command: ${joined}`);
      });

      const result = getChangedFilesFromGitDetails(execFileSyncMock, env);

      expect(result.files).toEqual(["Runtime/Core/MessageBus.cs"]);
      expect(result.source).toBe(source);
      expect(result.attemptedSources).toContain(source);
      expect(result.failures).toHaveLength(0);
    });

    test.each([
      [
        "pull request",
        {
          CI: "true",
          GITHUB_ACTIONS: "true",
          GITHUB_EVENT_NAME: "pull_request",
          GITHUB_BASE_REF: "main"
        },
        "diff -M --name-only origin/main...HEAD",
        "pull-request-empty"
      ],
      [
        "push",
        {
          CI: "true",
          GITHUB_ACTIONS: "true",
          GITHUB_EVENT_NAME: "push",
          GITHUB_EVENT_BEFORE: "abc123"
        },
        "diff -M --name-only abc123...HEAD",
        "push-empty"
      ]
    ])(
      "treats successful empty %s diffs as authoritative",
      (_name, env, expectedCommand, source) => {
        const execFileSyncMock = jest.fn((_command, args) => {
          const joined = args.join(" ");

          if (joined === "diff -M --name-only --cached" || joined === expectedCommand) {
            return "";
          }

          throw new Error(`Unexpected fallback command: ${joined}`);
        });

        const details = getChangedFilesFromGitDetails(execFileSyncMock, env);
        const errors = validateChangedFilesDiscovery(details);

        expect(details.files).toEqual([]);
        expect(details.source).toBe(source);
        expect(details.attemptedSources).not.toContain("head-fallback");
        expect(errors).toHaveLength(0);
      }
    );

    test.each([
      [
        "pull request",
        {
          CI: "true",
          GITHUB_ACTIONS: "true",
          GITHUB_EVENT_NAME: "pull_request",
          GITHUB_BASE_REF: "main"
        },
        "diff -M --name-only origin/main...HEAD",
        "pull-request"
      ],
      [
        "push",
        {
          CI: "true",
          GITHUB_ACTIONS: "true",
          GITHUB_EVENT_NAME: "push",
          GITHUB_EVENT_BEFORE: "abc123"
        },
        "diff -M --name-only abc123...HEAD",
        "push"
      ]
    ])(
      "does not mask a failed %s diff with HEAD fallback",
      (_name, env, expectedCommand, source) => {
        const execFileSyncMock = jest.fn((_command, args) => {
          const joined = args.join(" ");

          if (joined === "diff -M --name-only --cached") {
            return "";
          }

          if (joined === expectedCommand) {
            const error = new Error("missing base ref");
            error.stderr = `fatal: ${source}: no merge base\n`;
            throw error;
          }

          throw new Error(`Unexpected fallback command: ${joined}`);
        });

        const details = getChangedFilesFromGitDetails(execFileSyncMock, env);
        const errors = validateChangedFilesDiscovery(details);

        expect(details).toEqual(
          expect.objectContaining({
            files: [],
            source: "unavailable",
            attemptedSources: ["staged", source]
          })
        );
        expect(errors).toHaveLength(1);
        expect(errors[0].suggestion).toContain(`${source}: git diff -M --name-only`);
      }
    );

    test("reports local Git discovery failures instead of assuming no changes", () => {
      const execFileSyncMock = jest.fn((_command, args) => {
        const error = new Error(`failed ${args.join(" ")}`);
        error.stderr = "fatal: not a git repository\n";
        throw error;
      });

      const details = getChangedFilesFromGitDetails(execFileSyncMock, { CI: "false" });
      const errors = validateChangedFilesDiscovery(details);

      expect(details).toEqual(
        expect.objectContaining({
          files: [],
          source: "unavailable",
          attemptedSources: ["staged"]
        })
      );
      expect(errors).toHaveLength(1);
      expect(errors[0].code).toBe("E006");
    });

    test("reports partial local Git discovery failures", () => {
      const execFileSyncMock = jest.fn((_command, args) => {
        const joined = args.join(" ");

        if (joined === "diff -M --name-only --cached") {
          return "";
        }

        if (joined === "diff -M --name-only") {
          return "Runtime/Core/MessageBus.cs\n";
        }

        const error = new Error("untracked discovery failed");
        error.stderr = "fatal: unable to list untracked files\n";
        throw error;
      });

      const details = getChangedFilesFromGitDetails(execFileSyncMock, { CI: "false" });
      const errors = validateChangedFilesDiscovery(details);

      expect(details).toEqual(
        expect.objectContaining({
          files: ["Runtime/Core/MessageBus.cs"],
          source: "unavailable",
          attemptedSources: ["staged", "unstaged", "untracked"]
        })
      );
      expect(errors).toHaveLength(1);
      expect(errors[0].suggestion).toContain("untracked: git ls-files");
    });

    test("reports Git discovery failures when CI changed-file sources are unavailable", () => {
      const execFileSyncMock = jest.fn((_command, args) => {
        const error = new Error(`failed ${args.join(" ")}`);
        error.stderr = "fatal: bad revision\n";
        throw error;
      });

      const details = getChangedFilesFromGitDetails(execFileSyncMock, {
        CI: "true",
        GITHUB_ACTIONS: "true",
        GITHUB_EVENT_NAME: "pull_request",
        GITHUB_BASE_REF: "main"
      });
      const errors = validateChangedFilesDiscovery(details);

      expect(details).toEqual(
        expect.objectContaining({
          files: [],
          source: "unavailable",
          attemptedSources: ["staged"]
        })
      );
      expect(errors).toHaveLength(1);
      expect(errors[0]).toEqual(
        expect.objectContaining({
          code: "E006",
          severity: "ERROR"
        })
      );
      expect(errors[0].suggestion).toContain("staged: git diff -M --name-only --cached");
    });

    test("fails coverage when user-visible files changed without changelog", () => {
      const errors = validateCoverageRule(
        ["Runtime/Core/MessageBus.cs", "scripts/validate-changelog.js"],
        {
          source: "pull-request",
          attemptedSources: ["staged", "pull-request"],
          failures: []
        }
      );

      expect(errors).toHaveLength(1);
      expect(errors[0].code).toBe("E004");
      expect(errors[0].suggestion).toContain("Changed-file source: pull-request");
    });

    test("passes coverage when changelog is updated", () => {
      const errors = validateCoverageRule(["Runtime/Core/MessageBus.cs", "CHANGELOG.md"]);

      expect(errors).toHaveLength(0);
    });

    test("passes coverage for SourceGenerators test-only changes", () => {
      const errors = validateCoverageRule([
        "SourceGenerators/WallstopStudios.DxMessaging.SourceGenerators.Tests/MessageBusGeneratorTests.cs"
      ]);

      expect(errors).toHaveLength(0);
    });

    test("fails coverage for shipped analyzer/source-generator changes without changelog", () => {
      const errors = validateCoverageRule([
        "SourceGenerators/WallstopStudios.DxMessaging.Analyzer/Analyzers/MessageAwareComponentBaseCallAnalyzer.cs",
        "SourceGenerators/WallstopStudios.DxMessaging.SourceGenerators/MessageBusEmitterGenerator.cs"
      ]);

      expect(errors).toHaveLength(1);
      expect(errors[0].code).toBe("E004");
    });
  });

  describe("integration", () => {
    test("passes valid changelog with no warnings", () => {
      const result = validateChangelogPolicy({
        changelogContent: buildValidChangelog(),
        packageJsonContent: '{"version":"2.2.0"}',
        checkCoverage: true,
        changedFiles: ["CHANGELOG.md", "Runtime/Core/MessageBus.cs"]
      });

      expectNoPolicyErrors(result);
      expectNoPolicyWarnings(result);
    });

    test("returns structural errors and heuristic warnings together", () => {
      const result = validateChangelogPolicy({
        changelogContent: [
          "# Changelog",
          "",
          "## [Unreleased]",
          "",
          "### Additional",
          "",
          "- Regenerated corrupted meta files in scripts/wiki"
        ].join("\n"),
        packageJsonContent: '{"version":"2.2.0"}',
        checkCoverage: true,
        changedFiles: ["Runtime/Core/MessageBus.cs"]
      });

      expect(result.errors.some((error) => error.code === "E002")).toBe(true);
      expect(result.errors.some((error) => error.code === "E003")).toBe(true);
      expect(result.errors.some((error) => error.code === "E004")).toBe(true);
      expect(result.warnings.some((warning) => warning.code === "W002")).toBe(true);
    });

    test("validates repository changelog with no errors", () => {
      const repoRoot = path.resolve(__dirname, "../..");
      const changelogContent = fs.readFileSync(path.join(repoRoot, "CHANGELOG.md"), "utf8");
      const packageJsonContent = fs.readFileSync(path.join(repoRoot, "package.json"), "utf8");

      const result = validateChangelogPolicy({
        changelogContent,
        packageJsonContent,
        checkCoverage: false
      });

      expectNoPolicyErrors(result);
    });
  });
});
