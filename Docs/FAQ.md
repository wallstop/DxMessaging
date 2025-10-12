# FAQ

Do I need to use attributes or source generators?

- No. You can implement `IUntargetedMessage<T>`, `ITargetedMessage<T>`, or `IBroadcastMessage<T>` directly (recommended for structs). Attributes are optional and help tooling/source‑gen.

Which message type should I use?

- Untargeted: global notifications (any listener).
- Targeted: commands/events for a specific recipient.
- Broadcast: facts emitted from a source that others may observe.

How do I enforce ordering?

- Use the `priority` parameter at registration; lower runs earlier. Interceptors run before handlers; post‑processors run after.

Can I observe all targets/sources for a type?

- Yes. Use `RegisterTargetedWithoutTargeting<T>` or `RegisterBroadcastWithoutSource<T>` (and their post‑processor counterparts).

How do I diagnose what’s happening?

- Enable logs and diagnostics: [Diagnostics](Diagnostics.md).

Do I need a global bus?

- A global bus is provided (`MessageHandler.MessageBus`). You can also create and pass your own `MessageBus` instance to isolate subsystems and tests.

Is this compatible with Unity’s SendMessage/UnityEvents?

- Yes. You can integrate with legacy patterns via `ReflexiveMessage`. Prefer DxMessaging for new code.
