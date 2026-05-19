"use strict";

/**
 * integrity-gate-with-recovery.js
 *
 * Shared "probe node_modules integrity, attempt npm-ci recovery, re-probe"
 * gate. Consumed by every managed-tool wrapper (run-managed-jest,
 * run-managed-prettier, run-managed-cspell) so the auto-repair flow is
 * identical and not duplicated per wrapper.
 *
 * Behavior:
 *   1. Probe in-process via probeIntegrityFn. If ok -> return success.
 *   2. Consult isAutoRepairAllowedFn. If refused -> print banner via the
 *      decoder's PARTIAL_NODE_MODULES_INSTALL entry and return failure
 *      without invoking npm ci.
 *   3. Warn naming the missing files. Call attemptNpmCiRecoveryFn.
 *      If npm ci status non-zero -> print banner, return failure.
 *   4. Re-probe via probeIntegrityInSubprocessFn (defeats parent's stat
 *      cache). If subprocess probe ok -> return success with didRecover.
 *      Otherwise -> print banner, return failure.
 *
 * Opt-outs (the wrapper's caller threads these through):
 *   - DXMSG_HOOK_NO_AUTOREPAIR=1  -> isAutoRepairAllowedFn returns false.
 *   - DXMSG_HOOK_SKIP_INTEGRITY=1 -> the wrapper bypasses the gate entirely;
 *     this function is never called in that mode.
 *   - DXMSG_HOOK_AGGRESSIVE_RECOVERY=1 -> attemptNpmCiRecoveryFn rm-rf's
 *     node_modules before npm ci.
 */

const { isTruthyEnv } = require("./jest-error-decoder");
const {
    findZeroByteNativeBinaries,
    formatIntegrityFailure,
} = require("./node-modules-integrity");

/**
 * Decide whether auto-repair (npm ci) is safe to run.
 *
 * Refusal cases are checked in this exact order (any one is enough); the
 * JSDoc bullets MUST mirror the code below so reviewers can diff the policy
 * top-to-bottom against the implementation:
 *   1. DXMSG_HOOK_NO_AUTOREPAIR=1            -> caller asked us not to touch
 *      node_modules. Operator override; cheapest check first.
 *   2. getNpmMajorVersionFn() returns null   -> npm is unavailable or broken;
 *      we have no way to repair.
 *   3. .git/rebase-merge or .git/rebase-apply exists -> we are mid-rebase;
 *      touching node_modules could clobber a partially-resolved state.
 *   4. `git diff --quiet -- package-lock.json` exits non-zero -> the
 *      lockfile has unstaged changes; `npm ci` would refuse anyway, but
 *      surfacing the refusal here gives a clearer error. A null/error
 *      result from the spawn is also treated as refusal (defense in depth).
 *
 * Deliberate non-decision: we do NOT special-case CI environments (CI,
 * GITHUB_ACTIONS, etc.). `npm ci` is the correct repair in CI too because the
 * lockfile is committed and reproducible. Operators who want CI to fail
 * rather than auto-repair set `DXMSG_HOOK_NO_AUTOREPAIR=1` on the runner.
 *
 * @param {object} options
 * @param {object} options.env Process env (process.env or fake).
 * @param {string} options.repoRoot Absolute path to repo root.
 * @param {Function} options.getNpmMajorVersionFn Returns npm major version or null.
 * @param {Function} [options.existsSyncFn] Override fs.existsSync (defaults to fs).
 * @param {Function} [options.spawnPlatformCommandSyncFn] Override
 *   spawnPlatformCommandSync (defaults to scripts/lib/shell-command).
 * @returns {{allowed: boolean, reason: string|null}}
 */
