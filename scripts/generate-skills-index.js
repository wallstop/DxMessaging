#!/usr/bin/env node
/**
 * generate-skills-index.js
 *
 * Auto-generates index.md from all skill files in .llm/skills/.
 * Scans for markdown files with YAML frontmatter and produces a categorized,
 * searchable index with complexity badges, status indicators, and tag clouds.
 *
 * @usage
 *   node scripts/generate-skills-index.js
 *   node scripts/generate-skills-index.js --check
 *
 * @exitcodes
 *   0 - Success, index.md generated successfully
 *   1 - Error occurred during generation (e.g., file access issues)
 *
 * @example
 *   # Run from repository root
 *   node scripts/generate-skills-index.js
 *
 *   # Run from any directory
 *   node /path/to/repo/scripts/generate-skills-index.js
 */

const fs = require("fs");
const path = require("path");
const { spawnSync } = require("child_process");

const SKILLS_DIR = path.join(__dirname, "..", ".llm", "skills");
const INDEX_PATH = path.join(SKILLS_DIR, "index.md");
const EXCLUDED_FILES = ["index.md", "specification.md"];
const EXCLUDED_DIRS = ["templates"];
const REPO_ROOT = path.join(__dirname, "..");
const PRETTIER_VERSION = "3.8.1";

function normalizeToCrlf(text) {
    let normalized = text.replace(/\r\n/g, "\n");
    normalized = normalized.replace(/\r/g, "\n");
    return normalized.replace(/\n/g, "\r\n");
}

function getLatestSkillDate(skills) {
    const dates = skills
        .map((skill) => skill.updated || skill.created)
        .filter((date) => typeof date === "string" && /^\d{4}-\d{2}-\d{2}$/.test(date))
        .sort();

    if (dates.length > 0) {
        return dates[dates.length - 1];
    }

    return new Date().toISOString().split("T")[0];
}

function formatWithPrettier(content) {
    const prettierArgs = [
        "--yes",
        `prettier@${PRETTIER_VERSION}`,
        "--stdin-filepath",
        INDEX_PATH,
    ];

    const isWindows = process.platform === "win32";
    const command = isWindows ? "cmd.exe" : "npx";
    const args = isWindows ? ["/d", "/s", "/c", "npx", ...prettierArgs] : prettierArgs;

    const result = spawnSync(command, args, {
        cwd: REPO_ROOT,
        input: content,
        encoding: "utf8",
    });

    if (result.error) {
        throw result.error;
    }

    if (result.status !== 0) {
        const details = result.stderr ? result.stderr.trim() : "Unknown error";
        throw new Error(`Prettier failed: ${details}`);
    }

    return result.stdout;
}

/**
 * Parse YAML frontmatter from markdown file content.
 * Uses a stack-based approach to handle arbitrary nesting depth.
 * Returns null if no valid frontmatter found.
 */
