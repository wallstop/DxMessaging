/**
 * @fileoverview Static category guard for the "duplicate analyzer DLL" /
 * "analyzer meta missing RoslynAnalyzer label" / "csproj auto-copies analyzers
 * into Assets/Plugins" regression family.
 *
 * THE CATEGORY: Unity's analyzer/source-generator pipeline picks DLLs up by
 * their `RoslynAnalyzer` asset label (in the .meta file). When ANY of these
 * three forms of duplication / mis-labeling happen, Unity 2021 hard-fails the
 * build with
 *   PrecompiledAssemblyException: Multiple precompiled assemblies with the
 *   same name <name>.dll included on the current platform.
 * and Unity 2022+ silently hangs in csc for 20+ minutes before the operator
 * cancels:
 *   (1) Editor/Analyzers/WallstopStudios.DxMessaging.Analyzer.dll.meta is
 *       missing the `RoslynAnalyzer` label, so something else copies the DLL
 *       to a second location to label the copy, and now there are TWO copies
 *       of the same precompiled assembly. (Both shipped analyzer DLLs --
 *       WallstopStudios.DxMessaging.SourceGenerators.dll AND
 *       WallstopStudios.DxMessaging.Analyzer.dll -- must carry the label
 *       directly on their .meta so no copy-and-label-the-copy hack is
 *       needed.)
 *   (2) Editor/SetupCscRsp.cs auto-copies the analyzer DLLs into
 *       Assets/Plugins/Editor/WallstopStudios.DxMessaging at editor load,
 *       producing two on-disk copies of the same DLL (one in
 *       Editor/Analyzers/, one in Assets/Plugins/...).
 *   (3) Either source-generator csproj copies its build output into the same
 *       Assets/Plugins/Editor/WallstopStudios.DxMessaging directory in any
 *       Unity project that builds them in-tree.
 * Each one independently reproduces the duplicate-precompiled-assembly bug.
 * This guard enforces the post-fix shape so a future "convenience" change
 * cannot silently bring any of them back.
 *
 * Detectors (data-driven; no separate "this guard is alive" tests are required
 * because every detector's data set is generated from the on-disk repo --
 * Jest's `test.each([])` would fail loudly if a glob returned nothing):
 *   (A) For every `*.dll.meta` under `Editor/Analyzers/`:
 *         - `WallstopStudios.DxMessaging.SourceGenerators.dll.meta` and
 *           `WallstopStudios.DxMessaging.Analyzer.dll.meta` MUST contain
 *           `RoslynAnalyzer` (in a top-level `labels:` block).
 *         - All other `*.dll.meta` (Microsoft.CodeAnalysis.*, System.*) MUST
 *           NOT contain `RoslynAnalyzer` -- they are plain Roslyn runtime
 *           dependencies, not analyzers themselves.
 *   (B) `Editor/SetupCscRsp.cs` MUST NOT contain the auto-copy regression
 *       pattern (no `EnsureDLLsExistInAssets` method, no `EditorApplication.
 *       delayCall += EnsureDLLsExistInAssets;`, no `File.Copy(` near a
 *       string mentioning `Assets/Plugins/Editor/WallstopStudios.DxMessaging`,
 *       no `PluginImporter` mutation of an `Assets/Plugins/Editor/
 *       WallstopStudios.DxMessaging` path).
 *   (C) `SourceGenerators/.../*.csproj` for both the source-generator and
 *       the analyzer project MUST NOT have a target that copies analyzer
 *       DLLs into `Assets/Plugins/Editor/WallstopStudios.DxMessaging`.
 *
 * Pure static analysis. No shell-outs. Modelled on
 * unity-license-leak-safety.test.js's stripCode helper (we reuse the same
 * comment/string-stripping behavior for the C# detector so a "bad" identifier
 * mentioned only inside a string/comment does not false-fire).
 */

"use strict";

const fs = require("fs");
const path = require("path");

const REPO_ROOT = path.resolve(__dirname, "..", "..");

// ---- Surface under guard --------------------------------------------------
const ANALYZER_DIR = path.join(REPO_ROOT, "Editor", "Analyzers");
const EDITOR_DIR = path.join(REPO_ROOT, "Editor");
const SETUP_CSC_RSP_FILE = path.join(REPO_ROOT, "Editor", "SetupCscRsp.cs");
const SOURCE_GENERATORS_CSPROJ = path.join(
  REPO_ROOT,
  "SourceGenerators",
  "WallstopStudios.DxMessaging.SourceGenerators",
  "WallstopStudios.DxMessaging.SourceGenerators.csproj"
);
const ANALYZER_CSPROJ = path.join(
  REPO_ROOT,
  "SourceGenerators",
  "WallstopStudios.DxMessaging.Analyzer",
  "WallstopStudios.DxMessaging.Analyzer.csproj"
);

// DLL filenames that ARE analyzer hosts (must carry the RoslynAnalyzer label).
const ANALYZER_HOST_DLLS = new Set([
  "WallstopStudios.DxMessaging.SourceGenerators.dll",
  "WallstopStudios.DxMessaging.Analyzer.dll"
]);

