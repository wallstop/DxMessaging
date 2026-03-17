/**
 * @fileoverview Tests for check-eol.js helper behavior.
 */

"use strict";

const fs = require("fs");
const path = require("path");
const {
    splitNormalizedLines,
    hasBom,
    hasNonCrlfEol,
    hasNonLfEol,
} = require("../check-eol.js");
const { crlfExts, lfExts } = require("../lib/eol-policy.js");

function getLeadingBlockComment(filePath) {
    const content = fs.readFileSync(filePath, "utf8");
    const match = content.match(/\/\*[\s\S]*?\*\//);
    return match ? match[0] : "";
}

describe("check-eol helpers", () => {
    describe("splitNormalizedLines", () => {
        test("normalizes lone CR and CRLF before splitting", () => {
            const lines = splitNormalizedLines("a\r\nb\rc\n");
            expect(lines).toEqual(["a", "b", "c", ""]);
        });

        test("returns single line for content without line endings", () => {
            const lines = splitNormalizedLines("abc");
            expect(lines).toEqual(["abc"]);
        });
    });

    describe("hasBom", () => {
        test("detects UTF-8 BOM", () => {
            const withBom = Buffer.from([0xef, 0xbb, 0xbf, 0x61]);
            expect(hasBom(withBom)).toBe(true);
        });

        test("returns false when BOM is absent", () => {
            const withoutBom = Buffer.from("abc", "utf8");
            expect(hasBom(withoutBom)).toBe(false);
        });
    });

    describe("hasNonCrlfEol", () => {
        test("returns false for CRLF-only content", () => {
            const buf = Buffer.from("a\r\nb\r\n", "utf8");
            expect(hasNonCrlfEol(buf)).toBe(false);
        });

        test("returns true for lone CR", () => {
            const buf = Buffer.from("a\rb\r", "utf8");
            expect(hasNonCrlfEol(buf)).toBe(true);
        });

        test("returns true for LF-only content", () => {
            const buf = Buffer.from("a\nb\n", "utf8");
            expect(hasNonCrlfEol(buf)).toBe(true);
        });
    });

    describe("hasNonLfEol", () => {
        test("returns false for LF-only content", () => {
            const buf = Buffer.from("a\nb\n", "utf8");
            expect(hasNonLfEol(buf)).toBe(false);
        });

        test("returns true for CRLF content", () => {
            const buf = Buffer.from("a\r\nb\r\n", "utf8");
            expect(hasNonLfEol(buf)).toBe(true);
        });

        test("returns true for lone CR content", () => {
            const buf = Buffer.from("a\rb\r", "utf8");
            expect(hasNonLfEol(buf)).toBe(true);
        });
    });

    describe("EOL policy sync", () => {
        test("includes .props in CRLF extension set", () => {
            expect(crlfExts.has(".props")).toBe(true);
        });

        test("matches .gitattributes CRLF extension policy", () => {
            const gitattributesPath = path.resolve(__dirname, "../../.gitattributes");
            const content = fs.readFileSync(gitattributesPath, "utf8");

            const crlfFromGitattributes = new Set(
                content
                    .split(/\r\n|\r|\n/)
                    .map((line) => line.trim())
                    .filter((line) => line && !line.startsWith("#"))
                    .map((line) => line.split(/\s+/))
                    .filter((parts) => parts[0].startsWith("*.") && parts.includes("eol=crlf"))
                    .map((parts) => `.${parts[0].slice(2).toLowerCase()}`)
            );

            expect(new Set(crlfExts)).toEqual(crlfFromGitattributes);
        });

        test("LF extension policy is compatible with .gitattributes", () => {
            const gitattributesPath = path.resolve(__dirname, "../../.gitattributes");
            const content = fs.readFileSync(gitattributesPath, "utf8");
            const lines = content
                .split(/\r\n|\r|\n/)
                .map((line) => line.trim())
                .filter((line) => line && !line.startsWith("#"));

            const hasDefaultLfRule = lines.some(
                (line) => line.startsWith("*") && line.includes("text=auto") && line.includes("eol=lf")
            );
            const explicitLfExts = new Set(
                lines
                    .map((line) => line.split(/\s+/))
                    .filter((parts) => parts[0].startsWith("*.") && parts.includes("eol=lf"))
                    .map((parts) => `.${parts[0].slice(2).toLowerCase()}`)
            );

            for (const ext of lfExts) {
                // LF compatibility is satisfied by either an explicit per-extension rule or the default rule.
                expect(explicitLfExts.has(ext) || hasDefaultLfRule).toBe(true);
            }
        });

        test("LF extension policy includes all explicit .gitattributes LF entries", () => {
            const gitattributesPath = path.resolve(__dirname, "../../.gitattributes");
            const content = fs.readFileSync(gitattributesPath, "utf8");
            const explicitLfExts = new Set(
                content
                    .split(/\r\n|\r|\n/)
                    .map((line) => line.trim())
                    .filter((line) => line && !line.startsWith("#"))
                    .map((line) => line.split(/\s+/))
                    .filter((parts) => parts[0].startsWith("*.") && parts.includes("eol=lf"))
                    .map((parts) => `.${parts[0].slice(2).toLowerCase()}`)
            );

            for (const ext of explicitLfExts) {
                expect(lfExts.has(ext)).toBe(true);
            }
        });

        test("matches check-eol.ps1 CRLF extension policy", () => {
            const ps1Path = path.resolve(__dirname, "../check-eol.ps1");
            const content = fs.readFileSync(ps1Path, "utf8");

            const match = content.match(/\$crlfExtensions\s*=\s*@\(([\s\S]*?)\)/);
            expect(match).not.toBeNull();

            const fromPs1 = new Set(
                match[1]
                    .split(",")
                    .map((item) => item.trim())
                    .filter(Boolean)
                    .map((item) => item.replace(/['"\s]/g, "").toLowerCase())
            );

            expect(new Set(crlfExts)).toEqual(fromPs1);
        });

        test("matches check-eol.ps1 LF extension policy", () => {
            const ps1Path = path.resolve(__dirname, "../check-eol.ps1");
            const content = fs.readFileSync(ps1Path, "utf8");

            const match = content.match(/\$lfExtensions\s*=\s*@\(([\s\S]*?)\)/);
            expect(match).not.toBeNull();

            const fromPs1 = new Set(
                match[1]
                    .split(",")
                    .map((item) => item.trim())
                    .filter(Boolean)
                    .map((item) => item.replace(/['"\s]/g, "").toLowerCase())
            );

            expect(new Set(lfExts)).toEqual(fromPs1);
        });

        test("check-eol.ps1 enforces LF policy for extensionless git hooks", () => {
            const ps1Path = path.resolve(__dirname, "../check-eol.ps1");
            const content = fs.readFileSync(ps1Path, "utf8");

            expect(content).toMatch(/scripts\/hooks\//);
            expect(content).toMatch(/Test-IsGitHookPath/);
            expect(content).toMatch(/\$lfExtensions -contains \$ext -or \$isGitHook/);
        });

        test("does not overlap CRLF and LF extension sets", () => {
            const overlap = [...crlfExts].filter((ext) => lfExts.has(ext));
            expect(overlap).toEqual([]);
        });

        test("documents bidirectional sync references in shared policy files", () => {
            const eolPolicyPath = path.resolve(__dirname, "../lib/eol-policy.js");
            const checkEolPath = path.resolve(__dirname, "../check-eol.js");
            const fixEolPath = path.resolve(__dirname, "../fix-eol.js");
            const checkEolPs1Path = path.resolve(__dirname, "../check-eol.ps1");

            const eolPolicyContent = fs.readFileSync(eolPolicyPath, "utf8");
            expect(eolPolicyContent).toMatch(/scripts\/check-eol\.ps1/);
            expect(eolPolicyContent).toMatch(/scripts\/check-eol\.js/);
            expect(eolPolicyContent).toMatch(/scripts\/fix-eol\.js/);

            const checkEolContent = fs.readFileSync(checkEolPath, "utf8");
            expect(checkEolContent).toMatch(/scripts\/lib\/eol-policy\.js/);
            expect(checkEolContent).toMatch(/scripts\/check-eol\.ps1/);

            const fixEolContent = fs.readFileSync(fixEolPath, "utf8");
            expect(fixEolContent).toMatch(/scripts\/lib\/eol-policy\.js/);
            expect(fixEolContent).toMatch(/scripts\/check-eol\.ps1/);

            const checkEolPs1Content = fs.readFileSync(checkEolPs1Path, "utf8");
            expect(checkEolPs1Content).toMatch(/scripts\/lib\/eol-policy\.js/);
            expect(checkEolPs1Content).toMatch(/scripts\/check-eol\.js/);
            expect(checkEolPs1Content).toMatch(/scripts\/fix-eol\.js/);
        });
    });

    describe("EOL policy documentation", () => {
        test("describes mixed EOL policy in check and fix script headers", () => {
            const checkEolPath = path.resolve(__dirname, "../check-eol.js");
            const fixEolPath = path.resolve(__dirname, "../fix-eol.js");

            const checkEolHeader = getLeadingBlockComment(checkEolPath);
            const fixEolHeader = getLeadingBlockComment(fixEolPath);

            expect(checkEolHeader).toMatch(/mixed line-ending policy/i);
            expect(checkEolHeader).toMatch(/\.cs.*\.csproj.*\.sln.*\.props/i);
            expect(checkEolHeader).toMatch(/all other tracked text files.*LF/i);
            expect(checkEolHeader).not.toMatch(/Enforce CRLF line endings/i);

            expect(fixEolHeader).toMatch(/mixed line-ending policy/i);
            expect(fixEolHeader).toMatch(/converted to CRLF/i);
            expect(fixEolHeader).toMatch(/normalized to LF/i);
            expect(fixEolHeader).not.toMatch(/Fix CRLF line endings/i);
        });

        test("contributing guide states LF default and CRLF .NET exceptions", () => {
            const contributingPath = path.resolve(__dirname, "../../CONTRIBUTING.md");
            const content = fs.readFileSync(contributingPath, "utf8");

            expect(content).toMatch(/most text files to\s+\*\*LF\*\*/i);
            expect(content).toMatch(/C#\/\.NET files/i);
            expect(content).toMatch(/\.cs.*\.csproj.*\.sln.*\.props/i);
            expect(content).not.toMatch(/most text files to CRLF/i);
        });
    });

    describe("script walker safety", () => {
        test("fix-eol walk warns instead of silently swallowing readdirSync errors", () => {
            const fixEolPath = path.resolve(__dirname, "../fix-eol.js");
            const content = fs.readFileSync(fixEolPath, "utf8");

            expect(content).toMatch(/Warning: Unable to read directory/);
            expect(content).not.toMatch(/catch\s*\{\s*return files;\s*\}/);
        });

        test("recursive script scanners guard readdirSync with error handling", () => {
            const scriptPaths = [
                path.resolve(__dirname, "../generate-skills-index.js"),
                path.resolve(__dirname, "../validate-skills.js"),
                path.resolve(__dirname, "../update-llms-txt.js"),
                path.resolve(__dirname, "../validate-workflows.js"),
            ];

            for (const scriptPath of scriptPaths) {
                const content = fs.readFileSync(scriptPath, "utf8");
                expect(content).toMatch(/readdirSync\([\s\S]*?\}\s*catch\s*\(error\)\s*\{/);
                expect(content).toMatch(/Unable to read (directory|workflows directory)/);
            }
        });
    });
});
