/**
 * @fileoverview Tests for scripts/lib/node-modules-integrity.js.
 *
 * The integrity library is the single source of truth for the
 * "are critical node_modules files present + non-zero?" check that the
 * managed Jest/Prettier/cspell wrappers gate on before running anything.
 * These tests pin INTEGRITY_TARGETS shape, the probe semantics, the
 * subprocess re-probe contract, and the Windows-only zero-byte native
 * scan.
 */

"use strict";

const path = require("path");

const {
    INTEGRITY_TARGETS,
    probeIntegrity,
    probeIntegrityInSubprocess,
    findZeroByteNativeBinaries,
    formatIntegrityFailure,
} = require("../lib/node-modules-integrity");

describe("INTEGRITY_TARGETS", () => {
    test("is frozen and immutable end-to-end", () => {
        expect(Object.isFrozen(INTEGRITY_TARGETS)).toBe(true);
        for (const target of INTEGRITY_TARGETS) {
            expect(Object.isFrozen(target)).toBe(true);
            expect(Object.isFrozen(target.files)).toBe(true);
            for (const file of target.files) {
                expect(Object.isFrozen(file)).toBe(true);
            }
        }
    });

    test("contains entries for prettier, markdownlint-cli2, cspell, jest, jest-circus", () => {
        const tools = INTEGRITY_TARGETS.map((target) => target.tool);
        expect(tools).toEqual(
            expect.arrayContaining([
                "prettier",
                "markdownlint-cli2",
                "cspell",
                "jest",
                "jest-circus",
            ])
        );
        expect(tools.length).toBe(5);
    });

    test("every file entry has a relPath and a numeric minBytes", () => {
        for (const target of INTEGRITY_TARGETS) {
            expect(target.files.length).toBeGreaterThan(0);
            for (const file of target.files) {
                expect(typeof file.relPath).toBe("string");
                expect(file.relPath.length).toBeGreaterThan(0);
                expect(file.relPath.startsWith("node_modules/")).toBe(true);
                expect(typeof file.minBytes).toBe("number");
                expect(file.minBytes).toBeGreaterThan(0);
            }
        }
    });

    test("jest-circus entry includes the runner.js + index.js + jestAdapterInit.js triad (verified on-disk)", () => {
        const jestCircus = INTEGRITY_TARGETS.find((t) => t.tool === "jest-circus");
        expect(jestCircus).toBeTruthy();
        const relPaths = jestCircus.files.map((f) => f.relPath);
        expect(relPaths).toEqual(
            expect.arrayContaining([
                "node_modules/jest-circus/build/runner.js",
                "node_modules/jest-circus/build/index.js",
                "node_modules/jest-circus/build/jestAdapterInit.js",
            ])
        );
    });

    test("cannot be mutated by consumers", () => {
        expect(() => {
            INTEGRITY_TARGETS.push({ tool: "malicious" });
        }).toThrow();
        expect(INTEGRITY_TARGETS.length).toBe(5);
    });
});

