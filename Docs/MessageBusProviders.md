# Message Bus Providers

DxMessaging ships a set of ScriptableObject providers that let designers or installers configure which `IMessageBus` drives a `MessagingComponent`. This page outlines the two built-in providers and when to use each.

## Current Global Message Bus Provider

`CurrentGlobalMessageBusProvider` mirrors the process-wide `MessageHandler.MessageBus`. If you override the global bus during gameplay or tests via `MessageHandler.SetGlobalMessageBus` or `MessageHandler.OverrideGlobalMessageBus`, this provider resolves the active instance.

```csharp
[RequireComponent(typeof(MessagingComponent))]
public sealed class MessagingComponentConfigurator : MonoBehaviour
{
    [SerializeField]
    private CurrentGlobalMessageBusProvider provider;

    private void Awake()
    {
        MessagingComponent component = GetComponent<MessagingComponent>();
        component.Configure(provider, MessageBusRebindMode.RebindActive);
    }
}
```

## Initial Global Message Bus Provider

`InitialGlobalMessageBusProvider` always returns the startup bus that DxMessaging created during static initialisation. It ignores subsequent overrides, making it ideal when you need a stable point of comparison (for example, when diagnosing tests that temporarily swap the global bus).

```csharp
InitialGlobalMessageBusProvider initial = Resources.Load<InitialGlobalMessageBusProvider>(
    "InitialGlobalMessageBusProvider");
IMessageBus startupBus = initial.Resolve();

using (MessageHandler.OverrideGlobalMessageBus(testBus))
{
    // Execute test scenario against testBus
}

// startupBus still references the original global instance for assertions
Assert.AreSame(startupBus, initial.Resolve());
```

## Pairing Providers with Installers

Both providers can be referenced by `MessagingComponentInstaller` or the DI registration shims. The `Samples~/DI/Prefabs/MessagingInstallerSample.prefab` prefab demonstrates how the installer configures child components when a provider asset is assigned. Designers can swap between the current and initial providers to toggle behaviour without changing scene code.
