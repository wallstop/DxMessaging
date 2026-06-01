#!/usr/bin/env node
"use strict";

/**
 * preflight-before-push-guard.js
 *
 * Claude Code PreToolUse hook. When an agent is about to run a `git push`
 * Bash command, this guard first runs a dedicated changed-file cspell check,
 * then runs the change-aware preflight (the FULL change-set: committed range +
 * working tree, i.e. `--scope=full`) and, per the repo owner's decision, BLOCKS
 * the push when checks fail. The direct cspell pass is intentionally before the
 * broader preflight so already-committed / generated / shell-written spelling
 * failures cannot slip to native pre-push just because the broader guard
 * preflight timed out on a large branch.
 *
 * It reuses ONLY the I/O harness of yaml-line-length-guard.js (stdin JSON read,
 * `$CLAUDE_PROJECT_DIR` root resolution, pure Node, no deps). It is NOT a
 * template beyond that harness.
 *
 * Contract (design 5.2 + owner override "Block push + advisory Stop"):
 *   1. Read the PreToolUse event JSON from stdin -> `tool_name`,
 *      `tool_input.command`.
 *   2. `tool_name !== "Bash"` -> exit 0 silently (the common non-Bash case).
 *   3. Re-entrancy: if `DXMSG_PREFLIGHT_ACTIVE === "1"` -> exit 0 silently. A
 *      `git` call made INSIDE preflight/recovery must not be re-guarded.
 *   4. Conservative push detection via {@link commandLooksLikeGitPush} (a
 *      documented HEURISTIC, NOT a tokenizer): if it does not look like a push,
 *      exit 0 silently (~0 latency). Over-triggering is safe -- preflight is
 *      read-only and idempotent.
 *   5. On a probable push: compute the full change-set and run
 *        node scripts/run-managed-cspell.js --file-list <existing files>
 *      plus `stdin://<repo-path>` checks for committed HEAD content that the
 *      live worktree cannot faithfully represent. Any non-zero cspell exit
 *      DENIES the push; the managed cspell wrapper has already attempted
 *      node_modules auto-repair.
 *   6. Then spawn
 *        node scripts/preflight.js --json --profile=guard --scope=full --no-recover
 *      with `DXMSG_PREFLIGHT_ACTIVE=1` in the child env. `--profile=guard` runs
 *      the fast subset (the heavy Jest suites are deferred to the native hook);
 *      `--scope=full` uses the committed range + working tree; `--no-recover`
 *      avoids paying integrity recovery twice in a session.
 *   7. {@link buildDecision} maps the preflight `status` to a PreToolUse
 *      decision:
 *        - `ok`               -> permissionDecision "allow" (native hook is the
 *                                final backstop).
 *        - `checks-failed`    -> "deny" naming the failing hook ids + the single
 *                                remediation.
 *        - any `policyFailures` (even under infra-unavailable) -> "deny" (policy
 *                                / security hooks never fail open).
 *        - `infra-unavailable` (no policyFailures) -> "allow" + a WARNING (do
 *                                not wedge the agent on a broken host).
 *   8. Emit the documented PreToolUse `hookSpecificOutput` JSON on stdout and
 *      exit 0. The guard NEVER relies on its exit code to block (that is
 *      PostToolUse semantics); if a CLI version ignores `deny`, the native
 *      pre-push hook still gates the push.
 *
 * All child-process spawns route through `spawnPlatformCommandSync`
 * (scripts/lib/shell-command.js); no raw spawn, no shell.
 */

const fs = require("fs");
const os = require("os");
const path = require("path");
const { spawnPlatformCommandSync } = require("../lib/shell-command");
const { computeChangeSet } = require("../lib/changed-files");
const { CSPELL_EXTENSION_PATTERN } = require("./post-edit-validate-guard");

const HOOK_EVENT_NAME = "PreToolUse";

