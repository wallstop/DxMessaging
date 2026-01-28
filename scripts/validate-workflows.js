#!/usr/bin/env node
/**
 * validate-workflows.js
 *
 * Validates GitHub Actions workflow files for problematic patterns, specifically:
 * - Single-line multi-pattern `git add --renormalize` commands (FORBIDDEN)
 * - `git add --renormalize` commands without existence checks
 *
 * @usage
 *   node scripts/validate-workflows.js
 *
 * @exitcodes
 *   0 - Success (no violations found)
 *   1 - Validation failed (one or more violations found)
 *
 * @example
 *   # Run from repository root
 *   node scripts/validate-workflows.js
 *
 *   # Run in CI pipeline
 *   node scripts/validate-workflows.js || exit 1
 */

"use strict";

const fs = require("fs");
const path = require("path");

const WORKFLOWS_DIR = path.join(__dirname, "..", ".github", "workflows");

/**
 * Represents a validation violation.
 */
class Violation {
    constructor(file, line, pattern, message, severity = "error") {
        this.file = file;
        this.line = line;
        this.pattern = pattern;
        this.message = message;
        this.severity = severity;
    }

    toString() {
        const prefix = this.severity === "error" ? "ERROR" : "WARN";
        return `[${prefix}] ${this.file}:${this.line}: ${this.message}\n  Pattern: ${this.pattern}`;
    }
}

/**
 * Checks if a line contains a problematic single-line multi-pattern renormalize command.
 * These commands fail with exit code 128 if any pattern matches no files.
 *
 * @param {string} line - The line to check
 * @returns {boolean} True if the line contains a forbidden pattern
 */
function isForbiddenRenormalizePattern(line) {
    // Match lines that have git add --renormalize with multiple file patterns
    // Forbidden: git add --renormalize -- '*.md' '*.json' '*.yml'
    // Allowed: git add --renormalize -- "*.$ext" "**/*.$ext" (single extension via variable)

    const trimmed = line.trim();

    // Must contain git add --renormalize
    if (!trimmed.includes("git add") || !trimmed.includes("--renormalize")) {
        return false;
    }

    // Skip lines that use shell variable expansion (part of a loop)
    if (trimmed.includes("$ext") || trimmed.includes("${ext}")) {
        return false;
    }

    // Skip lines that target a single specific file (e.g., '.config/dotnet-tools.json')
    // These are safe because the file definitely exists or the step would have failed earlier
    const singleFilePattern = /git add --renormalize\s+--\s+'[^'*?]+'/;
    if (singleFilePattern.test(trimmed)) {
        return false;
    }

    // Count distinct file extension patterns (*.ext or **/*.ext)
    // Use a Set to count unique extensions
    const extensionPatterns = trimmed.match(/\*\.(\w+)/g) || [];
    const uniqueExtensions = new Set(
        extensionPatterns.map((p) => p.replace("*.", ""))
    );

    // If there are multiple unique extensions on one line, it's forbidden
    return uniqueExtensions.size > 1;
}

/**
 * Checks if a renormalize command is properly guarded by an existence check.
 * The existence check should be in a preceding line within the same block.
 *
 * @param {string[]} lines - All lines of the file
 * @param {number} lineIndex - Index of the renormalize line
 * @returns {boolean} True if properly guarded
 */
function hasExistenceCheck(lines, lineIndex) {
    // Look backwards for an existence check pattern
    // Pattern: if git ls-files "*.ext" | grep -q .; then
    // or: if git ls-files "*.$ext" | grep -q .; then
    const lookbackLines = 5;
    const startIndex = Math.max(0, lineIndex - lookbackLines);

    for (let i = lineIndex - 1; i >= startIndex; i--) {
        const line = lines[i];
        if (
            line.includes("git ls-files") &&
            line.includes("grep -q") &&
            (line.includes("then") || lines[i + 1]?.includes("then"))
        ) {
            return true;
        }
        // Also check for a for-loop with if-check pattern
        if (line.includes("for ext in") || line.includes("for EXT in")) {
            // Check if there's a git ls-files check between the for and the renormalize
            for (let j = i + 1; j < lineIndex; j++) {
                if (lines[j].includes("git ls-files") && lines[j].includes("grep -q")) {
                    return true;
                }
            }
        }
    }
    return false;
}

