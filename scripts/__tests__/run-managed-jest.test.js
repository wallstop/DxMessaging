/**
 * @fileoverview Tests for run-managed-jest.js.
 */

"use strict";

const fs = require("fs");
const path = require("path");
const childProcess = require("child_process");
const {
    REPO_ROOT,
    LOCAL_JEST_BIN,
    FALLBACK_JEST_SPEC,
    getPinnedFallbackJestSpec,
    toShellCommand,
    parseNpmMajorVersion,
    resolveLocalModule,
    isCommandUnavailable,
    hasHealthyLocalJestInstall,
    runManagedJest,
} = require("../run-managed-jest.js");

describe("run-managed-jest", () => {
    let existsSyncSpy;
    let spawnSyncSpy;

    beforeEach(() => {
        existsSyncSpy = jest.spyOn(fs, "existsSync");
        spawnSyncSpy = jest.spyOn(childProcess, "spawnSync");
    });

    afterEach(() => {
        existsSyncSpy.mockRestore();
        spawnSyncSpy.mockRestore();
    });

    test("parseNpmMajorVersion parses valid versions", () => {
        expect(parseNpmMajorVersion("11.11.0\n")).toBe(11);
        expect(parseNpmMajorVersion("v10.9.3")).toBe(10);
        expect(parseNpmMajorVersion("not-a-version")).toBeNull();
        expect(parseNpmMajorVersion(null)).toBeNull();
    });

    test("parseNpmMajorVersion rejects malformed version strings", () => {
        expect(parseNpmMajorVersion("v")).toBeNull();
        expect(parseNpmMajorVersion("")).toBeNull();
        expect(parseNpmMajorVersion("abc.1.2")).toBeNull();
        expect(parseNpmMajorVersion({})).toBeNull();
    });

    test("getPinnedFallbackJestSpec uses lockfile version when available", () => {
        const readFileSyncFn = jest.fn(() =>
            JSON.stringify({
                packages: {
                    "node_modules/jest": {
                        version: "30.3.1",
                    },
                },
            })
        );

        expect(getPinnedFallbackJestSpec(readFileSyncFn)).toBe("jest@30.3.1");
    });

    test("getPinnedFallbackJestSpec falls back to static version when lockfile is invalid", () => {
        const readFileSyncFn = jest.fn(() => "not-json");
        expect(getPinnedFallbackJestSpec(readFileSyncFn)).toBe(FALLBACK_JEST_SPEC);
    });

    test("toShellCommand applies platform-specific npm command suffixes", () => {
        expect(toShellCommand("npm", "linux")).toBe("npm");
        expect(toShellCommand("npm", "darwin")).toBe("npm");
        expect(toShellCommand("npm", "win32")).toBe("npm.cmd");
    });

    test("isCommandUnavailable handles common command-not-found scenarios", () => {
        expect(isCommandUnavailable(null)).toBe(true);
        expect(isCommandUnavailable({ status: 127, error: null })).toBe(true);
        expect(isCommandUnavailable({ status: null, error: { code: "ENOENT" } })).toBe(true);
        expect(isCommandUnavailable({ status: null, error: { code: "EACCES" } })).toBe(true);
        expect(isCommandUnavailable({ status: 1, error: null })).toBe(false);
    });

    test("hasHealthyLocalJestInstall returns false when local jest binary is missing", () => {
        const result = hasHealthyLocalJestInstall(() => path.join(REPO_ROOT, "node_modules", "jest-circus", "build", "runner.js"), () => false);
        expect(result).toBe(false);
    });

    test("hasHealthyLocalJestInstall returns false when jest-circus runner cannot be resolved", () => {
        const result = hasHealthyLocalJestInstall(() => null, () => true);
        expect(result).toBe(false);
    });

    test("hasHealthyLocalJestInstall rejects runner paths outside local node_modules", () => {
        const externalRunnerPath = path.join(path.dirname(REPO_ROOT), "external-cache", "jest-circus", "build", "runner.js");
        const result = hasHealthyLocalJestInstall(() => externalRunnerPath, () => true);
        expect(result).toBe(false);
    });

    test("hasHealthyLocalJestInstall accepts runner paths inside local node_modules", () => {
        const localRunnerPath = path.join(REPO_ROOT, "node_modules", "jest-circus", "build", "runner.js");
        const result = hasHealthyLocalJestInstall(() => localRunnerPath, () => true);
        expect(result).toBe(true);
    });

    test("resolveLocalModule resolves local jest-circus runner from repository dependencies", () => {
        const resolvedPath = resolveLocalModule("jest-circus/runner");
        expect(typeof resolvedPath).toBe("string");
        expect(resolvedPath).toContain(path.join("node_modules", "jest-circus"));
    });

    test("runManagedJest uses local jest when installed", () => {
        existsSyncSpy.mockReturnValue(true);
        spawnSyncSpy.mockReturnValue({ status: 0 });

        const result = runManagedJest(["--version"]);

        expect(result).toEqual({ status: 0, error: null });
        expect(spawnSyncSpy).toHaveBeenCalledWith(
            process.execPath,
            [LOCAL_JEST_BIN, "--version"],
            expect.objectContaining({ cwd: REPO_ROOT, stdio: "inherit" })
        );
    });

    test("runManagedJest uses npm exec fallback when local jest is missing and npm>=7", () => {
        existsSyncSpy.mockReturnValue(false);
        const pinnedFallbackJestSpec = getPinnedFallbackJestSpec();
        spawnSyncSpy
            .mockReturnValueOnce({ status: 0, stdout: "11.11.0\n", stderr: "" })
            .mockReturnValueOnce({ status: 0 });

        const result = runManagedJest(["--runTestsByPath", "scripts/__tests__/alpha.test.js"]);

        expect(result).toEqual({ status: 0, error: null });
        expect(spawnSyncSpy).toHaveBeenNthCalledWith(
            1,
            toShellCommand("npm"),
            ["--version"],
            expect.objectContaining({ cwd: REPO_ROOT, encoding: "utf8" })
        );
        expect(spawnSyncSpy).toHaveBeenNthCalledWith(
            2,
            toShellCommand("npm"),
            [
                "exec",
                "--yes",
                `--package=${pinnedFallbackJestSpec}`,
                "--",
                "jest",
                "--runTestsByPath",
                "scripts/__tests__/alpha.test.js",
            ],
            expect.objectContaining({ cwd: REPO_ROOT, stdio: "inherit" })
        );
    });

    test("runManagedJest uses npm exec fallback when local jest install is unhealthy", () => {
        existsSyncSpy.mockReturnValue(true);
        const pinnedFallbackJestSpec = getPinnedFallbackJestSpec();
        spawnSyncSpy
            .mockReturnValueOnce({ status: 0, stdout: "11.11.0\n", stderr: "" })
            .mockReturnValueOnce({ status: 0 });
        const fallbackWarningSpy = jest.fn();

        const result = runManagedJest(["--version"], {
            hasHealthyLocalJestInstallFn: () => false,
            printLocalJestFallbackWarningFn: fallbackWarningSpy,
        });

        expect(result).toEqual({ status: 0, error: null });
        expect(fallbackWarningSpy).toHaveBeenCalledTimes(1);
        expect(spawnSyncSpy).toHaveBeenNthCalledWith(
            1,
            toShellCommand("npm"),
            ["--version"],
            expect.objectContaining({ cwd: REPO_ROOT, encoding: "utf8" })
        );
        expect(spawnSyncSpy).toHaveBeenNthCalledWith(
            2,
            toShellCommand("npm"),
            ["exec", "--yes", `--package=${pinnedFallbackJestSpec}`, "--", "jest", "--version"],
            expect.objectContaining({ cwd: REPO_ROOT, stdio: "inherit" })
        );
    });

    test("runManagedJest uses npx fallback when npm major version is older than 7", () => {
        existsSyncSpy.mockReturnValue(false);
        const pinnedFallbackJestSpec = getPinnedFallbackJestSpec();
        spawnSyncSpy
            .mockReturnValueOnce({ status: 0, stdout: "6.14.18\n", stderr: "" })
            .mockReturnValueOnce({ status: 0 });

        const result = runManagedJest(["--version"]);

        expect(result).toEqual({ status: 0, error: null });
        expect(spawnSyncSpy).toHaveBeenNthCalledWith(
            2,
            toShellCommand("npx"),
            ["--yes", `--package=${pinnedFallbackJestSpec}`, "jest", "--version"],
            expect.objectContaining({ cwd: REPO_ROOT, stdio: "inherit" })
        );
    });

    test("runManagedJest uses npx fallback when npm major version cannot be determined", () => {
        existsSyncSpy.mockReturnValue(false);
        const pinnedFallbackJestSpec = getPinnedFallbackJestSpec();
        spawnSyncSpy
            .mockReturnValueOnce({ status: 1, stdout: "", stderr: "npm unavailable" })
            .mockReturnValueOnce({ status: 0 });

        const result = runManagedJest(["--version"]);

        expect(result).toEqual({ status: 0, error: null });
        expect(spawnSyncSpy).toHaveBeenNthCalledWith(
            2,
            toShellCommand("npx"),
            ["--yes", `--package=${pinnedFallbackJestSpec}`, "jest", "--version"],
            expect.objectContaining({ cwd: REPO_ROOT, stdio: "inherit" })
        );
    });

    test("runManagedJest falls back to npx when npm exec command is unavailable", () => {
        existsSyncSpy.mockReturnValue(false);
        const pinnedFallbackJestSpec = getPinnedFallbackJestSpec();
        spawnSyncSpy
            .mockReturnValueOnce({ status: 0, stdout: "11.11.0\n", stderr: "" })
            .mockReturnValueOnce({ status: null, error: { code: "EACCES", message: "npm denied" } })
            .mockReturnValueOnce({ status: 0 });

        const result = runManagedJest(["--version"]);

        expect(result).toEqual({ status: 0, error: null });
        expect(spawnSyncSpy).toHaveBeenNthCalledWith(
            2,
            toShellCommand("npm"),
            ["exec", "--yes", `--package=${pinnedFallbackJestSpec}`, "--", "jest", "--version"],
            expect.objectContaining({ cwd: REPO_ROOT, stdio: "inherit" })
        );
        expect(spawnSyncSpy).toHaveBeenNthCalledWith(
            3,
            toShellCommand("npx"),
            ["--yes", `--package=${pinnedFallbackJestSpec}`, "jest", "--version"],
            expect.objectContaining({ cwd: REPO_ROOT, stdio: "inherit" })
        );
    });
});