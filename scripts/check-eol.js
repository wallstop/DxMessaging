#!/usr/bin/env node
/*
  Enforce CRLF line endings and no UTF-8 BOM on text files.
  Mirrors the behavior of scripts/check-eol.ps1 used in CI.
*/
const fs = require('fs');
const path = require('path');

const repoRoot = process.cwd();

// Exclude directory patterns (match anywhere in path)
const excludeRegexes = [
  /(^|[\/\\])\.git([\/\\]|$)/i,
  /(^|[\/\\])node_modules([\/\\]|$)/i,
  /(^|[\/\\])Library([\/\\]|$)/,
  /(^|[\/\\])(Obj|obj)([\/\\]|$)/,
  /(^|[\/\\])Temp([\/\\]|$)/,
  /(^|[\/\\])Samples~([\/\\]|$)/,
  /(^|[\/\\])\.vs([\/\\]|$)/
];

// Text file extensions to validate
const exts = new Set([
  '.cs', '.csproj', '.sln',
  '.json', '.jsonc', '.toml',
  '.yaml', '.yml',
  '.md', '.markdown',
  '.xml', '.uxml', '.uss',
  '.shader', '.hlsl', '.compute', '.cginc',
  '.asmdef', '.asmref', '.meta',
  '.ps1'
]);

/** Recursively collect file paths under dir */
function walk(dir, files = []) {
  const entries = fs.readdirSync(dir, { withFileTypes: true });
  for (const ent of entries) {
    const full = path.join(dir, ent.name);
    if (excludeRegexes.some((re) => re.test(full))) {
      continue;
    }
    if (ent.isDirectory()) {
      walk(full, files);
    } else if (ent.isFile()) {
      files.push(full);
    }
  }
  return files;
}

function hasBom(buf) {
  return buf.length >= 3 && buf[0] === 0xef && buf[1] === 0xbb && buf[2] === 0xbf;
}

function hasNonCrlfEol(buf) {
  const txt = buf.toString('utf8');
  // Strip all valid CRLF pairs; any remaining bare \n or \r is invalid
  const stripped = txt.replace(/\r\n/g, '');
  return stripped.includes('\n') || stripped.includes('\r');
}

const allFiles = walk(repoRoot);
const bomFiles = [];
const badEolFiles = [];

for (const file of allFiles) {
  const ext = path.extname(file).toLowerCase();
  if (!exts.has(ext)) continue;
  let buf;
  try {
    buf = fs.readFileSync(file);
  } catch {
    continue;
  }
  if (hasBom(buf)) bomFiles.push(path.relative(repoRoot, file));
  if (hasNonCrlfEol(buf)) badEolFiles.push(path.relative(repoRoot, file));
}

if (bomFiles.length === 0 && badEolFiles.length === 0) {
  console.log('EOL check passed: CRLF only and no BOMs detected.');
  process.exit(0);
}

if (bomFiles.length) {
  console.log('Files contain a UTF-8 BOM (should be no BOM):');
  for (const f of bomFiles) console.log(`  ${f}`);
}
if (badEolFiles.length) {
  console.log('Files contain non-CRLF line endings (found LF or bare CR):');
  for (const f of badEolFiles) console.log(`  ${f}`);
}
console.error('EOL/BOM policy violations detected.');
process.exit(1);

