# Getting Started with DxMessaging

Welcome! This guide will take you from zero to productive with DxMessaging in about 10 minutes. By the end, you'll understand what DxMessaging is, why it's powerful, and how to use it effectively in your Unity projects.

## What is DxMessaging?

**DxMessaging is a high-performance, type-safe messaging system** that replaces sprawling C# events, brittle UnityEvents, and global static event buses with a clean, observable, and predictable communication pattern.

Think of it as **the event system you wish Unity had built-in** — one that:
- Decouples systems without manual subscribe/unsubscribe headaches
- Provides predictable execution order
- Gives you powerful debugging and observability tools
- Scales from simple prototypes to complex production codebases

## Why DxMessaging? (The "Aha!" Moment)

### The Problem

If you've built anything non-trivial in Unity, you've likely experienced:

**With C# Events:**
```csharp
// Tight coupling - UI needs reference to every system
public class GameUI : MonoBehaviour
{
    [SerializeField] private Player player;
    [SerializeField] private EnemySpawner spawner;
    [SerializeField] private InventorySystem inventory;

    void Awake() {
        player.OnHealthChanged += UpdateHealthBar;
        spawner.OnWaveStarted += ShowWaveUI;
        inventory.OnItemAdded += RefreshInventory;
    }

    void OnDestroy() {
        // Easy to forget! Memory leaks incoming...
        player.OnHealthChanged -= UpdateHealthBar;
        spawner.OnWaveStarted -= ShowWaveUI;
        inventory.OnItemAdded -= RefreshInventory;
    }
}
```

Problems:
- ❌ Manual subscribe/unsubscribe (easy to forget = memory leaks)
- ❌ Tight coupling (UI knows about every system)
- ❌ No execution order control
- ❌ Hard to debug ("which event fired when?")

**With Global Event Buses:**
```csharp
public static class EventBus {
    public static event Action<int> OnDamage;
    public static event Action<string> OnMessage;
    // ... 50+ more events ...
}
```

Problems:
- ❌ Everything is global (no context: who sent? who received?)
- ❌ Still manual lifecycle management
- ❌ No type safety (easy to mix up similar events)
- ❌ Can't intercept or validate messages

### The DxMessaging Solution

```csharp
using DxMessaging.Core.Attributes;
using DxMessaging.Core.Extensions;
using DxMessaging.Unity;

// 1. Define messages (clear, typed, immutable)
[DxTargetedMessage]
[DxAutoConstructor]
public readonly partial struct Heal { public readonly int amount; }

[DxBroadcastMessage]
[DxAutoConstructor]
public readonly partial struct TookDamage { public readonly int amount; }

// 2. Listen (automatic lifecycle, no unsubscribe needed)
public class GameUI : MessageAwareComponent
{
    protected override void RegisterMessageHandlers() {
        base.RegisterMessageHandlers();
        _ = Token.RegisterGameObjectTargeted<Heal>(playerGO, OnPlayerHealed);
        _ = Token.RegisterBroadcastWithoutSource<TookDamage>(OnAnyDamage);
    }

    void OnPlayerHealed(ref Heal m) => UpdateHealthBar(m.amount);
    void OnAnyDamage(InstanceId source, TookDamage m) => ShowDamageEffect(source);
}

// 3. Send (clean, discoverable)
var heal = new Heal(10);
heal.EmitGameObjectTargeted(playerGameObject);
```

Benefits:
- ✅ **Zero memory leaks** - automatic lifecycle management via tokens
- ✅ **Full decoupling** - no references between systems
- ✅ **Predictable order** - priority-based execution
- ✅ **Rich context** - know who sent/received every message
- ✅ **Intercept & validate** - enforce rules before handlers run
- ✅ **Built-in diagnostics** - see message history in the Inspector

## Core Concepts (The Mental Model)

DxMessaging has **3 message types** that map to communication patterns:

### 1. Untargeted Messages (Global Broadcasts)
**"Something happened that anyone might care about"**

```csharp
[DxUntargetedMessage]
[DxAutoConstructor]
public readonly partial struct GamePaused { }

[DxUntargetedMessage]
[DxAutoConstructor]
public readonly partial struct SettingsChanged {
    public readonly float volume;
    public readonly int quality;
}

// Anyone can listen
_ = token.RegisterUntargeted<GamePaused>(OnPause);
_ = token.RegisterUntargeted<SettingsChanged>(OnSettings);

// Anyone can send
var msg1 = new GamePaused();
msg1.Emit();
var msg2 = new SettingsChanged(0.8f, 2);
msg2.Emit();
```

