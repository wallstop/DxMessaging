/**
 * @fileoverview Tests for validate-docs-out-of-tree-links.js. The validator
 * guards against the failure mode that took down `Validate Documentation
 * Build / Build documentation (strict mode)`: relative links from docs/ that
 * climb above the docs/ tree (for example `../../.github/workflows/...`) are
 * invalid in mkdocs strict mode because mkdocs only resolves links inside
 * the docs/ tree. Such references must use the absolute `https://github.com/.../blob/master/...`
 * URL instead.
 */

"use strict";

const fs = require("fs");
const os = require("os");
const path = require("path");

const validator = require("../validate-docs-out-of-tree-links");

const FIXTURES = [
  {
    name: "in-tree sibling link",
    relativePath: "docs/guides/foo.md",
    content: "See [other](./other.md) for context.",
    expectedViolations: 0
  },
  {
    name: "in-tree parent-then-sibling link",
    relativePath: "docs/runbooks/foo.md",
    content: "See [reference](../reference/runtime-settings.md).",
    expectedViolations: 0
  },
  {
    name: "absolute https GitHub URL is OK",
    relativePath: "docs/runbooks/unity-runners-after-transfer.md",
    content:
      "Use [unstick-run.yml](https://github.com/Ambiguous-Interactive/DxMessaging/blob/master/.github/workflows/unstick-run.yml) for manual recovery.",
    expectedViolations: 0
  },
  {
    name: "absolute https external URL is OK",
    relativePath: "docs/runbooks/foo.md",
    content:
      "See [GitHub Community Discussion #186811](https://github.com/orgs/community/discussions/186811).",
    expectedViolations: 0
  },
  {
    name: "mailto link is OK",
    relativePath: "docs/index.md",
    content: "Email [team](mailto:team@example.com)",
    expectedViolations: 0
  },
  {
    name: "anchor link is OK",
    relativePath: "docs/index.md",
    content: "Jump to [the top](#top)",
    expectedViolations: 0
  },
  {
    name: "FAILS for ../../.github/workflows/... escape",
    relativePath: "docs/runbooks/foo.md",
    content: "Recovery: [unstick-run.yml](../../.github/workflows/unstick-run.yml).",
    expectedViolations: 1
  },
  {
    name: "FAILS for ../../scripts/... escape",
    relativePath: "docs/guides/foo.md",
    content: "See [helper](../../scripts/doctor.js) for diagnostics.",
    expectedViolations: 1
  },
  {
    name: "FAILS for ../../../Runtime/... escape from nested docs subdir",
    relativePath: "docs/reference/sub/foo.md",
    content: "Look at [code](../../../Runtime/Core/MessageBus.cs).",
    expectedViolations: 1
  },
  {
    name: "FAILS for ../README.md escape from top-level docs file",
    relativePath: "docs/index.md",
    content: "See [readme](../README.md) at repo root.",
    expectedViolations: 1
  },
  {
    name: "FAILS for multiple violations in one file",
    relativePath: "docs/runbooks/foo.md",
    content: [
      "First: [a](../../.github/workflows/unstick-run.yml)",
      "Second: [b](../../scripts/doctor.js)",
      "Third (in-tree, OK): [c](./other.md)"
    ].join("\n"),
    expectedViolations: 2
  },
  {
    name: "fenced code blocks are not scanned",
    relativePath: "docs/runbooks/foo.md",
    content: [
      "```text",
      "[fake](../../scripts/should-be-ignored.js)",
      "```",
      "",
      "Real OK link [other](./other.md)"
    ].join("\n"),
    expectedViolations: 0
  },
  {
    name: "FAILS for full reference-style link with out-of-tree definition",
    relativePath: "docs/runbooks/foo.md",
    content: [
      "Recovery: [unstick-run.yml][unstick].",
      "",
      "[unstick]: ../../.github/workflows/unstick-run.yml"
    ].join("\n"),
    expectedViolations: 1
  },
  {
    name: "FAILS for collapsed reference link with out-of-tree definition",
    relativePath: "docs/runbooks/foo.md",
    content: [
      "Recovery: [unstick][].",
      "",
      "[unstick]: ../../.github/workflows/unstick-run.yml"
    ].join("\n"),
    expectedViolations: 1
  },
  {
    name: "FAILS for shortcut reference link with out-of-tree definition",
    relativePath: "docs/runbooks/foo.md",
    content: [
      "Recovery: [unstick] right here.",
      "",
      "[unstick]: ../../.github/workflows/unstick-run.yml"
    ].join("\n"),
    expectedViolations: 1
  },
  {
    name: "in-tree reference definitions do NOT fail",
    relativePath: "docs/runbooks/foo.md",
    content: ["Recovery: [sibling][].", "", "[sibling]: ./other.md"].join("\n"),
    expectedViolations: 0
  },
  {
    name: "reference definition wrapped in <...> URL form is scanned",
    relativePath: "docs/runbooks/foo.md",
    content: ["See [bad][].", "", "[bad]: <../../scripts/doctor.js>"].join("\n"),
    expectedViolations: 1
  },
  {
    name: "reference definition with title is scanned",
    relativePath: "docs/runbooks/foo.md",
    content: ["See [bad][].", "", '[bad]: ../../scripts/doctor.js "title"'].join("\n"),
    expectedViolations: 1
  },
  {
    name: "absolute https reference definition is OK",
    relativePath: "docs/runbooks/foo.md",
    content: [
      "See [gh][].",
      "",
      "[gh]: https://github.com/Ambiguous-Interactive/DxMessaging/blob/master/scripts/doctor.js"
    ].join("\n"),
    expectedViolations: 0
  },
  {
    name: "inline backtick code spans containing escape are not scanned",
    relativePath: "docs/runbooks/foo.md",
    content:
      "Use ``[doc](../../scripts/should-be-ignored.js)`` in inline code is just a markdown sample.",
    expectedViolations: 0
  },
  {
    name: "single-tick inline code span is not scanned",
    relativePath: "docs/runbooks/foo.md",
    content: "The literal `[ref](../../scripts/doctor.js)` is an example -- ignore it.",
    expectedViolations: 0
  },
  // Round-3 NIT-D coverage: mixed-density / nested-density inline code.
  // The validator's `stripInlineCodeSpans` is intentionally scoped to
  // single-backtick spans on a single line (see the comment on
  // INLINE_CODE_SPAN_RE in scripts/validate-docs-out-of-tree-links.js).
  // Multi-backtick spans -- which CommonMark allows so authors can embed
  // single backticks in their inline code -- are out of scope by design.
  // These fixtures lock in the actual behavior so the trade-off is
  // explicit. If the validator is later extended to handle multi-tick
  // spans, update these fixtures (and the validator comment) together.
  {
    // Mixed-density nested case: outer wrapper `` (double-tick), inner
    // `` ` `` (single-tick) wrapping a link. The validator's single-tick
    // regex matches the spans `` `the inner ` `` and `` ` literal` ``,
    // leaving the LINK ITSELF unblanked between them. This is the
    // documented out-of-scope shape -- multi-backtick spans are not
    // detected (see the comment on INLINE_CODE_SPAN_RE in
    // scripts/validate-docs-out-of-tree-links.js). The link DOES
    // surface as a violation. If a future change extends the validator
    // to handle multi-tick spans, flip this fixture to 0 and update the
    // validator comment.
    name: "MIXED-DENSITY (out-of-scope): double-tick wrapper around single-tick + link surfaces a violation",
    relativePath: "docs/runbooks/foo.md",
    content: "See ``the inner `[ref](../../scripts/doctor.js)` literal`` for context.",
    expectedViolations: 1
  },
  {
    // Double-tick wrapper around a bare link, no inner ticks. The
    // single-tick regex matches the middle slice `..[ref](...)..` of
    // the outer double-tick wrapper (second `` ` `` of opener to first
    // `` ` `` of closer), which ENCLOSES the link. So this shape DOES
    // get suppressed -- it "happens to work" via the same mechanism as
    // the existing `[doc](../../scripts/should-be-ignored.js)` fixture
    // immediately above this block. Documented here so future authors
    // do not assume the validator has formal multi-tick support.
    name: "MIXED-DENSITY (out-of-scope): double-tick wrapper around a bare link is suppressed by regex coincidence",
    relativePath: "docs/runbooks/foo.md",
    content: "Sample: ``[ref](../../scripts/doctor.js)`` -- treat as inline code.",
    expectedViolations: 0
  },
  {
    // Mixed density where the link is OUTSIDE every backtick span. The
    // surrounding single-tick code spans should not affect detection.
    name: "MIXED-DENSITY: single-tick code on either side does not suppress a real out-of-tree link",
    relativePath: "docs/runbooks/foo.md",
    content: "Compare `foo()` to [bad](../../scripts/doctor.js) and `bar()`.",
    expectedViolations: 1
  },
  {
    name: "4-space-indented code block is not scanned",
    relativePath: "docs/runbooks/foo.md",
    content: [
      "Before block:",
      "",
      "    [example](../../scripts/should-be-ignored.js)",
      "    more code",
      "",
      "After block: [other](./other.md)"
    ].join("\n"),
    expectedViolations: 0
  },
  {
    name: "tab-indented code block is not scanned",
    relativePath: "docs/runbooks/foo.md",
    content: [
      "Tabbed:",
      "",
      "\t[example](../../scripts/should-be-ignored.js)",
      "",
      "Outside: [other](./other.md)"
    ].join("\n"),
    expectedViolations: 0
  }
];

