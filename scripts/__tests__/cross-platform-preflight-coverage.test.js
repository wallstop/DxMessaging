/**
 * @fileoverview Self-maintaining completeness guard for the cross-OS targeted
 * regression gate in .github/workflows/cross-platform-preflight.yml.
 *
 * WHY THIS EXISTS: that workflow runs a FAST, FIRST step
 * ("Run cross-platform spawn + host-env hermeticity regression suite") that
 * executes a hand-listed set of Jest files via
 * `node scripts/run-managed-jest.js --runTestsByPath <files>` on
 * ubuntu-latest + windows-latest + macos-latest. A regression in one of those
 * files then fails FAST with clear attribution instead of deep inside the long
 * full-preflight log. The list is hand-maintained, so it can rot two ways:
 *   (a) a new cross-platform regression test is added but never wired into the
 *       targeted step, so a Windows/macOS regression in it only surfaces (if at
 *       all) inside the full preflight; and
 *   (b) a listed path is renamed/removed/typo'd, leaving a stale entry that
 *       silently runs nothing.
 *
 * The fix is a MARKER, not a duplicate list. A test opts into the cross-OS
 * targeted gate by placing the literal token `@cross-platform-regression`
 * inside a COMMENT anywhere in its file (conventionally the file header). This
 * guard:
 *   1. Scans every scripts/**\/__tests__/*.test.js (both scripts/__tests__ and
 *      scripts/lib/__tests__) for the marker (only when it appears in a
 *      comment, never in a string), then asserts each marked file's
 *      repo-relative POSIX path appears inside the targeted step's
 *      `--runTestsByPath` block. (closes gap (a))
 *   2. Asserts every path listed in that block EXISTS on disk and is itself
 *      marked, so the list and the markers stay in sync both directions.
 *      (closes gap (b))
 *   3. Asserts the workflow's job matrix still contains ubuntu-latest AND
 *      windows-latest AND macos-latest, so an OS cannot be silently dropped
 *      from the cross-OS gate.
 *
 * The marker set is INTENTIONALLY curated to the fast-attribution subset, NOT
 * "every cross-platform test". The second workflow step (`npm run
 * preflight:pre-push`, the "Run pre-push preflight" step) already runs the
 * ENTIRE Jest suite on all three OSes, so an unmarked platform-divergent test
 * still gets full cross-OS coverage there. The marker only PROMOTES a test into
 * the first, targeted step so a regression in it fails fast with clear
 * attribution instead of deep inside the long combined log.
 *
 * Robustness: Node stdlib only, no shell-outs. Files are read CRLF/BOM-safe
 * (normalizeToLf + a BOM strip) and compared via POSIX-normalized
 * repo-relative paths. The marker is matched ONLY inside real comment SPANS via
 * `extractCommentsOnly` -- the inverse projection of stripJsCommentsAndStrings
 * that shares the SAME single-pass tokenizer: it preserves comment payloads
 * (line / block / JSDoc, multi-line, with OR without a leading `*`) verbatim and
 * blanks code + string/template payloads to spaces. A marker that survives that
 * projection lived in a genuine comment, never in a string (so `"a // b
 * @marker"` is correctly NOT a marker) and never on a starless block-comment
 * continuation line that an old per-line heuristic would have missed. All
 * regexes are literal-substring / linear (ReDoS-free). THIS guard file is
 * allow-listed so its own definition/self-test fixtures of the marker token do
 * not count as an opt-in.
 *
 * How to mutation-test this guard (do this when you touch it):
 *   1. Remove one listed path from the targeted step in
 *      cross-platform-preflight.yml -> this suite FAILS naming that file ->
 *      restore -> GREEN.
 *   2. Add the marker to a scratch fixture string fed to the pure detector (the
 *      self-tests below already do this) or remove an OS from the matrix
 *      in-place -> the matrix assertion FAILS -> restore -> GREEN.
 */

"use strict";

const fs = require("fs");
const path = require("path");
const { normalizeToLf } = require("../lib/quote-parser");
const { extractCommentsOnly } = require("../lib/source-stripping");

const REPO_ROOT = path.resolve(__dirname, "..", "..");
const SCRIPTS_ROOT = path.join(REPO_ROOT, "scripts");
const WORKFLOW_PATH = path.join(REPO_ROOT, ".github", "workflows", "cross-platform-preflight.yml");

