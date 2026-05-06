/**
 * @fileoverview Tests for validate-npm-meta.js
 *
 * These tests validate the NPM meta file validation logic:
 * - Directory meta files are properly validated
 * - File meta files are properly validated
 * - Orphaned meta files are detected
 * - Missing meta files are detected
 */

"use strict";

const childProcess = require("child_process");
const { toShellCommand } = require("../lib/shell-command");

const {
  getPackageFiles,
  parseNpmPackJsonOutput,
  parseTarListingOutput,
  validateDevelopmentFilesExcluded,
  validateMetaFilesHaveTargets,
  validateFilesHaveMetaFiles,
  validateNpmMeta
} = require("../validate-npm-meta.js");

describe("validate-npm-meta", () => {
  afterEach(() => {
    jest.restoreAllMocks();
  });

  describe("parseTarListingOutput", () => {
    test("parses package paths with LF line endings", () => {
      const tarOutput = ["package/Runtime/File.cs", "package/Runtime/File.cs.meta", ""].join("\n");

      const files = parseTarListingOutput(tarOutput);
      expect(files).toEqual(["Runtime/File.cs", "Runtime/File.cs.meta"]);
    });

    test("parses package paths with lone CR line endings", () => {
      const tarOutput = ["package/Runtime/File.cs", "package/Runtime/File.cs.meta", ""].join("\r");

      const files = parseTarListingOutput(tarOutput);
      expect(files).toEqual(["Runtime/File.cs", "Runtime/File.cs.meta"]);
    });
  });

  describe("parseNpmPackJsonOutput", () => {
    test("parses npm pack --json files with object entries", () => {
      const packOutput = JSON.stringify([
        {
          files: [{ path: "Runtime/File.cs" }, { path: "Runtime/File.cs.meta" }]
        }
      ]);

      const files = parseNpmPackJsonOutput(packOutput);
      expect(files).toEqual(["Runtime/File.cs", "Runtime/File.cs.meta"]);
    });

    test("parses npm pack --json files with string entries", () => {
      const packOutput = JSON.stringify([
        {
          files: ["Runtime/File.cs", "Runtime/File.cs.meta"]
        }
      ]);

      const files = parseNpmPackJsonOutput(packOutput);
      expect(files).toEqual(["Runtime/File.cs", "Runtime/File.cs.meta"]);
    });

    test("parses npm pack JSON output with CRLF and surrounding whitespace", () => {
      const packOutput =
        "\r\n" +
        JSON.stringify([
          {
            files: [{ path: "Runtime/File.cs" }, { path: "Runtime/File.cs.meta" }]
          }
        ]) +
        "\r\n";

      const files = parseNpmPackJsonOutput(packOutput);
      expect(files).toEqual(["Runtime/File.cs", "Runtime/File.cs.meta"]);
    });

    test("throws when npm pack output is not valid JSON", () => {
      expect(() => parseNpmPackJsonOutput("not-json")).toThrow(
        "Unable to parse npm pack --json output"
      );
    });

    test("throws when npm pack output does not include files", () => {
      const packOutput = JSON.stringify([
        {
          name: "com.wallstop-studios.dxmessaging"
        }
      ]);

      expect(() => parseNpmPackJsonOutput(packOutput)).toThrow("did not include a files list");
    });
  });

  describe("getPackageFiles", () => {
    test("uses cross-platform npm pack invocation and returns file list", () => {
      const spawnSyncSpy = jest.spyOn(childProcess, "spawnSync").mockReturnValue({
        status: 0,
        stdout: JSON.stringify([
          {
            files: [{ path: "Runtime/File.cs" }, { path: "Runtime/File.cs.meta" }]
          }
        ]),
        stderr: ""
      });

      const files = getPackageFiles();

      expect(files).toEqual(["Runtime/File.cs", "Runtime/File.cs.meta"]);
      expect(spawnSyncSpy).toHaveBeenCalledWith(
        toShellCommand("npm"),
        ["pack", "--json", "--dry-run"],
        expect.objectContaining({
          cwd: expect.any(String),
          encoding: "utf8",
          stdio: ["ignore", "pipe", "pipe"]
        })
      );
    });

    test("uses npm.cmd command name on win32", () => {
      expect(toShellCommand("npm", "win32")).toBe("npm.cmd");
      expect(toShellCommand("npx", "win32")).toBe("npx.cmd");
    });

    test("throws when npm pack exits with non-zero status", () => {
      jest.spyOn(childProcess, "spawnSync").mockReturnValue({
        status: 1,
        stdout: "",
        stderr: "simulated failure"
      });

      expect(() => getPackageFiles()).toThrow("npm pack --json --dry-run failed with exit code 1");
    });

    test("throws when npm process spawn fails", () => {
      jest.spyOn(childProcess, "spawnSync").mockReturnValue({
        error: new Error("spawn failed"),
        status: null,
        stdout: "",
        stderr: ""
      });

      expect(() => getPackageFiles()).toThrow("spawn failed");
    });
  });

  describe("validateMetaFilesHaveTargets", () => {
    test("should pass when all .meta files have corresponding files", () => {
      const files = [
        "Runtime/Core/MessageHandler.cs",
        "Runtime/Core/MessageHandler.cs.meta",
        "Editor/Settings.meta",
        "Editor/Settings/DxMessagingSettings.cs",
        "Editor/Settings/DxMessagingSettings.cs.meta"
      ];

      const result = validateMetaFilesHaveTargets(files);

      expect(result.valid).toBe(true);
      expect(result.errors).toHaveLength(0);
    });

    test("should pass when directory .meta files have files in that directory", () => {
      const files = [
        "Runtime.meta",
        "Runtime/Core.meta",
        "Runtime/Core/MessageHandler.cs",
        "Runtime/Core/MessageHandler.cs.meta"
      ];

      const result = validateMetaFilesHaveTargets(files);

      expect(result.valid).toBe(true);
      expect(result.errors).toHaveLength(0);
    });

    test("should fail when .meta file has no corresponding file", () => {
      const files = [
        "Runtime/Core/MessageHandler.cs.meta",
        "Runtime/Core/OtherFile.cs",
        "Runtime/Core/OtherFile.cs.meta"
      ];

      const result = validateMetaFilesHaveTargets(files);

      expect(result.valid).toBe(false);
      expect(result.errors).toHaveLength(1);
      expect(result.errors[0].type).toBe("orphaned-meta");
      expect(result.errors[0].file).toBe("Runtime/Core/MessageHandler.cs.meta");
    });

    test("should fail when directory .meta has no files in directory", () => {
      const files = [
        "Runtime.meta",
        "Runtime/Core.meta",
        "Editor/Settings.meta",
        "Editor/OtherDir.meta",
        "Editor/OtherDir/File.cs",
        "Editor/OtherDir/File.cs.meta"
      ];

      const result = validateMetaFilesHaveTargets(files);

      expect(result.valid).toBe(false);
      expect(result.errors).toHaveLength(2);
      // Runtime.meta and Runtime/Core.meta don't have files, but Editor/Settings.meta and Editor/OtherDir.meta do
      expect(result.errors.map((e) => e.file)).toContain("Runtime/Core.meta");
      expect(result.errors.map((e) => e.file)).toContain("Editor/Settings.meta");
    });

    test("should handle nested directory structures", () => {
      const files = [
        "Runtime.meta",
        "Runtime/Core.meta",
        "Runtime/Core/Messages.meta",
        "Runtime/Core/Messages/StringMessage.cs",
        "Runtime/Core/Messages/StringMessage.cs.meta"
      ];

      const result = validateMetaFilesHaveTargets(files);

      expect(result.valid).toBe(true);
      expect(result.errors).toHaveLength(0);
    });
  });

  describe("validateFilesHaveMetaFiles", () => {
    test("should pass when all files have .meta files", () => {
      const files = [
        "Runtime.meta",
        "Runtime/Core.meta",
        "Runtime/Core/MessageHandler.cs",
        "Runtime/Core/MessageHandler.cs.meta",
        "Editor.meta",
        "Editor/Settings.meta",
        "Editor/Settings/DxMessagingSettings.cs",
        "Editor/Settings/DxMessagingSettings.cs.meta"
      ];

      const result = validateFilesHaveMetaFiles(files);

      expect(result.valid).toBe(true);
      expect(result.errors).toHaveLength(0);
    });

    test("should fail when files are missing .meta files", () => {
      const files = [
        "Runtime.meta",
        "Runtime/Core.meta",
        "Runtime/Core/MessageHandler.cs",
        "Runtime/Core/OtherFile.cs",
        "Runtime/Core/OtherFile.cs.meta"
      ];

      const result = validateFilesHaveMetaFiles(files);

      expect(result.valid).toBe(false);
      expect(result.errors).toHaveLength(1);
      expect(result.errors[0].type).toBe("missing-meta");
      expect(result.errors[0].file).toBe("Runtime/Core/MessageHandler.cs");
    });

    test("should require directory .meta files for included Unity assets", () => {
      const files = [
        "Runtime.meta",
        "Runtime/Core/MessageHandler.cs",
        "Runtime/Core/MessageHandler.cs.meta"
      ];

      const result = validateFilesHaveMetaFiles(files);

      expect(result.valid).toBe(false);
      expect(result.errors).toEqual([
        {
          type: "missing-meta",
          file: "Runtime/Core",
          message: "Directory 'Runtime/Core' is missing its .meta file in the package"
        }
      ]);
    });

    test("should require nested directory .meta files for included Unity assets", () => {
      const files = [
        "Runtime.meta",
        "Runtime/Core.meta",
        "Runtime/Core/Messages/StringMessage.cs",
        "Runtime/Core/Messages/StringMessage.cs.meta"
      ];

      const result = validateFilesHaveMetaFiles(files);

      expect(result.valid).toBe(false);
      expect(result.errors).toEqual([
        {
          type: "missing-meta",
          file: "Runtime/Core/Messages",
          message: "Directory 'Runtime/Core/Messages' is missing its .meta file in the package"
        }
      ]);
    });

    test("should not require directory .meta files for non-Unity and development-only paths", () => {
      const files = [
        "package.json",
        "package-lock.json",
        ".github/workflows/build.yml",
        ".git/HEAD",
        "node_modules/some-package/index.js",
        ".unity-test-project/Packages/manifest.json",
        "scripts/validate-npm-meta.js",
        "Samples~/Mini Combat.meta",
        "Samples~/Mini Combat/Boot.cs",
        "Samples~/Mini Combat/Boot.cs.meta"
      ];

      const result = validateFilesHaveMetaFiles(files);

      expect(result.valid).toBe(true);
      expect(result.errors).toHaveLength(0);
    });

    test("should allow package.json and package-lock.json without .meta", () => {
      const files = [
        "package.json",
        "package-lock.json",
        "Runtime.meta",
        "Runtime/Core.meta",
        "Runtime/Core/MessageHandler.cs",
        "Runtime/Core/MessageHandler.cs.meta"
      ];

      const result = validateFilesHaveMetaFiles(files);

      expect(result.valid).toBe(true);
      expect(result.errors).toHaveLength(0);
    });

    test("should allow .github, .git, and node_modules paths without .meta", () => {
      const files = [
        ".github/workflows/build.yml",
        ".git/HEAD",
        "node_modules/some-package/index.js",
        "Runtime.meta",
        "Runtime/Core.meta",
        "Runtime/Core/MessageHandler.cs",
        "Runtime/Core/MessageHandler.cs.meta"
      ];

      const result = validateFilesHaveMetaFiles(files);

      expect(result.valid).toBe(true);
      expect(result.errors).toHaveLength(0);
    });

    test("should detect multiple missing .meta files", () => {
      const files = [
        "Runtime.meta",
        "Runtime/Core.meta",
        "Runtime/Core/File1.cs",
        "Runtime/Core/File2.cs",
        "Runtime/Core/File3.cs",
        "Runtime/Core/File3.cs.meta"
      ];

      const result = validateFilesHaveMetaFiles(files);

      expect(result.valid).toBe(false);
      expect(result.errors).toHaveLength(2);
      expect(result.errors.map((e) => e.file)).toContain("Runtime/Core/File1.cs");
      expect(result.errors.map((e) => e.file)).toContain("Runtime/Core/File2.cs");
    });

    test("should handle various file extensions", () => {
      const files = [
        "README.md",
        "README.md.meta",
        "LICENSE.md",
        "LICENSE.md.meta",
        "Runtime.meta",
        "Runtime/WallstopStudios.DxMessaging.asmdef",
        "Runtime/WallstopStudios.DxMessaging.asmdef.meta"
      ];

      const result = validateFilesHaveMetaFiles(files);

      expect(result.valid).toBe(true);
      expect(result.errors).toHaveLength(0);
    });
  });

  describe("validateDevelopmentFilesExcluded", () => {
    test("should pass for package runtime contents", () => {
      const files = [
        "package.json",
        "package.json.meta",
        "Runtime/Core/MessageHandler.cs",
        "Runtime/Core/MessageHandler.cs.meta",
        "Editor/Settings/DxMessagingSettings.cs",
        "Editor/Settings/DxMessagingSettings.cs.meta"
      ];

      const result = validateDevelopmentFilesExcluded(files);

      expect(result.valid).toBe(true);
      expect(result.errors).toHaveLength(0);
    });

    test("should reject development-only repository roots", () => {
      const files = [
        ".config/tool.json",
        ".unity-test-project/Packages/manifest.json",
        ".unity-test-project.meta",
        ".unity-test-project/ProjectSettings/ProjectVersion.txt",
        ".llm/context.md",
        ".github/workflows/unity-tests.yml",
        ".husky/pre-commit",
        ".devcontainer/devcontainer.json",
        ".venv/bin/python",
        "Tests/Runtime/SomeTest.cs",
        "Tests.meta",
        "SourceGenerators/WallstopStudios.DxMessaging.SourceGenerators.Tests/GeneratorTests.cs",
        "SourceGenerators/WallstopStudios.DxMessaging.SourceGenerators.Tests.meta",
        "scripts/validate-npm-meta.js",
        "scripts.meta",
        "node_modules/some-package/index.js",
        "node_modules.meta",
        "coverage/lcov.info",
        "coverage.meta",
        "site/index.html",
        "site.meta",
        "progress/notes.md",
        "progress.meta",
        "package-lock.json",
        "package-lock.json.meta",
        "Runtime/Core/MessageHandler.cs",
        "Runtime/Core/MessageHandler.cs.meta"
      ];

      const result = validateDevelopmentFilesExcluded(files);

      expect(result.valid).toBe(false);
      expect(result.errors.map((error) => error.file)).toEqual([
        ".config/tool.json",
        ".unity-test-project/Packages/manifest.json",
        ".unity-test-project.meta",
        ".unity-test-project/ProjectSettings/ProjectVersion.txt",
        ".llm/context.md",
        ".github/workflows/unity-tests.yml",
        ".husky/pre-commit",
        ".devcontainer/devcontainer.json",
        ".venv/bin/python",
        "Tests/Runtime/SomeTest.cs",
        "Tests.meta",
        "SourceGenerators/WallstopStudios.DxMessaging.SourceGenerators.Tests/GeneratorTests.cs",
        "SourceGenerators/WallstopStudios.DxMessaging.SourceGenerators.Tests.meta",
        "scripts/validate-npm-meta.js",
        "scripts.meta",
        "node_modules/some-package/index.js",
        "node_modules.meta",
        "coverage/lcov.info",
        "coverage.meta",
        "site/index.html",
        "site.meta",
        "progress/notes.md",
        "progress.meta",
        "package-lock.json",
        "package-lock.json.meta"
      ]);
      expect(new Set(result.errors.map((error) => error.type))).toEqual(
        new Set(["development-file-in-package"])
      );
    });

    test("should reject development-only root files and tool configuration", () => {
      const files = [
        "AGENTS.md",
        "AGENTS.md.meta",
        "CLAUDE.md",
        "CLAUDE.md.meta",
        "CONTRIBUTING.md",
        "CONTRIBUTING.md.meta",
        "GH-PAGES-PLAN.md",
        "GH-PAGES-PLAN.md.meta",
        "PLAN.md",
        "PLAN.md.meta",
        ".gitattributes",
        ".editorconfig",
        ".prettierrc",
        ".prettierrc.json",
        ".prettierignore",
        ".markdownlint.json",
        ".markdownlint.jsonc",
        ".markdownlint-cli2.jsonc",
        ".markdownlintignore",
        ".yamllint.yaml",
        ".cspell.json",
        ".lychee.toml",
        ".csharpierignore",
        ".csharpierrc.json",
        ".cursorrules",
        ".pre-commit-config.yaml",
        "Runtime/Core/MessageHandler.cs",
        "Runtime/Core/MessageHandler.cs.meta"
      ];

      const result = validateDevelopmentFilesExcluded(files);

      expect(result.valid).toBe(false);
      expect(result.errors.map((error) => error.file)).toEqual(files.slice(0, -2));
    });

    test("should reject documentation build tooling entries", () => {
      const files = [
        "requirements-docs.txt",
        "requirements-docs.txt.meta",
        "mkdocs.yml",
        "mkdocs.yml.meta",
        "docs/hooks.py",
        "docs/hooks.py.meta",
        "docs/__pycache__/hooks.pyc",
        "__pycache__.meta",
        "Runtime/Core/MessageHandler.cs",
        "Runtime/Core/MessageHandler.cs.meta"
      ];

      const result = validateDevelopmentFilesExcluded(files);

      expect(result.valid).toBe(false);
      expect(result.errors.map((error) => error.file)).toEqual(files.slice(0, -2));
    });
  });

  describe("integration scenarios", () => {
    test("should validate a typical Unity package structure", () => {
      const files = [
        "package.json",
        "package.json.meta",
        "README.md",
        "README.md.meta",
        "Runtime.meta",
        "Runtime/Core.meta",
        "Runtime/Core/MessageHandler.cs",
        "Runtime/Core/MessageHandler.cs.meta",
        "Editor.meta",
        "Editor/Settings.meta",
        "Editor/Settings/DxMessagingSettings.cs",
        "Editor/Settings/DxMessagingSettings.cs.meta"
      ];

      const metaResult = validateMetaFilesHaveTargets(files);
      const fileResult = validateFilesHaveMetaFiles(files);

      expect(metaResult.valid).toBe(true);
      expect(fileResult.valid).toBe(true);
    });

    test("should detect both orphaned and missing meta files", () => {
      const files = [
        "Runtime.meta",
        "Runtime/Core.meta",
        "Runtime/Core/MessageHandler.cs",
        "Runtime/Core/MissingFile.cs.meta",
        "Editor.meta",
        "Editor/Settings.meta",
        "Editor/Settings/DxMessagingSettings.cs"
      ];

      const metaResult = validateMetaFilesHaveTargets(files);
      const fileResult = validateFilesHaveMetaFiles(files);

      expect(metaResult.valid).toBe(false);
      expect(metaResult.errors).toHaveLength(1);
      expect(metaResult.errors[0].file).toBe("Runtime/Core/MissingFile.cs.meta");

      expect(fileResult.valid).toBe(false);
      expect(fileResult.errors).toHaveLength(2);
      expect(fileResult.errors.map((e) => e.file)).toContain("Runtime/Core/MessageHandler.cs");
      expect(fileResult.errors.map((e) => e.file)).toContain(
        "Editor/Settings/DxMessagingSettings.cs"
      );
    });

    test("validateNpmMeta should fail when npm pack includes development-only files", () => {
      jest.spyOn(console, "log").mockImplementation(() => {});
      jest.spyOn(childProcess, "spawnSync").mockReturnValue({
        status: 0,
        stdout: JSON.stringify([
          {
            files: [
              { path: "package.json" },
              { path: "package.json.meta" },
              { path: "Runtime.meta" },
              { path: "Runtime/Core.meta" },
              { path: "Runtime/Core/MessageHandler.cs" },
              { path: "Runtime/Core/MessageHandler.cs.meta" },
              { path: ".unity-test-project.meta" },
              { path: ".unity-test-project/Packages/manifest.json" },
              { path: ".unity-test-project/Packages/manifest.json.meta" }
            ]
          }
        ]),
        stderr: ""
      });

      const result = validateNpmMeta();

      expect(result.valid).toBe(false);
      expect(result.errors).toEqual([
        {
          type: "development-file-in-package",
          file: ".unity-test-project.meta",
          message:
            "Development-only file '.unity-test-project.meta' must not be included in the npm package"
        },
        {
          type: "development-file-in-package",
          file: ".unity-test-project/Packages/manifest.json",
          message:
            "Development-only file '.unity-test-project/Packages/manifest.json' must not be included in the npm package"
        },
        {
          type: "development-file-in-package",
          file: ".unity-test-project/Packages/manifest.json.meta",
          message:
            "Development-only file '.unity-test-project/Packages/manifest.json.meta' must not be included in the npm package"
        }
      ]);
    });
  });
});
