/**
 * @fileoverview Tests for the three new validator checks added to
 * scripts/validate-workflows.js after the unity matrix-eviction incident:
 *
 *   - findForbiddenSharedConcurrencyViolations  (sentinel guard)
 *   - findMatrixConcurrencyEvictionViolations   (matrix-without-expansion guard)
 *   - findSelfHostedLabelAllowlistViolations    (self-hosted label allowlist)
 *
 * Each suite covers positive (clean), negative (each violation form), and
 * the order-insensitive equivalence required by the allowlist comparison.
 */

"use strict";

const {
  findForbiddenSharedConcurrencyViolations,
  findMatrixConcurrencyEvictionViolations,
  findSelfHostedLabelAllowlistViolations,
  findDynamicRunsOnMissingNeedsViolations,
  extractEmittedLabelSetsFromBash,
  extractJobConcurrencyGroup,
  extractWorkflowConcurrencyGroup,
  extractJobMatrixMaxParallel,
  extractJobNeeds,
  parseInlineLabelArray,
  extractJobs,
} = require("../validate-workflows.js");

function asLines(text) {
  // Strip a single leading blank line so test fixtures can start with `\n`.
  const trimmed = text.replace(/^\n/, "");
  return trimmed.split("\n");
}

describe("findForbiddenSharedConcurrencyViolations", () => {
  test("flags the wallstop-organization-builds sentinel group", () => {
    const lines = asLines(`
jobs:
  unity-tests:
    runs-on: [self-hosted, Windows, RAM-64GB]
    concurrency:
      group: wallstop-organization-builds
      cancel-in-progress: false
    steps:
      - run: echo hi
`);

    const violations = findForbiddenSharedConcurrencyViolations("test.yml", lines);
    expect(violations).toHaveLength(1);
    expect(violations[0].severity).toBe("error");
    expect(violations[0].message).toContain("wallstop-organization-builds");
    expect(violations[0].message).toContain("reserved sentinel");
    // Line citation points at the group: line within the fixture.
    expect(violations[0].line).toBe(5);
  });

  test("clean workflow with no concurrency block produces no violations", () => {
    const lines = asLines(`
jobs:
  unity-tests:
    runs-on: [self-hosted, Windows, RAM-64GB]
    steps:
      - run: echo hi
`);
    expect(findForbiddenSharedConcurrencyViolations("test.yml", lines)).toEqual([]);
  });

  test("clean workflow with a non-sentinel concurrency.group produces no violations", () => {
    const lines = asLines(`
jobs:
  unity-tests:
    runs-on: [self-hosted, Windows, RAM-64GB]
    concurrency:
      group: \${{ github.workflow }}-\${{ github.ref }}
      cancel-in-progress: true
    steps:
      - run: echo hi
`);
    expect(findForbiddenSharedConcurrencyViolations("test.yml", lines)).toEqual([]);
  });

  test("flags sentinel in inline concurrency mapping form", () => {
    const lines = asLines(`
jobs:
  unity-tests:
    runs-on: [self-hosted, Windows, RAM-64GB]
    concurrency: { group: wallstop-organization-builds, cancel-in-progress: false }
    steps:
      - run: echo hi
`);
    const violations = findForbiddenSharedConcurrencyViolations("test.yml", lines);
    expect(violations).toHaveLength(1);
    expect(violations[0].message).toContain("wallstop-organization-builds");
  });
});

