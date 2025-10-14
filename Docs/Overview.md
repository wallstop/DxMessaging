# Overview

[← Back to Index](Index.md) | [Getting Started](GettingStarted.md) | [Quick Start](QuickStart.md) | [Visual Guide](VisualGuide.md)

---

DxMessaging is a high-performance, type-safe messaging system for Unity that **eliminates the three biggest pain points** of traditional event systems:

1. **Memory leaks** from forgotten unsubscribes → Automatic lifecycle management
1. **Tight coupling** creating refactoring nightmares → Full decoupling with no direct references
1. **Debugging black holes** ("what fired when?") → Built-in Inspector diagnostics

## What Problems Does It Solve?

### For Beginners: "I'm calling methods manually everywhere"

#### Your code probably looks like this

```csharp
public class Player : MonoBehaviour {
    public HealthBar healthBar;
    public AudioManager audio;
    public AchievementSystem achievements;

    void TakeDamage(int amount) {
        health -= amount;
        healthBar.UpdateHealth(health);  // Manual call
        audio.PlayDamageSound();         // Manual call
        achievements.CheckDamage(amount); // Manual call
        // Add analytics? More manual calls...
    }
}
```

**Every new system = another SerializeField + another manual call.** It's exhausting and brittle.

##### DxMessaging fixes this

```csharp
void TakeDamage(int amount) {
    health -= amount;
    new TookDamage(amount).EmitComponentBroadcast(this);
    // Done! Everything else reacts automatically.
}
```

### For Intermediate Devs: "I use C# events but they leak"

#### You know this pain

```csharp
void OnEnable() { GameManager.OnScoreChanged += UpdateUI; }
void OnDisable() { /* Forgot this? 💀 LEAK! */ }
```

**DxMessaging makes leaks impossible** - automatic cleanup when components die.

### For Advanced Devs: "I need observability and control"

- ✅ See message history in Inspector (timestamps, payloads, call counts)
- ✅ Priority-based execution (no more race conditions)
- ✅ Interceptors (validate/normalize before handlers)
- ✅ Global observers (track ALL instances of a message type)
- ✅ Local bus islands (isolated testing, zero global state)

## What It Solves (Technical)

- **Decoupling without references** - producers/consumers never know about each other
- **Predictable lifecycle** - explicit tokens tied to Unity component lifecycles
- **Performance** - struct messages passed by-ref, zero allocations, zero boxing
- **Observability** - interceptors, post-processors, diagnostics, registration logs
- **Scalable taxonomy** - three message types (Untargeted/Targeted/Broadcast) cover 99% of use cases

## When to Consider DxMessaging

### Use it when

- You have 3+ systems that need to communicate
- You've debugged memory leaks from forgotten unsubscribes
- You're tired of UI depending on 15 different gameplay systems
- You want to see "what fired when" without setting 50 breakpoints
- You're building for the long term (months/years, not days)

#### Skip it when

- Game jam prototype (<1 week)
- Tiny project (<1000 lines)
- Single-system communication (just call the method directly)
- You need synchronous return values (DxMessaging is fire-and-forget)

Core ideas

- Message categories
  - Untargeted: Global notifications (e.g., settings changed).
  - Targeted: Sent to one specific target (e.g., Heal(10) to Player).
  - Broadcast: Emitted from a source; anyone can observe (e.g., TookDamage from Enemy).
- Ordering: Lower priority runs earlier; same priority uses registration order.
- Pipeline: Interceptors → Handlers → Post‑Processors, with diagnostics optionally enabled.
- Unity integration: `MessagingComponent` and `MessageAwareComponent` manage lifecycles cleanly.

## Killer Features (What Makes It Special)

### 🚀 Global Observers: The Unique Advantage

#### Traditional event systems force you to do this

```csharp
// Subscribe to each entity type separately
PlayerEvents.OnDamaged += TrackPlayerDamage;
EnemyEvents.OnDamaged += TrackEnemyDamage;
NPCEvents.OnDamaged += TrackNPCDamage;
// Add a new entity type? More subscriptions...
```

##### DxMessaging lets you do this

```csharp
// Subscribe ONCE to ALL damage, regardless of source
_ = Token.RegisterBroadcastWithoutSource<TookDamage>(
    (InstanceId source, TookDamage msg) => {
        Analytics.Log($"{source} took {msg.amount} damage");
    }
);
```

**Real-world impact:** Build achievement systems, combat logs, and analytics that work with ANY entity - even ones that don't exist yet.

### Other Killer Features

- **Priority-based ordering** - eliminate race conditions, control execution flow explicitly
- **Interceptor pipeline** - validate/normalize messages BEFORE handlers run (one validation, all handlers protected)
- **Local bus islands** - isolated testing with zero global state contamination
- **Zero-allocation design** - struct messages passed by-ref, no boxing, no GC spikes
- **Auto-constructor generation** - `[DxAutoConstructor]` eliminates boilerplate while keeping type safety
- **Unity-first helpers** - `EmitGameObjectTargeted()`, `EmitComponentBroadcast()` feel natural
- **Inspector diagnostics** - see message history, registrations, call counts in real-time

---

## Related Documentation

### Start Here

- → [Visual Guide](VisualGuide.md) (5 min) — Beginner-friendly introduction
- → [Getting Started](GettingStarted.md) (10 min) — Complete guide
- → [Quick Start](QuickStart.md) (5 min) — Working example

#### Go Deeper

- → [Message Types](MessageTypes.md) — When to use Untargeted/Targeted/Broadcast
- → [Comparisons](Comparisons.md) — DxMessaging vs alternatives
- → [Design & Architecture](DesignAndArchitecture.md) — How it works

##### Install & Setup

- → [Install](Install.md) — Installation guide
- → [Compatibility](Compatibility.md) — Unity versions
