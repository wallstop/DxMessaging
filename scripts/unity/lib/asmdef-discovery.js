"use strict";

/**
 * @file asmdef-discovery.js
 *
 * Shared, deterministic discovery + classification of Unity test asmdef files.
 *
 * Used by:
 *   - .github/actions/compute-unity-assemblies (primary CI consumer)
 *   - scripts/unity/run-tests.sh (default include / exclude assembly list)
 *   - scripts/unity/run-tests.ps1 (PowerShell parity)
 *   - scripts/__tests__/unity-perf-isolation.test.js (Phase 4 contract)
 *   - .github/workflows-disabled/unity-tests.yml (customParameters template)
 *
 * No filesystem mutation. Pure functions only.
 *
 * Exports:
 *   - enumerateTestAsmdefs(repoRoot)
 *   - classifyAsmdef(name)
 *   - defaultIncludeAssemblies(repoRoot, options?)
 *   - defaultExcludeAssemblies(repoRoot, options?)
 *
 * Default include/exclude rules:
 *   - "core"        => INCLUDED by default.
 *   - "perf"        => EXCLUDED by default. Opt in with { includePerf: true }.
 *   - "comparison"  => EXCLUDED by default. Opt in with
 *                      { includeComparisons: true } after installing external
 *                      comparison packages.
 *   - "integration" => EXCLUDED by default (their packages are not in the test
 *                      project's manifest.json and would fail to compile).
 *                      Opt in with { includeIntegrations: true }.
 */

const fs = require("fs");
const path = require("path");

/**
 * Names matching this pattern are perf/benchmark/allocation assemblies and must
 * be excluded from default Unity Test Runner runs.
 *
 * Source of truth lives in .llm/context.md line 114 (perf isolation rule).
 *
 * @type {RegExp}
 */
const PERF_NAME_REGEX = /(?:Benchmarks|Allocations)/;
const COMPARISON_NAME_REGEX = /(?:Comparisons)/;

/**
 * Names matching this pattern are DI-container integration suites
 * (VContainer / Zenject / Reflex). EXCLUDED from the default suite because
 * their backing packages (com.gustavopsantos.reflex, com.svermeulen.extenject,
 * jp.hadashikick.vcontainer) are not declared in the test project's
 * manifest.json — including them would cause compile errors. Opt in via the
 * `includeIntegrations` option on `defaultIncludeAssemblies`.
 *
 * @type {RegExp}
 */
const INTEGRATION_NAME_REGEX = /(?:VContainer|Zenject|Reflex)/;

/**
 * Recursively enumerate every file path under `dir` whose basename matches
 * `predicate`. Sync. Returns POSIX-style relative paths joined to `dir`.
 *
 * @param {string} dir - Absolute directory to walk
 * @param {(basename: string) => boolean} predicate - File-name filter
 * @returns {string[]} Absolute file paths
 */
function walkSync(dir, predicate) {
  const results = [];
  if (!fs.existsSync(dir)) {
    return results;
  }

  /** @type {fs.Dirent[]} */
  const entries = fs.readdirSync(dir, { withFileTypes: true });
  for (const entry of entries) {
    const childPath = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      results.push(...walkSync(childPath, predicate));
      continue;
    }

    if (entry.isFile() && predicate(entry.name)) {
      results.push(childPath);
    }
  }

  return results;
}

/**
 * Strip the `.asmdef` extension and return the asmdef's declared name. The
 * file's `name` field is the canonical assembly name and must match the
 * filename per Unity convention; we read the JSON to be safe.
 *
 * @param {string} asmdefPath - Absolute path to an .asmdef file
 * @returns {string} Asmdef name (without extension)
 */
function readAsmdefName(asmdefPath) {
  const raw = fs.readFileSync(asmdefPath, "utf8");
  const parsed = JSON.parse(raw);
  if (typeof parsed.name !== "string" || parsed.name.length === 0) {
    // Fall back to the filename to keep this function pure-ish.
    return path.basename(asmdefPath, ".asmdef");
  }
  return parsed.name;
}

/**
 * Classify an asmdef name into a single category.
 *
 * Categories:
 *   - "perf"        — Benchmarks/Allocations (excluded from PR
 *                     gates per .llm/context.md line 114).
 *   - "comparison"  — external comparison benchmarks.
 *   - "integration" — VContainer/Zenject/Reflex DI integration suites.
 *   - "core"        — Everything else (Editor, Runtime, etc.).
 *
 * Note: comparison suites benchmark DxMessaging against alternative messaging
 * libraries, so they require an additional opt-in after the external packages
 * are installed in the harness manifest.
 *
 * @param {string} name - Asmdef assembly name (no extension)
 * @returns {"perf" | "comparison" | "integration" | "core"} Classification
 */
