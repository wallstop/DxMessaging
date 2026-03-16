/**
 * @fileoverview Tests for check-eol.js helper behavior.
 */

"use strict";

const {
    splitNormalizedLines,
    hasBom,
    hasNonCrlfEol,
    hasNonLfEol,
} = require("../check-eol.js");

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
});
