# Mini Combat Sample

> **What You'll Learn**: This sample demonstrates a simple combat system using DxMessaging to show how components communicate through messages. Perfect for beginners learning the messaging system!

## Overview

This mini-sample showcases a basic combat loop using DxMessaging with Unity-friendly APIs. You'll see how different game objects (Player, Enemy, UI) communicate without direct references to each other.

### Key Concepts Demonstrated:

- **Untargeted Messages**: Global messages anyone can listen to (like game settings)
- **Targeted Messages**: Messages sent to a specific component (like healing a player)
- **Broadcast Messages**: Messages announced to all listeners (like damage events)

---

## Sample Files

Here's what each script does:

| File | Purpose | Message Type |
|------|---------|--------------|
| **Messages.cs** | Defines all message types | Contains `VideoSettingsChanged`, `Heal`, and `TookDamage` |
| **Player.cs** | Handles receiving healing | Listens for `Heal` (Targeted) |
| **Enemy.cs** | Announces when damaged | Emits `TookDamage` (Broadcast) |
| **UIOverlay.cs** | Updates UI based on events | Listens to settings and `TookDamage` (Broadcast) |
| **Boot.cs** | Starts the demo | Simulates message flow |

---

## Quick Start Guide

### Method 1: Import from Package Manager (Recommended)

1. **Open Package Manager**:
   - Window → Package Manager

1. **Find DxMessaging**:
   - Look for "DxMessaging" in the package list
   - Click on it to select

1. **Import the Sample**:
   - In the Package Manager details view, scroll down to find the "Samples" section
   - Find "Mini Combat" and click **Import**
   - The sample files will be imported into your Assets/Samples folder

1. **Open the Scene**:
   - Navigate to Assets/Samples/DxMessaging/.../Mini Combat/
   - Open the sample scene or create a new scene and follow Step 1 below

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
   - **Player** GameObject → Add `Player.cs` script
   - **Enemy** GameObject → Add `Enemy.cs` script
   - **UIOverlay** GameObject → Add `UIOverlay.cs` script
   - **Boot** GameObject → Add `Boot.cs` script

#### Step 3: Run and Observe

Press Play! The Boot script will automatically:

1. Send a settings change message → UI updates
1. Send a heal message to the Player → Player's HP increases
1. Trigger Enemy damage → UI displays the damage event

**Pro Tip**: Enable **Diagnostics** on each MessagingComponent in the Inspector to see messages being sent and received in real-time!

---

## How It Works

### The Message Flow

```text
Boot.cs (sends messages)
    ↓
    ├─→ VideoSettingsChanged (Untargeted) → UIOverlay.cs (receives)
    ├─→ Heal (Targeted to Player)         → Player.cs (receives)
    └─→ TookDamage (Broadcast from Enemy) → UIOverlay.cs (receives)
```

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

- **New to messaging patterns?** Check the main [Getting Started Guide](../../Docs/GettingStarted.md)
- **Want more examples?** See [Patterns Documentation](../../Docs/Patterns.md)
- **Having issues?** Visit [Troubleshooting Guide](../../Docs/Troubleshooting.md)

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
**Message Types**: See `Messages.cs` for all available messages
**Modify Behavior**: Edit handler methods in Player.cs, Enemy.cs, or UIOverlay.cs
**Extend Scripts**: Always call `base.RegisterMessageHandlers()` and other `base.*` methods
