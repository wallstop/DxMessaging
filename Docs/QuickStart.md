# Quick Start — Your First Message in 5 Minutes

[← Back to Index](Index.md) | [Getting Started](GettingStarted.md) | [Visual Guide](VisualGuide.md) | [Samples~](../Samples~/)

---

**Goal:** Get a working message system in 5 minutes. Copy, paste, run. No explanations yet — just results!

**Stuck?** → [Troubleshooting](Troubleshooting.md) | [FAQ](FAQ.md)

---

## Step 0: Install (30 seconds)

Unity Package Manager → Add package from git URL:

```text
https://github.com/wallstop/DxMessaging.git
```

**Requirements:** Unity 2021.3+ | .NET Standard 2.1 | All render pipelines supported

---

## Your First Message (3 Steps)

### Step 1: Define messages

```csharp
using DxMessaging.Core.Messages;
using DxMessaging.Core.Attributes;

// Recommended: attributes + auto constructor (beginner‑friendly)
[DxUntargetedMessage]
[DxAutoConstructor]
public readonly partial struct WorldRegenerated
{
    public readonly int seed;
}

[DxUntargetedMessage]
[DxAutoConstructor]
public readonly partial struct VideoSettingsChanged
{
    public readonly int width;
    public readonly int height;
}

[DxTargetedMessage]
[DxAutoConstructor]
public readonly partial struct Heal
{
    public readonly int amount;
}

[DxBroadcastMessage]
[DxAutoConstructor]
public readonly partial struct TookDamage
{
    public readonly int amount;
}

// Performance option: generic interfaces on structs (zero boxing)
// public readonly struct Heal : ITargetedMessage<Heal> { public readonly int amount; public Heal(int amount) { this.amount = amount; } }
```

### Step 2: Add a messaging component

```csharp
using DxMessaging.Unity;
using DxMessaging.Core.Messages;
using UnityEngine;

public sealed class HealthUI : MessageAwareComponent
{
    protected override void RegisterMessageHandlers()
    {
        base.RegisterMessageHandlers();
        _ = Token.RegisterUntargeted<WorldRegenerated>(OnWorld);
        _ = Token.RegisterComponentTargeted<TookDamage>(this, OnDamage);
    }

    private void OnWorld(ref WorldRegenerated m) { /* update UI */ }
    private void OnDamage(ref TookDamage m) { /* flash damage */ }
}
```

### Step 3: Send messages

```csharp
using DxMessaging.Core.Extensions;   // Emit helpers
using UnityEngine;

// Untargeted (global)
var world = new WorldRegenerated(42);
world.Emit();

// Targeted (to a specific component or GameObject)
var heal = new Heal(10);
heal.EmitGameObjectTargeted(gameObject);     // no InstanceId cast needed

// Broadcast (from a specific source)
var hit = new TookDamage(5);
hit.EmitGameObjectBroadcast(gameObject);     // no InstanceId cast needed

// String convenience (global)
"Saved".Emit();
```

---

## ✅ Done! You just

1. ✅ Defined 4 message types
1. ✅ Created a component that listens
1. ✅ Sent messages from anywhere

**No manual unsubscribe. No memory leaks. Full type safety.**

---

## 🎯 What You Just Built

- **Untargeted messages** (`WorldRegenerated`, `VideoSettingsChanged`) → Global announcements anyone can hear
- **Targeted messages** (`Heal`) → Commands to a specific object
- **Broadcast messages** (`TookDamage`) → Events from a source that others observe

---

## 🚀 Next Steps

**Understand What You Did:**

- → [Getting Started Guide](GettingStarted.md) (10 min) — Full explanation with mental models
- → [Visual Guide](VisualGuide.md) (5 min) — Pictures and analogies

**Try Real Examples:**

- → [Samples~/Mini Combat](../Samples~/Mini%20Combat/README.md) — Working combat example
- → [Samples~/UI Buttons + Inspector](../Samples~/UI%20Buttons%20%2B%20Inspector/README.md) — See diagnostics in action

**Go Deeper:**

- → [Message Types](MessageTypes.md) (10 min) — When to use which type
- → [Common Patterns](Patterns.md) (15 min) — Real-world solutions
- → [Interceptors & Ordering](InterceptorsAndOrdering.md) (10 min) — Advanced control

**Reference:**

- → [Quick Reference](QuickReference.md) — Cheat sheet
- → [API Reference](Reference.md) — Complete API
- → [Troubleshooting](Troubleshooting.md) — Fix common issues

---

## 💡 Quick Tips

**Do's:**

- ✅ Use `MessageAwareComponent` for Unity components (automatic lifecycle)
- ✅ Store struct in variable before emitting: `var msg = new Heal(10); msg.Emit();`
- ✅ Call `base.RegisterMessageHandlers()` when overriding

**Don'ts:**

- ❌ Don't emit from temporaries: `new Heal(10).Emit()` won't compile correctly
- ❌ Don't use Untargeted for commands to one object (use Targeted instead)
- ❌ Don't forget `using DxMessaging.Core.Extensions;` for `Emit*` methods
