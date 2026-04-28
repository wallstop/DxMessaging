#!/usr/bin/env node
/**
 * validate-pre-commit-tooling.js
 *
 * Enforces non-interactive Node tooling rules for local hooks:
 * - npx calls must explicitly set install policy via --yes/-y or --no.
 * - Jest-related hooks must use scripts/run-managed-jest.js for deterministic execution.
 * - yamllint must be configured as a non-optional hook (no conditional skip wrappers).
 */

"use strict";

const fs = require("fs");
const path = require("path");
const { normalizeToLf } = require("./lib/quote-parser");

const PRE_COMMIT_CONFIG_PATH = path.join(__dirname, "..", ".pre-commit-config.yaml");

class Violation {
    constructor(hookId, line, message, entry) {
        this.hookId = hookId;
        this.line = line;
        this.message = message;
        this.entry = entry;
    }

    toString() {
        return `${this.hookId} (line ${this.line}): ${this.message}\n  entry: ${this.entry}`;
    }
}

function getIndent(line) {
    return line.length - line.trimStart().length;
}

function parseHookEntries(content) {
    const normalized = normalizeToLf(content);
    const lines = normalized.split("\n");
    const entries = [];

    let currentHookId = null;

    for (let i = 0; i < lines.length; i++) {
        const line = lines[i];
        const idMatch = /^(\s*)-\s+id:\s*([^\s#]+)\s*$/.exec(line);
        if (idMatch) {
            currentHookId = idMatch[2].trim();
            continue;
        }

        if (!currentHookId) {
            continue;
        }

        const entryMatch = /^(\s*)entry:\s*(.*)$/.exec(line);
        if (!entryMatch) {
            continue;
        }

        const entryIndent = entryMatch[1].length;
        const entryValue = entryMatch[2].trim();
        let command;

        if ([">", ">-", "|", "|-"] .includes(entryValue)) {
            const blockLines = [];
            let j = i + 1;
            while (j < lines.length) {
                const nextLine = lines[j];
                const nextLineIndent = getIndent(nextLine);

                if (nextLine.trim().length > 0 && nextLineIndent <= entryIndent) {
                    break;
                }

                if (nextLine.trim().length > 0) {
                    blockLines.push(nextLine.trim());
                }

                j++;
            }
            command = blockLines.join(" ").replace(/\s+/g, " ").trim();
        } else {
            command = entryValue;
        }

        entries.push({ id: currentHookId, line: i + 1, entry: command });
    }

    return entries;
}

function parseHookIds(content) {
    const lines = normalizeToLf(content).split("\n");
    const ids = [];

    for (let i = 0; i < lines.length; i++) {
        const idMatch = /^\s*-\s+id:\s*([^\s#]+)\s*$/.exec(lines[i]);
        if (idMatch) {
            ids.push({ id: idMatch[1].trim(), line: i + 1 });
        }
    }

    return ids;
}

function tokenizeCommand(entry) {
    const tokens = entry.match(/"(?:\\.|[^"])*"|'(?:\\.|[^'])*'|\S+/g) || [];
    return tokens.map((token) => token.replace(/^['"]|['"]$/g, ""));
}

function hasNpxInstallPolicy(entry) {
    const tokens = tokenizeCommand(entry);
    let foundNpx = false;

    for (let i = 0; i < tokens.length; i++) {
        if (tokens[i] !== "npx") {
            continue;
        }

        foundNpx = true;
        let hasPolicy = false;

        for (let j = i + 1; j < tokens.length; j++) {
            const token = tokens[j];

            if (token === "--yes" || token === "-y" || token === "--no") {
                hasPolicy = true;
                break;
            }

            if (token === "--") {
                break;
            }

            if (!token.startsWith("-")) {
                break;
            }
        }

        if (!hasPolicy) {
            return false;
        }
    }

    if (foundNpx) {
        return true;
    }

    // Fallback for quoted shell fragments that contain npx but were tokenized as a single token.
    // This check is intentionally lexical and does not attempt to evaluate shell expansion.
    if (/\bnpx\b/.test(entry)) {
        return /\b(--yes|-y|--no)\b/.test(entry);
    }

    return true;
}

function usesManagedJestWrapper(entry) {
    return /\bnode\b\s+scripts\/run-managed-jest\.js\b/.test(entry);
}

function isJestRelatedHook(hookId, entry) {
    return (
        usesManagedJestWrapper(entry) ||
        /\bjest\b/.test(entry) ||
        /script-(?:parser-)?tests/.test(hookId)
    );
}

function hasManagedJestInvocation(hookIdOrEntry, maybeEntry) {
    const hookId = maybeEntry === undefined ? "" : hookIdOrEntry;
    const entry = maybeEntry === undefined ? hookIdOrEntry : maybeEntry;

    if (!isJestRelatedHook(hookId, entry)) {
        return true;
    }

    return usesManagedJestWrapper(entry);
}

function validateHookEntries(entries) {
    const violations = [];

    for (const hook of entries) {
        if (/\bnpx\b/.test(hook.entry) && !hasNpxInstallPolicy(hook.entry)) {
            violations.push(
                new Violation(
                    hook.id,
                    hook.line,
                    "npx entry must explicitly set install policy with --yes/-y or --no.",
                    hook.entry
                )
            );
        }

        if (!hasManagedJestInvocation(hook.id, hook.entry)) {
            violations.push(
                new Violation(
                    hook.id,
                    hook.line,
                    "Jest-related hooks must invoke node scripts/run-managed-jest.js.",
                    hook.entry
                )
            );
        }
    }

    return violations;
}

function validateYamllintPolicy(content) {
    const violations = [];
    const normalized = normalizeToLf(content);
    const lines = normalized.split("\n");
    const hookIds = parseHookIds(content);
    const yamllintHook = hookIds.find((hook) => hook.id === "yamllint");

    if (!yamllintHook) {
        violations.push(
            new Violation(
                "yamllint",
                1,
                "Missing required yamllint hook. Configure a non-optional yamllint hook in .pre-commit-config.yaml.",
                "(missing hook)"
            )
        );
    }

    const forbiddenPatterns = [
        /yamllint not installed; skipping/i,
        /command\s+-v\s+yamllint/i,
    ];

    for (const pattern of forbiddenPatterns) {
        const lineIndex = lines.findIndex((line) => pattern.test(line));
        if (lineIndex !== -1) {
            violations.push(
                new Violation(
                    "yamllint",
                    lineIndex + 1,
                    "yamllint hook must not be conditionally skipped; use a deterministic managed hook.",
                    lines[lineIndex].trim()
                )
            );
        }
    }

    return violations;
}

function validateConfigContent(content) {
    const hooks = parseHookEntries(content);
    return [...validateHookEntries(hooks), ...validateYamllintPolicy(content)];
}

function validateConfigFile(filePath = PRE_COMMIT_CONFIG_PATH) {
    const content = fs.readFileSync(filePath, "utf8");
    return validateConfigContent(content);
}

function main() {
    const violations = validateConfigFile(PRE_COMMIT_CONFIG_PATH);

    if (violations.length === 0) {
        console.log("✅ Pre-commit Node tooling validation passed.");
        process.exit(0);
    }

    console.error(`❌ Found ${violations.length} pre-commit tooling violation(s):`);
    for (const violation of violations) {
        console.error(`\n- ${violation.toString()}`);
    }

    process.exit(1);
}

module.exports = {
    PRE_COMMIT_CONFIG_PATH,
    Violation,
    getIndent,
    parseHookEntries,
    parseHookIds,
    tokenizeCommand,
    hasNpxInstallPolicy,
    usesManagedJestWrapper,
    isJestRelatedHook,
    hasManagedJestInvocation,
    validateHookEntries,
    validateYamllintPolicy,
    validateConfigContent,
    validateConfigFile,
};

if (require.main === module) {
    main();
}
