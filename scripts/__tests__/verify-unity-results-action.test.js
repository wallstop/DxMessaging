"use strict";

const fs = require("fs");
const os = require("os");
const path = require("path");
const { spawnSync } = require("child_process");
const yaml = require("js-yaml");
const { combinedText } = require("../lib/pwsh-output");

const REPO_ROOT = path.resolve(__dirname, "..", "..");
const ACTION_PATH = path.join(
  REPO_ROOT,
  ".github",
  "actions",
  "verify-unity-results",
  "action.yml"
);

function pwshAvailable() {
  return (
    spawnSync("pwsh", ["-NoProfile", "-NonInteractive", "-Command", "exit 0"], {
      encoding: "utf8"
    }).status === 0
  );
}

const PWSH_PRESENT = pwshAvailable();
const workspaces = [];

afterAll(() => {
  for (const workspace of workspaces) {
    fs.rmSync(workspace, { recursive: true, force: true });
  }
});

function escapePwshDoubleQuoted(value) {
  return value.replace(/`/g, "``").replace(/\$/g, "`$").replace(/"/g, '`"');
}

function getActionRunScript(resultsDir, label = "Unity action test") {
  const action = yaml.load(fs.readFileSync(ACTION_PATH, "utf8"));
  const step = action.runs.steps.find(
    (candidate) => candidate.name === "Verify tests actually ran"
  );
  expect(step).toBeDefined();
  return step.run
    .replaceAll("${{ inputs.results-dir }}", escapePwshDoubleQuoted(resultsDir))
    .replaceAll("${{ inputs.label }}", escapePwshDoubleQuoted(label));
}

function runActionScript(resultsDir, label, cwd) {
  const script = getActionRunScript(resultsDir, label);
  const harness = path.join(cwd, "verify-unity-results-action.ps1");
  fs.writeFileSync(harness, script, "utf8");
  return spawnSync("pwsh", ["-NoProfile", "-NonInteractive", "-File", harness], {
    cwd,
    encoding: "utf8",
    maxBuffer: 16 * 1024 * 1024
  });
}

function makeWorkspace() {
  const workspace = fs.mkdtempSync(path.join(os.tmpdir(), "dxm-verify-unity-results-"));
  workspaces.push(workspace);
  return workspace;
}

function writeResultsXml(resultsDir, attrs) {
  fs.mkdirSync(resultsDir, { recursive: true });
  fs.writeFileSync(
    path.join(resultsDir, "results.xml"),
    `<test-run total="${attrs.total}" passed="${attrs.passed}" failed="${attrs.failed}" skipped="${attrs.skipped}"></test-run>`,
    "utf8"
  );
}

describe("verify-unity-results composite action", () => {
  test("action exists and uses pwsh", () => {
    expect(fs.existsSync(ACTION_PATH)).toBe(true);
    const action = yaml.load(fs.readFileSync(ACTION_PATH, "utf8"));
    expect(action.runs.steps[0].shell).toBe("pwsh");
  });

  if (!PWSH_PRESENT) {
    test.skip.each([
      "missing results directory",
      "provisioning diagnostics without XML",
      "zero tests",
      "failed count",
      "passing results"
    ])("%s", () => {});
    return;
  }

  test("missing results directory lists provisioning diagnostics when available", () => {
    const workspace = makeWorkspace();
    const summaryDir = path.join(workspace, ".artifacts", "unity", "missing", "provisioning");
    fs.mkdirSync(summaryDir, { recursive: true });
    fs.writeFileSync(
      path.join(summaryDir, "ensure-editor-summary.json"),
      JSON.stringify({
        finalClassification: "failed: provisioning",
        provisioningProfile: "EditorOnly"
      }),
      "utf8"
    );

    const result = runActionScript(
      path.join(workspace, ".artifacts", "unity", "does-not-exist"),
      "Missing Dir",
      workspace
    );

    expect(result.status).toBe(1);
    const text = combinedText(result);
    expect(text).toContain("No artifacts directory");
    expect(text).toContain("Provisioning diagnostics files");
    expect(text).toContain("ensure-editor-summary.json");
    expect(text).toContain("classification=failed: provisioning profile=EditorOnly");
  });

  test("directory with provisioning diagnostics but no XML points at the summary", () => {
    const workspace = makeWorkspace();
    const resultsDir = path.join(workspace, ".artifacts", "unity", "cell");
    const summaryDir = path.join(resultsDir, "provisioning");
    fs.mkdirSync(summaryDir, { recursive: true });
    fs.writeFileSync(
      path.join(summaryDir, "ensure-editor-summary.json"),
      JSON.stringify({
        finalClassification: "failed: Android NDK",
        provisioningProfile: "StandaloneWindowsIl2Cpp"
      }),
      "utf8"
    );

    const result = runActionScript(resultsDir, "No XML", workspace);

    expect(result.status).toBe(1);
    const text = combinedText(result);
    expect(text).toContain("No NUnit results.xml");
    expect(text).toContain("Provisioning summary:");
    expect(text).toContain("ensure-editor-summary.json");
    expect(text).toContain("classification=failed: Android NDK profile=StandaloneWindowsIl2Cpp");
  });

  test("zero-test XML fails with an explicit annotation", () => {
    const workspace = makeWorkspace();
    const resultsDir = path.join(workspace, ".artifacts", "unity", "zero");
    writeResultsXml(resultsDir, { total: 0, passed: 0, failed: 0, skipped: 0 });

    const result = runActionScript(resultsDir, "Zero Tests", workspace);

    expect(result.status).toBe(1);
    expect(combinedText(result)).toContain("0 tests ran for Zero Tests");
  });

  test("failed-count XML fails and reports the failed count", () => {
    const workspace = makeWorkspace();
    const resultsDir = path.join(workspace, ".artifacts", "unity", "failed");
    writeResultsXml(resultsDir, { total: 2, passed: 1, failed: 1, skipped: 0 });

    const result = runActionScript(resultsDir, "Failed Tests", workspace);

    expect(result.status).toBe(1);
    expect(combinedText(result)).toContain("Failed Tests reported 1 failed test(s)");
  });

  test("passing XML reports totals and succeeds", () => {
    const workspace = makeWorkspace();
    const resultsDir = path.join(workspace, ".artifacts", "unity", "passed");
    writeResultsXml(resultsDir, { total: 3, passed: 3, failed: 0, skipped: 0 });

    const result = runActionScript(resultsDir, "Passed Tests", workspace);

    expect(result.status).toBe(0);
    expect(combinedText(result)).toContain("Passed Tests: total=3 passed=3 failed=0 skipped=0");
  });
});
