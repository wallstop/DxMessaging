---
title: "Documentation Style Guide"
id: "documentation-style-guide"
category: "documentation"
version: "1.0.0"
created: "2026-01-22"
updated: "2026-01-22"

source:
  repository: "wallstop-studios/com.wallstop-studios.dxmessaging"
  files:
    - path: "Docs/"
    - path: "README.md"
  url: "https://github.com/wallstop-studios/com.wallstop-studios.dxmessaging"

tags:
  - "documentation"
  - "style"
  - "writing"
  - "clarity"

complexity:
  level: "basic"
  reasoning: "Applies consistent writing patterns for technical docs"

impact:
  performance:
    rating: "none"
    details: "Documentation only"
  maintainability:
    rating: "medium"
    details: "Consistent style reduces confusion and review time"
  testability:
    rating: "none"
    details: "Documentation does not affect testability"

prerequisites:
  - "Understanding of Markdown"

dependencies:
  packages: []
  skills:
    - "documentation-updates"

applies_to:
  languages:
    - "Markdown"
  frameworks:
    - "Unity"
    - ".NET"

aliases:
  - "Docs writing style"

related:
  - "documentation-updates"
  - "documentation-xml-docs"
  - "documentation-code-samples"

status: "stable"
---

# Documentation Style Guide

> **One-line summary**: Write concise, action-first documentation that uses code to explain technical details.

## Overview

Clear documentation is concise, direct, and optimized for quick understanding. This guide keeps documentation consistent across README and Docs/ content.

## Problem Statement

Inconsistent style leads to verbose, unclear docs that are hard to scan and easy to misunderstand.

## Solution

### Be Concise

```markdown
<!-- BAD: Too verbose -->

In order to be able to successfully emit a message to a target, you will
first need to ensure that you have obtained a valid InstanceId for that
target, which can be accomplished by...

<!-- GOOD: Concise -->

To emit a targeted message, get the recipient's InstanceId first:
```

### Use Active Voice

```markdown
<!-- BAD: Passive -->

The message is emitted by the MessageBus to all registered handlers.

<!-- GOOD: Active -->

The MessageBus emits the message to all registered handlers.
```

### Lead with the Action

```markdown
<!-- BAD: Buries the lede -->

There are several ways to register a handler, but the most common approach
that you'll want to use in most cases is to call RegisterMessageHandler.

<!-- GOOD: Action first -->

Call `RegisterMessageHandler` to subscribe to messages:
```

### Use Code, Not Prose, for Technical Details

````markdown
<!-- BAD: Describing code in words -->

The method takes a type parameter for the message type, a target object,
and a handler delegate. It returns a registration handle.

<!-- GOOD: Show, don't tell -->

```csharp
MessageRegistrationHandle handle = _messageRegistrationToken.RegisterGameObjectTargeted<DamageMessage>(
    gameObject,
    HandleDamage
);
```
````

## Anti-Patterns

### Bad: Placeholder Documentation

```csharp
/// <summary>
/// TODO: Add documentation
/// </summary>
public void ImportantMethod() { }
```

**Why it's wrong**: Placeholder docs ship to users and provide no value.

### Bad: Restating the Obvious

```csharp
/// <summary>
/// Gets the message bus.
/// </summary>
/// <returns>The message bus.</returns>
public MessageBus GetMessageBus() { }
```

**Why it's wrong**: Adds noise without adding information.

**Correct**:

```csharp
/// <summary>
/// Gets the shared MessageBus instance for this scene.
/// </summary>
/// <returns>
/// The active MessageBus. Returns null if called before scene initialization.
/// </returns>
public MessageBus GetMessageBus() { }
```

## See Also

- [Documentation Updates](documentation-updates.md)
- [XML Documentation Standards](documentation-xml-docs.md)
- [Documentation Code Samples](documentation-code-samples.md)

## References

- [Unity Documentation Best Practices](https://docs.unity3d.com/Manual/BestPractices.html)

## Changelog

| Version | Date       | Changes         |
| ------- | ---------- | --------------- |
| 1.0.0   | 2026-01-22 | Initial version |