/**
 * Self-imposed ceiling (ms) on the preflight child. The push-guard runs a
 * full-scope change-aware preflight; on a very large change-set that can exceed
 * the Claude Code hook framework's own timeout, whose default and kill semantics
 * vary by CLI version. Capping it here makes degradation DETERMINISTIC and
 * version-independent: on timeout the child is killed, no parseable JSON is
 * produced, and the guard fails open to "allow" (the native pre-push hook is the
 * real, exhaustive gate). Override with DXMSG_PREFLIGHT_HOOK_TIMEOUT_MS.
 */
const PREFLIGHT_TIMEOUT_MS = (() => {
  const raw = Number.parseInt(process.env.DXMSG_PREFLIGHT_HOOK_TIMEOUT_MS || "", 10);
  return Number.isFinite(raw) && raw > 0 ? raw : 45000;
})();

/**
 * Dedicated changed-file cspell ceiling (ms). This runs before the broader
 * guard preflight so committed or shell-generated spelling failures are caught
 * even when the full guard preflight is too slow for a large branch.
 */
const PUSH_CSPELL_TIMEOUT_MS = (() => {
  const raw = Number.parseInt(process.env.DXMSG_PREFLIGHT_CSPELL_TIMEOUT_MS || "", 10);
  return Number.isFinite(raw) && raw > 0 ? raw : 15000;
})();

/**
 * The single remediation line shown to the agent on a blocked push. Names the
 * canonical reproduce/auto-fix entrypoint and reaffirms that git hooks are the
 * last-resort backstop, not the first signal.
 */
const REMEDIATION =
  "Run `npm run preflight` to reproduce and auto-fix, then retry the push. " +
  "Git hooks are the last-resort backstop, not the first signal.";

const CSPELL_REMEDIATION =
  "Fix the spelling issue or add legitimate project vocabulary to .cspell.json, " +
  "then retry the push. This changed-file cspell guard runs before the slower " +
  "full preflight so the native pre-push hook stays a last-resort backstop.";

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
 * Resolve the repository root. Prefers the Claude Code project-dir env var;
 * falls back to the repo root relative to this script (scripts/hooks/..).
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
 * Re-entrancy check: true when preflight is already running in this process
 * tree (the sentinel env var is set), so a `git` call inside preflight/recovery
 * is not recursively guarded.
 *
 * @param {object} [env] Environment to inspect (default `process.env`).
 * @returns {boolean} True when the guard should skip.
 */
function shouldSkip(env = process.env) {
  return !!env && env.DXMSG_PREFLIGHT_ACTIVE === "1";
}

/**
 * Conservative HEURISTIC for "this Bash command probably runs `git push`".
 *
 * This is deliberately NOT a shell tokenizer (there is no shell parser in this
 * repo, and building one is out of scope). It scans the command string for a
 * `git` word token followed -- anywhere later in the string -- by a `push` word
 * token, using word-boundary matching so substrings like `pushd` or `gitlab`
 * do not trip it. It intentionally OVER-triggers on ambiguous input (e.g.
 * `git push` mentioned after a `&&`, with a leading `cd`, or with env-var
 * prefixes); that is safe because preflight is read-only and idempotent, and a
 * spurious extra preflight run is far cheaper than a missed one. The native
 * pre-push hook remains the real, tool-agnostic gate -- this guard only
 * accelerates the signal.
 *
 * Matches: `git push`, `git -C dir push`, `cd x && git push origin HEAD`,
 *   `FOO=1 git push`, `git push --force-with-lease`.
 * Does NOT match: `git status`, `git pushd-not-a-thing`, `echo done`,
 *   `npm run push:docs` (no standalone `git` token).
 *
 * @param {string} command The Bash command string from `tool_input.command`.
 * @returns {boolean} True when the command looks like a `git push`.
 */
