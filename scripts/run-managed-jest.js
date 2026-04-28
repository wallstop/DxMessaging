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

function runCommand(command, args) {
    const spawnSyncImpl = isShellShimCommand(command)
        ? spawnPlatformCommandSync
        : childProcess.spawnSync;

    const result = spawnSyncImpl(command, args, {
        cwd: REPO_ROOT,
        stdio: "inherit",
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
        "⚠️ Local Jest install appears incomplete; falling back to pinned npm exec Jest."
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
    } = options;

    if (hasHealthyLocalJestInstallFn()) {
        return runLocalJest(args);
    }

    if (fs.existsSync(LOCAL_JEST_BIN)) {
        printLocalJestFallbackWarningFn();
    }

    const npmMajor = getNpmMajorVersionFn();

    if (npmMajor === null || npmMajor < 7) {
        return runNpxJest(args);
    }

    const npmExecResult = runNpmExecJest(args);
    if (isCommandUnavailable(npmExecResult)) {
        return runNpxJest(args);
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
    normalizeForPathComparison,
    isPathInsideDirectory,
    resolveLocalModule,
    hasHealthyLocalJestInstall,
    printLocalJestFallbackWarning,
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