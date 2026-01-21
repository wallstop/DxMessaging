# Repository Guidelines

## Project Structure & Module Organization

- [Runtime](Runtime/) — core library (`DxMessaging.Core`) and Unity components (`DxMessaging.Unity`).
- [Editor](Editor/) — editor utilities, analyzers, and setup (`DxMessaging.Editor`).
- [SourceGenerators](SourceGenerators/) — Roslyn source generators (`netstandard2.0`).
- [Tests/Runtime](Tests/Runtime/) — NUnit/Unity Test Framework tests (e.g., [Core Nominal tests](Tests/Runtime/Core/NominalTests.cs)).
- [Docs](Docs/) — usage patterns and examples.
- Package manifest: [package.json](package.json) (published to NPM/UPM).

## Build, Test, and Development Commands

- Format: `dotnet tool restore` then `dotnet tool run csharpier format`.
- Build generators: `dotnet build SourceGenerators/WallstopStudios.DxMessaging.SourceGenerators/WallstopStudios.DxMessaging.SourceGenerators.csproj`.
- Unity tests: open a Unity 2021.3+ project that references this package, then Window > Test Runner > PlayMode. CLI example: `Unity -batchmode -nographics -quit -projectPath <your_project> -runTests -testPlatform PlayMode -testResults ./TestResults.xml`.

## Coding Style & Naming Conventions

- Indent with 4 spaces for `.cs` (JSON/YAML: 2). CRLF, UTF‑8 BOM (see [.editorconfig](.editorconfig)).
- Prefer explicit types over `var`. Braces required. `using` directives inside the namespace.
- Naming: `PascalCase` for types/methods/properties; interfaces `I*`; type parameters `T*`; events prefixed `On*`; public fields lowerCamelCase (matches examples in the [README](README.md)).
- Place code under `DxMessaging.Core`, `DxMessaging.Unity`, or `DxMessaging.Editor` as appropriate.
- Do not use underscores in function names, especially test function names.
- Do not use regions, anywhere, ever.
- Avoid `var` wherever possible, use expressive types.
- Do not use nullable reference types.

## Scripting Guidelines

Scripts in `scripts/` may be PowerShell (`.ps1`) or JavaScript (`.js`). Follow these practices:

- When parsing structured command output (git, build tools, etc.), prefer pattern matching over fixed positional indices. For example, use `Where-Object { $_ -like 'i/*' }` instead of assuming `[0]`.
- Validate array/collection length before accessing by index.
- Keep PowerShell and JavaScript implementations in sync when both exist for the same task.
- Add comments explaining the expected format of external command output.

## Testing Guidelines

- Frameworks: NUnit + Unity Test Framework. Use `[Test]`/`[UnityTest]` as needed.
- Location: add files under [Tests/Runtime](Tests/Runtime/) `<Area>/` named `*Tests.cs` with classes `*Tests`.
- Keep tests independent: prefer a local `MessageBus` and explicit `MessageRegistrationToken` lifecycles.
- Do not use underscores in test function names.
- Prefer expressive assertions and failure messages so it is clear what exactly is failing when a test fails.
- Do not use regions.
- Try to use minimal comments and instead rely on expressive naming conventions and assertions.
- Do not use Description annotations for tests.
- Do not create `async Task` test methods - the Unity test runner does not support this. Make do with `IEnumerator` based UnityTestMethods.
- Do not use `Assert.ThrowsAsync`, it does not exist.
- When asserting that UnityEngine.Objects are null or not null, please check for null directly (thing != null, thing == null), to properly adhere to Unity Object existence checks.

## Commit & Pull Request Guidelines

- Commits: short, imperative subject; group related changes; reference issues/PRs (e.g., “Fix registration dedupe (#123)”).
- PRs: include a clear description, linked issues, before/after notes for performance changes (see [Tests/Runtime/Benchmarks](Tests/Runtime/Benchmarks/)), and tests for bug fixes/features.
- Releasing: changes to [package.json](package.json) on `master` may trigger the NPM publish workflow.

## Security & Configuration Tips

- Editor analyzer DLLs are copied into the Unity project by [SetupCscRsp.cs](Editor/SetupCscRsp.cs); do not commit generated DLLs into this repo.
- Keep public APIs minimal and consistent; avoid breaking changes without a major version bump.
