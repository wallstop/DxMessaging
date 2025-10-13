#!/usr/bin/env node
/*
  Fix CRLF line endings and remove UTF-8 BOM on text files.
  Mirrors scope and rules from scripts/check-eol.js.
*/
const fs = require('fs');
const path = require('path');

const repoRoot = process.cwd();
const verbose = process.argv.includes('-v') || process.argv.includes('--verbose');

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

// Text file extensions to fix (must match check-eol.js)
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

function stripBom(buf) {
  if (buf.length >= 3 && buf[0] === 0xef && buf[1] === 0xbb && buf[2] === 0xbf) {
    return buf.slice(3);
  }
  return buf;
}

function toCrlf(txt) {
  // Normalize all newlines to \n then convert to CRLF
  // Replace CRLF first to avoid doubling
  let normalized = txt.replace(/\r\n/g, '\n');
  normalized = normalized.replace(/\r/g, '\n');
  return normalized.replace(/\n/g, '\r\n');
}

const allFiles = walk(repoRoot);
let changed = 0;
let scanned = 0;

for (const file of allFiles) {
  const ext = path.extname(file).toLowerCase();
  if (!exts.has(ext)) continue;
  let buf;
  try {
    buf = fs.readFileSync(file);
  } catch {
    continue;
  }
  scanned++;
  const origBuf = buf;
  buf = stripBom(buf);
  const origTxt = origBuf.toString('utf8');
  const noBomTxt = buf.toString('utf8');
  const crlfTxt = toCrlf(noBomTxt);

  if (crlfTxt !== origTxt) {
    fs.writeFileSync(file, crlfTxt, { encoding: 'utf8' });
    changed++;
    if (verbose) console.log(`Fixed: ${path.relative(repoRoot, file)}`);
  }
}

console.log(`Scanned ${scanned} text files. Updated ${changed}.`);
if (changed > 0) {
  console.log('All changes written with CRLF and no BOM.');
}

