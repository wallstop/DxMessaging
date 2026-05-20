/**
 * @fileoverview Contract tests for the .unity-test-project/ test harness.
 *
 * Phase 1 of the Unity headless workflow lays down a Unity project under
 * .unity-test-project/ that pulls the package via `file:../..` and exposes
 * the package's Tests/ asmdefs through `testables`. Several downstream
 * artifacts (run-tests.sh, the GitHub Actions workflow's cache key)
 * hard-code paths/values from these files, so the goal of this suite is to
 * make any silent rename or shape drift fail loudly at the JS-test layer.
 */

"use strict";

const fs = require("fs");
const path = require("path");
const yaml = require("js-yaml");

const REPO_ROOT = path.resolve(__dirname, "..", "..");
const TEST_PROJECT = path.join(REPO_ROOT, ".unity-test-project");

describe("unity test harness contract (.unity-test-project/)", () => {
  describe("Packages/manifest.json", () => {
    const manifestPath = path.join(TEST_PROJECT, "Packages", "manifest.json");

    test("manifest.json exists and parses as JSON", () => {
      expect(fs.existsSync(manifestPath)).toBe(true);
      const parsed = JSON.parse(fs.readFileSync(manifestPath, "utf8"));
      expect(parsed).toEqual(expect.any(Object));
    });

    test("declares the package as `file:../..` (so the harness pulls the workspace package)", () => {
      const parsed = JSON.parse(fs.readFileSync(manifestPath, "utf8"));
      expect(parsed.dependencies).toBeDefined();
      expect(parsed.dependencies["com.wallstop-studios.dxmessaging"]).toBe("file:../..");
    });

    test("declares Performance Testing package required by perf asmdefs", () => {
      const parsed = JSON.parse(fs.readFileSync(manifestPath, "utf8"));
      expect(parsed.dependencies["com.unity.test-framework.performance"]).toBe("3.4.2");
    });

    test("`testables` array contains the package id", () => {
      const parsed = JSON.parse(fs.readFileSync(manifestPath, "utf8"));
      expect(Array.isArray(parsed.testables)).toBe(true);
      expect(parsed.testables).toContain("com.wallstop-studios.dxmessaging");
    });
  });

  describe("Packages/packages-lock.json", () => {
    const lockPath = path.join(TEST_PROJECT, "Packages", "packages-lock.json");

    test("packages-lock.json exists and parses as JSON", () => {
      expect(fs.existsSync(lockPath)).toBe(true);
      const raw = fs.readFileSync(lockPath, "utf8");
      // Throw a friendly error on parse failure rather than the bare JSON one.
      let parsed;
      try {
        parsed = JSON.parse(raw);
      } catch (error) {
        throw new Error(`packages-lock.json is not valid JSON: ${error.message}`);
      }
      expect(parsed).toEqual(expect.any(Object));
    });

    test("locks Performance Testing package required by perf asmdefs", () => {
      const parsed = JSON.parse(fs.readFileSync(lockPath, "utf8"));
      expect(parsed.dependencies["com.unity.test-framework.performance"]).toEqual(
        expect.objectContaining({
          version: "3.4.2",
          source: "registry"
        })
      );
    });
  });

  describe("ProjectSettings/ProjectVersion.txt", () => {
    const versionPath = path.join(TEST_PROJECT, "ProjectSettings", "ProjectVersion.txt");

    test("ProjectVersion.txt exists and contains m_EditorVersion", () => {
      expect(fs.existsSync(versionPath)).toBe(true);
      const content = fs.readFileSync(versionPath, "utf8");
      expect(content).toMatch(/m_EditorVersion:/);
    });

    test("editor version matches one of the disabled unity-tests.yml template matrix entries", () => {
      const content = fs.readFileSync(versionPath, "utf8");
      const match = content.match(/m_EditorVersion:\s*(\S+)/);
      expect(match).not.toBeNull();
      const projectVersion = match[1].trim();

      const workflowPath = path.join(REPO_ROOT, ".github", "workflows-disabled", "unity-tests.yml");
      const workflowText = fs.readFileSync(workflowPath, "utf8");
      // The matrix is generated dynamically inside a shell heredoc, so a
      // structural YAML walk would skip those values; the canonical list
      // is encoded as a literal JSON array on one line. Grep for any
      // version literal that looks like a Unity tag and verify the
      // ProjectVersion is among them.
      const versionRegex = /\d+\.\d+\.\d+f\d+/g;
      const matrixVersions = new Set(workflowText.match(versionRegex) || []);
      expect(matrixVersions.size).toBeGreaterThan(0);
      expect(matrixVersions).toContain(projectVersion);
    });
  });

  describe("default runtime test asmdef", () => {
    const runtimeAsmdefPath = path.join(
      REPO_ROOT,
      "Tests",
      "Runtime",
      "WallstopStudios.DxMessaging.Tests.Runtime.asmdef"
    );

    test("does not reference optional DI integration assemblies", () => {
      const parsed = JSON.parse(fs.readFileSync(runtimeAsmdefPath, "utf8"));
      expect(parsed.references).toEqual(expect.any(Array));
      expect(parsed.references).not.toContain("WallstopStudios.DxMessaging.Reflex");
      expect(parsed.references).not.toContain("WallstopStudios.DxMessaging.VContainer");
      expect(parsed.references).not.toContain("WallstopStudios.DxMessaging.Zenject");
    });
  });

  describe("default benchmark asmdefs", () => {
    const externalComparisonRefs = ["Zenject", "MessagePipe", "UniRx", "UniTask"];

    test.each([
      ["Tests/Editor/Benchmarks/WallstopStudios.DxMessaging.Tests.00.Editor.Benchmarks.asmdef"],
      ["Tests/Editor/Allocations/WallstopStudios.DxMessaging.Tests.Editor.Allocations.asmdef"]
    ])("%s does not require external comparison packages", (relPath) => {
      const parsed = JSON.parse(fs.readFileSync(path.join(REPO_ROOT, relPath), "utf8"));
      for (const externalRef of externalComparisonRefs) {
        expect(parsed.references).not.toContain(externalRef);
      }
    });
  });

  describe("comparison benchmark asmdef", () => {
    const comparisonAsmdefPath = path.join(
      REPO_ROOT,
      "Tests",
      "Editor",
      "Comparisons",
      "WallstopStudios.DxMessaging.Tests.00.Editor.Comparisons.asmdef"
    );

    test("requires external comparison package symbols before Unity compiles it", () => {
      const parsed = JSON.parse(fs.readFileSync(comparisonAsmdefPath, "utf8"));
      expect(parsed.defineConstraints).toEqual(
        expect.arrayContaining([
          "MESSAGEPIPE_PRESENT",
          "UNIRX_PRESENT",
          "ZENJECT_PRESENT",
          "UNITASK_PRESENT"
        ])
      );
      expect(parsed.versionDefines).toEqual(
        expect.arrayContaining([
          expect.objectContaining({
            name: "com.cysharp.messagepipe",
            define: "MESSAGEPIPE_PRESENT"
          }),
          expect.objectContaining({ name: "com.svermeulen.extenject", define: "ZENJECT_PRESENT" }),
          expect.objectContaining({ name: "com.cysharp.unitask", define: "UNITASK_PRESENT" })
        ])
      );
    });
  });

  // Sanity: unused yaml import elsewhere would be dead weight; reference it
  // here so removing the dependency without updating other suites still trips
  // CI early. (Other tests use yaml extensively.)
  test("js-yaml is available for downstream YAML-shape suites", () => {
    expect(typeof yaml.load).toBe("function");
  });
});
