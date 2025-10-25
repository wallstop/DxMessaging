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
        component.Configure(_messageBus);
    }
}
```

- Add this component alongside any existing `MessagingComponent` to let Zenject push the scoped bus down before listeners call `Create`.
- Alternately, extend `MessageAwareComponent` and override `Awake` to resolve the bus and call `Configure` before `base.Awake()`.

1. **Injecting Tokens in Plain Classes**

```csharp
public sealed class PlayerController : IInitializable, IDisposable
{
    private readonly IMessageBus _messageBus;
    private readonly MessageRegistrationToken _token;

    public PlayerController(IMessageBus messageBus)
    {
        _messageBus = messageBus;
        MessageHandler handler = new MessageHandler(new InstanceId(123), _messageBus)
        {
            active = true
        };
        _token = MessageRegistrationToken.Create(handler, _messageBus);
    }

    public void Initialize()
    {
        _ = _token.RegisterUntargeted<PlayerSpawned>(OnPlayerSpawned);
        _token.Enable();
    }

    public void Dispose()
    {
        _token.Disable();
    }

    private void OnPlayerSpawned(ref PlayerSpawned message)
    {
        // business logic
    }
}
```

- `IInitializable`/`IDisposable` ensures registrations align with Zenject lifecycle.
- Use per-instance `MessageHandler` when operating outside MonoBehaviours; the injected bus keeps everything scoped.

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
