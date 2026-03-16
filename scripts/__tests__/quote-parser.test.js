/**
 * @fileoverview Tests for scripts/lib/quote-parser.js.
 */

"use strict";

const {
    hasMatchingBoundaryQuotes,
    stripMatchingBoundaryQuotes,
} = require("../lib/quote-parser");

describe("quote-parser", () => {
    describe("hasMatchingBoundaryQuotes", () => {
        test.each([
            ['"info"', true],
            ["'info'", true],
            ["\"info'", false],
            ["'info\"", false],
            ['"info', false],
            ['info"', false],
            ["info", false],
            ["", false],
            ["  'info'  ", true],
            ["  \"info  ", false],
        ])("returns %s -> %s", (value, expected) => {
            expect(hasMatchingBoundaryQuotes(value)).toBe(expected);
        });
    });

    describe("stripMatchingBoundaryQuotes", () => {
        test("strips matching single quotes", () => {
            expect(stripMatchingBoundaryQuotes("'value'")).toBe("value");
        });

        test("strips matching double quotes", () => {
            expect(stripMatchingBoundaryQuotes('"value"')).toBe("value");
        });

        test("preserves mismatched boundaries", () => {
            expect(stripMatchingBoundaryQuotes('"value\'')).toBe('"value\'');
        });

        test("preserves unclosed opening quote", () => {
            expect(stripMatchingBoundaryQuotes('"value')).toBe('"value');
        });

        test("preserves trailing-only quote", () => {
            expect(stripMatchingBoundaryQuotes('value"')).toBe('value"');
        });

        test("trims surrounding whitespace before evaluation", () => {
            expect(stripMatchingBoundaryQuotes("  'value'  ")).toBe("value");
        });
    });
});