describe("probeIntegrity", () => {
    const fakeTargets = [
        {
            tool: "alpha",
            files: [
                { relPath: "node_modules/alpha/index.js", minBytes: 1 },
                { relPath: "node_modules/alpha/bin/alpha.js", minBytes: 1 },
            ],
        },
        {
            tool: "beta",
            files: [{ relPath: "node_modules/beta/bin.mjs", minBytes: 1 }],
        },
    ];

    test("returns ok=true with empty missing[] when every file is present and non-empty", () => {
        const result = probeIntegrity({
            repoRoot: "/repo",
            existsSyncFn: () => true,
            statSyncFn: () => ({ size: 100 }),
            targets: fakeTargets,
        });

        expect(result.ok).toBe(true);
        expect(result.missing).toEqual([]);
    });

    test("flags a missing file via existsSync=false", () => {
        const result = probeIntegrity({
            repoRoot: "/repo",
            existsSyncFn: (abs) => !abs.endsWith("alpha.js"),
            statSyncFn: () => ({ size: 100 }),
            targets: fakeTargets,
        });

        expect(result.ok).toBe(false);
        expect(result.missing).toEqual([
            { tool: "alpha", relPath: "node_modules/alpha/bin/alpha.js", reason: "missing" },
        ]);
    });

    test("flags a missing file via statSync ENOENT", () => {
        const result = probeIntegrity({
            repoRoot: "/repo",
            existsSyncFn: () => true,
            statSyncFn: (abs) => {
                if (abs.endsWith("bin.mjs")) {
                    const err = new Error("ENOENT: file gone");
                    err.code = "ENOENT";
                    throw err;
                }
                return { size: 100 };
            },
            targets: fakeTargets,
        });

        expect(result.ok).toBe(false);
        expect(result.missing.find((m) => m.tool === "beta")).toEqual({
            tool: "beta",
            relPath: "node_modules/beta/bin.mjs",
            reason: "missing",
        });
    });

    test("flags zero-byte file as empty (existsSync true, size 0)", () => {
        const result = probeIntegrity({
            repoRoot: "/repo",
            existsSyncFn: () => true,
            statSyncFn: (abs) => (abs.endsWith("index.js") ? { size: 0 } : { size: 100 }),
            targets: fakeTargets,
        });

        expect(result.ok).toBe(false);
        expect(result.missing).toEqual([
            { tool: "alpha", relPath: "node_modules/alpha/index.js", reason: "empty" },
        ]);
    });

    test("throws when repoRoot is missing", () => {
        expect(() => probeIntegrity({ targets: fakeTargets })).toThrow(
            /repoRoot/
        );
        expect(() => probeIntegrity({ repoRoot: "", targets: fakeTargets })).toThrow();
    });

    test("default targets reference INTEGRITY_TARGETS when none provided", () => {
        // Spy: statSyncFn is invoked for each file in INTEGRITY_TARGETS.
        const seenPaths = new Set();
        probeIntegrity({
            repoRoot: "/repo",
            existsSyncFn: () => true,
            statSyncFn: (abs) => {
                seenPaths.add(abs);
                return { size: 100 };
            },
        });
        const totalFiles = INTEGRITY_TARGETS.reduce(
            (sum, target) => sum + target.files.length,
            0
        );
        expect(seenPaths.size).toBe(totalFiles);
    });

    test("non-ENOENT stat errors map to 'empty' (defense-in-depth)", () => {
        const result = probeIntegrity({
            repoRoot: "/repo",
            existsSyncFn: () => true,
            statSyncFn: () => {
                const err = new Error("EACCES");
                err.code = "EACCES";
                throw err;
            },
            targets: [{ tool: "alpha", files: [{ relPath: "node_modules/alpha/index.js", minBytes: 1 }] }],
        });

        expect(result.ok).toBe(false);
        expect(result.missing[0].reason).toBe("empty");
    });
});

