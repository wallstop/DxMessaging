/**
 * @fileoverview Contract test for the .claude/settings.local.json allowlist.
 *
 * The local Claude Code settings file pre-authorizes the canonical Unity-side
 * commands so contributors don't get a permission prompt every time they ask
 * the agent to run the headless test runner. The plan (Layer C.1) defines the
 * exact entry strings; this test makes sure they survive merges and edits.
 */

"use strict";

const fs = require("fs");
const path = require("path");

const REPO_ROOT = path.resolve(__dirname, "..", "..");
const SETTINGS_PATH = path.join(REPO_ROOT, ".claude", "settings.local.json");

/**
 * Parse a possibly-JSONC file (strip `//` and `/* ... *\/` comments before
 * JSON.parse). The Claude CLI accepts JSONC, so be permissive here.
 *
 * @param {string} text
 * @returns {unknown}
 */
function parseJsonc(text) {
  // Naive but adequate for our settings file:
  //   - block comments: /* ... */
  //   - line comments:  // ...
  // Skip stripping inside string literals so we don't corrupt patterns that
  // legitimately contain `//`.
  let out = "";
  let i = 0;
  let inString = false;
  let stringChar = "";
  while (i < text.length) {
    const c = text[i];
    const next = text[i + 1];
    if (inString) {
      if (c === "\\" && i + 1 < text.length) {
        out += c + next;
        i += 2;
        continue;
      }
      if (c === stringChar) {
        inString = false;
      }
      out += c;
      i += 1;
      continue;
    }
    if (c === '"' || c === "'") {
      inString = true;
      stringChar = c;
      out += c;
      i += 1;
      continue;
    }
    if (c === "/" && next === "/") {
      // Skip until end of line.
      while (i < text.length && text[i] !== "\n") {
        i += 1;
      }
      continue;
    }
    if (c === "/" && next === "*") {
      i += 2;
      while (i < text.length && !(text[i] === "*" && text[i + 1] === "/")) {
        i += 1;
      }
      i += 2;
      continue;
    }
    out += c;
    i += 1;
  }
  return JSON.parse(out);
}

const REQUIRED_ENTRIES = [
  "Bash(bash scripts/unity/run-tests.sh:*)",
  "Bash(pwsh -NoProfile -File scripts/unity/run-tests.ps1:*)",
  "Bash(bash scripts/unity/activate-license.sh:*)",
  "Bash(docker run --rm * unityci/editor:*)",
  "Bash(docker pull unityci/editor:*)",
  "Bash(docker volume:*)",
  "Bash(node scripts/run-managed-jest.js:*)"
];

describe(".claude/settings.local.json contract", () => {
  let parsed;

  beforeAll(() => {
    expect(fs.existsSync(SETTINGS_PATH)).toBe(true);
    const raw = fs.readFileSync(SETTINGS_PATH, "utf8");
    parsed = parseJsonc(raw);
  });

  test("is structurally valid JSON / JSONC", () => {
    expect(parsed).toEqual(expect.any(Object));
  });

  test("declares a permissions.allow array", () => {
    expect(parsed.permissions).toBeDefined();
    expect(Array.isArray(parsed.permissions.allow)).toBe(true);
  });

  test.each(REQUIRED_ENTRIES.map((entry) => [entry]))(
    "permissions.allow contains the canonical entry %s",
    (entry) => {
      expect(parsed.permissions.allow).toContain(entry);
    }
  );
});
