/**
 * @fileoverview Tests for scripts/doctor.js -- the agentic preflight gate.
 *
 * Each section function in doctor.js accepts a dependency-injection options
 * bag so these tests can drive every probe with deterministic fakes
 * (synthetic fs, synthetic spawnSync, synthetic YAML). Real fs/child_process
 * are not touched.
 */

"use strict";

const path = require("path");

const {
    STALE_CACHE_DAYS,
    STALE_CACHE_MS,
    KNOWN_STATUSES,
    aggregateStatus,
    statusRank,
    statusToExitCode,
    checkNodeModulesFreshness,
    checkIsolatedJestCache,
    checkEolPolicy,
    checkPreCommitConfig,
    checkHookPerfBudget,
    checkCrossPlatformSanity,
    checkWorkingTreeState,
    probeToolVersions,
    runDoctor,
    shouldUseColor,
    decorateStatusLabel,
    formatHeaderBanner,
    formatSection,
    formatFooter,
    main,
} = require("../doctor");

const precommitYaml = require("../lib/precommit-yaml");
const precommitPerfScore = require("../lib/precommit-perf-score");

function noopRequire() {
    return {};
}

describe("doctor.js status helpers", () => {
    test("statusRank orders fail > warn > ok", () => {
        expect(statusRank("fail")).toBeGreaterThan(statusRank("warn"));
        expect(statusRank("warn")).toBeGreaterThan(statusRank("ok"));
    });

    test("aggregateStatus returns the worst section status", () => {
        const swallow = () => {};
        expect(aggregateStatus([{ status: "ok" }, { status: "ok" }], { warn: swallow })).toBe("ok");
        expect(aggregateStatus([{ status: "ok" }, { status: "warn" }], { warn: swallow })).toBe("warn");
        expect(aggregateStatus([{ status: "warn" }, { status: "fail" }], { warn: swallow })).toBe("fail");
        expect(aggregateStatus([], { warn: swallow })).toBe("ok");
    });

    test("aggregateStatus treats unrecognized statuses as fail and logs a warning (M4)", () => {
        const warnings = [];
        const result = aggregateStatus(
            [
                { name: "weird", status: "maybe" },
                { name: "fine", status: "ok" },
            ],
            { warn: (msg) => warnings.push(msg) }
        );
        expect(result).toBe("fail");
        expect(warnings.length).toBe(1);
        expect(warnings[0]).toContain("'weird'");
        expect(warnings[0]).toContain("'maybe'");
        expect(warnings[0]).toContain("treating as 'fail'");
    });

    test("aggregateStatus accepts a default warn sink (smoke test, swallow stderr)", () => {
        // No explicit warn override -> the function reaches for the default
        // sink (process.stderr.write). We do not assert on stderr; we only
        // care that the call still returns the fail-safe status.
        const originalWrite = process.stderr.write;
        process.stderr.write = () => true;
        try {
            const result = aggregateStatus([{ name: "x", status: "unknown" }]);
            expect(result).toBe("fail");
        } finally {
            process.stderr.write = originalWrite;
        }
    });

    test("statusToExitCode returns 1 only for fail", () => {
        expect(statusToExitCode("ok")).toBe(0);
        expect(statusToExitCode("warn")).toBe(0);
        expect(statusToExitCode("fail")).toBe(1);
    });

    test("STALE_CACHE_MS matches STALE_CACHE_DAYS", () => {
        expect(STALE_CACHE_MS).toBe(STALE_CACHE_DAYS * 24 * 60 * 60 * 1000);
    });
});

describe("checkNodeModulesFreshness", () => {
    // Step 10: checkNodeModulesFreshness now consumes probeIntegrity from
    // scripts/lib/node-modules-integrity.js as the file-existence + size
    // layer. Tests inject a no-op `probeIntegrityFn` so the existing fake-fs
    // fixtures remain authoritative for the legacy `requiredFiles` path.
    const noopProbeIntegrity = () => ({ ok: true, missing: [] });

    test("returns ok when every tool resolves, exists, and loads", () => {
        const toolSpecs = [
            {
                name: "prettier",
                requiredFiles: ["node_modules/prettier/index.cjs"],
                load: "require",
                entry: "node_modules/prettier/index.cjs",
            },
            {
                name: "markdownlint-cli2",
                requiredFiles: ["node_modules/markdownlint-cli2/markdownlint-cli2.mjs"],
                load: "import",
                entry: "node_modules/markdownlint-cli2/markdownlint-cli2.mjs",
            },
            {
                name: "cspell",
                requiredFiles: ["node_modules/cspell/bin.mjs"],
            },
            {
                name: "jest-circus",
                requiredFiles: [],
                load: "resolve",
                entry: "jest-circus/runner",
            },
        ];

        const section = checkNodeModulesFreshness({
            probeIntegrityFn: noopProbeIntegrity,
            toolSpecs,
            existsSyncFn: () => true,
            requireResolveFn: () => "/repo/node_modules/jest-circus/build/runner.js",
            requireFn: noopRequire,
            repoRoot: "/repo",
        });

        expect(section.name).toBe("node_modules freshness");
        expect(section.status).toBe("ok");
        expect(section.lines.some((line) => line.includes("ok    prettier"))).toBe(true);
        expect(section.lines.some((line) => line.includes("ok    jest-circus"))).toBe(true);
    });

    test("returns fail when one tool's required file is missing", () => {
        const toolSpecs = [
            {
                name: "prettier",
                requiredFiles: ["node_modules/prettier/index.cjs"],
                load: "require",
                entry: "node_modules/prettier/index.cjs",
            },
            {
                name: "missing-tool",
                requiredFiles: ["node_modules/missing-tool/bin.js"],
            },
        ];

        const section = checkNodeModulesFreshness({
            probeIntegrityFn: noopProbeIntegrity,
            toolSpecs,
            existsSyncFn: (absPath) => !absPath.includes("missing-tool"),
            requireFn: noopRequire,
            requireResolveFn: () => "/repo/node_modules/jest-circus/build/runner.js",
            repoRoot: "/repo",
        });

        expect(section.status).toBe("fail");
        const text = section.lines.join("\n");
        expect(text).toContain("FAIL  missing-tool");
        expect(text).toContain("missing required file");
        expect(text).toContain("npm ci");
    });

    test("returns fail when require throws", () => {
        const toolSpecs = [
            {
                name: "prettier",
                requiredFiles: ["node_modules/prettier/index.cjs"],
                load: "require",
                entry: "node_modules/prettier/index.cjs",
            },
        ];

        const section = checkNodeModulesFreshness({
            probeIntegrityFn: noopProbeIntegrity,
            toolSpecs,
            existsSyncFn: () => true,
            requireFn: () => {
                throw new Error("transitive dep broken");
            },
            requireResolveFn: () => "/path",
            repoRoot: "/repo",
        });

        expect(section.status).toBe("fail");
        expect(section.lines.join("\n")).toContain("transitive dep broken");
    });

    test("handles tool-specs without load/entry (m5: cspell, jest bare)", () => {
        // A spec that only has requiredFiles (no load mode, no entry) should
        // still succeed when those files exist. This is the cspell/jest-bare
        // shape -- the spec contributes a presence check only.
        const toolSpecs = [
            {
                name: "cspell",
                requiredFiles: ["node_modules/cspell/bin.mjs"],
            },
            {
                name: "jest",
                requiredFiles: [
                    "node_modules/jest/package.json",
                    "node_modules/jest/bin/jest.js",
                ],
            },
        ];

        const section = checkNodeModulesFreshness({
            probeIntegrityFn: noopProbeIntegrity,
            toolSpecs,
            existsSyncFn: () => true,
            requireFn: noopRequire,
            requireResolveFn: () => "/should/not/be/called",
            repoRoot: "/repo",
        });

        expect(section.status).toBe("ok");
        const text = section.lines.join("\n");
        expect(text).toContain("ok    cspell");
        expect(text).toContain("ok    jest");
        // No resolve/required/loaded lines because there is no entry.
        expect(text).not.toContain("resolved:");
        expect(text).not.toContain("required:");
        expect(text).not.toContain("loaded:");
    });

    test("returns fail when resolve-mode lookup throws", () => {
        const toolSpecs = [
            {
                name: "jest-circus",
                requiredFiles: [],
                load: "resolve",
                entry: "jest-circus/runner",
            },
        ];

        const section = checkNodeModulesFreshness({
            probeIntegrityFn: noopProbeIntegrity,
            toolSpecs,
            existsSyncFn: () => true,
            requireFn: noopRequire,
            requireResolveFn: () => {
                throw new Error("Cannot find module 'jest-circus/runner'");
            },
            repoRoot: "/repo",
        });

        expect(section.status).toBe("fail");
        expect(section.lines.join("\n")).toContain("Cannot find module");
    });
});