function commandLooksLikeGitPush(command) {
  if (typeof command !== "string" || command.length === 0) {
    return false;
  }
  // A standalone `git` token (not part of a longer word such as `gitlab`).
  const hasGit = /(?:^|[^\w-])git(?![\w-])/.test(command);
  if (!hasGit) {
    return false;
  }
  // A standalone `push` token (not `pushd`, not `push:docs` as a single npm
  // script token -- the trailing `:` is not a word char so `push:` would match;
  // but that path also needs a `git` token, which `npm run push:docs` lacks).
  const hasPush = /(?:^|[^\w-])push(?![\w-])/.test(command);
  return hasPush;
}

/**
 * Return the subset of a change-set that the native cspell hook covers.
 *
 * @param {string[]} files Repo-relative POSIX paths.
 * @returns {string[]} Changed spelling files.
 */
function filterCspellFiles(files) {
  return (Array.isArray(files) ? files : []).filter((file) => CSPELL_EXTENSION_PATTERN.test(file));
}

function uniqueSorted(values) {
  return [...new Set(values)].sort();
}

function repoFileExists(repoRoot, rel, deps = {}) {
  const statSyncFn = deps.statSyncFn || fs.statSync.bind(fs);
  try {
    const stats = statSyncFn(path.join(repoRoot, ...rel.split("/")));
    return !!stats && (typeof stats.isFile !== "function" || stats.isFile());
  } catch {
    return false;
  }
}

function splitNulFields(stdout) {
  return String(stdout || "")
    .split("\0")
    .filter((field) => field.length > 0);
}

/**
 * Collect all tracked repo files. Used only when the full change-set cannot
 * resolve an integration merge-base; in that degraded state a committed typo
 * would otherwise be invisible to a changed-file pass.
 *
 * @param {string} repoRoot Absolute repo root.
 * @param {object} [deps] Injected dependencies.
 * @returns {string[]} Repo-relative POSIX tracked paths.
 */
function collectTrackedFiles(repoRoot, deps = {}) {
  const gitSpawnFn = deps.gitSpawnFn || spawnPlatformCommandSync;
  const result = gitSpawnFn("git", ["ls-files", "-z"], {
    cwd: repoRoot,
    encoding: "utf8",
    stdio: ["ignore", "pipe", "pipe"]
  });

  const status = result && typeof result.status === "number" ? result.status : 1;
  if (status !== 0 || (result && result.error)) {
    const combined =
      `${(result && result.stdout) || ""}\n${(result && result.stderr) || ""}`.trim();
    const spawnDetail =
      result && result.error && result.error.message ? `spawn error: ${result.error.message}` : "";
    throw new Error(
      [combined, spawnDetail].filter(Boolean).join("\n").trim() || "git ls-files failed"
    );
  }

  return splitNulFields(result.stdout).map((file) => file.replace(/\\/g, "/"));
}

function needsTrackedFallback(changeSet) {
  return (
    !!changeSet &&
    Object.prototype.hasOwnProperty.call(changeSet, "mergeBase") &&
    !changeSet.mergeBase
  );
}

function readGitHeadFile(repoRoot, rel, deps = {}) {
  const gitSpawnFn = deps.gitSpawnFn || spawnPlatformCommandSync;
  const result = gitSpawnFn("git", ["show", `HEAD:${rel}`], {
    cwd: repoRoot,
    encoding: "utf8",
    stdio: ["ignore", "pipe", "pipe"],
    maxBuffer: 16 * 1024 * 1024
  });
  const status = result && typeof result.status === "number" ? result.status : 1;
  if (status !== 0 || (result && result.error)) {
    return null;
  }
  return typeof result.stdout === "string" ? result.stdout : "";
}

