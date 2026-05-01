/**
 * @fileoverview Tests for run-managed-prettier.js.
 */

"use strict";

const childProcess = require("child_process");
const { toShellCommand } = require("../lib/shell-command");
const {
    REPO_ROOT,
    runCommand,
    runNpxPrettier,
    runManagedPrettier,
} = require("../run-managed-prettier");

describe("run-managed-prettier", () => {
    afterEach(() => {
        jest.restoreAllMocks();
    });

    test("runManagedPrettier prefers local prettier when available", () => {
        const runLocalPrettierFn = jest.fn(() => ({ status: 0, error: null }));
        const runNpxPrettierFn = jest.fn(() => ({ status: 1, error: null }));

        const result = runManagedPrettier(["--check", "README.md"], {
            existsSyncFn: () => true,
            runLocalPrettierFn,
            runNpxPrettierFn,
        });

        expect(result).toEqual({ status: 0, error: null });
        expect(runLocalPrettierFn).toHaveBeenCalledWith(["--check", "README.md"]);
        expect(runNpxPrettierFn).not.toHaveBeenCalled();
    });

    test("runManagedPrettier falls back to npx when local prettier is missing", () => {
        const runLocalPrettierFn = jest.fn(() => ({ status: 0, error: null }));
        const runNpxPrettierFn = jest.fn(() => ({ status: 0, error: null }));

        const result = runManagedPrettier(["--write", "README.md"], {
            existsSyncFn: () => false,
            runLocalPrettierFn,
            runNpxPrettierFn,
        });

        expect(result).toEqual({ status: 0, error: null });
        expect(runLocalPrettierFn).not.toHaveBeenCalled();
        expect(runNpxPrettierFn).toHaveBeenCalledWith(["--write", "README.md"]);
    });

    test("runNpxPrettier invokes npx with pinned package spec", () => {
        const spawnSyncSpy = jest
            .spyOn(childProcess, "spawnSync")
            .mockReturnValue({ status: 0, error: null });

        const result = runNpxPrettier(["--check", "README.md"], "prettier@3.8.3");

        const expectedOptions = { cwd: REPO_ROOT, stdio: "inherit" };
        if (process.platform === "win32") {
            expectedOptions.shell = true;
            expectedOptions.windowsHide = true;
        }

        expect(result).toEqual({ status: 0, error: null });
        expect(spawnSyncSpy).toHaveBeenCalledWith(
            toShellCommand("npx"),
            [
                "--yes",
                "--package=prettier@3.8.3",
                "prettier",
                "--check",
                "README.md",
            ],
            expect.objectContaining(expectedOptions)
        );
    });

    test("runCommand delegates non-shell-shim commands to child_process.spawnSync", () => {
        const spawnSyncSpy = jest
            .spyOn(childProcess, "spawnSync")
            .mockReturnValue({ status: 0, error: null });

        const result = runCommand(process.execPath, ["tool.js"]);

        expect(result).toEqual({ status: 0, error: null });
        expect(spawnSyncSpy).toHaveBeenCalledWith(
            process.execPath,
            ["tool.js"],
            expect.objectContaining({ cwd: REPO_ROOT, stdio: "inherit" })
        );
        spawnSyncSpy.mockRestore();
    });
});
