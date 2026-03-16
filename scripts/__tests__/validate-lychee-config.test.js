/**
 * @fileoverview Tests for validate-lychee-config.js logic.
 *
 * These tests validate the TOML parsing and field validation logic
 * for the lychee configuration validator. Also validates the actual
 * .lychee.toml configuration file against lychee v0.23.0's valid fields.
 */

"use strict";

const fs = require("fs");
const path = require("path");

const {
    parseTopLevelKeys,
    validateFields,
    VALID_FIELDS,
} = require("../validate-lychee-config.js");

const LYCHEE_CONFIG_PATH = path.resolve(__dirname, "../../.lychee.toml");

describe("parseTopLevelKeys", () => {
    test("should parse simple key = value lines", () => {
        const content = [
            'verbose = true',
            'no_progress = true',
            'max_concurrency = 4',
        ].join("\n");

        const keys = parseTopLevelKeys(content);
        expect(keys).toEqual(["verbose", "no_progress", "max_concurrency"]);
    });

    test("should skip comment lines", () => {
        const content = [
            "# This is a comment",
            'verbose = true',
            '# Another comment',
            'timeout = 20',
        ].join("\n");

        const keys = parseTopLevelKeys(content);
        expect(keys).toEqual(["verbose", "timeout"]);
    });

    test("should skip blank lines", () => {
        const content = [
            'verbose = true',
            '',
            'timeout = 20',
            '',
        ].join("\n");

        const keys = parseTopLevelKeys(content);
        expect(keys).toEqual(["verbose", "timeout"]);
    });

    test("should skip TOML table headers", () => {
        const content = [
            '[section]',
            'verbose = true',
            '[[array_section]]',
            'timeout = 20',
        ].join("\n");

        const keys = parseTopLevelKeys(content);
        expect(keys).toEqual(["verbose", "timeout"]);
    });

    test("should handle inline comments after values", () => {
        const content = [
            'timeout = 20            # seconds per request',
            'max_retries = 3         # retry transient failures',
        ].join("\n");

        const keys = parseTopLevelKeys(content);
        expect(keys).toEqual(["timeout", "max_retries"]);
    });

    test("should handle string values with equals signs", () => {
        const content = [
            'user_agent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)"',
        ].join("\n");

        const keys = parseTopLevelKeys(content);
        expect(keys).toEqual(["user_agent"]);
    });

    test("should handle array values", () => {
        const content = [
            'accept = ["200..=299", 429, 502]',
            'scheme = ["https", "http"]',
        ].join("\n");

        const keys = parseTopLevelKeys(content);
        expect(keys).toEqual(["accept", "scheme"]);
    });

    test("should handle multi-line array values", () => {
        const content = [
            'exclude = [',
            '  "^https?://localhost",',
            '  "^http://127\\\\.0\\\\.0\\\\.1",',
            ']',
        ].join("\n");

        const keys = parseTopLevelKeys(content);
        expect(keys).toEqual(["exclude"]);
    });

    test("should return empty array for empty content", () => {
        const keys = parseTopLevelKeys("");
        expect(keys).toEqual([]);
    });

    test("should return empty array for comment-only content", () => {
        const content = [
            "# Just comments",
            "# Nothing else",
        ].join("\n");

        const keys = parseTopLevelKeys(content);
        expect(keys).toEqual([]);
    });

    test("should handle CRLF line endings", () => {
        const content = "verbose = true\r\ntimeout = 20\r\n";

        const keys = parseTopLevelKeys(content);
        expect(keys).toEqual(["verbose", "timeout"]);
    });

    test("should handle keys with no spaces around equals", () => {
        const content = "verbose=true";

        const keys = parseTopLevelKeys(content);
        expect(keys).toEqual(["verbose"]);
    });

    test("should handle keys with extra spaces around equals", () => {
        const content = "verbose   =   true";

        const keys = parseTopLevelKeys(content);
        expect(keys).toEqual(["verbose"]);
    });

    test("should not parse TOML dotted keys or keys with hyphens (known limitation)", () => {
        // parseTopLevelKeys only matches bare keys matching [A-Za-z_][A-Za-z0-9_]*.
        // Dotted keys (e.g., header.Accept) and hyphenated keys (e.g., my-key) are
        // silently ignored. This is acceptable because lychee's configuration format
        // does not use these patterns for top-level fields.
        const content = [
            'header.Accept = "text/html"',
            'my-key = "value"',
            '"quoted.key" = "value"',
            'verbose = true',
        ].join("\n");

        const keys = parseTopLevelKeys(content);
        // Only the simple bare key "verbose" is parsed; dotted, hyphenated, and
        // quoted keys are all ignored by the regex
        expect(keys).toEqual(["verbose"]);
    });
});

describe("validateFields", () => {
    test("should accept all valid fields", () => {
        const validKeys = ["verbose", "no_progress", "max_concurrency", "timeout"];
        const { errors, warnings } = validateFields(validKeys);

        expect(errors).toEqual([]);
        expect(warnings).toEqual([]);
    });

    test("should reject invalid fields", () => {
        const keys = ["verbose", "invalid_field", "timeout"];
        const { errors } = validateFields(keys);

        expect(errors).toHaveLength(1);
        expect(errors[0]).toContain("invalid_field");
        expect(errors[0]).toContain("not a valid lychee v0.23.0 configuration option");
    });

    test("should reject multiple invalid fields", () => {
        const keys = ["deprecated_field", "also_bad", "verbose"];
        const { errors } = validateFields(keys);

        expect(errors).toHaveLength(2);
        expect(errors[0]).toContain("deprecated_field");
        expect(errors[1]).toContain("also_bad");
    });

    test("should warn about duplicate fields", () => {
        const keys = ["verbose", "timeout", "verbose"];
        const { errors, warnings } = validateFields(keys);

        expect(errors).toEqual([]);
        expect(warnings).toHaveLength(1);
        expect(warnings[0]).toContain("Duplicate field 'verbose'");
    });

    test("should handle empty key list", () => {
        const { errors, warnings } = validateFields([]);

        expect(errors).toEqual([]);
        expect(warnings).toEqual([]);
    });

    test("should report both errors and warnings together", () => {
        const keys = ["verbose", "verbose", "bad_field"];
        const { errors, warnings } = validateFields(keys);

        expect(errors).toHaveLength(1);
        expect(errors[0]).toContain("bad_field");
        expect(warnings).toHaveLength(1);
        expect(warnings[0]).toContain("Duplicate field 'verbose'");
    });
});

