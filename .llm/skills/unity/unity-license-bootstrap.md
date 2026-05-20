---
title: "Unity License Bootstrap"
id: "unity-license-bootstrap"
category: "unity"
version: "2.0.0"
created: "2026-05-05"
updated: "2026-05-05"

source:
  repository: "Ambiguous-Interactive/DxMessaging"
  files:
    - path: "scripts/unity/activate-license.sh"
    - path: "scripts/unity/run-tests.sh"
    - path: ".devcontainer/devcontainer.json"
    - path: ".github/workflows-disabled/unity-tests.yml"
  url: "https://github.com/Ambiguous-Interactive/DxMessaging"

tags:
  - "unity"
  - "license"
  - "ulf"
  - "personal"
  - "ci"
  - "secrets"

complexity:
  level: "basic"
  reasoning: "Two short configuration paths plus a 2FA caveat; no algorithmic content."

impact:
  performance:
    rating: "none"
    details: "Tooling only"
  maintainability:
    rating: "high"
    details: "ULF and serial paths match current Unity/GameCI behavior"
  testability:
    rating: "low"
    details: "Validated implicitly: the runner refuses to launch without a working license"

prerequisites:
  - "A Unity ID (sign-up at id.unity.com)"
  - "Docker socket reachable inside the devcontainer (for live --check)"

dependencies:
  packages: []
  skills:
    - "headless-test-runner"

applies_to:
  languages:
    - "Bash"
  frameworks:
    - "Unity"
  versions:
    unity: ">=2021.3"

aliases:
  - "Unity license"
  - "ULF activation"
  - "activate-license.sh"
  - "Unity Personal ULF"

related:
  - "headless-test-runner"
  - "unity-ci-matrix"
  - "cicd-devcontainer-workflows"

status: "stable"
---

<!-- trigger: unity, license, ulf, personal, pro, activation, secret, serial | ULF and serial bootstrap for headless Unity | Core -->

# Unity License Bootstrap

> **One-line summary**: Headless Unity in docker needs either a Unity `.ulf` or a paid serial. Use raw `.ulf` contents in `UNITY_LICENSE` for GameCI-compatible runs, `UNITY_LICENSE_B64` for local shell convenience, or `UNITY_SERIAL` + `UNITY_EMAIL` + `UNITY_PASSWORD` for Professional serial activation.

## When to Use

- First-time setup on a new dev machine, codespace, or CI runner.
- After a `.ulf` expires or a serial activation starts failing.
- When the runner reports `Error: No Unity license configured.`
- When a CI run fails with `License client failed` or `Failed to activate / find license`.

## License Types

| Type         | Activation Method                    | Cost | Notes                                                             |
| ------------ | ------------------------------------ | ---- | ----------------------------------------------------------------- |
| Personal     | Raw `.ulf` + Unity credentials       | Free | Email/password alone is not a supported headless container path.  |
| Professional | Serial + Unity credentials           | Paid | Use `UNITY_SERIAL`; do not mix serial activation with `.ulf`.     |
| Local ULF    | Base64 `.ulf` in `UNITY_LICENSE_B64` | Any  | Local-only convenience for shell profiles; not the GameCI secret. |
| Enterprise   | Floating license server              | Paid | `UNITY_LICENSING_SERVER` URL + client cert; not first-class here. |

The runner picks the path automatically: `UNITY_LICENSE` set -> raw `.ulf`;
`UNITY_LICENSE_B64` set -> local base64 `.ulf`; `UNITY_SERIAL` plus
`UNITY_EMAIL` plus `UNITY_PASSWORD` set -> serial activation. `UNITY_EMAIL` +
`UNITY_PASSWORD` alone exits 2 with remediation because current Unity
licensing does not grant the headless entitlement that way.

## Personal / ULF Path

Recommended for CI when using GameCI and for contributors with a Unity `.ulf`.

1. Obtain a `.ulf` through Unity Hub or Unity's manual activation flow.

1. For GitHub Actions/GameCI, paste the raw `.ulf` file contents into the
   `UNITY_LICENSE` repository secret. Also set the account credentials:

   ```bash
   export UNITY_LICENSE="$(cat path/to/Unity_lic.ulf)"
   export UNITY_EMAIL='you@example.com'
   export UNITY_PASSWORD='your-password'
   ```

1. For a local shell profile, use the base64 convenience variable instead of
   trying to store multiline XML:

   ```bash
   bash scripts/unity/activate-license.sh --apply path/to/Unity_lic.ulf
   ```

   Add the printed `UNITY_LICENSE_B64` export to your shell profile.

1. Verify: `bash scripts/unity/activate-license.sh --check` validates the
   configured secret shape.

1. Run: `bash scripts/unity/run-tests.sh --platform editmode`.

### Email/Password-Only Caveat

Do not configure only `UNITY_EMAIL` + `UNITY_PASSWORD`. A live repro on
2026-05-05 with `unityci/editor:2022.3.45f1-base-3` logged in successfully but
failed entitlement resolution with `com.unity.editor.headless` and
`No valid Unity Editor license found`.

