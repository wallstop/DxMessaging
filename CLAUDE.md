# Claude Configuration

See the [AI Agent Guidelines](./.llm/context.md) for all AI agent guidelines.

Three project-wide rules:

- Documentation must be pure ASCII (see [ASCII-only documentation guideline](./.llm/skills/documentation/ascii-only-docs.md)).
- Code samples must compile (see [Code samples must compile guideline](./.llm/skills/documentation/code-samples-must-compile.md)).
- For user-visible code changes (`Runtime/`, `Samples~/`, user-facing `Editor/`, `SourceGenerators/WallstopStudios.DxMessaging.SourceGenerators/`, or `SourceGenerators/WallstopStudios.DxMessaging.Analyzer/Analyzers/`), run `npm run validate:changelog:coverage` and rewrite `W002` entries around user impact before finishing.
