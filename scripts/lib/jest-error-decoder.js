"use strict";

/**
 * jest-error-decoder.js
 *
 * Pure module that turns opaque Jest stderr into actionable diagnostics for
 * pre-push hooks. No I/O, no top-level side effects, no caching.
 *
 * Public surface:
 *   - PATTERNS: frozen array of decoder entries.
 *   - decodeJestStderr(stderr): returns the first matching entry merged with
 *     the captured regex match, or null.
 *   - formatRepairBanner(decoded, { color, env, isTTY }): returns a plain-ASCII
 *     banner suitable for Windows CMD, Git Bash, and POSIX terminals.
 *
 * Pattern ordering is significant: PATTERNS is iterated in declaration order
 * and the first regex match wins. Earlier entries are MORE SPECIFIC than later
 * entries, so they must remain ordered most-specific-first. In particular,
 * MISSING_TEST_RUNNER (which only matches the precise "testRunner option was
 * not found" sentence) comes before the broader CORRUPT_ISOLATED_CACHE and
 * MISSING_LOCAL_JEST "Cannot find module" patterns. If stderr ever satisfies
 * both, the more-specific MISSING_TEST_RUNNER decode is preferred.
 */

const SKILL_REF = ".llm/skills/scripting/jest-hook-robustness.md";
const PREFLIGHT_COMMAND = "npm run preflight:pre-push";

/**
 * Cross-platform rm -rf snippet for an isolated managed-jest cache entry.
 *
 * NOTE: this clears the ENTIRE dxmessaging-managed-jest tree, not just the
 * pinned-spec subdirectory that `attemptIsolatedCacheReset()` targets. The
 * user-facing repair hint is broader on purpose: it works regardless of which
 * pinned spec is in play, and it is safe to run when no isolated cache is
 * present. The runtime self-heal path uses the narrower per-spec reset.
 */
const ISOLATED_CACHE_RESET_COMMAND =
    "node -e \"require('fs').rmSync(require('path').join(require('os').tmpdir(), 'dxmessaging-managed-jest'), { recursive: true, force: true })\"";

