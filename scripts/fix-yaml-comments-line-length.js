#!/usr/bin/env node
"use strict";

const fs = require("fs");
const path = require("path");
const { execFileSync } = require("child_process");
const { normalizeToLf } = require("./lib/quote-parser");

const DEFAULT_MAX_LINE_LENGTH = 200;
const DEFAULT_POLICY = Object.freeze({
  max: DEFAULT_MAX_LINE_LENGTH,
  allowNonBreakableWords: true,
  allowNonBreakableInlineMappings: false
});

function parseYamlBoolean(rawValue) {
  if (typeof rawValue !== "string") {
    return null;
  }

  const normalized = rawValue.trim().toLowerCase();
  if (normalized === "true") {
    return true;
  }
  if (normalized === "false") {
    return false;
  }

  return null;
}

function getIndent(line) {
  return line.length - line.trimStart().length;
}

function resolveYamlLineLengthPolicy(configPath) {
  const policy = {
    max: DEFAULT_POLICY.max,
    allowNonBreakableWords: DEFAULT_POLICY.allowNonBreakableWords,
    allowNonBreakableInlineMappings: DEFAULT_POLICY.allowNonBreakableInlineMappings
  };

  let content;
  try {
    content = fs.readFileSync(configPath, "utf8");
  } catch (_error) {
    return policy;
  }

  const lines = normalizeToLf(content).split("\n");
  let inLineLengthBlock = false;
  let blockIndent = -1;

  for (const line of lines) {
    const trimmed = line.trim();

    if (!inLineLengthBlock) {
      if (/^\s*line-length:\s*(?:#.*)?$/.test(line)) {
        inLineLengthBlock = true;
        blockIndent = getIndent(line);
      }
      continue;
    }

    if (trimmed.length === 0 || trimmed.startsWith("#")) {
      continue;
    }

    const indent = getIndent(line);
    if (indent <= blockIndent) {
      break;
    }

    const maxMatch = /^\s*max:\s*([0-9]+)\s*(?:#.*)?$/.exec(line);
    if (maxMatch) {
      const parsedMax = Number.parseInt(maxMatch[1], 10);
      if (Number.isFinite(parsedMax) && parsedMax > 0) {
        policy.max = parsedMax;
      }
      continue;
    }

    const allowWordsMatch = /^\s*allow-non-breakable-words:\s*([^#]+?)\s*(?:#.*)?$/.exec(line);
    if (allowWordsMatch) {
      const parsedBoolean = parseYamlBoolean(allowWordsMatch[1]);
      if (parsedBoolean !== null) {
        policy.allowNonBreakableWords = parsedBoolean;
      }
      continue;
    }

    const allowInlineMappingsMatch =
      /^\s*allow-non-breakable-inline-mappings:\s*([^#]+?)\s*(?:#.*)?$/.exec(line);
    if (!allowInlineMappingsMatch) {
      continue;
    }

    const parsedBoolean = parseYamlBoolean(allowInlineMappingsMatch[1]);
    if (parsedBoolean !== null) {
      policy.allowNonBreakableInlineMappings = parsedBoolean;
    }
  }

  if (policy.allowNonBreakableInlineMappings) {
    policy.allowNonBreakableWords = true;
  }

  return policy;
}

function splitWords(text) {
  return text
    .split(/\s+/)
    .map((word) => word.trim())
    .filter((word) => word.length > 0);
}

function wrapCommentLine(line, maxLength, options = {}) {
  if (line.length <= maxLength) {
    return [line];
  }

  const commentMatch = /^(\s*#\s?)(.*)$/.exec(line);
  if (!commentMatch) {
    return [line];
  }

  const prefix = commentMatch[1];
  const commentText = commentMatch[2].trim();
  if (commentText.length === 0) {
    return [line];
  }

  const available = maxLength - prefix.length;
  if (available <= 0) {
    return [line];
  }

  const words = splitWords(commentText);
  if (words.length === 0) {
    return [line];
  }

  if (options.allowNonBreakableWords === true && words.some((word) => word.length > available)) {
    return [line];
  }

  const wrapped = [];
  let current = "";

  for (const word of words) {
    if (current.length === 0) {
      current = word;
      continue;
    }

    if (current.length + 1 + word.length <= available) {
      current += ` ${word}`;
      continue;
    }

    wrapped.push(`${prefix}${current}`);
    current = word;
  }

  if (current.length > 0) {
    wrapped.push(`${prefix}${current}`);
  }

  if (wrapped.length === 0) {
    return [line];
  }

  return wrapped;
}

function rewriteYamlCommentLines(content, policy) {
  const normalized = normalizeToLf(content);
  const lines = normalized.split("\n");
  const rewritten = [];
  const changedLines = [];

  for (let index = 0; index < lines.length; index++) {
    const line = lines[index];
    const wrapped = wrapCommentLine(line, policy.max, {
      allowNonBreakableWords: policy.allowNonBreakableWords
    });

    rewritten.push(...wrapped);

    if (wrapped.length !== 1 || wrapped[0] !== line) {
      changedLines.push(index + 1);
    }
  }

  return {
    content: rewritten.join("\n"),
    changedLines
  };
}

function looksLikeYamlPath(candidate) {
  return /\.ya?ml$/i.test(candidate);
}

function resolveRepoRoot(cwd = process.cwd()) {
  return cwd;
}

function getAllTrackedYamlFiles(repoRoot) {
  const output = execFileSync("git", ["ls-files", "--", "*.yml", "*.yaml"], {
    cwd: repoRoot,
    encoding: "utf8"
  });

  return output
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => line.length > 0)
    .map((line) => path.resolve(repoRoot, line));
}

function parseArgs(argv) {
  const options = {
    check: false,
    allFiles: false,
    files: []
  };

  for (const arg of argv) {
    if (arg === "--check") {
      options.check = true;
      continue;
    }

    if (arg === "--all-files") {
      options.allFiles = true;
      continue;
    }

    options.files.push(arg);
  }

  return options;
}

function uniqueExistingYamlFiles(files) {
  const unique = new Set();

  for (const file of files) {
    if (!looksLikeYamlPath(file)) {
      continue;
    }

    const absolute = path.resolve(file);
    if (!fs.existsSync(absolute)) {
      continue;
    }

    unique.add(absolute);
  }

  return Array.from(unique.values());
}

function processFiles(files, options = {}) {
  const repoRoot = options.repoRoot || resolveRepoRoot();
  const configPath = path.join(repoRoot, ".yamllint.yaml");
  const policy = resolveYamlLineLengthPolicy(configPath);

  const changedFiles = [];
  const violations = [];

  for (const filePath of files) {
    const original = fs.readFileSync(filePath, "utf8");
    const rewritten = rewriteYamlCommentLines(original, policy);

    if (rewritten.changedLines.length === 0) {
      continue;
    }

    if (options.check === true) {
      violations.push({
        filePath,
        changedLines: rewritten.changedLines
      });
      continue;
    }

    fs.writeFileSync(filePath, rewritten.content, "utf8");
    changedFiles.push({
      filePath,
      changedLines: rewritten.changedLines
    });
  }

  return {
    changedFiles,
    violations,
    policy
  };
}

function main(argv = process.argv.slice(2)) {
  const args = parseArgs(argv);
  const repoRoot = resolveRepoRoot();

  let candidateFiles = args.files.map((filePath) => path.resolve(repoRoot, filePath));
  if (args.allFiles) {
    try {
      candidateFiles = [...candidateFiles, ...getAllTrackedYamlFiles(repoRoot)];
    } catch (error) {
      process.stderr.write(
        `fix-yaml-comments-line-length: unable to list tracked YAML files via git: ${error.message}\n`
      );
      return 1;
    }
  }

  const files = uniqueExistingYamlFiles(candidateFiles);
  if (files.length === 0) {
    return 0;
  }

  const result = processFiles(files, {
    check: args.check,
    repoRoot
  });

  if (args.check) {
    if (result.violations.length === 0) {
      return 0;
    }

    process.stderr.write(
      `YAML comment line-length check failed (${result.violations.length} file(s)).\n`
    );
    for (const violation of result.violations) {
      const relative = path.relative(repoRoot, violation.filePath).replace(/\\/g, "/");
      process.stderr.write(`  - ${relative}: lines ${violation.changedLines.join(", ")}\n`);
    }
    process.stderr.write(`Run: node scripts/fix-yaml-comments-line-length.js --all-files\n`);
    return 1;
  }

  if (result.changedFiles.length === 0) {
    return 0;
  }

  for (const changed of result.changedFiles) {
    const relative = path.relative(repoRoot, changed.filePath).replace(/\\/g, "/");
    process.stdout.write(
      `Wrapped YAML comments in ${relative} (lines ${changed.changedLines.join(", ")}).\n`
    );
  }

  return 0;
}

module.exports = {
  DEFAULT_MAX_LINE_LENGTH,
  parseYamlBoolean,
  resolveYamlLineLengthPolicy,
  splitWords,
  wrapCommentLine,
  rewriteYamlCommentLines,
  parseArgs,
  uniqueExistingYamlFiles,
  getAllTrackedYamlFiles,
  processFiles,
  main
};

if (require.main === module) {
  process.exit(main());
}
