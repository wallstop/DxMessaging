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
  isGitIgnoredPath,
  extractWorkflowPathEntries,
  findIgnoredPathViolations,
  extractRunBlocks,
  findLockfileInstallViolations,
  detectBashSyntaxPattern,
  findWindowsBashPortabilityViolations,
  findForbiddenRunsOnGroupViolations,
  findChangelogCoverageCheckoutViolations,
  validateWorkflow
} = require("../validate-workflows.js");

describe("isForbiddenRenormalizePattern", () => {
  describe("should detect FORBIDDEN patterns", () => {
    test("single line with multiple distinct extensions", () => {
      const line = "git add --renormalize -- '*.md' '**/*.md' '*.json' '**/*.json'";
      expect(isForbiddenRenormalizePattern(line)).toBe(true);
    });

    test("single line with three extensions", () => {
      const line = "git add --renormalize -- '*.md' '*.json' '*.yml'";
      expect(isForbiddenRenormalizePattern(line)).toBe(true);
    });

    test("double-quoted patterns", () => {
      const line = 'git add --renormalize -- "*.md" "**/*.md" "*.json" "**/*.json"';
      expect(isForbiddenRenormalizePattern(line)).toBe(true);
    });

    test("mixed quote styles", () => {
      const line = "git add --renormalize -- '*.cs' \"*.md\"";
      expect(isForbiddenRenormalizePattern(line)).toBe(true);
    });

    test("indented in workflow file", () => {
      const line = "          git add --renormalize -- '*.md' '*.json' '*.yml'";
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
      const line = 'echo "extensions: *.json" && git add --renormalize -- "*.md" "**/*.md"';
      expect(isForbiddenRenormalizePattern(line)).toBe(false);
    });

    test("single specific file", () => {
      const line = "git add --renormalize -- '.config/dotnet-tools.json'";
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
      const line = "# Use --renormalize to ensure line endings";
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
        "done"
      ];
      expect(hasExistenceCheck(lines, 2)).toBe(true);
    });

    test("for loop with existence check", () => {
      const lines = [
        "for ext in cs md json asmdef yml yaml; do",
        '  if git ls-files "*.$ext" "**/*.$ext" | grep -q .; then',
        '    git add --renormalize -- "*.$ext" "**/*.$ext"',
        "  fi",
        "done"
      ];
      expect(hasExistenceCheck(lines, 2)).toBe(true);
    });
  });

  describe("should detect missing guards", () => {
    test("direct command without check", () => {
      const lines = [
        "- name: Renormalize line endings",
        "  run: |",
        "    git add --renormalize -- '*.md' '**/*.md'"
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
        "git add --renormalize -- '*.md' '**/*.md'"
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
      '      - "package.json"',
      '      - "package-lock.json"',
      "  workflow_dispatch:",
      "    inputs:",
      "      target:",
      "        description: Target"
    ];

    const entries = extractWorkflowPathEntries(lines);
    expect(entries).toEqual([
      { line: 4, path: "package.json" },
      { line: 5, path: "package-lock.json" }
    ]);
  });
});

describe("findIgnoredPathViolations", () => {
  const isIgnoredPathMock = (_repoRoot, candidatePath) => candidatePath === "package-lock.json";

  test("reports ignored literal path entries", () => {
    const lines = [
      "on:",
      "  push:",
      "    paths:",
      "      - package-lock.json",
      "      - scripts/**/*.js"
    ];

    const violations = findIgnoredPathViolations("test.yml", lines, "/tmp", isIgnoredPathMock);

    expect(violations).toHaveLength(1);
    expect(violations[0].message).toContain("ignored by git");
    expect(violations[0].line).toBe(4);
  });

  test.each([
    "scripts/**/*.js",
    "**/*.yml",
    "${{ github.event.pull_request.head.ref }}",
    "!docs/**"
  ])("ignores non-literal path pattern '%s'", (pathPattern) => {
    const lines = ["on:", "  push:", "    paths:", `      - ${pathPattern}`];

    const violations = findIgnoredPathViolations("test.yml", lines, "/tmp", isIgnoredPathMock);

    expect(violations).toHaveLength(0);
  });
});

describe("isGitIgnoredPath", () => {
  test("uses git check-ignore with --no-index and -- separator", () => {
    const execFileSyncMock = jest.fn();

    const ignored = isGitIgnoredPath("/repo", "package-lock.json", execFileSyncMock);

    expect(ignored).toBe(true);
    expect(execFileSyncMock).toHaveBeenCalledWith(
      "git",
      ["check-ignore", "--quiet", "--no-index", "--", "package-lock.json"],
      expect.objectContaining({ cwd: "/repo" })
    );
  });

  test("falls back when git does not support --no-index", () => {
    const unsupportedNoIndexError = new Error("unknown option");
    unsupportedNoIndexError.status = 129;
    unsupportedNoIndexError.stderr = "error: unknown option `no-index`";

    const execFileSyncMock = jest
      .fn()
      .mockImplementationOnce(() => {
        throw unsupportedNoIndexError;
      })
      .mockImplementationOnce(() => {});

    const ignored = isGitIgnoredPath("/repo", "package-lock.json", execFileSyncMock);

    expect(ignored).toBe(true);
    expect(execFileSyncMock).toHaveBeenNthCalledWith(
      2,
      "git",
      ["check-ignore", "--quiet", "--", "package-lock.json"],
      expect.objectContaining({ cwd: "/repo" })
    );
  });

  test("returns false when fallback check-ignore reports not ignored", () => {
    const unsupportedNoIndexError = new Error("unknown option");
    unsupportedNoIndexError.status = 129;
    unsupportedNoIndexError.stderr = "error: unknown option `no-index`";

    const notIgnoredError = new Error("not ignored");
    notIgnoredError.status = 1;

    const execFileSyncMock = jest
      .fn()
      .mockImplementationOnce(() => {
        throw unsupportedNoIndexError;
      })
      .mockImplementationOnce(() => {
        throw notIgnoredError;
      });

    const ignored = isGitIgnoredPath("/repo", "package-lock.json", execFileSyncMock);

    expect(ignored).toBe(false);
  });

  test("throws when git binary is unavailable for --no-index check", () => {
    const missingGitError = new Error("git is not installed");
    missingGitError.code = "ENOENT";

    const execFileSyncMock = jest.fn().mockImplementationOnce(() => {
      throw missingGitError;
    });

    expect(() => isGitIgnoredPath("/repo", "package-lock.json", execFileSyncMock)).toThrow(
      /git executable was not found on PATH/i
    );
  });

  test("throws when fallback check-ignore cannot execute due to missing git", () => {
    const unsupportedNoIndexError = new Error("unknown option");
    unsupportedNoIndexError.status = 129;
    unsupportedNoIndexError.stderr = "error: unknown option `no-index`";

    const missingGitError = new Error("git is not installed");
    missingGitError.code = "ENOENT";

    const execFileSyncMock = jest
      .fn()
      .mockImplementationOnce(() => {
        throw unsupportedNoIndexError;
      })
      .mockImplementationOnce(() => {
        throw missingGitError;
      });

    expect(() => isGitIgnoredPath("/repo", "package-lock.json", execFileSyncMock)).toThrow(
      /check-ignore fallback/i
    );
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
      "    run: npm run test:scripts"
    ];

    const blocks = extractRunBlocks(lines);
    expect(blocks).toHaveLength(2);
    expect(blocks[0]).toEqual(expect.objectContaining({ startLine: 3, text: "npm ci" }));
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
        "      npm ci"
      ],
      expectedViolations: 1
    },
    {
      name: "flags unguarded npm ci when lockfile is ignored",
      lines: ["steps:", "  - run: npm ci"],
      expectedViolations: 1
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
        "      fi"
      ],
      expectedViolations: 0
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
        "      fi"
      ],
      expectedViolations: 0
    },
    {
      name: "allows npm ci with shell-or fallback",
      lines: ["steps:", "  - run: npm ci || npm i --no-audit --no-fund"],
      expectedViolations: 0
    },
    {
      name: "flags npm install-only blocks",
      lines: ["steps:", "  - run: npm i --no-audit --no-fund"],
      expectedViolations: 1
    },
    {
      name: "flags fallback with wrong lockfile guard",
      lines: [
        "steps:",
        "  - run: |",
        "      if [ -f npm-shrinkwrap.json ]; then",
        "        npm ci",
        "      else",
        "        npm i --no-audit --no-fund",
        "      fi"
      ],
      expectedViolations: 1
    }
  ])("findLockfileInstallViolations: $name", ({ lines, expectedViolations }) => {
    const violations = findLockfileInstallViolations("test.yml", lines, true);

    expect(violations).toHaveLength(expectedViolations);
  });

  test("does not enforce lockfile fallback policy when package-lock is not ignored", () => {
    const lines = ["steps:", "  - run: npm ci"];

    const violations = findLockfileInstallViolations("test.yml", lines, false);
    expect(violations).toHaveLength(0);
  });

  test("source avoids optional-suffix shorthand that trips cspell", () => {
    const source = fs.readFileSync(path.resolve(__dirname, "../validate-workflows.js"), "utf8");
    const optionalSuffixShorthand = `i(?:${"n" + "stall"})?`;

    expect(source).not.toContain(optionalSuffixShorthand);
  });
});

