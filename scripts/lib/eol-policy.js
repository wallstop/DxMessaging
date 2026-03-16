"use strict";

// SYNC: Keep in sync with scripts/check-eol.ps1 $crlfExtensions and .gitattributes EOL policy.
const crlfExts = new Set([
  ".cs",
  ".csproj",
  ".sln",
  ".props",
]);

// SYNC: Keep in sync with scripts/check-eol.ps1 $lfExtensions and .gitattributes EOL policy.
const lfExts = new Set([
  ".js", ".cjs", ".mjs",
  ".json", ".jsonc", ".toml",
  ".yaml", ".yml",
  ".md", ".markdown",
  ".xml", ".uxml", ".uss",
  ".shader", ".hlsl", ".compute", ".cginc",
  ".asmdef", ".asmref", ".meta",
  ".ps1",
  ".sh", ".bash", ".zsh", ".ksh", ".fish",
]);

module.exports = {
  crlfExts,
  lfExts,
};