describe("findMatrixConcurrencyEvictionViolations", () => {
  test("flags matrix job with a shared concurrency.group and no max-parallel declaration", () => {
    const lines = asLines(`
jobs:
  unity:
    runs-on: [self-hosted, Windows, RAM-64GB]
    concurrency:
      group: shared-unity-lock
      cancel-in-progress: false
    strategy:
      matrix:
        unity-version:
          - "2021.3.45f1"
          - "2022.3.45f1"
    steps:
      - run: echo hi
`);

    const violations = findMatrixConcurrencyEvictionViolations("test.yml", lines);
    expect(violations).toHaveLength(1);
    expect(violations[0].severity).toBe("error");
    expect(violations[0].message).toContain("shared-unity-lock");
    expect(violations[0].message).toContain("\${{ matrix.* }}");
    expect(violations[0].message).toContain("max-parallel: 1");
    expect(violations[0].message).toContain("no strategy.max-parallel declaration");
  });

  test("flags matrix job with a shared concurrency.group and max-parallel > 1", () => {
    const lines = asLines(`
jobs:
  unity:
    runs-on: [self-hosted, Windows, RAM-64GB]
    concurrency:
      group: shared-unity-lock
      cancel-in-progress: false
    strategy:
      max-parallel: 2
      matrix:
        unity-version:
          - "2021.3.45f1"
          - "2022.3.45f1"
    steps:
      - run: echo hi
`);

    const violations = findMatrixConcurrencyEvictionViolations("test.yml", lines);
    expect(violations).toHaveLength(1);
    expect(violations[0].severity).toBe("error");
    expect(violations[0].message).toContain("shared-unity-lock");
    expect(violations[0].message).toContain("strategy.max-parallel: 2");
  });

  test("allows matrix job whose concurrency.group expands ${{ matrix.unity-version }}", () => {
    const lines = asLines(`
jobs:
  unity:
    runs-on: [self-hosted, Windows, RAM-64GB]
    concurrency:
      group: unity-\${{ matrix.unity-version }}-\${{ matrix.test-mode }}
      cancel-in-progress: false
    strategy:
      matrix:
        unity-version:
          - "2021.3.45f1"
        test-mode:
          - editmode
    steps:
      - run: echo hi
`);
    expect(findMatrixConcurrencyEvictionViolations("test.yml", lines)).toEqual([]);
  });

  test("allows matrix job with shared concurrency.group when strategy.max-parallel: 1 is declared", () => {
    // This is the canonical Unity-Pro-license configuration: all four
    // Unity-credential-using jobs share `unity-pro-license` and rely on
    // matrix-internal serialization (`max-parallel: 1`) so matrix entries
    // never compete for the same group slot.
    const lines = asLines(`
jobs:
  unity:
    runs-on: [self-hosted, Windows, RAM-64GB]
    concurrency:
      group: unity-pro-license
      cancel-in-progress: false
    strategy:
      fail-fast: false
      max-parallel: 1
      matrix:
        unity-version:
          - "2021.3.45f1"
          - "2022.3.45f1"
        test-mode:
          - editmode
          - playmode
    steps:
      - run: echo hi
`);
    expect(findMatrixConcurrencyEvictionViolations("test.yml", lines)).toEqual([]);
  });

  test("allows matrix job with no concurrency.group at all", () => {
    const lines = asLines(`
jobs:
  unity:
    runs-on: [self-hosted, Windows, RAM-64GB]
    strategy:
      matrix:
        unity-version:
          - "2021.3.45f1"
    steps:
      - run: echo hi
`);
    expect(findMatrixConcurrencyEvictionViolations("test.yml", lines)).toEqual([]);
  });

  test("allows non-matrix job with a static concurrency.group", () => {
    const lines = asLines(`
jobs:
  release:
    runs-on: ubuntu-latest
    concurrency:
      group: release-lock
      cancel-in-progress: false
    steps:
      - run: echo hi
`);
    expect(findMatrixConcurrencyEvictionViolations("test.yml", lines)).toEqual([]);
  });
});