describe("checkIsolatedJestCache", () => {
    function makeStat(mtimeMs) {
        return { mtimeMs };
    }

    test("returns ok with no-cache message when cache root does not exist", () => {
        const section = checkIsolatedJestCache({
            existsSyncFn: () => false,
            readdirSyncFn: () => {
                throw new Error("should not read when root missing");
            },
            statSyncFn: () => {
                throw new Error("should not stat");
            },
            cacheRoot: "/tmp/dxmessaging-managed-jest",
            nowMs: 1_700_000_000_000,
        });

        expect(section.status).toBe("ok");
        const text = section.lines.join("\n");
        expect(text).toContain("No isolated managed-Jest cache yet");
        expect(text).toContain("Manual reset");
    });

    test("returns ok when cache root exists but is empty", () => {
        const section = checkIsolatedJestCache({
            existsSyncFn: () => true,
            readdirSyncFn: () => [],
            statSyncFn: () => makeStat(0),
            createRequireFn: () => ({ resolve: () => "/never" }),
            cacheRoot: "/tmp/dxmessaging-managed-jest",
            nowMs: 1_700_000_000_000,
        });

        expect(section.status).toBe("ok");
        expect(section.lines.join("\n")).toContain("contains no install dirs");
    });

    test("returns warn for stale entries (>30 days mtime)", () => {
        const now = 1_700_000_000_000;
        const fortyDaysAgo = now - 40 * 24 * 60 * 60 * 1000;

        // existsSyncFn must return true for the cache root, the package.json
        // inside the install dir, and the resolved runner path.
        const existsSyncFn = jest.fn(() => true);
        const readdirSyncFn = () => [
            { name: "jest_30.3.0", isDirectory: () => true },
        ];
        const statSyncFn = () => makeStat(fortyDaysAgo);
        const createRequireFn = () => ({
            resolve: () => "/tmp/dxmessaging-managed-jest/jest_30.3.0/node_modules/jest-circus/build/runner.js",
        });

        const section = checkIsolatedJestCache({
            existsSyncFn,
            readdirSyncFn,
            statSyncFn,
            createRequireFn,
            cacheRoot: "/tmp/dxmessaging-managed-jest",
            nowMs: now,
        });

        expect(section.status).toBe("warn");
        const text = section.lines.join("\n");
        expect(text).toContain("STALE");
        expect(text).toContain("age=40d");
    });

    test("returns fail when jest-circus/runner cannot be resolved from the install dir", () => {
        const now = 1_700_000_000_000;
        const recentMs = now - 1000;

        const section = checkIsolatedJestCache({
            existsSyncFn: () => true,
            readdirSyncFn: () => [{ name: "jest_30.3.0", isDirectory: () => true }],
            statSyncFn: () => makeStat(recentMs),
            createRequireFn: () => ({
                resolve: () => {
                    throw new Error("Cannot find module 'jest-circus/runner'");
                },
            }),
            cacheRoot: "/tmp/dxmessaging-managed-jest",
            nowMs: now,
        });

        expect(section.status).toBe("fail");
        expect(section.lines.join("\n")).toContain("Cannot find module 'jest-circus/runner'");
    });

    test("returns fail when readdirSync throws (m4)", () => {
        // Simulates a read failure mid-flight (EACCES, EIO, etc.). The
        // section should report fail and surface the underlying error
        // message; it must not silently treat the cache as empty.
        const section = checkIsolatedJestCache({
            existsSyncFn: () => true,
            readdirSyncFn: () => {
                const err = new Error("EACCES: permission denied");
                err.code = "EACCES";
                throw err;
            },
            statSyncFn: () => makeStat(0),
            createRequireFn: () => ({ resolve: () => "/never" }),
            cacheRoot: "/tmp/dxmessaging-managed-jest",
            nowMs: 1_700_000_000_000,
        });

        expect(section.status).toBe("fail");
        const text = section.lines.join("\n");
        expect(text).toContain("Could not read cache root");
        expect(text).toContain("EACCES: permission denied");
        expect(text).toContain("Manual reset");
    });

    test("ignores non-directory entries under cache root", () => {
        const section = checkIsolatedJestCache({
            existsSyncFn: () => true,
            readdirSyncFn: () => [
                { name: "stray-file.txt", isDirectory: () => false },
            ],
            statSyncFn: () => makeStat(Date.now()),
            createRequireFn: () => ({ resolve: () => "/never" }),
            cacheRoot: "/tmp/dxmessaging-managed-jest",
            nowMs: Date.now(),
        });

        expect(section.status).toBe("ok");
        expect(section.lines.join("\n")).toContain("contains no install dirs");
    });
});

