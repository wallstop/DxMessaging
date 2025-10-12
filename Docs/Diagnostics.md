# Diagnostics

Short intro

DxMessaging emphasizes visibility. You can enable diagnostics globally or per token, inspect recent emissions, page through registrations, and even view contexts (targets/sources) — all from the MessagingComponent inspector.

Toggles

- Global defaults
  - `IMessageBus.GlobalDiagnosticsMode`
  - `IMessageBus.GlobalMessageBufferSize`
- Per bus: `IMessageBus.DiagnosticsMode`
- Per token: `MessageRegistrationToken.DiagnosticMode`

Registration log

```csharp
using DxMessaging.Core; // MessageHandler

var bus = MessageHandler.MessageBus;
bus.Log.Enabled = true;
// ... after some registrations/unregistrations
UnityEngine.Debug.Log(bus.Log.ToString());
```

Emission history

- When diagnostics are enabled, buses/tokens record message emissions in a ring buffer.
- Inspect recent emissions per token via built‑in diagnostics or build tools atop post‑processors.

Logging integration

```csharp
using DxMessaging.Core; // MessagingDebug

MessagingDebug.enabled = true;
MessagingDebug.LogFunction = (level, msg) => UnityEngine.Debug.Log($"[{level}] {msg}");
```

Recommended practices

- Enable detailed diagnostics in Editor and in tests; disable in release builds if not needed.
- Use post‑processors to collect metrics and structured logs.

Related

- [Listening Patterns](ListeningPatterns.md)
- [Troubleshooting](Troubleshooting.md)

Editor integration (Inspector)

- Attach `MessagingComponent` to a GameObject. In the Unity Inspector:
  - Enable/Disable Global Diagnostics: toggles bus‑wide recording.
  - Global Buffer: paged view of recent emissions (type and context). Matching listeners are highlighted.
  - Local Buffer: per‑listener ring buffer; enable per‑token diagnostics to populate.
  - Registrations: paged list of what each listener registered for (type, priority, context).

Tips

- Turn on diagnostics while developing; turn off for release builds if you don’t need runtime recording.
- Use across‑all listeners (RegisterTargetedWithoutTargeting / RegisterBroadcastWithoutSource) for custom dashboards.
