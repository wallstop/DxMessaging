#!/usr/bin/env node
"use strict";

/**
 * preflight-before-push-guard.js
 *
 * Claude Code PreToolUse hook. When an agent is about to run a `git push`
 * Bash command, this guard runs the change-aware preflight FIRST (the FULL
 * change-set: committed range + working tree, i.e. `--scope=full`) and, per the
 * repo owner's decision, BLOCKS the push when checks fail. This catches the
 * cheap-but-loud failures (lint / spelling / docs / changelog / YAML / policy)
 * before the multi-minute native pre-push hook runs, so the agent gets the
 * signal in-loop instead of mid-push.
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
 *   5. On a probable push: spawn
 *        node scripts/preflight.js --json --profile=guard --scope=full --no-recover
 *      with `DXMSG_PREFLIGHT_ACTIVE=1` in the child env. `--profile=guard` runs
 *      the fast subset (the heavy Jest suites are deferred to the native hook);
 *      `--scope=full` uses the committed range + working tree; `--no-recover`
 *      avoids paying integrity recovery twice in a session.
 *   6. {@link buildDecision} maps the preflight `status` to a PreToolUse
 *      decision:
 *        - `ok`               -> permissionDecision "allow" (native hook is the
 *                                final backstop).
 *        - `checks-failed`    -> "deny" naming the failing hook ids + the single
 *                                remediation.
 *        - any `policyFailures` (even under infra-unavailable) -> "deny" (policy
 *                                / security hooks never fail open).
 *        - `infra-unavailable` (no policyFailures) -> "allow" + a WARNING (do
 *                                not wedge the agent on a broken host).
 *   7. Emit the documented PreToolUse `hookSpecificOutput` JSON on stdout and
 *      exit 0. The guard NEVER relies on its exit code to block (that is
 *      PostToolUse semantics); if a CLI version ignores `deny`, the native
 *      pre-push hook still gates the push.
 *
 * All child-process spawns route through `spawnPlatformCommandSync`
 * (scripts/lib/shell-command.js); no raw spawn, no shell.
 */

const path = require("path");
const { spawnPlatformCommandSync } = require("../lib/shell-command");

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
 * The single remediation line shown to the agent on a blocked push. Names the
 * canonical reproduce/auto-fix entrypoint and reaffirms that git hooks are the
 * last-resort backstop, not the first signal.
 */
const REMEDIATION =
  "Run `npm run preflight` to reproduce and auto-fix, then retry the push. " +
  "Git hooks are the last-resort backstop, not the first signal.";

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
    "node",
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
  resolveRepoRoot,
  shouldSkip,
  commandLooksLikeGitPush,
  buildDecision,
  runGuardPreflight,
  run
};

if (require.main === module) {
  readStdin().then((payload) => {
    process.exit(run(payload));
  });
}
