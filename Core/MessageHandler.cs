namespace DxMessaging.Core
{
    using MessageBus;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Messages;

    /// <summary>
    /// Abstraction layer for immediate-mode Message passing. An instance of this handles all
    /// kinds of types to trigger functions that are registered with it.
    /// </summary>
    [Serializable]
    public sealed class MessageHandler
    {
        /// <summary>
        /// MessageBus for all MessageHandlers to use. Currently immutable, but may change in the future.
        /// </summary>
        public static readonly IMessageBus MessageBus = new MessageBus.MessageBus();

        /// <summary>
        /// Maps Types to the corresponding Handler of that type.
        /// </summary>
        /// <note>
        /// Ideally, this would be something like a Dictionary[T,Handler[T]], but that can't be done with C#s type system.
        /// </note>
        private readonly Dictionary<IMessageBus, Dictionary<Type, IHandler>> _handlersByTypeByMessageBus;

        /// <summary>
        /// Whether or not this MessageHandler will process messages.
        /// </summary>
        public bool active;

        /// <summary>
        /// The Id of the GameObject that owns us.
        /// </summary>
        public readonly InstanceId owner;

        public MessageHandler(InstanceId owner)
        {
            this.owner = owner;
            _handlersByTypeByMessageBus = new Dictionary<IMessageBus, Dictionary<Type, IHandler>>();
        }

        /// <summary>
        /// Callback from the MessageBus for handling UntargetedMessages - user code should generally never use this.
        /// </summary>
        /// <note>
        /// In this case, "UntargetedMessage" refers to Targeted without targeting, and UntargetedMessages, hence T : AbstractMessage.
        /// </note>
        /// <param name="message">Message to handle.</param>
        /// <param name="messageBus">The specific MessageBus to use.</param>
        public void HandleUntargetedMessage(IMessage message, IMessageBus messageBus)
        {
            ActuallyHandleMessage(message.GetType(), typedHandler => typedHandler.HandleUntargeted(message), messageBus);
        }

        /// <summary>
        /// Callback from the MessageBus for handling TargetedMessages when this MessageHandler has subscribed - user code should generally never use this.
        /// </summary>
        /// <note>
        /// TargetedMessage refers to those that are intended for the GameObject that owns this MessageHandler.
        /// </note>
        /// <param name="message">Message to handle.</param>
        /// <param name="messageBus">The specific MessageBus to use.</param>
        public void HandleTargetedMessage(ITargetedMessage message, IMessageBus messageBus)
        {
            ActuallyHandleMessage(message.GetType(), typedHandler => typedHandler.HandleTargeted(message), messageBus);
        }

        /// <summary>
        /// Callback from the MessageBus for handling SourcedBroadcastMessages - user code should generally never use this.
        /// </summary>
        /// <note>
        /// SourcedBroadcastMessages generally refer to those that are sourced from the GameObject that owns this MessageHandler.
        /// </note>
        /// <param name="message">Message to handle</param>
        /// <param name="messageBus">The specific MessageBus to use.</param>
        public void HandleSourcedBroadcast(IBroadcastMessage message, IMessageBus messageBus)
        {
            ActuallyHandleMessage(message.GetType(), typedHandler => typedHandler.HandleSourcedBroadcast(message), messageBus);
        }

        /// <summary>
        /// Callback from the MessageBus for handling Messages when this MessageHandler has subscribed to GlobalAcceptAll - user code should generally never use this.
        /// </summary>
        /// <param name="message">Message to handle.</param>
        /// <param name="messageBus">The specific MessageBus to use.</param>
        public void HandleGlobalUntargetedMessage(IUntargetedMessage message, IMessageBus messageBus)
        {
            // Use the "IMessage" explicitly to indicate global messages, allowing us to multi-purpose a single dictionary
            ActuallyHandleMessage(typeof(IMessage), typedHandler => typedHandler.HandleGlobalUntargeted(message), messageBus);
        }

        /// <summary>
        /// Callback from the MessageBus for handling Messages when this MessageHandler has subscribed to GlobalAcceptAll - user code should generally never use this.
        /// </summary>
        /// <param name="target">Target of the message.</param>
        /// <param name="message">Message to handle.</param>
        /// <param name="messageBus">The specific MessageBus to use.</param>
        public void HandleGlobalTargetedMessage(InstanceId target, ITargetedMessage message, IMessageBus messageBus)
        {
            // Use the "IMessage" explicitly to indicate global messages, allowing us to multi-purpose a single dictionary
            ActuallyHandleMessage(typeof(IMessage), typedHandler => typedHandler.HandleGlobalTargeted(target, message), messageBus);
        }

        /// <summary>
        /// Callback from the MessageBus for handling Messages when this MessageHandler has subscribed to GlobalAcceptAll - user code should generally never use this.
        /// </summary>
        /// <param name="source">Source that this message is from.</param>
        /// <param name="message">Message to handle.</param>
        /// <param name="messageBus">The specific MessageBus to use.</param>
        public void HandleGlobalSourcedBroadcastMessage(InstanceId source, IBroadcastMessage message, IMessageBus messageBus)
        {
            // Use the "IMessage" explicitly to indicate global messages, allowing us to multi-purpose a single dictionary
            ActuallyHandleMessage(typeof(IMessage), typedHandler => typedHandler.HandleGlobalBroadcast(source, message), messageBus);
        }

        /// <summary>
        /// Actual MessageHandler implementation.
        /// </summary>
        /// <typeparam name="T">Type of Message to handle.</typeparam>
        /// <param name="handle">Handler function.</param>
        /// <param name="messageBus">The specific MessageBus to use.</param>
        private void ActuallyHandleMessage(Type type, Action<IHandler> handle, IMessageBus messageBus)
        {
            if (!active)
            {
                return;
            }

            IHandler handlerForType = GetHandlerForType(type, messageBus);
            if (!ReferenceEquals(handlerForType, null))
            {
                handle(handlerForType);
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
        public Action RegisterGlobalAcceptAll(Action<IUntargetedMessage> untargetedMessageHandler, Action<InstanceId, ITargetedMessage> targetedMessageHandler, Action<InstanceId, IBroadcastMessage> broadcastMessageHandler, IMessageBus messageBus = null)
        {
            messageBus = messageBus ?? MessageBus;
            Action messageBusDeregistration = messageBus.RegisterGlobalAcceptAll(this);
            TypedHandler<IMessage> typedHandler = GetOrCreateHandlerForType<IMessage>(messageBus);
            Action untargetedDeregistration = typedHandler.AddGlobalUntargetedHandler(untargetedMessageHandler, messageBusDeregistration);
            Action targetedDeregistration = typedHandler.AddGlobalTargetedHandler(targetedMessageHandler, messageBusDeregistration);
            Action broadcastDeregistration = typedHandler.AddGlobalBroadcastHandler(broadcastMessageHandler, messageBusDeregistration);

            return () =>
            {
                untargetedDeregistration();
                targetedDeregistration();
                broadcastDeregistration();
            };
        }

        /// <summary>
        /// Registers this MessageHandler to accept TargetedMessages via the MessageBus, properly handling de-registration.
        /// </summary>
        /// <typeparam name="T">Type of Message to be handled.</typeparam>
        /// <param name="messageHandler">Function that actually handles the message.</param>
        /// <param name="messageBus">IMessageBus override to register with, if any. Null/not provided defaults to the GlobalMessageBus.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterTargetedMessageHandler<T>(Action<T> messageHandler, IMessageBus messageBus = null) where T : ITargetedMessage
        {
            messageBus = messageBus ?? MessageBus;
            Action messageBusDeregistration = messageBus.RegisterTargeted<T>(this);
            TypedHandler<T> typedHandler = GetOrCreateHandlerForType<T>(messageBus);
            return typedHandler.AddTargetedHandler(messageHandler, messageBusDeregistration);
        }

        public Action RegisterTargetedMessageHandler<T>(InstanceId target, Action<T> messageHandler, IMessageBus messageBus = null) where T : ITargetedMessage
        {
            messageBus = messageBus ?? MessageBus;
            Action messageBusDeregistration = messageBus.RegisterTargeted<T>(target, this);
            TypedHandler<T> typedHandler = GetOrCreateHandlerForType<T>(messageBus);
            return typedHandler.AddTargetedHandler(messageHandler, messageBusDeregistration);
        }

        /// <summary>
        /// Registers this MessageHandler to accept TargetedMessages without Targeting via the MessageBus, properly handling de-registration.
        /// </summary>
        /// <typeparam name="T">Type of Message to be handled.</typeparam>
        /// <param name="messageHandler">Function that actually handles the message.</param>
        /// <param name="messageBus">IMessageBus override to register with, if any. Null/not provided defaults to the GlobalMessageBus.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterTargetedWithoutTargeting<T>(Action<T> messageHandler, IMessageBus messageBus = null) where T : ITargetedMessage
        {
            messageBus = messageBus ?? MessageBus;
            Action messageBusDeregistration = messageBus.RegisterTargetedWithoutTargeting<T>(this);
            TypedHandler<T> typedHandler = GetOrCreateHandlerForType<T>(messageBus);
            return typedHandler.AddUntargetedHandler(messageHandler, messageBusDeregistration);
        }

        /// <summary>
        /// Registers this MessageHandler to accept UntargetedMessages via the MessageBus, properly handling de-registration.
        /// </summary>
        /// <typeparam name="T">Type of Message to be handled.</typeparam>
        /// <param name="messageHandler">Function that actually handles the message.</param>
        /// <param name="messageBus">IMessageBus override to register with, if any. Null/not provided defaults to the GlobalMessageBus.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterUntargetedMessageHandler<T>(Action<T> messageHandler, IMessageBus messageBus = null) where T : IUntargetedMessage
        {
            messageBus = messageBus ?? MessageBus;
            Action messageBusDeregistration = messageBus.RegisterUntargeted<T>(this);
            TypedHandler<T> typedHandler = GetOrCreateHandlerForType<T>(messageBus);
            return typedHandler.AddUntargetedHandler(messageHandler, messageBusDeregistration);
        }

        /// <summary>
        /// Registers this MessageHandler to accept BroadcastMessages via their MessageBus, properly handling de-registration.
        /// </summary>
        /// <typeparam name="T">Type of Message to be handled.</typeparam>
        /// <param name="source">Source Id of BroadcastMessages to listen for.</param>
        /// <param name="messageHandler">Function that actually handles the message.</param>
        /// <param name="messageBus">IMessageBus override to register with, if any. Null/not provided defaults to the GlobalMessageBus.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterSourcedBroadcastMessageHandler<T>(InstanceId source, Action<T> messageHandler, IMessageBus messageBus = null)
            where T : IBroadcastMessage
        {
            messageBus = messageBus ?? MessageBus;
            Action messageBusDeregistration = messageBus.RegisterSourcedBroadcast<T>(source, this);
            TypedHandler<T> typedHandler = GetOrCreateHandlerForType<T>(messageBus);;
            return typedHandler.AddSourcedBroadcastHandler(messageHandler, messageBusDeregistration);
        }

        /// <summary>
        /// Registers this MessageHandler to accept BroadcastMessage regardless of source via their Messagebus, properly handling de-registration.
        /// </summary>
        /// <typeparam name="T">Type of Message to be handled.</typeparam>
        /// <param name="messageHandler">Function that actually handles the message.</param>
        /// <param name="messageBus">IMessageBus override to register with, if any. Null/not provided defaults to the GlobalMessageBus.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterSourcedBroadcastWithoutSource<T>(Action<T> messageHandler, IMessageBus messageBus = null) where T : IBroadcastMessage
        {
            messageBus = messageBus ?? MessageBus;
            Action messageBusDeregistration = messageBus.RegisterSourcedBroadcastWithoutSource<T>(this);
            TypedHandler<T> typedHandler = GetOrCreateHandlerForType<T>(messageBus);
            return typedHandler.AddUntargetedHandler(messageHandler, messageBusDeregistration);
        }

        /// <summary>
        /// Registers this MessageHandler to intercept messages of the provided type for the provided MessageBus, properly handling de-registration
        /// </summary>
        /// <typeparam name="T">Type of Message to be handled.</typeparam>
        /// <param name="transformer">Interceptor function.</param>
        /// <param name="messageBus">IMessageBus override to register with, if any. Null/not provided defaults to the GlobalMessageBus.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterInterceptor<T>(Func<T, object, T> transformer, IMessageBus messageBus = null) where T : IMessage
        {
            return (messageBus ?? MessageBus).RegisterInterceptor(transformer);
        }

        public override string ToString()
        {
            return new
            {
                OwnerId = owner,
                HandlerTypes = string.Join(",",
                    _handlersByTypeByMessageBus.Values.SelectMany(key => key.Keys).Select(type => type.Name)
                        .OrderBy(_ => _))
            }.ToString();
        }

        /// <summary>
        /// Retrieves an existing Handler for the specific type, if it exists, or creates a new Handler, if none exist.
        /// </summary>
        /// <typeparam name="T">Type of Message to retrieve a Handler for.</typeparam>
        /// <returns>Non-Null Handler for the specific type.</returns>
        private TypedHandler<T> GetOrCreateHandlerForType<T>(IMessageBus messageBus) where T : IMessage
        {
            Type type = typeof(T);

            if (!_handlersByTypeByMessageBus.TryGetValue(messageBus, out Dictionary<Type, IHandler> handlersByType))
            {
                handlersByType = new Dictionary<Type, IHandler>();
                _handlersByTypeByMessageBus[messageBus] = handlersByType;
            }

            if (handlersByType.TryGetValue(type, out IHandler existingTypedHandler))
            {
                return (TypedHandler<T>) existingTypedHandler;
            }

            TypedHandler<T> newTypedHandler = new TypedHandler<T>();
            handlersByType.Add(type, newTypedHandler);
            return newTypedHandler;
        }

        /// <summary>
        /// Gets an existing Handler for the specific type, if it exists.
        /// </summary>
        /// <param name="type">Message type to get the handler for.</param>
        /// <param name="messageBus">The specific MessageBus to use.</param>
        /// <returns>Existing handler for the specific type, or null if none exists..</returns>
        private IHandler GetHandlerForType(Type type, IMessageBus messageBus)
        {
            if (_handlersByTypeByMessageBus.TryGetValue(messageBus, out Dictionary<Type, IHandler> handlersByType) && handlersByType.TryGetValue(type, out IHandler existingTypedHandler))
            {
                return existingTypedHandler;
            }

            return null;
        }

        /// <summary>
        /// Needed for actually emitting messages
        /// </summary>
        private interface IHandler
        {
            void HandleUntargeted(IMessage message);
            void HandleTargeted(ITargetedMessage message);
            void HandleSourcedBroadcast(IBroadcastMessage message);

            void HandleGlobalUntargeted(IUntargetedMessage message);
            void HandleGlobalTargeted(InstanceId target, ITargetedMessage message);
            void HandleGlobalBroadcast(InstanceId source, IBroadcastMessage message);
        }

        /// <summary>
        /// One-size-fits-all wrapper around all possible Messaging sinks for a particular MessageHandler & MessageType.
        /// </summary>
        /// <typeparam name="T">Message type that this Handler exists to serve.</typeparam>
        [Serializable]
        private sealed class TypedHandler<T> : IHandler where T: IMessage
        {
            private readonly Dictionary<Action<T>, int> _targetedHandlers = new();
            private readonly Dictionary<Action<T>, int> _untargetedHandlers = new();
            private readonly Dictionary<Action<T>, int> _broadcastHandlers = new();
            private readonly Dictionary<Action<IUntargetedMessage>, int> _globalUntargetedHandlers = new();
            private readonly Dictionary<Action<InstanceId, ITargetedMessage>, int> _globalTargetedHandlers = new();
            private readonly Dictionary<Action<InstanceId, IBroadcastMessage>, int> _globalBroadcastHandlers = new();

            // Buffers so we don't allocate memory as often
            private readonly Stack<List<Action<T>>> _handlersStack = new();
            private readonly Stack<List<Action<IUntargetedMessage>>> _globalUntargetedHandlersStack = new();
            private readonly Stack<List<Action<InstanceId, ITargetedMessage>>> _globalTargetedHandlersStack = new();
            private readonly Stack<List<Action<InstanceId, IBroadcastMessage>>> _globalBroadcastHandlersStack = new();

            /// <summary>
            /// Emits the UntargetedMessage to all subscribed listeners.
            /// </summary>
            /// <param name="message">Message to emit.</param>
            public void HandleUntargeted(IMessage message)
            {
                if (_untargetedHandlers.Count <= 0)
                {
                    return;
                }

                List<Action<T>> handlers = GetOrAddNewHandlerStack(_untargetedHandlers.Keys);
                try
                {
                    foreach (Action<T> handler in handlers)
                    {
                        handler((T) message);
                    }
                }
                finally
                {
                    _handlersStack.Push(handlers);
                }
            }

            /// <summary>
            /// Emits the TargetedMessage to all subscribed listeners.
            /// </summary>
            /// <param name="message">Message to emit.</param>
            public void HandleTargeted(ITargetedMessage message)
            {
                if (_targetedHandlers.Count <= 0)
                {
                    return;
                }

                List<Action<T>> handlers = GetOrAddNewHandlerStack(_targetedHandlers.Keys);
                try
                {
                    foreach (Action<T> handler in handlers)
                    {
                        handler((T) message);
                    }
                }
                finally
                {
                    _handlersStack.Push(handlers);
                }
            }

            /// <summary>
            /// Emits the BroadcastMessage to all subscribed listeners.
            /// </summary>
            /// <param name="message">Message to emit.</param>
            public void HandleSourcedBroadcast(IBroadcastMessage message)
            {
                if (_broadcastHandlers.Count <= 0)
                {
                    return;
                }

                List<Action<T>> handlers = GetOrAddNewHandlerStack(_broadcastHandlers.Keys);
                try
                {
                    foreach (Action<T> handler in handlers)
                    {
                        handler((T) message);
                    }
                }
                finally
                {
                    _handlersStack.Push(handlers);
                }
            }

            /// <summary>
            /// Emits the UntargetedMessage to all global listeners.
            /// </summary>
            /// <param name="message">Message to emit.</param>
            public void HandleGlobalUntargeted(IUntargetedMessage message)
            {
                if (_globalUntargetedHandlers.Count <= 0)
                {
                    return;
                }

                if (_globalUntargetedHandlersStack.TryPop(out List<Action<IUntargetedMessage>> handlers))
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
                    _globalUntargetedHandlersStack.Push(handlers);
                }
            }

            /// <summary>
            /// Emits the TargetedMessage to all global listeners.
            /// </summary>
            /// <param name="target">Target that this message is intended for.</param>
            /// <param name="message">Message to emit.</param>
            public void HandleGlobalTargeted(InstanceId target, ITargetedMessage message)
            {
                if (_globalTargetedHandlers.Count <= 0)
                {
                    return;
                }

                if (_globalTargetedHandlersStack.TryPop(out List<Action<InstanceId, ITargetedMessage>> handlers))
                {
                    handlers.Clear();
                    handlers.AddRange(_globalTargetedHandlers.Keys);
                }
                else
                {
                    handlers = new List<Action<InstanceId, ITargetedMessage>>(_globalTargetedHandlers.Keys);
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
                    _globalTargetedHandlersStack.Push(handlers);
                }
            }

            /// <summary>
            /// Emits the BroadcastMessage to all global listeners.
            /// </summary>
            /// <param name="source">Source that this message is from.</param>
            /// <param name="message">Message to emit.</param>
            public void HandleGlobalBroadcast(InstanceId source, IBroadcastMessage message)
            {
                if (_globalBroadcastHandlers.Count <= 0)
                {
                    return;
                }

                if (_globalBroadcastHandlersStack.TryPop(out List<Action<InstanceId, IBroadcastMessage>> handlers))
                {
                    handlers.Clear();
                    handlers.AddRange(_globalBroadcastHandlers.Keys);
                }
                else
                {
                    handlers = new List<Action<InstanceId, IBroadcastMessage>>(_globalBroadcastHandlers.Keys);
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
                    _globalBroadcastHandlersStack.Push(handlers);
                }
            }

            /// <summary>
            /// Adds a TargetedHandler to listen to Messages of the given type, returning a de-registration action.
            /// </summary>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <returns>De-registration action to un-register the handler.</returns>
            public Action AddTargetedHandler(Action<T> handler, Action deregistration)
            {
                return AddHandler(_targetedHandlers, handler, deregistration);
            }

            /// <summary>
            /// Adds a UntargetedHandler to listen to Messages of the given type, returning a de-registration action.
            /// </summary>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <returns>De-registration action to un-register the handler.</returns>
            public Action AddUntargetedHandler(Action<T> handler, Action deregistration)
            {
                return AddHandler(_untargetedHandlers, handler, deregistration);
            }

            /// <summary>
            /// Adds a SourcedBroadcastHandler to listen to Messages of the given type from an entity, returning a de-registration action.
            /// </summary>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <returns>De-registration action to un-register the handler.</returns>
            public Action AddSourcedBroadcastHandler(Action<T> handler, Action deregistration)
            {
                return AddHandler(_broadcastHandlers, handler, deregistration);
            }

            /// <summary>
            /// Adds a Global UntargetedHandler to listen to all Untargeted Messages of all types, returning the de-registration action.
            /// </summary>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <returns>De-registration action to un-register the handler.</returns>
            public Action AddGlobalUntargetedHandler(Action<IUntargetedMessage> handler, Action deregistration)
            {
                return AddHandler(_globalUntargetedHandlers, handler, deregistration);
            }

            /// <summary>
            /// Adds a Global TargetedHandler to listen to all Targeted Messages of all types for all entities, returning the de-registration action.
            /// </summary>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <returns>De-registration action to un-register the handler.</returns>
            public Action AddGlobalTargetedHandler(Action<InstanceId, ITargetedMessage> handler, Action deregistration)
            {
                return AddHandler(_globalTargetedHandlers, handler, deregistration);
            }

            /// <summary>
            /// Adds a Global BroadcastHandler to listen to all Targeted Messages of all types for all entities, returning the de-registration action.
            /// </summary>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <param name="deregistration">Deregistration action for the handler.</param>
            /// <returns>De-registration action to un-register the handler.</returns>
            public Action AddGlobalBroadcastHandler(Action<InstanceId, IBroadcastMessage> handler, Action deregistration)
            {
                return AddHandler(_globalBroadcastHandlers, handler, deregistration);
            }

            private List<Action<T>> GetOrAddNewHandlerStack(IEnumerable<Action<T>> handlers)
            {
                if (_handlersStack.TryPop(out List<Action<T>> handlerStack))
                {
                    handlerStack.Clear();
                    handlerStack.AddRange(handlers);
                    return handlerStack;
                }

                return new List<Action<T>>(handlers);
            }

            private static Action AddHandler<U>(Dictionary<U, int> handlers, U handler, Action deregistration)
            {
                if (!handlers.TryGetValue(handler, out int count))
                {
                    count = 0;
                }

                ++count;
                handlers[handler] = count;
                return () =>
                {
                    if (!handlers.TryGetValue(handler, out count))
                    {
                        return;
                    }

                    --count;
                    if (count <= 0 && handlers.Remove(handler))
                    {
                        deregistration?.Invoke();
                        return;
                    }

                    handlers[handler] = count;
                };
            }
        }
    }
}
