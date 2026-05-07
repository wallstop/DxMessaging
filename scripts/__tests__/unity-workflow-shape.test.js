/**
 * @fileoverview Contract tests for the shape of Unity-related GitHub Actions
 * workflow templates.
 *
 * These workflows have non-obvious invariants that, if violated, cause silent
 * regressions:
 *   - game-ci backed Unity workflows are temporarily moved out of
 *     .github/workflows while the GitHub-hosted game-ci jobs are disabled.
 *     Local runners remain available.
 *   - All Unity workflows must include manifest, packages-lock, and
 *     ProjectVersion in the exact Library cache key, with no broad restore
 *     keys — otherwise stale Library/ dirs from a prior Unity version corrupt
 *     the run.
 *   - The devcontainer workflows must override `eventFilterForPush: ""` to
 *     avoid devcontainers/ci@v0.3's silent push-skip on schedule/dispatch.
 *
 * We use js-yaml when available for the structural parts (the on: block in
 * particular) and fall back to text-grep for cross-cutting requirements that
 * are easier to verify line-by-line (cache key references, action versions).
 */

"use strict";

const fs = require("fs");
const path = require("path");
const yaml = require("js-yaml");

const REPO_ROOT = path.resolve(__dirname, "..", "..");
const WORKFLOWS_DIR = path.join(REPO_ROOT, ".github", "workflows");
const DISABLED_WORKFLOWS_DIR = path.join(REPO_ROOT, ".github", "workflows-disabled");
const DISABLED_UNITY_WORKFLOWS = ["unity-tests.yml", "unity-il2cpp.yml", "unity-benchmarks.yml"];

function readWorkflow(name) {
  const abs = path.join(WORKFLOWS_DIR, name);
  expect(fs.existsSync(abs)).toBe(true);
  return fs.readFileSync(abs, "utf8");
}

function readDisabledWorkflow(name) {
  const abs = path.join(DISABLED_WORKFLOWS_DIR, name);
  expect(fs.existsSync(abs)).toBe(true);
  return fs.readFileSync(abs, "utf8");
}

function loadWorkflowYaml(name) {
  const text = readWorkflow(name);
  // js-yaml interprets the bare `on` key as the YAML 1.1 boolean `true`
  // unless we use FAILSAFE / CORE schemas; load with default schema and
  // pull either key when present.
  return yaml.load(text);
}

function loadDisabledWorkflowYaml(name) {
  return yaml.load(readDisabledWorkflow(name));
}

function collectSteps(parsed) {
  return Object.values(parsed.jobs || {}).flatMap((job) => job.steps || []);
}

function collectDevcontainerCiSteps(name) {
  const parsed = loadWorkflowYaml(name);
  return collectSteps(parsed).filter((step) => step.uses === "devcontainers/ci@v0.3");
}

function expectExactUnityLibraryCache(text) {
  expect(text).toContain("actions/cache@v4");
  expect(text).toContain("manifest.json");
  expect(text).toContain("packages-lock.json");
  expect(text).toContain("ProjectVersion.txt");
  expect(text).toContain("key: Library");
  expect(text).not.toContain("restore-keys:");
}

describe("Unity game-ci workflows disabled in GitHub", () => {
  test.each(DISABLED_UNITY_WORKFLOWS)("%s is not an active GitHub workflow", (name) => {
    expect(fs.existsSync(path.join(WORKFLOWS_DIR, name))).toBe(false);
    expect(fs.existsSync(path.join(DISABLED_WORKFLOWS_DIR, name))).toBe(true);
  });
});

describe(".github/workflows-disabled/unity-tests.yml", () => {
  let text;
  let parsed;

  beforeAll(() => {
    text = readDisabledWorkflow("unity-tests.yml");
    parsed = loadDisabledWorkflowYaml("unity-tests.yml");
  });

  test("stays workflow_dispatch only as a disabled template", () => {
    const onBlock = parsed.on || parsed[true];
    expect(Object.keys(onBlock).sort()).toEqual(["workflow_dispatch"]);
    expect(parsed.jobs["matrix-config"].if).toBe("${{ false }}");
  });

  test("uses game-ci/unity-test-runner@v4", () => {
    expect(text).toContain("game-ci/unity-test-runner@v4");
  });

  test("references secrets.UNITY_LICENSE", () => {
    expect(text).toMatch(/secrets\.UNITY_LICENSE/);
  });

  test("references secrets.UNITY_SERIAL for paid serial activation", () => {
    expect(text).toMatch(/secrets\.UNITY_SERIAL/);
  });

  test("Library cache key references manifest.json, packages-lock.json, and ProjectVersion.txt", () => {
    expectExactUnityLibraryCache(text);
  });

  test("uses actions/upload-artifact@v7 (matches repo baseline)", () => {
    expect(text).toContain("actions/upload-artifact@v7");
  });
});

