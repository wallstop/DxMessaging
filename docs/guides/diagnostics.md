# Diagnostics

DxMessaging emphasizes visibility. You can enable diagnostics globally or per token, inspect recent emissions, page through registrations, and even view contexts (targets/sources) — all from the MessagingComponent inspector.

## DiagnosticsTarget Enum

The `DiagnosticsTarget` enum is a flags enum that controls when diagnostics are enabled. It allows fine-grained control over which execution environments collect diagnostic data.

| Value     | Description                                                     |
| --------- | --------------------------------------------------------------- |
| `Off`     | Diagnostics are disabled in all environments.                   |
| `Editor`  | Diagnostics run only while in the Unity Editor.                 |
| `Runtime` | Diagnostics run only in player/runtime builds (not the Editor). |
| `All`     | Diagnostics run in both Editor and runtime environments.        |

Because `DiagnosticsTarget` is a flags enum, you can combine values:

```csharp
using DxMessaging.Core.MessageBus;

// Enable diagnostics only in the Unity Editor
IMessageBus.GlobalDiagnosticsTargets = DiagnosticsTarget.Editor;

// Enable diagnostics only in runtime builds
IMessageBus.GlobalDiagnosticsTargets = DiagnosticsTarget.Runtime;

// Enable diagnostics everywhere
IMessageBus.GlobalDiagnosticsTargets = DiagnosticsTarget.All;

// Disable diagnostics completely
IMessageBus.GlobalDiagnosticsTargets = DiagnosticsTarget.Off;
```

## Configuration Toggles

DxMessaging provides multiple levels of diagnostics control:

### Global Defaults

- `IMessageBus.GlobalDiagnosticsTargets` — Sets the default diagnostics mode for newly created buses and tokens. Uses the `DiagnosticsTarget` flags enum.
- `IMessageBus.GlobalMessageBufferSize` — Sets the default ring buffer size for emission history (default: 100).

### Per-Bus and Per-Token

- `IMessageBus.DiagnosticsMode` — Read-only property indicating whether diagnostics are active for a specific bus instance.
- `MessageRegistrationToken.DiagnosticMode` — Controls diagnostics for an individual registration token.

```csharp
using DxMessaging.Core;
using DxMessaging.Core.MessageBus;

// Configure global defaults before creating buses/tokens
IMessageBus.GlobalDiagnosticsTargets = DiagnosticsTarget.Editor;
IMessageBus.GlobalMessageBufferSize = 200;

// Check if diagnostics are enabled for a specific bus
IMessageBus bus = MessageHandler.MessageBus;
if (bus.DiagnosticsMode)
{
    Debug.Log("Diagnostics are active on this bus.");
}
```

## RegistrationLog API

The `RegistrationLog` class tracks all messaging registrations and deregistrations for a message bus. This is invaluable for debugging subscription issues and understanding message flow.

### Properties

| Property        | Type                                   | Description                                                             |
| --------------- | -------------------------------------- | ----------------------------------------------------------------------- |
| `Enabled`       | `bool`                                 | Get/set whether logging is active. Disabled by default for performance. |
| `Registrations` | `IReadOnlyList<MessagingRegistration>` | Read-only access to all logged registrations.                           |

### Methods

#### `Log(MessagingRegistration registration)`

Records a registration event. Called automatically by the message bus when `Enabled` is true.

#### `GetRegistrations(InstanceId instanceId)`

Returns all registrations for a specific instance. Useful for inspecting what a particular component has registered for.

```csharp
using DxMessaging.Core;
using DxMessaging.Core.MessageBus;

IMessageBus bus = MessageHandler.MessageBus;
bus.Log.Enabled = true;

// After some registrations occur...
InstanceId myComponent = GetComponent<MonoBehaviour>();
foreach (MessagingRegistration reg in bus.Log.GetRegistrations(myComponent))
{
    Debug.Log($"Registered for {reg.type.Name} via {reg.registrationMethod}");
}
```

#### `ToString()` and `ToString(Func<MessagingRegistration, string> serializer)`

Returns a string representation of all logged registrations. You can provide a custom serializer for formatted output.

```csharp
using DxMessaging.Core;
using DxMessaging.Core.MessageBus;

IMessageBus bus = MessageHandler.MessageBus;
bus.Log.Enabled = true;

// ... after some registrations/deregistrations
Debug.Log(bus.Log.ToString());

// Custom formatting
string formatted = bus.Log.ToString(reg =>
    $"[{reg.registrationType}] {reg.type.Name} @ {reg.time:F2}s"
);
Debug.Log(formatted);
```

#### `Clear(Predicate<MessagingRegistration> shouldRemove = null)`

Removes registrations from the log. Pass `null` to clear all, or provide a predicate to selectively remove entries.

```csharp
using DxMessaging.Core;
using DxMessaging.Core.MessageBus;

IMessageBus bus = MessageHandler.MessageBus;

// Clear all registrations
int cleared = bus.Log.Clear();

// Clear only deregistrations
int deregistrationsCleared = bus.Log.Clear(
    reg => reg.registrationType == RegistrationType.Deregister
);
```

## MessagingRegistration Struct

Each logged registration is stored as a `MessagingRegistration` struct containing:

| Field                | Type                 | Description                                                  |
| -------------------- | -------------------- | ------------------------------------------------------------ |
| `id`                 | `InstanceId`         | The handler's unique identifier.                             |
| `type`               | `Type`               | The message type being registered for.                       |
| `registrationType`   | `RegistrationType`   | Whether this was a `Register` or `Deregister` event.         |
| `registrationMethod` | `RegistrationMethod` | The exact registration category (Targeted, Broadcast, etc.). |
| `time`               | `float`              | Unity time when the registration occurred (Unity only).      |

