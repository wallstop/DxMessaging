"use strict";

const path = require("path");

const {
  MISSING_BUNDLED_NPX_CLI_MESSAGE,
  resolveBundledNpxCliPath,
  runBundledNpxCommand
} = require("../lib/managed-prettier");

describe("managed-prettier", () => {
  test("resolveBundledNpxCliPath returns bundled npx-cli.js when present", () => {
    const execPath = path.join(path.sep, "opt", "node", "bin", "node");
    const expected = path.join(path.dirname(execPath), "node_modules", "npm", "bin", "npx-cli.js");

    const resolved = resolveBundledNpxCliPath({
      execPath,
      existsSyncFn: (candidatePath) => candidatePath === expected
    });

    expect(resolved).toBe(expected);
  });

  test("resolveBundledNpxCliPath returns null when bundled npx-cli.js is missing", () => {
    const resolved = resolveBundledNpxCliPath({
      execPath: path.join(path.sep, "opt", "node", "bin", "node"),
      existsSyncFn: () => false
    });

    expect(resolved).toBeNull();
  });

  test("runBundledNpxCommand invokes Node with the bundled npx CLI", () => {
    const execPath = String.raw`C:\node\node.exe`;
    const npxCliPath = String.raw`C:\node\node_modules\npm\bin\npx-cli.js`;
    const runCommandFn = jest.fn(() => ({ status: 0 }));

    const result = runBundledNpxCommand(["--yes", "prettier", "--check", "README.md"], {
      execPath,
      resolveBundledNpxCliPathFn: () => npxCliPath,
      runCommandFn,
      cwd: path.join(path.sep, "repo")
    });

    expect(result).toEqual({ status: 0 });
    expect(runCommandFn).toHaveBeenCalledWith(
      execPath,
      [npxCliPath, "--yes", "prettier", "--check", "README.md"],
      expect.objectContaining({
        cwd: path.join(path.sep, "repo"),
        encoding: "utf8"
      })
    );
  });

  test("runBundledNpxCommand fails closed when bundled npx CLI cannot be resolved", () => {
    expect(() =>
      runBundledNpxCommand(["--yes", "prettier", "--check", "README.md"], {
        resolveBundledNpxCliPathFn: () => null,
        runCommandFn: jest.fn()
      })
    ).toThrow(MISSING_BUNDLED_NPX_CLI_MESSAGE);
  });
});