#!/usr/bin/env node
"use strict";

/**
 * post-edit-validate-guard.js
 *
 * Claude Code PostToolUse hook (Edit / Write / MultiEdit). A change-aware,
 * advisory validator that runs the FAST, read-only validators relevant to the
 * file an agent just edited, IN-LOOP, so the failure class is caught during
 * editing instead of slipping through to the last-resort native git hook.
 *
 * Motivating gap: editing `package.json` `files` or adding/removing a DLL or
 * .meta under `Editor/Analyzers/` changes the npm-pack output, but nothing
 * validated packaging until `git push` reached the native hook. This guard
 * closes that gap (and the whole category) by mapping the edited path to its
 * relevant validators through a dispatch table.
 *
 * Design (single responsibility, composable with the YAML guard):
 *   - YAML line-length is owned by `yaml-line-length-guard.js` (it auto-fixes).
 *     This guard deliberately SKIPS `*.yml`/`*.yaml` so the two hooks do not
 *     overlap. Both are wired under PostToolUse.
 *   - This guard is READ-ONLY (it never mutates the working tree) and ADVISORY
 *     (always exit 0; it MUST NOT block tool use). It reports failing
 *     validators back to the agent via hookSpecificOutput.additionalContext
 *     plus systemMessage.
 *   - The dispatch table is the generalization point: adding a new
 *     `{ id, matches, validators }` entry extends edit-time coverage to a new
 *     category without touching the engine. The HOT path (doc-quality on
 *     `.cs` / `.md`) is sub-50ms and read-only. The npm-packaging entry is the
 *     one multi-second exception: it shells `npm pack --json --dry-run`
 *     (~1-2s on Linux, more on Windows via the cmd.exe shim), which is accepted
 *     because it fires ONLY on infrequent packaging-input edits
 *     (`package.json` / `.npmignore` / `*.dll` / `*.meta`), never on the hot
 *     `.cs` / `.md` edit loop. By contrast cspell's multi-second dictionary
 *     load would fire on every doc edit, so it stays at pre-push, never here.
 *
 * Cross-platform: pure Node plus `scripts/lib/shell-command.js`
 * (spawnPlatformCommandSync) for child processes; no bash, no shell:true, no
 * devcontainer assumption. Runs on native Linux, macOS, and Windows.
 */

const fs = require("fs");
const path = require("path");
const { spawnPlatformCommandSync } = require("../lib/shell-command");
const { isOutsideRelative } = require("../lib/path-classifier");

const PACKAGING_SKILL = ".llm/skills/packaging/npm-package-configuration.md";
const DOCS_SKILL = ".llm/skills/documentation/documentation-style-guide.md";

/**
 * Re-entrancy guard. A validator this guard spawns may itself trigger nested
 * tooling; the env flag lets those nested invocations short-circuit so the
 * guard never recurses on its own child processes.
 */
const ACTIVE_ENV = "DXMSG_POST_EDIT_GUARD_ACTIVE";

/**
 * True when the repo-relative path is one whose edit can change npm-pack output
 * (package.json `files`, `.npmignore`, or any shipped/removed `.dll` / `.meta`).
 * Deliberately NARROW: editing `.cs` CONTENT under a shipped dir does not change
 * packaging, so it is excluded here (it is covered by the doc-quality entry).
 *
 * @param {string} rel Repo-relative POSIX path.
 * @returns {boolean} True when packaging validation is relevant.
 */
function isPackagingRelevant(rel) {
  return (
    rel === "package.json" ||
    rel === ".npmignore" ||
    /\.dll$/i.test(rel) ||
    /\.meta$/i.test(rel)
  );
}

/**
 * True when the path is a documentation-prose target (Markdown or C#) that the
 * ascii / code-pattern / prose validators inspect.
 *
 * @param {string} rel Repo-relative POSIX path.
 * @returns {boolean} True when doc-quality validation is relevant.
 */
function isDocQualityRelevant(rel) {
  return /\.(md|markdown|cs)$/i.test(rel);
}

/**
 * True for YAML, which is owned by the sibling yaml-line-length-guard. This
 * guard skips it so the two PostToolUse hooks never double-process a file.
 *
 * @param {string} rel Repo-relative POSIX path.
 * @returns {boolean} True for `*.yml` / `*.yaml`.
 */
function looksLikeYamlPath(rel) {
  return /\.ya?ml$/i.test(rel);
}

/**
 * The dispatch table. Each entry maps a class of edited file to the FAST,
 * read-only validators that should run in-loop. `validators[].args` is a
 * function of the absolute path so a validator can be invoked file-scoped.
 *
 * @returns {Array<{id:string, matches:(rel:string)=>boolean,
 *   validators:Array<{label:string, args:(abs:string)=>string[]}>,
 *   remediation:string}>}
 */
