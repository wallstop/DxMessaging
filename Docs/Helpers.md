# Helpers and Source Generation

## What Are Source Generators

**Source generators** are a C# feature (introduced in C# 9.0) that **automatically write code for you at compile time**. Think of them as smart code wizards that look at your code, see what you need, and generate the boilerplate automatically.

### In plain English

- You write: `[DxAutoConstructor]` on a struct
- Source generator sees: "Oh, they want a constructor!"
- Source generator creates: A constructor with all the fields as parameters
- You get: Less typing, fewer bugs, more consistent code

#### Learn more about source generators

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

##### What it generates

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

##### What it generates

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

##### What it generates

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

#### What you get (auto-generated)

```csharp
// You don't write this - it's generated for you!
public VideoSettingsChanged(int width, int height)
{
    this.width = width;
    this.height = height;
}
```

##### Rules

- Creates constructor parameters in **field declaration order**
- Only includes `public` fields
- Ignores `static` fields
- Works with `readonly` structs (recommended!)
- Supports nested types (types defined inside other classes/structs)
- Supports internal types (types with `internal` visibility)

### 3. `[DxOptionalParameter]` - Optional Constructor Parameters

Make constructor parameters optional with type-safe default values:

```csharp
[DxUntargetedMessage]
[DxAutoConstructor]
public readonly partial struct SettingsChanged
{
    public readonly float volume;
    public readonly int quality;
    [DxOptionalParameter]  // ← Defaults to type's default (false for bool)
    public readonly bool fullscreen;
    [DxOptionalParameter(100)]  // ← Custom default value
    public readonly int brightness;
}
```

#### Generated constructor

```csharp
public SettingsChanged(float volume, int quality, bool fullscreen = default, int brightness = 100)
{
    this.volume = volume;
    this.quality = quality;
    this.fullscreen = fullscreen;
    this.brightness = brightness;
}
```

##### Usage

```csharp
// All variations work!
var settings1 = new SettingsChanged(0.8f, 2, true, 80);
var settings2 = new SettingsChanged(0.8f, 2);  // fullscreen=false, brightness=100
var settings3 = new SettingsChanged(0.8f, 2, fullscreen: true);  // brightness=100
```

###### Supported default value types

- Numeric types: `int`, `uint`, `long`, `ulong`, `short`, `ushort`, `byte`, `sbyte`, `float`, `double`
- Text types: `char`, `string`
- Boolean: `bool`

```csharp
[DxOptionalParameter(42)]        // int default
public readonly int score;

[DxOptionalParameter("unknown")] // string default
public readonly string playerName;

[DxOptionalParameter(true)]      // bool default
public readonly bool isActive;
```

###### Advanced: Custom expressions for any type

For types that don't have built-in support (like enums, nullables, or custom structs), use the `Expression` property:

```csharp
[DxAutoConstructor]
public readonly partial struct ComplexMessage
{
    [DxOptionalParameter(Expression = "null")]
    public readonly string? optionalName;

    [DxOptionalParameter(Expression = "DamageType.Physical")]
    public readonly DamageType type;

    [DxOptionalParameter(Expression = "Vector3.zero")]
    public readonly Vector3 position;
}
```

###### How it works

- The expression is inserted verbatim into the generated constructor
- The C# compiler validates type safety automatically
- Perfect for enums, nullable types, static constants, or complex defaults

## Why Use Attributes Instead of Manual Implementation

### ✅ Attribute Definition (Clean, Automatic)

```csharp
[DxTargetedMessage]
[DxAutoConstructor]
public readonly partial struct Heal
{
    public readonly int amount;
}
```

### What the generator emits (for reference)

```csharp
// Auto-generated by DxMessaging (no need to hand-write this)
public readonly partial struct Heal : ITargetedMessage<Heal>
{
    public readonly int amount;

    public Heal(int amount)
    {
        this.amount = amount;
    }

    public Type MessageType => typeof(Heal);
}
```

#### Benefits

- ✅ **Less code** - 50% fewer lines
- ✅ **Fewer bugs** - Can't forget fields in constructor
- ✅ **Focused** - Focus on data, not boilerplate
- ✅ **Refactor-safe** - Add field? Constructor updates automatically!