describe("checkEolPolicy", () => {
    test("returns ok and reports policy counts consistent with the shared policy module", () => {
        const realPolicy = require("../lib/eol-policy");
        const section = checkEolPolicy({ policy: realPolicy });

        expect(section.status).toBe("ok");
        const text = section.lines.join("\n");
        expect(text).toContain(`CRLF extensions (${realPolicy.crlfExts.size})`);
        expect(text).toContain(`LF   extensions (${realPolicy.lfExts.size})`);
        const expectedTotal = realPolicy.crlfExts.size + realPolicy.lfExts.size;
        expect(text).toContain(`total tracked extensions: ${expectedTotal}`);
    });

    test("returns fail when an extension appears in both crlfExts and lfExts", () => {
        const conflictingPolicy = {
            crlfExts: new Set([".cs", ".js"]),
            lfExts: new Set([".js", ".md"]),
        };
        const section = checkEolPolicy({ policy: conflictingPolicy });
        expect(section.status).toBe("fail");
        expect(section.lines.join("\n")).toContain(".js");
    });
});

describe("checkPreCommitConfig", () => {
    const sampleConfig = [
        "repos:",
        "  - repo: local",
        "    hooks:",
        "      - id: alpha",
        "        entry: echo alpha",
        "        language: system",
        "        stages:",
        "          - pre-commit",
        "      - id: beta",
        "        entry: echo beta",
        "        language: system",
        "        stages:",
        "          - pre-push",
        "      - id: gamma",
        "        entry: echo gamma",
        "        language: system",
        "        stages:",
        "          - pre-commit",
        "          - pre-push",
        "",
    ].join("\n");

    function makeReadFile(map) {
        return (filePath) => {
            if (Object.prototype.hasOwnProperty.call(map, filePath)) {
                return map[filePath];
            }
            throw new Error(`unexpected read: ${filePath}`);
        };
    }

    test("counts total hooks and pre-push subset, verifies preflight coverage script", () => {
        const packageJson = JSON.stringify({
            scripts: {
                "preflight:pre-push":
                    "npm run preflight:pre-commit && pre-commit run --hook-stage pre-push --all-files",
            },
        });

        const section = checkPreCommitConfig({
            readFileSyncFn: makeReadFile({
                "/repo/.pre-commit-config.yaml": sampleConfig,
                "/repo/package.json": packageJson,
            }),
            parsePrecommitYaml: precommitYaml,
            runCommandFn: () => ({
                status: 0,
                error: null,
                stdout: "pre-commit 4.0.0",
                stderr: "",
            }),
            configPath: "/repo/.pre-commit-config.yaml",
            packageJsonPath: "/repo/package.json",
        });

        expect(section.status).toBe("ok");
        const text = section.lines.join("\n");
        expect(text).toContain("Parsed 3 hook block(s)");
        expect(text).toContain("pre-push hooks (2): beta, gamma");
        expect(text).toContain("pre-commit --version: pre-commit 4.0.0");
        expect(text).toContain("preflight:pre-push script present");
        expect(text).toContain(
            "invokes 'pre-commit run --hook-stage pre-push --all-files'"
        );
    });

    test("returns fail when preflight:pre-push is missing", () => {
        const packageJson = JSON.stringify({ scripts: {} });

        const section = checkPreCommitConfig({
            readFileSyncFn: makeReadFile({
                "/repo/.pre-commit-config.yaml": sampleConfig,
                "/repo/package.json": packageJson,
            }),
            parsePrecommitYaml: precommitYaml,
            runCommandFn: () => ({ status: 1, error: null, stdout: "", stderr: "" }),
            configPath: "/repo/.pre-commit-config.yaml",
            packageJsonPath: "/repo/package.json",
        });

        expect(section.status).toBe("fail");
        expect(section.lines.join("\n")).toContain("preflight:pre-push is missing");
    });

    test("returns fail when preflight:pre-push lacks the all-files invocation", () => {
        const packageJson = JSON.stringify({
            scripts: {
                "preflight:pre-push": "echo not-really",
            },
        });

        const section = checkPreCommitConfig({
            readFileSyncFn: makeReadFile({
                "/repo/.pre-commit-config.yaml": sampleConfig,
                "/repo/package.json": packageJson,
            }),
            parsePrecommitYaml: precommitYaml,
            runCommandFn: () => ({ status: 1, error: null, stdout: "", stderr: "" }),
            configPath: "/repo/.pre-commit-config.yaml",
            packageJsonPath: "/repo/package.json",
        });

        expect(section.status).toBe("fail");
        expect(section.lines.join("\n")).toContain(
            "does not invoke 'pre-commit run --hook-stage pre-push --all-files'"
        );
    });

    test("fails when pre-commit is not on PATH (ENOENT)", () => {
        // Without `pre-commit` on PATH, `npm run preflight:pre-push` cannot
        // run its `pre-commit run --hook-stage pre-push --all-files` step.
        // The doctor must surface this as a hard failure -- it directly
        // contradicts the doctor's premise that "this branch is safe to push".
        const packageJson = JSON.stringify({
            scripts: {
                "preflight:pre-push":
                    "npm run preflight:pre-commit && pre-commit run --hook-stage pre-push --all-files",
            },
        });

        const section = checkPreCommitConfig({
            readFileSyncFn: makeReadFile({
                "/repo/.pre-commit-config.yaml": sampleConfig,
                "/repo/package.json": packageJson,
            }),
            parsePrecommitYaml: precommitYaml,
            runCommandFn: () => ({
                status: null,
                error: { code: "ENOENT", message: "ENOENT" },
                stdout: "",
                stderr: "",
            }),
            configPath: "/repo/.pre-commit-config.yaml",
            packageJsonPath: "/repo/package.json",
        });

        expect(section.status).toBe("fail");
        const text = section.lines.join("\n");
        expect(text).toContain("pre-commit --version: not on PATH (ENOENT)");
        expect(text).toContain("preflight:pre-push cannot run");
        expect(text).toContain("pip install pre-commit");
    });

    test("fails when pre-commit --version errors with a non-ENOENT reason", () => {
        const packageJson = JSON.stringify({
            scripts: {
                "preflight:pre-push":
                    "npm run preflight:pre-commit && pre-commit run --hook-stage pre-push --all-files",
            },
        });

        const section = checkPreCommitConfig({
            readFileSyncFn: makeReadFile({
                "/repo/.pre-commit-config.yaml": sampleConfig,
                "/repo/package.json": packageJson,
            }),
            parsePrecommitYaml: precommitYaml,
            runCommandFn: () => ({
                status: 2,
                error: null,
                stdout: "",
                stderr: "some other failure",
            }),
            configPath: "/repo/.pre-commit-config.yaml",
            packageJsonPath: "/repo/package.json",
        });

        expect(section.status).toBe("fail");
        expect(section.lines.join("\n")).toContain("pre-commit --version: not available");
    });

    test("accepts a pre-resolved preCommitVersionResult and does NOT spawn", () => {
        const packageJson = JSON.stringify({
            scripts: {
                "preflight:pre-push":
                    "npm run preflight:pre-commit && pre-commit run --hook-stage pre-push --all-files",
            },
        });

        let spawned = 0;
        const section = checkPreCommitConfig({
            readFileSyncFn: makeReadFile({
                "/repo/.pre-commit-config.yaml": sampleConfig,
                "/repo/package.json": packageJson,
            }),
            parsePrecommitYaml: precommitYaml,
            runCommandFn: () => {
                spawned += 1;
                return { status: 1, error: null, stdout: "", stderr: "" };
            },
            preCommitVersionResult: {
                status: 0,
                error: null,
                stdout: "pre-commit 3.6.0",
                stderr: "",
            },
            configPath: "/repo/.pre-commit-config.yaml",
            packageJsonPath: "/repo/package.json",
        });

        expect(spawned).toBe(0);
        expect(section.status).toBe("ok");
        expect(section.lines.join("\n")).toContain("pre-commit --version: pre-commit 3.6.0");
    });
});