// The opt-in token. A cross-platform regression test declares itself part of
// the cross-OS targeted gate by placing this literal inside a comment anywhere
// in the file (conventionally the header). Detection is comment-SPAN based, so
// any real comment context counts and string/code occurrences never do.
const MARKER = "@cross-platform-regression";

// The name of the targeted step whose `--runTestsByPath` block this guard
// audits. Used to locate the right run block in the workflow.
const TARGETED_STEP_NAME = "Run cross-platform spawn + host-env hermeticity regression suite";

// The OSes the cross-OS gate must keep covering.
const REQUIRED_MATRIX_OSES = ["ubuntu-latest", "windows-latest", "macos-latest"];

const WALK_SKIP_DIRS = new Set(["node_modules", ".git", ".venv", "__pycache__", "Temp"]);

// THIS guard file references the marker in its own JSDoc/self-tests as data, so
// it must not count as a real opt-in. Allow-listed by repo-relative POSIX path.
const ALLOW_LIST = new Set(["scripts/__tests__/cross-platform-preflight-coverage.test.js"]);

function readUtf8(absolutePath) {
  // normalizeToLf collapses CR/CRLF; strip a leading UTF-8 BOM too so the first
  // token is anchored correctly.
  return normalizeToLf(fs.readFileSync(absolutePath, "utf8")).replace(/^﻿/, "");
}

function toRepoPosixRelative(absolutePath) {
  return path.relative(REPO_ROOT, absolutePath).split(path.sep).join("/");
}

function listFilesRecursive(absoluteDir, predicate) {
  const out = [];
  if (!fs.existsSync(absoluteDir)) {
    return out;
  }

  const stack = [absoluteDir];
  while (stack.length > 0) {
    const dir = stack.pop();
    let entries;
    try {
      entries = fs.readdirSync(dir, { withFileTypes: true });
    } catch {
      continue;
    }

    for (const entry of entries) {
      if (entry.isDirectory()) {
        if (WALK_SKIP_DIRS.has(entry.name)) {
          continue;
        }
        stack.push(path.join(dir, entry.name));
        continue;
      }
      if (entry.isFile() && predicate(path.join(dir, entry.name))) {
        out.push(path.join(dir, entry.name));
      }
    }
  }

  return out;
}

function listTestFiles() {
  return listFilesRecursive(SCRIPTS_ROOT, (abs) => {
    if (!abs.endsWith(".test.js")) {
      return false;
    }
    return path.relative(SCRIPTS_ROOT, abs).split(path.sep).includes("__tests__");
  });
}

/**
 * Pure detector: is the marker present in this source inside a real COMMENT
 * span (anywhere in the file -- not in a string, template literal, or code)?
 *
 * Comment-SPAN detection, not a per-line heuristic. We project the source
 * through `extractCommentsOnly` (the inverse of `stripJsCommentsAndStrings`,
 * sharing the SAME single-pass tokenizer): comment payloads survive verbatim
 * while code and string/template payloads are blanked to spaces, with line
 * breaks preserved. A marker that appears in that projection therefore lived in
 * a genuine comment span -- line (`//`), block (`/* ... *\/`, multi-line, with
 * OR without a leading `*`), or JSDoc -- and NEVER in a string/template literal
 * or code. This closes both per-line failure modes the old heuristic had:
 *   - false NEGATIVE: a marker on a plain block-comment continuation line with
 *     no leading `*` and no `/*`/`//` on that line is now detected, because the
 *     tokenizer knows it is still inside the open block-comment span; and
 *   - false POSITIVE: a marker inside a string such as `"a // b @marker"` is
 *     NOT detected, because `//` inside a string is string content (blanked),
 *     not a comment opener.
 * Reusing the shared tokenizer means the marked-in-comment and stripped-in-code
 * views can never disagree about what a comment is. BOM is stripped and CRLF is
 * normalized to LF by the caller (`readUtf8`), so the projection is
 * BOM/CRLF-safe by construction.
 *
 * @param {string} rawSource - LF-normalized, BOM-stripped source.
 * @returns {boolean} True when the marker appears inside a comment span.
 */
function sourceHasMarkerInComment(rawSource) {
  if (typeof rawSource !== "string" || rawSource.indexOf(MARKER) === -1) {
    return false;
  }
  return extractCommentsOnly(rawSource).indexOf(MARKER) !== -1;
}

/**
 * Extract the body lines of the targeted step's `run:` block from the workflow
 * source. Returns the joined run-block text (the multi-line bash command),
 * or null when the named step / its run block cannot be found.
 *
 * @param {string} rawWorkflow - LF-normalized workflow source.
 * @param {string} stepName - The `name:` value of the targeted step.
 * @returns {string|null}
 */
