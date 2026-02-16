# Contributing

Thanks for helping improve DxMessaging!

Before committing, please enable our git hooks and local linters so you catch issues early:

- Install pre-commit: `pip install pre-commit` or `pipx install pre-commit`
- Install hooks: `pre-commit install`
- Run on all files: `pre-commit run --all-files`

Line endings: Git normalizes most text files to CRLF through `.gitattributes`. **Exception:** Shell scripts (`.sh`, `.bash`, `.zsh`, `.ksh`, `.fish`) use LF for Unix compatibility. Run this once after cloning (especially on Windows) to fix your working tree:

```bash
git config core.autocrlf false
node scripts/fix-eol.js
```

This directly converts files in your working directory to the correct line endings. Add `-v` for verbose output showing each file fixed.

> **Note:** You may see references to `git add --renormalize`, but that command only updates the git index (staging area)—it does **not** modify your working tree files. Use `fix-eol.js` to actually fix files on disk.

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
- Format C#: `dotnet tool restore && dotnet tool run csharpier format`
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