function parseFrontmatter(content) {
    const match = content.match(/^---\r?\n([\s\S]*?)\r?\n---/);
    if (!match) {
        return null;
    }

    const yaml = match[1];
    const result = {};
    const lines = yaml.split(/\r?\n/);

    // Stack-based parser for arbitrary nesting depth
    // Each stack entry: { obj, key, indent, isArray }
    const contextStack = [{ obj: result, key: null, indent: -1, isArray: false }];

    for (let i = 0; i < lines.length; i++) {
        const line = lines[i];

        // Skip empty lines and comments
        if (!line.trim() || line.trim().startsWith("#")) {
            continue;
        }

        const trimmed = line.trim();
        const indent = line.search(/\S/);

        // Pop stack until we find the right context level
        while (contextStack.length > 1 && contextStack[contextStack.length - 1].indent >= indent) {
            contextStack.pop();
        }

        const currentContext = contextStack[contextStack.length - 1];

        // Handle array items
        if (trimmed.startsWith("- ")) {
            const arrayValue = trimmed.slice(2).trim();

            // Check if this is an object item (has nested content or is "key: value")
            const colonIndex = arrayValue.indexOf(":");
            if (colonIndex > 0 && !arrayValue.startsWith('"') && !arrayValue.startsWith("'")) {
                // Array of objects - parse as key: value
                const itemKey = arrayValue.slice(0, colonIndex).trim();
                let itemValue = arrayValue.slice(colonIndex + 1).trim().replace(/^["']|["']$/g, "");

                // Check if we need to create a new object in the array
                if (currentContext.isArray) {
                    // Create new object and add to array
                    const newObj = {};
                    newObj[itemKey] = itemValue;
                    currentContext.obj.push(newObj);
                    // Push this object onto the stack for potential nested properties
                    contextStack.push({ obj: newObj, key: itemKey, indent: indent + 2, isArray: false });
                }
            } else {
                // Simple array value
                const value = arrayValue.replace(/^["']|["']$/g, "");
                if (currentContext.isArray) {
                    currentContext.obj.push(value);
                }
            }
            continue;
        }

        // Handle key: value pairs
        const colonIndex = trimmed.indexOf(":");
        if (colonIndex === -1) {
            continue;
        }

        const key = trimmed.slice(0, colonIndex).trim();
        let value = trimmed.slice(colonIndex + 1).trim();

        if (value === "" || value === "[]") {
            // Check if next line starts an array or is nested
            const nextLine = i < lines.length - 1 ? lines[i + 1] : "";
            const nextTrimmed = nextLine.trim();

            if (nextTrimmed.startsWith("- ")) {
                // This key starts an array
                currentContext.obj[key] = [];
                contextStack.push({ obj: currentContext.obj[key], key: key, indent: indent, isArray: true });
            } else if (nextLine && nextLine.search(/\S/) > indent) {
                // This key starts a nested object
                currentContext.obj[key] = {};
                contextStack.push({ obj: currentContext.obj[key], key: key, indent: indent, isArray: false });
            } else {
                // Empty value
                currentContext.obj[key] = value === "[]" ? [] : "";
            }
        } else {
            // Simple key: value
            value = value.replace(/^["']|["']$/g, "");
            currentContext.obj[key] = value;
        }
    }

    return result;
}

/**
 * Recursively find all skill files.
 */
function findSkillFiles(dir, baseDir = dir) {
    const skills = [];

    if (!fs.existsSync(dir)) {
        return skills;
    }

    const entries = fs.readdirSync(dir, { withFileTypes: true });

    for (const entry of entries) {
        const fullPath = path.join(dir, entry.name);

        if (entry.isDirectory()) {
            if (!EXCLUDED_DIRS.includes(entry.name)) {
                skills.push(...findSkillFiles(fullPath, baseDir));
            }
        } else if (entry.isFile() && entry.name.endsWith(".md")) {
            if (!EXCLUDED_FILES.includes(entry.name)) {
                const relativePath = path.relative(baseDir, fullPath);
                const category = path.dirname(relativePath);

                if (category !== ".") {
                    skills.push({
                        path: fullPath,
                        relativePath: relativePath,
                        category: category,
                        filename: entry.name,
                    });
                }
            }
        }
    }

    return skills;
}

/**
 * Load skill metadata from file.
 * Returns null if file cannot be read or has no valid frontmatter.
 */
function loadSkill(skillFile) {
    let content;
    try {
        content = fs.readFileSync(skillFile.path, "utf8");
    } catch (error) {
        console.error(`Error reading ${skillFile.relativePath}: ${error.message}`);
        return null;
    }

    const lineCount = content.split(/\r?\n/).length;
    const frontmatter = parseFrontmatter(content);

    if (!frontmatter) {
        console.warn(`Warning: No frontmatter in ${skillFile.relativePath}`);
        return null;
    }

    return {
        ...skillFile,
        ...frontmatter,
        lineCount: lineCount,
    };
}

/**
 * Get complexity badge.
 */
function getComplexityBadge(level) {
    const badges = {
        basic: "🟢 Basic",
        intermediate: "🟡 Intermediate",
        advanced: "🟠 Advanced",
        expert: "🔴 Expert",
    };
    return badges[level] || level;
}

/**
 * Get status badge.
 */
function getStatusBadge(status) {
    const badges = {
        draft: "📝 Draft",
        review: "👀 Review",
        stable: "✅ Stable",
        deprecated: "⚠️ Deprecated",
    };
    return badges[status] || status;
}

/**
 * Get impact indicator.
 */
function getImpactIndicator(rating) {
    const indicators = {
        none: "○○○○○",
        low: "●○○○○",
        medium: "●●○○○",
        high: "●●●○○",
        critical: "●●●●●",
    };
    return indicators[rating] || "?";
}

/**
 * Get line size indicator based on file size limits.
 * < 200: 📝 (short)
 * 200-350: ✅ (ideal)
 * 351-500: ⚠️ (warning)
 * > 500: ❌ (error)
 */
function getLineSizeIndicator(lineCount) {
    if (typeof lineCount !== "number") {
        return "?";
    }
    if (lineCount > 500) {
        return "❌";
    }
    if (lineCount > 350) {
        return "⚠️";
    }
    if (lineCount >= 200) {
        return "✅";
    }
    return "📝";
}

/**
 * Generate the index content.
 */
function generateIndex(skills) {
    const indexDate = getLatestSkillDate(skills);

    // Group by category
    const byCategory = {};
    for (const skill of skills) {
        const cat = skill.category || "uncategorized";
        if (!byCategory[cat]) {
            byCategory[cat] = [];
        }
        byCategory[cat].push(skill);
    }

    // Sort categories and skills within
    const sortedCategories = Object.keys(byCategory).sort();
    for (const cat of sortedCategories) {
        byCategory[cat].sort((a, b) => (a.title || "").localeCompare(b.title || ""));
    }

    // Collect all tags
    const tagCounts = {};
    for (const skill of skills) {
        const tags = Array.isArray(skill.tags) ? skill.tags : [];
        for (const tag of tags) {
            tagCounts[tag] = (tagCounts[tag] || 0) + 1;
        }
    }

    // Build index
    let content = `# Skills Index

> **Auto-generated** on ${indexDate}. Do not edit manually.
> Run \`node scripts/generate-skills-index.js\` to regenerate.

---

## Summary

| Metric | Value |
|--------|-------|
| Total Skills | ${skills.length} |
| Categories | ${sortedCategories.length} |
| Unique Tags | ${Object.keys(tagCounts).length} |

---

## Table of Contents

`;

    for (const cat of sortedCategories) {
        const catTitle = cat.charAt(0).toUpperCase() + cat.slice(1).replace(/-/g, " ");
        content += `- [${catTitle}](#${cat}) (${byCategory[cat].length})\n`;
    }

    content += `- [Tag Cloud](#tag-cloud)\n`;
    content += `- [All Skills by Complexity](#all-skills-by-complexity)\n`;

    content += `\n---\n\n`;

    // Category sections
    for (const cat of sortedCategories) {
        const catTitle = cat.charAt(0).toUpperCase() + cat.slice(1).replace(/-/g, " ");
        content += `## ${catTitle}\n\n`;

        content += `| Skill | Lines | Complexity | Status | Performance | Tags |\n`;
        content += `|-------|-------|------------|--------|-------------|------|\n`;

        for (const skill of byCategory[cat]) {
            const title = skill.title || skill.filename;
            const link = `./${skill.relativePath.replace(/\\/g, "/")}`;
            const lineCount = skill.lineCount || "?";
            const lineIndicator = getLineSizeIndicator(skill.lineCount);
            const complexity = skill.complexity?.level
                ? getComplexityBadge(skill.complexity.level)
                : "?";
            const status = skill.status ? getStatusBadge(skill.status) : "?";
            const perfImpact = skill.impact?.performance?.rating
                ? getImpactIndicator(skill.impact.performance.rating)
                : "?";
            const tags = Array.isArray(skill.tags) ? skill.tags.slice(0, 3).join(", ") : "";

            content += `| [${title}](${link}) | ${lineIndicator} ${lineCount} | ${complexity} | ${status} | ${perfImpact} | ${tags} |\n`;
        }

        content += `\n`;
    }

    // Tag cloud
    content += `---\n\n## Tag Cloud\n\n`;

    const sortedTags = Object.entries(tagCounts).sort((a, b) => {
        if (b[1] !== a[1]) {
            return b[1] - a[1];
        }
        return a[0].localeCompare(b[0]);
    });

    for (const [tag, count] of sortedTags) {
        content += `\`${tag}\`×${count} `;
    }

    content += `\n\n`;

    // All skills by complexity
    content += `---\n\n## All Skills by Complexity\n\n`;

    const byComplexity = { basic: [], intermediate: [], advanced: [], expert: [] };
    for (const skill of skills) {
        const level = skill.complexity?.level || "intermediate";
        if (byComplexity[level]) {
            byComplexity[level].push(skill);
        }
    }

    for (const level of ["basic", "intermediate", "advanced", "expert"]) {
        if (byComplexity[level].length === 0) {
            continue;
        }

        content += `### ${getComplexityBadge(level)}\n\n`;

        const sortedSkills = byComplexity[level].slice().sort((a, b) => {
            return (a.title || "").localeCompare(b.title || "");
        });

        for (const skill of sortedSkills) {
            const title = skill.title || skill.filename;
            const link = `./${skill.relativePath.replace(/\\/g, "/")}`;
            content += `- [${title}](${link}) _(${skill.category})_\n`;
        }

        content += `\n`;
    }

    content += `---\n\n_Generated by \`scripts/generate-skills-index.js\`_\n`;

    return content;
}

/**
 * Main entry point.
 */
function main() {
    const args = process.argv.slice(2);
    const checkOnly = args.includes("--check");

    console.log("Scanning for skills in", SKILLS_DIR);

    const skillFiles = findSkillFiles(SKILLS_DIR);
    console.log(`Found ${skillFiles.length} skill files`);

    const skills = skillFiles.map(loadSkill).filter((s) => s !== null);
    console.log(`Loaded ${skills.length} skills with valid frontmatter`);

    const indexContent = generateIndex(skills);
    let formattedContent;

    try {
        formattedContent = formatWithPrettier(indexContent);
    } catch (error) {
        console.error(`Failed to format index with Prettier: ${error.message}`);
        return 1;
    }

    const normalizedContent = normalizeToCrlf(formattedContent);

    if (checkOnly) {
        let existingContent = null;
        try {
            existingContent = fs.readFileSync(INDEX_PATH, "utf8");
        } catch (error) {
            console.error(`Unable to read ${INDEX_PATH}: ${error.message}`);
            return 1;
        }

        if (existingContent !== normalizedContent) {
            console.error("index.md is out of date. Run node scripts/generate-skills-index.js");
            return 1;
        }

        console.log("Skills index is up to date.");
        return 0;
    }

    fs.writeFileSync(INDEX_PATH, normalizedContent, "utf8");
    console.log(`Generated ${INDEX_PATH}`);

    return 0;
}

process.exit(main());
