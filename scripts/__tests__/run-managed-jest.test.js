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
    ISOLATED_JEST_CACHE_ROOT,
    getPinnedFallbackJestSpec,
    getIsolatedJestPaths,
    hasCliOption,
    buildNodePathEnv,
    prepareIsolatedFallbackJest,
    toShellCommand,
    parseNpmMajorVersion,
    resolveLocalModule,
    isCommandUnavailable,
    hasHealthyLocalJestInstall,
    runIsolatedFallbackJest,
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

    test.each([
        { input: "11.11.0\n", expected: 11 },
        { input: "v10.9.3", expected: 10 },
        { input: "not-a-version", expected: null },
        { input: null, expected: null },
        { input: "v", expected: null },
        { input: "", expected: null },
        { input: "abc.1.2", expected: null },
        { input: {}, expected: null },
    ])("parseNpmMajorVersion($input) -> $expected", ({ input, expected }) => {
        expect(parseNpmMajorVersion(input)).toBe(expected);
    });

    test.each([
        { args: ["--version"], option: "--testRunner", expected: false },
        { args: ["--testRunner", "custom-runner.js"], option: "--testRunner", expected: true },
        { args: ["--testRunner=custom-runner.js"], option: "--testRunner", expected: true },
        { args: ["--watch"], option: "watch", expected: true },
        { args: ["--watchAll"], option: "watch", expected: false },
    ])("hasCliOption($args, $option) -> $expected", ({ args, option, expected }) => {
        expect(hasCliOption(args, option)).toBe(expected);
    });

    test.each([
        {
            isolatedNodeModulesPath: path.join("/tmp", "isolated", "node_modules"),
            baseEnv: {},
            expectedNodePath: path.join("/tmp", "isolated", "node_modules"),
        },
        {
            isolatedNodeModulesPath: path.join("/tmp", "isolated", "node_modules"),
            baseEnv: { NODE_PATH: path.join("/tmp", "existing", "node_modules") },
            expectedNodePath: [
                path.join("/tmp", "isolated", "node_modules"),
                path.join("/tmp", "existing", "node_modules"),
            ].join(path.delimiter),
        },
    ])("buildNodePathEnv prepends isolated node_modules", ({ isolatedNodeModulesPath, baseEnv, expectedNodePath }) => {
        const result = buildNodePathEnv(isolatedNodeModulesPath, baseEnv);
        expect(result.NODE_PATH).toBe(expectedNodePath);
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

    test.each([
        { value: null, expected: true },
        { value: { status: 127, error: null }, expected: true },
        { value: { status: null, error: { code: "ENOENT" } }, expected: true },
        { value: { status: null, error: { code: "EACCES" } }, expected: true },
        { value: { status: 1, error: null }, expected: false },
    ])("isCommandUnavailable(%j) -> $expected", ({ value, expected }) => {
        expect(isCommandUnavailable(value)).toBe(expected);
    });

    test("prepareIsolatedFallbackJest reuses cached isolated binary when available", () => {
        const jestSpec = "jest@30.3.0";
        const { jestBinPath, jestRunnerPath } = getIsolatedJestPaths(jestSpec);
        const existsSyncFn = jest.fn(
            (targetPath) => targetPath === jestBinPath || targetPath === jestRunnerPath
        );
        const runCommandFn = jest.fn();

        const result = prepareIsolatedFallbackJest(jestSpec, {
            existsSyncFn,
            runCommandFn,
        });

        expect(result).toEqual({ jestBinPath, jestRunnerPath, cacheHit: true });
        expect(runCommandFn).not.toHaveBeenCalled();
    });

    test("prepareIsolatedFallbackJest reinstalls when cached runner is missing", () => {
        const jestSpec = "jest@30.3.0";
        const { installDir, jestBinPath, jestRunnerPath } = getIsolatedJestPaths(jestSpec);
        const existingPaths = new Set([jestBinPath]);

        const existsSyncFn = jest.fn((targetPath) => existingPaths.has(targetPath));
        const runCommandFn = jest.fn((_command, _args, options) => {
            expect(options).toEqual(expect.objectContaining({ cwd: installDir }));
            existingPaths.add(jestBinPath);
            existingPaths.add(jestRunnerPath);
            return { status: 0, error: null };
        });

        const result = prepareIsolatedFallbackJest(jestSpec, {
            existsSyncFn,
            mkdirSyncFn: jest.fn(),
            writeFileSyncFn: jest.fn(),
            runCommandFn,
            warnFn: jest.fn(),
        });

        expect(result).toEqual({ jestBinPath, jestRunnerPath, cacheHit: false });
        expect(runCommandFn).toHaveBeenCalledTimes(1);
    });

    test("prepareIsolatedFallbackJest installs isolated fallback when cache is missing", () => {
        const jestSpec = "jest@30.3.0";
        const { installDir, packageJsonPath, jestBinPath, jestRunnerPath } = getIsolatedJestPaths(jestSpec);
        const existingPaths = new Set();

        const existsSyncFn = jest.fn((targetPath) => existingPaths.has(targetPath));
        const mkdirSyncFn = jest.fn();
        const writeFileSyncFn = jest.fn((targetPath) => {
            existingPaths.add(targetPath);
        });
        const runCommandFn = jest.fn((_command, _args, options) => {
            expect(options).toEqual(expect.objectContaining({ cwd: installDir }));
            existingPaths.add(jestBinPath);
            existingPaths.add(jestRunnerPath);
            return { status: 0, error: null };
        });

        const result = prepareIsolatedFallbackJest(jestSpec, {
            existsSyncFn,
            mkdirSyncFn,
            writeFileSyncFn,
            runCommandFn,
        });

        expect(result).toEqual({ jestBinPath, jestRunnerPath, cacheHit: false });
        expect(mkdirSyncFn).toHaveBeenCalledWith(installDir, { recursive: true });
        expect(writeFileSyncFn).toHaveBeenCalledWith(
            packageJsonPath,
            expect.stringContaining("dxmessaging-managed-jest-fallback-cache"),
            "utf8"
        );
        expect(runCommandFn).toHaveBeenCalledWith(
            "npm",
            [
                "install",
                "--no-audit",
                "--no-fund",
                "--no-package-lock",
                "--no-save",
                jestSpec,
            ],
            expect.objectContaining({ cwd: installDir })
        );
    });

    test("prepareIsolatedFallbackJest reports unavailable isolated fallback when install fails", () => {
        const warnFn = jest.fn();
        const result = prepareIsolatedFallbackJest("jest@30.3.0", {
            existsSyncFn: () => false,
            mkdirSyncFn: jest.fn(),
            writeFileSyncFn: jest.fn(),
            runCommandFn: () => ({ status: 1, error: null }),
            warnFn,
        });

        expect(result).toEqual({ jestBinPath: null, jestRunnerPath: null, cacheHit: false });
        expect(
            warnFn.mock.calls.some((call) => call[0].includes("install failed"))
        ).toBe(true);
    });

    test("runIsolatedFallbackJest injects isolated testRunner when caller did not provide one", () => {
        const jestSpec = "jest@30.3.0";
        const { jestBinPath, jestRunnerPath } = getIsolatedJestPaths(jestSpec);
        const runCommandFn = jest.fn(() => ({ status: 0, error: null }));
        const printIsolatedFallbackSelectionFn = jest.fn();
        const result = runIsolatedFallbackJest(["--version"], {
            getPinnedFallbackJestSpecFn: () => jestSpec,
            prepareIsolatedFallbackJestFn: () => ({
                jestBinPath,
                jestRunnerPath,
                cacheHit: true,
            }),
            runCommandFn,
            printIsolatedFallbackSelectionFn,
            existsSyncFn: () => true,
        });

        expect(result).toEqual({ status: 0, error: null });
        expect(printIsolatedFallbackSelectionFn).toHaveBeenCalledTimes(1);
        expect(printIsolatedFallbackSelectionFn).toHaveBeenCalledWith(
            jestBinPath,
            true,
            expect.objectContaining({
                testRunnerPath: jestRunnerPath,
                testRunnerInjected: true,
                callerProvidedTestRunner: false,
                nodePathOverride: expect.stringContaining(
                    path.join(ISOLATED_JEST_CACHE_ROOT, "jest_30.3.0", "node_modules")
                ),
            })
        );
        expect(runCommandFn).toHaveBeenCalledWith(
            process.execPath,
            [
                jestBinPath,
                "--testRunner",
                jestRunnerPath,
                "--version",
            ],
            expect.objectContaining({
                env: expect.objectContaining({
                    NODE_PATH: expect.stringContaining(
                        path.join(ISOLATED_JEST_CACHE_ROOT, "jest_30.3.0", "node_modules")
                    ),
                }),
            })
        );
    });

    test("runIsolatedFallbackJest preserves caller-provided --testRunner", () => {
        const jestSpec = "jest@30.3.0";
        const { jestBinPath, jestRunnerPath } = getIsolatedJestPaths(jestSpec);
        const runCommandFn = jest.fn(() => ({ status: 0, error: null }));
        const printIsolatedFallbackSelectionFn = jest.fn();

        runIsolatedFallbackJest(["--testRunner", "custom-runner.js", "--version"], {
            getPinnedFallbackJestSpecFn: () => jestSpec,
            prepareIsolatedFallbackJestFn: () => ({
                jestBinPath,
                jestRunnerPath,
                cacheHit: false,
            }),
            runCommandFn,
            printIsolatedFallbackSelectionFn,
            existsSyncFn: () => true,
        });

        expect(runCommandFn).toHaveBeenCalledWith(
            process.execPath,
            [
                jestBinPath,
                "--testRunner",
                "custom-runner.js",
                "--version",
            ],
            expect.objectContaining({
                env: expect.objectContaining({
                    NODE_PATH: expect.stringContaining(
                        path.join(ISOLATED_JEST_CACHE_ROOT, "jest_30.3.0", "node_modules")
                    ),
                }),
            })
        );
        expect(printIsolatedFallbackSelectionFn).toHaveBeenCalledWith(
            jestBinPath,
            false,
            expect.objectContaining({
                testRunnerPath: null,
                testRunnerInjected: false,
                callerProvidedTestRunner: true,
                nodePathOverride: expect.stringContaining(
                    path.join(ISOLATED_JEST_CACHE_ROOT, "jest_30.3.0", "node_modules")
                ),
            })
        );
    });

    test("runIsolatedFallbackJest returns null when isolated fallback cannot be prepared", () => {
        const runCommandFn = jest.fn();
        const result = runIsolatedFallbackJest(["--version"], {
            getPinnedFallbackJestSpecFn: () => "jest@30.3.0",
            prepareIsolatedFallbackJestFn: () => ({
                jestBinPath: null,
                cacheHit: false,
            }),
            runCommandFn,
        });

        expect(result).toBeNull();
        expect(runCommandFn).not.toHaveBeenCalled();
    });

    test("runIsolatedFallbackJest returns null when runner injection is required but unavailable", () => {
        const jestSpec = "jest@30.3.0";
        const runCommandFn = jest.fn();
        const warnFn = jest.fn();

        const result = runIsolatedFallbackJest(["--version"], {
            getPinnedFallbackJestSpecFn: () => jestSpec,
            prepareIsolatedFallbackJestFn: () => ({
                jestBinPath: path.join(ISOLATED_JEST_CACHE_ROOT, "jest_30.3.0", "node_modules", "jest", "bin", "jest.js"),
                jestRunnerPath: null,
                cacheHit: false,
            }),
            runCommandFn,
            existsSyncFn: () => false,
            warnFn,
        });

        expect(result).toBeNull();
        expect(runCommandFn).not.toHaveBeenCalled();
        expect(
            warnFn.mock.calls.some((call) => call[0].includes("runner unavailable"))
        ).toBe(true);
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
            runIsolatedFallbackJestFn: () => null,
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

    test("runManagedJest uses isolated fallback when local install is unhealthy", () => {
        existsSyncSpy.mockReturnValue(true);
        const isolatedResult = { status: 0, error: null };
        const runIsolatedFallbackJestFn = jest.fn(() => isolatedResult);
        const runNpmExecJestFn = jest.fn();

        const result = runManagedJest(["--version"], {
            hasHealthyLocalJestInstallFn: () => false,
            runIsolatedFallbackJestFn,
            runNpmExecJestFn,
        });

        expect(result).toEqual(isolatedResult);
        expect(runIsolatedFallbackJestFn).toHaveBeenCalledWith(["--version"]);
        expect(runNpmExecJestFn).not.toHaveBeenCalled();
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