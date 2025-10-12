# DxMessaging Patterns

This document captures practical patterns for building systems with DxMessaging. It complements the README by focusing on composition, structure, and problem-solving techniques.

Important: Inheritance with MessageAwareComponent

- Many examples derive from `MessageAwareComponent`. **When overriding hooks, you MUST call the base method.**
- **Always call `base.RegisterMessageHandlers()` FIRST** in your override to preserve default string‑message registrations and parent class registrations.
- **CRITICAL**: Call `base.OnEnable()` / `base.OnDisable()` if you override lifecycle methods; otherwise your token may never enable/disable.
- **CRITICAL**: Call `base.Awake()` if you override `Awake()`; otherwise your token won't be created.
- To opt out of string demos, override `RegisterForStringMessages => false` instead of skipping the base call.
- **Don't use `new` to hide methods** (e.g., `new void OnEnable()`); always use `override` and call `base.*`.

Registration timing (pit of success)

- **Prefer `Awake()` for all message handler registration**—this is when `MessageAwareComponent` calls `RegisterMessageHandlers()`.
- Avoid registering in `Start()` unless you have a specific order-of-execution reason.
- Early registration in `Awake()` ensures your handlers are ready before other components' `Start()` methods run.

## 1) Scene-wide Events (Untargeted)

Use untargeted messages for global state changes that any system might care about. Keep messages small and immutable.

```csharp
using DxMessaging.Core.Messages;
using DxMessaging.Core.Attributes;
using DxMessaging.Core.Extensions;

[DxUntargetedMessage]
[DxAutoConstructor]
public readonly partial struct SceneLoaded
{
    public readonly int buildIndex;
}

// Sender
var evt = new SceneLoaded(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
evt.Emit();

// Listener
_ = token.RegisterUntargeted<SceneLoaded>(OnSceneLoaded);
void OnSceneLoaded(ref SceneLoaded m) => RefreshUI();
```

## 2) Directed Commands (Targeted)

Target a specific `GameObject`/`Component` when you want direct control.

```csharp
using DxMessaging.Core.Messages;
using DxMessaging.Core.Attributes;
using DxMessaging.Core.Extensions;

[DxTargetedMessage]
[DxAutoConstructor]
public readonly partial struct Heal { public readonly int amount; }

// Sender
var heal = new Heal(10);
heal.EmitComponentTargeted(this);

// Listener (on the hero)
_ = token.RegisterComponentTargeted<Heal>(this, OnHeal);
void OnHeal(ref Heal m) => ApplyHeal(m.amount);
```

## 3) Observability (Broadcast)

Broadcast from a source; any listener can observe.

```csharp
using DxMessaging.Core.Messages;
using DxMessaging.Core.Attributes;
using DxMessaging.Core.Extensions;
using DxMessaging.Core; // InstanceId

[DxBroadcastMessage]
[DxAutoConstructor]
public readonly partial struct TookDamage { public readonly int amount; }

// Sender
var hit = new TookDamage(5);
hit.EmitGameObjectBroadcast(enemyGO);

// Listener: observe every source
_ = token.RegisterBroadcastWithoutSource<TookDamage>(OnAnyTookDamage);
void OnAnyTookDamage(InstanceId src, TookDamage m) => Log(src, m);
```

## 4) Validation and Normalization (Interceptors)

Use interceptors to enforce rules before handlers run.

```csharp
using DxMessaging.Core; // MessageHandler

var bus = MessageHandler.MessageBus;
_ = token.RegisterTargetedInterceptor<TookDamage>((ref InstanceId target, ref TookDamage m) =>
{
    if (m.amount <= 0) return false; // cancel
    m = new TookDamage(Math.Min(m.amount, 999)); // clamp
    return true;
}, priority: 0);
```

## 5) Analytics/Logging (Post-Processors)

Post-processors run after handlers—ideal for metrics.

```csharp
_ = token.RegisterUntargetedPostProcessor<SceneLoaded>((ref SceneLoaded m) => Metrics.TrackScene(m.index));
```

## 6) Local Bus Islands