**When to use:** Scene events, global state changes, settings updates

### 2. Targeted Messages (Commands to One Entity)
**"Hey YOU specifically, do this thing"**

```csharp
[DxTargetedMessage]
[DxAutoConstructor]
public readonly partial struct Heal { public readonly int amount; }

[DxTargetedMessage]
[DxAutoConstructor]
public readonly partial struct EquipWeapon { public readonly int weaponId; }

// Listen on specific GameObject/Component
_ = token.RegisterComponentTargeted<Heal>(this, OnHeal);

// Send to specific target
var heal = new Heal(25);
heal.EmitGameObjectTargeted(playerGameObject);
```

**When to use:** Direct commands, RPC-style calls, UI interactions targeting specific objects

### 3. Broadcast Messages (Events from One Source)
**"I did something - anyone interested can observe"**

```csharp
[DxBroadcastMessage]
[DxAutoConstructor]
public readonly partial struct TookDamage {
    public readonly int amount;
    public readonly string damageType;
}

// Listen to specific source
_ = token.RegisterGameObjectBroadcast<TookDamage>(enemyGO, OnEnemyDamaged);

// Listen to ALL sources (great for analytics!)
_ = token.RegisterBroadcastWithoutSource<TookDamage>(OnAnyDamage);

// Emit from source
var dmg = new TookDamage(15, "fire");
dmg.EmitGameObjectBroadcast(this.gameObject);
```

**When to use:** Observable events, analytics, achievements, notifications

## The Message Pipeline

Every message flows through 3 stages (see full details in [Interceptors & Ordering](InterceptorsAndOrdering.md)):

```
Producer → [Interceptors] → [Handlers] → [Post-Processors]
              ↑                ↑              ↑
           Validate &       Main Logic    Analytics &
           Transform                       Logging
```

### Interceptors (Optional Guards)
Run **before** handlers. Can **mutate or cancel** messages.

```csharp
// Validate and clamp damage
_ = token.RegisterBroadcastInterceptor<TookDamage>(
    (ref InstanceId source, ref TookDamage msg) => {
        if (msg.amount <= 0) return false; // Cancel invalid damage
        if (msg.amount > 999) {
            msg = new TookDamage(999, msg.damageType); // Clamp
        }
        return true; // Allow to proceed
    },
    priority: 0  // Lower priority = runs earlier
);
```

### Handlers (Main Logic)
The meat of your game logic. Run in **priority order**.

```csharp
_ = token.RegisterUntargeted<GamePaused>(OnPause, priority: 0);    // Runs first
_ = token.RegisterUntargeted<GamePaused>(OnPauseUI, priority: 10); // Runs second
```

### Post-Processors (After the Fact)
Run **after** all handlers. Perfect for logging/metrics.

```csharp
_ = token.RegisterBroadcastWithoutSourcePostProcessor<TookDamage>(
    (InstanceId src, TookDamage msg) => {
        Analytics.LogDamage(src, msg.amount);
    }
);
```

## Quick Start: Your First Message

### Step 1: Define a Message

```csharp
using DxMessaging.Core.Attributes;

[DxTargetedMessage]  // This is a command to a specific target
[DxAutoConstructor]  // Auto-generate constructor
public readonly partial struct OpenChest {
    public readonly int chestId;
}
```

### Step 2: Create a Listener

```csharp
using DxMessaging.Unity;

public class ChestController : MessageAwareComponent
{
    protected override void RegisterMessageHandlers() {
        base.RegisterMessageHandlers();
        // Listen for OpenChest messages targeted at this component
        _ = Token.RegisterComponentTargeted<OpenChest>(this, OnOpen);
    }

    void OnOpen(ref OpenChest msg) {
        Debug.Log($"Opening chest {msg.chestId}");
        // ... your chest opening logic ...
    }
}
```

### Step 3: Send the Message

```csharp
// From anywhere in your code:
var openMsg = new OpenChest(chestId: 42);
openMsg.EmitComponentTargeted(chestComponent);
```

That's it! No manual subscribe/unsubscribe, no tight coupling.

## Killer Features You'll Love

### 1. **Zero-Allocation Struct Messages**
Messages are structs passed by reference - no GC pressure!

```csharp
// Struct message - zero allocations
void OnDamage(ref TookDamage msg) {
    health -= msg.amount;
}
```