## Complete Example: Attribute Definition vs Generated Output

### Attribute Definition (8 lines)

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

### Generated Output (20 lines you never write)

```csharp
// Auto-generated by DxMessaging (for reference only)
public readonly partial struct PlayerDamaged : IBroadcastMessage<PlayerDamaged>
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

### Result

- ✅ Same functionality
- ✅ Less code to maintain
- ✅ Automatically updates when you add/remove fields
- ✅ Works for class messages too
- ✅ Zero effort once you mark the struct partial

## Advanced: Manual Implementation (When Attributes Aren't Enough)

Attributes cover almost every scenario. If you intentionally drop `[DxTargetedMessage]`, `[DxUntargetedMessage]`, or `[DxBroadcastMessage]`, you'll need to hand-write the interface implementations and constructors shown in the “generated output” examples. Keep the attributes unless you have a very specific data-backed reason not to.

### Generic Message Interfaces (Zero-Boxing for Structs)

`readonly struct` messages marked with the attributes already implement the generic interfaces, so emissions stay allocation-free. You get the same performance characteristics as the manual approach without writing any plumbing.

```csharp
[DxTargetedMessage]
[DxAutoConstructor]
public readonly partial struct Heal
{
    public readonly int amount;
}
```

### "Do I HAVE to use attributes?"

Technically no—but without them you must write the constructor, interface implementation, and `MessageType` property yourself (for speed, you can optionally leave this off, but it might box on certain call paths). Leaving the attributes on keeps everything consistent for the whole team.

```csharp
[DxUntargetedMessage]
[DxAutoConstructor]
public readonly partial struct MyMsg { }
```

### "What if I want custom constructor logic?"

Keep the attributes and add a factory/helper so you still benefit from the generated constructor:

```csharp
[DxUntargetedMessage]
[DxAutoConstructor]
public readonly partial struct ComplexMessage
{
    public readonly int value;

    public static ComplexMessage FromRaw(int rawValue)
    {
        int clamped = Math.Clamp(rawValue, 0, 100);
        return new ComplexMessage(clamped);
    }
}
```

If you truly must write a custom constructor, drop `[DxAutoConstructor]` for that type but keep the `[DxUntargetedMessage]`/`[DxTargetedMessage]` attribute so the interface plumbing stays consistent.

### "Can I mix attribute-based and manual messages?"

Yes. Attribute-driven messages happily coexist with string messages and any manual implementations you already have. You can migrate gradually by converting one message at a time:

```csharp
[DxUntargetedMessage]
[DxAutoConstructor]
public readonly partial struct MessageA
{
    public readonly int value;
}

// Existing manual message types keep working alongside attribute-driven ones.
```

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

### Automatic overload selection

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
var handler = new MessageHandler(new InstanceId(1), testBus) { active = true };
var token = MessageRegistrationToken.Create(handler, testBus);

// Register on isolated bus
_ = token.RegisterUntargeted<MyMessage>(OnMessage);

// Emit to isolated bus (won't affect global bus!)
var msg = new MyMessage();
msg.Emit(testBus);
```

### Use cases

- Unit tests (no global side effects!)
- Subsystem isolation (UI has own bus)
- Sandboxing (mod systems, untrusted code)

## Attributes Quick Reference

| Attribute               | Purpose                              | Example                                                               |
| ----------------------- | ------------------------------------ | --------------------------------------------------------------------- |
| `[DxUntargetedMessage]` | Mark as global message               | `[DxUntargetedMessage]`                                               |
| `[DxTargetedMessage]`   | Mark as targeted message             | `[DxTargetedMessage]`                                                 |
| `[DxBroadcastMessage]`  | Mark as broadcast message            | `[DxBroadcastMessage]`                                                |
| `[DxAutoConstructor]`   | Generate constructor                 | `[DxAutoConstructor]`                                                 |
| `[DxOptionalParameter]` | Make parameter optional (default)    | `[DxOptionalParameter] public readonly bool flag;`                    |
| `[DxOptionalParameter]` | Make parameter optional (custom)     | `[DxOptionalParameter(42)] public readonly int count;`                |
| `[DxOptionalParameter]` | Make parameter optional (expression) | `[DxOptionalParameter(Expression = "null")] public readonly string?;` |

