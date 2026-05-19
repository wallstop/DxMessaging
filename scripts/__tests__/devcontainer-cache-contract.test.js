/**
 * @fileoverview Contract tests for the .devcontainer/ cache mount surface.
 *
 * cache-contract.sh defines the bash arrays CACHE_MOUNT_SOURCES and
 * CACHE_MOUNT_TARGETS that post-create.sh, post-start.sh, validate-caching.sh,
 * and devcontainer.json all rely on. We line-scan rather than `source`-ing the
 * file because Jest runs in pure Node.js — and even when a bash were available,
 * `set -e` + the file's re-source guard would make repeat runs of the test
 * suite spuriously fail. The line-scan also keeps the test fast (<10ms).
 */

"use strict";

const fs = require("fs");
const path = require("path");

const REPO_ROOT = path.resolve(__dirname, "..", "..");
const DEVCONTAINER_DIR = path.join(REPO_ROOT, ".devcontainer");

/**
 * Parse a `readonly NAME=( "a" "b" )` style array out of a bash file. Tolerant
 * of leading whitespace, single- or double-quoted entries, and inline
 * comments. Throws when the array isn't found.
 *
 * @param {string} content - Raw bash source
 * @param {string} arrayName - The array variable name (without the `$`)
 * @returns {string[]} Array entries (quotes stripped)
 */
