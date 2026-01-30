#!/usr/bin/env node
/**
 * validate-skills.js
 *
 * Validates all skill files in .llm/skills/ for schema compliance and file size limits.
 * Checks YAML frontmatter for required fields, validates field values against allowed
 * enums, and enforces file size limits to ensure effective LLM context usage.
 *
 * @usage
 *   node scripts/validate-skills.js
 *
 * @exitcodes
 *   0 - Success (no errors; warnings may be present)
 *   1 - Validation failed (one or more errors found)
 *
 * @example
 *   # Run from repository root
 *   node scripts/validate-skills.js
 *
 *   # Run in CI pipeline
 *   node scripts/validate-skills.js || exit 1
 *
 *   # Run from any directory
 *   node /path/to/repo/scripts/validate-skills.js
 */

const fs = require('fs');
const path = require('path');

const SKILLS_DIR = path.join(__dirname, '..', '.llm', 'skills');
const CONTEXT_FILE = path.join(__dirname, '..', '.llm', 'context.md');
const EXCLUDED_FILES = ['index.md', 'specification.md'];
const EXCLUDED_DIRS = ['templates'];

// Files excluded from "short file" informational messages
const SHORT_FILE_EXCLUDES = ['context.md'];

// SYNC: Keep in sync with validate-skills-required-fields.test.js REQUIRED_FIELDS
const REQUIRED_FIELDS = ['title', 'id', 'category', 'version', 'created', 'updated', 'status'];

// File size limits (in lines)
const LINE_LIMIT_IDEAL_MIN = 200;
const LINE_LIMIT_IDEAL_MAX = 350;
const LINE_LIMIT_HARD_MAX = 500;

const VALID_CATEGORIES = [
    'performance',
    'testing',
    'solid',
    'messaging',
    'unity',
    'concurrency',
    'architecture',
    'error-handling',
    'code-generation',
    'documentation',
    'scripting',
    'github-actions',
];

const VALID_COMPLEXITY_LEVELS = ['basic', 'intermediate', 'advanced', 'expert'];
const VALID_STATUSES = ['draft', 'review', 'stable', 'deprecated'];
const VALID_IMPACT_RATINGS = ['none', 'low', 'medium', 'high', 'critical'];
const VALID_IMPACT_TYPES = ['performance', 'maintainability', 'testability'];

class ValidationError {
    constructor(file, field, message) {
        this.file = file;
        this.field = field;
        this.message = message;
    }

