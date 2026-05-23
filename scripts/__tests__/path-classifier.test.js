/**
 * @fileoverview Tests for scripts/lib/path-classifier.js.
 *
 * The classifier is a pure module used by the integrity gate and the tier
 * dispatcher to decide whether a captured runner path belongs to the
 * repository's node_modules tree, the isolated managed-Jest cache, or
 * neither. The boundary helpers (`normalizeForPathComparison`,
 * `isPathInsideDirectory`) were previously defined inside
 * `scripts/run-managed-jest.js`; these tests now own them as the canonical
 * specification.
 *
 * @cross-platform-regression -- this marker requires the file to be gated on
 * ubuntu/windows/macos via the targeted step in
 * .github/workflows/cross-platform-preflight.yml; enforced by
 * scripts/__tests__/cross-platform-preflight-coverage.test.js.
 */

"use strict";

const path = require("path");

const {
  PATH_CLASS_REPO,
  PATH_CLASS_ISOLATED,
  PATH_CLASS_UNKNOWN,
  normalizeForPathComparison,
  isPathInsideDirectory,
  classifyCapturedPath,
  toPosixPath,
  toRepoPosixRelative
} = require("../lib/path-classifier");

describe("normalizeForPathComparison", () => {
  test("returns an absolute, resolved path for a relative input", () => {
    const result = normalizeForPathComparison("./foo");
    expect(path.isAbsolute(result)).toBe(true);
  });

  test("idempotent: normalizing twice yields the same value", () => {
    const first = normalizeForPathComparison(__dirname);
    const second = normalizeForPathComparison(first);
    expect(second).toBe(first);
  });

  test("does not throw on a nonexistent path", () => {
    const phantom = path.join(__dirname, "this-path-does-not-exist-1234567890");
    expect(() => normalizeForPathComparison(phantom)).not.toThrow();
  });
});

describe("isPathInsideDirectory", () => {
  test("returns true when filePath is the directory itself", () => {
    expect(isPathInsideDirectory(__dirname, __dirname)).toBe(true);
  });

  test("returns true for a descendant path", () => {
    const child = path.join(__dirname, "fake-child.js");
    expect(isPathInsideDirectory(child, __dirname)).toBe(true);
  });

  test("returns false for a sibling path", () => {
    const sibling = path.join(path.dirname(__dirname), "sibling.js");
    expect(isPathInsideDirectory(sibling, __dirname)).toBe(false);
  });

  test("returns false for the parent directory", () => {
    expect(isPathInsideDirectory(path.dirname(__dirname), __dirname)).toBe(false);
  });
});

describe("classifyCapturedPath", () => {
  const repoNodeModules = path.resolve("/repo/node_modules");
  const isolatedCacheRoot = path.resolve("/tmp/dxmessaging-managed-jest");

  test("returns 'unknown' for null/undefined input", () => {
    expect(classifyCapturedPath(null, { repoNodeModules, isolatedCacheRoot })).toBe(
      PATH_CLASS_UNKNOWN
    );
    expect(classifyCapturedPath(undefined, { repoNodeModules, isolatedCacheRoot })).toBe(
      PATH_CLASS_UNKNOWN
    );
  });

  test("returns 'unknown' for empty / non-string input", () => {
    expect(classifyCapturedPath("", { repoNodeModules, isolatedCacheRoot })).toBe(
      PATH_CLASS_UNKNOWN
    );
    expect(classifyCapturedPath(42, { repoNodeModules, isolatedCacheRoot })).toBe(
      PATH_CLASS_UNKNOWN
    );
    expect(classifyCapturedPath({}, { repoNodeModules, isolatedCacheRoot })).toBe(
      PATH_CLASS_UNKNOWN
    );
  });

  test("returns 'repo' for a path under repoNodeModules", () => {
    const result = classifyCapturedPath(
      path.join(repoNodeModules, "jest-circus", "build", "runner.js"),
      { repoNodeModules, isolatedCacheRoot }
    );
    expect(result).toBe(PATH_CLASS_REPO);
  });

  test("returns 'isolated' for a path under isolatedCacheRoot", () => {
    const result = classifyCapturedPath(
      path.join(
        isolatedCacheRoot,
        "jest_30.3.0",
        "node_modules",
        "jest-circus",
        "build",
        "runner.js"
      ),
      { repoNodeModules, isolatedCacheRoot }
    );
    expect(result).toBe(PATH_CLASS_ISOLATED);
  });

  test("returns 'unknown' for a path outside both trees", () => {
    const outside = path.resolve("/somewhere/else/runner.js");
    const result = classifyCapturedPath(outside, { repoNodeModules, isolatedCacheRoot });
    expect(result).toBe(PATH_CLASS_UNKNOWN);
  });

  test("handles Windows-style backslashes when the boundary is also Windows-style", () => {
    // Constructing a Windows-shape repo node_modules requires platform-
    // specific path-resolution. We can still verify the classifier handles
    // backslash inputs without throwing and falls into the correct
    // bucket on the current platform via path.resolve normalization.
    const winishRepo = path.resolve(
      "D:/Code/Packages/com.wallstop-studios.dxmessaging/node_modules".replace(/\\/g, "/")
    );
    const winishCapture =
      "D:\\Code\\Packages\\com.wallstop-studios.dxmessaging\\node_modules\\jest-circus\\build\\runner.js";

    // On POSIX, path.resolve does not collapse backslashes inside the
    // captured string; classification therefore returns "unknown" because
    // the captured path does not normalize under repoNodeModules. The
    // test asserts the function is stable (no throw, returns a string in
    // the known vocabulary), not a specific bucket.
    const result = classifyCapturedPath(winishCapture, {
      repoNodeModules: winishRepo,
      isolatedCacheRoot
    });
    expect([PATH_CLASS_REPO, PATH_CLASS_ISOLATED, PATH_CLASS_UNKNOWN]).toContain(result);
  });

  test("repoNodeModules wins precedence when both bounds match", () => {
    // Construct a synthetic case where the isolatedCacheRoot is a parent
    // of repoNodeModules (artificial but proves the precedence rule).
    const overlap = path.resolve("/shared");
    const repo = path.join(overlap, "node_modules");
    const isolated = overlap;
    const captured = path.join(repo, "jest-circus", "runner.js");
    const result = classifyCapturedPath(captured, {
      repoNodeModules: repo,
      isolatedCacheRoot: isolated
    });
    expect(result).toBe(PATH_CLASS_REPO);
  });

  test("missing bounds: returns 'unknown' when both bounds are absent", () => {
    expect(classifyCapturedPath("/some/path", {})).toBe(PATH_CLASS_UNKNOWN);
    expect(
      classifyCapturedPath("/some/path", { repoNodeModules: "", isolatedCacheRoot: null })
    ).toBe(PATH_CLASS_UNKNOWN);
  });
});

