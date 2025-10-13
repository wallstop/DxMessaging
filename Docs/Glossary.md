# Glossary — DxMessaging Terms Explained

[← Back to Index](Index.md) | [Getting Started](GettingStarted.md) | [Visual Guide](VisualGuide.md)

---

**New to messaging systems?** This glossary explains key terms in plain English.

## Core Concepts

### Message

A **data structure** that represents something that happened or should happen. Think of it like a letter or announcement containing information.

Example: `Heal { amount: 10 }` is a message saying "heal for 10 points."

### Message Type

The **category** of message that determines who receives it:

- **Untargeted**: Everyone hears it (like a megaphone announcement)
- **Targeted**: One specific recipient gets it (like a letter to an address)
- **Broadcast**: Comes from one source, anyone can listen (like a news broadcast)

### Handler

A **function** that runs when a message is received. Your game logic goes here.

Example:

```csharp
void OnHeal(ref Heal msg) {
    health += msg.amount; // This is the handler
}
```

### Token

A **registration handle** that manages the lifecycle of your message handlers. It automatically enables/disables handlers when your component is active/inactive.

Think of it like a subscription card — when you destroy it, all your subscriptions end automatically.

### MessageAwareComponent

A **Unity MonoBehaviour base class** that handles all the lifecycle management for you. Inherit from this and you get automatic setup/cleanup.

```csharp
public class MyComponent : MessageAwareComponent {
    // Token is created automatically!
}
```

## Message Pipeline

### Interceptor

Code that runs **before** handlers. Can **validate, modify, or cancel** messages before anyone else sees them.

Example: "Only allow damage between 1 and 999"

```csharp
_ = token.RegisterInterceptor<Damage>((ref Damage msg) => {
    if (msg.amount <= 0) return false; // Cancel
    if (msg.amount > 999) msg = new Damage(999); // Clamp
    return true; // Allow
});
```

### Post-Processor

Code that runs **after** all handlers. Perfect for **logging, analytics, or metrics** that shouldn't affect gameplay.

Example: "Track every damage event for statistics"

```csharp
_ = token.RegisterPostProcessor<Damage>((ref Damage msg) => {
    Analytics.LogDamage(msg.amount);
});
```

### Priority

A **number** that controls execution order. **Lower numbers run first**.

Example:

```csharp
_ = token.RegisterUntargeted<GameExit>(SaveGame, priority: 0);  // Runs first
_ = token.RegisterUntargeted<GameExit>(FadeAudio, priority: 5); // Runs second
_ = token.RegisterUntargeted<GameExit>(ShowUI, priority: 10);   // Runs third
```

## Unity Integration

### InstanceId

A **unique identifier** for a GameObject or Component. Used internally to route messages to the right place.

You rarely use this directly — use the GameObject/Component helpers instead:

```csharp
msg.EmitGameObjectTargeted(gameObject); // Helper (use this)
// vs
msg.EmitTargeted(gameObject.GetInstanceID()); // Manual InstanceId (avoid)
```

### MessagingComponent

The **base Unity component** that provides messaging infrastructure. `MessageAwareComponent` inherits from this.

### Emit

To **send** a message. Like hitting "send" on an email.

```csharp
var heal = new Heal(10);
heal.Emit(); // Send it!
```

### Register

To **sign up** to receive messages. Like subscribing to a newsletter.

```csharp
_ = Token.RegisterUntargeted<Heal>(OnHeal); // Subscribe
```

## Advanced Terms

### Message Bus

The **central routing system** that delivers messages. Think of it like a post office.

There's a global bus (`MessageHandler.MessageBus`) that most code uses, but you can create local buses for testing or isolation.

### Local Bus / Bus Island

A **separate message bus** used to isolate subsystems or tests. Messages sent to a local bus don't affect the global bus.

```csharp
var testBus = new MessageBus(); // Create island
var token = MessageRegistrationToken.Create(handler, testBus); // Use it
```

### Global Accept-All

A special handler that receives **every single message** regardless of type. Used for tools, debuggers, and analytics.

```csharp
_ = Token.RegisterGlobalAcceptAll(
    (ref IUntargetedMessage m) => Debug.Log("Untargeted: " + m),
    (ref InstanceId t, ref ITargetedMessage m) => Debug.Log("Targeted: " + m),
    (ref InstanceId s, ref IBroadcastMessage m) => Debug.Log("Broadcast: " + m)
);
```

### Diagnostics Mode

A **debug feature** that tracks message history and handler statistics. Enable in Editor, disable in builds for performance.

```csharp
IMessageBus.GlobalDiagnosticsMode = true; // See message history
```

## Attributes (Source Generation)

### [DxUntargetedMessage]

Marks a struct as an **Untargeted message** (global announcement).

### [DxTargetedMessage]

Marks a struct as a **Targeted message** (command to one recipient).

### [DxBroadcastMessage]

Marks a struct as a **Broadcast message** (event from a source).

### [DxAutoConstructor]

Auto-generates a **constructor** for your message struct so you don't have to write it manually.

```csharp
[DxTargetedMessage]
[DxAutoConstructor] // Generates: public Heal(int amount) { this.amount = amount; }
public readonly partial struct Heal {
    public readonly int amount;
}
```

## Common Patterns

### Lifecycle

The **creation and destruction** of components and their message registrations.

DxMessaging handles this automatically via `MessageAwareComponent`:

- `Awake()`: Token created, handlers registered
- `OnEnable()`: Token enabled, handlers active
- `OnDisable()`: Token disabled, handlers inactive
- `OnDestroy()`: Token destroyed, everything cleaned up

### Decoupling

Making systems **independent** so they don't need references to each other.

**Before DxMessaging:**

```csharp
public class UI : MonoBehaviour {
    [SerializeField] Player player; // Tight coupling!
    [SerializeField] EnemySpawner spawner;
    [SerializeField] AudioManager audio;
}
```

**With DxMessaging:**

```csharp
public class UI : MessageAwareComponent {
    // No references needed! Just listen for messages.
    protected override void RegisterMessageHandlers() {
        _ = Token.RegisterBroadcastWithoutSource<PlayerDamaged>(OnDamage);
    }
}
```

## Quick Reference Table

| Term               | One-Line Explanation                                 |
| ------------------ | ---------------------------------------------------- |
| **Message**        | Data saying "something happened" or "do something"   |
| **Handler**        | Function that runs when you receive a message        |
| **Token**          | Subscription manager (auto-cleanup)                  |
| **Interceptor**    | Guard that checks/modifies messages before delivery  |
| **Post-Processor** | Code that runs after all handlers (logging/metrics)  |
| **Priority**       | Number controlling execution order (lower = earlier) |
| **Emit**           | Send a message                                       |
| **Register**       | Subscribe to receive messages                        |
| **InstanceId**     | Unique ID for a GameObject or Component              |
| **Bus**            | Central message router (like a post office)          |

---

## Related Documentation

**Learn More:**

- → [Visual Guide](VisualGuide.md) — See these concepts visualized
- → [Getting Started](GettingStarted.md) — Full introduction with examples
- → [Message Types](MessageTypes.md) — When to use each type

**Reference:**

- → [Quick Reference](QuickReference.md) — API cheat sheet
- → [API Reference](Reference.md) — Complete API documentation

**Examples:**

- → [Mini Combat sample](../Samples~/Mini%20Combat/README.md) — See concepts in action
- → [Patterns](Patterns.md) — Real-world usage patterns
