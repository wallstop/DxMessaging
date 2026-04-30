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
    hasRequiredParserPrecheckCommand,
    hasRequiredPackageJsonFormatCommand,
    hasRequiredScriptsCspellCommand,
    hasRequiredParserSuiteTestPaths,
    hasNpxInstallPolicy,
    hasManagedJestInvocation,
    hasManagedPrettierInvocation,
    validateYamllintPolicy,
    validatePrettierVersionResolution,
    validatePreflightScriptPolicy,
    REQUIRED_PRECHECK_PARSER_COMMAND,
    REQUIRED_PACKAGE_JSON_FORMAT_COMMAND,
    REQUIRED_SCRIPTS_CSPELL_COMMAND,
    REQUIRED_PARSER_SUITE_HOOK_ID,
    REQUIRED_PARSER_SUITE_TEST_PATHS,
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

    test("parseHookEntries handles consecutive folded entries", () => {
        const content = [
            "repos:",
            "  - repo: local",
            "    hooks:",
            "      - id: alpha",
            "        entry: >-",
            "          node scripts/run-managed-jest.js --runTestsByPath",
            "          scripts/__tests__/alpha.test.js",
            "      - id: beta",
            "        entry: >-",
            "          node scripts/run-managed-jest.js --runTestsByPath",
            "          scripts/__tests__/beta.test.js",
        ].join("\n");

        const hooks = parseHookEntries(content);

        expect(hooks).toHaveLength(2);
        expect(hooks[0]).toEqual(
            expect.objectContaining({
                id: "alpha",
                entry: "node scripts/run-managed-jest.js --runTestsByPath scripts/__tests__/alpha.test.js",
            })
        );
        expect(hooks[1]).toEqual(
            expect.objectContaining({
                id: "beta",
                entry: "node scripts/run-managed-jest.js --runTestsByPath scripts/__tests__/beta.test.js",
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

    test("hasRequiredParserPrecheckCommand detects parser command as chained step", () => {
        const script = [
            "npm run validate:pre-commit-tooling",
            "npm run check:prettier:hooks",
            REQUIRED_PRECHECK_PARSER_COMMAND,
        ].join(" && ");

        expect(hasRequiredParserPrecheckCommand(script)).toBe(true);
    });

    test("hasRequiredParserPrecheckCommand rejects substring-only matches", () => {
        const script = "npm run validate:pre-commit-tooling && echo pre-commit run script-parser-tests --all-files";

        expect(hasRequiredParserPrecheckCommand(script)).toBe(false);
    });

    test("hasRequiredPackageJsonFormatCommand detects package.json format precheck step", () => {
        const script = [
            REQUIRED_PACKAGE_JSON_FORMAT_COMMAND,
            "npm run check:prettier:hooks",
            REQUIRED_PRECHECK_PARSER_COMMAND,
        ].join(" && ");

        expect(hasRequiredPackageJsonFormatCommand(script)).toBe(true);
    });

    test("hasRequiredPackageJsonFormatCommand rejects substring-only matches", () => {
        const script = "npm run validate:pre-commit-tooling && echo npm run check:package-json-format";

        expect(hasRequiredPackageJsonFormatCommand(script)).toBe(false);
    });

    test("hasRequiredScriptsCspellCommand detects script cspell command as chained step", () => {
        const script = [
            REQUIRED_PACKAGE_JSON_FORMAT_COMMAND,
            REQUIRED_SCRIPTS_CSPELL_COMMAND,
            REQUIRED_PRECHECK_PARSER_COMMAND,
        ].join(" && ");

        expect(hasRequiredScriptsCspellCommand(script)).toBe(true);
    });

    test("hasRequiredScriptsCspellCommand rejects substring-only matches", () => {
        const script = "npm run validate:pre-commit-tooling && echo npm run check:cspell:scripts";

        expect(hasRequiredScriptsCspellCommand(script)).toBe(false);
    });

    test("hasRequiredParserSuiteTestPaths detects required parser regression test path", () => {
        const content = [
            "repos:",
            "  - repo: local",
            "    hooks:",
            `      - id: ${REQUIRED_PARSER_SUITE_HOOK_ID}`,
            "        entry: >-",
            "          node scripts/run-managed-jest.js --runTestsByPath scripts/__tests__/generate-skills-index.test.js",
            `          ${REQUIRED_PARSER_SUITE_TEST_PATHS[0]}`,
        ].join("\n");

        expect(hasRequiredParserSuiteTestPaths(content)).toBe(true);
    });

    test("hasRequiredParserSuiteTestPaths rejects missing required parser regression test path", () => {
        const content = [
            "repos:",
            "  - repo: local",
            "    hooks:",
            `      - id: ${REQUIRED_PARSER_SUITE_HOOK_ID}`,
            "        entry: node scripts/run-managed-jest.js --runTestsByPath scripts/__tests__/generate-skills-index.test.js",
        ].join("\n");

        expect(hasRequiredParserSuiteTestPaths(content)).toBe(false);
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

        const readFileSyncMock = jest.fn((filePath) => {
            if (filePath === "/tmp/package.json") {
                return JSON.stringify({
                    scripts: {
                        "preflight:pre-commit": `${REQUIRED_PACKAGE_JSON_FORMAT_COMMAND} && ${REQUIRED_SCRIPTS_CSPELL_COMMAND} && ${REQUIRED_PRECHECK_PARSER_COMMAND}`,
                    },
                });
            }

            if (filePath === "/tmp/pre-commit.yaml") {
                return [
                    "repos:",
                    "  - repo: local",
                    "    hooks:",
                    `      - id: ${REQUIRED_PARSER_SUITE_HOOK_ID}`,
                    "        entry: node scripts/run-managed-jest.js --runTestsByPath scripts/__tests__/generate-skills-index.test.js scripts/__tests__/fix-csharp-underscore-methods.test.js",
                ].join("\n");
            }

            return "";
        });

        const violations = validateConfigContent(content, {
            readFileSyncImpl: readFileSyncMock,
            packageJsonPath: "/tmp/package.json",
            preCommitConfigPath: "/tmp/pre-commit.yaml",
            getConfiguredPrettierSpecFn: () => "prettier@3.8.3",
            getPinnedPrettierSpecFn: () => "prettier@3.8.3",
        });

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

        const readFileSyncMock = jest.fn((filePath) => {
            if (filePath === "/tmp/package.json") {
                return JSON.stringify({
                    scripts: {
                        "preflight:pre-commit": `${REQUIRED_PACKAGE_JSON_FORMAT_COMMAND} && ${REQUIRED_SCRIPTS_CSPELL_COMMAND} && ${REQUIRED_PRECHECK_PARSER_COMMAND}`,
                    },
                });
            }

            if (filePath === "/tmp/pre-commit.yaml") {
                return [
                    "repos:",
                    "  - repo: local",
                    "    hooks:",
                    `      - id: ${REQUIRED_PARSER_SUITE_HOOK_ID}`,
                    "        entry: node scripts/run-managed-jest.js --runTestsByPath scripts/__tests__/generate-skills-index.test.js scripts/__tests__/fix-csharp-underscore-methods.test.js",
                ].join("\n");
            }

            return "";
        });

        const violations = validateConfigContent(content, {
            readFileSyncImpl: readFileSyncMock,
            packageJsonPath: "/tmp/package.json",
            preCommitConfigPath: "/tmp/pre-commit.yaml",
            getConfiguredPrettierSpecFn: () => "prettier@3.8.3",
            getPinnedPrettierSpecFn: () => "prettier@3.8.3",
        });

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
        expect(preflightScript).toContain(REQUIRED_PACKAGE_JSON_FORMAT_COMMAND);
        expect(preflightScript).toContain("npm run check:prettier:hooks");
        expect(preflightScript).toContain(REQUIRED_SCRIPTS_CSPELL_COMMAND);
        expect(packageJson.scripts["check:yaml"]).toContain(
            "pre-commit run yamllint --all-files"
        );
        expect(preflightScript).toContain("npm run check:yaml");
        expect(preflightScript).toContain("node scripts/generate-skills-index.js --check");
        expect(preflightScript).toContain("npm run validate:npm-meta");
        expect(preflightScript).toContain(REQUIRED_PRECHECK_PARSER_COMMAND);
        expect(preflightScript).not.toContain("node scripts/run-managed-jest.js --runTestsByPath");
    });

    test("validatePreflightScriptPolicy passes when parser precheck command exists", () => {
        const readFileSyncMock = jest.fn((filePath) => {
            if (filePath === "/tmp/package.json") {
                return JSON.stringify({
                    scripts: {
                        "preflight:pre-commit": `${REQUIRED_PACKAGE_JSON_FORMAT_COMMAND} && npm run validate:pre-commit-tooling && ${REQUIRED_SCRIPTS_CSPELL_COMMAND} && ${REQUIRED_PRECHECK_PARSER_COMMAND}`,
                    },
                });
            }

            if (filePath === "/tmp/pre-commit.yaml") {
                return [
                    "repos:",
                    "  - repo: local",
                    "    hooks:",
                    `      - id: ${REQUIRED_PARSER_SUITE_HOOK_ID}`,
                    "        entry: node scripts/run-managed-jest.js --runTestsByPath scripts/__tests__/generate-skills-index.test.js scripts/__tests__/fix-csharp-underscore-methods.test.js",
                ].join("\n");
            }

            return "";
        });

        const violations = validatePreflightScriptPolicy(
            readFileSyncMock,
            "/tmp/package.json",
            "/tmp/pre-commit.yaml"
        );

        expect(violations).toHaveLength(0);
        expect(readFileSyncMock).toHaveBeenCalledWith("/tmp/package.json", "utf8");
        expect(readFileSyncMock).toHaveBeenCalledWith("/tmp/pre-commit.yaml", "utf8");
    });

    test("validatePreflightScriptPolicy reports missing parser precheck command", () => {
        const readFileSyncMock = jest.fn((filePath) => {
            if (filePath === "/tmp/package.json") {
                return JSON.stringify({
                    scripts: {
                        "preflight:pre-commit": `${REQUIRED_PACKAGE_JSON_FORMAT_COMMAND} && npm run validate:pre-commit-tooling && ${REQUIRED_SCRIPTS_CSPELL_COMMAND}`,
                    },
                });
            }

            if (filePath === "/tmp/pre-commit.yaml") {
                return [
                    "repos:",
                    "  - repo: local",
                    "    hooks:",
                    `      - id: ${REQUIRED_PARSER_SUITE_HOOK_ID}`,
                    "        entry: node scripts/run-managed-jest.js --runTestsByPath scripts/__tests__/generate-skills-index.test.js scripts/__tests__/fix-csharp-underscore-methods.test.js",
                ].join("\n");
            }

            return "";
        });

        const violations = validatePreflightScriptPolicy(
            readFileSyncMock,
            "/tmp/package.json",
            "/tmp/pre-commit.yaml"
        );

        expect(violations).toHaveLength(1);
        expect(violations[0].hookId).toBe("preflight-script");
        expect(violations[0].message).toContain(REQUIRED_PRECHECK_PARSER_COMMAND);
    });

    test("validatePreflightScriptPolicy reports missing package.json format precheck command", () => {
        const readFileSyncMock = jest.fn((filePath) => {
            if (filePath === "/tmp/package.json") {
                return JSON.stringify({
                    scripts: {
                        "preflight:pre-commit": `npm run validate:pre-commit-tooling && ${REQUIRED_SCRIPTS_CSPELL_COMMAND} && ${REQUIRED_PRECHECK_PARSER_COMMAND}`,
                    },
                });
            }

            if (filePath === "/tmp/pre-commit.yaml") {
                return [
                    "repos:",
                    "  - repo: local",
                    "    hooks:",
                    `      - id: ${REQUIRED_PARSER_SUITE_HOOK_ID}`,
                    "        entry: node scripts/run-managed-jest.js --runTestsByPath scripts/__tests__/generate-skills-index.test.js scripts/__tests__/fix-csharp-underscore-methods.test.js",
                ].join("\n");
            }

            return "";
        });

        const violations = validatePreflightScriptPolicy(
            readFileSyncMock,
            "/tmp/package.json",
            "/tmp/pre-commit.yaml"
        );

        expect(violations).toHaveLength(1);
        expect(violations[0].hookId).toBe("preflight-script");
        expect(violations[0].message).toContain(REQUIRED_PACKAGE_JSON_FORMAT_COMMAND);
    });

    test("validatePreflightScriptPolicy reports missing scripts cspell precheck command", () => {
        const readFileSyncMock = jest.fn((filePath) => {
            if (filePath === "/tmp/package.json") {
                return JSON.stringify({
                    scripts: {
                        "preflight:pre-commit": `${REQUIRED_PACKAGE_JSON_FORMAT_COMMAND} && npm run validate:pre-commit-tooling && ${REQUIRED_PRECHECK_PARSER_COMMAND}`,
                    },
                });
            }

            if (filePath === "/tmp/pre-commit.yaml") {
                return [
                    "repos:",
                    "  - repo: local",
                    "    hooks:",
                    `      - id: ${REQUIRED_PARSER_SUITE_HOOK_ID}`,
                    "        entry: node scripts/run-managed-jest.js --runTestsByPath scripts/__tests__/generate-skills-index.test.js scripts/__tests__/fix-csharp-underscore-methods.test.js",
                ].join("\n");
            }

            return "";
        });

        const violations = validatePreflightScriptPolicy(
            readFileSyncMock,
            "/tmp/package.json",
            "/tmp/pre-commit.yaml"
        );

        expect(violations).toHaveLength(1);
        expect(violations[0].hookId).toBe("preflight-script");
        expect(violations[0].message).toContain(REQUIRED_SCRIPTS_CSPELL_COMMAND);
    });

    test("validatePreflightScriptPolicy reports missing parser suite hook", () => {
        const readFileSyncMock = jest.fn((filePath) => {
            if (filePath === "/tmp/package.json") {
                return JSON.stringify({
                    scripts: {
                        "preflight:pre-commit": `${REQUIRED_PACKAGE_JSON_FORMAT_COMMAND} && npm run validate:pre-commit-tooling && ${REQUIRED_SCRIPTS_CSPELL_COMMAND} && ${REQUIRED_PRECHECK_PARSER_COMMAND}`,
                    },
                });
            }

            if (filePath === "/tmp/pre-commit.yaml") {
                return [
                    "repos:",
                    "  - repo: local",
                    "    hooks:",
                    "      - id: alpha",
                    "        entry: node scripts/alpha.js",
                ].join("\n");
            }

            return "";
        });

        const violations = validatePreflightScriptPolicy(
            readFileSyncMock,
            "/tmp/package.json",
            "/tmp/pre-commit.yaml"
        );

        expect(violations).toHaveLength(1);
        expect(violations[0].hookId).toBe("preflight-script");
        expect(violations[0].message).toContain(REQUIRED_PARSER_SUITE_HOOK_ID);
    });

    test("validatePreflightScriptPolicy reports missing required parser regression test path", () => {
        const readFileSyncMock = jest.fn((filePath) => {
            if (filePath === "/tmp/package.json") {
                return JSON.stringify({
                    scripts: {
                        "preflight:pre-commit": `${REQUIRED_PACKAGE_JSON_FORMAT_COMMAND} && npm run validate:pre-commit-tooling && ${REQUIRED_SCRIPTS_CSPELL_COMMAND} && ${REQUIRED_PRECHECK_PARSER_COMMAND}`,
                    },
                });
            }

            if (filePath === "/tmp/pre-commit.yaml") {
                return [
                    "repos:",
                    "  - repo: local",
                    "    hooks:",
                    `      - id: ${REQUIRED_PARSER_SUITE_HOOK_ID}`,
                    "        entry: node scripts/run-managed-jest.js --runTestsByPath scripts/__tests__/generate-skills-index.test.js",
                ].join("\n");
            }

            return "";
        });

        const violations = validatePreflightScriptPolicy(
            readFileSyncMock,
            "/tmp/package.json",
            "/tmp/pre-commit.yaml"
        );

        expect(violations).toHaveLength(1);
        expect(violations[0].hookId).toBe("preflight-script");
        expect(violations[0].message).toContain(REQUIRED_PARSER_SUITE_TEST_PATHS[0]);
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

            expect(violations).toHaveLength(4);
            expect(violations.filter((violation) => violation.hookId === "bad")).toHaveLength(2);
            expect(violations.some((violation) => violation.hookId === "yamllint")).toBe(true);
            expect(violations.some((violation) => violation.hookId === "preflight-script")).toBe(true);
        } finally {
            fs.rmSync(tempDir, { recursive: true, force: true });
        }
    });
});
