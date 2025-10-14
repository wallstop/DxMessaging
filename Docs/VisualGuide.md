# DxMessaging Visual Guide for Absolute Beginners

If you're brand new to messaging systems, this visual guide will help you understand DxMessaging in minutes.

## 🎯 What Problem Does It Solve

### The Old Way (Spaghetti Code)

````text
┌─────────────┐
│   Player    │───────┐
└─────────────┘       │
                      ├──→ ┌─────────┐
┌─────────────┐       │    │   UI    │
│   Enemy     │───────┤    └─────────┘
└─────────────┘       │
                      │    ┌─────────┐
┌─────────────┐       └───→│  Audio  │
│ Inventory   │───────────→└─────────┘
└─────────────┘

Problems:
❌ Everyone needs to know everyone else
❌ Hard to add/remove systems
❌ Memory leaks from forgotten unsubscribes
```text

### The DxMessaging Way (Clean Separation)

```text
┌─────────┐                    ┌─────────┐
│ Player  │──→ Message ──→     │   UI    │
└─────────┘        ↓           └─────────┘
                   │
┌─────────┐        ↓           ┌─────────┐
│ Enemy   │──→  BUS   ──→      │  Audio  │
└─────────┘        ↓           └─────────┘
                   │
┌──────────┐       ↓           ┌──────────┐
│Inventory │──→ Message ──→    │Analytics │
└──────────┘                   └──────────┘

Benefits:
✅ Nobody knows about anyone else
✅ Easy to add/remove systems
✅ Zero memory leaks (automatic cleanup)
```text

## 📨 The Three Message Types (Simple!)

Think of messages like different kinds of mail:

### 1. 📢 Untargeted (Announcement to Everyone)

Like a megaphone announcement in a stadium - everyone hears it.

```csharp
// Define the announcement
[DxUntargetedMessage]
[DxAutoConstructor]
public readonly partial struct GamePaused { }

// Anyone can announce
var msg = new GamePaused();
msg.Emit();

// Anyone can listen
_ = token.RegisterUntargeted<GamePaused>(OnPause);
```text

#### Real-world uses:

- "Game paused!"
- "Settings changed!"
- "Level loaded!"

### 2. 📮 Targeted (Letter to One Person)

Like mailing a letter to a specific address - only that recipient gets it.

```csharp
// Define the letter
[DxTargetedMessage]
[DxAutoConstructor]
public readonly partial struct Heal { public readonly int amount; }

// Send to specific person
var heal = new Heal(50);
heal.EmitGameObjectTargeted(playerObject);

// Only the player listens
_ = token.RegisterComponentTargeted<Heal>(this, OnHeal);
```text

#### Real-world uses:

- "Player, heal yourself!"
- "Enemy #3, take damage!"
- "Button, update your text!"

### 3. 📡 Broadcast (News from One Source)

Like a news broadcast - comes from one source, anyone can tune in.

```csharp
// Define the news
[DxBroadcastMessage]
[DxAutoConstructor]
public readonly partial struct TookDamage { public readonly int amount; }

// Broadcast from enemy
var dmg = new TookDamage(25);
dmg.EmitGameObjectBroadcast(enemyObject);

// UI can listen to specific enemy
_ = token.RegisterGameObjectBroadcast<TookDamage>(enemyObject, OnThisEnemy);

// OR achievement system can listen to ALL enemies
_ = token.RegisterBroadcastWithoutSource<TookDamage>(OnAnyEnemy);
```text

#### Real-world uses:

- "I (player) took damage!"
- "I (enemy) died!"
- "I (chest) was opened!"

## 🔄 The Message Journey (Step by Step)

When you send a message, here's what happens:

```text
1. You create a message
   var heal = new Heal(10);

2. You emit it
   heal.EmitGameObjectTargeted(player);

3. [OPTIONAL] Interceptors check it
   ┌─────────────────────────────┐
   │ Is damage valid? (>0)       │
   │ Should we clamp it? (<999)  │
   │ Cancel bad messages? ❌     │
   └─────────────────────────────┘

