# Quick Start

This quick start walks you from install, to defining a message, to sending and receiving it in Unity.

Before you begin

- Install via UPM: [Install](Docs/Install.md)
- Target Unity 2021.3+ (see [Compatibility](Docs/Compatibility.md))

1) Define messages

```csharp
using DxMessaging.Core.Messages;
using DxMessaging.Core.Attributes;

// Recommended: attributes + auto constructor (beginner‑friendly)
[DxUntargetedMessage]
[DxAutoConstructor]
public readonly partial struct WorldRegenerated
{
    public readonly int seed;
}

[DxUntargetedMessage]
[DxAutoConstructor]
public readonly partial struct VideoSettingsChanged
{
    public readonly int width;
    public readonly int height;
}

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

// Performance option: generic interfaces on structs (zero boxing)
// public readonly struct Heal : ITargetedMessage<Heal> { public readonly int amount; public Heal(int amount) { this.amount = amount; } }
```

2) Add a messaging component

```csharp
using DxMessaging.Unity;
using DxMessaging.Core.Messages;
using UnityEngine;

public sealed class HealthUI : MessageAwareComponent
{
    protected override void RegisterMessageHandlers()
    {
        _ = Token.RegisterUntargeted<WorldRegenerated>(OnWorld);
        _ = Token.RegisterComponentTargeted<TookDamage>(this, OnDamage);
    }

    private void OnWorld(ref WorldRegenerated m) { /* update UI */ }
    private void OnDamage(ref TookDamage m) { /* flash damage */ }
}
```

3) Send messages

```csharp
using DxMessaging.Core.Extensions;   // Emit helpers
using UnityEngine;

// Untargeted (global)
var world = new WorldRegenerated(42);
world.Emit();

// Targeted (to a specific component or GameObject)
var heal = new Heal(10);
heal.EmitGameObjectTargeted(gameObject);     // no InstanceId cast needed

// Broadcast (from a specific source)
var hit = new TookDamage(5);
hit.EmitGameObjectBroadcast(gameObject);     // no InstanceId cast needed

// String convenience (global)
"Saved".Emit();
```

Do’s

- Prefer attributes + `DxAutoConstructor` for clarity; use interfaces on structs for hot paths.
- Bind struct messages to a variable before calling `Emit*`.
- Use GameObject/Component emit helpers to avoid manual `InstanceId` casts.
- Define named handler methods for readability and reuse.

Don’ts

- Don’t emit from temporaries (e.g., `new MyMessage(...).Emit()` won’t compile for structs).
- Don’t use Untargeted for per‑entity commands; use Targeted.
- Don’t manually manage lifecycles—use `MessageRegistrationToken` and enable/disable with component state.

What’s next

- [Message Types](Docs/MessageTypes.md) (when to use which)
- [Interceptors & Ordering](Docs/InterceptorsAndOrdering.md)
- [Unity Integration](Docs/UnityIntegration.md)
 - [Quick Reference](Docs/QuickReference.md)
 - [Targeting & Context](Docs/TargetingAndContext.md)
