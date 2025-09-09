namespace DxMessaging.Core
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using Helper;
    using MessageBus;
    using Messages;

    /// <summary>
    /// Abstraction layer for immediate-mode Message passing. An instance of this handles all
    /// kinds of types to trigger functions that are registered with it.
    /// </summary>
    public sealed class MessageHandler
        : IEquatable<MessageHandler>,
            IComparable,
            IComparable<MessageHandler>
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
        /// Whether this MessageHandler will process messages.
        /// </summary>
        public bool active;

        /// <summary>
        /// The Id of the GameObject that owns us.
        /// </summary>
        public readonly InstanceId owner;

        /// <summary>
        /// Maps Types to the corresponding Handler of that type.
        /// </summary>
        /// <note>
        /// Ideally, this would be something like a Dictionary[T, Handler[T]], but that can't be done with C#s type system.
        /// </note>
        private readonly List<MessageCache<object>> _handlersByTypeByMessageBus;

        public MessageHandler(InstanceId owner)
        {
            this.owner = owner;
            _handlersByTypeByMessageBus = new List<MessageCache<object>>();
        }

        /// <summary>
        /// Callback from the MessageBus for handling UntargetedMessages - user code should generally never use this.
        /// </summary>
        /// <note>
        /// In this case, "UntargetedMessage" refers to Targeted without targeting, and UntargetedMessages, hence T : IMessage.
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

            if (GetHandlerForType(messageBus, out TypedHandler<TMessage> handler))
            {
                handler.HandleUntargeted(ref message, priority);
            }
        }

        /// <summary>
        /// Callback from the MessageBus for handling UntargetedMessages - user code should generally never use this.
        /// </summary>
        /// <note>
        /// In this case, "UntargetedMessage" refers to Targeted without targeting, and UntargetedMessages, hence T : IUntargetedMessage.
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

            if (GetHandlerForType(messageBus, out TypedHandler<TMessage> handler))
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

            if (GetHandlerForType(messageBus, out TypedHandler<TMessage> handler))
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

            if (GetHandlerForType(messageBus, out TypedHandler<TMessage> handler))
            {
                handler.HandleTargetedWithoutTargeting(ref target, ref message, priority);
            }
        }

        /// <summary>
        /// Callback from the MessageBus for post-processing TargetedMessages when this MessageHandler has subscribed - user code should generally never use this.
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

            if (GetHandlerForType(messageBus, out TypedHandler<TMessage> handler))
            {
                handler.HandleTargetedPostProcessing(ref target, ref message, priority);
            }
        }

        /// <summary>
        /// Callback from the MessageBus for post-processing TargetedMessages when this MessageHandler has subscribed - user code should generally never use this.
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

            if (GetHandlerForType(messageBus, out TypedHandler<TMessage> handler))
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

            if (GetHandlerForType(messageBus, out TypedHandler<TMessage> handler))
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

            if (GetHandlerForType(messageBus, out TypedHandler<TMessage> handler))
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

            if (GetHandlerForType(messageBus, out TypedHandler<TMessage> handler))
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

            if (GetHandlerForType(messageBus, out TypedHandler<TMessage> handler))
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

            // Use the "IMessage" explicitly to indicate global messages, allowing us to multipurpose a single dictionary
            if (GetHandlerForType(messageBus, out TypedHandler<IMessage> handler))
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

            // Use the "IMessage" explicitly to indicate global messages, allowing us to multipurpose a single dictionary
            if (GetHandlerForType(messageBus, out TypedHandler<IMessage> handler))
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

            // Use the "IMessage" explicitly to indicate global messages, allowing us to multipurpose a single dictionary
            if (GetHandlerForType(messageBus, out TypedHandler<IMessage> handler))
            {
                handler.HandleGlobalBroadcast(ref source, ref message);
            }
        }

        /// <summary>
        /// Registers this MessageHandler to Globally Accept All Messages via the MessageBus, properly handling deregistration.
        /// </summary>
        /// <param name="untargetedMessageHandler">MessageHandler to accept all UntargetedMessages.</param>
        /// <param name="broadcastMessageHandler">MessageHandler to accept all TargetedMessages for all entities.</param>
        /// <param name="targetedMessageHandler">MessageHandler to accept all BroadcastMessages for all entities.</param>
        /// <param name="messageBus">IMessageBus override to register with, if any. Null/not provided defaults to the GlobalMessageBus.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterGlobalAcceptAll(
            Action<IUntargetedMessage> originalUntargetedMessageHandler,
            Action<IUntargetedMessage> untargetedMessageHandler,
            Action<InstanceId, ITargetedMessage> originalTargetedMessageHandler,
            Action<InstanceId, ITargetedMessage> targetedMessageHandler,
            Action<InstanceId, IBroadcastMessage> originalBroadcastMessageHandler,
            Action<InstanceId, IBroadcastMessage> broadcastMessageHandler,
            IMessageBus messageBus = null
        )
        {
            messageBus ??= MessageBus;
            Action messageBusDeregistration = messageBus.RegisterGlobalAcceptAll(this);
            TypedHandler<IMessage> typedHandler = GetOrCreateHandlerForType<IMessage>(messageBus);

            Action untargetedDeregistration = typedHandler.AddGlobalUntargetedHandler(
                originalUntargetedMessageHandler,
                untargetedMessageHandler,
                NullDeregistration
            );
            Action targetedDeregistration = typedHandler.AddGlobalTargetedHandler(
                originalTargetedMessageHandler,
                targetedMessageHandler,
                NullDeregistration
            );
            Action broadcastDeregistration = typedHandler.AddGlobalBroadcastHandler(
                originalBroadcastMessageHandler,
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
        /// Registers this MessageHandler to Globally Accept All Messages via the MessageBus, properly handling deregistration.
        /// </summary>
        /// <param name="untargetedMessageHandler">MessageHandler to accept all UntargetedMessages.</param>
        /// <param name="broadcastMessageHandler">MessageHandler to accept all TargetedMessages for all entities.</param>
        /// <param name="targetedMessageHandler">MessageHandler to accept all BroadcastMessages for all entities.</param>
        /// <param name="messageBus">IMessageBus override to register with, if any. Null/not provided defaults to the GlobalMessageBus.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterGlobalAcceptAll(
            FastHandler<IUntargetedMessage> originalUntargetedMessageHandler,
            FastHandler<IUntargetedMessage> untargetedMessageHandler,
            FastHandlerWithContext<ITargetedMessage> originalTargetedMessageHandler,
            FastHandlerWithContext<ITargetedMessage> targetedMessageHandler,
            FastHandlerWithContext<IBroadcastMessage> originalBroadcastMessageHandler,
            FastHandlerWithContext<IBroadcastMessage> broadcastMessageHandler,
            IMessageBus messageBus = null
        )
        {
            messageBus ??= MessageBus;
            Action messageBusDeregistration = messageBus.RegisterGlobalAcceptAll(this);
            TypedHandler<IMessage> typedHandler = GetOrCreateHandlerForType<IMessage>(messageBus);

            Action untargetedDeregistration = typedHandler.AddGlobalUntargetedHandler(
                originalUntargetedMessageHandler,
                untargetedMessageHandler,
                NullDeregistration
            );
            Action targetedDeregistration = typedHandler.AddGlobalTargetedHandler(
                originalTargetedMessageHandler,
                targetedMessageHandler,
                NullDeregistration
            );
            Action broadcastDeregistration = typedHandler.AddGlobalBroadcastHandler(
                originalBroadcastMessageHandler,
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
        /// Registers this MessageHandler to accept TargetedMessages via the MessageBus, properly handling deregistration.
        /// </summary>
        /// <typeparam name="T">Type of Message to be handled.</typeparam>
        /// <param name="target">Target Id of TargetedMessages to listen for.</param>
        /// <param name="messageHandler">Function that actually handles the message.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <param name="messageBus">IMessageBus override to register with, if any. Null/not provided defaults to the GlobalMessageBus.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterTargetedMessageHandler<T>(
            InstanceId target,
            Action<T> originalHandler,
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
                originalHandler,
                messageHandler,
                messageBusDeregistration,
                priority
            );
        }

        /// <summary>
        /// Registers this MessageHandler to accept fast TargetedMessages via the MessageBus, properly handling deregistration.
        /// </summary>
        /// <typeparam name="T">Type of Message to be handled.</typeparam>
        /// <param name="target">Target Id of TargetedMessages to listen for.</param>
        /// <param name="messageHandler">Function that actually handles the message.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <param name="messageBus">IMessageBus override to register with, if any. Null/not provided defaults to the GlobalMessageBus.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterTargetedMessageHandler<T>(
            InstanceId target,
            FastHandler<T> originalHandler,
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
                originalHandler,
                messageHandler,
                messageBusDeregistration,
                priority
            );
        }

        /// <summary>
        /// Registers this MessageHandler to post process TargetedMessages via the MessageBus, properly handling deregistration.
        /// </summary>
        /// <typeparam name="T">Type of Message to be handled.</typeparam>
        /// <param name="target">Target Id of TargetedMessages to listen for.</param>
        /// <param name="messageHandler">Function that actually handles the message.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <param name="messageBus">IMessageBus override to register with, if any. Null/not provided defaults to the GlobalMessageBus.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterTargetedPostProcessor<T>(
            InstanceId target,
            Action<T> originalHandler,
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
                originalHandler,
                messageHandler,
                messageBusDeregistration,
                priority
            );
        }

        /// <summary>
        /// Registers this MessageHandler to post process fast TargetedMessages via the MessageBus, properly handling deregistration.
        /// </summary>
        /// <typeparam name="T">Type of Message to be handled.</typeparam>
        /// <param name="target">Target Id of TargetedMessages to listen for.</param>
        /// <param name="messageHandler">Function that actually handles the message.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <param name="messageBus">IMessageBus override to register with, if any. Null/not provided defaults to the GlobalMessageBus.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterTargetedPostProcessor<T>(
            InstanceId target,
            FastHandler<T> originalHandler,
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
                originalHandler,
                messageHandler,
                messageBusDeregistration,
                priority
            );
        }

        /// <summary>
        /// Registers this MessageHandler to post-process TargetedMessages for all messages of the provided type via the MessageBus, properly handling deregistration.
        /// </summary>
        /// <typeparam name="T">Type of Message to be handled.</typeparam>
        /// <param name="messageHandler">Function that actually handles the message.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <param name="messageBus">IMessageBus override to register with, if any. Null/not provided defaults to the GlobalMessageBus.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterTargetedWithoutTargetingPostProcessor<T>(
            Action<InstanceId, T> originalHandler,
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
                originalHandler,
                messageHandler,
                messageBusDeregistration,
                priority
            );
        }

        /// <summary>
        /// Registers this MessageHandler to post process fast TargetedMessages for all messages of the provided type via the MessageBus, properly handling deregistration.
        /// </summary>
        /// <typeparam name="T">Type of Message to be handled.</typeparam>
        /// <param name="messageHandler">Function that actually handles the message.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <param name="messageBus">IMessageBus override to register with, if any. Null/not provided defaults to the GlobalMessageBus.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterTargetedWithoutTargetingPostProcessor<T>(
            FastHandlerWithContext<T> originalHandler,
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
                originalHandler,
                messageHandler,
                messageBusDeregistration,
                priority
            );
        }

        /// <summary>
        /// Registers this MessageHandler to accept TargetedMessages without Targeting via the MessageBus, properly handling deregistration.
        /// </summary>
        /// <typeparam name="T">Type of Message to be handled.</typeparam>
        /// <param name="messageHandler">Function that actually handles the message.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <param name="messageBus">IMessageBus override to register with, if any. Null/not provided defaults to the GlobalMessageBus.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterTargetedWithoutTargeting<T>(
            Action<InstanceId, T> originalHandler,
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
                originalHandler,
                messageHandler,
                messageBusDeregistration,
                priority
            );
        }

        /// <summary>
        /// Registers this MessageHandler to accept fast TargetedMessages without Targeting via the MessageBus, properly handling deregistration.
        /// </summary>
        /// <typeparam name="T">Type of Message to be handled.</typeparam>
        /// <param name="messageHandler">Function that actually handles the message.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <param name="messageBus">IMessageBus override to register with, if any. Null/not provided defaults to the GlobalMessageBus.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterTargetedWithoutTargeting<T>(
            FastHandlerWithContext<T> originalHandler,
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
                originalHandler,
                messageHandler,
                messageBusDeregistration,
                priority
            );
        }

        /// <summary>
        /// Registers this MessageHandler to accept UntargetedMessages via the MessageBus, properly handling deregistration.
        /// </summary>
        /// <typeparam name="T">Type of Message to be handled.</typeparam>
        /// <param name="messageHandler">Function that actually handles the message.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <param name="messageBus">IMessageBus override to register with, if any. Null/not provided defaults to the GlobalMessageBus.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterUntargetedMessageHandler<T>(
            Action<T> originalHandler,
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
                originalHandler,
                messageHandler,
                messageBusDeregistration,
                priority
            );
        }

        /// <summary>
        /// Registers this MessageHandler to accept fast UntargetedMessages via the MessageBus, properly handling deregistration.
        /// </summary>
        /// <typeparam name="T">Type of Message to be handled.</typeparam>
        /// <param name="messageHandler">Function that actually handles the message.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <param name="messageBus">IMessageBus override to register with, if any. Null/not provided defaults to the GlobalMessageBus.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterUntargetedMessageHandler<T>(
            FastHandler<T> originalHandler,
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
                originalHandler,
                messageHandler,
                messageBusDeregistration,
                priority
            );
        }

        /// <summary>
        /// Registers this MessageHandler to post-process UntargetedMessages via the MessageBus, properly handling deregistration.
        /// </summary>
        /// <typeparam name="T">Type of Message to be handled.</typeparam>
        /// <param name="messageHandler">Function that actually handles the message.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <param name="messageBus">IMessageBus override to register with, if any. Null/not provided defaults to the GlobalMessageBus.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterUntargetedPostProcessor<T>(
            Action<T> originalHandler,
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
                originalHandler,
                messageHandler,
                messageBusDeregistration,
                priority
            );
        }

        /// <summary>
        /// Registers this MessageHandler to post process fast UntargetedMessages via the MessageBus, properly handling deregistration.
        /// </summary>
        /// <typeparam name="T">Type of Message to be handled.</typeparam>
        /// <param name="messageHandler">Function that actually handles the message.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <param name="messageBus">IMessageBus override to register with, if any. Null/not provided defaults to the GlobalMessageBus.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterUntargetedPostProcessor<T>(
            FastHandler<T> originalHandler,
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
                originalHandler,
                messageHandler,
                messageBusDeregistration,
                priority
            );
        }

        /// <summary>
        /// Registers this MessageHandler to accept BroadcastMessages via their MessageBus, properly handling deregistration.
        /// </summary>
        /// <typeparam name="T">Type of Message to be handled.</typeparam>
        /// <param name="source">Source Id of BroadcastMessages to listen for.</param>
        /// <param name="messageHandler">Function that actually handles the message.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <param name="messageBus">IMessageBus override to register with, if any. Null/not provided defaults to the GlobalMessageBus.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterSourcedBroadcastMessageHandler<T>(
            InstanceId source,
            Action<T> originalHandler,
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

            return typedHandler.AddSourcedBroadcastHandler(
                source,
                originalHandler,
                messageHandler,
                messageBusDeregistration,
                priority
            );
        }

        /// <summary>
        /// Registers this MessageHandler to accept fast BroadcastMessages via their MessageBus, properly handling deregistration.
        /// </summary>
        /// <typeparam name="T">Type of Message to be handled.</typeparam>
        /// <param name="source">Source Id of BroadcastMessages to listen for.</param>
        /// <param name="messageHandler">Function that actually handles the message.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <param name="messageBus">IMessageBus override to register with, if any. Null/not provided defaults to the GlobalMessageBus.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterSourcedBroadcastMessageHandler<T>(
            InstanceId source,
            FastHandler<T> originalHandler,
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
                originalHandler,
                messageHandler,
                messageBusDeregistration,
                priority
            );
        }

        /// <summary>
        /// Registers this MessageHandler to accept BroadcastMessage regardless of source via their MessageBus, properly handling deregistration.
        /// </summary>
        /// <typeparam name="T">Type of Message to be handled.</typeparam>
        /// <param name="messageHandler">Function that actually handles the message.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <param name="messageBus">IMessageBus override to register with, if any. Null/not provided defaults to the GlobalMessageBus.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterSourcedBroadcastWithoutSource<T>(
            Action<InstanceId, T> originalHandler,
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
                originalHandler,
                messageHandler,
                messageBusDeregistration,
                priority
            );
        }

        /// <summary>
        /// Registers this MessageHandler to accept fast BroadcastMessage regardless of source via their MessageBus, properly handling deregistration.
        /// </summary>
        /// <typeparam name="T">Type of Message to be handled.</typeparam>
        /// <param name="messageHandler">Function that actually handles the message.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <param name="messageBus">IMessageBus override to register with, if any. Null/not provided defaults to the GlobalMessageBus.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterSourcedBroadcastWithoutSource<T>(
            FastHandlerWithContext<T> originalHandler,
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
                originalHandler,
                messageHandler,
                messageBusDeregistration,
                priority
            );
        }

        /// <summary>
        /// Registers this MessageHandler to post-processes BroadcastMessage messages.
        /// </summary>
        /// <typeparam name="T">Type of Message to be handled.</typeparam>
        /// <param name="source">Source object to listen for BroadcastMessages on.</param>
        /// <param name="messageHandler">Function that actually handles the message.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <param name="messageBus">IMessageBus override to register with, if any. Null/not provided defaults to the GlobalMessageBus.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterSourcedBroadcastPostProcessor<T>(
            InstanceId source,
            Action<T> originalHandler,
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
                originalHandler,
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
            FastHandler<T> originalHandler,
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
                originalHandler,
                messageHandler,
                messageBusDeregistration,
                priority
            );
        }

        /// <summary>
        /// Registers this MessageHandler to post-processes BroadcastMessage messages for all messages of the provided type.
        /// </summary>
        /// <typeparam name="T">Type of Message to be handled.</typeparam>
        /// <param name="messageHandler">Function that actually handles the message.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <param name="messageBus">IMessageBus override to register with, if any. Null/not provided defaults to the GlobalMessageBus.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterSourcedBroadcastWithoutSourcePostProcessor<T>(
            Action<InstanceId, T> originalHandler,
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
                originalHandler,
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
            FastHandlerWithContext<T> originalHandler,
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
                originalHandler,
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
        /// <param name="priority">Priority to register the interceptor at (interceptors are run from low -> high priority)</param>
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
        /// <param name="priority">Priority to register the interceptor at (interceptors are run from low -> high priority)</param>
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
        /// <param name="priority">Priority to register the interceptor at (interceptors are run from low -> high priority)</param>
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

        public override bool Equals(object obj)
        {
            return Equals(obj as MessageHandler);
        }

        public bool Equals(MessageHandler other)
        {
            if (other == null)
            {
                return false;
            }

            if (ReferenceEquals(other, this))
            {
                return true;
            }

            return owner.Equals(other.owner);
        }

        public override int GetHashCode()
        {
            return owner.GetHashCode();
        }

        public int CompareTo(MessageHandler other)
        {
            if (other == null)
            {
                return -1;
            }

            return owner.CompareTo(other.owner);
        }

        public int CompareTo(object obj)
        {
            return CompareTo(obj as MessageHandler);
        }

        public override string ToString()
        {
            return new { OwnerId = owner }.ToString();
        }

        /// <summary>
        /// Retrieves an existing Handler for the specific type if it exists, or creates a new Handler if none exist.
        /// </summary>
        /// <typeparam name="T">Type of Message to retrieve a Handler for.</typeparam>
        /// <returns>Non-Null Handler for the specific type.</returns>
        private TypedHandler<T> GetOrCreateHandlerForType<T>(IMessageBus messageBus)
            where T : IMessage
        {
            int messageBusIndex = messageBus.RegisteredGlobalSequentialIndex;
            while (_handlersByTypeByMessageBus.Count <= messageBusIndex)
            {
                _handlersByTypeByMessageBus.Add(new MessageCache<object>());
            }

            MessageCache<object> handlersByType = _handlersByTypeByMessageBus[messageBusIndex];
            if (handlersByType.TryGetValue<T>(out object untypedHandler))
            {
                return (TypedHandler<T>)untypedHandler;
            }

            TypedHandler<T> typedHandler = new();
            handlersByType.Set<T>(typedHandler);
            return typedHandler;
        }

        /// <summary>
        /// Gets an existing Handler for the specific type if it exists.
        /// </summary>
        /// <param name="messageBus">The specific MessageBus to use.</param>
        /// <param name="existingTypedHandler">Existing typed message handler, if one exists.</param>
        /// <returns>Existing handler for the specific type, or null if none exists.</returns>
        private bool GetHandlerForType<T>(
            IMessageBus messageBus,
            out TypedHandler<T> existingTypedHandler
        )
            where T : IMessage
        {
            int messageBusIndex = messageBus.RegisteredGlobalSequentialIndex;
            if (_handlersByTypeByMessageBus.Count <= messageBusIndex)
            {
                existingTypedHandler = default;
                return false;
            }

            if (
                _handlersByTypeByMessageBus[messageBusIndex]
                    .TryGetValue<T>(out object untypedHandler)
            )
            {
                existingTypedHandler = (TypedHandler<T>)untypedHandler;
                return true;
            }

            existingTypedHandler = default;
            return false;
        }

        private sealed class HandlerActionCache<T>
        {
            public readonly Dictionary<T, int> handlers = new();
            public readonly Dictionary<T, T> originalToAugmented = new();
            public readonly List<T> cache = new();
            public long version;
            public long lastSeenVersion = -1;
        }

        /// <summary>
        /// One-size-fits-all wrapper around all possible Messaging sinks for a particular MessageHandler & MessageType.
        /// </summary>
        /// <typeparam name="T">Message type that this Handler exists to serve.</typeparam>
        private sealed class TypedHandler<T>
            where T : IMessage
        {
            private Dictionary<
                InstanceId,
                Dictionary<int, HandlerActionCache<Action<T>>>
            > _targetedHandlers;
            private Dictionary<int, HandlerActionCache<Action<T>>> _untargetedHandlers;
            private Dictionary<
                InstanceId,
                Dictionary<int, HandlerActionCache<Action<T>>>
            > _broadcastHandlers;
            private Dictionary<
                InstanceId,
                Dictionary<int, HandlerActionCache<Action<T>>>
            > _targetedPostProcessingHandlers;
            private Dictionary<
                int,
                HandlerActionCache<Action<T>>
            > _untargetedPostProcessingHandlers;
            private Dictionary<
                InstanceId,
                Dictionary<int, HandlerActionCache<Action<T>>>
            > _broadcastPostProcessingHandlers;
            private Dictionary<
                InstanceId,
                Dictionary<int, HandlerActionCache<FastHandler<T>>>
            > _targetedFastHandlers;
            private Dictionary<int, HandlerActionCache<FastHandler<T>>> _untargetedFastHandlers;
            private Dictionary<
                InstanceId,
                Dictionary<int, HandlerActionCache<FastHandler<T>>>
            > _broadcastFastHandlers;
            private Dictionary<
                InstanceId,
                Dictionary<int, HandlerActionCache<FastHandler<T>>>
            > _targetedPostProcessingFastHandlers;
            private Dictionary<
                int,
                HandlerActionCache<FastHandler<T>>
            > _untargetedPostProcessingFastHandlers;
            private Dictionary<
                InstanceId,
                Dictionary<int, HandlerActionCache<FastHandler<T>>>
            > _broadcastPostProcessingFastHandlers;

            private HandlerActionCache<Action<IUntargetedMessage>> _globalUntargetedHandlers;

            private HandlerActionCache<
                Action<InstanceId, ITargetedMessage>
            > _globalTargetedHandlers;

            private HandlerActionCache<
                Action<InstanceId, IBroadcastMessage>
            > _globalBroadcastHandlers;

            private HandlerActionCache<
                FastHandler<IUntargetedMessage>
            > _globalUntargetedFastHandlers;

            private HandlerActionCache<
                FastHandlerWithContext<ITargetedMessage>
            > _globalTargetedFastHandlers;

            private HandlerActionCache<
                FastHandlerWithContext<IBroadcastMessage>
            > _globalBroadcastFastHandlers;
            private Dictionary<
                int,
                HandlerActionCache<Action<InstanceId, T>>
            > _targetedWithoutTargetingHandlers;
            private Dictionary<
                int,
                HandlerActionCache<FastHandlerWithContext<T>>
            > _fastTargetedWithoutTargetingHandlers;
            private Dictionary<
                int,
                HandlerActionCache<Action<InstanceId, T>>
            > _broadcastWithoutSourceHandlers;
            private Dictionary<
                int,
                HandlerActionCache<FastHandlerWithContext<T>>
            > _fastBroadcastWithoutSourceHandlers;
            private Dictionary<
                int,
                HandlerActionCache<Action<InstanceId, T>>
            > _targetedWithoutTargetingPostProcessingHandlers;
            private Dictionary<
                int,
                HandlerActionCache<FastHandlerWithContext<T>>
            > _fastTargetedWithoutTargetingPostProcessingHandlers;
            private Dictionary<
                int,
                HandlerActionCache<Action<InstanceId, T>>
            > _broadcastWithoutSourcePostProcessingHandlers;
            private Dictionary<
                int,
                HandlerActionCache<FastHandlerWithContext<T>>
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
            /// Emits the BroadcastMessage without a source to all subscribed listeners.
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
                RunFastHandlers(_globalUntargetedFastHandlers, ref message);
                if (_globalUntargetedHandlers?.handlers is not { Count: > 0 })
                {
                    return;
                }

                List<Action<IUntargetedMessage>> handlers = GetOrAddNewHandlerStack(
                    _globalUntargetedHandlers
                );
                foreach (Action<IUntargetedMessage> handler in handlers)
                {
                    handler(message);
                }
            }

            /// <summary>
            /// Emits the TargetedMessage to all global listeners.
            /// </summary>
            /// <param name="target">Target that this message is intended for.</param>
            /// <param name="message">Message to emit.</param>
            public void HandleGlobalTargeted(ref InstanceId target, ref ITargetedMessage message)
            {
                RunFastHandlers(ref target, _globalTargetedFastHandlers, ref message);

                if (_globalTargetedHandlers?.handlers is not { Count: > 0 })
                {
                    return;
                }

                List<Action<InstanceId, ITargetedMessage>> handlers = GetOrAddNewHandlerStack(
                    _globalTargetedHandlers
                );
                foreach (Action<InstanceId, ITargetedMessage> handler in handlers)
                {
                    handler(target, message);
                }
            }

            /// <summary>
            /// Emits the BroadcastMessage to all global listeners.
            /// </summary>
            /// <param name="source">Source that this message is from.</param>
            /// <param name="message">Message to emit.</param>
            public void HandleGlobalBroadcast(ref InstanceId source, ref IBroadcastMessage message)
            {
                RunFastHandlers(ref source, _globalBroadcastFastHandlers, ref message);

                if (_globalBroadcastHandlers?.handlers is not { Count: > 0 })
                {
                    return;
                }

                List<Action<InstanceId, IBroadcastMessage>> handlers = GetOrAddNewHandlerStack(
                    _globalBroadcastHandlers
                );
                switch (handlers.Count)
                {
                    case 1:
                    {
                        handlers[0](source, message);
                        return;
                    }
                    case 2:
                    {
                        handlers[0](source, message);
                        handlers[1](source, message);
                        return;
                    }
                    case 3:
                    {
                        handlers[0](source, message);
                        handlers[1](source, message);
                        handlers[2](source, message);
                        return;
                    }
                    case 4:
                    {
                        handlers[0](source, message);
                        handlers[1](source, message);
                        handlers[2](source, message);
                        handlers[3](source, message);
                        return;
                    }
                    case 5:
                    {
                        handlers[0](source, message);
                        handlers[1](source, message);
                        handlers[2](source, message);
                        handlers[3](source, message);
                        handlers[4](source, message);
                        return;
                    }
                }

                foreach (Action<InstanceId, IBroadcastMessage> handler in handlers)
                {
                    handler(source, message);
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
            /// Adds a TargetedHandler to listen to Messages of the given type, returning a deregistration action.
            /// </summary>
            /// <param name="target">Target the handler is for.</param>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <param name="priority">Priority at which to add the handler.</param>
            /// <returns>De-registration action to unregister the handler.</returns>
            public Action AddTargetedHandler(
                InstanceId target,
                Action<T> originalHandler,
                Action<T> handler,
                Action deregistration,
                int priority
            )
            {
                return AddHandler(
                    target,
                    ref _targetedHandlers,
                    originalHandler,
                    handler,
                    deregistration,
                    priority
                );
            }

            /// <summary>
            /// Adds a fast TargetedHandler to listen to Messages of the given type, returning a deregistration action.
            /// </summary>
            /// <param name="target">Target the handler is for.</param>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <param name="priority">Priority at which to add the handler.</param>
            /// <returns>De-registration action to unregister the handler.</returns>
            public Action AddTargetedHandler(
                InstanceId target,
                FastHandler<T> originalHandler,
                FastHandler<T> handler,
                Action deregistration,
                int priority
            )
            {
                return AddHandler(
                    target,
                    ref _targetedFastHandlers,
                    originalHandler,
                    handler,
                    deregistration,
                    priority
                );
            }

            /// <summary>
            /// Adds a TargetedWithoutTargetingHandler to listen to Messages of the given type, returning a deregistration action.
            /// </summary>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <param name="priority">Priority at which to add the handler.</param>
            /// <returns>De-registration action to unregister the handler.</returns>
            public Action AddTargetedWithoutTargetingHandler(
                Action<InstanceId, T> originalHandler,
                Action<InstanceId, T> handler,
                Action deregistration,
                int priority
            )
            {
                return AddHandler(
                    ref _targetedWithoutTargetingHandlers,
                    originalHandler,
                    handler,
                    deregistration,
                    priority
                );
            }

            /// <summary>
            /// Adds a fast TargetedWithoutTargetingHandler to listen to Messages of the given type, returning a deregistration action.
            /// </summary>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <param name="priority">Priority at which to add the handler.</param>
            /// <returns>De-registration action to unregister the handler.</returns>
            public Action AddTargetedWithoutTargetingHandler(
                FastHandlerWithContext<T> originalHandler,
                FastHandlerWithContext<T> handler,
                Action deregistration,
                int priority
            )
            {
                return AddHandler(
                    ref _fastTargetedWithoutTargetingHandlers,
                    originalHandler,
                    handler,
                    deregistration,
                    priority
                );
            }

            /// <summary>
            /// Adds a UntargetedHandler to listen to Messages of the given type, returning a deregistration action.
            /// </summary>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <param name="priority">Priority at which to add the handler.</param>
            /// <returns>De-registration action to unregister the handler.</returns>
            public Action AddUntargetedHandler(
                Action<T> originalHandler,
                Action<T> handler,
                Action deregistration,
                int priority
            )
            {
                return AddHandler(
                    ref _untargetedHandlers,
                    originalHandler,
                    handler,
                    deregistration,
                    priority
                );
            }

            /// <summary>
            /// Adds a fast UntargetedHandler to listen to Messages of the given type, returning a deregistration action.
            /// </summary>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <param name="priority">Priority at which to add the handler.</param>
            /// <returns>De-registration action to unregister the handler.</returns>
            public Action AddUntargetedHandler(
                FastHandler<T> originalHandler,
                FastHandler<T> handler,
                Action deregistration,
                int priority
            )
            {
                return AddHandler(
                    ref _untargetedFastHandlers,
                    originalHandler,
                    handler,
                    deregistration,
                    priority
                );
            }

            /// <summary>
            /// Adds a SourcedBroadcastHandler to listen to Messages of the given type from an entity, returning a deregistration action.
            /// </summary>
            /// <param name="source">The Source of the handler is for.</param>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <param name="priority">Priority at which to add the handler.</param>
            /// <returns>De-registration action to unregister the handler.</returns>
            public Action AddSourcedBroadcastHandler(
                InstanceId source,
                Action<T> originalHandler,
                Action<T> handler,
                Action deregistration,
                int priority
            )
            {
                return AddHandler(
                    source,
                    ref _broadcastHandlers,
                    originalHandler,
                    handler,
                    deregistration,
                    priority
                );
            }

            /// <summary>
            /// Adds a fast SourcedBroadcastHandler to listen to Messages of the given type from an entity, returning a deregistration action.
            /// </summary>
            /// <param name="source">The Source of the handler is for.</param>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <param name="priority">Priority at which to add the handler.</param>
            /// <returns>De-registration action to unregister the handler.</returns>
            public Action AddSourcedBroadcastHandler(
                InstanceId source,
                FastHandler<T> originalHandler,
                FastHandler<T> handler,
                Action deregistration,
                int priority
            )
            {
                return AddHandler(
                    source,
                    ref _broadcastFastHandlers,
                    originalHandler,
                    handler,
                    deregistration,
                    priority
                );
            }

            /// <summary>
            /// Adds a SourcedBroadcastWithoutSourceHandler to listen to Messages of the given type from an entity, returning a deregistration action.
            /// </summary>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <param name="priority">Priority at which to add the handler.</param>
            /// <returns>De-registration action to unregister the handler.</returns>
            public Action AddSourcedBroadcastWithoutSourceHandler(
                Action<InstanceId, T> originalHandler,
                Action<InstanceId, T> handler,
                Action deregistration,
                int priority
            )
            {
                return AddHandler(
                    ref _broadcastWithoutSourceHandlers,
                    originalHandler,
                    handler,
                    deregistration,
                    priority
                );
            }

            /// <summary>
            /// Adds a fast SourcedBroadcastWithoutSourceHandler to listen to Messages of the given type from an entity, returning a deregistration action.
            /// </summary>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <param name="priority">Priority at which to add the handler.</param>
            /// <returns>De-registration action to unregister the handler.</returns>
            public Action AddSourcedBroadcastWithoutSourceHandler(
                FastHandlerWithContext<T> originalHandler,
                FastHandlerWithContext<T> handler,
                Action deregistration,
                int priority
            )
            {
                return AddHandler(
                    ref _fastBroadcastWithoutSourceHandlers,
                    originalHandler,
                    handler,
                    deregistration,
                    priority
                );
            }

            /// <summary>
            /// Adds a Global UntargetedHandler to listen to all Untargeted Messages of all types, returning the deregistration action.
            /// </summary>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <returns>De-registration action to unregister the handler.</returns>
            public Action AddGlobalUntargetedHandler(
                Action<IUntargetedMessage> originalHandler,
                Action<IUntargetedMessage> handler,
                Action deregistration
            )
            {
                return AddHandler(
                    ref _globalUntargetedHandlers,
                    originalHandler,
                    handler,
                    deregistration
                );
            }

            /// <summary>
            /// Adds a Global fast UntargetedHandler to listen to all Untargeted Messages of all types, returning the deregistration action.
            /// </summary>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <returns>De-registration action to unregister the handler.</returns>
            public Action AddGlobalUntargetedHandler(
                FastHandler<IUntargetedMessage> originalHandler,
                FastHandler<IUntargetedMessage> handler,
                Action deregistration
            )
            {
                return AddHandler(
                    ref _globalUntargetedFastHandlers,
                    originalHandler,
                    handler,
                    deregistration
                );
            }

            /// <summary>
            /// Adds a Global TargetedHandler to listen to all Targeted Messages of all types for all entities, returning the deregistration action.
            /// </summary>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <returns>De-registration action to unregister the handler.</returns>
            public Action AddGlobalTargetedHandler(
                Action<InstanceId, ITargetedMessage> originalHandler,
                Action<InstanceId, ITargetedMessage> handler,
                Action deregistration
            )
            {
                return AddHandler(
                    ref _globalTargetedHandlers,
                    originalHandler,
                    handler,
                    deregistration
                );
            }

            /// <summary>
            /// Adds a Global fast TargetedHandler to listen to all Targeted Messages of all types for all entities (along with the target instance id), returning the deregistration action.
            /// </summary>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <returns>De-registration action to unregister the handler.</returns>
            public Action AddGlobalTargetedHandler(
                FastHandlerWithContext<ITargetedMessage> originalHandler,
                FastHandlerWithContext<ITargetedMessage> handler,
                Action deregistration
            )
            {
                return AddHandler(
                    ref _globalTargetedFastHandlers,
                    originalHandler,
                    handler,
                    deregistration
                );
            }

            /// <summary>
            /// Adds a Global BroadcastHandler to listen to all Targeted Messages of all types for all entities, returning the deregistration action.
            /// </summary>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <returns>De-registration action to unregister the handler.</returns>
            public Action AddGlobalBroadcastHandler(
                Action<InstanceId, IBroadcastMessage> originalHandler,
                Action<InstanceId, IBroadcastMessage> handler,
                Action deregistration
            )
            {
                return AddHandler(
                    ref _globalBroadcastHandlers,
                    originalHandler,
                    handler,
                    deregistration
                );
            }

            /// <summary>
            /// Adds a Global fast BroadcastHandler to listen to all Targeted Messages of all types for all entities (along with the source instance id), returning the deregistration action.
            /// </summary>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <returns>De-registration action to unregister the handler.</returns>
            public Action AddGlobalBroadcastHandler(
                FastHandlerWithContext<IBroadcastMessage> originalHandler,
                FastHandlerWithContext<IBroadcastMessage> handler,
                Action deregistration
            )
            {
                return AddHandler(
                    ref _globalBroadcastFastHandlers,
                    originalHandler,
                    handler,
                    deregistration
                );
            }

            /// <summary>
            /// Adds an Untargeted post-processor to be called after all other handlers have been called.
            /// </summary>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <param name="priority">Priority at which to add the handler.</param>
            /// <returns>De-registration action to unregister the handler.</returns>
            public Action AddUntargetedPostProcessor(
                Action<T> originalHandler,
                Action<T> handler,
                Action deregistration,
                int priority
            )
            {
                return AddHandler(
                    ref _untargetedPostProcessingHandlers,
                    originalHandler,
                    handler,
                    deregistration,
                    priority
                );
            }

            /// <summary>
            /// Adds a fast Untargeted post-processor to be called after all other handlers have been called.
            /// </summary>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <param name="priority">Priority at which to add the handler.</param>
            /// <returns>De-registration action to unregister the handler.</returns>
            public Action AddUntargetedPostProcessor(
                FastHandler<T> originalHandler,
                FastHandler<T> handler,
                Action deregistration,
                int priority
            )
            {
                return AddHandler(
                    ref _untargetedPostProcessingFastHandlers,
                    originalHandler,
                    handler,
                    deregistration,
                    priority
                );
            }

            /// <summary>
            /// Adds a Targeted post-processor to be called after all other handlers have been called.
            /// </summary>
            /// <param name="target">Target the handler is for.</param>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <param name="priority">Priority at which to add the handler.</param>
            /// <returns>De-registration action to unregister the handler.</returns>
            public Action AddTargetedPostProcessor(
                InstanceId target,
                Action<T> originalHandler,
                Action<T> handler,
                Action deregistration,
                int priority
            )
            {
                return AddHandler(
                    target,
                    ref _targetedPostProcessingHandlers,
                    originalHandler,
                    handler,
                    deregistration,
                    priority
                );
            }

            /// <summary>
            /// Adds a Targeted post-processor to be called after all other handlers have been called.
            /// </summary>
            /// <param name="target">Target the handler is for.</param>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <param name="priority">Priority at which to add the handler.</param>
            /// <returns>De-registration action to unregister the handler.</returns>
            public Action AddTargetedPostProcessor(
                InstanceId target,
                FastHandler<T> originalHandler,
                FastHandler<T> handler,
                Action deregistration,
                int priority
            )
            {
                return AddHandler(
                    target,
                    ref _targetedPostProcessingFastHandlers,
                    originalHandler,
                    handler,
                    deregistration,
                    priority
                );
            }

            /// <summary>
            /// Adds a Targeted post-processor to be called after all other handlers have been called after every message of the given type.
            /// </summary>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <param name="priority">Priority at which to add the handler.</param>
            /// <returns>De-registration action to unregister the handler.</returns>
            public Action AddTargetedWithoutTargetingPostProcessor(
                Action<InstanceId, T> originalHandler,
                Action<InstanceId, T> handler,
                Action deregistration,
                int priority
            )
            {
                return AddHandler(
                    ref _targetedWithoutTargetingPostProcessingHandlers,
                    originalHandler,
                    handler,
                    deregistration,
                    priority
                );
            }

            /// <summary>
            /// Adds a Targeted post-processor to be called after all other handlers have been called after every message of the given type.
            /// </summary>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <param name="priority">Priority at which to add the handler.</param>
            /// <returns>De-registration action to unregister the handler.</returns>
            public Action AddTargetedWithoutTargetingPostProcessor(
                FastHandlerWithContext<T> originalHandler,
                FastHandlerWithContext<T> handler,
                Action deregistration,
                int priority
            )
            {
                return AddHandler(
                    ref _fastTargetedWithoutTargetingPostProcessingHandlers,
                    originalHandler,
                    handler,
                    deregistration,
                    priority
                );
            }

            /// <summary>
            /// Adds a Broadcast post-processor to be called after all other handlers have been called.
            /// </summary>
            /// <param name="source">The Source the handler is for.</param>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <param name="priority">Priority at which to add the handler.</param>
            /// <returns>De-registration action to unregister the handler.</returns>
            public Action AddBroadcastPostProcessor(
                InstanceId source,
                Action<T> originalHandler,
                Action<T> handler,
                Action deregistration,
                int priority
            )
            {
                return AddHandler(
                    source,
                    ref _broadcastPostProcessingHandlers,
                    originalHandler,
                    handler,
                    deregistration,
                    priority
                );
            }

            /// <summary>
            /// Adds a fast Broadcast post-processor to be called after all other handlers have been called.
            /// </summary>
            /// <param name="source">The Source the handler is for.</param>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <param name="priority">Priority at which to add the handler.</param>
            /// <returns>De-registration action to unregister the handler.</returns>
            public Action AddBroadcastPostProcessor(
                InstanceId source,
                FastHandler<T> originalHandler,
                FastHandler<T> handler,
                Action deregistration,
                int priority
            )
            {
                return AddHandler(
                    source,
                    ref _broadcastPostProcessingFastHandlers,
                    originalHandler,
                    handler,
                    deregistration,
                    priority
                );
            }

            /// <summary>
            /// Adds a Broadcast post-processor to be called after all other handlers have been called for every message of the given type.
            /// </summary>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <param name="priority">Priority at which to add the handler.</param>
            /// <returns>De-registration action to unregister the handler.</returns>
            public Action AddBroadcastWithoutSourcePostProcessor(
                Action<InstanceId, T> originalHandler,
                Action<InstanceId, T> handler,
                Action deregistration,
                int priority
            )
            {
                return AddHandler(
                    ref _broadcastWithoutSourcePostProcessingHandlers,
                    originalHandler,
                    handler,
                    deregistration,
                    priority
                );
            }

            /// <summary>
            /// Adds a fast Broadcast post-processor to be called after all other handlers have been called.
            /// </summary>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <param name="priority">Priority at which to add the handler.</param>
            /// <returns>De-registration action to unregister the handler.</returns>
            public Action AddBroadcastWithoutSourcePostProcessor(
                FastHandlerWithContext<T> originalHandler,
                FastHandlerWithContext<T> handler,
                Action deregistration,
                int priority
            )
            {
                return AddHandler(
                    ref _fastBroadcastWithoutSourcePostProcessingHandlers,
                    originalHandler,
                    handler,
                    deregistration,
                    priority
                );
            }

            private static void RunFastHandlersWithContext<TMessage>(
                ref InstanceId context,
                Dictionary<
                    int,
                    HandlerActionCache<FastHandlerWithContext<T>>
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

                RunFastHandlers(ref context, fastHandlersByContext, ref message, priority);
            }

            private static void RunFastHandlersWithContext<TMessage>(
                ref InstanceId context,
                Dictionary<
                    InstanceId,
                    Dictionary<int, HandlerActionCache<FastHandler<T>>>
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
                        out Dictionary<int, HandlerActionCache<FastHandler<T>>> cache
                    )
                )
                {
                    return;
                }

                RunFastHandlers(cache, ref message, priority);
            }

            private static void RunFastHandlers<TMessage>(
                Dictionary<int, HandlerActionCache<FastHandler<T>>> fastHandlers,
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
                        out HandlerActionCache<FastHandler<T>> cache
                    )
                )
                {
                    return;
                }

                ref T typedMessage = ref Unsafe.As<TMessage, T>(ref message);

                List<FastHandler<T>> handlers = GetOrAddNewHandlerStack(cache);

                switch (handlers.Count)
                {
                    case 1:
                    {
                        handlers[0](ref typedMessage);
                        return;
                    }
                    case 2:
                    {
                        handlers[0](ref typedMessage);
                        handlers[1](ref typedMessage);
                        return;
                    }
                    case 3:
                    {
                        handlers[0](ref typedMessage);
                        handlers[1](ref typedMessage);
                        handlers[2](ref typedMessage);
                        return;
                    }
                    case 4:
                    {
                        handlers[0](ref typedMessage);
                        handlers[1](ref typedMessage);
                        handlers[2](ref typedMessage);
                        handlers[3](ref typedMessage);
                        return;
                    }
                    case 5:
                    {
                        handlers[0](ref typedMessage);
                        handlers[1](ref typedMessage);
                        handlers[2](ref typedMessage);
                        handlers[3](ref typedMessage);
                        handlers[4](ref typedMessage);
                        return;
                    }
                }

                foreach (FastHandler<T> fastHandler in handlers)
                {
                    fastHandler(ref typedMessage);
                }
            }

            private static void RunFastHandlers<TMessage, TU>(
                HandlerActionCache<FastHandler<TU>> cache,
                ref TMessage message
            )
                where TMessage : IMessage
                where TU : IMessage
            {
                if (cache?.handlers is not { Count: > 0 })
                {
                    return;
                }

                ref TU typedMessage = ref Unsafe.As<TMessage, TU>(ref message);

                List<FastHandler<TU>> handlers = GetOrAddNewHandlerStack(cache);
                switch (handlers.Count)
                {
                    case 1:
                    {
                        handlers[0](ref typedMessage);
                        return;
                    }
                    case 2:
                    {
                        handlers[0](ref typedMessage);
                        handlers[1](ref typedMessage);
                        return;
                    }
                    case 3:
                    {
                        handlers[0](ref typedMessage);
                        handlers[1](ref typedMessage);
                        handlers[2](ref typedMessage);
                        return;
                    }
                    case 4:
                    {
                        handlers[0](ref typedMessage);
                        handlers[1](ref typedMessage);
                        handlers[2](ref typedMessage);
                        handlers[3](ref typedMessage);
                        return;
                    }
                    case 5:
                    {
                        handlers[0](ref typedMessage);
                        handlers[1](ref typedMessage);
                        handlers[2](ref typedMessage);
                        handlers[3](ref typedMessage);
                        handlers[4](ref typedMessage);
                        return;
                    }
                }

                foreach (FastHandler<TU> fastHandler in handlers)
                {
                    fastHandler(ref typedMessage);
                }
            }

            private static void RunFastHandlers<TMessage, TU>(
                ref InstanceId context,
                HandlerActionCache<FastHandlerWithContext<TU>> cache,
                ref TMessage message
            )
                where TMessage : IMessage
                where TU : IMessage
            {
                if (cache?.handlers is not { Count: > 0 })
                {
                    return;
                }

                ref TU typedMessage = ref Unsafe.As<TMessage, TU>(ref message);

                List<FastHandlerWithContext<TU>> handlers = GetOrAddNewHandlerStack(cache);
                switch (handlers.Count)
                {
                    case 1:
                    {
                        handlers[0](ref context, ref typedMessage);
                        return;
                    }
                    case 2:
                    {
                        handlers[0](ref context, ref typedMessage);
                        handlers[1](ref context, ref typedMessage);
                        return;
                    }
                    case 3:
                    {
                        handlers[0](ref context, ref typedMessage);
                        handlers[1](ref context, ref typedMessage);
                        handlers[2](ref context, ref typedMessage);
                        return;
                    }
                    case 4:
                    {
                        handlers[0](ref context, ref typedMessage);
                        handlers[1](ref context, ref typedMessage);
                        handlers[2](ref context, ref typedMessage);
                        handlers[3](ref context, ref typedMessage);
                        return;
                    }
                    case 5:
                    {
                        handlers[0](ref context, ref typedMessage);
                        handlers[1](ref context, ref typedMessage);
                        handlers[2](ref context, ref typedMessage);
                        handlers[3](ref context, ref typedMessage);
                        handlers[4](ref context, ref typedMessage);
                        return;
                    }
                }

                foreach (FastHandlerWithContext<TU> fastHandler in handlers)
                {
                    fastHandler(ref context, ref typedMessage);
                }
            }

            private static void RunFastHandlers<TMessage, TU>(
                ref InstanceId context,
                Dictionary<int, HandlerActionCache<FastHandlerWithContext<TU>>> fastHandlers,
                ref TMessage message,
                int priority
            )
                where TMessage : IMessage
                where TU : IMessage
            {
                if (fastHandlers is not { Count: > 0 })
                {
                    return;
                }

                if (
                    !fastHandlers.TryGetValue(
                        priority,
                        out HandlerActionCache<FastHandlerWithContext<TU>> cache
                    )
                )
                {
                    return;
                }

                ref TU typedMessage = ref Unsafe.As<TMessage, TU>(ref message);

                List<FastHandlerWithContext<TU>> handlers = GetOrAddNewHandlerStack(cache);
                switch (handlers.Count)
                {
                    case 1:
                    {
                        handlers[0](ref context, ref typedMessage);
                        return;
                    }
                    case 2:
                    {
                        handlers[0](ref context, ref typedMessage);
                        handlers[1](ref context, ref typedMessage);
                        return;
                    }
                    case 3:
                    {
                        handlers[0](ref context, ref typedMessage);
                        handlers[1](ref context, ref typedMessage);
                        handlers[2](ref context, ref typedMessage);
                        return;
                    }
                    case 4:
                    {
                        handlers[0](ref context, ref typedMessage);
                        handlers[1](ref context, ref typedMessage);
                        handlers[2](ref context, ref typedMessage);
                        handlers[3](ref context, ref typedMessage);
                        return;
                    }
                    case 5:
                    {
                        handlers[0](ref context, ref typedMessage);
                        handlers[1](ref context, ref typedMessage);
                        handlers[2](ref context, ref typedMessage);
                        handlers[3](ref context, ref typedMessage);
                        handlers[4](ref context, ref typedMessage);
                        return;
                    }
                }

                foreach (FastHandlerWithContext<TU> fastHandler in handlers)
                {
                    fastHandler(ref context, ref typedMessage);
                }
            }

            private static void RunHandlersWithContext<TMessage>(
                ref InstanceId context,
                Dictionary<
                    InstanceId,
                    Dictionary<int, HandlerActionCache<Action<T>>>
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
                        out Dictionary<int, HandlerActionCache<Action<T>>> cache
                    )
                )
                {
                    return;
                }

                RunHandlers(cache, ref message, priority);
            }

            private static void RunHandlers<TMessage>(
                Dictionary<int, HandlerActionCache<Action<T>>> sortedHandlers,
                ref TMessage message,
                int priority
            )
                where TMessage : IMessage
            {
                if (sortedHandlers is not { Count: > 0 })
                {
                    return;
                }

                if (!sortedHandlers.TryGetValue(priority, out HandlerActionCache<Action<T>> cache))
                {
                    return;
                }

                List<Action<T>> handlers = GetOrAddNewHandlerStack(cache);
                ref T typedMessage = ref Unsafe.As<TMessage, T>(ref message);

                switch (handlers.Count)
                {
                    case 1:
                    {
                        handlers[0](typedMessage);
                        return;
                    }
                    case 2:
                    {
                        handlers[0](typedMessage);
                        handlers[1](typedMessage);
                        return;
                    }
                    case 3:
                    {
                        handlers[0](typedMessage);
                        handlers[1](typedMessage);
                        handlers[2](typedMessage);
                        return;
                    }
                    case 4:
                    {
                        handlers[0](typedMessage);
                        handlers[1](typedMessage);
                        handlers[2](typedMessage);
                        handlers[3](typedMessage);
                        return;
                    }
                    case 5:
                    {
                        handlers[0](typedMessage);
                        handlers[1](typedMessage);
                        handlers[2](typedMessage);
                        handlers[3](typedMessage);
                        handlers[4](typedMessage);
                        return;
                    }
                }

                foreach (Action<T> handler in handlers)
                {
                    handler(typedMessage);
                }
            }

            private static void RunHandlers<TMessage>(
                ref InstanceId context,
                Dictionary<int, HandlerActionCache<Action<InstanceId, T>>> handlers,
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
                        out HandlerActionCache<Action<InstanceId, T>> cache
                    )
                )
                {
                    return;
                }

                List<Action<InstanceId, T>> typedHandlers = GetOrAddNewHandlerStack(cache);
                ref T typedMessage = ref Unsafe.As<TMessage, T>(ref message);

                switch (typedHandlers.Count)
                {
                    case 1:
                    {
                        typedHandlers[0](context, typedMessage);
                        return;
                    }
                    case 2:
                    {
                        typedHandlers[0](context, typedMessage);
                        typedHandlers[1](context, typedMessage);
                        return;
                    }
                    case 3:
                    {
                        typedHandlers[0](context, typedMessage);
                        typedHandlers[1](context, typedMessage);
                        typedHandlers[2](context, typedMessage);
                        return;
                    }
                    case 4:
                    {
                        typedHandlers[0](context, typedMessage);
                        typedHandlers[1](context, typedMessage);
                        typedHandlers[2](context, typedMessage);
                        typedHandlers[3](context, typedMessage);
                        return;
                    }
                    case 5:
                    {
                        typedHandlers[0](context, typedMessage);
                        typedHandlers[1](context, typedMessage);
                        typedHandlers[2](context, typedMessage);
                        typedHandlers[3](context, typedMessage);
                        typedHandlers[4](context, typedMessage);
                        return;
                    }
                }

                foreach (Action<InstanceId, T> handler in typedHandlers)
                {
                    handler(context, typedMessage);
                }
            }

            private static List<TU> GetOrAddNewHandlerStack<TU>(HandlerActionCache<TU> actionCache)
            {
                if (actionCache.version == actionCache.lastSeenVersion)
                {
                    return actionCache.cache;
                }

                List<TU> cache = actionCache.cache;
                cache.Clear();
                foreach (TU handler in actionCache.originalToAugmented.Values)
                {
                    cache.Add(handler);
                }
                actionCache.lastSeenVersion = actionCache.version;
                return cache;
            }

            private static Action AddHandler<TU>(
                InstanceId context,
                ref Dictionary<
                    InstanceId,
                    Dictionary<int, HandlerActionCache<TU>>
                > handlersByContext,
                TU originalHandler,
                TU augmentedHandler,
                Action deregistration,
                int priority
            )
            {
                handlersByContext ??=
                    new Dictionary<InstanceId, Dictionary<int, HandlerActionCache<TU>>>();

                if (
                    !handlersByContext.TryGetValue(
                        context,
                        out Dictionary<int, HandlerActionCache<TU>> sortedHandlers
                    )
                )
                {
                    sortedHandlers = new Dictionary<int, HandlerActionCache<TU>>();
                    handlersByContext[context] = sortedHandlers;
                }

                if (!sortedHandlers.TryGetValue(priority, out HandlerActionCache<TU> cache))
                {
                    cache = new HandlerActionCache<TU>();
                    sortedHandlers[priority] = cache;
                }

                Dictionary<TU, int> handlers = cache.handlers;
                int count = handlers.GetValueOrDefault(originalHandler, 0);
                if (count == 0)
                {
                    cache.originalToAugmented[originalHandler] = augmentedHandler;
                }
                handlers[originalHandler] = count + 1;

                Dictionary<
                    InstanceId,
                    Dictionary<int, HandlerActionCache<TU>>
                > localHandlersByContext = handlersByContext;

                cache.version++;
                return () =>
                {
                    cache.version++;
                    if (!localHandlersByContext.TryGetValue(context, out sortedHandlers))
                    {
                        return;
                    }

                    if (!sortedHandlers.TryGetValue(priority, out cache))
                    {
                        return;
                    }

                    handlers = cache.handlers;

                    if (!handlers.TryGetValue(originalHandler, out count))
                    {
                        return;
                    }

                    // Always invoke deregistration action, as MessageBus dedupes this as well
                    deregistration?.Invoke();

                    if (count <= 1)
                    {
                        _ = handlers.Remove(originalHandler);
                        _ = cache.originalToAugmented.Remove(originalHandler);
                        if (0 < handlers.Count)
                        {
                            return;
                        }

                        _ = sortedHandlers.Remove(priority);
                        if (0 < sortedHandlers.Count)
                        {
                            return;
                        }

                        localHandlersByContext.Remove(context);
                        return;
                    }

                    handlers[originalHandler] = count - 1;
                };
            }

            private static Action AddHandler<TU>(
                ref HandlerActionCache<TU> cache,
                TU originalHandler,
                TU augmentedHandler,
                Action deregistration
            )
            {
                cache ??= new HandlerActionCache<TU>();
                Dictionary<TU, int> handlersByPriority = cache.handlers;
                int count = handlersByPriority.GetValueOrDefault(originalHandler, 0);
                if (count == 0)
                {
                    cache.originalToAugmented[originalHandler] = augmentedHandler;
                }

                handlersByPriority[originalHandler] = count + 1;

                Dictionary<TU, int> localHandlers = handlersByPriority;

                HandlerActionCache<TU> localCache = cache;
                localCache.version++;
                return () =>
                {
                    localCache.version++;
                    if (!localHandlers.TryGetValue(originalHandler, out count))
                    {
                        return;
                    }

                    // Always invoke deregistration action, as MessageBus dedupes this as well
                    deregistration?.Invoke();
                    if (count <= 1)
                    {
                        _ = localHandlers.Remove(originalHandler);
                        _ = localCache.originalToAugmented.Remove(originalHandler);
                        return;
                    }

                    localHandlers[originalHandler] = count - 1;
                };
            }

            private static Action AddHandler<TU>(
                ref Dictionary<int, HandlerActionCache<TU>> handlers,
                TU originalHandler,
                TU augmentedHandler,
                Action deregistration,
                int priority
            )
            {
                handlers ??= new Dictionary<int, HandlerActionCache<TU>>();

                if (!handlers.TryGetValue(priority, out HandlerActionCache<TU> cache))
                {
                    cache = new HandlerActionCache<TU>();
                    handlers[priority] = cache;
                }

                int count = cache.handlers.GetValueOrDefault(originalHandler, 0);
                if (count == 0)
                {
                    cache.originalToAugmented[originalHandler] = augmentedHandler;
                }

                cache.handlers[originalHandler] = count + 1;

                Dictionary<int, HandlerActionCache<TU>> localHandlers = handlers;
                cache.version++;

                return () =>
                {
                    cache.version++;
                    if (!localHandlers.TryGetValue(priority, out cache))
                    {
                        return;
                    }

                    if (!cache.handlers.TryGetValue(originalHandler, out count))
                    {
                        return;
                    }

                    // Always invoke deregistration action, as MessageBus dedupes this as well
                    deregistration?.Invoke();
                    if (count <= 1)
                    {
                        _ = cache.handlers.Remove(originalHandler);
                        _ = cache.originalToAugmented.Remove(originalHandler);
                        if (cache.handlers.Count == 0)
                        {
                            _ = localHandlers.Remove(priority);
                        }
                        return;
                    }

                    cache.handlers[originalHandler] = count - 1;
                };
            }
        }
    }
}