function extractTargetedStepRunBlock(rawWorkflow, stepName) {
  const lines = rawWorkflow.split("\n");

  // Locate the step header line: `- name: <stepName>` (the marker step is a
  // list item, so the name line begins with `- name:` after indentation).
  let nameLineIndex = -1;
  for (let i = 0; i < lines.length; i++) {
    const m = /^\s*-\s+name:\s*(.+?)\s*$/.exec(lines[i]);
    if (m && m[1].replace(/^["']|["']$/g, "") === stepName) {
      nameLineIndex = i;
      break;
    }
  }
  if (nameLineIndex === -1) {
    return null;
  }

  // The step ends at the next sibling list item (a line at the same `- `
  // indentation) or end of file. Find the run block within that window.
  const nameIndent = lines[nameLineIndex].length - lines[nameLineIndex].trimStart().length;
  let stepEnd = lines.length - 1;
  for (let i = nameLineIndex + 1; i < lines.length; i++) {
    const trimmed = lines[i].trim();
    if (trimmed.length === 0) {
      continue;
    }
    const indent = lines[i].length - lines[i].trimStart().length;
    if (indent <= nameIndent && /^-\s+/.test(trimmed)) {
      stepEnd = i - 1;
      break;
    }
    if (indent < nameIndent) {
      stepEnd = i - 1;
      break;
    }
  }

  // Find `run: |` (block scalar) within the step window, then collect its
  // indented body lines.
  for (let i = nameLineIndex + 1; i <= stepEnd; i++) {
    const runMatch = /^(\s*)run:\s*[|>][+-]?\s*$/.exec(lines[i]);
    if (!runMatch) {
      continue;
    }
    const runIndent = runMatch[1].length;
    const body = [];
    for (let j = i + 1; j <= stepEnd; j++) {
      const bodyLine = lines[j];
      const bodyTrimmed = bodyLine.trim();
      if (bodyTrimmed.length === 0) {
        body.push("");
        continue;
      }
      const bodyIndent = bodyLine.length - bodyLine.trimStart().length;
      if (bodyIndent <= runIndent) {
        break;
      }
      body.push(bodyLine);
    }
    return body.join("\n");
  }

  return null;
}

// A test-path token shape: a repo-relative POSIX path ending in `.test.js`.
// Linear / literal-class (ReDoS-free).
const TEST_PATH_TOKEN = /^scripts\/[\w./-]*\.test\.js$/;

/**
 * Parse the `--runTestsByPath` arguments out of the targeted run block as a
 * STRICT backslash-continued shell command, so the guard independently catches
 * a dropped continuation (truncation) rather than relying solely on
 * `set -euo pipefail` failing in CI.
 *
 * Continuation rule (matches bash): a line CONTINUES the command iff it ends in
 * a backslash that is the LAST character on the line, with at most a single
 * optional space before it (` \` or `\`). A trailing space AFTER the backslash
 * (`\ `) is NOT a bash line-continuation -- bash reads `\<space>` as an escaped
 * space and ends the logical line -- so we treat it as MALFORMED and throw,
 * matching what CI would do. (Documented design choice: trailing-space-after-
 * backslash is a hard error, not silently normalized.)
 *
 * Contiguity rule: starting from the `--runTestsByPath` line, every line up to
 * and including the last path line must be backslash-continued EXCEPT the final
 * path line, which must NOT continue. A non-final path line missing its
 * continuation means the argument list was truncated -- we throw an actionable
 * error naming the offending line so the failure points at the workflow, not at
 * a mysterious short path list.
 *
 * Returns repo-relative POSIX paths in listed order (deduped).
 *
 * @param {string} runBlock - The targeted step's run-block text.
 * @returns {string[]}
 * @throws {Error} when a continuation is malformed or the list is truncated.
 */
function extractListedTestPaths(runBlock) {
  if (typeof runBlock !== "string" || runBlock.length === 0) {
    return [];
  }

  const lines = runBlock.split("\n");

  // Classify a line's continuation status without flattening first, so we can
  // detect truncation. Returns { content, continues } or { malformed: true }.
  //   - valid continuation: line's last non-newline char is `\`
  //   - malformed: a `\` followed by trailing space(s) (bash reads `\<space>`
  //     as an escaped space, NOT a line continuation -- a hard error here)
  //   - no continuation: line does not end in a backslash
  // Implemented with endsWith / indexOf scans (no backtracking) to stay
  // ReDoS-free.
  function classify(line) {
    // Strip a single trailing CR defensively (caller already normalizes CRLF).
    const body = line.endsWith("\r") ? line.slice(0, -1) : line;
    if (body.endsWith("\\")) {
      // Last char is a backslash -> a real bash continuation. Content is
      // everything before it, with one optional separating space trimmed.
      const withoutSlash = body.slice(0, -1);
      const content = withoutSlash.endsWith(" ") ? withoutSlash.slice(0, -1) : withoutSlash;
      return { content, continues: true };
    }
    // A backslash followed only by trailing spaces => malformed (escaped space).
    // Reverse-scan the trailing horizontal whitespace (single linear pass, no
    // regex backtracking) so this stays genuinely ReDoS-free per the contract
    // above, even on a pathological all-whitespace line.
    let end = body.length;
    while (end > 0) {
      const ch = body[end - 1];
      if (ch === " " || ch === "\t" || ch === "\f" || ch === "\v" || ch === "\r") {
        end--;
      } else {
        break;
      }
    }
    const trimmedRight = body.slice(0, end);
    if (trimmedRight.endsWith("\\") && trimmedRight.length < body.length) {
      return { malformed: true };
    }
    return { content: body, continues: false };
  }

  // Locate the `--runTestsByPath` line; the path list begins there.
  let startIndex = -1;
  for (let i = 0; i < lines.length; i++) {
    if (lines[i].includes("--runTestsByPath")) {
      startIndex = i;
      break;
    }
  }
  if (startIndex === -1) {
    // No targeted invocation found -- fall back to scanning every line for
    // path tokens (defensive; the membership checks will still flag drift).
    return scanTestPathTokens(lines);
  }

  // The `--runTestsByPath` line itself must continue (the paths follow on the
  // next lines). If it does not, the command has no path arguments.
  const startClassified = classify(lines[startIndex]);
  if (startClassified.malformed) {
    throw new Error(
      "Malformed line continuation (trailing space after `\\`) in the targeted " +
        "regression step of .github/workflows/cross-platform-preflight.yml on the " +
        `--runTestsByPath line:\n  ${lines[startIndex]}`
    );
  }
  const seen = new Set();
  const paths = [];

  const collectFrom = (content) => {
    for (const token of content.split(/\s+/)) {
      if (TEST_PATH_TOKEN.test(token) && !seen.has(token)) {
        seen.add(token);
        paths.push(token);
      }
    }
  };

  // Same-line paths (rare) plus walk the contiguous, backslash-continued lines.
  collectFrom(startClassified.content);

  // `endIndex` is the index of the last line that belongs to the contiguous
  // command (inclusive). Lines after it are the command's "tail" -- they must
  // contain NO test-path tokens, or the list was truncated mid-stream.
  let endIndex = startIndex;
  let continuing = startClassified.continues;

  while (continuing && endIndex + 1 < lines.length) {
    const i = endIndex + 1;
    const line = lines[i];
    const classified = classify(line);
    if (classified.malformed) {
      throw new Error(
        "Malformed line continuation (trailing space after `\\`) in the targeted " +
          "regression step of .github/workflows/cross-platform-preflight.yml:\n" +
          `  ${line}\n` +
          "A bash line-continuation backslash must be the last character on the line."
      );
    }
    collectFrom(classified.content);
    endIndex = i;
    continuing = classified.continues;
  }

  // The contiguous command ended at `endIndex`. If any later line still carries
  // a test-path token, a continuation was dropped earlier and bash would have
  // split the command -- the list is truncated. Fail loudly and point at it.
  for (let i = endIndex + 1; i < lines.length; i++) {
    const orphan = lines[i].split(/\s+/).find((t) => TEST_PATH_TOKEN.test(t));
    if (orphan) {
      throw new Error(
        "Truncated --runTestsByPath list in the targeted regression step of " +
          ".github/workflows/cross-platform-preflight.yml: a path line is missing " +
          "its trailing `\\` continuation, so the shell command terminated early " +
          "and these path token(s) were orphaned:\n" +
          `  ${lines[i].trim()}\n` +
          "Every path line except the last must end in ` \\`."
      );
    }
  }

  return paths;
}

/**
 * Defensive fallback: scan arbitrary lines for repo-relative test-path tokens.
 * Used only when the `--runTestsByPath` anchor cannot be located.
 *
 * @param {string[]} lines
 * @returns {string[]}
 */
function scanTestPathTokens(lines) {
  const seen = new Set();
  const paths = [];
  for (const line of lines) {
    for (const token of line.split(/\s+/)) {
      if (TEST_PATH_TOKEN.test(token) && !seen.has(token)) {
        seen.add(token);
        paths.push(token);
      }
    }
  }
  return paths;
}

/**
 * Strip a YAML trailing line comment (` # ...`) while ignoring `#` inside
 * single/double quotes. Linear scan, ReDoS-free.
 *
 * @param {string} text
 * @returns {string}
 */
function stripYamlInlineComment(text) {
  let quote = null;
  for (let i = 0; i < text.length; i++) {
    const ch = text[i];
    if (quote) {
      if (ch === quote) {
        quote = null;
      }
      continue;
    }
    if (ch === '"' || ch === "'") {
      quote = ch;
      continue;
    }
    if (ch === "#") {
      // A `#` starts a comment only when preceded by whitespace or at line
      // start (YAML rule), which is always true for our trailing-comment case.
      if (i === 0 || /\s/.test(text[i - 1])) {
        return text.slice(0, i);
      }
    }
  }
  return text;
}

function unquoteYamlScalar(value) {
  const v = value.trim();
  if ((v.startsWith('"') && v.endsWith('"')) || (v.startsWith("'") && v.endsWith("'"))) {
    return v.slice(1, -1);
  }
  return v;
}

/**
 * Extract the matrix OS entries from the workflow. Scoped to a matrix block
 * (an `os:` key nested under `strategy:` -> `matrix:`) so an unrelated `os:`
 * elsewhere in the file cannot merge in. Handles BOTH YAML forms:
 *   - block sequence:  `os:` then indented `- ubuntu-latest` lines
 *   - flow sequence:   `os: [ubuntu-latest, windows-latest, macos-latest]`
 * Trailing `# comments` are ignored in both forms. Errs safe: if no matrix-
 * scoped `os:` is found the result is empty, which makes the OS-coverage
 * assertion RED (a missing/renamed matrix is a failure, not a silent pass).
 *
 * @param {string} rawWorkflow - LF-normalized workflow source.
 * @returns {Set<string>}
 */
function extractMatrixOses(rawWorkflow) {
  const lines = rawWorkflow.split("\n");
  const oses = new Set();

  // Track whether we are inside a `strategy:` -> `matrix:` scope so a stray
  // top-level/other `os:` cannot pollute the set. matrixIndent is the indent of
  // the `matrix:` key; its children are more deeply indented.
  let matrixIndent = -1;
  let inOsBlock = false;
  let osIndent = -1;

  const indentOf = (line) => line.length - line.trimStart().length;

  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];
    const indent = indentOf(line);
    const trimmedFull = line.trim();
    if (trimmedFull.length === 0 || trimmedFull.startsWith("#")) {
      continue;
    }
    const trimmed = stripYamlInlineComment(line).trim();

    // Enter a matrix scope on `matrix:` (it lives under `strategy:`; we anchor
    // on `matrix:` directly which is sufficient and robust to formatting).
    if (/^matrix:\s*$/.test(trimmed) || /^matrix:\s*#/.test(trimmedFull)) {
      matrixIndent = indent;
      inOsBlock = false;
      osIndent = -1;
      continue;
    }

    // Leaving the matrix scope: a key at or below matrixIndent that is not a
    // matrix child closes it (unless we are still consuming an os block).
    if (matrixIndent !== -1 && indent <= matrixIndent && !inOsBlock) {
      matrixIndent = -1;
    }

    if (matrixIndent === -1) {
      continue;
    }

    if (!inOsBlock) {
      // Flow form: `os: [a, b, c]`
      const flow = /^os:\s*\[(.*)\]\s*$/.exec(trimmed);
      if (flow) {
        for (const part of flow[1].split(",")) {
          const value = unquoteYamlScalar(part);
          if (value.length > 0) {
            oses.add(value);
          }
        }
        continue;
      }
      // Block form: bare `os:` then `- <name>` children.
      if (/^os:\s*$/.test(trimmed)) {
        inOsBlock = true;
        osIndent = indent;
      }
      continue;
    }

    // Inside a block-form os list: collect more-indented `- name` entries.
    if (indent > osIndent && /^-\s+/.test(trimmed)) {
      const value = unquoteYamlScalar(trimmed.replace(/^-\s+/, ""));
      if (value.length > 0) {
        oses.add(value);
      }
      continue;
    }

    // Dedent at/below the `os:` key closes the block. Re-evaluate this line for
    // matrix-scope exit on the next iteration by stepping back one index.
    if (indent <= osIndent) {
      inOsBlock = false;
      osIndent = -1;
      i--;
    }
  }

  return oses;
}