describe("checkHookPerfBudget", () => {
    test("returns ok when scoreConfig reports a score within budget", () => {
        const section = checkHookPerfBudget({
            readFileSyncFn: () => "irrelevant -- scoreConfigFn is mocked",
            scoreConfigFn: () => ({
                totalScore: 5,
                perHookScores: [],
                allowList: [],
                rejections: [],
                perHookViolations: [],
            }),
            budget: 10,
            perHookCeiling: 3,
            configPath: "/repo/.pre-commit-config.yaml",
        });

        expect(section.status).toBe("ok");
        expect(section.lines.join("\n")).toContain("score: 5 (budget=10");
    });

    test("returns fail when score exceeds the budget", () => {
        const section = checkHookPerfBudget({
            readFileSyncFn: () => "irrelevant",
            scoreConfigFn: () => ({
                totalScore: 99,
                perHookScores: [],
                allowList: [],
                rejections: [],
                perHookViolations: [],
            }),
            budget: 10,
            perHookCeiling: 3,
            configPath: "/repo/.pre-commit-config.yaml",
        });

        expect(section.status).toBe("fail");
        expect(section.lines.join("\n")).toContain("exceeds whole-pipeline budget");
    });

    test("returns fail when per-hook ceiling is violated", () => {
        const section = checkHookPerfBudget({
            readFileSyncFn: () => "irrelevant",
            scoreConfigFn: () => ({
                totalScore: 5,
                perHookScores: [],
                allowList: [],
                rejections: [],
                perHookViolations: [
                    { id: "bad-hook", score: 5, ceiling: 3 },
                ],
            }),
            budget: 10,
            perHookCeiling: 3,
            configPath: "/repo/.pre-commit-config.yaml",
        });

        expect(section.status).toBe("fail");
        expect(section.lines.join("\n")).toContain("bad-hook");
    });

    test("returns fail when scoreConfig reports rejected perf-allow directives", () => {
        const section = checkHookPerfBudget({
            readFileSyncFn: () => "irrelevant",
            scoreConfigFn: () => ({
                totalScore: 5,
                perHookScores: [],
                allowList: [],
                rejections: [
                    { id: "noisy", startLine: 12, error: "reason too short" },
                ],
                perHookViolations: [],
            }),
            budget: 10,
            perHookCeiling: 3,
            configPath: "/repo/.pre-commit-config.yaml",
        });

        expect(section.status).toBe("fail");
        expect(section.lines.join("\n")).toContain("reason too short");
    });

    test("integration: live precommit-perf-score.scoreConfig consumed correctly", () => {
        // The doctor reuses the EXISTING scoreConfig (per the deliverable
        // constraints). This integration smoke-test runs the real scorer
        // against a tiny synthetic config to prove the wiring.
        const tinyConfig = [
            "repos:",
            "  - repo: local",
            "    hooks:",
            "      - id: cheap",
            "        entry: echo hi",
            "        language: system",
            "        stages:",
            "          - pre-commit",
        ].join("\n");

        const section = checkHookPerfBudget({
            readFileSyncFn: () => tinyConfig,
            scoreConfigFn: precommitPerfScore.scoreConfig,
            budget: precommitPerfScore.PERF_BUDGET,
            perHookCeiling: precommitPerfScore.PER_HOOK_CEILING,
            configPath: "/synthetic/.pre-commit-config.yaml",
        });

        expect(section.status).toBe("ok");
    });
});