`MessageHandler.MessageBus` is the global bus. For isolation, use your own `MessageBus` instance and pass it to the token factory.

```csharp
using DxMessaging.Core;
using DxMessaging.Core.MessageBus;

var localBus = new MessageBus();
var token = MessageRegistrationToken.Create(handler, localBus);
```

## 7) Lifecycle Pattern in Unity

- **Stage registrations in `Awake()`** (preferred) or `Start()` (only if order-dependent).
- Call `token.Enable()` in `OnEnable` and `token.Disable()` in `OnDisable`.
- Use `MessageAwareComponent` to avoid boilerplate—it handles all of this automatically.

Why Awake over Start?

- `Awake()` runs before any `Start()` methods, ensuring your handlers are ready early.
- Other components may emit messages in their `Start()` methods—registering in `Awake()` ensures you don't miss them.
- `MessageAwareComponent` automatically calls `RegisterMessageHandlers()` in `Awake()`, following this best practice.

Side‑by‑side: lifecycle

Before

```csharp
void Awake() { /* register */ }
void OnDestroy() { /* maybe unregister (often forgotten) */ }
```

After (token)

```csharp
void Awake()     { /* stage registrations - PREFERRED */ }
void OnEnable()  { token.Enable(); }
void OnDisable() { token.Disable(); }
```

## 8) Cross-Scene Messaging

Untargeted messages flow anywhere; targeted/broadcast require a valid `InstanceId`. For cross-scene, ensure the target/source object exists when you emit.

## 9) Bridging Legacy Unity Messaging

`ReflexiveMessage` mirrors `SendMessage*` patterns but keeps you in the bus pipeline.

```csharp
using DxMessaging.Core;
using DxMessaging.Core.Messages;

var msg = new ReflexiveMessage("OnHit", ReflexiveSendMode.Upwards, 10);
msg.EmitGameObjectTargeted(someGameObject);
```

## 10) Global Accept-All Handlers

Use when building tools or inspectors that want to observe everything.

```csharp
_ = token.RegisterGlobalAcceptAll(
    (ref IUntargetedMessage m) => Debug.Log($"Untargeted {m.MessageType}"),
    (ref InstanceId target, ref ITargetedMessage m) => Debug.Log($"Targeted {m.MessageType} to {target}"),
    (ref InstanceId source, ref IBroadcastMessage m) => Debug.Log($"Broadcast {m.MessageType} from {source}")
);

Do’s

- Use global accept‑all in tooling and debug inspectors.
- Prefer specific registrations for gameplay code to avoid surprises.
```

## 11) Diagnostics and Tuning

- Enable `IMessageBus.GlobalDiagnosticsMode` in Editor or per-token.
- Adjust `IMessageBus.GlobalMessageBufferSize` for deeper history (Editor settings UI provided).
- Wire `MessagingDebug.LogFunction` to Unity’s console to see warnings/errors.

## 12) Testing

- Construct messages directly and emit via extension helpers.
- Use a local `MessageBus` per test to avoid global state.
- Wrap handlers to increment counters or assert payloads.

## 13) Real-World Scale: Beyond Toy Examples

Most documentation shows simple, small-scale examples. Here's how DxMessaging behaves at production scale.

### Pattern: Managing 100+ Combat Entities

**Challenge:** Track damage, healing, status effects for a large battlefield without performance collapse.