function isAutoRepairAllowed(options) {
    const {
        env,
        repoRoot,
        getNpmMajorVersionFn,
        existsSyncFn = require("fs").existsSync,
        spawnPlatformCommandSyncFn = require("./shell-command").spawnPlatformCommandSync,
        path: pathModule = require("path"),
    } = options;

    if (env && isTruthyEnv(env.DXMSG_HOOK_NO_AUTOREPAIR)) {
        return { allowed: false, reason: "DXMSG_HOOK_NO_AUTOREPAIR=1 set" };
    }

    const npmMajor = getNpmMajorVersionFn();
    if (npmMajor === null || typeof npmMajor !== "number") {
        return { allowed: false, reason: "npm executable unavailable (getNpmMajorVersion returned null)" };
    }

    const gitDir = pathModule.join(repoRoot, ".git");
    if (existsSyncFn(pathModule.join(gitDir, "rebase-merge"))) {
        return { allowed: false, reason: "mid-rebase: .git/rebase-merge exists" };
    }
    if (existsSyncFn(pathModule.join(gitDir, "rebase-apply"))) {
        return { allowed: false, reason: "mid-rebase: .git/rebase-apply exists" };
    }

    // Cheap "is package-lock.json dirty?" check. `git diff --quiet -- <path>`
    // exits 0 when there are no unstaged differences, 1 when there are, and
    // 128 when not a git repo. We treat any non-zero exit as "refuse" so the
    // operator's working copy is not silently overwritten by `npm ci`.
    const lockResult = spawnPlatformCommandSyncFn(
        "git",
        ["diff", "--quiet", "--", "package-lock.json"],
        {
            cwd: repoRoot,
            stdio: ["ignore", "pipe", "pipe"],
            encoding: "utf8",
        }
    );

    if (!lockResult || lockResult.status !== 0) {
        return {
            allowed: false,
            reason: "package-lock.json has unstaged changes (git diff --quiet returned non-zero)",
        };
    }

    return { allowed: true, reason: null };
}

/**
 * Run the integrity probe -> auto-repair -> re-probe flow.
 *
 * @param {object} options
 * @param {string} options.repoRoot Absolute path to repository root.
 * @param {Function} options.probeIntegrityFn Function returning { ok, missing }.
 * @param {Function} options.probeIntegrityInSubprocessFn Function returning
 *   { ok, missing } after re-probe in a child node.
 * @param {Function} options.attemptNpmCiRecoveryFn Function performing npm ci.
 * @param {Function} options.isAutoRepairAllowedFn Function returning
 *   { allowed, reason }.
 * @param {Function} options.printActionableRepairBannerFn Decoder-driven
 *   banner printer.
 * @param {object} options.decoder The jest-error-decoder module
 *   ({ PATTERNS, decodeJestStderr }) used to fetch the
 *   PARTIAL_NODE_MODULES_INSTALL entry.
 * @param {Function} [options.warnFn] Logging sink (defaults to console.warn).
 * @param {Function} [options.findZeroByteNativeBinariesFn] Windows-only
 *   scanner for zero-byte *.node binaries (defaults to the production
 *   implementation in node-modules-integrity).
 * @param {Function} [options.formatIntegrityFailureFn] Formatter for the
 *   single-line failure summary (defaults to the production formatter).
 * @param {Function} [options.platformFn] Returns the current platform string
 *   (defaults to () => process.platform). Injectable for tests because
 *   jest's standard mocking story cannot easily clobber process.platform.
 * @returns {{ok: boolean, didRecover: boolean, reason: string|null}}
 */
