/**
 * @fileoverview Tests for verify-managed-jest-fallback.js.
 */

"use strict";

const {
    resolveManagedRunnerPath,
    removeManagedRunner,
    verifyManagedJestFallback,
} = require("../verify-managed-jest-fallback.js");

describe("verify-managed-jest-fallback", () => {
    test("resolveManagedRunnerPath uses provided resolver", () => {
        const moduleResolver = jest.fn(() => "/tmp/jest-circus/runner.js");

        const runnerPath = resolveManagedRunnerPath(moduleResolver);

        expect(runnerPath).toBe("/tmp/jest-circus/runner.js");
        expect(moduleResolver).toHaveBeenCalledWith("jest-circus/runner");
    });

    test("removeManagedRunner throws when runner does not exist before deletion", () => {
        const existsSyncFn = jest.fn(() => false);
        const logFn = jest.fn();

        expect(() =>
            removeManagedRunner("/tmp/missing-runner.js", { existsSyncFn, logFn })
        ).toThrow("Runner path does not exist before deletion.");
    });

    test("removeManagedRunner removes existing runner", () => {
        const existsSyncFn = jest
            .fn()
            .mockReturnValueOnce(true)
            .mockReturnValueOnce(false);
        const rmSyncFn = jest.fn();
        const logFn = jest.fn();

        removeManagedRunner("/tmp/runner.js", { existsSyncFn, rmSyncFn, logFn });

        expect(logFn).toHaveBeenCalledWith("Deleting managed Jest runner: /tmp/runner.js");
        expect(rmSyncFn).toHaveBeenCalledWith("/tmp/runner.js");
        expect(existsSyncFn).toHaveBeenCalledTimes(2);
    });

    test("verifyManagedJestFallback resolves and removes runner", () => {
        const moduleResolver = jest.fn(() => "/tmp/runner.js");
        const existsSyncFn = jest
            .fn()
            .mockReturnValueOnce(true)
            .mockReturnValueOnce(false);
        const rmSyncFn = jest.fn();
        const logFn = jest.fn();

        verifyManagedJestFallback({
            moduleResolver,
            existsSyncFn,
            rmSyncFn,
            logFn,
        });

        expect(moduleResolver).toHaveBeenCalledWith("jest-circus/runner");
        expect(rmSyncFn).toHaveBeenCalledWith("/tmp/runner.js");
    });
});
