# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- New Roslyn base-call analyzer (`MessageAwareComponentBaseCallAnalyzer`) that flags `MessageAwareComponent` subclasses whose lifecycle overrides forget to invoke `base.Awake()`, `base.OnEnable()`, `base.OnDisable()`, `base.OnDestroy()`, or `base.RegisterMessageHandlers()`. Introduces diagnostics `DXMSG006` (missing base call), `DXMSG007` (lifecycle method hidden with `new`), `DXMSG008` (opt-out marker), `DXMSG009` (method implicitly hides a lifecycle method without `override`/`new`), and `DXMSG010` (`base.{method}()` chains into an override that does not reach `MessageAwareComponent`). Severity is tunable per project via `.editorconfig` (e.g. `dotnet_diagnostic.DXMSG006.severity = error`). Ships as a separate `WallstopStudios.DxMessaging.Analyzer.dll` deployed alongside the existing source-generator DLL by `SetupCscRsp` so it loads under both Unity 2021's Roslyn 3.8 analyzer host and newer Unity versions. Diagnostic help links now open the current analyzer reference page in the DxMessaging repository.
- New public `[DxIgnoreMissingBaseCall]` attribute (`DxMessaging.Core.Attributes`) for source-level opt-out of the base-call analyzer. Applied to a class, every guarded lifecycle method on that class is exempt; applied to a single method, only that method is exempt. The analyzer still emits an Info-level `DXMSG008` at the suppression site so opt-outs remain auditable, and the inspector overlay's snapshot honours the same scoping (method-level suppresses only the annotated method, type-level opts out the entire type). Not inherited -- derived classes must opt out explicitly.
- New inspector overlay (`MessageAwareComponentInspectorOverlay`) for every `MessageAwareComponent` subclass: missing-base-call warnings reported by the analyzer or harvested from the Unity console are surfaced as a HelpBox in the inspector header without clobbering user-defined `[CustomEditor]`s (the overlay hooks `Editor.finishedDefaultHeaderGUI`). The overlay restores the previous session's report immediately on Unity Editor startup (loaded from `Library/DxMessaging/baseCallReport.json`) instead of waiting for the first post-reload scan to complete; the HelpBox is annotated `(cached from previous session -- refreshing...)` until the first scan refreshes it. A companion fallback editor (`MessageAwareComponentFallbackEditor`) hosts the overlay for subclasses with no other custom editor and renders the body via `DrawDefaultInspector()` so subclasses with no serialized fields no longer leave an empty vertical gap below the inspector header.
- New DxMessaging project-wide settings asset (`DxMessagingSettings`, stored at `Assets/Editor/DxMessagingSettings.asset`) accessible from Unity's Project Settings. Controls diagnostics targets applied to `IMessageBus.GlobalDiagnosticsTargets`, the editor message buffer size, the domain-reload warning suppression, the base-call analyzer toggle, the project-wide base-call ignore list, and the optional Unity console bridge that feeds the inspector overlay.
- New `docs/reference/analyzers.md` reference page documenting every `DXMSG###` diagnostic the package emits, with severity, source generator/analyzer, trigger conditions, message text, and code samples for each. Added to the Reference section of the documentation site navigation.
- Added `llms.txt` plus README onboarding guidance so users can connect AI assistants with accurate DxMessaging package context.
- Test-suite hardening: parameterized scenario fixture (`MessageScenario`, `MessageScenarios`, `ScenarioHarness`, `AllocationAssertions`) under `Tests/Runtime/TestUtilities/` enabling kind-parameterized tests.
- Behavioural gap closures: `HandlerExceptionTests`, `ReentrantEmissionTests`, `NullAndInvalidInputTests`, `SingleThreadContractTests` pinning exception-in-handler, re-entrancy, null-input, and threading contracts.
- `AllocationMatrixTests` covering zero-GC dispatch across kinds, interceptors, post-processors, diagnostics, and priority-based dispatch.
- Expanded coverage now pins source-generator and analyzer behaviour that users rely on: generic / record struct / nested partial / nullable annotation cases for `DxMessageIdGenerator`; `[DxOptionalParameter]` permutations and DXMSG005 boundary cases for `DxAutoConstructorGenerator`; positive opt-out cases for `DxIgnoreMissingBaseCallAttribute`. No runtime API change.
- `[Category("Stress")]`, `[Category("Performance")]`, and `[Category("Allocation")]` tagging across the suite to enable filtered runs and a default-suite speed budget under 60 seconds.
- `SuiteSpeedBudgetTest` as a default-suite speed guard rail.
- `TestAttributeContractTests` extensions enforcing kind-parameterization and allocation coverage discipline.

