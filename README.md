# DxMessaging
Game engine agnostic robust, synchronous pub/sub C# messaging solution, mostly geared towards Unity and XNA/Monogame. See [this recorded talk](https://fathom.video/share/qjs8pn1MAwGb-yTAaW5WpZWdjzxcdwFR).

# To Install as Unity Package
1. Open Unity Package Manager
2. Open the Advanced Package Settings
3. Add an entry for a new "Scoped Registry"
    - Name: `NPM`
    - URL: `https://registry.npmjs.org`
    - Scope(s): `com.wallstop-studios.dxmessaging` *and* `com.wallstop-studios.unity-helpers`
4. Resolve the latest `DxMessaging`

# Benchmarks
DxMessaging is currently a bit slower (2-3x) than Unity's built in messaging solution (when running in Unity). [Source](./Tests/Runtime/Core/PerformanceTests.cs).
| Message Tech | Operations / Second |
| ------------ | ------------------- |
| Unity | 1,955,744 |
| DxMessaging (GameObject) - Normal | 596,409 |
| DxMessaging (Component) - Normal | 602,120 |
| DxMessaging (GameObject) - No-Copy | 583,824 |
| DxMessaging (Component) - No-Copy | 611,504 |
| DxMessaging (Untargeted) - No-Copy | 1,044,795 |

# Functionality
While not as fast, DxMessaging offers *additional functionality* as compared to Unity's messaging solution.
| Feature | Unity | DxMessaging |
| ------- | ----- | ----------- |
| Require knowledge of receiver's implementation | &check; | _ |
| Send a message to a GameObject | &check; | &check; |
| Send a message to a Component | _ | &check; |
| Ignore messages dynamically at runtime | _ | &check; (multiple ways) |
| Send messages to all receivers | _ | &check; |
| Listen to messages for another GameObject | _ | &check; (multiple ways)|
| Listen to messages for another Component | _ | &check; (multiple ways)|
| Listen to messages *from* another GameObject | _ | &check; (multiple ways)|
| Listen to messages *from* another Component | _ | &check; (multiple ways)|
| Send a message without boxing its parameters | _ | &check; |
| Listen to all messages | _ | &check; |
| View a filter-able history of message registrations | N/A | &check; |
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

Note: There are many *ways* to register for messages. These are covered in [MessageRegistrationToken Functions](#messageregistrationtoken-functions).

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
UntargetedMessages are a great fit for when you do not care about a sender or a receiver context. That is, the only thing you care about is that the message *is sent* and, potentially, the *contents* of the message. UntargetedMessages will be received by *all* active listeners. 
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
TargetedMessages are a great fit for when you want to send a command to something. Instead of having to reach into the object's guts and find the event handler to call, the caller can just emit the message *at* the target object, and the message framework will take care of the handling automatically.


## Message Emission Functions

## MessageRegistrationToken Functions