function runIntegrityGateWithRecovery(options) {
    const {
        repoRoot,
        probeIntegrityFn,
        probeIntegrityInSubprocessFn,
        attemptNpmCiRecoveryFn,
        isAutoRepairAllowedFn,
        printActionableRepairBannerFn,
        decoder,
        warnFn = console.warn,
        findZeroByteNativeBinariesFn = findZeroByteNativeBinaries,
        formatIntegrityFailureFn = formatIntegrityFailure,
        platformFn = () => process.platform,
    } = options;

    if (typeof repoRoot !== "string" || repoRoot.length === 0) {
        throw new TypeError("runIntegrityGateWithRecovery requires options.repoRoot");
    }
    if (typeof probeIntegrityFn !== "function") {
        throw new TypeError("runIntegrityGateWithRecovery requires options.probeIntegrityFn");
    }

    // 1. Probe in-process.
    const initial = probeIntegrityFn({ repoRoot });

    // 1a. On Windows, supplement the standard probe with a scan for
    // zero-byte *.node native binaries -- the canonical AV-mid-write
    // failure mode where the JS probe passes but `require()` of a native
    // module would still crash. On non-Windows, the helper returns []
    // immediately without walking. We merge the offenders into the
    // missing[] list so the same downstream banner formatting applies.
    let zeroByteNative = [];
    try {
        zeroByteNative = findZeroByteNativeBinariesFn({
            repoRoot,
            platform: platformFn(),
        });
    } catch {
        // Defensive: the scanner already swallows readdir/stat errors, so
        // this catch is only for a wholly broken injection. Treat as no
        // additional offenders.
        zeroByteNative = [];
    }

    const augmentedMissing = Array.isArray(initial && initial.missing)
        ? initial.missing.slice()
        : [];
    for (const relPath of zeroByteNative) {
        augmentedMissing.push({
            tool: "<native-binding>",
            relPath,
            reason: "zero-byte",
        });
    }

    const augmentedResult = {
        ok: !!(initial && initial.ok) && zeroByteNative.length === 0,
        missing: augmentedMissing,
    };

    if (augmentedResult.ok) {
        return { ok: true, didRecover: false, reason: null };
    }

    warnFn(`WARNING: ${formatIntegrityFailureFn(augmentedResult)}`);

    // 2. Auto-repair allowed?
    const repairDecision = isAutoRepairAllowedFn();
    if (!repairDecision || !repairDecision.allowed) {
        warnFn(
            `WARNING: auto-repair refused (${repairDecision && repairDecision.reason ? repairDecision.reason : "no reason"}); skipping npm ci.`
        );
        printIntegrityGateBanner(printActionableRepairBannerFn, decoder);
        return { ok: false, didRecover: false, reason: repairDecision && repairDecision.reason };
    }

    // 3. Run npm ci.
    const recoveryResult = attemptNpmCiRecoveryFn();
    if (!recoveryResult || recoveryResult.status !== 0) {
        warnFn("WARNING: npm ci recovery did not succeed; integrity gate failing.");
        printIntegrityGateBanner(printActionableRepairBannerFn, decoder);
        return { ok: false, didRecover: false, reason: "npm ci recovery failed" };
    }

    // 4. Re-probe in a subprocess (defeats parent's stat cache).
    const reprobe = probeIntegrityInSubprocessFn({ repoRoot });
    if (reprobe && reprobe.ok) {
        return { ok: true, didRecover: true, reason: null };
    }

    warnFn(
        "WARNING: node_modules integrity probe still failed after npm ci recovery; manual repair required."
    );
    printIntegrityGateBanner(printActionableRepairBannerFn, decoder);
    return {
        ok: false,
        didRecover: false,
        reason: "subprocess re-probe still failed after npm ci",
    };
}

/**
 * Find the PARTIAL_NODE_MODULES_INSTALL decoder entry and pass a synthetic
 * "decoded" object to the banner printer. This is the closed-loop way to
 * reuse the same repair-banner formatter from the jest stderr path without
 * baking a special branch into the decoder.
 */
function printIntegrityGateBanner(printActionableRepairBannerFn, decoder) {
    if (typeof printActionableRepairBannerFn !== "function" || !decoder) {
        return;
    }
    const patterns = Array.isArray(decoder.PATTERNS) ? decoder.PATTERNS : [];
    const entry = patterns.find((p) => p && p.kind === "PARTIAL_NODE_MODULES_INSTALL");
    if (!entry) {
        return;
    }
    const decoded = {
        kind: entry.kind,
        summary: entry.summary,
        rootCauses: entry.rootCauses,
        repairCommands: entry.repairCommands,
        skillRef: entry.skillRef,
        selfHeal: entry.selfHeal,
        capturedMatch: null,
    };
    printActionableRepairBannerFn(decoded);
}

module.exports = {
    isAutoRepairAllowed,
    runIntegrityGateWithRecovery,
    printIntegrityGateBanner,
};
