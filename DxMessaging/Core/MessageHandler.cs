namespace DxMessaging.Core
{
    using MessageBus;
    using System;
    using System.Collections.Generic;

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
        public static readonly IMessageBus MessageBus = GlobalMessageBus.Instance;

        /// <summary>
        /// Maps Types to the corresponding Handler of that type.
        /// </summary>
        /// <note>
        /// Ideally, this would be something like a Dictionary[T,Handler[T]], but that can't be done with C#s type system.
        /// </note>
        private readonly Dictionary<Type, object> _handlersByType;

        /// <summary>
        /// Whether or not this MessageHandler will process messages.
        /// </summary>
        public bool Active;

        /// <summary>
        /// The Id of the GameObject that owns us.
        /// </summary>
        public InstanceId Owner { get; private set; }

        public MessageHandler(InstanceId owner)
        {
            Owner = owner;
            _handlersByType = new Dictionary<Type, object>();
        }

        /// <summary>
        /// Callback from the MessageBus for handling UntargetedMessages - user code should generally never use this.
        /// </summary>
        /// <note>
        /// In this case, "UntargetedMessage" refers to Targeted without targeting, and UntargetedMessages, hence T : AbstractMessage.
        /// </note>
        /// <typeparam name="T">Specific type of UntargetedMessage</typeparam>
        /// <param name="message">Message to handle.</param>
        public void HandleUntargetedMessage<T>(T message) where T : AbstractMessage
        {
            ActuallyHandleMessage<T>(typedHandler => typedHandler.HandleUntargeted(message));
        }

        /// <summary>
        /// Callback from the MessageBus for handling TargetedMessages when this MessageHandler has subscribed - user code should generally never use this.
        /// </summary>
        /// <note>
        /// TargetedMessages refers to those that are intended for the GameObject that owns this MessageHandler.
        /// </note>
        /// <typeparam name="T">Specific type of TargetedMessage</typeparam>
        /// <param name="message">Message to handle.</param>
        public void HandleTargetedMessage<T>(T message) where T : TargetedMessage
        {
            ActuallyHandleMessage<T>(typedHandler => typedHandler.HandleTargeted(message));
        }

        /// <summary>
        /// Callback from the MessageBus for handling Messages when this MessageHandler has subscribed to GlobalAcceptAll - user code should generally never use this.
        /// </summary>
        /// <typeparam name="T">Specific type of TargetedMessage</typeparam>
        /// <param name="message">Message to handle.</param>
        public void HandleGlobalMessage<T>(T message) where T : AbstractMessage
        {
            // Use the "AbstractMessage" explicitly to indicate global messages, allowing us to multi-purpose a single dictionary
            HandleUntargetedMessage<AbstractMessage>(message);
        }

        /// <summary>
        /// Actual MessageHandler implementation.
        /// </summary>
        /// <typeparam name="T">Type of Message to handle.</typeparam>
        /// <param name="handle">Handler function.</param>
        private void ActuallyHandleMessage<T>(Action<TypedHandler<T>> handle) where T : AbstractMessage
        {
            if (!Active)
            {
                return;
            }
            TypedHandler<T> handlerForType = GetHandlerForType<T>();
            if (!ReferenceEquals(handlerForType, null))
            {
                handle(handlerForType);
            }
        }

        /// <summary>
        /// Registers this MessageHandler to Globally Accept All Messages via the MessageBus, properly handling de-registration.
        /// </summary>
        /// <param name="messageHandler">Function that actually handles the message.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterGlobalAcceptAll(Action<AbstractMessage> messageHandler)
        {
            Action messageBusDeregistration = MessageBus.RegisterGlobalAcceptAll(this);
            TypedHandler<AbstractMessage> typedHandler = GetOrCreateHandlerForType<AbstractMessage>();
            typedHandler.UntargetedDeregister ??= messageBusDeregistration;
            return typedHandler.AddUntargetedHandler(messageHandler);
        }

        /// <summary>
        /// Registers this MessageHandler to accept TargetedMessages without Targeting via the MessageBus, properly handling de-registration.
        /// </summary>
        /// <typeparam name="T">Type of Message to be handled.</typeparam>
        /// <param name="messageHandler">Function that actually handles the message.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterTargetedWithoutTargeting<T>(Action<T> messageHandler) where T : TargetedMessage
        {
            Action messageBusDeregistration = MessageBus.RegisterTargetedWithoutTargeting<T>(this);
            TypedHandler<T> typedHandler = GetOrCreateHandlerForType<T>();
            typedHandler.UntargetedDeregister ??= messageBusDeregistration;
            return typedHandler.AddUntargetedHandler(messageHandler);
        }

        /// <summary>
        /// Registers this MessageHandler to accept TargetedMessages via the MessageBus, properly handling de-registration.
        /// </summary>
        /// <typeparam name="T">Type of Message to be handled.</typeparam>
        /// <param name="messageHandler">Function that actually handles the message.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterTargetedMessageHandler<T>(Action<T> messageHandler) where T : TargetedMessage
        {
            Action messageBusDeregistration = MessageBus.RegisterTargeted<T>(this);
            TypedHandler<T> typedHandler = GetOrCreateHandlerForType<T>();
            typedHandler.TargetedDeregister ??= messageBusDeregistration;
            return typedHandler.AddTargetedHandler(messageHandler);
        }

        /// <summary>
        /// Registers this MessageHandler to accept UntargetedMessages via the MessageBus, properly handling de-registration.
        /// </summary>
        /// <typeparam name="T">Type of Message to be handled.</typeparam>
        /// <param name="messageHandler">Function that actually handles the message.</param>
        /// <returns>The de-registration action.</returns>
        public Action RegisterUntargetedMessageHandler<T>(Action<T> messageHandler) where T : UntargetedMessage
        {
            Action messageBusDeregistration = MessageBus.RegisterUntargeted<T>(this);
            TypedHandler<T> typedHandler = GetOrCreateHandlerForType<T>();
            typedHandler.UntargetedDeregister ??= messageBusDeregistration;
            return typedHandler.AddUntargetedHandler(messageHandler);
        }

        /// <summary>
        /// Retrieves an existing Handler for the specific type, if it exists, or creates a new Handler, if none exist.
        /// </summary>
        /// <typeparam name="T">Type of Message to retrieve a Handler for.</typeparam>
        /// <returns>Non-Null Handler for the specific type.</returns>
        private TypedHandler<T> GetOrCreateHandlerForType<T>() where T : AbstractMessage
        {
            Type type = typeof(T);
            if (_handlersByType.TryGetValue(type, out object existingTypedHandler))
            {
                return (TypedHandler<T>)existingTypedHandler;
            }

            TypedHandler<T> newTypedHandler = new TypedHandler<T>();
            _handlersByType.Add(type, newTypedHandler);
            return newTypedHandler;
        }

        /// <summary>
        /// Gets an existing Handler for the specific type, if it exists.
        /// </summary>
        /// <typeparam name="T">Type of Message to retrieve a Handler for.</typeparam>
        /// <returns>Existing handler for the specific type, or null if none exists..</returns>
        private TypedHandler<T> GetHandlerForType<T>() where T : AbstractMessage
        {
            Type type = typeof(T);
            if (_handlersByType.TryGetValue(type, out object existingTypedHandler))
            {
                return (TypedHandler<T>)existingTypedHandler;
            }
            return null;
        }

        /// <summary>
        /// One-size-fits-all wrapper around all possible Messaging sinks for a particular MessageHandler & MessageType.
        /// </summary>
        /// <typeparam name="T">Message type that this Handler exists to serve.</typeparam>
        [Serializable]
        private sealed class TypedHandler<T> where T : AbstractMessage
        {
            private readonly List<Action<T>> _targetedHandlers;
            private readonly List<Action<T>> _untargetedHandlers;
            public Action TargetedDeregister;
            public Action UntargetedDeregister;

            public TypedHandler()
            {
                _targetedHandlers = new List<Action<T>>();
                _untargetedHandlers = new List<Action<T>>();
            }

            /// <summary>
            /// Emits the UntargetedMessage to all subscribed listeners.
            /// </summary>
            /// <param name="message">Message to emit.</param>
            public void HandleUntargeted(T message)
            {
                // ReSharper disable once ForCanBeConvertedToForeach
                for (int i = 0; i < _untargetedHandlers.Count; ++i)
                {
                    _untargetedHandlers[i](message);
                }
            }

            /// <summary>
            /// Emits the TargetedMessage to all subscribed listeners.
            /// </summary>
            /// <param name="message">Message to emit.</param>
            public void HandleTargeted(T message)
            {
                // ReSharper disable once ForCanBeConvertedToForeach
                for (int i = 0; i < _targetedHandlers.Count; ++i)
                {
                    _targetedHandlers[i](message);
                }
            }

            /// <summary>
            /// Adds a TargetedHandler to listen to Messages of the given type, returning a de-registration action.
            /// </summary>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <returns>De-registration action to un-register the handler.</returns>
            public Action AddTargetedHandler(Action<T> handler)
            {
                _targetedHandlers.Add(handler);
                return () =>
                {
                    _targetedHandlers.Remove(handler);
                    // Last one out cleans up. De-register should never be null
                    if (_targetedHandlers.Count != 0 && !ReferenceEquals(TargetedDeregister, null))
                    {
                        TargetedDeregister.Invoke();
                        TargetedDeregister = null;
                    }
                };
            }

            /// <summary>
            /// Adds a UntargetedHandler to listen to Messages of the given type, returning a de-registration action.
            /// </summary>
            /// <param name="handler">Relevant MessageHandler.</param>
            /// <returns>De-registration action to un-register the handler.</returns>
            public Action AddUntargetedHandler(Action<T> handler)
            {
                _untargetedHandlers.Add(handler);
                return () =>
                {
                    _untargetedHandlers.Remove(handler);
                    // Last one out cleans up. De-register should never be null
                    if (_untargetedHandlers.Count != 0 && !ReferenceEquals(UntargetedDeregister, null))
                    {
                        UntargetedDeregister.Invoke();
                        UntargetedDeregister = null;
                    }
                };
            }
        }
    }
}
