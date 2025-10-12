# Unity Integration

Unity‑centric helpers make registration lifecycles explicit and safe.

MessagingComponent

- Attach to any GameObject that will send or receive messages.
- Creates a per‑owner `MessageHandler` and offers `Create(this)` to get a `MessageRegistrationToken`.
- Optional: set `emitMessagesWhenDisabled` if you need to emit while disabled.

MessageAwareComponent

- Derive for a batteries‑included pattern; it manages a token for you.
- Override `RegisterMessageHandlers()` to stage registrations.
- The token is enabled/disabled with the component’s enable state.

```csharp
using DxMessaging.Unity;
using DxMessaging.Core.Messages;

public sealed class HealthComponent : MessageAwareComponent
{
    protected override void RegisterMessageHandlers()
    {
        _ = Token.RegisterComponentTargeted<TookDamage>(this, OnTookDamage);
        _ = Token.RegisterUntargeted<WorldRegenerated>(OnWorldRegenerated);
    }

    private void OnTookDamage(ref TookDamage m) => Apply(m.amount);
    private void OnWorldRegenerated(ref WorldRegenerated m) => Reset();
}
```

Do’s

- Use `MessageAwareComponent` when possible to avoid boilerplate.
- Override `RegisterMessageHandlers()` and bind to named methods.
- Keep handlers small and fast; offload heavy work.

Don’ts

- Don’t register in Update; register once and enable/disable with component state.
- Don’t forget to call `base.RegisterMessageHandlers()` if your subclass relies on base registrations.

Manual token management

```csharp
using DxMessaging.Unity;
using DxMessaging.Core;
using DxMessaging.Core.Messages;

[RequireComponent(typeof(MessagingComponent))]
public sealed class InventoryUI : UnityEngine.MonoBehaviour
{
    private MessagingComponent _messaging;
    private MessageRegistrationToken _token;

    private void Awake()
    {
        _messaging = GetComponent<MessagingComponent>();
        _token = _messaging.Create(this);
        _ = _token.RegisterUntargeted<WorldRegenerated>(OnWorld);
        _ = _token.RegisterComponentTargeted<TookDamage>(this, OnDamage);
    }

    private void OnEnable() => _token.Enable();
    private void OnDisable() => _token.Disable();

    private void OnWorld(ref WorldRegenerated m) { /* update UI */ }
    private void OnDamage(ref TookDamage m) { /* flash damage */ }
}
```

Manual enable/disable (advanced)

```csharp
public sealed class AlwaysListening : MessageAwareComponent
{
    protected override bool MessageRegistrationTiedToEnableStatus => false; // keep token enabled

    protected override void RegisterMessageHandlers()
    {
        _ = Token.RegisterUntargeted<MyEvent>(OnEvent);
        Token.Enable(); // explicitly enable once
    }

    private void OnEvent(ref MyEvent m) { /* ... */ }
}
```

String message demos (opt‑out)

```csharp
public sealed class NoStringDemos : MessageAwareComponent
{
    protected override bool RegisterForStringMessages => false;

    protected override void RegisterMessageHandlers()
    {
        // only your registrations
    }
}
```

ReflexiveMessage (bridging legacy SendMessage)

```csharp
using DxMessaging.Core;
using DxMessaging.Core.Messages;

var msg = new ReflexiveMessage("OnHit", ReflexiveSendMode.Upwards, 10);
msg.EmitGameObjectTargeted(gameObject);
```

Related

- [Quick Start](Docs/QuickStart.md)
- [Patterns](Docs/Patterns.md)
- [Diagnostics (Inspector)](Docs/Diagnostics.md)
