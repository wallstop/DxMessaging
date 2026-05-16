/**
 * @fileoverview Tests for validate-repo-identity.js.
 */

"use strict";

const {
  ALLOWED_PACKAGE_ID,
  EXPECTED_REPOSITORY,
  findStaleIdentityReferencesInContent,
  getRepositoryCandidateFiles,
  parseGitFileList,
  validateRepoIdentity
} = require("../validate-repo-identity.js");

const STALE_REPOSITORY = ["wallstop", "DxMessaging"].join("/");
const STALE_REPOSITORY_URL = `https://github.com/${STALE_REPOSITORY}`;
const STALE_DOCS_URL = ["https://wallstop.github.io", "DxMessaging"].join("/");
const STALE_PACKAGE_REPOSITORY = ["wallstop-studios", "com.wallstop-studios.dxmessaging"].join("/");

describe("validate-repo-identity", () => {
  afterEach(() => {
    jest.restoreAllMocks();
  });

  test("detects stale wallstop GitHub URLs", () => {
    const errors = findStaleIdentityReferencesInContent(
      [
        `repository: ${STALE_REPOSITORY_URL}`,
        `changelog: ${STALE_REPOSITORY_URL}/blob/master/CHANGELOG.md`
      ].join("\n"),
      "README.md"
    );

    expect(errors).toEqual(
      expect.arrayContaining([
        expect.objectContaining({
          file: "README.md",
          line: 1,
          value: STALE_REPOSITORY_URL
        }),
        expect.objectContaining({
          file: "README.md",
          line: 2,
          value: `${STALE_REPOSITORY_URL}/blob/master/CHANGELOG.md`
        })
      ])
    );
  });

  test("detects stale GitHub Pages URLs", () => {
    const errors = findStaleIdentityReferencesInContent(
      `docs: ${STALE_DOCS_URL}/getting-started/install/`,
      "docs/index.md"
    );

    expect(errors).toEqual([
      expect.objectContaining({
        file: "docs/index.md",
        line: 1,
        value: `${STALE_DOCS_URL}/getting-started/install/`
      })
    ]);
  });

  test("detects stale repository slugs and old release-drafter guards", () => {
    const errors = findStaleIdentityReferencesInContent(
      [
        `repo: ${STALE_REPOSITORY}`,
        `mirror: ${STALE_PACKAGE_REPOSITORY}`,
        `if: github.repository == '${STALE_REPOSITORY}'`
      ].join("\n"),
      ".github/workflows/release-drafter.yml"
    );

    expect(errors).toEqual(
      expect.arrayContaining([
        expect.objectContaining({
          line: 1,
          value: STALE_REPOSITORY
        }),
        expect.objectContaining({
          line: 2,
          value: STALE_PACKAGE_REPOSITORY
        }),
        expect.objectContaining({
          line: 3,
          value: `github.repository == '${STALE_REPOSITORY}'`
        })
      ])
    );
  });

  test("detects stale Dependabot owner routing", () => {
    const errors = findStaleIdentityReferencesInContent(
      ["assignees:", "  - wallstop", "reviewers:", "  - wallstop"].join("\n"),
      ".github/dependabot.yml"
    );

    expect(errors).toEqual(
      expect.arrayContaining([
        expect.objectContaining({
          type: "stale-dependabot-routing",
          line: 2,
          value: "- wallstop"
        }),
        expect.objectContaining({
          type: "stale-dependabot-routing",
          line: 4,
          value: "- wallstop"
        })
      ])
    );
  });

  test("allows current repository identity and Unity package id", () => {
    const errors = findStaleIdentityReferencesInContent(
      [
        `repo: ${EXPECTED_REPOSITORY}`,
        `package: ${ALLOWED_PACKAGE_ID}`,
        `openupm add ${ALLOWED_PACKAGE_ID}`,
        `https://openupm.com/packages/${ALLOWED_PACKAGE_ID}/`,
        `if: github.repository == '${EXPECTED_REPOSITORY}'`
      ].join("\n"),
      "package.json"
    );

    expect(errors).toHaveLength(0);
  });

  test("validateRepoIdentity returns invalid with stale references", () => {
    jest.spyOn(console, "error").mockImplementation(() => {});

    const result = validateRepoIdentity({
      files: ["README.md"],
      readFileSync: () => STALE_REPOSITORY_URL,
      check: false
    });

    expect(result.valid).toBe(false);
    expect(result.errors).toHaveLength(1);
  });

  test("parseGitFileList normalizes git output", () => {
    expect(parseGitFileList("a\r\nb\n\n")).toEqual(["a", "b"]);
  });

  test("candidate files include tracked, staged, and untracked files", () => {
    const execFileSync = jest
      .fn()
      .mockReturnValueOnce("tracked.md\nshared.md\n")
      .mockReturnValueOnce("staged.yml\nshared.md\n")
      .mockReturnValueOnce("untracked.js\n");

    const files = getRepositoryCandidateFiles(execFileSync);

    expect(files).toEqual(["shared.md", "staged.yml", "tracked.md", "untracked.js"]);
    expect(execFileSync).toHaveBeenCalledWith(
      "git",
      ["ls-files", "--others", "--exclude-standard"],
      expect.any(Object)
    );
  });
});
