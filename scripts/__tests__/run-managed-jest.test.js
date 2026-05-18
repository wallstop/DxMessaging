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
    getDefaultIsolatedJestRunnerPath,
    getIsolatedJestPaths,
    resolveIsolatedJestRunnerPath,
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
    runLocalJest,
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

    test("resolveIsolatedJestRunnerPath prefers module-resolution output", () => {
        const installDir = path.join(ISOLATED_JEST_CACHE_ROOT, "jest_30.3.0");
        const packageJsonPath = path.join(installDir, "package.json");
        const resolvedRunnerPath = path.join(
            installDir,
            "node_modules",
            "jest-circus",
            "build",
            "runner.mjs"
        );
        const existsSyncFn = jest.fn(
            (targetPath) => targetPath === packageJsonPath || targetPath === resolvedRunnerPath
        );
        const resolveFn = jest.fn(() => resolvedRunnerPath);
        const createRequireFn = jest.fn(() => ({ resolve: resolveFn }));

        const runnerPath = resolveIsolatedJestRunnerPath(installDir, {
            existsSyncFn,
            createRequireFn,
        });

        expect(runnerPath).toBe(resolvedRunnerPath);
        expect(createRequireFn).toHaveBeenCalledWith(packageJsonPath);
        expect(resolveFn).toHaveBeenCalledWith("jest-circus/runner");
    });

    test("resolveIsolatedJestRunnerPath falls back to legacy path when resolution fails", () => {
        const installDir = path.join(ISOLATED_JEST_CACHE_ROOT, "jest_30.3.0");
        const packageJsonPath = path.join(installDir, "package.json");
        const legacyRunnerPath = getDefaultIsolatedJestRunnerPath(installDir);
        const existsSyncFn = jest.fn(
            (targetPath) => targetPath === packageJsonPath || targetPath === legacyRunnerPath
        );

        const runnerPath = resolveIsolatedJestRunnerPath(installDir, {
            existsSyncFn,
            createRequireFn: () => {
                throw new Error("resolution unavailable");
            },
        });

        expect(runnerPath).toBe(legacyRunnerPath);
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

    test("runIsolatedFallbackJest does not inject --testRunner when caller did not provide one", () => {
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
                callerProvidedTestRunner: false,
                nodePathOverride: expect.stringContaining(
                    path.join(ISOLATED_JEST_CACHE_ROOT, "jest_30.3.0", "node_modules")
                ),
            })
        );
        expect(runCommandFn).toHaveBeenCalledWith(
            process.execPath,
            [jestBinPath, "--version"],
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

    test("runIsolatedFallbackJest returns null when isolated runner is unavailable and caller provided no override", () => {
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

    test("hasHealthyLocalJestInstall returns false when tryLoadModule throws even though existsSync is true", () => {
        const localRunnerPath = path.join(REPO_ROOT, "node_modules", "jest-circus", "build", "runner.js");
        const tryLoadModuleFn = jest.fn(() => false);
        const result = hasHealthyLocalJestInstall(
            () => localRunnerPath,
            () => true,
            tryLoadModuleFn
        );
        expect(result).toBe(false);
        expect(tryLoadModuleFn).toHaveBeenCalledWith("jest-circus/runner");
    });

    test("runLocalJest does not inject --testRunner when validation passes", () => {
        const resolvedPath = path.join(REPO_ROOT, "node_modules", "jest-circus", "build", "runner.js");
        const runCommandFn = jest.fn(() => ({ status: 0, error: null }));
        const warnFn = jest.fn();

        const result = runLocalJest(["--version"], {
            moduleResolver: () => resolvedPath,
            tryLoadModuleFn: () => true,
            existsSyncFn: () => true,
            runCommandFn,
            warnFn,
        });

        expect(result).toEqual({ status: 0, error: null });
        expect(runCommandFn).toHaveBeenCalledWith(
            process.execPath,
            [LOCAL_JEST_BIN, "--version"]
        );
        const invocationArgs = runCommandFn.mock.calls[0][1];
        expect(invocationArgs).not.toContain("--testRunner");
    });

    test("runLocalJest does not inject --testRunner when the caller already provided one", () => {
        const userRunnerPath = path.join(REPO_ROOT, "custom-runner.js");
        const runCommandFn = jest.fn(() => ({ status: 0, error: null }));
        const moduleResolver = jest.fn();
        const tryLoadModuleFn = jest.fn();

        const result = runLocalJest(["--testRunner", userRunnerPath, "--version"], {
            moduleResolver,
            tryLoadModuleFn,
            existsSyncFn: () => true,
            runCommandFn,
            warnFn: () => {},
        });

        expect(result).toEqual({ status: 0, error: null });
        expect(moduleResolver).not.toHaveBeenCalled();
        expect(tryLoadModuleFn).not.toHaveBeenCalled();
        expect(runCommandFn).toHaveBeenCalledWith(
            process.execPath,
            [LOCAL_JEST_BIN, "--testRunner", userRunnerPath, "--version"]
        );
    });

    test("runLocalJest returns null when load-validation of jest-circus/runner fails", () => {
        const resolvedPath = path.join(REPO_ROOT, "node_modules", "jest-circus", "build", "runner.js");
        const runCommandFn = jest.fn();
        const warnFn = jest.fn();

        const result = runLocalJest(["--version"], {
            moduleResolver: () => resolvedPath,
            tryLoadModuleFn: () => false,
            existsSyncFn: () => true,
            runCommandFn,
            warnFn,
        });

        expect(result).toBeNull();
        expect(runCommandFn).not.toHaveBeenCalled();
        expect(warnFn.mock.calls.some((call) => String(call[0]).includes("failed load validation"))).toBe(true);
    });

    test("runManagedJest cascades local → isolated → npm exec when earlier tiers return null", () => {
        // Regression coverage for the full fallback cascade: gate fails, then
        // isolated fallback prep fails, then npm exec succeeds. This is the
        // exact code path exercised when a hook runs in an environment with a
        // corrupted local node_modules and no /tmp write access for the
        // isolated cache.
        const npmExecResult = { status: 0, error: null };
        const runLocalJestFn = jest.fn(() => null);
        const runIsolatedFallbackJestFn = jest.fn(() => null);
        const runNpmExecJestFn = jest.fn(() => npmExecResult);
        const runNpxJestFn = jest.fn();
        const printLocalJestFallbackWarningFn = jest.fn();

        existsSyncSpy.mockReturnValue(true);

        const result = runManagedJest(["--version"], {
            hasHealthyLocalJestInstallFn: () => true,
            getNpmMajorVersionFn: () => 10,
            runLocalJestFn,
            runIsolatedFallbackJestFn,
            runNpmExecJestFn,
            runNpxJestFn,
            printLocalJestFallbackWarningFn,
        });

        expect(result).toEqual(npmExecResult);
        expect(runLocalJestFn).toHaveBeenCalledTimes(1);
        expect(runIsolatedFallbackJestFn).toHaveBeenCalledTimes(1);
        expect(runNpmExecJestFn).toHaveBeenCalledTimes(1);
        expect(runNpxJestFn).not.toHaveBeenCalled();
    });

    test("runManagedJest falls through to isolated/npm-exec when runLocalJest returns null", () => {
        const isolatedResult = { status: 0, error: null };
        const runLocalJestFn = jest.fn(() => null);
        const runIsolatedFallbackJestFn = jest.fn(() => isolatedResult);
        const runNpmExecJestFn = jest.fn();
        const printLocalJestFallbackWarningFn = jest.fn();

        existsSyncSpy.mockReturnValue(true);

        const result = runManagedJest(["--version"], {
            hasHealthyLocalJestInstallFn: () => true,
            runLocalJestFn,
            runIsolatedFallbackJestFn,
            runNpmExecJestFn,
            printLocalJestFallbackWarningFn,
        });

        expect(result).toEqual(isolatedResult);
        expect(runLocalJestFn).toHaveBeenCalledWith(["--version"]);
        expect(runIsolatedFallbackJestFn).toHaveBeenCalledWith(["--version"]);
        expect(runNpmExecJestFn).not.toHaveBeenCalled();
    });

    test("resolveLocalModule resolves local jest-circus runner from repository dependencies", () => {
        const resolvedPath = resolveLocalModule("jest-circus/runner");
        expect(typeof resolvedPath).toBe("string");
        expect(resolvedPath).toContain(path.join("node_modules", "jest-circus"));
    });

    test("runManagedJest uses local jest when installed", () => {
        existsSyncSpy.mockReturnValue(true);
        spawnSyncSpy.mockReturnValue({ status: 0 });
        const warnSpy = jest.spyOn(console, "warn").mockImplementation(() => {});

        try {
            const result = runManagedJest(["--version"]);

            expect(result).toEqual({ status: 0, error: null });
            expect(spawnSyncSpy).toHaveBeenCalledWith(
                process.execPath,
                [LOCAL_JEST_BIN, "--version"],
                expect.objectContaining({ cwd: REPO_ROOT, stdio: "inherit" })
            );
            // Regression guard: never inject --testRunner with a hardcoded
            // jest-circus runner path. Jest 27+ resolves its bundled default
            // runner reliably; injecting absolute paths has caused Windows
            // failures ("Module ... in the testRunner option was not found").
            const invocationArgs = spawnSyncSpy.mock.calls[0][1];
            expect(invocationArgs).not.toContain("--testRunner");
        } finally {
            warnSpy.mockRestore();
        }
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