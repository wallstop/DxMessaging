# Troubleshooting — Common Issues & Solutions

[← Back to Index](Index.md) | [FAQ](FAQ.md) | [Getting Started](GettingStarted.md) | [Glossary](Glossary.md)

---

## Not receiving messages

- Ensure your `MessageRegistrationToken` is enabled (Enable in `OnEnable`, Disable in `OnDisable`).
- Verify the category matches the emission (Untargeted vs Targeted vs Broadcast).
- Targeted/Broadcast require a valid `InstanceId`; ensure the target/source object exists when you emit.
- In Unity, confirm your `MessagingComponent` exists on sender/receiver GameObjects.
- **CRITICAL**: If inheriting from `MessageAwareComponent`, ensure your overrides call base methods:
  - **`base.RegisterMessageHandlers()`** - Call this FIRST in your override to preserve default setup (including string message demos) and parent class registrations.
  - **`base.Awake()`** - Call this if you override `Awake()`, or your token won't be created (this is the #1 cause of handlers not firing).
  - **`base.OnEnable()` / `base.OnDisable()`** - Call these so the token actually enables/disables.
  - **Never use `new` to hide Unity methods** (e.g., `new void OnEnable()`); always use `override` and call `base.*`.

Registration timing

- **ALWAYS register message handlers in `Awake()`**, not `Start()`.
- `MessageAwareComponent` automatically calls `RegisterMessageHandlers()` in `Awake()`.
- Registering in `Awake()` ensures handlers are ready before other components' `Start()` methods run.
- If you register in `Start()`, you may miss messages emitted by other components in their `Start()` methods.

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

---

## Related Documentation

**Get Unstuck:**

- → [FAQ](FAQ.md) — Common questions answered
- → [Getting Started](GettingStarted.md) — Learn the basics
- → [Glossary](Glossary.md) — Understand the terminology

**Debug & Inspect:**

- → [Diagnostics](Diagnostics.md) — Inspector tools and debugging
- → [Listening Patterns](ListeningPatterns.md) — Verify you're listening correctly
- → [Message Types](MessageTypes.md) — Ensure you're using the right type

**Examples:**

- → [Mini Combat sample](../Samples~/Mini%20Combat/README.md) — See working code
- → [Common Patterns](Patterns.md) — Real-world solutions
