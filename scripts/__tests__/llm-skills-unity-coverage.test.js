/**
 * @fileoverview Contract test for the Unity + GitHub Actions skill coverage.
 *
 * Phase 4A creates a fixed set of skill pages under .llm/skills/unity/ and one
 * ported page under .llm/skills/github-actions/. The headless workflow, the
 * license bootstrap walkthrough, and the devcontainer cache contract docs all
 * live in those skill files (the .llm/context.md additions in Phase 4B link
 * directly to them). We lock the file paths and basic shape here so any
 * silent rename, deletion, or accidental wipe fails loudly.
 *
 * Note: this test will FAIL until Phase 4A finishes writing the 7 new skill
 * files. That is the intended contract behavior — the test is correct, and
 * will pass automatically once Phase 4A lands.
 */

"use strict";

const fs = require("fs");
const path = require("path");

const REPO_ROOT = path.resolve(__dirname, "..", "..");
const SKILLS_DIR = path.join(REPO_ROOT, ".llm", "skills");
const CONTEXT_PATH = path.join(REPO_ROOT, ".llm", "context.md");

const NEW_UNITY_SKILLS = [
  "headless-test-runner",
  "unity-license-bootstrap",
  "upm-test-harness",
  "devcontainer-cache-contract",
  "unity-ci-matrix",
  "unity-perf-test-isolation"
];

const NEW_GITHUB_ACTIONS_SKILL = "github-actions/cicd-devcontainer-workflows.md";

const EXISTING_BASELINE_SKILL = "unity/base-call-contract.md";

describe(".llm/skills unity + github-actions coverage", () => {
  test.each(NEW_UNITY_SKILLS.map((slug) => [slug]))("unity skill page exists: %s.md", (slug) => {
    const absPath = path.join(SKILLS_DIR, "unity", `${slug}.md`);
    expect(fs.existsSync(absPath)).toBe(true);
  });

  test("github-actions skill page cicd-devcontainer-workflows.md exists", () => {
    const absPath = path.join(SKILLS_DIR, NEW_GITHUB_ACTIONS_SKILL);
    expect(fs.existsSync(absPath)).toBe(true);
  });

  test("existing baseline skill (unity/base-call-contract.md) still exists", () => {
    const absPath = path.join(SKILLS_DIR, EXISTING_BASELINE_SKILL);
    expect(fs.existsSync(absPath)).toBe(true);
  });

  test.each(NEW_UNITY_SKILLS.map((slug) => [slug]))("%s.md is non-empty (>1 KB)", (slug) => {
    const absPath = path.join(SKILLS_DIR, "unity", `${slug}.md`);
    if (!fs.existsSync(absPath)) {
      // Surface a clearer failure than a synthetic stat error.
      throw new Error(`Skill ${slug}.md missing — Phase 4A must create it.`);
    }
    const stats = fs.statSync(absPath);
    expect(stats.size).toBeGreaterThan(1024);
  });

  test("github-actions/cicd-devcontainer-workflows.md is non-empty (>1 KB)", () => {
    const absPath = path.join(SKILLS_DIR, NEW_GITHUB_ACTIONS_SKILL);
    if (!fs.existsSync(absPath)) {
      throw new Error(
        "github-actions/cicd-devcontainer-workflows.md missing -- Phase 4A must create it."
      );
    }
    const stats = fs.statSync(absPath);
    expect(stats.size).toBeGreaterThan(1024);
  });

  test.each(NEW_UNITY_SKILLS.map((slug) => [slug]))(
    "%s.md begins with a `# ` heading on the first non-blank line",
    (slug) => {
      const absPath = path.join(SKILLS_DIR, "unity", `${slug}.md`);
      if (!fs.existsSync(absPath)) {
        throw new Error(`Skill ${slug}.md missing — Phase 4A must create it.`);
      }
      const content = fs.readFileSync(absPath, "utf8");
      const firstNonBlank = content.split(/\r?\n/).find((line) => line.trim().length > 0);
      // Frontmatter `---` lines are common in this repo's skill files;
      // accept either a `# ` heading directly OR `---` (frontmatter
      // delimiter) as the first non-blank line.
      expect(firstNonBlank).toMatch(/^(#\s|---\s*$)/);
    }
  );

  test("github-actions/cicd-devcontainer-workflows.md begins with `# ` heading or frontmatter", () => {
    const absPath = path.join(SKILLS_DIR, NEW_GITHUB_ACTIONS_SKILL);
    if (!fs.existsSync(absPath)) {
      throw new Error(
        "github-actions/cicd-devcontainer-workflows.md missing -- Phase 4A must create it."
      );
    }
    const content = fs.readFileSync(absPath, "utf8");
    const firstNonBlank = content.split(/\r?\n/).find((line) => line.trim().length > 0);
    expect(firstNonBlank).toMatch(/^(#\s|---\s*$)/);
  });

  test(".llm/context.md links to each new unity skill by file path", () => {
    const context = fs.readFileSync(CONTEXT_PATH, "utf8");
    for (const slug of NEW_UNITY_SKILLS) {
      // The slug must appear in a path-like reference (skills/unity/...)
      // somewhere in the file. Be tolerant of `./` and bare relative
      // forms.
      const re = new RegExp(`skills/unity/${slug}\\.md`);
      expect(context).toMatch(re);
    }
  });

  test(".llm/context.md links to the github-actions devcontainer skill", () => {
    const context = fs.readFileSync(CONTEXT_PATH, "utf8");
    expect(context).toMatch(/skills\/github-actions\/cicd-devcontainer-workflows\.md/);
  });
});
