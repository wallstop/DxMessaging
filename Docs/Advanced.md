# Advanced Usage: Lifecycles, Safety, and Manual Control

Short intro

This page covers advanced patterns: manual lifetimes, token control, Unity integration switches, string message defaults, and safety tips. If you’re new, start with Quick Start, then return here as you scale up.

Table of contents

- Lifecycles and tokens
- Manual enable/disable and UnregisterAll
- Unity integration knobs: MessageAwareComponent and MessagingComponent
- String messages opt‑in/out
- Local bus islands (subsystems/tests)
- Safety and troubleshooting
- Side‑by‑side patterns: before vs after
 - More advanced use cases

Lifecycles and tokens

- Token creation
  - MessageAwareComponent: created for you in Awake.
  - Manual: `var token = messagingComponent.Create(this);`
- Enable/Disable
  - Call `token.Enable()` in `OnEnable` and `token.Disable()` in `OnDisable`.
  - Idempotent; safe to call multiple times.
- Clearing
  - `token.UnregisterAll()` disables and clears staged registrations.
  - `token.RemoveRegistration(handle)` removes a single registration.
- Diagnostics
  - `token.DiagnosticMode = true` to record per‑registration call counts and emissions.

Example: manual lifetime

```csharp
using DxMessaging.Unity;
using DxMessaging.Core;
using UnityEngine;

[RequireComponent(typeof(MessagingComponent))]
public sealed class PauseOverlay : MonoBehaviour
{
    private MessagingComponent _messaging;
    private MessageRegistrationToken _token;

    private void Awake()
    {
        _messaging = GetComponent<MessagingComponent>();
        _token = _messaging.Create(this);
        _ = _token.RegisterUntargeted<GamePaused>(OnPaused);
        _ = _token.RegisterUntargeted<GameResumed>(OnResumed);
    }

    private void OnEnable()  => _token.Enable();
    private void OnDisable() => _token.Disable();

    private void OnPaused(ref GamePaused m)  => Show();
    private void OnResumed(ref GameResumed m) { Hide(); _token.UnregisterAll(); }
}
```

Unity integration knobs

MessageAwareComponent

- `protected virtual bool MessageRegistrationTiedToEnableStatus => true`.
  - Set to `false` to manage `Enable()`/`Disable()` yourself (e.g., persistent listeners on disabled components).
- `protected virtual bool RegisterForStringMessages => true`.
  - Set to `false` to not auto‑register string message demos.

```csharp
using DxMessaging.Unity;

public sealed class PersistentListener : MessageAwareComponent
{
    protected override bool MessageRegistrationTiedToEnableStatus => false; // stays enabled when component disables
    protected override bool RegisterForStringMessages => false;             // opt out of string demos

    protected override void RegisterMessageHandlers()
    {
        _ = Token.RegisterUntargeted<MyEvent>(OnMyEvent);
        Token.Enable(); // explicitly enable once
    }

    private void OnMyEvent(ref MyEvent m) => DoWork();
}
```

MessagingComponent

- `emitMessagesWhenDisabled`: continue emitting while the GameObject is disabled.
- `ToggleMessageHandler(bool)`: explicitly toggle the underlying handler.

```csharp
using DxMessaging.Unity;

public sealed class EmissionControl : MonoBehaviour
{
    public MessagingComponent messaging;

    public void PauseEmissions()
    {
        messaging.emitMessagesWhenDisabled = false;
        messaging.ToggleMessageHandler(false); // fully pause emissions
    }

    public void ResumeEmissions()
    {
        messaging.ToggleMessageHandler(true);
    }
}
```

Emit while disabled (opt‑in)

```csharp
// Keep emitting even when this GameObject is disabled
messaging.emitMessagesWhenDisabled = true;
// Now ToggleMessageHandler(false) will be ignored while emitMessagesWhenDisabled is true
```

String messages: opt‑in/out

- MessageAwareComponent registers string demos by default. Override `RegisterForStringMessages` to disable.
- See String Messages page for using `StringMessage` and `GlobalStringMessage` during prototyping.

Local bus islands (subsystems/tests)

- Create an isolated bus and pass it to the token factory to keep flows contained.

```csharp
using DxMessaging.Core;
using DxMessaging.Core.MessageBus;

var localBus  = new MessageBus();
var handler   = new MessageHandler(new InstanceId(123)) { active = true };
var token     = MessageRegistrationToken.Create(handler, localBus);
// stage registrations on token; emit to localBus in tests
```

Scoped interceptors (debug)

```csharp
using DxMessaging.Core;            // MessageHandler, InstanceId
using DxMessaging.Core.MessageBus;

var bus = MessageHandler.MessageBus;
Action remove = bus.RegisterBroadcastInterceptor<TookDamage>((ref InstanceId src, ref TookDamage m) =>
{
    Debug.Log($"Intercept {src}: {m.amount}");
    return true; // allow
}, priority: -100);

// Later
remove();
```

Safety and troubleshooting

Do’s

- Register once; enable/disable with component state.
- Prefer named handler methods for clarity and reuse.
- Use diagnostics in Editor; disable for release if not needed.
- Use GameObject/Component emit helpers (no manual `InstanceId`).

Don’ts

- Don’t register in `Update`/`FixedUpdate` every frame.
- Don’t double‑create tokens on the same component (MessagingComponent logs a warning).
- Don’t forget to clear or disable tokens on destruction if you manage them manually.

Side‑by‑side: manual events vs DxMessaging

Before (manual C# events)

```csharp
public sealed class Spawner
{
    public event Action Spawned;
    public void Spawn() => Spawned?.Invoke();
}
public sealed class UI
{
    private Spawner _spawner;
    void Awake() { _spawner.Spawned += OnSpawned; }
    void OnDestroy() { _spawner.Spawned -= OnSpawned; } // easy to forget
    void OnSpawned() => Refresh();
}
```

After (DxMessaging + token lifecycle)

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

More advanced use cases

- Scene transitions: create a local bus per scene (or sub‑system), pass it to tokens. Emit globally for cross‑scene untargeted notifications, but isolate targeted/broadcast to the scene’s bus.
- Analytics layer: use `RegisterTargetedWithoutTargeting` and `RegisterBroadcastWithoutSource` to observe all flows; record in a buffer or file; disable in release.
- Pausable systems: set `emitMessagesWhenDisabled` for senders you want alive while disabled; use `ToggleMessageHandler` to pause entire listeners.

Related

- [Unity Integration](Docs/UnityIntegration.md)
- [Diagnostics (Inspector)](Docs/Diagnostics.md)
- [String Messages](Docs/StringMessages.md)
