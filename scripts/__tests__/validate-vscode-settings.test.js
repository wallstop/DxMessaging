/**
 * @fileoverview Tests for validate-vscode-settings.js security checks.
 */

"use strict";

const fs = require("fs");
const os = require("os");
const path = require("path");
const {
    validateSettingsContent,
    validateVscodeSettings,
    toLineNumber,
} = require("../validate-vscode-settings.js");

describe("validate-vscode-settings", () => {
    test("toLineNumber handles first line and multiline indexes", () => {
        const content = ["line1", "line2", "line3"].join("\n");

        expect(toLineNumber(content, 0)).toBe(1);
        expect(toLineNumber(content, content.indexOf("line2"))).toBe(2);
        expect(toLineNumber(content, content.indexOf("line3"))).toBe(3);
    });

    test("validateSettingsContent accepts settings without auto-approval keys", () => {
        const content = JSON.stringify(
            {
                "editor.formatOnSave": true,
                "files.eol": "\n",
            },
            null,
            2
        );

        const violations = validateSettingsContent(content);

        expect(violations).toHaveLength(0);
    });

    test("validateSettingsContent rejects terminal auto-approval key", () => {
        const content = JSON.stringify(
            {
                "chat.tools.terminal.autoApprove": {
                    "pre-commit": true,
                },
            },
            null,
            2
        );

        const violations = validateSettingsContent(content);

        expect(violations).toHaveLength(1);
        expect(violations[0].line).toBe(2);
        expect(violations[0].message).toContain("must not define");
    });

    test("validateVscodeSettings skips missing file", () => {
        const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), "vscode-settings-"));
        try {
            const fakePath = path.join(tempDir, "settings.json");
            const result = validateVscodeSettings(fakePath);

            expect(result.fileExists).toBe(false);
            expect(result.violations).toHaveLength(0);
        } finally {
            fs.rmSync(tempDir, { recursive: true, force: true });
        }
    });

    test("validateVscodeSettings reports violation for forbidden setting", () => {
        const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), "vscode-settings-"));
        try {
            const settingsPath = path.join(tempDir, "settings.json");
            const content = [
                "{",
                '  "chat.tools.terminal.autoApprove": {',
                '    "pre-commit": true',
                "  }",
                "}",
            ].join("\n");

            fs.writeFileSync(settingsPath, content, "utf8");
            const result = validateVscodeSettings(settingsPath);

            expect(result.fileExists).toBe(true);
            expect(result.violations).toHaveLength(1);
            expect(result.violations[0].line).toBe(2);
        } finally {
            fs.rmSync(tempDir, { recursive: true, force: true });
        }
    });
});
