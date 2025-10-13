# API Reference (Practical)

Message registration (Unity‑friendly)

- `MessageRegistrationToken`
  - Enable/Disable: `Enable()`, `Disable()`, `UnregisterAll()`, `RemoveRegistration(handle)`
  - Untargeted
    - `RegisterUntargeted&lt;T&gt;(Action&lt;T&gt; handler, int priority = 0)`
    - `RegisterUntargeted&lt;T&gt;(MessageHandler.FastHandler&lt;T&gt; handler, int priority = 0)`
    - `RegisterUntargetedPostProcessor&lt;T&gt;(MessageHandler.FastHandler&lt;T&gt; handler, int priority = 0)`
  - Targeted
    - Specific target (GameObject/Component/InstanceId overloads)
    - `RegisterGameObjectTargeted&lt;T&gt;(...); RegisterComponentTargeted&lt;T&gt;(...); RegisterTargeted&lt;T&gt;(InstanceId, ...)`
    - Post‑processors: `RegisterTargetedPostProcessor&lt;T&gt;(target, ...)`
    - All targets: `RegisterTargetedWithoutTargeting&lt;T&gt;(...)`
    - All targets post: `RegisterTargetedWithoutTargetingPostProcessor&lt;T&gt;(...)`
  - Broadcast
    - Specific source: `RegisterBroadcast&lt;T&gt;(InstanceId, ...)`
    - Unity overloads: `RegisterGameObjectBroadcast&lt;T&gt;(GameObject, ...)`, `RegisterComponentBroadcast&lt;T&gt;(Component, ...)`
    - Post‑processors: `RegisterBroadcastPostProcessor&lt;T&gt;(source, ...)`
    - All sources: `RegisterBroadcastWithoutSource&lt;T&gt;(...)`
    - All sources post: `RegisterBroadcastWithoutSourcePostProcessor&lt;T&gt;(...)`

Emit helpers

- `DxMessaging.Core.Extensions.MessageExtensions`
  - Untargeted: `Emit&lt;T&gt;(this T message)` / `EmitUntargeted`
  - Targeted: `EmitTargeted&lt;T&gt;(InstanceId target)` and GameObject/Component variants
  - Broadcast: `EmitBroadcast&lt;T&gt;(InstanceId source)` and GameObject/Component variants
  - String conveniences: `"text".Emit()` and `"text".Emit(InstanceId target)`

Interceptors (bus‑level)

- `IMessageBus`
  - `RegisterUntargetedInterceptor&lt;T&gt;(UntargetedInterceptor&lt;T&gt; fn, int priority = 0)`
  - `RegisterTargetedInterceptor&lt;T&gt;(TargetedInterceptor&lt;T&gt; fn, int priority = 0)`
  - `RegisterBroadcastInterceptor&lt;T&gt;(BroadcastInterceptor&lt;T&gt; fn, int priority = 0)`
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
  - See also [String Messages](StringMessages.md)

Unity bridge types

- `DxMessaging.Unity.MessagingComponent`
  - Fields: `emitMessagesWhenDisabled`
  - Methods: `Create(MonoBehaviour)`, `ToggleMessageHandler(bool)`
- `DxMessaging.Unity.MessageAwareComponent`
  - Properties: `Token`
  - Virtuals: `MessageRegistrationTiedToEnableStatus`, `RegisterForStringMessages`
  - Hooks: `RegisterMessageHandlers()` calls in `Awake()`, auto `Enable()`/`Disable()` in `OnEnable`/`OnDisable` when tied to enable state
  - Inheritance tip: If you override any of these hooks, call the base method (`base.RegisterMessageHandlers()`, `base.OnEnable()`, `base.OnDisable()`, `base.Awake()`/`base.OnDestroy()`). Skipping base calls may prevent token setup and default string‑message registrations.

Source files (for exploration)

- Message bus interface: Runtime/Core/MessageBus/IMessageBus.cs
- Message bus: Runtime/Core/MessageBus/MessageBus.cs
- Message handler/token: Runtime/Core/MessageHandler.cs, Runtime/Core/MessageRegistrationToken.cs
- Emit helpers: Runtime/Core/Extensions/MessageExtensions.cs
- Attributes: Runtime/Core/Attributes/\*.cs

API tables (quick view)

Token registrations

| Category   | Specific (Unity overloads)                                                                                                                                      | All targets/sources                                                          | Post‑processing                                                                                                                                                                                     |
| ---------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Untargeted | `RegisterUntargeted&lt;T&gt;(Action&lt;T&gt; or FastHandler&lt;T&gt;)`                                                                                          | —                                                                            | `RegisterUntargetedPostProcessor&lt;T&gt;(FastHandler&lt;T&gt;)`                                                                                                                                    |
| Targeted   | `RegisterGameObjectTargeted&lt;T&gt;(GameObject, ...)` · `RegisterComponentTargeted&lt;T&gt;(Component, ...)` · `RegisterTargeted&lt;T&gt;(InstanceId, ...)`    | `RegisterTargetedWithoutTargeting&lt;T&gt;(FastHandlerWithContext&lt;T&gt;)` | `RegisterTargetedPostProcessor&lt;T&gt;(InstanceId, FastHandler&lt;T&gt;)` · `RegisterTargetedWithoutTargetingPostProcessor&lt;T&gt;(FastHandlerWithContext&lt;T&gt;)`                              |
| Broadcast  | `RegisterGameObjectBroadcast&lt;T&gt;(GameObject, ...)` · `RegisterComponentBroadcast&lt;T&gt;(Component, ...)` · `RegisterBroadcast&lt;T&gt;(InstanceId, ...)` | `RegisterBroadcastWithoutSource&lt;T&gt;(FastHandlerWithContext&lt;T&gt;)`   | `RegisterBroadcastPostProcessor&lt;T&gt;(InstanceId, FastHandler&lt;T&gt;)` · `RegisterBroadcastWithoutSourcePostProcessor&lt;T&gt;(Action&lt;InstanceId,T&gt; or FastHandlerWithContext&lt;T&gt;)` |

Bus‑level

| Category        | API                                                                                                                                                                                                                      |
| --------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Interceptors    | `RegisterUntargetedInterceptor&lt;T&gt;(UntargetedInterceptor&lt;T&gt;)` · `RegisterTargetedInterceptor&lt;T&gt;(TargetedInterceptor&lt;T&gt;)` · `RegisterBroadcastInterceptor&lt;T&gt;(BroadcastInterceptor&lt;T&gt;)` |
| Global observer | `RegisterGlobalAcceptAll(MessageHandler)`                                                                                                                                                                                |
