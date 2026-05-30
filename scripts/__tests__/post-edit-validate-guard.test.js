/**
 * @fileoverview Tests for scripts/hooks/post-edit-validate-guard.js -- the
 * generalized, advisory PostToolUse guard that runs the fast, read-only
 * validators relevant to a just-edited file (closing the edit-time gap that let
 * an npm-packaging regression slip through to the native git hook).
 *
 * The guard's child-process spawns are injectable, so these tests never spawn a
 * real validator and are OS-independent.
 */

"use strict";

const path = require("path");

const {
  ACTIVE_ENV,
  isPackagingRelevant,
  isDocQualityRelevant,
  looksLikeYamlPath,
  buildDispatchTable,
  resolveRepoRoot,
  toRepoRelativePosix,
  runValidator,
  evaluate,
  buildMessage,
  run
} = require("../hooks/post-edit-validate-guard.js");

const REPO_ROOT = path.resolve(__dirname, "..", "..");

/** Build a fake spawnSync-shaped result. */
function spawnResult({ status = 0, stdout = "", stderr = "", error = null }) {
  return { status, stdout, stderr, error };
}

/** A spawn impl that returns a fixed result and records its calls. */
function fakeSpawn(result) {
  const calls = [];
  const impl = (command, args, options) => {
    calls.push({ command, args, options });
    return typeof result === "function" ? result(command, args, options) : result;
  };
  impl.calls = calls;
  return impl;
}

describe("path classification", () => {
  test.each([
    ["package.json", true],
    [".npmignore", true],
    ["Editor/Analyzers/WallstopStudios.DxMessaging.Analyzer.dll", true],
    ["Editor/Analyzers/WallstopStudios.DxMessaging.Analyzer.dll.meta", true],
    ["Runtime/Foo.cs.meta", true],
    ["Runtime/Foo.cs", false],
    ["README.md", false],
    [".github/workflows/ci.yml", false]
  ])("isPackagingRelevant(%s) === %s", (rel, expected) => {
    expect(isPackagingRelevant(rel)).toBe(expected);
  });

  test.each([
    ["Runtime/Foo.cs", true],
    ["docs/guide.md", true],
    ["docs/guide.markdown", true],
    ["package.json", false],
    ["Editor/Analyzers/x.dll", false],
    ["a.yml", false]
  ])("isDocQualityRelevant(%s) === %s", (rel, expected) => {
    expect(isDocQualityRelevant(rel)).toBe(expected);
  });

  test.each([
    [".github/workflows/ci.yml", true],
    [".yamllint.yaml", true],
    ["package.json", false],
    ["Foo.cs", false]
  ])("looksLikeYamlPath(%s) === %s", (rel, expected) => {
    expect(looksLikeYamlPath(rel)).toBe(expected);
  });
});

describe("dispatch table", () => {
  test("has npm-packaging and doc-quality entries with required shape", () => {
    const table = buildDispatchTable();
    const ids = table.map((e) => e.id);
    expect(ids).toEqual(expect.arrayContaining(["npm-packaging", "doc-quality"]));

    for (const entry of table) {
      expect(typeof entry.matches).toBe("function");
      expect(typeof entry.remediation).toBe("string");
      expect(entry.remediation.length).toBeGreaterThan(0);
      expect(Array.isArray(entry.validators)).toBe(true);
      expect(entry.validators.length).toBeGreaterThan(0);
      for (const v of entry.validators) {
        expect(typeof v.label).toBe("string");
        expect(Array.isArray(v.args("/abs/path"))).toBe(true);
      }
    }
  });

  test("npm-packaging validator invokes validate-npm-meta --check (not a single-file arg)", () => {
    const table = buildDispatchTable();
    const packaging = table.find((e) => e.id === "npm-packaging");
    const args = packaging.validators[0].args("/abs/Editor/Analyzers/x.dll");
    expect(args).toEqual(["scripts/validate-npm-meta.js", "--check"]);
  });

  test("doc-quality validators are file-scoped read-only doc checks", () => {
    const table = buildDispatchTable();
    const doc = table.find((e) => e.id === "doc-quality");
    const labels = doc.validators.map((v) => v.label);
    expect(labels).toEqual(expect.arrayContaining(["docs-ascii", "docs-code-patterns", "docs-prose"]));
    expect(doc.validators[0].args("/abs/x.md")[1]).toBe("/abs/x.md");
  });
});

