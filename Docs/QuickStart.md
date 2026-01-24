# Quick Start — Your First Message in 5 Minutes

[← Back to Index](Index.md) | [Getting Started](GettingStarted.md) | [Visual Guide](VisualGuide.md) | [Samples](../Samples~/)

---

**Goal:** Get a working message system in 5 minutes. Copy, paste, run.

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

// Performance option: keep [DxTargetedMessage] on a readonly struct to stay zero-boxing friendly, and drop [DxAutoConstructor] only if you need custom constructor logic.

// Optional parameters with custom defaults
[DxTargetedMessage]
[DxAutoConstructor]
public readonly partial struct HealAdvanced
{
    public readonly int amount;
    [DxOptionalParameter(true)]  // Custom default value
    public readonly bool showEffect;
    [DxOptionalParameter(Expression = "Color.green")]  // Expression for any type
    public readonly Color effectColor;
}
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

## Summary

You have:

1. Defined 4 message types
1. Created a component that listens
1. Sent messages from anywhere

Registration cleanup is automatic. Messages are type-safe.

---

## What You Built

- **Untargeted messages** (`WorldRegenerated`, `VideoSettingsChanged`) → Global announcements anyone can hear
- **Targeted messages** (`Heal`) → Commands to a specific object
- **Broadcast messages** (`TookDamage`) → Events from a source that others observe

---

## Next Steps

### Understand What You Did

- → [Getting Started Guide](GettingStarted.md) (10 min) — Full explanation with mental models
- → [Visual Guide](VisualGuide.md) (5 min) — Pictures and analogies

#### Try Real Examples

- → [Mini Combat sample](../Samples~/Mini%20Combat/README.md) — Working combat example
- → [UI Buttons + Inspector sample](../Samples~/UI%20Buttons%20%2B%20Inspector/README.md) — See diagnostics in action

##### Go Deeper

- → [Message Types](MessageTypes.md) (10 min) — When to use which type
- → [Common Patterns](Patterns.md) (15 min) — Real-world solutions
- → [Interceptors & Ordering](InterceptorsAndOrdering.md) (10 min) — Advanced control

###### Reference

- → [Quick Reference](QuickReference.md) — Cheat sheet
- → [API Reference](Reference.md) — Complete API
- → [Troubleshooting](Troubleshooting.md) — Fix common issues

---

## Quick Tips

### Do's

- ✅ Use `MessageAwareComponent` for Unity components (automatic lifecycle)
- ✅ Store struct in variable before emitting: `var msg = new Heal(10); msg.Emit();`
- ✅ Call `base.RegisterMessageHandlers()` when overriding

#### Don'ts

- ❌ Don't emit from temporaries: `new Heal(10).Emit()` won't compile (struct emit methods require `ref this`)
- ❌ Don't use Untargeted for commands to one object (use Targeted instead)
- ❌ Don't forget `using DxMessaging.Core.Extensions;` for `Emit*` methods
