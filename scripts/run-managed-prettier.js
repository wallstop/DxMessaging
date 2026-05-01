#!/usr/bin/env node
/**
 * run-managed-prettier.js
 *
 * Runs Prettier in a robust, non-interactive way for hooks and local automation:
 * 1) Prefer local devDependency (node_modules/prettier/bin/prettier.cjs).
 * 2) If local Prettier is missing, provision a pinned fallback via npx.
 */

"use strict";

const fs = require("fs");
const path = require("path");
const childProcess = require("child_process");
const { isShellShimCommand, spawnPlatformCommandSync } = require("./lib/shell-command");
const { getPinnedPrettierSpec } = require("./lib/prettier-version");

const REPO_ROOT = path.join(__dirname, "..");
const LOCAL_PRETTIER_BIN = path.join(
    REPO_ROOT,
    "node_modules",
    "prettier",
    "bin",
    "prettier.cjs"
);

function runCommand(command, args, options = {}) {
    const spawnSyncImpl = isShellShimCommand(command)
        ? spawnPlatformCommandSync
        : childProcess.spawnSync;

    const result = spawnSyncImpl(command, args, {
        cwd: REPO_ROOT,
        stdio: "inherit",
        ...options,
    });

    return {
        status: result.status,
        error: result.error || null,
    };
}

function runLocalPrettier(args) {
    return runCommand(process.execPath, [LOCAL_PRETTIER_BIN, ...args]);
}

function runNpxPrettier(args, prettierSpec = getPinnedPrettierSpec()) {
    return runCommand("npx", [
        "--yes",
        `--package=${prettierSpec}`,
        "prettier",
        ...args,
    ]);
}

function runManagedPrettier(args, options = {}) {
    const {
        existsSyncFn = fs.existsSync,
        runLocalPrettierFn = runLocalPrettier,
        runNpxPrettierFn = runNpxPrettier,
    } = options;

    if (existsSyncFn(LOCAL_PRETTIER_BIN)) {
        return runLocalPrettierFn(args);
    }

    return runNpxPrettierFn(args);
}

function printManagedPrettierLaunchError(error) {
    const detail = error && error.message ? ` (${error.message})` : "";
    console.error(`Failed to launch managed Prettier${detail}.`);
    console.error("Ensure Node.js/npm are available in this shell, or run npm install.");
    if (process.platform === "win32") {
        console.error(
            "Windows tip: if you use nvm/fnm, open PowerShell or Git Bash with Node initialized and verify npm --version."
        );
    }
}

function main() {
    const result = runManagedPrettier(process.argv.slice(2));

    if (result.error) {
        printManagedPrettierLaunchError(result.error);
        process.exit(1);
    }

    process.exit(typeof result.status === "number" ? result.status : 1);
}

module.exports = {
    REPO_ROOT,
    LOCAL_PRETTIER_BIN,
    runCommand,
    runLocalPrettier,
    runNpxPrettier,
    runManagedPrettier,
    printManagedPrettierLaunchError,
};

if (require.main === module) {
    main();
}
