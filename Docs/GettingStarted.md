# Getting Started with DxMessaging

[← Back to Index](Index.md) | [Visual Guide](VisualGuide.md) | [Quick Start](QuickStart.md) | [FAQ](FAQ.md)

---

Welcome! This guide will take you from zero to productive with DxMessaging in about 10 minutes. By the end, you'll understand what DxMessaging is, why it's powerful, and how to use it effectively in your Unity projects.

## 🎯 Your Goal

By the end of this guide, you will:

- Understand the three message types and when to use each
- Write your first message handler in Unity
- Know when to use DxMessaging vs. traditional events
- Avoid common beginner mistakes

## Table of Contents

- [What is DxMessaging?](#what-is-dxmessaging)
- [Quick Start](#quick-start)
- [Message Types](#message-types)
- [Common Patterns](#common-patterns)
- [Troubleshooting](#troubleshooting)
- [Next Steps](#next-steps)
- [Quick Reference](#quick-reference)

## What is DxMessaging?

**Picture this:** You're building a Unity game. Your player takes damage. Now you need to:

- Update the health bar UI
- Play a damage sound
- Show a damage number popup
- Track damage for analytics
- Check if an achievement unlocked
- Maybe trigger a tutorial tip

**The old way:** The Player script needs references to all 6 systems. Or they all need references to the Player. It's a tangled mess.

**The DxMessaging way:** The Player emits one message: `TookDamage(25)`. Everyone who cares receives it automatically. Zero coupling, zero leaks, zero hassle.

---

**Technical summary:** DxMessaging is a high-performance, type-safe messaging system that replaces C# events, UnityEvents, and global event buses with a clean, observable, and predictable communication pattern.

**Think of it as:** The event system Unity should have shipped with.

### What you get

- **Decoupled systems** - no manual subscribe/unsubscribe, impossible to leak
- **Predictable execution** - priority-based ordering, see exactly what runs when
- **Actually debuggable** - Inspector shows message history with timestamps
- **Scales effortlessly** - works for prototypes and 100k+ line codebases

## Quick Start

1. Define messages

```csharp
using DxMessaging.Core.Attributes;

[DxTargetedMessage]
[DxAutoConstructor]
public readonly partial struct Heal
{
    public readonly int amount;
}

[DxBroadcastMessage]
[DxAutoConstructor]
public readonly partial struct TookDamage
{
    public readonly int amount;
}
```

1. Listen in a component

```csharp
using DxMessaging.Core;
using DxMessaging.Core.Extensions;
using DxMessaging.Unity;

public class GameUI : MessageAwareComponent
{
    // Assume this references a Player GameObject
    public UnityEngine.GameObject playerGO;

    protected override void RegisterMessageHandlers()
    {
        base.RegisterMessageHandlers();

        _ = Token.RegisterGameObjectTargeted<Heal>(playerGO, OnPlayerHealed);
        _ = Token.RegisterBroadcastWithoutSource<TookDamage>(OnAnyDamage);
    }

    private void OnPlayerHealed(ref Heal m) => UpdateHealthBar(m.amount);
    private void OnAnyDamage(InstanceId source, TookDamage m) => ShowDamageEffect(source);
}
```

1. Send a message

```csharp
var heal = new Heal(10);
heal.EmitGameObjectTargeted(playerGameObject);
```

## Message Types

### New to messaging? Use this decision tree

```text
Is it a global announcement? (pause, settings, scene load)
  → Use UNTARGETED

Are you commanding a specific entity? (heal Player, open Chest #3)
  → Use TARGETED

Is an entity announcing something happened? (Enemy died, Chest opened)
  → Use BROADCAST
```

#### Examples

```csharp
// Untargeted: "Everyone, the game paused!"
[DxUntargetedMessage]
public struct GamePaused { }

// Targeted: "Player, heal yourself by 50!"
[DxTargetedMessage]
public struct Heal { public int amount; }

// Broadcast: "I (Enemy #3) just died!"
[DxBroadcastMessage]
public struct EnemyDied { public string enemyName; }
```

**Still confused?** See the [Visual Guide](VisualGuide.md) for beginner-friendly explanations, or [MessageTypes](MessageTypes.md) for technical details.

## Common Patterns

### Want to see real examples? Here's what DxMessaging excels at

- **UI reacting to gameplay** - Health bar updates when player takes damage (without UI knowing about Player)
- **Achievement systems** - Track ALL kills across ALL enemies with ONE listener
- **Cross-system coordination** - Scene transitions coordinate audio, save system, and UI automatically
- **Input handling** - Decouple input system from player controller
- **Analytics** - Track all events without polluting gameplay code

**Want code examples?** See [Patterns](Patterns.md) for production-ready patterns and [ListeningPatterns](ListeningPatterns.md) for advanced techniques.

## Troubleshooting

### "My handler isn't being called!"

✅ Checklist:

1. Did you call `base.RegisterMessageHandlers()` first?
1. Is your component enabled in the scene?
1. Are you emitting to the right target? (Check GameObject vs Component)
1. Check the Inspector - does the registration show up?

#### "My message is firing twice!"

- Check if you accidentally registered the same handler multiple times
- Make sure you're calling `base.RegisterMessageHandlers()` only once

##### "I get a compile error on `[DxAutoConstructor]`"

- Did you mark the struct as `partial`? (required for code generation)
- Example: `public readonly partial struct MyMessage`

###### "Which message type should I use?"

- See the decision tree in [Message Types](#message-types) above
- When in doubt, start with `Broadcast` - it's the most flexible

**Still stuck?** See [Troubleshooting](Troubleshooting.md) for the complete guide or [FAQ](FAQ.md) for common questions.

## Next Steps

- [Visual Guide](VisualGuide.md)
- [Quick Reference](QuickReference.md)
- [Diagnostics](Diagnostics.md)
- [Interceptors and Ordering](InterceptorsAndOrdering.md)

## Quick Reference

```csharp
// Listen
_ = Token.RegisterBroadcastWithoutSource<TookDamage>(OnAnyDamage);
_ = Token.RegisterGameObjectTargeted<Heal>(playerGO, OnPlayerHealed);

// Emit
new TookDamage(25).EmitBroadcastWithoutSource();
new Heal(10).EmitGameObjectTargeted(playerGO);
```