describe("checkCrossPlatformSanity", () => {
    test("reports the injected platform and LF package.json", () => {
        const calls = [];
        const runCommandFn = (cmd) => {
            calls.push(cmd);
            if (cmd === "pwsh") {
                return { status: 0, stdout: "PowerShell 7.4.0\n", error: null };
            }
            if (cmd === "bash") {
                return {
                    status: 0,
                    stdout: "GNU bash, version 5.2.15(1)-release\n",
                    error: null,
                };
            }
            return { status: 1, error: null, stdout: "", stderr: "" };
        };

        const section = checkCrossPlatformSanity({
            platformFn: () => "linux",
            runCommandFn,
            readFileSyncFn: () =>
                "{\n  \"name\": \"clean\"\n}\n",
            packageJsonPath: "/repo/package.json",
            shellEnv: "/bin/bash",
        });

        expect(section.status).toBe("ok");
        const text = section.lines.join("\n");
        expect(text).toContain("process.platform: linux");
        expect(text).toContain("PowerShell 7.4.0");
        expect(text).toContain("bash, version 5.2.15");
        expect(text).toContain("package.json line endings: LF");
        expect(calls).toEqual(["pwsh", "bash"]);
    });

    test("gracefully handles pwsh missing on Linux (does not fail)", () => {
        const section = checkCrossPlatformSanity({
            platformFn: () => "linux",
            runCommandFn: (cmd) => {
                if (cmd === "pwsh") {
                    return { status: null, error: { code: "ENOENT" }, stdout: "", stderr: "" };
                }
                if (cmd === "bash") {
                    return { status: 0, stdout: "GNU bash, version 5.2\n", error: null };
                }
                return { status: 1, error: null, stdout: "", stderr: "" };
            },
            readFileSyncFn: () => "{\n}\n",
            packageJsonPath: "/repo/package.json",
            shellEnv: "/bin/bash",
        });

        expect(section.status).toBe("ok");
        expect(section.lines.join("\n")).toContain("pwsh:    not installed");
    });

    test("flags pwsh ENOENT as warn on win32", () => {
        const section = checkCrossPlatformSanity({
            platformFn: () => "win32",
            runCommandFn: (cmd) => {
                if (cmd === "pwsh") {
                    return { status: null, error: { code: "ENOENT" }, stdout: "", stderr: "" };
                }
                if (cmd === "bash") {
                    return { status: 0, stdout: "GNU bash, version 5.2\n", error: null };
                }
                return { status: 1, error: null, stdout: "", stderr: "" };
            },
            readFileSyncFn: () => "{}\n",
            packageJsonPath: "/repo/package.json",
            shellEnv: "C:\\Windows\\System32\\cmd.exe",
        });

        expect(section.status).toBe("warn");
    });

    test("fails when package.json has CRLF line endings", () => {
        const section = checkCrossPlatformSanity({
            platformFn: () => "linux",
            runCommandFn: () => ({ status: 0, stdout: "x\n", error: null }),
            readFileSyncFn: () => "{\r\n  \"name\": \"crlf\"\r\n}\r\n",
            packageJsonPath: "/repo/package.json",
            shellEnv: "/bin/bash",
        });

        expect(section.status).toBe("fail");
        expect(section.lines.join("\n")).toContain("CRLF line endings");
    });

    test("fails when package.json cannot be read", () => {
        const section = checkCrossPlatformSanity({
            platformFn: () => "linux",
            runCommandFn: () => ({ status: 0, stdout: "x\n", error: null }),
            readFileSyncFn: () => {
                throw new Error("ENOENT: package.json");
            },
            packageJsonPath: "/repo/package.json",
            shellEnv: "/bin/bash",
        });

        expect(section.status).toBe("fail");
        expect(section.lines.join("\n")).toContain("Could not read");
    });
});

describe("checkWorkingTreeState", () => {
    function makeRun(stdout, overrides = {}) {
        return () => ({
            status: 0,
            error: null,
            stdout,
            stderr: "",
            ...overrides,
        });
    }

    test("returns ok on a clean tree (empty porcelain)", () => {
        const section = checkWorkingTreeState({ runCommandFn: makeRun("") });
        expect(section.status).toBe("ok");
        expect(section.lines.join("\n")).toContain("Working tree is clean");
    });

    test("returns fail listing untracked paths (validate-untracked-policy gate)", () => {
        // Mirrors the exact case the adversarial reviewer caught: an untracked
        // path under scripts/ that validate-untracked-policy would reject.
        const section = checkWorkingTreeState({
            runCommandFn: makeRun("?? scripts/foo.js\n"),
        });
        expect(section.status).toBe("fail");
        const text = section.lines.join("\n");
        expect(text).toContain("1 untracked-and-unignored path(s)");
        expect(text).toContain("?? scripts/foo.js");
        expect(text).toContain("Remediation:");
        expect(text).toContain(".gitignore");
        expect(text).toContain("git add -N");
    });

    test("returns warn for modified-but-not-staged paths", () => {
        // " M" -> worktree change, no staged change. preflight does NOT fail
        // on these, but the doctor warns so the operator notices the drift.
        const section = checkWorkingTreeState({
            runCommandFn: makeRun(" M scripts/foo.js\n"),
        });
        expect(section.status).toBe("warn");
        const text = section.lines.join("\n");
        expect(text).toContain("1 modified-but-unstaged path(s)");
        expect(text).toContain(" M scripts/foo.js");
    });

    test("returns ok when only staged changes are present", () => {
        // "M " -> staged modification, worktree matches index. The push will
        // carry this; nothing to flag.
        const section = checkWorkingTreeState({
            runCommandFn: makeRun("M  scripts/foo.js\n"),
        });
        expect(section.status).toBe("ok");
        const text = section.lines.join("\n");
        expect(text).toContain("1 staged path(s)");
        expect(text).toContain("M  scripts/foo.js");
    });

    test("includes staged + unstaged info under a FAIL untracked diagnostic", () => {
        const porcelain = [
            "?? new.js",
            " M modified.js",
            "M  staged.js",
        ].join("\n") + "\n";
        const section = checkWorkingTreeState({
            runCommandFn: makeRun(porcelain),
        });
        expect(section.status).toBe("fail");
        const text = section.lines.join("\n");
        expect(text).toContain("?? new.js");
        expect(text).toContain(" M modified.js");
        expect(text).toContain("M  staged.js");
    });

    test("fails when git is not on PATH (ENOENT)", () => {
        const section = checkWorkingTreeState({
            runCommandFn: () => ({
                status: null,
                error: { code: "ENOENT", message: "ENOENT" },
                stdout: "",
                stderr: "",
            }),
        });
        expect(section.status).toBe("fail");
        expect(section.lines.join("\n")).toContain("git not found on PATH");
    });

    test("fails when git status exits non-zero", () => {
        const section = checkWorkingTreeState({
            runCommandFn: () => ({
                status: 128,
                error: null,
                stdout: "",
                stderr: "fatal: not a git repository",
            }),
        });
        expect(section.status).toBe("fail");
        const text = section.lines.join("\n");
        expect(text).toContain("git status exited with status 128");
        expect(text).toContain("not a git repository");
    });

    test("fails when spawn returns a non-ENOENT error", () => {
        const section = checkWorkingTreeState({
            runCommandFn: () => ({
                status: null,
                error: new Error("ETXTBSY: text file busy"),
                stdout: "",
                stderr: "",
            }),
        });
        expect(section.status).toBe("fail");
        expect(section.lines.join("\n")).toContain("ETXTBSY");
    });

    test("treats intent-to-add (` A` after git add -N) as warn, NOT fail", () => {
        // `git add -N path` produces " A path" in porcelain output. The
        // important distinction is that validate-untracked-policy's underlying
        // command (`git ls-files --others --exclude-standard`) does NOT list
        // intent-to-add files, so they are NOT a hard failure. Treating them
        // as modified-but-unstaged matches that semantic.
        const section = checkWorkingTreeState({
            runCommandFn: makeRun(" A scripts/new.js\n"),
        });
        expect(section.status).toBe("warn");
    });
});

