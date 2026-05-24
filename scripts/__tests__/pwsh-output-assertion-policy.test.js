/**
 * @fileoverview Repository-wide policy guard that makes the ENTIRE category of
 * "fragile phrase assertion on RAW PowerShell-rendered output" impossible to
 * reintroduce (in its direct member-access, tainted-local-variable, and
 * renamed-merge-helper forms; see "Residual limits" for what remains uncovered).
 *
 * Background:
 *   When `pwsh -File <script>` lets an unhandled `throw` reach the top of the
 *   script, PowerShell's ConciseView error formatter WORD-WRAPS the message at
 *   the host console width, inserting a `\n     | ` continuation gutter (plus
 *   ANSI color escapes) between words. On the narrower Windows CI runner this
 *   splits an asserted phrase -- e.g. "outside the managed root" became
 *   "...outside the\n     | managed root." -- so a literal `.toContain(...)` on
 *   the raw stdout/stderr found nothing and the test FAILED on Windows only
 *   (it passed on Linux/macOS, where the default width kept the phrase together).
 *   The production message was CORRECT; the assertion was width-dependent.
 *
 *   The fix is scripts/lib/pwsh-output.js's `normalizePwshText`, which strips
 *   ANSI/OSC escapes, rejoins the gutter, and collapses whitespace before the
 *   assertion. This guard ensures every phrase/error substring assertion against
 *   pwsh output in a pwsh-spawning test file goes through that normalizer (or a
 *   recognized wrapper helper), so the regression is loud at pre-push time on
 *   EVERY host, not just Windows.
 *
 *   Policy -- In a test file that SPAWNS pwsh, a phrase assertion (a
 *   `.toContain(<multi-word string literal>)` / `.not.toContain(<multi-word
 *   string literal>)` / `.toMatch(<regex literal>)`) applied to a RAW
 *   pwsh-output receiver is forbidden. A "raw pwsh-output receiver" is either:
 *     (a) a MEMBER-ACCESS on a spawn result that ends in `.stdout` / `.stderr`
 *         (e.g. `result.stdout`, `out.stderr`, `run.stdout`), or a template
 *         literal that merges such members (e.g. `` `${out.stdout}\n${out.stderr}` ``);
 *         OR
 *     (b) a LOCAL VARIABLE bound to such a raw merge in the same file
 *         (`const combined = `${out.stdout}\n${out.stderr}`; ... expect(combined)`).
 *   The receiver is COMPLIANT when it is wrapped in `normalizePwshText(...)`, or
 *   is (or is bound to) a recognized normalizing wrapper helper -- by convention
 *   `combinedText(...)` / `stdoutText(...)`, which this repo defines as
 *   `normalizePwshText`-backed one-liners. A single-word `.toContain("token")`
 *   (no interior whitespace) is NOT a wrappable phrase -- a single token cannot
 *   be split by a space-driven word wrap -- so it is intentionally not flagged.
 *
 *   A SEPARATE rule closes the "renamed helper" door: the receiver check above
 *   recognizes the normalizing wrappers by NAME, so a differently-named helper
 *   whose body rebuilds the raw `${out.stdout}\n${out.stderr}` template merge
 *   WITHOUT `normalizePwshText` would launder the anti-pattern past it. The guard
 *   therefore also forbids DEFINING, in a pwsh-spawning test file, a `return`/
 *   arrow-implicit-return whose value is an un-normalized `.stdout`/`.stderr`
 *   template merge -- the merge is flagged at its definition, independent of how it
 *   is later called (see findRenamedMergeHelpers).
 *
 * How to mutation-test this guard (do this when you touch it):
 *   1. In a consumer test (e.g. unity-ensure-editor-il2cpp-idempotency.test.js),
 *      change `const combined = combinedText(out);` back to
 *      `const combined = `${out.stdout || ""}\n${out.stderr || ""}`;`.
 *      Run this suite -> it must FAIL (tainted-local form). Revert.
 *   2. Add `expect(result.stdout).toContain("outside the managed root");` to a
 *      pwsh-spawning test. Run this suite -> it must FAIL (member-access form).
 *      Revert.
 *   3. In a consumer test, change `combinedText`'s body from
 *      `return normalizePwshText(`${run.stdout || ""}\n${run.stderr || ""}`);` to
 *      the un-normalized `return `${run.stdout || ""}\n${run.stderr || ""}`;`.
 *      Run this suite -> it must FAIL (renamed-merge-helper form). Revert.
 *   If a mutation does NOT fail, the guard is inadequate and must be hardened.
 *   Self-tests below feed crafted source strings through the same detector to
 *   prove it fires (and does not false-positive) without touching real files.
 *
 * Residual limits (KNOWN, intentionally-uncovered bypasses -- do NOT read this
 * guard as proof that every fragile phrase assertion is impossible; it kills the
 * common forms, not the long tail):
 *   - INTERPOLATED / TEMPLATE phrase ARGUMENT: the matcher argument check
 *     (argumentIsPhrase) treats a `${...}`-interpolated template as an expression,
 *     not a fixed phrase, and skips it -- so `expect(out.stdout).toContain(
 *     `prefix ${x} outside the managed root`)` is NOT flagged even though the
 *     literal words are width-wrappable. Phrase arguments must be plain string
 *     literals to be analyzable.
 *   - PHRASE STORED IN A VARIABLE then asserted: only LITERAL matcher arguments
 *     are inspected. `const phrase = "outside the managed root"; expect(
 *     out.stdout).toContain(phrase);` slips through -- the argument is an
 *     identifier, not a literal.
 *   - The renamed-merge-helper detector closes the TEMPLATE-LITERAL merge door
 *     (the only merge spelling this repo uses), but it does NOT cover a STRING-
 *     CONCATENATION merge (`run.stdout + "\n" + run.stderr`) nor a helper that
 *     returns a BARE single member (`return result.stdout;`) -- the latter is
 *     deliberately excluded because it is indistinguishable from the legitimate
 *     structural single-member reads this repo relies on (`return
 *     result.stdout.split(...)`, `return { stdout, stderr, status }`), and
 *     flagging it would cost real false positives.
 *   - The tainted-local-variable scan (collectTaintedRawVariables) reads each
 *     binding initializer only up to the first `;`/newline, so a raw merge whose
 *     initializer is split across REAL source newlines would not taint the
 *     variable -- a style this repo does not use.
 *   Closing any of these later must come with a self-test AND a repo-wide
 *   zero-false-positive proof, the same bar the existing detectors meet.
 *
 * @cross-platform-regression -- this marker requires the file to be gated on
 * ubuntu/windows/macos via the targeted step in
 * .github/workflows/cross-platform-preflight.yml; enforced by
 * scripts/__tests__/cross-platform-preflight-coverage.test.js.
 */

