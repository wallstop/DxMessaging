/**
 * @fileoverview Guards for DxMessaging Unity test-assembly SELECTION and the
 * skip-on-empty CI contract.
 *
 * (1) asmdef-discovery must add ONLY DxMessaging-owned test asmdefs to the Unity
 *     `-assemblyNames` list, on every target. A foreign asmdef that happens to
 *     live under `Tests/` (for example one pulled in by an external comparison
 *     package) would not compile against the harness manifest and would fail the
 *     run for a reason unrelated to DxMessaging.
 * (2) When discovery resolves NO DxMessaging assembly for a target, the CI run is
 *     SKIPPED (not hard-failed). That contract spans three files: the compute
 *     action emits `is-empty` -> the workflow gates provision/lock/run on it ->
 *     the verify action treats the absence of results as an expected skip. These
 *     static pins lock the whole contract so a future refactor cannot silently
 *     delete one leg of it and leave the others dangling.
 *
 * (3) The analyzer/source-generator must be registered with the compiler EXACTLY
 *     ONCE in the CI ephemeral project. `Editor/SetupCscRsp.cs` copies those DLLs
 *     into the consuming project's `Assets/` ON PURPOSE (that is how the generator
 *     runs when the package lives under `Packages/`), so the harness must NOT add
 *     a second registration. It previously pre-wrote `Assets/csc.rsp` with `-a:`
 *     entries, which -- together with SetupCscRsp's Assets copy -- handed the SAME
 *     generator to the compiler from two paths (CS0102 / PrecompiledAssemblyException
 *     on 2021/2022 play/standalone). The harness now pre-creates ONLY the Assets
 *     copy and writes no csc.rsp; a runner-side diagnostic names any recurrence.
 *
 * Fast static + in-memory assertions only (no Unity, no spawn). The harness's
 * project generation is exercised end-to-end by unity-test-harness-contract.test.js
 * (the GenerateOnly spawn test); the verify-action skip behavior by
 * verify-unity-results-action.test.js.
 */

"use strict";

const fs = require("fs");
const os = require("os");
const path = require("path");
const yaml = require("js-yaml");

const REPO_ROOT = path.resolve(__dirname, "..", "..");
const COMPUTE_ACTION = path.join(
  REPO_ROOT,
  ".github",
  "actions",
  "compute-unity-assemblies",
  "action.yml"
);
const VERIFY_ACTION = path.join(
  REPO_ROOT,
  ".github",
  "actions",
  "verify-unity-results",
  "action.yml"
);
const UNITY_WORKFLOW = path.join(REPO_ROOT, ".github", "workflows", "unity-tests.yml");
const RUN_CI_TESTS = path.join(REPO_ROOT, "scripts", "unity", "run-ci-tests.ps1");

const asmdef = require("../unity/lib/asmdef-discovery.js");

function read(file) {
  return fs.readFileSync(file, "utf8");
}

// ---------------------------------------------------------------------------
// asmdef-discovery must never add a foreign (non-DxMessaging-owned) asmdef to
// the Unity -assemblyNames list, on any target. enumerateTestAsmdefs reads the
// live Tests/ tree, so these cases build throwaway repos with a controlled
// asmdef set to exercise the ownership gate in isolation.
// ---------------------------------------------------------------------------
describe("asmdef discovery excludes foreign assemblies", () => {
  const tempRoots = [];

  afterAll(() => {
    for (const root of tempRoots) {
      fs.rmSync(root, { recursive: true, force: true });
    }
  });

  /**
   * Build a throwaway repo whose Tests/ tree contains exactly the given asmdefs.
   * @param {Array<{dir: string, name: string, includePlatforms?: string[]}>} entries
   * @returns {string} absolute repo root
   */
  function makeRepo(entries) {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), "dxm-asmdef-foreign-"));
    tempRoots.push(root);
    for (const entry of entries) {
      const dir = path.join(root, "Tests", entry.dir);
      fs.mkdirSync(dir, { recursive: true });
      const json = { name: entry.name };
      if (entry.includePlatforms) {
        json.includePlatforms = entry.includePlatforms;
      }
      fs.writeFileSync(path.join(dir, `${entry.name}.asmdef`), JSON.stringify(json));
    }
    return root;
  }

  // name -> expected isForeign. The foreign names deliberately include one that
  // ALSO matches a category regex (Zenject -> integration) to prove the
  // ownership gate wins over classification.
  const OWNERSHIP_CASES = [
    ["WallstopStudios.DxMessaging.Tests.Runtime", false],
    ["WallstopStudios.DxMessaging.Tests.Editor", false],
    ["AcmeCorp.External.Tests", true],
    ["SomeVendor.Zenject.Tests", true],
    ["DxMessaging.Tests.Runtime", true],
    ["WallstopStudios.DxMessagingExtras.Tests", true]
  ];

  test.each(OWNERSHIP_CASES)("'%s' isForeign === %s", (name, expectedForeign) => {
    const root = makeRepo([{ dir: name, name }]);
    const entries = asmdef.enumerateTestAsmdefs(root);
    expect(entries).toHaveLength(1);
    expect(entries[0].isForeign).toBe(expectedForeign);
  });

  test("a foreign asmdef is dropped from the include list but the owned one is kept", () => {
    const root = makeRepo([
      { dir: "Owned", name: "WallstopStudios.DxMessaging.Tests.Owned" },
      { dir: "Foreign", name: "AcmeCorp.External.Tests" },
      // Foreign AND integration-classified: must still be excluded.
      { dir: "ForeignZenject", name: "Vendor.Zenject.Tests" }
    ]);

    const included = asmdef.defaultIncludeAssemblies(root);
    expect(included).toEqual(["WallstopStudios.DxMessaging.Tests.Owned"]);

    const excluded = asmdef.defaultExcludeAssemblies(root);
    expect(excluded).toEqual(
      expect.arrayContaining(["AcmeCorp.External.Tests", "Vendor.Zenject.Tests"])
    );
    expect(excluded).not.toContain("WallstopStudios.DxMessaging.Tests.Owned");
  });

  test("a target with only foreign asmdefs yields an EMPTY include list (caller skips it)", () => {
    const root = makeRepo([
      { dir: "Foreign1", name: "AcmeCorp.External.Tests" },
      { dir: "Foreign2", name: "OtherVendor.Sample.Tests" }
    ]);
    expect(asmdef.defaultIncludeAssemblies(root)).toEqual([]);
  });
});

