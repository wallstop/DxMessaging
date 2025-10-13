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

DxMessaging is a high‑performance, type‑safe messaging system for Unity. It replaces sprawling C# events, brittle UnityEvents, and ad‑hoc global buses with clean, observable, and predictable communication.

Think of it as the event system you wish Unity had built‑in:

- Decoupled systems without manual subscribe/unsubscribe headaches
- Predictable execution order with powerful interception and diagnostics
- Scales from prototypes to large production codebases

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

- Untargeted: fire‑and‑forget announcements (no specific receiver)
- Targeted: deliver to a specific receiver (GameObject, InstanceId, etc.)
- Broadcast: deliver to all listeners (optionally including source metadata)

See [MessageTypes](MessageTypes.md) for details and APIs.

## Common Patterns

- UI reacting to gameplay state via targeted messages
- Cross‑system notifications using broadcast messages
- Configuration changes/feature toggles via untargeted announcements

See [ListeningPatterns](ListeningPatterns.md) and [Patterns](Patterns.md) for deeper examples.

## Troubleshooting

- Nothing happens? Ensure your listener registered in `RegisterMessageHandlers` and the component is enabled.
- Duplicate behavior? Check for multiple registrations or missing token disposal on custom lifecycles.
- Ordering issues? Use interceptors/post‑processors to control/inspect flow.

See [Troubleshooting](Troubleshooting.md) for more.

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