describe("extractJobMatrixMaxParallel", () => {
  function jobOf(text) {
    const lines = asLines(text);
    const jobs = extractJobs(lines);
    return { lines, job: jobs[0] };
  }

  test("returns the integer value for `max-parallel: 1`", () => {
    const { lines, job } = jobOf(`
jobs:
  unity:
    runs-on: [self-hosted, Windows, RAM-64GB]
    strategy:
      max-parallel: 1
      matrix:
        unity-version:
          - "2021.3.45f1"
    steps:
      - run: echo hi
`);
    expect(extractJobMatrixMaxParallel(lines, job)).toBe(1);
  });

  test("returns the integer value for `max-parallel: 4`", () => {
    const { lines, job } = jobOf(`
jobs:
  unity:
    runs-on: ubuntu-latest
    strategy:
      max-parallel: 4
      matrix:
        node:
          - 20
          - 22
    steps:
      - run: echo hi
`);
    expect(extractJobMatrixMaxParallel(lines, job)).toBe(4);
  });

  test("returns the integer for a double-quoted scalar `max-parallel: \"1\"`", () => {
    const { lines, job } = jobOf(`
jobs:
  unity:
    runs-on: ubuntu-latest
    strategy:
      max-parallel: "1"
      matrix:
        node:
          - 20
    steps:
      - run: echo hi
`);
    expect(extractJobMatrixMaxParallel(lines, job)).toBe(1);
  });

  test("returns the integer for a single-quoted scalar `max-parallel: '1'`", () => {
    const { lines, job } = jobOf(`
jobs:
  unity:
    runs-on: ubuntu-latest
    strategy:
      max-parallel: '1'
      matrix:
        node:
          - 20
    steps:
      - run: echo hi
`);
    expect(extractJobMatrixMaxParallel(lines, job)).toBe(1);
  });

  test("returns null when `max-parallel:` is absent", () => {
    const { lines, job } = jobOf(`
jobs:
  unity:
    runs-on: ubuntu-latest
    strategy:
      matrix:
        node:
          - 20
    steps:
      - run: echo hi
`);
    expect(extractJobMatrixMaxParallel(lines, job)).toBeNull();
  });

  test("returns null when the value is non-integer (expression form)", () => {
    // We refuse to statically resolve ${{ ... }} expressions; a dynamic
    // value cannot be guaranteed to be 1.
    const { lines, job } = jobOf(`
jobs:
  unity:
    runs-on: ubuntu-latest
    strategy:
      max-parallel: \${{ vars.MAX_PARALLEL }}
      matrix:
        node:
          - 20
    steps:
      - run: echo hi
`);
    expect(extractJobMatrixMaxParallel(lines, job)).toBeNull();
  });

  test("returns null when there is no strategy block", () => {
    const { lines, job } = jobOf(`
jobs:
  unity:
    runs-on: ubuntu-latest
    steps:
      - run: echo hi
`);
    expect(extractJobMatrixMaxParallel(lines, job)).toBeNull();
  });
});

