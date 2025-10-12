# Contributing

Thanks for helping improve DxMessaging!

Before committing, please enable our git hooks and local linters so you catch issues early:

- Install pre-commit: `pip install pre-commit` or `pipx install pre-commit`
- Install hooks: `pre-commit install`
- Run on all files: `pre-commit run --all-files`

What runs locally:

- Markdown link text check: enforces human-readable link text (no raw file names/paths)
- Internal markdown link validity: verifies relative links and anchors point to real files/sections
- Markdown style and formatting: markdownlint (auto-fix common issues)
- JSON/.asmdef formatting: Prettier (2-space indent)

On pull requests, CI also checks all markdown links (including external URLs) with lychee.

Handy commands:

- Internal links (local): `python .github/scripts/validate_markdown_links.py .`
- Lint markdown (all files): `pre-commit run markdownlint --all-files`
- Lint markdown (manual): `npx markdownlint-cli@0.41.0 "**/*.md" -c .markdownlint.jsonc --fix`
- Format JSON/.asmdef (all files): `pre-commit run prettier-json --all-files`
- Format JSON/.asmdef (manual): `npx prettier@3.3.3 --write "**/*.{json,asmdef}"`
- Format C#: `dotnet tool restore && dotnet tool run csharpier format`
