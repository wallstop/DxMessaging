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

        test("matches check-eol.ps1 CRLF extension policy", () => {
            const ps1Path = path.resolve(__dirname, "../check-eol.ps1");
            const content = fs.readFileSync(ps1Path, "utf8");

            const match = content.match(/\$crlfExtensions\s*=\s*@\(([^)]*)\)/m);
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

            const match = content.match(/\$lfExtensions\s*=\s*@\(([^)]*)\)/m);
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

        test("does not overlap CRLF and LF extension sets", () => {
            const overlap = [...crlfExts].filter((ext) => lfExts.has(ext));
            expect(overlap).toEqual([]);
        });
    });
});
