namespace DxMessaging.Core.MessageBus
{
    using System;
    using System.Collections.Generic;
    using Messages;

    /// <summary>
    /// Instanced MessageBus for use cases where you want distinct islands of MessageBuses.
    /// </summary>
    public sealed class MessageBus : IMessageBus
    {
        public RegistrationLog Log => _log;

        private readonly Dictionary<Type, Dictionary<MessageHandler, int>> _sinks = new();
        private readonly Dictionary<Type, Dictionary<InstanceId, Dictionary<MessageHandler, int>>> _targetedSinks = new();
        private readonly Dictionary<Type, Dictionary<InstanceId, Dictionary<MessageHandler, int>>> _broadcastSinks = new();
        private readonly Dictionary<MessageHandler, int> _globalSinks = new();
        private readonly Dictionary<Type, List<object>> _interceptsByType = new();

        private readonly RegistrationLog _log = new RegistrationLog();

        // These are used so we aren't allocating as much every time we send messages
        private readonly Stack<List<MessageHandler>> _messageHandlers = new Stack<List<MessageHandler>>();
        private readonly Stack<List<object>> _interceptors = new Stack<List<object>>();

        /// <inheritdoc/>
        public Action RegisterUntargeted<T>(MessageHandler messageHandler) where T : IUntargetedMessage
        {
            return InternalRegisterUntargeted<T>(messageHandler, RegistrationMethod.Untargeted);
        }

        /// <inheritdoc/>
        public Action RegisterTargeted<T>(MessageHandler messageHandler) where T : ITargetedMessage
        {
            if (messageHandler == null)
            {
                throw new ArgumentNullException(nameof(messageHandler));
            }

            InstanceId handlerOwnerId = messageHandler.owner;
            return RegisterTargeted<T>(handlerOwnerId, messageHandler);
        }

        public Action RegisterTargeted<T>(InstanceId target, MessageHandler messageHandler) where T : ITargetedMessage
        {
            Type type = typeof(T);
            if (!_targetedSinks.TryGetValue(type, out Dictionary<InstanceId, Dictionary<MessageHandler, int>> targetedSink))
            {
                targetedSink = new Dictionary<InstanceId, Dictionary<MessageHandler, int>>();
                _targetedSinks[type] = targetedSink;
            }

            if (!targetedSink.TryGetValue(target, out Dictionary<MessageHandler, int> existingHandlers))
            {
                existingHandlers = new Dictionary<MessageHandler, int>();
                targetedSink[target] = existingHandlers;
            }

            if (!existingHandlers.TryGetValue(messageHandler, out int count))
            {
                count = 0;
            }

            existingHandlers[messageHandler] = count + 1;
            _log.Log(new MessagingRegistration(target, type, RegistrationType.Register, RegistrationMethod.Targeted));

            return () =>
            {
                if (!targetedSink.TryGetValue(target, out existingHandlers) || !existingHandlers.TryGetValue(messageHandler, out count))
                {
                    MessagingDebug.Log(
                        "Received double targeted deregistration of {0} for {1}. Check to make sure you're not calling registration multiple times.",
                        typeof(T), target);
                    return;
                }

                --count;
                if (count <= 0)
                {
                    _ = existingHandlers.Remove(messageHandler);
                    if (existingHandlers.Count <= 0)
                    {
                        _ = targetedSink.Remove(target);
                    }
                }
                else
                {
                    existingHandlers[messageHandler] = count;
                }
                _log.Log(new MessagingRegistration(target, type, RegistrationType.Deregister, RegistrationMethod.Targeted));
            };
        }

