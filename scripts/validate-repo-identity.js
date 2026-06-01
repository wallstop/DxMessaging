#!/usr/bin/env node
/**
 * @fileoverview Validates repository identity references.
 */

"use strict";

const { execFileSync } = require("child_process");
const fs = require("fs");
const path = require("path");
const { normalizeToLf } = require("./lib/quote-parser");

const EXPECTED_REPOSITORY = "Ambiguous-Interactive/DxMessaging";
const ALLOWED_PACKAGE_ID = "com.wallstop-studios.dxmessaging";

const repoRoot = path.resolve(__dirname, "..");

const staleIdentityPatterns = [
  {
    pattern: /https?:\/\/github\.com\/wallstop\/DxMessaging(?:[/?#][^\s"'<>)]*)?/g,
    label: "stale GitHub URL",
    replacement: `https://github.com/${EXPECTED_REPOSITORY}`
  },
  {
    pattern: /https?:\/\/wallstop\.github\.io\/DxMessaging(?:\/[^\s"'<>)]*)?/g,
    label: "stale documentation URL",
    replacement: "https://ambiguous-interactive.github.io/DxMessaging/"
  },
  {
    pattern:
      /github\.repository\s*(?:==|!=)\s*['"](?:wallstop\/DxMessaging|wallstop-studios\/com\.wallstop-studios\.dxmessaging)['"]/g,
    label: "stale github.repository guard",
    replacement: `github.repository == '${EXPECTED_REPOSITORY}'`
  },
  {
    pattern: /\bwallstop-studios\/com\.wallstop-studios\.dxmessaging\b/g,
    label: "stale repository slug",
    replacement: EXPECTED_REPOSITORY
  },
  {
    pattern: /\bwallstop\/DxMessaging\b/g,
    label: "stale repository slug",
    replacement: EXPECTED_REPOSITORY
  }
];

function getTrackedFiles(execFileSyncImpl = execFileSync) {
  const output = execFileSyncImpl("git", ["ls-files"], {
    cwd: repoRoot,
    encoding: "utf8",
    stdio: ["ignore", "pipe", "pipe"]
  });

  return normalizeToLf(output)
    .split("\n")
    .map((line) => line.trim())
    .filter((line) => line.length > 0);
}

function parseGitFileList(output) {
  return normalizeToLf(output)
    .split("\n")
    .map((line) => line.trim())
    .filter((line) => line.length > 0);
}

function getRepositoryCandidateFiles(execFileSyncImpl = execFileSync) {
  const trackedFiles = parseGitFileList(
    execFileSyncImpl("git", ["ls-files"], {
      cwd: repoRoot,
      encoding: "utf8",
      stdio: ["ignore", "pipe", "pipe"]
    })
  );
  const stagedFiles = parseGitFileList(
    execFileSyncImpl("git", ["diff", "--cached", "--name-only", "--diff-filter=ACMR"], {
      cwd: repoRoot,
      encoding: "utf8",
      stdio: ["ignore", "pipe", "pipe"]
    })
  );
  const untrackedFiles = parseGitFileList(
    execFileSyncImpl("git", ["ls-files", "--others", "--exclude-standard"], {
      cwd: repoRoot,
      encoding: "utf8",
      stdio: ["ignore", "pipe", "pipe"]
    })
  );

  return [...new Set([...trackedFiles, ...stagedFiles, ...untrackedFiles])].sort();
}

function isTextContent(content) {
  return !content.includes("\u0000");
}

function findStaleIdentityReferencesInContent(content, filePath) {
  const errors = [];
  const normalizedContent = normalizeToLf(content);
  const lines = normalizedContent.split("\n");

  for (let lineIndex = 0; lineIndex < lines.length; lineIndex++) {
    const line = lines[lineIndex];
    const reportedRanges = [];

    for (const stalePattern of staleIdentityPatterns) {
      stalePattern.pattern.lastIndex = 0;

      for (const match of line.matchAll(stalePattern.pattern)) {
        const value = match[0];
        const start = match.index;
        const end = start + value.length;
        const overlapsReportedRange = reportedRanges.some(
          (range) => start < range.end && end > range.start
        );

        if (overlapsReportedRange) {
          continue;
        }

        // The package id is a valid Unity/OpenUPM identifier and must not be treated as repo identity.
        if (value === ALLOWED_PACKAGE_ID) {
          continue;
        }

        reportedRanges.push({ start, end });

        errors.push({
          type: "stale-repository-identity",
          file: filePath,
          line: lineIndex + 1,
          value,
          message:
            `${filePath}:${lineIndex + 1} contains ${stalePattern.label} '${value}'. ` +
            `Use '${stalePattern.replacement}' for repository identity.`
        });
      }
    }

    if (filePath === ".github/dependabot.yml" && /^\s*-\s*wallstop\s*$/.test(line)) {
      errors.push({
        type: "stale-dependabot-routing",
        file: filePath,
        line: lineIndex + 1,
        value: line.trim(),
        message:
          `${filePath}:${lineIndex + 1} routes Dependabot ownership to '${line.trim()}'. ` +
          "Remove the stale owner or replace it with Ambiguous-owned routing."
      });
    }
  }

  return errors;
}

function findStaleIdentityReferences(filePaths, options = {}) {
  const errors = [];
  const readFileSyncImpl = options.readFileSync || fs.readFileSync;

  for (const filePath of filePaths) {
    const absolutePath = path.resolve(repoRoot, filePath);
    let content;

    try {
      content = readFileSyncImpl(absolutePath, "utf8");
    } catch (error) {
      errors.push({
        type: "unreadable-file",
        file: filePath,
        line: 0,
        value: "",
        message: `${filePath}: unable to read file: ${error.message}`
      });
      continue;
    }

    if (!isTextContent(content)) {
      continue;
    }

    errors.push(...findStaleIdentityReferencesInContent(content, filePath));
  }

  return errors;
}

function validateRepoIdentity(options = {}) {
  const files = options.files || getRepositoryCandidateFiles(options.execFileSync);
  const errors = findStaleIdentityReferences(files, options);

  if (errors.length === 0) {
    console.log(`Repository identity validation passed for ${EXPECTED_REPOSITORY}.`);
    return { valid: true, errors: [] };
  }

  console.error(
    `Repository identity validation failed: found ${errors.length} stale reference(s).`
  );
  for (const error of errors) {
    console.error(`  - ${error.message}`);
  }

  return { valid: false, errors };
}

if (require.main === module) {
  const args = process.argv.slice(2);

  try {
    const result = validateRepoIdentity({ check: args.includes("--check") });
    if (!result.valid) {
      process.exitCode = 1;
    }
  } catch (error) {
    console.error("Repository identity validation failed:", error.message);
    process.exit(1);
  }
}

module.exports = {
  ALLOWED_PACKAGE_ID,
  EXPECTED_REPOSITORY,
  findStaleIdentityReferences,
  findStaleIdentityReferencesInContent,
  getRepositoryCandidateFiles,
  getTrackedFiles,
  parseGitFileList,
  validateRepoIdentity
};
