---
title: "Headless Unity Test Runner"
id: "headless-test-runner"
category: "unity"
version: "1.0.0"
created: "2026-05-05"
updated: "2026-05-05"

source:
  repository: "Ambiguous-Interactive/DxMessaging"
  files:
    - path: "scripts/unity/run-tests.sh"
    - path: "scripts/unity/run-tests.ps1"
    - path: "scripts/unity/lib/asmdef-discovery.js"
    - path: "scripts/unity/lib/parse-test-results.py"
    - path: ".github/workflows-disabled/unity-tests.yml"
  url: "https://github.com/Ambiguous-Interactive/DxMessaging"

tags:
  - "unity"
  - "testing"
  - "devcontainer"
  - "docker"
  - "test-runner"
  - "headless"

complexity:
  level: "intermediate"
  reasoning: "Requires understanding of docker-outside-of-docker, Unity batchmode flags, and the asmdef classification module."

impact:
  performance:
    rating: "none"
    details: "Tooling only; no runtime cost"
  maintainability:
    rating: "high"
    details: "One canonical entry point covers EditMode, PlayMode, and IL2CPP standalone runs"
  testability:
    rating: "high"
    details: "Phase 4 contract tests pin the flag matrix and exclusion rules"

prerequisites:
  - "Devcontainer running with docker-outside-of-docker enabled"
  - "UNITY_LICENSE, UNITY_LICENSE_B64, or UNITY_SERIAL path configured (see unity-license-bootstrap)"

dependencies:
  packages: []
  skills:
    - "unity-license-bootstrap"
    - "upm-test-harness"
    - "unity-perf-test-isolation"
    - "devcontainer-cache-contract"

applies_to:
  languages:
    - "Bash"
    - "PowerShell"
  frameworks:
    - "Unity"
  versions:
    unity: ">=2021.3"

aliases:
  - "Unity test runner"
  - "run-tests.sh"
  - "Headless Unity"

related:
  - "unity-license-bootstrap"
  - "upm-test-harness"
  - "unity-perf-test-isolation"
  - "unity-ci-matrix"
  - "devcontainer-cache-contract"
  - "cicd-devcontainer-workflows"

status: "stable"
---

<!-- trigger: unity, headless, run-tests, editmode, playmode, il2cpp, batchmode | Canonical headless Unity test runner for DxMessaging | Core -->

# Headless Unity Test Runner

> **One-line summary**: `bash scripts/unity/run-tests.sh --platform <editmode|playmode|standalone>` is the canonical command to run Unity tests inside the devcontainer; it spawns an ephemeral `unityci/editor` container via the host docker socket and streams the log to stdout.

## When to Use

- Iterating on Runtime/Editor code that has Unity tests under `Tests/Editor` or `Tests/Runtime`.
- Reproducing a Unity workflow-template failure from `unity-tests.yml` or `unity-il2cpp.yml` locally.
- Smoke-testing a change to `scripts/unity/lib/asmdef-discovery.js` or the test harness.
- Verifying the perf-isolation contract by running with and without `--include-perf`.

## When NOT to Use

- Source-generator tests under `SourceGenerators/`. Use `dotnet test` directly; those tests run on the .NET SDK and do not need Unity.
- Standalone analyzer unit tests. Same: `dotnet test SourceGenerators/...Tests`.
- Pure documentation or markdown changes; no Unity surface to exercise.
- Apple Silicon (ARM Mac) hosts. See the limitation section below.

## Command Reference

| Flag                     | Type       | Default                             | When to Set                                                                                     |
| ------------------------ | ---------- | ----------------------------------- | ----------------------------------------------------------------------------------------------- |
| `--platform`             | enum (req) | none                                | `editmode`, `playmode`, or `standalone`. Required.                                              |
| `--unity-version`        | string     | `2022.3.45f1` (or `$UNITY_VERSION`) | Pin a different Editor version when reproducing a matrix-specific failure.                      |
| `--filter`               | regex      | empty                               | Forward to Unity's `-testFilter`. Use to narrow to a single fixture or namespace.               |
| `--include-perf`         | bool flag  | off                                 | Include `Benchmarks` / `Allocations` asmdefs. Default local runs keep these excluded.           |
| `--include-integrations` | bool flag  | off                                 | Include `VContainer` / `Zenject` / `Reflex` suites. Requires those packages in `manifest.json`. |
| `--include-comparisons`  | bool flag  | off                                 | Include external comparison benchmarks. Requires MessagePipe / UniRx / UniTask / Zenject.       |
| `--results`              | path       | `.artifacts/unity/results.xml`      | Override NUnit XML output path. Must live under the repo (bind-mount limit).                    |
| `--help`                 | flag       | -                                   | Print usage and exit 0.                                                                         |