function classifyAsmdef(name) {
  if (typeof name !== "string" || name.length === 0) {
    return "core";
  }

  if (PERF_NAME_REGEX.test(name)) {
    return "perf";
  }

  if (COMPARISON_NAME_REGEX.test(name)) {
    return "comparison";
  }

  if (INTEGRATION_NAME_REGEX.test(name)) {
    return "integration";
  }

  return "core";
}

/**
 * @typedef {object} AsmdefEntry
 * @property {string} name - Asmdef assembly name
 * @property {string} path - Absolute path to the asmdef file
 * @property {boolean} isPerf - True when classification is "perf"
 * @property {boolean} isComparison - True when classification is "comparison"
 * @property {boolean} isInteg - True when classification is "integration"
 * @property {boolean} isEditorOnly - True iff includePlatforms is exactly ["Editor"]
 */

/**
 * Read an asmdef's `includePlatforms` array and decide whether the assembly is
 * editor-only. An assembly is editor-only iff `includePlatforms` is exactly
 * `["Editor"]`. Editor-only test assemblies (EditMode suites + Editor
 * benchmarks/integrations) cannot run inside a built player, so the standalone
 * runtime-only flow must exclude them.
 *
 * @param {string} asmdefPath - Absolute path to an .asmdef file
 * @returns {boolean} True when includePlatforms === ["Editor"]
 */
function readAsmdefIsEditorOnly(asmdefPath) {
  const raw = fs.readFileSync(asmdefPath, "utf8");
  const parsed = JSON.parse(raw);
  const platforms = parsed.includePlatforms;
  return (
    Array.isArray(platforms) && platforms.length === 1 && platforms[0] === "Editor"
  );
}

/**
 * Enumerate every asmdef under `<repoRoot>/Tests/`. Sorted by `name` for
 * stable downstream output (CI summaries, contract tests).
 *
 * @param {string} repoRoot - Absolute path to the repository root
 * @returns {AsmdefEntry[]} Discovered test asmdefs
 */
function enumerateTestAsmdefs(repoRoot) {
  if (typeof repoRoot !== "string" || repoRoot.length === 0) {
    throw new TypeError("enumerateTestAsmdefs: repoRoot must be a non-empty string");
  }

  const testsDir = path.join(repoRoot, "Tests");
  const asmdefPaths = walkSync(testsDir, (n) => n.endsWith(".asmdef"));

  /** @type {AsmdefEntry[]} */
  const entries = asmdefPaths.map((asmdefPath) => {
    const name = readAsmdefName(asmdefPath);
    const classification = classifyAsmdef(name);
    return {
      name,
      path: asmdefPath,
      isPerf: classification === "perf",
      isComparison: classification === "comparison",
      isInteg: classification === "integration",
      isEditorOnly: readAsmdefIsEditorOnly(asmdefPath)
    };
  });

  entries.sort((a, b) => a.name.localeCompare(b.name));
  return entries;
}

/**
 * @typedef {object} IncludeOptions
 * @property {boolean} [includePerf=false]         Include "perf" asmdefs.
 * @property {boolean} [includeComparisons=false]  Include comparison benchmark asmdefs.
 * @property {boolean} [includeIntegrations=false] Include "integration" asmdefs.
 * @property {boolean} [runtimeOnly=false]         Drop editor-only asmdefs
 *                     (includePlatforms === ["Editor"]). Used by the standalone
 *                     player flow, where EditMode tests cannot run. Applied
 *                     BEFORE the perf/comparison/integration gating so it
 *                     composes (e.g. { runtimeOnly: true, includePerf: true } ->
 *                     Runtime + Runtime.Benchmarks).
 */

/**
 * Names of test asmdefs included in the default Unity Test Runner suite.
 *
 * By default ONLY "core" asmdefs are returned. Perf and integration suites
 * are opt-in:
 *   - includePerf:         add Benchmarks/Allocations.
 *   - includeComparisons:  add external comparison benchmarks.
 *   - includeIntegrations: add VContainer/Zenject/Reflex (caller must ensure
 *                          the corresponding DI packages are in manifest.json).
 *
 * @param {string} repoRoot - Absolute path to the repository root
 * @param {IncludeOptions} [options] - Opt-in flags (default: all false)
 * @returns {string[]} Sorted asmdef names (no extension)
 */