describe("findSelfHostedLabelAllowlistViolations", () => {
  test("allows the standard inline-array Windows-64GB label set", () => {
    const lines = asLines(`
jobs:
  unity:
    runs-on: [self-hosted, Windows, RAM-64GB]
    steps:
      - run: echo hi
`);
    expect(findSelfHostedLabelAllowlistViolations("test.yml", lines)).toEqual([]);
  });

  test("allows the fast Windows-64GB label set", () => {
    const lines = asLines(`
jobs:
  unity:
    runs-on: [self-hosted, Windows, RAM-64GB, fast]
    steps:
      - run: echo hi
`);
    expect(findSelfHostedLabelAllowlistViolations("test.yml", lines)).toEqual([]);
  });

  test("treats label order as insignificant (order-insensitive equivalence)", () => {
    const lines = asLines(`
jobs:
  a:
    runs-on: [Windows, RAM-64GB, self-hosted]
    steps:
      - run: echo a
  b:
    runs-on: [fast, Windows, RAM-64GB, self-hosted]
    steps:
      - run: echo b
`);
    expect(findSelfHostedLabelAllowlistViolations("test.yml", lines)).toEqual([]);
  });

  test("flags typo'd casing such as RAM-64Gb", () => {
    const lines = asLines(`
jobs:
  unity:
    runs-on: [self-hosted, Windows, RAM-64Gb]
    steps:
      - run: echo hi
`);
    const violations = findSelfHostedLabelAllowlistViolations("test.yml", lines);
    expect(violations).toHaveLength(1);
    expect(violations[0].severity).toBe("error");
    expect(violations[0].message).toContain("RAM-64Gb");
    expect(violations[0].message).toContain("not in the documented allowlist");
  });

  test("ignores hosted runners (no self-hosted label)", () => {
    const lines = asLines(`
jobs:
  ubuntu:
    runs-on: ubuntu-latest
    steps:
      - run: echo hi
  ubuntu-array:
    runs-on: [ubuntu-latest, large]
    steps:
      - run: echo hi
`);
    expect(findSelfHostedLabelAllowlistViolations("test.yml", lines)).toEqual([]);
  });

  test("flags multi-line block list self-hosted label sets that drift from allowlist", () => {
    const lines = asLines(`
jobs:
  unity:
    runs-on:
      - self-hosted
      - Windows
      - RAM-128GB
    steps:
      - run: echo hi
`);
    const violations = findSelfHostedLabelAllowlistViolations("test.yml", lines);
    expect(violations).toHaveLength(1);
    expect(violations[0].message).toContain("RAM-128GB");
  });

  test("accepts dynamic ${{ fromJSON(...) }} runs-on whose emitter produces allowlisted sets", () => {
    const lines = asLines(`
jobs:
  matrix-config:
    runs-on: ubuntu-latest
    outputs:
      runner-labels: \${{ steps.runners.outputs.labels }}
    steps:
      - id: runners
        shell: bash
        run: |
          if [[ "\${{ github.event_name }}" == "pull_request" ]]; then
            echo 'labels=["self-hosted","Windows","RAM-64GB","fast"]' >> "$GITHUB_OUTPUT"
          else
            echo 'labels=["self-hosted","Windows","RAM-64GB"]' >> "$GITHUB_OUTPUT"
          fi
  unity:
    needs: matrix-config
    runs-on: \${{ fromJSON(needs.matrix-config.outputs.runner-labels) }}
    steps:
      - run: echo hi
`);
    expect(findSelfHostedLabelAllowlistViolations("test.yml", lines)).toEqual([]);
  });

  test("flags dynamic runs-on whose emitter produces a forbidden label set", () => {
    const lines = asLines(`
jobs:
  matrix-config:
    runs-on: ubuntu-latest
    outputs:
      runner-labels: \${{ steps.runners.outputs.labels }}
    steps:
      - id: runners
        shell: bash
        run: |
          if [[ "\${{ github.event_name }}" == "pull_request" ]]; then
            echo 'labels=["self-hosted","Windows","RAM-64Gb"]' >> "$GITHUB_OUTPUT"
          else
            echo 'labels=["self-hosted","Windows","RAM-64GB"]' >> "$GITHUB_OUTPUT"
          fi
  unity:
    needs: matrix-config
    runs-on: \${{ fromJSON(needs.matrix-config.outputs.runner-labels) }}
    steps:
      - run: echo hi
`);
    const violations = findSelfHostedLabelAllowlistViolations("test.yml", lines);
    expect(violations.length).toBeGreaterThanOrEqual(1);
    expect(violations.some((v) => v.message.includes("RAM-64Gb"))).toBe(true);
  });

  test("flags dynamic runs-on whose emitter produces no labels= lines", () => {
    const lines = asLines(`
jobs:
  matrix-config:
    runs-on: ubuntu-latest
    outputs:
      runner-labels: \${{ steps.runners.outputs.labels }}
    steps:
      - id: runners
        shell: bash
        run: |
          echo "no labels declared here"
  unity:
    needs: matrix-config
    runs-on: \${{ fromJSON(needs.matrix-config.outputs.runner-labels) }}
    steps:
      - run: echo hi
`);
    const violations = findSelfHostedLabelAllowlistViolations("test.yml", lines);
    expect(violations).toHaveLength(1);
    expect(violations[0].message).toContain("emits no 'labels=[...]'");
  });
});

describe("extractEmittedLabelSetsFromBash", () => {
  test("parses single-quoted labels= JSON arrays from echo statements", () => {
    const runText = [
      "if [[ \"x\" == \"pull_request\" ]]; then",
      "  echo 'labels=[\"self-hosted\",\"Windows\",\"RAM-64GB\",\"fast\"]' >> \"$GITHUB_OUTPUT\"",
      "else",
      "  echo 'labels=[\"self-hosted\",\"Windows\",\"RAM-64GB\"]' >> \"$GITHUB_OUTPUT\"",
      "fi"
    ].join("\n");
    expect(extractEmittedLabelSetsFromBash(runText)).toEqual([
      ["self-hosted", "Windows", "RAM-64GB", "fast"],
      ["self-hosted", "Windows", "RAM-64GB"]
    ]);
  });

  test("parses a bare labels=... assignment without surrounding bash quotes", () => {
    const runText = "labels=[\"self-hosted\",\"Windows\",\"RAM-64GB\"]";
    expect(extractEmittedLabelSetsFromBash(runText)).toEqual([
      ["self-hosted", "Windows", "RAM-64GB"]
    ]);
  });

  test("returns empty array when no labels= line is present", () => {
    expect(extractEmittedLabelSetsFromBash("echo hi")).toEqual([]);
  });

  test("returns null entry for malformed JSON in a labels= assignment", () => {
    const runText = "labels=[not-json]";
    expect(extractEmittedLabelSetsFromBash(runText)).toEqual([null]);
  });

  test("tolerates a label literal that contains a `]` character inside a JSON string", () => {
    // The previous regex `[^\]]*` would stop at the first `]`. With balanced-
    // bracket scanning that respects JSON string spans, the inner `]` is
    // captured as part of the label literal.
    const runText = `labels=["self-hosted","weird]name","RAM-64GB"]`;
    expect(extractEmittedLabelSetsFromBash(runText)).toEqual([
      ["self-hosted", "weird]name", "RAM-64GB"]
    ]);
  });
});

