# DxMessaging + Reflex

## Goals

- Register DxMessaging buses inside Reflex installers without introducing new global state.
- Configure `MessagingComponent` instances so pooled MonoBehaviours receive the container-provided bus.
- Demonstrate lightweight patterns that fit Reflex’ minimal API surface.

## Setup Steps

1. **Install Packages**
   - Add DxMessaging to your Unity project (UPM Git URL).
   - Install Reflex (`gustavopsantos/Reflex`) via source or UPM.

1. **Create a Reflex Installer**

```csharp
using DxMessaging.Core.MessageBus;
using Reflex.Core;
using Reflex.Injectors;

public sealed class DxMessagingInstaller : Installer
{
    protected override void InstallBindings()
    {
        // Single MessageBus per container. Bind both concrete and interface.
        Container.Bind<MessageBus>().AsSingleton();
        Container.Bind<IMessageBus>().FromContainer<MessageBus>();
    }
}
```

- Reflex bindings are additive; if you need per-feature buses, register additional named bindings or child containers with their own installer.

1. **Configure MessagingComponent Instances**

```csharp
using DxMessaging.Core.MessageBus;
using DxMessaging.Unity;
using Reflex.Attributes;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(MessagingComponent))]
public sealed class MessagingComponentConfigurator : MonoBehaviour
{
    [Inject]
    private IMessageBus messageBus;

    private void Awake()
    {
        GetComponent<MessagingComponent>().Configure(messageBus);
    }
}
```

- Attach this behaviour to prefabs that include `MessagingComponent`. Reflex injects the bus before dependent scripts call `Create`/`Token`.

1. **Injecting Tokens into Plain Classes**

```csharp
using DxMessaging.Core;
using DxMessaging.Core.MessageBus;
using Reflex.Core;

public sealed class DamageService
{
    private readonly MessageRegistrationToken token;

    public DamageService(IMessageBus messageBus)
    {
        MessageHandler handler = new MessageHandler(new InstanceId(314), messageBus)
        {
            active = true
        };

        token = MessageRegistrationToken.Create(handler, messageBus);
    }

    public void Initialize()
    {
        _ = token.RegisterUntargeted<PlayerDamaged>(OnPlayerDamaged);
        token.Enable();
    }

    public void Dispose()
    {
        token.Disable();
    }

    private void OnPlayerDamaged(ref PlayerDamaged message)
    {
        // Business logic
    }
}
```

- Reflex has no lifecycle interfaces; wire `Initialize`/`Dispose` using installer hooks (`Container.Instantiate`, custom bootstrapper) or call them from a controlling MonoBehaviour.

1. **Pooling & Runtime Instantiation**

- When spawning pooled MonoBehaviours, inject the `MessagingComponentConfigurator` so `Configure` runs after `Instantiate`.
- If your pool bypasses Reflex, manually call `container.Inject(instance)` to satisfy the `[Inject]` field before calling `Create` on the messaging token.

1. **Testing Tips**

- In edit-mode tests, build a temporary `Container` with `new ContainerBuilder().Install(new DxMessagingInstaller());` then resolve `IMessageBus` for the system under test.
- Override the global singleton via `MessageHandler.SetGlobalMessageBus(resolvedBus)` when legacy helpers or diagnostics expect it.

## Checklist

- [ ] Install `DxMessagingInstaller` in every Reflex container that needs messaging.
- [ ] Add `MessagingComponentConfigurator` to prefabs using `MessagingComponent`.
- [ ] Replace `MessageHandler.MessageBus` usages with injected `IMessageBus`.
- [ ] Ensure pooled objects receive injection before registering message handlers.
