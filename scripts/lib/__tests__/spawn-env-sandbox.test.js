/**
 * @fileoverview Golden unit tests for scripts/lib/spawn-env-sandbox.js.
 *
 * WHY THIS EXISTS: tests that spawn host-sensitive scripts (e.g.
 * ensure-editor.ps1, which probes `${env:ProgramFiles}\Unity\Hub\Editor\...`)
 * must neutralize the host-default FOLDER vars so a real machine install cannot
 * leak into the resolution path. The old pattern `delete env.ProgramFiles` is a
 * latent cross-platform bug: Windows env-var NAMES are CASE-INSENSITIVE while a
 * JS `delete` is CASE-SENSITIVE, so a surviving case-variant key (e.g.
 * `PROGRAMFILES`) keeps the real folder visible to the child process. On Linux
 * `${env:ProgramFiles}` is empty so the bug is invisible -- it only bites on a
 * Windows host that has Unity installed.
 *
 * These tests LOCK DOWN the fix with literal assertions, including the EXACT
 * failure mode (an ALL-CAPS / odd-cased input key) so a regression to the
 * case-sensitive delete is caught on any OS.
 */

"use strict";

const fs = require("fs");
const os = require("os");
const path = require("path");

const {
  sandboxHostFolderEnv,
  HOST_FOLDER_CANONICAL_VARS,
  HOST_FOLDER_DENYLIST,
  sandboxDirNameFor
} = require("../spawn-env-sandbox");

const workspaces = [];

function makeSandboxRoot() {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "dxm-spawn-env-sandbox-"));
  workspaces.push(dir);
  return dir;
}

afterAll(() => {
  for (const ws of workspaces) {
    try {
      fs.rmSync(ws, { recursive: true, force: true });
    } catch {
      // best-effort temp cleanup; never fail the suite on teardown.
    }
  }
});

