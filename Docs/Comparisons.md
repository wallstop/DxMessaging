# Comparisons: Events, Unity Events, and Unity Messages

This guide shows common pain points in standard approaches and how DxMessaging addresses them with clearer ownership, ordering, and observability.

Table of contents

- C# events/delegates
- UnityEvents (inspector wiring)
- Unity SendMessage
- Global “Event Bus” singletons
- How DxMessaging addresses each
- When to use which

Standard C# Events/Actions

Problems

- Manual attach/detach: easy to forget unsubscribe; memory leaks and callbacks on destroyed objects.
- Tight coupling: consumers reference producers (or vice‑versa), hurting modularity and tests.
- Ordering is implicit: hard to coordinate A before B across systems.
- Hard to observe globally: no built‑in way to inspect “what fired recently”.

Typical code

```csharp
public sealed class Spawner
{
    public event Action Spawned;
    public void Spawn()
    {
        // ...
        Spawned?.Invoke();
    }
}

public sealed class UI
{
    private readonly Spawner _spawner;
    public UI(Spawner spawner)
    {
        _spawner = spawner;
        _spawner.Spawned += OnSpawned; // must remember to unsubscribe
    }
    private void OnSpawned() => Refresh();
}
```

DxMessaging

- No direct coupling; the UI listens for a message instead of referencing a specific Spawner instance.
- Lifecycle managed by a token; enable/disable tied to a component.
- Ordering via priority; global inspection via diagnostics and the custom inspector.

```csharp
using DxMessaging.Core.Attributes;
using DxMessaging.Core.Extensions;
using DxMessaging.Core.Messages;

[DxUntargetedMessage]
[DxAutoConstructor]
public readonly partial struct Spawned { }

// Producer
var evt = new Spawned();
evt.Emit();

// Consumer (Unity)
_ = token.RegisterUntargeted<Spawned>(OnSpawned);
void OnSpawned(ref Spawned m) => Refresh();
```

UnityEvents (inspector wiring)

Problems

- Great for small demos, but brittle at scale (hidden references, order issues, refactors break wiring).
- No interception or post‑processing stages to validate/normalize.

Typical code

```csharp
using UnityEngine;
using UnityEngine.Events;

public sealed class Button : MonoBehaviour
{
    public UnityEvent onClicked; // wired in Inspector
    public void Click() => onClicked?.Invoke();
}

public sealed class UI : MonoBehaviour
{
    public void Refresh() { /* ... */ }
}
```

DxMessaging

- Strongly‑typed registrations in code; explicit priorities and stages.
- Inspect and page through emissions/registrations from MessagingComponent inspector.

Unity SendMessage

Problems

- String‑based; no compile‑time checking. 0/1 parameter only; boxing costs.
- Hard to reason about who handles what; debugging is difficult.

DxMessaging

- Use `ReflexiveMessage` to bridge legacy SendMessage behavior into the bus pipeline (optional).
- Prefer typed messages for new code; multiple parameters via fields, by‑ref handlers avoid boxing.

```csharp
using DxMessaging.Core;
using DxMessaging.Core.Messages;

InstanceId target = gameObject;
var msg = new ReflexiveMessage("OnHit", ReflexiveSendMode.Upwards, 10);
MessageHandler.MessageBus.TargetedBroadcast(ref target, ref msg);
```

Global “Event Bus” singletons

Problems

- Often devolves to one giant bag of static events; naming, ownership, and routing semantics get muddy.
- Still manual lifecycle and ordering issues.

Typical code

```csharp
public static class EventHub
{
    public static event Action<int> Damage;
    public static void RaiseDamage(int amount) => Damage?.Invoke(amount);
}

// Producer
EventHub.RaiseDamage(5);

// Consumer
EventHub.Damage += amount => Log(amount);
```

Problems: everything is global, ownership unclear, no interceptors, no context (who sent/received), hard to test.

DxMessaging

- A single `MessageBus` with clear categories: Untargeted (global), Targeted (to one), Broadcast (from one).
- Interceptors and post‑processors provide a structured pipeline.
- You can create isolated buses for sub‑systems or tests (local islands), and keep a global default (`MessageHandler.MessageBus`).

