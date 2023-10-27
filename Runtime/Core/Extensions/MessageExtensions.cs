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
        public static void EmitGameObjectTargeted<TMessage>(this TMessage message, GameObject target, IMessageBus messageBus = null) where TMessage : class, ITargetedMessage
        {
            InstanceId targetId = target;
            messageBus ??= MessageHandler.MessageBus;
            if (typeof(TMessage) != message.MessageType)
            {
                messageBus.UntypedTargetedBroadcast(targetId, message);
                return;
            }

            messageBus.TargetedBroadcast(ref targetId, ref message);
        }

        /// <summary>
        /// Emits a TargetedMessage of the given type.
        /// </summary>
        /// <param name="message">TargetedMessage to emit.</param>
        /// <param name="target">Target that this message is intended for.</param>
        /// <param name="messageBus">MessageBus to emit to. If null, uses the GlobalMessageBus.</param>
        public static void EmitGameObjectTargeted<TMessage>(this ref TMessage message, GameObject target, IMessageBus messageBus = null) where TMessage : struct, ITargetedMessage
        {
            InstanceId targetId = target;
            messageBus ??= MessageHandler.MessageBus;
            if (typeof(TMessage) != message.MessageType)
            {
                messageBus.UntypedTargetedBroadcast(targetId, message);
                return;
            }

            messageBus.TargetedBroadcast(ref targetId, ref message);
        }

        /// <summary>
        /// Emits a TargetedMessage of the given type.
        /// </summary>
        /// <param name="message">TargetedMessage to emit.</param>
        /// <param name="target">Target that this message is intended for.</param>
        /// <param name="messageBus">MessageBus to emit to. If null, uses the GlobalMessageBus.</param>
        public static void EmitComponentTargeted<TMessage>(this TMessage message, Component target, IMessageBus messageBus = null) where TMessage : class, ITargetedMessage
        {
            InstanceId targetId = target;
            messageBus ??= MessageHandler.MessageBus;
            if (typeof(TMessage) != message.MessageType)
            {
                messageBus.UntypedTargetedBroadcast(targetId, message);
                return;
            }

            messageBus.TargetedBroadcast(ref targetId, ref message);
        }

        /// <summary>
        /// Emits a TargetedMessage of the given type.
        /// </summary>
        /// <param name="message">TargetedMessage to emit.</param>
        /// <param name="target">Target that this message is intended for.</param>
        /// <param name="messageBus">MessageBus to emit to. If null, uses the GlobalMessageBus.</param>
        public static void EmitComponentTargeted<TMessage>(this ref TMessage message, Component target, IMessageBus messageBus = null) where TMessage : struct, ITargetedMessage
        {
            InstanceId targetId = target;
            messageBus ??= MessageHandler.MessageBus;
            if (typeof(TMessage) != message.MessageType)
            {
                messageBus.UntypedTargetedBroadcast(targetId, message);
                return;
            }

            messageBus.TargetedBroadcast(ref targetId, ref message);
        }

        /// <summary>
        /// Emits an UntargetedMessage of the given type.
        /// </summary>
        /// <param name="message">UntargetedMessage to emit.</param>
        /// <param name="messageBus">MessageBus to emit to. If null, uses the GlobalMessageBus.</param>
        public static void EmitUntargeted<TMessage>(this TMessage message, IMessageBus messageBus = null) where TMessage : class, IUntargetedMessage
        {
            messageBus ??= MessageHandler.MessageBus;
            if (typeof(TMessage) != message.MessageType)
            {
                messageBus.UntypedUntargetedBroadcast(message);
                return;
            }

            messageBus.UntargetedBroadcast(ref message);
        }

        /// <summary>
        /// Emits an UntargetedMessage of the given type.
        /// </summary>
        /// <param name="message">UntargetedMessage to emit.</param>
        /// <param name="messageBus">MessageBus to emit to. If null, uses the GlobalMessageBus.</param>
        public static void EmitUntargeted<TMessage>(this ref TMessage message, IMessageBus messageBus = null) where TMessage : struct, IUntargetedMessage
        {
            messageBus ??= MessageHandler.MessageBus;
            if (typeof(TMessage) != message.MessageType)
            {
                messageBus.UntypedUntargetedBroadcast(message);
                return;
            }

            messageBus.UntargetedBroadcast(ref message);
        }

        /// <summary>
        /// Emits a BroadcastMessage of the given type.
        /// </summary>
        /// <param name="message">BroadcastMessage to emit.</param>
        /// <param name="source">Source of this message.</param>
        /// <param name="messageBus">MessageBus to emit to. If null, uses the GlobalMessageBus.</param>
        public static void EmitGameObjectBroadcast<TMessage>(this TMessage message, GameObject source, IMessageBus messageBus = null) where TMessage : class, IBroadcastMessage
        {
            InstanceId sourceId = source;
            messageBus ??= MessageHandler.MessageBus;
            if (typeof(TMessage) != message.MessageType)
            {
                messageBus.UntypedSourcedBroadcast(sourceId, message);
                return;
            }

            messageBus.SourcedBroadcast(ref sourceId, ref message);
        }

        /// <summary>
        /// Emits a BroadcastMessage of the given type.
        /// </summary>
        /// <param name="message">BroadcastMessage to emit.</param>
        /// <param name="source">Source of this message.</param>
        /// <param name="messageBus">MessageBus to emit to. If null, uses the GlobalMessageBus.</param>
        public static void EmitGameObjectBroadcast<TMessage>(this ref TMessage message, GameObject source, IMessageBus messageBus = null) where TMessage : struct, IBroadcastMessage
        {
            InstanceId sourceId = source;
            messageBus ??= MessageHandler.MessageBus;
            if (typeof(TMessage) != message.MessageType)
            {
                messageBus.UntypedSourcedBroadcast(sourceId, message);
                return;
            }

            messageBus.SourcedBroadcast(ref sourceId, ref message);
        }

        /// <summary>
        /// Emits a BroadcastMessage of the given type from the specified component.
        /// </summary>
        /// <param name="message">BroadcastMessage to emit.</param>
        /// <param name="source">Source of this message.</param>
        /// <param name="messageBus">MessageBus to emit to. If null, uses the GlobalMessageBus.</param>
        public static void EmitComponentBroadcast<TMessage>(this TMessage message, Component source, IMessageBus messageBus = null) where TMessage : class, IBroadcastMessage
        {
            InstanceId sourceId = source;
            messageBus ??= MessageHandler.MessageBus;
            if (typeof(TMessage) != message.MessageType)
            {
                messageBus.UntypedSourcedBroadcast(sourceId, message);
                return;
            }

            messageBus.SourcedBroadcast(ref sourceId, ref message);
        }

        /// <summary>
        /// Emits a BroadcastMessage of the given type from the specified component.
        /// </summary>
        /// <param name="message">BroadcastMessage to emit.</param>
        /// <param name="source">Source of this message.</param>
        /// <param name="messageBus">MessageBus to emit to. If null, uses the GlobalMessageBus.</param>
        public static void EmitComponentBroadcast<TMessage>(this ref TMessage message, Component source, IMessageBus messageBus = null) where TMessage : struct, IBroadcastMessage
        {
            InstanceId sourceId = source;
            messageBus ??= MessageHandler.MessageBus;
            if (typeof(TMessage) != message.MessageType)
            {
                messageBus.UntypedSourcedBroadcast(sourceId, message);
                return;
            }

            messageBus.SourcedBroadcast(ref sourceId, ref message);
        }
    }
}