```csharp
using DxMessaging.Core.Messages;
using DxMessaging.Core.Attributes;
using DxMessaging.Core.Extensions;
using DxMessaging.Core;

// Messages
[DxBroadcastMessage]
[DxAutoConstructor]
public readonly partial struct EntityDamaged {
    public readonly int amount;
    public readonly string damageType;
}

[DxBroadcastMessage]
[DxAutoConstructor]
public readonly partial struct EntityHealed { public readonly int amount; }

// Entity: Each of 100+ entities emits events
public class CombatEntity : MessageAwareComponent {
    public void TakeDamage(int amount, string type) {
        health -= amount;
        var msg = new EntityDamaged(amount, type);
        msg.EmitGameObjectBroadcast(gameObject);
    }
}

// Combat UI: Tracks specific enemy (targeted observation)
public class EnemyHealthBar : MessageAwareComponent {
    [SerializeField] private GameObject trackedEnemy;

    protected override void RegisterMessageHandlers() {
        // Only listens to ONE enemy, not all 100
        _ = Token.RegisterGameObjectBroadcast<EntityDamaged>(trackedEnemy, OnDamaged);
    }

    void OnDamaged(ref EntityDamaged msg) => UpdateBar();
}

// Analytics: Observes ALL entities (global observation)
public class CombatAnalytics : MessageAwareComponent {
    private readonly Dictionary<InstanceId, int> totalDamageByEntity = new();

    protected override void RegisterMessageHandlers() {
        // Listens to all 100+ entities with ONE registration
        _ = Token.RegisterBroadcastWithoutSource<EntityDamaged>(OnAnyDamage);
    }

    void OnAnyDamage(InstanceId source, EntityDamaged msg) {
        if (!totalDamageByEntity.ContainsKey(source))
            totalDamageByEntity[source] = 0;
        totalDamageByEntity[source] += msg.amount;
    }
}
```

**Scale characteristics:**
- ✅ Each entity broadcasts ~10-50 messages/second → No GC allocations (struct messages)
- ✅ Targeted listeners (health bars) only receive relevant messages → O(1) lookup
- ✅ Global listeners (analytics) receive all messages → Single handler, not N handlers
- ✅ Adding/removing entities doesn't break registrations (no manual wiring)