// ---------------------------------------------------------------------------
// Shared fixtures: read the workflow + scan the marked files ONCE.
// ---------------------------------------------------------------------------
const workflowSource = readUtf8(WORKFLOW_PATH);
const targetedRunBlock = extractTargetedStepRunBlock(workflowSource, TARGETED_STEP_NAME);

// Parse the path list defensively: a malformed/truncated continuation makes
// `extractListedTestPaths` throw (Finding 3). Capture that here so it surfaces
// as a deterministic, named TEST FAILURE rather than crashing the whole file at
// module-load time (which would report "0 tests" with no actionable assertion).
let listedPaths = [];
let listedParseError = null;
try {
  listedPaths = extractListedTestPaths(targetedRunBlock || "");
} catch (err) {
  listedParseError = err;
}
const listedSet = new Set(listedPaths);

function collectMarkedTestPaths() {
  const marked = [];
  for (const abs of listTestFiles()) {
    const rel = toRepoPosixRelative(abs);
    if (ALLOW_LIST.has(rel)) {
      continue;
    }
    const raw = readUtf8(abs);
    if (sourceHasMarkerInComment(raw)) {
      marked.push(rel);
    }
  }
  return marked;
}

// Guard the suite registration so the module can be `require()`d for its pure
// helpers OUTSIDE Jest (where `describe`/`test`/`expect` are undefined) without
// throwing at load time. Under Jest this runs exactly as before. (Finding 7)
// We register the suite via `maybeDescribe`, which is the real `describe` under
// Jest and a no-op otherwise, so the body's indentation is unchanged.
const maybeDescribe = typeof describe === "function" ? describe : () => {};

