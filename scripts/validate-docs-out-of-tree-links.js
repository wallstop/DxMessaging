#!/usr/bin/env node
/**
 * validate-docs-out-of-tree-links.js
 *
 * Scans Markdown files inside docs/ for relative links that climb above the
 * docs/ tree. mkdocs strict mode escalates "warnings" for such links into
 * build failures (it cannot resolve repo files outside docs/ as valid
 * navigation targets). Docs-to-repo references must use the absolute
 * `https://github.com/Ambiguous-Interactive/DxMessaging/blob/master/...` URL
 * so the rendered site links work and the strict build stays green.
 *
 * @usage
 *   node scripts/validate-docs-out-of-tree-links.js [<file>...]
 *
 * With no arguments, scans every Markdown file under docs/. With one or
 * more arguments, only scans those (used by the pre-commit hook entry).
 *
 * @exitcodes
 *   0 - All checked files are clean.
 *   1 - At least one out-of-tree relative link was found.
 */

"use strict";

const fs = require("fs");
const path = require("path");

const REPO_ROOT = path.resolve(__dirname, "..");
const DOCS_ROOT = path.join(REPO_ROOT, "docs");

// CommonMark link syntaxes the validator must recognize:
//   - Inline:                `[text](url "title")`
//   - Full reference:        `[text][ref]` + `[ref]: url "title"`
//   - Collapsed reference:   `[ref][]` + `[ref]: url "title"`
//   - Shortcut reference:    `[ref]` + `[ref]: url "title"`
// mkdocs strict mode treats all four forms identically when it follows the
// link to a destination outside the docs/ tree, so the validator MUST cover
// every form. The reviewer verified empirically that
// `[text][ref]\n[ref]: ../../foo.yml` does trigger the mkdocs failure mode
// that inline-only matching would have missed.
const MARKDOWN_LINK_RE = /\[([^\]]*)\]\(([^)\s]+)(?:\s+"[^"]*")?\)/g;
// Reference definition: optional 1-3 space indent, `[label]:`, then a URL
// (a non-whitespace token, optionally wrapped in `<...>`), then an optional
// title in `"..."`, `'...'`, or `(...)`.
const REFERENCE_DEFINITION_RE =
  /^[ ]{0,3}\[([^\]\n]+)\]:\s*<?([^\s>]+)>?(?:\s+(?:"[^"]*"|'[^']*'|\([^)]*\)))?\s*$/gm;
const FENCED_BLOCK_RE = /```[\s\S]*?```|~~~[\s\S]*?~~~/g;
// Inline backtick code spans on a single line. Conservative: only opens with
// a single backtick, closes at the next backtick on the same line. Markdown
// allows multi-backtick spans (``code with `tick` ``) but those are rare in
// docs/ and detection there is a hard problem; this validator deliberately
// scopes to the common case.
const INLINE_CODE_SPAN_RE = /`[^`\n]+`/g;
// 4-space-indented code block detector. Pragmatic approximation: a line that
// starts with 4+ spaces (or a tab) AND is not a continuation of a list item
// in the previous line. We blank-out the link content but preserve newlines
// so violation line numbers stay accurate. CommonMark's full rules around
// list-item continuations are intricate; this approximation covers the docs/
// authoring patterns the project uses (validator behaviour documented in
// scripts/__tests__/docs-out-of-tree-link-guard.test.js).
const INDENTED_CODE_LINE_RE = /^(?: {4}|\t)/;

function listAllDocsFiles() {
  const out = [];
  walk(DOCS_ROOT, out);
  return out;
}

function walk(dir, out) {
  let entries;
  try {
    entries = fs.readdirSync(dir, { withFileTypes: true });
  } catch {
    return;
  }
  for (const entry of entries) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      walk(full, out);
    } else if (entry.isFile() && entry.name.toLowerCase().endsWith(".md")) {
      out.push(full);
    }
  }
}

function stripFencedBlocks(text) {
  return text.replace(FENCED_BLOCK_RE, (block) => block.replace(/[^\n]/g, " "));
}

/**
 * Replace inline backtick code spans with same-length runs of spaces.
 * Newlines are preserved so subsequent line-number reporting stays accurate.
 * Single-line backticks only; multi-backtick spans (e.g., ``foo `bar` baz``)
 * are out of scope per the comment on INLINE_CODE_SPAN_RE.
 */
function stripInlineCodeSpans(text) {
  return text.replace(INLINE_CODE_SPAN_RE, (span) => span.replace(/[^\n]/g, " "));
}

/**
 * Blank-out 4-space-indented code blocks. A line is considered part of an
 * indented code block when it starts with 4+ spaces (or a tab) AND the
 * preceding non-blank line was either blank or itself an indented code line
 * -- this avoids blanking continuation lines of list items. Heuristic;
 * adequate for docs/ patterns the project uses.
 */
function stripIndentedCodeBlocks(text) {
  const lines = text.split("\n");
  let inBlock = false;
  let prevWasBlank = true;
  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];
    const isIndented = INDENTED_CODE_LINE_RE.test(line);
    const isBlank = line.trim().length === 0;
    if (inBlock) {
      if (isIndented || isBlank) {
        if (isIndented) {
          lines[i] = line.replace(/[^\n]/g, " ");
        }
        // blank lines pass through; we stay in-block.
      } else {
        inBlock = false;
      }
    } else if (isIndented && prevWasBlank) {
      // Enter an indented-code block.
      inBlock = true;
      lines[i] = line.replace(/[^\n]/g, " ");
    }
    prevWasBlank = isBlank;
  }
  return lines.join("\n");
}

function isExternalUrl(url) {
  return /^[a-z][a-z0-9+.-]*:/i.test(url) || url.startsWith("//") || url.startsWith("#");
}

function isMailtoOrAnchor(url) {
  return url.startsWith("mailto:") || url.startsWith("#");
}

function escapesDocsTree(fromFile, linkTarget) {
  // Resolve link target relative to the source file's directory; check
  // whether the resolved absolute path leaves the docs/ tree.
  const fileDir = path.dirname(fromFile);
  const targetPath = linkTarget.split("#")[0].split("?")[0];
  if (!targetPath) {
    return false;
  }
  const resolved = path.resolve(fileDir, targetPath);
  const relative = path.relative(DOCS_ROOT, resolved);
  if (relative.startsWith("..") || path.isAbsolute(relative)) {
    return true;
  }
  return false;
}

function lineNumberOf(text, charIndex) {
  let n = 1;
  for (let i = 0; i < charIndex && i < text.length; i++) {
    if (text[i] === "\n") {
      n++;
    }
  }
  return n;
}

function scanContent(filePath, content) {
  const violations = [];
  // Strip code regions in this order: fenced blocks first (so their content
  // is fully blanked before inline-tick detection), then inline backtick
  // spans, then 4-space-indented blocks. Each pass replaces forbidden
  // regions with spaces (preserving newlines) so line-number reporting in
  // the link-detection passes below stays accurate.
  let stripped = stripFencedBlocks(content);
  stripped = stripInlineCodeSpans(stripped);
  stripped = stripIndentedCodeBlocks(stripped);

  const checkUrl = (url, charIndex) => {
    if (isExternalUrl(url) || isMailtoOrAnchor(url)) {
      return;
    }
    if (!escapesDocsTree(filePath, url)) {
      return;
    }
    violations.push({
      file: filePath,
      line: lineNumberOf(stripped, charIndex),
      url,
      reason:
        "links from docs/ to repo files outside docs/ must use the full https://github.com/Ambiguous-Interactive/DxMessaging/blob/master/... URL"
    });
  };

  // Inline links: `[text](url)`.
  MARKDOWN_LINK_RE.lastIndex = 0;
  let match;
  while ((match = MARKDOWN_LINK_RE.exec(stripped)) !== null) {
    checkUrl(match[2], match.index);
  }

  // Reference-style link DEFINITIONS: `[ref]: url "title"`. These are the
  // line that actually carries the URL; the in-body usage (`[text][ref]`,
  // `[ref][]`, or bare `[ref]`) does not need separate scanning because
  // mkdocs strict mode trips on the resolved URL, which lives in the
  // definition. CommonMark requires definitions to start at column 0-3 and
  // we anchor with `^` via the `m` flag.
  REFERENCE_DEFINITION_RE.lastIndex = 0;
  while ((match = REFERENCE_DEFINITION_RE.exec(stripped)) !== null) {
    checkUrl(match[2], match.index);
  }
  return violations;
}

function scanFile(filePath) {
  let content;
  try {
    content = fs.readFileSync(filePath, "utf8");
  } catch (error) {
    return [
      {
        file: filePath,
        line: 0,
        url: "",
        reason: `failed to read: ${error.message}`
      }
    ];
  }
  return scanContent(filePath, content);
}

function toRepoRelative(absPath) {
  const rel = path.relative(REPO_ROOT, absPath);
  return rel.split(path.sep).join("/");
}

function isDocsMarkdown(filePath) {
  const abs = path.isAbsolute(filePath) ? filePath : path.resolve(process.cwd(), filePath);
  if (!abs.toLowerCase().endsWith(".md")) {
    return false;
  }
  const rel = path.relative(DOCS_ROOT, abs);
  return !rel.startsWith("..") && !path.isAbsolute(rel);
}

function main(argv) {
  let files;
  if (argv.length === 0) {
    files = listAllDocsFiles();
  } else {
    files = argv
      .map((file) => (path.isAbsolute(file) ? file : path.resolve(process.cwd(), file)))
      .filter((file) => isDocsMarkdown(file));
  }

  if (files.length === 0) {
    process.stdout.write("validate-docs-out-of-tree-links: no docs/*.md files to inspect.\n");
    return 0;
  }

  const allViolations = [];
  for (const file of files) {
    allViolations.push(...scanFile(file));
  }

  if (allViolations.length === 0) {
    process.stdout.write(
      `validate-docs-out-of-tree-links: 0 violations across ${files.length} file(s).\n`
    );
    return 0;
  }

  for (const v of allViolations) {
    process.stderr.write(
      `${toRepoRelative(v.file)}:${v.line}: out-of-tree link "${v.url}" -- ${v.reason}\n`
    );
  }
  process.stderr.write(
    `validate-docs-out-of-tree-links: ${allViolations.length} violation(s) found.\n`
  );
  return 1;
}

module.exports = {
  DOCS_ROOT,
  REPO_ROOT,
  scanContent,
  scanFile,
  escapesDocsTree,
  isDocsMarkdown,
  listAllDocsFiles,
  stripFencedBlocks,
  stripInlineCodeSpans,
  stripIndentedCodeBlocks,
  main
};

if (require.main === module) {
  process.exit(main(process.argv.slice(2)));
}
