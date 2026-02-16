#!/usr/bin/env node

/**
 * Update llms.txt with current project information
 *
 * This script generates/updates llms.txt by extracting information from:
 * - package.json (version, description)
 * - mkdocs.yml (documentation structure)
 * - .llm/ directory (skill count and categories)
 *
 * Usage:
 *   node scripts/update-llms-txt.js [--check]
 *
 * Options:
 *   --check  Verify llms.txt is up-to-date without modifying it
 *
 * Exit codes:
 *   0 - Success (file updated or already current)
 *   1 - Error or --check found differences
 */

const fs = require("fs");
const path = require("path");

const ROOT_DIR = path.resolve(__dirname, "..");
const LLMS_TXT_PATH = path.join(ROOT_DIR, "llms.txt");
const PACKAGE_JSON_PATH = path.join(ROOT_DIR, "package.json");
const LLM_SKILLS_DIR = path.join(ROOT_DIR, ".llm", "skills");

/**
 * Count markdown files in .llm/skills directory
 */
function countSkillFiles() {
  let count = 0;

  function walkDir(dir) {
    const entries = fs.readdirSync(dir, { withFileTypes: true });
    for (const entry of entries) {
      const fullPath = path.join(dir, entry.name);
      if (entry.isDirectory()) {
        walkDir(fullPath);
      } else if (entry.isFile() && entry.name.endsWith(".md")) {
        count++;
      }
    }
  }

  if (fs.existsSync(LLM_SKILLS_DIR)) {
    walkDir(LLM_SKILLS_DIR);
  }

  return count;
}

/**
 * Get skill categories from .llm/skills directory
 */
function getSkillCategories() {
  const categories = [];

  if (fs.existsSync(LLM_SKILLS_DIR)) {
    const entries = fs.readdirSync(LLM_SKILLS_DIR, { withFileTypes: true });
    for (const entry of entries) {
      if (entry.isDirectory()) {
        categories.push(entry.name);
      }
    }
  }

  return categories.sort();
}

/**
 * Extract version and description from package.json
 */
function getPackageInfo() {
  const pkg = JSON.parse(fs.readFileSync(PACKAGE_JSON_PATH, "utf8"));
  return {
    version: pkg.version,
    description: pkg.description,
    name: pkg.name,
    displayName: pkg.displayName,
  };
}

/**
 * Generate llms.txt content
 */