describe("windows matrix bash shell portability policy", () => {
  test.each([
    {
      name: "detects if bracket conditionals",
      runText: "if [ -f package-lock.json ]; then\n  npm ci\nfi",
      expected: "if/elif [ ... ] conditional"
    },
    {
      name: "detects elif bracket conditionals",
      runText:
        "if [ -f package-lock.json ]; then\n  npm ci\nelif [ -f npm-shrinkwrap.json ]; then\n  npm ci\nfi",
      expected: "if/elif [ ... ] conditional"
    },
    {
      name: "detects for-in loops",
      runText: 'for ext in md json; do\n  echo "$ext"\ndone',
      expected: "for ... in loop"
    },
    {
      name: "detects while loops",
      runText: "while [ -f package-lock.json ]; do\n  break\ndone",
      expected: "while [ ... ] loop"
    },
    {
      name: "detects until loops",
      runText: "until [ -f package-lock.json ]; do\n  break\ndone",
      expected: "until [ ... ] loop"
    },
    {
      name: "detects set shell options",
      runText: "set -euo pipefail\nnpm ci",
      expected: "set -e/-o shell option"
    },
    {
      name: "detects test builtins",
      runText: "test -f package-lock.json && npm ci",
      expected: "test -f/-d shell check"
    },
    {
      name: "detects logical chaining operators",
      runText: "npm ci && npm run validate:npm-meta",
      expected: "logical chaining operator (&&/||)"
    },
    {
      name: "ignores commented bash snippets",
      runText: "# if [ -f package-lock.json ]; then\nnpm ci",
      expected: null
    },
    {
      name: "ignores plain npm command without chaining",
      runText: "npm ci",
      expected: null
    }
  ])("detectBashSyntaxPattern: $name", ({ runText, expected }) => {
    expect(detectBashSyntaxPattern(runText)).toBe(expected);
  });

  test.each([
    {
      name: "flags bash syntax in windows matrix job without shell override",
      lines: [
        "name: test",
        "jobs:",
        "  validate:",
        "    runs-on: ${{ matrix.os }}",
        "    strategy:",
        "      matrix:",
        "        os:",
        "          - ubuntu-latest",
        "          - windows-latest",
        "    steps:",
        "      - name: Install",
        "        run: |",
        "          if [ -f package-lock.json ]; then",
        "            npm ci",
        "          else",
        "            npm i --no-audit --no-fund",
        "          fi"
      ],
      expectedViolationCount: 1
    },
    {
      name: "flags shell chaining operators in windows matrix job without shell override",
      lines: [
        "name: test",
        "jobs:",
        "  validate:",
        "    runs-on: ${{ matrix.os }}",
        "    strategy:",
        "      matrix:",
        "        os:",
        "          - ubuntu-latest",
        "          - windows-latest",
        "    steps:",
        "      - name: Install",
        "        run: npm ci && npm run validate:npm-meta"
      ],
      expectedViolationCount: 1
    },
    {
      name: "allows step-level shell override",
      lines: [
        "name: test",
        "jobs:",
        "  validate:",
        "    runs-on: ${{ matrix.os }}",
        "    strategy:",
        "      matrix:",
        "        os:",
        "          - ubuntu-latest",
        "          - windows-latest",
        "    steps:",
        "      - name: Install",
        "        shell: bash",
        "        run: |",
        "          if [ -f package-lock.json ]; then",
        "            npm ci",
        "          else",
        "            npm i --no-audit --no-fund",
        "          fi"
      ],
      expectedViolationCount: 0
    },
    {
      name: "allows job defaults.run.shell override",
      lines: [
        "name: test",
        "jobs:",
        "  validate:",
        "    runs-on: ${{ matrix.os }}",
        "    strategy:",
        "      matrix:",
        "        os:",
        "          - ubuntu-latest",
        "          - windows-latest",
        "    defaults:",
        "      run:",
        "        shell: bash",
        "    steps:",
        "      - name: Install",
        "        run: |",
        "          if [ -f package-lock.json ]; then",
        "            npm ci",
        "          else",
        "            npm i --no-audit --no-fund",
        "          fi"
      ],
      expectedViolationCount: 0
    },
    {
      name: "allows workflow defaults.run.shell override",
      lines: [
        "name: test",
        "defaults:",
        "  run:",
        "    shell: bash",
        "jobs:",
        "  validate:",
        "    runs-on: ${{ matrix.os }}",
        "    strategy:",
        "      matrix:",
        "        os:",
        "          - ubuntu-latest",
        "          - windows-latest",
        "    steps:",
        "      - name: Install",
        "        run: |",
        "          if [ -f package-lock.json ]; then",
        "            npm ci",
        "          else",
        "            npm i --no-audit --no-fund",
        "          fi"
      ],
      expectedViolationCount: 0
    },
    {
      name: "does not enforce bash-shell policy for ubuntu-only jobs",
      lines: [
        "name: test",
        "jobs:",
        "  validate:",
        "    runs-on: ubuntu-latest",
        "    steps:",
        "      - name: Install",
        "        run: |",
        "          if [ -f package-lock.json ]; then",
        "            npm ci",
        "          else",
        "            npm i --no-audit --no-fund",
        "          fi"
      ],
      expectedViolationCount: 0
    },
    {
      name: "does not flag non-bash run blocks in windows jobs",
      lines: [
        "name: test",
        "jobs:",
        "  validate:",
        "    runs-on: ${{ matrix.os }}",
        "    strategy:",
        "      matrix:",
        "        os:",
        "          - ubuntu-latest",
        "          - windows-latest",
        "    steps:",
        "      - name: Install",
        "        run: npm ci"
      ],
      expectedViolationCount: 0
    }
  ])("findWindowsBashPortabilityViolations: $name", ({ lines, expectedViolationCount }) => {
    const violations = findWindowsBashPortabilityViolations("test.yml", lines);

    expect(violations).toHaveLength(expectedViolationCount);
  });
});

