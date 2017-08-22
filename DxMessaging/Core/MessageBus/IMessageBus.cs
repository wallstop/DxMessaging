using System;

namespace DxMessaging.Core.MessageBus
{
    /**
        <summary>
            Description of a general purpose message bus that provides both registration, deregistration, and broadcast capabilities
        </summary>
    */

    public interface IMessageBus
    {
        /**
            <summary>
                Registers the specified MessageHandler to receive messages of the specified type.
            </summary>
            <return>
                The deregistration action
            </return>
        */
        Action RegisterUntargeted<T>(MessageHandler messageHandler) where T : UntargetedMessage;

        /**
            <summary>
                Registers the specified MessageHandler to receive targeted messages of the specified type.
                The message will only be routed to the properly identified MessageHandler.
            </summary>
            <return>
                The deregistration action
            </return>
        */
        Action RegisterTargeted<T>(MessageHandler messageHandler) where T : TargetedMessage;

        /**
            <summary>
                Registers the specified MessageHandler to receive targeted messages of the specified type.
                This registration IGNORES the targeting of the TargetedMessage.
            </summary>
            <return>
                The deregistration action
            </return>
        */
        Action RegisterTargetedWithoutTargeting<T>(MessageHandler messageHandler) where T : TargetedMessage;

        /**
            <summary>
                Registers the specified MessageHandler to receive ALL messages.
                If the Message is targeted, it doesn't matter, this MessageHandler will be invoked for it.
            </summary>
            <return>
                The deregistration action
            </return>
        */
        Action RegisterGlobalAcceptAll(MessageHandler messageHandler);

        /**
            <summary>
                Broadcasts a message to all listeners registered to this bus
            </summary>
        */
        void UntargetedBroadcast<T>(T typedMessage) where T : UntargetedMessage;

        /**
            <summary>
                Broadcasts a message to all listeners registered to this bus
            </summary>
        */
        void TargetedBroadcast<T>(T typedMessage) where T : TargetedMessage;

        /**
            <summary>
                Broadcasts a message to all listeners registered to this bus. Should only be used if the exact type of the message isn't known.
            </summary>
            <note>
                This should be sparingly used, as implementations of this will be generally more expensive than (Un)targetedBroadcast
            </note>
        */
        void Broadcast(AbstractMessage message);
    }
}
