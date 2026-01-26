# Quick Reference (Cheat Sheet)

Use this as a rapid guide to define/emit/listen and manage lifecycles.

Do’s

- Use attributes + `DxAutoConstructor` for clarity (or interfaces on structs for perf).
- Bind struct messages to a variable before emitting.
- Use GameObject/Component emit helpers (no manual `InstanceId`).
- Register once; enable/disable with component state.
- Prefer named handler methods over inline lambdas for reuse and clarity.
- When using DI, inject `IMessageRegistrationBuilder` instead of newing `MessageHandler`s manually.

## Don'ts

- Don’t emit from temporaries; use a local variable (e.g., `var msg = new M(...); msg.Emit();`).
- Don’t mix Component vs GameObject targeting if you expect matches (see targeting notes below).
- Don’t register in Update; use `Awake` for staging + `OnEnable`/`OnDisable` for lifecycle.
- Don’t forget base calls when inheriting from `MessageAwareComponent` — call `base.RegisterMessageHandlers()` and `base.OnEnable()`/`base.OnDisable()`.
- Don’t hide Unity methods with `new` (e.g., `new void OnEnable()`); prefer `override` and call `base.*`.

## Define messages

```csharp
using DxMessaging.Core.Attributes;

[DxUntargetedMessage]
[DxAutoConstructor]
public readonly partial struct SceneLoaded { public readonly int buildIndex; }

[DxTargetedMessage]
[DxAutoConstructor]
public readonly partial struct Heal { public readonly int amount; }

[DxBroadcastMessage]
[DxAutoConstructor]
public readonly partial struct TookDamage { public readonly int amount; }
```

## Emit (Unity helpers)

```csharp
using DxMessaging.Core.Extensions;

var scene = new SceneLoaded(1); scene.Emit();
var heal  = new Heal(10);       heal.EmitGameObjectTargeted(gameObject);
var hit   = new TookDamage(5);  hit.EmitComponentBroadcast(this);

// String shorthands
"Saved".Emit();                   // GlobalStringMessage
"Hello".EmitAt(gameObject);       // StringMessage to GO (or .Emit(instanceId))
"Hit".EmitFrom(gameObject);       // SourcedStringMessage from GO
```

## Register (Unity, via token)

```csharp
using DxMessaging.Core; // InstanceId
// Untargeted
_ = token.RegisterUntargeted<SceneLoaded>(OnSceneLoaded);
void OnSceneLoaded(ref SceneLoaded m) { /* ... */ }

// Targeted: to this component or gameObject
_ = token.RegisterComponentTargeted<Heal>(this, OnHeal);
_ = token.RegisterGameObjectTargeted<Heal>(gameObject, OnHeal);
void OnHeal(ref Heal m) { /* ... */ }

// Broadcast: from this component or gameObject
_ = token.RegisterComponentBroadcast<TookDamage>(this, OnDamageFromMe);
_ = token.RegisterGameObjectBroadcast<TookDamage>(gameObject, OnDamageFromMe);
void OnDamageFromMe(ref TookDamage m) { /* ... */ }

// Listen to all targets/sources
_ = token.RegisterTargetedWithoutTargeting<Heal>(OnAnyHeal);
void OnAnyHeal(ref InstanceId target, ref Heal m) { /* ... */ }

_ = token.RegisterBroadcastWithoutSource<TookDamage>(OnAnyDamage);
void OnAnyDamage(ref InstanceId src, ref TookDamage m) { /* ... */ }
```

## Register (DI / services)

```csharp
using DxMessaging.Core.MessageBus;

public sealed class DamageSystem : IStartable, IDisposable
{
    private readonly MessageRegistrationLease lease;

    public DamageSystem(IMessageRegistrationBuilder registrationBuilder)
    {
        lease = registrationBuilder.Build(new MessageRegistrationBuildOptions
        {
            Configure = token =>
            {
                _ = token.RegisterUntargeted<TookDamage>(OnDamage);
            }
        });
    }

    public void Start() => lease.Activate();

    public void Dispose() => lease.Dispose();

    private static void OnDamage(ref TookDamage message) { /* respond */ }
}
```

Tip: Define `ZENJECT_PRESENT`, `VCONTAINER_PRESENT`, or `REFLEX_PRESENT` to enable the optional shims under [Runtime/Unity/Integrations](../../Runtime/Unity/Integrations/) that bind the builder automatically for those containers.

## Interceptors and post‑processors

```csharp
using DxMessaging.Core;            // MessageHandler
using DxMessaging.Core.MessageBus; // IMessageBus

var bus = MessageHandler.MessageBus;
_ = bus.RegisterBroadcastInterceptor<TookDamage>((ref InstanceId src, ref TookDamage m) =>
{
    if (m.amount <= 0) return false; // cancel
    m = new TookDamage(Math.Min(m.amount, 999));
    return true;
});

_ = token.RegisterUntargetedPostProcessor<SceneLoaded>((ref SceneLoaded m) => LogScene(m.buildIndex));
```

