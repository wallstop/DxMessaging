#!/usr/bin/env node
"use strict";

const fs = require("fs");
const path = require("path");
const { pathToFileURL } = require("url");

const REPO_ROOT = path.resolve(__dirname, "..");

const TOOL_SPECS = [
  {
    name: "prettier",
    requiredFiles: ["node_modules/prettier/index.cjs", "node_modules/prettier/bin/prettier.cjs"],
    load: "require",
    entry: "node_modules/prettier/index.cjs"
  },
  {
    name: "markdownlint-cli2",
    requiredFiles: ["node_modules/markdownlint-cli2/markdownlint-cli2.mjs"],
    load: "import",
    entry: "node_modules/markdownlint-cli2/markdownlint-cli2.mjs"
  },
  {
    name: "cspell",
    requiredFiles: ["node_modules/cspell/bin.mjs"]
  }
];

function toAbs(repoRelativePath) {
  return path.join(REPO_ROOT, ...repoRelativePath.split("/"));
}

function formatInstallGuidance() {
  return [
    "Repair local Node tooling before running hooks:",
    "  npm install",
    "If package-lock.json is present and should be authoritative in this workspace, use:",
    "  npm ci"
  ].join("\n");
}

async function importToolEntry(absPath) {
  return import(pathToFileURL(absPath).href);
}

async function validateTooling(options = {}) {
  const {
    existsSyncFn = fs.existsSync,
    requireFn = require,
    importFn = importToolEntry,
    toolSpecs = TOOL_SPECS
  } = options;
  const violations = [];

  for (const tool of toolSpecs) {
    for (const requiredFile of tool.requiredFiles) {
      const absPath = toAbs(requiredFile);
      if (!existsSyncFn(absPath)) {
        violations.push(`${tool.name}: missing ${requiredFile}`);
      }
    }

    if (tool.entry && tool.load === "require") {
      try {
        requireFn(toAbs(tool.entry));
      } catch (error) {
        violations.push(`${tool.name}: failed to load ${tool.entry}: ${error.message}`);
      }
    }

    if (tool.entry && tool.load === "import") {
      try {
        await importFn(toAbs(tool.entry));
      } catch (error) {
        violations.push(`${tool.name}: failed to import ${tool.entry}: ${error.message}`);
      }
    }
  }

  return violations;
}

async function main() {
  const violations = await validateTooling();
  if (violations.length === 0) {
    console.log("Node tooling dependency health validation passed.");
    return 0;
  }

  console.error(`Found ${violations.length} Node tooling health violation(s):`);
  for (const violation of violations) {
    console.error(`- ${violation}`);
  }
  console.error(formatInstallGuidance());
  return 1;
}

module.exports = {
  REPO_ROOT,
  TOOL_SPECS,
  toAbs,
  formatInstallGuidance,
  importToolEntry,
  validateTooling,
  main
};

if (require.main === module) {
  main().then(
    (code) => process.exit(code),
    (error) => {
      console.error(`validate-node-tooling: fatal error: ${error.stack || error.message}`);
      process.exit(1);
    }
  );
}
