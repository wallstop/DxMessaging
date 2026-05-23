"use strict";

/**
 * @fileoverview Pure source-stripping helper shared by static-analysis tests.
 *
 * Removes comments and replaces string-literal payloads with empty quotes so
 * downstream regex/substring checks operate only on actual JavaScript code.
 * This is used by the `--testRunner` injection policy guards and any other
 * static lint that needs to avoid false positives from docstrings, banners,
 * error messages, or string-table contents.
 *
 * Implementation: this is a single-pass state-machine tokenizer that walks
 * the source one character at a time. The earlier regex-pipeline form had a
 * bypass: a block-comment regex of `/\/\*[\s\S]*?\*\//g` matches greedily
 * across string literals, so source like:
 *
 *     const opener = "/*";
 *     args.push("--testRunner", "/tmp/x.js");
 *     const closer = "* /";   // (real source uses a literal `*` and `/`)
 *
 * would have the entire middle line eaten as a "block comment". The
 * tokenizer below correctly recognizes the `/*` and `*\/` tokens as STRING
 * CONTENT (not comment delimiters) and preserves the surrounding code.
 *
 * States tracked:
 *   - `code`            (default)
 *   - `lineComment`     (after `//`)
 *   - `blockComment`    (after `/*` outside any string)
 *   - `stringSingle`    (inside `'...'`)
 *   - `stringDouble`    (inside `"..."`)
 *   - `templateLiteral` (inside backticks)
 *   - `templateExpr`    (inside `${...}` within a template literal — this
 *                        sub-context contains real code and may recursively
 *                        contain strings, templates, and comments)
 *
 * Output invariants:
 *   - Line breaks are preserved everywhere (including inside comments and
 *     multi-line strings) so line numbers in error snippets stay accurate.
 *   - Comment payloads (both line and block) are erased entirely; only the
 *     line breaks they spanned remain.
 *   - String/template literal payloads are erased; only the surrounding
 *     quote/backtick markers and any line breaks remain. For template
 *     literals, the `${` and `}` delimiters of expressions are preserved
 *     and the expression content is processed as code.
 *
 * What this DOES NOT protect against:
 *   - Runtime indirection. If code stores a forbidden literal in a variable
 *     (e.g. `const flag = "--testRunner"; args.push(flag);`), the tokenizer
 *     correctly erases the string content but the runtime injection still
 *     happens. Source-scanning guards cannot detect this; structural guards
 *     in the policy tests (e.g. "no `*.push()` call site references a known
 *     runner-path identifier") are the defense.
 *   - Regex literals. We do not tokenize `/regex/` patterns. In practice this
 *     is acceptable for the policy tests because regex literals cannot
 *     contain the bare token `--testRunner` (the `-` would be ambiguous in a
 *     character class) AND a regex literal's body is not a comment delimiter.
 *     If a future policy needs regex-literal awareness, add a `regex` state
 *     that triggers when `/` follows an operator/keyword context.
 *
 * Input handling:
 *   - Non-string input (undefined, null, numbers, Buffers, etc.) returns the
 *     empty string. Callers that need to pass a Buffer must `.toString("utf8")`
 *     it first; we deliberately do not auto-decode to keep the helper pure
 *     and to avoid accidentally swallowing binary payloads.
 *
 * Two projections share ONE tokenizer pass (`projectSource`):
 *   - `stripJsCommentsAndStrings(source)` keeps code, blanks comment payloads
 *     entirely and string/template payloads (preserving quote/backtick markers
 *     and `${...}` delimiters). This is the historical behavior; output is
 *     byte-for-byte identical to the prior implementation.
 *   - `extractCommentsOnly(source)` is its INVERSE: it preserves comment
 *     payloads verbatim (line + block + JSDoc, with or without a leading `*`)
 *     and blanks code + string/template payloads to whitespace, preserving line
 *     breaks so offsets/line numbers stay aligned with the source. A token that
 *     survives `extractCommentsOnly` lived in a real comment span -- never in a
 *     string, template, or code -- which is exactly the discrimination a
 *     marker-in-comment guard needs (a string such as `"a // b @marker"` blanks
 *     to whitespace because `//` inside a string is string content, not a
 *     comment opener). Sharing the state machine guarantees the two projections
 *     can never disagree about what is a comment.
 *
 * Pure: no `require`s with side effects, no top-level I/O, no globals.
 */

