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
const { spawnSync } = require("child_process");

const { sandboxHostFolderEnv } = require("../lib/spawn-env-sandbox");

const REPO_ROOT = path.resolve(__dirname, "..", "..");
const CI_RUNNER = path.join(REPO_ROOT, "scripts", "unity", "run-ci-tests.ps1");

function pwshAvailable() {
  const probe = spawnSync("pwsh", ["-NoProfile", "-NonInteractive", "-Command", "exit 0"], {
    encoding: "utf8"
  });
  return probe.status === 0;
}

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

    test("writes DxMessaging analyzer csc.rsp before any Unity compile", () => {
      const initializeIndex = content.indexOf("function Initialize-EphemeralProject");
      const cscWriteIndex = content.indexOf(
        "New-CscRspContent -Root $Root -Project $project",
        initializeIndex
      );
      const generateOnlyIndex = content.indexOf("if ($GenerateOnly)");
      const firstUnityLaunchIndex = content.indexOf(
        "Invoke-UnityNativeStartupProbe -EditorPath $UnityEditorPath"
      );

      expect(initializeIndex).toBeGreaterThanOrEqual(0);
      expect(cscWriteIndex).toBeGreaterThan(initializeIndex);
      expect(cscWriteIndex).toBeLessThan(generateOnlyIndex);
      expect(cscWriteIndex).toBeLessThan(firstUnityLaunchIndex);
      expect(content).toContain("Join-Path $project 'Assets\\csc.rsp'");
    });

    test("generated csc.rsp contains DxMessaging source-generator and analyzer entries", () => {
      expect(content).toContain("function New-CscRspContent");
      expect(content).toContain("WallstopStudios.DxMessaging.SourceGenerators.dll");
      expect(content).toContain("WallstopStudios.DxMessaging.Analyzer.dll");
      expect(content).toContain("Missing required DxMessaging analyzer DLL(s)");
      expect(content).toContain("Resolve-FullPath -Path $sourcePath");
      expect(content).toContain('-a:`"$analyzerPath`"');
      expect(content).toContain('-additionalfile:`"$ignoreSidecarRelativePath`"');
    });

    test("reports whether Unity compile logs mention DxMessaging analyzer arguments", () => {
      expect(content).toContain("function Write-CscRspDiagnostics");
      expect(content).toContain("Generated Assets/csc.rsp is missing required");
      expect(content).toContain("Unity compile log mentioned DxMessaging source-generator arg");
      expect(content).toContain("Unity compile log mentioned DxMessaging analyzer arg");
      expect(content).toContain("Write-CscRspDiagnostics -Project $ProjectPath");
    });

    test("GenerateOnly writes Assets/csc.rsp with required DxMessaging analyzer entries", () => {
      if (!pwshAvailable()) {
        console.warn("[unity-harness-contract] pwsh not found; skipping GenerateOnly assertion.");
        return;
      }

      const base = fs.mkdtempSync(path.join(require("os").tmpdir(), "dxm-generate-only-"));
      const repoRoot = path.join(base, "repo");
      const project = path.join(base, "project");
      const artifacts = path.join(base, "artifacts");
      try {
        fs.mkdirSync(path.join(repoRoot, "Runtime"), { recursive: true });
        fs.mkdirSync(path.join(repoRoot, "Editor", "Analyzers"), { recursive: true });
        fs.writeFileSync(path.join(repoRoot, "package.json"), "{}\n", "utf8");
        for (const dllName of [
          "WallstopStudios.DxMessaging.SourceGenerators.dll",
          "WallstopStudios.DxMessaging.Analyzer.dll"
        ]) {
          fs.writeFileSync(path.join(repoRoot, "Editor", "Analyzers", dllName), "stub", "utf8");
        }

        // Hermetic by construction: run-ci-tests.ps1 probes host-default FOLDER
        // vars (`$env:LOCALAPPDATA`, `${env:ProgramFiles}`, ...). Even though
        // -GenerateOnly exits before those probes today, build the spawn env via
        // sandboxHostFolderEnv (empty sandbox dirs under this run's temp base) so
        // this spawn stays inside the hermetic discipline and a future code path
        // that probes the host before -GenerateOnly cannot leak a real install.
        const hostEnvSandbox = path.join(base, "host-env-sandbox");
        const run = spawnSync(
          "pwsh",
          [
            "-NoProfile",
            "-NonInteractive",
            "-File",
            CI_RUNNER,
            "-UnityVersion",
            "2021.3.45f1",
            "-TestMode",
            "editmode",
            "-AssemblyNames",
            "WallstopStudios.DxMessaging.Tests.Editor",
            "-ArtifactsPath",
            artifacts,
            "-RepoRoot",
            repoRoot,
            "-ProjectPath",
            project,
            "-GenerateOnly"
          ],
          {
            env: sandboxHostFolderEnv(process.env, hostEnvSandbox),
            encoding: "utf8",
            maxBuffer: 16 * 1024 * 1024
          }
        );

        expect(run.status).toBe(0);
        const rspPath = path.join(project, "Assets", "csc.rsp");
        expect(fs.existsSync(rspPath)).toBe(true);
        const rsp = fs.readFileSync(rspPath, "utf8");
        const sourceGeneratorPath = path
          .join(repoRoot, "Editor", "Analyzers", "WallstopStudios.DxMessaging.SourceGenerators.dll")
          .replace(/\\/g, "/");
        const analyzerPath = path
          .join(repoRoot, "Editor", "Analyzers", "WallstopStudios.DxMessaging.Analyzer.dll")
          .replace(/\\/g, "/");
        expect(rsp).toContain(`-a:"${sourceGeneratorPath}"`);
        expect(rsp).toContain(`-a:"${analyzerPath}"`);
      } finally {
        fs.rmSync(base, { recursive: true, force: true });
      }
    });

    test("validates real NUnit output instead of trusting Unity process success", () => {
      expect(content).toContain("Test-NUnitResults");
      expect(content).toContain("SelectSingleNode('//test-run')");
      expect(content).toContain("$total -lt 1");
      expect(content).toContain("$failed -gt 0");
      expect(content).toContain("Write-UnityResultFailureDiagnostics");
      expect(content).toContain("Invoke-UnityEditorWithFailureDiagnostics");
      expect(content).toContain("Write-CscRspDiagnostics -Project $Project");
      expect(content).toContain("warning CS8032");
      expect(content).toContain("Unity exited with code 0 but did not write NUnit results");
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

  describe("Unity 2021 compiler compatibility guards", () => {
    const tokenPath = path.join(REPO_ROOT, "Runtime", "Core", "MessageRegistrationToken.cs");
    const compilerHostProjects = [
      [
        "source generator",
        "SourceGenerators/WallstopStudios.DxMessaging.SourceGenerators/WallstopStudios.DxMessaging.SourceGenerators.csproj"
      ],
      [
        "analyzer",
        "SourceGenerators/WallstopStudios.DxMessaging.Analyzer/WallstopStudios.DxMessaging.Analyzer.csproj"
      ]
    ];

    test("runtime sources avoid null-conditional out-var definite-assignment patterns", () => {
      const runtimeFiles = listTrackedRuntimeSources();
      expect(runtimeFiles.length).toBeGreaterThan(0);
      const violations = [];
      for (const relPath of runtimeFiles) {
        const source = fs.readFileSync(path.join(REPO_ROOT, relPath), "utf8");
        const pattern =
          /\?\.\s*[A-Za-z_][A-Za-z0-9_]*\s*\([^)]*\bout\s+(?:var|[A-Za-z_][A-Za-z0-9_<>,.?[\]\s]*)\s+[A-Za-z_][A-Za-z0-9_]*/g;
        if (pattern.test(source)) {
          violations.push(relPath);
        }
      }

      expect(violations).toEqual([]);
      const tokenSource = fs.readFileSync(tokenPath, "utf8");
      expect(tokenSource).toContain(
        "_deregistrations.Remove(handle, out Action deregistrationAction)"
      );
    });

    test.each(compilerHostProjects)(
      "%s production compiler host stays pinned to Roslyn 3.8 for Unity 2021",
      (_label, relPath) => {
        const source = fs.readFileSync(path.join(REPO_ROOT, relPath), "utf8");

        expect(source).toContain(
          "<MicrosoftCodeAnalysisVersion>3.8.0</MicrosoftCodeAnalysisVersion>"
        );
        expect(source).toContain(
          '<PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="3.8.0">'
        );
        expect(source).not.toMatch(/<MicrosoftCodeAnalysisVersion>4\./);
        expect(source).not.toMatch(
          /<PackageReference Include="Microsoft\.CodeAnalysis\.CSharp" Version="4\./
        );
      }
    );
  });
});

function listTrackedRuntimeSources() {
  const result = spawnSync("git", ["ls-files", "Runtime/**/*.cs"], {
    cwd: REPO_ROOT,
    encoding: "utf8"
  });
  expect(result.status).toBe(0);
  return result.stdout
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean);
}