4. Handlers receive it (your main logic)
   ┌─────────────────────────────┐
   │ priority: 0  → SaveSystem   │
   │ priority: 5  → AudioSystem  │
   │ priority: 10 → UISystem     │
   └─────────────────────────────┘

5. [OPTIONAL] Post-processors log it
   ┌─────────────────────────────┐
   │ Analytics.Track(...)        │
   │ Debug.Log(...)              │
   └─────────────────────────────┘
```text

## 🎮 Your First Message (3 Easy Steps)

### Step 1️⃣: Define It

```csharp
using DxMessaging.Core.Attributes;

[DxTargetedMessage]     // ← What kind of message?
[DxAutoConstructor]     // ← Auto-make a constructor
public readonly partial struct Heal {
    public readonly int amount;
}
````

#### What are those `[DxSomething]` tags?

They're called **attributes** — magic markers that tell the computer to write code for you! It's like having a robot assistant:

- **`[DxTargetedMessage]`** → "Hey robot, make this a targeted message"
- **`[DxAutoConstructor]`** → "Hey robot, create a constructor for me"

The `[DxAutoConstructor]` automatically creates this:

```csharp
public Heal(int amount) { this.amount = amount; }
```

**Why `partial`?** So the robot can add code to your type in a separate file. Think of it as giving permission to extend your struct.

**Want to learn more?** See [Helpers & Source Generation](Helpers.md) for the full explanation!

### Step 2️⃣: Listen for It

```csharp
using DxMessaging.Unity;

public class Player : MessageAwareComponent {
    protected override void RegisterMessageHandlers() {
        base.RegisterMessageHandlers();
        // "When someone sends Heal to ME, call OnHeal"
        _ = Token.RegisterComponentTargeted<Heal>(this, OnHeal);
    }

    void OnHeal(ref Heal msg) {
        health += msg.amount;
        Debug.Log($"Healed {msg.amount}!");
    }
}
```

**Magic:** `MessageAwareComponent` handles all the lifecycle automatically!

- Creates registration in `Awake()`
- Activates in `OnEnable()`
- Deactivates in `OnDisable()`
- Cleans up in `OnDestroy()`

### Step 3️⃣: Send It

```csharp
// From anywhere in your code:
var healMsg = new Heal(50);
healMsg.EmitComponentTargeted(playerComponent);

// That's it! Player will receive it automatically.
```

## 🧩 Common Patterns Visualized

### Pattern: Scene Transition

```text
SceneManager               AudioSystem
     │                          │
     │  [SceneChanged]          │
     ├────────────────────────→ │ FadeOutMusic()
     │                          │
     │                     SaveSystem
     │                          │
     │  [SceneChanged]          │
     └────────────────────────→ │ SaveGame()

All independent! No coupling!
```

Code:

```csharp
// Define
[DxUntargetedMessage]
[DxAutoConstructor]
public readonly partial struct SceneChanged { public readonly int sceneIndex; }

// Anyone can send
var msg = new SceneChanged(2);
msg.Emit();

// Many can listen independently
_ = audioToken.RegisterUntargeted<SceneChanged>(OnScene);
_ = saveToken.RegisterUntargeted<SceneChanged>(OnScene);
```

### Pattern: Player Input → Action

```text
InputSystem          Player
     │                 │
     │   [Jump]        │
     ├───────────────→ │ ApplyForce()
     │                 │

Decoupled! Input doesn't need reference to Player.
```

Code:

```csharp
// Input system (doesn't know about Player!)
void Update() {
    if (Input.GetKeyDown(KeyCode.Space)) {
        var jump = new Jump(10f);
        jump.EmitComponentTargeted(playerController);
    }
}

// Player (doesn't know about Input system!)
_ = token.RegisterComponentTargeted<Jump>(this, OnJump);
void OnJump(ref Jump msg) {
    rb.AddForce(Vector3.up * msg.force, ForceMode.Impulse);
}
```