describe("findForbiddenRunsOnGroupViolations", () => {
  test.each([
    {
      name: "flags multi-line runs-on with a group: key",
      lines: [
        "jobs:",
        "  unity:",
        "    runs-on:",
        "      group: some-runner-group",
        "      labels:",
        "        - self-hosted",
        "        - Windows",
        "    steps:",
        "      - run: echo hello"
      ],
      expectedViolations: 1
    },
    {
      name: "flags inline mapping runs-on with a group: key",
      lines: [
        "jobs:",
        "  unity:",
        "    runs-on: { group: some-runner-group, labels: [self-hosted, Windows] }",
        "    steps:",
        "      - run: echo hello"
      ],
      expectedViolations: 1
    },
    {
      name: "allows labels-only inline array runs-on",
      lines: [
        "jobs:",
        "  unity:",
        "    runs-on: [self-hosted, Windows, RAM-64GB]",
        "    steps:",
        "      - run: echo hello"
      ],
      expectedViolations: 0
    },
    {
      name: "allows labels-only block list runs-on",
      lines: [
        "jobs:",
        "  unity:",
        "    runs-on:",
        "      - self-hosted",
        "      - Windows",
        "      - RAM-64GB",
        "    steps:",
        "      - run: echo hello"
      ],
      expectedViolations: 0
    },
    {
      name: "allows plain scalar runs-on",
      lines: [
        "jobs:",
        "  ubuntu:",
        "    runs-on: ubuntu-latest",
        "    steps:",
        "      - run: echo hello"
      ],
      expectedViolations: 0
    },
    {
      name: "does not confuse concurrency.group with runs-on.group (multi-line)",
      lines: [
        "jobs:",
        "  unity:",
        "    concurrency:",
        "      group: wallstop-organization-builds",
        "      cancel-in-progress: false",
        "    runs-on:",
        "      labels:",
        "        - self-hosted",
        "        - Windows",
        "        - RAM-64GB",
        "    steps:",
        "      - run: echo hello"
      ],
      expectedViolations: 0
    },
    {
      name: "still flags runs-on.group when a sibling concurrency.group is present",
      lines: [
        "jobs:",
        "  unity:",
        "    concurrency:",
        "      group: wallstop-organization-builds",
        "      cancel-in-progress: false",
        "    runs-on:",
        "      group: some-runner-group",
        "      labels:",
        "        - self-hosted",
        "        - Windows",
        "        - RAM-64GB",
        "    steps:",
        "      - run: echo hello"
      ],
      expectedViolations: 1
    }
  ])("$name", ({ lines, expectedViolations }) => {
    const violations = findForbiddenRunsOnGroupViolations("test.yml", lines);

    expect(violations).toHaveLength(expectedViolations);
    if (expectedViolations > 0) {
      for (const violation of violations) {
        expect(violation.severity).toBe("error");
        expect(violation.message).toContain("runs-on.group is forbidden");
        expect(violation.message).toContain("wallstop-organization-builds");
      }
    }
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
      const renormalizeLine = lines.findIndex((l) => l.includes("git add --renormalize"));

      // Should NOT be forbidden (uses variable)
      expect(isForbiddenRenormalizePattern(lines[renormalizeLine])).toBe(false);
      // Should have existence check
      expect(hasExistenceCheck(lines, renormalizeLine)).toBe(true);
    });

    test("problematic single-line pattern", () => {
      const workflowContent = `
      - name: Renormalize line endings
        run: git add --renormalize -- '*.md' '*.markdown' '*.json'
`;
      const lines = workflowContent.split("\n");
      const renormalizeLine = lines.findIndex((l) => l.includes("git add --renormalize"));

      // Should BE forbidden (multiple extensions on single line)
      expect(isForbiddenRenormalizePattern(lines[renormalizeLine])).toBe(true);
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
        "      - run: git add --renormalize -- '*.md' '*.json'"
      ].join("\r");

      fs.writeFileSync(workflowPath, workflowContent, "utf8");
      const violations = validateWorkflow(workflowPath);

      expect(violations.some((v) => v.severity === "error")).toBe(true);
    } finally {
      fs.rmSync(tempDir, { recursive: true, force: true });
    }
  });
});

