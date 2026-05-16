# Ambiguous Release Migration Operator Guide

This guide tracks the human setup work behind the Ambiguous release migration
for DxMessaging. It is safe to commit because it contains only public
identifiers, checklist structure, and verification steps.

Do not commit secrets, recovery codes, account screenshots, publisher account
identifiers, personal access tokens, one-time codes, private billing details, or
local execution notes. Use `npm run generate:ambiguous-release-runbook` for an
ignored local checklist only for non-sensitive execution notes, such as public
PR URLs, public release URLs, and dates when public verification was completed.

## Public Identifiers

| Surface                                 | Public value                                                     |
| --------------------------------------- | ---------------------------------------------------------------- |
| GitHub repository                       | `Ambiguous-Interactive/DxMessaging`                              |
| Canonical repository URL                | `https://github.com/Ambiguous-Interactive/DxMessaging`           |
| GitHub Pages URL                        | `https://ambiguous-interactive.github.io/DxMessaging/`           |
| Unity and npm package id                | `com.wallstop-studios.dxmessaging`                               |
| Display name                            | `DxMessaging`                                                    |
| Minimum Unity version in `package.json` | `2021.3`                                                         |
| Release workflow                        | `.github/workflows/release.yml`                                  |
| Documentation workflow                  | `.github/workflows/deploy-docs.yml`                              |
| npm package validation workflow         | `.github/workflows/validate-npm-meta.yml`                        |
| Unity test workflow                     | `.github/workflows/unity-tests.yml`                              |
| Unity IL2CPP workflow                   | `.github/workflows/unity-il2cpp.yml`                             |
| Unity benchmark workflow                | `.github/workflows/unity-benchmarks.yml`                         |
| Unity self-hosted runner group          | `ambiguous-interactive-organization-builds`                      |
| GitHub Pages environment                | `github-pages`                                                   |
| OpenUPM package page                    | `https://openupm.com/packages/com.wallstop-studios.dxmessaging/` |

## Transfer Inventory

Before changing external settings, capture the public state that must survive
the migration.

- [ ] Confirm `Ambiguous-Interactive/DxMessaging` is the intended canonical
      repository slug and no target repository or fork network conflict blocks
      the transfer.
- [ ] Confirm the default branch used for release and docs operations. Current
      tracked links use `master`, while workflows also accept pushes to `main`.
- [ ] Confirm `package.json` still has `name` set to
      `com.wallstop-studios.dxmessaging`, `displayName` set to `DxMessaging`,
      and repository URL
      `git+https://github.com/Ambiguous-Interactive/DxMessaging.git`.
- [ ] Record the latest public tag, latest GitHub Release, and latest npm and
      OpenUPM package versions in the ignored local runbook, not in tracked
      docs.
- [ ] Confirm maintainers who need post-transfer admin access are available for
      GitHub, npm, OpenUPM, and Unity Publisher Portal actions.
- [ ] Confirm no release-critical workflow depends on a personal fork, personal
      token, or old repository URL.

## GitHub Repository Transfer

Use GitHub repository settings for the transfer. GitHub documents that the
operator must have administrator access, the target owner must be valid, and the
target must not already have a repository with the same name or a fork in the
same network.

- [ ] From the source repository, open **Settings** and use the repository
      transfer control in the danger zone.
- [ ] Set the new owner to `Ambiguous-Interactive` and keep the repository name
      `DxMessaging`.
- [ ] Read GitHub's transfer warnings before confirming. Pay specific attention
      to Pages, protected branches, collaborators, issue assignments, and
      Marketplace/action-name retirement warnings.
- [ ] After acceptance, open `https://github.com/Ambiguous-Interactive/DxMessaging`
      directly and confirm the repository, issues, pull requests, tags, releases,
      and Actions tabs are visible to maintainers.
- [ ] Confirm redirects from old public URLs are working, but do not keep old
      slugs in tracked configuration or docs.
- [ ] Run `npm run validate:repo-identity` after tracked URL updates so stale
      repository identity references are caught before release.

