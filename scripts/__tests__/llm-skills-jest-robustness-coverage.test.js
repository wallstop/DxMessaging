/**
 * @fileoverview Contract test for the Jest-hook-robustness skill coverage.
 *
 * Phase 4 adds two LLM skill pages (jest-hook-robustness.md and
 * let-tools-resolve-modules.md) plus context.md edits that link to them and
 * surface the new preflight:pre-push and doctor commands. We lock the file
 * paths, link presence, and underlying npm script shape here so any silent
 * rename, deletion, or accidental wipe fails loudly.
 */

"use strict";

const fs = require("fs");
const path = require("path");

const REPO_ROOT = path.resolve(__dirname, "..", "..");
const SKILLS_DIR = path.join(REPO_ROOT, ".llm", "skills");
const CONTEXT_PATH = path.join(REPO_ROOT, ".llm", "context.md");
const PACKAGE_JSON_PATH = path.join(REPO_ROOT, "package.json");

const REQUIRED_SKILL_RELATIVE_PATHS = [
    "scripting/jest-hook-robustness.md",
    "scripting/let-tools-resolve-modules.md",
];

describe(".llm/skills jest-hook-robustness coverage", () => {
    test.each(REQUIRED_SKILL_RELATIVE_PATHS.map((rel) => [rel]))(
        "skill page exists: %s",
        (relativePath) => {
            const absPath = path.join(SKILLS_DIR, relativePath);
            expect(fs.existsSync(absPath)).toBe(true);
        }
    );

    test.each(REQUIRED_SKILL_RELATIVE_PATHS.map((rel) => [rel]))(
        "%s is non-trivial (>1 KB)",
        (relativePath) => {
            const absPath = path.join(SKILLS_DIR, relativePath);
            if (!fs.existsSync(absPath)) {
                throw new Error(
                    `Skill ${relativePath} missing -- Phase 4 must create it.`
                );
            }
            const stats = fs.statSync(absPath);
            expect(stats.size).toBeGreaterThan(1024);
        }
    );

    test.each(REQUIRED_SKILL_RELATIVE_PATHS.map((rel) => [rel]))(
        "%s begins with a `# ` heading or frontmatter on the first non-blank line",
        (relativePath) => {
            const absPath = path.join(SKILLS_DIR, relativePath);
            if (!fs.existsSync(absPath)) {
                throw new Error(
                    `Skill ${relativePath} missing -- Phase 4 must create it.`
                );
            }
            const content = fs.readFileSync(absPath, "utf8");
            const firstNonBlank = content
                .split(/\r?\n/)
                .find((line) => line.trim().length > 0);
            expect(firstNonBlank).toMatch(/^(#\s|---\s*$)/);
        }
    );

    test(".llm/context.md links to each new skill by file path", () => {
        const context = fs.readFileSync(CONTEXT_PATH, "utf8");
        for (const relativePath of REQUIRED_SKILL_RELATIVE_PATHS) {
            const re = new RegExp(
                `skills/${relativePath.replace(/\./g, "\\.")}`
            );
            expect(context).toMatch(re);
        }
    });

    test(".llm/context.md references npm run preflight:pre-push", () => {
        const context = fs.readFileSync(CONTEXT_PATH, "utf8");
        expect(context).toContain("npm run preflight:pre-push");
    });

    test(".llm/context.md references npm run doctor", () => {
        const context = fs.readFileSync(CONTEXT_PATH, "utf8");
        expect(context).toContain("npm run doctor");
    });

    test("package.json scripts.preflight:pre-push is a non-empty string", () => {
        const pkg = JSON.parse(fs.readFileSync(PACKAGE_JSON_PATH, "utf8"));
        expect(pkg.scripts).toBeDefined();
        const value = pkg.scripts["preflight:pre-push"];
        expect(typeof value).toBe("string");
        expect(value.trim().length).toBeGreaterThan(0);
    });

    test("package.json scripts.doctor is a non-empty string", () => {
        const pkg = JSON.parse(fs.readFileSync(PACKAGE_JSON_PATH, "utf8"));
        expect(pkg.scripts).toBeDefined();
        const value = pkg.scripts.doctor;
        expect(typeof value).toBe("string");
        expect(value.trim().length).toBeGreaterThan(0);
    });
});
