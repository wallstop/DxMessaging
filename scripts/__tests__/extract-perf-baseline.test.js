"use strict";

const fs = require("fs");
const os = require("os");
const path = require("path");
const childProcess = require("child_process");

const { CSV_HEADER, buildCsv, extractRows } = require("../unity/extract-perf-baseline.js");

const SCRIPT = path.resolve(__dirname, "..", "unity", "extract-perf-baseline.js");

describe("extract-perf-baseline", () => {
  test("extracts CSV rows from Unity output and preserves quoted platform fields", () => {
    const content = [
      "Noise before results",
      'UntargetedFlood_OneHandler,"Editor Mono x64 Development (LinuxEditor; Unity 2022.3.45f1)",25a4dcc,-1,25000000.125,0,1000.000',
      "Noise after results"
    ].join("\n");

    const rows = extractRows(content);

    expect(rows).toHaveLength(1);
    expect(rows[0]).toMatchObject({
      scenario: "UntargetedFlood_OneHandler",
      platform: "Editor Mono x64 Development (LinuxEditor; Unity 2022.3.45f1)",
      commit: "25a4dcc",
      runIndex: "-1",
      emitsPerSecond: "25000000.125",
      allocatedBytesDelta: "0",
      wallClockMs: "1000.000"
    });
  });

  test("extracts structured Debug.Log rows from prefixed Unity log lines", () => {
    const content =
      '[TestRunner] {scenario:"BroadcastFlood_OneHandler", platform:"Editor Mono x64 Development (LinuxEditor; Unity 2022.3.45f1)", commit:"HEAD", runIndex:-1, emitsPerSec:17000000.5, allocatedBytesDelta:0, wallClockMs:1000.25}';

    expect(buildCsv(extractRows(content))).toBe(
      [
        CSV_HEADER,
        "BroadcastFlood_OneHandler,Editor Mono x64 Development (LinuxEditor; Unity 2022.3.45f1),HEAD,-1,17000000.500,0,1000.250",
        ""
      ].join("\n")
    );
  });

  test("appends rows to an existing baseline without duplicating the header", () => {
    const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), "dxm-perf-"));
    const inputPath = path.join(tempDir, "unity.log");
    const outputPath = path.join(tempDir, "perf-baseline.csv");
    fs.writeFileSync(`${outputPath}`, `${CSV_HEADER}\n`, "utf8");
    fs.writeFileSync(
      inputPath,
      'TargetedFlood_OneListener,"Editor Mono x64 Development (LinuxEditor; Unity 2022.3.45f1)",29a5338,-1,18000000.000,0,1000.000\n',
      "utf8"
    );

    const result = childProcess.spawnSync(
      process.execPath,
      [SCRIPT, "--input", inputPath, "--output", outputPath, "--append"],
      { encoding: "utf8" }
    );

    expect(result.status).toBe(0);
    expect(fs.readFileSync(outputPath, "utf8")).toBe(
      [
        CSV_HEADER,
        "TargetedFlood_OneListener,Editor Mono x64 Development (LinuxEditor; Unity 2022.3.45f1),29a5338,-1,18000000.000,0,1000.000",
        ""
      ].join("\n")
    );
  });
});
