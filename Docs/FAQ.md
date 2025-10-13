# FAQ — Frequently Asked Questions

[← Back to Index](Index.md) | [Troubleshooting](Troubleshooting.md) | [Getting Started](GettingStarted.md) | [Glossary](Glossary.md)

---

## Do I need to use attributes or source generators

- No. You can implement `IUntargetedMessage<T>`, `ITargetedMessage<T>`, or `IBroadcastMessage<T>` directly (recommended for structs). Attributes are optional and help tooling/source‑gen.

Which message type should I use?

- Untargeted: global notifications (any listener).
- Targeted: commands/events for a specific recipient.
- Broadcast: facts emitted from a source that others may observe.

How do I enforce ordering?

- Use the `priority` parameter at registration; lower runs earlier. Interceptors run before handlers; post‑processors run after.

Can I observe all targets/sources for a type?

- Yes. Use `RegisterTargetedWithoutTargeting<T>` or `RegisterBroadcastWithoutSource<T>` (and their post‑processor counterparts).

How do I diagnose what's happening?

- Enable logs and diagnostics: [Diagnostics](Diagnostics.md).

## What happens if I register a listener inside a message handler?

- The newly registered listener will **not** run for the current message emission. It will only become active starting with the **next** message emission.
- This is called "snapshot semantics" — when a message is emitted, DxMessaging takes a snapshot of all current listeners and uses that frozen list for the entire emission.
- This applies to all listener types (handlers, interceptors, post-processors) and all message categories (Untargeted, Targeted, Broadcast).
- This behavior prevents infinite loops and ensures predictable execution order. See [Interceptors & Ordering](InterceptorsAndOrdering.md#snapshot-semantics-frozen-listener-lists) for details and examples.

Do I need a global bus?

- A global bus is provided (`MessageHandler.MessageBus`). You can also create and pass your own `MessageBus` instance to isolate subsystems and tests.

## Is this compatible with Unity's SendMessage/UnityEvents

- Yes. You can integrate with legacy patterns via `ReflexiveMessage`. Prefer DxMessaging for new code.

---

## Related Documentation

**New to DxMessaging?**

- → [Visual Guide](VisualGuide.md) — Beginner-friendly introduction
- → [Getting Started](GettingStarted.md) — Complete guide
- → [Glossary](Glossary.md) — All terms explained

**Common Issues:**

- → [Troubleshooting](Troubleshooting.md) — Solutions to common problems
- → [Common Patterns](Patterns.md) — See how to use it correctly

**Reference:**

- → [Quick Reference](QuickReference.md) — API cheat sheet
- → [Message Types](MessageTypes.md) — Which type to use when
