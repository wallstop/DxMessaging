# Helpers and Source Generation

## What Are Source Generators?

**Source generators** are a C# feature (introduced in C# 9.0) that **automatically write code for you at compile time**. Think of them as smart code wizards that look at your code, see what you need, and generate the boilerplate automatically.

**In plain English:**
- You write: `[DxAutoConstructor]` on a struct
- Source generator sees: "Oh, they want a constructor!"
- Source generator creates: A constructor with all the fields as parameters
- You get: Less typing, fewer bugs, cleaner code

**Learn more about source generators:**
- [Microsoft Docs: Source Generators](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/source-generators-overview)
- [Introduction to C# Source Generators](https://devblogs.microsoft.com/dotnet/introducing-c-source-generators/)

## DxMessaging Attributes (Your Code Wizards)

DxMessaging provides **3 main attributes** that use source generators to eliminate boilerplate:

### 1. Message Type Attributes (Pick Your Message Category)

These tell the source generator what KIND of message you're making:

#### `[DxUntargetedMessage]` - Global Messages

```csharp
[DxUntargetedMessage]  // ← Tells generator: "This is a global message"
public readonly partial struct GamePaused { }
```

**What it generates:**
- Implements `IUntargetedMessage<GamePaused>`
- Adds required plumbing for the message system
- Makes it work with `.Emit()` extension methods

#### `[DxTargetedMessage]` - Messages to Specific Targets

```csharp
[DxTargetedMessage]  // ← Tells generator: "This goes to one specific target"
public readonly partial struct Heal {
    public readonly int amount;
}
```

**What it generates:**
- Implements `ITargetedMessage<Heal>`
- Adds required plumbing for targeted emissions
- Makes it work with `.EmitGameObjectTargeted()` / `.EmitComponentTargeted()`

#### `[DxBroadcastMessage]` - Messages from a Source

```csharp
[DxBroadcastMessage]  // ← Tells generator: "This broadcasts from a source"
public readonly partial struct TookDamage {
    public readonly int amount;
}
```

**What it generates:**
- Implements `IBroadcastMessage<TookDamage>`
- Adds required plumbing for broadcast emissions
- Makes it work with `.EmitGameObjectBroadcast()` / `.EmitComponentBroadcast()`

### 2. `[DxAutoConstructor]` - Automatic Constructors

**Problem:** Writing constructors for every message is tedious and error-prone.

**Solution:** Let the source generator do it!

```csharp
[DxUntargetedMessage]
[DxAutoConstructor]  // ← Magic happens here!
public readonly partial struct VideoSettingsChanged
{
    public readonly int width;
    public readonly int height;
}
```

**What you get (auto-generated):**
```csharp
// You don't write this - it's generated for you!
public VideoSettingsChanged(int width, int height)
{
    this.width = width;
    this.height = height;
}
```

**Rules:**
- Creates constructor parameters in **field declaration order**
- Only includes `public` fields
- Ignores `static` fields
- Works with `readonly` structs (recommended!)

### 3. `[DxOptionalParameter]` - Optional Constructor Parameters

Make some constructor parameters optional:

```csharp
[DxUntargetedMessage]
[DxAutoConstructor]
public readonly partial struct SettingsChanged
{
    public readonly float volume;
    public readonly int quality;
    [DxOptionalParameter]  // ← This parameter becomes optional
    public readonly bool fullscreen;
}
```

**Generated constructor:**
```csharp
public SettingsChanged(float volume, int quality, bool fullscreen = default)
{
    this.volume = volume;
    this.quality = quality;
    this.fullscreen = fullscreen;
}
```

**Usage:**
```csharp
// Both work!
var settings1 = new SettingsChanged(0.8f, 2, true);
var settings2 = new SettingsChanged(0.8f, 2);  // fullscreen defaults to false
```

## Why Use Attributes Instead of Manual Implementation?

### ❌ Manual Way (Verbose, Error-Prone)

```csharp
public readonly struct Heal : ITargetedMessage<Heal>
{
    public readonly int amount;

    // You write this yourself (boring!)
    public Heal(int amount)
    {
        this.amount = amount;
    }

    // Required plumbing (easy to mess up!)
    public Type MessageType => typeof(Heal);
}
```

### ✅ Attribute Way (Clean, Automatic)

```csharp
[DxTargetedMessage]
[DxAutoConstructor]
public readonly partial struct Heal
{
    public readonly int amount;
}
// Constructor and plumbing generated automatically!
```

**Benefits:**
- ✅ **Less code** - 50% fewer lines
- ✅ **Fewer bugs** - Can't forget fields in constructor
- ✅ **Cleaner** - Focus on data, not boilerplate
- ✅ **Refactor-safe** - Add field? Constructor updates automatically!

## Complete Example: Before & After

### Before (Manual - 20 lines)

```csharp
using DxMessaging.Core.Messages;

public readonly struct PlayerDamaged : IBroadcastMessage<PlayerDamaged>
{
    public readonly int amount;
    public readonly string damageType;
    public readonly GameObject source;

    public PlayerDamaged(int amount, string damageType, GameObject source)
    {
        this.amount = amount;
        this.damageType = damageType;
        this.source = source;
    }

    public Type MessageType => typeof(PlayerDamaged);
}
```

### After (Attributes - 9 lines!)

```csharp
using DxMessaging.Core.Attributes;

[DxBroadcastMessage]
[DxAutoConstructor]
public readonly partial struct PlayerDamaged
{
    public readonly int amount;
    public readonly string damageType;
    public readonly GameObject source;
}
```

**Result:** Same functionality, 55% less code, zero boilerplate!

## Advanced: Manual Implementation (When Attributes Aren't Enough)

Sometimes you might want full control and skip source generators. You can implement interfaces manually:

### Generic Message Interfaces (Zero-Boxing for Structs)

**For performance-critical code**, implement the generic interfaces directly:

```csharp
using DxMessaging.Core.Messages;

public readonly struct Heal : ITargetedMessage<Heal>
{
    public readonly int amount;

    public Heal(int amount)
    {
        this.amount = amount;
    }

    // Required for IMessage
    public Type MessageType => typeof(Heal);
}
```

**Why use generics?**
- Avoids boxing structs (important for performance)
- Provides stable `MessageType` without `GetType()` calls
- Same performance as attribute-based approach

**When to use:**
- Hot path messages (sent/received every frame)
- Very large structs where boxing matters
- When you want explicit control

**When to use attributes:**
- 99% of cases (they generate the same code!)
- Cleaner, less boilerplate
- Easier to maintain

## Extension Methods (Emit Helpers)

DxMessaging provides extension methods to make emitting messages easy:

```csharp
using DxMessaging.Core.Extensions;  // ← Don't forget this!
using UnityEngine;

// Untargeted (global)
var settings = new VideoSettingsChanged(1920, 1080);
settings.Emit();

// Targeted - GameObject overload
var heal = new Heal(10);
heal.EmitGameObjectTargeted(playerGameObject);

// Targeted - Component overload
heal.EmitComponentTargeted(playerComponent);

// Broadcast - GameObject overload
var dmg = new TookDamage(25);
dmg.EmitGameObjectBroadcast(enemyGameObject);

// Broadcast - Component overload
dmg.EmitComponentBroadcast(enemyComponent);

// String convenience (for quick prototyping)
"LevelCompleted".Emit();
```

**Located in:** `DxMessaging.Core.Extensions.MessageExtensions`

**Automatic overload selection:**
- Extension methods pick the right overload based on type
- Defaults to global `MessageHandler.MessageBus`
- Pass custom bus with optional parameter

## Local Bus Islands (Isolated Testing)

Create isolated message buses for tests or subsystems:

```csharp
using DxMessaging.Core;
using DxMessaging.Core.MessageBus;

// Create isolated bus
var testBus = new MessageBus();
var handler = new MessageHandler(new InstanceId(1)) { active = true };
var token = MessageRegistrationToken.Create(handler, testBus);

// Register on isolated bus
_ = token.RegisterUntargeted<MyMessage>(OnMessage);

// Emit to isolated bus (won't affect global bus!)
var msg = new MyMessage();
msg.Emit(testBus);
```

**Use cases:**
- Unit tests (no global side effects!)
- Subsystem isolation (UI has own bus)
- Sandboxing (mod systems, untrusted code)

## Attributes Quick Reference

| Attribute | Purpose | Example |
|-----------|---------|---------|
| `[DxUntargetedMessage]` | Mark as global message | `[DxUntargetedMessage]` |
| `[DxTargetedMessage]` | Mark as targeted message | `[DxTargetedMessage]` |
| `[DxBroadcastMessage]` | Mark as broadcast message | `[DxBroadcastMessage]` |
| `[DxAutoConstructor]` | Generate constructor | `[DxAutoConstructor]` |
| `[DxOptionalParameter]` | Make parameter optional | `[DxOptionalParameter] public readonly bool flag;` |

## Common Patterns with Attributes

### Pattern 1: Simple Message (No Data)

```csharp
[DxUntargetedMessage]
public readonly partial struct GamePaused { }
// No constructor needed - empty message
```

### Pattern 2: Message with Data

```csharp
[DxTargetedMessage]
[DxAutoConstructor]
public readonly partial struct Heal
{
    public readonly int amount;
}
// Auto-generates: Heal(int amount)
```

### Pattern 3: Message with Optional Fields

```csharp
[DxBroadcastMessage]
[DxAutoConstructor]
public readonly partial struct Attack
{
    public readonly int damage;
    [DxOptionalParameter]
    public readonly string damageType;
}
// Auto-generates: Attack(int damage, string damageType = default)
```

### Pattern 4: Complex Message

```csharp
[DxUntargetedMessage]
[DxAutoConstructor]
public readonly partial struct LevelCompleted
{
    public readonly int level;
    public readonly float time;
    public readonly int score;
    [DxOptionalParameter]
    public readonly bool perfectRun;
    [DxOptionalParameter]
    public readonly string bonusReason;
}
// All fields become constructor parameters, last two are optional
```

## FAQ: Source Generators & Attributes

### "Do I HAVE to use attributes?"

**No!** You can implement interfaces manually:

```csharp
// Manual (works fine, just more code)
public readonly struct MyMsg : IUntargetedMessage<MyMsg>
{
    public Type MessageType => typeof(MyMsg);
}

// Attributes (same result, less code)
[DxUntargetedMessage]
public readonly partial struct MyMsg { }
```

**Recommendation:** Use attributes unless you need explicit control.

### "Why `partial` when using attributes?"

Source generators need to **add code to your type**. The `partial` keyword allows them to extend your struct/class in a separate file.

```csharp
// Your file (MyMessage.cs)
[DxUntargetedMessage]
public readonly partial struct MyMessage { }

// Generated file (MyMessage.g.cs - auto-created!)
public readonly partial struct MyMessage : IUntargetedMessage<MyMessage>
{
    public Type MessageType => typeof(MyMessage);
}
```

**Forget `partial`?** You'll get a compile error: "Type must be partial to use source generators"

### "Can I see the generated code?"

**Yes!** In Visual Studio/Rider:
1. Right-click on the message type
2. Select "Go to Implementation" or "Go to Definition"
3. You'll see the auto-generated file

**Or** check your `obj/` folder for `.g.cs` files.

### "What if I want custom constructor logic?"

Use manual implementation:

```csharp
public readonly struct ComplexMessage : IUntargetedMessage<ComplexMessage>
{
    public readonly int value;

    public ComplexMessage(int rawValue)
    {
        // Custom logic
        value = Math.Clamp(rawValue, 0, 100);
    }

    public Type MessageType => typeof(ComplexMessage);
}
```

### "Do attributes affect runtime performance?"

**No!** Source generation happens at **compile time**. Generated code is identical to hand-written code. Zero runtime overhead.

### "Can I mix attributes and manual implementation?"

**Not on the same type**, but you can have:
- Some messages using attributes
- Other messages using manual implementation
- Mix and match across your codebase

```csharp
// Message A: Attributes
[DxUntargetedMessage]
[DxAutoConstructor]
public readonly partial struct MessageA { public readonly int value; }

// Message B: Manual
public readonly struct MessageB : IUntargetedMessage<MessageB>
{
    public readonly int value;
    public MessageB(int value) { this.value = value; }
    public Type MessageType => typeof(MessageB);
}

// Both work perfectly together!
```

## Troubleshooting Source Generators

### "Attributes not working / code not generated"

**Checklist:**
1. ✅ Is type marked `partial`?
2. ✅ Did you rebuild the project?
3. ✅ Is Unity 2021.3+ (Roslyn source generator support)?
4. ✅ Check `obj/` folder for `.g.cs` files

**Fix:**
```csharp
// ❌ Missing partial
[DxAutoConstructor]
public readonly struct MyMsg { }

// ✅ Correct
[DxAutoConstructor]
public readonly partial struct MyMsg { }
```

### "Constructor not generated"

**Cause:** No public fields to generate from

```csharp
// ❌ No public fields
[DxAutoConstructor]
public readonly partial struct Empty { }

// ✅ Has public field
[DxAutoConstructor]
public readonly partial struct WithData {
    public readonly int value;
}
```

### "Unity can't find generated code"

**Solution:**
1. Close Unity
2. Delete `Library/` folder
3. Reopen Unity
4. Let it reimport everything

## Related Documentation

- **[API Reference](Reference.md)** — Complete API documentation
- **[Message Types](MessageTypes.md)** — When to use Untargeted/Targeted/Broadcast
- **[Quick Reference](QuickReference.md)** — Cheat sheet
- **[Design & Architecture](DesignAndArchitecture.md)** — How source generation works internally

## Summary

**Source generators = Code wizards that write boilerplate for you**

**Use attributes for:**
- ✅ Clean, maintainable code
- ✅ Automatic constructor generation
- ✅ Zero boilerplate
- ✅ Refactor safety

**Use manual implementation for:**
- ✅ Custom constructor logic
- ✅ Explicit control
- ✅ Understanding exactly what happens

**Best practice:** Start with attributes (they cover 99% of cases), switch to manual only when needed!