function parseBashArray(content, arrayName) {
  const re = new RegExp(
    `^\\s*(?:readonly\\s+|declare\\s+-[a-z]+\\s+)?${arrayName}\\s*=\\s*\\(([\\s\\S]*?)\\)`,
    "m"
  );
  const match = content.match(re);
  if (!match) {
    throw new Error(`bash array ${arrayName} not found`);
  }
  return match[1]
    .split("\n")
    .map((line) => line.replace(/#.*$/, "").trim())
    .filter((line) => line.length > 0)
    .map((line) => line.replace(/^["']|["']$/g, ""));
}

function devcontainerTargetForContractTarget(target) {
  return target.replace("${CACHE_WORKSPACE_ROOT}", "${containerWorkspaceFolder}");
}

describe(".devcontainer cache mount contract", () => {
  const cacheContractPath = path.join(DEVCONTAINER_DIR, "cache-contract.sh");
  const devcontainerJsonPath = path.join(DEVCONTAINER_DIR, "devcontainer.json");
  const dockerfilePath = path.join(DEVCONTAINER_DIR, "Dockerfile");
  const postCreatePath = path.join(DEVCONTAINER_DIR, "post-create.sh");
  const postStartPath = path.join(DEVCONTAINER_DIR, "post-start.sh");
  const validateCachingPath = path.join(DEVCONTAINER_DIR, "validate-caching.sh");

  let cacheContract;
  let devcontainerJson;
  let dockerfile;
  let sources;
  let targets;

  beforeAll(() => {
    cacheContract = fs.readFileSync(cacheContractPath, "utf8");
    devcontainerJson = fs.readFileSync(devcontainerJsonPath, "utf8");
    dockerfile = fs.readFileSync(dockerfilePath, "utf8");
    sources = parseBashArray(cacheContract, "CACHE_MOUNT_SOURCES");
    targets = parseBashArray(cacheContract, "CACHE_MOUNT_TARGETS");
  });

  test("cache-contract.sh exists", () => {
    expect(fs.existsSync(cacheContractPath)).toBe(true);
  });

  test("CACHE_MOUNT_SOURCES has at least 4 entries", () => {
    expect(sources.length).toBeGreaterThanOrEqual(4);
  });

  test("CACHE_MOUNT_TARGETS has the same length as CACHE_MOUNT_SOURCES", () => {
    // Aligned-by-index is the documented contract; a length mismatch would
    // silently shift mounts under the rug.
    expect(targets.length).toBe(sources.length);
  });

  test("each source name appears verbatim in devcontainer.json `mounts`", () => {
    for (const source of sources) {
      expect(devcontainerJson).toContain(`source=${source}`);
    }
  });

  test("each target path appears verbatim in devcontainer.json `mounts`", () => {
    for (const target of targets) {
      expect(devcontainerJson).toContain(`target=${devcontainerTargetForContractTarget(target)}`);
    }
  });

  test("devcontainer keeps Linux node_modules in a named volume", () => {
    const nodeModulesSource = "dxm-node-modules";
    const contractNodeModulesTarget = "${CACHE_WORKSPACE_ROOT}/node_modules";
    const devcontainerNodeModulesTarget = "${containerWorkspaceFolder}/node_modules";

    expect(sources).toContain(nodeModulesSource);
    expect(targets).toContain(contractNodeModulesTarget);
    expect(devcontainerJson).toContain(
      `source=${nodeModulesSource},target=${devcontainerNodeModulesTarget},type=volume`
    );
  });

  test("devcontainer cache contract does not include Unity Library", () => {
    expect(sources).not.toContain("dxm-unity-library-cache");
    expect(targets).not.toContain(
      "/workspaces/com.wallstop-studios.dxmessaging/.unity-test-project/Library"
    );
    expect(devcontainerJson).not.toContain("dxm-unity-library-cache");
  });

  test("Dockerfile pre-creates every cache target that lives under the workspace", () => {
    const workspaceTargets = targets.filter(
      (target) => target.startsWith("/workspaces/") || target.startsWith("${CACHE_WORKSPACE_ROOT}/")
    );

    for (const target of workspaceTargets) {
      if (target.startsWith("${CACHE_WORKSPACE_ROOT}/")) {
        expect(dockerfile).toContain("/workspaces");
      } else {
        expect(dockerfile).toContain(target);
      }
    }
  });

  test("Dockerfile does not pre-create static Unity Library cache target", () => {
    expect(dockerfile).not.toContain(
      "/workspaces/com.wallstop-studios.dxmessaging/.unity-test-project/Library"
    );
  });

  test("devcontainer.json includes the docker-outside-of-docker feature", () => {
    // The feature reference key on the registry; any version is fine.
    expect(devcontainerJson).toMatch(/devcontainers\/features\/docker-outside-of-docker:1/);
  });

  test("devcontainer forwards Unity license and host workspace env vars", () => {
    expect(devcontainerJson).toContain('"UNITY_LICENSE"');
    expect(devcontainerJson).toContain('"UNITY_LICENSE_B64"');
    expect(devcontainerJson).toContain('"UNITY_SERIAL"');
    expect(devcontainerJson).toContain('"UNITY_EMAIL"');
    expect(devcontainerJson).toContain('"UNITY_PASSWORD"');
    expect(devcontainerJson).toContain('"LOCAL_WORKSPACE_FOLDER": "${localWorkspaceFolder}"');
  });

  test("Dockerfile declares the BuildKit syntax directive on the first line", () => {
    // First non-empty line must be a `# syntax=docker/dockerfile:<v>`
    // directive — Docker only honors it when it is the very first line.
    const firstLine = dockerfile.split(/\r?\n/)[0];
    expect(firstLine).toMatch(/^#\s*syntax=docker\/dockerfile:1\.\d+/);
  });

  test("post-create.sh sources cache-contract.sh", () => {
    const postCreate = fs.readFileSync(postCreatePath, "utf8");
    expect(postCreate).toMatch(/source\s+["']?[^"'\s]*cache-contract\.sh/);
  });

  test("post-start.sh sources cache-contract.sh", () => {
    const postStart = fs.readFileSync(postStartPath, "utf8");
    expect(postStart).toMatch(/source\s+["']?[^"'\s]*cache-contract\.sh/);
  });

  test("validate-caching.sh checks both devcontainer workflows", () => {
    const validateCaching = fs.readFileSync(validateCachingPath, "utf8");

    expect(validateCaching).toContain(".github/workflows/devcontainer-test.yml");
    expect(validateCaching).toContain(".github/workflows/devcontainer-prebuild.yml");
    expect(validateCaching).toContain('docker push "${IMAGE}"');
    expect(validateCaching).toContain('docker pull "${IMAGE}"');
  });
});