describe("scalar shorthand concurrency form (extractJobConcurrencyGroup)", () => {
  function singleJobLines(concurrencyLine) {
    return [
      "jobs:",
      "  unity-tests:",
      "    runs-on: [self-hosted, Windows, RAM-64GB]",
      `    ${concurrencyLine}`,
      "    steps:",
      "      - run: echo hi"
    ];
  }

  test("recognizes bare scalar concurrency: wallstop-organization-builds", () => {
    const lines = singleJobLines("concurrency: wallstop-organization-builds");
    const jobs = extractJobs(lines);
    const result = extractJobConcurrencyGroup(lines, jobs[0]);
    expect(result).not.toBeNull();
    expect(result.group).toBe("wallstop-organization-builds");
    expect(result.cancelInProgress).toBeUndefined();
  });

  test("recognizes double-quoted scalar concurrency: \"wallstop-organization-builds\"", () => {
    const lines = singleJobLines('concurrency: "wallstop-organization-builds"');
    const jobs = extractJobs(lines);
    const result = extractJobConcurrencyGroup(lines, jobs[0]);
    expect(result).not.toBeNull();
    expect(result.group).toBe("wallstop-organization-builds");
  });

  test("recognizes single-quoted scalar concurrency: 'wallstop-organization-builds'", () => {
    const lines = singleJobLines("concurrency: 'wallstop-organization-builds'");
    const jobs = extractJobs(lines);
    const result = extractJobConcurrencyGroup(lines, jobs[0]);
    expect(result).not.toBeNull();
    expect(result.group).toBe("wallstop-organization-builds");
  });

  test("returns null for `concurrency: ~` (YAML null) and bare empty value", () => {
    const linesNull = singleJobLines("concurrency: ~");
    const linesEmpty = [
      "jobs:",
      "  unity-tests:",
      "    runs-on: [self-hosted, Windows, RAM-64GB]",
      "    concurrency:",
      "    steps:",
      "      - run: echo hi"
    ];
    const jobsNull = extractJobs(linesNull);
    const jobsEmpty = extractJobs(linesEmpty);
    expect(extractJobConcurrencyGroup(linesNull, jobsNull[0])).toBeNull();
    expect(extractJobConcurrencyGroup(linesEmpty, jobsEmpty[0])).toBeNull();
  });
});

