# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed

- Fixed a bug where no messages would get received by any listeners due to specifics in Unity play mode timings

## [2.1.8]

### Fixed

- Added npmignore for proper npm publishing (incorrectly packaging some items)

## [2.1.7]

### Changed

- Improved README with prominent Mental Model section
- Added Mermaid diagrams and decision flowchart for choosing message types
- Added Common Mistakes callout with troubleshooting link
- Updated performance comparison table with accurate benchmark range (10-17M ops/sec)

### Fixed

- Regenerated corrupted meta files in `scripts/wiki`

## [2.1.6]

### Added

- Concepts index page and Mental Model documentation for understanding DxMessaging's design principles

### Fixed

- Orphaned documentation pages in Concepts section now included in mkdocs.yml navigation
- Burst compiler assembly resolution errors when using DxMessaging as a package on disk and building for player platforms. Benchmarks and integration test assembly definitions now specify Editor-only platform to prevent Burst from attempting to resolve these assemblies during player builds.

## [2.1.5]

### Added

- GitHub Pages documentation deployment with MkDocs Material theme
- Wiki synchronization workflow that automatically syncs documentation to GitHub Wiki
- Documentation validation workflow that runs on pull requests and pushes
- MkDocs build validation in pre-push hooks
- Searchable documentation site at <https://wallstop.github.io/DxMessaging/>
- Theme-aware Mermaid diagrams with automatic light/dark mode switching for GitHub Pages
- User-visible error messages when Mermaid diagrams fail to render

### Changed

- Updated `documentationUrl` in package.json to point to GitHub Pages site
- Enhanced README.md with links to documentation site, wiki, and changelog
- Mermaid diagrams now use neutral theme fallback for GitHub/VSCode markdown preview compatibility

### Fixed

- Comprehensive syntax highlighting for C# code blocks in documentation with distinct colors for keywords, types, functions, strings, numbers, comments, namespaces, and attributes
- WCAG AA accessibility compliance for code syntax highlighting in both light and dark themes