### 2. **Automatic Lifecycle Management**
The `MessageRegistrationToken` handles everything:

```csharp
public class MyComponent : MessageAwareComponent
{
    // MessageAwareComponent automatically:
    // - Creates token in Awake()
    // - Enables in OnEnable()
    // - Disables in OnDisable()
    // - Cleans up in OnDestroy()
}
```

### 3. **Listen to "All Targets" or "All Sources"**
Perfect for analytics, debugging, and tools:

```csharp
// Log ALL damage events, regardless of source
_ = token.RegisterBroadcastWithoutSource<TookDamage>(
    (InstanceId source, TookDamage msg) => {
        Debug.Log($"{source} took {msg.amount} damage");
    }
);
```

### 4. **Powerful Inspector**
The `MessagingComponent` inspector shows:
- All registered handlers
- Recent message history
- Call counts per handler
- Message priorities

### 5. **Local Bus Islands**
Create isolated buses for testing or subsystems:

```csharp
// Test with isolated bus - no global side effects
var testBus = new MessageBus();
var token = MessageRegistrationToken.Create(handler, testBus);
```

### 6. **Priority-Based Execution**
Control exactly when handlers run:

```csharp
_ = token.RegisterUntargeted<GameStarted>(SaveSystem, priority: 0);   // First
_ = token.RegisterUntargeted<GameStarted>(AudioSystem, priority: 5);  // Second
_ = token.RegisterUntargeted<GameStarted>(UISystem, priority: 10);    // Third
```

## Common Patterns

### Pattern 1: Scene Transitions
```csharp
[DxUntargetedMessage]
[DxAutoConstructor]
public readonly partial struct SceneTransition {
    public readonly string sceneName;
}

// Multiple systems can react independently
_ = token.RegisterUntargeted<SceneTransition>(OnSceneChange);
```

### Pattern 2: Player Input → Action
```csharp
[DxTargetedMessage]
[DxAutoConstructor]
public readonly partial struct Jump { public readonly float force; }

// Input system sends
void Update() {
    if (Input.GetKeyDown(KeyCode.Space)) {
        var msg = new Jump(10f);
        msg.EmitComponentTargeted(playerController);
    }
}

// Player controller handles
void OnJump(ref Jump msg) {
    rb.AddForce(Vector3.up * msg.force, ForceMode.Impulse);
}
```

### Pattern 3: Achievement System
```csharp
// Listen to EVERYTHING
_ = achievementToken.RegisterGlobalAcceptAll(
    (ref IUntargetedMessage m) => CheckAchievements(m),
    (ref InstanceId t, ref ITargetedMessage m) => CheckAchievements(m),
    (ref InstanceId s, ref IBroadcastMessage m) => CheckAchievements(m)
);
```

## DxMessaging vs Alternatives

| Feature | DxMessaging | C# Events | UnityEvents | Static Event Bus |
|---------|-------------|-----------|-------------|------------------|
| Decoupling | ✅ Full | ❌ Tight | ⚠️ Hidden | ✅ Yes |
| Lifecycle Safety | ✅ Auto | ❌ Manual | ⚠️ Unity-managed | ❌ Manual |
| Execution Order | ✅ Priority | ❌ Undefined | ⚠️ Undefined | ❌ Undefined |
| Type Safety | ✅ Strong | ✅ Strong | ⚠️ Weak | ⚠️ Weak |
| Context (Who/What) | ✅ Rich | ❌ None | ❌ None | ❌ None |
| Interception | ✅ Yes | ❌ No | ❌ No | ❌ No |
| Observability | ✅ Built-in | ❌ No | ❌ No | ❌ No |
| Performance | ✅ Zero-alloc | ✅ Good | ⚠️ Boxing | ✅ Good |

## Do's and Don'ts

### ✅ DO:

1. **Use MessageAwareComponent for Unity integration**
   ```csharp
   public class MyComponent : MessageAwareComponent {
       protected override void RegisterMessageHandlers() {
           base.RegisterMessageHandlers();
           _ = Token.RegisterUntargeted<MyMessage>(OnMessage);
       }
   }
   ```

2. **Define messages as readonly structs**
   ```csharp
   [DxUntargetedMessage]
   [DxAutoConstructor]
   public readonly partial struct MyMessage {
       public readonly int value;
   }
   ```

3. **Use GameObject/Component emit helpers**
   ```csharp
   msg.EmitGameObjectTargeted(gameObject);  // ✅ Clean
   ```

