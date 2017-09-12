using System;

namespace DxMessaging.Core.MessageBus
{

    /// <summary>
    /// Description of a general purpose message bus that provides both registration, deregistration, and broadcast capabilities
    /// </summary>
    public interface IMessageBus
    {
        /// <summary>
        /// Registers the specified MessageHandler to receive UntargetedMessages of the specified type.
        /// </summary>
        /// <typeparam name="T">Specific type of UntargetedMessages to register for.</typeparam>
        /// <param name="messageHandler">MessageHandler to register to accept UntargetedMessages of the specified type.</param>
        /// <returns>The deregistration action. Should be invoked when the handler no longer wants to receive messages.</returns>
        Action RegisterUntargeted<T>(MessageHandler messageHandler) where T : UntargetedMessage;

        /// <summary>
        /// Registers the specified MessageHandler to receive TargetedMessages of the specified type.
        /// The message will only be routed to the properly identified MessageHandler.
        /// </summary>
        /// <typeparam name="T">Specific type of TargetedMessages to register for.</typeparam>
        /// <param name="messageHandler">MessageHandler to register to accept TargetedMessages of the specified type.</param>
        /// <returns>The deregistration action. Should be invoked when the handler no longer wants to receive messages.</returns>
        Action RegisterTargeted<T>(MessageHandler messageHandler) where T : TargetedMessage;

        /// <summary>
        /// Registers the specified MessageHandler to receive TargetedMessages of the specified type.
        /// This registration IGNORES the targeting of the TargetedMessage.
        /// </summary>
        /// <typeparam name="T">Specific type of TargetedMessages to register for.</typeparam>
        /// <param name="messageHandler">MessageHandler to register to accept all TargetedMessages of the specified type.</param>
        /// <returns>The deregistration action. Should be invoked when the handler no longer wants to receive messages.</returns>
        Action RegisterTargetedWithoutTargeting<T>(MessageHandler messageHandler) where T : TargetedMessage;

        /// <summary>
        /// Registers the specified MessageHandler to receive ALL messages.
        /// It doesn't matter if the message is Targeted or Untargeted, this MessageHandler will be invoked for it.
        /// </summary>
        /// <param name="messageHandler">MessageHandler to register to accept all messages.</param>
        /// <returns>The deregistration action. Should be invoked when the handler no longer wants to receive messages.</returns>
        Action RegisterGlobalAcceptAll(MessageHandler messageHandler);

        /// <summary>
        /// Broadcasts an Untargeted message to all listeners registered to this bus.
        /// </summary>
        /// <typeparam name="T">Specific type of the UntargetedMessage.</typeparam>
        /// <param name="typedMessage">Message to broadcast.</param>
        void UntargetedBroadcast<T>(T typedMessage) where T : UntargetedMessage;

        /// <summary>
        /// Broadcasts a TargetedMessage to all listeners registered to this bus.
        /// </summary>
        /// <typeparam name="T">Specific type of the TargetedMessage.</typeparam>
        /// <param name="typedMessage">Message to broadcast.</param>
        void TargetedBroadcast<T>(T typedMessage) where T : TargetedMessage;

        /// <summary>
        /// Broadcasts a message to all listeners registered to this bus. Should only be used if the exact type of the message isn't known.
        /// </summary>
        /// <param name="message"></param>
        /// <note>
        /// This should be sparingly used, as implementations of this will be generally more expensive than [Un]targetedBroadcast. 
        /// This is particularly useful if you're using some abstraction over messages, and want to pass around either 
        /// "TargetedMessage"s, "UntargetedMessage"s, or "AbstractMessage"s.
        /// </note>
        void Broadcast(AbstractMessage message);
    }
}