/**
 * Validates a single workflow file.
 *
 * @param {string} filePath - Absolute path to the workflow file
 * @returns {Violation[]} Array of violations found
 */
function validateWorkflow(filePath) {
    const violations = [];
    const relativePath = path.relative(
        path.join(__dirname, ".."),
        filePath
    );

    let content;
    try {
        content = fs.readFileSync(filePath, "utf8");
    } catch (error) {
        violations.push(
            new Violation(relativePath, 0, "", `Failed to read file: ${error.message}`)
        );
        return violations;
    }

    const lines = content.split(/\r?\n/);

    lines.forEach((line, index) => {
        const lineNumber = index + 1;

        // Check for forbidden single-line multi-pattern renormalize
        if (isForbiddenRenormalizePattern(line)) {
            violations.push(
                new Violation(
                    relativePath,
                    lineNumber,
                    line.trim(),
                    "FORBIDDEN: Single-line multi-pattern git add --renormalize. Use per-extension loop pattern instead.",
                    "error"
                )
            );
        }

        // Check for unguarded renormalize commands (warning only for non-variable patterns)
        if (
            line.includes("git add") &&
            line.includes("--renormalize") &&
            !line.includes("$ext") &&
            !line.includes("${ext}") &&
            line.includes("*.")
        ) {
            if (!hasExistenceCheck(lines, index)) {
                violations.push(
                    new Violation(
                        relativePath,
                        lineNumber,
                        line.trim(),
                        "WARNING: git add --renormalize without existence check may fail if pattern matches no files.",
                        "warning"
                    )
                );
            }
        }
    });

    return violations;
}

/**
 * Main entry point.
 */
function main() {
    console.log("Validating workflow files for git add --renormalize patterns...\n");

    if (!fs.existsSync(WORKFLOWS_DIR)) {
        console.log(`Workflows directory not found: ${WORKFLOWS_DIR}`);
        console.log("Nothing to validate.");
        process.exit(0);
    }

    const workflowFiles = fs.readdirSync(WORKFLOWS_DIR).filter((file) =>
        file.endsWith(".yml") || file.endsWith(".yaml")
    );

    if (workflowFiles.length === 0) {
        console.log("No workflow files found.");
        process.exit(0);
    }

    console.log(`Found ${workflowFiles.length} workflow file(s)\n`);

    const allViolations = [];

    workflowFiles.forEach((file) => {
        const filePath = path.join(WORKFLOWS_DIR, file);
        const violations = validateWorkflow(filePath);
        allViolations.push(...violations);
    });

    const errors = allViolations.filter((v) => v.severity === "error");
    const warnings = allViolations.filter((v) => v.severity === "warning");

    if (allViolations.length === 0) {
        console.log("✅ All workflow files passed validation.\n");
        console.log("No forbidden git add --renormalize patterns detected.");
        process.exit(0);
    }

    if (errors.length > 0) {
        console.log(`\n❌ Found ${errors.length} error(s):\n`);
        errors.forEach((v) => console.log(v.toString() + "\n"));
    }

    if (warnings.length > 0) {
        console.log(`\n⚠️  Found ${warnings.length} warning(s):\n`);
        warnings.forEach((v) => console.log(v.toString() + "\n"));
    }

    console.log("\n--- Summary ---");
    console.log(`Errors:   ${errors.length}`);
    console.log(`Warnings: ${warnings.length}`);

    if (errors.length > 0) {
        console.log("\nValidation FAILED. Please fix the errors above.");
        console.log(
            "\nSee .llm/skills/github-actions/git-renormalize-patterns.md for the required pattern."
        );
        process.exit(1);
    }

    console.log("\nValidation passed with warnings.");
    process.exit(0);
}

main();