const PATTERNS = Object.freeze([
    Object.freeze({
        kind: "MISSING_TEST_RUNNER",
        regex: /Module\s+(.+?)\s+in the testRunner option was not found\./i,
        summary: "Jest's runner validator rejected the testRunner module path.",
        rootCauses: Object.freeze([
            "partial node_modules install (jest-circus not fully extracted)",
            "isolated managed-jest cache corruption",
            "legacy --testRunner injection re-introduced from a regressed wrapper or hook entry",
        ]),
        repairCommands: Object.freeze([
            "npm ci",
            "node scripts/validate-node-tooling.js",
            PREFLIGHT_COMMAND,
        ]),
        skillRef: SKILL_REF,
        selfHeal: Object.freeze({ isolatedCacheReset: true, retryOnce: true }),
    }),
    Object.freeze({
        kind: "CORRUPT_ISOLATED_CACHE",
        // Anchor on Cannot find module to avoid colliding with MISSING_LOCAL_JEST.
        // The trailing (?!-) keeps us from grabbing "jest-circus-..." extensions.
        regex: /Cannot find module ['"]jest-circus(?:\/runner)?['"]/i,
        summary: "Isolated managed-Jest cache is missing jest-circus.",
        rootCauses: Object.freeze([
            "previous fallback install was interrupted",
            "cache directory was partially deleted",
        ]),
        repairCommands: Object.freeze([
            ISOLATED_CACHE_RESET_COMMAND,
            "node scripts/run-managed-jest.js --version",
            PREFLIGHT_COMMAND,
        ]),
        skillRef: SKILL_REF,
        selfHeal: Object.freeze({ isolatedCacheReset: true, retryOnce: true }),
    }),
    Object.freeze({
        kind: "MISSING_LOCAL_JEST",
        // Anchored on either a line start OR an "Error:" prefix so a short
        // module identifier ending in ": Cannot find module 'jest'" (e.g. the
        // tail of an unrelated diagnostic) cannot accidentally trigger this
        // pattern. The (?![\w-]) suffix prevents matching jest-circus, jest-cli,
        // etc. Multiline flag is required for the `^` anchor to find the start
        // of any line, not just the start of the whole buffer.
        regex: /^(?:\s*Error:\s+)?Cannot find module ['"](?:jest|jest\/bin\/jest(?:\.js)?)['"](?![\w-])/m,
        summary: "Local jest binary missing from node_modules.",
        rootCauses: Object.freeze([
            "partial install",
            "postinstall validator skipped",
            "node_modules wiped",
        ]),
        repairCommands: Object.freeze([
            "npm ci",
            "node scripts/validate-node-tooling.js",
            PREFLIGHT_COMMAND,
        ]),
        skillRef: SKILL_REF,
        selfHeal: Object.freeze({ npmCi: true, retryOnce: true }),
    }),
]);

/**
 * Treat null, empty string, "0", "false", "no", and "off" as falsy. Anything
 * else (including "1", "true", arbitrary strings) is truthy.
 *
 * @param {*} value Raw environment-variable value.
 * @returns {boolean}
 */
function isTruthyEnv(value) {
    if (value == null) {
        return false;
    }
    const stringValue = String(value).trim().toLowerCase();
    if (stringValue === "" || stringValue === "0" || stringValue === "false" || stringValue === "no" || stringValue === "off") {
        return false;
    }
    return true;
}

/**
 * Decode the first matching pattern out of a Jest stderr stream.
 *
 * The function iterates `PATTERNS` in declaration order and returns the first
 * match; patterns are ordered most-specific-first by convention (see the
 * module header for details).
 *
 * @param {string|Buffer|null|undefined} stderr Raw stderr text from a Jest
 *   invocation. Buffers are converted to UTF-8 strings internally.
 * @returns {object|null} A pattern object (kind, summary, rootCauses,
 *   repairCommands, skillRef, selfHeal) plus capturedMatch (regex result), or
 *   null when no pattern matched or input is empty.
 */
function decodeJestStderr(stderr) {
    let text;
    if (Buffer.isBuffer(stderr)) {
        text = stderr.toString("utf8");
    } else if (typeof stderr === "string") {
        text = stderr;
    } else {
        return null;
    }

    if (text.length === 0) {
        return null;
    }

    for (const pattern of PATTERNS) {
        const match = pattern.regex.exec(text);
        if (match) {
            return {
                kind: pattern.kind,
                summary: pattern.summary,
                rootCauses: pattern.rootCauses,
                repairCommands: pattern.repairCommands,
                skillRef: pattern.skillRef,
                selfHeal: pattern.selfHeal,
                capturedMatch: match,
            };
        }
    }

    return null;
}

/**
 * Produce a plain-ASCII repair banner. Uses only `=`, `-`, `|` so Windows
 * CMD cannot mojibake Unicode box-drawing characters.
 *
 * @param {object|null} decoded Output of decodeJestStderr().
 * @param {object} [options]
 * @param {boolean} [options.color] When true, ANSI-color the header line, but
 *   only if the gating env/TTY checks pass.
 * @param {object} [options.env] Process env object to inspect for CI flags.
 *   Defaults to process.env. Injectable so tests can be deterministic.
 * @param {boolean} [options.isTTY] Whether the destination stream is a TTY.
 *   Defaults to process.stderr.isTTY (the banner is written to stderr, so the
 *   TTY decision must mirror stderr, not stdout).
 * @returns {string} The banner, or "" when decoded is null.
 */
function formatRepairBanner(decoded, options = {}) {
    if (!decoded) {
        return "";
    }

    const color = Boolean(options && options.color);
    const env = (options && options.env) || process.env;
    const isTTY = options && Object.prototype.hasOwnProperty.call(options, "isTTY")
        ? Boolean(options.isTTY)
        : Boolean(process.stderr && process.stderr.isTTY);

    const horizontal = "=".repeat(64);
    const divider = "-".repeat(64);

    const rootCauseLines = decoded.rootCauses.map((cause) => `  - ${cause}`);
    const repairLines = decoded.repairCommands.map(
        (command, index) => `  ${index + 1}. ${command}`
    );

    const headerLine = `jest-hook diagnostic: ${decoded.kind}`;
    const ciIsSet = isTruthyEnv(env && env.CI);
    const shouldColorize = color && isTTY && !ciIsSet;
    const decoratedHeader = shouldColorize
        ? `\x1b[31m${headerLine}\x1b[0m`
        : headerLine;

    const lines = [
        horizontal,
        decoratedHeader,
        divider,
        `Summary: ${decoded.summary}`,
        "",
        "Most likely root cause:",
        ...rootCauseLines,
        "",
        "Suggested repair (run in order):",
        ...repairLines,
        "",
        `Skill reference: ${decoded.skillRef}`,
        `Run after repair: ${PREFLIGHT_COMMAND}`,
        horizontal,
    ];

    return `${lines.join("\n")}\n`;
}

module.exports = {
    PATTERNS,
    decodeJestStderr,
    formatRepairBanner,
    isTruthyEnv,
};
