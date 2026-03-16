#!/usr/bin/env node
/**
 * validate-lychee-config.js
 *
 * Validates .lychee.toml configuration against lychee v0.23.0's valid field list.
 * Catches deprecated or misspelled fields before they break CI.
 *
 * @usage
 *   node scripts/validate-lychee-config.js
 *
 * @exitcodes
 *   0 - Success (all fields are valid)
 *   1 - Validation failed (invalid fields found)
 *
 * @example
 *   # Run from repository root
 *   node scripts/validate-lychee-config.js
 *
 *   # Run in CI pipeline
 *   node scripts/validate-lychee-config.js || exit 1
 */

"use strict";

const fs = require("fs");
const path = require("path");

const CONFIG_PATH = path.join(__dirname, "..", ".lychee.toml");

// SYNC: Keep in sync with lychee v0.23.0 valid configuration fields.
// Source: https://github.com/lycheeverse/lychee/blob/master/lychee.example.toml
// SYNC: Tests in validate-lychee-config.test.js VALID_FIELDS describe block reference this constant
const VALID_FIELDS = new Set([
    "accept",
    "accept_timeouts",
    "archive",
    "base_url",
    "basic_auth",
    "cache",
    "cache_exclude_status",
    "cookie_jar",
    "default_extension",
    "dump",
    "dump_inputs",
    "exclude",
    "exclude_all_private",
    "exclude_file",
    "exclude_link_local",
    "exclude_loopback",
    "exclude_path",
    "exclude_private",
    "extensions",
    "fallback_extensions",
    "files_from",
    "format",
    "generate",
    "github_token",
    "glob_ignore_case",
    "header",
    "hidden",
    "host_concurrency",
    "host_request_interval",
    "host_stats",
    "hosts",
    "include",
    "include_fragments",
    "include_mail",
    "include_verbatim",
    "include_wikilinks",
    "index_files",
    "insecure",
    "max_cache_age",
    "max_concurrency",
    "max_redirects",
    "max_retries",
    "method",
    "min_tls",
    "mode",
    "no_ignore",
    "no_progress",
    "offline",
    "output",
    "preprocess",
    "remap",
    "require_https",
    "retry_wait_time",
    "root_dir",
    "scheme",
    "skip_missing",
    "suggest",
    "threads",
    "timeout",
    "user_agent",
    "verbose",
]);

// SYNC: Keep in sync with lychee v0.23.0 valid verbose values.
// Source: https://github.com/lycheeverse/lychee/blob/master/lychee.example.toml
// SYNC: Tests in validate-lychee-config.test.js validateFieldValues describe block reference this constant
const VALID_VERBOSE_VALUES = ["error", "warn", "info", "debug", "trace"];

/**
 * Split a TOML dotted path into segments while respecting quoted segments.
 *
 * Examples:
 *   basic_auth.example.com -> ["basic_auth", "example", "com"]
 *   "my.section".key -> ["my.section", "key"]
 *
 * @param {string} pathExpression - TOML key or table expression
 * @returns {string[]} Path segments (quotes stripped)
 */
function splitTomlPath(pathExpression) {
    const segments = [];
    let current = "";
    let quoteChar = null;

    for (let i = 0; i < pathExpression.length; i += 1) {
        const char = pathExpression[i];

        if (quoteChar !== null) {
            // Keep escaped characters inside quoted segments.
            if (char === "\\" && i + 1 < pathExpression.length) {
                current += char + pathExpression[i + 1];
                i += 1;
                continue;
            }

            if (char === quoteChar) {
                quoteChar = null;
                continue;
            }

            current += char;
            continue;
        }

        if (char === '"' || char === "'") {
            quoteChar = char;
            continue;
        }

        if (char === ".") {
            segments.push(current.trim());
            current = "";
            continue;
        }

        current += char;
    }

    segments.push(current.trim());
    return segments.filter((segment) => segment.length > 0);
}

/**
 * Parse TOML table header names from lines like [section] and [[array_section]].
 *
 * @param {string} line - TOML line with comments already stripped
 * @returns {{ tableName: string, isArrayTable: boolean } | null}
 */
function parseTomlTableHeader(line) {
    const arrayTableMatch = line.match(/^\[\[(.+)\]\]$/);
    if (arrayTableMatch) {
        return { tableName: arrayTableMatch[1].trim(), isArrayTable: true };
    }

    const tableMatch = line.match(/^\[(.+)\]$/);
    if (tableMatch) {
        return { tableName: tableMatch[1].trim(), isArrayTable: false };
    }

    return null;
}

/**
 * Strip inline TOML comments while preserving hash characters inside quoted values.
 *
 * @param {string} line - TOML line
 * @returns {string} TOML line without trailing inline comments
 */