The defaults match `defaultIncludeAssemblies(repoRoot)` from `scripts/unity/lib/asmdef-discovery.js`. That module is the single source of truth and is also called by the disabled `unity-tests.yml` workflow template.

## Expected Runtimes

Numbers below assume the mode-specific `dxm-unity-library-<image-tag>-<mode>` volume is warm. First-ever run on a fresh image pulls roughly 6 GB into the `unityci/editor:<tag>` layer cache; that pull is one-time per Unity version.

| Mode         | Cold (first pull) | Warm Library cache | Notes                                                       |
| ------------ | ----------------- | ------------------ | ----------------------------------------------------------- |
| `editmode`   | ~6-10 min         | ~30-90 s           | Cheapest. Runs in the Editor's edit-time NUnit harness.     |
| `playmode`   | ~7-12 min         | ~2-5 min           | Spins up a play-mode test runner; longer domain reload.     |
| `standalone` | ~15-25 min        | ~10+ min           | IL2CPP build pass plus runtime pass; AOT compile dominates. |

If a warm run takes more than 2x the expected time, the Library cache is likely cold or a domain reload is looping. Inspect `.artifacts/unity/log.txt`.

## Realtime Feedback

- `-logFile -` streams Unity's log to stdout while the container is alive. The script also `tee`s the same stream into `.artifacts/unity/log.txt`.
- NUnit XML lands at `.artifacts/unity/results.xml` (or `--results` override). The script invokes `python3 scripts/unity/lib/parse-test-results.py` on that XML and prints a one-line `PASS` or `FAIL` summary; the script's exit code matches the test status.
- For IL2CPP standalone runs the build pass writes `.artifacts/unity/build-log.txt` and the run pass writes `.artifacts/unity/log.txt`. Both are kept on the volume so a failure can be diffed against the prior run.

## License Setup

The runner refuses to launch without a supported Unity license path. The exact bootstrap flow lives in [unity-license-bootstrap](./unity-license-bootstrap.md):

1. For GameCI-compatible ULF activation, set raw `.ulf` contents in `UNITY_LICENSE`.
1. For local shell profiles, run `bash scripts/unity/activate-license.sh --apply <path-to.ulf>` and add the printed `UNITY_LICENSE_B64` export.
1. For paid serial activation, set `UNITY_SERIAL`, `UNITY_EMAIL`, and `UNITY_PASSWORD`.
1. Run `bash scripts/unity/activate-license.sh --check` before the first test run.

The devcontainer forwards `UNITY_LICENSE`, `UNITY_LICENSE_B64`, `UNITY_SERIAL`, `UNITY_EMAIL`, `UNITY_PASSWORD`, and `LOCAL_WORKSPACE_FOLDER` from the host via `remoteEnv` in `.devcontainer/devcontainer.json`. Inside a devcontainer the local runner still prefers `docker inspect` of the current container mount over `LOCAL_WORKSPACE_FOLDER`; this avoids passing Windows drive-letter paths to a Linux Docker CLI.

## Iteration Patterns

Run a single fixture by class name:

```bash
bash scripts/unity/run-tests.sh --platform editmode --filter 'MessageBusBasicTests'
```

Reproduce a regression on the oldest supported LTS:

```bash
bash scripts/unity/run-tests.sh --platform playmode --unity-version 2021.3.45f1
```

Run the perf suite locally (no CI parity; use sparingly):

```bash
bash scripts/unity/run-tests.sh --platform editmode --include-perf
```

Run external comparison benchmarks after adding their packages to the harness manifest:

```bash
bash scripts/unity/run-tests.sh --platform editmode --include-comparisons
```

Build and run the IL2CPP test player end-to-end:

```bash
bash scripts/unity/run-tests.sh --platform standalone
```

Diff the discovered assembly list before changing the runner default:

```bash
node scripts/unity/lib/asmdef-discovery.js
```

## Failure Tree

Pick the matching error signature in stdout, then apply the listed remediation.

| Signature                                                           | Cause                                                        | Remediation                                                                                           |
| ------------------------------------------------------------------- | ------------------------------------------------------------ | ----------------------------------------------------------------------------------------------------- |
| `No Unity license configured.`                                      | ULF and serial paths are unset.                              | Configure `UNITY_LICENSE`, `UNITY_LICENSE_B64`, or `UNITY_SERIAL` plus credentials.                   |
| `com.unity.editor.headless` / `No valid Unity Editor license found` | Email/password-only path or invalid entitlement.             | Configure a `.ulf` or paid serial path; email/password alone is unsupported for headless docker runs. |
| `Error: docker socket is not reachable.`                            | DooD feature is missing or socket is not mounted.            | Verify `docker-outside-of-docker` in `.devcontainer/devcontainer.json`. Rebuild the container.        |
| Hangs at `Pulling unityci/editor:...`                               | Slow registry pull or rate limit.                            | First pull is ~6 GB; let it finish. If it hangs > 20 min, retry; the volume keeps partial layers.     |
| `ERROR: 0 tests ran. Check filter / assembly list.`                 | `--filter` matched nothing or the asmdef list excluded all.  | Re-run without `--filter`; verify discovery via `node scripts/unity/lib/asmdef-discovery.js`.         |
| `IL2CPP build failed (exit ...).`                                   | Code-stripping, AOT, or generic-virtual-method regression.   | See [unity-ci-matrix](./unity-ci-matrix.md) for the IL2CPP-only failure catalog.                      |
| `IL2CPP build reported success but binary missing at ...`           | TestRunnerBuilder wrote elsewhere or build silently aborted. | Check `.artifacts/unity/build-log.txt`; verify `DXM_IL2CPP_BUILD_PATH` is consistent with the runner. |
| `Cannot determine host path for the workspace.`                     | Inside a container without an inspectable bind mount.        | Set `DXM_HOST_REPO_ROOT=/absolute/path/on/host` before invoking the script.                           |
| `Activation rate limit` in Unity log                                | Too many license activations in a short window.              | Wait ~1 hour. See [unity-license-bootstrap](./unity-license-bootstrap.md) for the cooldown details.   |

## ARM Mac (Apple Silicon) Limitation

`unityci/editor` images are amd64-only as of 2026-05. Running them via `docker run` on Apple Silicon falls back to QEMU emulation, which is roughly 10x slower and frequently hangs the editor during domain reload. There are two sanctioned paths on M-series Macs:

1. Skip local Unity runs only when GitHub Unity workflows are re-enabled. They are currently local-only.
1. Open the repo in a hosted GitHub Codespace (`gh codespace create`). The Codespace runs on amd64 hardware and the in-container Unity flow works the same as on Linux/Windows hosts.

`.llm/context.md` carries a single-line warning so an agent flags this proactively when `uname -m` returns `arm64`.

## CI Parity

When `CI=true` is set, the script does NOT spawn docker locally. It prints the equivalent `game-ci/unity-test-runner@v4` parameters and exits 0. This is what the disabled `unity-tests.yml` template consumes when re-enabled. The shape is locked by a Phase 4 contract test (`unity-runner-script-contract.test.js`) so help text, flag names, and the assembly source-of-truth cannot drift apart.

## See Also

- [Unity License Bootstrap](./unity-license-bootstrap.md)
- [UPM Test Harness](./upm-test-harness.md)
- [Unity Perf Test Isolation](./unity-perf-test-isolation.md)
- [Unity CI Matrix](./unity-ci-matrix.md)
- [Devcontainer Cache Contract](./devcontainer-cache-contract.md)
- [CI/CD Devcontainer Workflows](../github-actions/cicd-devcontainer-workflows.md)

## References

- Unity command-line arguments: https://docs.unity3d.com/Manual/CommandLineArguments.html
- game-ci unity-test-runner: https://github.com/game-ci/unity-test-runner
- Source: `scripts/unity/run-tests.sh`