        /// <inheritdoc/>
        public Action RegisterSourcedBroadcast<T>(InstanceId source, MessageHandler messageHandler) where T : IBroadcastMessage
        {
            if (messageHandler == null)
            {
                throw new ArgumentNullException(nameof(messageHandler));
            }

            Type type = typeof(T);
            if (!_broadcastSinks.TryGetValue(type, out Dictionary<InstanceId, Dictionary<MessageHandler, int>> broadcastHandlers))
            {
                broadcastHandlers = new Dictionary<InstanceId, Dictionary<MessageHandler, int>>();
                _broadcastSinks[type] = broadcastHandlers;
            }

            if (!broadcastHandlers.TryGetValue(source, out Dictionary<MessageHandler, int> handlers))
            {
                handlers = new Dictionary<MessageHandler, int>();
                broadcastHandlers[source] = handlers;
            }

            if (!handlers.TryGetValue(messageHandler, out int count))
            {
                count = 0;
            }

            ++count;
            handlers[messageHandler] = count;
            _log.Log(new MessagingRegistration(source, type, RegistrationType.Register, RegistrationMethod.Broadcast));

            return () =>
            {
                if (!handlers.TryGetValue(messageHandler, out int handlerCount))
                {
                    MessagingDebug.Log(
                        "Received over-deregistration of {0} for {1}. Check to make sure you're not calling (de)registration multiple times.",
                        type, messageHandler);
                    return;
                }

                --handlerCount;
                if (handlerCount <= 0)
                {
                    _ = handlers.Remove(messageHandler);
                }
                else
                {
                    handlers[messageHandler] = handlerCount;
                }

                _log.Log(new MessagingRegistration(source, type, RegistrationType.Deregister, RegistrationMethod.Broadcast));
            };
        }

        /// <inheritdoc/>
        public Action RegisterSourcedBroadcastWithoutSource<T>(MessageHandler messageHandler) where T : IBroadcastMessage
        {
            return InternalRegisterUntargeted<T>(messageHandler, RegistrationMethod.BroadcastWithoutSource);
        }

        /// <inheritdoc/>
        public Action RegisterTargetedWithoutTargeting<T>(MessageHandler messageHandler) where T : ITargetedMessage
        {
            return InternalRegisterUntargeted<T>(messageHandler, RegistrationMethod.TargetedWithoutTargeting);
        }

        /// <inheritdoc/>
        public Action RegisterGlobalAcceptAll(MessageHandler messageHandler)
        {
            if (!_globalSinks.TryGetValue(messageHandler, out int count))
            {
                count = 0;
            }

            Type type = typeof(IMessage);
            ++count;
            _globalSinks[messageHandler] = count;
            _log.Log(new MessagingRegistration(messageHandler.owner, type, RegistrationType.Register, RegistrationMethod.GlobalAcceptAll));

            return () =>
            {
                if (!_globalSinks.TryGetValue(messageHandler, out int handlerCount))
                {
                    MessagingDebug.Log(
                        "Received over-deregistration of GlobalAcceptAll for MessageHandler {0}. Check to make sure you're not calling (de)registration multiple times.",
                        messageHandler);

                    return;
                }

                --handlerCount;
                if (handlerCount <= 0)
                {
                    _ = _globalSinks.Remove(messageHandler);
                }
                else
                {
                    _globalSinks[messageHandler] = handlerCount;
                }
                _log.Log(new MessagingRegistration(messageHandler.owner, type, RegistrationType.Deregister, RegistrationMethod.GlobalAcceptAll));
            };
        }

        public Action RegisterIntercept<T>(Func<T, T> transformer) where T : IMessage
        {
            Type type = typeof(T);
            if (!_interceptsByType.TryGetValue(type, out List<object> interceptors))
            {
                interceptors = new List<object>();
                _interceptsByType[type] = interceptors;
            }
            interceptors.Add(transformer);
            _log.Log(new MessagingRegistration(InstanceId.EmptyId, type, RegistrationType.Register, RegistrationMethod.Interceptor));

            return () =>
            {
                _ = interceptors.Remove(transformer);
                if (interceptors.Count <= 0)
                {
                    _ = _interceptsByType.Remove(type);
                }
                _log.Log(new MessagingRegistration(InstanceId.EmptyId, type, RegistrationType.Deregister, RegistrationMethod.Interceptor));
            };
        }