describe("findForbiddenSharedConcurrencyViolations: shorthand and workflow-level", () => {
  test("flags scalar shorthand at job level (bare)", () => {
    const lines = asLines(`
jobs:
  unity-tests:
    runs-on: [self-hosted, Windows, RAM-64GB]
    concurrency: wallstop-organization-builds
    steps:
      - run: echo hi
`);
    const violations = findForbiddenSharedConcurrencyViolations("test.yml", lines);
    expect(violations).toHaveLength(1);
    expect(violations[0].message).toContain("wallstop-organization-builds");
    expect(violations[0].message).toContain("reserved sentinel");
  });

  test("flags scalar shorthand at job level (double-quoted)", () => {
    const lines = asLines(`
jobs:
  unity-tests:
    runs-on: [self-hosted, Windows, RAM-64GB]
    concurrency: "wallstop-organization-builds"
    steps:
      - run: echo hi
`);
    const violations = findForbiddenSharedConcurrencyViolations("test.yml", lines);
    expect(violations).toHaveLength(1);
    expect(violations[0].message).toContain("wallstop-organization-builds");
  });

  test("flags scalar shorthand at job level (single-quoted)", () => {
    const lines = asLines(`
jobs:
  unity-tests:
    runs-on: [self-hosted, Windows, RAM-64GB]
    concurrency: 'wallstop-organization-builds'
    steps:
      - run: echo hi
`);
    const violations = findForbiddenSharedConcurrencyViolations("test.yml", lines);
    expect(violations).toHaveLength(1);
    expect(violations[0].message).toContain("wallstop-organization-builds");
  });

  test("flags workflow-level inline mapping with sentinel name", () => {
    const lines = asLines(`
name: Unity Tests
concurrency:
  group: wallstop-organization-builds
  cancel-in-progress: false
jobs:
  unity-tests:
    runs-on: [self-hosted, Windows, RAM-64GB]
    steps:
      - run: echo hi
`);
    const violations = findForbiddenSharedConcurrencyViolations("test.yml", lines);
    expect(violations).toHaveLength(1);
    expect(violations[0].message).toContain("Workflow-level");
    expect(violations[0].message).toContain("wallstop-organization-builds");
  });

  test("flags workflow-level scalar shorthand with sentinel name", () => {
    const lines = asLines(`
name: Unity Tests
concurrency: wallstop-organization-builds
jobs:
  unity-tests:
    runs-on: [self-hosted, Windows, RAM-64GB]
    steps:
      - run: echo hi
`);
    const violations = findForbiddenSharedConcurrencyViolations("test.yml", lines);
    expect(violations).toHaveLength(1);
    expect(violations[0].message).toContain("Workflow-level");
    expect(violations[0].message).toContain("wallstop-organization-builds");
  });

  test("allows workflow-level non-sentinel concurrency", () => {
    const lines = asLines(`
name: Unity Tests
concurrency:
  group: \${{ github.workflow }}-\${{ github.ref }}
  cancel-in-progress: true
jobs:
  unity-tests:
    runs-on: [self-hosted, Windows, RAM-64GB]
    steps:
      - run: echo hi
`);
    expect(findForbiddenSharedConcurrencyViolations("test.yml", lines)).toEqual([]);
  });
});

describe("extractWorkflowConcurrencyGroup", () => {
  test("returns group + line for inline mapping workflow-level concurrency", () => {
    const lines = asLines(`
name: Unity Tests
concurrency: { group: foo, cancel-in-progress: false }
jobs:
  a:
    runs-on: ubuntu-latest
    steps:
      - run: echo
`);
    const result = extractWorkflowConcurrencyGroup(lines);
    expect(result).not.toBeNull();
    expect(result.group).toBe("foo");
  });

  test("returns null when no workflow-level concurrency exists", () => {
    const lines = asLines(`
name: Unity Tests
jobs:
  a:
    runs-on: ubuntu-latest
    concurrency: foo
    steps:
      - run: echo
`);
    expect(extractWorkflowConcurrencyGroup(lines)).toBeNull();
  });
});

describe("findMatrixConcurrencyEvictionViolations: scalar shorthand on a matrix job", () => {
  test("flags scalar shorthand on a matrix job missing matrix expansion", () => {
    const lines = asLines(`
jobs:
  unity:
    runs-on: [self-hosted, Windows, RAM-64GB]
    concurrency: shared-unity-lock
    strategy:
      matrix:
        unity-version:
          - "2021.3.45f1"
          - "2022.3.45f1"
    steps:
      - run: echo hi
`);
    const violations = findMatrixConcurrencyEvictionViolations("test.yml", lines);
    expect(violations).toHaveLength(1);
    expect(violations[0].message).toContain("shared-unity-lock");
  });

  test("allows scalar shorthand on a matrix job whose group expands ${{ matrix.* }}", () => {
    const lines = asLines(`
jobs:
  unity:
    runs-on: [self-hosted, Windows, RAM-64GB]
    concurrency: unity-\${{ matrix.unity-version }}
    strategy:
      matrix:
        unity-version:
          - "2021.3.45f1"
    steps:
      - run: echo hi
`);
    expect(findMatrixConcurrencyEvictionViolations("test.yml", lines)).toEqual([]);
  });
});