## Lifecycle

```csharp
void Awake()     { /* stage registrations */ }
void OnEnable()  { token.Enable(); }
void OnDisable() { token.Disable(); }
```

## Inheritance tip (MessageAwareComponent)

- If you override `RegisterMessageHandlers`, start with `base.RegisterMessageHandlers()`.
- If you override Unity lifecycle methods, call `base.OnEnable()` / `base.OnDisable()` (and `base.Awake()`/`base.OnDestroy()` if overridden).

## Targeting notes (Component vs GameObject)

- A targeted message matches if the emitted `InstanceId` equals the registered `InstanceId`.
- Registering for a Component target listens for messages targeted at that specific Component.
- Registering for a GameObject target listens for messages targeted at that GameObject.
- Emitting to a GameObject will not reach Component‑targeted listeners (and vice‑versa). Use the matching helper.
- Shorthands exist for strings too; be explicit about using a GameObject vs Component with `EmitAt`/`EmitFrom`.

## See also

- [Emit Shorthands](../advanced/emit-shorthands.md)
- [Advanced](../guides/advanced.md)
- [Targeting & Context](../concepts/targeting-and-context.md)
- [Interceptors & Ordering](../concepts/interceptors-and-ordering.md)

## Execution order (short)

- **Untargeted** - Interceptors → Global Accept‑All → `Handlers<T>` → `Post‑Processors<T>`
- **Targeted** - Interceptors → Global Accept‑All → `Handlers<T>` @ target → `Handlers<T>` (All Targets) → `Post‑Processors<T>` @ target → `Post‑Processors<T>` (All Targets)
- **Broadcast** - Interceptors → Global Accept‑All → `Handlers<T>` @ source → `Handlers<T>` (All Sources) → `Post‑Processors<T>` @ source → `Post‑Processors<T>` (All Sources)

Notes: Lower priority runs earlier. Same priority preserves registration order. Within a priority, fast (by‑ref) handlers run before action handlers.

## API quick ref

- **Token: Untargeted**
  - `RegisterUntargeted<T>(Action<T> | FastHandler<T>, priority=0)`
  - `RegisterUntargetedPostProcessor<T>(FastHandler<T>, priority=0)`
- **Token: Targeted (specific)**
  - `RegisterGameObjectTargeted<T>(GameObject, Action<T> | FastHandler<T>, priority=0)`
  - `RegisterComponentTargeted<T>(Component, Action<T> | FastHandler<T>, priority=0)`
  - `RegisterTargeted<T>(InstanceId, Action<T> | FastHandler<T>, priority=0)`
  - Post: `RegisterTargetedPostProcessor<T>(InstanceId, FastHandler<T>, priority=0)`
- **Token: Targeted (all targets)**
  - `RegisterTargetedWithoutTargeting<T>(FastHandlerWithContext<T>, priority=0)`
  - Post: `RegisterTargetedWithoutTargetingPostProcessor<T>(FastHandlerWithContext<T>, priority=0)`
- **Token: Broadcast (specific)**
  - `RegisterGameObjectBroadcast<T>(GameObject, Action<T> | FastHandler<T>, priority=0)`
  - `RegisterComponentBroadcast<T>(Component, Action<T> | FastHandler<T>, priority=0)`
  - `RegisterBroadcast<T>(InstanceId, Action<T> | FastHandler<T>, priority=0)`
  - Post: `RegisterBroadcastPostProcessor<T>(InstanceId, FastHandler<T>, priority=0)`
- **Token: Broadcast (all sources)**
  - `RegisterBroadcastWithoutSource<T>(FastHandlerWithContext<T>, priority=0)`
  - Post: `RegisterBroadcastWithoutSourcePostProcessor<T>(Action<InstanceId,T> | FastHandlerWithContext<T>, priority=0)`
- **Token: Global observer**
  - `RegisterGlobalAcceptAll(Action<IUntargetedMessage>, Action<InstanceId,ITargetedMessage>, Action<InstanceId,IBroadcastMessage>)`
  - `RegisterGlobalAcceptAll(FastHandler<IUntargetedMessage>, FastHandlerWithContext<ITargetedMessage>, FastHandlerWithContext<IBroadcastMessage>)`
- **Bus: Interceptors**
  - `RegisterUntargetedInterceptor<T>(UntargetedInterceptor<T>, priority=0)`
  - `RegisterTargetedInterceptor<T>(TargetedInterceptor<T>, priority=0)`
  - `RegisterBroadcastInterceptor<T>(BroadcastInterceptor<T>, priority=0)`
  - `RegisterGlobalAcceptAll(MessageHandler)` (bus‑level)
