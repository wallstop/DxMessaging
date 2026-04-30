#!/usr/bin/env node
/**
 * validate-workflows.js
 *
 * Validates GitHub Actions workflow files for problematic patterns, specifically:
 * - Single-line multi-pattern `git add --renormalize` commands (FORBIDDEN)
 * - `git add --renormalize` commands without existence checks
 * - Bash syntax in Windows-targeting jobs without Bash-compatible shell overrides
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

const { execFileSync } = require("child_process");
const fs = require("fs");
const path = require("path");
const { normalizeToLf } = require("./lib/quote-parser");

const REPO_ROOT = path.join(__dirname, "..");
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

function getIndent(line) {
    return line.length - line.trimStart().length;
}

function usesVariableExtensionPattern(line) {
    return /\*\.\$\{?[A-Za-z_][A-Za-z0-9_]*\}?/.test(line);
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
    if (usesVariableExtensionPattern(trimmed)) {
        return false;
    }

    const commandMatch =
        /git add --renormalize\s+--\s+(.+?)(?:\s*(?:&&|\|\||;|\|)\s*.+)?$/.exec(
            trimmed
        );
    const renormalizeArgs = commandMatch ? commandMatch[1] : trimmed;

    // Skip lines that target a single specific file (e.g., '.config/dotnet-tools.json')
    // These are safe because the file definitely exists or the step would have failed earlier
    const singleFilePattern = /^["']?[^"'*?\s]+["']?$/;
    if (singleFilePattern.test(renormalizeArgs)) {
        return false;
    }

    // Count distinct file extension patterns (*.ext or **/*.ext)
    // Use a Set to count unique extensions
    const extensionPatterns = renormalizeArgs.match(/\*\.(\w+)/g) || [];
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
    const lookbackLines = 10;
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

function isGitIgnoredPath(repoRoot, relativePath, execFileSyncImpl = execFileSync) {
    if (typeof relativePath !== "string" || relativePath.trim().length === 0) {
        return false;
    }

    const runCheckIgnore = (args) =>
        execFileSyncImpl("git", args, {
            cwd: repoRoot,
            stdio: ["ignore", "ignore", "pipe"],
        });

    const isUnsupportedNoIndex = (error) => {
        const stderr =
            error && error.stderr
                ? String(error.stderr)
                : "";
        const message = error && error.message ? String(error.message) : "";
        const combined = `${message}\n${stderr}`;

        return (
            (error && typeof error.status === "number" && error.status === 129)
            || /unknown option|unknown switch/i.test(combined)
            || /check-ignore/i.test(combined) && /no-index/i.test(combined)
        );
    };

    try {
        runCheckIgnore(["check-ignore", "--quiet", "--no-index", "--", relativePath]);
        return true;
    } catch (error) {
        if (error && typeof error.status === "number" && error.status === 1) {
            return false;
        }

        if (error && error.code === "ENOENT") {
            return false;
        }

        if (isUnsupportedNoIndex(error)) {
            try {
                runCheckIgnore(["check-ignore", "--quiet", "--", relativePath]);
                return true;
            } catch (fallbackError) {
                if (
                    fallbackError
                    && typeof fallbackError.status === "number"
                    && fallbackError.status === 1
                ) {
                    return false;
                }

                if (fallbackError && fallbackError.code === "ENOENT") {
                    return false;
                }

                const fallbackMessage =
                    fallbackError && fallbackError.message
                        ? fallbackError.message
                        : String(fallbackError);
                throw new Error(
                    `Unable to evaluate git ignore status for '${relativePath}' after falling back from --no-index: ${fallbackMessage}`
                );
            }
        }

        const message = error && error.message ? error.message : String(error);
        throw new Error(
            `Unable to evaluate git ignore status for '${relativePath}': ${message}`
        );
    }
}

