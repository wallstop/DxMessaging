"use strict";

const { formatInstallGuidance, validateTooling } = require("../validate-node-tooling");

describe("validate-node-tooling", () => {
  test("passes when required files exist and load checks succeed", async () => {
    const violations = await validateTooling({
      existsSyncFn: () => true,
      requireFn: () => ({}),
      importFn: async () => ({}),
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

  test("install guidance keeps hooks out of dependency bootstrapping", () => {
    expect(formatInstallGuidance()).toContain("npm install");
    expect(formatInstallGuidance()).toContain("npm ci");
  });
});