function writeCspellFileList(repoRoot, files, deps = {}) {
  const mkdtempSyncFn = deps.mkdtempSyncFn || fs.mkdtempSync.bind(fs);
  const writeFileSyncFn = deps.writeFileSyncFn || fs.writeFileSync.bind(fs);
  const rmSyncFn = deps.rmSyncFn || fs.rmSync.bind(fs);
  const tmpdirFn = deps.tmpdirFn || os.tmpdir;
  const tempDir = mkdtempSyncFn(path.join(tmpdirFn(), "dxm-prepush-cspell-"));
  const fileListPath = path.join(tempDir, "files.txt");
  const keptFiles = [];
  const inputPaths = [];

  try {
    for (const file of files) {
      if (!repoFileExists(repoRoot, file, deps)) {
        continue;
      }
      keptFiles.push(file);
      inputPaths.push(path.join(repoRoot, ...file.split("/")));
    }

    const body = inputPaths.join("\n") + (inputPaths.length > 0 ? "\n" : "");
    writeFileSyncFn(fileListPath, body, "utf8");
  } catch (error) {
    try {
      rmSyncFn(tempDir, { recursive: true, force: true });
    } catch {
      // Best effort: the caller will report the write failure as the blocker.
    }
    throw error;
  }
  return {
    fileListPath,
    keptFiles,
    cleanup() {
      try {
        rmSyncFn(tempDir, { recursive: true, force: true });
      } catch {
        // Advisory guard temp cleanup must not mask the cspell result.
      }
    }
  };
}

function collectVirtualHeadFiles(
  repoRoot,
  files,
  committedLikeFiles,
  worktreeLikeFiles,
  requiredHeadFiles,
  deps = {}
) {
  const virtualFiles = [];
  const missingHeadFiles = [];
  for (const file of files) {
    if (!committedLikeFiles.has(file)) {
      continue;
    }

    const worktreeCanRepresentHead =
      repoFileExists(repoRoot, file, deps) && !worktreeLikeFiles.has(file);
    if (worktreeCanRepresentHead) {
      continue;
    }

    const content = readGitHeadFile(repoRoot, file, deps);
    if (content !== null) {
      virtualFiles.push({ file, content });
    } else if (requiredHeadFiles.has(file)) {
      missingHeadFiles.push(file);
    }
  }
  return { virtualFiles, missingHeadFiles };
}

function hasStructuredSources(changeSet) {
  return !!changeSet && !!changeSet.sources && typeof changeSet.sources === "object";
}

function addAll(target, values) {
  for (const value of values) {
    target.add(value);
  }
}

function runManagedCspell(spawnFn, repoRoot, args, env, input, skipIntegrity) {
  const childEnv = { ...env, DXMSG_PREFLIGHT_ACTIVE: "1" };
  if (skipIntegrity) {
    childEnv.DXMSG_HOOK_SKIP_INTEGRITY = "1";
  }

  return spawnFn(process.execPath, args, {
    cwd: repoRoot,
    encoding: "utf8",
    stdio: input === undefined ? ["ignore", "pipe", "pipe"] : ["pipe", "pipe", "pipe"],
    input,
    env: childEnv,
    timeout: PUSH_CSPELL_TIMEOUT_MS,
    killSignal: "SIGTERM",
    maxBuffer: 16 * 1024 * 1024
  });
}

function cspellResultFailed(result) {
  const status = result && typeof result.status === "number" ? result.status : 1;
  return status !== 0 || !!(result && result.error);
}

function cspellResultDetail(result) {
  const combined = `${(result && result.stdout) || ""}\n${(result && result.stderr) || ""}`.trim();
  const spawnDetail =
    result && result.error && result.error.message ? `spawn error: ${result.error.message}` : "";
  return [combined, spawnDetail].filter(Boolean).join("\n").trim();
}

/**
 * Run the changed-file cspell guard. This is intentionally narrower than the
 * full preflight: it exists so already-committed, generated, or shell-written
 * spelling failures cannot slip past the agent push guard just because the
 * broader preflight hit its timeout. Any non-zero cspell exit blocks the push;
 * the managed cspell wrapper has already attempted node_modules auto-repair.
 *
 * @param {string} repoRoot Absolute repo root.
 * @param {object} [deps] Injected dependencies.
 * @returns {{kind:"ok"|"checks-failed", files:string[], detail:string}}
 */