function stripInlineTomlComment(line) {
    let quoteChar = null;

    for (let i = 0; i < line.length; i += 1) {
        const char = line[i];

        if (quoteChar !== null) {
            if (char === "\\" && i + 1 < line.length) {
                i += 1;
                continue;
            }

            if (char === quoteChar) {
                quoteChar = null;
            }
            continue;
        }

        if (char === '"' || char === "'") {
            quoteChar = char;
            continue;
        }

        if (char === "#") {
            return line.slice(0, i).trimEnd();
        }
    }

    return line;
}

/**
 * Parse a TOML file into top-level keys and key-value entries with optional table context.
 *
 * @param {string} content - The TOML file content
 * @returns {{ keys: string[], keyValues: Array<{ key: string, value: string, table?: string, keyPath?: string }> }}
 */
function parseTomlForLycheeValidation(content) {
    const keys = [];
    const keyValues = [];
    const lines = content.split(/\r?\n/);
    let currentTable = null;

    for (const line of lines) {
        const trimmed = line.trim();
        if (trimmed === "" || trimmed.startsWith("#")) {
            continue;
        }

        const lineWithoutComment = stripInlineTomlComment(trimmed).trim();
        if (lineWithoutComment === "") {
            continue;
        }

        const tableHeader = parseTomlTableHeader(lineWithoutComment);
        if (tableHeader) {
            currentTable = tableHeader.tableName;
            const tableSegments = splitTomlPath(tableHeader.tableName);
            if (tableSegments.length > 0) {
                keys.push(tableSegments[0]);
            }
            continue;
        }

        const equalsIndex = lineWithoutComment.indexOf("=");
        if (equalsIndex === -1) {
            continue;
        }

        const rawKey = lineWithoutComment.slice(0, equalsIndex).trim();
        if (rawKey.length === 0) {
            continue;
        }

        const value = lineWithoutComment.slice(equalsIndex + 1).trim();
        const keySegments = splitTomlPath(rawKey);
        if (keySegments.length === 0) {
            continue;
        }

        const parsedKey = keySegments[keySegments.length - 1];

        if (currentTable !== null) {
            keyValues.push({
                key: parsedKey,
                value,
                table: currentTable,
                keyPath: `${currentTable}.${rawKey}`,
            });
            continue;
        }

        keys.push(keySegments[0]);
        keyValues.push({ key: parsedKey, value });
    }

    return { keys, keyValues };
}

/**
 * Parse top-level keys from a TOML file.
 * Handles top-level assignments and TOML table headers.
 * Ignores comments and blank lines.
 *
 * For table headers such as [basic_auth] or [[hosts]], the first table segment
 * is treated as the top-level field key.
 *
 * @param {string} content - The TOML file content
 * @returns {string[]} Array of top-level key names
 */
function parseTopLevelKeys(content) {
    return parseTomlForLycheeValidation(content).keys;
}

/**
 * Parse top-level key-value pairs from a TOML file.
 * Returns an array of { key, value } objects where value is the raw string
 * after the equals sign (trimmed).
 *
 * If a key is defined inside a TOML table, the pair also includes:
 *   - table: table path (e.g., basic_auth, hosts, basic_auth."example.com")
 *   - keyPath: fully-qualified key path (e.g., basic_auth.username)
 *
 * @param {string} content - The TOML file content
 * @returns {{ key: string, value: string, table?: string, keyPath?: string }[]} Array of key-value pairs
 */
function parseTopLevelKeyValues(content) {
    return parseTomlForLycheeValidation(content).keyValues;
}

/**
 * Returns true when a value has matching quote boundaries using the same quote character.
 *
 * @param {string} value - Raw value from TOML
 * @returns {boolean} True when value starts and ends with matching single or double quotes
 */
function hasMatchingBoundaryQuotes(value) {
    const trimmed = value.trim();
    if (trimmed.length < 2) {
        return false;
    }

    const firstChar = trimmed[0];
    const lastChar = trimmed[trimmed.length - 1];
    const isQuote = firstChar === '"' || firstChar === "'";

    return isQuote && firstChar === lastChar;
}

/**
 * Strip wrapping quotes only when both boundary quotes match.
 *
 * @param {string} value - Raw value from TOML
 * @returns {string} Trimmed value with matching boundary quotes removed
 */
function stripMatchingBoundaryQuotes(value) {
    const trimmed = value.trim();
    if (!hasMatchingBoundaryQuotes(trimmed)) {
        return trimmed;
    }

    return trimmed.slice(1, -1);
}

/**
 * Validate field values against known constraints.
 * Currently validates:
 *   - verbose: must be one of "error", "warn", "info", "debug", "trace"
 *
 * This function is designed to be extended with additional field validations
 * as lychee evolves.
 *
 * @param {Array<{ key: string, value: string, table?: string, keyPath?: string }>} keyValues
 *   - Array of key-value pairs from the TOML file, including optional table/keyPath metadata
 * @returns {{ errors: string[], warnings: string[] }} Validation results
 */
