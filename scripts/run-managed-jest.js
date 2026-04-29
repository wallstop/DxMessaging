#!/usr/bin/env node
/**
 * run-managed-jest.js
 *
 * Runs Jest in a robust, non-interactive way for hooks and local automation:
 * 1) Prefer local devDependency (node_modules/jest/bin/jest.js).
 * 2) If local Jest is missing, provision a pinned fallback via npm exec.
 * 3) If npm is too old for npm exec (or unavailable), fall back to npx.
 */

"use strict";

const fs = require("fs");
const os = require("os");
const path = require("path");
const childProcess = require("child_process");
const { createRequire } = require("module");
const {
    toShellCommand,
    isShellShimCommand,
    spawnPlatformCommandSync,
} = require("./lib/shell-command");

const REPO_ROOT = path.join(__dirname, "..");
const REPO_NODE_MODULES = path.join(REPO_ROOT, "node_modules");
const PACKAGE_LOCK_PATH = path.join(REPO_ROOT, "package-lock.json");
const LOCAL_JEST_BIN = path.join(REPO_ROOT, "node_modules", "jest", "bin", "jest.js");
const FALLBACK_JEST_SPEC = "jest@30.3.0";
const ISOLATED_JEST_CACHE_ROOT = path.join(os.tmpdir(), "dxmessaging-managed-jest");
const REPO_REQUIRE = createRequire(path.join(REPO_ROOT, "package.json"));

function parseNpmMajorVersion(versionText) {
    if (typeof versionText !== "string") {
        return null;
    }

    const match = /^v?(\d+)/.exec(versionText.trim());
    if (!match) {
        return null;
    }

    return Number(match[1]);
}

function getNpmMajorVersion() {
    const result = spawnPlatformCommandSync("npm", ["--version"], {
        cwd: REPO_ROOT,
        encoding: "utf8",
        stdio: ["ignore", "pipe", "pipe"],
    });

    if (result.error || result.status !== 0) {
        return null;
    }

    return parseNpmMajorVersion(result.stdout);
}

function runCommand(command, args, spawnOptions = {}) {
    const spawnSyncImpl = isShellShimCommand(command)
        ? spawnPlatformCommandSync
        : childProcess.spawnSync;

    const result = spawnSyncImpl(command, args, {
        cwd: REPO_ROOT,
        stdio: "inherit",
        ...spawnOptions,
    });

    return {
        status: result.status,
        error: result.error || null,
    };
}

function runLocalJest(args) {
    return runCommand(process.execPath, [LOCAL_JEST_BIN, ...args]);
}

function getPinnedFallbackJestSpec(
    readFileSyncFn = fs.readFileSync,
    fallbackSpec = FALLBACK_JEST_SPEC
) {
    try {
        const packageLock = JSON.parse(readFileSyncFn(PACKAGE_LOCK_PATH, "utf8"));
        const lockedVersion = packageLock && packageLock.packages && packageLock.packages["node_modules/jest"] && packageLock.packages["node_modules/jest"].version;
        if (typeof lockedVersion === "string" && /^\d+\.\d+\.\d+$/.test(lockedVersion)) {
            return `jest@${lockedVersion}`;
        }
    } catch {
        // Fall through to static fallback when lockfile is unavailable or malformed.
    }

    return fallbackSpec;
}

function runNpmExecJest(args) {
    const jestSpec = getPinnedFallbackJestSpec();
    return runCommand("npm", [
        "exec",
        "--yes",
        `--package=${jestSpec}`,
        "--",
        "jest",
        ...args,
    ]);
}

function runNpxJest(args) {
    const jestSpec = getPinnedFallbackJestSpec();
    return runCommand("npx", [
        "--yes",
        `--package=${jestSpec}`,
        "jest",
        ...args,
    ]);
}

function sanitizeCacheKey(value) {
    return String(value).replace(/[^a-zA-Z0-9._-]+/g, "_");
}

