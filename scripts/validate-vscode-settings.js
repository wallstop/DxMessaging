#!/usr/bin/env node
/**
 * validate-vscode-settings.js
 *
 * Guards against committing VS Code chat terminal auto-approval settings.
 * Repository settings must not auto-approve terminal commands.
 */

"use strict";

const fs = require("fs");
const path = require("path");
const { normalizeToLf } = require("./lib/quote-parser");

const SETTINGS_PATH = path.join(__dirname, "..", ".vscode", "settings.json");
const FORBIDDEN_KEY_PATTERN = /"chat\.tools\.terminal\.[^"]*autoApprove[^"]*"\s*:/i;

class Violation {
    constructor(line, message) {
        this.line = line;
        this.message = message;
    }
}

function toLineNumber(content, index) {
    if (index < 0) {
        return 1;
    }
    const prefix = content.slice(0, index);
    return prefix.split("\n").length;
}

function validateSettingsContent(content) {
    const normalized = normalizeToLf(content);
    const violations = [];

    const keyMatch = FORBIDDEN_KEY_PATTERN.exec(normalized);
    if (keyMatch) {
        violations.push(
            new Violation(
                toLineNumber(normalized, keyMatch.index),
                "Repository settings must not define chat.tools.terminal auto-approval keys."
            )
        );
    }

    return violations;
}

function validateVscodeSettings(settingsPath = SETTINGS_PATH) {
    if (!fs.existsSync(settingsPath)) {
        return { fileExists: false, violations: [] };
    }

    const content = fs.readFileSync(settingsPath, "utf8");
    return {
        fileExists: true,
        violations: validateSettingsContent(content),
    };
}

function main() {
    const result = validateVscodeSettings();

    if (!result.fileExists) {
        console.log("No .vscode/settings.json found; skipping validation.");
        process.exit(0);
    }

    if (result.violations.length === 0) {
        console.log("✅ .vscode/settings.json passed security validation.");
        process.exit(0);
    }

    console.error("❌ .vscode/settings.json contains forbidden auto-approval settings:");
    for (const violation of result.violations) {
        console.error(`  - Line ${violation.line}: ${violation.message}`);
    }
    process.exit(1);
}

module.exports = {
    SETTINGS_PATH,
    FORBIDDEN_KEY_PATTERN,
    Violation,
    validateSettingsContent,
    validateVscodeSettings,
    toLineNumber,
};

if (require.main === module) {
    main();
}
