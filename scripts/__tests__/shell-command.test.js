/**
 * @fileoverview Tests for scripts/lib/shell-command.js.
 *
 * @cross-platform-regression -- this marker requires the file to be gated on
 * ubuntu/windows/macos via the targeted step in
 * .github/workflows/cross-platform-preflight.yml; enforced by
 * scripts/__tests__/cross-platform-preflight-coverage.test.js.
 */

"use strict";

const {
  toShellCommand,
  isShellShimCommand,
  resolveSpawnCommand,
  resolveSpawnOptions,
  buildSpawnInvocation,
  spawnPlatformCommandSync
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

  test("resolveSpawnOptions avoids shell mode for npm/npx on win32", () => {
    const options = resolveSpawnOptions("npm", { cwd: "C:/repo", shell: false }, "win32");

    expect(options).toEqual(
      expect.objectContaining({
        cwd: "C:/repo",
        shell: false,
        windowsHide: true
      })
    );
  });

  test("resolveSpawnOptions leaves non-shim commands unchanged", () => {
    const options = resolveSpawnOptions("git", { cwd: "/repo", stdio: "pipe" }, "win32");

    expect(options).toEqual(
      expect.objectContaining({
        cwd: "/repo",
        stdio: "pipe"
      })
    );
    expect(options.shell).toBeUndefined();
  });

  test("spawnPlatformCommandSync delegates with resolved command/options", () => {
    const spawnSyncMock = jest.fn(() => ({ status: 0, stdout: "", stderr: "" }));

    spawnPlatformCommandSync("npm", ["--version"], { cwd: "C:/repo" }, spawnSyncMock, "win32");

    expect(spawnSyncMock).toHaveBeenCalledWith(
      expect.stringMatching(/(?:cmd\.exe|cmd)$/i),
      ["/d", "/s", "/c", "npm.cmd", "--version"],
      expect.objectContaining({
        cwd: "C:/repo",
        windowsHide: true
      })
    );
  });

  // ---------------------------------------------------------------------------
  // Golden cross-platform contract for buildSpawnInvocation / spawnPlatformCommandSync.
  //
  // These assertions are deliberately LITERAL (not derived from the function
  // under test) so they pin down the exact spawn shape on every platform. This
  // file is the canonical contract: every consumer test derives its expectation
  // from buildSpawnInvocation, and buildSpawnInvocation is locked down here.
  // ---------------------------------------------------------------------------
  describe("buildSpawnInvocation golden contract", () => {
    const COMSPEC_SENTINEL = "X:\\sentinel\\cmd.exe";
    let originalComSpec;

    beforeEach(() => {
      originalComSpec = process.env.ComSpec;
    });

    afterEach(() => {
      if (originalComSpec === undefined) {
        delete process.env.ComSpec;
      } else {
        process.env.ComSpec = originalComSpec;
      }
    });

    test("linux: npm passthrough preserves command, args, and options verbatim", () => {
      const invocation = buildSpawnInvocation(
        "npm",
        ["pack", "--json", "--dry-run", "--ignore-scripts"],
        { cwd: "/repo", encoding: "utf8" },
        "linux"
      );

      expect(invocation).toEqual({
        command: "npm",
        args: ["pack", "--json", "--dry-run", "--ignore-scripts"],
        options: { cwd: "/repo", encoding: "utf8" }
      });
      // Passthrough must not inject shell/windowsHide on non-win32.
      expect(invocation.options.shell).toBeUndefined();
      expect(invocation.options.windowsHide).toBeUndefined();
    });

    test("darwin: npx passthrough preserves command, args, and options verbatim", () => {
      const invocation = buildSpawnInvocation(
        "npx",
        ["--yes", "--package=jest@30.3.0", "jest", "--version"],
        { cwd: "/repo" },
        "darwin"
      );

      expect(invocation).toEqual({
        command: "npx",
        args: ["--yes", "--package=jest@30.3.0", "jest", "--version"],
        options: { cwd: "/repo" }
      });
    });

    test("win32: npm wraps through ComSpec with /d /s /c npm.cmd and exact arg order", () => {
      process.env.ComSpec = COMSPEC_SENTINEL;

      const invocation = buildSpawnInvocation(
        "npm",
        ["pack", "--json", "--dry-run", "--ignore-scripts"],
        { cwd: "C:/repo", encoding: "utf8" },
        "win32"
      );

      expect(invocation.command).toBe(COMSPEC_SENTINEL);
      expect(invocation.args).toEqual([
        "/d",
        "/s",
        "/c",
        "npm.cmd",
        "pack",
        "--json",
        "--dry-run",
        "--ignore-scripts"
      ]);
      expect(invocation.options).toEqual({
        cwd: "C:/repo",
        encoding: "utf8",
        shell: false,
        windowsHide: true
      });
    });

    test("win32: npx wraps through ComSpec with /d /s /c npx.cmd and exact arg order", () => {
      process.env.ComSpec = COMSPEC_SENTINEL;

      const invocation = buildSpawnInvocation(
        "npx",
        ["--yes", "--package=jest@30.3.0", "jest", "--version"],
        { cwd: "C:/repo" },
        "win32"
      );

      expect(invocation.command).toBe(COMSPEC_SENTINEL);
      expect(invocation.args).toEqual([
        "/d",
        "/s",
        "/c",
        "npx.cmd",
        "--yes",
        "--package=jest@30.3.0",
        "jest",
        "--version"
      ]);
    });

    test("win32: ComSpec unset falls back to cmd.exe", () => {
      delete process.env.ComSpec;

      const invocation = buildSpawnInvocation("npm", ["--version"], {}, "win32");

      expect(invocation.command).toBe("cmd.exe");
      expect(invocation.args).toEqual(["/d", "/s", "/c", "npm.cmd", "--version"]);
    });

    test("win32: caller-supplied windowsHide is preserved (not overwritten)", () => {
      const invocation = buildSpawnInvocation(
        "npm",
        ["--version"],
        { windowsHide: false },
        "win32"
      );

      expect(invocation.options.windowsHide).toBe(false);
      expect(invocation.options.shell).toBe(false);
    });

    test("git (non-shim) is a passthrough on every platform", () => {
      for (const platform of ["linux", "darwin", "win32"]) {
        const invocation = buildSpawnInvocation(
          "git",
          ["rev-parse", "HEAD"],
          { cwd: "/repo" },
          platform
        );

        expect(invocation).toEqual({
          command: "git",
          args: ["rev-parse", "HEAD"],
          options: { cwd: "/repo" }
        });
      }
    });

    test("buildSpawnInvocation does not mutate the caller's args or options", () => {
      const args = ["--version"];
      const options = { cwd: "C:/repo" };

      buildSpawnInvocation("npm", args, options, "win32");

      expect(args).toEqual(["--version"]);
      expect(options).toEqual({ cwd: "C:/repo" });
    });
  });

  describe("spawnPlatformCommandSync golden contract", () => {
    const COMSPEC_SENTINEL = "X:\\sentinel\\cmd.exe";
    let originalComSpec;

    beforeEach(() => {
      originalComSpec = process.env.ComSpec;
    });

    afterEach(() => {
      if (originalComSpec === undefined) {
        delete process.env.ComSpec;
      } else {
        process.env.ComSpec = originalComSpec;
      }
    });

    test.each(["linux", "darwin"])(
      "%s: spawnPlatformCommandSync passes npm through unchanged",
      (platform) => {
        const spawnSyncMock = jest.fn(() => ({ status: 0 }));

        spawnPlatformCommandSync(
          "npm",
          ["pack", "--json"],
          { cwd: "/repo", encoding: "utf8" },
          spawnSyncMock,
          platform
        );

        expect(spawnSyncMock).toHaveBeenCalledWith("npm", ["pack", "--json"], {
          cwd: "/repo",
          encoding: "utf8"
        });
      }
    );

    test("win32: spawnPlatformCommandSync calls spawnSync with the cmd.exe wrapper", () => {
      process.env.ComSpec = COMSPEC_SENTINEL;
      const spawnSyncMock = jest.fn(() => ({ status: 0 }));

      spawnPlatformCommandSync(
        "npm",
        ["pack", "--json"],
        { cwd: "C:/repo" },
        spawnSyncMock,
        "win32"
      );

      expect(spawnSyncMock).toHaveBeenCalledWith(
        COMSPEC_SENTINEL,
        ["/d", "/s", "/c", "npm.cmd", "pack", "--json"],
        { cwd: "C:/repo", shell: false, windowsHide: true }
      );
    });

    test("win32: spawnPlatformCommandSync result matches buildSpawnInvocation triple exactly", () => {
      // Proves the two share one code path: production must call spawnSync with
      // precisely the triple buildSpawnInvocation computes.
      const spawnSyncMock = jest.fn(() => ({ status: 0 }));
      const inv = buildSpawnInvocation("npm", ["exec", "--yes"], { cwd: "C:/repo" }, "win32");

      spawnPlatformCommandSync(
        "npm",
        ["exec", "--yes"],
        { cwd: "C:/repo" },
        spawnSyncMock,
        "win32"
      );

      expect(spawnSyncMock).toHaveBeenCalledWith(inv.command, inv.args, inv.options);
    });
  });
});