### Fixed

- Cross-priority deregistration during in-flight emit no longer drops handlers from the current dispatch. Previously, when a handler at one priority removed a handler at a later priority of the same emission, the later priority's typed-handler stack was rebuilt from the now-mutated registry on first touch and the scheduled-for-removal handler was silently skipped, breaking the documented "frozen handler list per emission" contract. This affected sourced-broadcast, broadcast-without-source, and targeted-without-targeting dispatch (the targeted/untargeted paths already pre-froze every bucket up-front). Two related defects are fixed in the same change: the sourced-broadcast and broadcast-without-source dispatch loops short-circuited on the live `cache.handlers.Count == 0` even when the per-emission snapshot still held the deregistered handler, and post-processor prefreeze took a single-bucket/single-entry fast-path that skipped pre-freezing per-MessageHandler post-processor caches -- a regular handler that registered a new post-processor on the same MessageHandler+priority during its own callback would then see the new post-processor fire on the in-flight emission instead of waiting for the next one. The bus now pre-freezes every priority bucket's typed-handler caches up-front for every dispatch surface (sourced-broadcast, broadcast-without-source, targeted-without-targeting), uses the per-emission snapshot count for the dispatch-loop early-out, and unconditionally pre-freezes post-processor caches; removals and post-processor registrations performed mid-emit now consistently only take effect on the next emission.
- `DxMessagingStaticState.Reset` is now race-safe against deferred deregistrations. Previously, when a message-aware component was destroyed but its disable callback had not yet run (Unity defers Object.Destroy to end of frame) and Reset ran in between, the deferred token teardown would log spurious "Received over-deregistration of {type} for {handler}" errors against the user's Unity console. The bus now stamps each captured deregister closure with a generation counter and silently no-ops closures captured before a Reset. Applied uniformly across every register entry point (untargeted, targeted, broadcast, GlobalAcceptAll, and all three interceptor kinds). The same race-safety guarantee is now propagated to user-installed custom global buses via `MessageBus.BumpResetGeneration()`, which `DxMessagingStaticState.Reset` invokes on the active global bus when it differs from the built-in default; the custom bus's sinks are intentionally left intact to avoid clobbering state the user installed it to preserve. User code is unaffected except that previously-spurious error logs disappear.
- `MessageRegistrationToken.RemoveRegistration(handle)` no longer leaks the staged registration entry, so a `Disable()`/`Enable()` cycle after `RemoveRegistration` no longer silently re-registers the removed handler. The fix also drops the matching metadata and call-count entries so diagnostic mode does not accumulate stale handles.

### Changed

- Mutation tests now exercise every messaging kind (Untargeted/Targeted/Broadcast) via a single parameterized fixture (`[ValueSource(MessageScenarios.AllKinds)]`) across `MutationDuringEmissionTests`, `MutationInterceptorTests`, and `MutationDestructionTests`. Users get tighter cross-kind parity guarantees; no runtime API change. (~720 lines of duplication removed; test count preserved.)
- `MessagingTestBase` now reseeds a deterministic `Random` with a logged seed (env var `DXMESSAGING_TEST_SEED` to override), polls a generous 1.5-second timeout for handler cleanup, drains the prior test's deferred destroy queue before resetting `DxMessagingStaticState` in `[UnitySetUp]`, and asserts the bus returns to a fresh state at the end of `[UnityTearDown]`.
- Renamed `UntargetedTests`, `TargetedTests`, `BroadcastTests` to `EmitUntargetedSpecificTests`, `EmitTargetedSpecificTests`, `EmitBroadcastSpecificTests` to clarify that kind-common tests live in `EmitTests` and kind-specific tests live in the renamed files. (Test-suite hardening is test-only; no `Runtime/` behavior was modified.)

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
