/**
 * @fileoverview Version-parity tests for cspell and markdownlint-cli2.
 *
 * The hook entries in `.pre-commit-config.yaml` invoke pinned package
 * versions (e.g. `cspell@9.3.0`) for the npx fallback path. Those literal
 * versions MUST match the devDependencies versions in `package.json` so
 * cold-cache fallback installs the same binary that `npm install` brings
 * in. The previous round used a managed Node wrapper to derive the version
 * dynamically; the wrapper added wall-clock cost. These parity tests are
 * the cheaper guardrail against version drift.
 */

"use strict";

const fs = require("fs");
const path = require("path");

const REPO_ROOT = path.resolve(__dirname, "..", "..");
const PACKAGE_JSON_PATH = path.join(REPO_ROOT, "package.json");
const PRE_COMMIT_CONFIG_PATH = path.join(REPO_ROOT, ".pre-commit-config.yaml");
const CSPELL_CONFIG_PATH = path.join(REPO_ROOT, ".cspell.json");

function readPackageJson() {
  return JSON.parse(fs.readFileSync(PACKAGE_JSON_PATH, "utf8"));
}

function readPreCommitConfig() {
  return fs.readFileSync(PRE_COMMIT_CONFIG_PATH, "utf8");
}

function normalizeVersion(version) {
  if (typeof version !== "string") return null;
  const trimmed = version.trim().replace(/^[~^]/, "");
  if (!/^\d+\.\d+\.\d+(?:[-+].+)?$/.test(trimmed)) return null;
  return trimmed;
}

function findHookBlockText(content, hookId) {
  // Naive but sufficient: locate "- id: <hookId>" and capture lines until
  // the next "- id:" or the end of file. Whitespace-tolerant.
  const lines = content.split(/\r\n|\r|\n/);
  const startIndex = lines.findIndex((line) =>
    new RegExp(`^\\s*-\\s+id:\\s*${hookId}\\s*$`).test(line)
  );
  if (startIndex < 0) return null;
  const out = [lines[startIndex]];
  for (let i = startIndex + 1; i < lines.length; i++) {
    if (/^\s*-\s+id:\s*\S+\s*$/.test(lines[i])) break;
    out.push(lines[i]);
  }
  return out.join("\n");
}

function findPinnedVersionInEntry(blockText, packageName) {
  if (!blockText) return null;
  const re = new RegExp(
    `${packageName.replace(/[.+*?^${}()|[\]\\]/g, "\\$&")}@([0-9]+\\.[0-9]+\\.[0-9]+(?:[-+][\\w.]+)?)`
  );
  const match = blockText.match(re);
  return match ? match[1] : null;
}

describe("cspell version parity", () => {
  const pkg = readPackageJson();
  const config = readPreCommitConfig();
  const block = findHookBlockText(config, "cspell");
  const declaredVersion = normalizeVersion(pkg.devDependencies?.cspell);
  const entryVersion = findPinnedVersionInEntry(block, "cspell");

  test("package.json devDependencies.cspell is a concrete version", () => {
    expect(declaredVersion).not.toBeNull();
    expect(declaredVersion).toMatch(/^\d+\.\d+\.\d+/);
  });

  test("cspell hook entry pins the same version as package.json devDependencies", () => {
    // If the hook entry pattern changes (e.g. different fallback flow),
    // either preserve the @<version> token or update this test.
    expect(block).not.toBeNull();
    expect(entryVersion).not.toBeNull();
    expect(entryVersion).toBe(declaredVersion);
  });
});

describe("markdownlint-cli2 version parity", () => {
  const pkg = readPackageJson();
  const declaredVersion = normalizeVersion(pkg.devDependencies?.["markdownlint-cli2"]);

  test("package.json devDependencies.markdownlint-cli2 is a concrete version", () => {
    expect(declaredVersion).not.toBeNull();
    expect(declaredVersion).toMatch(/^\d+\.\d+\.\d+/);
  });

  test("the installed markdownlint-cli2 module matches the package.json devDependency version", () => {
    // Round-4: the standalone `markdownlint` hook was folded into
    // `run-staged-md-pipeline.js`, which dynamically imports
    // node_modules/markdownlint-cli2/markdownlint-cli2.mjs. There is no
    // longer a pinned `markdownlint-cli2@<v>` literal in the YAML to
    // diff against, so the parity check now reads the installed
    // module's package.json instead. If `npm install` lands a different
    // version than declared, this test fails the same way the YAML
    // literal check used to.
    const installedPkgPath = path.resolve(
      REPO_ROOT,
      "node_modules",
      "markdownlint-cli2",
      "package.json"
    );
    expect(fs.existsSync(installedPkgPath)).toBe(true);
    const installedPkg = JSON.parse(fs.readFileSync(installedPkgPath, "utf8"));
    const installedVersion = normalizeVersion(installedPkg.version);
    expect(installedVersion).toBe(declaredVersion);
  });
});

describe("cspell configuration exclusions", () => {
  test("ignores Vale reject vocabularies containing intentional misspellings", () => {
    const cspellConfig = JSON.parse(fs.readFileSync(CSPELL_CONFIG_PATH, "utf8"));

    expect(cspellConfig.ignorePaths).toContain(".vale/styles/Vocab/**/reject.txt");
  });

  test("ignores generated dependency lockfiles", () => {
    const cspellConfig = JSON.parse(fs.readFileSync(CSPELL_CONFIG_PATH, "utf8"));

    expect(cspellConfig.ignorePaths).toContain("package-lock.json");
    expect(cspellConfig.ignorePaths).toContain("**/packages-lock.json");
  });
});