maybeDescribe("cross-platform-preflight targeted-gate coverage", () => {
  test("the targeted regression step exists and parses to a non-empty list", () => {
    if (listedParseError) {
      throw listedParseError;
    }
    expect(targetedRunBlock).not.toBeNull();
    expect(listedPaths.length).toBeGreaterThan(0);
  });

  test("every @cross-platform-regression-marked test is in the targeted step", () => {
    const marked = collectMarkedTestPaths();
    expect(marked.length).toBeGreaterThan(0);

    const missing = marked.filter((rel) => !listedSet.has(rel));
    if (missing.length > 0) {
      const details = missing.map((rel) => `  ${rel}`).join("\n");
      throw new Error(
        "Marked cross-platform regression test(s) are not gated on win+mac:\n" +
          details +
          "\nAdd each to the targeted regression step in " +
          ".github/workflows/cross-platform-preflight.yml " +
          `(the "${TARGETED_STEP_NAME}" step's --runTestsByPath list).`
      );
    }
  });

  test("every test listed in the targeted step exists on disk", () => {
    const missingOnDisk = listedPaths.filter((rel) => !fs.existsSync(path.join(REPO_ROOT, rel)));
    if (missingOnDisk.length > 0) {
      const details = missingOnDisk.map((rel) => `  ${rel}`).join("\n");
      throw new Error(
        "Stale or mistyped entries in the targeted regression step of " +
          ".github/workflows/cross-platform-preflight.yml -- these paths do not " +
          "exist on disk:\n" +
          details
      );
    }
  });

  test("every test listed in the targeted step carries the marker", () => {
    // Bidirectional sync: a path in the list that lost its marker (or was added
    // without one) is a drift signal -- the list and the markers must agree.
    const marked = new Set(collectMarkedTestPaths());
    const unmarked = listedPaths.filter((rel) => !marked.has(rel));
    if (unmarked.length > 0) {
      const details = unmarked.map((rel) => `  ${rel}`).join("\n");
      throw new Error(
        "Test(s) listed in the targeted regression step of " +
          ".github/workflows/cross-platform-preflight.yml are missing the " +
          `${MARKER} marker comment:\n` +
          details +
          `\nAdd a header comment containing ${MARKER} so the list and the ` +
          "markers stay in sync."
      );
    }
  });

  test("the cross-OS matrix still covers ubuntu, windows, and macos", () => {
    const oses = extractMatrixOses(workflowSource);
    for (const required of REQUIRED_MATRIX_OSES) {
      expect(oses.has(required)).toBe(true);
    }
  });

  // -------------------------------------------------------------------------
  // Self-tests: prove the pure detectors fire on the exact shapes and do NOT
  // false-positive, WITHOUT writing real repo files.
  // -------------------------------------------------------------------------
  describe("detector self-tests", () => {
    // -- sourceHasMarkerInComment: comment-SPAN detection (Findings 1 & 2) ----
    test("flags a JSDoc-body marker (leading star)", () => {
      const src = `/**\n * ${MARKER}: gated cross-OS.\n */\n"use strict";\n`;
      expect(sourceHasMarkerInComment(src)).toBe(true);
    });

    test("flags a // line-comment marker", () => {
      const src = `"use strict";\n// ${MARKER} -- runs on win+mac\nconst x = 1;\n`;
      expect(sourceHasMarkerInComment(src)).toBe(true);
    });

    test("flags a marker on a PLAIN block-comment continuation line (no leading star) -- Finding 1", () => {
      // The old per-line heuristic missed this and silently treated the file
      // as unmarked, letting it escape the gate.
      const src = `/*\n   ${MARKER}\n*/\n"use strict";\n`;
      expect(sourceHasMarkerInComment(src)).toBe(true);
    });

    test("flags a marker in INDENTED block-comment prose (no leading star)", () => {
      const src = `/*\n      see ${MARKER} for the gated set\n*/\n`;
      expect(sourceHasMarkerInComment(src)).toBe(true);
    });

    test("does NOT flag the marker inside a double/single/template string", () => {
      expect(sourceHasMarkerInComment(`const a = "${MARKER}";\n`)).toBe(false);
      expect(sourceHasMarkerInComment(`const a = '${MARKER}';\n`)).toBe(false);
      expect(sourceHasMarkerInComment(`const a = \`${MARKER}\`;\n`)).toBe(false);
    });

    test("does NOT flag a marker in a string that contains `//` or `/*` -- Finding 2", () => {
      // The old per-line heuristic saw the `//` / `/*` earlier in the string
      // and wrongly counted these as comment markers.
      expect(sourceHasMarkerInComment(`const u = "a // b ${MARKER}";\n`)).toBe(false);
      expect(sourceHasMarkerInComment(`const u = "a /* b ${MARKER}";\n`)).toBe(false);
    });

    test("does NOT flag the marker as a bare code identifier", () => {
      // `@cross-platform-regression` is not a valid bare identifier, but the
      // detector must still reject any non-comment occurrence; emulate a code
      // hit by checking a marker spliced into code via a property-ish token.
      const src = `const obj = { x: 1 }; obj["${MARKER}"];\n`;
      expect(sourceHasMarkerInComment(src)).toBe(false);
    });

    test("does NOT flag a file without the marker", () => {
      const src = `/**\n * @fileoverview Some unrelated test.\n */\n`;
      expect(sourceHasMarkerInComment(src)).toBe(false);
    });

    test("is BOM + CRLF safe (block comment with CRLF and a BOM)", () => {
      // readUtf8 normalizes CRLF -> LF and strips the BOM; emulate that here.
      const src = normalizeToLf(`﻿/*\r\n   ${MARKER}\r\n*/\r\n`).replace(/^﻿/, "");
      expect(sourceHasMarkerInComment(src)).toBe(true);
    });

    // -- existing desync directions ------------------------------------------
    test("a marked-but-unlisted file is detected by the listed-set check", () => {
      const marked = ["scripts/__tests__/imaginary-new-regression.test.js"];
      const missing = marked.filter((rel) => !listedSet.has(rel));
      expect(missing).toEqual(["scripts/__tests__/imaginary-new-regression.test.js"]);
    });

    test("a listed-but-missing-file is detected by the on-disk check", () => {
      const fakeListed = ["scripts/__tests__/does-not-exist.test.js"];
      const missingOnDisk = fakeListed.filter((rel) => !fs.existsSync(path.join(REPO_ROOT, rel)));
      expect(missingOnDisk).toEqual(["scripts/__tests__/does-not-exist.test.js"]);
    });

    // -- extractListedTestPaths: strict continuation parsing (Finding 3) ------
    test("happy path: contiguous backslash-continued list parses cleanly", () => {
      const block =
        "          set -euo pipefail\n" +
        "          node scripts/run-managed-jest.js --runTestsByPath \\\n" +
        "            scripts/__tests__/a.test.js \\\n" +
        "            scripts/lib/__tests__/b.test.js";
      expect(extractListedTestPaths(block)).toEqual([
        "scripts/__tests__/a.test.js",
        "scripts/lib/__tests__/b.test.js"
      ]);
    });

    test("a DROPPED continuation backslash (truncation) is detected -- Finding 3", () => {
      const block =
        "          node scripts/run-managed-jest.js --runTestsByPath \\\n" +
        "            scripts/__tests__/a.test.js\n" + // <-- backslash dropped
        "            scripts/__tests__/b.test.js \\\n" +
        "            scripts/__tests__/c.test.js";
      expect(() => extractListedTestPaths(block)).toThrow(/Truncated --runTestsByPath/);
    });

    test("a TRAILING SPACE after a continuation backslash is a hard error -- Finding 3", () => {
      // Documented design: `\ ` (backslash + trailing space) is an escaped
      // space in bash, NOT a line continuation -> we throw, matching CI.
      const block =
        "          node scripts/run-managed-jest.js --runTestsByPath \\ \n" +
        "            scripts/__tests__/a.test.js";
      expect(() => extractListedTestPaths(block)).toThrow(/Malformed line continuation/);
    });

    // -- extractMatrixOses: block + flow + comments + scoping (Finding 4) -----
    test("parses the BLOCK-sequence os list", () => {
      const wf =
        "    strategy:\n" +
        "      matrix:\n" +
        "        os:\n" +
        "          - ubuntu-latest\n" +
        "          - windows-latest\n" +
        "          - macos-latest\n" +
        "    steps:\n";
      const oses = extractMatrixOses(wf);
      for (const required of REQUIRED_MATRIX_OSES) {
        expect(oses.has(required)).toBe(true);
      }
    });

    test("parses the FLOW-sequence os list", () => {
      const wf =
        "    strategy:\n" +
        "      matrix:\n" +
        "        os: [ubuntu-latest, windows-latest, macos-latest]\n" +
        "    steps:\n";
      const oses = extractMatrixOses(wf);
      for (const required of REQUIRED_MATRIX_OSES) {
        expect(oses.has(required)).toBe(true);
      }
    });

    test("ignores a trailing comment on the os line and entries", () => {
      const wf =
        "    strategy:\n" +
        "      matrix:\n" +
        "        os: # the cross-OS gate\n" +
        "          - ubuntu-latest\n" +
        "          - windows-latest # win-only regressions\n" +
        "          - macos-latest\n" +
        "    steps:\n";
      const oses = extractMatrixOses(wf);
      for (const required of REQUIRED_MATRIX_OSES) {
        expect(oses.has(required)).toBe(true);
      }
    });

    test("an UNRELATED os: outside the matrix scope is NOT merged in", () => {
      const wf =
        "env:\n" +
        "  os: some-unrelated-value\n" +
        "    strategy:\n" +
        "      matrix:\n" +
        "        os:\n" +
        "          - ubuntu-latest\n" +
        "          - windows-latest\n" +
        "          - macos-latest\n";
      const oses = extractMatrixOses(wf);
      expect(oses.has("some-unrelated-value")).toBe(false);
      for (const required of REQUIRED_MATRIX_OSES) {
        expect(oses.has(required)).toBe(true);
      }
    });

    test("each required OS removed -> the matrix assertion goes RED (block form)", () => {
      for (const dropped of REQUIRED_MATRIX_OSES) {
        const kept = REQUIRED_MATRIX_OSES.filter((o) => o !== dropped);
        const wf =
          "    strategy:\n" +
          "      matrix:\n" +
          "        os:\n" +
          kept.map((o) => `          - ${o}\n`).join("");
        const oses = extractMatrixOses(wf);
        expect(oses.has(dropped)).toBe(false);
      }
    });

    test("each required OS removed -> the matrix assertion goes RED (flow form)", () => {
      for (const dropped of REQUIRED_MATRIX_OSES) {
        const kept = REQUIRED_MATRIX_OSES.filter((o) => o !== dropped);
        const wf = "    strategy:\n" + "      matrix:\n" + `        os: [${kept.join(", ")}]\n`;
        const oses = extractMatrixOses(wf);
        expect(oses.has(dropped)).toBe(false);
      }
    });
  });
});

if (typeof module !== "undefined" && module.exports) {
  module.exports = {
    MARKER,
    TARGETED_STEP_NAME,
    REQUIRED_MATRIX_OSES,
    sourceHasMarkerInComment,
    extractTargetedStepRunBlock,
    extractListedTestPaths,
    extractMatrixOses
  };
}
