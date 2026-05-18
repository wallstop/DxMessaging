/**
 * @fileoverview Tests for run-managed-prettier.js.
 */

"use strict";

const childProcess = require("child_process");
const path = require("path");
const {
    MISSING_BUNDLED_NPX_CLI_MESSAGE,
    resolveBundledNpxCliPath,
    runBundledNpxCommand,
} = require("../lib/managed-prettier");
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

    test("runNpxPrettier invokes bundled npx with pinned package spec", () => {
        const runBundledNpxCommandFn = jest.fn(() => ({ status: 0, error: null }));

        const result = runNpxPrettier(["--check", "README.md"], "prettier@3.8.3", {
            runBundledNpxCommandFn,
        });

        expect(result).toEqual({ status: 0, error: null });
        expect(runBundledNpxCommandFn).toHaveBeenCalledWith(
            [
                "--yes",
                "--package=prettier@3.8.3",
                "prettier",
                "--check",
                "README.md",
            ],
            expect.objectContaining({
                cwd: REPO_ROOT,
                stdio: "inherit",
            })
        );
    });

    test("runNpxPrettier returns launch error object when bundled npx resolver throws", () => {
        const missingCliError = new Error("missing npx-cli.js");

        const result = runNpxPrettier(["--check", "README.md"], "prettier@3.8.3", {
            runBundledNpxCommandFn: () => {
                throw missingCliError;
            },
        });

        expect(result).toEqual({
            status: null,
            error: missingCliError,
        });
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

    test("resolveBundledNpxCliPath returns bundled npx-cli.js when present", () => {
        const execPath = path.join(path.sep, "opt", "node", "bin", "node");
        const expected = path.join(path.dirname(execPath), "node_modules", "npm", "bin", "npx-cli.js");

        const resolved = resolveBundledNpxCliPath({
            execPath,
            existsSyncFn: (candidatePath) => candidatePath === expected,
        });

        expect(resolved).toBe(expected);
    });

    test("resolveBundledNpxCliPath returns null when bundled npx-cli.js is missing", () => {
        const resolved = resolveBundledNpxCliPath({
            execPath: path.join(path.sep, "opt", "node", "bin", "node"),
            existsSyncFn: () => false,
        });

        expect(resolved).toBeNull();
    });

    test("resolveBundledNpxCliPath supports Linux distro npm layout", () => {
        const execPath = path.join(path.sep, "usr", "bin", "node");
        const expected = path.join(path.sep, "usr", "lib", "node_modules", "npm", "bin", "npx-cli.js");

        const resolved = resolveBundledNpxCliPath({
            execPath,
            existsSyncFn: (candidatePath) => candidatePath === expected,
        });

        expect(resolved).toBe(expected);
    });

    test("runBundledNpxCommand invokes Node with the bundled npx CLI", () => {
        const execPath = String.raw`C:\node\node.exe`;
        const npxCliPath = String.raw`C:\node\node_modules\npm\bin\npx-cli.js`;
        const runCommandFn = jest.fn(() => ({ status: 0 }));

        const result = runBundledNpxCommand(["--yes", "prettier", "--check", "README.md"], {
            execPath,
            resolveBundledNpxCliPathFn: () => npxCliPath,
            runCommandFn,
            cwd: path.join(path.sep, "repo"),
        });

        expect(result).toEqual({ status: 0 });
        expect(runCommandFn).toHaveBeenCalledWith(
            execPath,
            [npxCliPath, "--yes", "prettier", "--check", "README.md"],
            expect.objectContaining({
                cwd: path.join(path.sep, "repo"),
                encoding: "utf8",
            })
        );
    });

    test("runBundledNpxCommand fails closed when bundled npx CLI cannot be resolved", () => {
        expect(() =>
            runBundledNpxCommand(["--yes", "prettier", "--check", "README.md"], {
                resolveBundledNpxCliPathFn: () => null,
                runCommandFn: jest.fn(),
            })
        ).toThrow(MISSING_BUNDLED_NPX_CLI_MESSAGE);
    });
});
