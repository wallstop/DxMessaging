/**
 * @fileoverview Unit tests for scripts/lib/source-stripping.js.
 *
 * Pins behavior used by the `--testRunner` injection policy guards. If these
 * tests fail, the static-analysis tests that depend on this helper will
 * produce false positives or false negatives.
 *
 * Covers in particular the bypass-class fixed in the state-machine tokenizer
 * rewrite: a block-comment regex that matched across string literals could
 * be defeated by source like `const a = "/" + "*"; ...; const b = "*" + "/";`
 * (with the tokens spelled as actual `/*` and `*\/` inside strings). The
 * tokenizer recognizes such tokens as STRING CONTENT and preserves the code
 * between the strings.
 */

"use strict";

const fs = require("fs");
const path = require("path");

const { stripJsCommentsAndStrings, extractCommentsOnly } = require("../source-stripping");

const REPO_ROOT = path.resolve(__dirname, "..", "..", "..");
const RUN_MANAGED_JEST_PATH = path.join(REPO_ROOT, "scripts", "run-managed-jest.js");

describe("stripJsCommentsAndStrings", () => {
  test("empty input returns empty output", () => {
    expect(stripJsCommentsAndStrings("")).toBe("");
  });

  test("non-string input returns empty string", () => {
    expect(stripJsCommentsAndStrings(undefined)).toBe("");
    expect(stripJsCommentsAndStrings(null)).toBe("");
    expect(stripJsCommentsAndStrings(42)).toBe("");
    expect(stripJsCommentsAndStrings(Buffer.from("x"))).toBe("");
  });

  test("all-whitespace input returns the same whitespace", () => {
    expect(stripJsCommentsAndStrings("   \n\t\n")).toBe("   \n\t\n");
  });

  test("block comments are removed", () => {
    const source = "var a = 1; /* keep --testRunner out */ var b = 2;";
    const stripped = stripJsCommentsAndStrings(source);
    expect(stripped).not.toContain("--testRunner");
    expect(stripped).toContain("var a = 1;");
    expect(stripped).toContain("var b = 2;");
  });

  test("multi-line block comments are removed but preserve newlines", () => {
    const source = [
      "var a = 1;",
      "/*",
      " * --testRunner banner explaining the regression",
      " */",
      "var b = 2;"
    ].join("\n");
    const stripped = stripJsCommentsAndStrings(source);
    expect(stripped).not.toContain("--testRunner");
    // Line count is preserved so error messages keep their line numbers.
    expect(stripped.split("\n").length).toBe(source.split("\n").length);
  });

  test("line comments are removed", () => {
    const source = "var a = 1; // forbid --testRunner here\nvar b = 2;";
    const stripped = stripJsCommentsAndStrings(source);
    expect(stripped).not.toContain("--testRunner");
    expect(stripped).toContain("var a = 1;");
    expect(stripped).toContain("var b = 2;");
  });

  test("single-quoted string literals are reduced to empty quotes", () => {
    const source = "var a = 'hello --testRunner world';";
    const stripped = stripJsCommentsAndStrings(source);
    expect(stripped).not.toContain("--testRunner");
    expect(stripped).toContain("''");
  });

  test("double-quoted string literals are reduced to empty quotes", () => {
    const source = 'var a = "hello --testRunner world";';
    const stripped = stripJsCommentsAndStrings(source);
    expect(stripped).not.toContain("--testRunner");
    expect(stripped).toContain('""');
  });

  test("backtick template strings preserve backticks and erase literal portions", () => {
    const source = "var a = `hello --testRunner world`;";
    const stripped = stripJsCommentsAndStrings(source);
    expect(stripped).not.toContain("--testRunner");
    expect(stripped).toContain("``");
  });

  test("escaped quotes inside strings are handled", () => {
    const singleQuoted = "var a = 'it\\'s --testRunner';";
    const doubleQuoted = 'var a = "he said \\"--testRunner\\"";';
    const backtickQuoted = "var a = `he said \\`--testRunner\\``;";

    expect(stripJsCommentsAndStrings(singleQuoted)).not.toContain("--testRunner");
    expect(stripJsCommentsAndStrings(doubleQuoted)).not.toContain("--testRunner");
    expect(stripJsCommentsAndStrings(backtickQuoted)).not.toContain("--testRunner");
  });

  test("URL-like substrings (http://) inside strings survive stripping pipeline", () => {
    const source = 'var a = "https://example.com/path";\nvar b = 2;';
    const stripped = stripJsCommentsAndStrings(source);
    expect(stripped).toContain("var a =");
    expect(stripped).toContain("var b = 2;");
    // String payload is removed; no URL leakage either way.
    expect(stripped).not.toContain("example.com");
  });

  test('identifier ending in `https` followed by `+ "://..."` is not truncated', () => {
    // The regex pipeline used a `[^:\\]` lookbehind hack to avoid eating
    // `://`. The tokenizer does not need that hack because it tokenizes
    // each construct in order; `//` only triggers `lineComment` when the
    // characters appear in `code` state.
    const source = 'var a = https + "://example.com";';
    const stripped = stripJsCommentsAndStrings(source);
    expect(stripped).toContain("https +");
  });

  test("mixed real-world source from run-managed-jest.js is processed without throwing", () => {
    const source = fs.readFileSync(RUN_MANAGED_JEST_PATH, "utf8");
    const stripped = stripJsCommentsAndStrings(source);

    expect(typeof stripped).toBe("string");
    expect(stripped.length).toBeGreaterThan(0);

    // The wrapper documents `--testRunner` in comments and forwards it
    // via caller args — both are in comments or string literals. After
    // stripping, the literal must NOT appear in remaining code.
    expect(stripped).not.toContain("--testRunner");
  });

  test("multiple constructs on a single line are all stripped", () => {
    const source = 'var a = "--testRunner"; /* --testRunner */ // --testRunner';
    const stripped = stripJsCommentsAndStrings(source);
    expect(stripped).not.toContain("--testRunner");
  });

  // ---- Bypass-class regression tests ----------------------------------

  test("PoC: strings containing /* and */ do not cause code between them to be erased", () => {
    // This is the C1 bypass that the regex-pipeline form was vulnerable to.
    // The old `/\/\*[\s\S]*?\*\//g` regex would match from the `/*` inside
    // the first string to the `*/` inside the last string, eating the
    // entire function body.
    const source = [
      'const _opener = "/*";',
      "function inject(args) {",
      '    args.push("--testRunner", "/tmp/x.js");',
      "}",
      'const _closer = "*/";'
    ].join("\n");
    const stripped = stripJsCommentsAndStrings(source);

    // Real code between the strings MUST be preserved.
    expect(stripped).toContain("function inject(args)");
    expect(stripped).toContain("args.push(");

    // String contents are erased; the literal `--testRunner` (which was
    // inside a string) does not survive.
    expect(stripped).not.toContain("--testRunner");
    expect(stripped).not.toContain("/tmp/x.js");
  });

  test("strings containing // do not cause subsequent code to be treated as line comment", () => {
    const source = ['const url = "http://example.com";', "var a = 1;"].join("\n");
    const stripped = stripJsCommentsAndStrings(source);
    expect(stripped).toContain("var a = 1;");
  });

  test('block comments containing "foo" do not cause subsequent strings to be mis-parsed', () => {
    const source = [
      '/* a block comment with "foo" inside */',
      'var a = "--testRunner";',
      "var b = 2;"
    ].join("\n");
    const stripped = stripJsCommentsAndStrings(source);
    expect(stripped).not.toContain("--testRunner");
    expect(stripped).not.toContain("foo");
    expect(stripped).toContain("var a =");
    expect(stripped).toContain("var b = 2;");
  });

  test("template literal ${} expressions are processed as code; inner strings are blanked", () => {
    // `foo ${"bar"} baz` — the literal portions and `"bar"` payload are
    // erased, but the `${` and `}` delimiters survive so static analysis
    // can recognize the expression boundary.
    const source = 'var a = `foo ${"bar"} baz ${"--testRunner"}`;';
    const stripped = stripJsCommentsAndStrings(source);
    expect(stripped).not.toContain("--testRunner");
    expect(stripped).not.toContain("bar");
    expect(stripped).not.toContain("foo");
    // Expression delimiters preserved.
    expect(stripped).toContain("${");
    expect(stripped).toContain("}");
  });

  test("template literal with nested braces in ${} expression tracks brace depth", () => {
    // `${ { a: "bar" } }` — the inner object braces should NOT prematurely
    // pop the templateExpr state.
    const source = 'var a = `pre ${ { a: "--testRunner" } } post`;';
    const stripped = stripJsCommentsAndStrings(source);
    expect(stripped).not.toContain("--testRunner");
    expect(stripped).toContain("${");
    // The object braces survive (they are code, not string).
    expect(stripped).toMatch(/\{\s*a\s*:\s*""\s*\}/);
  });

  test("nested template literal inside ${} expression is handled", () => {
    // `outer ${ `inner ${"x"}` } end` — every layer must be tokenized.
    const source = 'var a = `outer ${ `inner ${"--testRunner"}` } end`;';
    const stripped = stripJsCommentsAndStrings(source);
    expect(stripped).not.toContain("--testRunner");
    expect(stripped).not.toContain("outer");
    expect(stripped).not.toContain("inner");
    expect(stripped).not.toContain("end");
  });

  test("comment inside ${} expression is stripped", () => {
    const source = "var a = `pre ${ /* --testRunner */ x } post`;";
    const stripped = stripJsCommentsAndStrings(source);
    expect(stripped).not.toContain("--testRunner");
    expect(stripped).toContain("x");
  });

  test("unterminated block comment does not throw and consumes rest of input", () => {
    const source = "var a = 1; /* unterminated\nvar b = 2;";
    // Behavior: enter blockComment at `/*` and never exit. All subsequent
    // characters are erased (line breaks are preserved). The function
    // must return cleanly.
    const stripped = stripJsCommentsAndStrings(source);
    expect(typeof stripped).toBe("string");
    expect(stripped).toContain("var a = 1;");
    expect(stripped).not.toContain("--testRunner");
  });

  test("unterminated string does not throw and consumes rest of input", () => {
    const source = 'var a = "unterminated\nvar b = 2;';
    const stripped = stripJsCommentsAndStrings(source);
    expect(typeof stripped).toBe("string");
    expect(stripped).toContain("var a =");
  });

  test("line count is preserved across stripping", () => {
    const source = [
      "var a = 1;",
      "/* multi",
      "   line",
      "   block */",
      'var b = "multi\\n',
      '   line";',
      "// trailing",
      "var c = 3;"
    ].join("\n");
    const stripped = stripJsCommentsAndStrings(source);
    expect(stripped.split("\n").length).toBe(source.split("\n").length);
  });
});

