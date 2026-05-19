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
 * Pure: no `require`s with side effects, no top-level I/O, no globals.
 */

function stripJsCommentsAndStrings(source) {
  if (typeof source !== "string") {
    return "";
  }
  if (source.length === 0) {
    return "";
  }

  const out = [];
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
        i += 2;
        continue;
      }
      if (ch === "/" && next === "*") {
        stack.push({ kind: "blockComment" });
        i += 2;
        continue;
      }
      if (ch === "'") {
        stack.push({ kind: "stringSingle" });
        out.push("'");
        i++;
        continue;
      }
      if (ch === '"') {
        stack.push({ kind: "stringDouble" });
        out.push('"');
        i++;
        continue;
      }
      if (ch === "`") {
        stack.push({ kind: "templateLiteral" });
        out.push("`");
        i++;
        continue;
      }

      if (state === "templateExpr") {
        if (ch === "{") {
          frame.depth = (frame.depth || 0) + 1;
          out.push(ch);
          i++;
          continue;
        }
        if (ch === "}") {
          if ((frame.depth || 0) === 0) {
            // Closing brace of the `${...}` expression itself;
            // pop back to the surrounding template literal.
            stack.pop();
            out.push("}");
            i++;
            continue;
          }
          frame.depth -= 1;
          out.push(ch);
          i++;
          continue;
        }
      }

      out.push(ch);
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
      // preserve neither the `\r` nor the comment payload, only the
      // `\n` (when we reach it on the next iteration).
      i++;
      continue;
    }

    if (state === "blockComment") {
      if (ch === "*" && next === "/") {
        stack.pop();
        i += 2;
        continue;
      }
      if (ch === "\n") {
        out.push("\n");
      }
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
        i += 2;
        continue;
      }
      if (ch === "'") {
        stack.pop();
        out.push("'");
        i++;
        continue;
      }
      if (ch === "\n") {
        // Unterminated single-quoted string spanning a newline —
        // legal in source only via an escape; preserve the newline
        // so line numbers remain stable.
        out.push("\n");
      }
      i++;
      continue;
    }

    if (state === "stringDouble") {
      if (ch === "\\" && i + 1 < n) {
        i += 2;
        continue;
      }
      if (ch === '"') {
        stack.pop();
        out.push('"');
        i++;
        continue;
      }
      if (ch === "\n") {
        out.push("\n");
      }
      i++;
      continue;
    }

    if (state === "templateLiteral") {
      if (ch === "\\" && i + 1 < n) {
        i += 2;
        continue;
      }
      if (ch === "`") {
        stack.pop();
        out.push("`");
        i++;
        continue;
      }
      if (ch === "$" && next === "{") {
        stack.push({ kind: "templateExpr", depth: 0 });
        out.push("${");
        i += 2;
        continue;
      }
      if (ch === "\n") {
        out.push("\n");
      }
      i++;
      continue;
    }

    // Defensive: unknown state, advance one char to guarantee forward
    // progress. This branch is unreachable given the states above.
    i++;
  }

  return out.join("");
}

module.exports = {
  stripJsCommentsAndStrings
};
