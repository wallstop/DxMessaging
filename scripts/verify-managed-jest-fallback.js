#!/usr/bin/env node
"use strict";

const fs = require("fs");

function resolveManagedRunnerPath(moduleResolver = require.resolve) {
    return moduleResolver("jest-circus/runner");
}

function removeManagedRunner(
    runnerPath,
    {
        existsSyncFn = fs.existsSync,
        rmSyncFn = fs.rmSync,
        logFn = console.log,
    } = {}
) {
    logFn(`Deleting managed Jest runner: ${runnerPath}`);

    if (!existsSyncFn(runnerPath)) {
        throw new Error("Runner path does not exist before deletion.");
    }

    rmSyncFn(runnerPath);

    if (existsSyncFn(runnerPath)) {
        throw new Error("Runner path still exists after deletion.");
    }
}

function verifyManagedJestFallback(options = {}) {
    const {
        moduleResolver = require.resolve,
        existsSyncFn,
        rmSyncFn,
        logFn,
    } = options;

    const runnerPath = resolveManagedRunnerPath(moduleResolver);
    removeManagedRunner(runnerPath, {
        existsSyncFn,
        rmSyncFn,
        logFn,
    });
}

function main() {
    verifyManagedJestFallback();
}

module.exports = {
    resolveManagedRunnerPath,
    removeManagedRunner,
    verifyManagedJestFallback,
};

if (require.main === module) {
    main();
}