function runChangedCspellGuard(repoRoot, deps = {}) {
  const {
    computeChangeSetFn = computeChangeSet,
    spawnFn = spawnPlatformCommandSync,
    collectTrackedFilesFn = collectTrackedFiles,
    env = process.env
  } = deps;

  let changeSet;
  try {
    changeSet = computeChangeSetFn({ scope: "full" });
  } catch (error) {
    const detail = error && error.message ? error.message : String(error);
    return {
      kind: "checks-failed",
      files: [],
      detail: `changed-file cspell guard could not enumerate changed files: ${detail}`
    };
  }

  let files = filterCspellFiles(changeSet && changeSet.files);
  const structuredSources = hasStructuredSources(changeSet);
  const sources = structuredSources ? changeSet.sources : {};
  const committedLikeFiles = new Set(filterCspellFiles(sources.committed));
  const requiredHeadFiles = new Set(committedLikeFiles);
  const worktreeLikeFiles = new Set(
    filterCspellFiles([
      ...(Array.isArray(sources.staged) ? sources.staged : []),
      ...(Array.isArray(sources.unstaged) ? sources.unstaged : []),
      ...(Array.isArray(sources.untracked) ? sources.untracked : [])
    ])
  );
  if (!structuredSources) {
    addAll(worktreeLikeFiles, files);
  }

  if (needsTrackedFallback(changeSet)) {
    try {
      const tracked = filterCspellFiles(collectTrackedFilesFn(repoRoot, deps));
      addAll(committedLikeFiles, tracked);
      files = uniqueSorted([...files, ...tracked]);
    } catch (error) {
      const detail = error && error.message ? error.message : String(error);
      return {
        kind: "checks-failed",
        files,
        detail: `changed-file cspell guard could not enumerate tracked fallback files: ${detail}`
      };
    }
  }

  if (files.length === 0) {
    return { kind: "ok", files, detail: "" };
  }

  const { virtualFiles, missingHeadFiles } = collectVirtualHeadFiles(
    repoRoot,
    files,
    committedLikeFiles,
    worktreeLikeFiles,
    requiredHeadFiles,
    deps
  );
  if (missingHeadFiles.length > 0) {
    return {
      kind: "checks-failed",
      files: uniqueSorted(missingHeadFiles),
      detail:
        "changed-file cspell guard could not read committed HEAD content for: " +
        `${uniqueSorted(missingHeadFiles).join(", ")}`
    };
  }

  let fileList;
  try {
    fileList = writeCspellFileList(repoRoot, files, deps);
  } catch (error) {
    const detail = error && error.message ? error.message : String(error);
    return {
      kind: "checks-failed",
      files,
      detail: `changed-file cspell guard could not create file list: ${detail}`
    };
  }

  files = uniqueSorted([...fileList.keptFiles, ...virtualFiles.map((item) => item.file)]);
  if (files.length === 0) {
    fileList.cleanup();
    return { kind: "ok", files, detail: "" };
  }

  const results = [];
  const cspellBaseArgs = [
    "scripts/run-managed-cspell.js",
    "--no-progress",
    "--no-summary",
    "--no-must-find-files"
  ];
  let integrityAlreadyChecked = false;

  try {
    if (fileList.keptFiles.length > 0) {
      results.push(
        runManagedCspell(
          spawnFn,
          repoRoot,
          [...cspellBaseArgs, "--file-list", fileList.fileListPath],
          env,
          undefined,
          false
        )
      );
      integrityAlreadyChecked = true;
    }

    for (const item of virtualFiles) {
      results.push(
        runManagedCspell(
          spawnFn,
          repoRoot,
          [...cspellBaseArgs, `stdin://${item.file}`],
          env,
          item.content,
          integrityAlreadyChecked
        )
      );
      integrityAlreadyChecked = true;
    }
  } finally {
    fileList.cleanup();
  }

  const failures = results.filter(cspellResultFailed);
  if (failures.length === 0) {
    return { kind: "ok", files, detail: "" };
  }

  const detail = failures.map(cspellResultDetail).filter(Boolean).join("\n").trim();
  return {
    kind: "checks-failed",
    files,
    detail: detail || "changed-file cspell guard failed without diagnostic output"
  };
}