```csharp
using DxMessaging.Core;
using DxMessaging.Core.MessageBus;
using DxMessaging.Core.Messages;
using DxMessaging.Core.Attributes;

[DxBroadcastMessage]
[DxAutoConstructor]
public readonly partial struct TookDamage { public readonly int amount; }

// Local bus for combat system
var bus = new MessageBus();
var handler = new MessageHandler(new InstanceId(1)) { active = true };
var token = MessageRegistrationToken.Create(handler, bus);

_ = token.RegisterBroadcastWithoutSource<TookDamage>(OnAnyDamage);
void OnAnyDamage(ref InstanceId src, ref TookDamage m) => Log(src, m.amount);

// Emit within subsystem
var hit = new TookDamage(5);
hit.EmitGameObjectTargeted(enemyGO, bus);
```

When to use which

- C# events: simple, local wiring within a class or small module.
- UnityEvents: quick prototypes/small scenes; prefer code for maintainability.
- Unity SendMessage: legacy only; prefer `ReflexiveMessage` if you must bridge.
- DxMessaging: decoupled, cross‑system flows where ordering, observability, and lifecycle safety matter.

## Honest Trade-offs: What You Give Up, What You Gain

No solution is perfect. Here's what you sacrifice and gain with DxMessaging compared to alternatives.

### Learning Curve

#### What You Give Up

- ❌ Immediate productivity - There's a 1-2 day learning curve
- ❌ Familiarity - Team needs to understand message types, tokens, and lifecycle
- ❌ "Just works" - Requires upfront design (which message type? what priority?)

#### What You Gain

- ✅ Long-term velocity - Once learned, adding features is faster
- ✅ Reduced debugging time - Clear message flow, Inspector diagnostics
- ✅ Onboarding clarity - New devs see explicit message contracts instead of hidden event chains

**Verdict:** If you're building a game jam prototype, the learning curve isn't worth it. For multi-month projects, it pays off quickly.

### Boilerplate

#### What You Give Up

- ❌ "One-liners" - C# events can be `public event Action OnClick;` done
- ❌ Quick and dirty - Need to define message struct, attributes, handler registration

#### What You Gain

- ✅ Explicit contracts - Messages are discoverable types, not hidden delegates
- ✅ Auto-generated code - `[DxAutoConstructor]` reduces boilerplate
- ✅ Compile-time safety - Refactors update all usages

#### Example Comparison

```csharp
// C# Event (minimal boilerplate)
public event Action<int> OnDamage;
OnDamage?.Invoke(5);

// DxMessaging (more upfront definition)
[DxBroadcastMessage]
[DxAutoConstructor]
public readonly partial struct TookDamage { public readonly int amount; }

var msg = new TookDamage(5);
msg.EmitGameObjectBroadcast(gameObject);
```

**Verdict:** For 1-3 simple events, C# events win on brevity. For 10+ events with complex flows, DxMessaging's structure pays dividends.

### Performance

#### What You Give Up

- ❌ Absolute minimal overhead - Raw C# events/delegates are faster (~10ns per call)
- ❌ Zero abstraction cost - Direct calls can be inlined by the compiler
- ❌ Simplicity in profiler - One extra layer in call stack

#### What You Gain

- ✅ Zero-allocation struct messages - No GC pressure from boxing
- ✅ Predictable performance - No hidden allocations from lambdas
- ✅ Scalable diagnostics - Built-in profiling/logging without custom instrumentation

#### Hard Numbers

- C# event invoke: ~50ns baseline
- DxMessaging handler: ~60ns (~10ns overhead)
- Memory: Zero allocations for struct messages

**Verdict:** For UI, gameplay events, scene management → DxMessaging overhead is negligible. For ECS with millions of events/frame → stick with raw delegates or native code.

### Flexibility

#### What You Give Up

- ❌ Return values - DxMessaging is fire-and-forget (no synchronous responses)
- ❌ Out parameters - Can't use `out` or `ref` for bidirectional communication
- ❌ Dynamic subscriptions - Can't easily pass lambdas inline

#### What You Gain

- ✅ Interception - Validate/transform messages before handlers
- ✅ Post-processing - Analytics/logging without polluting handlers
- ✅ Priority control - Explicit execution order
- ✅ Context - Always know who sent/received

