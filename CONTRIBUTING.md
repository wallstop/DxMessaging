# Contributing

Thanks for helping improve DxMessaging!

Before committing, please enable our git hooks and local linters so you catch issues early:

- Install pre-commit: `pip install pre-commit` or `pipx install pre-commit`
- Install hooks: `pre-commit install`
- Run on all files: `pre-commit run --all-files`

Line endings: Git already normalizes files to CRLF through `.gitattributes`. Run this once after cloning (especially on Windows) so your working tree matches CI:

```bash
git config core.autocrlf false
git add --renormalize .
```

What runs locally:

- Markdown link text check: enforces human-readable link text (no raw file names/paths)
- Internal markdown link validity: verifies relative links and anchors point to real files/sections
- Markdown style and formatting: markdownlint (auto-fix common issues)
- JSON/.asmdef formatting: Prettier (2-space indent)
- YAML formatting: Prettier (2-space indent) + yamllint

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

If you do still need to repair line endings manually (for example, after copying files from an external tool), run `node scripts/fix-eol.js -v` and then re-stage the affected files.