"use strict";

const fs = require("fs");
const path = require("path");

const { normalizeToLf } = require("../lib/quote-parser");
const { stripJsCommentsAndStrings } = require("../lib/source-stripping");

const REPO_ROOT = path.resolve(__dirname, "..", "..");
const TESTS_ROOT = path.join(REPO_ROOT, "scripts", "__tests__");
const LIB_TESTS_ROOT = path.join(REPO_ROOT, "scripts", "lib", "__tests__");

// Matchers that take a phrase/regex and assert it against the `expect(...)`
// receiver. `.not.` is handled by the chain scanner, so both positive and
// negative forms are covered.
const PHRASE_MATCHERS = new Set(["toContain", "toMatch"]);

// Recognized helpers that internally route through normalizePwshText. A receiver
// that is a call to one of these (or a variable bound to such a call) is
// compliant. Keep this list small and intentional.
const NORMALIZING_HELPERS = new Set(["normalizePwshText", "combinedText", "stdoutText"]);

// This guard file embeds crafted source strings (the detector self-tests) and
// prose describing exactly what is forbidden; those literals are data, not real
// assertions. The normalizer's own unit test references the helper by name in
// prose/imports but performs no pwsh spawn.
const SELF = path.join("scripts", "__tests__", "pwsh-output-assertion-policy.test.js");

function toRepoRelative(absolutePath) {
  return path.relative(REPO_ROOT, absolutePath).split(path.sep).join("/");
}

function readUtf8(absolutePath) {
  return normalizeToLf(fs.readFileSync(absolutePath, "utf8"));
}

function listTestFiles() {
  const out = [];
  for (const root of [TESTS_ROOT, LIB_TESTS_ROOT]) {
    if (!fs.existsSync(root)) {
      continue;
    }
    for (const entry of fs.readdirSync(root, { withFileTypes: true })) {
      if (entry.isFile() && entry.name.endsWith(".test.js")) {
        out.push(path.join(root, entry.name));
      }
    }
  }
  return out;
}

/**
 * True when the source SPAWNS pwsh via child_process (so its stdout/stderr is
 * real ConciseView-rendered output). Matches `spawnSync("pwsh"` / `spawn("pwsh"`
 * and the `REAL_PWSH` / `PWSH` indirection used by this repo's harnesses, in
 * single/double/backtick quoting and across whitespace/newlines.
 *
 * @param {string} source - Raw source (LF-normalized).
 * @returns {boolean}
 */