describe("extractCommentsOnly (inverse projection)", () => {
  const TOKEN = "@needle";
  const inComment = (src) => extractCommentsOnly(src).includes(TOKEN);

  test("non-string / empty input returns empty string", () => {
    expect(extractCommentsOnly(undefined)).toBe("");
    expect(extractCommentsOnly(null)).toBe("");
    expect(extractCommentsOnly(42)).toBe("");
    expect(extractCommentsOnly(Buffer.from("x"))).toBe("");
    expect(extractCommentsOnly("")).toBe("");
  });

  test("keeps line-comment payloads, blanks code", () => {
    const out = extractCommentsOnly("const x = 1; // " + TOKEN + "\n");
    expect(out).toContain(TOKEN);
    expect(out).not.toContain("const");
  });

  test("keeps block-comment payload on a plain continuation line (no leading star)", () => {
    expect(inComment("/*\n   " + TOKEN + "\n*/\n")).toBe(true);
  });

  test("keeps indented block-comment text", () => {
    expect(inComment("/*\n      text " + TOKEN + " here\n*/\n")).toBe(true);
  });

  test("keeps JSDoc star-prefixed body payload", () => {
    expect(inComment("/**\n * " + TOKEN + "\n */\n")).toBe(true);
  });

  test("blanks single/double/template string payloads (token does NOT survive)", () => {
    expect(inComment('const a = "' + TOKEN + '";\n')).toBe(false);
    expect(inComment("const a = '" + TOKEN + "';\n")).toBe(false);
    expect(inComment("const a = `" + TOKEN + "`;\n")).toBe(false);
  });

  test("a `//` or `/*` INSIDE a string does not open a comment", () => {
    expect(inComment('const a = "x // y ' + TOKEN + '";\n')).toBe(false);
    expect(inComment('const a = "x /* y ' + TOKEN + '";\n')).toBe(false);
  });

  test("bare code identifier does not survive", () => {
    expect(inComment("const " + TOKEN.slice(1) + " = 1;\n")).toBe(false);
  });

  test("line breaks are preserved so line numbers stay aligned", () => {
    const source = ["var a = 1;", "/* block", "   " + TOKEN, "   */", "var b = 2;"].join("\n");
    const out = extractCommentsOnly(source);
    expect(out.split("\n").length).toBe(source.split("\n").length);
    expect(out).toContain(TOKEN);
  });

  test("comment inside a template ${} expression is preserved; surrounding template is blanked", () => {
    const out = extractCommentsOnly("var a = `pre ${ /* " + TOKEN + " */ x } post`;\n");
    expect(out).toContain(TOKEN);
    expect(out).not.toContain("pre");
    expect(out).not.toContain("post");
  });

  test("is the exact complement of strip mode for which-region-won a token", () => {
    // A token in a comment survives extractCommentsOnly and is gone from strip.
    const commented = "// " + TOKEN + "\n";
    expect(extractCommentsOnly(commented)).toContain(TOKEN);
    expect(stripJsCommentsAndStrings(commented)).not.toContain(TOKEN);
    // A token in code survives strip and is gone from extractCommentsOnly.
    const coded = "const a = " + TOKEN.slice(1) + ";\n";
    expect(stripJsCommentsAndStrings(coded)).toContain(TOKEN.slice(1));
    expect(extractCommentsOnly(coded)).not.toContain(TOKEN.slice(1));
  });
});
