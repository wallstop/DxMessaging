/**
 * @fileoverview Unit tests for normalizePwshText (scripts/lib/pwsh-output.js).
 *
 * These pin the behavior the CI flake depends on: an unhandled `throw` rendered
 * by PowerShell's ConciseView formatter word-wraps the error MESSAGE at the host
 * console width, splitting an asserted phrase across a `\n     | ` continuation
 * gutter and surrounding every fragment with ANSI color escapes. The normalizer
 * must recover the logical message regardless of the width it was rendered at,
 * while being a harmless no-op (modulo whitespace trimming) on already-clean text.
 *
 * The CI_WRAPPED_THROW fixture below is the REAL captured output of a width-110
 * PTY run of a script that throws the Confirm-UnityCliManagedInstallRoot guard
 * message (the production path that flaked). It is reproduced byte-for-byte
 * (ANSI escapes via \x1B, CRLF as \r\n) so the test exercises the exact bytes,
 * not an idealized approximation.
 */

"use strict";

const { normalizePwshText } = require("../pwsh-output");

// === REAL width-110 ConciseView wrap of the managed-root guard throw ===
// Captured via `python3 pty_run.py 110 throw-script.ps1`. The asserted phrase
// "outside the managed root" is split: "...outside the" ends one gutter line and
// "managed root." begins the next. Color escapes (\x1B[31;1m etc.) wrap every
// fragment; lines are CRLF-terminated. … is the ConciseView source-excerpt
// truncation ellipsis. The fixture intentionally contains the ANSI-glued token
// `mthrow` (the SGR terminator `m` immediately before `throw`) and the truncated
// `becaus` from the real capture; both are byte-faithful fixture noise, not words.
// cspell:ignore mthrow becaus
const ESC = "\x1B";
const CI_WRAPPED_THROW =
  `${ESC}[31;1mException: ${ESC}[0m/tmp/repro.ps1:4${ESC}[0m\r\n` +
  `${ESC}[31;1m${ESC}[0m${ESC}[36;1mLine |${ESC}[0m\r\n` +
  `${ESC}[31;1m${ESC}[0m${ESC}[36;1m${ESC}[36;1m   4 | ${ESC}[0m     ` +
  `${ESC}[36;1mthrow "CI-managed Unity provisioning cannot mutate editors becaus${ESC}[0m ` +
  `…${ESC}[0m\r\n` +
  `${ESC}[31;1m${ESC}[0m${ESC}[36;1m${ESC}[36;1m${ESC}[0m${ESC}[36;1m${ESC}[0m` +
  `${ESC}[36;1m     | ${ESC}[31;1m     ` +
  "~".repeat(61) +
  `${ESC}[0m\r\n` +
  `${ESC}[31;1m${ESC}[0m${ESC}[36;1m${ESC}[36;1m${ESC}[0m${ESC}[36;1m${ESC}[0m` +
  `${ESC}[36;1m${ESC}[31;1m${ESC}[31;1m${ESC}[36;1m     | ${ESC}[31;1m` +
  "CI-managed Unity provisioning cannot mutate editors because the Unity CLI install root is outside the" +
  `${ESC}[0m\r\n` +
  `${ESC}[31;1m${ESC}[0m${ESC}[36;1m${ESC}[36;1m${ESC}[0m${ESC}[36;1m${ESC}[0m` +
  `${ESC}[36;1m${ESC}[31;1m${ESC}[31;1m${ESC}[36;1m${ESC}[31;1m${ESC}[36;1m     | ${ESC}[31;1m` +
  "managed root. CLI root: 'C:\\external\\root'. Managed root: 'C:\\configured\\root'." +
  `${ESC}[0m\r\n`;

describe("normalizePwshText", () => {
  // --- The CI-wrapped fixture recovers the split phrases and embedded paths. ---
  describe("recovers a ConciseView word-wrapped throw message", () => {
    const normalized = normalizePwshText(CI_WRAPPED_THROW);

    test("the RAW fixture does NOT contain the split phrase (the bug it models)", () => {
      // Sanity: prove the fixture genuinely splits the phrase, so a passing
      // normalized assertion is meaningful and not a fixture that never wrapped.
      expect(CI_WRAPPED_THROW).not.toContain("outside the managed root");
    });

    test.each([
      ["the asserted phrase", "outside the managed root"],
      ["the guard reason", "cannot mutate editors"],
      ["the full leading sentence", "the Unity CLI install root is outside the managed root"],
      ["the external CLI root path", "C:\\external\\root"],
      ["the managed root path", "C:\\configured\\root"]
    ])("normalized text contains %s", (_label, phrase) => {
      expect(normalized).toContain(phrase);
    });

    test("strips every ANSI escape (no ESC byte survives)", () => {
      expect(normalized).not.toContain(ESC);
    });

    test("rejoins the continuation gutter (no '     | ' remains)", () => {
      expect(normalized).not.toMatch(/\n[ \t]*\|/);
      expect(normalized).not.toContain(" | managed root");
    });
  });

  // --- Focused, data-driven transformation cases. ---
  describe("individual transformations", () => {
    test.each([
      ["strips a CSI/SGR color run", "\x1B[31;1mError:\x1B[0m boom", "Error: boom"],
      [
        "strips an OSC title sequence (BEL terminated)",
        "\x1B]0;window title\x07payload here",
        "payload here"
      ],
      [
        "strips an OSC sequence (ST / ESC-backslash terminated)",
        "\x1B]8;;https://example.com\x1B\\link text",
        "link text"
      ],
      [
        "normalizes CRLF to a single space after collapse",
        "line one\r\nline two",
        "line one line two"
      ],
      ["normalizes a lone CR", "line one\rline two", "line one line two"],
      [
        "rejoins a single wrap gutter",
        "outside the\n     | managed root.",
        "outside the managed root."
      ],
      ["rejoins a gutter with a tab and no trailing space", "alpha\n\t|beta", "alpha beta"],
      ["collapses interior whitespace runs", "a   b\t\tc", "a b c"],
      ["trims leading and trailing whitespace", "   padded   ", "padded"]
    ])("%s", (_label, input, expected) => {
      expect(normalizePwshText(input)).toBe(expected);
    });
  });

  // --- Idempotence and harmlessness on clean input. ---
  describe("idempotence and clean-input safety", () => {
    test("is idempotent (normalizing twice equals normalizing once) on the wrapped fixture", () => {
      const once = normalizePwshText(CI_WRAPPED_THROW);
      expect(normalizePwshText(once)).toBe(once);
    });

    test("is a no-op on already-clean single-line stdout", () => {
      const clean = "::error::CI-managed Unity provisioning cannot mutate editors";
      expect(normalizePwshText(clean)).toBe(clean);
    });

    test("preserves a Write-Host annotation phrase verbatim (wrap-immune target)", () => {
      const annotation =
        "::error::the Unity CLI install root is outside the managed root. CLI root: 'C:\\external\\root'.";
      expect(normalizePwshText(annotation)).toContain("outside the managed root");
      expect(normalizePwshText(annotation)).toBe(annotation);
    });

    test("coerces a non-string input without throwing", () => {
      expect(normalizePwshText(undefined)).toBe("undefined");
      expect(normalizePwshText(null)).toBe("null");
      expect(normalizePwshText(42)).toBe("42");
    });

    test("does not eat a literal pipe that is not a wrap gutter", () => {
      // A `|` mid-line (not preceded by a newline + spaces) is real content.
      expect(normalizePwshText("a | b")).toBe("a | b");
    });
  });
});
