# Emit Shorthands (`Emit`, `EmitAt`, `EmitFrom`)

**Quick, readable ways to send messages** without reaching for `MessageHandler.MessageBus` directly.

These shorthands make your code cleaner and more expressive, but they come with important Unity-specific gotchas around GameObject vs Component targeting. Read the [Pitfalls](#unity-targeting-pitfalls) section to avoid silent bugs!

## What You Get

Three simple methods that work on any message:

| Method                 | Purpose                   | Message Type         | Example                                |
| ---------------------- | ------------------------- | -------------------- | -------------------------------------- |
| **`Emit()`**           | Send globally to everyone | `IUntargetedMessage` | `new SceneLoaded(1).Emit();`           |
| **`EmitAt(target)`**   | Send to a specific target | `ITargetedMessage`   | `new Heal(10).EmitAt(playerId);`       |
| **`EmitFrom(source)`** | Broadcast from a source   | `IBroadcastMessage`  | `new TookDamage(5).EmitFrom(enemyId);` |

## Quick Start Examples

```csharp
using DxMessaging.Core; // InstanceId
using DxMessaging.Core.Attributes; // Dx* attributes
using DxMessaging.Core.Extensions; // Emit/EmitAt/EmitFrom

// Define your messages
[DxUntargetedMessage]
[DxAutoConstructor]
public readonly partial struct SceneLoaded { public readonly int buildIndex; }

[DxTargetedMessage]
[DxAutoConstructor]
public readonly partial struct Heal { public readonly int amount; }

[DxBroadcastMessage]
[DxAutoConstructor]
public readonly partial struct TookDamage { public readonly int amount; }

// Emit them
var scene = new SceneLoaded(1);
scene.Emit(); // Global: everyone listening receives this

var heal = new Heal(10);
heal.EmitAt(player); // Targeted: only the player receives this

var damage = new TookDamage(5);
damage.EmitFrom(enemy); // Broadcast: listeners interested in enemy damage receive this
```

## Understanding Each Shorthand

### `Emit()` — Global Broadcast (Untargeted)

**When to use:** Scene-wide notifications that everyone should know about.

```csharp
[DxUntargetedMessage]
[DxAutoConstructor]
public readonly partial struct GamePaused { }

var msg = new GamePaused();
msg.Emit();  // Every listener receives this
```

**Equivalent to:**

```csharp
msg.EmitUntargeted();
// or
MessageHandler.MessageBus.UntargetedBroadcast(ref msg);
```

**Examples:**

- Scene loaded/unloaded
- Game state changes (paused, resumed)
- Settings changed
- Level-up notifications

### `EmitAt(target)` — Targeted Message

**When to use:** Commands or notifications for a specific GameObject or Component.

```csharp
[DxTargetedMessage]
[DxAutoConstructor]
public readonly partial struct Heal { public readonly int amount; }

var heal = new Heal(10);
heal.EmitAt(player); // Only the player receives this
```

**Caution:** `InstanceId` has implicit conversion from both `GameObject` and `Component`. Make sure your target type matches how listeners registered!

**Examples:**

- Heal/damage commands to specific entities
- UI updates for specific panels
- State changes for specific objects

### `EmitFrom(source)` — Broadcast from Source

**When to use:** Announcements where the source identity matters.

```csharp
[DxBroadcastMessage]
[DxAutoConstructor]
public readonly partial struct TookDamage { public readonly int amount; }

var damage = new TookDamage(5);
damage.EmitFrom(enemy);  // Anyone interested in this enemy receives it
```

**Use cases:**

- Combat logging ("Enemy X took damage")
- Analytics ("Track damage from specific boss")
- Achievements ("Listen for player kills")
- Observer patterns (watch specific objects)

## Critical: GameObject vs Component Targeting

**In Unity, GameObject and Component are separate channels.** This is the #1 cause of "handler not firing" bugs!

### Quick Example: The `this` Trap

```csharp
// ❌ COMMON MISTAKE
_ = token.RegisterGameObjectTargeted<Heal>(gameObject, OnHeal);
heal.EmitAt(this);  // Won't work! 'this' is a Component, not a GameObject

// ✅ FIXES
// Option 1: Both use GameObject
heal.EmitAt(gameObject);

// Option 2: Both use Component
_ = token.RegisterComponentTargeted<Heal>(this, OnHeal);
heal.EmitAt(this);
```

**Remember:** In Unity, `this` inside a `MonoBehaviour` is always a Component, not a GameObject!

### 🚀 Global Observers: Listen to ALL Events

**Killer feature:** Unlike traditional event buses, you can listen to ALL targeted or broadcast messages without knowing specific targets/sources!

```csharp
// Listen to ALL heals, regardless of target
_ = token.RegisterTargetedWithoutTargeting<Heal>(OnAnyHeal);
void OnAnyHeal(ref InstanceId target, ref Heal msg)
{
    Debug.Log($"Someone healed {target} for {msg.amount}");
    // You get WHO was healed as a parameter!
}

// Listen to ALL damage, regardless of source
_ = token.RegisterBroadcastWithoutSource<TookDamage>(OnAnyDamage);
void OnAnyDamage(ref InstanceId source, ref TookDamage msg)
{
    Debug.Log($"{source} took {msg.amount} damage");
    // You get WHO took damage as a parameter!
}
```

**Why this is revolutionary:**

- **Classic event bus:** Subscribe to PlayerDamaged, EnemyDamaged, NPCDamaged... (tight coupling)
- **DxMessaging:** Subscribe to ALL damage once (zero coupling, infinite scalability)

**Use cases:** Analytics, debugging, achievements, global UI, combat logs

---

**📖 For the full deep dive on GameObject vs Component targeting and global observers, see [Targeting & Context](TargetingAndContext.md)**

---

## Best Practices

### Use Explicit Helpers (Recommended for Unity)

When clarity matters more than brevity, use the explicit methods:

```csharp
// Clear intent: targeting GameObject
heal.EmitGameObjectTargeted(gameObject);

// Clear intent: targeting Component
heal.EmitComponentTargeted(this);

// Clear intent: broadcasting from GameObject
damage.EmitGameObjectBroadcast(gameObject);

// Clear intent: broadcasting from Component
damage.EmitComponentBroadcast(this);
```

**Pros:** Self-documenting, impossible to mix GameObject/Component, great for code reviews

**Cons:** More verbose

### Use Shorthands (Recommended for Non-Unity)

When you're working with `InstanceId` directly (tests, non-Unity systems):

```csharp
InstanceId targetId = GetTarget();
heal.EmitAt(targetId); // Clean and clear

InstanceId sourceId = GetSource();
damage.EmitFrom(sourceId); // Clean and clear
```

**Pros:** Concise and readable, no GameObject/Component ambiguity

**Cons:** In Unity code, requires explicit attention to GameObject vs Component

## String Message Shorthands

DxMessaging includes three built-in string message types for rapid prototyping:

| String Type   | Message Class          | Shorthand                 | Use Case                            |
| ------------- | ---------------------- | ------------------------- | ----------------------------------- |
| **Global**    | `GlobalStringMessage`  | `"text".Emit()`           | Debug notifications                 |
| **Targeted**  | `StringMessage`        | `"text".EmitAt(target)`   | Commands to specific objects        |
| **Broadcast** | `SourcedStringMessage` | `"text".EmitFrom(source)` | Announcements from specific objects |

### Examples

```csharp
// Global notification
"Game Saved".Emit();

// Targeted message to GameObject
"Hello".EmitAt(gameObject);

// Or use explicit helpers
"Hello".EmitGameObjectTargeted(gameObject); // Clearer!

// Broadcast from GameObject
"Died".EmitFrom(gameObject);

// Or use explicit helpers
"Died".EmitGameObjectBroadcast(gameObject); // Clearer!
```

### When to Use String Messages

**Good for:**

- Rapid prototyping
- Debug logging
- Test utilities
- Tool scripts

**Not good for:**

- Production gameplay code (use typed messages instead)
- Performance-critical paths
- Public APIs

See [String Messages](StringMessages.md) for more details.

## Advanced: Optional Bus Parameter

All shorthands accept an optional `IMessageBus` parameter:

```csharp
var localBus = new MessageBus();

msg.Emit(localBus);              // Emit to specific bus
msg.EmitAt(target, localBus);    // Target on specific bus
msg.EmitFrom(source, localBus);  // Broadcast on specific bus
```

**When to use:**

- Testing with isolated buses
- Subsystems with their own message domains
- Advanced architecture patterns

**Default:** If you don't provide a bus, `MessageHandler.MessageBus` (the global bus) is used.

## Unity Targeting Pitfalls

Unity distinguishes between GameObjects and Components. Targeted and Broadcast messages must use the same context for both registration and emission.

- Registering on a GameObject requires emitting to a GameObject target
- Registering on a Component requires emitting to a Component target
- Prefer explicit helpers to avoid confusion:
  - `EmitGameObjectTargeted` / `EmitComponentTargeted`
  - `EmitGameObjectBroadcast` / `EmitComponentBroadcast`

If a handler isn’t firing, first suspect a GameObject vs Component mismatch. See the Troubleshooting checklist below.

## Troubleshooting

### "My handler isn't firing!"

**Check these in order:**

1. **GameObject vs Component mismatch?**

   - Did you register for a GameObject but emit to a Component (or vice versa)?
   - Use explicit helpers (`EmitGameObjectTargeted` vs `EmitComponentTargeted`) to eliminate this issue

1. **Is the handler enabled?**

   - Check that `token.Enable()` was called (usually in `OnEnable`)
   - Verify the component is active

1. **Correct message type?**

   - Untargeted messages use `Emit()`
   - Targeted messages use `EmitAt(target)`
   - Broadcast messages use `EmitFrom(source)`

1. **Registration succeeded?**
   - Check that you assigned the return value (even `_`) to ensure registration happened
   - Verify registration happened before the emission

### "How do I debug message flow?"

Use the built-in diagnostics:

```csharp
// Add to any Unity component
using DxMessaging.Unity;

public class DebugListener : MessagingComponent
{
    // Inspector will show recent emissions and registrations
}
```

See [Diagnostics](Diagnostics.md) for more debugging tools.

## Comparison: Shorthands vs Explicit Helpers

| Scenario                      | Shorthand                  | Explicit Helper                           |
| ----------------------------- | -------------------------- | ----------------------------------------- |
| **Untargeted/Global**         | `msg.Emit()`               | `msg.EmitUntargeted()`                    |
| **Targeted to GameObject**    | `msg.EmitAt(gameObject)`   | `msg.EmitGameObjectTargeted(gameObject)`  |
| **Targeted to Component**     | `msg.EmitAt(component)`    | `msg.EmitComponentTargeted(component)`    |
| **Broadcast from GameObject** | `msg.EmitFrom(gameObject)` | `msg.EmitGameObjectBroadcast(gameObject)` |
| **Broadcast from Component**  | `msg.EmitFrom(component)`  | `msg.EmitComponentBroadcast(component)`   |
| **Non-Unity InstanceId**      | `msg.EmitAt(id)`           | `msg.EmitTargeted(id)`                    |

**Our recommendation:**

- In Unity gameplay code: Use **explicit helpers** for clarity and safety
- In tests and non-Unity code: Use **shorthands** for brevity
- In examples and documentation: Use **shorthands** for readability

## Complete Example

Here's a full example showing all three shorthand types:

```csharp
using DxMessaging.Core;
using DxMessaging.Core.Attributes;
using DxMessaging.Core.Extensions;
using UnityEngine;

// Message definitions
[DxUntargetedMessage]
[DxAutoConstructor]
public readonly partial struct WaveStarted { public readonly int waveNumber; }

[DxTargetedMessage]
[DxAutoConstructor]
public readonly partial struct SpawnEnemy { public readonly string enemyType; }

[DxBroadcastMessage]
[DxAutoConstructor]
public readonly partial struct EnemyDied { public readonly int score; }

// Producer
public class GameManager : MonoBehaviour
{
    void StartWave(int wave)
    {
        // Global broadcast: everyone should know
        var waveMsg = new WaveStarted(wave);
        waveMsg.Emit();

        // Targeted: tell specific spawner what to spawn
        var spawnMsg = new SpawnEnemy("Goblin");
        spawnMsg.EmitGameObjectTargeted(spawnerObject);
    }
}

// Consumer
public class Enemy : MonoBehaviour
{
    void Die()
    {
        // Broadcast from this enemy: anyone watching can react
        var deathMsg = new EnemyDied(100);
        deathMsg.EmitGameObjectBroadcast(gameObject);
    }
}

// Listeners
public class UIManager : MessageAwareComponent
{
    protected override void RegisterMessageHandlers()
    {
        _ = Token.RegisterUntargeted<WaveStarted>(OnWaveStarted);
    }

    void OnWaveStarted(ref WaveStarted msg)
    {
        Debug.Log($"Wave {msg.waveNumber} started!");
    }
}

public class AchievementTracker : MessageAwareComponent
{
    protected override void RegisterMessageHandlers()
    {
        // Listen to ALL enemy deaths (no specific source)
        _ = Token.RegisterBroadcastWithoutSource<EnemyDied>(OnAnyEnemyDied);
    }

    void OnAnyEnemyDied(ref InstanceId source, ref EnemyDied msg)
    {
        Debug.Log($"Enemy {source} died for {msg.score} points");
    }
}
```

## See Also

- **[Quick Reference](QuickReference.md)** — API cheat sheet for all emit methods
- **[Message Types](MessageTypes.md)** — Understand Untargeted, Targeted, and Broadcast messages
- **[Targeting & Context](TargetingAndContext.md)** — Deep dive into GameObject vs Component
- **[String Messages](StringMessages.md)** — More about string message helpers
- **[Diagnostics](Diagnostics.md)** — Debugging tools and Inspector integration
