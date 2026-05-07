# FAQ -- Frequently Asked Questions

[Back to Index](../getting-started/index.md) | [Troubleshooting](troubleshooting.md) | [Getting Started](../getting-started/getting-started.md) | [Glossary](glossary.md)

---

## Do I need to use attributes or source generators

- No. You can implement `IUntargetedMessage<T>`, `ITargetedMessage<T>`, or `IBroadcastMessage<T>` directly (recommended for structs). Attributes are optional and help tooling/source-gen.

## Which message type should I use?

- **Untargeted** - global notifications (any listener).
- **Targeted** - commands/events for a specific recipient.
- **Broadcast** - facts emitted from a source that others may observe.

## How do I enforce ordering?

- Use the `priority` parameter at registration; lower runs earlier. Interceptors run before handlers; post-processors run after.

## Can I observe all targets/sources for a type?

- Yes. Use `RegisterTargetedWithoutTargeting<T>` or `RegisterBroadcastWithoutSource<T>` (and their post-processor counterparts).

## How do I diagnose what's happening?

- Enable logs and diagnostics: [Diagnostics](../guides/diagnostics.md).

## My MessageAwareComponent subclass does not receive messages. What is wrong?

The most common cause is forgetting to call `base.Awake()` (or `base.OnEnable()`, `base.OnDisable()`, `base.OnDestroy()`, `base.RegisterMessageHandlers()`) when you override one of those methods. The framework's setup runs in those base calls; without them, your registration token is never created or your handlers never enable. The Roslyn analyzer flags this as DXMSG006. See [Inheritance and base calls](../getting-started/quick-start.md#important-inheritance-and-base-calls) for the full list of guarded methods.

## What happens if I register a listener inside a message handler?

- The newly registered listener will **not** run for the current message emission. It will only become active starting with the **next** message emission.
- This is called "snapshot semantics" -- when a message is emitted, DxMessaging takes a snapshot of all current listeners and uses that frozen list for the entire emission.
- This applies to all listener types (handlers, interceptors, post-processors) and all message categories (Untargeted, Targeted, Broadcast).
- This behavior prevents infinite loops and ensures predictable execution order. See [Interceptors & Ordering](../concepts/interceptors-and-ordering.md#snapshot-semantics-frozen-listener-lists) for details and examples.

## Do I need a global bus?

- A global bus is provided (`MessageHandler.MessageBus`). You can also create and pass your own `MessageBus` instance to isolate subsystems and tests.

## Is this compatible with Unity's SendMessage/UnityEvents

- Yes. You can integrate with legacy patterns via `ReflexiveMessage`. Prefer DxMessaging for new code.

## Why is my game retaining memory across scenes?

- Each scene introduces new `InstanceId`s and sometimes new message types,
  which add empty slots on the bus when their handlers tear down. Idle
  eviction will reclaim them eventually; for deterministic cleanup call
  `MessageHandler.TrimAll(force: true)` on scene unload (or
  `bus.Trim(force: true)` for a non-global bus). See the
  [Memory Reclamation guide](../guides/memory-reclamation.md) for the full
  pattern.

---

## Related Documentation

- **New to DxMessaging?**
  - to [Visual Guide](../getting-started/visual-guide.md) -- Beginner-friendly introduction
  - to [Getting Started](../getting-started/getting-started.md) -- Complete guide
  - to [Glossary](glossary.md) -- All terms explained
- **Common Issues**
  - to [Troubleshooting](troubleshooting.md) -- Solutions to common problems
  - to [Common Patterns](../guides/patterns.md) -- See how to use it correctly
- **Reference**
  - to [Quick Reference](quick-reference.md) -- API cheat sheet
  - to [Message Types](../concepts/message-types.md) -- Which type to use when
