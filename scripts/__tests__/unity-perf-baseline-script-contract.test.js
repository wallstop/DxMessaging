"use strict";

const childProcess = require("child_process");
const fs = require("fs");
const os = require("os");
const path = require("path");

const REPO_ROOT = path.resolve(__dirname, "..", "..");
const SCRIPT_PATH = path.join(REPO_ROOT, "scripts", "unity", "capture-perf-baseline.ps1");
const REAL_PWSH = (() => {
  const result = childProcess.spawnSync("pwsh", ["-NoProfile", "-Command", "(Get-Command pwsh).Source"], {
    cwd: REPO_ROOT,
    encoding: "utf8",
  });

  if (result.status !== 0) {
    return null;
  }

  return result.stdout.trim();
})();

function makeTempToolDir() {
  const tempRoot = fs.mkdtempSync(path.join(os.tmpdir(), "dxmsg-perf-baseline-wrapper-"));
  const binDir = path.join(tempRoot, "bin");
  fs.mkdirSync(binDir);

  const pwshMarker = path.join(tempRoot, "fake-pwsh.json");
  const nodeMarker = path.join(tempRoot, "fake-node.json");

  const fakePwsh = [
    "#!/usr/bin/env bash",
    "set -euo pipefail",
    'printf \'{"commit":%s}\\n\' "$("$FAKE_REAL_NODE" -e \'process.stdout.write(JSON.stringify(process.env.DX_PERF_COMMIT || \"\"))\')" > "$FAKE_PWSH_MARKER"',
    'printf "%s\\n" "fake unity stdout for $DX_PERF_COMMIT"',
    'printf "%s\\n" "fake unity stderr for $DX_PERF_COMMIT" >&2',
    'exit "${FAKE_PWSH_EXIT:-0}"',
    "",
  ].join("\n");

  const fakeNode = [
    "#!/usr/bin/env bash",
    "set -euo pipefail",
    'printf "%s\\n" "node invoked" > "$FAKE_NODE_MARKER"',
    'exit "${FAKE_NODE_EXIT:-0}"',
    "",
  ].join("\n");

  fs.writeFileSync(path.join(binDir, "pwsh"), fakePwsh, { mode: 0o755 });
  fs.writeFileSync(path.join(binDir, "node"), fakeNode, { mode: 0o755 });

  return { tempRoot, binDir, pwshMarker, nodeMarker };
}

function runCapture(args, tools, extraEnv = {}) {
  return childProcess.spawnSync(REAL_PWSH, ["-NoProfile", "-File", SCRIPT_PATH, ...args], {
    cwd: REPO_ROOT,
    encoding: "utf8",
    env: {
      ...process.env,
      ...extraEnv,
      FAKE_NODE_MARKER: tools.nodeMarker,
      FAKE_PWSH_MARKER: tools.pwshMarker,
      FAKE_REAL_NODE: process.execPath,
      PATH: `${tools.binDir}${path.delimiter}${process.env.PATH}`,
    },
  });
}

function cleanupPerfArtifacts(commit) {
  const artifactToken = commit.replace(/[^A-Za-z0-9_.-]/g, "-");
  for (const suffix of ["results.xml", "unity-log.txt"]) {
    fs.rmSync(path.join(REPO_ROOT, ".artifacts", `perf-${artifactToken}-${suffix}`), {
      force: true,
    });
  }
}