function getIsolatedJestPaths(jestSpec) {
    const cacheKey = sanitizeCacheKey(jestSpec);
    const installDir = path.join(ISOLATED_JEST_CACHE_ROOT, cacheKey);
    return {
        installDir,
        packageJsonPath: path.join(installDir, "package.json"),
        jestBinPath: path.join(installDir, "node_modules", "jest", "bin", "jest.js"),
        jestRunnerPath: path.join(installDir, "node_modules", "jest-circus", "build", "runner.js"),
    };
}

function hasCliOption(args, optionName) {
    const normalizedOption = optionName.startsWith("--")
        ? optionName
        : `--${optionName}`;

    return args.some((arg) =>
        arg === normalizedOption || arg.startsWith(`${normalizedOption}=`)
    );
}

function buildNodePathEnv(isolatedNodeModulesPath, baseEnv = process.env) {
    const existingNodePath = baseEnv.NODE_PATH;
    const nextNodePath = existingNodePath
        ? `${isolatedNodeModulesPath}${path.delimiter}${existingNodePath}`
        : isolatedNodeModulesPath;

    return {
        ...baseEnv,
        NODE_PATH: nextNodePath,
    };
}

function writeIsolatedJestCacheManifest(
    packageJsonPath,
    writeFileSyncFn = fs.writeFileSync
) {
    const manifest = {
        name: "dxmessaging-managed-jest-fallback-cache",
        private: true,
    };
    writeFileSyncFn(packageJsonPath, `${JSON.stringify(manifest, null, 2)}\n`, "utf8");
}

function prepareIsolatedFallbackJest(
    jestSpec = getPinnedFallbackJestSpec(),
    {
        existsSyncFn = fs.existsSync,
        mkdirSyncFn = fs.mkdirSync,
        writeFileSyncFn = fs.writeFileSync,
        runCommandFn = runCommand,
        warnFn = console.warn,
    } = {}
) {
    const { installDir, packageJsonPath, jestBinPath, jestRunnerPath } = getIsolatedJestPaths(jestSpec);

    const hasCachedJestBin = existsSyncFn(jestBinPath);
    const hasCachedJestRunner = existsSyncFn(jestRunnerPath);

    if (hasCachedJestBin && hasCachedJestRunner) {
        return {
            jestBinPath,
            jestRunnerPath,
            cacheHit: true,
        };
    }

    if (hasCachedJestBin && !hasCachedJestRunner) {
        warnFn(`⚠️ Isolated fallback cache is missing Jest runner; reinstalling fallback: ${jestRunnerPath}`);
    }

    mkdirSyncFn(installDir, { recursive: true });

    if (!existsSyncFn(packageJsonPath)) {
        writeIsolatedJestCacheManifest(packageJsonPath, writeFileSyncFn);
    }

    warnFn(`⚠️ Installing isolated fallback Jest (${jestSpec}).`);
    const installResult = runCommandFn(
        "npm",
        [
            "install",
            "--no-audit",
            "--no-fund",
            "--no-package-lock",
            "--no-save",
            jestSpec,
        ],
        {
            cwd: installDir,
        }
    );

    if (installResult.error || installResult.status !== 0) {
        const detail = installResult.error && installResult.error.message
            ? installResult.error.message
            : `status=${installResult.status}`;
        warnFn(`⚠️ Isolated fallback Jest install failed (${detail}).`);
        return {
            jestBinPath: null,
            jestRunnerPath: null,
            cacheHit: false,
        };
    }

    if (!existsSyncFn(jestBinPath)) {
        warnFn(`⚠️ Isolated fallback Jest binary missing after install: ${jestBinPath}`);
        return {
            jestBinPath: null,
            jestRunnerPath: null,
            cacheHit: false,
        };
    }

    if (!existsSyncFn(jestRunnerPath)) {
        warnFn(`⚠️ Isolated fallback Jest runner missing after install: ${jestRunnerPath}`);
        return {
            jestBinPath: null,
            jestRunnerPath: null,
            cacheHit: false,
        };
    }

    return {
        jestBinPath,
        jestRunnerPath,
        cacheHit: false,
    };
}

