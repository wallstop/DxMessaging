# Targeting and Context (Component vs GameObject)

Short intro

Targeted and broadcast messages carry context: an `InstanceId` for the target (targeted) or source (broadcast). In Unity, `InstanceId` can represent either a `GameObject` or a specific `Component`. This page explains the differences and how to choose.

Key concepts

- InstanceId
  - Implicitly converts from `GameObject` and from `Component`.
  - Equality is by Unity instance ID; under the hood, the bus compares `InstanceId`s for matches.
- Matching
  - A targeted handler registered for a `GameObject` receives messages emitted to that `GameObject`.
  - A targeted handler registered for a `Component` receives messages emitted to that `Component`.
  - These are distinct; emitting to a `GameObject` does not match handlers registered for a specific `Component` on it (and vice‑versa).
- Ownership
  - `MessagingComponent` hosts a `MessageHandler` per GameObject; tokens come from it and can register for both GameObject or Component contexts.

Targeted: Component vs GameObject

```csharp
using DxMessaging.Core.Attributes;
using DxMessaging.Core.Extensions;
using DxMessaging.Core.Messages;

[DxTargetedMessage]
[DxAutoConstructor]
public readonly partial struct Heal { public readonly int amount; }

// Register for GameObject target
_ = token.RegisterGameObjectTargeted<Heal>(gameObject, OnHealGO);
void OnHealGO(ref Heal m) => Log($"GO heal {m.amount}");

// Register for Component target (this script)
_ = token.RegisterComponentTargeted<Heal>(this, OnHealComponent);
void OnHealComponent(ref Heal m) => Log($"Component heal {m.amount}");

// Emitting to GameObject
var heal = new Heal(10);
heal.EmitGameObjectTargeted(gameObject); // matches OnHealGO only

// Emitting to Component
heal.EmitComponentTargeted(this);        // matches OnHealComponent only
```

Broadcast: Component vs GameObject

```csharp
[DxBroadcastMessage]
[DxAutoConstructor]
public readonly partial struct TookDamage { public readonly int amount; }

// Register for broadcasts from a GameObject
_ = token.RegisterGameObjectBroadcast<TookDamage>(gameObject, OnDamageFromGO);
void OnDamageFromGO(ref TookDamage m) => Log($"GO damage {m.amount}");

// Register for broadcasts from this Component
_ = token.RegisterComponentBroadcast<TookDamage>(this, OnDamageFromComponent);
void OnDamageFromComponent(ref TookDamage m) => Log($"Component damage {m.amount}");

// Emitting from GameObject
var hit = new TookDamage(5);
hit.EmitGameObjectBroadcast(gameObject); // matches OnDamageFromGO only

// Emitting from Component
hit.EmitComponentBroadcast(this);        // matches OnDamageFromComponent only
```

When to use which

- GameObject context
  - When the whole object is the logical target/source (e.g., “Player was healed”, “Enemy took damage”).
  - When multiple components should coordinate under the same address.
- Component context
  - When the message is explicitly for one component’s responsibility (e.g., “open JUST this DoorController”).
  - When multiple components on the same GameObject must be independently addressable.

Do’s

- Pick a context (GO vs Component) and be consistent across emitters and listeners for that message type.
- Prefer GameObject when in doubt — easier coordination among components on the same object.

Don’ts

- Don’t emit to GameObject and expect Component‑registered handlers to receive it (and vice‑versa).
- Don’t register the same handler under both unless you intend to handle both contexts.

Related

- [Quick Reference](QuickReference.md)
- [Message Types](MessageTypes.md)
- [Unity Integration](UnityIntegration.md)