### Works with

- Top-level types (public structs/classes)
- Nested types (types inside other classes)
- Internal types (assembly-private messages)

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

### Pattern 5: Nested Types

Define messages inside other classes for better organization:

```csharp
public partial class GameEvents
{
    [DxUntargetedMessage]
    [DxAutoConstructor]
    public readonly partial struct LevelUp
    {
        public readonly int newLevel;
    }

    [DxTargetedMessage]
    [DxAutoConstructor]
    public readonly partial struct GainExperience
    {
        public readonly int amount;
    }
}

// Usage:
var levelUp = new GameEvents.LevelUp(5);
levelUp.Emit();
```

#### Benefits

- Organizes related messages into namespaces or classes
- Reduces global namespace pollution
- Works identically to top-level messages

### Pattern 6: Internal Types (Assembly-Private Messages)

Keep implementation details private to your assembly:

```csharp
[DxUntargetedMessage]
[DxAutoConstructor]
internal readonly partial struct InternalDebugMessage
{
    public readonly string debugInfo;
}

// Only visible within this assembly
// Perfect for internal messaging that shouldn't leak to other packages
```

#### Use cases

- Implementation details that shouldn't be public API
- Plugin or package-internal messaging
- Test-only messages

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
   $11. Select "Go to Implementation" or "Go to Definition"
   $11. You'll see the auto-generated file

**Or** check your `obj/` folder for `.g.cs` files.

### "What if I want custom constructor logic?"

Keep the attributes and wrap the generated constructor with a helper so you can inject custom logic without losing the source-generated plumbing:

```csharp
[DxUntargetedMessage]
[DxAutoConstructor]
public readonly partial struct ComplexMessage
{
    public readonly int value;

    public static ComplexMessage FromRaw(int rawValue)
    {
        int clamped = Math.Clamp(rawValue, 0, 100);
        return new ComplexMessage(clamped);
    }
}
```

If you truly need to hand-craft the constructor, drop `[DxAutoConstructor]` for that specific type but keep the `[DxUntargetedMessage]`/`[DxTargetedMessage]` attribute so the interface implementation is still generated.

### "Do attributes affect runtime performance?"

**No!** Source generation happens at **compile time**. Generated code is identical to hand-written code. Zero runtime overhead.

### "Can I mix attributes and manual implementation?"

Yes. Attribute-driven messages happily coexist with any legacy manual messages or string messages you already emit. Convert types gradually—one message at a time:

```csharp
[DxUntargetedMessage]
[DxAutoConstructor]
public readonly partial struct MessageA
{
    public readonly int value;
}

// Existing manual messages keep working alongside attribute-driven ones.
```

## Troubleshooting Source Generators

### "Attributes not working / code not generated"

#### Checklist

1. ✅ Is type marked `partial`?
1. ✅ Did you rebuild the project?
1. ✅ Is Unity 2021.3+ (Roslyn source generator support)?
1. ✅ Check `obj/` folder for `.g.cs` files

##### Fix

```csharp
// ❌ Missing partial, will not compile
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

#### Solution

1. Close Unity
1. Delete `Library/` folder
1. Reopen Unity
1. Let it reimport everything

## Related Documentation

- **[API Reference](Reference.md)** — Complete API documentation
- **[Message Types](MessageTypes.md)** — When to use Untargeted/Targeted/Broadcast
- **[Quick Reference](QuickReference.md)** — Cheat sheet
- **[Design & Architecture](DesignAndArchitecture.md)** — How source generation works internally

## Summary

### Source generators = Code wizards that write boilerplate for you

#### Use attributes for

- ✅ Clean, maintainable code
- ✅ Automatic constructor generation
- ✅ Zero boilerplate
- ✅ Refactor safety

##### Use manual implementation for

- ✅ Custom constructor logic
- ✅ Explicit control
- ✅ Understanding exactly what happens

**Recommendation:** Start with attributes (they cover most cases), switch to manual only when needed.