function printIsolatedFallbackSelection(
    jestBinPath,
    cacheHit,
    {
        testRunnerPath = null,
        testRunnerInjected = false,
        callerProvidedTestRunner = false,
        nodePathOverride = null,
    } = {}
) {
    const cacheLabel = cacheHit ? "cache hit" : "fresh install";
    console.warn(`⚠️ Using isolated fallback Jest (${cacheLabel}): ${jestBinPath}`);

    if (testRunnerInjected && testRunnerPath) {
        console.warn(`⚠️ Injected isolated Jest test runner: ${testRunnerPath}`);
    }

    if (callerProvidedTestRunner) {
        console.warn("⚠️ Caller provided --testRunner; managed runner did not override it.");
    }

    if (nodePathOverride) {
        console.warn(`⚠️ Injected NODE_PATH for isolated fallback: ${nodePathOverride}`);
    }
}

function runIsolatedFallbackJest(
    args,
    {
        getPinnedFallbackJestSpecFn = getPinnedFallbackJestSpec,
        prepareIsolatedFallbackJestFn = prepareIsolatedFallbackJest,
        runCommandFn = runCommand,
        printIsolatedFallbackSelectionFn = printIsolatedFallbackSelection,
        existsSyncFn = fs.existsSync,
        hasCliOptionFn = hasCliOption,
        warnFn = console.warn,
    } = {}
) {
    const jestSpec = getPinnedFallbackJestSpecFn();
    const prepared = prepareIsolatedFallbackJestFn(jestSpec);

    if (!prepared || !prepared.jestBinPath) {
        return null;
    }

    const invocationArgs = [prepared.jestBinPath];
    const callerProvidedTestRunner = hasCliOptionFn(args, "--testRunner");

    let injectedRunnerPath = null;
    if (!callerProvidedTestRunner) {
        if (!prepared.jestRunnerPath || !existsSyncFn(prepared.jestRunnerPath)) {
            warnFn(`⚠️ Isolated fallback Jest runner unavailable at expected path: ${prepared.jestRunnerPath}`);
            return null;
        }

        invocationArgs.push("--testRunner", prepared.jestRunnerPath);
        injectedRunnerPath = prepared.jestRunnerPath;
    }

    invocationArgs.push(...args);

    const isolatedNodeModulesPath = path.dirname(
        path.dirname(path.dirname(prepared.jestBinPath))
    );
    const isolatedNodePathEnv = buildNodePathEnv(isolatedNodeModulesPath);

    printIsolatedFallbackSelectionFn(prepared.jestBinPath, prepared.cacheHit, {
        testRunnerPath: injectedRunnerPath,
        testRunnerInjected: Boolean(injectedRunnerPath),
        callerProvidedTestRunner,
        nodePathOverride: isolatedNodePathEnv.NODE_PATH,
    });

    return runCommandFn(process.execPath, invocationArgs, {
        env: isolatedNodePathEnv,
    });
}

function isCommandUnavailable(result) {
    if (!result) {
        return true;
    }

    if (result.error && ["ENOENT", "EACCES"].includes(result.error.code)) {
        return true;
    }

    return result.status === 127;
}

function normalizeForPathComparison(targetPath) {
    let resolved = path.resolve(targetPath);
    try {
        resolved = fs.realpathSync.native ? fs.realpathSync.native(resolved) : fs.realpathSync(resolved);
    } catch {
        // Keep resolved path when target is unavailable; callers handle existence separately.
    }
    return process.platform === "win32" ? resolved.toLowerCase() : resolved;
}

function isPathInsideDirectory(filePath, directoryPath) {
    const normalizedFilePath = normalizeForPathComparison(filePath);
    const normalizedDirectoryPath = normalizeForPathComparison(directoryPath);
    const relativePath = path.relative(normalizedDirectoryPath, normalizedFilePath);
    return relativePath === "" || (!relativePath.startsWith("..") && !path.isAbsolute(relativePath));
}

function resolveLocalModule(moduleSpecifier) {
    try {
        return REPO_REQUIRE.resolve(moduleSpecifier);
    } catch {
        return null;
    }
}

