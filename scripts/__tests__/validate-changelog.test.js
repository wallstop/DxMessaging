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
  getChangedFilesFromGit,
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
    test("recognizes user-visible paths", () => {
      expect(isLikelyUserVisiblePath("Runtime/Core/MessageBus.cs")).toBe(true);
      expect(isLikelyUserVisiblePath("Editor/CustomEditors/FallbackEditor.cs")).toBe(true);
      expect(
        isLikelyUserVisiblePath(
          "SourceGenerators/WallstopStudios.DxMessaging.SourceGenerators/MessageBusEmitterGenerator.cs"
        )
      ).toBe(true);
      expect(
        isLikelyUserVisiblePath(
          "SourceGenerators/WallstopStudios.DxMessaging.Analyzer/Analyzers/MessageAwareComponentBaseCallAnalyzer.cs"
        )
      ).toBe(true);
      expect(isLikelyUserVisiblePath("Runtime/Core/MessageBus.cs.meta")).toBe(false);
      expect(isLikelyUserVisiblePath("Editor/Analyzers/Analyzer.cs")).toBe(false);
      expect(
        isLikelyUserVisiblePath(
          "SourceGenerators/WallstopStudios.DxMessaging.SourceGenerators.Tests/BaseCallScannerTests.cs"
        )
      ).toBe(false);
      expect(isLikelyUserVisiblePath("SourceGenerators/Directory.Build.props")).toBe(false);
      expect(isLikelyUserVisiblePath("scripts/validate-changelog.js")).toBe(false);
      expect(isLikelyUserVisiblePath("CHANGELOG.md")).toBe(false);
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

    test("fails coverage when user-visible files changed without changelog", () => {
      const errors = validateCoverageRule([
        "Runtime/Core/MessageBus.cs",
        "scripts/validate-changelog.js"
      ]);

      expect(errors).toHaveLength(1);
      expect(errors[0].code).toBe("E004");
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

      expect(result.errors).toHaveLength(0);
      expect(result.warnings).toHaveLength(0);
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

      expect(result.errors).toHaveLength(0);
      expect(result.warnings).toHaveLength(0);
    });
  });
});