describe(".github/workflows-disabled/unity-il2cpp.yml", () => {
  let text;
  let parsed;

  beforeAll(() => {
    text = readDisabledWorkflow("unity-il2cpp.yml");
    parsed = loadDisabledWorkflowYaml("unity-il2cpp.yml");
  });

  test("stays workflow_dispatch only as a disabled template", () => {
    const onBlock = parsed.on || parsed[true];
    expect(Object.keys(onBlock).sort()).toEqual(["workflow_dispatch"]);
    expect(parsed.jobs["matrix-config"].if).toBe("${{ false }}");
  });

  test("uses game-ci/unity-builder@v4", () => {
    expect(text).toContain("game-ci/unity-builder@v4");
  });

  test("references secrets.UNITY_LICENSE", () => {
    expect(text).toMatch(/secrets\.UNITY_LICENSE/);
  });

  test("references secrets.UNITY_SERIAL for paid serial activation", () => {
    expect(text).toMatch(/secrets\.UNITY_SERIAL/);
  });

  test("Library cache key references manifest.json, packages-lock.json, and ProjectVersion.txt", () => {
    expectExactUnityLibraryCache(text);
  });

  test("uses actions/upload-artifact@v7", () => {
    expect(text).toContain("actions/upload-artifact@v7");
  });

  test("references secrets.UNITY_SERIAL for paid serial activation", () => {
    expect(text).toMatch(/secrets\.UNITY_SERIAL/);
  });
});

describe(".github/workflows-disabled/unity-benchmarks.yml", () => {
  let text;
  let parsed;

  beforeAll(() => {
    text = readDisabledWorkflow("unity-benchmarks.yml");
    parsed = loadDisabledWorkflowYaml("unity-benchmarks.yml");
  });

  test("`on:` block has ONLY workflow_dispatch as a disabled template", () => {
    // YAML 1.1 turns the bare `on` key into `true`; check both keys to
    // tolerate either representation across yaml/parser versions.
    const onBlock = parsed.on || parsed[true];
    expect(onBlock).toBeDefined();
    expect(typeof onBlock).toBe("object");

    const triggerKeys = Object.keys(onBlock).sort();
    expect(triggerKeys).toEqual(["workflow_dispatch"]);
    expect(parsed.jobs["matrix-config"].if).toBe("${{ false }}");

    // Belt-and-suspenders text grep: a stray `pull_request:` or `push:`
    // anywhere in the on: block (even commented-out or otherwise missed
    // by the structural walk above) should not appear at column-2
    // indentation.
    const onLineMatch = text.match(/^on:[\s\S]*?(?=^\w)/m);
    expect(onLineMatch).not.toBeNull();
    const onSection = onLineMatch[0];
    expect(onSection).not.toMatch(/^\s{2}pull_request:/m);
    expect(onSection).not.toMatch(/^\s{2}push:/m);
  });

  test("Library cache key references manifest.json, packages-lock.json, and ProjectVersion.txt", () => {
    expectExactUnityLibraryCache(text);
  });

  test("uses actions/upload-artifact@v7", () => {
    expect(text).toContain("actions/upload-artifact@v7");
  });
});

describe(".github/workflows/devcontainer-test.yml", () => {
  let text;
  let devcontainerCiSteps;

  beforeAll(() => {
    text = readWorkflow("devcontainer-test.yml");
    devcontainerCiSteps = collectDevcontainerCiSteps("devcontainer-test.yml");
  });

  test('contains eventFilterForPush: "" exactly (overrides devcontainers/ci@v0.3 default)', () => {
    // The default of "push" silently skips publishing on schedule /
    // workflow_dispatch — empty string makes the explicit `push:` knob
    // the single source of truth. Match either flow style or block style.
    expect(text).toMatch(/eventFilterForPush:\s*""/);
  });

  test("has one publishing build step and one smoke-test step", () => {
    expect(devcontainerCiSteps).toHaveLength(2);
    expect(devcontainerCiSteps.map((step) => step.with.push)).toEqual(["filter", "never"]);
    expect(devcontainerCiSteps[0].with.refFilterForPush).toBe("refs/heads/master");
    expect(devcontainerCiSteps[0].with.eventFilterForPush).toBe("");
    expect(devcontainerCiSteps[1].with.runCmd).toContain(
      "=== All devcontainer smoke checks passed ==="
    );
  });
});