function validateFieldValues(keyValues) {
    const errors = [];
    const warnings = [];

    for (const { key, value, keyPath } of keyValues) {
        if (key === "verbose") {
            // verbose must be a quoted string matching one of the valid log levels
            // Require matching boundary quotes so malformed TOML like '"info' is rejected.
            const isQuotedString = hasMatchingBoundaryQuotes(value);
            const keyDisplay = keyPath || key;

            if (!isQuotedString) {
                errors.push(
                    `Invalid value for '${keyDisplay}': ${value} (must be a quoted string, one of: ${VALID_VERBOSE_VALUES.join(", ")})`
                );
                continue;
            }

            const unquoted = stripMatchingBoundaryQuotes(value);
            if (!VALID_VERBOSE_VALUES.includes(unquoted)) {
                errors.push(
                    `Invalid value for '${keyDisplay}': ${value} (must be one of: ${VALID_VERBOSE_VALUES.join(", ")})`
                );
            }
        }
    }

    return { errors, warnings };
}

/**
 * Validate top-level keys against the known valid fields.
 *
 * @param {string[]} keys - Array of top-level key names from the TOML file
 * @returns {{ errors: string[], warnings: string[] }} Validation results
 */
function validateFields(keys) {
    const errors = [];
    const warnings = [];

    for (const key of keys) {
        if (!VALID_FIELDS.has(key)) {
            errors.push(
                `Invalid field '${key}': not a valid lychee v0.23.0 configuration option`
            );
        }
    }

    // Check for duplicate keys
    const seen = new Set();
    for (const key of keys) {
        if (seen.has(key)) {
            warnings.push(`Duplicate field '${key}' found`);
        }
        seen.add(key);
    }

    return { errors, warnings };
}

/**
 * Main entry point.
 */
function main() {
    console.log(`Validating lychee configuration: ${CONFIG_PATH}`);
    console.log();

    if (!fs.existsSync(CONFIG_PATH)) {
        console.log("No .lychee.toml found; skipping validation.");
        return 0;
    }

    let content;
    try {
        content = fs.readFileSync(CONFIG_PATH, "utf8");
    } catch (error) {
        console.error(`Cannot read .lychee.toml: ${error.message}`);
        return 1;
    }

    const keys = parseTopLevelKeys(content);
    console.log(`Found ${keys.length} top-level fields: ${keys.join(", ")}`);
    console.log();

    const { errors, warnings } = validateFields(keys);

    // Phase 2: Validate field values
    const keyValues = parseTopLevelKeyValues(content);
    const valueResult = validateFieldValues(keyValues);
    errors.push(...valueResult.errors);
    warnings.push(...valueResult.warnings);

    for (const warning of warnings) {
        console.log(`  Warning: ${warning}`);
    }

    for (const error of errors) {
        console.log(`  Error: ${error}`);
    }

    if (errors.length > 0) {
        console.log();
        console.log(
            `Validation failed: ${errors.length} error(s), ${warnings.length} warning(s)`
        );
        console.log();
        console.log("Valid lychee v0.23.0 fields:");
        const sortedFields = [...VALID_FIELDS].sort();
        for (const field of sortedFields) {
            console.log(`  - ${field}`);
        }
        return 1;
    }

    if (warnings.length > 0) {
        console.log();
        console.log(`Validation passed with ${warnings.length} warning(s)`);
    } else {
        console.log("All fields are valid lychee v0.23.0 configuration options.");
    }

    return 0;
}

/**
 * @module validate-lychee-config
 * @description Validates .lychee.toml configuration against lychee v0.23.0's valid field list.
 * Used by pre-push hooks and CI pipelines to catch deprecated or misspelled fields.
 *
 * @exports {Function} parseTopLevelKeys - Parses top-level key names from TOML content
 * @exports {Function} parseTopLevelKeyValues - Parses top-level key-value pairs from TOML content
 * @exports {Function} validateFields - Validates field names against the known valid set
 * @exports {Function} validateFieldValues - Validates field values against known constraints
 * @exports {Set<string>} VALID_FIELDS - Set of valid lychee v0.23.0 configuration field names
 * @exports {string[]} VALID_VERBOSE_VALUES - Array of valid verbose log level values
 */
if (typeof module !== "undefined" && module.exports) {
    module.exports = {
        parseTopLevelKeys,
        parseTopLevelKeyValues,
        validateFields,
        validateFieldValues,
        VALID_FIELDS,
        VALID_VERBOSE_VALUES,
    };
}

// Only run main when executed directly (not when required as a module)
if (require.main === module) {
    process.exit(main());
}
