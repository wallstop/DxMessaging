# DxMessaging
Game engine agnostic robust, synchronous pub/sub C# messaging solution, mostly geared towards Unity and XNA/Monogame. See [this recorded talk](https://fathom.video/share/qjs8pn1MAwGb-yTAaW5WpZWdjzxcdwFR).

# CI/CD Status
![Npm Publish](https://github.com/wallstop/DxMessaging/actions/workflows/npm-publish.yml/badge.svg)

# Compatibility
| Platform | Compatible |
| --- | --- |
| Unity 2021 | Likely, but untested |
| Unity 2022 | &check; |
| Unity 2023 | &check; |
| Unity 6 | &check; |
| URP | &check; |
| HDRP | &check; |

# Installation

## From Releases
Check out the latest [Releases](https://github.com/wallstop/DxMessaging/releases) to grab the Unity Package and import to your project.

## To Install as Unity Package
1. Open Unity Package Manager
2. (Optional) Enable Pre-release packages to get the latest, cutting-edge builds
3. Open the Advanced Package Settings
4. Add an entry for a new "Scoped Registry"
    - Name: `NPM`
    - URL: `https://registry.npmjs.org`
    - Scope(s): `com.wallstop-studios.dxmessaging`
5. Resolve the latest `DxMessaging`

## From Source
Grab a copy of this repo (either `git clone` both [this repo](https://github.com/wallstop/DxMessaging) *and* [Unity Helpers](https://github.com/wallstop/unity-helpers) or [download a zip of the source](https://github.com/wallstop/DxMessaging/archive/refs/heads/master.zip) and [Unity Helper's source](https://github.com/wallstop/unity-helpers/archive/refs/heads/main.zip)) and copy the contents to your project's `Assets` folder.

# Benchmarks
In addition to providing a richer feature set, DxMessaging is *faster* than Unity's built in messaging solution. [Source](./Tests/Runtime/Benchmarks/PerformanceTests.cs). It is allocation-free and can be used in hot paths. 

For UntargetedMessages, DxMessaging is significantly faster (roughly 2x) than Unity.

| Message Tech | Operations / Second | Allocations? |
| ------------ | ------------------- | ------------ | 
| Unity | 2,670,600 | Yes |
| DxMessaging (GameObject) - Normal | 2,722,400 | No |
| DxMessaging (Component) - Normal | 2,738,800 | No |
| DxMessaging (GameObject) - No-Copy | 2,871,800 | No |
| DxMessaging (Component) - No-Copy | 2,876,600 | No |
| DxMessaging (Untargeted) - No-Copy | 4,480,200 | No |
| Reflexive (One Argument) | 2,287,800 | No |
| Reflexive (Two Arguments) | 1,009,000 | No |
| Reflexive (Three Arguments) | 1,000,400 | No |

# Functionality
While not as fast, DxMessaging offers *additional functionality* as compared to Unity's messaging solution.
| Feature | Unity | DxMessaging |
| ------- | ----- | ----------- |
| Require knowledge of receiver's implementation | &check; | _ |
| Send a message to a GameObject | &check; | &check; |
| Send a message to a Component | _ | &check; |
| (Optional) Explicit message ordering | _ | &check; |
| Ignore messages dynamically at runtime | _ | &check; (multiple ways) |
| Send messages to all receivers | _ | &check; |
| Listen to messages for another GameObject | _ | &check; (multiple ways)|
| Listen to messages for another Component | _ | &check; (multiple ways)|
| Listen to messages *from* another GameObject | _ | &check; (multiple ways)|
| Listen to messages *from* another Component | _ | &check; (multiple ways)|
| Send a message without boxing its parameters | _ | &check; |
| Listen to all messages | _ | &check; |
| View a filter-able history of message registrations | N/A | &check; |
| "PreUpdate" style handlers | _ | &check; |
| "LateUpdate" style handlers | _ | &check; |

# Concepts
There are a few important concepts that DxMessaging provides.
* **MessageBus**: An implementation of the `IMessageBus` interface, configured to relay messages. There is no limit to these. By default there is only one, the global message bus.
* **Emitter**: Any piece of code, anywhere can emit messages to any MessageBus. By default messages are emitted to the global message bus.
* **Receiver**: A piece of code that has registered with a message bus to receive messages of certain types from it.
* **UntargetedMessage**: Messages that are global, and will be sent to every receiver that is registered for this type.
* **TargetedMessage**: Messages that are intended *for* a receiver, like a piece of mail or a command.
* **BroadcastMessage**: Messages that are *from* an emitter, indicating events that happened to the emitter.

## Receivers
Receivers can subscribe to any number of message types. 
* For targeted messages, generally receivers listen to their own object as the target, but the receiver can opt to listen to any target, or even all targets.
* For broadcast messages, receivers can listen to any source, including themselves, or even all sources.

For Unity, we have an easy-to-integrate [MessageAwareComponent](./Runtime/Unity/MessageAwareComponent.cs) - simply extend any component you want off of this base class. This will handle message registration lifetimes automatically for you. 
If you have your own base classes or aren't using Unity, then you'll need to add lifetimes yourself. Please use the MessageAwareComponent as reference.

### Implementation Notes (Unity)
Please note, if you want to receive messages and inherit off of the `MessageAwareComponent`, the component implements several Unity-specific methods to manage lifetimes. Specifically,
* `protected virtual void Awake()`
* `protected virtual void OnEnable()`
* `protected virtual void OnDisable()`
* `protected virtual void OnDestroy()`
* `protected virtual void OnApplicationQuit()`

If you wish to use any of these methods in components that inherit from `MessageAwareComponent`, please make sure to have the overrides call the base methods, otherwise messaging *may break* or not work as expected.

## Integration - Source Generators (v2 and above)
I'm piloting some new tech in version 2 of this library. Currently, if implementing any of the message interfaces, there will be boxing performed on the messages, if they are structs. This is due to the nature of default interface methods, which I was not aware of.

To get around this, I have updated both the Unity Helpers dependent package, as well as this package, to take advantage of Roslyn Source Generators.

Requirements:
1. Use one of `DxTargetedMessage, DxBroadcastMessage, DxUntargetedMessage` depending on message requirement.
2. Ensure that the struct or class is marked as `partial`. 

To use:
```csharp
[DxTargetedMessage]
public partial struct SimpleTargetedMessage // No longer needed : ITargetedMessage<SimpleTargetedMessage>
{
}

[DxBroadcastMessage]
public partial struct SimpleBroadcastMessage // No longer needed : IBroadcastMessage<SimpleBroadcastMessage>
{
}

[DxUntargetedMessage]
public partial struct SimpleUntargetedMessage // No longer needed : IUntargetedMesssage<SimpleUntargetedMessage>
{
}
```

## Integration
See the [tests](./Tests/Runtime/Scripts/) directory for examples about how to integrate with the MessageAwareComponent. But, for some starters:
```csharp
public readonly struct SimpleTargetedMessage : ITargetedMessage<SimpleTargetedMessage>
{
}

public sealed class SimpleMessageAwareComponent : MessageAwareComponent
{
    protected override void RegisterMessageHandlers()
    {
        _ = _messageRegistrationToken.RegisterGameObjectTargeted<SimpleTargetedMessage>(gameObject, HandleSimpleTargetedMessage);
    }
-
    private void HandleSimpleTargetedMessage(ref SimpleTargetedMessage message)
    {
        Debug.Log("Received SimpleTargetedMessage.");
    }
}

// In some other bit of code
// Select a target
SimpleMessageAwareComponent target = Object.FindObjectOfType<SimpleMessageAwareComponent>();
// Create your message
SimpleTargetedMessage message = new();
// Send it - this will synchrously invoke all relevant handlers and return execution once complete
message.EmitGameObjectTargeted(target.gameObject);
```

There are three things that need to be done in order to integrate with any of the message types.

### Message Definition
The message must be defined. Messages are classes or structs that implement one of the three message interfaces - `IUntargetedMessage`, `ITargetedMessage`, or `IBroadcastMessage`. Structs are generally more efficient, as they don't allocate any memory on the heap. For most of my code that uses this framework, I prefer `readonly` structs, as I never have a use case where I want to mutate the message in the typical caller path, as there is no guarantee of message receiver order of execution.

Note: Each of the message types has a more specialized interface that can be inherited from - `IUntargetedMessage<T>`, `ITargetedMessage<T>`, or `IBroadcastMessage<T>`. Implementers of these generic forms will *avoid boxing* in the messaging system. The core messaging system requires knowledge of the message's type, which will by default call `object.GetType()`, which will box structs, generating some garbage. These specialized versions of the message interfaces avoid boxing and allocation, as the messaging system is able to reason about their runtime types more efficiently.

Note: Message definition is totally up to the user of the framework and is one of the primary benefits of the framework. The messages can be arbitrarily complex and have references to any kind of object or property.

```csharp
public readonly struct SimpleTargetedMessage : ITargetedMessage<SimpleTargetedMessage> {}
public readonly struct SimnpleUntargetedMessage : IUntargetedMessage<SimnpleUntargetedMessage> {}
public readonly struct SimpleBroadcastMessage : ITargetedMessage<SimpleBroadcastMessage> {}
```

### Message Receiver Registration
Now that you have a message, you need code that is able to listen to it. The easiest way of doing this is by having a class inherit off of [`MessageAwareComponent` (Unity)](./Runtime/Unity/MessageAwareComponent.cs). This class will take care of all of the registration lifetime hooks for you automatically. 

Note: By default, message listeners are only active if their object / script is active. If you want an implementer to always listen to messages, you can have your implementing class override the `MessageRegistrationTiedToEnableStatus` property to return `false`. If you do this, the component will still have its listeners actively cleaned up upon script destruction, so there are no leaks or additional worries you have to worry about.

If you cannot inherit from MessageAwareComponent, then you can lift its registration logic and state into your own base/implementing class - there's nothing special about it to the messaging system.

Note: Implementing from `MessageAwareComponent` will cause a `MessagingComponent` to automatically be added to the GameObject. This component serves as a centralized registration brain and the messaging system expects that there is only one of these per GameObject. There are some "one brain per object" internal invariants that the messaging system core relies upon.

Note: Message listeners can be one of two forms:
1. `void Listener(ref SimpleTargetedMessage message) {}`
2. `void Listener(SimpleTargetedMessage) {}`

Form one causes a copy to happen. Form two is copy-free. This distinction matters only when your messages are value types (structs). Please note that the behavior of the system is undefined if you update what the message is pointing to while using the `ref` concept.
```csharp
public sealed class MyCoolMessageReceiver : MessageAwareComponent
{
    protected override void RegisterMessageHandlers()
    {
        _ = _messageRegistrationToken.RegisterGameObjectTargeted(gameObject, (ref SimpleTargetedMessage message) => Debug.Log("Received SimpleTargetedMessage!"));
    }
}
```
### Message Emission
Now that you have both a message definition and a listener, you can now emit a message! In any piece of code, anywhere, messages can be emitted without any dependency on Unity or MessageAwareComponents.
```csharp
GameObject target = null; // Thing you want to send your target to, this can *not* be null
SimpleTargetedMessage targetedMessage = new();
targetedMessage.EmitGameObjectTargeted(target);
```
That's it! Once the call from `EmitGameObjectTargeted` completes, all message listeners will have executed.

See [Message Emission Functions](#message-emission-functions) for more information on the ways that messages can be emitted.
## When to use each message type
Note: There is no limit to the number of listeners for any given message.
Note: Message instances can be cached and re-emitted, if you think this is a good idea for your code.
Note: Message registration automatically dedupes listeners - even if your code registers a listener more than once, it will only be called once.
### UntargetedMessage
```csharp
public readonly struct SimpleUntargetedMessage : IUntargetedMessage<SimpleUntargetedMessage>
{
    public readonly string debugMessage;

    public SimpleUntargetedMessage(string debugMessage)
    {
        this.debugMessage = debugMessage;
    }
}

public sealed class SimpleUntargetedReceiver : MessageAwareComponent
{
    protected override void RegisterMessageHandlers()
    {
        _ = _messageRegistrationToken.RegisterUntargeted((ref SimpleUntargetedMessage message) => Debug.Log($"Received SimpleUntargetedMessage {message.debugMessage}."));
    }
}

// In some piece of code
SimpleUntargetedMessage message = new("Hello, world");
message.EmitUntargeted();
```
UntargetedMessages are a great fit for when you do not care about a sender or a receiver context. That is, the only thing you care about is that the message *is sent* and, potentially, the *contents* of the message. UntargetedMessages will be received by *all* active listeners, they're essentially global messages. What would normally require some global event bus or static event handlers is now completely decomposed and decoupled into the sender and receiver, each without knowledge of the other.
### TargetedMessage
```csharp
public readonly struct SimpleTargetedMessage : ITargetedMessage<SimpleTargetedMessage>
{
    public readonly string debugMessage;

    public SimpleTargetedMessage(string debugMessage)
    {
        this.debugMessage = debugMessage;
    }
}

public sealed class SimpleTargetedReceiver : MessageAwareComponent
{
    protected override void RegisterMessageHandlers()
    {
        _ = _messageRegistrationToken.RegisterGameObjectTargeted((ref SimpleTargetedMessage message) => Debug.Log($"Received SimpleTargetedMessage {message.debugMessage}."));
    }
}

// In some piece of code
GameObject target = null; // You need a reference to the thing you're targeting, it can't be null
SimpleTargetedMessage message = new($"I'm targeting you, {target.name}");
message.EmitGameObjectTargeted(target);
```
TargetedMessages are a great fit for when you want to send a command to something. Instead of having to reach into the object's guts and find the event handler to call, the caller can just emit the message *at* the target object, and the message framework will take care of the handling automatically. This functionality basically replace looking up specific component(s) and calling public methods on them, allowing loose coupling between senders and receivers.

Note: TargetedMessages can be sent to either GameObjects or Components. If sent to a GameObject, all listeners on that object that have registered for `GameObjectTargeted` will be invoked. If sent to a Component, only the listeners on that Component will be invoked. Recommendation is to use `GameObjectTargeted` unless you absolutely require callers to differentiate between receivers. `ComponentTargeted` requires knowledge of what Component to send the message to, requiring a tighter coupling than just knowing about a GameObject. 

Note: TargetedMessages can be received as if they were UntargetedMessages. That is, listeners can register without needing a target to bind to at registration time. To do so, register a listener with the signature 
```csharp
void HandleSimpleTargetedMessageWithoutTargeting(ref InstanceId target, ref SimpleTargetedMessage message) {}
```
This listener will receive all messages of this type along with the target that the message is for. Unity users can get the GameObject or Component the message is from using InstanceId's `.Object` property.
### BroadcastMessage
```csharp
public readonly struct SimpleBroadcastMessage : IBroadcastMessage<SimpleBroadcastMessage>
{
    public readonly string debugMessage;

    public SimpleBroadcastMessage(string debugMessage)
    {
        this.debugMessage = debugMessage;
    }
}

public sealed class SimpleBroadcastReceiver : MessageAwareComponent
{
    [SerializeField]
    private GameObject _thingToListenTo;

    protected override void RegisterMessageHandlers()
    {
        _ = _messageRegistrationToken.RegisterGameObjectBroadcast(_thingToListenTo, (ref SimpleBroadcastMessage message) => Debug.Log($"Received SimpleBroadcastMessage {message.debugMessage}."));
    }
}

// In the code that is the source of the message
SimpleBroadcastReceiver message = new("Something happened to me!");
// gameObject here is the gameObject property of the source
message.GameObjectBroadcast(gameObject);
```
BroadcastMessages are one of the most commonly used types of messages in the games that I build. Broadcast messages are *events* that *happen to* something. The thing that the event happens to *broadcasts* this message to anyone that is listening. Concepts like "my health changed", "I died", "I started channeling", where stuff is happening to *you*, the source code. This concept replaces a traditional event handler that has to be manually attached to, allowing for decoupling. The messaging system requires that receivers of BroadcastEvent must have some reference to the source object at registration time.

Note: BroadcastMessages can be sent from either GameObjects or Components. If sent from a GameObject, all listeners that have registered for events from that GameObject via `RegisterGameObjectBroadcast` will be invoked. If sent from a Component, only listeners that have explicitly listened to that Component will be invoked. Recommendation is to use `GameObjectBroadcast` unless you absolutely require receivers to differentiate between callers. `ComponentBroadcast` requires knowledge of the specific Component that is sending the message, requiring tighter coupling than just knowing about a GameObject.

Note: BroadcastMessages can be received as if they were UntargetedMessages. That is, listeners do not need a source to bind to at registration time. To do so, register a listener with the signature 
```csharp
void HandleSimpleBroadcastMessageWithoutSource(ref InstanceId source, ref SimpleBroadcastMessage message) {}
```
This listener will receive all messages of this type along with the source that the message is from. Unify users can get the GameObject or Component the message is from using InstanceId's `.Object` property.
## Advanced Concepts
The core functionality of the messaging system is for code to be sending and receiving messages of one of the three supported types. However, the messaging system provides additional functionality beyond this. With DxMessaging, you can...
### Register Interceptors
Sometimes, depending on certain system state, you may want to have all listeners *ignore* certain types of messages. This is where the concept of Interceptors fits in - Interceptors are message pre-processors that run in a specified order. They have the following forms:
```csharp
/// <summary>
/// Given an Untargeted message, determines whether or not it should be processed or skipped
/// </summary>
/// <typeparam name="TMessage">Specific type of message.</typeparam>
/// <param name="message">Message to consider.</param>
/// <returns>True if the message should be processed, false if it should be skipped.</returns>
public delegate bool UntargetedInterceptor<TMessage>(ref TMessage message) where TMessage : IUntargetedMessage;

/// <summary>
/// Given an Targeted message and its target, determines whether or not it should be processed or skipped.
/// </summary>
/// <typeparam name="TMessage">Specific type of message.</typeparam>
/// <param name="target">Target of the message.</param>
/// <param name="message">Message to consider.</param>
/// <returns>True if the message should be processed, false if it should be skipped.</returns>
public delegate bool TargetedInterceptor<TMessage>(ref InstanceId target, ref TMessage message) where TMessage : ITargetedMessage;

/// <summary>
/// Given an Broadcast message and its source, determines whether or not it should be processed or skipped.
/// </summary>
/// <typeparam name="TMessage">Specific type of message.</typeparam>
/// <param name="source">Source of the message.</param>
/// <param name="message">Message to consider.</param>
/// <returns>True if the message should be processed, false if it should be skipped.</returns>
public delegate bool BroadcastInterceptor<TMessage>(ref InstanceId source, ref TMessage message) where TMessage : IBroadcastMessage;
```
The primary use case of Interceptors is to block the actual emission of a message, by returning `false`. Unlike the message handlers, where `ref` is optional, `ref` is the only form of the Interceptor's parameters. This is because, by design, the Interceptors *can* mutate the message, allowing for very interesting runtime behavior. 

Note: `ref` mutation isn't required, and will likely lead to confusing scenarios. 

Note: Interceptors are ran sequentially. If any return false, the rest in line are not ran, and no message handler is ran.

Note: Interceptors run before messages of that type are handled, by design.

When registering an Interceptor, the system asks for a priority. Interceptors are ran from low -> high priority. Interceptors at the same priority are ran in the order registered.
### Register PostProcessors
Similar to a the `LateUpdate` concept that many game engines provide, DxMessaging system provides registration for handlers that run *after* all regular handlers. These are referred to as PostProcessors. This concept is useful if you want to guarantee that some listener runs after another.

Note: PostProcessors will still be ran synchronously before the `Emit` call finishes on the message.
### Listen to *all* messages
DxMessaging provides hooks for listeners to register a `GlobalAcceptAll`, where the listener will receive all messages that are sent through the system. This is particularly useful for networked applications where you want to serialize messages across the network, or if you have something like a HUD proxying messages from the player. This is an open-closed approach and allows for loosely coupled systems when the right need arises.

GlobalAcceptAll requires registration of listener functions for all three message types.

Note: GlobalAcceptAll listeners are ran before the normal listener and PostProcessing loop.
### Have a Debug insight into buggy registrations
You can bind the `MessagingDebug.Log` function to a custom logging function (likely Debug.Log) to get any error messages from the messaging system that indicate something has gone wrong.
### Have an insight into registrations and deregistrations
DxMessaging provides a registration log that is turned off by default. This registration log can be turned on by referencing `MessageBus.RegistrationLog` and setting `Enabled=true`. You can then programatically dump/filter events.
### Segment your message space
By default, DxMessaging uses an implicit global message bus. But you can create as many MessageBuses as you like, if you want to segment your game space. Each registration and emission function is overloaded for the users to specify a message bus to send or listen for messages on. If the message bus is null, the global bus is used.
## Message Emission Extension Functions
Message emission is relatively simple. Since the point of the framework is to decouple senders and receivers, the APIs are verbose to prevent bugs. Since it's possible to listen to and for messages involving either Components or GameObjects, my philosophy is that I'd rather have longer lines of code that are more descriptive ("I'm listening to this *Component* for this message) than accidentally have an incorrect coupling ("I sent this message to a Component when I meant to send it to a GameObject").

Each of the message extension functions aims to be as clear in its intent as possible.
### `EmitUntargeted`
This is the only way to emit an UntargetedMessage.
### `EmitTargeted`
This is the Unity-agnostic way of emitting TargetedMessages. Unity users can use this by providing a GameObject or Component as the target, but it is discouraged.
### `EmitGameObjectTargeted`
Send a TargetedMessage to a particular GameObject. Only receivers that are registered via `RegisterGameObjectTargeted` will be called. GameObject cannot be null.
### `EmitComponentTargeted`
Send a TargetedMessage to a particular Component. Only receivers that are registered via `RegisterComponentTargeted` will be called. Component cannot be null.
### `EmitBroadcast`
This is the Unity-agnostic way of emitting BroadcastMessages. Unity users can use this by providing a GameObject or Component as the source, but it is discouraged.
### `EmitGameObjectBroadcast`
Send a BroadcastMessage from a particular GameObject. Only listeners that are registered via `RegisterGameObjectBroadcast` will be called. GameObject cannot be null.
### `EmitComponentBroadcast`
Send a BroadcastMessage from a particular Component. Only listeners that are registered via `RegisterComponentBroadcast` will be called. Component cannot be null.
## MessageRegistrationToken
The MessageRegistrationToken is used to automatically track the lifecycle of listeners as well as provide integration with the messaging system. This is the class that you will be interacting with the most in user code to register listeners. If you're using Unity, one is provided as a usable member variable named `_messageRegistrationToken` in the base class MessageAwareComponent.

Each of the registration functions corresponds to one of the concepts linked above. They are heavily doc-commented and should be mostly self-explanatory.