/**
 * Single-pass tokenizer shared by both projections.
 *
 * @param {string} source - Source text (any LF/CRLF; BOM-agnostic).
 * @param {boolean} commentsOnly - When false (default), behaves as
 *   `stripJsCommentsAndStrings` (keep code, blank comments + string payloads).
 *   When true, behaves as `extractCommentsOnly` (keep comment payloads, blank
 *   code + string payloads). Line breaks are preserved in BOTH modes.
 * @returns {string}
 */
function projectSource(source, commentsOnly) {
  if (typeof source !== "string") {
    return "";
  }
  if (source.length === 0) {
    return "";
  }

  const out = [];
  // In commentsOnly mode, code/string regions are blanked to spaces (so the
  // marker token cannot survive there) while line breaks are preserved to keep
  // offsets and line numbers aligned with the source.
  const emitCode = (ch) => {
    if (commentsOnly) {
      if (ch === "\n") {
        out.push("\n");
      } else {
        out.push(" ");
      }
    } else {
      out.push(ch);
    }
  };
  // Comment payload characters: kept verbatim in commentsOnly mode, erased in
  // strip mode (only the line breaks they spanned are preserved -- handled by
  // each comment state explicitly pushing "\n").
  const emitCommentChar = (ch) => {
    if (commentsOnly) {
      out.push(ch);
    }
  };
  // Erased-in-strip / blanked-in-commentsOnly characters. Covers two cases:
  //   1. String/template payload chars and the escapes they contain -- strip
  //      mode reduces `"abc"` to `""` (emits nothing), commentsOnly blanks them
  //      to spaces so the marker token cannot survive inside a string literal.
  //   2. Comment delimiters (`//`, `/*`, `*/`) -- strip mode drops them (emits
  //      nothing, preserving the historical byte-for-byte output), commentsOnly
  //      blanks them to spaces so offsets stay aligned with the source.
  // The surrounding quote/backtick markers and `${`/`}` are emitted via
  // `emitCode` instead, so they survive in strip mode exactly as before.
  const emitBlank = () => {
    if (commentsOnly) {
      out.push(" ");
    }
  };
  // Stack permits templateExpr nesting (e.g. `a${`b${c}d`}e`). The top of
  // the stack is the active state.
  const stack = [{ kind: "code" }];
  const n = source.length;
  let i = 0;

  while (i < n) {
    const frame = stack[stack.length - 1];
    const state = frame.kind;
    const ch = source[i];
    const next = i + 1 < n ? source[i + 1] : "";

    if (state === "code" || state === "templateExpr") {
      if (ch === "/" && next === "/") {
        stack.push({ kind: "lineComment" });
        // Blank the `//` opener in commentsOnly mode to keep offsets aligned.
        emitBlank();
        emitBlank();
        i += 2;
        continue;
      }
      if (ch === "/" && next === "*") {
        stack.push({ kind: "blockComment" });
        // Blank the `/*` opener in commentsOnly mode to keep offsets aligned.
        emitBlank();
        emitBlank();
        i += 2;
        continue;
      }
      if (ch === "'") {
        stack.push({ kind: "stringSingle" });
        emitCode("'");
        i++;
        continue;
      }
      if (ch === '"') {
        stack.push({ kind: "stringDouble" });
        emitCode('"');
        i++;
        continue;
      }
      if (ch === "`") {
        stack.push({ kind: "templateLiteral" });
        emitCode("`");
        i++;
        continue;
      }

      if (state === "templateExpr") {
        if (ch === "{") {
          frame.depth = (frame.depth || 0) + 1;
          emitCode(ch);
          i++;
          continue;
        }
        if (ch === "}") {
          if ((frame.depth || 0) === 0) {
            // Closing brace of the `${...}` expression itself;
            // pop back to the surrounding template literal.
            stack.pop();
            emitCode("}");
            i++;
            continue;
          }
          frame.depth -= 1;
          emitCode(ch);
          i++;
          continue;
        }
      }

      emitCode(ch);
      i++;
      continue;
    }

    if (state === "lineComment") {
      if (ch === "\n") {
        stack.pop();
        out.push("\n");
        i++;
        continue;
      }
      // Defensive: a `\r` immediately before `\n` is part of CRLF; we
      // preserve neither the `\r` (strip mode) nor a redundant marker. In
      // commentsOnly mode we keep the comment payload verbatim (a stray `\r`
      // is harmless to substring checks); in strip mode it is erased.
      emitCommentChar(ch);
      i++;
      continue;
    }

    if (state === "blockComment") {
      if (ch === "*" && next === "/") {
        stack.pop();
        // Blank the `*/` closer in commentsOnly mode to keep offsets aligned.
        emitBlank();
        emitBlank();
        i += 2;
        continue;
      }
      if (ch === "\n") {
        out.push("\n");
        i++;
        continue;
      }
      emitCommentChar(ch);
      i++;
      continue;
    }

    if (state === "stringSingle") {
      if (ch === "\\" && i + 1 < n) {
        // Skip the escape sequence entirely (both chars). For
        // multi-line escapes like `\<newline>` we still drop the
        // backslash and the newline — the line-count invariant in
        // the file as a whole is unaffected because such constructs
        // also remove a logical line from the source. The dominant
        // case (single-char escapes) is correct.
        emitBlank();
        emitBlank();
        i += 2;
        continue;
      }
      if (ch === "'") {
        stack.pop();
        emitCode("'");
        i++;
        continue;
      }
      if (ch === "\n") {
        // Unterminated single-quoted string spanning a newline —
        // legal in source only via an escape; preserve the newline
        // so line numbers remain stable.
        out.push("\n");
        i++;
        continue;
      }
      emitBlank();
      i++;
      continue;
    }

    if (state === "stringDouble") {
      if (ch === "\\" && i + 1 < n) {
        emitBlank();
        emitBlank();
        i += 2;
        continue;
      }
      if (ch === '"') {
        stack.pop();
        emitCode('"');
        i++;
        continue;
      }
      if (ch === "\n") {
        out.push("\n");
        i++;
        continue;
      }
      emitBlank();
      i++;
      continue;
    }

    if (state === "templateLiteral") {
      if (ch === "\\" && i + 1 < n) {
        emitBlank();
        emitBlank();
        i += 2;
        continue;
      }
      if (ch === "`") {
        stack.pop();
        emitCode("`");
        i++;
        continue;
      }
      if (ch === "$" && next === "{") {
        stack.push({ kind: "templateExpr", depth: 0 });
        emitCode("$");
        emitCode("{");
        i += 2;
        continue;
      }
      if (ch === "\n") {
        out.push("\n");
        i++;
        continue;
      }
      emitBlank();
      i++;
      continue;
    }

    // Defensive: unknown state, advance one char to guarantee forward
    // progress. This branch is unreachable given the states above.
    i++;
  }

  return out.join("");
}

/**
 * Keep code, blank comment payloads and string/template payloads (preserving
 * quote/backtick markers and `${...}` delimiters). See the file header for full
 * semantics. Output is byte-for-byte identical to the historical implementation.
 *
 * @param {string} source
 * @returns {string}
 */
function stripJsCommentsAndStrings(source) {
  return projectSource(source, false);
}

/**
 * INVERSE of `stripJsCommentsAndStrings`: keep comment payloads verbatim and
 * blank everything else (code + string/template payloads) to spaces, preserving
 * line breaks. A substring/token that appears in the result lived in a real
 * comment span (line, block, or JSDoc, with or without a leading `*`) and never
 * in a string, template literal, or code. This is the discriminator a
 * marker-in-comment guard needs without re-implementing a tokenizer.
 *
 * @param {string} source
 * @returns {string}
 */
function extractCommentsOnly(source) {
  return projectSource(source, true);
}

module.exports = {
  stripJsCommentsAndStrings,
  extractCommentsOnly
};