function defaultIncludeAssemblies(repoRoot, options) {
  const opts = options || {};
  const includePerf = opts.includePerf === true;
  const includeComparisons = opts.includeComparisons === true;
  const includeIntegrations = opts.includeIntegrations === true;
  const runtimeOnly = opts.runtimeOnly === true;

  return enumerateTestAsmdefs(repoRoot)
    .filter((entry) => {
      // Runtime-only gate first so it composes with the category opt-ins:
      // editor-only asmdefs cannot run in a built player and are dropped
      // before any perf/comparison/integration decision.
      if (runtimeOnly && entry.isEditorOnly) {
        return false;
      }
      if (entry.isPerf) {
        return includePerf;
      }
      if (entry.isComparison) {
        return includeComparisons;
      }
      if (entry.isInteg) {
        return includeIntegrations;
      }
      return true;
    })
    .map((entry) => entry.name);
}

/**
 * Names of test asmdefs excluded from the default Unity Test Runner suite.
 * Mirror of `defaultIncludeAssemblies` — anything not selected by the include
 * options is returned here. With no options, returns all perf + integration
 * asmdefs.
 *
 * @param {string} repoRoot - Absolute path to the repository root
 * @param {IncludeOptions} [options] - Opt-in flags (default: all false)
 * @returns {string[]} Sorted asmdef names (no extension)
 */
function defaultExcludeAssemblies(repoRoot, options) {
  const opts = options || {};
  const includePerf = opts.includePerf === true;
  const includeComparisons = opts.includeComparisons === true;
  const includeIntegrations = opts.includeIntegrations === true;
  const runtimeOnly = opts.runtimeOnly === true;

  return enumerateTestAsmdefs(repoRoot)
    .filter((entry) => {
      // Mirror of defaultIncludeAssemblies: when runtimeOnly is set, every
      // editor-only asmdef is excluded regardless of category.
      if (runtimeOnly && entry.isEditorOnly) {
        return true;
      }
      if (entry.isPerf) {
        return !includePerf;
      }
      if (entry.isComparison) {
        return !includeComparisons;
      }
      if (entry.isInteg) {
        return !includeIntegrations;
      }
      return false;
    })
    .map((entry) => entry.name);
}

module.exports = {
  PERF_NAME_REGEX,
  COMPARISON_NAME_REGEX,
  INTEGRATION_NAME_REGEX,
  classifyAsmdef,
  enumerateTestAsmdefs,
  defaultIncludeAssemblies,
  defaultExcludeAssemblies
};

if (require.main === module) {
  // Self-test mode: print classified asmdefs for the current repo.
  const repoRoot = path.resolve(__dirname, "..", "..", "..");
  const all = enumerateTestAsmdefs(repoRoot);
  const include = defaultIncludeAssemblies(repoRoot);
  const exclude = defaultExcludeAssemblies(repoRoot);

  process.stdout.write(`repoRoot: ${repoRoot}\n`);
  process.stdout.write(`discovered ${all.length} asmdef(s):\n`);
  for (const entry of all) {
    const cls = entry.isPerf
      ? "perf"
      : entry.isComparison
        ? "comparison"
        : entry.isInteg
          ? "integration"
          : "core";
    process.stdout.write(`  [${cls}] ${entry.name}\n`);
  }
  process.stdout.write(
    `\ndefault include (${include.length}, core only — pass ` +
      `{ includePerf, includeComparisons, includeIntegrations } to opt in):\n`
  );
  for (const name of include) {
    process.stdout.write(`  + ${name}\n`);
  }
  process.stdout.write(
    `\ndefault exclude (${exclude.length}, perf + comparison + integration suites):\n`
  );
  for (const name of exclude) {
    process.stdout.write(`  - ${name}\n`);
  }

  // Diagnostic: runtime-only include list (used by the standalone player flow,
  // where EditMode/editor-only asmdefs cannot run).
  const runtimeInclude = defaultIncludeAssemblies(repoRoot, { runtimeOnly: true });
  process.stdout.write(
    `\nruntime-only include (${runtimeInclude.length}, drops editor-only asmdefs):\n`
  );
  for (const name of runtimeInclude) {
    process.stdout.write(`  * ${name}\n`);
  }
}