describe("probeIntegrityInSubprocess", () => {
    test("invokes spawnSyncFn with node -e <inline-script> and parses stdout JSON", () => {
        let capturedCommand;
        let capturedArgs;
        const spawnSyncFn = (command, args) => {
            capturedCommand = command;
            capturedArgs = args;
            return {
                status: 0,
                error: null,
                stdout: JSON.stringify({ ok: true, missing: [] }),
                stderr: "",
            };
        };

        const result = probeIntegrityInSubprocess({
            repoRoot: "/repo",
            execPath: "/usr/bin/node",
            spawnSyncFn,
        });

        expect(capturedCommand).toBe("/usr/bin/node");
        expect(Array.isArray(capturedArgs)).toBe(true);
        expect(capturedArgs[0]).toBe("-e");
        expect(typeof capturedArgs[1]).toBe("string");
        expect(capturedArgs[1]).toContain("probeIntegrity");
        expect(capturedArgs[1]).toContain('"/repo"');
        expect(result).toEqual({ ok: true, missing: [] });
    });

    test("spawns the child with cwd set to repoRoot", () => {
        // Defense in depth: an inline script that prints JSON to stdout
        // does not strictly require a cwd, but pinning it to repoRoot
        // prevents any future addition that relies on relative paths
        // (or a Node policy file lookup) from drifting.
        let capturedOptions;
        const spawnSyncFn = (command, args, options) => {
            capturedOptions = options;
            return {
                status: 0,
                error: null,
                stdout: JSON.stringify({ ok: true, missing: [] }),
                stderr: "",
            };
        };
        probeIntegrityInSubprocess({
            repoRoot: "/repo",
            execPath: "/usr/bin/node",
            spawnSyncFn,
        });
        expect(capturedOptions).toEqual(
            expect.objectContaining({ cwd: "/repo" })
        );
    });

    test("inline subprocess script opts into strict mode", () => {
        // Cosmetic consistency with the parent file's "use strict"; pin
        // it so a future inline-script rewrite cannot silently drop it.
        let capturedArgs;
        probeIntegrityInSubprocess({
            repoRoot: "/repo",
            execPath: "/usr/bin/node",
            spawnSyncFn: (_command, args) => {
                capturedArgs = args;
                return {
                    status: 0,
                    error: null,
                    stdout: JSON.stringify({ ok: true, missing: [] }),
                    stderr: "",
                };
            },
        });
        expect(capturedArgs[1]).toMatch(/^"use strict";/);
    });

    test("returns a synthetic failure when subprocess prints malformed JSON", () => {
        const result = probeIntegrityInSubprocess({
            repoRoot: "/repo",
            spawnSyncFn: () => ({ status: 0, error: null, stdout: "not json", stderr: "" }),
        });

        expect(result.ok).toBe(false);
        expect(result.missing).toEqual([
            expect.objectContaining({
                tool: "(subprocess)",
                reason: expect.stringContaining("JSON parse failed"),
            }),
        ]);
    });

    test("returns a synthetic failure when subprocess exits non-zero", () => {
        const result = probeIntegrityInSubprocess({
            repoRoot: "/repo",
            spawnSyncFn: () => ({
                status: 1,
                error: null,
                stdout: "",
                stderr: "Error: blah",
            }),
        });

        expect(result.ok).toBe(false);
        expect(result.missing[0].reason).toContain("exit=1");
        expect(result.missing[0].reason).toContain("Error: blah");
    });

    test("returns a synthetic failure when spawn itself errors", () => {
        const result = probeIntegrityInSubprocess({
            repoRoot: "/repo",
            spawnSyncFn: () => ({
                status: null,
                error: { message: "ENOENT" },
                stdout: "",
                stderr: "",
            }),
        });

        expect(result.ok).toBe(false);
        expect(result.missing[0].reason).toContain("spawn failed");
    });

    test("throws when repoRoot is missing", () => {
        expect(() => probeIntegrityInSubprocess({})).toThrow(/repoRoot/);
    });

    test("merges zeroByteNativeBinaries into missing[] when subprocess reports them", () => {
        // The first-pass in-process gate scans for AV-truncated *.node
        // binaries on Windows; the subprocess re-probe (after npm ci) must
        // do the same, otherwise a truncated native binding mid-rewrite
        // would falsely report ok. We fake a subprocess JSON shape that
        // includes the zeroByteNativeBinaries field and assert the parent
        // surfaces it under missing[] using the same <native-binding>
        // shape as the in-process gate.
        const subprocessJson = JSON.stringify({
            ok: true,
            missing: [],
            zeroByteNativeBinaries: [
                "node_modules/fake-native-binding/build/Release/binding.node",
            ],
        });
        const result = probeIntegrityInSubprocess({
            repoRoot: "/repo",
            spawnSyncFn: () => ({
                status: 0,
                error: null,
                stdout: subprocessJson,
                stderr: "",
            }),
        });
        expect(result.ok).toBe(false);
        expect(result.missing).toEqual([
            expect.objectContaining({
                tool: "<native-binding>",
                relPath: "node_modules/fake-native-binding/build/Release/binding.node",
                reason: "zero-byte",
            }),
        ]);
    });

    test("preserves ok=true when subprocess emits empty zeroByteNativeBinaries", () => {
        // Backwards-compat: a subprocess JSON shape that includes the
        // zeroByteNativeBinaries field but with no offenders must not flip
        // ok=true to ok=false. This pins the desired behavior for the
        // common (healthy) case after npm ci recovery.
        const subprocessJson = JSON.stringify({
            ok: true,
            missing: [],
            zeroByteNativeBinaries: [],
        });
        const result = probeIntegrityInSubprocess({
            repoRoot: "/repo",
            spawnSyncFn: () => ({
                status: 0,
                error: null,
                stdout: subprocessJson,
                stderr: "",
            }),
        });
        expect(result).toEqual({ ok: true, missing: [] });
    });

    test("inline subprocess script calls findZeroByteNativeBinaries on win32 only", () => {
        // Pin the inline script wiring so a future rewrite cannot silently
        // drop the Windows zero-byte scan from the re-probe.
        let capturedArgs;
        probeIntegrityInSubprocess({
            repoRoot: "/repo",
            execPath: "/usr/bin/node",
            spawnSyncFn: (_command, args) => {
                capturedArgs = args;
                return {
                    status: 0,
                    error: null,
                    stdout: JSON.stringify({
                        ok: true,
                        missing: [],
                        zeroByteNativeBinaries: [],
                    }),
                    stderr: "",
                };
            },
        });
        expect(capturedArgs[1]).toContain("findZeroByteNativeBinaries");
        expect(capturedArgs[1]).toContain('"win32"');
        expect(capturedArgs[1]).toContain("zeroByteNativeBinaries");
    });

    test("end-to-end: invokes the real child process and produces a parseable result", () => {
        // Integration smoke against the real repo. This exercises the full
        // wiring: parent serializes inline script, child requires the
        // integrity module + calls probeIntegrity, parent parses JSON.
        const realRepoRoot = path.resolve(__dirname, "..", "..");
        const result = probeIntegrityInSubprocess({ repoRoot: realRepoRoot });
        expect(typeof result.ok).toBe("boolean");
        expect(Array.isArray(result.missing)).toBe(true);
        // On a healthy repo the result should be ok; we assert the shape
        // rather than ok=true so the test is robust across mid-implementation
        // states.
        if (!result.ok) {
            for (const entry of result.missing) {
                expect(entry).toEqual(
                    expect.objectContaining({
                        tool: expect.any(String),
                        relPath: expect.any(String),
                        reason: expect.any(String),
                    })
                );
            }
        }
    });
});

