/**
 * @fileoverview Contract tests for the generated Unity package test harness.
 *
 * CI creates an ephemeral Unity project under .artifacts/ that imports this
 * repo as a UPM package and exposes the package's Tests/ asmdefs through
 * `testables`. The repository itself remains a package, not a checked-in
 * Unity project.
 */

"use strict";

const fs = require("fs");
const path = require("path");
const yaml = require("js-yaml");

const REPO_ROOT = path.resolve(__dirname, "..", "..");
const CI_RUNNER = path.join(REPO_ROOT, "scripts", "unity", "run-ci-tests.ps1");

describe("generated Unity test harness contract", () => {
  describe("scripts/unity/run-ci-tests.ps1", () => {
    let content;

    beforeAll(() => {
      expect(fs.existsSync(CI_RUNNER)).toBe(true);
      content = fs.readFileSync(CI_RUNNER, "utf8");
    });

    test("creates the Unity project only under .artifacts", () => {
      expect(content).toContain(".artifacts\\unity\\projects\\$Version-$Mode");
      expect(content).toContain("Initialize-EphemeralProject");
      expect(content).not.toContain(".unity-test-project");
    });

    test("generates a minimal manifest that imports this repo as the package under test", () => {
      expect(content).toContain("'com.unity.test-framework'");
      expect(content).toContain("'com.unity.test-framework.performance'");
      expect(content).toContain("'com.wallstop-studios.dxmessaging'");
      expect(content).toContain('"file:$packagePath"');
      expect(content).toContain("testables = @($PackageName)");
    });

    test("configures standalone Windows IL2CPP in the generated project", () => {
      expect(content).toContain("DxmCiTestConfigurator");
      expect(content).toContain("BuildTarget.StandaloneWindows64");
      expect(content).toContain("ScriptingImplementation.IL2CPP");
    });

    test("validates real NUnit output instead of trusting Unity process success", () => {
      expect(content).toContain("Test-NUnitResults");
      expect(content).toContain("SelectSingleNode('//test-run')");
      expect(content).toContain("$total -lt 1");
      expect(content).toContain("$failed -gt 0");
    });

    test("wires Unity Accelerator and UPM caches without mutating package source", () => {
      expect(content).toContain("UNITY_ACCELERATOR_ENDPOINT");
      expect(content).toContain("-cacheServerEndpoint");
      expect(content).toContain("UPM_CACHE_ROOT");
      expect(content).toContain("UPM_NPM_CACHE_PATH");
      expect(content).toContain("LOCALAPPDATA");
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