// The forbidden auto-copy destination -- the regression pattern that
// reproduced the Unity 2021 duplicate-precompiled-assembly error.
const FORBIDDEN_COPY_PATH_SUBSTR = "Assets/Plugins/Editor/WallstopStudios.DxMessaging";

// ---- Helpers --------------------------------------------------------------

// Strip C# // line comments and /* block */ comments and blank the interior
// of "..." string literals AND '...' char literals, so a method-name token
// or copy-path substring mentioned only inside a comment, string, or char
// literal can never be mistaken for a real occurrence.
//
// CHAR LITERAL NUANCE: a C# char literal like `'"'` would be mis-parsed if
// we ignored the leading apostrophe: the embedded `"` would be treated as
// a string-literal opener and everything until the next `"` would be
// silently blanked, swallowing real code. The fix: when we hit `'` outside
// a string/comment, scan to the matching `'` (handling `\\` escapes so
// `'\''` and `'\\'` parse correctly), then blank the interior just like a
// string interior.
//
// VERBATIM STRING NUANCE: a C# verbatim string `@"..."` treats `\` as
// LITERAL (not an escape) and treats `""` as the only way to embed a
// single `"` (doubled quotes do NOT close the string). The old code
// entered regular-string mode at the `"` of `@"...` and consumed `\"` as
// an escape inside the body -- this would mis-handle a payload like
// `@"foo\"` because the `\"` reads as escaped-quote in regular mode but
// is actually backslash-then-closing-quote in verbatim mode. The fix:
// recognize the `@"` opener and enter a separate `inVerbatim` mode whose
// state machine consumes `""` as a literal `"` (still inside the string)
// and treats `\` as plain interior data.
//
// INTERPOLATED VERBATIM NUANCE (C# 8+): both `@$"..."` and `$@"..."` are
// interpolated verbatim strings. They share the verbatim-string parsing
// rules (`""` embeds a `"`, `\` is literal interior data), so we treat
// them identically to a `@"..."` and enter `inVerbatim` at the opening
// `"`. Without this, a payload like `@$"foo\"; var leak = "Bad";` opens
// regular-string mode at the `"` after `@$`, then consumes `\"` as an
// escaped quote, then the NEXT `"` (the opener of the second string)
// silently closes the still-open string -- so the identifier inside the
// second string would survive stripping. Interpolation holes (`{...}`)
// inside the verbatim string are NOT modelled (the detectors do not look
// for identifiers inside interpolation holes); the goal here is just to
// keep the stripper from drifting past the closing `"` of the literal.
function stripCSharp(text) {
  let out = "";
  let i = 0;
  const len = text.length;
  let inLine = false;
  let inBlock = false;
  let inString = false;
  let inVerbatim = false;
  while (i < len) {
    const ch = text[i];
    const next = i + 1 < len ? text[i + 1] : "";
    if (inLine) {
      if (ch === "\n") {
        inLine = false;
        out += ch;
      } else {
        out += " ";
      }
      i++;
      continue;
    }
    if (inBlock) {
      if (ch === "*" && next === "/") {
        inBlock = false;
        out += "  ";
        i += 2;
        continue;
      }
      if (ch === "\n") {
        out += ch;
      } else {
        out += " ";
      }
      i++;
      continue;
    }
    if (inVerbatim) {
      // Verbatim string: `""` is an embedded `"` (stay in string),
      // `\\` is NOT an escape (just literal data), a single `"` closes
      // the string. Preserve newlines so line numbers do not shift on a
      // multi-line verbatim payload (verbatim strings legally span lines).
      if (ch === '"' && next === '"') {
        // Doubled quote inside verbatim -- emit two blanks to preserve
        // byte offsets and stay in verbatim mode.
        out += "  ";
        i += 2;
        continue;
      }
      if (ch === '"') {
        inVerbatim = false;
        out += ch;
        i++;
        continue;
      }
      out += ch === "\n" ? "\n" : " ";
      i++;
      continue;
    }
    if (inString) {
      if (ch === "\\" && next !== "") {
        // Escaped char inside a regular string; both chars are interior data.
        out += "  ";
        i += 2;
        continue;
      }
      if (ch === '"') {
        inString = false;
        out += ch;
        i++;
        continue;
      }
      // String interior -- preserve newlines (so line numbers don't shift),
      // blank everything else.
      out += ch === "\n" ? "\n" : " ";
      i++;
      continue;
    }
    if (ch === "/" && next === "/") {
      inLine = true;
      out += "  ";
      i += 2;
      continue;
    }
    if (ch === "/" && next === "*") {
      inBlock = true;
      out += "  ";
      i += 2;
      continue;
    }
    // Char literal '...' -- scan to the matching close quote, blanking the
    // interior. This MUST come BEFORE the string-literal opener check so
    // `char Q = '"';` does not accidentally enter string mode at the `"`
    // inside the char literal.
    if (ch === "'") {
      out += ch;
      i++;
      while (i < len) {
        const cc = text[i];
        const nn = i + 1 < len ? text[i + 1] : "";
        if (cc === "\\" && nn !== "") {
          // Escaped char inside char literal (e.g. `'\''`, `'\"'`, `'\n'`).
          out += "  ";
          i += 2;
          continue;
        }
        if (cc === "'") {
          out += cc;
          i++;
          break;
        }
        // Preserve newlines so line numbers do not shift on a poorly
        // closed char literal; blank everything else.
        out += cc === "\n" ? "\n" : " ";
        i++;
      }
      continue;
    }
    // Interpolated verbatim opener `@$"..."` / `$@"..."` (C# 8+): both
    // forms share verbatim-string parsing rules, so we enter verbatim
    // mode at the opening quote. MUST be checked BEFORE the bare
    // `@"...` and `"...` openers below so we consume BOTH prefix
    // characters before flipping state.
    const third = i + 2 < len ? text[i + 2] : "";
    if (
      (ch === "@" && next === "$" && third === '"') ||
      (ch === "$" && next === "@" && third === '"')
    ) {
      out += ch;
      i++;
      out += text[i];
      i++;
      inVerbatim = true;
      out += text[i];
      i++;
      continue;
    }
    // Verbatim string opener `@"..."`: enter verbatim mode at the quote
    // (the `@` itself is benign code that we emit as-is). MUST be checked
    // BEFORE the regular `"` opener below.
    if (ch === "@" && next === '"') {
      out += ch;
      i++;
      inVerbatim = true;
      out += text[i];
      i++;
      continue;
    }
    // Regular string opener.
    if (ch === '"') {
      inString = true;
      out += ch;
      i++;
      continue;
    }
    out += ch;
    i++;
  }
  return out;
}