4. **Assign struct to variable before emitting**
   ```csharp
   var msg = new MyMessage(42);
   msg.Emit();  // ✅ Correct
   ```

5. **Use appropriate message types**
   - Global state? → Untargeted
   - Command to one? → Targeted
   - Event from one? → Broadcast

### ❌ DON'T:

1. **Don't emit from temporaries (structs)**
   ```csharp
   // ✅ CORRECT - Store in a local variable, then emit
   var msg = new MyMessage(42);
   msg.Emit();
   ```

2. **Don't use Untargeted for entity-specific commands**
   ```csharp
   // ❌ Wrong - use Targeted instead
   [DxUntargetedMessage]
   public struct DamagePlayer { public int amount; }
   ```

3. **Don't manually manage lifecycles**
   ```csharp
   // ❌ Bad
   void OnDestroy() {
       token.Disable();
       token = null;
   }

   // ✅ Good - MessageAwareComponent does this
   ```

4. **Don't create message handler spaghetti**
   ```csharp
   // ❌ Too many handlers in one place
   _ = token.RegisterUntargeted<Msg1>(...);
   _ = token.RegisterUntargeted<Msg2>(...);
   // ... 50+ more ...

   // ✅ Better - separate concerns into focused components
   ```

## Common Beginner Mistakes (Avoid These!)

### ❌ Mistake 1: Emitting from Temporaries

```csharp
// ✅ CORRECT - Store in a local variable first
var msg = new MyMessage(42);
msg.Emit();
```

**Why?** Struct extension methods need a variable reference.

### ❌ Mistake 2: Wrong Message Type

```csharp
// ❌ WRONG - Using Untargeted for entity-specific command
[DxUntargetedMessage]
public struct DamagePlayer { public int amount; }

// ✅ CORRECT - Use Targeted for specific entity
[DxTargetedMessage]
public struct DamagePlayer { public int amount; }
```

**Rule of thumb:**
- Everyone hears it? → Untargeted
- One specific target? → Targeted
- From one source, others observe? → Broadcast

### ❌ Mistake 3: Forgetting `readonly` on Structs

```csharp
// ❌ WRONG - Not readonly (can be accidentally mutated)
[DxUntargetedMessage]
public partial struct MyMsg { public int value; }

// ✅ CORRECT - Readonly struct, readonly field
[DxUntargetedMessage]
public readonly partial struct MyMsg { public readonly int value; }
```

**Why?** Immutability prevents bugs and enables optimizations.

### ❌ Mistake 4: Manual Lifecycle Management

```csharp
// ❌ WRONG - Don't manage lifecycle manually
public class MyComponent : MonoBehaviour {
    void OnDestroy() {
        token.Disable();  // MessageAwareComponent does this!
        token = null;
    }
}

// ✅ CORRECT - Use MessageAwareComponent
public class MyComponent : MessageAwareComponent {
    // Lifecycle handled automatically!
}
```

### ❌ Mistake 5: Forgetting Handler Parameter

```csharp
// ❌ WRONG - Missing ref keyword
void OnDamage(TookDamage msg) { }

// ✅ CORRECT - Use ref for struct messages
void OnDamage(ref TookDamage msg) { }
```

**Why?** `ref` avoids copying structs (performance + semantics).

### ❌ Mistake 6: Not Checking for Null/Invalid InstanceId

```csharp
// ❌ WRONG - Can emit to destroyed object
msg.EmitGameObjectTargeted(someGameObject);

// ✅ CORRECT - Check first
if (someGameObject != null) {
    msg.EmitGameObjectTargeted(someGameObject);
}
```

**Why?** Objects can be destroyed between frames.

## Troubleshooting Quick Fixes

### CRITICAL: Inheritance gotcha - Always call base.* in overrides

