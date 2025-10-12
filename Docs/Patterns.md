# DxMessaging Patterns

This document captures practical patterns for building systems with DxMessaging. It complements the README by focusing on composition, structure, and problem-solving techniques.

## 1) Scene-wide Events (Untargeted)

Use untargeted messages for global state changes that any system might care about. Keep messages small and immutable.

```csharp
using DxMessaging.Core.Messages;
using DxMessaging.Core.Attributes;
using DxMessaging.Core.Extensions;

[DxUntargetedMessage]
[DxAutoConstructor]
public readonly partial struct SceneLoaded
{
    public readonly int buildIndex;
}

// Sender
var evt = new SceneLoaded(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
evt.Emit();

// Listener
_ = token.RegisterUntargeted<SceneLoaded>(OnSceneLoaded);
void OnSceneLoaded(ref SceneLoaded m) => RefreshUI();
```

## 2) Directed Commands (Targeted)

Target a specific `GameObject`/`Component` when you want direct control.

```csharp
using DxMessaging.Core.Messages;
using DxMessaging.Core.Attributes;
using DxMessaging.Core.Extensions;

[DxTargetedMessage]
[DxAutoConstructor]
public readonly partial struct Heal { public readonly int amount; }

// Sender
var heal = new Heal(10);
heal.EmitComponentTargeted(this);

// Listener (on the hero)
_ = token.RegisterComponentTargeted<Heal>(this, OnHeal);
void OnHeal(ref Heal m) => ApplyHeal(m.amount);
```

## 3) Observability (Broadcast)

Broadcast from a source; any listener can observe.

```csharp
using DxMessaging.Core.Messages;
using DxMessaging.Core.Attributes;
using DxMessaging.Core.Extensions;
using DxMessaging.Core; // InstanceId

[DxBroadcastMessage]
[DxAutoConstructor]
public readonly partial struct TookDamage { public readonly int amount; }

// Sender
var hit = new TookDamage(5);
hit.EmitGameObjectBroadcast(enemyGO);

// Listener: observe every source
_ = token.RegisterBroadcastWithoutSource<TookDamage>(OnAnyTookDamage);
void OnAnyTookDamage(InstanceId src, TookDamage m) => Log(src, m);
```

## 4) Validation and Normalization (Interceptors)

Use interceptors to enforce rules before handlers run.

```csharp
using DxMessaging.Core; // MessageHandler

var bus = MessageHandler.MessageBus;
_ = token.RegisterTargetedInterceptor<TookDamage>((ref InstanceId target, ref TookDamage m) =>
{
    if (m.amount <= 0) return false; // cancel
    m = new TookDamage(Math.Min(m.amount, 999)); // clamp
    return true;
}, priority: 0);
```

## 5) Analytics/Logging (Post-Processors)

Post-processors run after handlers—ideal for metrics.

```csharp
_ = token.RegisterUntargetedPostProcessor<SceneLoaded>((ref SceneLoaded m) => Metrics.TrackScene(m.index));
```

## 6) Local Bus Islands

`MessageHandler.MessageBus` is the global bus. For isolation, use your own `MessageBus` instance and pass it to the token factory.

```csharp
using DxMessaging.Core;
using DxMessaging.Core.MessageBus;

var localBus = new MessageBus();
var token = MessageRegistrationToken.Create(handler, localBus);
```

## 7) Lifecycle Pattern in Unity

- Stage registrations in `Awake`/`Start`.
- Call `token.Enable()` in `OnEnable` and `token.Disable()` in `OnDisable`.
- Use `MessageAwareComponent` to avoid boilerplate.

Side‑by‑side: lifecycle

Before

```csharp
void Awake() { /* register */ }
void OnDestroy() { /* maybe unregister (often forgotten) */ }
```

After (token)

```csharp
void Awake()     { /* stage */ }
void OnEnable()  { token.Enable(); }
void OnDisable() { token.Disable(); }
```

## 8) Cross-Scene Messaging

Untargeted messages flow anywhere; targeted/broadcast require a valid `InstanceId`. For cross-scene, ensure the target/source object exists when you emit.

## 9) Bridging Legacy Unity Messaging

`ReflexiveMessage` mirrors `SendMessage*` patterns but keeps you in the bus pipeline.

```csharp
using DxMessaging.Core;
using DxMessaging.Core.Messages;

var msg = new ReflexiveMessage("OnHit", ReflexiveSendMode.Upwards, 10);
msg.EmitGameObjectTargeted(someGameObject);
```

## 10) Global Accept-All Handlers

Use when building tools or inspectors that want to observe everything.

```csharp
_ = token.RegisterGlobalAcceptAll(
    (ref IUntargetedMessage m) => Debug.Log($"Untargeted {m.MessageType}"),
    (ref InstanceId target, ref ITargetedMessage m) => Debug.Log($"Targeted {m.MessageType} to {target}"),
    (ref InstanceId source, ref IBroadcastMessage m) => Debug.Log($"Broadcast {m.MessageType} from {source}")
);

Do’s

- Use global accept‑all in tooling and debug inspectors.
- Prefer specific registrations for gameplay code to avoid surprises.
```

## 11) Diagnostics and Tuning

- Enable `IMessageBus.GlobalDiagnosticsMode` in Editor or per-token.
- Adjust `IMessageBus.GlobalMessageBufferSize` for deeper history (Editor settings UI provided).
- Wire `MessagingDebug.LogFunction` to Unity’s console to see warnings/errors.

## 12) Testing

- Construct messages directly and emit via extension helpers.
- Use a local `MessageBus` per test to avoid global state.
- Wrap handlers to increment counters or assert payloads.
