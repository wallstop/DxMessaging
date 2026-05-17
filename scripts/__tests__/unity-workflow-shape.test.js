/**
 * @fileoverview Contract tests for the shape of Unity-related GitHub Actions
 * workflow templates.
 *
 * These workflows have non-obvious invariants that, if violated, cause silent
 * regressions:
 *   - Licensed Unity jobs must only run for same-repo pull requests,
 *     protected branch pushes, schedules, and manual dispatch.
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
const UNITY_WORKFLOWS = ["unity-tests.yml", "unity-il2cpp.yml", "unity-benchmarks.yml"];
const UNITY_VERSIONS = ["2021.3.45f1", "2022.3.45f1", "6000.0.32f1"];

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

function getOnBlock(parsed) {
  return parsed.on || parsed[true];
}

function expectUnityRunnerContract(job, expectation) {
  // Unity Pro is a single-seat license, so every Unity-credential-using
  // job shares the `unity-pro-license` concurrency group with
  // `cancel-in-progress: false`. This serializes Unity work across all
  // four workflows so two licensed jobs cannot run simultaneously on
  // ELI-MACHINE and DAD-MACHINE and fight for the license.
  expect(job.concurrency).toEqual({
    group: "unity-pro-license",
    "cancel-in-progress": false
  });

  // All four Unity-credential-using jobs request the same static label set
  // so either Windows machine can pick up any job. Within-workflow matrix
  // serialization is provided by `strategy.max-parallel: 1` (asserted
  // separately); cross-workflow serialization comes from the shared
  // concurrency group above.
  expect(job["runs-on"]).toEqual(["self-hosted", "Windows", "RAM-64GB"]);

  if (expectation.hasMatrix) {
    // The validator (findMatrixConcurrencyEvictionViolations) requires
    // matrix + shared concurrency group to declare max-parallel: 1; here
    // we assert the contract directly so a regression is caught even if
    // the validator rule shifts.
    expect(job.strategy).toBeDefined();
    expect(job.strategy["max-parallel"]).toBe(1);
  } else {
    // Non-matrix jobs (release.unity-checks) need no max-parallel.
    if (job.strategy !== undefined) {
      expect(job.strategy["max-parallel"]).toBeUndefined();
    }
  }
}

function expectDiagnosticsStep(job) {
  expect(Array.isArray(job.steps)).toBe(true);
  const diagnosticsStep = job.steps.find(
    (step) => step && step.name === "Print runner diagnostics"
  );
  expect(diagnosticsStep).toBeDefined();
  expect(diagnosticsStep.shell).toBe("bash");
}

function expectSameRepoAndProtectedBranchGuard(job) {
  expect(job.if).toContain("github.event_name != 'pull_request'");
  expect(job.if).toContain("github.event.pull_request.head.repo.full_name == github.repository");
  expect(job.if).toContain("github.event_name != 'push'");
  expect(job.if).toContain("github.ref_protected");
}

describe("Unity workflows are active GitHub workflows", () => {
  test.each(UNITY_WORKFLOWS)("%s exists under .github/workflows", (name) => {
    expect(fs.existsSync(path.join(WORKFLOWS_DIR, name))).toBe(true);
    expect(fs.existsSync(path.join(DISABLED_WORKFLOWS_DIR, name))).toBe(true);
  });
});

// Data-driven contract for every Unity-credential-using job across all four
// workflows. Anything that genuinely repeats (runner contract, concurrency
// contract, license secrets, Library cache contract, upload-artifact version,
// diagnostics step, same-repo + protected-branch guards) lives here.
const UNITY_LICENSED_JOBS = [
  {
    workflow: "unity-tests.yml",
    jobId: "unity-tests",
    requiresProtectedBranchGuard: true,
    requiresLibraryCache: true,
    requiresLicenseSecrets: true,
    hasMatrix: true
  },
  {
    workflow: "unity-il2cpp.yml",
    jobId: "il2cpp-tests",
    requiresProtectedBranchGuard: true,
    requiresLibraryCache: true,
    requiresLicenseSecrets: true,
    hasMatrix: true
  },
  {
    workflow: "unity-benchmarks.yml",
    jobId: "benchmarks",
    requiresProtectedBranchGuard: false,
    requiresLibraryCache: true,
    requiresLicenseSecrets: true,
    hasMatrix: true
  },
  {
    workflow: "release.yml",
    jobId: "unity-checks",
    requiresProtectedBranchGuard: false,
    requiresLibraryCache: false,
    requiresLicenseSecrets: true,
    hasMatrix: false
  }
];

describe("Unity-credential-using jobs share the same runner + concurrency contract", () => {
  test.each(UNITY_LICENSED_JOBS)(
    "$workflow job '$jobId' uses static [self-hosted, Windows, RAM-64GB] runs-on and the unity-pro-license group",
    ({ workflow, jobId, hasMatrix }) => {
      const parsed = loadWorkflowYaml(workflow);
      expectUnityRunnerContract(parsed.jobs[jobId], { hasMatrix });
    }
  );

  test.each(UNITY_LICENSED_JOBS)(
    "$workflow contains exactly one literal 'group: unity-pro-license' occurrence",
    ({ workflow }) => {
      // Defense-in-depth: even if the structural assertion above ever
      // regressed silently (e.g., due to YAML parser quirks), the raw text
      // must still reference the canonical group name exactly so log
      // readers can grep for the licensing serialization mechanism.
      //
      // We further assert exactly ONE occurrence of `group: unity-pro-license`
      // per workflow file: introducing a second group declaration (for
      // example, an accidentally-duplicated job or a workflow-level
      // concurrency block reusing the license group name) would silently
      // alter the serialization semantics and is treated as a regression.
      // This complements the structural assertion above; both checks catch
      // different regressions.
      const text = readWorkflow(workflow);
      expect(text).toMatch(/group:\s*unity-pro-license/);
      // Anchor the count to the YAML declaration form (`group: <single-space>
      // unity-pro-license` at the start of a line, allowing leading
      // indentation). Diagnostic `echo` lines that print the human-readable
      // string `Concurrency group:     unity-pro-license` use multiple
      // spaces and live mid-line, so they do not collide with this anchor.
      const declarationOccurrences = text.match(/^\s*group: unity-pro-license\b/gm) || [];
      expect(declarationOccurrences).toHaveLength(1);
    }
  );

  test("no active Unity workflow declares concurrency.group: wallstop-organization-builds", () => {
    // The legacy sentinel must never reappear. Cross-check by raw text so
    // even commented or otherwise-skipped references are caught.
    for (const { workflow } of UNITY_LICENSED_JOBS) {
      const text = readWorkflow(workflow);
      // Covers all three YAML forms the validator catches:
      //   - block:    `concurrency:\n  group: wallstop-organization-builds`
      //   - flow-map: `concurrency: { group: wallstop-organization-builds, ... }`
      //   - scalar:   `concurrency: wallstop-organization-builds`
      expect(text).not.toMatch(
        /(?:concurrency|group):\s*["']?wallstop-organization-builds/
      );
    }
  });

  test.each(UNITY_LICENSED_JOBS)(
    "$workflow job '$jobId' has a 'Print runner diagnostics' bash step",
    ({ workflow, jobId }) => {
      const parsed = loadWorkflowYaml(workflow);
      expectDiagnosticsStep(parsed.jobs[jobId]);
    }
  );

  test.each(UNITY_LICENSED_JOBS.filter((job) => job.requiresLicenseSecrets))(
    "$workflow references Unity license + serial secrets",
    ({ workflow }) => {
      const text = readWorkflow(workflow);
      expect(text).toMatch(/secrets\.UNITY_LICENSE/);
      expect(text).toMatch(/secrets\.UNITY_SERIAL/);
    }
  );

  test.each(UNITY_LICENSED_JOBS.filter((job) => job.requiresLibraryCache))(
    "$workflow declares an exact Library cache key with no broad restore-keys",
    ({ workflow }) => {
      expectExactUnityLibraryCache(readWorkflow(workflow));
    }
  );

  test.each(UNITY_LICENSED_JOBS)(
    "$workflow uses actions/upload-artifact@v7 (matches repo baseline)",
    ({ workflow }) => {
      // unity-checks in release.yml does not currently upload its own
      // artifacts (the validate job does), so this assertion only applies
      // workflow-wide rather than per-job.
      const text = readWorkflow(workflow);
      if (workflow === "release.yml") {
        // release.yml uploads the packed npm artifact from the validate job.
        expect(text).toContain("actions/upload-artifact@v7");
      } else {
        expect(text).toContain("actions/upload-artifact@v7");
      }
    }
  );

  test.each(UNITY_LICENSED_JOBS.filter((job) => job.requiresProtectedBranchGuard))(
    "$workflow job '$jobId' guards same-repo + protected-branch execution",
    ({ workflow, jobId }) => {
      const parsed = loadWorkflowYaml(workflow);
      const text = readWorkflow(workflow);
      expectSameRepoAndProtectedBranchGuard(parsed.jobs[jobId]);
      expect(text).not.toContain("pull_request_target");
    }
  );
});

describe(".github/workflows/unity-tests.yml", () => {
  let text;
  let parsed;

  beforeAll(() => {
    text = readWorkflow("unity-tests.yml");
    parsed = loadWorkflowYaml("unity-tests.yml");
  });

  test("runs for same-repo PRs, protected branch pushes, schedules, and dispatch", () => {
    const onBlock = getOnBlock(parsed);
    expect(Object.keys(onBlock).sort()).toEqual([
      "pull_request",
      "push",
      "schedule",
      "workflow_dispatch"
    ]);
    expect(text).not.toContain("pull_request_target");
  });

  test("uses the full Unity version x test mode matrix", () => {
    for (const unityVersion of UNITY_VERSIONS) {
      expect(text).toContain(unityVersion);
    }
    expect(text).toContain('modes=\'["editmode","playmode"]\'');
  });
});

describe(".github/workflows/unity-il2cpp.yml", () => {
  let text;
  let parsed;

  beforeAll(() => {
    text = readWorkflow("unity-il2cpp.yml");
    parsed = loadWorkflowYaml("unity-il2cpp.yml");
  });

  test("runs separately for same-repo PRs, protected branch pushes, schedules, and dispatch", () => {
    const onBlock = getOnBlock(parsed);
    expect(Object.keys(onBlock).sort()).toEqual([
      "pull_request",
      "push",
      "schedule",
      "workflow_dispatch"
    ]);
    expect(text).not.toContain("pull_request_target");
  });

  test("runs the standalone IL2CPP path through the repo runner", () => {
    expect(text).toContain("-Platform standalone");
    expect(text).toContain("-Runner docker");
  });
});

describe(".github/workflows/unity-benchmarks.yml", () => {
  let text;
  let parsed;

  beforeAll(() => {
    text = readWorkflow("unity-benchmarks.yml");
    parsed = loadWorkflowYaml("unity-benchmarks.yml");
  });

  test("`on:` block has ONLY schedule and workflow_dispatch", () => {
    // YAML 1.1 turns the bare `on` key into `true`; check both keys to
    // tolerate either representation across yaml/parser versions.
    const onBlock = getOnBlock(parsed);
    expect(onBlock).toBeDefined();
    expect(typeof onBlock).toBe("object");

    const triggerKeys = Object.keys(onBlock).sort();
    expect(triggerKeys).toEqual(["schedule", "workflow_dispatch"]);

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

  test("includes perf assemblies through the repo runner", () => {
    expect(text).toContain("-IncludePerf");
    expect(text).not.toContain("pull_request_target");
  });
});

describe(".github/workflows/release.yml", () => {
  let text;
  let parsed;

  beforeAll(() => {
    text = readWorkflow("release.yml");
    parsed = loadWorkflowYaml("release.yml");
  });

  test("is tag-triggered only and validates exact semver tags", () => {
    const onBlock = getOnBlock(parsed);
    expect(Object.keys(onBlock)).toEqual(["push"]);
    expect(onBlock.push).toEqual({ tags: ["v[0-9]*.[0-9]*.[0-9]*"] });
    expect(text).toContain("^v[0-9]+\\.[0-9]+\\.[0-9]+$");
    expect(text).toContain("Tag ${tag} does not match package.json version");
    expect(text).not.toContain("workflow_dispatch");
  });

  test("validates, runs Unity checks, packs, attests, releases, and publishes with provenance", () => {
    expect(Object.keys(parsed.jobs).sort()).toEqual([
      "publish",
      "unity-checks",
      "validate",
      "verify-tag"
    ]);
    expect(parsed.jobs.publish["runs-on"]).toBe("ubuntu-latest");
    expect(text).toContain("npm run validate:npm-meta");
    expect(text).toContain("npm run test:unity-contracts");
    expect(text).toContain("npm pack --json");
    expect(text).toContain("actions/attest-build-provenance@v3");
    expect(text).toContain("actions/upload-artifact@v7");
    expect(text).toContain("gh release create");
    expect(text).toContain("npx --yes --package=npm@^11.5.1 npm publish");
    expect(text).toContain("--provenance");
    expect(text).not.toContain("NPM_TOKEN");
  });

  test("attestation job grants provenance permissions and validates from a full checkout", () => {
    const attestationJobEntry = Object.entries(parsed.jobs).find(([, job]) =>
      job.steps.some((step) => step.uses === "actions/attest-build-provenance@v3")
    );
    expect(attestationJobEntry).toBeDefined();

    const [, attestationJob] = attestationJobEntry;
    expect(attestationJob.permissions).toEqual({
      attestations: "write",
      contents: "read",
      "id-token": "write"
    });

    const checkoutIndex = attestationJob.steps.findIndex(
      (step) => step.uses === "actions/checkout@v6"
    );
    const validateAllIndex = attestationJob.steps.findIndex(
      (step) => step.run && step.run.includes("npm run validate:all")
    );

    expect(checkoutIndex).toBeGreaterThanOrEqual(0);
    expect(validateAllIndex).toBeGreaterThan(checkoutIndex);

    const checkoutStep = attestationJob.steps[checkoutIndex];
    expect(checkoutStep).toEqual(
      expect.objectContaining({
        with: expect.objectContaining({
          "fetch-depth": 0
        })
      })
    );
  });

  test("publish job has Trusted Publishing permissions", () => {
    expect(parsed.jobs.publish.permissions).toEqual({
      attestations: "write",
      contents: "write",
      "id-token": "write"
    });
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
