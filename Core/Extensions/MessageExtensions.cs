namespace DxMessaging.Core.Extensions
{
    using Core;
    using MessageBus;
    using Messages;
    using UnityEngine;

    /// <summary>
    /// Extensions to smartly go about emitting messages :^)
    /// </summary>
    public static class MessageExtensions
    {
        /// <summary>
        /// Emits a TargetedMessage of the given type.
        /// </summary>
        /// <param name="message">TargetedMessage to emit.</param>
        /// <param name="target">Target that this message is intended for.</param>
        /// <param name="messageBus">MessageBus to emit to. If null, uses the GlobalMessageBus.</param>
        public static void EmitGameObjectTargeted(this ITargetedMessage message, GameObject target, IMessageBus messageBus = null)
        {
            (messageBus ?? MessageHandler.MessageBus).TargetedBroadcast(target, message);
        }

        /// <summary>
        /// Emits a TargetedMessage of the given type.
        /// </summary>
        /// <param name="message">TargetedMessage to emit.</param>
        /// <param name="target">Target that this message is intended for.</param>
        /// <param name="messageBus">MessageBus to emit to. If null, uses the GlobalMessageBus.</param>
        public static void EmitComponentTargeted(this ITargetedMessage message, Component target, IMessageBus messageBus = null)
        {
            (messageBus ?? MessageHandler.MessageBus).TargetedBroadcast(target, message);
        }

        /// <summary>
        /// Emits an UntargetedMessage of the given type.
        /// </summary>
        /// <param name="message">UntargetedMessage to emit.</param>
        /// <param name="messageBus">MessageBus to emit to. If null, uses the GlobalMessageBus.</param>
        public static void EmitUntargeted(this IUntargetedMessage message, IMessageBus messageBus = null)
        {
            (messageBus ?? MessageHandler.MessageBus).UntargetedBroadcast(message);
        }

        /// <summary>
        /// Emits a BroadcastMessage of the given type.
        /// </summary>
        /// <param name="message">BroadcastMessage to emit.</param>
        /// <param name="source">Source of this message.</param>
        /// <param name="messageBus">MessageBus to emit to. If null, uses the GlobalMessageBus.</param>
        public static void EmitGameObjectBroadcast(this IBroadcastMessage message, GameObject source, IMessageBus messageBus = null)
        {
            (messageBus ?? MessageHandler.MessageBus).SourcedBroadcast(source, message);
        }

        /// <summary>
        /// Emits a BroadcastMessage of the given type from the specified component.
        /// </summary>
        /// <param name="message">BroadcastMessage to emit.</param>
        /// <param name="source">Source of this message.</param>
        /// <param name="messageBus">MessageBus to emit to. If null, uses the GlobalMessageBus.</param>
        public static void EmitComponentBroadcast(this IBroadcastMessage message, Component source, IMessageBus messageBus = null)
        {
            (messageBus ?? MessageHandler.MessageBus).SourcedBroadcast(source, message);
        }
    }
}
