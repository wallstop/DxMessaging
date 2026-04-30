/**
 * @fileoverview Validates required hook stage and coverage policies in .pre-commit-config.yaml.
 */

"use strict";

const fs = require("fs");
const path = require("path");
const { normalizeToLf } = require("../lib/quote-parser");

function getIndent(line) {
    return line.length - line.trimStart().length;
}

function findHookBlock(lines, hookId) {
    let startIndex = -1;
    let hookIndent = -1;

    for (let i = 0; i < lines.length; i++) {
        const idMatch = /^(\s*)-\s+id:\s*([^\s#]+)\s*$/.exec(lines[i]);
        if (!idMatch || idMatch[2].trim() !== hookId) {
            continue;
        }

        startIndex = i;
        hookIndent = idMatch[1].length;
        break;
    }

    if (startIndex === -1) {
        return null;
    }

    let endIndex = lines.length;
    for (let i = startIndex + 1; i < lines.length; i++) {
        const idMatch = /^(\s*)-\s+id:\s*([^\s#]+)\s*$/.exec(lines[i]);
        if (!idMatch) {
            continue;
        }

        if (idMatch[1].length === hookIndent) {
            endIndex = i;
            break;
        }
    }

    return {
        startLine: startIndex + 1,
        lines: lines.slice(startIndex, endIndex),
    };
}

function extractStagesFromHookBlock(hookBlock) {
    if (!hookBlock) {
        return [];
    }

    const stages = [];

    for (let i = 0; i < hookBlock.lines.length; i++) {
        const stagesMatch = /^(\s*)stages:\s*$/.exec(hookBlock.lines[i]);
        if (!stagesMatch) {
            continue;
        }

        const stagesIndent = stagesMatch[1].length;

        for (let j = i + 1; j < hookBlock.lines.length; j++) {
            const line = hookBlock.lines[j];
            if (!line.trim()) {
                continue;
            }

            const indent = getIndent(line);
            if (indent <= stagesIndent) {
                break;
            }

            const stageMatch = /^\s*-\s*([^\s#]+)\s*$/.exec(line);
            if (stageMatch) {
                stages.push(stageMatch[1].trim());
            }
        }

        break;
    }

    return stages;
}

describe("pre-commit hook stage policy", () => {
    const configPath = path.resolve(__dirname, "../../.pre-commit-config.yaml");
    const configContent = normalizeToLf(fs.readFileSync(configPath, "utf8"));
    const configLines = configContent.split("\n");

    test("cspell hook runs at pre-commit and pre-push", () => {
        const cspellBlock = findHookBlock(configLines, "cspell");
        expect(cspellBlock).not.toBeNull();

        const stages = extractStagesFromHookBlock(cspellBlock);
        expect(stages).toEqual(expect.arrayContaining(["pre-commit", "pre-push"]));
    });

    test("validate-npm-meta hook runs at pre-commit and pre-push", () => {
        const npmMetaBlock = findHookBlock(configLines, "validate-npm-meta");
        expect(npmMetaBlock).not.toBeNull();

        const stages = extractStagesFromHookBlock(npmMetaBlock);
        expect(stages).toEqual(expect.arrayContaining(["pre-commit", "pre-push"]));
    });

    test("fix-csharp-underscore-methods hook runs at pre-commit", () => {
        const fixerBlock = findHookBlock(configLines, "fix-csharp-underscore-methods");
        expect(fixerBlock).not.toBeNull();

        const stages = extractStagesFromHookBlock(fixerBlock);
        expect(stages).toEqual(expect.arrayContaining(["pre-commit"]));

        const blockText = fixerBlock.lines.join("\n");
        expect(blockText).toContain("scripts/fix-csharp-underscore-methods.js");
        expect(blockText).toContain("git add \"$@\"");
        expect(blockText).not.toContain("|| true");
        expect(blockText).not.toContain("|| echo");
    });

    test("script-parser-tests includes npm-meta and shell-safety regressions", () => {
        const parserTestsBlock = findHookBlock(configLines, "script-parser-tests");
        expect(parserTestsBlock).not.toBeNull();

        const blockText = parserTestsBlock.lines.join("\n");
        expect(blockText).toContain("scripts/__tests__/validate-npm-meta.test.js");
        expect(blockText).toContain("scripts/__tests__/run-managed-prettier.test.js");
        expect(blockText).toContain("scripts/__tests__/prettier-version.test.js");
        expect(blockText).toContain("scripts/__tests__/shell-command.test.js");
        expect(blockText).toContain("scripts/__tests__/detect-shell-redirection-antipattern.test.js");
        expect(blockText).toContain("scripts/__tests__/fix-csharp-underscore-methods.test.js");
    });
});
