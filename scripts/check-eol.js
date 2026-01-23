#!/usr/bin/env node
/*
  Enforce CRLF line endings and no UTF-8 BOM on text files.
  Mirrors the behavior of scripts/check-eol.ps1 used in CI.
*/
const fs = require('fs');
const path = require('path');
const { spawnSync } = require('child_process');

function getGitRepoRoot() {
  const result = spawnSync('git', ['rev-parse', '--show-toplevel'], {
    cwd: process.cwd(),
    encoding: 'utf8'
  });
  if (result.status === 0 && result.stdout) {
    return result.stdout.trim();
  }
  // Fallback to cwd if not in a git repo
  return process.cwd();
}

const repoRoot = getGitRepoRoot();
const argv = process.argv.slice(2);
let checkEntireRepo = false;
const targetArgs = [];

// Parse arguments: --all checks entire repo, otherwise paths are checked
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
  '.js', '.cjs', '.mjs',
  '.json', '.jsonc', '.toml',
  '.yaml', '.yml',
  '.md', '.markdown',
  '.xml', '.uxml', '.uss',
  '.shader', '.hlsl', '.compute', '.cginc',
  '.asmdef', '.asmref', '.meta',
  '.ps1'
]);

/**
 * Recursively collect file paths under dir, excluding paths matching excludeRegexes.
 * @param {string} dir - Absolute path to the directory to walk.
 * @param {string[]} files - Accumulator array for collected file paths.
 * @returns {string[]} Array of absolute file paths.
 */
function walk(dir, files = []) {
  let entries;
  try {
    entries = fs.readdirSync(dir, { withFileTypes: true });
  } catch (err) {
    console.warn(`Warning: cannot read directory, skipping: ${dir} (${err.code || err.message})`);
    return files;
  }
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

/**
 * Resolve target paths (files or directories) to an array of absolute file paths.
 * Paths are resolved relative to the git repository root.
 * @param {string[]} targets - Array of relative or absolute paths (files or directories).
 * @returns {string[]} Array of absolute file paths, deduplicated and filtered by exclusions.
 */
function resolveTargets(targets) {
  if (!targets || targets.length === 0) {
    return [];
  }

  const seen = new Set();
  const resolved = [];

  for (const rawTarget of targets) {
    // Paths are resolved relative to git repo root (not cwd), matching git's behavior
    const targetPath = path.resolve(repoRoot, rawTarget);
    if (!fs.existsSync(targetPath)) {
      console.warn(`Warning: path does not exist, skipping: ${rawTarget} (resolved to: ${targetPath})`);
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

/**
 * Get list of staged files from git (Added, Copied, Modified, Renamed).
 * Returns paths relative to repoRoot, suitable for passing to resolveTargets().
 * @returns {string[]} Array of staged file paths relative to repo root.
 */
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

/**
 * Check for non-normalized line endings in the git index for tracked text files.
 * The repository should store LF even when working tree uses CRLF.
 * @param {string[]} files - Absolute file paths to check.
 * @returns {Array<{path: string, indexEol: string}>} Array of index EOL issues.
 */
function getIndexEolIssues(files) {
  if (!files || files.length === 0) {
    return [];
  }

  const relPaths = files.map((file) => path.relative(repoRoot, file));
  const result = spawnSync('git', ['ls-files', '--eol', '--', ...relPaths], {
    cwd: repoRoot,
    encoding: 'utf8'
  });

  if (result.status !== 0 || !result.stdout) {
    return [];
  }

  const issues = [];
  const lines = result.stdout.split(/\r?\n/);
  for (const line of lines) {
    if (!line) {
      continue;
    }
    const tabIndex = line.indexOf('\t');
    if (tabIndex < 0) {
      continue;
    }
    const meta = line.slice(0, tabIndex).trim();
    const filePath = line.slice(tabIndex + 1).trim();
    const ext = path.extname(filePath).toLowerCase();
    if (!exts.has(ext)) {
      continue;
    }

    // git ls-files --eol output format: i/[eol] w/[eol] attr/[attrs] [path]
    const parts = meta.split(/\s+/);
    const indexToken = parts.find((part) => part.startsWith('i/'));
    const attrToken = parts.find((part) => part.startsWith('attr/'));
    if (!indexToken) {
      continue;
    }
    if (attrToken === 'attr/-text') {
      continue;
    }

    if (indexToken !== 'i/lf' && indexToken !== 'i/none') {
      issues.push({ path: filePath, indexEol: indexToken });
    }
  }

  return issues;
}

/**
 * Check if a buffer starts with UTF-8 BOM (byte order mark).
 * @param {Buffer} buf - File content buffer.
 * @returns {boolean} True if BOM is present.
 */
function hasBom(buf) {
  return buf.length >= 3 && buf[0] === 0xef && buf[1] === 0xbb && buf[2] === 0xbf;
}

/**
 * Check if buffer contains non-CRLF line endings (bare LF or CR).
 * @param {Buffer} buf - File content buffer.
 * @returns {boolean} True if non-CRLF line endings are found.
 */
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

  // candidateFiles already contains absolute paths from walk() or resolveTargets()
  if (seenFiles.has(file)) {
    continue;
  }

  seenFiles.add(file);
  textFiles.push(file);
}

if (textFiles.length === 0) {
  console.log('EOL check skipped: no matching files to verify.');
  process.exit(0);
}

const bomFiles = [];
const badEolFiles = [];
const indexEolIssues = getIndexEolIssues(textFiles);

for (const file of textFiles) {
  let buf;
  try {
    buf = fs.readFileSync(file);
  } catch (err) {
    console.warn(`Warning: cannot read file, skipping: ${file} (${err.code || err.message})`);
    continue;
  }
  if (hasBom(buf)) bomFiles.push(path.relative(repoRoot, file));
  if (hasNonCrlfEol(buf)) badEolFiles.push(path.relative(repoRoot, file));
}

if (bomFiles.length === 0 && badEolFiles.length === 0 && indexEolIssues.length === 0) {
  console.log(`EOL check passed: ${textFiles.length} file(s) verified, CRLF only and no BOMs detected.`);
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
if (indexEolIssues.length) {
  console.log('Git index contains non-normalized line endings (expected LF in repo for text files):');
  for (const issue of indexEolIssues) {
    console.log(`  ${issue.path} (${issue.indexEol})`);
  }
  console.log('Fix: git add --renormalize .');
}
console.error('EOL/BOM policy violations detected.');
process.exit(1);
