# API Reference (Practical)

Message registration (Unity‑friendly)

- `MessageRegistrationToken`
  - Enable/Disable: `Enable()`, `Disable()`, `UnregisterAll()`, `RemoveRegistration(handle)`
  - Untargeted
    - `RegisterUntargeted<T>(Action<T> handler, int priority = 0)`
    - `RegisterUntargeted<T>(MessageHandler.FastHandler<T> handler, int priority = 0)`
    - `RegisterUntargetedPostProcessor<T>(MessageHandler.FastHandler<T> handler, int priority = 0)`
  - Targeted
    - Specific target (GameObject/Component/InstanceId overloads)
    - `RegisterGameObjectTargeted<T>(...); RegisterComponentTargeted<T>(...); RegisterTargeted<T>(InstanceId, ...)`
    - Post‑processors: `RegisterTargetedPostProcessor<T>(target, ...)`
    - All targets: `RegisterTargetedWithoutTargeting<T>(...)`
    - All targets post: `RegisterTargetedWithoutTargetingPostProcessor<T>(...)`
  - Broadcast
    - Specific source: `RegisterBroadcast<T>(InstanceId, ...)`
    - Unity overloads: `RegisterGameObjectBroadcast<T>(GameObject, ...)`, `RegisterComponentBroadcast<T>(Component, ...)`
    - Post‑processors: `RegisterBroadcastPostProcessor<T>(source, ...)`
    - All sources: `RegisterBroadcastWithoutSource<T>(...)`
    - All sources post: `RegisterBroadcastWithoutSourcePostProcessor<T>(...)`

Emit helpers

- `DxMessaging.Core.Extensions.MessageExtensions`
  - Untargeted: `Emit<T>(this T message)` / `EmitUntargeted`
  - Targeted: `EmitTargeted<T>(InstanceId target)` and GameObject/Component variants
  - Broadcast: `EmitBroadcast<T>(InstanceId source)` and GameObject/Component variants
  - String conveniences: `"text".Emit()` and `"text".Emit(InstanceId target)`

Interceptors (bus‑level)

- `IMessageBus`
  - `RegisterUntargetedInterceptor<T>(UntargetedInterceptor<T> fn, int priority = 0)`
  - `RegisterTargetedInterceptor<T>(TargetedInterceptor<T> fn, int priority = 0)`
  - `RegisterBroadcastInterceptor<T>(BroadcastInterceptor<T> fn, int priority = 0)`
  - `RegisterGlobalAcceptAll(MessageHandler handler)`

Diagnostics

- `IMessageBus.GlobalDiagnosticsMode`, `IMessageBus.GlobalMessageBufferSize`
- `IMessageBus.DiagnosticsMode`, `MessageRegistrationToken.DiagnosticMode`
- `RegistrationLog` (`bus.Log.Enabled`, `bus.Log.ToString()`)

Key types

- `DxMessaging.Core.InstanceId` — value type identity for GameObjects/Components or custom owners.
- `DxMessaging.Core.MessageHandler` — per‑owner callback runner.
- `DxMessaging.Core.MessageBus.MessageBus` — instanced bus; global at `MessageHandler.MessageBus`.
- `DxMessaging.Core.Messages.*` — Untargeted/Targeted/Broadcast interfaces and built‑in string messages.
  - See also [String Messages](Docs/StringMessages.md)

Unity bridge types

- `DxMessaging.Unity.MessagingComponent`
  - Fields: `emitMessagesWhenDisabled`
  - Methods: `Create(MonoBehaviour)`, `ToggleMessageHandler(bool)`
- `DxMessaging.Unity.MessageAwareComponent`
  - Properties: `Token`
  - Virtuals: `MessageRegistrationTiedToEnableStatus`, `RegisterForStringMessages`
  - Hooks: `RegisterMessageHandlers()` calls in `Awake()`, auto `Enable()`/`Disable()` in `OnEnable`/`OnDisable` when tied to enable state

Source files (for exploration)

- Message bus interface: Runtime/Core/MessageBus/IMessageBus.cs
- Message bus: Runtime/Core/MessageBus/MessageBus.cs
- Message handler/token: Runtime/Core/MessageHandler.cs, Runtime/Core/MessageRegistrationToken.cs
- Emit helpers: Runtime/Core/Extensions/MessageExtensions.cs
- Attributes: Runtime/Core/Attributes/*.cs

API tables (quick view)

Token registrations

| Category | Specific (Unity overloads) | All targets/sources | Post‑processing |
| --- | --- | --- | --- |
| Untargeted | `RegisterUntargeted<T>(Action<T> | FastHandler<T>)` | — | `RegisterUntargetedPostProcessor<T>(FastHandler<T>)` |
| Targeted | `RegisterGameObjectTargeted<T>(GameObject, ...)` · `RegisterComponentTargeted<T>(Component, ...)` · `RegisterTargeted<T>(InstanceId, ...)` | `RegisterTargetedWithoutTargeting<T>(FastHandlerWithContext<T>)` | `RegisterTargetedPostProcessor<T>(InstanceId, FastHandler<T>)` · `RegisterTargetedWithoutTargetingPostProcessor<T>(FastHandlerWithContext<T>)` |
| Broadcast | `RegisterGameObjectBroadcast<T>(GameObject, ...)` · `RegisterComponentBroadcast<T>(Component, ...)` · `RegisterBroadcast<T>(InstanceId, ...)` | `RegisterBroadcastWithoutSource<T>(FastHandlerWithContext<T>)` | `RegisterBroadcastPostProcessor<T>(InstanceId, FastHandler<T>)` · `RegisterBroadcastWithoutSourcePostProcessor<T>(Action<InstanceId,T> | FastHandlerWithContext<T>)` |

Bus‑level

| Category | API |
| --- | --- |
| Interceptors | `RegisterUntargetedInterceptor<T>(UntargetedInterceptor<T>)` · `RegisterTargetedInterceptor<T>(TargetedInterceptor<T>)` · `RegisterBroadcastInterceptor<T>(BroadcastInterceptor<T>)` |
| Global observer | `RegisterGlobalAcceptAll(MessageHandler)` |