describe("runValidator", () => {
  const validator = { label: "demo", args: (abs) => ["scripts/x.js", abs] };

  test("status 0 => ok, no detail", () => {
    const spawn = fakeSpawn(spawnResult({ status: 0, stdout: "ok" }));
    expect(runValidator(validator, "/abs/x", REPO_ROOT, spawn)).toEqual({
      label: "demo",
      ok: true,
      detail: ""
    });
  });

  test("non-zero status => not ok, detail is the output tail", () => {
    const spawn = fakeSpawn(spawnResult({ status: 1, stdout: "line1\nline2\nBOOM" }));
    const out = runValidator(validator, "/abs/x", REPO_ROOT, spawn);
    expect(out.ok).toBe(false);
    expect(out.detail).toContain("BOOM");
  });

  test("ENOENT (node missing) => ok (advisory guards never manufacture infra noise)", () => {
    const spawn = fakeSpawn(spawnResult({ status: null, error: { code: "ENOENT" } }));
    expect(runValidator(validator, "/abs/x", REPO_ROOT, spawn).ok).toBe(true);
  });

  test("passes the file path through args and sets the re-entrancy env", () => {
    const spawn = fakeSpawn(spawnResult({ status: 0 }));
    runValidator(validator, "/abs/here", REPO_ROOT, spawn);
    expect(spawn.calls[0].args).toEqual(["scripts/x.js", "/abs/here"]);
    expect(spawn.calls[0].options.env[ACTIVE_ENV]).toBe("1");
  });
});

describe("evaluate", () => {
  const table = [
    { id: "pkg", matches: (r) => r === "package.json", remediation: "fix pkg", validators: [{ label: "npm-meta", args: () => ["scripts/validate-npm-meta.js", "--check"] }] },
    { id: "doc", matches: (r) => /\.md$/.test(r), remediation: "fix docs", validators: [{ label: "ascii", args: (a) => ["scripts/validate-docs-ascii.js", a] }] }
  ];

  test("returns a report only for matching entries whose validator failed", () => {
    const spawn = fakeSpawn(spawnResult({ status: 1, stdout: "violation" }));
    const reports = evaluate("package.json", "/abs/package.json", REPO_ROOT, { spawnImpl: spawn, table });
    expect(reports).toHaveLength(1);
    expect(reports[0].id).toBe("pkg");
    expect(reports[0].failures[0].label).toBe("npm-meta");
  });

  test("passing validators => no reports", () => {
    const spawn = fakeSpawn(spawnResult({ status: 0 }));
    expect(evaluate("package.json", "/abs/package.json", REPO_ROOT, { spawnImpl: spawn, table })).toEqual([]);
  });

  test("non-matching path => no validators run, no reports", () => {
    const spawn = fakeSpawn(spawnResult({ status: 1 }));
    expect(evaluate("src/Foo.cs", "/abs/src/Foo.cs", REPO_ROOT, { spawnImpl: spawn, table })).toEqual([]);
    expect(spawn.calls).toHaveLength(0);
  });
});

describe("buildMessage", () => {
  test("no reports => null", () => {
    expect(buildMessage("package.json", [])).toBeNull();
  });

  test("reports => message naming the file, the entry id, and remediation", () => {
    const msg = buildMessage("package.json", [
      { id: "npm-packaging", remediation: "Run validate-npm-meta.", failures: [{ label: "validate-npm-meta", detail: "missing .meta" }] }
    ]);
    expect(msg).toContain("package.json");
    expect(msg).toContain("[npm-packaging]");
    expect(msg).toContain("validate-npm-meta");
    expect(msg).toContain("missing .meta");
    expect(msg).toContain("Run validate-npm-meta.");
  });
});