Reference: [GitHub repository transfer documentation](https://docs.github.com/articles/about-repository-transfers).

## Runner Group Setup

Unity workflows require the organization self-hosted runner group
`ambiguous-interactive-organization-builds` with labels `self-hosted`,
`Windows`, and `RAM-64GB`.

- [ ] In the `Ambiguous-Interactive` organization, open **Settings**,
      **Actions**, then **Runner groups**.
- [ ] Confirm `ambiguous-interactive-organization-builds` exists and grants
      repository access to `Ambiguous-Interactive/DxMessaging`.
- [ ] Confirm the runner group has online Windows runners labeled
      `self-hosted`, `Windows`, and `RAM-64GB`.
- [ ] Confirm `.github/workflows/unity-tests.yml`,
      `.github/workflows/unity-il2cpp.yml`, `.github/workflows/unity-benchmarks.yml`,
      and the `unity-checks` job in `.github/workflows/release.yml` all resolve
      to that group.
- [ ] Keep fork pull requests off self-hosted runners. The Unity workflows only
      allow same-repository pull requests and protected branch pushes; do not
      replace those guards with `pull_request_target`.
- [ ] Protect release tags before the first production release. The release
      workflow also runs trusted Unity checks on `v*` tags, so `vX.Y.Z` tags
      must be covered by GitHub rulesets or tag protection that require the
      approved release process and reviewed release commit.
- [ ] Remove any temporary runner group access granted only for transfer work.

Reference: [GitHub runner group access documentation](https://docs.github.com/en/actions/hosting-your-own-runners/managing-access-to-self-hosted-runners-using-groups).

## GitHub Environments, Secrets, and Protections

### Environments

- [ ] Confirm the `github-pages` environment exists for
      `.github/workflows/deploy-docs.yml`.
- [ ] Confirm `github-pages` allows deployments from the protected default
      branch path used by the docs workflow.
- [ ] If a future npm publishing environment is added to `.github/workflows/release.yml`,
      create the matching GitHub environment before adding it to npm Trusted
      Publishing. The current tracked release workflow does not declare a
      publish environment.
- [ ] If reviewers or wait timers are required by organization policy, configure
      them in GitHub and record only the policy name in local notes.

### Secrets

The tracked workflows expose only secret names. Never write the values into a
tracked file or generated artifact.

- [ ] Confirm Unity workflows can read the required secret names:
      `UNITY_LICENSE`, `UNITY_SERIAL`, `UNITY_EMAIL`, and `UNITY_PASSWORD`.
- [ ] Confirm `.github/workflows/release.yml` does not require `NPM_TOKEN`; npm
      publishing should use Trusted Publishing and OIDC.
- [ ] Confirm the release workflow grants `id-token: write` only to jobs that
      need attestations or Trusted Publishing.
- [ ] Rotate any transfer-only token after migration. Prefer environment,
      repository, or organization secrets only when OIDC is not supported.
- [ ] On self-hosted runners, treat environment secrets with the same care as
      repository and organization secrets because the runner host is not a
      clean hosted runner image.

Reference: [GitHub environments documentation](https://docs.github.com/en/actions/reference/deployments-and-environments).

### Branches and Tags

- [ ] Protect the default branch used for release commits, currently `master`
      unless maintainers intentionally migrate to `main`.
- [ ] Require pull request review before merging release changes.
- [ ] Require the checks that gate release readiness, at minimum script tests,
      npm package validation, docs validation, repo identity validation, and the
      Unity workflow checks that the organization expects before publishing.
- [ ] Keep force pushes and branch deletion disabled for protected branches.
- [ ] Restrict who can create or update `v*` release tags if GitHub rulesets are
      available in the organization.
- [ ] Confirm release automation can create GitHub Releases and upload release
      assets through `.github/workflows/release.yml`.

## npm Ownership, Trusted Publishing, and Provenance

The npm package name is `com.wallstop-studios.dxmessaging`. The release workflow
publishes the packed package from `.artifacts/release` with
`npm publish --provenance --access public` through npm `^11.5.1`.

- [ ] In npm, confirm the package exists under the expected owner or maintainer
      set and that active maintainers have two-factor authentication enabled.
- [ ] Remove maintainers who were only needed for transfer work.
- [ ] Configure npm Trusted Publishing for package
      `com.wallstop-studios.dxmessaging` with:
      `repository owner = Ambiguous-Interactive`, `repository name = DxMessaging`,
      `workflow filename = release.yml`, and `environment = none` unless the
      release workflow later declares a publish environment.
- [ ] Confirm the `publish` job in `.github/workflows/release.yml` has
      `id-token: write` and does not read `NPM_TOKEN`.
- [ ] Confirm the npm package provenance view links back to
      `Ambiguous-Interactive/DxMessaging` after the first Trusted Publishing
      release.
- [ ] Run `npm run validate:npm-meta` before publishing to verify Unity `.meta`
      files and package contents.
- [ ] Use `npm pack --json --dry-run --ignore-scripts` or
      `npm run validate:npm-meta` for a non-publishing package check.

References:

- [npm Trusted Publishing documentation](https://docs.npmjs.com/trusted-publishers)
- [npm provenance documentation](https://docs.npmjs.com/generating-provenance-statements)

## Semver Tag Release Flow

`.github/workflows/release.yml` runs only on tags matching `vX.Y.Z`. It validates
that the tag exactly matches `package.json` version before packing, attesting,
creating or updating the GitHub Release, and publishing to npm.

- [ ] Update `package.json` `version` to the intended public version.
- [ ] Update `CHANGELOG.md` for the user-facing release.
- [ ] Run `npm run test:scripts`, `npm run test:unity-contracts`,
      `npm run validate:npm-meta`, `npm run validate:llms-txt`,
      `npm run validate:repo-identity`, and `npm run validate:all`.
- [ ] Merge the release commit to the protected default branch.
- [ ] Create the release tag from the exact release commit:
      `git tag -s vX.Y.Z` when signing is available, or the repository-approved
      annotated tag method when signing is not available.
- [ ] Push only the intended release tag: `git push origin vX.Y.Z`.
- [ ] Confirm `.github/workflows/release.yml` starts on the tag and that the
      `verify-tag`, `validate`, `unity-checks`, and `publish` jobs complete.
- [ ] Confirm the GitHub Release includes the `.tgz` package and `.sha256`
      artifact.
- [ ] Confirm npm shows the new `com.wallstop-studios.dxmessaging` version with
      provenance linked to the release workflow.

## OpenUPM Metadata Update

OpenUPM uses package metadata YAML and monitors versioned Git tags. The package
page for this package is
`https://openupm.com/packages/com.wallstop-studios.dxmessaging/`.

- [ ] Find the OpenUPM metadata entry for `com.wallstop-studios.dxmessaging`.
- [ ] Confirm its `repoUrl` is `https://github.com/Ambiguous-Interactive/DxMessaging`.
- [ ] Confirm the metadata still points to the root package path if `package.json`
      remains at the repository root.
- [ ] Confirm metadata values match `package.json`: package id
      `com.wallstop-studios.dxmessaging`, display name `DxMessaging`, license
      `MIT`, and Unity version `2021.3`.
- [ ] If any metadata changed, submit an OpenUPM metadata pull request using the
      package name in the title, then link the public PR from local operator
      notes only.
- [ ] After the PR merges, wait for OpenUPM indexing and confirm the package
      page reports the latest `vX.Y.Z` tag without build issues.

Reference: [OpenUPM package metadata documentation](https://openupm.com/docs/adding-upm-package.html).

## Unity Asset Store UPM Onboarding

Unity Asset Store UPM publishing is separate from npm and OpenUPM. It uses Unity
Publisher Portal enrollment, publisher verification, package validation, upload,
review, and Asset Store distribution.

- [ ] Confirm the publisher account is enrolled or eligible for UPM publishing
      before promising Asset Store availability.
- [ ] Confirm the namespace requested in Unity Publisher Portal is compatible
      with package id `com.wallstop-studios.dxmessaging`.
- [ ] Validate the UPM package structure from the repository root, including
      `package.json`, `Runtime/**`, `Editor/**`, `Samples~/**`, `README.md`,
      `CHANGELOG.md`, `LICENSE.md`, and all required `.meta` files.
- [ ] Confirm public links in `package.json` resolve:
      documentation `https://ambiguous-interactive.github.io/DxMessaging/`,
      changelog `https://raw.githubusercontent.com/Ambiguous-Interactive/DxMessaging/master/CHANGELOG.md`,
      and license `https://github.com/Ambiguous-Interactive/DxMessaging/blob/master/LICENSE.md`.
- [ ] Use Unity's current UPM publishing tools or Publisher Portal upload flow
      for validation and upload. Do not commit upload receipts, portal
      screenshots, package signing output, or account-specific identifiers.
- [ ] Treat Asset Store package signing as Unity-controlled review output. Do
      not modify package contents after validation without re-running validation
      and upload.
- [ ] Record only the public listing URL in tracked follow-up issues or release
      notes.

References:

- [Unity UPM publishing overview](https://support.unity.com/hc/en-us/articles/46563578188180-What-is-the-UPM-publishing-workflow-and-how-is-it-different)
- [Unity Asset Store UPM publishing page](https://assetstore.unity.com/publishing/upm-publishing)

## Post-Transfer Verification

Run these checks after the transfer and again after the first tagged release.

- [ ] Fresh clone succeeds:
      `git clone https://github.com/Ambiguous-Interactive/DxMessaging.git`.
- [ ] `git remote -v` uses `https://github.com/Ambiguous-Interactive/DxMessaging.git`
      or the matching SSH remote for the same slug.
- [ ] `git fetch --tags --prune` returns the expected `vX.Y.Z` release tags.
- [ ] `npm run validate:repo-identity` passes.
- [ ] `npm run validate:npm-meta` passes.
- [ ] `npm run test:scripts` passes.
- [ ] `.github/workflows/deploy-docs.yml` deploys to
      `https://ambiguous-interactive.github.io/DxMessaging/`.
- [ ] `.github/workflows/unity-tests.yml` can run on
      `ambiguous-interactive-organization-builds` for same-repository pull
      requests or protected branch pushes.
- [ ] `.github/workflows/release.yml` succeeds for a real semver tag and does
      not require `NPM_TOKEN`.
- [ ] npm, OpenUPM, GitHub Releases, and GitHub Pages all point to
      `Ambiguous-Interactive/DxMessaging`.
- [ ] If Unity has approved and published the Asset Store UPM listing, confirm
      the public listing points to `Ambiguous-Interactive/DxMessaging`. If Unity
      approval is still pending, keep the approval state in Unity Publisher
      Portal or the approved organization password manager instead of treating
      the missing listing as a release blocker.
- [ ] Maintainers can recover GitHub, npm, OpenUPM, and Unity Publisher Portal
      access without relying on private material committed to the repository.
