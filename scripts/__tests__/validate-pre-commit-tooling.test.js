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
    hasManagedPrettierInvocation,
    validateYamllintPolicy,
    validatePrettierVersionResolution,
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

    test("hasManagedPrettierInvocation requires managed prettier wrapper for prettier hook", () => {
        expect(hasManagedPrettierInvocation("prettier", "npx --yes prettier@3.8.3 --write")).toBe(false);
        expect(hasManagedPrettierInvocation("prettier", "node scripts/run-managed-prettier.js --write")).toBe(true);
        expect(hasManagedPrettierInvocation("other-hook", "npx --yes prettier@3.8.3 --write")).toBe(true);
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

    test("validatePrettierVersionResolution passes when configured and resolved specs match", () => {
        const violations = validatePrettierVersionResolution(
            () => "prettier@3.8.3",
            () => "prettier@3.8.3"
        );

        expect(violations).toHaveLength(0);
    });

    test("validatePrettierVersionResolution reports mismatch between configured and resolved specs", () => {
        const violations = validatePrettierVersionResolution(
            () => "prettier@3.8.3",
            () => "prettier@3.9.0"
        );

        expect(violations).toHaveLength(1);
        expect(violations[0].hookId).toBe("prettier-version");
        expect(violations[0].message).toContain("must match package.json");
    });

    test("validatePrettierVersionResolution reports missing configured spec", () => {
        const violations = validatePrettierVersionResolution(
            () => null,
            () => "prettier@3.8.3"
        );

        expect(violations).toHaveLength(1);
        expect(violations[0].hookId).toBe("prettier-version");
        expect(violations[0].message).toContain("Missing pinned prettier version");
    });

    test("validateConfigFile passes for repository pre-commit config", () => {
        const repoConfigPath = path.resolve(__dirname, "../../.pre-commit-config.yaml");
        const configContent = fs.readFileSync(repoConfigPath, "utf8");
        const hooks = parseHookEntries(configContent);
        const violations = validateConfigFile(repoConfigPath);

        expect(hooks.length).toBeGreaterThan(0);
        expect(violations).toHaveLength(0);
    });

    test("validateConfigContent reports unmanaged prettier hook", () => {
        const content = [
            "repos:",
            "  - repo: https://github.com/adrienverge/yamllint",
            "    rev: v1.38.0",
            "    hooks:",
            "      - id: yamllint",
            "        args: [-c, .yamllint.yaml]",
            "  - repo: local",
            "    hooks:",
            "      - id: prettier",
            "        entry: npx --yes prettier@3.8.3 --write",
        ].join("\n");

        const violations = validateConfigContent(content);

        expect(violations).toHaveLength(1);
        expect(violations[0].hookId).toBe("prettier");
        expect(violations[0].message).toContain("run-managed-prettier.js");
    });

    test("package preflight script includes YAML, runtime, and portability gates", () => {
        const packageJsonPath = path.resolve(__dirname, "../../package.json");
        const packageJson = JSON.parse(fs.readFileSync(packageJsonPath, "utf8"));
        const preflightScript = packageJson.scripts["preflight:pre-commit"];

        expect(packageJson.scripts["check:prettier:hooks"]).toContain(
            "node scripts/run-managed-prettier.js --check"
        );
        expect(preflightScript).toContain("npm run check:prettier:hooks");
        expect(packageJson.scripts["check:yaml"]).toContain(
            "pre-commit run yamllint --all-files"
        );
        expect(preflightScript).toContain("npm run check:yaml");
        expect(preflightScript).toContain("node scripts/generate-skills-index.js --check");
        expect(preflightScript).toContain("npm run validate:npm-meta");
        expect(preflightScript).toContain("scripts/__tests__/generate-skills-index.test.js");
        expect(preflightScript).toContain("scripts/__tests__/shell-command.test.js");
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
