# DxMessaging + Zenject

## Goals

- Drive DxMessaging with a Zenject-managed `MessageBus` singleton.
- Allow MonoBehaviours to opt into the container-provided bus without leaking references.
- Demonstrate bridging to `SignalBus` for teams already using Zenject signals.

## Setup Steps

1. **Install Packages**
   - Add DxMessaging to your Unity project (UPM Git URL).
   - Install Zenject/Extenject (source or UPM).

1. **Create an Installer**

```csharp
using DxMessaging.Core.MessageBus;
using DxMessaging.Unity;
using Zenject;

public sealed class DxMessagingInstaller : MonoInstaller
{
    public override void InstallBindings()
    {
        Container.BindInterfacesAndSelfTo<MessageBus>().AsSingle();
    }
}

}
```

- Binding the concrete `MessageBus` as a singleton lets it be injected via `IMessageBus` anywhere.
- The factory pattern ensures you can spawn utility GameObjects at runtime if needed.

1. **Configure Existing MessagingComponents**

```csharp
[RequireComponent(typeof(MessagingComponent))]
public sealed class MessagingComponentConfigurator : MonoBehaviour
{
    [Inject]
    private IMessageBus _messageBus;

    private void Awake()
    {
        MessagingComponent component = GetComponent<MessagingComponent>();
        component.Configure(_messageBus, MessageBusRebindMode.RebindActive);
    }
}
```

- Add this component alongside any existing `MessagingComponent` to let Zenject push the scoped bus down before listeners call `Create`.
- Alternately, extend `MessageAwareComponent` and override `Awake` to resolve the bus and call `Configure` before `base.Awake()`.
- When compiling with Zenject, Define `ZENJECT_PRESENT` (the asmdef under `Runtime/Unity/Integrations/Zenject/` adds it for that assembly automatically when a supported Zenject package is present) and include `DxMessagingRegistrationInstaller` to expose `IMessageRegistrationBuilder` automatically.

1. **Injecting Tokens in Plain Classes**

```csharp
public sealed class PlayerController : IInitializable, IDisposable
{
    private readonly MessageRegistrationLease lease;

    public PlayerController(IMessageRegistrationBuilder registrationBuilder)
    {
        MessageRegistrationBuildOptions options = new MessageRegistrationBuildOptions
        {
            Configure = token =>
            {
                _ = token.RegisterUntargeted<PlayerSpawned>(OnPlayerSpawned);
            }
        };

        lease = registrationBuilder.Build(options);
    }

    public void Initialize()
    {
        lease.Activate();
    }

    public void Dispose()
    {
        lease.Dispose();
    }

    private static void OnPlayerSpawned(ref PlayerSpawned message)
    {
        // business logic
    }
}
```

- `IInitializable`/`IDisposable` aligns activation and disposal with the container lifecycle.
- The builder resolves the scoped bus automatically and keeps the handler/diagnostics wiring consistent.
- MonoBehaviours can call `MessagingComponent.CreateRegistrationBuilder()` if they need to construct additional leases for helper services.

1. **Bridging to Zenject Signals**

```csharp
public sealed class DxToSignalBridge : IInitializable, IDisposable
{
    private readonly IMessageBus _messageBus;
    private readonly SignalBus _signalBus;
    private Action _deregister;

    public DxToSignalBridge(IMessageBus messageBus, SignalBus signalBus)
    {
        _messageBus = messageBus;
        _signalBus = signalBus;
    }

    public void Initialize()
    {
        MessageHandler handler = new MessageHandler(new InstanceId(0), _messageBus)
        {
            active = true
        };
        MessageRegistrationToken token = MessageRegistrationToken.Create(handler, _messageBus);
        _ = token.RegisterUntargeted<SceneTransition>(OnSceneTransition);
        token.Enable();
        _deregister = () => token.Disable();
    }

    public void Dispose()
    {
        _deregister?.Invoke();
    }

    private void OnSceneTransition(ref SceneTransition message)
    {
        _signalBus.Fire(message);
    }
}
```

- This pattern keeps Zenject consumers in sync while DxMessaging remains the primary event system.

1. **Testing**

- In Zenject unit tests, inject a fake `MessageBus` implementation or configure a fresh instance via `SetGlobalMessageBus`.
- Verify handlers receive emissions through the container-provided bus to ensure no code accidentally calls the static singleton.

## Checklist

- [ ] Add `MessagingComponentConfigurator` alongside every `MessagingComponent` prefab.
- [ ] Register `DxMessagingInstaller` in `ProjectContext`.
- [ ] Replace direct `MessageHandler.MessageBus` references with injected `IMessageBus`.
- [ ] Bridge critical signals via `DxToSignalBridge` if the project still relies on Zenject signals.
