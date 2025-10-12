# Helpers and Source Generation

Attributes

- `DxAutoConstructor`: generate a constructor that assigns all public non‑static fields in declaration order; fields marked `DxOptionalParameter` become optional parameters.
- `DxUntargetedMessage`, `DxTargetedMessage`, `DxBroadcastMessage`: mark a type as a message to enable source‑generator‑powered `IMessage` plumbing without implementing generic interfaces manually.

```csharp
using DxMessaging.Core.Attributes;

[DxUntargetedMessage]
[DxAutoConstructor]
public readonly partial struct VideoSettingsChanged
{
    public readonly int width;
    public readonly int height;
    [DxOptionalParameter] public readonly bool fullscreen; // optional
}
// Generated: VideoSettingsChanged(int width, int height, bool fullscreen = default)
```

Generic message interfaces (zero‑boxing for structs)

- Prefer `IUntargetedMessage<T>`, `ITargetedMessage<T>`, and `IBroadcastMessage<T>` on `struct` messages to avoid boxing and expose a stable `MessageType`.

```csharp
using DxMessaging.Core.Messages;

public readonly struct Heal : ITargetedMessage<Heal>
{
    public readonly int amount;
    public Heal(int amount) { this.amount = amount; }
}
```

Emit helpers (extensions)

- Located in `DxMessaging.Core.Extensions.MessageExtensions`.
- Pick the right overload automatically, defaulting to the global bus.

```csharp
using DxMessaging.Core.Extensions;
using UnityEngine;

// Untargeted
var world = new WorldRegenerated(42);
world.Emit();

// Targeted (GameObject / Component overloads)
var heal = new Heal(10);
heal.EmitGameObjectTargeted(gameObject);

// Broadcast (GameObject / Component overloads)
var hit = new TookDamage(5);
hit.EmitGameObjectBroadcast(gameObject);

// String convenience (global)
"Hello".Emit();
```

Local bus islands

- Create an isolated `MessageBus` for tests or sub‑systems and pass it to `MessageRegistrationToken.Create(handler, messageBus)`.

Related

- [API Reference](Docs/Reference.md)