describe("probeToolVersions", () => {
    test("spawns one --version probe per tool and returns them keyed by name", () => {
        const calls = [];
        const runCommandFn = (cmd, args) => {
            calls.push({ cmd, args });
            return { status: 0, error: null, stdout: `${cmd}-version-output`, stderr: "" };
        };
        const probes = probeToolVersions({ runCommandFn });
        expect(probes.npm.stdout).toBe("npm-version-output");
        expect(probes.preCommit.stdout).toBe("pre-commit-version-output");
        expect(probes.pwsh.stdout).toBe("pwsh-version-output");
        expect(probes.bash.stdout).toBe("bash-version-output");
        // Exactly one spawn per tool.
        expect(calls.map((c) => c.cmd)).toEqual(["npm", "pre-commit", "pwsh", "bash"]);
    });
});

describe("KNOWN_STATUSES", () => {
    test("exposes the canonical status vocabulary", () => {
        expect(KNOWN_STATUSES).toEqual(["ok", "warn", "fail"]);
    });
});

describe("runDoctor aggregator", () => {
    test("aggregates section statuses; any fail yields exit code 1", () => {
        // We cannot trivially inject all six section deps via runDoctor()
        // without effectively duplicating the section logic in tests, so we
        // exercise the aggregator pathway with overrides that force one
        // section to fail by triggering a failure condition that depends only
        // on the injected deps.
        const overrides = {
            checkNodeModulesFreshness: {
                toolSpecs: [
                    {
                        name: "always-missing",
                        requiredFiles: ["node_modules/always-missing/index.js"],
                    },
                ],
                existsSyncFn: () => false,
                requireFn: noopRequire,
                requireResolveFn: () => null,
                repoRoot: "/repo",
            },
            checkIsolatedJestCache: {
                existsSyncFn: () => false,
                readdirSyncFn: () => [],
                statSyncFn: () => ({ mtimeMs: 0 }),
                cacheRoot: "/never",
                nowMs: 0,
            },
            checkEolPolicy: {
                policy: { crlfExts: new Set(), lfExts: new Set() },
            },
            checkPreCommitConfig: {
                readFileSyncFn: (filePath) => {
                    if (filePath.endsWith(".pre-commit-config.yaml")) {
                        return "repos: []\n";
                    }
                    return JSON.stringify({
                        scripts: {
                            "preflight:pre-push":
                                "npm run preflight:pre-commit && pre-commit run --hook-stage pre-push --all-files",
                        },
                    });
                },
                parsePrecommitYaml: precommitYaml,
                runCommandFn: () => ({ status: 0, stdout: "pre-commit 4.0.0", error: null }),
            },
            checkHookPerfBudget: {
                readFileSyncFn: () => "anything",
                scoreConfigFn: () => ({
                    totalScore: 0,
                    perHookScores: [],
                    allowList: [],
                    rejections: [],
                    perHookViolations: [],
                }),
                budget: 10,
                perHookCeiling: 3,
            },
            checkCrossPlatformSanity: {
                platformFn: () => "linux",
                runCommandFn: () => ({ status: 0, stdout: "x\n", error: null }),
                readFileSyncFn: () => "{}\n",
                shellEnv: "/bin/bash",
            },
            checkWorkingTreeState: {
                runCommandFn: () => ({ status: 0, stdout: "", error: null, stderr: "" }),
            },
        };

        const report = runDoctor({ overrides });
        expect(report.overall).toBe("fail");
        expect(report.status).toBe(1);
        const failingSection = report.sections.find((s) => s.status === "fail");
        expect(failingSection).toBeTruthy();
        expect(failingSection.name).toBe("node_modules freshness");
    });

    test("aggregates to ok when every section is ok", () => {
        const overrides = {
            checkNodeModulesFreshness: {
                toolSpecs: [],
                existsSyncFn: () => true,
                requireFn: noopRequire,
                requireResolveFn: () => "/x",
                repoRoot: "/repo",
            },
            checkIsolatedJestCache: {
                existsSyncFn: () => false,
                readdirSyncFn: () => [],
                statSyncFn: () => ({ mtimeMs: 0 }),
                cacheRoot: "/never",
                nowMs: 0,
            },
            checkEolPolicy: {
                policy: { crlfExts: new Set([".cs"]), lfExts: new Set([".js"]) },
            },
            checkPreCommitConfig: {
                readFileSyncFn: (filePath) => {
                    if (filePath.endsWith(".pre-commit-config.yaml")) {
                        return "repos: []\n";
                    }
                    return JSON.stringify({
                        scripts: {
                            "preflight:pre-push":
                                "npm run preflight:pre-commit && pre-commit run --hook-stage pre-push --all-files",
                        },
                    });
                },
                parsePrecommitYaml: precommitYaml,
                runCommandFn: () => ({ status: 0, stdout: "pre-commit 4.0.0", error: null }),
            },
            checkHookPerfBudget: {
                readFileSyncFn: () => "anything",
                scoreConfigFn: () => ({
                    totalScore: 0,
                    perHookScores: [],
                    allowList: [],
                    rejections: [],
                    perHookViolations: [],
                }),
                budget: 10,
                perHookCeiling: 3,
            },
            checkCrossPlatformSanity: {
                platformFn: () => "linux",
                runCommandFn: () => ({ status: 0, stdout: "x\n", error: null }),
                readFileSyncFn: () => "{}\n",
                shellEnv: "/bin/bash",
            },
            checkWorkingTreeState: {
                runCommandFn: () => ({ status: 0, stdout: "", error: null, stderr: "" }),
            },
        };

        const report = runDoctor({ overrides });
        expect(report.overall).toBe("ok");
        expect(report.status).toBe(0);
    });

    test("threads versionProbes into pre-commit + cross-platform sections without re-spawning (M1)", () => {
        // Sentinel results that the sections SHOULD consume from
        // versionProbes. If they ignored versionProbes and spawned their own
        // probes, runCommandFn (which we set up to throw) would be invoked
        // for those tools.
        const versionProbes = {
            npm: { status: 0, error: null, stdout: "11.99.0", stderr: "" },
            preCommit: { status: 0, error: null, stdout: "pre-commit SENTINEL-PC", stderr: "" },
            pwsh: { status: 0, error: null, stdout: "PowerShell SENTINEL-PWSH\n", stderr: "" },
            bash: { status: 0, error: null, stdout: "GNU bash, version SENTINEL-BASH\n", stderr: "" },
        };

        const throwOnSpawn = (cmd) => {
            // The aggregator passes the versionProbes-derived results into
            // sections; sections must not re-spawn. The only spawn allowed
            // through this fixture is git status inside checkWorkingTreeState
            // (its own runCommandFn is independently injected below).
            throw new Error(`Unexpected spawn for ${cmd} -- versionProbes should have been used`);
        };

        const overrides = {
            checkNodeModulesFreshness: {
                toolSpecs: [],
                existsSyncFn: () => true,
                requireFn: noopRequire,
                requireResolveFn: () => "/x",
                repoRoot: "/repo",
            },
            checkIsolatedJestCache: {
                existsSyncFn: () => false,
                readdirSyncFn: () => [],
                statSyncFn: () => ({ mtimeMs: 0 }),
                cacheRoot: "/never",
                nowMs: 0,
            },
            checkEolPolicy: {
                policy: { crlfExts: new Set([".cs"]), lfExts: new Set([".js"]) },
            },
            checkPreCommitConfig: {
                readFileSyncFn: (filePath) => {
                    if (filePath.endsWith(".pre-commit-config.yaml")) {
                        return "repos: []\n";
                    }
                    return JSON.stringify({
                        scripts: {
                            "preflight:pre-push":
                                "npm run preflight:pre-commit && pre-commit run --hook-stage pre-push --all-files",
                        },
                    });
                },
                parsePrecommitYaml: precommitYaml,
                runCommandFn: throwOnSpawn,
            },
            checkHookPerfBudget: {
                readFileSyncFn: () => "anything",
                scoreConfigFn: () => ({
                    totalScore: 0,
                    perHookScores: [],
                    allowList: [],
                    rejections: [],
                    perHookViolations: [],
                }),
                budget: 10,
                perHookCeiling: 3,
            },
            checkCrossPlatformSanity: {
                platformFn: () => "linux",
                runCommandFn: throwOnSpawn,
                readFileSyncFn: () => "{}\n",
                shellEnv: "/bin/bash",
            },
            checkWorkingTreeState: {
                runCommandFn: () => ({ status: 0, stdout: "", error: null, stderr: "" }),
            },
        };

        const report = runDoctor({ overrides, versionProbes });
        expect(report.overall).toBe("ok");

        const preCommitSection = report.sections.find((s) => s.name === "pre-commit config");
        expect(preCommitSection.lines.join("\n")).toContain("SENTINEL-PC");

        const sanitySection = report.sections.find((s) => s.name === "cross-platform sanity");
        const sanityText = sanitySection.lines.join("\n");
        expect(sanityText).toContain("SENTINEL-PWSH");
        expect(sanityText).toContain("SENTINEL-BASH");
    });
});