### Pattern: Achievement Tracking

````text
Any System                Achievement System
     │                           │
     │  [Any Message]            │
     ├─────────────────────────→ │ CheckProgress()
     │                           │ UnlockIfReady()

Achievements see EVERYTHING without coupling!
```text

Code:

```csharp
public class AchievementSystem : MessageAwareComponent {
    protected override void RegisterMessageHandlers() {
        base.RegisterMessageHandlers();
        // Listen to EVERYTHING
        _ = Token.RegisterGlobalAcceptAll(
            (ref IUntargetedMessage m) => Check(m),
            (ref InstanceId t, ref ITargetedMessage m) => Check(m),
            (ref InstanceId s, ref IBroadcastMessage m) => Check(m)
        );
    }
}
````

## 🚦 When to Use Which Message Type

### Use Untargeted When

- ✅ Global game state changes (pause, settings, scene load)
- ✅ System-wide announcements
- ✅ Configuration updates

### Use Targeted When

- ✅ Commanding a specific object ("You, do this!")
- ✅ UI updates for specific elements
- ✅ Direct communication (A → B)

### Use Broadcast When

- ✅ Events others should know about ("I did this!")
- ✅ Analytics tracking
- ✅ Achievement triggers
- ✅ Notifications from specific sources

## 🎨 Mental Model: Restaurant Analogy

Think of DxMessaging like a restaurant:

### Untargeted = Restaurant Announcement

````text
"Attention all customers: We're closing in 10 minutes!"
→ Everyone hears it
```text

### Targeted = Waiter Delivering Food

```text
"Order for table 5: Here's your burger"
→ Only table 5 gets it
````

### Broadcast = Customer Calling Waiter

````text
"Excuse me, I need a refill!" (from table 3)
→ Comes from table 3
→ Any available waiter can respond
→ Manager might track it for statistics
```text

## 🔍 Debugging Visualized

DxMessaging has built-in Inspector support!

```text
MessagingComponent Inspector:
┌─────────────────────────────────────┐
│ Message History (last 10)           │
│ ┌─────────────────────────────────┐ │
│ │ 12:34:05 - Heal → Player (50)   │ │
│ │ 12:34:03 - Jump → Player        │ │
│ │ 12:34:01 - GamePaused (global)  │ │
│ └─────────────────────────────────┘ │
│                                     │
│ Registrations:                      │
│ ┌─────────────────────────────────┐ │
│ │ ✓ Heal (priority: 0, 5 calls)   │ │
│ │ ✓ Jump (priority: 0, 2 calls)   │ │
│ │ ✓ TookDamage (priority: 10)     │ │
│ └─────────────────────────────────┘ │
└─────────────────────────────────────┘
````

## ⚡ Performance at a Glance

```text
Traditional C# Event: ████░░░░░░ (baseline)
DxMessaging:          █████░░░░░ (~10ns slower, negligible)

Memory:
Traditional Events:   ████████░░ (can leak!)
DxMessaging:          ██░░░░░░░░ (zero leaks, struct messages)

Coupling:
Traditional Events:   ██████████ (tight!)
DxMessaging:          ░░░░░░░░░░ (zero!)
```

**Bottom line:** Slightly slower than raw events, but:

- ✅ Zero memory leaks
- ✅ Zero coupling
- ✅ Full observability
- ✅ Predictable ordering

## 🎓 Learning Path

```text
START HERE
    │
    ├─→ 1. Read this Visual Guide (5 min) ✓
    │
    ├─→ 2. Try Quick Start example (5 min)
    │      [Define → Listen → Send]
    │
    ├─→ 3. Import Mini Combat sample (10 min)
    │      [See it in action!]
    │
    ├─→ 4. Read Common Patterns (15 min)
    │      [Real-world solutions]
    │
    └─→ 5. Build your first feature! (30 min)
        [You're ready!]
```

## 🆘 Common Beginner Questions

