/**
 * @fileoverview Tests for validate-workflows.js logic.
 *
 * These tests validate the core detection logic for problematic
 * git add --renormalize patterns in GitHub Actions workflows.
 */

"use strict";

const fs = require("fs");
const os = require("os");
const path = require("path");

const {
    isForbiddenRenormalizePattern,
    hasExistenceCheck,
    extractWorkflowPathEntries,
    findIgnoredPathViolations,
    extractRunBlocks,
    findLockfileInstallViolations,
    validateWorkflow,
} = require('../validate-workflows.js');

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

        test("variable-based pattern with uppercase variable name", () => {
            const line = 'git add --renormalize -- "*.${FILE_EXT}" "**/*.${FILE_EXT}"';
            expect(isForbiddenRenormalizePattern(line)).toBe(false);
        });

        test("ignores non-command extension text on same line", () => {
            const line =
                'echo "extensions: *.json" && git add --renormalize -- "*.md" "**/*.md"';
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
                "",
                "",
                "",
                "",
                "",
                "",
                "git add --renormalize -- '*.md' '**/*.md'",
            ];
            // Index 13, lookback 10 won't reach index 0
            expect(hasExistenceCheck(lines, 13)).toBe(false);
        });
    });
});

describe("extractWorkflowPathEntries", () => {
    test("collects entries under paths blocks", () => {
        const lines = [
            "on:",
            "  pull_request:",
            "    paths:",
            "      - \"package.json\"",
            "      - \"package-lock.json\"",
            "  workflow_dispatch:",
            "    inputs:",
            "      target:",
            "        description: Target",
        ];

        const entries = extractWorkflowPathEntries(lines);
        expect(entries).toEqual([
            { line: 4, path: "package.json" },
            { line: 5, path: "package-lock.json" },
        ]);
    });
});

describe("findIgnoredPathViolations", () => {
    const isIgnoredPathMock = (_repoRoot, candidatePath) =>
        candidatePath === "package-lock.json";

    test("reports ignored literal path entries", () => {
        const lines = [
            "on:",
            "  push:",
            "    paths:",
            "      - package-lock.json",
            "      - scripts/**/*.js",
        ];

        const violations = findIgnoredPathViolations(
            "test.yml",
            lines,
            "/tmp",
            isIgnoredPathMock
        );

        expect(violations).toHaveLength(1);
        expect(violations[0].message).toContain("ignored by git");
        expect(violations[0].line).toBe(4);
    });

    test.each([
        "scripts/**/*.js",
        "**/*.yml",
        "${{ github.event.pull_request.head.ref }}",
        "!docs/**",
    ])("ignores non-literal path pattern '%s'", (pathPattern) => {
        const lines = [
            "on:",
            "  push:",
            "    paths:",
            `      - ${pathPattern}`,
        ];

        const violations = findIgnoredPathViolations(
            "test.yml",
            lines,
            "/tmp",
            isIgnoredPathMock
        );

        expect(violations).toHaveLength(0);
    });
});

