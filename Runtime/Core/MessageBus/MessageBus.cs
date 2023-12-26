namespace DxMessaging.Core.MessageBus
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using Messages;
    using static IMessageBus;

    /// <summary>
    /// Instanced MessageBus for use cases where you want distinct islands of MessageBuses.
    /// </summary>
    public sealed class MessageBus : IMessageBus
    {
        public int RegisteredTargeted => _targetedSinks.Select(kvp => kvp.Value.Count).Sum();
        public int RegisteredBroadcast => _broadcastSinks.Select(kvp => kvp.Value.Count).Sum();
        public int RegisteredUntargeted => _sinks.Select(kvp => kvp.Value.Count).Sum();

        private static readonly Type MessageBusType = typeof(MessageBus);
        // For use with re-broadcasting to generic methods
        private static readonly object[] ReflectionMethodArgumentsCache = new object[2];

        private const BindingFlags ReflectionHelperBindingFlags = BindingFlags.Static | BindingFlags.NonPublic;

        private delegate void FastUntargetedBroadcast<T>(ref T message) where T : IUntargetedMessage;
        private delegate void FastTargetedBroadcast<T>(ref InstanceId target, ref T message) where T : ITargetedMessage;
        private delegate void FastSourcedBroadcast<T>(ref InstanceId target, ref T message) where T : IBroadcastMessage;

        public RegistrationLog Log => _log;

        private readonly Dictionary<Type, Dictionary<MessageHandler, int>> _sinks = new();
        private readonly Dictionary<Type, Dictionary<InstanceId, Dictionary<MessageHandler, int>>> _targetedSinks = new();
        private readonly Dictionary<Type, Dictionary<InstanceId, Dictionary<MessageHandler, int>>> _broadcastSinks = new();
        private readonly Dictionary<Type, Dictionary<MessageHandler, int>> _postProcessingSinks = new();
        private readonly Dictionary<Type, Dictionary<InstanceId, Dictionary<MessageHandler, int>>> _postProcessingTargetedSinks = new();
        private readonly Dictionary<Type, Dictionary<InstanceId, Dictionary<MessageHandler, int>>> _postProcessingBroadcastSinks = new();
        private readonly Dictionary<MessageHandler, int> _globalSinks = new();
        private readonly Dictionary<Type, SortedDictionary<int, List<object>>> _interceptsByType = new();
        private readonly Dictionary<object, Dictionary<int, int>> _uniqueInterceptorsAndPriorities = new();

        private readonly Dictionary<Type, object> _broadcastMethodsByType = new();

        private readonly RegistrationLog _log = new();

        // These are used so we aren't allocating as much every time we send messages
        private readonly Stack<List<MessageHandler>> _messageHandlers = new();
        private readonly Stack<List<object>> _interceptors = new();
        private readonly Stack<List<int>> _interceptorKeys = new();

        /// <inheritdoc/>
        public Action RegisterUntargeted<T>(MessageHandler messageHandler) where T : IUntargetedMessage
        {
            return InternalRegisterUntargeted<T>(messageHandler, _sinks, RegistrationMethod.Untargeted);
        }

        /// <inheritdoc/>
        public Action RegisterTargeted<T>(InstanceId target, MessageHandler messageHandler) where T : ITargetedMessage
        {
            return InternalRegisterWithContext<T>(target, messageHandler, _targetedSinks, RegistrationMethod.Targeted);
        }

        /// <inheritdoc/>
        public Action RegisterSourcedBroadcast<T>(InstanceId source, MessageHandler messageHandler) where T : IBroadcastMessage
        {
            return InternalRegisterWithContext<T>(source, messageHandler, _broadcastSinks, RegistrationMethod.Broadcast);
        }

        /// <inheritdoc/>
        public Action RegisterSourcedBroadcastWithoutSource<T>(MessageHandler messageHandler) where T : IBroadcastMessage
        {
            return InternalRegisterUntargeted<T>(messageHandler, _sinks, RegistrationMethod.BroadcastWithoutSource);
        }

        /// <inheritdoc/>
        public Action RegisterTargetedWithoutTargeting<T>(MessageHandler messageHandler) where T : ITargetedMessage
        {
            return InternalRegisterUntargeted<T>(messageHandler, _sinks, RegistrationMethod.TargetedWithoutTargeting);
        }

        /// <inheritdoc/>
        public Action RegisterGlobalAcceptAll(MessageHandler messageHandler)
        {
            if (!_globalSinks.TryGetValue(messageHandler, out int count))
            {
                count = 0;
            }

            Type type = typeof(IMessage);
            _globalSinks[messageHandler] = count + 1;
            _log.Log(new MessagingRegistration(messageHandler.owner, type, RegistrationType.Register, RegistrationMethod.GlobalAcceptAll));

            return () =>
            {
                if (!_globalSinks.TryGetValue(messageHandler, out count))
                {
                    MessagingDebug.Log(
                        "Received over-deregistration of GlobalAcceptAll for MessageHandler {0}. Check to make sure you're not calling (de)registration multiple times.",
                        messageHandler);
                    return;
                }

                if (count <= 1)
                {
                    _ = _globalSinks.Remove(messageHandler);
                }
                else
                {
                    _globalSinks[messageHandler] = count - 1;
                }
                _log.Log(new MessagingRegistration(messageHandler.owner, type, RegistrationType.Deregister, RegistrationMethod.GlobalAcceptAll));
            };
        }

        /// <inheritdoc/>
        public Action RegisterUntargetedInterceptor<T>(UntargetedInterceptor<T> interceptor, int priority = 0) where T : IUntargetedMessage
        {
            return RegisterInterceptor<T>(interceptor, priority);
        }

        /// <inheritdoc/>
        public Action RegisterTargetedInterceptor<T>(TargetedInterceptor<T> interceptor, int priority = 0) where T : ITargetedMessage
        {
            return RegisterInterceptor<T>(interceptor, priority);
        }

        /// <inheritdoc/>
        public Action RegisterBroadcastInterceptor<T>(BroadcastInterceptor<T> interceptor, int priority = 0) where T : IBroadcastMessage
        {
            return RegisterInterceptor<T>(interceptor, priority);
        }

        /// <inheritdoc/>
        public Action RegisterUntargetedPostProcessor<T>(MessageHandler messageHandler) where T : IUntargetedMessage
        {
            return InternalRegisterUntargeted<T>(messageHandler, _postProcessingSinks, RegistrationMethod.UntargetedPostProcessor);
        }

        /// <inheritdoc/>
        public Action RegisterTargetedPostProcessor<T>(InstanceId target, MessageHandler messageHandler) where T : ITargetedMessage
        {
            return InternalRegisterWithContext<T>(target, messageHandler, _postProcessingTargetedSinks, RegistrationMethod.TargetedPostProcessor);
        }

        /// <inheritdoc/>
        public Action RegisterBroadcastPostProcessor<T>(InstanceId source, MessageHandler messageHandler) where T : IBroadcastMessage
        {
            return InternalRegisterWithContext<T>(source, messageHandler, _postProcessingBroadcastSinks, RegistrationMethod.BroadcastPostProcessor);
        }

        private Action RegisterInterceptor<T>(object interceptor, int priority) where T : IMessage
        {
            Type type = typeof(T);
            if (!_interceptsByType.TryGetValue(type, out SortedDictionary<int, List<object>> prioritizedInterceptors))
            {
                prioritizedInterceptors = new SortedDictionary<int, List<object>>();
                _interceptsByType[type] = prioritizedInterceptors;
            }

            if (!prioritizedInterceptors.TryGetValue(priority, out List<object> interceptors))
            {
                interceptors = new List<object>();
                prioritizedInterceptors[priority] = interceptors;
            }

            if (!_uniqueInterceptorsAndPriorities.TryGetValue(interceptor, out Dictionary<int, int> priorityCount))
            {
                priorityCount = new Dictionary<int, int>();
                _uniqueInterceptorsAndPriorities[interceptor] = priorityCount;
            }

            if (!priorityCount.TryGetValue(priority, out int count))
            {
                count = 0;
                interceptors.Add(interceptor);
            }

            priorityCount[priority] = count + 1;

            _log.Log(new MessagingRegistration(InstanceId.EmptyId, type, RegistrationType.Register, RegistrationMethod.Interceptor));

            return () =>
            {
                if (!priorityCount.TryGetValue(priority, out count))
                {
                    MessagingDebug.Log(
                        "Received over-deregistration of Interceptor {0}. Check to make sure you're not calling (de)registration multiple times.",
                        interceptor);
                    return;
                }

                if (count <= 1)
                {
                    _ = priorityCount.Remove(priority);
                    if (priorityCount.Count <= 0)
                    {
                        _ = _uniqueInterceptorsAndPriorities.Remove(interceptor);
                    }

                    _ = interceptors.Remove(interceptor);
                    if (interceptors.Count <= 0)
                    {
                        _ = prioritizedInterceptors.Remove(priority);
                        if (priorityCount.Count <= 0)
                        {
                            _ = _interceptsByType.Remove(type);
                        }
                    }
                }

                _log.Log(new MessagingRegistration(InstanceId.EmptyId, type, RegistrationType.Deregister, RegistrationMethod.Interceptor));
            };
        }

        /// <inheritdoc/>
        public void UntypedUntargetedBroadcast(IUntargetedMessage typedMessage)
        {
            Type messageType = typedMessage.MessageType;
            if (!_broadcastMethodsByType.TryGetValue(messageType, out object untargetedMethod))
            {
                // ReSharper disable once PossibleNullReferenceException
                MethodInfo broadcastMethod = MessageBusType.GetMethod(nameof(UntargetedBroadcast)).MakeGenericMethod(messageType);
                // ReSharper disable once PossibleNullReferenceException
                MethodInfo helperMethod = MessageBusType.GetMethod(nameof(UntargetedBroadcastReflectionHelper), ReflectionHelperBindingFlags).MakeGenericMethod(messageType);

                ReflectionMethodArgumentsCache[0] = this;
                ReflectionMethodArgumentsCache[1] = broadcastMethod;
                untargetedMethod = helperMethod.Invoke(null, ReflectionMethodArgumentsCache);
                _broadcastMethodsByType[messageType] = untargetedMethod;
            }

            Action<IUntargetedMessage> broadcast = (Action<IUntargetedMessage>)untargetedMethod;
            broadcast.Invoke(typedMessage);
        }

        /// <inheritdoc/>
        public void UntargetedBroadcast<TMessage>(ref TMessage typedMessage) where TMessage : IUntargetedMessage
        {
            InternalUntargetedBroadcast(ref typedMessage);
        }

        private void InternalUntargetedBroadcast<TMessage>(ref TMessage typedMessage) where TMessage : IUntargetedMessage
        {
            Type type = typedMessage.MessageType;
            if (!RunUntargetedInterceptors(type, ref typedMessage))
            {
                return;
            }

            if (0 < _globalSinks.Count)
            {
                IUntargetedMessage untargetedMessage = typedMessage;
                BroadcastGlobalUntargeted(ref untargetedMessage);
            }

            bool foundAnyHandlers = InternalUntargetedBroadcast(ref typedMessage, type);

            if (_postProcessingSinks.TryGetValue(type, out Dictionary<MessageHandler, int> handlers) && 0 < handlers.Count)
            {
                foundAnyHandlers = true;
                List<MessageHandler> messageHandlers = GetOrAddMessageHandlerStack(handlers.Keys);
                try
                {
                    foreach (MessageHandler handler in messageHandlers)
                    {
                        handler.HandleUntargetedPostProcessing(ref typedMessage, this);
                    }
                }
                finally
                {
                    _messageHandlers.Push(messageHandlers);
                }
            }

            if (!foundAnyHandlers)
            {
                MessagingDebug.Log("Could not find a matching untargeted broadcast handler for Message: {0}.", typedMessage);
            }
        }

        /// <inheritdoc/>
        public void UntypedTargetedBroadcast(InstanceId target, ITargetedMessage typedMessage)
        {
            Type messageType = typedMessage.MessageType;
            if (!_broadcastMethodsByType.TryGetValue(messageType, out object targetedMethod))
            {
                // ReSharper disable once PossibleNullReferenceException
                MethodInfo broadcastMethod = MessageBusType.GetMethod(nameof(TargetedBroadcast)).MakeGenericMethod(messageType);
                // ReSharper disable once PossibleNullReferenceException
                MethodInfo helperMethod = MessageBusType.GetMethod(nameof(TargetedBroadcastReflectionHelper), ReflectionHelperBindingFlags).MakeGenericMethod(messageType);

                ReflectionMethodArgumentsCache[0] = this;
                ReflectionMethodArgumentsCache[1] = broadcastMethod;
                targetedMethod = helperMethod.Invoke(null, ReflectionMethodArgumentsCache);
                _broadcastMethodsByType[messageType] = targetedMethod;
            }

            Action<InstanceId, ITargetedMessage> broadcast = (Action<InstanceId, ITargetedMessage>)targetedMethod;
            broadcast.Invoke(target, typedMessage);
        }

        /// <inheritdoc/>
        public void TargetedBroadcast<TMessage>(ref InstanceId target, ref TMessage typedMessage) where TMessage : ITargetedMessage
        {
            InternalTargetedBroadcast(ref target, ref typedMessage);
        }

        private void InternalTargetedBroadcast<TMessage>(ref InstanceId target, ref TMessage typedMessage)
            where TMessage : ITargetedMessage
        {
            Type type = typedMessage.MessageType;
            if (!RunTargetedInterceptors(type, ref typedMessage, ref target))
            {
                return;
            }

            if (_globalSinks.Count < 0)
            {
                ITargetedMessage targetedMessage = typedMessage;
                BroadcastGlobalTargeted(ref target, ref targetedMessage);
            }

            bool foundAnyHandlers = false;
            if (_targetedSinks.TryGetValue(type, out Dictionary<InstanceId, Dictionary<MessageHandler, int>> targetedHandlers) 
                && targetedHandlers.TryGetValue(target, out Dictionary<MessageHandler, int> handlers) 
                && 0 < handlers.Count)
            {
                foundAnyHandlers = true;
                List<MessageHandler> messageHandlers = GetOrAddMessageHandlerStack(handlers.Keys);
                try
                {
                    foreach (MessageHandler handler in messageHandlers)
                    {
                        handler.HandleTargeted(ref target, ref typedMessage, this);
                    }
                }
                finally
                {
                    _messageHandlers.Push(messageHandlers);
                }
            }

            if (InternalTargetedWithoutTargetingBroadcast(ref target, ref typedMessage, type))
            {
                foundAnyHandlers = true;
            }

            if (_postProcessingTargetedSinks.TryGetValue(type, out targetedHandlers) && targetedHandlers.TryGetValue(target, out handlers) && 0 < handlers.Count)
            {
                foundAnyHandlers = true;
                List<MessageHandler> messageHandlers = GetOrAddMessageHandlerStack(handlers.Keys);
                try
                {
                    foreach (MessageHandler handler in messageHandlers)
                    {
                        handler.HandleTargetedPostProcessing(ref target, ref typedMessage, this);
                    }
                }
                finally
                {
                    _messageHandlers.Push(messageHandlers);
                }
            }

            if (!foundAnyHandlers)
            {
                MessagingDebug.Log("Could not find a matching targeted broadcast handler for Id: {0}, Message: {1}.", target,
                    typedMessage);
            }
        }

        /// <inheritdoc/>
        public void UntypedSourcedBroadcast(InstanceId source, IBroadcastMessage typedMessage)
        {
            Type messageType = typedMessage.MessageType;
            if (!_broadcastMethodsByType.TryGetValue(messageType, out object sourcedBroadcastMethod))
            {
                // ReSharper disable once PossibleNullReferenceException
                MethodInfo broadcastMethod = MessageBusType.GetMethod(nameof(SourcedBroadcast)).MakeGenericMethod(messageType);
                // ReSharper disable once PossibleNullReferenceException
                MethodInfo helperMethod = MessageBusType.GetMethod(nameof(SourcedBroadcastReflectionHelper), ReflectionHelperBindingFlags).MakeGenericMethod(messageType);
                
                ReflectionMethodArgumentsCache[0] = this;
                ReflectionMethodArgumentsCache[1] = broadcastMethod;
                sourcedBroadcastMethod = helperMethod.Invoke(null, ReflectionMethodArgumentsCache);
                
                _broadcastMethodsByType[messageType] = sourcedBroadcastMethod;
            }

            Action<InstanceId, IBroadcastMessage> broadcast = (Action<InstanceId, IBroadcastMessage>)sourcedBroadcastMethod;
            broadcast.Invoke(source, typedMessage);
        }

        /// <inheritdoc/>
        public void SourcedBroadcast<TMessage>(ref InstanceId source, ref TMessage typedMessage) where TMessage : IBroadcastMessage
        {
            InternalSourcedBroadcast(ref source, ref typedMessage);
        }

        private void InternalSourcedBroadcast<TMessage>(ref InstanceId source, ref TMessage typedMessage) where TMessage : IBroadcastMessage
        {
            Type type = typedMessage.MessageType;
            if (!RunBroadcastInterceptors(type, ref typedMessage, ref source))
            {
                return;
            }

            bool foundAnyHandlers = false;

            if (0 < _globalSinks.Count)
            {
                IBroadcastMessage broadcastMessage = typedMessage;
                BroadcastGlobalSourcedBroadcast(ref source, ref broadcastMessage);
            }

            if (_broadcastSinks.TryGetValue(type, out Dictionary<InstanceId, Dictionary<MessageHandler, int>> broadcastHandlers) 
                && broadcastHandlers.TryGetValue(source, out Dictionary<MessageHandler, int> handlers) 
                && 0 < handlers.Count)
            {
                foundAnyHandlers = true;
                List<MessageHandler> messageHandlers = GetOrAddMessageHandlerStack(handlers.Keys);
                try
                {
                    foreach (MessageHandler handler in messageHandlers)
                    {
                        handler.HandleSourcedBroadcast(ref source, ref typedMessage, this);
                    }
                }
                finally
                {
                    _messageHandlers.Push(messageHandlers);
                }
            }

            if (InternalBroadcastWithoutSource(ref source, ref typedMessage, type))
            {
                foundAnyHandlers = true;
            }

            if (_postProcessingBroadcastSinks.TryGetValue(type, out broadcastHandlers) && broadcastHandlers.TryGetValue(source, out handlers) && 0 < handlers.Count)
            {
                foundAnyHandlers = true;
                List<MessageHandler> messageHandlers = GetOrAddMessageHandlerStack(handlers.Keys);
                try
                {
                    foreach (MessageHandler handler in messageHandlers)
                    {
                        handler.HandleSourcedBroadcastPostProcessing(ref source, ref typedMessage, this);
                    }
                }
                finally
                {
                    _messageHandlers.Push(messageHandlers);
                }
            }

            if (!foundAnyHandlers)
            {
                MessagingDebug.Log("Could not find a matching sourced broadcast handler for Id: {0}, Message: {1}.",
                    source, typedMessage);
            }
        }

        private void BroadcastGlobalUntargeted(ref IUntargetedMessage message)
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
                    handler.HandleGlobalUntargetedMessage(ref message, this);
                }
            }
            finally
            {
                _messageHandlers.Push(messageHandlers);
            }
        }

        private void BroadcastGlobalTargeted(ref InstanceId target, ref ITargetedMessage message)
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
                    handler.HandleGlobalTargetedMessage(ref target, ref message, this);
                }
            }
            finally
            {
                _messageHandlers.Push(messageHandlers);
            }
        }

        private void BroadcastGlobalSourcedBroadcast(ref InstanceId source, ref IBroadcastMessage message)
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
                    handler.HandleGlobalSourcedBroadcastMessage(ref source, ref message, this);
                }
            }
            finally
            {
                _messageHandlers.Push(messageHandlers);
            }
        }

        private bool TryGetInterceptorCaches(Type type, out SortedDictionary<int, List<object>> interceptors, out List<int> interceptorKeys, out List<object> interceptorStack)
        {
            if (!_interceptsByType.TryGetValue(type, out interceptors) || interceptors.Count <= 0)
            {
                interceptorKeys = default;
                interceptorStack = default;
                return false;
            }

            if (_interceptors.Count <= 0)
            {
                interceptorStack = new List<object>();
            }
            else
            {
                interceptorStack = _interceptors.Pop();
                interceptorStack.Clear();
            }

            if (_interceptorKeys.Count <= 0)
            {
                interceptorKeys = new List<int>();
            }
            else
            {
                interceptorKeys = _interceptorKeys.Pop();
                interceptorKeys.Clear();
            }

            return true;
        }

        private bool RunUntargetedInterceptors<T>(Type type, ref T message) where T : IUntargetedMessage
        {
            if (!TryGetInterceptorCaches(
                    type, 
                    out SortedDictionary<int, List<object>> interceptors, 
                    out List<int> interceptorKeys,
                    out List<object> interceptorStack))
            {
                return true;
            }

            try
            {
                interceptorKeys.AddRange(interceptors.Keys);
                foreach (int priority in interceptorKeys)
                {
                    if (!interceptors.TryGetValue(priority, out List<object> untypedInterceptors) || untypedInterceptors.Count <= 0)
                    {
                        continue;
                    }

                    interceptorStack.Clear();
                    interceptorStack.AddRange(untypedInterceptors);

                    foreach (object transformer in interceptorStack)
                    {
                        if (transformer is not UntargetedInterceptor<T> typedTransformer)
                        {
                            continue;
                        }

                        if (!typedTransformer(ref message))
                        {
                            return false;
                        }
                    }
                }
            }
            finally
            {
                _interceptors.Push(interceptorStack);
                _interceptorKeys.Push(interceptorKeys);
            }

            return true;
        }

        private bool RunTargetedInterceptors<T>(Type type, ref T message, ref InstanceId target) where T : ITargetedMessage
        {
            if (!TryGetInterceptorCaches(
                    type,
                    out SortedDictionary<int, List<object>> interceptors,
                    out List<int> interceptorKeys,
                    out List<object> interceptorStack))
            {
                return true;
            }

            try
            {
                interceptorKeys.AddRange(interceptors.Keys);
                foreach (int priority in interceptorKeys)
                {
                    if (!interceptors.TryGetValue(priority, out List<object> untypedInterceptors) || untypedInterceptors.Count <= 0)
                    {
                        continue;
                    }

                    interceptorStack.Clear();
                    interceptorStack.AddRange(untypedInterceptors);

                    foreach (object transformer in interceptorStack)
                    {
                        if (transformer is not TargetedInterceptor<T> typedTransformer)
                        {
                            continue;
                        }

                        if (!typedTransformer(ref target, ref message))
                        {
                            return false;
                        }
                    }
                }
            }
            finally
            {
                _interceptors.Push(interceptorStack);
                _interceptorKeys.Push(interceptorKeys);
            }

            return true;
        }

        private bool RunBroadcastInterceptors<T>(Type type, ref T message, ref InstanceId source) where T : IBroadcastMessage
        {
            if (!TryGetInterceptorCaches(
                    type,
                    out SortedDictionary<int, List<object>> interceptors,
                    out List<int> interceptorKeys,
                    out List<object> interceptorStack))
            {
                return true;
            }

            try
            {
                interceptorKeys.AddRange(interceptors.Keys);
                foreach (int priority in interceptorKeys)
                {
                    if (!interceptors.TryGetValue(priority, out List<object> untypedInterceptors) || untypedInterceptors.Count <= 0)
                    {
                        continue;
                    }

                    interceptorStack.Clear();
                    interceptorStack.AddRange(untypedInterceptors);

                    foreach (object transformer in interceptorStack)
                    {
                        if (transformer is not BroadcastInterceptor<T> typedTransformer)
                        {
                            continue;
                        }

                        if (!typedTransformer(ref source, ref message))
                        {
                            return false;
                        }
                    }
                }
            }
            finally
            {
                _interceptors.Push(interceptorStack);
                _interceptorKeys.Push(interceptorKeys);
            }

            return true;
        }

        private bool InternalUntargetedBroadcast<TMessage>(ref TMessage message, Type type) where TMessage : IMessage
        {
            if (!_sinks.TryGetValue(type, out Dictionary<MessageHandler, int> handlers) || handlers.Count <= 0)
            {
                return false;
            }

            List<MessageHandler> messageHandlers = GetOrAddMessageHandlerStack(handlers.Keys);
            try
            {
                foreach (MessageHandler handler in messageHandlers)
                {
                    handler.HandleUntargetedMessage(ref message, this);
                }
            }
            finally
            {
                _messageHandlers.Push(messageHandlers);
            }

            return true;
        }

        private bool InternalTargetedWithoutTargetingBroadcast<TMessage>(
            ref InstanceId target, ref TMessage message, Type type) where TMessage : ITargetedMessage
        {
            if (!_sinks.TryGetValue(type, out Dictionary<MessageHandler, int> handlers) || handlers.Count <= 0)
            {
                return false;
            }

            List<MessageHandler> messageHandlers = GetOrAddMessageHandlerStack(handlers.Keys);
            try
            {
                foreach (MessageHandler handler in messageHandlers)
                {
                    handler.HandleTargetedWithoutTargeting(ref target, ref message, this);
                }
            }
            finally
            {
                _messageHandlers.Push(messageHandlers);
            }

            return true;
        }

        private bool InternalBroadcastWithoutSource<TMessage>(
            ref InstanceId target, ref TMessage message, Type type) where TMessage : IBroadcastMessage
        {
            if (!_sinks.TryGetValue(type, out Dictionary<MessageHandler, int> handlers) || handlers.Count <= 0)
            {
                return false;
            }

            List<MessageHandler> messageHandlers = GetOrAddMessageHandlerStack(handlers.Keys);
            try
            {
                foreach (MessageHandler handler in messageHandlers)
                {
                    handler.HandleSourcedBroadcastWithoutSource(ref target, ref message, this);
                }
            }
            finally
            {
                _messageHandlers.Push(messageHandlers);
            }

            return true;
        }

        private Action InternalRegisterUntargeted<T>(MessageHandler messageHandler, Dictionary<Type, Dictionary<MessageHandler, int>> sinks, RegistrationMethod registrationMethod) where T : IMessage
        {
            if (messageHandler == null)
            {
                throw new ArgumentNullException(nameof(messageHandler));
            }

            InstanceId handlerOwnerId = messageHandler.owner;
            Type type = typeof(T);

            if (!sinks.TryGetValue(type, out Dictionary<MessageHandler, int> handlers))
            {
                handlers = new Dictionary<MessageHandler, int>();
                sinks[type] = handlers;
            }

            if (!handlers.TryGetValue(messageHandler, out int count))
            {
                count = 0;
            }

            handlers[messageHandler] = count + 1;
            _log.Log(new MessagingRegistration(handlerOwnerId, type, RegistrationType.Register, registrationMethod));

            return () =>
            {
                if (!sinks.TryGetValue(type, out handlers) || !handlers.TryGetValue(messageHandler, out count))
                {
                    MessagingDebug.Log(
                        "Received over-deregistration of {0} for {1}. Check to make sure you're not calling (de)registration multiple times.",
                        type, messageHandler);

                    return;
                }

                if (count <= 1)
                {
                    _ = handlers.Remove(messageHandler);
                    if (handlers.Count <= 0)
                    {
                        _ = sinks.Remove(type);
                    }
                }
                else
                {
                    handlers[messageHandler] = count - 1;
                }

                _log.Log(new MessagingRegistration(handlerOwnerId, type, RegistrationType.Deregister, registrationMethod));
            };
        }

        private Action InternalRegisterWithContext<T>(InstanceId context, MessageHandler messageHandler, Dictionary<Type, Dictionary<InstanceId, Dictionary<MessageHandler, int>>> sinks, RegistrationMethod registrationMethod)
        {
            if (messageHandler == null)
            {
                throw new ArgumentNullException(nameof(messageHandler));
            }

            Type type = typeof(T);
            if (!sinks.TryGetValue(type, out Dictionary<InstanceId, Dictionary<MessageHandler, int>> broadcastHandlers))
            {
                broadcastHandlers = new Dictionary<InstanceId, Dictionary<MessageHandler, int>>();
                sinks[type] = broadcastHandlers;
            }

            if (!broadcastHandlers.TryGetValue(context, out Dictionary<MessageHandler, int> handlers))
            {
                handlers = new Dictionary<MessageHandler, int>();
                broadcastHandlers[context] = handlers;
            }

            if (!handlers.TryGetValue(messageHandler, out int count))
            {
                count = 0;
            }

            handlers[messageHandler] = count + 1;
            _log.Log(new MessagingRegistration(context, type, RegistrationType.Register, registrationMethod));

            return () =>
            {
                if (!sinks.TryGetValue(type, out broadcastHandlers) || !broadcastHandlers.TryGetValue(context, out handlers) || !handlers.TryGetValue(messageHandler, out count))
                {
                    MessagingDebug.Log(
                        "Received over-deregistration of {0} for {1}. Check to make sure you're not calling (de)registration multiple times.",
                        type, messageHandler);
                    return;
                }

                if (count <= 1)
                {
                    _ = handlers.Remove(messageHandler);
                    if (handlers.Count <= 0)
                    {
                        _ = broadcastHandlers.Remove(context);
                    }

                    if (broadcastHandlers.Count <= 0)
                    {
                        _ = sinks.Remove(type);
                    }
                }
                else
                {
                    handlers[messageHandler] = count - 1;
                }

                _log.Log(new MessagingRegistration(context, type, RegistrationType.Deregister, registrationMethod));
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

        // https://blogs.msmvps.com/jonskeet/2008/08/09/making-reflection-fly-and-exploring-delegates/
        private static Action<IUntargetedMessage> UntargetedBroadcastReflectionHelper<T>(IMessageBus messageBus, MethodInfo methodInfo) where T : IUntargetedMessage
        {
            FastUntargetedBroadcast<T> untargetedBroadcast = (FastUntargetedBroadcast<T>) Delegate.CreateDelegate(typeof(FastUntargetedBroadcast<T>), messageBus, methodInfo);
            void UntypedBroadcast(IUntargetedMessage message)
            {
                T typedMessage = (T) message;
                untargetedBroadcast(ref typedMessage);
            }

            return UntypedBroadcast;
        }

        private static Action<InstanceId, ITargetedMessage> TargetedBroadcastReflectionHelper<T>(IMessageBus messageBus, MethodInfo methodInfo) where T : ITargetedMessage
        {
            FastTargetedBroadcast<T> targetedBroadcast = (FastTargetedBroadcast<T>) Delegate.CreateDelegate(typeof(FastTargetedBroadcast<T>), messageBus, methodInfo);
            void UntypedBroadcast(InstanceId target, ITargetedMessage message)
            {
                T typedMessage = (T) message;
                targetedBroadcast(ref target, ref typedMessage);
            }

            return UntypedBroadcast;
        }

        private static Action<InstanceId, IBroadcastMessage> SourcedBroadcastReflectionHelper<T>(IMessageBus messageBus, MethodInfo methodInfo) where T : IBroadcastMessage
        {
            FastSourcedBroadcast<T> sourcedBroadcast = (FastSourcedBroadcast<T>)Delegate.CreateDelegate(typeof(FastSourcedBroadcast<T>), messageBus, methodInfo);
            void UntypedBroadcast(InstanceId target, IBroadcastMessage message)
            {
                T typedMessage = (T)message;
                sourcedBroadcast(ref target, ref typedMessage);
            }

            return UntypedBroadcast;
        }
    }
}