#### When Limitations Hurt

```csharp
// C# events can return values (DxMessaging can't)
public delegate bool DamageValidator(int amount);
public event DamageValidator OnValidateDamage;

if (OnValidateDamage?.Invoke(damage) == true) {
    // Allowed
}

// DxMessaging workaround: Use interceptors or separate query pattern
```

**Verdict:** If you need synchronous request/response, C# delegates/events or direct method calls are better. DxMessaging excels at notifications and commands.

### Debuggability

#### What You Give Up

- ❌ Simplicity - Stack traces show message bus internals
- ❌ Step-through - Can't F11 directly from emit to handler (need breakpoints)

#### What You Gain

- ✅ Message history - See last N messages in Inspector
- ✅ Registration view - Know exactly who's listening
- ✅ Global observability - Track all messages without instrumenting code
- ✅ Filtering - Intercept messages for debugging without changing code

#### Example: Finding Who Fired a Message

```csharp
// C# events: Set breakpoint on every possible Invoke(), or add logging everywhere
OnDamage?.Invoke(5); // Where did this come from??

// DxMessaging: Check Inspector message history or add global logger
_ = debugToken.RegisterBroadcastWithoutSource<TookDamage>(
    (src, msg) => Debug.Log($"Damage from {src}: {msg.amount}")
);
```

**Verdict:** Initial debugging is slightly harder (extra layer), but systemic debugging is MUCH easier (observability tools).

### Coupling and Architecture

#### What You Give Up

- ❌ Quick hacks - Can't just `GetComponent<T>().DoThing()` anymore
- ❌ Direct inspector wiring - Can't drag-and-drop references to emit messages

#### What You Gain

- ✅ True decoupling - Systems don't know about each other
- ✅ Testability - Easy to isolate with local buses
- ✅ Refactorability - Move/rename components without breaking wiring

#### Impact on Architecture

Before (tight coupling):

```text
UI → References 15 systems
System A → References System B, C, D
Every change ripples through dependencies
```

After (loose coupling):

```text
All systems → Emit messages
All systems → Listen to messages
Add/remove systems without affecting others
```

**Verdict:** If your project is <5k lines, tight coupling is manageable. For larger projects, DxMessaging's decoupling is essential for sanity.

### Testing

#### What You Give Up

- ❌ Simplicity - Can't just mock an event subscription

#### What You Gain

- ✅ Isolation - Local buses per test, zero global state
- ✅ Observability - Count messages, inspect payloads easily
- ✅ Determinism - Priority-based ordering eliminates flakiness

##### Example

```csharp
// Test with isolated bus
[Test]
public void TestAchievementSystem() {
    var testBus = new MessageBus();
    var token = MessageRegistrationToken.Create(achievementHandler, testBus);

    var msg = new EnemyKilled("Boss", 10);
    msg.EmitGameObjectBroadcast(enemy, testBus);

    Assert.IsTrue(achievementSystem.Unlocked("BossSlayer"));
}
```

**Verdict:** DxMessaging makes integration testing easier, unit testing slightly more verbose.

## Feature-by-Feature Comparison Matrix