describe("run (PostToolUse entry)", () => {
  let writeSpy;
  let written;

  beforeEach(() => {
    written = [];
    writeSpy = jest.spyOn(process.stdout, "write").mockImplementation((chunk) => {
      written.push(String(chunk));
      return true;
    });
  });

  afterEach(() => {
    writeSpy.mockRestore();
  });

  const payloadFor = (absPath) =>
    JSON.stringify({ hook_event_name: "PostToolUse", tool_input: { file_path: absPath } });

  test("re-entrancy: ACTIVE_ENV=1 => exit 0, silent", () => {
    const spawn = fakeSpawn(spawnResult({ status: 1 }));
    const code = run(payloadFor(path.join(REPO_ROOT, "package.json")), { [ACTIVE_ENV]: "1" }, { spawnImpl: spawn });
    expect(code).toBe(0);
    expect(written).toHaveLength(0);
    expect(spawn.calls).toHaveLength(0);
  });

  test("invalid JSON => exit 0, silent", () => {
    expect(run("{not json", {}, {})).toBe(0);
    expect(written).toHaveLength(0);
  });

  test("missing file_path => exit 0, silent", () => {
    expect(run(JSON.stringify({ tool_input: {} }), {}, {})).toBe(0);
    expect(written).toHaveLength(0);
  });

  test("YAML file is skipped (owned by the yaml guard) even though it matches nothing here", () => {
    const spawn = fakeSpawn(spawnResult({ status: 1 }));
    const code = run(payloadFor(path.join(REPO_ROOT, ".yamllint.yaml")), {}, { spawnImpl: spawn });
    expect(code).toBe(0);
    expect(spawn.calls).toHaveLength(0);
  });

  test("non-matching existing file => exit 0, silent, no spawn", () => {
    const spawn = fakeSpawn(spawnResult({ status: 1 }));
    const code = run(payloadFor(path.join(REPO_ROOT, "LICENSE.md")), {}, { spawnImpl: spawn, table: [
      { id: "pkg", matches: (r) => r === "package.json", remediation: "x", validators: [{ label: "a", args: () => ["s.js"] }] }
    ] });
    expect(code).toBe(0);
    expect(spawn.calls).toHaveLength(0);
  });

  test("path outside the repo => exit 0, silent", () => {
    const spawn = fakeSpawn(spawnResult({ status: 1 }));
    const code = run(payloadFor("/etc/hosts"), {}, { spawnImpl: spawn });
    expect(code).toBe(0);
    expect(spawn.calls).toHaveLength(0);
  });

  test("packaging edit with a FAILING validator => exit 0 and an advisory systemMessage", () => {
    const spawn = fakeSpawn(spawnResult({ status: 1, stdout: "Missing .meta in tarball" }));
    const code = run(payloadFor(path.join(REPO_ROOT, "package.json")), {}, { spawnImpl: spawn });
    expect(code).toBe(0);
    expect(written).toHaveLength(1);
    const out = JSON.parse(written[0]);
    expect(out.hookSpecificOutput.hookEventName).toBe("PostToolUse");
    expect(out.systemMessage).toContain("package.json");
    expect(out.systemMessage).toContain("npm-packaging");
    expect(out.hookSpecificOutput.additionalContext).toBe(out.systemMessage);
  });

  test("packaging edit with a PASSING validator => exit 0, silent", () => {
    const spawn = fakeSpawn(spawnResult({ status: 0 }));
    const code = run(payloadFor(path.join(REPO_ROOT, "package.json")), {}, { spawnImpl: spawn });
    expect(code).toBe(0);
    expect(written).toHaveLength(0);
  });
});

describe("helpers", () => {
  test("resolveRepoRoot prefers CLAUDE_PROJECT_DIR when set", () => {
    const prev = process.env.CLAUDE_PROJECT_DIR;
    process.env.CLAUDE_PROJECT_DIR = "/tmp/some-root";
    try {
      expect(resolveRepoRoot()).toBe("/tmp/some-root");
    } finally {
      if (prev === undefined) {
        delete process.env.CLAUDE_PROJECT_DIR;
      } else {
        process.env.CLAUDE_PROJECT_DIR = prev;
      }
    }
  });

  test("toRepoRelativePosix returns null for outside-repo paths and POSIX for inside", () => {
    expect(toRepoRelativePosix(REPO_ROOT, "/etc/hosts")).toBeNull();
    expect(toRepoRelativePosix(REPO_ROOT, path.join(REPO_ROOT, "package.json"))).toBe("package.json");
  });
});
