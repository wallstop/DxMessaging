# Troubleshooting

Not receiving messages

- Ensure your `MessageRegistrationToken` is enabled (Enable in `OnEnable`, Disable in `OnDisable`).
- Verify the category matches the emission (Untargeted vs Targeted vs Broadcast).
- Targeted/Broadcast require a valid `InstanceId`; ensure the target/source object exists when you emit.
- In Unity, confirm your `MessagingComponent` exists on sender/receiver GameObjects.
- If inheriting from `MessageAwareComponent`, ensure your overrides call base methods:
  - `base.RegisterMessageHandlers()` to preserve default setup (including string message demos).
  - `base.OnEnable()` / `base.OnDisable()` so the token actually enables/disables.

Unexpected ordering

- Check `priority` values on registrations; lower runs earlier. Same priority is registration order.
- Interceptors always precede handlers and can cancel; confirm interceptors return `true`.

Double registration or over‑deregistration warnings

- Avoid calling stage/enable multiple times; pair registrations and lifecycles consistently.
- Review logs with `bus.Log.Enabled = true` to see the registration history.

Allocations/boxing

- Prefer struct messages implementing the generic interfaces: `I*Message<T>`.
- Use by‑ref handler overloads to avoid copies.

Emitting while disabled

- If you need to emit when a component is disabled, use a bus not tied to enable state or set `emitMessagesWhenDisabled` on `MessagingComponent`.

Diagnostics overhead

- Disable diagnostics in release builds (`IMessageBus.GlobalDiagnosticsMode = false`).

See also

- [Diagnostics](Docs/Diagnostics.md)
- [Listening Patterns](Docs/ListeningPatterns.md)