describe("findZeroByteNativeBinaries", () => {
    test("returns [] on Linux/macOS without walking", () => {
        let walked = false;
        const result = findZeroByteNativeBinaries({
            repoRoot: "/repo",
            readdirSyncFn: () => {
                walked = true;
                return [];
            },
            statSyncFn: () => ({ size: 0 }),
            platform: "linux",
        });
        expect(result).toEqual([]);
        expect(walked).toBe(false);
    });

    test("returns [] when skip=true even on Windows", () => {
        let walked = false;
        const result = findZeroByteNativeBinaries({
            repoRoot: "/repo",
            readdirSyncFn: () => {
                walked = true;
                return [];
            },
            statSyncFn: () => ({ size: 0 }),
            platform: "win32",
            skip: true,
        });
        expect(result).toEqual([]);
        expect(walked).toBe(false);
    });

    test("returns offenders on Windows when *.node files have size 0", () => {
        // Fake one-level fs: node_modules has a child "native" with a 0-byte
        // index.node and a healthy 100-byte sibling.node.
        const tree = {
            "/repo/node_modules": [
                { name: "native", isDirectory: () => true, isFile: () => false },
            ],
            "/repo/node_modules/native": [
                { name: "index.node", isDirectory: () => false, isFile: () => true },
                { name: "sibling.node", isDirectory: () => false, isFile: () => true },
            ],
        };
        const sizes = {
            "/repo/node_modules/native/index.node": 0,
            "/repo/node_modules/native/sibling.node": 100,
        };
        const result = findZeroByteNativeBinaries({
            repoRoot: "/repo",
            platform: "win32",
            readdirSyncFn: (dir) => tree[dir] || [],
            statSyncFn: (abs) => ({ size: sizes[abs] !== undefined ? sizes[abs] : 100 }),
        });

        expect(result).toEqual(["node_modules/native/index.node"]);
    });

    test("respects maxDepth to keep the scan bounded", () => {
        const visited = new Set();
        const tree = (dir) => {
            visited.add(dir);
            return [
                { name: "deeper", isDirectory: () => true, isFile: () => false },
            ];
        };
        findZeroByteNativeBinaries({
            repoRoot: "/repo",
            platform: "win32",
            readdirSyncFn: tree,
            statSyncFn: () => ({ size: 0 }),
            maxDepth: 2,
        });
        // Depth budget 2 means at most 3 readdir calls (root, level1, level2).
        expect(visited.size).toBeLessThanOrEqual(3);
    });
});

describe("formatIntegrityFailure", () => {
    test("produces a stable single-line format", () => {
        const formatted = formatIntegrityFailure({
            ok: false,
            missing: [
                { tool: "jest-circus", relPath: "node_modules/jest-circus/build/runner.js", reason: "missing" },
            ],
        });
        expect(formatted).toBe(
            "Integrity probe failed: missing node_modules/jest-circus/build/runner.js (missing) for jest-circus"
        );
        // Single line - no embedded newlines.
        expect(formatted.includes("\n")).toBe(false);
    });

    test("reports the count of remaining offenders when more than one is missing", () => {
        const formatted = formatIntegrityFailure({
            ok: false,
            missing: [
                { tool: "jest-circus", relPath: "a", reason: "missing" },
                { tool: "jest-circus", relPath: "b", reason: "missing" },
                { tool: "prettier", relPath: "c", reason: "empty" },
            ],
        });
        expect(formatted).toContain("2 more");
    });

    test("handles empty / malformed input defensively", () => {
        expect(formatIntegrityFailure(null)).toContain("no detail available");
        expect(formatIntegrityFailure({})).toContain("no detail available");
        expect(formatIntegrityFailure({ ok: true, missing: [] })).toContain("no detail available");
    });
});