### "Do I always need MessageAwareComponent?"

**For Unity:** Yes! It's the easiest way. Think of it like `MonoBehaviour` - you inherit from it and it handles all the messy lifecycle stuff automatically.

**For pure C#:** No, you can use `MessageRegistrationToken` directly if you're not in Unity.

**Bottom line:** If you're in Unity, just use `MessageAwareComponent`. It'll save you hours of debugging.

### "Can I send a message to multiple targets?"

**No** - Targeted messages go to ONE specific entity (like mailing a letter to one address).

#### Instead, use

- **Untargeted** if literally everyone should hear it (like a megaphone announcement)
- **Broadcast** if it's from one source and many can observe (like a news broadcast)

##### Example

```csharp
// ❌ DON'T: Try to target multiple entities
msg.EmitComponentTargeted(player1);
msg.EmitComponentTargeted(player2);  // Feels wrong, right?

// ✅ DO: Use broadcast so everyone can listen
msg.EmitGameObjectBroadcast(enemy);  // Now anyone can observe this enemy
```

### "What if I forget to unsubscribe?"

**You literally can't forget!** 🎉

When your component is destroyed, DxMessaging automatically cleans up. No `OnDestroy()` needed. No memory leaks possible.

#### Old way (easy to forget)

```csharp
void OnEnable() { GameManager.OnScoreChanged += Update; }
void OnDisable() { GameManager.OnScoreChanged -= Update; }  // Forgot this? LEAK!
```

##### DxMessaging way (impossible to forget)

```csharp
protected override void RegisterMessageHandlers() {
    _ = Token.RegisterUntargeted<ScoreChanged>(Update);
}
// That's it! Automatic cleanup when component dies.
```

### "Is it slower than regular events?"

**Barely** (~10ns per handler = 0.00001 milliseconds).

#### Put it this way

- Regular C# event: ~50ns
- DxMessaging: ~60ns
- The difference: Drinking a coffee takes 3 billion nanoseconds

You get automatic lifecycle, zero leaks, full observability, and predictable ordering for a 20% overhead that's **completely negligible** in any real game.

### "Can I cancel a message?"

#### Yes! That's what interceptors are for

```csharp
// Cancel invalid damage
_ = token.RegisterBroadcastInterceptor<TookDamage>(
    (ref InstanceId source, ref TookDamage msg) => {
        if (msg.amount <= 0) return false;  // Cancel invalid damage
        if (IsInvincible(source)) return false;  // Cancel during invincibility
        return true;  // Allow
    }
);
```

##### Real-world uses

- Block input during cutscenes
- Cancel damage when invincible
- Prevent cheating (clamp values)
- Enforce game rules globally

### "Can I see what messages are firing?"

#### Yes! Open any component in the Inspector and scroll down

You'll see:

- Message history (last 50 messages with timestamps)
- Active registrations (what you're listening to)
- Call counts (how many times each handler ran)

**No more guessing.** You can literally see your event flow in real-time.

## ✅ Quick Checklist: Am I Doing It Right

- [ ] Using `MessageAwareComponent` for Unity components? ✅
- [ ] Defining messages as `readonly struct`? ✅
- [ ] Using `[DxAutoConstructor]` to avoid boilerplate? ✅
- [ ] Storing struct in variable before emitting? ✅
- [ ] Choosing the right message type (Untargeted/Targeted/Broadcast)? ✅
- [ ] Using GameObject/Component emit helpers? ✅

If you checked all these, **you're doing it right!** 🎉

## 🚀 Next Steps

Ready to dive deeper?

1. **[Getting Started Guide](GettingStarted.md)** - Full guide with more details
1. **[Common Patterns](Patterns.md)** - Real-world examples
1. **[Message Types](MessageTypes.md)** - Deep dive into when to use what
1. **[Diagnostics](Diagnostics.md)** - Master the Inspector tools

---

**Remember:** DxMessaging makes complex communication simple. You define what to say, who should hear it, and the system handles the rest! 🎊