function generateLlmsTxt() {
  const pkg = getPackageInfo();
  const skillCount = countSkillFiles();
  const skillCategories = getSkillCategories();
  const currentDate = new Date().toISOString().split("T")[0];

  // Format skill categories for display
  const skillCategoriesText = skillCategories
    .map((cat) => `  - **${cat}/**`)
    .join("\n");

  return `# DxMessaging

> Type-safe, synchronous event bus and messaging system for Unity

## Overview

DxMessaging is a high-performance messaging library for Unity (v2021.3+) that replaces traditional C# events and UnityEvents with a type-safe, lifecycle-managed communication pattern. It enables decoupled communication between game systems without direct references.

**Version:** ${pkg.version}
**License:** MIT
**Repository:** https://github.com/wallstop/DxMessaging
**Documentation:** https://wallstop.github.io/DxMessaging/

## Quick Facts

- **Language:** C# (.NET Standard 2.0)
- **Platform:** Unity 2021.3+
- **Package Manager:** OpenUPM, npm, Unity Package Manager
- **Tests:** NUnit + Unity Test Framework
- **Documentation:** MkDocs Material

## Key Features

- **Three Message Types:** Untargeted (PA System), Targeted (Commands), Broadcast (Observable Facts)
- **Automatic Lifecycle Management:** No manual unsubscribe needed, prevents memory leaks
- **Zero Coupling:** Systems communicate without direct references
- **Inspector Diagnostics:** Built-in Unity Editor tools showing message flow, timestamps, and payloads
- **Priority-based Execution:** Control message handler ordering
- **Interceptor Pipeline:** Validate and normalize messages before handlers execute
- **DI Framework Support:** Integrations for Zenject, VContainer, and Reflex
- **Source Generators:** Auto-constructor generation for messages
- **Low Allocation Design:** Struct-based with minimal GC pressure
- **Local Bus Islands:** Isolated testing with zero global state

## Core Concepts

### Message Types

1. **Untargeted Messages** - Global announcements (like a PA system)
   - No specific target
   - Anyone can listen
   - Example: Game settings changed

2. **Targeted Messages** - Commands to specific entities
   - Has a specific GameObject/Component target
   - Only target and its children listen
   - Example: Heal this specific character

3. **Broadcast Messages** - Observable facts from a source
   - Has a source GameObject/Component
   - Anyone can observe what happened
   - Example: This enemy took damage

### Message Flow

\`\`\`text
Emitter → MessageBus → Interceptors → Handlers (by priority)
\`\`\`

## Project Structure

\`\`\`text
/Runtime/Core/          Core messaging engine (MessageBus, Messages, Attributes)
/Runtime/Unity/         Unity integration (Components, DI support)
/Editor/                Inspector tools, analyzers, custom editors
/SourceGenerators/      C# Source Generators for auto-constructors
/Tests/Runtime/         NUnit tests
/Samples~/              Example projects (Mini Combat, DI, Inspector)
/docs/                  MkDocs documentation site
\`\`\`

## Getting Started

### Installation (OpenUPM - Recommended)

\`\`\`bash
openupm add ${pkg.name}
\`\`\`

### Basic Usage

\`\`\`csharp
// Define a message
public readonly struct PlayerHealthChanged
{
    public readonly float newHealth;
    
    [DxAutoConstructor] // Auto-generates constructor
    public PlayerHealthChanged() { }
}

// Send a message
MessageBus.Emit(new PlayerHealthChanged(75f));

// Listen for messages
MessageBus.Register<PlayerHealthChanged>(msg => {
    Debug.Log($"Health changed to {msg.newHealth}");
});
\`\`\`

### Unity Component Integration

\`\`\`csharp
public class HealthDisplay : MessageAwareComponent
{
    void OnEnable()
    {
        Register<PlayerHealthChanged>(OnHealthChanged);
    }
    
    void OnHealthChanged(PlayerHealthChanged msg)
    {
        // Automatically unregistered when component is disabled/destroyed
    }
}
\`\`\`

## Documentation Structure

### Getting Started
- [Overview](https://wallstop.github.io/DxMessaging/getting-started/overview/)
- [Installation](https://wallstop.github.io/DxMessaging/getting-started/install/)
- [Quick Start](https://wallstop.github.io/DxMessaging/getting-started/quick-start/)
- [Visual Guide](https://wallstop.github.io/DxMessaging/getting-started/visual-guide/)

### Concepts
- [Mental Model](https://wallstop.github.io/DxMessaging/concepts/mental-model/) - Core philosophy and design principles
- [Message Types](https://wallstop.github.io/DxMessaging/concepts/message-types/) - Untargeted, Targeted, Broadcast
- [Listening Patterns](https://wallstop.github.io/DxMessaging/concepts/listening-patterns/)
- [Targeting & Context](https://wallstop.github.io/DxMessaging/concepts/targeting-and-context/)
- [Interceptors & Ordering](https://wallstop.github.io/DxMessaging/concepts/interceptors-and-ordering/)

### Guides
- [Patterns](https://wallstop.github.io/DxMessaging/guides/patterns/) - Best practices and common patterns
- [Unity Integration](https://wallstop.github.io/DxMessaging/guides/unity-integration/)
- [Testing](https://wallstop.github.io/DxMessaging/guides/testing/) - Testing strategies for message-based systems
- [Diagnostics](https://wallstop.github.io/DxMessaging/guides/diagnostics/) - Inspector tools and debugging
- [Migration Guide](https://wallstop.github.io/DxMessaging/guides/migration-guide/)

### Architecture
- [Design & Architecture](https://wallstop.github.io/DxMessaging/architecture/design-and-architecture/)
- [Performance](https://wallstop.github.io/DxMessaging/architecture/performance/) - Benchmarks (10-17M ops/sec)
- [Comparisons](https://wallstop.github.io/DxMessaging/architecture/comparisons/) - vs Events, UnityEvents, other buses

### Advanced Topics
- [Emit Shorthands](https://wallstop.github.io/DxMessaging/advanced/emit-shorthands/)
- [Message Bus Providers](https://wallstop.github.io/DxMessaging/advanced/message-bus-providers/)
- [Registration Builders](https://wallstop.github.io/DxMessaging/advanced/registration-builders/)
- [Runtime Configuration](https://wallstop.github.io/DxMessaging/advanced/runtime-configuration/)

### Integrations
- [Zenject](https://wallstop.github.io/DxMessaging/integrations/zenject/) - Extenject/Zenject DI integration
- [VContainer](https://wallstop.github.io/DxMessaging/integrations/vcontainer/) - VContainer DI integration
- [Reflex](https://wallstop.github.io/DxMessaging/integrations/reflex/) - Reflex DI integration

### Reference
- [Quick Reference](https://wallstop.github.io/DxMessaging/reference/quick-reference/)
- [FAQ](https://wallstop.github.io/DxMessaging/reference/faq/)
- [Glossary](https://wallstop.github.io/DxMessaging/reference/glossary/)
- [Troubleshooting](https://wallstop.github.io/DxMessaging/reference/troubleshooting/)

## Key Files

- [README.md](https://github.com/wallstop/DxMessaging/blob/master/README.md) - 30-second pitch, mental models, quick start
- [CHANGELOG.md](https://github.com/wallstop/DxMessaging/blob/master/CHANGELOG.md) - Version history
- [CONTRIBUTING.md](https://github.com/wallstop/DxMessaging/blob/master/CONTRIBUTING.md) - Contribution guidelines
- [package.json](https://github.com/wallstop/DxMessaging/blob/master/package.json) - Package manifest
- [.llm/context.md](https://github.com/wallstop/DxMessaging/blob/master/.llm/context.md) - Repository guidelines for AI agents

## Development

### Build & Test Commands

\`\`\`bash
# Format code
dotnet tool restore
dotnet tool run csharpier .

# Build source generators
dotnet build SourceGenerators/WallstopStudios.DxMessaging.SourceGenerators/WallstopStudios.DxMessaging.SourceGenerators.csproj

# Run tests (Unity Test Runner)
# Open Unity 2021.3+ project → Window → Test Runner → PlayMode

# Format markdown
npm run format:md

# Lint markdown
npm run lint:markdown

# Validate YAML
npm run check:yaml

# Spell check
npx cspell "**/*"
\`\`\`

### Project Standards

- **Code Style:** 4-space indent, explicit types (no \`var\`), PascalCase for public APIs
- **Line Endings:** CRLF for most files, LF for shell scripts
- **Tests:** NUnit + Unity Test Framework, no underscores in test names
- **Documentation:** MkDocs Material, lazy numbering for ordered lists
- **Commits:** Imperative mood, reference issues/PRs

## AI Agent Context

This repository includes comprehensive AI agent guidance in the \`.llm/\` directory:

- **[.llm/context.md](https://github.com/wallstop/DxMessaging/blob/master/.llm/context.md)** - Repository guidelines, coding standards, testing policies
- **[.llm/skills/](https://github.com/wallstop/DxMessaging/tree/master/.llm/skills)** - ${skillCount}+ specialized skill documents covering:
${skillCategoriesText}

## Common Pitfalls & Solutions

### Memory Leaks
**Problem:** Forgot to unsubscribe from events
**Solution:** Use \`MessageAwareComponent\` or \`MessageHandler\` for automatic lifecycle management

### Message Not Received
**Problem:** Handler registered after message was emitted
**Solution:** Messages are synchronous; ensure registration happens during \`Awake\`/\`OnEnable\`

### Wrong Message Type
**Problem:** Used Broadcast when Targeted was needed
**Solution:** See [Mental Model](https://wallstop.github.io/DxMessaging/concepts/mental-model/) for type selection guidance

### Performance Issues
**Problem:** Too many handlers or heavy interceptors
**Solution:** Use priority ordering, profile with Inspector diagnostics

## Performance Characteristics

- **Message Emit:** 10-17M operations/second (OS-specific)
- **Memory:** Low allocation, struct-based design
- **Handler Invocation:** Direct calls, no reflection
- **Registration:** O(1) add/remove with backing dictionary
- **Priority Ordering:** Stable sort on registration

See [Performance Documentation](https://wallstop.github.io/DxMessaging/architecture/performance/) for detailed benchmarks.

## Examples

### Mini Combat Sample
Demonstrates all three message types in a simple combat scenario:
- **Untargeted:** Game settings changes
- **Targeted:** Heal specific character
- **Broadcast:** Enemy takes damage

**Location:** \`Samples~/Mini Combat\`

### DI Integration Sample
Shows integration with Zenject, VContainer, and Reflex:
- Scoped message buses
- Container lifecycle integration
- IMessageRegistrationBuilder usage

**Location:** \`Samples~/DI\`

### Inspector Diagnostics Sample
Demonstrates debugging tools:
- Global observer pattern
- Message flow visualization
- Timestamp and payload inspection

**Location:** \`Samples~/UI Buttons + Inspector\`

## Support & Community

- **Issues:** https://github.com/wallstop/DxMessaging/issues
- **Discussions:** https://github.com/wallstop/DxMessaging/discussions
- **Email:** wallstop@wallstopstudios.com
- **OpenUPM:** https://openupm.com/packages/com.wallstop-studios.dxmessaging/

## License

MIT License - see [LICENSE.md](https://github.com/wallstop/DxMessaging/blob/master/LICENSE.md)

Copyright (c) 2017-2026 Wallstop Studios

---

**Last Updated:** ${currentDate}
**Generated from:** package.json v${pkg.version}, mkdocs.yml, .llm/context.md
`;
}

/**
 * Main execution
 */
function main() {
  const args = process.argv.slice(2);
  const checkMode = args.includes("--check");

  try {
    const newContent = generateLlmsTxt();

    if (checkMode) {
      // Check if file exists and matches
      if (!fs.existsSync(LLMS_TXT_PATH)) {
        console.error("ERROR: llms.txt does not exist");
        process.exit(1);
      }

      const currentContent = fs.readFileSync(LLMS_TXT_PATH, "utf8");
      if (currentContent.trim() !== newContent.trim()) {
        console.error("ERROR: llms.txt is out of date");
        console.error("Run: node scripts/update-llms-txt.js");
        process.exit(1);
      }

      console.log("✓ llms.txt is up to date");
      process.exit(0);
    }

    // Update mode - write the file
    fs.writeFileSync(LLMS_TXT_PATH, newContent, "utf8");
    console.log("✓ Updated llms.txt");
    process.exit(0);
  } catch (error) {
    console.error("ERROR:", error.message);
    process.exit(1);
  }
}

// Only run if executed directly (not required as module)
if (require.main === module) {
  main();
}

module.exports = { generateLlmsTxt, countSkillFiles, getSkillCategories };