GameCI's current activation guide also treats Personal as a one-time `.ulf`
setup, not an email/password-only activation.

### CI Secrets (Personal)

| Secret           | Value                  | Required |
| ---------------- | ---------------------- | -------- |
| `UNITY_EMAIL`    | Unity account email    | Yes      |
| `UNITY_PASSWORD` | Unity account password | Yes      |
| `UNITY_LICENSE`  | Raw `.ulf` contents    | Yes      |

The active workflows under `.github/workflows/unity-*.yml` pass all three to
`game-ci/unity-test-runner@v4` on the self-hosted Windows runners; it picks the
activation path from whichever secrets are set. Each workflow runs
`./.github/actions/validate-unity-license` before acquiring the central
organization Unity lock so missing serial credentials, missing activation
secrets, or ambiguous activation modes fail with a clear diagnostic before Unity
starts or blocks the shared Unity seat. The
`.github/workflows-disabled/*` files are the ubuntu reference mirrors.

## Professional Serial Path

Use this path for paid serial activation.

1. Set the paid Unity serial and account credentials:

   ```bash
   export UNITY_SERIAL='XX-XXXX-XXXX-XXXX-XXXX-XXXX'
   export UNITY_EMAIL='you@example.com'
   export UNITY_PASSWORD='your-password'
   ```

1. Verify: `bash scripts/unity/activate-license.sh --check`.

1. Run: `bash scripts/unity/run-tests.sh --platform editmode`.

### CI Secrets (Pro / Plus)

| Secret           | Value                                    | Required |
| ---------------- | ---------------------------------------- | -------- |
| `UNITY_SERIAL`   | Paid Unity serial                        | Yes      |
| `UNITY_EMAIL`    | Unity account email                      | Yes      |
| `UNITY_PASSWORD` | Unity account password                   | Yes      |
| `UNITY_LICENSE`  | Leave unset when using serial activation | No       |

## Enterprise Path (Stub)

Floating-license servers are not first-class here. Set
`UNITY_LICENSING_SERVER=<url>` and provide the client cert per Unity
Enterprise support. `run-tests.sh` does not yet wire the cert through;
treat this as a future enhancement.

## Common Failures

| Signature                                                           | Cause                                                        | Remediation                                                                                 |
| ------------------------------------------------------------------- | ------------------------------------------------------------ | ------------------------------------------------------------------------------------------- |
| `No Unity license configured`                                       | ULF and serial paths unset.                                  | Set `UNITY_LICENSE`, `UNITY_LICENSE_B64`, or `UNITY_SERIAL` plus credentials.               |
| `com.unity.editor.headless` / `No valid Unity Editor license found` | Email/password-only path or invalid entitlement.             | Configure a `.ulf` or paid serial path.                                                     |
| `License client failed to start`                                    | Activation rate limit, expired `.ulf`, or wrong credentials. | Wait 1 hour; then verify the ULF/serial and account credentials.                            |
| `Activation rate limit` in the Unity log                            | Too many activations from one IP.                            | Cool down ~1 hour; do not retry in a loop. Use a single shared CI secret.                   |
| `LICENSE SYSTEM ... License is not valid for this build target`     | The `.ulf` was issued for a different Unity major version.   | Refresh the Hub on the issuing dev machine and re-run `--apply`.                            |
| `verification code` / `two-factor` in the log                       | 2FA is enabled on the Unity account.                         | Disable 2FA temporarily, or migrate to a dedicated CI account.                              |
| `Warn: <path> does not look like a Unity license file.`             | `--apply` was pointed at the wrong file.                     | Re-run with the actual `.ulf` from the Unity Hub install path.                              |
| Network unreachable from container                                  | Corporate proxy or container egress blocked.                 | Set `HTTP_PROXY` / `HTTPS_PROXY` in the devcontainer env, or run the bootstrap on the host. |

## Renewal

- **ULF**: when the file expires or changes Unity version requirements,
  refresh it and update `UNITY_LICENSE` in GitHub or `UNITY_LICENSE_B64`
  locally.
- **Serial**: if activation fails after a license renewal, verify the serial
  in the Unity dashboard and update `UNITY_SERIAL` if it changed.

## See Also

- [Headless Test Runner](./headless-test-runner.md)
- [Unity CI Matrix](./unity-ci-matrix.md)
- [CI/CD Devcontainer Workflows](../github-actions/cicd-devcontainer-workflows.md)

## References

- Unity license activation methods: <https://docs.unity3d.com/Manual/LicenseActivationMethods.html>
- Unity manual activation support: <https://docs.unity3d.com/Manual/ManualActivationGuide.html>
- GameCI activation guide: <https://game.ci/docs/github/activation/>
- GameCI test runner: <https://game.ci/docs/github/test-runner/>
- Source: `scripts/unity/activate-license.sh`, `scripts/unity/run-tests.sh`
