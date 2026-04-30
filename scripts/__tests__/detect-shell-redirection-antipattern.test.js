/**
 * @fileoverview Guards against shell-redirection anti-patterns in production scripts.
 *
 * Shell redirection in command strings (for example `> /dev/null 2>&1`) is not
 * cross-platform and can fail on Windows hooks.
 */

"use strict";

const fs = require("fs");
const path = require("path");
const { normalizeToLf } = require("../lib/quote-parser");

function collectProductionScriptFiles(directoryPath) {
    const entries = fs.readdirSync(directoryPath, { withFileTypes: true });
    const files = [];

    for (const entry of entries) {
        const absolutePath = path.join(directoryPath, entry.name);

        if (entry.isDirectory()) {
            if (entry.name === "__tests__") {
                continue;
            }

            files.push(...collectProductionScriptFiles(absolutePath));
            continue;
        }

        if (!entry.isFile()) {
            continue;
        }

        if (!entry.name.endsWith(".js")) {
            continue;
        }

        files.push(absolutePath);
    }

    return files;
}

function findShellRedirectionViolations(filePath) {
    const content = normalizeToLf(fs.readFileSync(filePath, "utf8"));
    const violations = [];

    const commandLiteralPattern =
        /\b(?:execSync|spawnSync|execFileSync)\s*\(\s*(["'`])((?:\\.|(?!\1)[\s\S])*?)\1/g;
    const shellRedirectionPattern = /(?:^|\s)(?:>>?|<)\s|2>&1/;

    let match = commandLiteralPattern.exec(content);
    while (match !== null) {
        const commandLiteral = match[2];
        if (shellRedirectionPattern.test(commandLiteral)) {
            const line = content.slice(0, match.index).split("\n").length;
            const source = match[0].split("\n")[0].trim();

            violations.push({
                line,
                source,
            });
        }

        match = commandLiteralPattern.exec(content);
    }

    return violations;
}

function findNonPortableNpmCommandViolations(filePath) {
    const content = normalizeToLf(fs.readFileSync(filePath, "utf8"));
    const violations = [];

    const directNpmPattern =
        /\b(?:spawnSync|execFileSync|execSync)\s*\(\s*(["'`])(npm|npx)\1/g;
    const legacyShellWrapperPattern =
        /\b(?:spawnSync|execFileSync|execSync)\s*\(\s*toShellCommand\s*\(/g;

    let match = directNpmPattern.exec(content);
    while (match !== null) {
        const line = content.slice(0, match.index).split("\n").length;
        const source = match[0].split("\n")[0].trim();

        violations.push({
            line,
            source,
            reason: "direct-npm-command",
        });

        match = directNpmPattern.exec(content);
    }

    match = legacyShellWrapperPattern.exec(content);
    while (match !== null) {
        const line = content.slice(0, match.index).split("\n").length;
        const source = match[0].split("\n")[0].trim();

        violations.push({
            line,
            source,
            reason: "legacy-shell-wrapper",
        });

        match = legacyShellWrapperPattern.exec(content);
    }

    return violations;
}

describe("detect-shell-redirection-antipattern", () => {
    test("production scripts avoid shell redirection in child_process command strings", () => {
        const scriptsRoot = path.resolve(__dirname, "..");
        const scriptFiles = collectProductionScriptFiles(scriptsRoot);
        const failures = [];

        for (const filePath of scriptFiles) {
            const violations = [
                ...findShellRedirectionViolations(filePath).map((violation) => ({
                    ...violation,
                    reason: "shell-redirection",
                })),
                ...findNonPortableNpmCommandViolations(filePath),
            ];
            if (violations.length === 0) {
                continue;
            }

            failures.push({
                filePath: path.relative(path.resolve(__dirname, "../.."), filePath),
                violations,
            });
        }

        if (failures.length > 0) {
            const details = failures
                .map((failure) => {
                    const lines = failure.violations
                        .map(
                            (violation) =>
                                `    line ${violation.line} [${violation.reason}]: ${violation.source}`
                        )
                        .join("\n");
                    return `  ${failure.filePath}\n${lines}`;
                })
                .join("\n");

            throw new Error(
                `Found shell command portability violations in production scripts:\n${details}\n` +
                    "Use child_process argument arrays with stdio options and call spawnPlatformCommandSync() for npm/npx invocations."
            );
        }
    });
});
