# Listening Patterns

Targeted across all targets

- Accept every targeted message of a given type regardless of who it’s for.

```csharp
using DxMessaging.Core;   // InstanceId
using DxMessaging.Core.Messages;

// Observe all Heal messages and their intended targets
_ = token.RegisterTargetedWithoutTargeting<Heal>(OnAnyHeal);
void OnAnyHeal(InstanceId target, Heal m) => Audit(target, m);

// Post‑process all targeted of type
_ = token.RegisterTargetedWithoutTargetingPostProcessor<Heal>(OnAnyHealPost);
void OnAnyHealPost(InstanceId target, Heal m) => Log(target, m);
```

Broadcast across all sources

- Accept every broadcast message of a given type regardless of who emitted it.

```csharp
using DxMessaging.Core;   // InstanceId
using DxMessaging.Core.Messages;

// Observe all TookDamage messages and their sources
_ = token.RegisterBroadcastWithoutSource<TookDamage>(OnAnyTookDamage);
void OnAnyTookDamage(InstanceId source, TookDamage m) => Track(source, m);

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

Real‑World Use Cases

## Development Debug Dump

Capture all messages during development for debugging and diagnostics:

```csharp
using DxMessaging.Core;
using DxMessaging.Core.Messages;
using UnityEngine;

public class DebugMessageLogger : MessageHandler
{
    public DebugMessageLogger() : base(new InstanceId(999)) { }

    public override void Handle(ref IUntargetedMessage message)
    {
        Debug.Log($"[Untargeted] {message.GetType().Name}: {message}");
    }

    public override void Handle(ref InstanceId target, ref ITargetedMessage message)
    {
        Debug.Log($"[Targeted → {target}] {message.GetType().Name}: {message}");
    }

    public override void Handle(ref InstanceId source, ref IBroadcastMessage message)
    {
        Debug.Log($"[Broadcast ← {source}] {message.GetType().Name}: {message}");
    }
}

// Register in development builds only
#if DEVELOPMENT_BUILD || UNITY_EDITOR
var logger = new DebugMessageLogger { active = true };
_ = MessageHandler.MessageBus.RegisterGlobalAcceptAll(logger);
#endif
```

### Attribute‑Based Network Replication

Automatically replicate messages marked with custom attributes across the network:

```csharp
using System;
using System.Reflection;
using System.Collections.Generic;
using DxMessaging.Core;
using DxMessaging.Core.Messages;

// Mark messages that should be replicated
[AttributeUsage(AttributeTargets.Struct)]
public class NetworkedAttribute : Attribute { }

[Networked]
[DxUntargetedMessage]
[DxAutoConstructor]
public readonly partial struct PlayerMoved
{
    public readonly Vector3 position;
}

[Networked]
[DxTargetedMessage]
[DxAutoConstructor]
public readonly partial struct DealDamage
{
    public readonly float amount;
}

// Network replication handler
public class NetworkReplicator : MessageHandler
{
    private readonly INetworkManager _network;
    private readonly HashSet<Type> _networkedTypes = new();

    public NetworkReplicator(INetworkManager network) : base(new InstanceId(1000))
    {
        _network = network;
        CacheNetworkedTypes();
    }

    private void CacheNetworkedTypes()
    {
        // Find all message types with [Networked] attribute
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            foreach (var type in assembly.GetTypes())
            {
                if (type.GetCustomAttribute<NetworkedAttribute>() != null)
                {
                    _networkedTypes.Add(type);
                }
            }
        }
    }

    public override void Handle(ref IUntargetedMessage message)
    {
        if (_networkedTypes.Contains(message.GetType()))
        {
            _network.Send(message);  // Serialize and send
        }
    }

    public override void Handle(ref InstanceId target, ref ITargetedMessage message)
    {
        if (_networkedTypes.Contains(message.GetType()))
        {
            _network.Send(target, message);
        }
    }

    public override void Handle(ref InstanceId source, ref IBroadcastMessage message)
    {
        if (_networkedTypes.Contains(message.GetType()))
        {
            _network.Send(source, message);
        }
    }
}

// Usage: any message with [Networked] automatically replicates
var replicator = new NetworkReplicator(networkManager) { active = true };
_ = MessageHandler.MessageBus.RegisterGlobalAcceptAll(replicator);

// These messages are now replicated across the network
var playerMoved = new PlayerMoved(playerPos);
playerMoved.Emit();
var dealDamage = new DealDamage(50f);
dealDamage.EmitTargeted(enemyId);
```

#### Message Analytics and Metrics

Track message frequency and performance across your entire game:

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using DxMessaging.Core;
using DxMessaging.Core.Messages;

public class MessageAnalytics : MessageHandler
{
    private readonly Dictionary<Type, (int count, long totalMs)> _stats = new();
    private readonly Stopwatch _stopwatch = new();

    public MessageAnalytics() : base(new InstanceId(1001)) { }

    public override void Handle(ref IUntargetedMessage message)
    {
        TrackMessage(message.GetType());
    }

    public override void Handle(ref InstanceId target, ref ITargetedMessage message)
    {
        TrackMessage(message.GetType());
    }

    public override void Handle(ref InstanceId source, ref IBroadcastMessage message)
    {
        TrackMessage(message.GetType());
    }

    private void TrackMessage(Type messageType)
    {
        _stopwatch.Restart();
        // Message processing happens here
        _stopwatch.Stop();

        if (!_stats.TryGetValue(messageType, out var stat))
        {
            stat = (0, 0);
        }
        _stats[messageType] = (stat.count + 1, stat.totalMs + _stopwatch.ElapsedMilliseconds);
    }

    public void PrintStats()
    {
        foreach (var kvp in _stats)
        {
            var avg = kvp.Value.totalMs / (double)kvp.Value.count;
            UnityEngine.Debug.Log($"{kvp.Key.Name}: {kvp.Value.count} messages, avg {avg:F2}ms");
        }
    }
}
```

When to Use Global Accept‑All

✅ **Good use cases:**

- Development‑time debugging and logging
- Cross‑cutting concerns (analytics, telemetry, metrics)
- Attribute‑based systems (networking, serialization, persistence)
- Testing and diagnostics tools
- Message replay/recording systems

⚠️ **Performance consideration:**
Global Accept-All handlers are invoked for **every** message of **every** type. For performance-sensitive gameplay logic, prefer type-specific registrations which use O(1) lookup instead of O(N) iteration.

❌ **Avoid for:**

- Core gameplay logic that only needs specific message types
- Hot paths with thousands of messages per frame
- Production code that can use specific type registrations instead

Tips

- Use across‑all listeners for diagnostics, analytics, or cross‑cutting observers.
- Prefer specific (target/source) registrations for gameplay logic.

Related

- [Interceptors & Ordering](InterceptorsAndOrdering.md)
- [Diagnostics](Diagnostics.md)
