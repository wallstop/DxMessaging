namespace DxMessaging.Core
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using MessageBus;
    using Messages;

    /// <summary>
    /// Abstraction layer for immediate-mode Message passing. An instance of this handles all
    /// kinds of types to trigger functions that are registered with it.
    /// </summary>
    public sealed class MessageHandler
    {
        public delegate void FastHandler<TMessage>(ref TMessage message)
            where TMessage : IMessage;
        public delegate void FastHandlerWithContext<TMessage>(
            ref InstanceId context,
            ref TMessage message
        )
            where TMessage : IMessage;

        /// <summary>
        /// MessageBus for all MessageHandlers to use. Currently immutable, but may change in the future.
        /// </summary>
        public static readonly MessageBus.MessageBus MessageBus = new();

        /// <summary>
        /// Maps Types to the corresponding Handler of that type.
        /// </summary>
        /// <note>
        /// Ideally, this would be something like a Dictionary[T,Handler[T]], but that can't be done with C#s type system.
        /// </note>
        private readonly Dictionary<
            IMessageBus,
            Dictionary<Type, object>
        > _handlersByTypeByMessageBus;

        /// <summary>
        /// Whether this MessageHandler will process messages.
        /// </summary>
        public bool active;

        /// <summary>
        /// The Id of the GameObject that owns us.
        /// </summary>
        public readonly InstanceId owner;

        public MessageHandler(InstanceId owner)
        {
            this.owner = owner;
            _handlersByTypeByMessageBus = new Dictionary<IMessageBus, Dictionary<Type, object>>();
        }

        /// <summary>
        /// Callback from the MessageBus for handling UntargetedMessages - user code should generally never use this.
        /// </summary>
        /// <note>
        /// In this case, "UntargetedMessage" refers to Targeted without targeting, and UntargetedMessages, hence T : AbstractMessage.
        /// </note>
        /// <param name="message">Message to handle.</param>
        /// <param name="messageBus">The specific MessageBus to use.</param>
        /// <param name="priority">Priority at which to run the handlers.</param>
        public void HandleUntargetedMessage<TMessage>(
            ref TMessage message,
            IMessageBus messageBus,
            int priority
        )
            where TMessage : IMessage
        {
            if (!active)
            {
                return;
            }

            if (
                GetHandlerForType(
                    message.MessageType,
                    messageBus,
                    out TypedHandler<TMessage> handler
                )
            )
            {
                handler.HandleUntargeted(ref message, priority);
            }
        }

        /// <summary>
        /// Callback from the MessageBus for handling UntargetedMessages - user code should generally never use this.
        /// </summary>
        /// <note>
        /// In this case, "UntargetedMessage" refers to Targeted without targeting, and UntargetedMessages, hence T : AbstractMessage.
        /// </note>
        /// <param name="message">Message to handle.</param>
        /// <param name="messageBus">The specific MessageBus to use.</param>
        /// <param name="priority">Priority at which to run the handlers.</param>
        public void HandleUntargetedPostProcessing<TMessage>(
            ref TMessage message,
            IMessageBus messageBus,
            int priority
        )
            where TMessage : IUntargetedMessage
        {
            if (!active)
            {
                return;
            }

            if (
                GetHandlerForType(
                    message.MessageType,
                    messageBus,
                    out TypedHandler<TMessage> handler
                )
            )
            {
                handler.HandleUntargetedPostProcessing(ref message, priority);
            }
        }

        /// <summary>
        /// Callback from the MessageBus for handling TargetedMessages when this MessageHandler has subscribed - user code should generally never use this.
        /// </summary>
        /// <note>
        /// TargetedMessage refers to those that are intended for the GameObject that owns this MessageHandler.
        /// </note>
        /// <param name="target">Target Id the message is for.</param>
        /// <param name="message">Message to handle.</param>
        /// <param name="messageBus">The specific MessageBus to use.</param>
        /// <param name="priority">Priority at which to run the handlers.</param>
        public void HandleTargeted<TMessage>(
            ref InstanceId target,
            ref TMessage message,
            IMessageBus messageBus,
            int priority
        )
            where TMessage : ITargetedMessage
        {
            if (!active)
            {
                return;
            }

            if (
                GetHandlerForType(
                    message.MessageType,
                    messageBus,
                    out TypedHandler<TMessage> handler
                )
            )
            {
                handler.HandleTargeted(ref target, ref message, priority);
            }
        }

        /// <summary>
        /// Callback from the MessageBus for handling TargetedMessages without targeting when this MessageHandler has subscribed - user code should generally never use this.
        /// </summary>
        /// <note>
        /// Any TargetedMessage.
        /// </note>
        /// <param name="target">Target Id the message is for.</param>
        /// <param name="message">Message to handle.</param>
        /// <param name="messageBus">The specific MessageBus to use.</param>
        /// <param name="priority">Priority at which to run the handlers.</param>
        public void HandleTargetedWithoutTargeting<TMessage>(
            ref InstanceId target,
            ref TMessage message,
            IMessageBus messageBus,
            int priority
        )
            where TMessage : ITargetedMessage
        {
            if (!active)
            {
                return;
            }

            if (
                GetHandlerForType(
                    message.MessageType,
                    messageBus,
                    out TypedHandler<TMessage> handler
                )
            )
            {
                handler.HandleTargetedWithoutTargeting(ref target, ref message, priority);
            }
        }

        /// <summary>
        /// Callback from the MessageBus for post processing TargetedMessages when this MessageHandler has subscribed - user code should generally never use this.
        /// </summary>
        /// <note>
        /// TargetedMessage refers to those that are intended for the GameObject that owns this MessageHandler.
        /// </note>
        /// <param name="target">Target Id the message is for.</param>
        /// <param name="message">Message to handle.</param>
        /// <param name="messageBus">The specific MessageBus to use.</param>
        /// <param name="priority">Priority at which to run the handlers.</param>
        public void HandleTargetedPostProcessing<TMessage>(
            ref InstanceId target,
            ref TMessage message,
            IMessageBus messageBus,
            int priority
        )
            where TMessage : ITargetedMessage
        {
            if (!active)
            {
                return;
            }

            if (
                GetHandlerForType(
                    message.MessageType,
                    messageBus,
                    out TypedHandler<TMessage> handler
                )
            )
            {
                handler.HandleTargetedPostProcessing(ref target, ref message, priority);
            }
        }

        /// <summary>
        /// Callback from the MessageBus for post processing TargetedMessages when this MessageHandler has subscribed - user code should generally never use this.
        /// </summary>
        /// <note>
        /// TargetedMessage refers to those that are intended for the GameObject that owns this MessageHandler.
        /// </note>
        /// <param name="target">Target Id the message is for.</param>
        /// <param name="message">Message to handle.</param>
        /// <param name="messageBus">The specific MessageBus to use.</param>
        /// <param name="priority">Priority at which to run the handlers.</param>
        public void HandleTargetedWithoutTargetingPostProcessing<TMessage>(
            ref InstanceId target,
            ref TMessage message,
            IMessageBus messageBus,
            int priority
        )
            where TMessage : ITargetedMessage
        {
            if (!active)
            {
                return;
            }

            if (
                GetHandlerForType(
                    message.MessageType,
                    messageBus,
                    out TypedHandler<TMessage> handler
                )
            )
            {
                handler.HandleTargetedWithoutTargetingPostProcessing(
                    ref target,
                    ref message,
                    priority
                );
            }
        }

        /// <summary>
        /// Callback from the MessageBus for handling SourcedBroadcastMessages - user code should generally never use this.
        /// </summary>
        /// <note>
        /// SourcedBroadcastMessages generally refer to those that are sourced from the GameObject that owns this MessageHandler.
        /// </note>
        /// <param name="source">Source Id the broadcast message is from.</param>
        /// <param name="message">Message to handle</param>
        /// <param name="messageBus">The specific MessageBus to use.</param>
        /// <param name="priority">Priority at which to run the handlers.</param>
        public void HandleSourcedBroadcast<TMessage>(
            ref InstanceId source,
            ref TMessage message,
            IMessageBus messageBus,
            int priority
        )
            where TMessage : IBroadcastMessage
        {
            if (!active)
            {
                return;
            }

            if (
                GetHandlerForType(
                    message.MessageType,
                    messageBus,
                    out TypedHandler<TMessage> handler
                )
            )
            {
                handler.HandleSourcedBroadcast(ref source, ref message, priority);
            }
        }

        /// <summary>
        /// Callback from the MessageBus for handling SourcedBroadcastMessages without source - user code should generally never use this.
        /// </summary>
        /// <note>
        /// Any SourcedBroadcastMessages.
        /// </note>
        /// <param name="source">Source Id the broadcast message is from.</param>
        /// <param name="message">Message to handle</param>
        /// <param name="messageBus">The specific MessageBus to use.</param>
        /// <param name="priority">Priority at which to run the handlers.</param>
        public void HandleSourcedBroadcastWithoutSource<TMessage>(
            ref InstanceId source,
            ref TMessage message,
            IMessageBus messageBus,
            int priority
        )
            where TMessage : IBroadcastMessage
        {
            if (!active)
            {
                return;
            }

            if (
                GetHandlerForType(
                    message.MessageType,
                    messageBus,
                    out TypedHandler<TMessage> handler
                )
            )
            {
                handler.HandleSourcedBroadcastWithoutSource(ref source, ref message, priority);
            }
        }

        /// <summary>
        /// Callback from the MessageBus for handling SourcedBroadcastPostProcessing - user code should generally never use this.
        /// </summary>
        /// <note>
        /// SourcedBroadcastMessages generally refer to those that are sourced from the GameObject that owns this MessageHandler.
        /// </note>
        /// <param name="source">Source Id the broadcast message is from.</param>
        /// <param name="message">Message to handle</param>
        /// <param name="messageBus">The specific MessageBus to use.</param>
        /// <param name="priority">Priority at which to run the handlers.</param>
        public void HandleSourcedBroadcastPostProcessing<TMessage>(
            ref InstanceId source,
            ref TMessage message,
            IMessageBus messageBus,
            int priority
        )
            where TMessage : IBroadcastMessage
        {
            if (!active)
            {
                return;
            }

            if (
                GetHandlerForType(
                    message.MessageType,
                    messageBus,
                    out TypedHandler<TMessage> handler
                )
            )
            {
                handler.HandleSourcedBroadcastPostProcessing(ref source, ref message, priority);
            }
        }

        /// <summary>
        /// Callback from the MessageBus for handling SourcedBroadcastPostProcessing - user code should generally never use this.
        /// </summary>
        /// <note>
        /// SourcedBroadcastMessages generally refer to those that are sourced from the GameObject that owns this MessageHandler.
        /// </note>
        /// <param name="source">Source Id the broadcast message is from.</param>
        /// <param name="message">Message to handle</param>
        /// <param name="messageBus">The specific MessageBus to use.</param>
        /// <param name="priority">Priority at which to run the handlers.</param>
        public void HandleSourcedBroadcastWithoutSourcePostProcessing<TMessage>(
            ref InstanceId source,
            ref TMessage message,
            IMessageBus messageBus,
            int priority
        )
            where TMessage : IBroadcastMessage
        {
            if (!active)
            {
                return;
            }

            if (
                GetHandlerForType(
                    message.MessageType,
                    messageBus,
                    out TypedHandler<TMessage> handler
                )
            )
            {
                handler.HandleBroadcastWithoutSourcePostProcessing(
                    ref source,
                    ref message,
                    priority
                );
            }
        }

        /// <summary>
        /// Callback from the MessageBus for handling Messages when this MessageHandler has subscribed to GlobalAcceptAll - user code should generally never use this.
        /// </summary>
        /// <param name="message">Message to handle.</param>
        /// <param name="messageBus">The specific MessageBus to use.</param>
        public void HandleGlobalUntargetedMessage(
            ref IUntargetedMessage message,
            IMessageBus messageBus
        )
        {
            if (!active)
            {
                return;
            }

            // Use the "IMessage" explicitly to indicate global messages, allowing us to multi-purpose a single dictionary
            if (GetHandlerForType(typeof(IMessage), messageBus, out TypedHandler<IMessage> handler))
            {
                handler.HandleGlobalUntargeted(ref message);
            }
        }

        /// <summary>
        /// Callback from the MessageBus for handling Messages when this MessageHandler has subscribed to GlobalAcceptAll - user code should generally never use this.
        /// </summary>
        /// <param name="target">Target of the message.</param>
        /// <param name="message">Message to handle.</param>
        /// <param name="messageBus">The specific MessageBus to use.</param>
        public void HandleGlobalTargetedMessage(
            ref InstanceId target,
            ref ITargetedMessage message,
            IMessageBus messageBus
        )
        {
            if (!active)
            {
                return;
            }

            // Use the "IMessage" explicitly to indicate global messages, allowing us to multi-purpose a single dictionary
            if (GetHandlerForType(typeof(IMessage), messageBus, out TypedHandler<IMessage> handler))
            {
                handler.HandleGlobalTargeted(ref target, ref message);
            }
        }

        /// <summary>
        /// Callback from the MessageBus for handling Messages when this MessageHandler has subscribed to GlobalAcceptAll - user code should generally never use this.
        /// </summary>
        /// <param name="source">Source that this message is from.</param>
        /// <param name="message">Message to handle.</param>
        /// <param name="messageBus">The specific MessageBus to use.</param>
        public void HandleGlobalSourcedBroadcastMessage(
            ref InstanceId source,
            ref IBroadcastMessage message,
            IMessageBus messageBus
        )
        {
            if (!active)
            {
                return;
            }

            // Use the "IMessage" explicitly to indicate global messages, allowing us to multi-purpose a single dictionary
            if (GetHandlerForType(typeof(IMessage), messageBus, out TypedHandler<IMessage> handler))
            {
                handler.HandleGlobalBroadcast(ref source, ref message);
            }
        }

        /// <summary>
        /// Registers this MessageHandler to Globally Accept All Messages via the MessageBus, properly handling de-registration.
        /// </summary>
        /// <param name="untargetedMessageHandler">MessageHandler to accept all UntargetedMessages.</param>
        /// <param name="broadcastMessageHandler">MessageHandler to accept all TargetedMessages for all entities.</param>
        /// <param name="targetedMessageHandler">MessageHandler to accept all BroadcastMessages for all entities.</param>
        /// <param name="messageBus">IMessageBus override to register with, if any. Null/not provided defaults to the GlobalMessageBus.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterGlobalAcceptAll(
            Action<IUntargetedMessage> untargetedMessageHandler,
            Action<InstanceId, ITargetedMessage> targetedMessageHandler,
            Action<InstanceId, IBroadcastMessage> broadcastMessageHandler,
            IMessageBus messageBus = null
        )
        {
            messageBus ??= MessageBus;
            Action messageBusDeregistration = messageBus.RegisterGlobalAcceptAll(this);
            TypedHandler<IMessage> typedHandler = GetOrCreateHandlerForType<IMessage>(messageBus);

            Action untargetedDeregistration = typedHandler.AddGlobalUntargetedHandler(
                untargetedMessageHandler,
                NullDeregistration
            );
            Action targetedDeregistration = typedHandler.AddGlobalTargetedHandler(
                targetedMessageHandler,
                NullDeregistration
            );
            Action broadcastDeregistration = typedHandler.AddGlobalBroadcastHandler(
                broadcastMessageHandler,
                NullDeregistration
            );

            return () =>
            {
                untargetedDeregistration();
                targetedDeregistration();
                broadcastDeregistration();
                messageBusDeregistration?.Invoke();
            };

            void NullDeregistration()
            {
                // No-op
            }
        }

        /// <summary>
        /// Registers this MessageHandler to Globally Accept All Messages via the MessageBus, properly handling de-registration.
        /// </summary>
        /// <param name="untargetedMessageHandler">MessageHandler to accept all UntargetedMessages.</param>
        /// <param name="broadcastMessageHandler">MessageHandler to accept all TargetedMessages for all entities.</param>
        /// <param name="targetedMessageHandler">MessageHandler to accept all BroadcastMessages for all entities.</param>
        /// <param name="messageBus">IMessageBus override to register with, if any. Null/not provided defaults to the GlobalMessageBus.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterGlobalAcceptAll(
            FastHandler<IUntargetedMessage> untargetedMessageHandler,
            FastHandlerWithContext<ITargetedMessage> targetedMessageHandler,
            FastHandlerWithContext<IBroadcastMessage> broadcastMessageHandler,
            IMessageBus messageBus = null
        )
        {
            messageBus ??= MessageBus;
            Action messageBusDeregistration = messageBus.RegisterGlobalAcceptAll(this);
            TypedHandler<IMessage> typedHandler = GetOrCreateHandlerForType<IMessage>(messageBus);

            Action untargetedDeregistration = typedHandler.AddGlobalUntargetedHandler(
                untargetedMessageHandler,
                NullDeregistration
            );
            Action targetedDeregistration = typedHandler.AddGlobalTargetedHandler(
                targetedMessageHandler,
                NullDeregistration
            );
            Action broadcastDeregistration = typedHandler.AddGlobalBroadcastHandler(
                broadcastMessageHandler,
                NullDeregistration
            );

            return () =>
            {
                untargetedDeregistration();
                targetedDeregistration();
                broadcastDeregistration();
                messageBusDeregistration?.Invoke();
            };

            void NullDeregistration()
            {
                // No-op
            }
        }

        /// <summary>
        /// Registers this MessageHandler to accept TargetedMessages via the MessageBus, properly handling de-registration.
        /// </summary>
        /// <typeparam name="T">Type of Message to be handled.</typeparam>
        /// <param name="target">Target Id of TargetedMessages to listen for.</param>
        /// <param name="messageHandler">Function that actually handles the message.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <param name="messageBus">IMessageBus override to register with, if any. Null/not provided defaults to the GlobalMessageBus.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterTargetedMessageHandler<T>(
            InstanceId target,
            Action<T> messageHandler,
            int priority = 0,
            IMessageBus messageBus = null
        )
            where T : ITargetedMessage
        {
            messageBus ??= MessageBus;
            Action messageBusDeregistration = messageBus.RegisterTargeted<T>(
                target,
                this,
                priority: priority
            );
            TypedHandler<T> typedHandler = GetOrCreateHandlerForType<T>(messageBus);
            return typedHandler.AddTargetedHandler(
                target,
                messageHandler,
                messageBusDeregistration,
                priority
            );
        }

        /// <summary>
        /// Registers this MessageHandler to accept fast TargetedMessages via the MessageBus, properly handling de-registration.
        /// </summary>
        /// <typeparam name="T">Type of Message to be handled.</typeparam>
        /// <param name="target">Target Id of TargetedMessages to listen for.</param>
        /// <param name="messageHandler">Function that actually handles the message.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <param name="messageBus">IMessageBus override to register with, if any. Null/not provided defaults to the GlobalMessageBus.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterTargetedMessageHandler<T>(
            InstanceId target,
            FastHandler<T> messageHandler,
            int priority = 0,
            IMessageBus messageBus = null
        )
            where T : ITargetedMessage
        {
            messageBus ??= MessageBus;
            Action messageBusDeregistration = messageBus.RegisterTargeted<T>(
                target,
                this,
                priority: priority
            );
            TypedHandler<T> typedHandler = GetOrCreateHandlerForType<T>(messageBus);
            return typedHandler.AddTargetedHandler(
                target,
                messageHandler,
                messageBusDeregistration,
                priority
            );
        }

        /// <summary>
        /// Registers this MessageHandler to post process TargetedMessages via the MessageBus, properly handling de-registration.
        /// </summary>
        /// <typeparam name="T">Type of Message to be handled.</typeparam>
        /// <param name="target">Target Id of TargetedMessages to listen for.</param>
        /// <param name="messageHandler">Function that actually handles the message.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <param name="messageBus">IMessageBus override to register with, if any. Null/not provided defaults to the GlobalMessageBus.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterTargetedPostProcessor<T>(
            InstanceId target,
            Action<T> messageHandler,
            int priority = 0,
            IMessageBus messageBus = null
        )
            where T : ITargetedMessage
        {
            messageBus ??= MessageBus;
            Action messageBusDeregistration = messageBus.RegisterTargetedPostProcessor<T>(
                target,
                this,
                priority
            );
            TypedHandler<T> typedHandler = GetOrCreateHandlerForType<T>(messageBus);
            return typedHandler.AddTargetedPostProcessor(
                target,
                messageHandler,
                messageBusDeregistration,
                priority
            );
        }

        /// <summary>
        /// Registers this MessageHandler to post process fast TargetedMessages via the MessageBus, properly handling de-registration.
        /// </summary>
        /// <typeparam name="T">Type of Message to be handled.</typeparam>
        /// <param name="target">Target Id of TargetedMessages to listen for.</param>
        /// <param name="messageHandler">Function that actually handles the message.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <param name="messageBus">IMessageBus override to register with, if any. Null/not provided defaults to the GlobalMessageBus.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterTargetedPostProcessor<T>(
            InstanceId target,
            FastHandler<T> messageHandler,
            int priority = 0,
            IMessageBus messageBus = null
        )
            where T : ITargetedMessage
        {
            messageBus ??= MessageBus;
            Action messageBusDeregistration = messageBus.RegisterTargetedPostProcessor<T>(
                target,
                this,
                priority: priority
            );
            TypedHandler<T> typedHandler = GetOrCreateHandlerForType<T>(messageBus);
            return typedHandler.AddTargetedPostProcessor(
                target,
                messageHandler,
                messageBusDeregistration,
                priority
            );
        }

        /// <summary>
        /// Registers this MessageHandler to post process TargetedMessages for all messages of the provided type via the MessageBus, properly handling de-registration.
        /// </summary>
        /// <typeparam name="T">Type of Message to be handled.</typeparam>
        /// <param name="messageHandler">Function that actually handles the message.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <param name="messageBus">IMessageBus override to register with, if any. Null/not provided defaults to the GlobalMessageBus.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterTargetedWithoutTargetingPostProcessor<T>(
            Action<InstanceId, T> messageHandler,
            int priority = 0,
            IMessageBus messageBus = null
        )
            where T : ITargetedMessage
        {
            messageBus ??= MessageBus;
            Action messageBusDeregistration =
                messageBus.RegisterTargetedWithoutTargetingPostProcessor<T>(
                    priority: priority,
                    messageHandler: this
                );
            TypedHandler<T> typedHandler = GetOrCreateHandlerForType<T>(messageBus);
            return typedHandler.AddTargetedWithoutTargetingPostProcessor(
                messageHandler,
                messageBusDeregistration,
                priority
            );
        }

        /// <summary>
        /// Registers this MessageHandler to post process fast TargetedMessages for all messages of the provided type via the MessageBus, properly handling de-registration.
        /// </summary>
        /// <typeparam name="T">Type of Message to be handled.</typeparam>
        /// <param name="messageHandler">Function that actually handles the message.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <param name="messageBus">IMessageBus override to register with, if any. Null/not provided defaults to the GlobalMessageBus.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterTargetedWithoutTargetingPostProcessor<T>(
            FastHandlerWithContext<T> messageHandler,
            int priority = 0,
            IMessageBus messageBus = null
        )
            where T : ITargetedMessage
        {
            messageBus ??= MessageBus;
            Action messageBusDeregistration =
                messageBus.RegisterTargetedWithoutTargetingPostProcessor<T>(
                    priority: priority,
                    messageHandler: this
                );
            TypedHandler<T> typedHandler = GetOrCreateHandlerForType<T>(messageBus);
            return typedHandler.AddTargetedWithoutTargetingPostProcessor(
                messageHandler,
                messageBusDeregistration,
                priority
            );
        }

        /// <summary>
        /// Registers this MessageHandler to accept TargetedMessages without Targeting via the MessageBus, properly handling de-registration.
        /// </summary>
        /// <typeparam name="T">Type of Message to be handled.</typeparam>
        /// <param name="messageHandler">Function that actually handles the message.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <param name="messageBus">IMessageBus override to register with, if any. Null/not provided defaults to the GlobalMessageBus.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterTargetedWithoutTargeting<T>(
            Action<InstanceId, T> messageHandler,
            int priority = 0,
            IMessageBus messageBus = null
        )
            where T : ITargetedMessage
        {
            messageBus ??= MessageBus;
            Action messageBusDeregistration = messageBus.RegisterTargetedWithoutTargeting<T>(
                this,
                priority: priority
            );
            TypedHandler<T> typedHandler = GetOrCreateHandlerForType<T>(messageBus);
            return typedHandler.AddTargetedWithoutTargetingHandler(
                messageHandler,
                messageBusDeregistration,
                priority
            );
        }

        /// <summary>
        /// Registers this MessageHandler to accept fast TargetedMessages without Targeting via the MessageBus, properly handling de-registration.
        /// </summary>
        /// <typeparam name="T">Type of Message to be handled.</typeparam>
        /// <param name="messageHandler">Function that actually handles the message.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <param name="messageBus">IMessageBus override to register with, if any. Null/not provided defaults to the GlobalMessageBus.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterTargetedWithoutTargeting<T>(
            FastHandlerWithContext<T> messageHandler,
            int priority = 0,
            IMessageBus messageBus = null
        )
            where T : ITargetedMessage
        {
            messageBus ??= MessageBus;
            Action messageBusDeregistration = messageBus.RegisterTargetedWithoutTargeting<T>(
                this,
                priority: priority
            );
            TypedHandler<T> typedHandler = GetOrCreateHandlerForType<T>(messageBus);
            return typedHandler.AddTargetedWithoutTargetingHandler(
                messageHandler,
                messageBusDeregistration,
                priority
            );
        }

        /// <summary>
        /// Registers this MessageHandler to accept UntargetedMessages via the MessageBus, properly handling de-registration.
        /// </summary>
        /// <typeparam name="T">Type of Message to be handled.</typeparam>
        /// <param name="messageHandler">Function that actually handles the message.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <param name="messageBus">IMessageBus override to register with, if any. Null/not provided defaults to the GlobalMessageBus.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterUntargetedMessageHandler<T>(
            Action<T> messageHandler,
            int priority = 0,
            IMessageBus messageBus = null
        )
            where T : IUntargetedMessage
        {
            messageBus ??= MessageBus;
            Action messageBusDeregistration = messageBus.RegisterUntargeted<T>(
                this,
                priority: priority
            );
            TypedHandler<T> typedHandler = GetOrCreateHandlerForType<T>(messageBus);
            return typedHandler.AddUntargetedHandler(
                messageHandler,
                messageBusDeregistration,
                priority
            );
        }

        /// <summary>
        /// Registers this MessageHandler to accept fast UntargetedMessages via the MessageBus, properly handling de-registration.
        /// </summary>
        /// <typeparam name="T">Type of Message to be handled.</typeparam>
        /// <param name="messageHandler">Function that actually handles the message.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <param name="messageBus">IMessageBus override to register with, if any. Null/not provided defaults to the GlobalMessageBus.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterUntargetedMessageHandler<T>(
            FastHandler<T> messageHandler,
            int priority = 0,
            IMessageBus messageBus = null
        )
            where T : IUntargetedMessage
        {
            messageBus ??= MessageBus;
            Action messageBusDeregistration = messageBus.RegisterUntargeted<T>(
                this,
                priority: priority
            );
            TypedHandler<T> typedHandler = GetOrCreateHandlerForType<T>(messageBus);
            return typedHandler.AddUntargetedHandler(
                messageHandler,
                messageBusDeregistration,
                priority
            );
        }

        /// <summary>
        /// Registers this MessageHandler to post process UntargetedMessages via the MessageBus, properly handling de-registration.
        /// </summary>
        /// <typeparam name="T">Type of Message to be handled.</typeparam>
        /// <param name="messageHandler">Function that actually handles the message.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <param name="messageBus">IMessageBus override to register with, if any. Null/not provided defaults to the GlobalMessageBus.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterUntargetedPostProcessor<T>(
            Action<T> messageHandler,
            int priority = 0,
            IMessageBus messageBus = null
        )
            where T : IUntargetedMessage
        {
            messageBus ??= MessageBus;
            Action messageBusDeregistration = messageBus.RegisterUntargetedPostProcessor<T>(
                priority: priority,
                messageHandler: this
            );
            TypedHandler<T> typedHandler = GetOrCreateHandlerForType<T>(messageBus);
            return typedHandler.AddUntargetedPostProcessor(
                messageHandler,
                messageBusDeregistration,
                priority
            );
        }

        /// <summary>
        /// Registers this MessageHandler to post process fast UntargetedMessages via the MessageBus, properly handling de-registration.
        /// </summary>
        /// <typeparam name="T">Type of Message to be handled.</typeparam>
        /// <param name="messageHandler">Function that actually handles the message.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <param name="messageBus">IMessageBus override to register with, if any. Null/not provided defaults to the GlobalMessageBus.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterUntargetedPostProcessor<T>(
            FastHandler<T> messageHandler,
            int priority = 0,
            IMessageBus messageBus = null
        )
            where T : IUntargetedMessage
        {
            messageBus ??= MessageBus;
            Action messageBusDeregistration = messageBus.RegisterUntargetedPostProcessor<T>(
                priority: priority,
                messageHandler: this
            );
            TypedHandler<T> typedHandler = GetOrCreateHandlerForType<T>(messageBus);
            return typedHandler.AddUntargetedPostProcessor(
                messageHandler,
                messageBusDeregistration,
                priority
            );
        }

        /// <summary>
        /// Registers this MessageHandler to accept BroadcastMessages via their MessageBus, properly handling de-registration.
        /// </summary>
        /// <typeparam name="T">Type of Message to be handled.</typeparam>
        /// <param name="source">Source Id of BroadcastMessages to listen for.</param>
        /// <param name="messageHandler">Function that actually handles the message.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <param name="messageBus">IMessageBus override to register with, if any. Null/not provided defaults to the GlobalMessageBus.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterSourcedBroadcastMessageHandler<T>(
            InstanceId source,
            Action<T> messageHandler,
            int priority = 0,
            IMessageBus messageBus = null
        )
            where T : IBroadcastMessage
        {
            messageBus ??= MessageBus;
            Action messageBusDeregistration = messageBus.RegisterSourcedBroadcast<T>(
                source,
                this,
                priority: priority
            );
            TypedHandler<T> typedHandler = GetOrCreateHandlerForType<T>(messageBus);
            ;
            return typedHandler.AddSourcedBroadcastHandler(
                source,
                messageHandler,
                messageBusDeregistration,
                priority
            );
        }

        /// <summary>
        /// Registers this MessageHandler to accept fast BroadcastMessages via their MessageBus, properly handling de-registration.
        /// </summary>
        /// <typeparam name="T">Type of Message to be handled.</typeparam>
        /// <param name="source">Source Id of BroadcastMessages to listen for.</param>
        /// <param name="messageHandler">Function that actually handles the message.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <param name="messageBus">IMessageBus override to register with, if any. Null/not provided defaults to the GlobalMessageBus.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterSourcedBroadcastMessageHandler<T>(
            InstanceId source,
            FastHandler<T> messageHandler,
            int priority = 0,
            IMessageBus messageBus = null
        )
            where T : IBroadcastMessage
        {
            messageBus ??= MessageBus;
            Action messageBusDeregistration = messageBus.RegisterSourcedBroadcast<T>(
                source,
                this,
                priority: priority
            );
            TypedHandler<T> typedHandler = GetOrCreateHandlerForType<T>(messageBus);
            return typedHandler.AddSourcedBroadcastHandler(
                source,
                messageHandler,
                messageBusDeregistration,
                priority
            );
        }

        /// <summary>
        /// Registers this MessageHandler to accept BroadcastMessage regardless of source via their MessageBus, properly handling de-registration.
        /// </summary>
        /// <typeparam name="T">Type of Message to be handled.</typeparam>
        /// <param name="messageHandler">Function that actually handles the message.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <param name="messageBus">IMessageBus override to register with, if any. Null/not provided defaults to the GlobalMessageBus.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterSourcedBroadcastWithoutSource<T>(
            Action<InstanceId, T> messageHandler,
            int priority = 0,
            IMessageBus messageBus = null
        )
            where T : IBroadcastMessage
        {
            messageBus ??= MessageBus;
            Action messageBusDeregistration = messageBus.RegisterSourcedBroadcastWithoutSource<T>(
                this,
                priority: priority
            );
            TypedHandler<T> typedHandler = GetOrCreateHandlerForType<T>(messageBus);
            return typedHandler.AddSourcedBroadcastWithoutSourceHandler(
                messageHandler,
                messageBusDeregistration,
                priority
            );
        }

        /// <summary>
        /// Registers this MessageHandler to accept fast BroadcastMessage regardless of source via their MessageBus, properly handling de-registration.
        /// </summary>
        /// <typeparam name="T">Type of Message to be handled.</typeparam>
        /// <param name="messageHandler">Function that actually handles the message.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <param name="messageBus">IMessageBus override to register with, if any. Null/not provided defaults to the GlobalMessageBus.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterSourcedBroadcastWithoutSource<T>(
            FastHandlerWithContext<T> messageHandler,
            int priority = 0,
            IMessageBus messageBus = null
        )
            where T : IBroadcastMessage
        {
            messageBus ??= MessageBus;
            Action messageBusDeregistration = messageBus.RegisterSourcedBroadcastWithoutSource<T>(
                this,
                priority: priority
            );
            TypedHandler<T> typedHandler = GetOrCreateHandlerForType<T>(messageBus);
            return typedHandler.AddSourcedBroadcastWithoutSourceHandler(
                messageHandler,
                messageBusDeregistration,
                priority
            );
        }

        /// <summary>
        /// Registers this MessageHandler to post processes BroadcastMessage messages.
        /// </summary>
        /// <typeparam name="T">Type of Message to be handled.</typeparam>
        /// <param name="source">Source object to listen for BroadcastMessages on.</param>
        /// <param name="messageHandler">Function that actually handles the message.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <param name="messageBus">IMessageBus override to register with, if any. Null/not provided defaults to the GlobalMessageBus.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterSourcedBroadcastPostProcessor<T>(
            InstanceId source,
            Action<T> messageHandler,
            int priority = 0,
            IMessageBus messageBus = null
        )
            where T : IBroadcastMessage
        {
            messageBus ??= MessageBus;
            Action messageBusDeregistration = messageBus.RegisterBroadcastPostProcessor<T>(
                source,
                messageHandler: this,
                priority: priority
            );
            TypedHandler<T> typedHandler = GetOrCreateHandlerForType<T>(messageBus);
            return typedHandler.AddBroadcastPostProcessor(
                source,
                messageHandler,
                messageBusDeregistration,
                priority
            );
        }

        /// <summary>
        /// Registers this MessageHandler to post processes fast BroadcastMessage messages.
        /// </summary>
        /// <typeparam name="T">Type of Message to be handled.</typeparam>
        /// <param name="source">Source object to listen for BroadcastMessages on.</param>
        /// <param name="messageHandler">Function that actually handles the message.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <param name="messageBus">IMessageBus override to register with, if any. Null/not provided defaults to the GlobalMessageBus.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterSourcedBroadcastPostProcessor<T>(
            InstanceId source,
            FastHandler<T> messageHandler,
            int priority = 0,
            IMessageBus messageBus = null
        )
            where T : IBroadcastMessage
        {
            messageBus ??= MessageBus;
            Action messageBusDeregistration = messageBus.RegisterBroadcastPostProcessor<T>(
                source,
                priority: priority,
                messageHandler: this
            );
            TypedHandler<T> typedHandler = GetOrCreateHandlerForType<T>(messageBus);
            return typedHandler.AddBroadcastPostProcessor(
                source,
                messageHandler,
                messageBusDeregistration,
                priority
            );
        }

        /// <summary>
        /// Registers this MessageHandler to post processes BroadcastMessage messages for all messages of the provided type.
        /// </summary>
        /// <typeparam name="T">Type of Message to be handled.</typeparam>
        /// <param name="messageHandler">Function that actually handles the message.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <param name="messageBus">IMessageBus override to register with, if any. Null/not provided defaults to the GlobalMessageBus.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterSourcedBroadcastWithoutSourcePostProcessor<T>(
            Action<InstanceId, T> messageHandler,
            int priority = 0,
            IMessageBus messageBus = null
        )
            where T : IBroadcastMessage
        {
            messageBus ??= MessageBus;
            Action messageBusDeregistration =
                messageBus.RegisterBroadcastWithoutSourcePostProcessor<T>(
                    priority: priority,
                    messageHandler: this
                );
            TypedHandler<T> typedHandler = GetOrCreateHandlerForType<T>(messageBus);
            return typedHandler.AddBroadcastWithoutSourcePostProcessor(
                messageHandler,
                messageBusDeregistration,
                priority
            );
        }

        /// <summary>
        /// Registers this MessageHandler to post processes fast BroadcastMessage messages for all messages of the provided type.
        /// </summary>
        /// <typeparam name="T">Type of Message to be handled.</typeparam>
        /// <param name="messageHandler">Function that actually handles the message.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <param name="messageBus">IMessageBus override to register with, if any. Null/not provided defaults to the GlobalMessageBus.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterSourcedBroadcastWithoutSourcePostProcessor<T>(
            FastHandlerWithContext<T> messageHandler,
            int priority = 0,
            IMessageBus messageBus = null
        )
            where T : IBroadcastMessage
        {
            messageBus ??= MessageBus;
            Action messageBusDeregistration =
                messageBus.RegisterBroadcastWithoutSourcePostProcessor<T>(
                    priority: priority,
                    messageHandler: this
                );
            TypedHandler<T> typedHandler = GetOrCreateHandlerForType<T>(messageBus);
            return typedHandler.AddBroadcastWithoutSourcePostProcessor(
                messageHandler,
                messageBusDeregistration,
                priority
            );
        }

        /// <summary>
        /// Registers an UntargetedInterceptor for messages of the provided type at the provided priority.
        /// </summary>
        /// <typeparam name="T">Type of the UntargetedMessage to intercept.</typeparam>
        /// <param name="interceptor">Interceptor to register.</param>
        /// <param name="priority">Priority to register the interceptor at (interceptors are ran from low -> high priority)</param>
        /// <param name="messageBus">Message bus to register the interceptor on.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterUntargetedInterceptor<T>(
            IMessageBus.UntargetedInterceptor<T> interceptor,
            int priority = 0,
            IMessageBus messageBus = null
        )
            where T : IUntargetedMessage
        {
            return (messageBus ?? MessageBus).RegisterUntargetedInterceptor(interceptor, priority);
        }

        /// <summary>
        /// Registers a BroadcastInterceptor for messages of the provided type at the provided priority.
        /// </summary>
        /// <typeparam name="T">Type of the BroadcastMessage to intercept.</typeparam>
        /// <param name="interceptor">Interceptor to register.</param>
        /// <param name="priority">Priority to register the interceptor at (interceptors are ran from low -> high priority)</param>
        /// <param name="messageBus">Message bus to register the interceptor on.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterBroadcastInterceptor<T>(
            IMessageBus.BroadcastInterceptor<T> interceptor,
            int priority = 0,
            IMessageBus messageBus = null
        )
            where T : IBroadcastMessage
        {
            return (messageBus ?? MessageBus).RegisterBroadcastInterceptor(interceptor, priority);
        }

        /// <summary>
        /// Registers a TargetedInterceptor for messages of the provided type at the provided priority.
        /// </summary>
        /// <typeparam name="T">Type of the TargetedMessage to intercept.</typeparam>
        /// <param name="interceptor">Interceptor to register.</param>
        /// <param name="priority">Priority to register the interceptor at (interceptors are ran from low -> high priority)</param>
        /// <param name="messageBus">Message bus to register the interceptor on.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterTargetedInterceptor<T>(
            IMessageBus.TargetedInterceptor<T> interceptor,
            int priority = 0,
            IMessageBus messageBus = null
        )
            where T : ITargetedMessage
        {
            return (messageBus ?? MessageBus).RegisterTargetedInterceptor(interceptor, priority);
        }

        public override string ToString()
        {
            return new
            {
                OwnerId = owner,
                HandlerTypes = string.Join(
                    ",",
                    _handlersByTypeByMessageBus
                        .Values.SelectMany(handlers => handlers.Keys)
                        .Distinct()
                        .Select(type => type.Name)
                        .OrderBy(_ => _)
                ),
            }.ToString();
        }

        /// <summary>
        /// Retrieves an existing Handler for the specific type, if it exists, or creates a new Handler, if none exist.
        /// </summary>
        /// <typeparam name="T">Type of Message to retrieve a Handler for.</typeparam>
        /// <returns>Non-Null Handler for the specific type.</returns>
        private TypedHandler<T> GetOrCreateHandlerForType<T>(IMessageBus messageBus)
            where T : IMessage
        {
            Type type = typeof(T);

            if (
                !_handlersByTypeByMessageBus.TryGetValue(
                    messageBus,
                    out Dictionary<Type, object> handlersByType
                )
            )
            {
                handlersByType = new Dictionary<Type, object>();
                _handlersByTypeByMessageBus[messageBus] = handlersByType;
            }

            if (handlersByType.TryGetValue(type, out object existingTypedHandler))
            {
                return (TypedHandler<T>)existingTypedHandler;
            }

            TypedHandler<T> newTypedHandler = new();
            handlersByType[type] = newTypedHandler;
            return newTypedHandler;
        }

        /// <summary>
        /// Gets an existing Handler for the specific type, if it exists.
        /// </summary>
        /// <param name="type">Message type to get the handler for.</param>
        /// <param name="messageBus">The specific MessageBus to use.</param>
        /// <param name="existingTypedHandler">Existing typed message handler, if one exists.</param>
        /// <returns>Existing handler for the specific type, or null if none exists..</returns>
        private bool GetHandlerForType<T>(
            Type type,
            IMessageBus messageBus,
            out TypedHandler<T> existingTypedHandler
        )
            where T : IMessage
        {
            if (
                _handlersByTypeByMessageBus.TryGetValue(
                    messageBus,
                    out Dictionary<Type, object> handlersByType
                ) && handlersByType.TryGetValue(type, out object untypedHandler)
            )
            {
                existingTypedHandler = (TypedHandler<T>)untypedHandler;
                return true;
            }

            existingTypedHandler = default;
            return false;
        }

        private abstract class TypedHandler
        {
            protected static readonly Stack<
                List<Action<IUntargetedMessage>>
            > GlobalUntargetedHandlersStack = new();
            protected static readonly Stack<
                List<Action<InstanceId, ITargetedMessage>>
            > GlobalTargetedHandlersStack = new();
            protected static readonly Stack<
                List<Action<InstanceId, IBroadcastMessage>>
            > GlobalBroadcastHandlersStack = new();
            protected static readonly Stack<
                List<FastHandler<IUntargetedMessage>>
            > GlobalUntargetedFastHandlersStack = new();
            protected static Stack<
                List<FastHandlerWithContext<ITargetedMessage>>
            > GlobalTargetedFastHandlersStack = new();
            protected static Stack<
                List<FastHandlerWithContext<IBroadcastMessage>>
            > GlobalBroadcastFastHandlersStack = new();
        }

        /// <summary>
        /// One-size-fits-all wrapper around all possible Messaging sinks for a particular MessageHandler & MessageType.
        /// </summary>
        /// <typeparam name="T">Message type that this Handler exists to serve.</typeparam>
        private sealed class TypedHandler<T> : TypedHandler
            where T : IMessage
        {
            // Buffers so we don't allocate memory as often
            private static Stack<List<Action<T>>> HandlersStack;
            private static Stack<List<Action<InstanceId, T>>> HandlersWithoutContextStack;
            private static Stack<List<FastHandler<T>>> FastHandlersStack;
            private static Stack<List<FastHandlerWithContext<T>>> FastHandlersWithContextStack;

            private Dictionary<
                InstanceId,
                SortedDictionary<int, Dictionary<Action<T>, int>>
            > _targetedHandlers;
            private SortedDictionary<int, Dictionary<Action<T>, int>> _untargetedHandlers;
            private Dictionary<
                InstanceId,
                SortedDictionary<int, Dictionary<Action<T>, int>>
            > _broadcastHandlers;
            private Dictionary<
                InstanceId,
                SortedDictionary<int, Dictionary<Action<T>, int>>
            > _targetedPostProcessingHandlers;
            private SortedDictionary<
                int,
                Dictionary<Action<T>, int>
            > _untargetedPostProcessingHandlers;
            private Dictionary<
                InstanceId,
                SortedDictionary<int, Dictionary<Action<T>, int>>
            > _broadcastPostProcessingHandlers;
            private Dictionary<
                InstanceId,
                SortedDictionary<int, Dictionary<FastHandler<T>, int>>
            > _targetedFastHandlers;
            private SortedDictionary<int, Dictionary<FastHandler<T>, int>> _untargetedFastHandlers;
            private Dictionary<
                InstanceId,
                SortedDictionary<int, Dictionary<FastHandler<T>, int>>
            > _broadcastFastHandlers;
            private Dictionary<
                InstanceId,
                SortedDictionary<int, Dictionary<FastHandler<T>, int>>
            > _targetedPostProcessingFastHandlers;
            private SortedDictionary<
                int,
                Dictionary<FastHandler<T>, int>
            > _untargetedPostProcessingFastHandlers;
            private Dictionary<
                InstanceId,
                SortedDictionary<int, Dictionary<FastHandler<T>, int>>
            > _broadcastPostProcessingFastHandlers;
            private Dictionary<Action<IUntargetedMessage>, int> _globalUntargetedHandlers;
            private Dictionary<Action<InstanceId, ITargetedMessage>, int> _globalTargetedHandlers;
            private Dictionary<Action<InstanceId, IBroadcastMessage>, int> _globalBroadcastHandlers;
            private Dictionary<FastHandler<IUntargetedMessage>, int> _globalUntargetedFastHandlers;
            private Dictionary<
                FastHandlerWithContext<ITargetedMessage>,
                int
            > _globalTargetedFastHandlers;
            private Dictionary<
                FastHandlerWithContext<IBroadcastMessage>,
                int
            > _globalBroadcastFastHandlers;
            private SortedDictionary<
                int,
                Dictionary<Action<InstanceId, T>, int>
            > _targetedWithoutTargetingHandlers;
            private SortedDictionary<
                int,
                Dictionary<FastHandlerWithContext<T>, int>
            > _fastTargetedWithoutTargetingHandlers;
            private SortedDictionary<
                int,
                Dictionary<Action<InstanceId, T>, int>
            > _broadcastWithoutSourceHandlers;
            private SortedDictionary<
                int,
                Dictionary<FastHandlerWithContext<T>, int>
            > _fastBroadcastWithoutSourceHandlers;
            private SortedDictionary<
                int,
                Dictionary<Action<InstanceId, T>, int>
            > _targetedWithoutTargetingPostProcessingHandlers;
            private SortedDictionary<
                int,
                Dictionary<FastHandlerWithContext<T>, int>
            > _fastTargetedWithoutTargetingPostProcessingHandlers;
            private SortedDictionary<
                int,
                Dictionary<Action<InstanceId, T>, int>
            > _broadcastWithoutSourcePostProcessingHandlers;
            private SortedDictionary<
                int,
                Dictionary<FastHandlerWithContext<T>, int>
            > _fastBroadcastWithoutSourcePostProcessingHandlers;

            /// <summary>
            /// Emits the UntargetedMessage to all subscribed listeners.
            /// </summary>
            /// <param name="message">Message to emit.</param>
            /// <param name="priority">Priority at which to run the handlers.</param>
            public void HandleUntargeted(ref T message, int priority)
            {
                RunFastHandlers(_untargetedFastHandlers, ref message, priority);
                RunHandlers(_untargetedHandlers, ref message, priority);
            }

            /// <summary>
            /// Emits the TargetedMessage to all subscribed listeners.
            /// </summary>
            /// <param name="target">Target the message is for.</param>
            /// <param name="message">Message to emit.</param>
            /// <param name="priority">Priority at which to run the handlers.</param>
            public void HandleTargeted(ref InstanceId target, ref T message, int priority)
            {
                RunFastHandlersWithContext(
                    ref target,
                    _targetedFastHandlers,
                    ref message,
                    priority
                );
                RunHandlersWithContext(ref target, _targetedHandlers, ref message, priority);
            }

            /// <summary>
            /// Emits the TargetedMessage without targeting to all subscribed listeners.
            /// </summary>
            /// <param name="target">Target the message is for.</param>
            /// <param name="message">Message to emit.</param>
            /// <param name="priority">Priority at which to run the handlers.</param>
            public void HandleTargetedWithoutTargeting(
                ref InstanceId target,
                ref T message,
                int priority
            )
            {
                RunFastHandlers(
                    ref target,
                    ref FastHandlersWithContextStack,
                    _fastTargetedWithoutTargetingHandlers,
                    ref message,
                    priority
                );
                RunHandlers(ref target, _targetedWithoutTargetingHandlers, ref message, priority);
            }

            /// <summary>
            /// Emits the BroadcastMessage to all subscribed listeners.
            /// </summary>
            /// <param name="source">Source the message is from.</param>
            /// <param name="message">Message to emit.</param>
            /// <param name="priority">Priority at which to run the handlers.</param>
            public void HandleSourcedBroadcast(ref InstanceId source, ref T message, int priority)
            {
                RunFastHandlersWithContext(
                    ref source,
                    _broadcastFastHandlers,
                    ref message,
                    priority
                );
                RunHandlersWithContext(ref source, _broadcastHandlers, ref message, priority);
            }

            /// <summary>
            /// Emits the BroadcastMessage without source to all subscribed listeners.
            /// </summary>
            /// <param name="source">Source the message is from.</param>
            /// <param name="message">Message to emit.</param>
            /// <param name="priority">Priority at which to run the handlers.</param>
            public void HandleSourcedBroadcastWithoutSource(
                ref InstanceId source,
                ref T message,
                int priority
            )
            {
                RunFastHandlers(
                    ref source,
                    ref FastHandlersWithContextStack,
                    _fastBroadcastWithoutSourceHandlers,
                    ref message,
                    priority
                );
                RunHandlers(ref source, _broadcastWithoutSourceHandlers, ref message, priority);
            }

            /// <summary>
            /// Emits the UntargetedMessage to all global listeners.
            /// </summary>
            /// <param name="message">Message to emit.</param>
            public void HandleGlobalUntargeted(ref IUntargetedMessage message)
            {
                RunFastHandlers(
                    GlobalUntargetedFastHandlersStack,
                    _globalUntargetedFastHandlers,
                    ref message
                );

                if (_globalUntargetedHandlers is not { Count: > 0 })
                {
                    return;
                }

                if (
                    GlobalUntargetedHandlersStack.TryPop(
                        out List<Action<IUntargetedMessage>> handlers
                    )
                )
                {
                    handlers.Clear();
                    handlers.AddRange(_globalUntargetedHandlers.Keys);
                }
                else
                {
                    handlers = new List<Action<IUntargetedMessage>>(_globalUntargetedHandlers.Keys);
                }

                try
                {
                    foreach (Action<IUntargetedMessage> handler in handlers)
                    {
                        handler(message);
                    }
                }
                finally
                {
                    GlobalUntargetedHandlersStack.Push(handlers);
                }
            }

            /// <summary>
            /// Emits the TargetedMessage to all global listeners.
            /// </summary>
            /// <param name="target">Target that this message is intended for.</param>
            /// <param name="message">Message to emit.</param>
            public void HandleGlobalTargeted(ref InstanceId target, ref ITargetedMessage message)
            {
                RunFastHandlers(
                    ref target,
                    ref GlobalTargetedFastHandlersStack,
                    _globalTargetedFastHandlers,
                    ref message
                );

                if (_globalTargetedHandlers is not { Count: > 0 })
                {
                    return;
                }

                if (
                    GlobalTargetedHandlersStack.TryPop(
                        out List<Action<InstanceId, ITargetedMessage>> handlers
                    )
                )
                {
                    handlers.Clear();
                    handlers.AddRange(_globalTargetedHandlers.Keys);
                }
                else
                {
                    handlers = new List<Action<InstanceId, ITargetedMessage>>(
                        _globalTargetedHandlers.Keys
                    );
                }

                try
                {
                    foreach (Action<InstanceId, ITargetedMessage> handler in handlers)
                    {
                        handler(target, message);
                    }
                }
                finally
                {
                    GlobalTargetedHandlersStack.Push(handlers);
                }
            }

            /// <summary>
            /// Emits the BroadcastMessage to all global listeners.
            /// </summary>
            /// <param name="source">Source that this message is from.</param>
            /// <param name="message">Message to emit.</param>
            public void HandleGlobalBroadcast(ref InstanceId source, ref IBroadcastMessage message)
            {
                RunFastHandlers(
                    ref source,
                    ref GlobalBroadcastFastHandlersStack,
                    _globalBroadcastFastHandlers,
                    ref message
                );

                if (_globalBroadcastHandlers is not { Count: > 0 })
                {
                    return;
                }

                if (
                    GlobalBroadcastHandlersStack.TryPop(
                        out List<Action<InstanceId, IBroadcastMessage>> handlers
                    )
                )
                {
                    handlers.Clear();
                    handlers.AddRange(_globalBroadcastHandlers.Keys);
                }
                else
                {
                    handlers = new List<Action<InstanceId, IBroadcastMessage>>(
                        _globalBroadcastHandlers.Keys
                    );
                }

                try
                {
                    foreach (Action<InstanceId, IBroadcastMessage> handler in handlers)
                    {
                        handler(source, message);
                    }
                }
                finally
                {
                    GlobalBroadcastHandlersStack.Push(handlers);
                }
            }

            public void HandleUntargetedPostProcessing(ref T message, int priority)
            {
                RunFastHandlers(_untargetedPostProcessingFastHandlers, ref message, priority);
                RunHandlers(_untargetedPostProcessingHandlers, ref message, priority);
            }

            public void HandleTargetedPostProcessing(
                ref InstanceId target,
                ref T message,
                int priority
            )
            {
                RunFastHandlersWithContext(
                    ref target,
                    _targetedPostProcessingFastHandlers,
                    ref message,
                    priority
                );
                RunHandlersWithContext(
                    ref target,
                    _targetedPostProcessingHandlers,
                    ref message,
                    priority
                );
            }

            public void HandleTargetedWithoutTargetingPostProcessing(
                ref InstanceId target,
                ref T message,
                int priority
            )
            {
                RunFastHandlersWithContext(
                    ref target,
                    _fastTargetedWithoutTargetingPostProcessingHandlers,
                    ref message,
                    priority
                );
                RunHandlers(
                    ref target,
                    _targetedWithoutTargetingPostProcessingHandlers,
                    ref message,
                    priority
                );
            }

            public void HandleSourcedBroadcastPostProcessing(
                ref InstanceId source,
                ref T message,
                int priority
            )
            {
                RunFastHandlersWithContext(
                    ref source,
                    _broadcastPostProcessingFastHandlers,
                    ref message,
                    priority
                );
                RunHandlersWithContext(
                    ref source,
                    _broadcastPostProcessingHandlers,
                    ref message,
                    priority
                );
            }

            public void HandleBroadcastWithoutSourcePostProcessing(
                ref InstanceId source,
                ref T message,
                int priority
            )
            {
                RunFastHandlersWithContext(
                    ref source,
                    _fastBroadcastWithoutSourcePostProcessingHandlers,
                    ref message,
                    priority
                );
                RunHandlers(
                    ref source,
                    _broadcastWithoutSourcePostProcessingHandlers,
                    ref message,
                    priority
                );
            }

            /// <summary>
            /// Adds a TargetedHandler to listen to Messages of the given type, returning a de-registration action.
            /// </summary>
            /// <param name="target">Target the handler is for.</param>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <param name="priority">Priority at which to add the handler.</param>
            /// <returns>De-registration action to un-register the handler.</returns>
            public Action AddTargetedHandler(
                InstanceId target,
                Action<T> handler,
                Action deregistration,
                int priority
            )
            {
                return AddHandler(target, ref _targetedHandlers, handler, deregistration, priority);
            }

            /// <summary>
            /// Adds a fast TargetedHandler to listen to Messages of the given type, returning a de-registration action.
            /// </summary>
            /// <param name="target">Target the handler is for.</param>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <param name="priority">Priority at which to add the handler.</param>
            /// <returns>De-registration action to un-register the handler.</returns>
            public Action AddTargetedHandler(
                InstanceId target,
                FastHandler<T> handler,
                Action deregistration,
                int priority
            )
            {
                return AddHandler(
                    target,
                    ref _targetedFastHandlers,
                    handler,
                    deregistration,
                    priority
                );
            }

            /// <summary>
            /// Adds a TargetedWithoutTargetingHandler to listen to Messages of the given type, returning a de-registration action.
            /// </summary>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <param name="priority">Priority at which to add the handler.</param>
            /// <returns>De-registration action to un-register the handler.</returns>
            public Action AddTargetedWithoutTargetingHandler(
                Action<InstanceId, T> handler,
                Action deregistration,
                int priority
            )
            {
                return AddHandler(
                    ref _targetedWithoutTargetingHandlers,
                    handler,
                    deregistration,
                    priority
                );
            }

            /// <summary>
            /// Adds a fast TargetedWithoutTargetingHandler to listen to Messages of the given type, returning a de-registration action.
            /// </summary>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <param name="priority">Priority at which to add the handler.</param>
            /// <returns>De-registration action to un-register the handler.</returns>
            public Action AddTargetedWithoutTargetingHandler(
                FastHandlerWithContext<T> handler,
                Action deregistration,
                int priority
            )
            {
                return AddHandler(
                    ref _fastTargetedWithoutTargetingHandlers,
                    handler,
                    deregistration,
                    priority
                );
            }

            /// <summary>
            /// Adds a UntargetedHandler to listen to Messages of the given type, returning a de-registration action.
            /// </summary>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <param name="priority">Priority at which to add the handler.</param>
            /// <returns>De-registration action to un-register the handler.</returns>
            public Action AddUntargetedHandler(
                Action<T> handler,
                Action deregistration,
                int priority
            )
            {
                return AddHandler(ref _untargetedHandlers, handler, deregistration, priority);
            }

            /// <summary>
            /// Adds a fast UntargetedHandler to listen to Messages of the given type, returning a de-registration action.
            /// </summary>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <param name="priority">Priority at which to add the handler.</param>
            /// <returns>De-registration action to un-register the handler.</returns>
            public Action AddUntargetedHandler(
                FastHandler<T> handler,
                Action deregistration,
                int priority
            )
            {
                return AddHandler(ref _untargetedFastHandlers, handler, deregistration, priority);
            }

            /// <summary>
            /// Adds a SourcedBroadcastHandler to listen to Messages of the given type from an entity, returning a de-registration action.
            /// </summary>
            /// <param name="source">Source of the handler is for.</param>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <param name="priority">Priority at which to add the handler.</param>
            /// <returns>De-registration action to un-register the handler.</returns>
            public Action AddSourcedBroadcastHandler(
                InstanceId source,
                Action<T> handler,
                Action deregistration,
                int priority
            )
            {
                return AddHandler(
                    source,
                    ref _broadcastHandlers,
                    handler,
                    deregistration,
                    priority
                );
            }

            /// <summary>
            /// Adds a fast SourcedBroadcastHandler to listen to Messages of the given type from an entity, returning a de-registration action.
            /// </summary>
            /// <param name="source">Source of the handler is for.</param>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <param name="priority">Priority at which to add the handler.</param>
            /// <returns>De-registration action to un-register the handler.</returns>
            public Action AddSourcedBroadcastHandler(
                InstanceId source,
                FastHandler<T> handler,
                Action deregistration,
                int priority
            )
            {
                return AddHandler(
                    source,
                    ref _broadcastFastHandlers,
                    handler,
                    deregistration,
                    priority
                );
            }

            /// <summary>
            /// Adds a SourcedBroadcastWithoutSourceHandler to listen to Messages of the given type from an entity, returning a de-registration action.
            /// </summary>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <param name="priority">Priority at which to add the handler.</param>
            /// <returns>De-registration action to un-register the handler.</returns>
            public Action AddSourcedBroadcastWithoutSourceHandler(
                Action<InstanceId, T> handler,
                Action deregistration,
                int priority
            )
            {
                return AddHandler(
                    ref _broadcastWithoutSourceHandlers,
                    handler,
                    deregistration,
                    priority
                );
            }

            /// <summary>
            /// Adds a fast SourcedBroadcastWithoutSourceHandler to listen to Messages of the given type from an entity, returning a de-registration action.
            /// </summary>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <param name="priority">Priority at which to add the handler.</param>
            /// <returns>De-registration action to un-register the handler.</returns>
            public Action AddSourcedBroadcastWithoutSourceHandler(
                FastHandlerWithContext<T> handler,
                Action deregistration,
                int priority
            )
            {
                return AddHandler(
                    ref _fastBroadcastWithoutSourceHandlers,
                    handler,
                    deregistration,
                    priority
                );
            }

            /// <summary>
            /// Adds a Global UntargetedHandler to listen to all Untargeted Messages of all types, returning the de-registration action.
            /// </summary>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <returns>De-registration action to un-register the handler.</returns>
            public Action AddGlobalUntargetedHandler(
                Action<IUntargetedMessage> handler,
                Action deregistration
            )
            {
                return AddHandler(ref _globalUntargetedHandlers, handler, deregistration);
            }

            /// <summary>
            /// Adds a Global fast UntargetedHandler to listen to all Untargeted Messages of all types, returning the de-registration action.
            /// </summary>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <returns>De-registration action to un-register the handler.</returns>

            public Action AddGlobalUntargetedHandler(
                FastHandler<IUntargetedMessage> handler,
                Action deregistration
            )
            {
                return AddHandler(ref _globalUntargetedFastHandlers, handler, deregistration);
            }

            /// <summary>
            /// Adds a Global TargetedHandler to listen to all Targeted Messages of all types for all entities, returning the de-registration action.
            /// </summary>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <returns>De-registration action to un-register the handler.</returns>
            public Action AddGlobalTargetedHandler(
                Action<InstanceId, ITargetedMessage> handler,
                Action deregistration
            )
            {
                return AddHandler(ref _globalTargetedHandlers, handler, deregistration);
            }

            /// <summary>
            /// Adds a Global fast TargetedHandler to listen to all Targeted Messages of all types for all entities (along with the target instance id), returning the de-registration action.
            /// </summary>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <returns>De-registration action to un-register the handler.</returns>

            public Action AddGlobalTargetedHandler(
                FastHandlerWithContext<ITargetedMessage> handler,
                Action deregistration
            )
            {
                return AddHandler(ref _globalTargetedFastHandlers, handler, deregistration);
            }

            /// <summary>
            /// Adds a Global BroadcastHandler to listen to all Targeted Messages of all types for all entities, returning the de-registration action.
            /// </summary>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <returns>De-registration action to un-register the handler.</returns>
            public Action AddGlobalBroadcastHandler(
                Action<InstanceId, IBroadcastMessage> handler,
                Action deregistration
            )
            {
                return AddHandler(ref _globalBroadcastHandlers, handler, deregistration);
            }

            /// <summary>
            /// Adds a Global fast BroadcastHandler to listen to all Targeted Messages of all types for all entities (along with the source instance id), returning the de-registration action.
            /// </summary>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <returns>De-registration action to un-register the handler.</returns>
            public Action AddGlobalBroadcastHandler(
                FastHandlerWithContext<IBroadcastMessage> handler,
                Action deregistration
            )
            {
                return AddHandler(ref _globalBroadcastFastHandlers, handler, deregistration);
            }

            /// <summary>
            /// Adds an Untargeted post processor to be called after all other handlers have been called.
            /// </summary>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <param name="priority">Priority at which to add the handler.</param>
            /// <returns>De-registration action to un-register the handler.</returns>
            public Action AddUntargetedPostProcessor(
                Action<T> handler,
                Action deregistration,
                int priority
            )
            {
                return AddHandler(
                    ref _untargetedPostProcessingHandlers,
                    handler,
                    deregistration,
                    priority
                );
            }

            /// <summary>
            /// Adds a fast Untargeted post processor to be called after all other handlers have been called.
            /// </summary>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <param name="priority">Priority at which to add the handler.</param>
            /// <returns>De-registration action to un-register the handler.</returns>
            public Action AddUntargetedPostProcessor(
                FastHandler<T> handler,
                Action deregistration,
                int priority
            )
            {
                return AddHandler(
                    ref _untargetedPostProcessingFastHandlers,
                    handler,
                    deregistration,
                    priority
                );
            }

            /// <summary>
            /// Adds an Targeted post processor to be called after all other handlers have been called.
            /// </summary>
            /// <param name="target">Target the handler is for.</param>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <param name="priority">Priority at which to add the handler.</param>
            /// <returns>De-registration action to un-register the handler.</returns>
            public Action AddTargetedPostProcessor(
                InstanceId target,
                Action<T> handler,
                Action deregistration,
                int priority
            )
            {
                return AddHandler(
                    target,
                    ref _targetedPostProcessingHandlers,
                    handler,
                    deregistration,
                    priority
                );
            }

            /// <summary>
            /// Adds a Targeted post processor to be called after all other handlers have been called.
            /// </summary>
            /// <param name="target">Target the handler is for.</param>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <param name="priority">Priority at which to add the handler.</param>
            /// <returns>De-registration action to un-register the handler.</returns>
            public Action AddTargetedPostProcessor(
                InstanceId target,
                FastHandler<T> handler,
                Action deregistration,
                int priority
            )
            {
                return AddHandler(
                    target,
                    ref _targetedPostProcessingFastHandlers,
                    handler,
                    deregistration,
                    priority
                );
            }

            /// <summary>
            /// Adds a Targeted post processor to be called after all other handlers have been called after every message of the given type.
            /// </summary>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <param name="priority">Priority at which to add the handler.</param>
            /// <returns>De-registration action to un-register the handler.</returns>
            public Action AddTargetedWithoutTargetingPostProcessor(
                Action<InstanceId, T> handler,
                Action deregistration,
                int priority
            )
            {
                return AddHandler(
                    ref _targetedWithoutTargetingPostProcessingHandlers,
                    handler,
                    deregistration,
                    priority
                );
            }

            /// <summary>
            /// Adds a Targeted post processor to be called after all other handlers have been called after every message of the given type.
            /// </summary>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <param name="priority">Priority at which to add the handler.</param>
            /// <returns>De-registration action to un-register the handler.</returns>
            public Action AddTargetedWithoutTargetingPostProcessor(
                FastHandlerWithContext<T> handler,
                Action deregistration,
                int priority
            )
            {
                return AddHandler(
                    ref _fastTargetedWithoutTargetingPostProcessingHandlers,
                    handler,
                    deregistration,
                    priority
                );
            }

            /// <summary>
            /// Adds a Broadcast post processor to be called after all other handlers have been called.
            /// </summary>
            /// <param name="source">Source the handler is for.</param>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <param name="priority">Priority at which to add the handler.</param>
            /// <returns>De-registration action to un-register the handler.</returns>
            public Action AddBroadcastPostProcessor(
                InstanceId source,
                Action<T> handler,
                Action deregistration,
                int priority
            )
            {
                return AddHandler(
                    source,
                    ref _broadcastPostProcessingHandlers,
                    handler,
                    deregistration,
                    priority
                );
            }

            /// <summary>
            /// Adds a fast Broadcast post processor to be called after all other handlers have been called.
            /// </summary>
            /// <param name="source">Source the handler is for.</param>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <param name="priority">Priority at which to add the handler.</param>
            /// <returns>De-registration action to un-register the handler.</returns>
            public Action AddBroadcastPostProcessor(
                InstanceId source,
                FastHandler<T> handler,
                Action deregistration,
                int priority
            )
            {
                return AddHandler(
                    source,
                    ref _broadcastPostProcessingFastHandlers,
                    handler,
                    deregistration,
                    priority
                );
            }

            /// <summary>
            /// Adds a Broadcast post processor to be called after all other handlers have been called for every message of the given type.
            /// </summary>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <param name="priority">Priority at which to add the handler.</param>
            /// <returns>De-registration action to un-register the handler.</returns>
            public Action AddBroadcastWithoutSourcePostProcessor(
                Action<InstanceId, T> handler,
                Action deregistration,
                int priority
            )
            {
                return AddHandler(
                    ref _broadcastWithoutSourcePostProcessingHandlers,
                    handler,
                    deregistration,
                    priority
                );
            }

            /// <summary>
            /// Adds a fast Broadcast post processor to be called after all other handlers have been called.
            /// </summary>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <param name="priority">Priority at which to add the handler.</param>
            /// <returns>De-registration action to un-register the handler.</returns>
            public Action AddBroadcastWithoutSourcePostProcessor(
                FastHandlerWithContext<T> handler,
                Action deregistration,
                int priority
            )
            {
                return AddHandler(
                    ref _fastBroadcastWithoutSourcePostProcessingHandlers,
                    handler,
                    deregistration,
                    priority
                );
            }

            private static void RunFastHandlersWithContext<TMessage>(
                ref InstanceId context,
                SortedDictionary<
                    int,
                    Dictionary<FastHandlerWithContext<T>, int>
                > fastHandlersByContext,
                ref TMessage message,
                int priority
            )
                where TMessage : IMessage
            {
                if (fastHandlersByContext is not { Count: > 0 })
                {
                    return;
                }

                RunFastHandlers(
                    ref context,
                    ref FastHandlersWithContextStack,
                    fastHandlersByContext,
                    ref message,
                    priority
                );
            }

            private static void RunFastHandlersWithContext<TMessage>(
                ref InstanceId context,
                Dictionary<
                    InstanceId,
                    SortedDictionary<int, Dictionary<FastHandler<T>, int>>
                > fastHandlersByContext,
                ref TMessage message,
                int priority
            )
                where TMessage : IMessage
            {
                if (
                    fastHandlersByContext is not { Count: > 0 }
                    || !fastHandlersByContext.TryGetValue(
                        context,
                        out SortedDictionary<int, Dictionary<FastHandler<T>, int>> fastHandlers
                    )
                )
                {
                    return;
                }

                RunFastHandlers(fastHandlers, ref message, priority);
            }

            private static void RunFastHandlers<TMessage>(
                SortedDictionary<int, Dictionary<FastHandler<T>, int>> fastHandlers,
                ref TMessage message,
                int priority
            )
                where TMessage : IMessage
            {
                if (fastHandlers is not { Count: > 0 })
                {
                    return;
                }

                if (
                    !fastHandlers.TryGetValue(
                        priority,
                        out Dictionary<FastHandler<T>, int> fastHandlersByPriority
                    )
                )
                {
                    return;
                }

                ref T typedMessage = ref Unsafe.As<TMessage, T>(ref message);

                List<FastHandler<T>> handlers = GetOrAddNewHandlerStack(
                    ref FastHandlersStack,
                    fastHandlersByPriority.Keys
                );
                try
                {
                    foreach (FastHandler<T> fastHandler in handlers)
                    {
                        fastHandler(ref typedMessage);
                    }
                }
                finally
                {
                    FastHandlersStack.Push(handlers);
                }
            }

            private static void RunFastHandlers<TMessage, U>(
                Stack<List<FastHandler<U>>> stack,
                Dictionary<FastHandler<U>, int> fastHandlers,
                ref TMessage message
            )
                where TMessage : IMessage
                where U : IMessage
            {
                if (fastHandlers is not { Count: > 0 })
                {
                    return;
                }

                ref U typedMessage = ref Unsafe.As<TMessage, U>(ref message);

                List<FastHandler<U>> handlers = GetOrAddNewHandlerStack(
                    ref stack,
                    fastHandlers.Keys
                );
                try
                {
                    foreach (FastHandler<U> fastHandler in handlers)
                    {
                        fastHandler(ref typedMessage);
                    }
                }
                finally
                {
                    stack.Push(handlers);
                }
            }

            private static void RunFastHandlers<TMessage, U>(
                ref InstanceId context,
                ref Stack<List<FastHandlerWithContext<U>>> stack,
                Dictionary<FastHandlerWithContext<U>, int> priorityHandlers,
                ref TMessage message
            )
                where TMessage : IMessage
                where U : IMessage
            {
                if (priorityHandlers is not { Count: > 0 })
                {
                    return;
                }

                ref U typedMessage = ref Unsafe.As<TMessage, U>(ref message);

                List<FastHandlerWithContext<U>> handlers = GetOrAddNewHandlerStack(
                    ref stack,
                    priorityHandlers.Keys
                );
                try
                {
                    foreach (FastHandlerWithContext<U> fastHandler in handlers)
                    {
                        fastHandler(ref context, ref typedMessage);
                    }
                }
                finally
                {
                    stack.Push(handlers);
                }
            }

            private static void RunFastHandlers<TMessage, U>(
                ref InstanceId context,
                ref Stack<List<FastHandlerWithContext<U>>> stack,
                SortedDictionary<int, Dictionary<FastHandlerWithContext<U>, int>> fastHandlers,
                ref TMessage message,
                int priority
            )
                where TMessage : IMessage
                where U : IMessage
            {
                if (fastHandlers is not { Count: > 0 })
                {
                    return;
                }

                if (
                    !fastHandlers.TryGetValue(
                        priority,
                        out Dictionary<FastHandlerWithContext<U>, int> priorityHandlers
                    )
                )
                {
                    return;
                }

                ref U typedMessage = ref Unsafe.As<TMessage, U>(ref message);

                List<FastHandlerWithContext<U>> handlers = GetOrAddNewHandlerStack(
                    ref stack,
                    priorityHandlers.Keys
                );
                try
                {
                    foreach (FastHandlerWithContext<U> fastHandler in handlers)
                    {
                        fastHandler(ref context, ref typedMessage);
                    }
                }
                finally
                {
                    stack.Push(handlers);
                }
            }

            private static void RunHandlersWithContext<TMessage>(
                ref InstanceId context,
                Dictionary<
                    InstanceId,
                    SortedDictionary<int, Dictionary<Action<T>, int>>
                > handlersByContext,
                ref TMessage message,
                int priority
            )
                where TMessage : IMessage
            {
                if (
                    handlersByContext is not { Count: > 0 }
                    || !handlersByContext.TryGetValue(
                        context,
                        out SortedDictionary<int, Dictionary<Action<T>, int>> handlers
                    )
                )
                {
                    return;
                }

                RunHandlers(handlers, ref message, priority);
            }

            private static void RunHandlers<TMessage>(
                SortedDictionary<int, Dictionary<Action<T>, int>> sortedHandlers,
                ref TMessage message,
                int priority
            )
                where TMessage : IMessage
            {
                if (sortedHandlers is not { Count: > 0 })
                {
                    return;
                }

                if (!sortedHandlers.TryGetValue(priority, out Dictionary<Action<T>, int> handlers))
                {
                    return;
                }

                List<Action<T>> typedHandlers = GetOrAddNewHandlerStack(
                    ref HandlersStack,
                    handlers.Keys
                );
                try
                {
                    ref T typedMessage = ref Unsafe.As<TMessage, T>(ref message);

                    foreach (Action<T> handler in typedHandlers)
                    {
                        handler(typedMessage);
                    }
                }
                finally
                {
                    HandlersStack.Push(typedHandlers);
                }
            }

            private static void RunHandlers<TMessage>(
                ref InstanceId context,
                SortedDictionary<int, Dictionary<Action<InstanceId, T>, int>> handlers,
                ref TMessage message,
                int priority
            )
                where TMessage : IMessage
            {
                if (handlers is not { Count: > 0 })
                {
                    return;
                }

                if (
                    !handlers.TryGetValue(
                        priority,
                        out Dictionary<Action<InstanceId, T>, int> handlersByPriority
                    )
                )
                {
                    return;
                }

                List<Action<InstanceId, T>> typedHandlers = GetOrAddNewHandlerStack(
                    ref HandlersWithoutContextStack,
                    handlersByPriority.Keys
                );
                try
                {
                    ref T typedMessage = ref Unsafe.As<TMessage, T>(ref message);

                    foreach (Action<InstanceId, T> handler in typedHandlers)
                    {
                        handler(context, typedMessage);
                    }
                }
                finally
                {
                    HandlersWithoutContextStack.Push(typedHandlers);
                }
            }

            private static List<U> GetOrAddNewHandlerStack<U>(
                ref Stack<List<U>> stack,
                IEnumerable<U> handlers
            )
            {
                stack ??= new Stack<List<U>>();
                if (!stack.TryPop(out List<U> typedHandlerStack))
                {
                    return new List<U>(handlers);
                }

                typedHandlerStack.Clear();
                typedHandlerStack.AddRange(handlers);
                return typedHandlerStack;
            }

            private static Action AddHandler<U>(
                InstanceId context,
                ref Dictionary<
                    InstanceId,
                    SortedDictionary<int, Dictionary<U, int>>
                > handlersByContext,
                U handler,
                Action deregistration,
                int priority
            )
            {
                handlersByContext ??=
                    new Dictionary<InstanceId, SortedDictionary<int, Dictionary<U, int>>>();

                if (
                    !handlersByContext.TryGetValue(
                        context,
                        out SortedDictionary<int, Dictionary<U, int>> sortedHandlers
                    )
                )
                {
                    sortedHandlers = new SortedDictionary<int, Dictionary<U, int>>();
                    handlersByContext[context] = sortedHandlers;
                }

                if (!sortedHandlers.TryGetValue(priority, out Dictionary<U, int> handlers))
                {
                    handlers = new Dictionary<U, int>();
                    sortedHandlers[priority] = handlers;
                }

                int count = handlers.GetValueOrDefault(handler, 0);

                handlers[handler] = count + 1;

                Dictionary<
                    InstanceId,
                    SortedDictionary<int, Dictionary<U, int>>
                > localHandlersByContext = handlersByContext;

                return () =>
                {
                    if (!localHandlersByContext.TryGetValue(context, out sortedHandlers))
                    {
                        return;
                    }

                    if (!sortedHandlers.TryGetValue(priority, out handlers))
                    {
                        return;
                    }

                    if (!handlers.TryGetValue(handler, out count))
                    {
                        return;
                    }

                    // Always invoke deregistration action, as MessageBus dedupes this as well
                    deregistration?.Invoke();

                    if (count <= 1)
                    {
                        _ = handlers.Remove(handler);
                        return;
                    }

                    handlers[handler] = count - 1;

                    if (handlers.Count <= 0)
                    {
                        _ = localHandlersByContext.Remove(context);
                    }
                };
            }

            private static Action AddHandler<U>(
                ref Dictionary<U, int> handlersByPriority,
                U handler,
                Action deregistration
            )
            {
                handlersByPriority ??= new Dictionary<U, int>();

                int count = handlersByPriority.GetValueOrDefault(handler, 0);

                handlersByPriority[handler] = count + 1;

                Dictionary<U, int> localHandlers = handlersByPriority;

                return () =>
                {
                    if (!localHandlers.TryGetValue(handler, out count))
                    {
                        return;
                    }

                    // Always invoke deregistration action, as MessageBus dedupes this as well
                    deregistration?.Invoke();
                    if (count <= 1)
                    {
                        _ = localHandlers.Remove(handler);
                        return;
                    }

                    localHandlers[handler] = count - 1;
                };
            }

            private static Action AddHandler<U>(
                ref SortedDictionary<int, Dictionary<U, int>> sortedHandlers,
                U handler,
                Action deregistration,
                int priority
            )
            {
                sortedHandlers ??= new SortedDictionary<int, Dictionary<U, int>>();

                if (
                    !sortedHandlers.TryGetValue(priority, out Dictionary<U, int> handlersByPriority)
                )
                {
                    handlersByPriority = new Dictionary<U, int>();
                    sortedHandlers[priority] = handlersByPriority;
                }

                int count = handlersByPriority.GetValueOrDefault(handler, 0);

                handlersByPriority[handler] = count + 1;

                SortedDictionary<int, Dictionary<U, int>> localSortedHandlers = sortedHandlers;
                Dictionary<U, int> localHandlers = handlersByPriority;

                return () =>
                {
                    if (!localHandlers.TryGetValue(handler, out count))
                    {
                        return;
                    }

                    // Always invoke deregistration action, as MessageBus dedupes this as well
                    deregistration?.Invoke();
                    if (count <= 1)
                    {
                        _ = localHandlers.Remove(handler);

                        if (localHandlers.Count <= 0)
                        {
                            _ = localSortedHandlers.Remove(priority);
                        }

                        return;
                    }

                    localHandlers[handler] = count - 1;
                };
            }
        }
    }
}