describe(".github/workflows/devcontainer-prebuild.yml", () => {
  let text;
  let devcontainerCiSteps;

  beforeAll(() => {
    text = readWorkflow("devcontainer-prebuild.yml");
    devcontainerCiSteps = collectDevcontainerCiSteps("devcontainer-prebuild.yml");
  });

  test("builds locally, pushes explicitly, then verifies GHCR", () => {
    const pushNeverIndex = text.indexOf("push: never");
    const dockerPushIndex = text.indexOf('docker push "${IMAGE}"');
    const dockerPullIndex = text.indexOf('docker pull "${IMAGE}"');

    expect(pushNeverIndex).toBeGreaterThanOrEqual(0);
    expect(dockerPushIndex).toBeGreaterThan(pushNeverIndex);
    expect(dockerPullIndex).toBeGreaterThan(dockerPushIndex);
  });

  test("does not rely on devcontainers/ci post-action publishing", () => {
    expect(devcontainerCiSteps).toHaveLength(1);
    expect(devcontainerCiSteps[0].with.push).toBe("never");
    expect(devcontainerCiSteps[0].with.eventFilterForPush).toBeUndefined();
    expect(text).toContain('docker manifest inspect "${IMAGE}" > "${manifest_file}"');
  });
});

describe("devcontainer workflow GHCR publishing contract", () => {
  const expectedImage = "ghcr.io/${{ steps.repo.outputs.repository_lowercase }}/devcontainer";
  const workflowCases = [
    {
      name: "devcontainer-prebuild.yml",
      expectedActionPush: "never",
      requiresExplicitPushBeforePull: true,
      requiresEventFilterOverride: false
    },
    {
      name: "devcontainer-test.yml",
      expectedActionPush: "filter",
      requiresExplicitPushBeforePull: false,
      requiresEventFilterOverride: true
    }
  ];

  test.each(workflowCases)(
    "$name uses a lowercase GHCR image and current GHCR login action",
    ({ name }) => {
      const workflowText = readWorkflow(name);
      const devcontainerCiSteps = collectDevcontainerCiSteps(name);

      expect(workflowText).toContain("uses: docker/login-action@v4");
      expect(workflowText).toContain("id: repo");
      expect(workflowText).toContain("repository_lowercase=${repo_lower}");
      expect(workflowText).toContain("tr '[:upper:]' '[:lower:]'");
      expect(devcontainerCiSteps.map((step) => step.with.imageName)).toEqual(
        devcontainerCiSteps.map(() => expectedImage)
      );
      expect(devcontainerCiSteps.map((step) => step.with.cacheFrom)).toEqual(
        devcontainerCiSteps.map(() => expectedImage)
      );
      expect(workflowText).not.toContain("ghcr.io/${{ github.repository }}");
      expect(workflowText).not.toContain("ghcr.io/${{ github.repository_owner }}/");
      expect(workflowText).not.toContain("docker/login-action@v3");
    }
  );

  test.each(workflowCases)(
    "$name keeps devcontainers/ci push behavior compatible with verification",
    ({ name, expectedActionPush, requiresExplicitPushBeforePull, requiresEventFilterOverride }) => {
      const workflowText = readWorkflow(name);
      const devcontainerCiSteps = collectDevcontainerCiSteps(name);

      expect(devcontainerCiSteps.some((step) => step.with.push === expectedActionPush)).toBe(true);

      if (requiresEventFilterOverride) {
        expect(devcontainerCiSteps.some((step) => step.with.eventFilterForPush === "")).toBe(true);
      } else {
        expect(
          devcontainerCiSteps.every((step) => step.with.eventFilterForPush === undefined)
        ).toBe(true);
      }

      const dockerPushIndex = workflowText.indexOf('docker push "${IMAGE}"');
      const dockerPullIndex = workflowText.indexOf('docker pull "${IMAGE}"');

      if (requiresExplicitPushBeforePull) {
        expect(dockerPushIndex).toBeGreaterThanOrEqual(0);
        expect(dockerPullIndex).toBeGreaterThan(dockerPushIndex);
      } else {
        expect(dockerPushIndex).toBe(-1);
      }
    }
  );
});
