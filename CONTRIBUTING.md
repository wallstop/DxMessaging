## Contributing

Thanks for helping improve DxMessaging!

Before committing, please enable our git hooks and local linters so you catch issues early:

- Install pre-commit: `pip install pre-commit` or `pipx install pre-commit`
- Install hooks: `pre-commit install`
- Run on all files: `pre-commit run --all-files`

What runs locally:

- Markdown link text check: enforces human-readable link text (no raw file names/paths)
- Internal markdown link validity: verifies relative links and anchors point to real files/sections

On pull requests, CI also checks all markdown links (including external URLs) with lychee.

Handy commands:

- Internal links (local): `python .github/scripts/validate_markdown_links.py .`
- Format C#: `dotnet tool restore && dotnet tool run csharpier format`