function hasHealthyLocalJestInstall(
    moduleResolver = resolveLocalModule,
    existsSyncFn = fs.existsSync
) {
    if (!existsSyncFn(LOCAL_JEST_BIN)) {
        return false;
    }

    const circusRunnerPath = moduleResolver("jest-circus/runner");
    if (!circusRunnerPath) {
        return false;
    }

    if (!existsSyncFn(circusRunnerPath)) {
        return false;
    }

    return isPathInsideDirectory(circusRunnerPath, REPO_NODE_MODULES);
}

function printLocalJestFallbackWarning() {
    console.warn(
        "⚠️ Local Jest install appears incomplete; falling back to managed Jest."
    );
    if (process.platform === "win32") {
        console.warn("Windows tip: run npm install/npm ci in the same shell used by git hooks.");
    }
}

function runManagedJest(args, options = {}) {
    const {
        hasHealthyLocalJestInstallFn = hasHealthyLocalJestInstall,
        getNpmMajorVersionFn = getNpmMajorVersion,
        printLocalJestFallbackWarningFn = printLocalJestFallbackWarning,
        runIsolatedFallbackJestFn = runIsolatedFallbackJest,
        runNpmExecJestFn = runNpmExecJest,
        runNpxJestFn = runNpxJest,
    } = options;

    if (hasHealthyLocalJestInstallFn()) {
        return runLocalJest(args);
    }

    const hasLocalJestBinary = fs.existsSync(LOCAL_JEST_BIN);

    if (hasLocalJestBinary) {
        printLocalJestFallbackWarningFn();

        const isolatedFallbackResult = runIsolatedFallbackJestFn(args);
        if (isolatedFallbackResult) {
            return isolatedFallbackResult;
        }

        console.warn("⚠️ Isolated fallback Jest was unavailable; trying npm exec/npx fallback.");
    }

    const npmMajor = getNpmMajorVersionFn();

    if (npmMajor === null || npmMajor < 7) {
        return runNpxJestFn(args);
    }

    const npmExecResult = runNpmExecJestFn(args);
    if (isCommandUnavailable(npmExecResult)) {
        return runNpxJestFn(args);
    }

    return npmExecResult;
}

function printManagedJestLaunchError(error) {
    const detail = error && error.message ? ` (${error.message})` : "";
    console.error(`❌ Failed to launch managed Jest${detail}.`);
    console.error("Ensure Node.js/npm are available in this shell, or run npm install.");
    if (process.platform === "win32") {
        console.error("Windows tip: if you use nvm/fnm, open PowerShell or Git Bash with Node initialized and verify npm --version.");
    }
}

function main() {
    const result = runManagedJest(process.argv.slice(2));

    if (result.error) {
        printManagedJestLaunchError(result.error);
        process.exit(1);
    }

    const status = typeof result.status === "number" ? result.status : 1;
    process.exit(status);
}

module.exports = {
    REPO_ROOT,
    REPO_NODE_MODULES,
    PACKAGE_LOCK_PATH,
    LOCAL_JEST_BIN,
    FALLBACK_JEST_SPEC,
    ISOLATED_JEST_CACHE_ROOT,
    normalizeForPathComparison,
    isPathInsideDirectory,
    resolveLocalModule,
    hasHealthyLocalJestInstall,
    printLocalJestFallbackWarning,
    sanitizeCacheKey,
    getIsolatedJestPaths,
    hasCliOption,
    buildNodePathEnv,
    writeIsolatedJestCacheManifest,
    prepareIsolatedFallbackJest,
    printIsolatedFallbackSelection,
    runIsolatedFallbackJest,
    toShellCommand,
    parseNpmMajorVersion,
    getNpmMajorVersion,
    getPinnedFallbackJestSpec,
    runCommand,
    runLocalJest,
    runNpmExecJest,
    runNpxJest,
    isShellShimCommand,
    spawnPlatformCommandSync,
    isCommandUnavailable,
    runManagedJest,
    printManagedJestLaunchError,
};

if (require.main === module) {
    main();
}