function readUtf8(absolutePath) {
  return fs.readFileSync(absolutePath, "utf8");
}

// List every *.dll.meta directly under Editor/Analyzers/. Sorted so test
// output is deterministic across OSes/filesystems.
function listAnalyzerDllMetaFiles() {
  if (!fs.existsSync(ANALYZER_DIR)) {
    return [];
  }
  return fs
    .readdirSync(ANALYZER_DIR)
    .filter((name) => name.endsWith(".dll.meta"))
    .sort();
}

// True if a YAML-ish .meta text contains a top-level `labels:` block that
// lists `RoslynAnalyzer`. We do a string-grep (not a YAML parse) because the
// .meta format is hand-edited Unity-flavored YAML and we only need to assert
// presence/absence of the literal token; using a parser would add
// complexity without changing behavior.
function metaHasRoslynAnalyzerLabel(text) {
  // Match either the canonical `\n- RoslynAnalyzer\n` (single-item list under
  // `labels:`) or any other YAML list form that puts the literal on its own
  // line under a `labels:` block. The two-line regex is robust to either
  // dash-indent style ("- RoslynAnalyzer" vs "  - RoslynAnalyzer").
  return /\nlabels:\s*\n(?:[^\n]*\n)*?\s*-\s+RoslynAnalyzer\b/.test(text);
}

