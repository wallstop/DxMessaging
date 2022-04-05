namespace DxMessaging.Core.MessageBus
{
    using Core;
    using System;
    using Messages;
    using UnityEngine;

    /// <summary>
    /// Description of a general purpose message bus that provides both registration, de-registration, and broadcast capabilities
    /// </summary>
    public interface IMessageBus
    {
        /// <summary>
        /// The registration log of all messaging registrations for this MessageBus.
        /// </summary>
        RegistrationLog Log { get; }

        /// <summary>
        /// Registers the specified MessageHandler to receive UntargetedMessages of the specified type.
        /// </summary>
        /// <typeparam name="T">Specific type of UntargetedMessages to register for.</typeparam>
        /// <param name="messageHandler">MessageHandler to register to accept UntargetedMessages of the specified type.</param>
        /// <returns>The de-registration action. Should be invoked when the handler no longer wants to receive messages.</returns>
        Action RegisterUntargeted<T>(MessageHandler messageHandler) where T : IUntargetedMessage;

        /// <summary>
        /// Registers the specified MessageHandler to receive TargetedMessages of the specified type.
        /// The message will only be routed to the properly identified MessageHandler.
        /// </summary>
        /// <typeparam name="T">Specific type of TargetedMessages to register for.</typeparam>
        /// <param name="messageHandler">MessageHandler to register to accept TargetedMessages of the specified type.</param>
        /// <returns>The de-registration action. Should be invoked when the handler no longer wants to receive messages.</returns>
        Action RegisterTargeted<T>(MessageHandler messageHandler) where T : ITargetedMessage;

        /// <summary>
        /// Registers the specified MessageHandler to receive TargetedMessages of the specified type for the specified target.
        /// </summary>
        /// <typeparam name="T">Specific type of TargetedMessages to register for.</typeparam>
        /// <param name="target">Target of messages to listen for.</param>
        /// <param name="messageHandler">MessageHandler to register the TargetedMessages of the specified type.</param>
        /// <returns>The de-registration action. Should be invoked when the handler no longer wants to receive the messages.</returns>
        Action RegisterTargeted<T>(InstanceId target, MessageHandler messageHandler) where T : ITargetedMessage;

        /// <summary>
        /// Registers the specified MessageHandler to receive TargetedMessages of the specified type.
        /// This registration IGNORES the targeting of the TargetedMessage.
        /// </summary>
        /// <typeparam name="T">Specific type of TargetedMessages to register for.</typeparam>
        /// <param name="messageHandler">MessageHandler to register to accept all TargetedMessages of the specified type.</param>
        /// <returns>The de-registration action. Should be invoked when the handler no longer wants to receive messages.</returns>
        Action RegisterTargetedWithoutTargeting<T>(MessageHandler messageHandler) where T : ITargetedMessage;

        /// <summary>
        /// Registers the specified MessageHandler to receive BroadcastMessages of the specified type from the provided source.
        /// </summary>
        /// <typeparam name="T">Type of the BroadcastMessage to register.</typeparam>
        /// <param name="source">InstanceId of the source for BroadcastMessages to listen to.</param>
        /// <param name="messageHandler">MessageHandler to register to accept BroadcastMessages.</param>
        /// <returns>The de-registration action. Should be invoked when the handler no longer wants to receive messages.</returns>
        Action RegisterSourcedBroadcast<T>(InstanceId source, MessageHandler messageHandler) where T : IBroadcastMessage;

        /// <summary>
        /// Registers the specified MessageHandler to receive BroadcastMessages of the specified type from ALL sources.
        /// This registration IGNORES the source of the BroadcastMessage.
        /// </summary>
        /// <typeparam name="T">Type of the BroadcastMessage to register.</typeparam>
        /// <param name="messageHandler">MessageHandler to register to accept BroadcastMessages.</param>
        /// <returns>The de-registration action. Should be invoked when the handler no longer wants to receive messages.</returns>
        Action RegisterSourcedBroadcastWithoutSource<T>(MessageHandler messageHandler) where T : IBroadcastMessage;

        /// <summary>
        /// Registers the specified MessageHandler to receive ALL messages.
        /// It doesn't matter if the message is Targeted or Untargeted, this MessageHandler will be invoked for it.
        /// </summary>
        /// <param name="messageHandler">MessageHandler to register to accept all messages.</param>
        /// <returns>The de-registration action. Should be invoked when the handler no longer wants to receive messages.</returns>
        Action RegisterGlobalAcceptAll(MessageHandler messageHandler);

        /// <summary>
        /// Registers the specified MessageHandler and transformer function as an interceptor for Messages of type T.
        /// Whenever messages of that type are sent, interceptors will be ran in order, transforming that message into
        /// new, mutated types. The message at the end of the transformations will be then sent to registered message handlers.
        /// </summary>
        /// <note>
        /// Transformer function can return null to "cancel" the message being sent.
        /// </note>
        /// <typeparam name="T">Type of message to intercept.</typeparam>
        /// <param name="transformer">Transformation function to run on messages of the chosen type.</param>
        /// <returns>The de-registration action. Should be invoked when the handler no longer wants to intercept messages.</returns>
        Action RegisterIntercept<T>(Func<T, T> transformer) where T : IMessage;

        /// <summary>
        /// Broadcasts an Untargeted message to all listeners registered to this bus.
        /// </summary>
        /// <param name="typedMessage">Message to broadcast.</param>
        void UntargetedBroadcast(IUntargetedMessage typedMessage);

        /// <summary>
        /// Broadcasts a TargetedMessage to all listeners registered to this bus.
        /// </summary>
        /// <param name="target">Target to send the message to.</param>
        /// <param name="typedMessage">Message to broadcast.</param>
        void TargetedBroadcast(InstanceId target, ITargetedMessage typedMessage);

        /// <summary>
        /// Broadcasts a BroadcastMessage to all listeners registered to this bus.
        /// </summary>
        /// <param name="source">Source of the message.</param>
        /// <param name="typedMessage">Message to broadcast.</param>
        void SourcedBroadcast(InstanceId source, IBroadcastMessage typedMessage);
    }
}
