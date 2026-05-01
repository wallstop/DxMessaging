#!/usr/bin/env node
/**
 * @fileoverview Validates NPM package meta file integrity.
 *
 * This script ensures that:
 * 1. Every .meta file in the package corresponds to an actual file or directory
 * 2. Every Unity-tracked file/directory has its .meta file included in the package
 *
 * Unity requires .meta files for every asset to maintain consistent GUIDs across
 * installations. Missing or orphaned .meta files can break Unity projects.
 *
 * Usage:
 *   node scripts/validate-npm-meta.js         # Validate the npm package
 *   node scripts/validate-npm-meta.js --check # Exit with error if validation fails
 */

"use strict";

const path = require("path");
const { normalizeToLf } = require("./lib/quote-parser");
const { spawnPlatformCommandSync } = require("./lib/shell-command");

/**
 * Parse tar listing output into package-relative file paths.
 *
 * @param {string} tarOutput - Raw `tar -tzf` output
 * @returns {string[]} Package-relative file list
 */
function parseTarListingOutput(tarOutput) {
    return normalizeToLf(tarOutput)
        .split("\n")
        .filter((line) => line.trim())
        .map((line) => line.replace(/^package\//, ""))
        .filter((line) => line); // Remove empty strings
}

/**
 * Parse `npm pack --json --dry-run` output and return package-relative file paths.
 *
 * @param {string} packOutput - Raw JSON output from npm pack
 * @returns {string[]} Package-relative file list
 */
function parseNpmPackJsonOutput(packOutput) {
    const trimmedOutput = normalizeToLf(packOutput || "").trim();

    if (!trimmedOutput) {
        throw new Error("npm pack produced no output");
    }

    let parsedOutput;
    try {
        parsedOutput = JSON.parse(trimmedOutput);
    } catch (error) {
        throw new Error(`Unable to parse npm pack --json output: ${error.message}`);
    }

    if (
        !Array.isArray(parsedOutput) ||
        parsedOutput.length === 0 ||
        parsedOutput[0] === null ||
        typeof parsedOutput[0] !== "object"
    ) {
        throw new Error("npm pack --json output did not contain package metadata");
    }

    const packageInfo = parsedOutput[0];
    if (!Array.isArray(packageInfo.files)) {
        throw new Error("npm pack --json output did not include a files list");
    }

    const files = packageInfo.files
        .map((entry) => {
            if (typeof entry === "string") {
                return entry;
            }

            if (entry && typeof entry.path === "string") {
                return entry.path;
            }

            return "";
        })
        .filter((entry) => entry.length > 0);

    if (files.length === 0) {
        throw new Error("npm pack --json output contained an empty files list");
    }

    return files;
}

/**
 * Get list of files that would be included in the npm package.
 *
 * Uses npm's JSON dry-run output so the check is shell-safe and cross-platform.
 *
 * @returns {string[]} Array of file paths relative to package root
 */
function getPackageFiles() {
    const repoRoot = path.resolve(__dirname, "..");

    try {
        console.log("Computing package file list via npm pack --json --dry-run...");
        const packResult = spawnPlatformCommandSync("npm", ["pack", "--json", "--dry-run"], {
            encoding: "utf8",
            cwd: repoRoot,
            stdio: ["ignore", "pipe", "pipe"],
        });

        if (packResult.error) {
            throw packResult.error;
        }

        if (packResult.status !== 0) {
            const stderr = normalizeToLf(packResult.stderr || "").trim();
            throw new Error(
                `npm pack --json --dry-run failed with exit code ${packResult.status}${stderr ? `: ${stderr}` : ""}`
            );
        }

        return parseNpmPackJsonOutput(packResult.stdout || "");
    } catch (error) {
        if (error && error.code === "ENOENT") {
            console.error(
                "Error creating or reading npm package:",
                `${error.message}\n` +
                    "npm was not found in this hook shell. Verify npm --version in the same shell used for git commits."
            );
        } else {
            console.error("Error creating or reading npm package:", error.message);
        }
        throw error;
    }
}

/**
 * Validate that all .meta files correspond to actual files/directories
 * @param {string[]} files - List of files in the package
 * @returns {Object} Validation result with errors array
 */
function validateMetaFilesHaveTargets(files) {
    const errors = [];
    const fileSet = new Set(files);

    for (const file of files) {
        if (file.endsWith(".meta")) {
            // Remove .meta extension to get the target path
            const targetPath = file.substring(0, file.length - 5);

            // Check if the target file exists directly
            const hasTargetFile = fileSet.has(targetPath);

            // Check if this is a directory .meta by seeing if any files start with targetPath/
            const targetPathPrefix = targetPath + "/";
            const hasFilesInDirectory = files.some((f) => f.startsWith(targetPathPrefix));

            if (!hasTargetFile && !hasFilesInDirectory) {
                errors.push({
                    type: "orphaned-meta",
                    file: file,
                    message: `Meta file '${file}' has no corresponding file or directory in the package`,
                });
            }
        }
    }

    return { valid: errors.length === 0, errors };
}

/**
 * Validate that all files/directories have corresponding .meta files
 * @param {string[]} files - List of files in the package
 * @returns {Object} Validation result with errors array
 */
function validateFilesHaveMetaFiles(files) {
    const errors = [];
    const metaFiles = new Set(files.filter((f) => f.endsWith(".meta")));

    // Files that don't need .meta files (non-Unity assets)
    const excludePatterns = [
        /^package\.json$/,
        /^package-lock\.json$/,
        /^node_modules\//,
        /^\.git\//,
        /^\.github\//,
    ];

    for (const file of files) {
        // Skip .meta files themselves
        if (file.endsWith(".meta")) {
            continue;
        }

        // Skip files that don't need .meta files
        if (excludePatterns.some((pattern) => pattern.test(file))) {
            continue;
        }

        const metaPath = file + ".meta";
        if (!metaFiles.has(metaPath)) {
            errors.push({
                type: "missing-meta",
                file: file,
                message: `File '${file}' is missing its .meta file in the package`,
            });
        }
    }

    return { valid: errors.length === 0, errors };
}

/**
 * Main validation function
 * @param {Object} options - Options for validation
 * @param {boolean} options.check - If true, exit with error code on validation failure
 * @returns {Object} Validation results
 */
function validateNpmMeta(options = {}) {
    console.log("Validating NPM package meta files...\n");

    const files = getPackageFiles();
    console.log(`Found ${files.length} files in package\n`);

    // Count .meta files
    const metaFileCount = files.filter((f) => f.endsWith(".meta")).length;
    const regularFileCount = files.length - metaFileCount;
    console.log(`  - Regular files: ${regularFileCount}`);
    console.log(`  - Meta files: ${metaFileCount}\n`);

    // Validate orphaned .meta files
    console.log("Checking for orphaned .meta files...");
    const orphanedResult = validateMetaFilesHaveTargets(files);
    if (orphanedResult.valid) {
        console.log("✓ All .meta files have corresponding files/directories\n");
    } else {
        console.log(`✗ Found ${orphanedResult.errors.length} orphaned .meta file(s):\n`);
        for (const error of orphanedResult.errors) {
            console.log(`  - ${error.message}`);
        }
        console.log();
    }

    // Validate missing .meta files
    console.log("Checking for missing .meta files...");
    const missingResult = validateFilesHaveMetaFiles(files);
    if (missingResult.valid) {
        console.log("✓ All files have corresponding .meta files\n");
    } else {
        console.log(`✗ Found ${missingResult.errors.length} file(s) missing .meta:\n`);
        for (const error of missingResult.errors) {
            console.log(`  - ${error.message}`);
        }
        console.log();
    }

    // Summary
    const allValid = orphanedResult.valid && missingResult.valid;
    if (allValid) {
        console.log("✓ NPM package meta file validation passed!");
        return { valid: true, errors: [] };
    } else {
        console.log("✗ NPM package meta file validation failed!");
        const allErrors = [...orphanedResult.errors, ...missingResult.errors];

        if (options.check) {
            process.exit(1);
        }

        return { valid: false, errors: allErrors };
    }
}

// Run validation if called directly
if (require.main === module) {
    const args = process.argv.slice(2);
    const check = args.includes("--check");

    try {
        validateNpmMeta({ check });
    } catch (error) {
        console.error("Validation failed with error:", error.message);
        process.exit(1);
    }
}

// Export for testing
module.exports = {
    getPackageFiles,
    parseNpmPackJsonOutput,
    parseTarListingOutput,
    validateMetaFilesHaveTargets,
    validateFilesHaveMetaFiles,
    validateNpmMeta,
};
