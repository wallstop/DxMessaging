"use strict";

/**
 * path-classifier.js
 *
 * Pure helpers for resolving and classifying filesystem paths during managed
 * Jest/Prettier/cspell self-heal flows. The integrity gate (and several
 * tier-level decisions) need to answer two questions cheaply:
 *
 *   1. "Is this resolved path inside a particular directory?" Used to refuse
 *      cache-reset against the repo's node_modules, and to refuse repo-wide
 *      repair against an isolated cache subtree.
 *   2. "Does a captured runner path belong to the repo, the isolated cache,
 *      or neither?" Used to choose between npm-ci and isolated-cache-reset
 *      recoveries.
 *
 * No side effects at module load; every function is pure modulo the
 * fs.realpathSync probe inside `normalizeForPathComparison` (which is the
 * existing production behavior preserved verbatim).
 */

const fs = require("fs");
const path = require("path");

const PATH_CLASS_REPO = "repo";
const PATH_CLASS_ISOLATED = "isolated";
const PATH_CLASS_UNKNOWN = "unknown";

/**
 * Resolve a path to an absolute, OS-canonical, symlink-followed form suitable
 * for prefix/inside-of comparison. On Windows, the comparison is
 * case-insensitive (lowercased). On POSIX, the comparison is case-sensitive.
 *
 * If `fs.realpathSync` fails (e.g. the target does not exist), the resolved
 * path is returned without realpath resolution; callers handle existence
 * separately. This mirrors the original `run-managed-jest.js` implementation.
 *
 * @param {string} targetPath Path to normalize.
 * @returns {string} Normalized absolute path.
 */
function normalizeForPathComparison(targetPath) {
  let resolved = path.resolve(targetPath);
  try {
    resolved = fs.realpathSync.native
      ? fs.realpathSync.native(resolved)
      : fs.realpathSync(resolved);
  } catch {
    // Keep resolved path when target is unavailable; callers handle existence separately.
  }
  return process.platform === "win32" ? resolved.toLowerCase() : resolved;
}

/**
 * Return true when `filePath` is `directoryPath` itself or a descendant of it.
 * Comparison is symlink-resolved and case-folded on Windows (see
 * `normalizeForPathComparison`).
 *
 * @param {string} filePath Path under test.
 * @param {string} directoryPath Candidate parent directory.
 * @returns {boolean}
 */
function isPathInsideDirectory(filePath, directoryPath) {
  const normalizedFilePath = normalizeForPathComparison(filePath);
  const normalizedDirectoryPath = normalizeForPathComparison(directoryPath);
  const relativePath = path.relative(normalizedDirectoryPath, normalizedFilePath);
  return relativePath === "" || (!relativePath.startsWith("..") && !path.isAbsolute(relativePath));
}

/**
 * Classify a captured runner/module path into one of three buckets:
 *   - "repo"     - the path lives under the repository node_modules tree.
 *   - "isolated" - the path lives under the isolated managed-Jest cache root.
 *   - "unknown"  - the path is null/undefined, empty, non-string, or lives
 *                  outside both trees.
 *
 * The repo path takes precedence over isolated when both options exist (the
 * repo's node_modules is rarely placed under the isolated cache root in
 * practice, but defense-in-depth: if it ever is, the repo-tier recovery is
 * the correct first attempt).
 *
 * @param {string|null|undefined} capturedPath Path observed in stderr (or
 *   resolved by the wrapper).
 * @param {object} bounds Bucket boundaries.
 * @param {string} bounds.repoNodeModules Absolute path to the repo's
 *   node_modules directory.
 * @param {string} bounds.isolatedCacheRoot Absolute path to the isolated
 *   managed-Jest cache root.
 * @returns {"repo"|"isolated"|"unknown"}
 */
function classifyCapturedPath(capturedPath, { repoNodeModules, isolatedCacheRoot } = {}) {
  if (typeof capturedPath !== "string" || capturedPath.length === 0) {
    return PATH_CLASS_UNKNOWN;
  }
  if (typeof repoNodeModules === "string" && repoNodeModules.length > 0) {
    if (isPathInsideDirectory(capturedPath, repoNodeModules)) {
      return PATH_CLASS_REPO;
    }
  }
  if (typeof isolatedCacheRoot === "string" && isolatedCacheRoot.length > 0) {
    if (isPathInsideDirectory(capturedPath, isolatedCacheRoot)) {
      return PATH_CLASS_ISOLATED;
    }
  }
  return PATH_CLASS_UNKNOWN;
}

/**
 * Convert any path-like string to POSIX (forward-slash) separators.
 *
 * Idempotent on POSIX input. Does NOT resolve or normalize; pure separator
 * swap. Use for user-facing display strings and for cross-platform string
 * assertions where the comparison value is known in POSIX form.
 *
 * Null / undefined map to the empty string (`""`) so callers can use this
 * helper inside template literals without paying for runtime type narrowing
 * AND without leaking the strings `"null"` / `"undefined"` into log output
 * when the upstream value was unset. Non-null primitives (number, boolean)
 * are coerced via `String(value)` and then separator-swapped; this keeps
 * the helper resilient when a caller accidentally hands it a non-string.
 *
 * @param {*} value Path-like value (typically a string).
 * @returns {string} POSIX-separator form; `""` for null / undefined; the
 *   stringified-and-swapped form for other non-string inputs.
 */
function toPosixPath(value) {
  if (value === null || value === undefined) {
    return "";
  }
  if (typeof value !== "string") {
    return String(value).replace(/\\/g, "/");
  }
  return value.replace(/\\/g, "/");
}

/**
 * Repo-relative POSIX form of `absPath`.
 *
 * Falls back to the POSIX absolute form (via {@link toPosixPath}) when the
 * path lives outside `repoRoot` (i.e. `path.relative` returns a parent-
 * traversal or an absolute path on Windows for cross-drive inputs). Non-
 * string inputs are returned unchanged.
 *
 * Use this helper anywhere a user-facing log line names a path that is
 * "usually" inside the repo: the relative form is shorter and platform-
 * agnostic; the absolute fallback is still POSIX-normalized so log scrapers
 * never see backslashes.
 *
 * @param {*} absPath Absolute path-like value (typically a string).
 * @param {*} repoRoot Absolute repository root path.
 * @returns {*} POSIX-relative path when inside repo, POSIX-absolute fallback
 *   otherwise; original value when either input is not a string.
 */
function toRepoPosixRelative(absPath, repoRoot) {
  if (typeof absPath !== "string" || typeof repoRoot !== "string") {
    return absPath;
  }
  const rel = path.relative(repoRoot, absPath);
  if (rel === "" || rel.startsWith("..") || path.isAbsolute(rel)) {
    return toPosixPath(absPath);
  }
  return toPosixPath(rel);
}

module.exports = {
  PATH_CLASS_REPO,
  PATH_CLASS_ISOLATED,
  PATH_CLASS_UNKNOWN,
  normalizeForPathComparison,
  isPathInsideDirectory,
  classifyCapturedPath,
  toPosixPath,
  toRepoPosixRelative
};
