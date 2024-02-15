# DxMessaging
Game engine agnostic robust, synchronous pub/sub C# messaging solution, mostly geared towards Unity and XNA/Monogame. See [this recorded talk](https://fathom.video/share/qjs8pn1MAwGb-yTAaW5WpZWdjzxcdwFR).

# To Install as Unity Package
1. Open Unity Package Manager
2. Open the Advanced Package Settings
3. Add an entry for a new "Scoped Registry"
    - Name: `NPM`
    - URL: `https://registry.npm.js.org`
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
### Integration
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