        /// <inheritdoc/>
        public void UntargetedBroadcast(IUntargetedMessage typedMessage)
        {
            RunInterceptors(ref typedMessage);
            if (typedMessage == null)
            {
                return;
            }
            BroadcastGlobalUntargeted(typedMessage);
            bool foundAnyHandlers = false;

            Type type = typedMessage.GetType();
            if (InternalUntargetedBroadcast(typedMessage, type))
            {
                foundAnyHandlers = true;
            }

            if (!foundAnyHandlers)
            {
                MessagingDebug.Log("Could not find a matching untargeted broadcast handler for Message: {0}.", typedMessage);
            }
        }

        /// <inheritdoc/>
        public void TargetedBroadcast(InstanceId target, ITargetedMessage typedMessage)
        {
            RunInterceptors(ref typedMessage);
            if (typedMessage == null)
            {
                return;
            }
            BroadcastGlobalTargeted(target, typedMessage);
            bool foundAnyHandlers = false;

            Type type = typedMessage.GetType();
            if (_targetedSinks.TryGetValue(type, out Dictionary<InstanceId, Dictionary<MessageHandler, int>> targetedHandlers) &&
                targetedHandlers.TryGetValue(target, out Dictionary<MessageHandler, int> handlers))
            {
                foundAnyHandlers = 0 < handlers.Count;
                if (foundAnyHandlers)
                {
                    List<MessageHandler> messageHandlers = GetOrAddMessageHandlerStack(handlers.Keys);
                    try
                    {
                        foreach (MessageHandler handler in messageHandlers)
                        {
                            handler.HandleTargetedMessage(typedMessage, this);
                        }
                    }
                    finally
                    {
                        _messageHandlers.Push(messageHandlers);
                    }
                }
            }

            if (InternalUntargetedBroadcast(typedMessage, type))
            {
                foundAnyHandlers = true;
            }

            if (!foundAnyHandlers)
            {
                MessagingDebug.Log("Could not find a matching targeted broadcast handler for Id: {0}, Message: {1}.", target,
                    typedMessage);
            }
        }

        /// <inheritdoc/>
        public void SourcedBroadcast(InstanceId source, IBroadcastMessage typedMessage)
        {
            RunInterceptors(ref typedMessage);
            if (typedMessage == null)
            {
                return;
            }
            BroadcastGlobalSourcedBroadcast(source, typedMessage);
            bool foundAnyHandlers = false;
            Type type = typedMessage.GetType();
            if (_broadcastSinks.TryGetValue(type,
                    out Dictionary<InstanceId, Dictionary<MessageHandler, int>> broadcastHandlers) &&
                broadcastHandlers.TryGetValue(source, out Dictionary<MessageHandler, int> handlers) && 0 < handlers.Count)
            {
                List<MessageHandler> messageHandlers = GetOrAddMessageHandlerStack(handlers.Keys);
                try
                {
                    foreach (MessageHandler handler in messageHandlers)
                    {
                        handler.HandleSourcedBroadcast(typedMessage, this);
                    }
                }
                finally
                {
                    _messageHandlers.Push(messageHandlers);
                }
            }

            if (InternalUntargetedBroadcast(typedMessage, type))
            {
                foundAnyHandlers = true;
            }

            if (!foundAnyHandlers)
            {
                MessagingDebug.Log("Could not find a matching sourced broadcast handler for Id: {0}, Message: {1}.",
                    source, typedMessage);
            }
        }

        private void BroadcastGlobalUntargeted(IUntargetedMessage message)
        {
            if (_globalSinks.Count <= 0)
            {
                return;
            }

            List<MessageHandler> messageHandlers = GetOrAddMessageHandlerStack(_globalSinks.Keys);
            try
            {
                foreach (MessageHandler handler in messageHandlers)
                {
                    handler.HandleGlobalUntargetedMessage(message, this);
                }
            }
            finally
            {
                _messageHandlers.Push(messageHandlers);
            }
        }

        private void BroadcastGlobalTargeted(InstanceId target, ITargetedMessage message)
        {
            if (_globalSinks.Count <= 0)
            {
                return;
            }

            List<MessageHandler> messageHandlers = GetOrAddMessageHandlerStack(_globalSinks.Keys);
            try
            {
                foreach (MessageHandler handler in messageHandlers)
                {
                    handler.HandleGlobalTargetedMessage(target, message, this);
                }
            }
            finally
            {
                _messageHandlers.Push(messageHandlers);
            }
        }