// SYNC: VALID_FIELDS is the source of truth defined in validate-lychee-config.js VALID_FIELDS constant
describe("VALID_FIELDS", () => {
    test("should be a non-empty Set", () => {
        expect(VALID_FIELDS).toBeInstanceOf(Set);
        expect(VALID_FIELDS.size).toBeGreaterThan(0);
    });

    test("should contain core lychee fields used in this repository", () => {
        // Fields used in .lychee.toml in this repository
        const repoFields = [
            "verbose",
            "no_progress",
            "max_concurrency",
            "include_mail",
            "timeout",
            "max_retries",
            "retry_wait_time",
            "max_redirects",
            "user_agent",
            "accept",
            "scheme",
            "exclude",
        ];

        for (const field of repoFields) {
            expect(VALID_FIELDS.has(field)).toBe(true);
        }
    });

    test("should contain commonly used lychee fields", () => {
        const commonFields = [
            "cache",
            "output",
            "format",
            "base_url",
            "include",
            "exclude_file",
            "exclude_path",
            "github_token",
            "method",
            "header",
        ];

        for (const field of commonFields) {
            expect(VALID_FIELDS.has(field)).toBe(true);
        }
    });

    test("should not contain obviously invalid field names", () => {
        const invalidNames = [
            "not_a_field",
            "deprecated",
            "invalid",
            "",
        ];

        for (const name of invalidNames) {
            expect(VALID_FIELDS.has(name)).toBe(false);
        }
    });
});

describe("End-to-end validation", () => {
    test("should validate a configuration matching this repository's .lychee.toml", () => {
        const content = [
            'verbose = true',
            'no_progress = true',
            'max_concurrency = 4',
            'include_mail = false',
            '',
            '# Network tuning',
            'timeout = 20            # seconds per request',
            'max_retries = 3         # retry transient failures',
            'retry_wait_time = 2     # seconds between retries',
            'max_redirects = 10',
            'user_agent = "Mozilla/5.0"',
            '',
            'accept = ["200..=299", 429, 502]',
            '',
            'scheme = ["https", "http"]',
            '',
            'exclude = [',
            '  "^https?://localhost",',
            ']',
        ].join("\n");

        const keys = parseTopLevelKeys(content);
        const { errors, warnings } = validateFields(keys);

        expect(errors).toEqual([]);
        expect(warnings).toEqual([]);
    });

    test("should catch a configuration with deprecated or invalid fields", () => {
        const content = [
            'verbose = true',
            'max_connections = 4',
            'exclude_mail = true',
        ].join("\n");

        const keys = parseTopLevelKeys(content);
        const { errors } = validateFields(keys);

        expect(errors).toHaveLength(2);
        expect(errors.some((e) => e.includes("max_connections"))).toBe(true);
        expect(errors.some((e) => e.includes("exclude_mail"))).toBe(true);
    });
});

describe("actual .lychee.toml config file validation", () => {
    const configContent = fs.readFileSync(LYCHEE_CONFIG_PATH, "utf8");
    const configKeys = parseTopLevelKeys(configContent);

    test("config file should exist", () => {
        expect(fs.existsSync(LYCHEE_CONFIG_PATH)).toBe(true);
    });

    test("config file should not be empty", () => {
        expect(configContent.trim().length).toBeGreaterThan(0);
    });

    test("config should contain at least one top-level key", () => {
        expect(configKeys.length).toBeGreaterThan(0);
    });

    describe("all config keys are valid for lychee v0.23.0", () => {
        test.each(configKeys)("'%s' should be a recognized lychee v0.23.0 field", (key) => {
            expect(VALID_FIELDS.has(key)).toBe(true);
        });
    });

    describe("no deprecated fields are present", () => {
        const deprecatedFieldMappings = [
            ["exclude_mail", "use 'include_mail = false' instead"],
            ["retries", "renamed to 'max_retries'"],
            ["verbosity", "removed; use 'verbose = true' instead"],
        ];

        test.each(deprecatedFieldMappings)(
            "deprecated field '%s' should not be present (%s)",
            (deprecatedField) => {
                expect(configKeys).not.toContain(deprecatedField);
            }
        );
    });

    describe("essential fields are present", () => {
        const essentialFields = [
            ["include_mail", "controls whether mailto: links are checked"],
            ["timeout", "prevents hanging on slow endpoints"],
            ["max_retries", "handles transient network failures"],
            ["exclude", "prevents checking known-bad URLs"],
            ["accept", "defines which HTTP status codes are acceptable"],
        ];

        test.each(essentialFields)(
            "essential field '%s' should be present (%s)",
            (field) => {
                expect(configKeys).toContain(field);
            }
        );
    });

    test("config should pass full validation with no errors", () => {
        const { errors } = validateFields(configKeys);
        expect(errors).toEqual([]);
    });
});
