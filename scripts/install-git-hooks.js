#!/usr/bin/env node
"use strict";

const path = require("path");
const childProcess = require("child_process");

const HOOKS_PATH = "scripts/hooks";

function runGit(args, options) {
  const result = childProcess.spawnSync("git", args, {
    cwd: options.cwd,
    encoding: "utf8",
    stdio: options.stdio || ["ignore", "pipe", "pipe"]
  });
  return result;
}

function getRepoRoot(cwd) {
  const result = runGit(["rev-parse", "--show-toplevel"], { cwd });
  if (result.error || result.status !== 0) {
    return null;
  }

  const root = String(result.stdout || "").trim();
  return root.length > 0 ? root : null;
}

function getConfiguredHooksPath(repoRoot) {
  const result = runGit(["config", "--local", "--get", "core.hooksPath"], { cwd: repoRoot });
  if (result.error || result.status !== 0) {
    return "";
  }

  return String(result.stdout || "")
    .trim()
    .replace(/\\/g, "/");
}

function installGitHooks(options = {}) {
  const cwd = options.cwd || process.cwd();
  const log = options.log || console.log;
  const warn = options.warn || console.warn;
  const repoRoot = getRepoRoot(cwd);

  if (!repoRoot) {
    log("git hooks: not inside a Git worktree; skipping native hook installation.");
    return { ok: true, changed: false, skipped: true };
  }

  const hooksDir = path.join(repoRoot, HOOKS_PATH);
  const prePush = path.join(hooksDir, "pre-push");
  if (!require("fs").existsSync(prePush)) {
    warn(`git hooks: ${HOOKS_PATH}/pre-push is missing; cannot configure native hooks.`);
    return { ok: false, changed: false, skipped: false };
  }

  const current = getConfiguredHooksPath(repoRoot);
  if (current === HOOKS_PATH) {
    log(`git hooks: core.hooksPath already set to ${HOOKS_PATH}.`);
    return { ok: true, changed: false, skipped: false };
  }

  const result = runGit(["config", "--local", "core.hooksPath", HOOKS_PATH], {
    cwd: repoRoot,
    stdio: "inherit"
  });
  if (result.error || result.status !== 0) {
    warn(
      `git hooks: failed to set core.hooksPath to ${HOOKS_PATH}; ` +
        "native hooks may not run until this is repaired."
    );
    return { ok: false, changed: false, skipped: false };
  }

  log(`git hooks: configured core.hooksPath=${HOOKS_PATH}.`);
  return { ok: true, changed: true, skipped: false };
}

if (require.main === module) {
  const result = installGitHooks();
  process.exit(result.ok ? 0 : 1);
}

module.exports = {
  HOOKS_PATH,
  installGitHooks
};