describe("sandboxHostFolderEnv", () => {
  test("removes host-default folder vars CASE-INSENSITIVELY (the regression delete env.X missed)", () => {
    const sandboxRoot = makeSandboxRoot();
    // Fixture base env spelled with the EXACT casings a JS `delete env.ProgramFiles`
    // would have missed on Windows: ALL CAPS, odd inner casing, and mixed case.
    const baseEnv = {
      PROGRAMFILES: "C:\\Program Files",
      "ProgramFiles(X86)": "C:\\Program Files (x86)",
      LocalAppData: "C:\\Users\\runner\\AppData\\Local",
      PATH: "/usr/bin"
    };

    const result = sandboxHostFolderEnv(baseEnv, sandboxRoot);

    // None of the original host-folder keys (any casing) may survive pointing at
    // their original host path.
    expect(result.PROGRAMFILES).toBeUndefined();
    expect(result["ProgramFiles(X86)"]).toBeUndefined();
    expect(result.LocalAppData).toBeUndefined();

    // They are replaced by canonical-cased keys under the sandbox root.
    expect(result.ProgramFiles.startsWith(sandboxRoot)).toBe(true);
    expect(result["ProgramFiles(x86)"].startsWith(sandboxRoot)).toBe(true);
    expect(result.LOCALAPPDATA.startsWith(sandboxRoot)).toBe(true);

    // Unrelated vars are preserved verbatim.
    expect(result.PATH).toBe("/usr/bin");
  });

  test("result has NO key (any casing) still pointing at a non-sandbox host path", () => {
    const sandboxRoot = makeSandboxRoot();
    const baseEnv = {
      ProgramFiles: "C:\\Program Files",
      "PROGRAMFILES(X86)": "C:\\Program Files (x86)",
      ProgramW6432: "C:\\Program Files",
      CommonProgramFiles: "C:\\Program Files\\Common Files",
      "commonprogramfiles(x86)": "C:\\Program Files (x86)\\Common Files",
      COMMONPROGRAMW6432: "C:\\Program Files\\Common Files",
      localappdata: "C:\\Users\\runner\\AppData\\Local"
    };

    const result = sandboxHostFolderEnv(baseEnv, sandboxRoot);

    for (const [key, value] of Object.entries(result)) {
      if (HOST_FOLDER_DENYLIST.has(key.toLowerCase())) {
        // Every surviving host-folder key must live under the sandbox root.
        expect(value.startsWith(sandboxRoot)).toBe(true);
      }
    }
  });

  test("sets every canonical host-folder var to a DISTINCT sandbox subdir that exists on disk", () => {
    const sandboxRoot = makeSandboxRoot();
    const result = sandboxHostFolderEnv({}, sandboxRoot);

    const seen = new Set();
    for (const varName of HOST_FOLDER_CANONICAL_VARS) {
      const dir = result[varName];
      expect(typeof dir).toBe("string");
      expect(dir.startsWith(sandboxRoot)).toBe(true);
      // Distinct per var.
      expect(seen.has(dir)).toBe(false);
      seen.add(dir);
      // Exists on disk and is an (empty) directory.
      expect(fs.existsSync(dir)).toBe(true);
      expect(fs.statSync(dir).isDirectory()).toBe(true);
      expect(fs.readdirSync(dir)).toHaveLength(0);
    }
  });

  test("is PURE: does not mutate the input env or process.env", () => {
    const sandboxRoot = makeSandboxRoot();
    const baseEnv = { PROGRAMFILES: "C:\\Program Files", PATH: "/usr/bin" };
    const baseSnapshot = JSON.stringify(baseEnv);
    const processEnvSnapshot = JSON.stringify(process.env);

    const result = sandboxHostFolderEnv(baseEnv, sandboxRoot);

    // Input untouched.
    expect(JSON.stringify(baseEnv)).toBe(baseSnapshot);
    // process.env untouched.
    expect(JSON.stringify(process.env)).toBe(processEnvSnapshot);
    // Returned object is a new reference.
    expect(result).not.toBe(baseEnv);
  });

  test("defaults baseEnv to process.env without mutating it", () => {
    const sandboxRoot = makeSandboxRoot();
    const processEnvSnapshot = JSON.stringify(process.env);

    const result = sandboxHostFolderEnv(undefined, sandboxRoot);

    expect(JSON.stringify(process.env)).toBe(processEnvSnapshot);
    // The canonical vars are sandboxed regardless of what process.env held.
    for (const varName of HOST_FOLDER_CANONICAL_VARS) {
      expect(result[varName].startsWith(sandboxRoot)).toBe(true);
    }
  });

  test("options.extraVars are removed case-insensitively and set to empty sandbox dirs", () => {
    const sandboxRoot = makeSandboxRoot();
    const baseEnv = { CUSTOMHOSTROOT: "C:\\Custom", PATH: "/usr/bin" };

    const result = sandboxHostFolderEnv(baseEnv, sandboxRoot, {
      extraVars: ["CustomHostRoot"]
    });

    // The original case-variant key is gone.
    expect(result.CUSTOMHOSTROOT).toBeUndefined();
    // The canonical-cased extra var is set under the sandbox root and on disk.
    expect(result.CustomHostRoot.startsWith(sandboxRoot)).toBe(true);
    expect(fs.existsSync(result.CustomHostRoot)).toBe(true);
  });

  test("throws when sandboxRootDir is missing or empty", () => {
    expect(() => sandboxHostFolderEnv({}, "")).toThrow(/sandboxRootDir/);
    expect(() => sandboxHostFolderEnv({})).toThrow(/sandboxRootDir/);
  });

  test("tolerates an explicit null options argument (does not throw on null.extraVars)", () => {
    const sandboxRoot = makeSandboxRoot();
    expect(() => sandboxHostFolderEnv({ PATH: "/usr/bin" }, sandboxRoot, null)).not.toThrow();
    const result = sandboxHostFolderEnv({ PATH: "/usr/bin" }, sandboxRoot, null);
    // The built-in canonical vars are still sandboxed even with null options.
    for (const varName of HOST_FOLDER_CANONICAL_VARS) {
      expect(result[varName].startsWith(sandboxRoot)).toBe(true);
    }
  });

  test("sandboxDirNameFor produces distinct filesystem-safe leaf names", () => {
    expect(sandboxDirNameFor("ProgramFiles")).toBe("ProgramFiles");
    expect(sandboxDirNameFor("ProgramFiles(x86)")).toBe("ProgramFiles_x86_");
    // The (x86) variant must NOT collapse to the same leaf as the base var.
    expect(sandboxDirNameFor("ProgramFiles(x86)")).not.toBe(sandboxDirNameFor("ProgramFiles"));
  });
});