    toString() {
        return `[${this.file}] ${this.field}: ${this.message}`;
    }
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
        if (!line.trim() || line.trim().startsWith('#')) {
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
        if (trimmed.startsWith('- ')) {
            const arrayValue = trimmed.slice(2).trim();

            // Check if this is an object item (has nested content or is "key: value")
            const colonIndex = arrayValue.indexOf(':');
            if (colonIndex > 0 && !arrayValue.startsWith('"') && !arrayValue.startsWith("'")) {
                // Array of objects - parse as key: value
                const itemKey = arrayValue.slice(0, colonIndex).trim();
                let itemValue = arrayValue.slice(colonIndex + 1).trim().replace(/^["']|["']$/g, '');

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
                const value = arrayValue.replace(/^["']|["']$/g, '');
                if (currentContext.isArray) {
                    currentContext.obj.push(value);
                }
            }
            continue;
        }

        // Handle key: value pairs
        const colonIndex = trimmed.indexOf(':');
        if (colonIndex === -1) {
            continue;
        }

        const key = trimmed.slice(0, colonIndex).trim();
        let value = trimmed.slice(colonIndex + 1).trim();

        if (value === '' || value === '[]') {
            // Check if next line starts an array or is nested
            const nextLine = i < lines.length - 1 ? lines[i + 1] : '';
            const nextTrimmed = nextLine.trim();

            if (nextTrimmed.startsWith('- ')) {
                // This key starts an array
                currentContext.obj[key] = [];
                contextStack.push({ obj: currentContext.obj[key], key: key, indent: indent, isArray: true });
            } else if (nextLine && nextLine.search(/\S/) > indent) {
                // This key starts a nested object
                currentContext.obj[key] = {};
                contextStack.push({ obj: currentContext.obj[key], key: key, indent: indent, isArray: false });
            } else {
                // Empty value
                currentContext.obj[key] = value === '[]' ? [] : '';
            }
        } else {
            // Simple key: value
            value = value.replace(/^["']|["']$/g, '');
            currentContext.obj[key] = value;
        }
    }

    return result;
}

/**
 * Find all skill files recursively.
 */
function findSkillFiles(dir) {
    const skills = [];

    if (!fs.existsSync(dir)) {
        return skills;
    }

    const entries = fs.readdirSync(dir, { withFileTypes: true });

    for (const entry of entries) {
        const fullPath = path.join(dir, entry.name);

        if (entry.isDirectory()) {
            if (!EXCLUDED_DIRS.includes(entry.name)) {
                skills.push(...findSkillFiles(fullPath));
            }
        } else if (entry.isFile() && entry.name.endsWith('.md')) {
            if (!EXCLUDED_FILES.includes(entry.name)) {
                const relativePath = path.relative(SKILLS_DIR, fullPath);
                const category = path.dirname(relativePath);

                if (category !== '.') {
                    skills.push({
                        path: fullPath,
                        relativePath: relativePath,
                        category: category,
                        filename: entry.name,
                        expectedId: entry.name.replace(/\.md$/, ''),
                    });
                }
            }
        }
    }

    return skills;
}

/**
 * Validate a single skill file.
 * Returns validation results with errors, warnings, and line count.
 */
function validateSkill(skillFile) {
    const errors = [];
    const warnings = [];

    let content;
    try {
        content = fs.readFileSync(skillFile.path, 'utf8');
    } catch (error) {
        errors.push(
            new ValidationError(
                skillFile.relativePath,
                'file',
                `Cannot read file: ${error.message}`
            )
        );
        return { errors, warnings, lineCount: 0 };
    }

    const lineCount = content.split(/\r?\n/).length;
    const frontmatter = parseFrontmatter(content);

    // Check line count limits
    if (lineCount > LINE_LIMIT_HARD_MAX) {
        errors.push(
            new ValidationError(
                skillFile.relativePath,
                'size',
                `File has ${lineCount} lines (max: ${LINE_LIMIT_HARD_MAX}). Split into smaller focused skills.`
            )
        );
    } else if (lineCount > LINE_LIMIT_IDEAL_MAX) {
        warnings.push(
            new ValidationError(
                skillFile.relativePath,
                'size',
                `File has ${lineCount} lines (ideal: ${LINE_LIMIT_IDEAL_MIN}-${LINE_LIMIT_IDEAL_MAX}). Consider splitting.`
            )
        );
    }

    // Store line count for reporting
    skillFile.lineCount = lineCount;

    if (!frontmatter) {
        errors.push(new ValidationError(skillFile.relativePath, 'frontmatter', 'Missing or invalid YAML frontmatter'));
        return { errors, warnings, lineCount };
    }

    // Check required fields
    // SYNC: Keep logic in sync with validate-skills-required-fields.test.js validateRequiredField()
    for (const field of REQUIRED_FIELDS) {
        if (frontmatter[field] === undefined || frontmatter[field] === null) {
            errors.push(new ValidationError(skillFile.relativePath, field, `Required field '${field}' is missing`));
        } else if (frontmatter[field] === '') {
            errors.push(new ValidationError(skillFile.relativePath, field, `Required field '${field}' is empty`));
        }
    }

    // Validate id matches filename
    // SYNC: Keep presence check pattern in sync with validate-skills-required-fields.test.js
    if (frontmatter.id != null && frontmatter.id !== '' && frontmatter.id !== skillFile.expectedId) {
        errors.push(
            new ValidationError(
                skillFile.relativePath,
                'id',
                `ID '${frontmatter.id}' does not match filename '${skillFile.expectedId}'`
            )
        );
    }

    // Validate category matches folder
    // SYNC: Keep presence check pattern in sync with validate-skills-required-fields.test.js
    if (frontmatter.category != null && frontmatter.category !== '' && frontmatter.category !== skillFile.category) {
        errors.push(
            new ValidationError(
                skillFile.relativePath,
                'category',
                `Category '${frontmatter.category}' does not match folder '${skillFile.category}'`
            )
        );
    }

    // Validate category is known
    // SYNC: Keep presence check pattern in sync with validate-skills-required-fields.test.js
    if (frontmatter.category != null && frontmatter.category !== '' && !VALID_CATEGORIES.includes(frontmatter.category)) {
        warnings.push(
            new ValidationError(
                skillFile.relativePath,
                'category',
                `Unknown category '${frontmatter.category}'. Valid: ${VALID_CATEGORIES.join(', ')}`
            )
        );
    }

    // Validate status
    // SYNC: Keep presence check pattern in sync with validate-skills-required-fields.test.js
    if (frontmatter.status != null && frontmatter.status !== '' && !VALID_STATUSES.includes(frontmatter.status)) {
        errors.push(
            new ValidationError(
                skillFile.relativePath,
                'status',
                `Invalid status '${frontmatter.status}'. Valid: ${VALID_STATUSES.join(', ')}`
            )
        );
    }

    // Validate complexity level
    if (
        frontmatter.complexity != null &&
        frontmatter.complexity.level != null &&
        frontmatter.complexity.level !== '' &&
        !VALID_COMPLEXITY_LEVELS.includes(frontmatter.complexity.level)
    ) {
        warnings.push(
            new ValidationError(
                skillFile.relativePath,
                'complexity.level',
                `Invalid complexity level '${frontmatter.complexity.level}'. Valid: ${VALID_COMPLEXITY_LEVELS.join(', ')}`
            )
        );
    }

    // Validate impact ratings
    // SYNC: Keep presence check pattern in sync with validate-skills-optional-fields.test.js isValidImpactObject()
    if (frontmatter.impact != null && typeof frontmatter.impact === 'object') {
        // Warn about unknown impact types
        for (const impactType of Object.keys(frontmatter.impact)) {
            if (!VALID_IMPACT_TYPES.includes(impactType)) {
                warnings.push(
                    new ValidationError(
                        skillFile.relativePath,
                        `impact.${impactType}`,
                        `Unknown impact type '${impactType}'. Valid: ${VALID_IMPACT_TYPES.join(', ')}`
                    )
                );
            }
        }
        // Validate ratings for known impact types
        for (const impactType of VALID_IMPACT_TYPES) {
            if (
                frontmatter.impact[impactType] != null &&
                frontmatter.impact[impactType].rating != null &&
                frontmatter.impact[impactType].rating !== '' &&
                !VALID_IMPACT_RATINGS.includes(frontmatter.impact[impactType].rating)
            ) {
                warnings.push(
                    new ValidationError(
                        skillFile.relativePath,
                        `impact.${impactType}.rating`,
                        `Invalid rating '${frontmatter.impact[impactType].rating}'. Valid: ${VALID_IMPACT_RATINGS.join(', ')}`
                    )
                );
            }
        }
    }

    // Validate version format (semver-like)
    // SYNC: Keep presence check pattern in sync with validate-skills-required-fields.test.js
    if (frontmatter.version != null && frontmatter.version !== '') {
        // Coerce to string to handle numeric or other non-string types
        const versionStr = String(frontmatter.version);
        if (!versionStr.match(/^\d+\.\d+\.\d+$/)) {
            warnings.push(
                new ValidationError(
                    skillFile.relativePath,
                    'version',
                    `Version '${versionStr}' should be in semver format (e.g., 1.0.0)`
                )
            );
        }
    }

    // Warn about missing optional fields that affect skills index display
    // These are not required but cause '?' placeholders in the generated index
    // SYNC: Keep logic in sync with validate-skills-optional-fields.test.js validateComplexityLevel()
    if (frontmatter.complexity == null || frontmatter.complexity.level == null) {
        warnings.push(
            new ValidationError(
                skillFile.relativePath,
                'complexity.level',
                `Missing 'complexity.level' - will show '?' in Complexity column of skills index`
            )
        );
    } else if (frontmatter.complexity.level === '') {
        warnings.push(
            new ValidationError(
                skillFile.relativePath,
                'complexity.level',
                `Empty 'complexity.level' - will show '?' in Complexity column of skills index`
            )
        );
    }

    // SYNC: Keep logic in sync with validate-skills-optional-fields.test.js validatePerformanceRating()
    if (frontmatter.impact == null || frontmatter.impact.performance == null || frontmatter.impact.performance.rating == null) {
        warnings.push(
            new ValidationError(
                skillFile.relativePath,
                'impact.performance.rating',
                `Missing 'impact.performance.rating' - will show '?' in Performance column of skills index`
            )
        );
    } else if (frontmatter.impact.performance.rating === '') {
        warnings.push(
            new ValidationError(
                skillFile.relativePath,
                'impact.performance.rating',
                `Empty 'impact.performance.rating' - will show '?' in Performance column of skills index`
            )
        );
    }

    // SYNC: Keep logic in sync with validate-skills-tags.test.js validateTags()
    if (frontmatter.tags === undefined || frontmatter.tags === null) {
        warnings.push(
            new ValidationError(
                skillFile.relativePath,
                'tags',
                `Missing 'tags' array - will show empty Tags column in skills index`
            )
        );
    } else if (!Array.isArray(frontmatter.tags)) {
        warnings.push(
            new ValidationError(
                skillFile.relativePath,
                'tags',
                `'tags' must be an array, got ${typeof frontmatter.tags} - will show empty Tags column in skills index`
            )
        );
    } else if (frontmatter.tags.length === 0) {
        warnings.push(
            new ValidationError(
                skillFile.relativePath,
                'tags',
                `Empty 'tags' array - will show empty Tags column in skills index`
            )
        );
    }

    // Validate date format
    // SYNC: Keep presence check pattern in sync with validate-skills-required-fields.test.js
    for (const dateField of ['created', 'updated']) {
        if (frontmatter[dateField] != null && frontmatter[dateField] !== '') {
            // Coerce to string to handle numeric or other non-string types
            const dateStr = String(frontmatter[dateField]);
            if (!dateStr.match(/^\d{4}-\d{2}-\d{2}$/)) {
                warnings.push(
                    new ValidationError(
                        skillFile.relativePath,
                        dateField,
                        `Date '${dateStr}' should be in ISO format (YYYY-MM-DD)`
                    )
                );
            }
        }
    }

    // Check for required sections in body
    const requiredSections = ['## Overview', '## Solution'];
    for (const section of requiredSections) {
        if (!content.includes(section)) {
            warnings.push(
                new ValidationError(skillFile.relativePath, 'body', `Missing recommended section: '${section}'`)
            );
        }
    }

    return { errors, warnings, lineCount };
}

/**
 * Validate context.md file for line count.
 * Returns validation results with errors, warnings, and line count.
 */
function validateContextFile() {
    const errors = [];
    const warnings = [];

    if (!fs.existsSync(CONTEXT_FILE)) {
        return { errors, warnings, lineCount: 0 };
    }

    let content;
    try {
        content = fs.readFileSync(CONTEXT_FILE, 'utf8');
    } catch (error) {
        errors.push(
            new ValidationError(
                'context.md',
                'file',
                `Cannot read file: ${error.message}`
            )
        );
        return { errors, warnings, lineCount: 0 };
    }

    const lineCount = content.split(/\r?\n/).length;

    if (lineCount > LINE_LIMIT_HARD_MAX) {
        errors.push(
            new ValidationError(
                'context.md',
                'size',
                `File has ${lineCount} lines (max: ${LINE_LIMIT_HARD_MAX}). Consider extracting sections to skill files.`
            )
        );
    } else if (lineCount > LINE_LIMIT_IDEAL_MAX) {
        warnings.push(
            new ValidationError(
                'context.md',
                'size',
                `File has ${lineCount} lines (ideal: ${LINE_LIMIT_IDEAL_MIN}-${LINE_LIMIT_IDEAL_MAX}). Consider extracting sections.`
            )
        );
    }

    return { errors, warnings, lineCount };
}

/**
 * Main entry point.
 */
function main() {
    console.log('Validating skills in', SKILLS_DIR);
    console.log();

    const skillFiles = findSkillFiles(SKILLS_DIR);
    console.log(`Found ${skillFiles.length} skill files to validate`);
    console.log();

    let totalErrors = 0;
    let totalWarnings = 0;
    const fileSizeReport = [];

    // Validate context.md
    const contextResult = validateContextFile();
    if (contextResult.lineCount > 0) {
        fileSizeReport.push({ file: 'context.md', lines: contextResult.lineCount });
    }
    for (const error of contextResult.errors) {
        console.log(`📄 context.md`);
        console.log(`  ❌ ${error.field}: ${error.message}`);
        console.log();
    }
    for (const warning of contextResult.warnings) {
        console.log(`📄 context.md`);
        console.log(`  ⚠️  ${warning.field}: ${warning.message}`);
        console.log();
    }
    totalErrors += contextResult.errors.length;
    totalWarnings += contextResult.warnings.length;

    // Validate skill files
    for (const skillFile of skillFiles) {
        const { errors, warnings, lineCount } = validateSkill(skillFile);

        fileSizeReport.push({ file: skillFile.relativePath, lines: lineCount });

        if (errors.length > 0 || warnings.length > 0) {
            console.log(`📄 ${skillFile.relativePath}`);

            for (const error of errors) {
                console.log(`  ❌ ${error.field}: ${error.message}`);
            }

            for (const warning of warnings) {
                console.log(`  ⚠️  ${warning.field}: ${warning.message}`);
            }

            console.log();
        }

        totalErrors += errors.length;
        totalWarnings += warnings.length;
    }

    // Print file size summary
    console.log('---');
    console.log('📊 File Size Report:');
    console.log();

    // Sort by line count descending
    fileSizeReport.sort((a, b) => b.lines - a.lines);

    const maxFileLen = Math.max(...fileSizeReport.map((f) => f.file.length));
    for (const { file, lines } of fileSizeReport) {
        let indicator = '✅';
        if (lines > LINE_LIMIT_HARD_MAX) {
            indicator = '❌';
        } else if (lines > LINE_LIMIT_IDEAL_MAX) {
            indicator = '⚠️ ';
        } else if (lines < LINE_LIMIT_IDEAL_MIN && !SHORT_FILE_EXCLUDES.includes(file)) {
            indicator = '📝';
        }
        console.log(`  ${indicator} ${file.padEnd(maxFileLen)} : ${lines} lines`);
    }

    console.log();
    console.log(
        `  Legend: ✅ Ideal (${LINE_LIMIT_IDEAL_MIN}-${LINE_LIMIT_IDEAL_MAX}) | ` +
            `⚠️  Warning (>${LINE_LIMIT_IDEAL_MAX}) | ❌ Error (>${LINE_LIMIT_HARD_MAX}) | 📝 Short (<${LINE_LIMIT_IDEAL_MIN})`
    );

    console.log();
    console.log('---');
    console.log(`Validation complete: ${totalErrors} errors, ${totalWarnings} warnings`);

    if (totalErrors > 0) {
        console.log('\n❌ Validation failed');
        return 1;
    }

    if (totalWarnings > 0) {
        console.log('\n⚠️  Validation passed with warnings');
    } else {
        console.log('\n✅ All skills valid');
    }

    return 0;
}

// Export for testing when required as a module
if (typeof module !== 'undefined' && module.exports) {
    module.exports = {
        validateSkill,
        parseFrontmatter,
        ValidationError,
        VALID_COMPLEXITY_LEVELS,
        VALID_IMPACT_RATINGS,
    };
}

// Only run main when executed directly (not when required as a module)
if (require.main === module) {
    process.exit(main());
}