- `MessageAwareComponent` uses virtual hooks for setup and lifecycle.
- **If you override ANY of these methods, you MUST call the base method:**
  - **`base.RegisterMessageHandlers()`** - ALWAYS call this FIRST in your override to preserve default string‑message registrations and parent class registrations.
  - **`base.Awake()`** - If you override `Awake()`, call this or your token won't be created (handlers will never fire).
  - **`base.OnEnable()` / `base.OnDisable()`** - Call these to preserve token enable/disable (handlers won't activate without them).
  - **`base.OnDestroy()`** - Call this if you override to ensure proper cleanup.
- **Skipping base calls is the #1 cause of "handlers not firing"** or string messages not appearing.
- **Don't hide Unity methods** with `new` (e.g., `new void OnEnable()`). Always use `override` and call `base.*`.

### Registration timing: Prefer Awake() over Start()

- **ALWAYS prefer `Awake()` for message handler registration**, not `Start()`.
- `MessageAwareComponent` automatically calls `RegisterMessageHandlers()` in `Awake()`.
- Why Awake?
  - Runs before any `Start()` methods, ensuring handlers are ready early.
  - Other components may emit messages in their `Start()`—you don't want to miss them.
  - Follows Unity's recommended initialization pattern.
- Only use `Start()` if you have a specific order-of-execution requirement.

### "My handler isn't being called!"

**Checklist:**
1. Is the component enabled? (Check `OnEnable()` was called)
2. Did you register in `RegisterMessageHandlers()`?
3. Are you emitting to the right target?
4. Is there an interceptor cancelling the message?

**Debug:**
```csharp
// Add logging to verify registration
_ = Token.RegisterUntargeted<MyMsg>(msg => {
    Debug.Log("Handler called!");  // Does this print?
});
```

### "Compile error: 'Emit' not found"

**Cause:** Forgetting `using DxMessaging.Core.Extensions;`

**Fix:**
```csharp
using DxMessaging.Core.Extensions;  // ← Add this!
```

### "Messages work in Editor but not in build"

**Cause:** IL2CPP stripping or AOT compilation issues.

**Fix:** Add link.xml to preserve types (see [Troubleshooting](Troubleshooting.md))

### "Performance is slower than expected"

**Causes:**
1. Diagnostics enabled in production
2. Too many GlobalAcceptAll handlers
3. Hundreds of handlers per message

**Fixes:**
```csharp
// Disable diagnostics in builds
IMessageBus.GlobalDiagnosticsMode = false;

// Use specific handlers, not GlobalAcceptAll everywhere
```

## Next Steps

Now that you understand the basics:

1. **Try the samples** - Open the Mini Combat sample from the Package Manager
2. **Read the patterns guide** - [Common Patterns](Patterns.md) for real-world solutions
3. **Understand message types deeply** - [Message Types](MessageTypes.md)
4. **Learn about interceptors** - [Interceptors & Ordering](InterceptorsAndOrdering.md)
5. **Master the inspector** - [Diagnostics](Diagnostics.md)

**Still confused?** Check out the [Visual Guide](VisualGuide.md) for pictures and analogies!

## Quick Reference Card

```csharp
// ─── DEFINE MESSAGES ───
[DxUntargetedMessage]
[DxAutoConstructor]
public readonly partial struct MyGlobalMsg { public readonly int value; }

[DxTargetedMessage]
[DxAutoConstructor]
public readonly partial struct MyTargetMsg { public readonly string data; }

[DxBroadcastMessage]
[DxAutoConstructor]
public readonly partial struct MyBroadcastMsg { public readonly float time; }

// ─── LISTEN (in MessageAwareComponent) ───
protected override void RegisterMessageHandlers() {
    base.RegisterMessageHandlers();
    // Untargeted (global)
    _ = Token.RegisterUntargeted<MyGlobalMsg>(OnGlobal);

    // Targeted (to this component)
    _ = Token.RegisterComponentTargeted<MyTargetMsg>(this, OnTarget);

    // Broadcast (from specific source)
    _ = Token.RegisterGameObjectBroadcast<MyBroadcastMsg>(sourceGO, OnBroadcast);

    // Broadcast (from any source)
    _ = Token.RegisterBroadcastWithoutSource<MyBroadcastMsg>(OnAnyBroadcast);
}

// ─── EMIT ───
var globalMsg = new MyGlobalMsg(42);
globalMsg.Emit();

var targetMsg = new MyTargetMsg("hi");
targetMsg.EmitComponentTargeted(targetComponent);

var broadcastMsg = new MyBroadcastMsg(Time.time);
broadcastMsg.EmitGameObjectBroadcast(this.gameObject);

// ─── INTERCEPTORS & POST-PROCESSORS ───
_ = Token.RegisterUntargetedInterceptor<MyMsg>(
    (ref MyMsg m) => { /* validate/mutate */ return true; },
    priority: 0
);

_ = Token.RegisterUntargetedPostProcessor<MyMsg>(
    (ref MyMsg m) => { /* log/analytics */ },
    priority: 0
);
```

Welcome to clean, scalable messaging! 🚀