function spawnsPwsh(source) {
  // Direct literal: spawnSync("pwsh", ...) / spawn(`pwsh`, ...).
  if (/\b(?:spawnSync|spawn|execFileSync|execFile)\s*\(\s*["'`]pwsh["'`]/.test(source)) {
    return true;
  }
  // Indirection through a pwsh-path variable resolved from a pwsh probe. The
  // harnesses bind REAL_PWSH / PWSH from `(Get-Command pwsh).Source`; treat a
  // spawn whose command identifier is one of those as a pwsh spawn.
  if (
    /\b(?:spawnSync|spawn|execFileSync|execFile)\s*\(\s*(?:REAL_PWSH|PWSH|pwshPath|PWSH_PATH)\b/.test(
      source
    )
  ) {
    return true;
  }
  return false;
}

/**
 * Find the index just past the matching close paren for an open paren at
 * `openIndex`, skipping over string/template-literal payloads so parens inside
 * strings never unbalance the scan. Returns -1 if unbalanced.
 *
 * @param {string} source - Raw source.
 * @param {number} openIndex - Index of the `(`.
 * @returns {number} Index of the matching `)`, or -1.
 */
function matchParen(source, openIndex) {
  let depth = 0;
  let quote = null;
  for (let i = openIndex; i < source.length; i++) {
    const ch = source[i];
    const prev = i > 0 ? source[i - 1] : "";
    if (quote) {
      if (ch === quote && prev !== "\\") {
        quote = null;
      }
      continue;
    }
    if (ch === '"' || ch === "'" || ch === "`") {
      quote = ch;
      continue;
    }
    if (ch === "(") {
      depth++;
    } else if (ch === ")") {
      depth--;
      if (depth === 0) {
        return i;
      }
    }
  }
  return -1;
}

function lineNumberAt(source, index) {
  return source.slice(0, index).split("\n").length;
}

// A receiver is a RAW pwsh-output member access if it contains a `.stdout` or
// `.stderr` property access that is NOT the argument of a normalizing helper.
// (When wrapped, the `.stdout` lives inside `normalizePwshText(... .stdout ...)`,
// and the OUTERMOST callee is the helper -- see classifyReceiver.)
const STDOUT_STDERR_MEMBER = /\.\s*(?:stdout|stderr)\b/;

/**
 * The OUTERMOST callee name of a receiver expression, if the whole receiver is a
 * single call `name(...)`. Returns null when the receiver is not a bare call
 * (e.g. a plain identifier, or a member access). Used to recognize a receiver
 * that is `normalizePwshText(...)` / `combinedText(...)`.
 *
 * @param {string} receiver - Trimmed receiver source.
 * @returns {string|null}
 */
function outermostCalleeName(receiver) {
  const m = /^([A-Za-z_$][\w$]*)\s*\(/.exec(receiver);
  if (!m) {
    return null;
  }
  // Ensure the call spans the WHOLE receiver (its close paren is the last char),
  // so `a(b).c` (which is not simply a normalizing call) is not misread.
  const close = matchParen(receiver, m.index + m[0].length - 1);
  if (close !== receiver.length - 1) {
    return null;
  }
  return m[1];
}

/**
 * Collect local variable names that are bound to a RAW pwsh-output merge -- e.g.
 * `const combined = `${out.stdout}\n${out.stderr}`;` or
 * `const combined = `${result.stdout || ""}\n${result.stderr || ""}`;`. These are
 * "tainted": a phrase assertion on them is the anti-pattern. A binding to a
 * normalizing helper (`const combined = combinedText(out);`) is NOT tainted and
 * is intentionally excluded.
 *
 * The initializer is read up to the first `;`/newline, which covers the
 * single-line merge forms this repo uses (the `\n` between the two members is a
 * two-character escape on ONE source line, not a real line break). Residual: a
 * raw merge whose initializer is split across REAL source newlines would not
 * taint the variable here -- an unusual style this repo does not use; the member-
 * access detector (violation (a)) still catches the direct `expect(x.stdout)`
 * form, and the normalizer's own unit test is the primary correctness backstop.
 *
 * @param {string} source - Raw source (LF-normalized).
 * @returns {Set<string>} Tainted local variable names.
 */
function collectTaintedRawVariables(source) {
  const tainted = new Set();
  const bindingRe = /\b(?:const|let|var)\s+([A-Za-z_$][\w$]*)\s*=\s*([^\n;]+)/g;
  let match = bindingRe.exec(source);
  while (match !== null) {
    const name = match[1];
    const initializer = match[2];
    // A template literal (or string concat) that interpolates a .stdout/.stderr
    // member, and is NOT itself a normalizing-helper call, taints the variable.
    const callee = outermostCalleeName(initializer.trim());
    const isHelperCall = callee !== null && NORMALIZING_HELPERS.has(callee);
    if (!isHelperCall && STDOUT_STDERR_MEMBER.test(initializer)) {
      tainted.add(name);
    }
    match = bindingRe.exec(source);
  }
  return tainted;
}

/**
 * Decide whether a phrase-assertion receiver is COMPLIANT (normalized) given the
 * set of tainted raw-output variables in the file.
 *
 * @param {string} receiver - Trimmed receiver source from `expect(RECEIVER)`.
 * @param {Set<string>} taintedVars - Names bound to raw pwsh-output merges.
 * @returns {{ flagged: boolean, reason?: string }}
 */
function classifyReceiver(receiver, taintedVars) {
  // Compliant: the receiver is itself a normalizing-helper call.
  const callee = outermostCalleeName(receiver);
  if (callee !== null && NORMALIZING_HELPERS.has(callee)) {
    return { flagged: false };
  }

  // Violation (b): a bare identifier that is a tainted raw-output variable.
  if (/^[A-Za-z_$][\w$]*$/.test(receiver) && taintedVars.has(receiver)) {
    return { flagged: true, reason: `raw pwsh-output variable '${receiver}'` };
  }

  // Violation (a): a member access / merge expression that touches .stdout or
  // .stderr without being wrapped by a normalizing helper. (A wrapped receiver
  // hit the compliant branch above because its outermost callee is the helper.)
  if (STDOUT_STDERR_MEMBER.test(receiver)) {
    return { flagged: true, reason: `raw pwsh-output member access \`${receiver.trim()}\`` };
  }

  return { flagged: false };
}

/**
 * A `.toContain(arg)` argument is a "phrase" when it is a string LITERAL with
 * interior whitespace (a multi-word phrase that a word wrap could split). A
 * single-token literal, or a non-literal argument (variable/expression), is not
 * a wrappable phrase and is not flagged. `.toMatch(...)` is always treated as a
 * phrase assertion (regexes routinely span multiple words).
 *
 * @param {string} matcher - "toContain" | "toMatch".
 * @param {string} argText - Raw text of the matcher's first argument.
 * @returns {boolean}
 */
function argumentIsPhrase(matcher, argText) {
  if (matcher === "toMatch") {
    return true;
  }
  const trimmed = argText.trim();
  // String literal: '...' | "..." | `...` (no interpolation). Require interior
  // whitespace to count as a multi-word phrase.
  const literal = /^(["'`])([\s\S]*)\1$/.exec(trimmed);
  if (!literal) {
    return false;
  }
  if (literal[1] === "`" && /\$\{/.test(literal[2])) {
    // An interpolated template is an expression, not a fixed phrase; skip.
    return false;
  }
  return /\s/.test(literal[2]);
}

/**
 * Scan a single test file's source for the anti-pattern. Returns an array of
 * `{ line, matcher, receiver, reason }` violations.
 *
 * @param {string} source - Raw source (LF-normalized).
 * @returns {Array<{line:number, matcher:string, receiver:string, reason:string}>}
 */
function findRawPwshPhraseAssertions(source) {
  if (!spawnsPwsh(source)) {
    return [];
  }
  const taintedVars = collectTaintedRawVariables(source);
  const violations = [];

  const expectRe = /\bexpect\s*\(/g;
  let m = expectRe.exec(source);
  while (m !== null) {
    const openIndex = m.index + m[0].length - 1;
    const closeIndex = matchParen(source, openIndex);
    if (closeIndex === -1) {
      m = expectRe.exec(source);
      continue;
    }
    const receiver = source.slice(openIndex + 1, closeIndex).trim();

    // Walk the matcher chain after `expect(...)`: an optional `.not`, then the
    // matcher call. Allow whitespace between tokens.
    const tail = source.slice(closeIndex + 1);
    const chain = /^\s*(?:\.\s*not\s*)?\.\s*([A-Za-z_$][\w$]*)\s*\(/.exec(tail);
    if (chain && PHRASE_MATCHERS.has(chain[1])) {
      const matcher = chain[1];
      const matcherOpen = closeIndex + 1 + chain.index + chain[0].length - 1;
      const matcherClose = matchParen(source, matcherOpen);
      if (matcherClose !== -1) {
        const argList = source.slice(matcherOpen + 1, matcherClose);
        const firstArg = splitTopLevelArgs(argList)[0] || "";
        if (argumentIsPhrase(matcher, firstArg)) {
          const verdict = classifyReceiver(receiver, taintedVars);
          if (verdict.flagged) {
            violations.push({
              line: lineNumberAt(source, m.index),
              matcher,
              receiver,
              reason: verdict.reason
            });
          }
        }
      }
    }

    m = expectRe.exec(source);
  }

  return violations;
}

/**
 * Split a call's argument-list source into TOP-LEVEL comma-separated arguments,
 * ignoring commas nested in parens/brackets/braces/strings/templates. Returns the
 * trimmed text of each argument.
 *
 * @param {string} argsText - Text between a call's outermost parens.
 * @returns {string[]}
 */
function splitTopLevelArgs(argsText) {
  const args = [];
  let depth = 0;
  let current = "";
  let quote = null;
  for (let i = 0; i < argsText.length; i++) {
    const ch = argsText[i];
    const prev = i > 0 ? argsText[i - 1] : "";
    if (quote) {
      current += ch;
      if (ch === quote && prev !== "\\") {
        quote = null;
      }
      continue;
    }
    if (ch === '"' || ch === "'" || ch === "`") {
      quote = ch;
      current += ch;
      continue;
    }
    if (ch === "(" || ch === "[" || ch === "{") {
      depth++;
      current += ch;
      continue;
    }
    if (ch === ")" || ch === "]" || ch === "}") {
      depth--;
      current += ch;
      continue;
    }
    if (ch === "," && depth === 0) {
      args.push(current.trim());
      current = "";
      continue;
    }
    current += ch;
  }
  if (current.trim().length > 0) {
    args.push(current.trim());
  }
  return args;
}

// ---------------------------------------------------------------------------
// "Renamed helper" door: a LOCALLY-DEFINED helper that merges raw pwsh output
// without normalizing.
//
// The receiver scanner above recognizes `combinedText(...)` / `stdoutText(...)`
// by NAME. That leaves a bypass: define a DIFFERENTLY-named helper whose body
// reconstructs the same raw `${out.stdout}\n${out.stderr}` merge but omits
// `normalizePwshText`, then phrase-assert on its result -- the call-site receiver
// is just `myHelper(out)`, which the name-based check waves through. The
// width-wrap regression rides back in through that helper.
//
// We kill that class at the SOURCE: a helper DEFINITION (named `return` body or
// arrow implicit-return) whose value is a TEMPLATE LITERAL interpolating
// `.stdout`/`.stderr` and is NOT wrapped by a normalizing helper IS the raw merge,
// regardless of what it is later called. Flagging the definition needs no
// call-graph and is robust: the merge is dead on arrival.
//
// Robustness: the scan runs over COMMENT/STRING-STRIPPED source
// (stripJsCommentsAndStrings), so the word "return" in prose, a backtick phrase
// in a comment, or a merge spelled out inside a STRING LITERAL (this guard's own
// self-test data, error-message text, docs) cannot false-positive -- only real
// code is seen. We scope to template-literal merges specifically: that is the
// exact shape combinedText uses, and it is what distinguishes a raw merge from
// the legitimate `return { stdout, stderr, status }` result-shaper and from
// structural single-member reads like `return result.stdout.split(...)` (a member
// chain, not a backtick), neither of which is flagged. See "Residual limits" in
// the file header for the merge spellings this intentionally does NOT cover.
// ---------------------------------------------------------------------------

/**
 * Read the expression that begins at `start` (on COMMENT/STRING-STRIPPED source)
 * up to its top-level terminator: a `;` / `,` at bracket depth 0, or the first
 * UNMATCHED close bracket (the `}` / `)` that ends the enclosing body). Bracket
 * depth is tracked so an object-literal `{...}`, call `(...)`, or array `[...]`
 * inside the expression is read whole. Strings/templates were already neutralized
 * by stripJsCommentsAndStrings, so quotes need no special handling here.
 *
 * @param {string} source - Comment/string-stripped source.
 * @param {number} start - Index just past the `return` keyword or `=>` token.
 * @returns {string} The trimmed expression text.
 */
function readReturnExpression(source, start) {
  let i = start;
  while (i < source.length && /\s/.test(source[i])) {
    i++;
  }
  const exprStart = i;
  let depth = 0;
  for (; i < source.length; i++) {
    const ch = source[i];
    if (ch === "(" || ch === "[" || ch === "{") {
      depth++;
    } else if (ch === ")" || ch === "]" || ch === "}") {
      if (depth === 0) {
        break;
      }
      depth--;
    } else if ((ch === ";" || ch === ",") && depth === 0) {
      break;
    }
  }
  return source.slice(exprStart, i).trim();
}

/**
 * True when a returned / arrow-body expression is a RAW MERGE of pwsh output: a
 * TEMPLATE LITERAL interpolating `.stdout`/`.stderr` that is NOT wrapped by a
 * recognized normalizing helper. `normalizePwshText(`${a.stdout}...`)` and the
 * by-convention `combinedText(...)` / `stdoutText(...)` are compliant because the
 * outermost callee is a normalizing helper.
 *
 * @param {string} expr - Trimmed return/arrow-body expression (stripped source).
 * @returns {boolean}
 */
function isRawMergeExpression(expr) {
  const callee = outermostCalleeName(expr);
  if (callee !== null && NORMALIZING_HELPERS.has(callee)) {
    return false;
  }
  return expr.startsWith("`") && STDOUT_STDERR_MEMBER.test(expr);
}

/**
 * Find "renamed helper" raw-merge definitions in a pwsh-spawning test file: a
 * `return <template merge>` or an arrow implicit-return `=> <template merge>`
 * whose value is an un-normalized `.stdout`/`.stderr` template merge. Operates on
 * comment/string-stripped source so prose and string-literal data cannot
 * false-positive. Non-pwsh-spawning files are skipped (their stdout is not real
 * ConciseView output).
 *
 * @param {string} rawSource - Raw source (any line endings).
 * @returns {Array<{line:number, kind:string, expr:string}>}
 */
function findRenamedMergeHelpers(rawSource) {
  const normalized = normalizeToLf(rawSource);
  if (!spawnsPwsh(normalized)) {
    return [];
  }
  const source = stripJsCommentsAndStrings(normalized);
  const hits = [];

  const returnRe = /\breturn\b/g;
  let m = returnRe.exec(source);
  while (m !== null) {
    const expr = readReturnExpression(source, m.index + "return".length);
    if (isRawMergeExpression(expr)) {
      hits.push({ line: lineNumberAt(source, m.index), kind: "return", expr });
    }
    m = returnRe.exec(source);
  }

  // Arrow implicit return: `=> <expr>` where the body is NOT a `{` block (block
  // bodies use an explicit `return`, already covered above).
  const arrowRe = /=>/g;
  m = arrowRe.exec(source);
  while (m !== null) {
    let j = m.index + 2;
    while (j < source.length && /\s/.test(source[j])) {
      j++;
    }
    if (source[j] !== "{") {
      const expr = readReturnExpression(source, j);
      if (isRawMergeExpression(expr)) {
        hits.push({ line: lineNumberAt(source, m.index), kind: "arrow", expr });
      }
    }
    m = arrowRe.exec(source);
  }

  return hits;
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe("pwsh-output-assertion-policy (repo-wide)", () => {
  test("no pwsh-spawning test asserts a phrase against RAW (un-normalized) pwsh output", () => {
    const offenders = [];

    for (const abs of listTestFiles()) {
      const relative = path.relative(REPO_ROOT, abs);
      if (relative === SELF) {
        continue;
      }
      const source = readUtf8(abs);
      for (const hit of findRawPwshPhraseAssertions(source)) {
        offenders.push({ file: toRepoRelative(abs), ...hit });
      }
    }

    if (offenders.length > 0) {
      const details = offenders
        .map((o) => `  ${o.file}:${o.line} [.${o.matcher} on ${o.reason}]`)
        .join("\n");
      throw new Error(
        "pwsh-output-assertion-policy violation: phrase assertion(s) run against RAW " +
          "PowerShell-rendered output.\n" +
          "When `pwsh -File` renders an unhandled `throw`, PowerShell's ConciseView formatter " +
          "WORD-WRAPS the message at the host console width (inserting a `\\n     | ` gutter), " +
          'splitting phrases like "outside the managed root" on the narrower Windows runner -- ' +
          "the assertion then FAILS on Windows only and rots silently on Linux.\n" +
          "FIX: normalize the output before the phrase assertion via normalizePwshText(...) " +
          "(scripts/lib/pwsh-output.js), or a recognized wrapper helper " +
          "(combinedText(...) / stdoutText(...)), e.g.\n" +
          "  const combined = combinedText(out); // = normalizePwshText(`${out.stdout}\\n${out.stderr}`)\n" +
          '  expect(combined).toContain("outside the managed root");\n\n' +
          "Offending assertions:\n" +
          details
      );
    }
  });

  test("no pwsh-spawning test defines a 'renamed helper' that merges raw pwsh output without normalizing", () => {
    const offenders = [];

    for (const abs of listTestFiles()) {
      const relative = path.relative(REPO_ROOT, abs);
      if (relative === SELF) {
        continue;
      }
      const source = readUtf8(abs);
      for (const hit of findRenamedMergeHelpers(source)) {
        offenders.push({ file: toRepoRelative(abs), ...hit });
      }
    }

    if (offenders.length > 0) {
      const details = offenders
        .map((o) => `  ${o.file}:${o.line} [${o.kind} of raw merge \`${o.expr}\`]`)
        .join("\n");
      throw new Error(
        "pwsh-output-assertion-policy violation: a locally-defined helper merges RAW " +
          "PowerShell output without normalizing it.\n" +
          "A helper that returns `${run.stdout}\\n${run.stderr}` (or an arrow `=> `${...}``) " +
          "rebuilds the width-wrap-fragile raw merge under a NEW name, so phrase assertions " +
          "on its result dodge the receiver guard and rot on the narrower Windows runner.\n" +
          "FIX: wrap the merge in normalizePwshText(...) (scripts/lib/pwsh-output.js) inside the " +
          "helper, e.g.\n" +
          "  function combinedText(run) {\n" +
          '    return normalizePwshText(`${run.stdout || ""}\\n${run.stderr || ""}`);\n' +
          "  }\n" +
          "(or add the helper name to NORMALIZING_HELPERS only if it provably routes through " +
          "normalizePwshText).\n\n" +
          "Offending helper definitions:\n" +
          details
      );
    }
  });

  // -------------------------------------------------------------------------
  // Self-tests: prove the detector fires on the exact anti-patterns and does
  // NOT false-positive on the compliant shapes, using crafted source strings.
  // -------------------------------------------------------------------------
  describe("detector self-tests", () => {
    const SPAWN = 'const r = spawnSync("pwsh", ["-File", s], {});\n';

    test("flags a member-access phrase assertion on result.stdout", () => {
      const src = SPAWN + 'expect(result.stdout).toContain("outside the managed root");';
      expect(findRawPwshPhraseAssertions(src)).toHaveLength(1);
    });

    test("flags a raw merge template-literal receiver", () => {
      const src =
        SPAWN + 'expect(`${out.stdout}\\n${out.stderr}`).toContain("cannot mutate editors");';
      expect(findRawPwshPhraseAssertions(src)).toHaveLength(1);
    });

    test("flags a tainted local variable bound to a raw merge", () => {
      const src =
        SPAWN +
        'const combined = `${out.stdout || ""}\\n${out.stderr || ""}`;\n' +
        'expect(combined).toContain("outside the managed root");';
      expect(findRawPwshPhraseAssertions(src)).toHaveLength(1);
    });

    test("flags a .not.toContain phrase on raw output", () => {
      const src = SPAWN + 'expect(result.stdout).not.toContain("cannot be found on this object");';
      expect(findRawPwshPhraseAssertions(src)).toHaveLength(1);
    });

    test("flags a .toMatch regex on raw output", () => {
      const src = SPAWN + "expect(out.stderr).toMatch(/did not produce NUnit results/);";
      expect(findRawPwshPhraseAssertions(src)).toHaveLength(1);
    });

    test("flags a member access on a .stdout member nested in a merge", () => {
      const src = SPAWN + 'expect(`${result.stdout}`).toContain("multi word phrase");';
      expect(findRawPwshPhraseAssertions(src)).toHaveLength(1);
    });

    test("does NOT flag a normalizePwshText-wrapped receiver", () => {
      const src =
        SPAWN +
        'expect(normalizePwshText(`${out.stdout}\\n${out.stderr}`)).toContain("outside the managed root");';
      expect(findRawPwshPhraseAssertions(src)).toHaveLength(0);
    });

    test("does NOT flag a combinedText(...) wrapper receiver", () => {
      const src = SPAWN + 'expect(combinedText(out)).toContain("outside the managed root");';
      expect(findRawPwshPhraseAssertions(src)).toHaveLength(0);
    });

    test("does NOT flag a variable bound to combinedText(...)", () => {
      const src =
        SPAWN +
        "const combined = combinedText(out);\n" +
        'expect(combined).toContain("outside the managed root");';
      expect(findRawPwshPhraseAssertions(src)).toHaveLength(0);
    });

    test("does NOT flag a stdoutText(...) wrapper receiver", () => {
      const src = SPAWN + 'expect(stdoutText(result)).toContain("did not write baseline CSV");';
      expect(findRawPwshPhraseAssertions(src)).toHaveLength(0);
    });

    test("does NOT flag a single-token .toContain on raw output (cannot be wrapped)", () => {
      const src = SPAWN + 'expect(result.stdout).toContain("windows-il2cpp");';
      expect(findRawPwshPhraseAssertions(src)).toHaveLength(0);
    });

    test("does NOT flag a structural last-line read on raw stdout", () => {
      const src = SPAWN + "expect(stdout.trim().split(/\\r?\\n/).pop()).toBe(editorExe);";
      expect(findRawPwshPhraseAssertions(src)).toHaveLength(0);
    });

    test("does NOT flag a phrase assertion in a file that does not spawn pwsh", () => {
      const src = 'const text = render();\nexpect(result.stdout).toContain("multi word phrase");';
      expect(findRawPwshPhraseAssertions(src)).toHaveLength(0);
    });

    test("does NOT flag a phrase assertion on file content read from disk", () => {
      const src =
        SPAWN +
        "const content = fs.readFileSync(p, 'utf8');\n" +
        'expect(content).toContain("multi word phrase");';
      expect(findRawPwshPhraseAssertions(src)).toHaveLength(0);
    });

    test("does NOT flag an .toBe equality on raw stdout (not a phrase matcher)", () => {
      const src = SPAWN + 'expect(out.stdout).toBe("False");';
      expect(findRawPwshPhraseAssertions(src)).toHaveLength(0);
    });

    test("does NOT flag a synthetic-spawn file (mocked runCommandFn, no real pwsh spawn)", () => {
      // doctor.test.js shape: a synthetic stdout string, no child_process pwsh spawn.
      const src =
        "const result = { status: 0, stdout: 'PowerShell 7.4.0' };\n" +
        'expect(result.stdout).toContain("PowerShell 7.4.0");';
      expect(findRawPwshPhraseAssertions(src)).toHaveLength(0);
    });

    test("spawnsPwsh recognizes the REAL_PWSH indirection", () => {
      expect(spawnsPwsh('spawnSync(REAL_PWSH, ["-File", s], {});')).toBe(true);
    });

    test("spawnsPwsh recognizes a direct pwsh literal across newlines", () => {
      expect(spawnsPwsh('spawnSync(\n  "pwsh",\n  ["-File", s]\n);')).toBe(true);
    });

    test("collectTaintedRawVariables ignores a combinedText-bound variable", () => {
      const tainted = collectTaintedRawVariables("const combined = combinedText(out);");
      expect(tainted.has("combined")).toBe(false);
    });

    test("argumentIsPhrase treats an interpolated template as a non-phrase", () => {
      expect(argumentIsPhrase("toContain", "`fake unity stdout for ${commit}`")).toBe(false);
    });

    // -----------------------------------------------------------------------
    // "Renamed helper" detector: prove findRenamedMergeHelpers catches a
    // differently-named raw-merge helper and does NOT false-positive on the
    // compliant combinedText/stdoutText shapes or on legitimate structural code.
    // -----------------------------------------------------------------------
    describe("renamed-helper detector", () => {
      test("flags a renamed function helper that returns a raw .stdout/.stderr merge", () => {
        const src = SPAWN + "function merged(out) { return `${out.stdout}\\n${out.stderr}`; }";
        expect(findRenamedMergeHelpers(src)).toHaveLength(1);
      });

      test("flags a renamed ARROW helper with an implicit raw-merge return", () => {
        const src = SPAWN + "const merged = (out) => `${out.stdout}\\n${out.stderr}`;";
        expect(findRenamedMergeHelpers(src)).toHaveLength(1);
      });

      test("flags a renamed helper that merges a single member into a template", () => {
        const src = SPAWN + "function only(out) { return `prefix ${out.stderr}`; }";
        expect(findRenamedMergeHelpers(src)).toHaveLength(1);
      });

      test("does NOT flag the compliant combinedText shape (normalizePwshText-wrapped)", () => {
        const src =
          SPAWN +
          'function combinedText(run) { return normalizePwshText(`${run.stdout || ""}\\n${run.stderr || ""}`); }';
        expect(findRenamedMergeHelpers(src)).toHaveLength(0);
      });

      test("does NOT flag the compliant stdoutText shape (single member, wrapped, no template)", () => {
        const src =
          SPAWN + 'function stdoutText(result) { return normalizePwshText(result.stdout || ""); }';
        expect(findRenamedMergeHelpers(src)).toHaveLength(0);
      });

      test("does NOT flag a compliant normalizePwshText-wrapped arrow helper", () => {
        const src =
          SPAWN + "const c = (run) => normalizePwshText(`${run.stdout}\\n${run.stderr}`);";
        expect(findRenamedMergeHelpers(src)).toHaveLength(0);
      });

      test("does NOT flag a result-shaper that returns an object literal of stdout/stderr", () => {
        const src =
          SPAWN +
          'function probe() { return { stdout: (run.stdout || "").trim(), stderr: run.stderr || "", status: run.status }; }';
        expect(findRenamedMergeHelpers(src)).toHaveLength(0);
      });

      test("does NOT flag a structural single-member read (.stdout.split chain, not a template)", () => {
        const src =
          SPAWN + 'function list() { return result.stdout.split("\\n").map((x) => x.trim()); }';
        expect(findRenamedMergeHelpers(src)).toHaveLength(0);
      });

      test("does NOT flag a throw-error template (not a return/arrow body)", () => {
        const src =
          SPAWN +
          "if (run.status !== 0) { throw new Error(`failed: ${run.stderr || run.stdout}`); }";
        expect(findRenamedMergeHelpers(src)).toHaveLength(0);
      });

      test("does NOT flag the merge text when it lives inside a STRING LITERAL (data, not code)", () => {
        const src = SPAWN + 'const doc = "return `${out.stdout}\\n${out.stderr}`";';
        expect(findRenamedMergeHelpers(src)).toHaveLength(0);
      });

      test("does NOT flag the word 'return' + backtick + .stdout inside a COMMENT (prose)", () => {
        const src =
          SPAWN + "// after this `return` see `${x.stdout}` in this comment\nconst a = 1;";
        expect(findRenamedMergeHelpers(src)).toHaveLength(0);
      });

      test("does NOT flag a renamed raw merge in a file that does not spawn pwsh", () => {
        const src = "function merged(out) { return `${out.stdout}\\n${out.stderr}`; }";
        expect(findRenamedMergeHelpers(src)).toHaveLength(0);
      });
    });
  });
});
