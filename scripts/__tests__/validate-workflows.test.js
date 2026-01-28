/**
 * @fileoverview Tests for validate-workflows.js logic.
 *
 * These tests validate the core detection logic for problematic
 * git add --renormalize patterns in GitHub Actions workflows.
 */

"use strict";

/**
 * Checks if a line contains a problematic single-line multi-pattern renormalize command.
 * Extracted from validate-workflows.js for testing.
 *
 * @param {string} line - The line to check
 * @returns {boolean} True if the line contains a forbidden pattern
 */
function isForbiddenRenormalizePattern(line) {
    const trimmed = line.trim();

    if (!trimmed.includes("git add") || !trimmed.includes("--renormalize")) {
        return false;
    }

    if (trimmed.includes("$ext") || trimmed.includes("${ext}")) {
        return false;
    }

    const singleFilePattern = /git add --renormalize\s+--\s+'[^'*?]+'/;
    if (singleFilePattern.test(trimmed)) {
        return false;
    }

    const extensionPatterns = trimmed.match(/\*\.(\w+)/g) || [];
    const uniqueExtensions = new Set(
        extensionPatterns.map((p) => p.replace("*.", ""))
    );

    return uniqueExtensions.size > 1;
}

/**
 * Checks if a renormalize command is properly guarded by an existence check.
 *
 * @param {string[]} lines - All lines of the file
 * @param {number} lineIndex - Index of the renormalize line
 * @returns {boolean} True if properly guarded
 */
function hasExistenceCheck(lines, lineIndex) {
    const lookbackLines = 5;
    const startIndex = Math.max(0, lineIndex - lookbackLines);

    for (let i = lineIndex - 1; i >= startIndex; i--) {
        const line = lines[i];
        if (
            line.includes("git ls-files") &&
            line.includes("grep -q") &&
            (line.includes("then") || lines[i + 1]?.includes("then"))
        ) {
            return true;
        }
        if (line.includes("for ext in") || line.includes("for EXT in")) {
            for (let j = i + 1; j < lineIndex; j++) {
                if (lines[j].includes("git ls-files") && lines[j].includes("grep -q")) {
                    return true;
                }
            }
        }
    }
    return false;
}

describe("isForbiddenRenormalizePattern", () => {
    describe("should detect FORBIDDEN patterns", () => {
        test("single line with multiple distinct extensions", () => {
            const line =
                "git add --renormalize -- '*.md' '**/*.md' '*.json' '**/*.json'";
            expect(isForbiddenRenormalizePattern(line)).toBe(true);
        });

        test("single line with three extensions", () => {
            const line =
                "git add --renormalize -- '*.md' '*.json' '*.yml'";
            expect(isForbiddenRenormalizePattern(line)).toBe(true);
        });

        test("double-quoted patterns", () => {
            const line =
                'git add --renormalize -- "*.md" "**/*.md" "*.json" "**/*.json"';
            expect(isForbiddenRenormalizePattern(line)).toBe(true);
        });

        test("mixed quote styles", () => {
            const line =
                "git add --renormalize -- '*.cs' \"*.md\"";
            expect(isForbiddenRenormalizePattern(line)).toBe(true);
        });

        test("indented in workflow file", () => {
            const line =
                "          git add --renormalize -- '*.md' '*.json' '*.yml'";
            expect(isForbiddenRenormalizePattern(line)).toBe(true);
        });
    });

    describe("should ALLOW safe patterns", () => {
        test("single extension with recursive pattern", () => {
            const line = "git add --renormalize -- '*.md' '**/*.md'";
            expect(isForbiddenRenormalizePattern(line)).toBe(false);
        });

        test("variable-based pattern in loop", () => {
            const line = 'git add --renormalize -- "*.$ext" "**/*.$ext"';
            expect(isForbiddenRenormalizePattern(line)).toBe(false);
        });

        test("variable-based pattern with braces", () => {
            const line = 'git add --renormalize -- "*.${ext}" "**/*.${ext}"';
            expect(isForbiddenRenormalizePattern(line)).toBe(false);
        });

        test("single specific file", () => {
            const line =
                "git add --renormalize -- '.config/dotnet-tools.json'";
            expect(isForbiddenRenormalizePattern(line)).toBe(false);
        });

        test("line without git add", () => {
            const line = "echo 'renormalize'";
            expect(isForbiddenRenormalizePattern(line)).toBe(false);
        });

        test("line without renormalize", () => {
            const line = "git add -- '*.md' '*.json'";
            expect(isForbiddenRenormalizePattern(line)).toBe(false);
        });

        test("add_options: --renormalize (YAML key)", () => {
            const line = "add_options: --renormalize";
            expect(isForbiddenRenormalizePattern(line)).toBe(false);
        });

        test("comment describing renormalize", () => {
            const line =
                "# Use --renormalize to ensure line endings";
            expect(isForbiddenRenormalizePattern(line)).toBe(false);
        });
    });
});