function extractWorkflowPathEntries(lines) {
    const entries = [];
    let inPathsBlock = false;
    let pathsIndent = -1;

    for (let i = 0; i < lines.length; i++) {
        const line = lines[i];
        const trimmed = line.trim();
        const indent = getIndent(line);

        if (!inPathsBlock && /^\s*paths:\s*$/.test(line)) {
            inPathsBlock = true;
            pathsIndent = indent;
            continue;
        }

        if (!inPathsBlock) {
            continue;
        }

        if (trimmed.length === 0 || trimmed.startsWith("#")) {
            continue;
        }

        if (indent <= pathsIndent && !/^\s*-\s+/.test(line)) {
            inPathsBlock = false;
            pathsIndent = -1;

            if (/^\s*paths:\s*$/.test(line)) {
                inPathsBlock = true;
                pathsIndent = indent;
            }
            continue;
        }

        const pathEntry = /^\s*-\s*["']?([^"'#]+)["']?\s*(?:#.*)?$/.exec(line);
        if (pathEntry) {
            entries.push({
                line: i + 1,
                path: pathEntry[1].trim(),
            });
        }
    }

    return entries;
}

function isLiteralPath(pathValue) {
    return !/[\*\?\[\]\{\}]|\$\{\{/.test(pathValue) && !pathValue.startsWith("!");
}

function findIgnoredPathViolations(
    relativePath,
    lines,
    repoRoot = REPO_ROOT,
    isIgnoredPathFn = isGitIgnoredPath
) {
    const violations = [];
    const entries = extractWorkflowPathEntries(lines);

    for (const entry of entries) {
        if (!isLiteralPath(entry.path)) {
            continue;
        }

        if (!isIgnoredPathFn(repoRoot, entry.path)) {
            continue;
        }

        violations.push(
            new Violation(
                relativePath,
                entry.line,
                entry.path,
                `Workflow trigger path '${entry.path}' is ignored by git and cannot trigger this workflow. Remove it from paths filters or update ignore policy.`,
                "error"
            )
        );
    }

    return violations;
}

function extractRunBlocks(lines) {
    const blocks = [];

    for (let i = 0; i < lines.length; i++) {
        const line = lines[i];
        const blockRunMatch = /^(\s*)(?:-\s+)?run:\s*[>|][+-]?\s*$/.exec(line);

        if (blockRunMatch) {
            const baseIndent = blockRunMatch[1].length;
            const blockLines = [];
            let j = i + 1;

            while (j < lines.length) {
                const nextLine = lines[j];
                const trimmed = nextLine.trim();
                const nextIndent = getIndent(nextLine);

                if (trimmed.length > 0 && nextIndent <= baseIndent) {
                    break;
                }

                blockLines.push(nextLine.trim());
                j++;
            }

            blocks.push({
                startLine: i + 1,
                text: blockLines.join("\n").trim(),
            });

            i = j - 1;
            continue;
        }

        const inlineRunMatch = /^\s*(?:-\s+)?run:\s*(.+?)\s*$/.exec(line);
        if (inlineRunMatch) {
            blocks.push({
                startLine: i + 1,
                text: inlineRunMatch[1].trim(),
            });
        }
    }

    return blocks;
}

function findLockfileInstallViolations(relativePath, lines, packageLockIgnored) {
    const violations = [];

    if (!packageLockIgnored) {
        return violations;
    }

    const runBlocks = extractRunBlocks(lines);

    for (const block of runBlocks) {
        const hasNpmCi = /(^|\n|;|&&)\s*npm\s+ci\b/m.test(block.text);
        const hasNpmInstall = /(^|\n|;|&&)\s*npm\s+(?:install|i)\b/m.test(block.text);

        if (hasNpmInstall && !hasNpmCi) {
            violations.push(
                new Violation(
                    relativePath,
                    block.startLine,
                    "npm install",
                    "Repository ignores package-lock.json, so dependency install blocks must be lockfile-aware. Use npm ci when package-lock.json exists and npm install fallback when it does not.",
                    "error"
                )
            );
            continue;
        }

        if (!hasNpmCi) {
            continue;
        }

        const hasLockfileCheck =
            /\[\s*-f\s+package-lock\.json\s*\]/.test(block.text) ||
            /\btest\s+-f\s+package-lock\.json\b/.test(block.text);
        const hasAnyIfElseFallback =
            /\bif\b[\s\S]*?\bnpm\s+ci\b[\s\S]*?\belse\b[\s\S]*?\bnpm\s+(?:install|i)\b/.test(block.text);
        const hasOrFallbackInstall =
            /\bnpm\s+ci\b\s*\|\|\s*\bnpm\s+(?:install|i)\b/.test(block.text);
        const hasMissingLockfileHardFail =
            /\[\s*!\s+-f\s+package-lock\.json\s*\][\s\S]*?\bexit\s+1\b/.test(block.text);

        if (hasOrFallbackInstall) {
            continue;
        }

        if (hasMissingLockfileHardFail) {
            violations.push(
                new Violation(
                    relativePath,
                    block.startLine,
                    "npm ci",
                    "Repository ignores package-lock.json, so workflows must not fail when the lockfile is absent. Use npm ci/npm install fallback.",
                    "error"
                )
            );
            continue;
        }

        if (!hasLockfileCheck || !hasAnyIfElseFallback) {
            violations.push(
                new Violation(
                    relativePath,
                    block.startLine,
                    "npm ci",
                    "Repository ignores package-lock.json, so npm ci blocks must include a lockfile presence check and npm install fallback.",
                    "error"
                )
            );
        }
    }

    return violations;
}

function extractJobs(lines) {
    const jobs = [];
    let inJobsBlock = false;
    let jobsIndent = -1;

    for (let i = 0; i < lines.length; i++) {
        const line = lines[i];
        const trimmed = line.trim();
        const indent = getIndent(line);

        if (!inJobsBlock && /^\s*jobs:\s*$/.test(line)) {
            inJobsBlock = true;
            jobsIndent = indent;
            continue;
        }

        if (!inJobsBlock) {
            continue;
        }

        if (trimmed.length === 0 || trimmed.startsWith("#")) {
            continue;
        }

        if (indent <= jobsIndent) {
            break;
        }

        const jobHeader = /^\s*([A-Za-z0-9_-]+):\s*$/.exec(line);
        if (!jobHeader || indent !== jobsIndent + 2) {
            continue;
        }

        let endLine = lines.length - 1;
        for (let j = i + 1; j < lines.length; j++) {
            const nextLine = lines[j];
            const nextTrimmed = nextLine.trim();
            const nextIndent = getIndent(nextLine);

            if (nextTrimmed.length === 0 || nextTrimmed.startsWith("#")) {
                continue;
            }

            if (nextIndent <= jobsIndent) {
                endLine = j - 1;
                break;
            }

            if (
                nextIndent === jobsIndent + 2
                && /^\s*[A-Za-z0-9_-]+:\s*$/.test(nextLine)
            ) {
                endLine = j - 1;
                break;
            }
        }

        jobs.push({
            id: jobHeader[1],
            startLine: i + 1,
            endLine: endLine + 1,
            indent,
        });

        i = endLine;
    }

    return jobs;
}

function extractDefaultRunShellFromBlock(lines, startIndex, endIndex, defaultsIndent) {
    let runIndent = -1;

    for (let i = startIndex + 1; i <= endIndex && i < lines.length; i++) {
        const line = lines[i];
        const trimmed = line.trim();
        const indent = getIndent(line);

        if (trimmed.length === 0 || trimmed.startsWith("#")) {
            continue;
        }

        if (indent <= defaultsIndent) {
            break;
        }

        if (runIndent === -1 && /^\s*run:\s*$/.test(line) && indent === defaultsIndent + 2) {
            runIndent = indent;
            continue;
        }

        if (runIndent !== -1) {
            if (indent <= runIndent) {
                break;
            }

            const shellMatch = /^\s*shell:\s*["']?([^"'\s#]+)["']?\s*(?:#.*)?$/.exec(line);
            if (shellMatch && indent === runIndent + 2) {
                return shellMatch[1].toLowerCase();
            }
        }
    }

    return null;
}

function extractWorkflowDefaultsShell(lines) {
    for (let i = 0; i < lines.length; i++) {
        const line = lines[i];
        if (!/^\s*defaults:\s*$/.test(line) || getIndent(line) !== 0) {
            continue;
        }

        return extractDefaultRunShellFromBlock(lines, i, lines.length - 1, 0);
    }

    return null;
}

function extractJobDefaultsShell(lines, job) {
    const startIndex = job.startLine - 1;
    const endIndex = job.endLine - 1;

    for (let i = startIndex + 1; i <= endIndex; i++) {
        const line = lines[i];
        const trimmed = line.trim();
        const indent = getIndent(line);

        if (trimmed.length === 0 || trimmed.startsWith("#")) {
            continue;
        }

        if (indent <= job.indent) {
            break;
        }

        if (!/^\s*defaults:\s*$/.test(line) || indent !== job.indent + 2) {
            continue;
        }

        return extractDefaultRunShellFromBlock(lines, i, endIndex, indent);
    }

    return null;
}

function jobTargetsWindows(lines, job) {
    const startIndex = job.startLine - 1;
    const endIndex = job.endLine - 1;
    let runsOnValue = null;

    for (let i = startIndex + 1; i <= endIndex; i++) {
        const line = lines[i];
        const trimmed = line.trim();
        const indent = getIndent(line);

        if (trimmed.length === 0 || trimmed.startsWith("#")) {
            continue;
        }

        if (indent <= job.indent) {
            break;
        }

        if (indent !== job.indent + 2) {
            continue;
        }

        const runsOnMatch = /^\s*runs-on:\s*(.+?)\s*$/.exec(line);
        if (!runsOnMatch) {
            continue;
        }

        runsOnValue = runsOnMatch[1].trim();
        break;
    }

    if (!runsOnValue) {
        return false;
    }

    if (/\bwindows(?:-[a-z0-9]+)?\b/i.test(runsOnValue)) {
        return true;
    }

    if (!/matrix\./i.test(runsOnValue)) {
        return false;
    }

    let inMatrixBlock = false;
    let matrixIndent = -1;

    for (let i = startIndex + 1; i <= endIndex; i++) {
        const line = lines[i];
        const trimmed = line.trim();
        const indent = getIndent(line);

        if (!inMatrixBlock && /^\s*matrix:\s*$/.test(line)) {
            inMatrixBlock = true;
            matrixIndent = indent;
            continue;
        }

        if (!inMatrixBlock) {
            continue;
        }

        if (trimmed.length === 0 || trimmed.startsWith("#")) {
            continue;
        }

        if (indent <= matrixIndent) {
            inMatrixBlock = false;
            matrixIndent = -1;
            continue;
        }

        if (/\bwindows(?:-[a-z0-9]+)?\b/i.test(line)) {
            return true;
        }
    }

    return false;
}

function extractStepRun(lines, stepStartIndex, stepEndIndex) {
    for (let i = stepStartIndex; i <= stepEndIndex; i++) {
        const line = lines[i];
        const blockRunMatch = /^(\s*)(?:-\s+)?run:\s*[>|][+-]?\s*$/.exec(line);

        if (blockRunMatch) {
            const baseIndent = blockRunMatch[1].length;
            const blockLines = [];
            let j = i + 1;

            while (j <= stepEndIndex) {
                const nextLine = lines[j];
                const trimmed = nextLine.trim();
                const nextIndent = getIndent(nextLine);

                if (trimmed.length > 0 && nextIndent <= baseIndent) {
                    break;
                }

                blockLines.push(nextLine.trim());
                j++;
            }

            return {
                line: i + 1,
                text: blockLines.join("\n").trim(),
            };
        }

        const inlineRunMatch = /^\s*(?:-\s+)?run:\s*(.+?)\s*$/.exec(line);
        if (inlineRunMatch) {
            return {
                line: i + 1,
                text: inlineRunMatch[1].trim(),
            };
        }
    }

    return null;
}

function extractStepShell(lines, stepStartIndex, stepEndIndex) {
    for (let i = stepStartIndex; i <= stepEndIndex; i++) {
        const line = lines[i];
        const shellMatch = /^\s*shell:\s*["']?([^"'\s#]+)["']?\s*(?:#.*)?$/.exec(line);

        if (shellMatch) {
            return shellMatch[1].toLowerCase();
        }
    }

    return null;
}

function extractJobSteps(lines, job) {
    const steps = [];
    const startIndex = job.startLine - 1;
    const endIndex = job.endLine - 1;
    let stepsStartIndex = -1;
    let stepsIndent = -1;

    for (let i = startIndex + 1; i <= endIndex; i++) {
        const line = lines[i];
        if (/^\s*steps:\s*$/.test(line) && getIndent(line) === job.indent + 2) {
            stepsStartIndex = i;
            stepsIndent = getIndent(line);
            break;
        }
    }

    if (stepsStartIndex === -1) {
        return steps;
    }

    let i = stepsStartIndex + 1;
    while (i <= endIndex) {
        const line = lines[i];
        const trimmed = line.trim();
        const indent = getIndent(line);

        if (trimmed.length === 0 || trimmed.startsWith("#")) {
            i++;
            continue;
        }

        if (indent <= stepsIndent) {
            break;
        }

        if (!(indent === stepsIndent + 2 && /^\s*-\s+/.test(line))) {
            i++;
            continue;
        }

        const stepStartIndex = i;
        let stepEndIndex = endIndex;

        for (let j = i + 1; j <= endIndex; j++) {
            const nextLine = lines[j];
            const nextTrimmed = nextLine.trim();
            const nextIndent = getIndent(nextLine);

            if (nextTrimmed.length === 0 || nextTrimmed.startsWith("#")) {
                continue;
            }

            if (nextIndent <= stepsIndent) {
                stepEndIndex = j - 1;
                break;
            }

            if (nextIndent === stepsIndent + 2 && /^\s*-\s+/.test(nextLine)) {
                stepEndIndex = j - 1;
                break;
            }
        }

        const run = extractStepRun(lines, stepStartIndex, stepEndIndex);
        steps.push({
            shell: extractStepShell(lines, stepStartIndex, stepEndIndex),
            run,
        });

        i = stepEndIndex + 1;
    }

    return steps;
}

const BASH_SYNTAX_PATTERNS = [
    {
        label: "if/elif [ ... ] conditional",
        regex: /^(?:if|elif)\s+\[\[?/,
    },
    {
        label: "for ... in loop",
        regex: /^for\s+[A-Za-z_][A-Za-z0-9_]*\s+in\b/,
    },
    {
        label: "while [ ... ] loop",
        regex: /^while\s+\[\[?/,
    },
    {
        label: "until [ ... ] loop",
        regex: /^until\s+\[\[?/,
    },
    {
        label: "set -e/-o shell option",
        regex: /^set\s+-[A-Za-z]/,
    },
    {
        label: "test -f/-d shell check",
        regex: /^test\s+-[A-Za-z]/,
    },
    {
        label: "logical chaining operator (&&/||)",
        regex: /&&|\|\|/,
    },
];

function detectBashSyntaxPattern(runText) {
    if (typeof runText !== "string" || runText.trim().length === 0) {
        return null;
    }

    const runLines = runText.split("\n");

    for (const rawLine of runLines) {
        const line = rawLine.trim();
        if (line.length === 0 || line.startsWith("#")) {
            continue;
        }

        for (const pattern of BASH_SYNTAX_PATTERNS) {
            if (pattern.regex.test(line)) {
                return pattern.label;
            }
        }
    }

    return null;
}

function isBashCompatibleShell(shell) {
    return shell === "bash" || shell === "sh";
}

function findWindowsBashPortabilityViolations(relativePath, lines) {
    const violations = [];
    const jobs = extractJobs(lines);
    const workflowDefaultsShell = extractWorkflowDefaultsShell(lines);

    for (const job of jobs) {
        if (!jobTargetsWindows(lines, job)) {
            continue;
        }

        const jobDefaultsShell = extractJobDefaultsShell(lines, job);
        const steps = extractJobSteps(lines, job);

        for (const step of steps) {
            if (!step.run || typeof step.run.text !== "string") {
                continue;
            }

            const bashPattern = detectBashSyntaxPattern(step.run.text);
            if (!bashPattern) {
                continue;
            }

            const effectiveShell = step.shell || jobDefaultsShell || workflowDefaultsShell;
            if (isBashCompatibleShell(effectiveShell)) {
                continue;
            }

            violations.push(
                new Violation(
                    relativePath,
                    step.run.line,
                    bashPattern,
                    `Windows-targeting workflow job '${job.id}' uses Bash syntax (${bashPattern}) without a Bash-compatible shell. Add 'shell: bash' to the step or set 'defaults.run.shell: bash' at job/workflow scope.`,
                    "error"
                )
            );
        }
    }

    return violations;
}

/**
 * Validates a single workflow file.
 *
 * @param {string} filePath - Absolute path to the workflow file
 * @returns {Violation[]} Array of violations found
 */
function validateWorkflow(filePath, options = {}) {
    const violations = [];
    const repoRoot = options.repoRoot || REPO_ROOT;
    const isIgnoredPathFn = options.isIgnoredPathFn || isGitIgnoredPath;
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

    const lines = normalizeToLf(content).split("\n");

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
            !usesVariableExtensionPattern(line) &&
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

    try {
        violations.push(
            ...findIgnoredPathViolations(
                relativePath,
                lines,
                repoRoot,
                isIgnoredPathFn
            )
        );

        const packageLockIgnored = isIgnoredPathFn(repoRoot, "package-lock.json");
        violations.push(
            ...findLockfileInstallViolations(relativePath, lines, packageLockIgnored)
        );

        violations.push(
            ...findWindowsBashPortabilityViolations(relativePath, lines)
        );
    } catch (error) {
        violations.push(
            new Violation(
                relativePath,
                0,
                "git check-ignore",
                `Workflow validation failed while evaluating ignore policy: ${error.message}`,
                "error"
            )
        );
    }

    return violations;
}

/**
 * Main entry point.
 */
function main() {
    console.log("Validating workflow files for policy and reliability patterns...\n");

    if (!fs.existsSync(WORKFLOWS_DIR)) {
        console.log(`Workflows directory not found: ${WORKFLOWS_DIR}`);
        console.log("Nothing to validate.");
        process.exit(0);
    }

    let workflowFiles;
    try {
        workflowFiles = fs
            .readdirSync(WORKFLOWS_DIR)
            .filter((file) => file.endsWith(".yml") || file.endsWith(".yaml"));
    } catch (error) {
        // Unlike recursive scanners, this validator cannot proceed without the workflows root.
        console.error(`Unable to read workflows directory: ${error.message}`);
        process.exit(1);
    }

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
        console.log("No workflow policy violations detected.");
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
            "\nSee .llm/skills/github-actions/git-renormalize-patterns.md for renormalize guidance."
        );
        process.exit(1);
    }

    console.log("\nValidation passed with warnings.");
    process.exit(0);
}

// Export for testing when required as a module
if (typeof module !== 'undefined' && module.exports) {
    module.exports = {
        isForbiddenRenormalizePattern,
        hasExistenceCheck,
        isGitIgnoredPath,
        extractWorkflowPathEntries,
        findIgnoredPathViolations,
        extractRunBlocks,
        findLockfileInstallViolations,
        extractJobs,
        extractWorkflowDefaultsShell,
        extractJobDefaultsShell,
        jobTargetsWindows,
        extractJobSteps,
        detectBashSyntaxPattern,
        findWindowsBashPortabilityViolations,
        validateWorkflow,
        Violation,
    };
}

// Only run main when executed directly (not when required as a module)
if (require.main === module) {
    main();
}
