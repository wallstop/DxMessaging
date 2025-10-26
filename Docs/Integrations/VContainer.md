# DxMessaging + VContainer

## Goals

- Resolve `MessageBus` through VContainer scopes for deterministic lifetimes.
- Configure `MessagingComponent` instances with the container-provided bus.
- Demonstrate how scene/lifetime scopes can isolate messaging islands.

## Setup Steps

1. **Install Packages**
   - Add DxMessaging to your Unity project (UPM Git URL).
   - Install VContainer (Git URL or OpenUPM registry).

1. **Register the MessageBus in a LifetimeScope**

```csharp
using DxMessaging.Core.MessageBus;
using UnityEngine;
using VContainer;
using VContainer.Unity;

public sealed class MessagingLifetimeScope : LifetimeScope
{
    protected override void Configure(IContainerBuilder builder)
    {
        builder.Register<MessageBus>(Lifetime.Singleton);

        // Optionally expose IMessageBus for constructor injection.
        builder.Register<MessageBus>(Lifetime.Singleton).As<IMessageBus>();
    }
}
```

- Register `MessageBus` as `Lifetime.Singleton` if you want one bus per project scope. Use `Lifetime.Scoped` when constructing isolated scopes (e.g., additive scenes).

1. **Configure MessagingComponent Instances**

```csharp
using DxMessaging.Core.MessageBus;
using DxMessaging.Unity;
using UnityEngine;
using VContainer;
using VContainer.Unity;

[RequireComponent(typeof(MessagingComponent))]
public sealed class MessagingComponentConfigurator : MonoBehaviour, IStartable
{
    [Inject]
    private readonly IMessageBus messageBus;

    private MessagingComponent messagingComponent;

    private void Awake()
    {
        messagingComponent = GetComponent<MessagingComponent>();
    }

    public void Start()
    {
        messagingComponent.Configure(messageBus, MessageBusRebindMode.RebindActive);
    }
}
```

- Hook into `IStartable` so the bus is applied before dependents call `Create`. For prefabs instantiated via `LifetimeScope`, add this component alongside `MessagingComponent`.

1. **Injecting Tokens into Plain Classes**

```csharp
using DxMessaging.Core.MessageBus;
using DxMessaging.Tests.Runtime.Scripts.Messages;
using VContainer.Unity;

public sealed class PlayerService : IStartable, IDisposable
{
    private readonly MessageRegistrationLease lease;

    public PlayerService(IMessageRegistrationBuilder registrationBuilder)
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

    public void Start()
    {
        lease.Activate();
    }

    public void Dispose()
    {
        lease.Dispose();
    }

    private static void OnPlayerSpawned(ref PlayerSpawned message)
    {
        // Handle spawn
    }
}
```

- `IStartable` and `IDisposable` align the lease lifetime with the container scope. Use `LifetimeScope.CreateChild` when you need isolated buses for spawned levels or gameplay modes.
- Components can call `MessagingComponent.CreateRegistrationBuilder()` to share the same scoped bus with plain services.
- Define `VCONTAINER_PRESENT` (the asmdef in `Runtime/Unity/Integrations/VContainer/` adds it for that assembly automatically once VContainer is detected). With the define active you can call `RegisterMessageRegistrationBuilder` (see `Runtime/Unity/Integrations/`) for a one-line binding inside your scopes.

1. **Scene Scopes and Isolation**

```csharp
public sealed class LevelLoader
{
    private readonly LifetimeScope parentScope;

    public LevelLoader(LifetimeScope parentScope)
    {
        this.parentScope = parentScope;
    }

    public LifetimeScope LoadLevel(GameObject lifetimeScopePrefab)
    {
        return parentScope.CreateChildFromPrefab(lifetimeScopePrefab, builder =>
        {
            builder.Register<MessageBus>(Lifetime.Singleton).As<IMessageBus>();
        });
    }
}
```

- Each child scope can host its own `MessageBus`, giving per-level or per-feature isolation. Ensure objects in the child scene call `Configure` to pick up the new bus.

1. **Testing Tips**

- In edit-mode tests, instantiate a `LifetimeScope`, resolve `MessageBus`, then call `MessageHandler.SetGlobalMessageBus(resolvedBus)` if test shorthands rely on the singleton.
- For play-mode tests, spawn a temporary `LifetimeScope` prefab providing a fresh bus; call `Dispose` after the test to ensure handlers deregister cleanly.

## Checklist

- [ ] Add `MessagingLifetimeScope` to the project (Root scope or scene scope).
- [ ] Ensure every `MessagingComponent` prefab includes `MessagingComponentConfigurator`.
- [ ] Replace direct `MessageHandler.MessageBus` usages with injected `IMessageBus`.
- [ ] Use scope factories to create isolated buses for additive scenes or gameplay modes.
