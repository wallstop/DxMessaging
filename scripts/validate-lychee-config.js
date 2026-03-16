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

/**
 * Parse top-level keys from a TOML file.
 * Handles simple key = value lines at the top level (not inside table headers).
 * Ignores comments and blank lines.
 *
 * Limitation: This parser only handles simple `key = value` patterns where the
 * key is a bare identifier matching [A-Za-z_][A-Za-z0-9_]*. It does NOT handle:
 *   - TOML dotted keys (e.g., `header.Accept = "text/html"`)
 *   - Keys containing hyphens (e.g., `my-key = "value"`)
 *   - Quoted keys (e.g., `"special.key" = "value"`)
 * These patterns are not currently used in lychee's configuration format, but
 * this limitation should be revisited if lychee adds support for them.
 *
 * @param {string} content - The TOML file content
 * @returns {string[]} Array of top-level key names
 */
function parseTopLevelKeys(content) {
    const keys = [];
    const lines = content.split(/\r?\n/);

    for (const line of lines) {
        const trimmed = line.trim();

        // Skip empty lines and comments
        if (trimmed === "" || trimmed.startsWith("#")) {
            continue;
        }

        // Skip TOML table headers like [section]
        if (trimmed.startsWith("[")) {
            continue;
        }

        // Match key = value pattern (top-level TOML key)
        // Only matches bare keys with underscores; see JSDoc for limitations
        const match = trimmed.match(/^([A-Za-z_][A-Za-z0-9_]*)\s*=/);
        if (match) {
            keys.push(match[1]);
        }
    }

    return keys;
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
 * @exports {Function} validateFields - Validates field names against the known valid set
 * @exports {Set<string>} VALID_FIELDS - Set of valid lychee v0.23.0 configuration field names
 */
if (typeof module !== "undefined" && module.exports) {
    module.exports = {
        parseTopLevelKeys,
        validateFields,
        VALID_FIELDS,
    };
}

// Only run main when executed directly (not when required as a module)
if (require.main === module) {
    process.exit(main());
}