describe("findChangelogCoverageCheckoutViolations", () => {
  test.each([
    ["direct changelog validator", "node scripts/validate-changelog.js --check-coverage"],
    ["pre-commit changelog policy hook", "pre-commit run validate-changelog-policy --all-files"],
    ["npm coverage script", "npm run validate:changelog:coverage"],
    ["npm preflight script", "npm run preflight:pre-commit"],
    ["npm full validation script", "npm run validate:all"]
  ])("requires full-history checkout for %s", (_name, coverageCommand) => {
    const lines = [
      "jobs:",
      "  changelog:",
      "    runs-on: ubuntu-latest",
      "    steps:",
      "      - name: Checkout repository",
      "        uses: actions/checkout@v6",
      "        with:",
      "          persist-credentials: false",
      "      - name: Validate changelog coverage",
      `        run: ${coverageCommand}`
    ];

    const violations = findChangelogCoverageCheckoutViolations("workflow.yml", lines);

    expect(violations).toHaveLength(1);
    expect(violations[0]).toEqual(
      expect.objectContaining({
        file: "workflow.yml",
        line: 10,
        pattern: coverageCommand,
        severity: "error"
      })
    );
    expect(violations[0].message).toContain("fetch-depth: 0");
  });

  test.each([
    ["unquoted zero", "          fetch-depth: 0"],
    ["quoted zero", '          fetch-depth: "0"']
  ])("allows full-history checkout with %s", (_name, fetchDepthLine) => {
    const lines = [
      "jobs:",
      "  changelog:",
      "    runs-on: ubuntu-latest",
      "    steps:",
      "      - name: Checkout repository",
      "        uses: actions/checkout@v6",
      "        with:",
      fetchDepthLine,
      "          persist-credentials: false",
      "      - name: Validate changelog coverage",
      "        run: pre-commit run validate-changelog-policy --all-files"
    ];

    const violations = findChangelogCoverageCheckoutViolations("workflow.yml", lines);

    expect(violations).toHaveLength(0);
  });

  test("allows shorthand full-history checkout before changelog coverage", () => {
    const lines = [
      "jobs:",
      "  changelog:",
      "    runs-on: ubuntu-latest",
      "    steps:",
      "      - uses: actions/checkout@v6",
      "        with:",
      "          fetch-depth: 0",
      "      - name: Validate changelog coverage",
      "        run: node scripts/validate-changelog.js --check-coverage"
    ];

    const violations = findChangelogCoverageCheckoutViolations("workflow.yml", lines);

    expect(violations).toHaveLength(0);
  });

  test.each([
    [
      "folded run block",
      [
        "      - name: Validate changelog coverage",
        "        run: >-",
        "          node scripts/validate-changelog.js",
        "          --check-coverage"
      ]
    ],
    [
      "literal run block",
      [
        "      - name: Validate changelog coverage",
        "        run: |",
        "          pre-commit run",
        "          validate-changelog-policy --all-files"
      ]
    ]
  ])("detects changelog coverage in %s", (_name, runStepLines) => {
    const lines = [
      "jobs:",
      "  changelog:",
      "    runs-on: ubuntu-latest",
      "    steps:",
      "      - name: Checkout repository",
      "        uses: actions/checkout@v6",
      "      - name: Other step",
      "        run: echo before",
      ...runStepLines
    ];

    const violations = findChangelogCoverageCheckoutViolations("workflow.yml", lines);

    expect(violations).toHaveLength(1);
    expect(violations[0].message).toContain("preceding full-history checkout");
  });

  test("requires the full-history checkout to precede changelog coverage", () => {
    const lines = [
      "jobs:",
      "  changelog:",
      "    runs-on: ubuntu-latest",
      "    steps:",
      "      - name: Validate changelog coverage",
      "        run: node scripts/validate-changelog.js --check-coverage",
      "      - name: Checkout repository",
      "        uses: actions/checkout@v6",
      "        with:",
      "          fetch-depth: 0"
    ];

    const violations = findChangelogCoverageCheckoutViolations("workflow.yml", lines);

    expect(violations).toHaveLength(1);
  });

  test("uses the most recent checkout before changelog coverage", () => {
    const lines = [
      "jobs:",
      "  changelog:",
      "    runs-on: ubuntu-latest",
      "    steps:",
      "      - name: Full checkout",
      "        uses: actions/checkout@v6",
      "        with:",
      "          fetch-depth: 0",
      "      - name: Later shallow checkout",
      "        uses: actions/checkout@v6",
      "      - name: Validate changelog coverage",
      "        run: node scripts/validate-changelog.js --check-coverage"
    ];

    const violations = findChangelogCoverageCheckoutViolations("workflow.yml", lines);

    expect(violations).toHaveLength(1);
  });

  test("shorthand shallow checkout after full checkout invalidates changelog coverage", () => {
    const lines = [
      "jobs:",
      "  changelog:",
      "    runs-on: ubuntu-latest",
      "    steps:",
      "      - name: Full checkout",
      "        uses: actions/checkout@v6",
      "        with:",
      "          fetch-depth: 0",
      "      - uses: actions/checkout@v6",
      "      - name: Validate changelog coverage",
      "        run: node scripts/validate-changelog.js --check-coverage"
    ];

    const violations = findChangelogCoverageCheckoutViolations("workflow.yml", lines);

    expect(violations).toHaveLength(1);
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
        "          npm ci"
      ].join("\n");

      fs.writeFileSync(workflowPath, workflowContent, "utf8");
      const isIgnoredPathMock = (_repoRoot, candidatePath) => candidatePath === "package-lock.json";
      const violations = validateWorkflow(workflowPath, {
        repoRoot: tempDir,
        isIgnoredPathFn: isIgnoredPathMock
      });

      const errorMessages = violations
        .filter((violation) => violation.severity === "error")
        .map((violation) => violation.message);

      expect(errorMessages.some((message) => message.includes("ignored by git"))).toBe(true);
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
          "      - package-lock.json"
        ].join("\n"),
        "utf8"
      );

      const violations = validateWorkflow(workflowPath, {
        repoRoot: tempDir,
        isIgnoredPathFn: () => {
          throw new Error("mock git failure");
        }
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

  test("uses provided repoRoot when reporting violation file paths", () => {
    const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), "validate-workflows-repo-root-"));
    try {
      const workflowDir = path.join(tempDir, ".github", "workflows");
      fs.mkdirSync(workflowDir, { recursive: true });

      const workflowPath = path.join(workflowDir, "relative-path.yml");
      fs.writeFileSync(
        workflowPath,
        [
          "name: Relative Path",
          "jobs:",
          "  lint:",
          "    runs-on: ubuntu-latest",
          "    steps:",
          "      - run: git add --renormalize -- '*.md' '*.json'"
        ].join("\n"),
        "utf8"
      );

      const violations = validateWorkflow(workflowPath, {
        repoRoot: tempDir,
        isIgnoredPathFn: () => false
      });

      expect(violations.length).toBeGreaterThan(0);
      expect(
        violations.every((violation) => violation.file === ".github/workflows/relative-path.yml")
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

  test("pre-commit tooling workflow invokes pre-push-only parser hook at pre-push stage", () => {
    const workflowPath = path.resolve(
      __dirname,
      "../../.github/workflows/pre-commit-tooling-check.yml"
    );
    const workflowContent = fs.readFileSync(workflowPath, "utf8");

    expect(workflowContent).toContain(
      "pre-commit run --hook-stage pre-push script-parser-tests --all-files"
    );
    expect(workflowContent).not.toContain("pre-commit run script-parser-tests --all-files");
  });

  test("hook performance workflow provisions dotnet tools before measuring C# hooks", () => {
    const workflowPath = path.resolve(
      __dirname,
      "../../.github/workflows/hook-perf-measurement.yml"
    );
    const workflowContent = fs.readFileSync(workflowPath, "utf8");
    const setupDotnetIndex = workflowContent.indexOf("actions/setup-dotnet@v5");
    const restoreIndex = workflowContent.indexOf("dotnet tool restore");
    const diagnosticsIndex = workflowContent.indexOf("dotnet tool list");
    const measureIndex = workflowContent.indexOf("node scripts/measure-hook-wallclock.js");

    expect(setupDotnetIndex).toBeGreaterThanOrEqual(0);
    expect(restoreIndex).toBeGreaterThanOrEqual(0);
    expect(diagnosticsIndex).toBeGreaterThanOrEqual(0);
    expect(measureIndex).toBeGreaterThanOrEqual(0);
    expect(workflowContent).toContain('dotnet-version: "8.0.x"');
    expect(setupDotnetIndex).toBeLessThan(restoreIndex);
    expect(restoreIndex).toBeLessThan(measureIndex);
    expect(diagnosticsIndex).toBeLessThan(measureIndex);
  });

  test("release drafter lets version resolver choose the draft version from labels", () => {
    const workflowPath = path.resolve(__dirname, "../../.github/workflows/release-drafter.yml");
    const workflowContent = fs.readFileSync(workflowPath, "utf8");

    expect(workflowContent).toContain("uses: release-drafter/release-drafter@v7");
    expect(workflowContent).not.toMatch(
      /\bversion:\s*\$\{\{\s*steps\.version\.outputs\.version\s*\}\}/
    );
    expect(workflowContent).not.toContain("name: version");
    expect(workflowContent).not.toContain("id: version");
  });

  test("release drafter body uses Unreleased changelog without changing the draft tag", () => {
    const workflowPath = path.resolve(__dirname, "../../.github/workflows/release-drafter.yml");
    const workflowContent = fs.readFileSync(workflowPath, "utf8");

    expect(workflowContent).toContain('awk -v ver="Unreleased"');
    expect(workflowContent).toContain("CHANGELOG_SECTION");
    expect(workflowContent).not.toMatch(/\btag_name:\s*version\b/);
    expect(workflowContent).not.toMatch(/\bname:\s*version\b/);
  });

  test("workflow Node versions satisfy current JavaScript toolchain engines", () => {
    const workflowDirs = [
      path.resolve(__dirname, "../../.github/workflows"),
      path.resolve(__dirname, "../../.github/workflows-disabled")
    ];

    const nodeVersionEntries = [];
    for (const workflowsDir of workflowDirs) {
      const workflowFiles = fs
        .readdirSync(workflowsDir)
        .filter((fileName) => fileName.endsWith(".yml") || fileName.endsWith(".yaml"));

      for (const workflowFile of workflowFiles) {
        const workflowPath = path.join(workflowsDir, workflowFile);
        const workflowContent = fs.readFileSync(workflowPath, "utf8");
        const matches = workflowContent.matchAll(/node-version:\s*["']?([^"'\s#]+)["']?/g);
        for (const match of matches) {
          nodeVersionEntries.push({
            workflowFile: path.relative(path.resolve(__dirname, "../.."), workflowPath),
            version: match[1]
          });
        }
      }
    }

    expect(nodeVersionEntries.length).toBeGreaterThan(0);
    for (const entry of nodeVersionEntries) {
      expect(entry.version).toBe("22.18.0");
    }
  });
});