describe("scripts/unity/capture-perf-baseline.ps1 contract", () => {
  let content;

  beforeAll(() => {
    expect(fs.existsSync(SCRIPT_PATH)).toBe(true);
    content = fs.readFileSync(SCRIPT_PATH, "utf8");
  });

  test("accepts or prompts for a commit and exports DX_PERF_COMMIT", () => {
    expect(content).toContain("[Parameter(Position = 0)]");
    expect(content).toContain("Read-Host 'Commit/ref for DX_PERF_COMMIT'");
    expect(content).toContain("$env:DX_PERF_COMMIT = $Commit");
    expect(content).toContain("$previousDxPerfCommit = $env:DX_PERF_COMMIT");
  });

  test("runs playmode performance benchmarks through the PowerShell Unity runner", () => {
    expect(content).toContain("run-tests.ps1");
    expect(content).toContain("-Platform playmode");
    expect(content).toContain("-IncludePerf");
    expect(content).toContain(
      "DxMessaging.Tests.Runtime.Benchmarks.DispatchThroughputBenchmarks.*"
    );
    expect(content).not.toContain("run-tests.sh");
  });

  test("tees Unity stdout and stderr to a commit-specific artifacts log", () => {
    expect(content).toContain("2>&1 |");
    expect(content).toContain("Tee-Object -FilePath $logPath");
    expect(content).toContain('Join-Path $ArtifactsDir "perf-$artifactToken-unity-log.txt"');
  });

  test("uses commit-specific results and writes the shared baseline CSV", () => {
    expect(content).toContain('Join-Path $ArtifactsDir "perf-$artifactToken-results.xml"');
    expect(content).toContain("[string]$Output = '.artifacts/perf-baseline.csv'");
    expect(content).toContain("$artifactToken = $Commit -replace '[^A-Za-z0-9_.-]', '-'");
  });

  test("extracts baseline rows from both the log and NUnit XML inputs", () => {
    expect(content).toContain("extract-perf-baseline.js");
    expect(content).toContain("'--input', $logPath");
    expect(content).toContain("'--input', $resultsPath");
    expect(content).toContain("'--output', $Output");
    expect(content).toContain("$extractArgs += '--append'");
    expect(content).toContain("$extractArgs += '--replace'");
  });

  test("fails before invoking pwsh when output exists without append or replace", () => {
    if (!REAL_PWSH) {
      return;
    }

    const tools = makeTempToolDir();
    const outputPath = path.join(tools.tempRoot, "existing-baseline.csv");
    fs.writeFileSync(outputPath, "Benchmark,Median\n");

    const result = runCapture(["-Commit", "collision-test", "-Output", outputPath], tools);

    expect(result.status).toBe(2);
    expect(result.stdout).toContain("Output already exists");
    expect(fs.existsSync(tools.pwshMarker)).toBe(false);
    expect(fs.existsSync(tools.nodeMarker)).toBe(false);
  });

  test("exports DX_PERF_COMMIT to pwsh and tees its output to the Unity log", () => {
    if (!REAL_PWSH) {
      return;
    }

    const commit = "behavior-tee-test";
    const tools = makeTempToolDir();
    const outputPath = path.join(tools.tempRoot, "baseline.csv");

    try {
      const result = runCapture(["-Commit", commit, "-Output", outputPath, "-Replace"], tools);
      const logPath = path.join(REPO_ROOT, ".artifacts", `perf-${commit}-unity-log.txt`);

      expect(result.status).toBe(0);
      expect(JSON.parse(fs.readFileSync(tools.pwshMarker, "utf8")).commit).toBe(commit);
      expect(result.stdout).toContain(`fake unity stdout for ${commit}`);
      expect(result.stdout).toContain(`fake unity stderr for ${commit}`);
      expect(fs.readFileSync(logPath, "utf8")).toContain(`fake unity stdout for ${commit}`);
      expect(fs.readFileSync(logPath, "utf8")).toContain(`fake unity stderr for ${commit}`);
      expect(fs.existsSync(tools.nodeMarker)).toBe(true);
    } finally {
      cleanupPerfArtifacts(commit);
    }
  });

  test("propagates a nonzero pwsh exit and does not invoke node", () => {
    if (!REAL_PWSH) {
      return;
    }

    const commit = "failing-unity-test";
    const tools = makeTempToolDir();
    const outputPath = path.join(tools.tempRoot, "baseline.csv");

    try {
      const result = runCapture(["-Commit", commit, "-Output", outputPath, "-Replace"], tools, {
        FAKE_PWSH_EXIT: "37",
      });

      expect(result.status).toBe(37);
      expect(result.stdout).toContain("Unity perf run failed with exit code 37");
      expect(fs.existsSync(tools.pwshMarker)).toBe(true);
      expect(fs.existsSync(tools.nodeMarker)).toBe(false);
    } finally {
      cleanupPerfArtifacts(commit);
    }
  });
});