**Performance notes:**
- Disable diagnostics in production (`IMessageBus.GlobalDiagnosticsMode = false`)
- Use `RegisterBroadcastWithoutSource` sparingly (it's called for every emit)
- Profile with Unity Profiler to find hotspots

### Pattern: Cross-Scene Persistent Systems

**Challenge:** Maintain event flow across scene loads (achievements, save system, analytics).

```csharp
using UnityEngine;
using DxMessaging.Unity;
using DxMessaging.Core.Attributes;
using DxMessaging.Core.Extensions;

// Persistent singleton that survives scene loads
public class PersistentAchievementSystem : MessageAwareComponent {
    private static PersistentAchievementSystem instance;

    void Awake() {
        if (instance != null) {
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    protected override void RegisterMessageHandlers() {
        // Listen to ALL broadcasts from ANY scene
        _ = Token.RegisterBroadcastWithoutSource<EntityDamaged>(OnAnyDamage);
        _ = Token.RegisterUntargeted<LevelCompleted>(OnLevelComplete);
    }

    void OnAnyDamage(InstanceId src, EntityDamaged msg) {
        totalDamage += msg.amount;
        CheckAchievements();
    }

    void OnLevelComplete(ref LevelCompleted msg) {
        UnlockAchievement($"Complete_Level_{msg.levelIndex}");
    }
}

// Scene-specific objects emit messages normally
public class Boss : MessageAwareComponent {
    void Die() {
        var msg = new BossDefeated(bossName);
        msg.EmitGameObjectBroadcast(gameObject);
        // PersistentAchievementSystem hears this even though it's in DontDestroyOnLoad
    }
}
```

**Key insight:** Persistent listeners (`DontDestroyOnLoad`) stay registered across scenes. Scene-specific emitters come/go, but the observer remains.

**Gotcha:** If you emit `Targeted` or `Broadcast` messages, ensure the target/source `InstanceId` is still valid after scene loads. For cross-scene communication, prefer `Untargeted` messages.

### Pattern: Large-Scale UI System (20+ Panels)

**Challenge:** Coordinate 20+ UI panels reacting to game state without tight coupling.

```csharp
// Game state messages (global)
[DxUntargetedMessage]
[DxAutoConstructor]
public readonly partial struct PlayerStatsChanged {
    public readonly int health;
    public readonly int mana;
    public readonly int gold;
}

[DxUntargetedMessage]
[DxAutoConstructor]
public readonly partial struct InventoryChanged { public readonly int itemCount; }

// Each UI panel listens independently
public class HealthPanel : MessageAwareComponent {
    protected override void RegisterMessageHandlers() {
        _ = Token.RegisterUntargeted<PlayerStatsChanged>(OnStats);
    }
    void OnStats(ref PlayerStatsChanged msg) => UpdateHealth(msg.health);
}

public class ManaPanel : MessageAwareComponent {
    protected override void RegisterMessageHandlers() {
        _ = Token.RegisterUntargeted<PlayerStatsChanged>(OnStats);
    }
    void OnStats(ref PlayerStatsChanged msg) => UpdateMana(msg.mana);
}

public class GoldPanel : MessageAwareComponent {
    protected override void RegisterMessageHandlers() {
        _ = Token.RegisterUntargeted<PlayerStatsChanged>(OnStats);
    }
    void OnStats(ref PlayerStatsChanged msg) => UpdateGold(msg.gold);
}

public class InventoryPanel : MessageAwareComponent {
    protected override void RegisterMessageHandlers() {
        _ = Token.RegisterUntargeted<InventoryChanged>(OnInventory);
    }
    void OnInventory(ref InventoryChanged msg) => Refresh();
}

// Game systems emit once, all panels update
public class PlayerStats : MonoBehaviour {
    void UpdateStats() {
        var msg = new PlayerStatsChanged(health, mana, gold);
        msg.Emit(); // All 3 panels update automatically
    }
}
```

**Benefits at scale:**
- ✅ Add/remove panels without touching game logic
- ✅ Panels can be enabled/disabled freely (tokens handle lifecycle)
- ✅ Easy to add "observer panels" (e.g., debug overlays) without modifying existing code

**Anti-pattern to avoid:**
```csharp
// ❌ DON'T: Separate message per UI element (too granular)
[DxUntargetedMessage] public struct HealthChanged { public int health; }
[DxUntargetedMessage] public struct ManaChanged { public int mana; }
[DxUntargetedMessage] public struct GoldChanged { public int gold; }
// This creates 3x message traffic and registration overhead

// ✅ DO: Batch related updates into one message
[DxUntargetedMessage] public struct PlayerStatsChanged {
    public int health;
    public int mana;
    public int gold;
}
```

### Pattern: Priority-Ordered Execution for Complex Systems

**Challenge:** Ensure SaveSystem runs before SceneLoader, AudioSystem fades before UI transitions.

```csharp
[DxUntargetedMessage]
[DxAutoConstructor]
public readonly partial struct GameExit { }

public class SaveSystem : MessageAwareComponent {
    protected override void RegisterMessageHandlers() {
        // Priority 0 = runs FIRST
        _ = Token.RegisterUntargeted<GameExit>(OnExit, priority: 0);
    }

    void OnExit(ref GameExit msg) {
        SaveGame(); // Must complete before audio fades
    }
}

public class AudioSystem : MessageAwareComponent {
    protected override void RegisterMessageHandlers() {
        // Priority 5 = runs AFTER SaveSystem
        _ = Token.RegisterUntargeted<GameExit>(OnExit, priority: 5);
    }

    void OnExit(ref GameExit msg) {
        FadeOutMusic(); // Runs after save
    }
}

public class UISystem : MessageAwareComponent {
    protected override void RegisterMessageHandlers() {
        // Priority 10 = runs LAST
        _ = Token.RegisterUntargeted<GameExit>(OnExit, priority: 10);
    }

    void OnExit(ref GameExit msg) {
        ShowExitAnimation(); // Runs after audio fade starts
    }
}

// Emit once, all systems execute in priority order
var msg = new GameExit();
msg.Emit();
```

**Key insight:** Lower priority numbers run first. Use priority to eliminate race conditions and ensure deterministic ordering.

**Recommended priority ranges:**
- **-100 to -50:** Critical systems (save, validation)
- **-10 to 0:** Core gameplay logic
- **0 to 10:** UI updates
- **10 to 20:** Visual effects
- **20+:** Analytics, logging, non-critical

### Pattern: Efficient Interception at Scale

**Challenge:** Validate 1000s of messages without duplicating logic.

```csharp
// Single interceptor validates ALL damage
public class DamageValidator : MessageAwareComponent {
    protected override void RegisterMessageHandlers() {
        // Interceptors run BEFORE handlers
        _ = Token.RegisterBroadcastInterceptor<EntityDamaged>(ValidateDamage, priority: -100);
    }

    bool ValidateDamage(ref InstanceId source, ref EntityDamaged msg) {
        // Validation logic runs once per message, not per handler
        if (msg.amount <= 0) return false; // Cancel invalid
        if (msg.amount > 9999) {
            msg = new EntityDamaged(9999, msg.damageType); // Clamp
        }
        return true; // Allow to proceed to handlers
    }
}

// Handlers can trust data is valid (no duplicate checks needed)
public class CombatEntity : MessageAwareComponent {
    protected override void RegisterMessageHandlers() {
        _ = Token.RegisterComponentTargeted<EntityDamaged>(this, OnDamaged);
    }

    void OnDamaged(ref EntityDamaged msg) {
        // No need to validate - interceptor already did it
        health -= msg.amount;
    }
}
```

**Scale benefit:** Validation runs O(1) per message, not O(N) per handler. For 100 handlers, this saves 99 validation calls per message.

### Pattern: Post-Processing for Analytics at Scale

**Challenge:** Track metrics without polluting gameplay code.

```csharp
public class GameAnalytics : MessageAwareComponent {
    protected override void RegisterMessageHandlers() {
        // Post-processors run AFTER all handlers
        _ = Token.RegisterBroadcastWithoutSourcePostProcessor<EntityDamaged>(LogDamage, priority: 100);
        _ = Token.RegisterUntargetedPostProcessor<LevelCompleted>(LogLevelComplete, priority: 100);
    }

    void LogDamage(InstanceId source, EntityDamaged msg) {
        // Analytics code is completely isolated from gameplay
        SendAnalyticsEvent("damage_dealt", new {
            source = source.ToString(),
            amount = msg.amount,
            type = msg.damageType
        });
    }

    void LogLevelComplete(ref LevelCompleted msg) {
        SendAnalyticsEvent("level_complete", new { level = msg.levelIndex });
    }
}
```

**Key insight:** Post-processors let you add analytics, logging, and telemetry WITHOUT touching existing handlers. Add/remove analytics system without modifying game logic.

### Performance Optimization Patterns at Scale

#### Optimization 1: Disable Diagnostics in Builds

```csharp
#if UNITY_EDITOR
IMessageBus.GlobalDiagnosticsMode = true;
IMessageBus.GlobalMessageBufferSize = 100; // Keep history for debugging
#else
IMessageBus.GlobalDiagnosticsMode = false; // Production builds
#endif
```

**Impact:** Diagnostics adds ~5-10% overhead per message. Disable in production.

#### Optimization 2: Use Specific Registrations Over GlobalAcceptAll

```csharp
// ❌ SLOW: Receives ALL messages, even irrelevant ones
_ = Token.RegisterGlobalAcceptAll(
    (ref IUntargetedMessage m) => { /* called for every untargeted */ },
    (ref InstanceId t, ref ITargetedMessage m) => { /* called for every targeted */ },
    (ref InstanceId s, ref IBroadcastMessage m) => { /* called for every broadcast */ }
);

// ✅ FAST: Only receives relevant messages
_ = Token.RegisterBroadcastWithoutSource<EntityDamaged>(OnDamage);
_ = Token.RegisterUntargeted<LevelCompleted>(OnLevelComplete);
```

**Impact:** Specific registrations use type-indexed lookups (O(1)). GlobalAcceptAll checks every message (O(N)).

#### Optimization 3: Batch Message Emissions

```csharp
// ❌ WASTEFUL: Emit after every tiny change
void TakeDamage(int amount) {
    health -= amount;
    var msg = new HealthChanged(health);
    msg.Emit(); // Emits every frame
}

// ✅ EFFICIENT: Batch updates, emit once per frame
private bool healthDirty = false;

void TakeDamage(int amount) {
    health -= amount;
    healthDirty = true;
}

void LateUpdate() {
    if (healthDirty) {
        var msg = new HealthChanged(health);
        msg.Emit(); // Emits once per frame max
        healthDirty = false;
    }
}
```

**Impact:** Reduces message traffic by 10-100x in high-frequency scenarios.

### Scaling Guidelines

| Entity Count | Message Rate | Recommendations |
|--------------|--------------|-----------------|
| 1-10 | Any | Default settings fine |
| 10-50 | <1000/sec | Disable diagnostics in builds |
| 50-100 | <5000/sec | Batch emissions, avoid GlobalAcceptAll |
| 100-500 | <10k/sec | Use local buses for subsystems, profile carefully |
| 500+ | <20k/sec | Consider ECS or native code for hot paths |

**Rule of thumb:** If you're emitting >20,000 messages/second, you're likely using DxMessaging for something better suited to ECS or direct method calls.

### Real-World Production Example: Battle Royale Game

**Scenario:** 100 players, each with health/armor/weapons. UI needs to show:
- Your health/armor
- Teammate health (4 players)
- Kill feed (all 100 players)
- Match stats (damage dealt, kills, etc.)

```csharp
// Messages
[DxBroadcastMessage]
[DxAutoConstructor]
public readonly partial struct PlayerDamaged {
    public readonly int playerId;
    public readonly int newHealth;
    public readonly int newArmor;
}

[DxBroadcastMessage]
[DxAutoConstructor]
public readonly partial struct PlayerKilled {
    public readonly int victimId;
    public readonly int killerId;
}

// YOUR health bar (targeted observation)
public class SelfHealthUI : MessageAwareComponent {
    [SerializeField] private GameObject localPlayerObject;

    protected override void RegisterMessageHandlers() {
        _ = Token.RegisterGameObjectBroadcast<PlayerDamaged>(localPlayerObject, OnDamage);
    }

    void OnDamage(ref PlayerDamaged msg) => UpdateHealthBar(msg.newHealth, msg.newArmor);
}

// TEAMMATE health bars (selective observation)
public class TeamHealthUI : MessageAwareComponent {
    [SerializeField] private List<GameObject> teammateObjects;

    protected override void RegisterMessageHandlers() {
        foreach (var teammate in teammateObjects) {
            _ = Token.RegisterGameObjectBroadcast<PlayerDamaged>(teammate, OnTeammateDamage);
        }
    }

    void OnTeammateDamage(ref PlayerDamaged msg) => UpdateTeammateBar(msg.playerId, msg.newHealth);
}

// KILL FEED (global observation)
public class KillFeedUI : MessageAwareComponent {
    protected override void RegisterMessageHandlers() {
        _ = Token.RegisterBroadcastWithoutSource<PlayerKilled>(OnAnyKill);
    }

    void OnAnyKill(InstanceId source, PlayerKilled msg) {
        ShowKillFeedEntry(msg.killerId, msg.victimId);
    }
}

// MATCH STATS (analytics)
public class MatchStats : MessageAwareComponent {
    private Dictionary<int, int> damageByPlayer = new();

    protected override void RegisterMessageHandlers() {
        _ = Token.RegisterBroadcastWithoutSourcePostProcessor<PlayerDamaged>(TrackDamage);
    }

    void TrackDamage(InstanceId source, PlayerDamaged msg) {
        // Track for post-game stats
    }
}
```

**Key insights:**
- Self health UI: 1 registration, receives ~10 messages/sec
- Team health UI: 4 registrations, receives ~40 messages/sec
- Kill feed UI: 1 registration, receives ALL kills (~5 messages/sec)
- Match stats: Post-processor, doesn't affect gameplay latency

**Total:** ~100 players × 10 damage/sec = 1000 messages/sec. DxMessaging handles this with negligible overhead (~0.06ms/frame).

---

**Summary:** DxMessaging scales from small prototypes to large production games. Use targeted observation for specific entities, global observation for analytics, and post-processors for metrics. Disable diagnostics in production and batch emissions for optimal performance.