// ---- (B) Editor/*.cs forbidden auto-copy detector -------------------------
//
// Scans a single .cs file for the auto-copy regression pattern. Used both by
// the broad Editor/-tree sweep and by the M3b detector self-test that
// confirms the detector FIRES on a fixture .cs file containing the bad
// pattern.
function findEditorAutoCopyViolations(absoluteSourcePath) {
  const text = readUtf8(absoluteSourcePath);
  const stripped = stripCSharp(text);
  const violations = [];
  const relative = path.relative(REPO_ROOT, absoluteSourcePath).replace(/\\/g, "/");

  // (B1) No method named EnsureDLLsExistInAssets.
  if (/\bEnsureDLLsExistInAssets\b/.test(stripped)) {
    violations.push(
      `${relative} still references EnsureDLLsExistInAssets. ` +
        "Remove the method and its delayCall registration -- it auto-copies " +
        "analyzer DLLs into Assets/Plugins/Editor/WallstopStudios.DxMessaging " +
        "and reproduces the Unity 2021 duplicate-precompiled-assembly bug."
    );
  }

  // (B2) No `EditorApplication.delayCall += EnsureDLLsExistInAssets;` line.
  if (/EditorApplication\.delayCall\s*\+=\s*EnsureDLLsExistInAssets\b/.test(stripped)) {
    violations.push(
      `${relative} static ctor still registers EnsureDLLsExistInAssets ` +
        "via EditorApplication.delayCall."
    );
  }

  // (B3) `File.Copy(` whose argument list contains a STRING LITERAL holding
  // the forbidden Assets/Plugins path. We deliberately require a literal
  // string here -- a previous version of this detector scanned the raw text
  // within +/-400 chars of `File.Copy(`, which produced false positives
  // when a comment somewhere in the file mentioned the path even though
  // the File.Copy() call was unrelated.
  //
  // We search the STRIPPED text for File.Copy(...): stripCSharp blanks
  // string interiors and comment bodies, so a forbidden path mentioned
  // ONLY inside a comment or unrelated string cannot ever appear inside a
  // surviving File.Copy(...) argument list. The path substring inside a
  // real File.Copy() string-literal argument survives because the path
  // characters themselves survive (we only blank the INTERIOR of string
  // literals; the bracketing quotes and surrounding code remain).
  //
  // Wait -- stripCSharp blanks the path interior too. So we must check the
  // ORIGINAL text for the literal-arg shape. The fix: find each
  // `File.Copy(` in the stripped text (so we ignore File.Copy mentioned
  // only in comments), then locate the SAME byte offset in the raw text
  // and read the argument list there. The raw arg list still contains the
  // original string literal.
  const fileCopyRe = /\bFile\.Copy\s*\(/g;
  let m;
  const fileCopyArgRe = new RegExp(
    `\\bFile\\.Copy\\s*\\([^)]*"[^"]*${escapeRegex(FORBIDDEN_COPY_PATH_SUBSTR)}[^"]*"`
  );
  while ((m = fileCopyRe.exec(stripped)) !== null) {
    // The match is in the STRIPPED text but at the same byte offset in
    // the RAW text (stripCSharp preserves byte positions). Scan a window
    // of the RAW text starting at the match position and apply the
    // arg-literal regex anchored at that byte; the arg list ends at the
    // matching `)` which is well within a small window.
    const window = text.slice(m.index, Math.min(text.length, m.index + 800));
    if (fileCopyArgRe.test(window)) {
      violations.push(
        `${relative} still contains a File.Copy() call whose argument list ` +
          `includes a string literal naming the forbidden ` +
          `${FORBIDDEN_COPY_PATH_SUBSTR} path. ` +
          "Auto-copying analyzer DLLs into Assets reproduces the Unity 2021 " +
          "duplicate-precompiled-assembly bug."
      );
      break;
    }
  }

  // (B4) `PluginImporter` whose usage names the forbidden path inside a
  // string literal (typical shape:
  // PluginImporter.GetAtPath("Assets/Plugins/Editor/WallstopStudios.DxMessaging/x.dll")
  // OR AssetDatabase.LoadMainAssetAtPath("Assets/Plugins/Editor/...")
  // immediately followed by `as PluginImporter` / a PluginImporter cast).
  // Like B3 above, we anchor on the stripped-text occurrence of the
  // PluginImporter identifier (so a mention inside a comment is harmless)
  // and require a string-literal containing the forbidden path inside the
  // raw text within a small window of that occurrence.
  const pluginImporterRe = /\bPluginImporter\b/g;
  const pluginImporterStringRe = new RegExp(
    `"[^"]*${escapeRegex(FORBIDDEN_COPY_PATH_SUBSTR)}[^"]*"`
  );
  while ((m = pluginImporterRe.exec(stripped)) !== null) {
    const start = Math.max(0, m.index - 200);
    const end = Math.min(text.length, m.index + 600);
    const window = text.slice(start, end);
    if (pluginImporterStringRe.test(window)) {
      violations.push(
        `${relative} still mutates a PluginImporter whose path argument ` +
          `is a string literal naming the forbidden ${FORBIDDEN_COPY_PATH_SUBSTR}. ` +
          "The shipped .dll.meta carries RoslynAnalyzer directly; do not relabel " +
          "a Unity-side copy."
      );
      break;
    }
  }

  return violations;
}

// Escape every regex metacharacter so a literal substring (which may contain
// `.`, `(`, etc.) can be embedded inside a larger regex pattern.
function escapeRegex(str) {
  return str.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

// Recursively list every .cs file under the package's Editor/ directory.
// Skips Editor/Analyzers/ (which only contains shipped DLLs and their .meta
// siblings; the directory should never contain .cs sources, but the skip is
// defensive against a future change). Output is deterministic across
// filesystems.
//
// SYMLINK SAFETY: `entry.isDirectory()` returns true for symlinks that
// point at a directory, which means a circular link (e.g. `Editor/foo ->
// ..`) would send the recursion into an infinite loop. We skip ALL
// symlinks defensively -- the package's Editor/ tree does not legitimately
// contain symlinked subdirectories, and recursing through one would at
// best traverse the same file twice and at worst hang the test.
function listEditorCsFiles() {
  if (!fs.existsSync(EDITOR_DIR)) {
    return [];
  }
  const out = [];
  function walk(dir) {
    const entries = fs
      .readdirSync(dir, { withFileTypes: true })
      .sort((a, b) => a.name.localeCompare(b.name));
    for (const entry of entries) {
      const abs = path.join(dir, entry.name);
      // Skip symlinks before any other classification check: a symlink
      // pointing at a directory satisfies isDirectory() (which would
      // recurse), and a symlink at a file satisfies isFile() (which
      // would read through the link). Neither is desirable for a static
      // package-tree sweep.
      if (entry.isSymbolicLink()) {
        continue;
      }
      if (entry.isDirectory()) {
        // Skip Analyzers/ (DLL/meta only). Defensive against future
        // additions.
        if (path.relative(EDITOR_DIR, abs) === "Analyzers") {
          continue;
        }
        walk(abs);
        continue;
      }
      if (entry.isFile() && entry.name.endsWith(".cs")) {
        out.push(abs);
      }
    }
  }
  walk(EDITOR_DIR);
  return out;
}

// ---- (C) csproj auto-copy detector ----------------------------------------
function findCsprojForbiddenCopyTarget(absolutePath) {
  const text = readUtf8(absolutePath);

  // We DELIBERATELY do not parse the XML: the legacy target name was generic
  // (`PostBuildCopyAnalyzers`) and the relevant signal is that the file
  // contains the forbidden destination path at all. A clean csproj only
  // copies to `Editor/Analyzers` and never names the Assets/Plugins path.
  if (text.includes(FORBIDDEN_COPY_PATH_SUBSTR)) {
    // Allow a comment that explains why the legacy copy was removed: a
    // bare-mention as documentation (inside an XML comment <!-- ... -->) is
    // fine; an actual <Copy> or <MakeDir> referencing the path is not.
    // Easiest heuristic: blank out XML comments and then re-check.
    const withoutComments = text.replace(/<!--[\s\S]*?-->/g, "");
    if (withoutComments.includes(FORBIDDEN_COPY_PATH_SUBSTR)) {
      return (
        `${path.relative(REPO_ROOT, absolutePath)} still references ` +
        `${FORBIDDEN_COPY_PATH_SUBSTR} outside a <!-- comment -->. ` +
        "Building this csproj inside a Unity project tree would create a " +
        "second on-disk copy of the analyzer DLL and reproduce the Unity " +
        "2021 duplicate-precompiled-assembly bug."
      );
    }
  }
  return null;
}

// ---- Tests ----------------------------------------------------------------
describe("Unity analyzer .dll.meta label invariants", () => {
  const META_FILES = listAnalyzerDllMetaFiles();

  test("Editor/Analyzers/ contains the expected analyzer + dependency .dll.meta files", () => {
    // ANTI-NO-OP: a silently-empty scan would defeat the whole point of the
    // data-driven cases below.
    expect(META_FILES.length).toBeGreaterThan(0);
    // Both analyzer DLLs must be present and meta-labeled.
    expect(META_FILES).toContain("WallstopStudios.DxMessaging.SourceGenerators.dll.meta");
    expect(META_FILES).toContain("WallstopStudios.DxMessaging.Analyzer.dll.meta");
  });

  test.each(META_FILES)("%s carries the correct RoslynAnalyzer label state", (metaFile) => {
    const text = readUtf8(path.join(ANALYZER_DIR, metaFile));
    const dllName = metaFile.replace(/\.meta$/, "");
    const hasLabel = metaHasRoslynAnalyzerLabel(text);
    if (ANALYZER_HOST_DLLS.has(dllName)) {
      expect(hasLabel).toBe(true);
    } else {
      // Plain Roslyn runtime deps: MUST NOT carry the analyzer label, or
      // Unity will try to load them as analyzer hosts and break.
      expect(hasLabel).toBe(false);
    }
  });
});

describe("Editor/*.cs files do not contain the auto-copy regression pattern", () => {
  // Discovery sanity: at minimum SetupCscRsp.cs must be found, so a future
  // refactor that accidentally moves the file out of Editor/ (or that
  // accidentally drops the .cs extension) cannot silently turn the sweep
  // into a no-op.
  test("SetupCscRsp.cs is discovered by the Editor/-tree sweep", () => {
    expect(fs.existsSync(SETUP_CSC_RSP_FILE)).toBe(true);
    const discovered = listEditorCsFiles();
    expect(discovered.length).toBeGreaterThan(0);
    expect(discovered).toContain(SETUP_CSC_RSP_FILE);
  });

  test.each(listEditorCsFiles())(
    "%s has no EnsureDLLsExistInAssets / File.Copy into Assets/Plugins / PluginImporter mutation",
    (absoluteSourcePath) => {
      // Skip this test file itself if it ever ends up under Editor/ (it
      // doesn't today; defensive). The file extension check above also
      // guards against accidentally pulling in our own fixtures.
      const violations = findEditorAutoCopyViolations(absoluteSourcePath);
      expect(violations).toEqual([]);
    }
  );
});

describe("SourceGenerator csproj files do not auto-copy analyzers into Assets/Plugins", () => {
  const CSPROJ_CASES = [
    { label: "SourceGenerators csproj", path: SOURCE_GENERATORS_CSPROJ },
    { label: "Analyzer csproj", path: ANALYZER_CSPROJ }
  ];

  test.each(CSPROJ_CASES)("$label exists at the expected path", ({ path: absolutePath }) => {
    expect(fs.existsSync(absolutePath)).toBe(true);
  });

  test.each(CSPROJ_CASES)(
    "$label has no target copying analyzer DLLs into Assets/Plugins/Editor/WallstopStudios.DxMessaging",
    ({ path: absolutePath }) => {
      const violation = findCsprojForbiddenCopyTarget(absolutePath);
      expect(violation).toBeNull();
    }
  );
});

// Detector self-tests on tiny in-memory fixtures. These prove each detector
// FIRES on the bad shape and is SILENT on the good shape, modelled after the
// fixture-driven self-tests in unity-license-leak-safety.test.js.
describe("detector self-tests", () => {
  describe("metaHasRoslynAnalyzerLabel", () => {
    test("MATCHES the canonical Unity .meta label block", () => {
      const good =
        "fileFormatVersion: 2\n" +
        "guid: abc\n" +
        "labels:\n" +
        "- RoslynAnalyzer\n" +
        "PluginImporter:\n";
      expect(metaHasRoslynAnalyzerLabel(good)).toBe(true);
    });

    test("does NOT match a .meta with no labels block", () => {
      const bad = "fileFormatVersion: 2\nguid: abc\nPluginImporter:\n";
      expect(metaHasRoslynAnalyzerLabel(bad)).toBe(false);
    });

    test("does NOT match a labels block that lists a different label", () => {
      const irrelevant =
        "fileFormatVersion: 2\nguid: abc\nlabels:\n- SomethingElse\nPluginImporter:\n";
      expect(metaHasRoslynAnalyzerLabel(irrelevant)).toBe(false);
    });
  });

  describe("findEditorAutoCopyViolations on fixture shapes", () => {
    test("FIRES on a string containing EnsureDLLsExistInAssets identifier", () => {
      // Direct unit on the stripCSharp + regex without writing to disk.
      const stripped = stripCSharp("EditorApplication.delayCall += EnsureDLLsExistInAssets;");
      expect(/\bEnsureDLLsExistInAssets\b/.test(stripped)).toBe(true);
    });

    test("SILENT when the identifier appears only inside a // comment", () => {
      const stripped = stripCSharp("// historical note: EnsureDLLsExistInAssets was removed");
      expect(/\bEnsureDLLsExistInAssets\b/.test(stripped)).toBe(false);
    });

    test("SILENT when the identifier appears only inside a string literal", () => {
      const stripped = stripCSharp('Debug.Log("removed: EnsureDLLsExistInAssets");');
      expect(/\bEnsureDLLsExistInAssets\b/.test(stripped)).toBe(false);
    });
  });

  // M3a: char-literal handling. Without this, a `char Q = '"';` would flip
  // the stripper into string mode at the embedded `"` and silently swallow
  // arbitrary code until the next `"`. The cases below confirm that:
  //   (a) the char literal interior is blanked,
  //   (b) the code AFTER the char literal survives intact (the literal
  //       does NOT cause the stripper to eat the following statement), and
  //   (c) an embedded escaped quote inside the char literal does not break
  //       the parse.
  describe("stripCSharp handles C# char literals containing quotes", () => {
    test("char Q = '\"'; does not flip into string mode (raw apostrophe-around-double)", () => {
      const src = "char Q = '\"'; var token = EnsureDLLsExistInAssets;";
      const stripped = stripCSharp(src);
      // Char literal interior is blanked, but identifiers on either side are
      // preserved. The detector identifier `EnsureDLLsExistInAssets` is real
      // code (NOT inside a string), so it MUST survive stripping.
      expect(stripped).toContain("EnsureDLLsExistInAssets");
      // The interior `"` MUST be blanked (no real `"` should survive inside
      // the char literal). We assert on the substring between the apostrophes
      // by reading the same byte positions in the stripped output.
      const openIndex = src.indexOf("'");
      const closeIndex = src.indexOf("'", openIndex + 1);
      const blankInterior = stripped.slice(openIndex + 1, closeIndex);
      expect(blankInterior).toBe(" ");
    });

    test("char Q = '\\\"'; (escaped double quote) preserves later identifiers", () => {
      const src = "char Q = '\\\"'; var token = EnsureDLLsExistInAssets;";
      const stripped = stripCSharp(src);
      expect(stripped).toContain("EnsureDLLsExistInAssets");
    });

    test("char Q = '\\''; (escaped apostrophe) does not break out early", () => {
      // `'\''` is the canonical C# escape for a single-quote char. The
      // stripper MUST treat the middle `'` as escaped (consumed by the
      // preceding backslash) and only close on the trailing apostrophe.
      const src = "char Q = '\\''; var token = EnsureDLLsExistInAssets;";
      const stripped = stripCSharp(src);
      expect(stripped).toContain("EnsureDLLsExistInAssets");
    });

    test("real string literal AFTER a char literal still blanks correctly", () => {
      // Defensive: the char-literal handling must restore the stripper to
      // a clean code state so the NEXT `"..."` is recognized as a string.
      const src = "char Q = '\"'; Debug.Log(\"EnsureDLLsExistInAssets\");";
      const stripped = stripCSharp(src);
      // The string-literal `EnsureDLLsExistInAssets` MUST be blanked.
      expect(/\bEnsureDLLsExistInAssets\b/.test(stripped)).toBe(false);
    });
  });

  // Verbatim-string handling regression: in OLD code (no verbatim mode), the
  // payload `@"foo\"` opened regular-string mode at the `"`, then read `\"`
  // as an escaped-quote consume-pair, then the NEXT `"` (the opener of the
  // following regular string) actually CLOSED the still-open string. The
  // remaining identifier inside the second string then sat OUTSIDE any
  // string and would surface to the detectors. The new verbatim mode handles
  // `@"..."` separately: `\` is literal (not an escape), `""` is the only
  // way to embed a `"`, and a single `"` closes the string. So the second
  // string in the source below MUST correctly be in regular-string mode,
  // and the identifier inside it MUST be blanked.
  describe("stripCSharp handles C# verbatim @\"...\" strings", () => {
    test("var s = @\"foo\\\"; var t = \"EnsureDLLsExistInAssets\"; blanks the identifier inside the regular string", () => {
      const src = 'var s = @"foo\\"; var t = "EnsureDLLsExistInAssets";';
      const stripped = stripCSharp(src);
      // The identifier MUST NOT survive: it lives inside a regular `"..."`
      // string whose interior the stripper blanks. The verbatim string
      // ahead of it must NOT leak its state into the following code.
      expect(/\bEnsureDLLsExistInAssets\b/.test(stripped)).toBe(false);
    });

    test("var s = @\"foo\"\"bar\"; (doubled-quote embedded inside verbatim) does not break later code", () => {
      // `""` inside a verbatim string embeds a literal `"`. The stripper
      // must consume both characters as interior (still in verbatim mode),
      // NOT close-and-reopen on the first `"`. Otherwise the parse drifts
      // and code AFTER the verbatim string can be silently swallowed.
      const src = 'var s = @"foo""bar"; var token = EnsureDLLsExistInAssets;';
      const stripped = stripCSharp(src);
      expect(stripped).toContain("EnsureDLLsExistInAssets");
    });

    test("verbatim string with embedded backslash does NOT treat \\\" as escape", () => {
      // Sanity: an interior `\` is literal data in verbatim mode. The
      // immediately-following `"` must close the verbatim string, not be
      // consumed as part of an escape.
      const src = 'var s = @"a\\"; Debug.Log("EnsureDLLsExistInAssets");';
      const stripped = stripCSharp(src);
      // The identifier inside the regular string MUST be blanked: the
      // verbatim string closed at the `"` right after `\`, leaving us in
      // code mode for the next statement.
      expect(/\bEnsureDLLsExistInAssets\b/.test(stripped)).toBe(false);
    });

    // INTERPOLATED VERBATIM (C# 8+): `@$"..."` and `$@"..."` share the
    // verbatim-string parsing rules, so the stripper must treat them
    // identically to `@"..."`. Without the dedicated opener detection,
    // the leading `@$` or `$@` would surface as code and the following
    // `"` would open a REGULAR string -- then `\"` inside the body would
    // be consumed as an escaped quote and the next `"` (the opener of an
    // unrelated string) would silently close the still-open literal. The
    // identifier inside the SECOND string would then drift out of any
    // string and surface to the detectors.
    test('var s = @$"foo\\"; var leak = "EnsureDLLsExistInAssets"; blanks the identifier inside the regular string', () => {
      const src = 'var s = @$"foo\\"; var leak = "EnsureDLLsExistInAssets";';
      const stripped = stripCSharp(src);
      expect(/\bEnsureDLLsExistInAssets\b/.test(stripped)).toBe(false);
    });

    test('var s = $@"foo\\"; var leak = "EnsureDLLsExistInAssets"; (reversed prefix) blanks the identifier inside the regular string', () => {
      const src = 'var s = $@"foo\\"; var leak = "EnsureDLLsExistInAssets";';
      const stripped = stripCSharp(src);
      expect(/\bEnsureDLLsExistInAssets\b/.test(stripped)).toBe(false);
    });

    test('@$"foo""bar" (doubled-quote inside interpolated verbatim) does not break later code', () => {
      const src = 'var s = @$"foo""bar"; var token = EnsureDLLsExistInAssets;';
      const stripped = stripCSharp(src);
      // The identifier outside the literal is real code and MUST survive
      // stripping; the doubled-quote inside the interpolated verbatim
      // must stay inside the string.
      expect(stripped).toContain("EnsureDLLsExistInAssets");
    });
  });

  describe("findCsprojForbiddenCopyTarget on fixture shapes", () => {
    // Suffix the fixture root with the current jest invocation's PID so a
    // parallel `jest --runTestsByPath` run (or a stale crashed run leaving
    // its tree on disk) cannot collide with this one. Otherwise two
    // simultaneous runs writing to the same `.artifacts/test-fixtures-...`
    // directory race during `mkdirSync`/`rmSync` cleanup.
    const tmpRoot = path.join(
      REPO_ROOT,
      ".artifacts",
      `test-fixtures-analyzer-meta-${process.pid}`
    );

    beforeAll(() => {
      fs.mkdirSync(tmpRoot, { recursive: true });
    });
    afterAll(() => {
      try {
        fs.rmSync(tmpRoot, { recursive: true, force: true });
      } catch {
        // best-effort cleanup
      }
    });

    test("SILENT on a csproj that mentions the path only inside an XML comment", () => {
      const fixture = path.join(tmpRoot, "doc-comment.csproj");
      fs.writeFileSync(
        fixture,
        "<Project>\n" +
          "  <!-- The legacy Assets/Plugins/Editor/WallstopStudios.DxMessaging copy was removed. -->\n" +
          "</Project>\n"
      );
      expect(findCsprojForbiddenCopyTarget(fixture)).toBeNull();
    });

    test("FIRES on a csproj that names the path in a real element", () => {
      const fixture = path.join(tmpRoot, "real-copy.csproj");
      fs.writeFileSync(
        fixture,
        "<Project>\n" +
          '  <Copy SourceFiles="x.dll" DestinationFolder="Assets/Plugins/Editor/WallstopStudios.DxMessaging" />\n' +
          "</Project>\n"
      );
      expect(findCsprojForbiddenCopyTarget(fixture)).not.toBeNull();
    });
  });

  // M3b: temp-file self-test that confirms findEditorAutoCopyViolations
  // FIRES on a fake .cs source that names a File.Copy() into the forbidden
  // Assets/Plugins path. Lives under .artifacts/ (gitignored) so it is not
  // committed by accident.
  describe("findEditorAutoCopyViolations on temp .cs fixture", () => {
    // PID-suffix the fixture root: see the comment on
    // `test-fixtures-analyzer-meta-${process.pid}` above for the same
    // collision rationale.
    const tmpRoot = path.join(
      REPO_ROOT,
      ".artifacts",
      `test-fixtures-editor-auto-copy-${process.pid}`
    );

    beforeAll(() => {
      fs.mkdirSync(tmpRoot, { recursive: true });
    });
    afterAll(() => {
      try {
        fs.rmSync(tmpRoot, { recursive: true, force: true });
      } catch {
        // best-effort cleanup
      }
    });

    test("FIRES on a fixture .cs file containing File.Copy(...Assets/Plugins/Editor/WallstopStudios.DxMessaging...)", () => {
      // The mutation-test shape the reviewer called out: an INLINE string
      // literal naming the forbidden path inside the File.Copy() argument
      // list. The detector regex is anchored to a literal in the arg list
      // (vs. a variable indirection) so it cannot false-fire on a comment
      // or unrelated string the way the old +/-400-char window did.
      const fixture = path.join(tmpRoot, "Bad.cs");
      const csSource =
        "using System.IO;\n" +
        "static class Bad {\n" +
        "  static void Run(string source) {\n" +
        '    File.Copy(source, "Assets/Plugins/Editor/WallstopStudios.DxMessaging/x.dll", true);\n' +
        "  }\n" +
        "}\n";
      fs.writeFileSync(fixture, csSource);
      const violations = findEditorAutoCopyViolations(fixture);
      expect(violations.length).toBeGreaterThan(0);
      expect(violations.join("\n")).toMatch(/File\.Copy/);
    });

    test("SILENT on a fixture .cs file where a File.Copy is unrelated to a same-file comment mentioning the forbidden path", () => {
      // False-positive guard for the OLD detector's +/-400-char window: a
      // File.Copy() call that copies an unrelated file MUST NOT fire just
      // because a nearby comment or unrelated string mentions the path.
      // The new detector requires the path to appear as a string literal
      // INSIDE the File.Copy() argument list, so this fixture stays silent.
      const fixture = path.join(tmpRoot, "Unrelated.cs");
      const csSource =
        "using System.IO;\n" +
        "// Historical note: Assets/Plugins/Editor/WallstopStudios.DxMessaging used to live here.\n" +
        "static class Unrelated {\n" +
        "  static void CopyOther(string s, string d) {\n" +
        "    File.Copy(s, d, true);\n" +
        "  }\n" +
        "}\n";
      fs.writeFileSync(fixture, csSource);
      const violations = findEditorAutoCopyViolations(fixture);
      expect(violations).toEqual([]);
    });

    test("SILENT on a fixture .cs file with no forbidden pattern", () => {
      const fixture = path.join(tmpRoot, "Good.cs");
      const csSource =
        "static class Good {\n" +
        "  // Historical note: the legacy auto-copy lived here.\n" +
        '  static readonly string Note = "Assets/Plugins/Editor/WallstopStudios.DxMessaging is the legacy path";\n' +
        "  static int Compute() { return 42; }\n" +
        "}\n";
      fs.writeFileSync(fixture, csSource);
      const violations = findEditorAutoCopyViolations(fixture);
      // The path substring inside a STRING does not trigger File.Copy/
      // PluginImporter detectors (they require those identifiers near the
      // path), and the identifier `EnsureDLLsExistInAssets` is absent.
      expect(violations).toEqual([]);
    });
  });
});

module.exports = {
  stripCSharp,
  metaHasRoslynAnalyzerLabel,
  findEditorAutoCopyViolations,
  findCsprojForbiddenCopyTarget,
  listEditorCsFiles
};