/**
 * Build the blocking PreToolUse output for the dedicated cspell guard.
 *
 * @param {{files:string[], detail:string}} result cspell guard result.
 * @returns {{hookSpecificOutput: object}} PreToolUse hook output.
 */
function buildCspellDecision(result) {
  const files = result && Array.isArray(result.files) ? result.files : [];
  const fileText = files.length > 0 ? ` changed spelling file(s): ${files.join(", ")}.` : "";
  const detail = result && result.detail ? ` ${result.detail}` : "";
  return {
    hookSpecificOutput: {
      hookEventName: HOOK_EVENT_NAME,
      permissionDecision: "deny",
      permissionDecisionReason: `Preflight blocked this push: changed-file cspell failed.${fileText}${detail} ${CSPELL_REMEDIATION}`
    }
  };
}

/**
 * Build the PreToolUse decision object from a preflight `status`.
 *
 * `status` is the `report.status` produced by scripts/preflight.js
 * (`{ kind, failures, policyFailures, warnings }`). Decision rules (owner
 * override "block push on checks-failed"):
 *   - `checks-failed` OR any `policyFailures` -> deny (policy/security hooks
 *     never fail open, even when the run is otherwise infra-unavailable).
 *   - `infra-unavailable` with NO policyFailures -> allow + a WARNING reason (do
 *     not wedge the agent on a broken host; the native hook still gates).
 *   - `ok` (or anything else) -> allow.
 *
 * @param {{kind: string, failures?: string[], policyFailures?: string[],
 *   warnings?: string[]}} status Preflight status object.
 * @returns {{hookSpecificOutput: object}} PreToolUse hook output.
 */
function buildDecision(status) {
  const safe = status && typeof status === "object" ? status : { kind: "ok" };
  const failures = Array.isArray(safe.failures) ? safe.failures : [];
  const policyFailures = Array.isArray(safe.policyFailures) ? safe.policyFailures : [];
  const warnings = Array.isArray(safe.warnings) ? safe.warnings : [];

  const allFailing = [...policyFailures, ...failures];
  const blocks = safe.kind === "checks-failed" || policyFailures.length > 0;

  if (blocks) {
    const named = allFailing.length > 0 ? allFailing.join(", ") : "(see preflight output above)";
    const reason = `Preflight blocked this push: failing hook(s): ${named}. ${REMEDIATION}`;
    return {
      hookSpecificOutput: {
        hookEventName: HOOK_EVENT_NAME,
        permissionDecision: "deny",
        permissionDecisionReason: reason
      }
    };
  }

  if (safe.kind === "infra-unavailable") {
    const warningText = warnings.length > 0 ? ` (${warnings.join("; ")})` : "";
    const reason =
      "Preflight could not run all checks because of an infrastructure issue " +
      `(not a code failure)${warningText}; allowing the push. The native pre-push ` +
      "hook and CI remain the backstop.";
    return {
      hookSpecificOutput: {
        hookEventName: HOOK_EVENT_NAME,
        permissionDecision: "allow",
        permissionDecisionReason: reason
      }
    };
  }

  return {
    hookSpecificOutput: {
      hookEventName: HOOK_EVENT_NAME,
      permissionDecision: "allow"
    }
  };
}

/**
 * Spawn the change-aware preflight in guard profile / full scope and parse its
 * `--json` report. Returns the parsed report, or null when preflight could not
 * be spawned or produced no parseable JSON (the caller then fails open).
 *
 * @param {string} repoRoot Absolute repo root (child cwd).
 * @param {object} [deps] Injected dependencies (spawn fn + base env) for tests.
 * @returns {object|null} The parsed preflight report, or null.
 */
