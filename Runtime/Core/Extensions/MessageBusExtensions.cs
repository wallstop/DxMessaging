namespace DxMessaging.Core.Extensions
{
    using System;
    using DxMessaging.Core;
    using DxMessaging.Core.MessageBus;
    using DxMessaging.Core.Messages;
#if UNITY_2017_1_OR_NEWER
    using UnityEngine;
#endif

    /// <summary>
    /// <see cref="IMessageBus"/> helpers that mirror the message-centric shorthands in <see cref="MessageExtensions"/>.
    /// </summary>
    public static class MessageBusExtensions
    {
        public static void EmitUntargeted<TMessage>(this IMessageBus messageBus, TMessage message)
            where TMessage : class, IUntargetedMessage
        {
            if (messageBus == null)
            {
                throw new ArgumentNullException(nameof(messageBus));
            }

            message.EmitUntargeted(messageBus);
        }

        public static void EmitUntargeted<TMessage>(
            this IMessageBus messageBus,
            ref TMessage message
        )
            where TMessage : struct, IUntargetedMessage
        {
            if (messageBus == null)
            {
                throw new ArgumentNullException(nameof(messageBus));
            }

            message.EmitUntargeted(messageBus);
        }

        public static void EmitTargeted<TMessage>(
            this IMessageBus messageBus,
            InstanceId target,
            TMessage message
        )
            where TMessage : class, ITargetedMessage
        {
            if (messageBus == null)
            {
                throw new ArgumentNullException(nameof(messageBus));
            }

            message.EmitTargeted(target, messageBus);
        }

        public static void EmitTargeted<TMessage>(
            this IMessageBus messageBus,
            InstanceId target,
            ref TMessage message
        )
            where TMessage : struct, ITargetedMessage
        {
            if (messageBus == null)
            {
                throw new ArgumentNullException(nameof(messageBus));
            }

            message.EmitTargeted(target, messageBus);
        }

        public static void EmitBroadcast<TMessage>(
            this IMessageBus messageBus,
            InstanceId source,
            TMessage message
        )
            where TMessage : class, IBroadcastMessage
        {
            if (messageBus == null)
            {
                throw new ArgumentNullException(nameof(messageBus));
            }

            message.EmitBroadcast(source, messageBus);
        }

        public static void EmitBroadcast<TMessage>(
            this IMessageBus messageBus,
            InstanceId source,
            ref TMessage message
        )
            where TMessage : struct, IBroadcastMessage
        {
            if (messageBus == null)
            {
                throw new ArgumentNullException(nameof(messageBus));
            }

            message.EmitBroadcast(source, messageBus);
        }

#if UNITY_2017_1_OR_NEWER
        public static void EmitGameObjectTargeted<TMessage>(
            this IMessageBus messageBus,
            GameObject target,
            TMessage message
        )
            where TMessage : class, ITargetedMessage
        {
            if (messageBus == null)
            {
                throw new ArgumentNullException(nameof(messageBus));
            }

            message.EmitGameObjectTargeted(target, messageBus);
        }

        public static void EmitGameObjectTargeted<TMessage>(
            this IMessageBus messageBus,
            GameObject target,
            ref TMessage message
        )
            where TMessage : struct, ITargetedMessage
        {
            if (messageBus == null)
            {
                throw new ArgumentNullException(nameof(messageBus));
            }

            message.EmitGameObjectTargeted(target, messageBus);
        }

        public static void EmitComponentTargeted<TMessage>(
            this IMessageBus messageBus,
            Component target,
            TMessage message
        )
            where TMessage : class, ITargetedMessage
        {
            if (messageBus == null)
            {
                throw new ArgumentNullException(nameof(messageBus));
            }

            message.EmitComponentTargeted(target, messageBus);
        }

        public static void EmitComponentTargeted<TMessage>(
            this IMessageBus messageBus,
            Component target,
            ref TMessage message
        )
            where TMessage : struct, ITargetedMessage
        {
            if (messageBus == null)
            {
                throw new ArgumentNullException(nameof(messageBus));
            }

            message.EmitComponentTargeted(target, messageBus);
        }

        public static void EmitGameObjectBroadcast<TMessage>(
            this IMessageBus messageBus,
            GameObject source,
            TMessage message
        )
            where TMessage : class, IBroadcastMessage
        {
            if (messageBus == null)
            {
                throw new ArgumentNullException(nameof(messageBus));
            }

            message.EmitGameObjectBroadcast(source, messageBus);
        }

        public static void EmitGameObjectBroadcast<TMessage>(
            this IMessageBus messageBus,
            GameObject source,
            ref TMessage message
        )
            where TMessage : struct, IBroadcastMessage
        {
            if (messageBus == null)
            {
                throw new ArgumentNullException(nameof(messageBus));
            }

            message.EmitGameObjectBroadcast(source, messageBus);
        }

        public static void EmitComponentBroadcast<TMessage>(
            this IMessageBus messageBus,
            Component source,
            TMessage message
        )
            where TMessage : class, IBroadcastMessage
        {
            if (messageBus == null)
            {
                throw new ArgumentNullException(nameof(messageBus));
            }

            message.EmitComponentBroadcast(source, messageBus);
        }

        public static void EmitComponentBroadcast<TMessage>(
            this IMessageBus messageBus,
            Component source,
            ref TMessage message
        )
            where TMessage : struct, IBroadcastMessage
        {
            if (messageBus == null)
            {
                throw new ArgumentNullException(nameof(messageBus));
            }

            message.EmitComponentBroadcast(source, messageBus);
        }
#endif

        public static void Emit(this IMessageBus messageBus, string payload)
        {
            if (messageBus == null)
            {
                throw new ArgumentNullException(nameof(messageBus));
            }

            payload.Emit(messageBus);
        }

        public static void EmitAt(this IMessageBus messageBus, InstanceId target, string payload)
        {
            if (messageBus == null)
            {
                throw new ArgumentNullException(nameof(messageBus));
            }

            payload.Emit(target, messageBus);
        }

        public static void EmitFrom(this IMessageBus messageBus, InstanceId source, string payload)
        {
            if (messageBus == null)
            {
                throw new ArgumentNullException(nameof(messageBus));
            }

            payload.EmitFrom(source, messageBus);
        }
    }
}