### RegistrationMethod Values

The `RegistrationMethod` enum captures how the handler was wired up:

- `Targeted` — Bound to a specific recipient
- `Untargeted` — Global untargeted handler
- `Broadcast` — Bound to a specific source
- `BroadcastWithoutSource` — Broadcast handler without explicit source
- `TargetedWithoutTargeting` — Targeted handler ignoring runtime target
- `GlobalAcceptAll` — Catch-all handler
- `Interceptor` — Message interceptor
- `UntargetedPostProcessor`, `TargetedPostProcessor`, `BroadcastPostProcessor` — Post-processors
- `TargetedWithoutTargetingPostProcessor` — Post-processor for targeted messages ignoring runtime target
- `BroadcastWithoutSourcePostProcessor` — Post-processor for broadcasts without explicit source

## Emission History

When diagnostics are enabled, buses and tokens record message emissions in a ring buffer:

- Buffer size is controlled by `IMessageBus.GlobalMessageBufferSize` (default: 100).
- Setting buffer size to 0 disables history retention (emissions are silently discarded).
- Inspect recent emissions per token via built-in diagnostics or build custom tools using post-processors.

```csharp
using DxMessaging.Core.MessageBus;

// Increase buffer size for more history
IMessageBus.GlobalMessageBufferSize = 500;
```

## Logging Integration

Integrate DxMessaging with your logging framework:

```csharp
using DxMessaging.Core;

MessagingDebug.enabled = true;
MessagingDebug.LogFunction = (level, msg) =>
    UnityEngine.Debug.Log($"[DxMessaging:{level}] {msg}");
```

## Per-Environment Configuration

A common pattern is enabling diagnostics only in the Editor for development visibility while keeping runtime builds lean.

### Editor-Only Diagnostics

```csharp
using DxMessaging.Core.MessageBus;

// Enable diagnostics only when running in the Unity Editor
IMessageBus.GlobalDiagnosticsTargets = DiagnosticsTarget.Editor;
```

This is the recommended default for most projects. You get full visibility during development without any performance cost in production builds.

### Runtime Diagnostics for QA Builds

For QA or debug builds where you need diagnostics in the player:

```csharp
using DxMessaging.Core.MessageBus;

#if DEVELOPMENT_BUILD || UNITY_EDITOR
IMessageBus.GlobalDiagnosticsTargets = DiagnosticsTarget.All;
#else
IMessageBus.GlobalDiagnosticsTargets = DiagnosticsTarget.Off;
#endif
```

### Conditional Logging Based on Build Type

```csharp
using DxMessaging.Core;
using DxMessaging.Core.MessageBus;

public static class DiagnosticsBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
#if UNITY_EDITOR
        IMessageBus.GlobalDiagnosticsTargets = DiagnosticsTarget.Editor;
        IMessageBus.GlobalMessageBufferSize = 200;
        MessageHandler.MessageBus.Log.Enabled = true;
#elif DEVELOPMENT_BUILD
        IMessageBus.GlobalDiagnosticsTargets = DiagnosticsTarget.Runtime;
        IMessageBus.GlobalMessageBufferSize = 50;
#else
        IMessageBus.GlobalDiagnosticsTargets = DiagnosticsTarget.Off;
#endif
    }
}
```

## Performance Considerations

Diagnostics add overhead. Consider these factors when enabling them:

### Memory Impact

- Each `MessagingRegistration` struct consumes memory for the registration log.
- The emission ring buffer stores `MessageEmissionData` records (controlled by `GlobalMessageBufferSize`).
- Larger buffer sizes consume more memory but provide more history.

### CPU Impact

- Registration logging adds overhead to every `Register` and `Deregister` call.
- Emission recording adds overhead to every message broadcast.
- Post-processor chains for diagnostics run after each message dispatch.

### Recommendations

| Environment        | Recommended Setting                     | Buffer Size |
| ------------------ | --------------------------------------- | ----------- |
| Development/Editor | `DiagnosticsTarget.Editor`              | 100-200     |
| QA/Debug Builds    | `DiagnosticsTarget.All`                 | 50-100      |
| Release Builds     | `DiagnosticsTarget.Off`                 | N/A         |
| Automated Tests    | `DiagnosticsTarget.All` + `Log.Enabled` | 100         |

```csharp
using DxMessaging.Core.MessageBus;

// Production-safe defaults
IMessageBus.GlobalDiagnosticsTargets = DiagnosticsTarget.Off;
IMessageBus.GlobalMessageBufferSize = 0; // No history retention
```

## Editor Integration (Inspector)

Attach `MessagingComponent` to a GameObject. In the Unity Inspector:

- **Enable/Disable Global Diagnostics**: Toggles bus-wide recording.
- **Global Buffer**: Paged view of recent emissions (type and context). Matching listeners are highlighted.
- **Local Buffer**: Per-listener ring buffer; enable per-token diagnostics to populate.
- **Registrations**: Paged list of what each listener registered for (type, priority, context).

## Tips

- Turn on diagnostics while developing; turn off for release builds if you don't need runtime recording.
- Use `RegisterTargetedWithoutTargeting` or `RegisterBroadcastWithoutSource` for custom monitoring dashboards.
- Set `Log.Enabled = true` in tests to verify registration behavior.
- Use `Log.Clear()` between test cases to isolate registration tracking.

## Related

- [Listening Patterns](../concepts/listening-patterns.md)
- [Troubleshooting](../reference/troubleshooting.md)