// ---------------------------------------------------------------------------
// Skip-on-empty CI contract. When discovery resolves NO DxMessaging assembly
// for a target, the run is SKIPPED (not hard-failed). The contract spans three
// files; these static pins lock every leg so a refactor cannot silently break
// it. The verify-action behavior itself is exercised end-to-end by
// verify-unity-results-action.test.js.
// ---------------------------------------------------------------------------
describe("skip-on-empty CI contract", () => {
  test("compute-unity-assemblies exposes assemblies + is-empty outputs", () => {
    const action = yaml.load(read(COMPUTE_ACTION));
    expect(action.outputs).toBeDefined();
    expect(action.outputs.assemblies).toBeDefined();
    expect(action.outputs["is-empty"]).toBeDefined();
  });

  test("compute-unity-assemblies SKIPS on empty and only hard-fails on a discovery error", () => {
    const run = yaml.load(read(COMPUTE_ACTION)).runs.steps[0].run;
    // Empty branch: notice + is-empty=true + exit 0 (NOT exit 1).
    expect(run).toMatch(/::notice::/);
    expect(run).toMatch(/is-empty=true[\s\S]*exit 0/);
    // The only hard failure is the discovery-script-failed branch.
    expect(run).toMatch(/discovery script failed[\s\S]*exit 1/);
  });

  test("verify-unity-results honors expected-empty (input + DXM_EXPECTED_EMPTY env + early skip)", () => {
    const action = yaml.load(read(VERIFY_ACTION));
    expect(action.inputs["expected-empty"]).toBeDefined();
    const step = action.runs.steps[0];
    expect(String(step.env.DXM_EXPECTED_EMPTY)).toContain("inputs.expected-empty");
    expect(step.run).toContain("$env:DXM_EXPECTED_EMPTY");
    // The verify step gate must stay exactly !cancelled() (pinned elsewhere too);
    // the skip is driven by the env signal, never by widening that gate.
    expect(String(step.if).replace(/\s|\$\{\{|\}\}/g, "")).toBe("!cancelled()");
  });

  test("unity-tests.yml gates provision/acquire/run on is-empty and passes expected-empty to verify", () => {
    const workflow = yaml.load(read(UNITY_WORKFLOW));
    const steps = workflow.jobs["unity-tests"].steps;
    const byName = (name) => steps.find((step) => step.name === name);

    expect(byName("Compute test assembly list").id).toBe("compute");

    const gate = "steps.compute.outputs.is-empty != 'true'";
    for (const name of [
      "Provision Unity Editor",
      "Acquire organization Unity lock",
      "Run Unity Test Runner"
    ]) {
      expect(String(byName(name).if)).toContain(gate);
    }

    const verify = byName("Verify tests actually ran");
    expect(String(verify.with["expected-empty"])).toContain("steps.compute.outputs.is-empty");
  });
});

// ---------------------------------------------------------------------------
// Single analyzer registration in the CI ephemeral project. The harness must
// reproduce the consumer's SINGLE registration (the SetupCscRsp Assets copy) and
// must NOT add a second one via csc.rsp -- that double-registered the generator
// and broke 2021/2022 play/standalone. (The behavioral proof is the GenerateOnly
// spawn test in unity-test-harness-contract.test.js; these are fast static pins
// plus the recurrence diagnostic.)
// ---------------------------------------------------------------------------
describe("single analyzer registration (CI project generation)", () => {
  test("run-ci-tests.ps1 pre-creates the Assets copy and writes NO csc.rsp", () => {
    const run = read(RUN_CI_TESTS);
    expect(run).toContain("function Copy-DxMessagingAnalyzersToAssets");
    expect(run).toContain("Assets\\Plugins\\Editor\\WallstopStudios.DxMessaging");
    // The duplicate-registration channel is gone: no csc.rsp generation.
    expect(run).not.toContain("function New-CscRspContent");
    expect(run).not.toContain("Join-Path $project 'Assets\\csc.rsp'");
    expect(run).not.toContain('-a:`"$analyzerPath`"');
  });

  test("run-ci-tests.ps1 carries the duplicate-analyzer recurrence diagnostic", () => {
    const run = read(RUN_CI_TESTS);
    expect(run).toContain("function Write-DuplicateAnalyzerDiagnostics");
    // Wired into the failure-diagnostics path so a regression is named loudly.
    expect(run).toContain("Write-DuplicateAnalyzerDiagnostics -LogPath");
  });
});
