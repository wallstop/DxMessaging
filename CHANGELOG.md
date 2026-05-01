# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- New Roslyn base-call analyzer (`MessageAwareComponentBaseCallAnalyzer`) that flags `MessageAwareComponent` subclasses whose lifecycle overrides forget to invoke `base.Awake()`, `base.OnEnable()`, `base.OnDisable()`, `base.OnDestroy()`, or `base.RegisterMessageHandlers()`. Introduces diagnostics `DXMSG006` (missing base call), `DXMSG007` (lifecycle method hidden with `new`), `DXMSG008` (opt-out marker), `DXMSG009` (method implicitly hides a lifecycle method without `override`/`new`), and `DXMSG010` (`base.{method}()` chains into an override that does not reach `MessageAwareComponent`). Severity is tunable per project via `.editorconfig` (e.g. `dotnet_diagnostic.DXMSG006.severity = error`). Ships as a separate `WallstopStudios.DxMessaging.Analyzer.dll` deployed alongside the existing source-generator DLL by `SetupCscRsp` so it loads under both Unity 2021's Roslyn 3.8 analyzer host and newer Unity versions.
- New public `[DxIgnoreMissingBaseCall]` attribute (`DxMessaging.Core.Attributes`) for source-level opt-out of the base-call analyzer. Applied to a class, every guarded lifecycle method on that class is exempt; applied to a single method, only that method is exempt. The analyzer still emits an Info-level `DXMSG008` at the suppression site so opt-outs remain auditable, and the inspector overlay's snapshot honours the same scoping (method-level suppresses only the annotated method, type-level opts out the entire type). Not inherited -- derived classes must opt out explicitly.
- New inspector overlay (`MessageAwareComponentInspectorOverlay`) for every `MessageAwareComponent` subclass: missing-base-call warnings reported by the analyzer or harvested from the Unity console are surfaced as a HelpBox in the inspector header without clobbering user-defined `[CustomEditor]`s (the overlay hooks `Editor.finishedDefaultHeaderGUI`). The overlay restores the previous session's report immediately on Unity Editor startup (loaded from `Library/DxMessaging/baseCallReport.json`) instead of waiting for the first post-reload scan to complete; the HelpBox is annotated `(cached from previous session -- refreshing...)` until the first scan refreshes it. A companion fallback editor (`MessageAwareComponentFallbackEditor`) hosts the overlay for subclasses with no other custom editor and renders the body via `DrawDefaultInspector()` so subclasses with no serialized fields no longer leave an empty vertical gap below the inspector header.
- New DxMessaging project-wide settings asset (`DxMessagingSettings`, stored at `Assets/Editor/DxMessagingSettings.asset`) accessible from Unity's Project Settings. Controls diagnostics targets applied to `IMessageBus.GlobalDiagnosticsTargets`, the editor message buffer size, the domain-reload warning suppression, the base-call analyzer toggle, the project-wide base-call ignore list, and the optional Unity console bridge that feeds the inspector overlay.
- New `docs/reference/analyzers.md` reference page documenting every `DXMSG###` diagnostic the package emits, with severity, source generator/analyzer, trigger conditions, message text, and code samples for each. Added to the Reference section of the documentation site navigation.
- Added `llms.txt` file following [llmstxt.org](https://llmstxt.org/) standard for improved AI agent integration
- Added automation script `scripts/update-llms-txt.js` to keep `llms.txt` up-to-date
- Added documentation about AI agent integration in README

### Fixed

- Fixed `scripts/validate-workflows.js` to fail loudly when `git` is unavailable during ignore-policy checks instead of silently bypassing validation.
- Fixed `scripts/validate-workflows.js` violation path reporting to compute file paths relative to the provided `repoRoot` override.
- Removed unused `using Unity;` imports from editor custom-editor and harness files to avoid unnecessary namespace dependencies.

## [2.2.0]

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
