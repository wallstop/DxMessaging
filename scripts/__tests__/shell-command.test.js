/**
 * @fileoverview Tests for scripts/lib/shell-command.js.
 */

"use strict";

const {
    toShellCommand,
    isShellShimCommand,
    resolveSpawnCommand,
    resolveSpawnOptions,
    spawnPlatformCommandSync,
} = require("../lib/shell-command");

describe("shell-command", () => {
    test("toShellCommand returns .cmd wrappers on win32", () => {
        expect(toShellCommand("npm", "win32")).toBe("npm.cmd");
        expect(toShellCommand("npx", "win32")).toBe("npx.cmd");
        expect(toShellCommand("npm", "linux")).toBe("npm");
    });

    test("isShellShimCommand identifies npm/npx", () => {
        expect(isShellShimCommand("npm")).toBe(true);
        expect(isShellShimCommand("npx")).toBe(true);
        expect(isShellShimCommand("git")).toBe(false);
    });

    test("resolveSpawnCommand maps npm shim commands on win32", () => {
        expect(resolveSpawnCommand("npm", "win32")).toBe("npm.cmd");
        expect(resolveSpawnCommand("npx", "win32")).toBe("npx.cmd");
        expect(resolveSpawnCommand("git", "win32")).toBe("git");
        expect(resolveSpawnCommand("npm", "linux")).toBe("npm");
    });

    test("resolveSpawnOptions enforces shell mode for npm/npx on win32", () => {
        const options = resolveSpawnOptions("npm", { cwd: "C:/repo", shell: false }, "win32");

        expect(options).toEqual(
            expect.objectContaining({
                cwd: "C:/repo",
                shell: true,
                windowsHide: true,
            })
        );
    });

    test("resolveSpawnOptions leaves non-shim commands unchanged", () => {
        const options = resolveSpawnOptions("git", { cwd: "/repo", stdio: "pipe" }, "win32");

        expect(options).toEqual(
            expect.objectContaining({
                cwd: "/repo",
                stdio: "pipe",
            })
        );
        expect(options.shell).toBeUndefined();
    });

    test("spawnPlatformCommandSync delegates with resolved command/options", () => {
        const spawnSyncMock = jest.fn(() => ({ status: 0, stdout: "", stderr: "" }));

        spawnPlatformCommandSync("npm", ["--version"], { cwd: "C:/repo" }, spawnSyncMock, "win32");

        expect(spawnSyncMock).toHaveBeenCalledWith(
            "npm.cmd",
            ["--version"],
            expect.objectContaining({
                cwd: "C:/repo",
                shell: true,
                windowsHide: true,
            })
        );
    });
});