describe("re-export through run-managed-jest", () => {
  test("scripts/run-managed-jest.js re-exports normalizeForPathComparison and isPathInsideDirectory", () => {
    const wrapper = require("../run-managed-jest");
    expect(typeof wrapper.normalizeForPathComparison).toBe("function");
    expect(typeof wrapper.isPathInsideDirectory).toBe("function");
    // Functional smoke: the re-exports must behave identically.
    expect(wrapper.normalizeForPathComparison(__dirname)).toBe(
      normalizeForPathComparison(__dirname)
    );
    expect(wrapper.isPathInsideDirectory(__filename, __dirname)).toBe(true);
  });

  test("scripts/run-managed-jest.js re-exports toPosixPath and toRepoPosixRelative", () => {
    const wrapper = require("../run-managed-jest");
    expect(typeof wrapper.toPosixPath).toBe("function");
    expect(typeof wrapper.toRepoPosixRelative).toBe("function");
    expect(wrapper.toPosixPath("a\\b\\c")).toBe("a/b/c");
  });
});

describe("toPosixPath", () => {
  test("converts Windows-style backslashes to forward slashes", () => {
    expect(toPosixPath("D:\\Code\\foo\\bar.js")).toBe("D:/Code/foo/bar.js");
  });

  test("is idempotent on POSIX input", () => {
    expect(toPosixPath("/usr/local/bin")).toBe("/usr/local/bin");
    expect(toPosixPath("a/b/c")).toBe("a/b/c");
    expect(toPosixPath(toPosixPath("a\\b\\c"))).toBe("a/b/c");
  });

  test("returns empty string for null / undefined (no 'undefined' leak)", () => {
    // Regression: when this helper was inlined into warnFn/console.warn
    // template literals, returning the original undefined produced the
    // literal string "undefined" in log output. Mapping to "" keeps the
    // interpolation safe even when the upstream value was unset.
    expect(toPosixPath(null)).toBe("");
    expect(toPosixPath(undefined)).toBe("");
    expect(`runner ${toPosixPath(undefined)}`).toBe("runner ");
    expect(`runner ${toPosixPath(null)}`).toBe("runner ");
  });

  test("coerces non-null primitives via String() with separator swap", () => {
    expect(toPosixPath(42)).toBe("42");
    expect(toPosixPath(true)).toBe("true");
    // Object coercion goes through Object#toString -> "[object Object]";
    // no backslashes there, so the result is the coerced string verbatim.
    expect(toPosixPath({ a: 1 })).toBe("[object Object]");
  });

  test("handles mixed-separator input", () => {
    expect(toPosixPath("a\\b/c\\d")).toBe("a/b/c/d");
  });

  test("returns empty string unchanged (no separators present)", () => {
    expect(toPosixPath("")).toBe("");
  });
});

describe("toRepoPosixRelative", () => {
  test("produces POSIX-relative path for an input inside repoRoot", () => {
    const repo = path.resolve("/repo");
    const inside = path.join(repo, "node_modules", "foo", "bar.js");
    expect(toRepoPosixRelative(inside, repo)).toBe("node_modules/foo/bar.js");
  });

  test("produces POSIX-absolute path for an input outside repoRoot", () => {
    const repo = path.resolve("/repo");
    const outside = path.resolve("/elsewhere/foo.js");
    const result = toRepoPosixRelative(outside, repo);
    // Outside-of-repo paths fall back to POSIX-absolute form: any
    // backslashes are converted, and the input is preserved as-is.
    expect(result).toBe(toPosixPath(outside));
    expect(result.includes("\\")).toBe(false);
  });

  test("returns non-string inputs unchanged", () => {
    expect(toRepoPosixRelative(null, "/repo")).toBe(null);
    expect(toRepoPosixRelative("/repo/foo.js", null)).toBe("/repo/foo.js");
    expect(toRepoPosixRelative(42, "/repo")).toBe(42);
  });

  test("repoRoot itself maps to a POSIX-absolute fallback (empty relative)", () => {
    const repo = path.resolve("/repo");
    // path.relative(repo, repo) === ""; toRepoPosixRelative treats that
    // as "outside" (no useful relative form) and emits the POSIX
    // fallback, which is the repo root itself.
    expect(toRepoPosixRelative(repo, repo)).toBe(toPosixPath(repo));
  });
});