describe("findSelfHostedLabelAllowlistViolations: extra coverage", () => {
  test("flags `runs-on: 'self-hosted'` scalar quoted form for missing modifiers", () => {
    const lines = asLines(`
jobs:
  unity:
    runs-on: 'self-hosted'
    steps:
      - run: echo hi
`);
    const violations = findSelfHostedLabelAllowlistViolations("test.yml", lines);
    expect(violations).toHaveLength(1);
    expect(violations[0].message).toContain("not in the documented allowlist");
  });

  test("flags `runs-on: \"self-hosted\"` scalar double-quoted form for missing modifiers", () => {
    const lines = asLines(`
jobs:
  unity:
    runs-on: "self-hosted"
    steps:
      - run: echo hi
`);
    const violations = findSelfHostedLabelAllowlistViolations("test.yml", lines);
    expect(violations).toHaveLength(1);
    expect(violations[0].message).toContain("not in the documented allowlist");
  });

  test("flags `runs-on: self-hosted` scalar bare form for missing modifiers", () => {
    const lines = asLines(`
jobs:
  unity:
    runs-on: self-hosted
    steps:
      - run: echo hi
`);
    const violations = findSelfHostedLabelAllowlistViolations("test.yml", lines);
    expect(violations).toHaveLength(1);
    expect(violations[0].message).toContain("not in the documented allowlist");
  });

  test("flags trailing-comma inline label array with a clear error", () => {
    const lines = asLines(`
jobs:
  unity:
    runs-on: [self-hosted, Windows, RAM-64GB,]
    steps:
      - run: echo hi
`);
    const violations = findSelfHostedLabelAllowlistViolations("test.yml", lines);
    expect(violations).toHaveLength(1);
    expect(violations[0].message).toContain("Trailing or duplicate comma");
  });

  test("matrix-include-only fixture: matrix.include emits allowlisted self-hosted entries", () => {
    // Even when the matrix uses include-only syntax (no top-level matrix
    // dimensions), the self-hosted label allowlist still applies to the
    // job's static runs-on declaration.
    const lines = asLines(`
jobs:
  unity:
    runs-on: [self-hosted, Windows, RAM-64GB, fast]
    strategy:
      matrix:
        include:
          - unity-version: "2021.3.45f1"
            test-mode: editmode
          - unity-version: "2022.3.45f1"
            test-mode: playmode
    steps:
      - run: echo hi
`);
    expect(findSelfHostedLabelAllowlistViolations("test.yml", lines)).toEqual([]);
  });

  test("multiple \${{ matrix.* }} tokens in one expansion are accepted for matrix-eviction check", () => {
    const lines = asLines(`
jobs:
  unity:
    runs-on: [self-hosted, Windows, RAM-64GB]
    concurrency:
      group: unity-\${{ matrix.unity-version }}-\${{ matrix.test-mode }}-\${{ matrix.platform }}
      cancel-in-progress: false
    strategy:
      matrix:
        unity-version:
          - "2021.3.45f1"
        test-mode:
          - editmode
        platform:
          - windows
    steps:
      - run: echo hi
`);
    expect(findMatrixConcurrencyEvictionViolations("test.yml", lines)).toEqual([]);
  });
});

describe("parseInlineLabelArray: trailing comma rejection", () => {
  test("throws for `[a, b, c,]` trailing comma", () => {
    expect(() => parseInlineLabelArray("[a, b, c,]")).toThrow(/Trailing or duplicate comma/);
  });

  test("throws for `[a,,b]` duplicate comma", () => {
    expect(() => parseInlineLabelArray("[a,,b]")).toThrow(/Trailing or duplicate comma/);
  });

  test("accepts well-formed `[a, b, c]`", () => {
    expect(parseInlineLabelArray("[a, b, c]")).toEqual(["a", "b", "c"]);
  });
});

