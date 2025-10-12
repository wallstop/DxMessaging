# Comparisons: Events, Unity Events, and Unity Messages

This guide shows common pain points in standard approaches and how DxMessaging addresses them with clearer ownership, ordering, and observability.

Table of contents

- C# events/delegates
- UnityEvents (inspector wiring)
- Unity SendMessage
- Global “Event Bus” singletons
- How DxMessaging addresses each
- When to use which

Standard C# Events/Actions

Problems

- Manual attach/detach: easy to forget unsubscribe; memory leaks and callbacks on destroyed objects.
- Tight coupling: consumers reference producers (or vice‑versa), hurting modularity and tests.
- Ordering is implicit: hard to coordinate A before B across systems.
- Hard to observe globally: no built‑in way to inspect “what fired recently”.

Typical code

```csharp
public sealed class Spawner
{
    public event Action Spawned;
    public void Spawn()
    {
        // ...
        Spawned?.Invoke();
    }
}

public sealed class UI
{
    private readonly Spawner _spawner;
    public UI(Spawner spawner)
    {
        _spawner = spawner;
        _spawner.Spawned += OnSpawned; // must remember to unsubscribe
    }
    private void OnSpawned() => Refresh();
}
```

DxMessaging

- No direct coupling; the UI listens for a message instead of referencing a specific Spawner instance.
- Lifecycle managed by a token; enable/disable tied to a component.
- Ordering via priority; global inspection via diagnostics and the custom inspector.

```csharp
using DxMessaging.Core.Attributes;
using DxMessaging.Core.Extensions;
using DxMessaging.Core.Messages;

[DxUntargetedMessage]
[DxAutoConstructor]
public readonly partial struct Spawned { }

// Producer
var evt = new Spawned();
evt.Emit();

// Consumer (Unity)
_ = token.RegisterUntargeted<Spawned>(OnSpawned);
void OnSpawned(ref Spawned m) => Refresh();
```

UnityEvents (inspector wiring)

Problems

- Great for small demos, but brittle at scale (hidden references, order issues, refactors break wiring).
- No interception or post‑processing stages to validate/normalize.

 Typical code

```csharp
using UnityEngine;
using UnityEngine.Events;

public sealed class Button : MonoBehaviour
{
    public UnityEvent onClicked; // wired in Inspector
    public void Click() => onClicked?.Invoke();
}

public sealed class UI : MonoBehaviour
{
    public void Refresh() { /* ... */ }
}
```

DxMessaging

- Strongly‑typed registrations in code; explicit priorities and stages.
- Inspect and page through emissions/registrations from MessagingComponent inspector.

Unity SendMessage

Problems

- String‑based; no compile‑time checking. 0/1 parameter only; boxing costs.
- Hard to reason about who handles what; debugging is difficult.

DxMessaging

- Use `ReflexiveMessage` to bridge legacy SendMessage behavior into the bus pipeline (optional).
- Prefer typed messages for new code; multiple parameters via fields, by‑ref handlers avoid boxing.

```csharp
using DxMessaging.Core;
using DxMessaging.Core.Messages;

var target = (InstanceId)gameObject;
var msg = new ReflexiveMessage("OnHit", ReflexiveSendMode.Upwards, 10);
MessageHandler.MessageBus.TargetedBroadcast(ref target, ref msg);
```

Global “Event Bus” singletons

Problems

- Often devolves to one giant bag of static events; naming, ownership, and routing semantics get muddy.
- Still manual lifecycle and ordering issues.

 Typical code

```csharp
public static class EventHub
{
    public static event Action<int> Damage;
    public static void RaiseDamage(int amount) => Damage?.Invoke(amount);
}

// Producer
EventHub.RaiseDamage(5);

// Consumer
EventHub.Damage += amount => Log(amount);
```

Problems: everything is global, ownership unclear, no interceptors, no context (who sent/received), hard to test.

DxMessaging

- A single `MessageBus` with clear categories: Untargeted (global), Targeted (to one), Broadcast (from one).
- Interceptors and post‑processors provide a structured pipeline.
- You can create isolated buses for sub‑systems or tests (local islands), and keep a global default (`MessageHandler.MessageBus`).
  
```csharp
using DxMessaging.Core;
using DxMessaging.Core.MessageBus;
using DxMessaging.Core.Messages;
using DxMessaging.Core.Attributes;

[DxBroadcastMessage]
[DxAutoConstructor]
public readonly partial struct TookDamage { public readonly int amount; }

// Local bus for combat system
var bus = new MessageBus();
var handler = new MessageHandler(new InstanceId(1)) { active = true };
var token = MessageRegistrationToken.Create(handler, bus);

_ = token.RegisterBroadcastWithoutSource<TookDamage>(OnAnyDamage);
void OnAnyDamage(ref InstanceId src, ref TookDamage m) => Log(src, m.amount);

// Emit within subsystem
var hit = new TookDamage(5);
hit.EmitGameObjectTargeted(enemyGO, bus);
```

When to use which

- C# events: simple, local wiring within a class or small module.
- UnityEvents: quick prototypes/small scenes; prefer code for maintainability.
- Unity SendMessage: legacy only; prefer `ReflexiveMessage` if you must bridge.
- DxMessaging: decoupled, cross‑system flows where ordering, observability, and lifecycle safety matter.

See also

- [Message Types](Docs/MessageTypes.md)
- [Diagnostics (Editor inspector)](Docs/Diagnostics.md)
