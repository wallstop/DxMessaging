# Contributing

Thanks for helping improve DxMessaging!

Before committing, please enable our git hooks and local linters so you catch issues early.
Run these steps in order:

1. Install Node dependencies: `npm install`
1. Install pre-commit: `pip install pre-commit` or `pipx install pre-commit`
1. Install hooks: `pre-commit install`
1. Run Node tooling preflight: `npm run preflight:pre-commit` (includes YAML formatting + yamllint checks)
1. Run on all files: `pre-commit run --all-files`

`jest` does not need to be installed globally. Hooks and scripts route through `scripts/run-managed-jest.js` so they can use local devDependencies first, then a managed fallback when needed.

Windows note: if you use `nvm` or `fnm`, run commits from a shell where Node is initialized (PowerShell or Git Bash) and verify `npm --version` before running hooks.
If you edit `.github/workflows/*.yml`, run `npm run preflight:pre-commit` in that same shell before `git commit`.

Line endings: Git normalizes most text files to **LF** through `.gitattributes`. **Exception:** C#/.NET files (`.cs`, `.csproj`, `.sln`, `.props`) use CRLF per .NET conventions. Run this once after cloning (especially on Windows) to fix your working tree:

```bash
git config core.autocrlf false
node scripts/fix-eol.js
```

This directly converts files in your working directory to the correct line endings. Add `-v` for verbose output showing each file fixed.

> **Note:** You may see references to `git add --renormalize`, but that command only updates the git index (staging area)—it does **not** modify your working tree files. Use `fix-eol.js` to actually fix files on disk.

## VS Code Security Policy

- Do not commit terminal auto-approval settings (for example `chat.tools.terminal.autoApprove`) to `.vscode/settings.json`.
- Repository settings must not bypass command review prompts for chat-invoked terminal commands.
- If you need personal auto-approval rules, keep them in local user settings, not repository-tracked files.

What runs locally:

- Markdown link text check: enforces human-readable link text (no raw file names/paths)
- Internal markdown link validity: verifies relative links and anchors point to real files/sections
- Markdown style and formatting: markdownlint (auto-fix common issues)
- JSON/.asmdef formatting: Prettier (2-space indent)
- YAML formatting: Prettier (2-space indent) + yamllint
- NPM package validation: ensures all Unity .meta files are properly included in npm package

On pull requests, CI also checks all markdown links (including external URLs) with lychee.

Handy commands:

- Internal links (local): `python .github/scripts/validate_markdown_links.py .`
- Lint markdown (all files): `pre-commit run markdownlint --all-files`
- Lint markdown (manual): `npx markdownlint-cli2@0.20.0 "**/*.md" --fix`
- Format JSON/.asmdef (all files): `pre-commit run prettier --all-files`
- Format JSON/.asmdef (manual): `npx prettier@3.8.1 --write "**/*.{json,asmdef}"`
- Format YAML (all files): `pre-commit run prettier-yaml --all-files`
- Check YAML formatting + lint: `npm run check:yaml`
- Run yamllint hook directly: `pre-commit run yamllint --all-files`

Prettier keeps YAML formatting consistent but does not automatically wrap long YAML lines. `yamllint` is the authoritative check for the 200-character YAML line-length rule.

If `npm run check:yaml` reports a YAML line-length failure:

1. For workflow `run:` commands, use folded scalars (`run: >-`) to split long commands across readable lines.
1. For non-command YAML values, break long strings into multiline YAML values where valid, or refactor the content so each line stays within 200 characters.

- Format C#: `dotnet tool restore && dotnet tool run csharpier format`
- Validate pre-commit Node tooling policy: `npm run validate:pre-commit-tooling`
- Run pre-commit Node preflight: `npm run preflight:pre-commit`
- Validate NPM package: `npm run validate:npm-meta`

## NPM Package Validation

Unity requires `.meta` files for every asset to maintain consistent GUIDs across installations. The `validate:npm-meta` script ensures:

1. Every `.meta` file in the package corresponds to an actual file or directory
1. Every Unity-tracked file/directory has its `.meta` file included

This validation runs automatically:

- Before every push via pre-commit hooks
- In CI/CD on every pull request

If validation fails, it means either:

- **Orphaned .meta files**: A `.meta` file exists without its corresponding file/directory (often from deleted files)
- **Missing .meta files**: A file/directory exists without its `.meta` file (Unity will generate a new GUID, breaking references)

To fix issues:

- For orphaned .meta files: Delete the orphaned `.meta` file
- For missing .meta files: Ensure Unity generates the `.meta` file, or copy it from the repository

If you do still need to repair line endings manually (for example, after copying files from an external tool), run `node scripts/fix-eol.js -v` and then re-stage the affected files.
