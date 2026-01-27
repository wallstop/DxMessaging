# Mini Combat Sample

> **Ideal for:** First-time users who want to see messaging in action with a working combat example

## What You'll Learn in 10 Minutes

**This sample demonstrates messaging concepts through a functional example.** It shows:

1. How Player heals without UI knowing about Player (**zero coupling**)
2. How Enemy broadcasts damage without knowing who cares (**observer pattern**)
3. How settings changes update everything automatically (**global events**)

**No prior messaging experience needed** - this sample walks you through everything.

---

## Why This Sample Matters

### Before DxMessaging:

```csharp
public class Player {
    [SerializeField] private UI ui;          // Coupling!
    [SerializeField] private AudioManager audio; // More coupling!

    void Heal(int amount) {
        health += amount;
        ui.UpdateHealth(health);     // Manual call
        audio.PlayHealSound();        // Manual call
    }
}
```

#### Every new system = another SerializeField + another manual call.

##### With DxMessaging:

```csharp
public class Player : MessageAwareComponent {
    void Heal(int amount) {
        health += amount;
        new PlayerHealed(amount).EmitBroadcast(this);
        // Done! UI, audio, analytics all react automatically.
    }
}
```

###### This decouples systems and removes manual wiring.

---

## Key Concepts (3 Message Types)

### 1. Untargeted - "Everyone, listen up!"

**Example:** Game settings changed

- Like a stadium announcement - everyone hears it
- No specific target
- Perfect for: settings, pause, scene transitions

### 2. Targeted - "Hey YOU, do this!"

**Example:** Heal this specific player

- Like mailing a letter to one address
- Only the target receives it
- Perfect for: commands, direct actions

### 3. Broadcast - "I did something!"

**Example:** Enemy took damage

- Like ringing a bell - anyone nearby can hear
- Source announces, observers react
- Perfect for: events, notifications, analytics

---

## Sample Files

Here's what each script does:

| File | Purpose | Message Type |
|------|---------|--------------|
| **[Messages.cs](./Messages.cs)** | Defines all message types | Contains `VideoSettingsChanged`, `Heal`, and `TookDamage` |
| **[Player.cs](./Player.cs)** | Handles receiving healing | Listens for `Heal` (Targeted) |
| **[Enemy.cs](./Enemy.cs)** | Announces when damaged | Emits `TookDamage` (Broadcast) |
| **[UIOverlay.cs](./UIOverlay.cs)** | Updates UI based on events | Listens to settings and `TookDamage` (Broadcast) |
| **[Boot.cs](./Boot.cs)** | Starts the demo | Simulates message flow |

---

## Quick Start Guide (2 Minutes to Working Demo)

### Import & Run (Fastest Way)

#### Want to see it work immediately?

1. **Open Package Manager**: Window → Package Manager
2. **Find DxMessaging** in the package list
3. **Scroll to Samples** section → Find "Mini Combat" → Click **Import**
4. **Navigate to** Assets/Samples/DxMessaging/.../Mini Combat/
5. **Open the scene**
6. **Press Play** 🎮

Watch the Console logs as messages flow.

**Pro tip:** Enable diagnostics in the Inspector (MessagingComponent → Enable Diagnostics) to see message traffic in real-time.

### Method 2: Manual Setup in Your Scene

#### Step 1: Set Up Your Scene

1. **Create GameObjects** in your Unity scene:
   - Create a GameObject named "Player"
   - Create a GameObject named "Enemy"
   - Create a GameObject named "UIOverlay"
   - Create a GameObject named "Boot"

#### Step 2: Add Components

For **each GameObject**, you need TWO components:

1. **Add MessagingComponent** (DxMessaging's Unity component)
   - Click GameObject → Add Component → "MessagingComponent"

1. **Add the sample script**:
   - **Player** GameObject → Add [Player.cs](./Player.cs) script
   - **Enemy** GameObject → Add [Enemy.cs](./Enemy.cs) script
   - **UIOverlay** GameObject → Add [UIOverlay.cs](./UIOverlay.cs) script
   - **Boot** GameObject → Add [Boot.cs](./Boot.cs) script

#### Step 3: Run and Observe

Press Play! The Boot script will automatically:

1. Send a settings change message → UI updates
1. Send a heal message to the Player → Player's HP increases
1. Trigger Enemy damage → UI displays the damage event

**Pro Tip**: Enable **Diagnostics** on each MessagingComponent in the Inspector to see messages being sent and received in real-time!

---

## How It Works

### The Message Flow

#### [Boot.cs](./Boot.cs) sends messages:

1. `VideoSettingsChanged` (Untargeted) → [UIOverlay.cs](./UIOverlay.cs) receives
2. `Heal` (Targeted to Player) → [Player.cs](./Player.cs) receives
3. `TookDamage` (Broadcast from Enemy) → [UIOverlay.cs](./UIOverlay.cs) receives

### Understanding Message Types

1. **Untargeted Messages** (`VideoSettingsChanged`)
   - Like a public announcement everyone can hear
   - No specific recipient
   - Anyone interested can listen

1. **Targeted Messages** (`Heal`)
   - Sent directly to a specific component
   - Only that component receives it
   - Like sending a letter to a specific person

1. **Broadcast Messages** (`TookDamage`)
   - Announced from a GameObject to all listeners
   - Anyone listening for this message type will receive it
   - Like shouting in a room—everyone hears it

---

## Important Notes

### Assembly Definitions

This sample includes an `.asmdef` (Assembly Definition) file that references `WallstopStudios.DxMessaging`.

#### What this means:

- The sample scripts are in their own assembly for clean separation
- The assembly definition already references DxMessaging for you
- `using DxMessaging.*` statements work automatically

##### If you move these scripts:

- If you move them to your main Assets folder, they'll use your project's default assembly
- If you move them to a different assembly, make sure that assembly references `WallstopStudios.DxMessaging`

### Message Registration

Each script uses `MessagingComponent.Create(this)` to get a `MessageRegistrationToken`:

- This token manages all message subscriptions for that component
- It automatically cleans up when the component is destroyed
- Think of it as your "ticket" to use the messaging system

### CRITICAL: Inheriting from MessageAwareComponent

#### If you extend these sample scripts or create your own derived classes:

1. **ALWAYS call `base.RegisterMessageHandlers()` FIRST** in your override:

   ```csharp
   protected override void RegisterMessageHandlers() {
       base.RegisterMessageHandlers();  // ← MUST call this first!
       _ = Token.RegisterUntargeted<YourMessage>(OnYourMessage);
   }
   ```

1. **If you override Unity lifecycle methods, call the base version:**
   - `base.Awake()` - Skipping this means your token won't be created (handlers will never fire!)
   - `base.OnEnable()` / `base.OnDisable()` - Skipping these means handlers won't activate
   - `base.OnDestroy()` - Skipping this can cause memory leaks

1. **Never use `new` to hide methods** - Always use `override` and call `base.*`

**Why this matters:** Forgetting `base.RegisterMessageHandlers()` or `base.Awake()` is the #1 cause of "my handlers aren't firing" issues. The base class does essential setup that your code depends on.

### Registration Timing: Use Awake, Not Start

- All sample scripts register in `Awake()` (via `RegisterMessageHandlers()`)
- This is **the recommended pattern** - handlers are ready before any `Start()` methods run
- **Avoid registering in `Start()`** unless you have a specific order-of-execution reason
- Early registration ensures you don't miss messages emitted by other components

### Learning vs Production

This sample is designed for **learning** and **experimentation**:

- It's intentionally simple and focused
- Great starting point for your own messaging systems
- Feel free to modify and extend it!
- Use it as a foundation for your game's communication patterns

---

## Next Steps

### Deep Dive Walkthrough

Ready to understand the implementation details?

👉 **[Read the Complete Walkthrough](Walkthrough.md)** - Step-by-step explanation of how everything works

### Need Help

- **New to messaging patterns?** Check the main [Getting Started Guide](../../docs/getting-started/getting-started.md)
- **Want more examples?** See [Patterns Documentation](../../docs/guides/patterns.md)
- **Having issues?** Visit [Troubleshooting Guide](../../docs/reference/troubleshooting.md)

---

## Common Pitfalls (Avoid These!)

### ❌ Pitfall #1: Forgetting base.RegisterMessageHandlers()

```csharp
// ❌ WRONG - Missing base call
protected override void RegisterMessageHandlers() {
    _ = Token.RegisterUntargeted<MyMessage>(OnMessage);
}

// ✅ CORRECT - Always call base first
protected override void RegisterMessageHandlers() {
    base.RegisterMessageHandlers();  // Essential!
    _ = Token.RegisterUntargeted<MyMessage>(OnMessage);
}
```

### ❌ Pitfall #2: Overriding Awake() without calling base

```csharp
// ❌ WRONG - Token never created, handlers never fire
protected override void Awake() {
    myCustomSetup();
}

// ✅ CORRECT - Call base.Awake()
protected override void Awake() {
    base.Awake();  // Creates the token!
    myCustomSetup();
}
```

### ❌ Pitfall #3: Registering in Start() instead of Awake()

```csharp
// ❌ WRONG - Might miss messages from other components
void Start() {
    _ = Token.RegisterUntargeted<MyMessage>(OnMessage);
}

// ✅ CORRECT - Use Awake via RegisterMessageHandlers
protected override void RegisterMessageHandlers() {
    base.RegisterMessageHandlers();
    _ = Token.RegisterUntargeted<MyMessage>(OnMessage);
}
```

### ❌ Pitfall #4: Using 'new' instead of 'override'

```csharp
// ❌ WRONG - Hides the base method, breaks functionality
new void OnEnable() {
    // This doesn't override, it hides!
}

// ✅ CORRECT - Always use override
protected override void OnEnable() {
    base.OnEnable();
    // Your code here
}
```

## Quick Reference

**Enable Diagnostics**: Select MessagingComponent in Inspector → Enable Diagnostics
**Message Types**: See [Messages.cs](./Messages.cs) for all available messages
**Modify Behavior**: Edit handler methods in [Player.cs](./Player.cs), [Enemy.cs](./Enemy.cs), or [UIOverlay.cs](./UIOverlay.cs)
**Extend Scripts**: Always call `base.RegisterMessageHandlers()` and other `base.*` methods
