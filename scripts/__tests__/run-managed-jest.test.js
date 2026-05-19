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
    runCommandCapturingStderr,
    attemptIsolatedCacheReset,
    attemptNpmCiRecovery,
    printActionableRepairBanner,
} = require("../run-managed-jest.js");

describe("run-managed-jest", () => {
    let existsSyncSpy;
    let spawnSyncSpy;
    let originalSkipIntegrity;

    beforeEach(() => {
        existsSyncSpy = jest.spyOn(fs, "existsSync");
        spawnSyncSpy = jest.spyOn(childProcess, "spawnSync");
        // The integrity gate (Step 5) runs before tier dispatch and consults
        // fs.existsSync + fs.statSync. The legacy tier-fallback tests in
        // this describe block intentionally mock `existsSync` to return
        // false (simulating a missing local jest), which would also tell
        // the gate "no INTEGRITY_TARGETS files exist" and short-circuit the
        // wrapper to status=1. Skip the gate here so those tests continue
        // to exercise the tier-fallback contract; a dedicated describe
        // block below covers the gate's own behavior.
        originalSkipIntegrity = process.env.DXMSG_HOOK_SKIP_INTEGRITY;
        process.env.DXMSG_HOOK_SKIP_INTEGRITY = "1";
    });

    afterEach(() => {
        existsSyncSpy.mockRestore();
        spawnSyncSpy.mockRestore();
        if (originalSkipIntegrity === undefined) {
            delete process.env.DXMSG_HOOK_SKIP_INTEGRITY;
        } else {
            process.env.DXMSG_HOOK_SKIP_INTEGRITY = originalSkipIntegrity;
        }
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

    test("prepareIsolatedFallbackJest deletes installDir before reinstall when cached runner is missing", () => {
        // Idempotency invariant (Step 3): half-populated isolated cache dirs
        // must be torn down before re-install so npm install cannot short-
        // circuit on a stale lockfile / inconsistent manifest.
        const jestSpec = "jest@30.3.0";
        const { installDir, packageJsonPath, jestBinPath, jestRunnerPath } = getIsolatedJestPaths(jestSpec);
        // Pretend the install dir exists but the runner is missing (bin OK).
        const existingPaths = new Set([installDir, jestBinPath]);
        let runnerInstalled = false;

        const existsSyncFn = jest.fn((targetPath) => existingPaths.has(targetPath));
        const rmSyncFn = jest.fn((target) => {
            // After rm, the install dir and bin are gone.
            existingPaths.delete(target);
            existingPaths.delete(jestBinPath);
            existingPaths.delete(packageJsonPath);
        });
        const mkdirSyncFn = jest.fn(() => existingPaths.add(installDir));
        const writeFileSyncFn = jest.fn((target) => existingPaths.add(target));
        const runCommandFn = jest.fn(() => {
            existingPaths.add(jestBinPath);
            existingPaths.add(jestRunnerPath);
            runnerInstalled = true;
            return { status: 0, error: null };
        });

        const result = prepareIsolatedFallbackJest(jestSpec, {
            existsSyncFn,
            mkdirSyncFn,
            writeFileSyncFn,
            rmSyncFn,
            runCommandFn,
            warnFn: jest.fn(),
        });

        expect(rmSyncFn).toHaveBeenCalledTimes(1);
        expect(rmSyncFn).toHaveBeenCalledWith(
            path.resolve(installDir),
            expect.objectContaining({ recursive: true, force: true })
        );
        expect(mkdirSyncFn).toHaveBeenCalledWith(installDir, { recursive: true });
        expect(runnerInstalled).toBe(true);
        expect(result).toEqual({ jestBinPath, jestRunnerPath, cacheHit: false });
    });

    test("prepareIsolatedFallbackJest refuses to rm when sanitized path traverses outside cache root", () => {
        // sanitizeCacheKey("..") returns ".." which path.resolve maps to the
        // parent of ISOLATED_JEST_CACHE_ROOT (typically the OS temp dir).
        // The defensive validator must refuse to rm in that case.
        const poisonedSpec = "..";
        const { installDir, jestBinPath, jestRunnerPath } = getIsolatedJestPaths(poisonedSpec);
        const existingPaths = new Set([installDir]);

        const existsSyncFn = jest.fn((targetPath) => existingPaths.has(targetPath));
        const rmSyncFn = jest.fn();
        const mkdirSyncFn = jest.fn(() => existingPaths.add(installDir));
        const writeFileSyncFn = jest.fn();
        // runCommandFn is invoked because rm is refused, but the cache then
        // skips through to install — we make install succeed so the function
        // returns rather than dies. We only care that rm was refused.
        const runCommandFn = jest.fn(() => {
            existingPaths.add(jestBinPath);
            existingPaths.add(jestRunnerPath);
            return { status: 0, error: null };
        });
        const warnFn = jest.fn();

        prepareIsolatedFallbackJest(poisonedSpec, {
            existsSyncFn,
            mkdirSyncFn,
            writeFileSyncFn,
            rmSyncFn,
            runCommandFn,
            warnFn,
        });

        expect(rmSyncFn).not.toHaveBeenCalled();
        expect(
            warnFn.mock.calls.some((call) =>
                String(call[0]).includes("Refusing to rm partial isolated fallback install dir")
            )
        ).toBe(true);
    });

    test("prepareIsolatedFallbackJest skips rm on cache-hit (both bin and runner present)", () => {
        const jestSpec = "jest@30.3.0";
        const { jestBinPath, jestRunnerPath } = getIsolatedJestPaths(jestSpec);
        const existsSyncFn = jest.fn(
            (targetPath) => targetPath === jestBinPath || targetPath === jestRunnerPath
        );
        const rmSyncFn = jest.fn();
        const runCommandFn = jest.fn();

        const result = prepareIsolatedFallbackJest(jestSpec, {
            existsSyncFn,
            mkdirSyncFn: jest.fn(),
            writeFileSyncFn: jest.fn(),
            rmSyncFn,
            runCommandFn,
            warnFn: jest.fn(),
        });

        expect(result).toEqual({ jestBinPath, jestRunnerPath, cacheHit: true });
        expect(rmSyncFn).not.toHaveBeenCalled();
        expect(runCommandFn).not.toHaveBeenCalled();
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
        spawnSyncSpy.mockReturnValue({ status: 0, stderr: Buffer.from("") });
        const warnSpy = jest.spyOn(console, "warn").mockImplementation(() => {});

        try {
            const result = runManagedJest(["--version"]);

            expect(result).toEqual(
                expect.objectContaining({ status: 0, error: null, stderr: "" })
            );
            expect(spawnSyncSpy).toHaveBeenCalledWith(
                process.execPath,
                [LOCAL_JEST_BIN, "--version"],
                expect.objectContaining({
                    cwd: REPO_ROOT,
                    stdio: ["inherit", "inherit", "pipe"],
                })
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
            .mockReturnValueOnce({ status: 0, stderr: Buffer.from("") });

        const result = runManagedJest(["--runTestsByPath", "scripts/__tests__/alpha.test.js"]);

        expect(result).toEqual(
            expect.objectContaining({ status: 0, error: null, stderr: "" })
        );
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
            expect.objectContaining({
                cwd: REPO_ROOT,
                stdio: ["inherit", "inherit", "pipe"],
            })
        );
    });

    test("runManagedJest uses npm exec fallback when local jest install is unhealthy", () => {
        existsSyncSpy.mockReturnValue(true);
        const pinnedFallbackJestSpec = getPinnedFallbackJestSpec();
        spawnSyncSpy
            .mockReturnValueOnce({ status: 0, stdout: "11.11.0\n", stderr: "" })
            .mockReturnValueOnce({ status: 0, stderr: Buffer.from("") });
        const fallbackWarningSpy = jest.fn();

        const result = runManagedJest(["--version"], {
            hasHealthyLocalJestInstallFn: () => false,
            printLocalJestFallbackWarningFn: fallbackWarningSpy,
            runIsolatedFallbackJestFn: () => null,
        });

        expect(result).toEqual(
            expect.objectContaining({ status: 0, error: null, stderr: "" })
        );
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
            expect.objectContaining({
                cwd: REPO_ROOT,
                stdio: ["inherit", "inherit", "pipe"],
            })
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
            .mockReturnValueOnce({ status: 0, stderr: Buffer.from("") });

        const result = runManagedJest(["--version"]);

        expect(result).toEqual(
            expect.objectContaining({ status: 0, error: null, stderr: "" })
        );
        expect(spawnSyncSpy).toHaveBeenNthCalledWith(
            2,
            toShellCommand("npx"),
            ["--yes", `--package=${pinnedFallbackJestSpec}`, "jest", "--version"],
            expect.objectContaining({
                cwd: REPO_ROOT,
                stdio: ["inherit", "inherit", "pipe"],
            })
        );
    });

    test("runManagedJest uses npx fallback when npm major version cannot be determined", () => {
        existsSyncSpy.mockReturnValue(false);
        const pinnedFallbackJestSpec = getPinnedFallbackJestSpec();
        spawnSyncSpy
            .mockReturnValueOnce({ status: 1, stdout: "", stderr: "npm unavailable" })
            .mockReturnValueOnce({ status: 0, stderr: Buffer.from("") });

        const result = runManagedJest(["--version"]);

        expect(result).toEqual(
            expect.objectContaining({ status: 0, error: null, stderr: "" })
        );
        expect(spawnSyncSpy).toHaveBeenNthCalledWith(
            2,
            toShellCommand("npx"),
            ["--yes", `--package=${pinnedFallbackJestSpec}`, "jest", "--version"],
            expect.objectContaining({
                cwd: REPO_ROOT,
                stdio: ["inherit", "inherit", "pipe"],
            })
        );
    });

    test("runManagedJest falls back to npx when npm exec command is unavailable", () => {
        existsSyncSpy.mockReturnValue(false);
        const pinnedFallbackJestSpec = getPinnedFallbackJestSpec();
        spawnSyncSpy
            .mockReturnValueOnce({ status: 0, stdout: "11.11.0\n", stderr: "" })
            .mockReturnValueOnce({ status: null, error: { code: "EACCES", message: "npm denied" } })
            .mockReturnValueOnce({ status: 0, stderr: Buffer.from("") });

        const result = runManagedJest(["--version"]);

        expect(result).toEqual(
            expect.objectContaining({ status: 0, error: null, stderr: "" })
        );
        expect(spawnSyncSpy).toHaveBeenNthCalledWith(
            2,
            toShellCommand("npm"),
            ["exec", "--yes", `--package=${pinnedFallbackJestSpec}`, "--", "jest", "--version"],
            expect.objectContaining({
                cwd: REPO_ROOT,
                stdio: ["inherit", "inherit", "pipe"],
            })
        );
        expect(spawnSyncSpy).toHaveBeenNthCalledWith(
            3,
            toShellCommand("npx"),
            ["--yes", `--package=${pinnedFallbackJestSpec}`, "jest", "--version"],
            expect.objectContaining({
                cwd: REPO_ROOT,
                stdio: ["inherit", "inherit", "pipe"],
            })
        );
    });
});

describe("run-managed-jest self-heal and decoder integration", () => {
    let originalSkipIntegrity;
    beforeEach(() => {
        // Same rationale as the parent describe block: these tests exercise
        // tier-level recovery (npm ci, isolated cache reset, banner timing)
        // and predate the integrity gate. Skip the gate to isolate the
        // tier-level contract; the gate has its own dedicated suite below.
        originalSkipIntegrity = process.env.DXMSG_HOOK_SKIP_INTEGRITY;
        process.env.DXMSG_HOOK_SKIP_INTEGRITY = "1";
    });
    afterEach(() => {
        if (originalSkipIntegrity === undefined) {
            delete process.env.DXMSG_HOOK_SKIP_INTEGRITY;
        } else {
            process.env.DXMSG_HOOK_SKIP_INTEGRITY = originalSkipIntegrity;
        }
    });

    test("runCommandCapturingStderr returns { status, error, stderr } with stderr decoded", () => {
        // Use a tiny node invocation to validate the wiring end-to-end. This
        // is cross-platform because we invoke process.execPath directly (not
        // npm/npx), so no shell shim is involved.
        const writeStderrSpy = jest.spyOn(process.stderr, "write").mockImplementation(() => true);
        try {
            const result = runCommandCapturingStderr(
                process.execPath,
                ["-e", "process.stderr.write('hello'); process.exit(7);"]
            );

            expect(typeof result.stderr).toBe("string");
            expect(result.stderr).toContain("hello");
            expect(result.status).toBe(7);
            expect(result.error).toBeNull();
        } finally {
            writeStderrSpy.mockRestore();
        }
    });

    test("isolated-path MISSING_TEST_RUNNER stderr triggers exactly one cache reset and one retry", () => {
        // Use a captured runner path that lives under the isolated cache
        // root so the new path-classifier wire-up routes to the
        // cache-reset branch (the historical behavior). A non-classifiable
        // path now goes to the "unknown" branch and prints the banner
        // without auto-repair, covered by a separate test below.
        const isolatedRunnerPath = path.join(
            ISOLATED_JEST_CACHE_ROOT,
            "jest_30.3.0",
            "node_modules",
            "jest-circus",
            "build",
            "runner.js"
        );
        const failingResult = {
            status: 1,
            error: null,
            stderr: `Module ${isolatedRunnerPath} in the testRunner option was not found.`,
        };
        const recoveredResult = {
            status: 0,
            error: null,
            stderr: "",
        };
        const runIsolatedFallbackJestFn = jest
            .fn()
            .mockReturnValueOnce(failingResult)
            .mockReturnValueOnce(recoveredResult);
        const attemptIsolatedCacheResetFn = jest.fn(() => true);
        const attemptNpmCiRecoveryFn = jest.fn();
        const runLocalJestFn = jest.fn();
        const printActionableRepairBannerFn = jest.fn();
        const printLocalJestFallbackWarningFn = jest.fn();
        const existsSyncSpy = jest.spyOn(fs, "existsSync").mockReturnValue(true);

        try {
            const result = runManagedJest(["--version"], {
                hasHealthyLocalJestInstallFn: () => false,
                runLocalJestFn,
                runIsolatedFallbackJestFn,
                attemptIsolatedCacheResetFn,
                attemptNpmCiRecoveryFn,
                printActionableRepairBannerFn,
                printLocalJestFallbackWarningFn,
                getPinnedFallbackJestSpecFn: () => "jest@30.3.0",
            });

            expect(result).toBe(recoveredResult);
            expect(runIsolatedFallbackJestFn).toHaveBeenCalledTimes(2);
            expect(attemptIsolatedCacheResetFn).toHaveBeenCalledTimes(1);
            expect(attemptIsolatedCacheResetFn).toHaveBeenCalledWith("jest@30.3.0");
            // npm-ci recovery is scoped to the local tier; isolated reset
            // never invokes it.
            expect(attemptNpmCiRecoveryFn).not.toHaveBeenCalled();
            // No banner on successful self-heal.
            expect(printActionableRepairBannerFn).not.toHaveBeenCalled();
        } finally {
            existsSyncSpy.mockRestore();
        }
    });

    test("post-Jest MISSING_TEST_RUNNER with repo-classified path routes to npm ci recovery", () => {
        // The captured runner path lives under the repo's node_modules
        // tree, signaling a partial-extract failure that isolated cache
        // reset cannot fix. The classifier returns "repo" and the
        // dispatcher must invoke attemptNpmCiRecoveryFn (gated by
        // isAutoRepairAllowed) and re-probe via the subprocess probe.
        const repoRunnerPath = path.join(
            REPO_ROOT,
            "node_modules",
            "jest-circus",
            "build",
            "runner.js"
        );
        const failingResult = {
            status: 1,
            error: null,
            stderr: `Module ${repoRunnerPath} in the testRunner option was not found.`,
        };
        const recoveredResult = { status: 0, error: null, stderr: "" };
        const runIsolatedFallbackJestFn = jest
            .fn()
            .mockReturnValueOnce(failingResult)
            .mockReturnValueOnce(recoveredResult);
        const attemptIsolatedCacheResetFn = jest.fn();
        const attemptNpmCiRecoveryFn = jest.fn(() => ({ status: 0, error: null }));
        const probeIntegrityInSubprocessFn = jest.fn(() => ({ ok: true, missing: [] }));
        const isAutoRepairAllowedFn = jest.fn(() => ({ allowed: true, reason: null }));
        const printActionableRepairBannerFn = jest.fn();
        const printLocalJestFallbackWarningFn = jest.fn();
        const existsSyncSpy = jest.spyOn(fs, "existsSync").mockReturnValue(true);

        try {
            const result = runManagedJest(["--version"], {
                hasHealthyLocalJestInstallFn: () => false,
                runIsolatedFallbackJestFn,
                attemptIsolatedCacheResetFn,
                attemptNpmCiRecoveryFn,
                probeIntegrityInSubprocessFn,
                isAutoRepairAllowedFn,
                printActionableRepairBannerFn,
                printLocalJestFallbackWarningFn,
                getPinnedFallbackJestSpecFn: () => "jest@30.3.0",
            });

            expect(result).toBe(recoveredResult);
            // npm ci ran exactly once, cache reset did NOT.
            expect(attemptNpmCiRecoveryFn).toHaveBeenCalledTimes(1);
            expect(attemptIsolatedCacheResetFn).not.toHaveBeenCalled();
            // Subprocess re-probe ran before the retry.
            expect(probeIntegrityInSubprocessFn).toHaveBeenCalledTimes(1);
            // Retry attempt followed.
            expect(runIsolatedFallbackJestFn).toHaveBeenCalledTimes(2);
            // No banner on successful self-heal.
            expect(printActionableRepairBannerFn).not.toHaveBeenCalled();
        } finally {
            existsSyncSpy.mockRestore();
        }
    });

    test("post-Jest MISSING_TEST_RUNNER with unknown-classified path skips auto-repair and prints banner only", () => {
        // Captured path is outside BOTH the repo node_modules and the
        // isolated cache root. The classifier returns "unknown"; the
        // dispatcher must refuse to auto-repair and print the banner.
        const failingResult = {
            status: 1,
            error: null,
            stderr: "Module /nowhere/special/runner.js in the testRunner option was not found.",
        };
        const runIsolatedFallbackJestFn = jest.fn(() => failingResult);
        const attemptIsolatedCacheResetFn = jest.fn();
        const attemptNpmCiRecoveryFn = jest.fn();
        const probeIntegrityInSubprocessFn = jest.fn();
        const isAutoRepairAllowedFn = jest.fn(() => ({ allowed: true, reason: null }));
        const printActionableRepairBannerFn = jest.fn();
        const printLocalJestFallbackWarningFn = jest.fn();
        const existsSyncSpy = jest.spyOn(fs, "existsSync").mockReturnValue(true);
        const warnFn = jest.fn();

        try {
            const result = runManagedJest(["--version"], {
                hasHealthyLocalJestInstallFn: () => false,
                runIsolatedFallbackJestFn,
                attemptIsolatedCacheResetFn,
                attemptNpmCiRecoveryFn,
                probeIntegrityInSubprocessFn,
                isAutoRepairAllowedFn,
                printActionableRepairBannerFn,
                printLocalJestFallbackWarningFn,
                getPinnedFallbackJestSpecFn: () => "jest@30.3.0",
                warnFn,
            });

            expect(result).toBe(failingResult);
            // Neither auto-repair path was attempted.
            expect(attemptIsolatedCacheResetFn).not.toHaveBeenCalled();
            expect(attemptNpmCiRecoveryFn).not.toHaveBeenCalled();
            expect(probeIntegrityInSubprocessFn).not.toHaveBeenCalled();
            // Banner was printed exactly once.
            expect(printActionableRepairBannerFn).toHaveBeenCalledTimes(1);
            // Only the initial isolated call ran; no retry.
            expect(runIsolatedFallbackJestFn).toHaveBeenCalledTimes(1);
        } finally {
            existsSyncSpy.mockRestore();
        }
    });

    test("post-Jest MISSING_TEST_RUNNER with repo path is refused when auto-repair is disabled", () => {
        // Operator override: even with a repo-classified path, the
        // dispatcher honors isAutoRepairAllowed and prints the banner.
        const repoRunnerPath = path.join(
            REPO_ROOT,
            "node_modules",
            "jest-circus",
            "build",
            "runner.js"
        );
        const failingResult = {
            status: 1,
            error: null,
            stderr: `Module ${repoRunnerPath} in the testRunner option was not found.`,
        };
        const runIsolatedFallbackJestFn = jest.fn(() => failingResult);
        const attemptNpmCiRecoveryFn = jest.fn();
        const probeIntegrityInSubprocessFn = jest.fn();
        const isAutoRepairAllowedFn = jest.fn(() => ({
            allowed: false,
            reason: "DXMSG_HOOK_NO_AUTOREPAIR=1 set",
        }));
        const printActionableRepairBannerFn = jest.fn();
        const printLocalJestFallbackWarningFn = jest.fn();
        const existsSyncSpy = jest.spyOn(fs, "existsSync").mockReturnValue(true);

        try {
            const result = runManagedJest(["--version"], {
                hasHealthyLocalJestInstallFn: () => false,
                runIsolatedFallbackJestFn,
                attemptNpmCiRecoveryFn,
                probeIntegrityInSubprocessFn,
                isAutoRepairAllowedFn,
                printActionableRepairBannerFn,
                printLocalJestFallbackWarningFn,
                getPinnedFallbackJestSpecFn: () => "jest@30.3.0",
            });

            expect(result).toBe(failingResult);
            expect(attemptNpmCiRecoveryFn).not.toHaveBeenCalled();
            expect(probeIntegrityInSubprocessFn).not.toHaveBeenCalled();
            expect(printActionableRepairBannerFn).toHaveBeenCalledTimes(1);
            expect(runIsolatedFallbackJestFn).toHaveBeenCalledTimes(1);
        } finally {
            existsSyncSpy.mockRestore();
        }
    });

    test("CORRUPT_ISOLATED_CACHE (npmCi flag absent) bypasses classification and always cache-resets", () => {
        // CORRUPT_ISOLATED_CACHE's selfHeal only carries isolatedCacheReset
        // (no npmCi), so the dispatcher must skip the classifier entirely
        // and take the cache-reset branch even though stderr does not
        // expose a captured runner path. This locks the dispatch
        // shortcut so future regex changes don't accidentally change
        // routing semantics for this pattern.
        const failingResult = {
            status: 1,
            error: null,
            stderr: "Error: Cannot find module 'jest-circus/runner'",
        };
        const recoveredResult = { status: 0, error: null, stderr: "" };
        const runIsolatedFallbackJestFn = jest
            .fn()
            .mockReturnValueOnce(failingResult)
            .mockReturnValueOnce(recoveredResult);
        const attemptIsolatedCacheResetFn = jest.fn(() => true);
        const attemptNpmCiRecoveryFn = jest.fn();
        const printActionableRepairBannerFn = jest.fn();
        const printLocalJestFallbackWarningFn = jest.fn();
        const existsSyncSpy = jest.spyOn(fs, "existsSync").mockReturnValue(true);

        try {
            const result = runManagedJest(["--version"], {
                hasHealthyLocalJestInstallFn: () => false,
                runIsolatedFallbackJestFn,
                attemptIsolatedCacheResetFn,
                attemptNpmCiRecoveryFn,
                printActionableRepairBannerFn,
                printLocalJestFallbackWarningFn,
                getPinnedFallbackJestSpecFn: () => "jest@30.3.0",
            });

            expect(result).toBe(recoveredResult);
            expect(attemptIsolatedCacheResetFn).toHaveBeenCalledTimes(1);
            expect(attemptNpmCiRecoveryFn).not.toHaveBeenCalled();
        } finally {
            existsSyncSpy.mockRestore();
        }
    });

    test("local-path MISSING_LOCAL_JEST stderr triggers exactly one npm ci recovery", () => {
        const failingResult = {
            status: 1,
            error: null,
            stderr: "Error: Cannot find module 'jest/bin/jest.js'",
        };
        const recoveredResult = {
            status: 0,
            error: null,
            stderr: "",
        };
        const runLocalJestFn = jest
            .fn()
            .mockReturnValueOnce(failingResult)
            .mockReturnValueOnce(recoveredResult);
        const attemptNpmCiRecoveryFn = jest.fn(() => ({ status: 0, error: null }));
        const attemptIsolatedCacheResetFn = jest.fn();
        const printActionableRepairBannerFn = jest.fn();

        const result = runManagedJest(["--version"], {
            hasHealthyLocalJestInstallFn: () => true,
            runLocalJestFn,
            attemptNpmCiRecoveryFn,
            attemptIsolatedCacheResetFn,
            printActionableRepairBannerFn,
        });

        expect(result).toBe(recoveredResult);
        expect(runLocalJestFn).toHaveBeenCalledTimes(2);
        expect(attemptNpmCiRecoveryFn).toHaveBeenCalledTimes(1);
        // Local-tier failures must never trigger isolated cache reset.
        expect(attemptIsolatedCacheResetFn).not.toHaveBeenCalled();
        expect(printActionableRepairBannerFn).not.toHaveBeenCalled();
    });

    test("no decoder match passes status through unchanged with no recovery", () => {
        const failingResult = {
            status: 1,
            error: null,
            stderr: "Some other Jest assertion failure: expected true to be false",
        };
        const runLocalJestFn = jest.fn(() => failingResult);
        const attemptNpmCiRecoveryFn = jest.fn();
        const attemptIsolatedCacheResetFn = jest.fn();
        const printActionableRepairBannerFn = jest.fn();

        const result = runManagedJest(["--version"], {
            hasHealthyLocalJestInstallFn: () => true,
            runLocalJestFn,
            attemptNpmCiRecoveryFn,
            attemptIsolatedCacheResetFn,
            printActionableRepairBannerFn,
        });

        expect(result).toBe(failingResult);
        expect(runLocalJestFn).toHaveBeenCalledTimes(1);
        expect(attemptNpmCiRecoveryFn).not.toHaveBeenCalled();
        expect(attemptIsolatedCacheResetFn).not.toHaveBeenCalled();
        expect(printActionableRepairBannerFn).not.toHaveBeenCalled();
    });

    test("local-tier MISSING_TEST_RUNNER stderr does NOT invoke attemptIsolatedCacheReset and falls through to isolated tier", () => {
        // The LOCAL tier must scope cache reset to the isolated path only. It
        // also has no in-place repair for a MISSING_TEST_RUNNER stderr (npm
        // ci wouldn't fix a missing runner module), so the wrapper falls
        // through to the isolated-fallback tier. No banner is printed at the
        // local tier because a later tier may self-heal or surface its own
        // diagnostic.
        const failingResult = {
            status: 1,
            error: null,
            stderr: "Module /tmp/foo/runner.js in the testRunner option was not found.",
        };
        const isolatedResult = { status: 0, error: null };
        const runLocalJestFn = jest.fn(() => failingResult);
        const runIsolatedFallbackJestFn = jest.fn(() => isolatedResult);
        const attemptIsolatedCacheResetFn = jest.fn();
        const attemptNpmCiRecoveryFn = jest.fn();
        const printActionableRepairBannerFn = jest.fn();
        const printLocalJestFallbackWarningFn = jest.fn();
        const existsSyncSpyLocal = jest.spyOn(fs, "existsSync").mockReturnValue(true);

        try {
            const result = runManagedJest(["--version"], {
                hasHealthyLocalJestInstallFn: () => true,
                runLocalJestFn,
                runIsolatedFallbackJestFn,
                attemptIsolatedCacheResetFn,
                attemptNpmCiRecoveryFn,
                printActionableRepairBannerFn,
                printLocalJestFallbackWarningFn,
            });

            expect(result).toBe(isolatedResult);
            expect(runLocalJestFn).toHaveBeenCalledTimes(1);
            expect(runIsolatedFallbackJestFn).toHaveBeenCalledTimes(1);
            // Local tier must never invoke isolated cache reset.
            expect(attemptIsolatedCacheResetFn).not.toHaveBeenCalled();
            // npm ci is not appropriate for MISSING_TEST_RUNNER — selfHeal flag is isolatedCacheReset, not npmCi.
            expect(attemptNpmCiRecoveryFn).not.toHaveBeenCalled();
            // No banner at the local tier: fall-through defers to later tiers.
            expect(printActionableRepairBannerFn).not.toHaveBeenCalled();
        } finally {
            existsSyncSpyLocal.mockRestore();
        }
    });

    test("runLocalJest returns null when stderr matches MISSING_TEST_RUNNER so caller falls through to isolated tier", () => {
        // Production behavior: when `runLocalJest` itself observes a
        // MISSING_TEST_RUNNER stderr from the spawned Jest process, it treats
        // the local install as unhealthy and returns null. This avoids
        // forcing every caller to special-case the failure mode.
        const resolvedPath = path.join(REPO_ROOT, "node_modules", "jest-circus", "build", "runner.js");
        const failingResult = {
            status: 1,
            error: null,
            stderr: "Module /tmp/foo/runner.js in the testRunner option was not found.",
        };
        const runCommandFn = jest.fn(() => failingResult);
        const warnFn = jest.fn();

        const result = runLocalJest(["--version"], {
            moduleResolver: () => resolvedPath,
            tryLoadModuleFn: () => true,
            existsSyncFn: () => true,
            runCommandFn,
            warnFn,
        });

        expect(result).toBeNull();
        expect(runCommandFn).toHaveBeenCalledTimes(1);
        expect(
            warnFn.mock.calls.some((call) => String(call[0]).includes("MISSING_TEST_RUNNER"))
        ).toBe(true);
    });

    test("runLocalJest passes MISSING_LOCAL_JEST stderr through to caller (handled by runManagedJest)", () => {
        // Local-tier MISSING_LOCAL_JEST is recoverable in-place via `npm ci`,
        // so `runLocalJest` does NOT collapse it to null. The caller decides
        // whether to attempt recovery.
        const resolvedPath = path.join(REPO_ROOT, "node_modules", "jest-circus", "build", "runner.js");
        const failingResult = {
            status: 1,
            error: null,
            stderr: "Error: Cannot find module 'jest/bin/jest.js'",
        };
        const runCommandFn = jest.fn(() => failingResult);

        const result = runLocalJest(["--version"], {
            moduleResolver: () => resolvedPath,
            tryLoadModuleFn: () => true,
            existsSyncFn: () => true,
            runCommandFn,
            warnFn: () => {},
        });

        expect(result).toBe(failingResult);
    });

    test("banner is printed exactly once per final failure", () => {
        const failingResult = {
            status: 1,
            error: null,
            stderr: "Error: Cannot find module 'jest-circus/runner'",
        };
        const runIsolatedFallbackJestFn = jest.fn(() => failingResult);
        const attemptIsolatedCacheResetFn = jest.fn(() => true);
        const printActionableRepairBannerFn = jest.fn();
        const printLocalJestFallbackWarningFn = jest.fn();
        const existsSyncSpy = jest.spyOn(fs, "existsSync").mockReturnValue(true);

        try {
            // Both the initial isolated attempt and the retry fail with the
            // same CORRUPT_ISOLATED_CACHE stderr.
            const result = runManagedJest(["--version"], {
                hasHealthyLocalJestInstallFn: () => false,
                runIsolatedFallbackJestFn,
                attemptIsolatedCacheResetFn,
                printActionableRepairBannerFn,
                printLocalJestFallbackWarningFn,
                getPinnedFallbackJestSpecFn: () => "jest@30.3.0",
            });

            expect(result).toBe(failingResult);
            // Initial call + retry = 2 isolated invocations.
            expect(runIsolatedFallbackJestFn).toHaveBeenCalledTimes(2);
            expect(attemptIsolatedCacheResetFn).toHaveBeenCalledTimes(1);
            // Banner printed exactly once after the retry fails.
            expect(printActionableRepairBannerFn).toHaveBeenCalledTimes(1);
        } finally {
            existsSyncSpy.mockRestore();
        }
    });

    test("no banner is printed when self-heal retry succeeds", () => {
        // Use an isolated-cache-rooted runner path so the post-Jest
        // classifier routes to the cache-reset branch (the legacy
        // self-heal path). The dispatcher's "unknown" branch has its own
        // dedicated test above.
        const isolatedRunnerPath = path.join(
            ISOLATED_JEST_CACHE_ROOT,
            "jest_30.3.0",
            "node_modules",
            "jest-circus",
            "build",
            "runner.js"
        );
        const failingResult = {
            status: 1,
            error: null,
            stderr: `Module ${isolatedRunnerPath} in the testRunner option was not found.`,
        };
        const successResult = {
            status: 0,
            error: null,
            stderr: "",
        };
        const runIsolatedFallbackJestFn = jest
            .fn()
            .mockReturnValueOnce(failingResult)
            .mockReturnValueOnce(successResult);
        const attemptIsolatedCacheResetFn = jest.fn(() => true);
        const printActionableRepairBannerFn = jest.fn();
        const printLocalJestFallbackWarningFn = jest.fn();
        const existsSyncSpy = jest.spyOn(fs, "existsSync").mockReturnValue(true);

        try {
            const result = runManagedJest(["--version"], {
                hasHealthyLocalJestInstallFn: () => false,
                runIsolatedFallbackJestFn,
                attemptIsolatedCacheResetFn,
                printActionableRepairBannerFn,
                printLocalJestFallbackWarningFn,
                getPinnedFallbackJestSpecFn: () => "jest@30.3.0",
            });

            expect(result).toBe(successResult);
            expect(printActionableRepairBannerFn).not.toHaveBeenCalled();
        } finally {
            existsSyncSpy.mockRestore();
        }
    });

    test("attemptIsolatedCacheReset deletes the isolated install directory", () => {
        const rmSyncFn = jest.fn();
        const warnFn = jest.fn();
        const ok = attemptIsolatedCacheReset("jest@30.3.0", { rmSyncFn, warnFn });

        expect(ok).toBe(true);
        expect(rmSyncFn).toHaveBeenCalledTimes(1);
        const [installDir, options] = rmSyncFn.mock.calls[0];
        expect(installDir).toContain(path.join("dxmessaging-managed-jest", "jest_30.3.0"));
        expect(options).toEqual({ recursive: true, force: true });
        expect(warnFn).not.toHaveBeenCalled();
    });

    test("attemptIsolatedCacheReset refuses to delete when jestSpec resolves outside ISOLATED_JEST_CACHE_ROOT (parent traversal)", () => {
        // sanitizeCacheKey("..") returns "..", which would naively resolve to
        // the parent of ISOLATED_JEST_CACHE_ROOT. Defense-in-depth: refuse to
        // rm anything that isn't a strict descendant of the cache root.
        const rmSyncFn = jest.fn();
        const warnFn = jest.fn();

        const ok = attemptIsolatedCacheReset("..", { rmSyncFn, warnFn });

        expect(ok).toBe(false);
        expect(rmSyncFn).not.toHaveBeenCalled();
        expect(
            warnFn.mock.calls.some((call) => String(call[0]).includes("not a descendant"))
        ).toBe(true);
    });

    test("attemptIsolatedCacheReset refuses to delete when jestSpec resolves to ISOLATED_JEST_CACHE_ROOT itself (empty key)", () => {
        // Empty input sanitizes to "_" which IS a descendant, so use the
        // current-directory traversal ".". sanitizeCacheKey(".") returns ".",
        // which resolves back to the cache root itself — also forbidden.
        const rmSyncFn = jest.fn();
        const warnFn = jest.fn();

        const ok = attemptIsolatedCacheReset(".", { rmSyncFn, warnFn });

        expect(ok).toBe(false);
        expect(rmSyncFn).not.toHaveBeenCalled();
        expect(
            warnFn.mock.calls.some((call) => String(call[0]).includes("not a descendant"))
        ).toBe(true);
    });

    test("attemptIsolatedCacheReset deletes successfully when jestSpec resolves under ISOLATED_JEST_CACHE_ROOT", () => {
        const rmSyncFn = jest.fn();
        const warnFn = jest.fn();

        const ok = attemptIsolatedCacheReset("jest@30.3.0", { rmSyncFn, warnFn });

        expect(ok).toBe(true);
        expect(rmSyncFn).toHaveBeenCalledTimes(1);
        const [installDir] = rmSyncFn.mock.calls[0];
        expect(installDir.startsWith(path.resolve(ISOLATED_JEST_CACHE_ROOT))).toBe(true);
        expect(installDir).not.toBe(path.resolve(ISOLATED_JEST_CACHE_ROOT));
    });

    test("attemptIsolatedCacheReset does NOT trip the traversal guard for prefix-only matches like '..foo'", () => {
        // The guard rejects "", "..", and any path starting with ".." +
        // path.sep, but a sanitized key that merely STARTS with ".."
        // (e.g. "..foo") resolves to a legitimate descendant of the
        // cache root and must be allowed. This locks the
        // segment-boundary semantics of the guard.
        const rmSyncFn = jest.fn();
        const warnFn = jest.fn();

        // sanitizeCacheKey("..foo") -> "..foo" (allowed chars: ._-a-zA-Z0-9).
        const ok = attemptIsolatedCacheReset("..foo", { rmSyncFn, warnFn });

        expect(ok).toBe(true);
        expect(rmSyncFn).toHaveBeenCalledTimes(1);
        const [installDir] = rmSyncFn.mock.calls[0];
        expect(installDir.startsWith(path.resolve(ISOLATED_JEST_CACHE_ROOT))).toBe(true);
        // The directory name must literally be "..foo" sanitized form.
        expect(installDir).toContain("..foo");
        expect(warnFn).not.toHaveBeenCalled();
    });

    test("attemptIsolatedCacheReset returns false when rm fails", () => {
        const rmSyncFn = jest.fn(() => {
            throw new Error("EBUSY: resource busy");
        });
        const warnFn = jest.fn();
        const ok = attemptIsolatedCacheReset("jest@30.3.0", { rmSyncFn, warnFn });

        expect(ok).toBe(false);
        expect(warnFn).toHaveBeenCalledTimes(1);
        expect(warnFn.mock.calls[0][0]).toContain("EBUSY");
    });

    test("attemptNpmCiRecovery invokes npm ci and returns the runCommand result", () => {
        const runCommandFn = jest.fn(() => ({ status: 0, error: null }));
        const warnFn = jest.fn();
        const result = attemptNpmCiRecovery({ runCommandFn, warnFn });

        expect(result).toEqual({ status: 0, error: null });
        expect(runCommandFn).toHaveBeenCalledTimes(1);
        const [command, args, options] = runCommandFn.mock.calls[0];
        expect(command).toBe("npm");
        expect(args).toEqual(["ci", "--no-audit", "--no-fund"]);
        expect(options).toEqual(expect.objectContaining({ cwd: REPO_ROOT }));
    });

    test("attemptNpmCiRecovery returns the failure result and warns when npm ci fails", () => {
        const failureResult = { status: 1, error: null };
        const runCommandFn = jest.fn(() => failureResult);
        const warnFn = jest.fn();
        const result = attemptNpmCiRecovery({ runCommandFn, warnFn });

        expect(result).toBe(failureResult);
        // Assert content, not call count: the wrapper announces the attempt
        // and then the failure. Exact call count is brittle to future logging
        // additions.
        const warnMessages = warnFn.mock.calls.map((call) => String(call[0]));
        expect(warnMessages.some((message) => message.includes("Attempting `npm ci` recovery"))).toBe(true);
        expect(warnMessages.some((message) => message.includes("did not succeed"))).toBe(true);
    });

    test("printActionableRepairBanner writes the banner once, no-op on null decoded", () => {
        const writeFn = jest.fn();

        printActionableRepairBanner(null, { writeFn, envCi: "" });
        expect(writeFn).not.toHaveBeenCalled();

        const decoded = {
            kind: "MISSING_TEST_RUNNER",
            regex: /x/,
            summary: "Test summary.",
            rootCauses: ["cause one"],
            repairCommands: ["do thing"],
            skillRef: ".llm/skills/scripting/jest-hook-robustness.md",
            selfHeal: { retryOnce: true },
            capturedMatch: null,
        };
        printActionableRepairBanner(decoded, { writeFn, envCi: "1" });
        expect(writeFn).toHaveBeenCalledTimes(1);
        expect(writeFn.mock.calls[0][0]).toContain("jest-hook diagnostic: MISSING_TEST_RUNNER");
        expect(writeFn.mock.calls[0][0]).toContain("do thing");
    });
});

describe("run-managed-jest integrity gate (Step 5)", () => {
    // These tests exercise the integrity gate without skipping it, so they
    // control the gate via injected fakes only. They live in their own
    // describe block so the parent's DXMSG_HOOK_SKIP_INTEGRITY setup does
    // not interfere.

    function makeIntegrityResult(ok, missing = []) {
        return { ok, missing };
    }

    test("integrity gate runs BEFORE any tier", () => {
        const probeIntegrityFn = jest.fn(() => makeIntegrityResult(false, [
            { tool: "jest-circus", relPath: "node_modules/jest-circus/build/runner.js", reason: "missing" },
        ]));
        const probeIntegrityInSubprocessFn = jest.fn(() => makeIntegrityResult(true));
        const attemptNpmCiRecoveryFn = jest.fn(() => ({ status: 0, error: null }));
        const isAutoRepairAllowedFn = jest.fn(() => ({ allowed: true, reason: null }));
        const runLocalJestFn = jest.fn(() => ({ status: 0, error: null, stderr: "" }));
        const runIsolatedFallbackJestFn = jest.fn();
        const runNpmExecJestFn = jest.fn();
        const runNpxJestFn = jest.fn();
        const printActionableRepairBannerFn = jest.fn();
        // Re-implement runIntegrityGateWithRecoveryFn to provably gate tier
        // dispatch via call ordering. The default implementation already
        // does this; the test asserts the wiring.
        const result = runManagedJest(["--version"], {
            envFn: () => ({}),
            probeIntegrityFn,
            probeIntegrityInSubprocessFn,
            attemptNpmCiRecoveryFn,
            isAutoRepairAllowedFn,
            runLocalJestFn,
            runIsolatedFallbackJestFn,
            runNpmExecJestFn,
            runNpxJestFn,
            hasHealthyLocalJestInstallFn: () => true,
            printActionableRepairBannerFn,
            // Provide the real gate so the call ordering test is meaningful.
        });

        // Initial probe must run.
        expect(probeIntegrityFn).toHaveBeenCalledTimes(1);
        // Auto-repair decision must be consulted because integrity failed.
        expect(isAutoRepairAllowedFn).toHaveBeenCalledTimes(1);
        // npm ci is invoked.
        expect(attemptNpmCiRecoveryFn).toHaveBeenCalledTimes(1);
        // Subprocess re-probe runs.
        expect(probeIntegrityInSubprocessFn).toHaveBeenCalledTimes(1);
        // After re-probe succeeds, tier dispatch proceeds (runLocalJestFn
        // was called).
        expect(runLocalJestFn).toHaveBeenCalledTimes(1);
        // Result preserves whatever runLocalJestFn returned (here null ->
        // cascades to runIsolatedFallbackJestFn, etc., but our fakes return
        // undefined). The important assertion is that the call ORDER had
        // the gate first.
        // Establish call order via mock.invocationCallOrder.
        expect(probeIntegrityFn.mock.invocationCallOrder[0]).toBeLessThan(
            isAutoRepairAllowedFn.mock.invocationCallOrder[0]
        );
        expect(isAutoRepairAllowedFn.mock.invocationCallOrder[0]).toBeLessThan(
            attemptNpmCiRecoveryFn.mock.invocationCallOrder[0]
        );
        expect(attemptNpmCiRecoveryFn.mock.invocationCallOrder[0]).toBeLessThan(
            probeIntegrityInSubprocessFn.mock.invocationCallOrder[0]
        );
        expect(probeIntegrityInSubprocessFn.mock.invocationCallOrder[0]).toBeLessThan(
            runLocalJestFn.mock.invocationCallOrder[0]
        );
        // No banner because recovery succeeded.
        expect(printActionableRepairBannerFn).not.toHaveBeenCalled();
        // Result preserves whatever runLocalJestFn returned. The important
        // assertion is the call ORDER above; the wrapper returns the
        // tier's result on success.
        expect(result).toEqual(expect.objectContaining({ status: 0 }));
    });

    test("integrity ok: gate does not run npm ci or print banner; tier dispatch proceeds", () => {
        const probeIntegrityFn = jest.fn(() => makeIntegrityResult(true));
        const probeIntegrityInSubprocessFn = jest.fn();
        const attemptNpmCiRecoveryFn = jest.fn();
        const isAutoRepairAllowedFn = jest.fn();
        const runLocalJestFn = jest.fn(() => ({ status: 0, error: null }));
        const printActionableRepairBannerFn = jest.fn();

        const result = runManagedJest(["--version"], {
            envFn: () => ({}),
            probeIntegrityFn,
            probeIntegrityInSubprocessFn,
            attemptNpmCiRecoveryFn,
            isAutoRepairAllowedFn,
            runLocalJestFn,
            hasHealthyLocalJestInstallFn: () => true,
            printActionableRepairBannerFn,
        });

        expect(probeIntegrityFn).toHaveBeenCalledTimes(1);
        expect(isAutoRepairAllowedFn).not.toHaveBeenCalled();
        expect(attemptNpmCiRecoveryFn).not.toHaveBeenCalled();
        expect(probeIntegrityInSubprocessFn).not.toHaveBeenCalled();
        expect(printActionableRepairBannerFn).not.toHaveBeenCalled();
        expect(runLocalJestFn).toHaveBeenCalledTimes(1);
        expect(result).toEqual({ status: 0, error: null });
    });

    test("single-pass after failed auto-repair: banner printed once, no Jest invocation", () => {
        const probeIntegrityFn = jest.fn(() => makeIntegrityResult(false, [
            { tool: "jest", relPath: "node_modules/jest/bin/jest.js", reason: "missing" },
        ]));
        const probeIntegrityInSubprocessFn = jest.fn(() => makeIntegrityResult(false, [
            { tool: "jest", relPath: "node_modules/jest/bin/jest.js", reason: "missing" },
        ]));
        const attemptNpmCiRecoveryFn = jest.fn(() => ({ status: 0, error: null }));
        const isAutoRepairAllowedFn = jest.fn(() => ({ allowed: true, reason: null }));
        const runLocalJestFn = jest.fn();
        const printActionableRepairBannerFn = jest.fn();

        const result = runManagedJest(["--version"], {
            envFn: () => ({}),
            probeIntegrityFn,
            probeIntegrityInSubprocessFn,
            attemptNpmCiRecoveryFn,
            isAutoRepairAllowedFn,
            runLocalJestFn,
            hasHealthyLocalJestInstallFn: () => true,
            printActionableRepairBannerFn,
        });

        // The gate ran probe -> repair -> reprobe exactly once each.
        expect(probeIntegrityFn).toHaveBeenCalledTimes(1);
        expect(attemptNpmCiRecoveryFn).toHaveBeenCalledTimes(1);
        expect(probeIntegrityInSubprocessFn).toHaveBeenCalledTimes(1);
        // Banner printed exactly once.
        expect(printActionableRepairBannerFn).toHaveBeenCalledTimes(1);
        // Tier dispatch was NOT invoked.
        expect(runLocalJestFn).not.toHaveBeenCalled();
        // Status = 1, error null per the gate's failure contract.
        expect(result).toEqual({ status: 1, error: null });
    });

    test("DXMSG_HOOK_NO_AUTOREPAIR=1 skips npm ci but proceeds with degraded gate", () => {
        const probeIntegrityFn = jest.fn(() => makeIntegrityResult(false, [
            { tool: "jest", relPath: "node_modules/jest/bin/jest.js", reason: "missing" },
        ]));
        const probeIntegrityInSubprocessFn = jest.fn();
        const attemptNpmCiRecoveryFn = jest.fn();
        const runLocalJestFn = jest.fn(() => ({ status: 5, error: null }));
        const printActionableRepairBannerFn = jest.fn();

        const result = runManagedJest(["--version"], {
            envFn: () => ({ DXMSG_HOOK_NO_AUTOREPAIR: "1" }),
            probeIntegrityFn,
            probeIntegrityInSubprocessFn,
            attemptNpmCiRecoveryFn,
            runLocalJestFn,
            hasHealthyLocalJestInstallFn: () => true,
            printActionableRepairBannerFn,
        });

        // Probe ran, npm ci skipped, subprocess re-probe skipped.
        expect(probeIntegrityFn).toHaveBeenCalledTimes(1);
        expect(attemptNpmCiRecoveryFn).not.toHaveBeenCalled();
        expect(probeIntegrityInSubprocessFn).not.toHaveBeenCalled();
        // Banner printed once for the integrity failure.
        expect(printActionableRepairBannerFn).toHaveBeenCalledTimes(1);
        // Tier dispatch proceeded (degraded gate) and returned status=5.
        expect(runLocalJestFn).toHaveBeenCalledTimes(1);
        expect(result).toEqual({ status: 5, error: null });
    });

    test("DXMSG_HOOK_SKIP_INTEGRITY=1 skips probe entirely", () => {
        const probeIntegrityFn = jest.fn();
        const probeIntegrityInSubprocessFn = jest.fn();
        const attemptNpmCiRecoveryFn = jest.fn();
        const runLocalJestFn = jest.fn(() => ({ status: 0, error: null }));

        const result = runManagedJest(["--version"], {
            envFn: () => ({ DXMSG_HOOK_SKIP_INTEGRITY: "1" }),
            probeIntegrityFn,
            probeIntegrityInSubprocessFn,
            attemptNpmCiRecoveryFn,
            runLocalJestFn,
            hasHealthyLocalJestInstallFn: () => true,
        });

        // Gate is bypassed entirely.
        expect(probeIntegrityFn).not.toHaveBeenCalled();
        expect(probeIntegrityInSubprocessFn).not.toHaveBeenCalled();
        expect(attemptNpmCiRecoveryFn).not.toHaveBeenCalled();
        expect(runLocalJestFn).toHaveBeenCalledTimes(1);
        expect(result).toEqual({ status: 0, error: null });
    });

    test("auto-repair refused when getNpmMajorVersion returns null", () => {
        const probeIntegrityFn = jest.fn(() => makeIntegrityResult(false, [
            { tool: "jest", relPath: "node_modules/jest/bin/jest.js", reason: "missing" },
        ]));
        const attemptNpmCiRecoveryFn = jest.fn();
        const printActionableRepairBannerFn = jest.fn();
        // Use the production isAutoRepairAllowed by NOT injecting one; pass a
        // fake getNpmMajorVersionFn that returns null.
        const result = runManagedJest(["--version"], {
            envFn: () => ({}),
            probeIntegrityFn,
            attemptNpmCiRecoveryFn,
            getNpmMajorVersionFn: () => null,
            printActionableRepairBannerFn,
            hasHealthyLocalJestInstallFn: () => true,
            runLocalJestFn: jest.fn(),
        });

        expect(attemptNpmCiRecoveryFn).not.toHaveBeenCalled();
        expect(printActionableRepairBannerFn).toHaveBeenCalledTimes(1);
        expect(result).toEqual({ status: 1, error: null });
    });

    test("DXMSG_HOOK_AGGRESSIVE_RECOVERY=1 invokes rmSync before npm ci", () => {
        const rmSyncFn = jest.fn();
        const runCommandFn = jest.fn(() => ({ status: 0, error: null }));
        const warnFn = jest.fn();
        const result = attemptNpmCiRecovery({
            rmSyncFn,
            runCommandFn,
            envFn: () => ({ DXMSG_HOOK_AGGRESSIVE_RECOVERY: "1" }),
            warnFn,
        });

        expect(rmSyncFn).toHaveBeenCalledTimes(1);
        const [rmTarget, rmOpts] = rmSyncFn.mock.calls[0];
        expect(rmTarget).toContain("node_modules");
        expect(rmOpts).toEqual({ recursive: true, force: true });
        // npm ci followed rm.
        expect(runCommandFn.mock.invocationCallOrder[0]).toBeGreaterThan(
            rmSyncFn.mock.invocationCallOrder[0]
        );
        expect(result).toEqual({ status: 0, error: null });
    });

    test("attemptNpmCiRecovery does NOT rm without DXMSG_HOOK_AGGRESSIVE_RECOVERY=1", () => {
        const rmSyncFn = jest.fn();
        const runCommandFn = jest.fn(() => ({ status: 0, error: null }));
        attemptNpmCiRecovery({
            rmSyncFn,
            runCommandFn,
            envFn: () => ({}),
            warnFn: jest.fn(),
        });
        expect(rmSyncFn).not.toHaveBeenCalled();
        expect(runCommandFn).toHaveBeenCalledTimes(1);
    });

    test("attemptNpmCiRecovery passes explicit cwd: REPO_ROOT", () => {
        const runCommandFn = jest.fn(() => ({ status: 0, error: null }));
        attemptNpmCiRecovery({
            runCommandFn,
            envFn: () => ({}),
            warnFn: jest.fn(),
        });
        expect(runCommandFn).toHaveBeenCalledTimes(1);
        const [, , opts] = runCommandFn.mock.calls[0];
        expect(opts).toEqual(expect.objectContaining({ cwd: REPO_ROOT }));
    });
});

describe("isAutoRepairAllowed (production policy)", () => {
    const { isAutoRepairAllowed } = require("../lib/integrity-gate-with-recovery");

    test("refused when getNpmMajorVersionFn returns null", () => {
        const result = isAutoRepairAllowed({
            env: {},
            repoRoot: "/repo",
            getNpmMajorVersionFn: () => null,
            existsSyncFn: () => false,
            spawnPlatformCommandSyncFn: () => ({ status: 0 }),
        });
        expect(result.allowed).toBe(false);
        expect(result.reason).toContain("npm executable unavailable");
    });

    test("refused mid-rebase (.git/rebase-merge present)", () => {
        const result = isAutoRepairAllowed({
            env: {},
            repoRoot: "/repo",
            getNpmMajorVersionFn: () => 10,
            existsSyncFn: (p) => p.endsWith("rebase-merge"),
            spawnPlatformCommandSyncFn: () => ({ status: 0 }),
        });
        expect(result.allowed).toBe(false);
        expect(result.reason).toContain("rebase-merge");
    });

    test("refused mid-rebase-apply", () => {
        const result = isAutoRepairAllowed({
            env: {},
            repoRoot: "/repo",
            getNpmMajorVersionFn: () => 10,
            existsSyncFn: (p) => p.endsWith("rebase-apply"),
            spawnPlatformCommandSyncFn: () => ({ status: 0 }),
        });
        expect(result.allowed).toBe(false);
        expect(result.reason).toContain("rebase-apply");
    });

    test("refused when package-lock dirty (git diff --quiet exits non-zero)", () => {
        // Also asserts that the production policy forwards cwd: repoRoot to
        // spawnPlatformCommandSyncFn. Without that, `git diff --quiet
        // package-lock.json` would run from whatever the parent process
        // happened to chdir to, which on Windows during pre-commit hooks
        // can be different from the repo root.
        const spawnPlatformCommandSyncFn = jest.fn(() => ({ status: 1 }));
        const result = isAutoRepairAllowed({
            env: {},
            repoRoot: "/repo",
            getNpmMajorVersionFn: () => 10,
            existsSyncFn: () => false,
            spawnPlatformCommandSyncFn,
        });
        expect(result.allowed).toBe(false);
        expect(result.reason).toContain("package-lock.json");
        expect(spawnPlatformCommandSyncFn).toHaveBeenCalledTimes(1);
        const [cmd, args, opts] = spawnPlatformCommandSyncFn.mock.calls[0];
        expect(cmd).toBe("git");
        expect(args).toEqual(["diff", "--quiet", "--", "package-lock.json"]);
        expect(opts).toEqual(expect.objectContaining({ cwd: "/repo" }));
    });

    test("refused when DXMSG_HOOK_NO_AUTOREPAIR is set", () => {
        const result = isAutoRepairAllowed({
            env: { DXMSG_HOOK_NO_AUTOREPAIR: "1" },
            repoRoot: "/repo",
            getNpmMajorVersionFn: () => 10,
            existsSyncFn: () => false,
            spawnPlatformCommandSyncFn: () => ({ status: 0 }),
        });
        expect(result.allowed).toBe(false);
        expect(result.reason).toContain("DXMSG_HOOK_NO_AUTOREPAIR");
    });

    test("allowed in clean state", () => {
        const result = isAutoRepairAllowed({
            env: {},
            repoRoot: "/repo",
            getNpmMajorVersionFn: () => 10,
            existsSyncFn: () => false,
            spawnPlatformCommandSyncFn: () => ({ status: 0 }),
        });
        expect(result.allowed).toBe(true);
        expect(result.reason).toBeNull();
    });

    test("refused when spawnPlatformCommandSync returns null (defense in depth)", () => {
        // A null spawn result (process spawn outright failed) must be
        // treated the same as a non-zero exit: refuse, so npm ci cannot
        // run in an environment where we cannot even read the lockfile
        // status.
        const result = isAutoRepairAllowed({
            env: {},
            repoRoot: "/repo",
            getNpmMajorVersionFn: () => 10,
            existsSyncFn: () => false,
            spawnPlatformCommandSyncFn: () => null,
        });
        expect(result.allowed).toBe(false);
        expect(result.reason).toContain("package-lock.json");
    });
});

describe("runIntegrityGateWithRecovery (native binary + formatter wiring)", () => {
    const { runIntegrityGateWithRecovery } = require("../lib/integrity-gate-with-recovery");
    const jestErrorDecoderModule = require("../lib/jest-error-decoder");

    test("Windows-only: gate runs findZeroByteNativeBinaries and includes results in missing[]", () => {
        // The probe itself returns ok=true, but a zero-byte *.node binary
        // surfaces a second-class failure that should be reported through
        // the same auto-repair flow. Inject platformFn so we don't depend
        // on the host actually being Windows.
        const findZeroByteNativeBinariesFn = jest.fn(() => [
            "node_modules/fake-native-binding/build/Release/binding.node",
        ]);
        let captured;
        const formatIntegrityFailureFn = jest.fn((result) => {
            captured = result;
            return "Integrity probe failed: synthetic-fixture";
        });
        const warnFn = jest.fn();
        const printActionableRepairBannerFn = jest.fn();
        const isAutoRepairAllowedFn = jest.fn(() => ({ allowed: false, reason: "test" }));

        const result = runIntegrityGateWithRecovery({
            repoRoot: "/repo",
            probeIntegrityFn: () => ({ ok: true, missing: [] }),
            probeIntegrityInSubprocessFn: jest.fn(),
            attemptNpmCiRecoveryFn: jest.fn(),
            isAutoRepairAllowedFn,
            printActionableRepairBannerFn,
            decoder: jestErrorDecoderModule,
            findZeroByteNativeBinariesFn,
            formatIntegrityFailureFn,
            platformFn: () => "win32",
            warnFn,
        });

        expect(findZeroByteNativeBinariesFn).toHaveBeenCalledTimes(1);
        expect(findZeroByteNativeBinariesFn).toHaveBeenCalledWith(
            expect.objectContaining({ repoRoot: "/repo", platform: "win32" })
        );
        // The captured failure passed to the formatter must include the
        // synthetic native-binding entry concatenated to missing[].
        expect(captured.missing.some((m) => m.tool === "<native-binding>" && m.reason === "zero-byte")).toBe(true);
        // augmented result is not ok because zero-byte natives were found.
        expect(result.ok).toBe(false);
    });

    test("non-Windows: findZeroByteNativeBinaries returns [] and the augmented result mirrors initial.ok", () => {
        const findZeroByteNativeBinariesFn = jest.fn(() => []);
        const formatIntegrityFailureFn = jest.fn(() => "noop");
        const result = runIntegrityGateWithRecovery({
            repoRoot: "/repo",
            probeIntegrityFn: () => ({ ok: true, missing: [] }),
            probeIntegrityInSubprocessFn: jest.fn(),
            attemptNpmCiRecoveryFn: jest.fn(),
            isAutoRepairAllowedFn: jest.fn(),
            printActionableRepairBannerFn: jest.fn(),
            decoder: jestErrorDecoderModule,
            findZeroByteNativeBinariesFn,
            formatIntegrityFailureFn,
            platformFn: () => "linux",
            warnFn: jest.fn(),
        });
        expect(findZeroByteNativeBinariesFn).toHaveBeenCalledWith(
            expect.objectContaining({ platform: "linux" })
        );
        expect(result.ok).toBe(true);
        // The formatter is never invoked on a clean pass.
        expect(formatIntegrityFailureFn).not.toHaveBeenCalled();
    });

    test("warning text comes from formatIntegrityFailure (not an ad-hoc string)", () => {
        const formatIntegrityFailureFn = jest.fn(
            () => "Integrity probe failed: missing CANARY (missing) for jest-circus"
        );
        const warnFn = jest.fn();
        runIntegrityGateWithRecovery({
            repoRoot: "/repo",
            probeIntegrityFn: () => ({
                ok: false,
                missing: [
                    { tool: "jest-circus", relPath: "node_modules/jest-circus/build/runner.js", reason: "missing" },
                ],
            }),
            probeIntegrityInSubprocessFn: jest.fn(),
            attemptNpmCiRecoveryFn: jest.fn(() => ({ status: 1 })),
            isAutoRepairAllowedFn: jest.fn(() => ({ allowed: true })),
            printActionableRepairBannerFn: jest.fn(),
            decoder: jestErrorDecoderModule,
            findZeroByteNativeBinariesFn: () => [],
            formatIntegrityFailureFn,
            platformFn: () => "linux",
            warnFn,
        });
        // The warn line includes the formatter output verbatim.
        const sawCanary = warnFn.mock.calls.some((call) =>
            String(call[0]).includes("CANARY")
        );
        expect(sawCanary).toBe(true);
    });
});