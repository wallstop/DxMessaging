/**
 * @fileoverview Normalize PowerShell-rendered console output so that phrase /
 * substring assertions are STABLE across host console widths and color modes.
 *
 * THE ROOT-CAUSE THIS SOLVES (a real, width-dependent CI flake):
 *   When `pwsh -File <script>` lets an unhandled terminating error (`throw`)
 *   reach the top of the script, PowerShell's default "ConciseView" error
 *   formatter renders it as a multi-line block: a source-context excerpt, a
 *   `Line |` header, a caret (`~~~`) underline, and the error MESSAGE itself --
 *   and it WORD-WRAPS that message at the host console width, inserting a
 *   continuation gutter (`\n     | `) between words, plus ANSI color escapes
 *   around every fragment. On a wide terminal the message stays on one line; on
 *   a narrower one (the Windows CI runner's effective width, ~105-120 cols) a
 *   phrase like "outside the managed root" is split as
 *   "...outside the\n     | managed root.", so a literal substring assertion for
 *   "outside the managed root" finds nothing -- on Windows only. The PRODUCTION
 *   message is correct; the assertion is what is fragile.
 *
 *   `Write-Host` output, by contrast, is NOT subject to this wrap, which is why
 *   `::error::` / `::warning::` annotations are a stable assertion target.
 *
 * THE FIX:
 *   Run pwsh stdout/stderr through `normalizePwshText` BEFORE a phrase / error
 *   substring assertion. It strips ANSI/OSC escapes, normalizes line endings,
 *   rejoins the ConciseView continuation gutter, and collapses whitespace runs,
 *   recovering the logical message text regardless of the width it was rendered
 *   at. It is PURE and IDEMPOTENT: on already-clean text (plain `Write-Host`
 *   stdout, or text that was never wrapped) it only collapses surrounding
 *   whitespace, so it is safe to apply uniformly to any value used for a phrase
 *   assertion. Do NOT use it where line STRUCTURE matters (e.g. taking the last
 *   line of stdout to read a resolved path) -- it intentionally flattens
 *   newlines; keep the raw stream for those reads.
 *
 * SCOPE / WHAT IT DOES NOT DO:
 *   This recovers WORD-WRAPPED text (the documented ConciseView failure). It does
 *   not attempt to reconstruct a phrase that PowerShell TRUNCATED with a trailing
 *   ellipsis on the source-context excerpt line (the `4 | throw "..." ...` line);
 *   that excerpt is a rendering of the SOURCE, not the message, and the full
 *   message is always emitted (wrapped) on its own gutter lines below, which IS
 *   recovered. Assert against the message, not the source excerpt.
 *
 *   Two consequences of the flatten-everything-to-one-line approach, both of which
 *   mean a `.toContain` here proves only that words appear in the stream, NOT that
 *   they were emitted as one contiguous phrase:
 *     (a) Collapsing newlines GLUES the end of one logical line to the start of the
 *         next, so a substring can span text that was never rendered contiguously
 *         (e.g. the tail of an error line + a `Write-Host` line that followed it can
 *         read as a single phrase). Because of this, the MOST robust assertion
 *         target is the wrap-immune, single-line `::error::` / `::warning::`
 *         annotation (Write-Host is not word-wrapped) -- assert the substring is in
 *         THAT line, not merely somewhere in the whitespace-collapsed blob.
 *     (b) The gutter rejoin merges ANY line whose first non-space character is `|`
 *         into the previous line -- it cannot tell the ConciseView continuation
 *         gutter apart from a LEGITIMATE leading-`|` value. A Markdown table row, a
 *         `Format-Table` rule, or any emitted value that starts with `|` would be
 *         folded into the line above. The normalizer is scoped to ERROR-MESSAGE
 *         recovery; do not run it over output where a leading `|` is real data.
 */

"use strict";

// Control Sequence Introducer / SGR color escapes: ESC [ <params> <intermediate>
// <final>. Covers the color runs PowerShell wraps every fragment in, cursor
// moves, etc. Parameter bytes 0x30-0x3F, intermediate 0x20-0x2F, final 0x40-0x7E.
const CSI_ESCAPE = /\x1B\[[0-9;?]*[ -/]*[@-~]/g;

// Operating System Command sequences: ESC ] ... terminated by BEL (0x07) or the
// String Terminator ESC \. Some terminals/title writers emit these; strip them so
// they never leak into an assertion. The body excludes BEL and ESC so the match
// cannot run past its terminator.
const OSC_ESCAPE = /\x1B\][^\x07\x1B]*(?:\x07|\x1B\\)/g;

// Lone two-character (Fe) escapes that are neither CSI nor OSC (e.g. ESC c reset).
// Stripped after CSI/OSC so those longer, more specific forms match first.
const LONE_FE_ESCAPE = /\x1B[@-Z\\-_]/g;

// The ConciseView continuation gutter that PowerShell inserts when it word-wraps
// the error message: a newline, run of spaces/tabs, a single `|`, then an
// optional single separating space. Rejoining with a single space reconstitutes
// the wrapped words ("outside the\n     | managed" -> "outside the managed").
const CONTINUATION_GUTTER = /\n[ \t]*\|[ \t]?/g;

// Any run of whitespace (including the newlines that survive gutter rejoin)
// collapses to a single space so a multi-line rendering compares equal to its
// single-line logical form.
const WHITESPACE_RUN = /\s+/g;

/**
 * Normalize PowerShell-rendered console text for stable phrase/substring
 * assertions. Strips ANSI/OSC escape sequences, normalizes CRLF/CR to LF,
 * rejoins the ConciseView word-wrap continuation gutter, collapses whitespace
 * runs to single spaces, and trims. Pure and idempotent.
 *
 * @param {unknown} value - Raw pwsh stdout/stderr (or any value; coerced to string).
 * @returns {string} The normalized, wrap-immune text.
 */
function normalizePwshText(value) {
  return String(value)
    .replace(OSC_ESCAPE, "")
    .replace(CSI_ESCAPE, "")
    .replace(LONE_FE_ESCAPE, "")
    .replace(/\r\n?/g, "\n")
    .replace(CONTINUATION_GUTTER, " ")
    .replace(WHITESPACE_RUN, " ")
    .trim();
}

module.exports = { normalizePwshText };