        private void BroadcastGlobalSourcedBroadcast(InstanceId source, IBroadcastMessage message)
        {
            if (_globalSinks.Count <= 0)
            {
                return;
            }

            List<MessageHandler> messageHandlers = GetOrAddMessageHandlerStack(_globalSinks.Keys);
            try
            {
                foreach (MessageHandler handler in messageHandlers)
                {
                    handler.HandleGlobalSourcedBroadcastMessage(source, message, this);
                }
            }
            finally
            {
                _messageHandlers.Push(messageHandlers);
            }
        }

        private void RunInterceptors<T>(ref T message) where T : IMessage
        {
            if (!_interceptsByType.TryGetValue(typeof(T), out List<object> interceptors) || interceptors.Count <= 0)
            {
                return;
            }

            List<object> interceptorsStack;
            if (_interceptors.Count <= 0)
            {
                interceptorsStack = new List<object>(interceptors.Count);
            }
            else
            {
                interceptorsStack = _interceptors.Pop();
                interceptorsStack.Clear();
            }

            interceptorsStack.AddRange(interceptors);

            try
            {
                foreach (Func<T, T> transformer in interceptorsStack)
                {
                    if (message == null)
                    {
                        return;
                    }

                    message = transformer(message);
                }
            }
            finally
            {
                _interceptors.Push(interceptorsStack);
            }
        }

        private bool InternalUntargetedBroadcast(IMessage message, Type type)
        {
            if (_sinks.TryGetValue(type, out Dictionary<MessageHandler, int> handlers) && 0 < handlers.Count)
            {
                List<MessageHandler> messageHandlers = GetOrAddMessageHandlerStack(handlers.Keys);
                try
                {
                    foreach (MessageHandler handler in messageHandlers)
                    {
                        handler.HandleUntargetedMessage(message, this);
                    }
                }
                finally
                {
                    _messageHandlers.Push(messageHandlers);
                }

                return true;
            }

            return false;
        }

        private Action InternalRegisterUntargeted<T>(MessageHandler messageHandler, RegistrationMethod registrationMethod) where T : IMessage
        {
            if (messageHandler == null)
            {
                throw new ArgumentNullException(nameof(messageHandler));
            }

            InstanceId handlerOwnerId = messageHandler.owner;
            Type type = typeof(T);

            if (!_sinks.TryGetValue(type, out Dictionary<MessageHandler, int> handlers))
            {
                handlers = new Dictionary<MessageHandler, int>();
                _sinks[type] = handlers;
            }

            if (!handlers.TryGetValue(messageHandler, out int count))
            {
                count = 0;
            }

            ++count;
            handlers[messageHandler] = count;
            _log.Log(new MessagingRegistration(handlerOwnerId, type, RegistrationType.Register, registrationMethod));

            return () =>
            {
                if (!handlers.TryGetValue(messageHandler, out int handlerCount))
                {
                    MessagingDebug.Log(
                        "Received over-deregistration of {0} for {1}. Check to make sure you're not calling (de)registration multiple times.",
                        type, messageHandler);

                    return;
                }

                --handlerCount;
                if (handlerCount <= 0)
                {
                    _ = handlers.Remove(messageHandler);
                }
                else
                {
                    handlers[messageHandler] = handlerCount;
                }
                _log.Log(new MessagingRegistration(handlerOwnerId, type, RegistrationType.Deregister, registrationMethod));
            };
        }

        private List<MessageHandler> GetOrAddMessageHandlerStack(IEnumerable<MessageHandler> handlers)
        {
            if (_messageHandlers.Count <= 0)
            {
                return new List<MessageHandler>(handlers);
            }

            List<MessageHandler> messageHandlers = _messageHandlers.Pop();
            messageHandlers.Clear();
            messageHandlers.AddRange(handlers);
            return messageHandlers;
        }
    }
}