function buildDispatchTable() {
  return [
    {
      id: "npm-packaging",
      matches: isPackagingRelevant,
      remediation:
        `Editing packaging inputs changed what 'npm pack' ships. Run ` +
        `'node scripts/validate-npm-meta.js --check' and fix the package.json ` +
        `'files' array / .npmignore so every shipped .meta and the required ` +
        `analyzer DLLs stay in the tarball. See ${PACKAGING_SKILL}.`,
      validators: [
        {
          label: "validate-npm-meta",
          // pass_filenames-style: the validator resolves the whole package; it
          // does not take a single-file arg.
          args: () => ["scripts/validate-npm-meta.js", "--check"]
        }
      ]
    },
    {
      id: "doc-quality",
      matches: isDocQualityRelevant,
      remediation:
        `Documentation must be pure ASCII, compile in code samples, and avoid ` +
        `LLM-style filler. Run the normalizers (e.g. ` +
        `'node scripts/normalize-docs-ascii.js <file>') then re-check. See ` +
        `${DOCS_SKILL}.`,
      validators: [
        { label: "docs-ascii", args: (abs) => ["scripts/validate-docs-ascii.js", abs] },
        {
          label: "docs-code-patterns",
          args: (abs) => ["scripts/validate-doc-code-patterns.js", abs]
        },
        { label: "docs-prose", args: (abs) => ["scripts/validate-docs-prose.js", abs] }
      ]
    }
  ];
}

/**
 * Read stdin to completion as a UTF-8 string.
 *
 * @returns {Promise<string>} The full stdin payload.
 */
function readStdin() {
  return new Promise((resolve) => {
    let data = "";
    process.stdin.setEncoding("utf8");
    process.stdin.on("data", (chunk) => {
      data += chunk;
    });
    process.stdin.on("end", () => resolve(data));
    process.stdin.on("error", () => resolve(data));
  });
}

/**
 * Resolve the repository root. Prefers the Claude Code project dir env var;
 * falls back to the repo root relative to this script.
 *
 * @returns {string} Absolute repository root.
 */
function resolveRepoRoot() {
  if (process.env.CLAUDE_PROJECT_DIR && process.env.CLAUDE_PROJECT_DIR.trim().length > 0) {
    return process.env.CLAUDE_PROJECT_DIR;
  }
  return path.resolve(__dirname, "..", "..");
}

/**
 * Convert an absolute path to a repo-relative POSIX path. Returns null when the
 * path is outside the repo (those are never validated here).
 *
 * @param {string} repoRoot Absolute repo root.
 * @param {string} absPath Absolute file path.
 * @returns {string|null} Repo-relative POSIX path, or null when outside repo.
 */
function toRepoRelativePosix(repoRoot, absPath) {
  const rawRel = path.relative(repoRoot, absPath);
  // `isOutsideRelative` is cross-drive-safe: on Windows where the edited file
  // and the repo live on different drives, `path.relative` returns an ABSOLUTE
  // target rather than a `..` chain, and the bare `rel.startsWith("..")` form
  // would wrongly treat that out-of-repo file as repo-relative and run
  // validators against a corrupted path. The empty string means absPath IS the
  // repo root (a directory, never validated here), so treat it as outside too.
  if (rawRel.length === 0 || isOutsideRelative(rawRel)) {
    return null;
  }
  return rawRel.replace(/\\/g, "/");
}

/**
 * Run one validator (read-only) under the active repo root and capture its
 * outcome. Uses spawnPlatformCommandSync for cross-platform argv-array
 * execution (no shell).
 *
 * @param {{label:string, args:(abs:string)=>string[]}} validator Table entry.
 * @param {string} absPath Absolute path of the edited file.
 * @param {string} repoRoot Absolute repo root (cwd for the spawn).
 * @param {Function} [spawnImpl] Injectable spawn for tests.
 * @returns {{label:string, ok:boolean, detail:string}} Outcome.
 */
function runValidator(validator, absPath, repoRoot, spawnImpl = spawnPlatformCommandSync) {
  const args = validator.args(absPath);
  const result = spawnImpl(process.execPath, args, {
    cwd: repoRoot,
    encoding: "utf8",
    env: { ...process.env, [ACTIVE_ENV]: "1" },
    stdio: ["ignore", "pipe", "pipe"]
  });

  if (result.error && result.error.code === "ENOENT") {
    // Node missing is an environment problem, not a content failure; stay quiet
    // (advisory guards must never manufacture noise from infra gaps).
    return { label: validator.label, ok: true, detail: "" };
  }

  const status = typeof result.status === "number" ? result.status : 1;
  if (status === 0) {
    return { label: validator.label, ok: true, detail: "" };
  }

  const combined = `${result.stdout || ""}\n${result.stderr || ""}`.trim();
  const lastLines = combined.split("\n").filter(Boolean).slice(-4).join(" | ");
  return { label: validator.label, ok: false, detail: lastLines };
}