describe("extractJobNeeds", () => {
  function jobsFrom(text) {
    return extractJobs(asLines(text));
  }

  test("returns [] when no needs declared", () => {
    const lines = asLines(`
jobs:
  a:
    runs-on: ubuntu-latest
    steps:
      - run: echo
`);
    expect(extractJobNeeds(lines, jobsFrom(`
jobs:
  a:
    runs-on: ubuntu-latest
    steps:
      - run: echo
`)[0])).toEqual([]);
  });

  test("parses scalar form needs: matrix-config", () => {
    const text = `
jobs:
  unity:
    needs: matrix-config
    runs-on: ubuntu-latest
    steps:
      - run: echo
`;
    const lines = asLines(text);
    expect(extractJobNeeds(lines, jobsFrom(text)[0])).toEqual(["matrix-config"]);
  });

  test("parses inline-array form needs: [a, b]", () => {
    const text = `
jobs:
  unity:
    needs: [matrix-config, validate]
    runs-on: ubuntu-latest
    steps:
      - run: echo
`;
    const lines = asLines(text);
    expect(extractJobNeeds(lines, jobsFrom(text)[0])).toEqual(["matrix-config", "validate"]);
  });

  test("parses multi-line block list form", () => {
    const text = `
jobs:
  unity:
    needs:
      - matrix-config
      - validate
    runs-on: ubuntu-latest
    steps:
      - run: echo
`;
    const lines = asLines(text);
    expect(extractJobNeeds(lines, jobsFrom(text)[0])).toEqual(["matrix-config", "validate"]);
  });
});

describe("findDynamicRunsOnMissingNeedsViolations", () => {
  test("flags dynamic runs-on whose target job is not in needs", () => {
    const lines = asLines(`
jobs:
  matrix-config:
    runs-on: ubuntu-latest
    outputs:
      runner-labels: \${{ steps.runners.outputs.labels }}
    steps:
      - id: runners
        run: |
          echo 'labels=["self-hosted","Windows","RAM-64GB"]' >> "$GITHUB_OUTPUT"
  unity:
    runs-on: \${{ fromJSON(needs.matrix-config.outputs.runner-labels) }}
    steps:
      - run: echo hi
`);
    const violations = findDynamicRunsOnMissingNeedsViolations("test.yml", lines);
    expect(violations).toHaveLength(1);
    expect(violations[0].message).toContain("matrix-config");
    expect(violations[0].message).toContain("not in the job's needs:");
  });

  test("accepts dynamic runs-on whose target job is in needs (scalar)", () => {
    const lines = asLines(`
jobs:
  matrix-config:
    runs-on: ubuntu-latest
    outputs:
      runner-labels: \${{ steps.runners.outputs.labels }}
    steps:
      - id: runners
        run: |
          echo 'labels=["self-hosted","Windows","RAM-64GB"]' >> "$GITHUB_OUTPUT"
  unity:
    needs: matrix-config
    runs-on: \${{ fromJSON(needs.matrix-config.outputs.runner-labels) }}
    steps:
      - run: echo hi
`);
    expect(findDynamicRunsOnMissingNeedsViolations("test.yml", lines)).toEqual([]);
  });

  test("accepts dynamic runs-on whose target job is in needs (inline array)", () => {
    const lines = asLines(`
jobs:
  matrix-config:
    runs-on: ubuntu-latest
    outputs:
      runner-labels: \${{ steps.runners.outputs.labels }}
    steps:
      - id: runners
        run: echo
  validate:
    runs-on: ubuntu-latest
    steps:
      - run: echo
  unity:
    needs: [matrix-config, validate]
    runs-on: \${{ fromJSON(needs.matrix-config.outputs.runner-labels) }}
    steps:
      - run: echo hi
`);
    expect(findDynamicRunsOnMissingNeedsViolations("test.yml", lines)).toEqual([]);
  });

  test("ignores jobs that do not use the dynamic fromJSON pattern", () => {
    const lines = asLines(`
jobs:
  unity:
    runs-on: ubuntu-latest
    steps:
      - run: echo hi
`);
    expect(findDynamicRunsOnMissingNeedsViolations("test.yml", lines)).toEqual([]);
  });
});

describe("historical sentinel string remains searchable in error messages", () => {
  // The reviewer asked that future log readers be able to grep CI failure
  // text for the historical 'wallstop-organization-builds' incident name.
  // This test asserts at least one validator code path mentions it.
  test("at least one violation message in the sentinel-guard pipeline contains the incident name", () => {
    const lines = asLines(`
jobs:
  unity-tests:
    runs-on: [self-hosted, Windows, RAM-64GB]
    concurrency:
      group: wallstop-organization-builds
      cancel-in-progress: false
    steps:
      - run: echo hi
`);
    const violations = findForbiddenSharedConcurrencyViolations("test.yml", lines);
    expect(violations.some((v) => v.message.includes("wallstop-organization-builds"))).toBe(true);
  });
});
