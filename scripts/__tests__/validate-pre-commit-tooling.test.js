/**
 * @fileoverview Tests for validate-pre-commit-tooling.js.
 */

"use strict";

const fs = require("fs");
const os = require("os");
const path = require("path");

const {
    parseHookEntries,
    parseHookIds,
    hasNpxInstallPolicy,
    hasManagedJestInvocation,
    validateYamllintPolicy,
    validateConfigContent,
    validateConfigFile,
} = require("../validate-pre-commit-tooling.js");

describe("validate-pre-commit-tooling", () => {
    test("parseHookEntries reads folded and inline entry styles", () => {
        const content = [
            "repos:",
            "  - repo: local",
            "    hooks:",
            "      - id: alpha",
            "        entry: node scripts/alpha.js",
            "      - id: beta",
            "        entry: >-",
            "          npx --yes jest --runTestsByPath scripts/__tests__/beta.test.js",
            "          scripts/__tests__/gamma.test.js",
        ].join("\n");

        const hooks = parseHookEntries(content);

        expect(hooks).toHaveLength(2);
        expect(hooks[0]).toEqual(
            expect.objectContaining({
                id: "alpha",
                entry: "node scripts/alpha.js",
            })
        );
        expect(hooks[1]).toEqual(
            expect.objectContaining({
                id: "beta",
                entry: "npx --yes jest --runTestsByPath scripts/__tests__/beta.test.js scripts/__tests__/gamma.test.js",
            })
        );
    });

    test("parseHookIds captures hook ids across repos", () => {
        const content = [
            "repos:",
            "  - repo: https://github.com/adrienverge/yamllint",
            "    rev: v1.38.0",
            "    hooks:",
            "      - id: yamllint",
            "  - repo: local",
            "    hooks:",
            "      - id: alpha",
            "        entry: node scripts/alpha.js",
        ].join("\n");

        const ids = parseHookIds(content);

        expect(ids).toEqual(
            expect.arrayContaining([
                expect.objectContaining({ id: "yamllint" }),
                expect.objectContaining({ id: "alpha" }),
            ])
        );
    });

    test("hasNpxInstallPolicy rejects npx without explicit policy", () => {
        const okWithYes = hasNpxInstallPolicy("npx --yes jest --runTestsByPath foo.test.js");
        const okWithNo = hasNpxInstallPolicy("npx --no jest --runTestsByPath foo.test.js");
        const bad = hasNpxInstallPolicy("npx jest --runTestsByPath foo.test.js");

        expect(okWithYes).toBe(true);
        expect(okWithNo).toBe(true);
        expect(bad).toBe(false);
    });

    test("hasManagedJestInvocation detects unmanaged bare jest command", () => {
        expect(hasManagedJestInvocation("jest --runTestsByPath foo.test.js")).toBe(false);
        expect(hasManagedJestInvocation("node scripts/run-managed-jest.js --runTestsByPath foo.test.js")).toBe(true);
        expect(hasManagedJestInvocation("script-tests", "npm run test:scripts")).toBe(false);
        expect(hasManagedJestInvocation("script-tests", "node scripts/run-managed-jest.js")).toBe(true);
    });

    test("validateConfigContent reports missing npx policy and unmanaged jest", () => {
        const content = [
            "repos:",
            "  - repo: https://github.com/adrienverge/yamllint",
            "    rev: v1.38.0",
            "    hooks:",
            "      - id: yamllint",
            "        args: [-c, .yamllint.yaml]",
            "  - repo: local",
            "    hooks:",
            "      - id: bad-npx",
            "        entry: npx jest --runTestsByPath scripts/__tests__/a.test.js",
            "      - id: bad-jest",
            "        entry: jest --runTestsByPath scripts/__tests__/b.test.js",
            "      - id: good",
            "        entry: node scripts/run-managed-jest.js --runTestsByPath scripts/__tests__/c.test.js",
        ].join("\n");

        const violations = validateConfigContent(content);

        expect(violations).toHaveLength(3);
        expect(violations.filter((violation) => violation.hookId === "bad-npx")).toHaveLength(2);
        expect(violations.filter((violation) => violation.hookId === "bad-jest")).toHaveLength(1);
    });

    test("validateYamllintPolicy reports missing yamllint hook", () => {
        const content = [
            "repos:",
            "  - repo: local",
            "    hooks:",
            "      - id: alpha",
            "        entry: node scripts/alpha.js",
        ].join("\n");

        const violations = validateYamllintPolicy(content);

        expect(violations).toHaveLength(1);
        expect(violations[0].message).toContain("Missing required yamllint hook");
    });

    test("validateYamllintPolicy rejects conditional skip pattern", () => {
        const content = [
            "repos:",
            "  - repo: local",
            "    hooks:",
            "      - id: yamllint",
            "        entry: bash -c 'if command -v yamllint >/dev/null 2>&1; then yamllint -c .yamllint.yaml \"$@\"; else echo \"yamllint not installed; skipping\"; fi' --",
        ].join("\n");

        const violations = validateYamllintPolicy(content);

        expect(violations.length).toBeGreaterThanOrEqual(1);
        expect(
            violations.some((violation) =>
                violation.message.includes("must not be conditionally skipped")
            )
        ).toBe(true);
    });

    test("validateConfigFile passes for repository pre-commit config", () => {
        const repoConfigPath = path.resolve(__dirname, "../../.pre-commit-config.yaml");
        const configContent = fs.readFileSync(repoConfigPath, "utf8");
        const hooks = parseHookEntries(configContent);
        const violations = validateConfigFile(repoConfigPath);

        expect(hooks.length).toBeGreaterThan(0);
        expect(violations).toHaveLength(0);
    });

    test("package preflight script includes YAML validation gate", () => {
        const packageJsonPath = path.resolve(__dirname, "../../package.json");
        const packageJson = JSON.parse(fs.readFileSync(packageJsonPath, "utf8"));

        expect(packageJson.scripts["check:yaml"]).toContain(
            "pre-commit run yamllint --all-files"
        );
        expect(packageJson.scripts["preflight:pre-commit"]).toContain(
            "npm run check:yaml"
        );
    });

    test("validateConfigFile handles CRLF and lone CR line endings", () => {
        const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), "pre-commit-tooling-"));
        const filePath = path.join(tempDir, ".pre-commit-config.yaml");

        try {
            const content = [
                "repos:",
                "  - repo: local",
                "    hooks:",
                "      - id: bad",
                "        entry: npx jest --runTestsByPath scripts/__tests__/a.test.js",
            ].join("\r");

            fs.writeFileSync(filePath, content, "utf8");
            const violations = validateConfigFile(filePath);

            expect(violations).toHaveLength(3);
            expect(violations.filter((violation) => violation.hookId === "bad")).toHaveLength(2);
            expect(violations.some((violation) => violation.hookId === "yamllint")).toBe(true);
        } finally {
            fs.rmSync(tempDir, { recursive: true, force: true });
        }
    });
});