/**
 * Pure core: given the edited file's repo-relative + absolute path, run every
 * matching dispatch entry's validators and collect failures. No stdin / stdout.
 *
 * @param {string} rel Repo-relative POSIX path.
 * @param {string} absPath Absolute file path.
 * @param {string} repoRoot Absolute repo root.
 * @param {object} [deps] Injectable deps for tests.
 * @param {Function} [deps.spawnImpl] Spawn implementation.
 * @param {Array} [deps.table] Dispatch table override.
 * @returns {Array<{id:string, remediation:string,
 *   failures:Array<{label:string, detail:string}>}>} One result per matching
 *   entry that had at least one failing validator.
 */
function evaluate(rel, absPath, repoRoot, deps = {}) {
  const spawnImpl = deps.spawnImpl || spawnPlatformCommandSync;
  const table = deps.table || buildDispatchTable();
  const reports = [];

  for (const entry of table) {
    if (!entry.matches(rel)) {
      continue;
    }

    const failures = [];
    for (const validator of entry.validators) {
      const outcome = runValidator(validator, absPath, repoRoot, spawnImpl);
      if (!outcome.ok) {
        failures.push({ label: outcome.label, detail: outcome.detail });
      }
    }

    if (failures.length > 0) {
      reports.push({ id: entry.id, remediation: entry.remediation, failures });
    }
  }

  return reports;
}

/**
 * Build the agent-facing advisory message from evaluation reports. Returns null
 * when there is nothing to report.
 *
 * @param {string} rel Repo-relative path for display.
 * @param {Array} reports Output of {@link evaluate}.
 * @returns {string|null} Advisory text, or null when silent.
 */
function buildMessage(rel, reports) {
  if (reports.length === 0) {
    return null;
  }

  const parts = [`post-edit-validate-guard found issues after editing ${rel}:`];
  for (const report of reports) {
    const failing = report.failures
      .map((f) => (f.detail ? `${f.label} (${f.detail})` : f.label))
      .join("; ");
    parts.push(`[${report.id}] ${failing}. ${report.remediation}`);
  }
  return parts.join(" ");
}

/**
 * Main hook entry. Reads the PostToolUse payload, runs matching validators, and
 * emits advisory JSON. Always exits 0 (a guard must not block tool use).
 *
 * @param {string} stdinPayload Raw PostToolUse JSON from stdin.
 * @param {object} [env] Environment (injectable for tests).
 * @param {object} [deps] Injectable deps (spawnImpl, table).
 * @returns {number} Process exit code (always 0).
 */
function run(stdinPayload, env = process.env, deps = {}) {
  // Never recurse on a child process the guard itself spawned.
  if (env[ACTIVE_ENV] === "1") {
    return 0;
  }

  let event;
  try {
    event = JSON.parse(stdinPayload);
  } catch (_error) {
    return 0;
  }

  const toolInput = event && event.tool_input ? event.tool_input : {};
  const filePath = toolInput.file_path;
  if (typeof filePath !== "string" || filePath.length === 0) {
    return 0;
  }

  const repoRoot = resolveRepoRoot();
  const rel = toRepoRelativePosix(repoRoot, filePath);
  if (rel === null) {
    return 0;
  }

  // YAML is owned by the sibling guard; skip to avoid double-processing.
  if (looksLikeYamlPath(rel)) {
    return 0;
  }

  // Fast early-exit: only proceed when the file exists and matches an entry.
  const table = deps.table || buildDispatchTable();
  if (!table.some((entry) => entry.matches(rel))) {
    return 0;
  }

  try {
    if (!fs.statSync(filePath).isFile()) {
      return 0;
    }
  } catch (_error) {
    return 0;
  }

  const reports = evaluate(rel, filePath, repoRoot, deps);
  const message = buildMessage(rel, reports);
  if (message === null) {
    return 0;
  }

  const output = {
    hookSpecificOutput: {
      hookEventName: "PostToolUse",
      additionalContext: message
    },
    systemMessage: message
  };
  process.stdout.write(`${JSON.stringify(output)}\n`);
  return 0;
}

module.exports = {
  ACTIVE_ENV,
  isPackagingRelevant,
  isDocQualityRelevant,
  looksLikeYamlPath,
  buildDispatchTable,
  resolveRepoRoot,
  toRepoRelativePosix,
  runValidator,
  evaluate,
  buildMessage,
  run
};

if (require.main === module) {
  readStdin().then((payload) => {
    process.exit(run(payload));
  });
}