describe("run block lockfile policy", () => {
    test("extractRunBlocks handles folded and inline run definitions", () => {
        const lines = [
            "steps:",
            "  - name: Install dependencies",
            "    run: |",
            "      npm ci",
            "  - name: Inline",
            "    run: npm run test:scripts",
        ];

        const blocks = extractRunBlocks(lines);
        expect(blocks).toHaveLength(2);
        expect(blocks[0]).toEqual(
            expect.objectContaining({ startLine: 3, text: "npm ci" })
        );
        expect(blocks[1]).toEqual(
            expect.objectContaining({ startLine: 6, text: "npm run test:scripts" })
        );
    });

    test.each([
        {
            name: "flags hard failure when lockfile is missing",
            lines: [
                "steps:",
                "  - run: |",
                "      if [ ! -f package-lock.json ]; then",
                "        exit 1",
                "      fi",
                "      npm ci",
            ],
            expectedViolations: 1,
        },
        {
            name: "flags unguarded npm ci when lockfile is ignored",
            lines: [
                "steps:",
                "  - run: npm ci",
            ],
            expectedViolations: 1,
        },
        {
            name: "allows npm ci with fallback install",
            lines: [
                "steps:",
                "  - run: |",
                "      if [ -f package-lock.json ]; then",
                "        npm ci",
                "      else",
                "        npm i --no-audit --no-fund",
                "      fi",
            ],
            expectedViolations: 0,
        },
        {
            name: "allows npm ci with fallback full install command",
            lines: [
                "steps:",
                "  - run: |",
                "      if [ -f package-lock.json ]; then",
                "        npm ci",
                "      else",
                "        npm install --no-audit --no-fund",
                "      fi",
            ],
            expectedViolations: 0,
        },
        {
            name: "allows npm ci with shell-or fallback",
            lines: [
                "steps:",
                "  - run: npm ci || npm i --no-audit --no-fund",
            ],
            expectedViolations: 0,
        },
    ])("findLockfileInstallViolations: $name", ({ lines, expectedViolations }) => {
        const violations = findLockfileInstallViolations(
            "test.yml",
            lines,
            true
        );

        expect(violations).toHaveLength(expectedViolations);
    });

    test("does not enforce lockfile fallback policy when package-lock is not ignored", () => {
        const lines = [
            "steps:",
            "  - run: npm ci",
        ];

        const violations = findLockfileInstallViolations("test.yml", lines, false);
        expect(violations).toHaveLength(0);
    });

    test("source avoids optional-suffix shorthand that trips cspell", () => {
        const source = fs.readFileSync(
            path.resolve(__dirname, "../validate-workflows.js"),
            "utf8"
        );
        const optionalSuffixShorthand = `i(?:${"n" + "stall"})?`;

        expect(source).not.toContain(optionalSuffixShorthand);
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

describe("validateWorkflow newline handling", () => {
    test("detects forbidden pattern when file uses lone CR line endings", () => {
        const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), "validate-workflows-"));
        try {
            const workflowPath = path.join(tempDir, "test.yml");

            const workflowContent = [
                "name: Test",
                "jobs:",
                "  lint:",
                "    steps:",
                "      - run: git add --renormalize -- '*.md' '*.json'",
            ].join("\r");

            fs.writeFileSync(workflowPath, workflowContent, "utf8");
            const violations = validateWorkflow(workflowPath);

            expect(violations.some((v) => v.severity === "error")).toBe(true);
        } finally {
            fs.rmSync(tempDir, { recursive: true, force: true });
        }
    });
});

describe("validateWorkflow policy integration", () => {
    test("reports ignored path filters and unsafe lockfile install policy", () => {
        const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), "validate-workflows-policy-"));
        try {
            const workflowPath = path.join(tempDir, "policy-test.yml");
            const workflowContent = [
                "name: Policy Test",
                "on:",
                "  pull_request:",
                "    paths:",
                "      - package-lock.json",
                "jobs:",
                "  test:",
                "    runs-on: ubuntu-latest",
                "    steps:",
                "      - name: Install dependencies",
                "        run: |",
                "          if [ ! -f package-lock.json ]; then",
                "            exit 1",
                "          fi",
                "          npm ci",
            ].join("\n");

            fs.writeFileSync(workflowPath, workflowContent, "utf8");
            const isIgnoredPathMock = (_repoRoot, candidatePath) =>
                candidatePath === "package-lock.json";
            const violations = validateWorkflow(workflowPath, {
                repoRoot: tempDir,
                isIgnoredPathFn: isIgnoredPathMock,
            });

            const errorMessages = violations
                .filter((violation) => violation.severity === "error")
                .map((violation) => violation.message);

            expect(
                errorMessages.some((message) => message.includes("ignored by git"))
            ).toBe(true);
            expect(
                errorMessages.some((message) =>
                    message.includes("must not fail when the lockfile is absent")
                )
            ).toBe(true);
        } finally {
            fs.rmSync(tempDir, { recursive: true, force: true });
        }
    });

    test("surfaces ignore policy evaluation failures as validation errors", () => {
        const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), "validate-workflows-policy-error-"));
        try {
            const workflowPath = path.join(tempDir, "policy-error-test.yml");
            fs.writeFileSync(
                workflowPath,
                [
                    "name: Policy Error Test",
                    "on:",
                    "  pull_request:",
                    "    paths:",
                    "      - package-lock.json",
                ].join("\n"),
                "utf8"
            );

            const violations = validateWorkflow(workflowPath, {
                repoRoot: tempDir,
                isIgnoredPathFn: () => {
                    throw new Error("mock git failure");
                },
            });

            expect(
                violations.some((violation) =>
                    violation.message.includes("Workflow validation failed while evaluating ignore policy")
                )
            ).toBe(true);
        } finally {
            fs.rmSync(tempDir, { recursive: true, force: true });
        }
    });

    test("current repository workflows pass with no validation errors", () => {
        const workflowsDir = path.resolve(__dirname, "../../.github/workflows");
        const workflowFiles = fs
            .readdirSync(workflowsDir)
            .filter((fileName) => fileName.endsWith(".yml") || fileName.endsWith(".yaml"));

        for (const workflowFile of workflowFiles) {
            const workflowPath = path.join(workflowsDir, workflowFile);
            const violations = validateWorkflow(workflowPath);
            const errors = violations.filter((violation) => violation.severity === "error");
            expect(errors).toHaveLength(0);
        }
    });
});