describe("validate-docs-out-of-tree-links scanContent", () => {
  let tempDir;
  beforeAll(() => {
    tempDir = fs.mkdtempSync(path.join(os.tmpdir(), "docs-out-of-tree-"));
  });
  afterAll(() => {
    fs.rmSync(tempDir, { recursive: true, force: true });
  });

  test.each(FIXTURES)("$name", ({ relativePath, content, expectedViolations }) => {
    // Build a synthetic docs root in tempDir so escapesDocsTree compares
    // against THIS tree, not the repo's real docs/. The validator uses
    // `validator.DOCS_ROOT` as the boundary, so we point relative paths
    // at the real docs/ by faking the file path under <repo>/docs/.
    const realDocsRoot = validator.DOCS_ROOT;
    const fakeAbsPath = path.join(realDocsRoot, "..", relativePath.replace(/^docs\//, "docs/"));
    // Make sure the file we hand the scanner actually points into the
    // real docs/ subtree (we never write to it on disk; scanContent
    // only needs the path to resolve the link's destination).
    const violations = validator.scanContent(fakeAbsPath, content);
    expect(violations).toHaveLength(expectedViolations);
    for (const v of violations) {
      expect(v.reason).toMatch(/full https:\/\/github\.com/);
    }
  });
});

describe("validate-docs-out-of-tree-links isDocsMarkdown", () => {
  test("docs markdown is recognized", () => {
    expect(validator.isDocsMarkdown(path.join(validator.DOCS_ROOT, "runbooks", "x.md"))).toBe(true);
  });
  test("non-docs markdown is rejected", () => {
    expect(validator.isDocsMarkdown(path.join(validator.REPO_ROOT, "README.md"))).toBe(false);
  });
  test("docs non-markdown is rejected", () => {
    expect(validator.isDocsMarkdown(path.join(validator.DOCS_ROOT, "runbooks", "x.txt"))).toBe(
      false
    );
  });
});

describe("validate-docs-out-of-tree-links real docs tree", () => {
  test("the live docs/ tree has no out-of-tree relative links", () => {
    const files = validator.listAllDocsFiles();
    expect(files.length).toBeGreaterThan(0);
    const violations = [];
    for (const file of files) {
      violations.push(...validator.scanFile(file));
    }
    if (violations.length > 0) {
      const lines = violations.map(
        (v) => `${path.relative(validator.REPO_ROOT, v.file)}:${v.line} -> ${v.url}`
      );
      throw new Error(`Found ${violations.length} out-of-tree link(s):\n${lines.join("\n")}`);
    }
    expect(violations).toHaveLength(0);
  });
});