describe("hasExistenceCheck", () => {
    describe("should detect proper guards", () => {
        test("if statement with git ls-files and grep", () => {
            const lines = [
                "for ext in cs md json; do",
                '  if git ls-files "*.$ext" "**/*.$ext" | grep -q .; then',
                '    git add --renormalize -- "*.$ext" "**/*.$ext"',
                "  fi",
                "done",
            ];
            expect(hasExistenceCheck(lines, 2)).toBe(true);
        });

        test("for loop with existence check", () => {
            const lines = [
                "for ext in cs md json asmdef yml yaml; do",
                '  if git ls-files "*.$ext" "**/*.$ext" | grep -q .; then',
                '    git add --renormalize -- "*.$ext" "**/*.$ext"',
                "  fi",
                "done",
            ];
            expect(hasExistenceCheck(lines, 2)).toBe(true);
        });
    });

    describe("should detect missing guards", () => {
        test("direct command without check", () => {
            const lines = [
                "- name: Renormalize line endings",
                "  run: |",
                "    git add --renormalize -- '*.md' '**/*.md'",
            ];
            expect(hasExistenceCheck(lines, 2)).toBe(false);
        });

        test("guard too far away", () => {
            const lines = [
                'if git ls-files "*.md" | grep -q .; then',
                "  echo 'files exist'",
                "fi",
                "",
                "",
                "",
                "",
                "git add --renormalize -- '*.md' '**/*.md'",
            ];
            // Index 7, lookback 5 won't reach index 0
            expect(hasExistenceCheck(lines, 7)).toBe(false);
        });
    });
});

describe("Real workflow patterns", () => {
    describe("should correctly handle actual workflow content", () => {
        test("correct per-extension loop pattern", () => {
            const workflowContent = `
      - name: Renormalize line endings
        shell: bash
        run: |
          # Renormalize each extension separately to avoid "pathspec did not match" failures
          for ext in cs md json asmdef yml yaml; do
            if git ls-files "*.$ext" "**/*.$ext" | grep -q .; then
              git add --renormalize -- "*.$ext" "**/*.$ext"
            fi
          done
`;
            const lines = workflowContent.split("\n");
            const renormalizeLine = lines.findIndex((l) =>
                l.includes("git add --renormalize")
            );

            // Should NOT be forbidden (uses variable)
            expect(isForbiddenRenormalizePattern(lines[renormalizeLine])).toBe(
                false
            );
            // Should have existence check
            expect(hasExistenceCheck(lines, renormalizeLine)).toBe(true);
        });

        test("problematic single-line pattern", () => {
            const workflowContent = `
      - name: Renormalize line endings
        run: git add --renormalize -- '*.md' '*.markdown' '*.json'
`;
            const lines = workflowContent.split("\n");
            const renormalizeLine = lines.findIndex((l) =>
                l.includes("git add --renormalize")
            );

            // Should BE forbidden (multiple extensions on single line)
            expect(isForbiddenRenormalizePattern(lines[renormalizeLine])).toBe(
                true
            );
        });
    });
});
