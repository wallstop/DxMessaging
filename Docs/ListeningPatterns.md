# Listening Patterns

Targeted across all targets

- Accept every targeted message of a given type regardless of who it’s for.

```csharp
using DxMessaging.Core;   // InstanceId
using DxMessaging.Core.Messages;

// Observe all Heal messages and their intended targets
_ = token.RegisterTargetedWithoutTargeting<Heal>(OnAnyHeal);
void OnAnyHeal(ref InstanceId target, ref Heal m) => Audit(target, m);

// Post‑process all targeted of type
_ = token.RegisterTargetedWithoutTargetingPostProcessor<Heal>(OnAnyHealPost);
void OnAnyHealPost(ref InstanceId target, ref Heal m) => Log(target, m);
```

Broadcast across all sources

- Accept every broadcast message of a given type regardless of who emitted it.

```csharp
using DxMessaging.Core;   // InstanceId
using DxMessaging.Core.Messages;

// Observe all TookDamage messages and their sources
_ = token.RegisterBroadcastWithoutSource<TookDamage>(OnAnyTookDamage);
void OnAnyTookDamage(ref InstanceId source, ref TookDamage m) => Track(source, m);

// Post‑process all broadcast of type
_ = token.RegisterBroadcastWithoutSourcePostProcessor<TookDamage>(OnAnyTookDamagePost);
void OnAnyTookDamagePost(InstanceId source, TookDamage m) => Log(source, m);
```

Global accept‑all (debug/inspection)

- Receive every message of every type on a handler; useful for tooling.

```csharp
using DxMessaging.Core;

var bus = MessageHandler.MessageBus;
var handler = new MessageHandler(new InstanceId(1)) { active = true };
var dereg = bus.RegisterGlobalAcceptAll(handler);
// implement handler callbacks for generic categories on your MessageHandler
```

Tips

- Use across‑all listeners for diagnostics, analytics, or cross‑cutting observers.
- Prefer specific (target/source) registrations for gameplay logic.

Related

- [Interceptors & Ordering](InterceptorsAndOrdering.md)
- [Diagnostics](Diagnostics.md)