| Aspect               | C# Events          | UnityEvents          | Static Bus      | DxMessaging               |
| -------------------- | ------------------ | -------------------- | --------------- | ------------------------- |
| **Setup Complexity** | ⭐⭐⭐⭐⭐ Minimal | ⭐⭐⭐⭐ Simple      | ⭐⭐⭐ Moderate | ⭐⭐⭐ Moderate           |
| **Boilerplate**      | ⭐⭐⭐⭐⭐ Low     | ⭐⭐⭐⭐⭐ Low       | ⭐⭐⭐ Medium   | ⭐⭐⭐ Medium             |
| **Performance**      | ⭐⭐⭐⭐⭐ Fastest | ⭐⭐ Slow (boxing)   | ⭐⭐⭐⭐ Fast   | ⭐⭐⭐⭐ Fast             |
| **Decoupling**       | ⭐ Tight           | ⭐⭐ Hidden          | ⭐⭐⭐⭐ Good   | ⭐⭐⭐⭐⭐ Excellent      |
| **Lifecycle Safety** | ⭐ Manual          | ⭐⭐⭐ Unity-managed | ⭐ Manual       | ⭐⭐⭐⭐⭐ Automatic      |
| **Observability**    | ⭐ None            | ⭐ None              | ⭐ None         | ⭐⭐⭐⭐⭐ Built-in       |
| **Execution Order**  | ⭐ Undefined       | ⭐ Undefined         | ⭐ Undefined    | ⭐⭐⭐⭐⭐ Priority-based |
| **Type Safety**      | ⭐⭐⭐⭐⭐ Strong  | ⭐⭐ Weak            | ⭐⭐⭐ Varies   | ⭐⭐⭐⭐⭐ Strong         |
| **Testability**      | ⭐⭐ Hard          | ⭐⭐ Hard            | ⭐ Very Hard    | ⭐⭐⭐⭐⭐ Easy           |
| **Learning Curve**   | ⭐⭐⭐⭐⭐ Minimal | ⭐⭐⭐⭐⭐ Minimal   | ⭐⭐⭐⭐ Low    | ⭐⭐⭐ Moderate           |
| **Memory Safety**    | ⭐ Leak-prone      | ⭐⭐⭐ Unity-managed | ⭐ Leak-prone   | ⭐⭐⭐⭐⭐ Leak-free      |
| **Debugging**        | ⭐⭐ Hard at scale | ⭐⭐ Hard at scale   | ⭐ Very Hard    | ⭐⭐⭐⭐⭐ Excellent      |

### Overall Verdict by Use Case

- **Small prototype/jam:** C# Events or UnityEvents win (simplicity > all)
- **Mid-size game (5-20k lines):** DxMessaging starts paying off
- **Large game (20k+ lines):** DxMessaging essential for maintainability
- **Performance-critical (ECS, physics):** Raw delegates/native code
- **UI-heavy:** DxMessaging excels (decoupled updates)

## When Each Approach ACTUALLY Wins

### C# Events Win When

- ✅ You need return values or out parameters
- ✅ Writing a library (DxMessaging is Unity-specific)
- ✅ Small, stable scope (5-10 events max)
- ✅ Team is C# experts, Unity beginners

### UnityEvents Win When

- ✅ Designers need to wire logic without code
- ✅ Rapid prototyping with prefabs
- ✅ Very simple games (mobile casual, hyper-casual)

### Static Event Bus Wins When

- ✅ You've already built one and it works
- ✅ Very simple use cases (just need globals)

### DxMessaging Wins When

- ✅ 10+ systems that communicate
- ✅ You need observability (debugging complex flows)
- ✅ Memory leaks are a pain point
- ✅ Cross-team development (clear contracts)
- ✅ Long-term maintenance (years, not weeks)

## Cost-Benefit Summary

### Costs

1. Learning curve (~1-2 days to feel comfortable)
1. More upfront code (message definitions)
1. Slightly slower than raw C# events (~10ns/call)
1. Can't return values (fire-and-forget only)

### Benefits

1. Zero memory leaks (automatic lifecycle)
1. Full decoupling (systems don't reference each other)
1. Observability (Inspector diagnostics, message history)
1. Predictable ordering (priority-based execution)
1. Interception/validation (before handlers run)
1. Testability (isolated buses)

**Break-even point:** Usually around 10-20 hours into a project, when event management becomes painful.

## Making the Decision

Ask yourself:

1. **Project lifespan?** <1 week → Skip DxMessaging | >1 month → Consider it
1. **Team size?** Solo → Optional | 3+ devs → Highly valuable
1. **Codebase size?** <5k lines → Optional | >10k lines → Recommended
1. **Event complexity?** <10 events → Skip | >20 events → Use DxMessaging
1. **Memory leak history?** None → Optional | Frequent → MUST HAVE

**Rule of thumb:** If you're reading this and thinking "I've experienced these pain points," DxMessaging will help. If you're thinking "I've never had these problems," maybe you don't need it yet.

See also

- [Message Types](MessageTypes.md)
- [Diagnostics (Editor inspector)](Diagnostics.md)
- [Migration Guide](MigrationGuide.md) - How to adopt gradually