describe("doctor presentation helpers", () => {
    test("shouldUseColor suppresses color when CI is truthy", () => {
        expect(shouldUseColor({ envCi: "1", isTTY: true })).toBe(false);
        expect(shouldUseColor({ envCi: "true", isTTY: true })).toBe(false);
        expect(shouldUseColor({ envCi: "", isTTY: true })).toBe(true);
        expect(shouldUseColor({ envCi: "0", isTTY: true })).toBe(true);
        expect(shouldUseColor({ envCi: null, isTTY: false })).toBe(false);
    });

    test("shouldUseColor honors NO_COLOR even on a TTY (m7)", () => {
        // NO_COLOR is the highest-priority opt-out per https://no-color.org/.
        expect(shouldUseColor({ envNoColor: "1", isTTY: true })).toBe(false);
        expect(shouldUseColor({ envNoColor: "true", isTTY: true })).toBe(false);
        // Falsy NO_COLOR has no effect; falls through to other rules.
        expect(shouldUseColor({ envNoColor: "0", isTTY: true })).toBe(true);
        expect(shouldUseColor({ envNoColor: "", isTTY: true })).toBe(true);
    });

    test("shouldUseColor honors FORCE_COLOR even without a TTY (m7)", () => {
        // FORCE_COLOR overrides isTTY=false (e.g. when piping through tee).
        expect(shouldUseColor({ envForceColor: "1", isTTY: false })).toBe(true);
        expect(shouldUseColor({ envForceColor: "true", isTTY: false })).toBe(true);
        // NO_COLOR still wins over FORCE_COLOR (the more restrictive opt-out).
        expect(
            shouldUseColor({ envNoColor: "1", envForceColor: "1", isTTY: false })
        ).toBe(false);
        // CI is overridden by FORCE_COLOR.
        expect(
            shouldUseColor({ envCi: "1", envForceColor: "1", isTTY: false })
        ).toBe(true);
    });

    test("decorateStatusLabel includes ANSI only when useColor=true", () => {
        const colorized = decorateStatusLabel("fail", true);
        expect(colorized).toMatch(/\x1b\[/);
        expect(colorized).toContain("[FAIL]");
        const plain = decorateStatusLabel("fail", false);
        expect(plain).toBe("[FAIL]");
    });

    test("formatHeaderBanner contains repo/node/os fields and uses ASCII box drawing", () => {
        const banner = formatHeaderBanner({
            repoRoot: "/repo",
            nodeVersion: "v24.0.0",
            platform: "linux",
            arch: "x64",
            shellEnv: "/bin/bash",
            npmVersion: "11.0.0",
        });
        expect(banner).toContain("Repo:    /repo");
        expect(banner).toContain("Node:    v24.0.0");
        expect(banner).toContain("npm:     11.0.0");
        expect(banner).toContain("OS:      linux/x64");
        // ASCII-only.
        for (let i = 0; i < banner.length; i += 1) {
            const code = banner.charCodeAt(i);
            const ok = code === 0x0a || (code >= 0x20 && code <= 0x7e);
            if (!ok) {
                throw new Error(
                    `Header banner contains non-ASCII codepoint 0x${code.toString(16)} at index ${i}`
                );
            }
        }
    });

    test("formatSection and formatFooter produce printable text", () => {
        const section = { name: "x", status: "ok", lines: ["  ok    body"] };
        const text = formatSection(section, false);
        expect(text).toContain("[ OK ] x");
        expect(text).toContain("ok    body");

        const footer = formatFooter([section], "ok", false);
        expect(footer).toContain("Summary:");
        expect(footer).toContain("Overall: [ OK ]");
    });
});

describe("doctor main()", () => {
    test("main returns the runDoctor status code and writes output via writeFn", () => {
        const writes = [];
        const writeFn = (text) => writes.push(text);
        const runDoctorFn = () => ({
            status: 0,
            overall: "ok",
            sections: [{ name: "fake", status: "ok", lines: ["  ok    placeholder"] }],
        });
        const runCommandFn = () => ({ status: 0, stdout: "11.0.0", error: null });
        const probeToolVersionsFn = () => ({
            npm: { status: 0, error: null, stdout: "11.0.0", stderr: "" },
            preCommit: { status: 0, error: null, stdout: "pre-commit 4.0.0", stderr: "" },
            pwsh: { status: 0, error: null, stdout: "PowerShell 7.4.0\n", stderr: "" },
            bash: { status: 0, error: null, stdout: "GNU bash, version 5.2\n", stderr: "" },
        });

        const exitCode = main({
            writeFn,
            envCi: "1",
            envNoColor: undefined,
            envForceColor: undefined,
            isTTY: false,
            runDoctorFn,
            runCommandFn,
            probeToolVersionsFn,
        });

        expect(exitCode).toBe(0);
        const joined = writes.join("");
        expect(joined).toContain("DxMessaging doctor");
        expect(joined).toContain("[ OK ] fake");
        expect(joined).toContain("Overall: [ OK ]");
    });

    test("main returns 1 when runDoctor fails", () => {
        const writes = [];
        const writeFn = (text) => writes.push(text);
        const runDoctorFn = () => ({
            status: 1,
            overall: "fail",
            sections: [{ name: "fake", status: "fail", lines: ["  FAIL  bad"] }],
        });
        const runCommandFn = () => ({ status: 0, stdout: "11.0.0", error: null });
        const probeToolVersionsFn = () => ({
            npm: { status: 0, error: null, stdout: "11.0.0", stderr: "" },
            preCommit: { status: 0, error: null, stdout: "pre-commit 4.0.0", stderr: "" },
            pwsh: { status: 0, error: null, stdout: "PowerShell 7.4.0\n", stderr: "" },
            bash: { status: 0, error: null, stdout: "GNU bash, version 5.2\n", stderr: "" },
        });

        const exitCode = main({
            writeFn,
            envCi: "1",
            envNoColor: undefined,
            envForceColor: undefined,
            isTTY: false,
            runDoctorFn,
            runCommandFn,
            probeToolVersionsFn,
        });

        expect(exitCode).toBe(1);
        expect(writes.join("")).toContain("Overall: [FAIL]");
    });

    test("main calls probeToolVersionsFn exactly once (M1)", () => {
        const writes = [];
        let probeCalls = 0;
        const probeToolVersionsFn = () => {
            probeCalls += 1;
            return {
                npm: { status: 0, error: null, stdout: "11.0.0", stderr: "" },
                preCommit: { status: 0, error: null, stdout: "pre-commit 4.0.0", stderr: "" },
                pwsh: { status: 0, error: null, stdout: "PowerShell 7.4.0\n", stderr: "" },
                bash: { status: 0, error: null, stdout: "GNU bash, version 5.2\n", stderr: "" },
            };
        };
        const runDoctorFn = () => ({
            status: 0,
            overall: "ok",
            sections: [{ name: "fake", status: "ok", lines: [] }],
        });

        main({
            writeFn: (text) => writes.push(text),
            envCi: "1",
            envNoColor: undefined,
            envForceColor: undefined,
            isTTY: false,
            runDoctorFn,
            runCommandFn: () => ({ status: 0, stdout: "", error: null }),
            probeToolVersionsFn,
        });

        expect(probeCalls).toBe(1);
    });
});

describe("doctor file lives next to other scripts", () => {
    test("doctor.js is located at scripts/doctor.js relative to repo root", () => {
        // Defensive: the hook regex and the preflight script entry both
        // assume the doctor lives at scripts/doctor.js. If a refactor moves
        // it, the failure should surface here, not at push time.
        const expected = path.resolve(__dirname, "../doctor.js");
        const required = require.resolve("../doctor");
        expect(required).toBe(expected);
    });
});
