"use strict";

const path = require("path");

const {
  formatInstallGuidance,
  validateManagedNpxCliAvailability,
  validateManagedNpxPolicy,
  validateTooling
} = require("../validate-node-tooling");

const REPO_ROOT = path.resolve(__dirname, "../..");

describe("validate-node-tooling", () => {
  test("passes when required files exist and load checks succeed", async () => {
    const violations = await validateTooling({
      existsSyncFn: () => true,
      requireFn: () => ({}),
      importFn: async () => ({}),
      enforceManagedNpxCliAvailability: false,
      scriptSources: [],
      toolSpecs: [
        {
          name: "prettier",
          requiredFiles: ["node_modules/prettier/index.cjs"],
          load: "require",
          entry: "node_modules/prettier/index.cjs"
        },
        {
          name: "markdownlint-cli2",
          requiredFiles: ["node_modules/markdownlint-cli2/markdownlint-cli2.mjs"],
          load: "import",
          entry: "node_modules/markdownlint-cli2/markdownlint-cli2.mjs"
        }
      ]
    });

    expect(violations).toEqual([]);
  });

  test("reports missing files and failed transitive imports", async () => {
    const violations = await validateTooling({
      existsSyncFn: () => false,
      requireFn: () => ({}),
      importFn: async () => {
        throw new Error("Cannot find package 'fast-glob' imported from globby/index.js");
      },
      enforceManagedNpxCliAvailability: false,
      scriptSources: [],
      toolSpecs: [
        {
          name: "markdownlint-cli2",
          requiredFiles: ["node_modules/markdownlint-cli2/markdownlint-cli2.mjs"],
          load: "import",
          entry: "node_modules/markdownlint-cli2/markdownlint-cli2.mjs"
        }
      ]
    });

    expect(violations).toEqual([
      "markdownlint-cli2: missing node_modules/markdownlint-cli2/markdownlint-cli2.mjs",
      expect.stringContaining("Cannot find package 'fast-glob'")
    ]);
  });

  test("reports unresolved modules for resolve-based tooling checks", async () => {
    const violations = await validateTooling({
      existsSyncFn: () => true,
      resolveModuleFn: () => null,
      enforceManagedNpxCliAvailability: false,
      scriptSources: [],
      toolSpecs: [
        {
          name: "jest-circus",
          requiredFiles: [],
          load: "resolve",
          entry: "jest-circus/runner"
        }
      ]
    });

    expect(violations).toEqual(["jest-circus: failed to resolve jest-circus/runner"]);
  });

  test("reports missing files when resolve-based entries point to stale paths", async () => {
    const resolvedRunnerPath = "/repo/node_modules/jest-circus/build/runner.js";
    const violations = await validateTooling({
      existsSyncFn: () => false,
      resolveModuleFn: () => resolvedRunnerPath,
      enforceManagedNpxCliAvailability: false,
      scriptSources: [],
      toolSpecs: [
        {
          name: "jest-circus",
          requiredFiles: [],
          load: "resolve",
          entry: "jest-circus/runner"
        }
      ]
    });

    expect(violations).toEqual([
      `jest-circus: resolved jest-circus/runner to missing file: ${resolvedRunnerPath}`
    ]);
  });

  test("reports load failure when resolve-based entry exists but cannot be required", async () => {
    const resolvedRunnerPath = "/repo/node_modules/jest-circus/build/runner.js";
    const violations = await validateTooling({
      existsSyncFn: () => true,
      resolveModuleFn: () => resolvedRunnerPath,
      requireFn: (modulePath) => {
        if (modulePath === resolvedRunnerPath) {
          throw new Error("Unexpected end of file");
        }
        return {};
      },
      enforceManagedNpxCliAvailability: false,
      scriptSources: [],
      toolSpecs: [
        {
          name: "jest-circus",
          requiredFiles: [],
          load: "resolve",
          entry: "jest-circus/runner"
        }
      ]
    });

    expect(violations).toEqual([
      "jest-circus: resolved jest-circus/runner could not be loaded: Unexpected end of file"
    ]);
  });

  test("validateManagedNpxPolicy reports direct npx process spawns", () => {
    const violations = validateManagedNpxPolicy({
      scriptSources: [
        {
          filePath: path.join(REPO_ROOT, "scripts", "bad-npx.js"),
          content: 'childProcess.spawnSync("npx", ["--yes", "prettier", "--check", "README.md"]);'
        }
      ]
    });

    expect(violations).toEqual([
      expect.stringContaining("managed-npx-policy: scripts/bad-npx.js uses direct npx process spawning")
    ]);
  });

  test("validateManagedNpxPolicy reports npx command variables passed to process invokers", () => {
    const violations = validateManagedNpxPolicy({
      scriptSources: [
        {
          filePath: path.join(REPO_ROOT, "scripts", "bad-npx-command-var.js"),
          content: [
            'const childProcess = require("child_process");',
            'const npxCommand = "npx";',
            "childProcess.spawnSync(npxCommand, [\"--yes\", \"prettier\", \"--check\", \"README.md\"]);"
          ].join("\n")
        }
      ]
    });

    expect(violations).toEqual([
      expect.stringContaining(
        "managed-npx-policy: scripts/bad-npx-command-var.js uses direct npx process spawning"
      )
    ]);
  });

  test("validateManagedNpxPolicy reports child_process alias invokers with npx command variables", () => {
    const violations = validateManagedNpxPolicy({
      scriptSources: [
        {
          filePath: path.join(REPO_ROOT, "scripts", "bad-npx-invoker-alias.js"),
          content: [
            'const childProcess = require("child_process");',
            "const { spawnSync: invoke } = childProcess;",
            'const cmd = "npx.cmd";',
            "invoke(cmd, [\"--yes\", \"prettier\", \"--check\", \"README.md\"]);"
          ].join("\n")
        }
      ]
    });

    expect(violations).toEqual([
      expect.stringContaining(
        "managed-npx-policy: scripts/bad-npx-invoker-alias.js uses direct npx process spawning"
      )
    ]);
  });

  test("validateManagedNpxPolicy allows managed bundled npx helper usage", () => {
    const violations = validateManagedNpxPolicy({
      scriptSources: [
        {
          filePath: path.join(REPO_ROOT, "scripts", "good-npx.js"),
          content:
            'runBundledNpxCommand(["--yes", "--package=prettier@3.8.3", "prettier", "--check", "README.md"]);'
        }
      ]
    });

    expect(violations).toEqual([]);
  });

  test("validateManagedNpxCliAvailability reports missing bundled npx-cli", () => {
    const violations = validateManagedNpxCliAvailability({
      execPath: "/opt/node/bin/node",
      resolveBundledNpxCliPathFn: () => null,
      existsSyncFn: () => false
    });

    expect(violations).toEqual([
      expect.stringContaining("unable to resolve npm bundled npx-cli.js")
    ]);
  });

  test("validateManagedNpxCliAvailability passes when bundled npx-cli resolves", () => {
    const violations = validateManagedNpxCliAvailability({
      execPath: "/opt/node/bin/node",
      resolveBundledNpxCliPathFn: () => "/opt/node/bin/node_modules/npm/bin/npx-cli.js",
      existsSyncFn: () => true
    });

    expect(violations).toEqual([]);
  });

  test("validateTooling includes managed npx-cli availability violations by default", async () => {
    const violations = await validateTooling({
      existsSyncFn: () => true,
      requireFn: () => ({}),
      importFn: async () => ({}),
      resolveBundledNpxCliPathFn: () => null,
      scriptSources: [],
      toolSpecs: []
    });

    expect(violations).toEqual([
      expect.stringContaining("unable to resolve npm bundled npx-cli.js")
    ]);
  });

  test("install guidance keeps hooks out of dependency bootstrapping", () => {
    expect(formatInstallGuidance()).toContain("npm install");
    expect(formatInstallGuidance()).toContain("npm ci");
  });
});
