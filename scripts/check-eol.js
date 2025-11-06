#!/usr/bin/env node
/*
  Enforce CRLF line endings and no UTF-8 BOM on text files.
  Mirrors the behavior of scripts/check-eol.ps1 used in CI.
*/
const fs = require('fs');
const path = require('path');
const { spawnSync } = require('child_process');

const repoRoot = process.cwd();
const argv = process.argv.slice(2);
let checkEntireRepo = false;
const targetArgs = [];

for (const arg of argv) {
  if (arg === '--all') {
    checkEntireRepo = true;
    continue;
  }
  targetArgs.push(arg);
}

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

function resolveTargets(targets) {
  if (!targets || targets.length === 0) {
    return [];
  }

  const seen = new Set();
  const resolved = [];

  for (const rawTarget of targets) {
    const targetPath = path.resolve(repoRoot, rawTarget);
    if (!fs.existsSync(targetPath)) {
      continue;
    }

    const isExcluded = excludeRegexes.some((re) => re.test(targetPath));
    if (isExcluded) {
      continue;
    }

    const stats = fs.statSync(targetPath);
    if (stats.isDirectory()) {
      for (const file of walk(targetPath)) {
        if (!seen.has(file)) {
          resolved.push(file);
          seen.add(file);
        }
      }
    } else if (stats.isFile()) {
      if (!seen.has(targetPath)) {
        resolved.push(targetPath);
        seen.add(targetPath);
      }
    }
  }

  return resolved;
}

function getStagedFiles() {
  const result = spawnSync('git', ['diff', '--cached', '--name-only', '--diff-filter=ACMR'], {
    cwd: repoRoot,
    encoding: 'utf8'
  });

  if (result.status !== 0) {
    return [];
  }

  return result.stdout
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean);
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

const candidateFiles = checkEntireRepo
  ? walk(repoRoot)
  : resolveTargets(targetArgs.length > 0 ? targetArgs : getStagedFiles());

const textFiles = [];
const seenFiles = new Set();

for (const file of candidateFiles) {
  const ext = path.extname(file).toLowerCase();
  if (!exts.has(ext)) {
    continue;
  }

  const absolute = path.resolve(repoRoot, file);
  if (seenFiles.has(absolute)) {
    continue;
  }

  seenFiles.add(absolute);
  textFiles.push(absolute);
}

if (textFiles.length === 0) {
  console.log('EOL check skipped: no matching files to verify.');
  process.exit(0);
}

const bomFiles = [];
const badEolFiles = [];

for (const file of textFiles) {
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
