# Getting Started with DxMessaging

[← Back to Index](Index.md) | [Visual Guide](VisualGuide.md) | [Quick Start](QuickStart.md) | [FAQ](FAQ.md)

---

This guide covers the basics of DxMessaging. By the end, you'll understand what DxMessaging is, when it might be useful, and how to use it in your Unity projects.

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

**One approach:** The Player script needs references to all 6 systems. Or they all need references to the Player. This can create tight coupling.

**With DxMessaging:** The Player emits one message: `TookDamage(25)`. Interested systems receive it through subscriptions. This approach decouples systems and handles cleanup automatically.

---

**Technical summary:** DxMessaging is a type-safe messaging system that provides an alternative to C# events, UnityEvents, and global event buses.

**Think of it as:** An alternative event system designed for larger projects.

### What you get

- **Decoupled systems** - no manual subscribe/unsubscribe, leak prevention built-in
- **Predictable execution** - priority-based ordering, see exactly what runs when
- **Actually debuggable** - Inspector shows message history with timestamps
- **Designed to scale** - works for prototypes and larger codebases

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

**Need more detail?** See the [Visual Guide](VisualGuide.md) for additional explanations, or [MessageTypes](MessageTypes.md) for technical details.

## Common Patterns

### Example use cases

- **UI reacting to gameplay** - Health bar updates when player takes damage (without UI knowing about Player)
- **Achievement systems** - Track ALL kills across ALL enemies with ONE listener
- **Cross-system coordination** - Scene transitions coordinate audio, save system, and UI automatically
- **Input handling** - Decouple input system from player controller
- **Analytics** - Track all events without polluting gameplay code

**Want code examples?** See [Patterns](Patterns.md) for example patterns and [ListeningPatterns](ListeningPatterns.md) for additional techniques.

## Troubleshooting

### "My handler isn't being called"

Checklist:

1. Did you call `base.RegisterMessageHandlers()` first?
1. Is your component enabled in the scene?
1. Are you emitting to the right target? (Check GameObject vs Component)
1. Check the Inspector - does the registration show up?

#### "My message is firing twice"

- Check if you accidentally registered the same handler multiple times
- Make sure you're calling `base.RegisterMessageHandlers()` only once

##### "I get a compile error on `[DxAutoConstructor]`"

- Did you mark the struct as `partial`? (required for code generation)
- Example: `public readonly partial struct MyMessage`

###### "Which message type should I use"

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

// Emit (broadcasts always require a source)
var damage = new TookDamage(25);
damage.EmitGameObjectBroadcast(enemyGO);  // enemyGO is the source
var heal = new Heal(10);
heal.EmitGameObjectTargeted(playerGO);
```
