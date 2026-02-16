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

const { execSync } = require("child_process");
const fs = require("fs");
const path = require("path");

/**
 * Get list of files that would be included in the npm package
 * @returns {string[]} Array of file paths relative to package root
 */
function getPackageFiles() {
    const repoRoot = path.resolve(__dirname, "..");

    try {
        // Create a temporary tarball
        console.log("Creating package tarball...");
        execSync("npm pack 2>&1 > /dev/null", {
            encoding: "utf8",
            cwd: repoRoot,
        });

        // Find the tarball file
        const tarballs = fs
            .readdirSync(repoRoot)
            .filter((f) => f.endsWith(".tgz") || f.endsWith(".tar.gz"));

        if (tarballs.length === 0) {
            throw new Error("No tarball file found after npm pack");
        }

        const tarballPath = path.join(repoRoot, tarballs[0]);

        // Extract file list from tarball
        const tarOutput = execSync(`tar -tzf "${tarballPath}"`, {
            encoding: "utf8",
            cwd: repoRoot,
        });

        // Parse file list, removing the "package/" prefix and empty lines
        const files = tarOutput
            .split("\n")
            .filter((line) => line.trim())
            .map((line) => line.replace(/^package\//, ""))
            .filter((line) => line); // Remove empty strings

        // Clean up tarball
        fs.unlinkSync(tarballPath);

        return files;
    } catch (error) {
        console.error("Error creating or reading npm package:", error.message);
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
    const fileSet = new Set(files);
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
    validateMetaFilesHaveTargets,
    validateFilesHaveMetaFiles,
    validateNpmMeta,
};