function runGuardPreflight(repoRoot, deps = {}) {
  const { spawnFn = spawnPlatformCommandSync, env = process.env } = deps;

  const result = spawnFn(
    process.execPath,
    ["scripts/preflight.js", "--json", "--profile=guard", "--scope=full", "--no-recover"],
    {
      cwd: repoRoot,
      encoding: "utf8",
      stdio: ["ignore", "pipe", "pipe"],
      env: { ...env, DXMSG_PREFLIGHT_ACTIVE: "1" },
      timeout: PREFLIGHT_TIMEOUT_MS,
      killSignal: "SIGTERM",
      maxBuffer: 16 * 1024 * 1024
    }
  );

  // Prefer the report whenever preflight emitted parseable JSON (the
  // authoritative signal), even if spawnSync also flagged a non-fatal condition
  // such as a large captured stderr. When the child timed out (SIGTERM /
  // ETIMEDOUT), could not spawn, or produced no parseable report, fall through
  // to null so the caller fails open -- the native pre-push hook still gates.
  if (!result) {
    return null;
  }

  const stdout = result.stdout ? String(result.stdout) : "";
  // preflight prints pure JSON on stdout (progress/recovery go to stderr), but
  // tolerate leading noise by extracting the first balanced JSON object.
  const start = stdout.indexOf("{");
  if (start === -1) {
    return null;
  }
  try {
    return JSON.parse(stdout.slice(start));
  } catch (_error) {
    return null;
  }
}

/**
 * Main hook entry. Reads the PreToolUse payload, decides whether to run
 * preflight, and emits the decision JSON. Always exits 0 (a PreToolUse guard
 * communicates via `permissionDecision`, never via a non-zero exit).
 *
 * @param {string} stdinPayload Raw PreToolUse JSON from stdin.
 * @param {object} [deps] Injected dependencies for tests.
 * @returns {number} Process exit code (always 0).
 */
function run(stdinPayload, deps = {}) {
  const { env = process.env, repoRoot = resolveRepoRoot() } = deps;

  let event;
  try {
    event = JSON.parse(stdinPayload);
  } catch (_error) {
    return 0;
  }

  if (!event || event.tool_name !== "Bash") {
    return 0;
  }

  if (shouldSkip(env)) {
    return 0;
  }

  const toolInput =
    event.tool_input && typeof event.tool_input === "object" ? event.tool_input : {};
  if (!commandLooksLikeGitPush(toolInput.command)) {
    return 0;
  }

  const cspellResult = runChangedCspellGuard(repoRoot, deps);
  if (cspellResult.kind === "checks-failed") {
    process.stdout.write(`${JSON.stringify(buildCspellDecision(cspellResult))}\n`);
    return 0;
  }

  const report = runGuardPreflight(repoRoot, deps);

  // Fail open when preflight itself could not be spawned/parsed: emit an allow
  // (the native pre-push hook is the guarantee). This is an infra condition, not
  // a check failure.
  const status =
    report && report.status && typeof report.status === "object"
      ? report.status
      : { kind: "infra-unavailable", failures: [], policyFailures: [], warnings: [] };

  const decision = buildDecision(status);
  process.stdout.write(`${JSON.stringify(decision)}\n`);
  return 0;
}

module.exports = {
  HOOK_EVENT_NAME,
  REMEDIATION,
  CSPELL_REMEDIATION,
  resolveRepoRoot,
  shouldSkip,
  commandLooksLikeGitPush,
  filterCspellFiles,
  collectTrackedFiles,
  needsTrackedFallback,
  readGitHeadFile,
  writeCspellFileList,
  runChangedCspellGuard,
  buildCspellDecision,
  buildDecision,
  runGuardPreflight,
  run
};

if (require.main === module) {
  readStdin().then((payload) => {
    process.exit(run(payload));
  });
}
