#!/usr/bin/env node
// Safe auto-fix for markdownlint MD036 (no-emphasis-as-heading)
// Converts isolated paragraphs that are just **text** or __text__ into level-3 headings.
// - Skips code fences
// - Requires blank line before and after (or start/end of file)

const fs = require('fs');
const path = require('path');

const files = process.argv.slice(2);
if (files.length === 0) {
  console.error('Usage: node scripts/fix-md036-headings.js <file1.md> [file2.md] ...');
  process.exit(1);
}

for (const rel of files) {
  const filePath = path.resolve(process.cwd(), rel);
  let src;
  try {
    src = fs.readFileSync(filePath, 'utf8');
  } catch (e) {
    console.error(`Skipping ${rel}: ${e.message}`);
    continue;
  }

  const lines = src.replace(/\r\n/g, '\n').replace(/\r/g, '\n').split('\n');
  let inFence = false;
  let lastHeadingLevel = 0;
  let changed = false;

  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];
    const trimmed = line.trim();

    // Track fenced code blocks (``` or ~~~)
    if (/^(```|~~~)/.test(trimmed)) {
      inFence = !inFence;
      continue;
    }
    if (inFence) continue;

    // Track last heading level
    const headingMatch = trimmed.match(/^(#{1,6})\s/);
    if (headingMatch) {
      lastHeadingLevel = headingMatch[1].length;
      continue;
    }

    const prev = i > 0 ? lines[i - 1].trim() : '';
    const next = i < lines.length - 1 ? lines[i + 1].trim() : '';
    const isIsolated = (prev === '' || i === 0) && (next === '' || i === lines.length - 1);

    // Match **text** or __text__ (no extra characters on line)
    const m = trimmed.match(/^(\*\*|__)([^*_].*?)(\1)$/);
    if (isIsolated && m) {
      const text = m[2].trim();
      if (text.length > 0) {
        const level = Math.min((lastHeadingLevel || 2) + 1, 6);
        const hashes = '#'.repeat(level);
        lines[i] = `${hashes} ${text}`;
        lastHeadingLevel = level;
        changed = true;
      }
    }
  }

  if (changed) {
    fs.writeFileSync(filePath, lines.join('\n'));
    console.log(`MD036 fixed: ${path.relative(process.cwd(), filePath)}`);
  }
}
