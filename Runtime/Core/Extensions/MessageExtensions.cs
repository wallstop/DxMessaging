namespace DxMessaging.Core.Extensions
{
    using Core;
    using MessageBus;
    using Messages;

    /// <summary>
    /// Extensions to smartly go about emitting messages :^)
    /// </summary>
    public static class MessageExtensions
    {
#if UNITY_2017_1_OR_NEWER
        /// <summary>
        /// Emits a TargetedMessage of the given type.
        /// </summary>
        /// <param name="message">TargetedMessage to emit.</param>
        /// <param name="target">Target that this message is intended for.</param>
        /// <param name="messageBus">MessageBus to emit to. If null, uses the GlobalMessageBus.</param>
        public static void EmitGameObjectTargeted<TMessage>(
            this TMessage message,
            UnityEngine.GameObject target,
            IMessageBus messageBus = null
        )
            where TMessage : class, ITargetedMessage
        {
            InstanceId targetId = target;
            messageBus ??= MessageHandler.MessageBus;
            if (typeof(TMessage) == typeof(ITargetedMessage))
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
        public static void EmitGameObjectTargeted<TMessage>(
            this ref TMessage message,
            UnityEngine.GameObject target,
            IMessageBus messageBus = null
        )
            where TMessage : struct, ITargetedMessage
        {
            InstanceId targetId = target;
            messageBus ??= MessageHandler.MessageBus;
            if (typeof(TMessage) == typeof(ITargetedMessage))
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
        public static void EmitComponentTargeted<TMessage>(
            this TMessage message,
            UnityEngine.Component target,
            IMessageBus messageBus = null
        )
            where TMessage : class, ITargetedMessage
        {
            InstanceId targetId = target;
            messageBus ??= MessageHandler.MessageBus;
            if (typeof(TMessage) == typeof(ITargetedMessage))
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
        public static void EmitComponentTargeted<TMessage>(
            this ref TMessage message,
            UnityEngine.Component target,
            IMessageBus messageBus = null
        )
            where TMessage : struct, ITargetedMessage
        {
            InstanceId targetId = target;
            messageBus ??= MessageHandler.MessageBus;
            if (typeof(TMessage) == typeof(ITargetedMessage))
            {
                messageBus.UntypedTargetedBroadcast(targetId, message);
                return;
            }

            messageBus.TargetedBroadcast(ref targetId, ref message);
        }
#endif

        /// <summary>
        /// Emits a TargetedMessage of the given type.
        /// </summary>
        /// <param name="message">TargetedMessage to emit.</param>
        /// <param name="target">Target that this message is intended for.</param>
        /// <param name="messageBus">MessageBus to emit to. If null, uses the GlobalMessageBus.</param>
        public static void EmitTargeted<TMessage>(
            this TMessage message,
            InstanceId target,
            IMessageBus messageBus = null
        )
            where TMessage : class, ITargetedMessage
        {
            messageBus ??= MessageHandler.MessageBus;
            if (typeof(TMessage) == typeof(ITargetedMessage))
            {
                messageBus.UntypedTargetedBroadcast(target, message);
                return;
            }

            messageBus.TargetedBroadcast(ref target, ref message);
        }

        /// <summary>
        /// Emits a TargetedMessage of the given type.
        /// </summary>
        /// <param name="message">TargetedMessage to emit.</param>
        /// <param name="target">Target that this message is intended for.</param>
        /// <param name="messageBus">MessageBus to emit to. If null, uses the GlobalMessageBus.</param>
        public static void EmitTargeted<TMessage>(
            this ref TMessage message,
            InstanceId target,
            IMessageBus messageBus = null
        )
            where TMessage : struct, ITargetedMessage
        {
            messageBus ??= MessageHandler.MessageBus;
            if (typeof(TMessage) == typeof(ITargetedMessage))
            {
                messageBus.UntypedTargetedBroadcast(target, message);
                return;
            }

            messageBus.TargetedBroadcast(ref target, ref message);
        }

        /// <summary>
        /// Emits an UntargetedMessage of the given type.
        /// </summary>
        /// <param name="message">UntargetedMessage to emit.</param>
        /// <param name="messageBus">MessageBus to emit to. If null, uses the GlobalMessageBus.</param>
        public static void EmitUntargeted<TMessage>(
            this TMessage message,
            IMessageBus messageBus = null
        )
            where TMessage : class, IUntargetedMessage
        {
            messageBus ??= MessageHandler.MessageBus;
            if (typeof(TMessage) == typeof(IUntargetedMessage))
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
        public static void Emit<TMessage>(this TMessage message, IMessageBus messageBus = null)
            where TMessage : class, IUntargetedMessage
        {
            messageBus ??= MessageHandler.MessageBus;
            if (typeof(TMessage) == typeof(IUntargetedMessage))
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
        public static void EmitUntargeted<TMessage>(
            this ref TMessage message,
            IMessageBus messageBus = null
        )
            where TMessage : struct, IUntargetedMessage
        {
            messageBus ??= MessageHandler.MessageBus;
            if (typeof(TMessage) == typeof(IUntargetedMessage))
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
        public static void Emit<TMessage>(this ref TMessage message, IMessageBus messageBus = null)
            where TMessage : struct, IUntargetedMessage
        {
            messageBus ??= MessageHandler.MessageBus;
            if (typeof(TMessage) == typeof(IUntargetedMessage))
            {
                messageBus.UntypedUntargetedBroadcast(message);
                return;
            }

            messageBus.UntargetedBroadcast(ref message);
        }

#if UNITY_2017_1_OR_NEWER
        /// <summary>
        /// Emits a BroadcastMessage of the given type.
        /// </summary>
        /// <param name="message">BroadcastMessage to emit.</param>
        /// <param name="source">Source of this message.</param>
        /// <param name="messageBus">MessageBus to emit to. If null, uses the GlobalMessageBus.</param>
        public static void EmitGameObjectBroadcast<TMessage>(
            this TMessage message,
            UnityEngine.GameObject source,
            IMessageBus messageBus = null
        )
            where TMessage : class, IBroadcastMessage
        {
            InstanceId sourceId = source;
            messageBus ??= MessageHandler.MessageBus;
            if (typeof(TMessage) == typeof(IBroadcastMessage))
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
        public static void EmitGameObjectBroadcast<TMessage>(
            this ref TMessage message,
            UnityEngine.GameObject source,
            IMessageBus messageBus = null
        )
            where TMessage : struct, IBroadcastMessage
        {
            InstanceId sourceId = source;
            messageBus ??= MessageHandler.MessageBus;
            if (typeof(TMessage) == typeof(IBroadcastMessage))
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
        public static void EmitComponentBroadcast<TMessage>(
            this TMessage message,
            UnityEngine.Component source,
            IMessageBus messageBus = null
        )
            where TMessage : class, IBroadcastMessage
        {
            InstanceId sourceId = source;
            messageBus ??= MessageHandler.MessageBus;
            if (typeof(TMessage) == typeof(IBroadcastMessage))
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
        public static void EmitComponentBroadcast<TMessage>(
            this ref TMessage message,
            UnityEngine.Component source,
            IMessageBus messageBus = null
        )
            where TMessage : struct, IBroadcastMessage
        {
            InstanceId sourceId = source;
            messageBus ??= MessageHandler.MessageBus;
            if (typeof(TMessage) == typeof(IBroadcastMessage))
            {
                messageBus.UntypedSourcedBroadcast(sourceId, message);
                return;
            }

            messageBus.SourcedBroadcast(ref sourceId, ref message);
        }
#endif

        /// <summary>
        /// Emits a BroadcastMessage of the given type from the specified component.
        /// </summary>
        /// <param name="message">BroadcastMessage to emit.</param>
        /// <param name="source">Source of this message.</param>
        /// <param name="messageBus">MessageBus to emit to. If null, uses the GlobalMessageBus.</param>
        public static void EmitBroadcast<TMessage>(
            this TMessage message,
            InstanceId source,
            IMessageBus messageBus = null
        )
            where TMessage : class, IBroadcastMessage
        {
            messageBus ??= MessageHandler.MessageBus;
            if (typeof(TMessage) == typeof(IBroadcastMessage))
            {
                messageBus.UntypedSourcedBroadcast(source, message);
                return;
            }

            messageBus.SourcedBroadcast(ref source, ref message);
        }

        /// <summary>
        /// Emits a BroadcastMessage of the given type from the specified component.
        /// </summary>
        /// <param name="message">BroadcastMessage to emit.</param>
        /// <param name="source">Source of this message.</param>
        /// <param name="messageBus">MessageBus to emit to. If null, uses the GlobalMessageBus.</param>
        public static void EmitBroadcast<TMessage>(
            this ref TMessage message,
            InstanceId source,
            IMessageBus messageBus = null
        )
            where TMessage : struct, IBroadcastMessage
        {
            messageBus ??= MessageHandler.MessageBus;
            if (typeof(TMessage) == typeof(IBroadcastMessage))
            {
                messageBus.UntypedSourcedBroadcast(source, message);
                return;
            }

            messageBus.SourcedBroadcast(ref source, ref message);
        }

        /// <summary>
        /// Emits a StringMessage at the target containing the provided string.
        /// </summary>
        /// <param name="message">Message to send to the target.</param>
        /// <param name="target">Target to send the message to.</param>
        /// <param name="messageBus">MessageBus to emit to. If null, uses the GlobalMessageBus.</param>
        public static void Emit(
            this string message,
            InstanceId target,
            IMessageBus messageBus = null
        )
        {
            StringMessage stringMessage = new(message);
            (messageBus ?? MessageHandler.MessageBus).TargetedBroadcast(
                ref target,
                ref stringMessage
            );
        }

        /// <summary>
        /// Emits a GlobalStringMessage containing the provided string.
        /// </summary>
        /// <param name="message">Message to send globally.</param>
        /// <param name="messageBus">MessageBus to emit to. If null, uses the GlobalMessageBus.</param>
        public static void Emit(this string message, IMessageBus messageBus = null)
        {
            GlobalStringMessage stringMessage = new(message);
            (messageBus ?? MessageHandler.MessageBus).UntargetedBroadcast(ref stringMessage);
        }
    }
}
