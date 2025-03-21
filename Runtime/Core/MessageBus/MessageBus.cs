namespace DxMessaging.Core.MessageBus
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using Messages;
    using static IMessageBus;

    /// <summary>
    /// Instanced MessageBus for use cases where you want distinct islands of MessageBuses.
    /// </summary>
    public sealed class MessageBus : IMessageBus
    {
        public int RegisteredTargeted
        {
            get
            {
                int count = 0;
                foreach (
                    KeyValuePair<
                        Type,
                        Dictionary<InstanceId, SortedList<int, SortedList<MessageHandler, int>>>
                    > entry in _targetedSinks
                )
                {
                    count += entry.Value.Count;
                }

                return count;
            }
        }

        public int RegisteredBroadcast
        {
            get
            {
                int count = 0;
                foreach (var entry in _broadcastSinks)
                {
                    count += entry.Value.Count;
                }

                return count;
            }
        }

        public int RegisteredUntargeted
        {
            get
            {
                int count = 0;
                foreach (var entry in _sinks)
                {
                    count += entry.Value.Count;
                }

                return count;
            }
        }

        private static readonly Type MessageBusType = typeof(MessageBus);

        // For use with re-broadcasting to generic methods
        private static readonly object[] ReflectionMethodArgumentsCache = new object[2];

        private const BindingFlags ReflectionHelperBindingFlags =
            BindingFlags.Static | BindingFlags.NonPublic;

        private delegate void FastUntargetedBroadcast<T>(ref T message)
            where T : IUntargetedMessage;
        private delegate void FastTargetedBroadcast<T>(ref InstanceId target, ref T message)
            where T : ITargetedMessage;
        private delegate void FastSourcedBroadcast<T>(ref InstanceId target, ref T message)
            where T : IBroadcastMessage;

        public RegistrationLog Log => _log;

        private readonly Dictionary<Type, SortedList<int, SortedList<MessageHandler, int>>> _sinks =
            new();
        private readonly Dictionary<
            Type,
            Dictionary<InstanceId, SortedList<int, SortedList<MessageHandler, int>>>
        > _targetedSinks = new();

        private readonly Dictionary<
            Type,
            Dictionary<InstanceId, SortedList<int, SortedList<MessageHandler, int>>>
        > _broadcastSinks = new();
        private readonly Dictionary<
            Type,
            SortedList<int, SortedList<MessageHandler, int>>
        > _postProcessingSinks = new();
        private readonly Dictionary<
            Type,
            Dictionary<InstanceId, SortedList<int, SortedList<MessageHandler, int>>>
        > _postProcessingTargetedSinks = new();
        private readonly Dictionary<
            Type,
            Dictionary<InstanceId, SortedList<int, SortedList<MessageHandler, int>>>
        > _postProcessingBroadcastSinks = new();
        private readonly Dictionary<
            Type,
            SortedList<int, SortedList<MessageHandler, int>>
        > _postProcessingTargetedWithoutTargetingSinks = new();
        private readonly Dictionary<
            Type,
            SortedList<int, SortedList<MessageHandler, int>>
        > _postProcessingBroadcastWithoutSourceSinks = new();
        private readonly SortedList<MessageHandler, int> _globalSinks = new();
        private readonly Dictionary<Type, SortedList<int, List<object>>> _interceptsByType = new();
        private readonly Dictionary<object, Dictionary<int, int>> _uniqueInterceptorsAndPriorities =
            new();

        private readonly Dictionary<Type, object> _broadcastMethodsByType = new();

        private readonly RegistrationLog _log = new();

        // These are used so we aren't allocating as much every time we send messages
        private readonly Stack<List<MessageHandler>> _messageHandlers = new();
        private readonly Stack<
            List<KeyValuePair<int, SortedList<MessageHandler, int>>>
        > _sortedHandlers = new();
        private readonly Stack<List<List<object>>> _interceptors = new();
        private readonly Stack<List<object>> _innerInterceptorsStack = new();

        public Action RegisterUntargeted<T>(MessageHandler messageHandler, int priority = 0)
            where T : IUntargetedMessage
        {
            return InternalRegisterUntargeted<T>(
                messageHandler,
                _sinks,
                RegistrationMethod.Untargeted,
                priority
            );
        }

        public Action RegisterTargeted<T>(
            InstanceId target,
            MessageHandler messageHandler,
            int priority = 0
        )
            where T : ITargetedMessage
        {
            return InternalRegisterWithContext<T>(
                target,
                messageHandler,
                _targetedSinks,
                RegistrationMethod.Targeted,
                priority
            );
        }

        public Action RegisterSourcedBroadcast<T>(
            InstanceId source,
            MessageHandler messageHandler,
            int priority = 0
        )
            where T : IBroadcastMessage
        {
            return InternalRegisterWithContext<T>(
                source,
                messageHandler,
                _broadcastSinks,
                RegistrationMethod.Broadcast,
                priority
            );
        }

        public Action RegisterSourcedBroadcastWithoutSource<T>(
            MessageHandler messageHandler,
            int priority = 0
        )
            where T : IBroadcastMessage
        {
            return InternalRegisterUntargeted<T>(
                messageHandler,
                _sinks,
                RegistrationMethod.BroadcastWithoutSource,
                priority
            );
        }

        public Action RegisterTargetedWithoutTargeting<T>(
            MessageHandler messageHandler,
            int priority = 0
        )
            where T : ITargetedMessage
        {
            return InternalRegisterUntargeted<T>(
                messageHandler,
                _sinks,
                RegistrationMethod.TargetedWithoutTargeting,
                priority
            );
        }

        public Action RegisterGlobalAcceptAll(MessageHandler messageHandler)
        {
            int count = _globalSinks.GetValueOrDefault(messageHandler, 0);

            Type type = typeof(IMessage);
            _globalSinks[messageHandler] = count + 1;
            _log.Log(
                new MessagingRegistration(
                    messageHandler.owner,
                    type,
                    RegistrationType.Register,
                    RegistrationMethod.GlobalAcceptAll
                )
            );

            return () =>
            {
                _log.Log(
                    new MessagingRegistration(
                        messageHandler.owner,
                        type,
                        RegistrationType.Deregister,
                        RegistrationMethod.GlobalAcceptAll
                    )
                );
                if (!_globalSinks.TryGetValue(messageHandler, out count))
                {
                    if (MessagingDebug.enabled)
                    {
                        MessagingDebug.Log(
                            LogLevel.Error,
                            "Received over-deregistration of GlobalAcceptAll for MessageHandler {0}. Check to make sure you're not calling (de)registration multiple times.",
                            messageHandler
                        );
                    }

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
            };
        }

        public Action RegisterUntargetedInterceptor<T>(
            UntargetedInterceptor<T> interceptor,
            int priority = 0
        )
            where T : IUntargetedMessage
        {
            return RegisterInterceptor<T>(interceptor, priority);
        }

        public Action RegisterTargetedInterceptor<T>(
            TargetedInterceptor<T> interceptor,
            int priority = 0
        )
            where T : ITargetedMessage
        {
            return RegisterInterceptor<T>(interceptor, priority);
        }

        public Action RegisterBroadcastInterceptor<T>(
            BroadcastInterceptor<T> interceptor,
            int priority = 0
        )
            where T : IBroadcastMessage
        {
            return RegisterInterceptor<T>(interceptor, priority);
        }

        public Action RegisterUntargetedPostProcessor<T>(
            MessageHandler messageHandler,
            int priority = 0
        )
            where T : IUntargetedMessage
        {
            return InternalRegisterUntargeted<T>(
                messageHandler,
                _postProcessingSinks,
                RegistrationMethod.UntargetedPostProcessor,
                priority
            );
        }

        public Action RegisterTargetedPostProcessor<T>(
            InstanceId target,
            MessageHandler messageHandler,
            int priority = 0
        )
            where T : ITargetedMessage
        {
            return InternalRegisterWithContext<T>(
                target,
                messageHandler,
                _postProcessingTargetedSinks,
                RegistrationMethod.TargetedPostProcessor,
                priority
            );
        }

        public Action RegisterTargetedWithoutTargetingPostProcessor<T>(
            MessageHandler messageHandler,
            int priority = 0
        )
            where T : ITargetedMessage
        {
            return InternalRegisterUntargeted<T>(
                messageHandler,
                _postProcessingTargetedWithoutTargetingSinks,
                RegistrationMethod.TargetedWithoutTargetingPostProcessor,
                priority
            );
        }

        public Action RegisterBroadcastPostProcessor<T>(
            InstanceId source,
            MessageHandler messageHandler,
            int priority = 0
        )
            where T : IBroadcastMessage
        {
            return InternalRegisterWithContext<T>(
                source,
                messageHandler,
                _postProcessingBroadcastSinks,
                RegistrationMethod.BroadcastPostProcessor,
                priority
            );
        }

        public Action RegisterBroadcastWithoutSourcePostProcessor<T>(
            MessageHandler messageHandler,
            int priority = 0
        )
            where T : IBroadcastMessage
        {
            return InternalRegisterUntargeted<T>(
                messageHandler,
                _postProcessingBroadcastWithoutSourceSinks,
                RegistrationMethod.BroadcastWithoutSourcePostProcessor,
                priority
            );
        }

        private Action RegisterInterceptor<T>(object interceptor, int priority)
            where T : IMessage
        {
            Type type = typeof(T);
            if (
                !_interceptsByType.TryGetValue(
                    type,
                    out SortedList<int, List<object>> prioritizedInterceptors
                )
            )
            {
                prioritizedInterceptors = new SortedList<int, List<object>>();
                _interceptsByType[type] = prioritizedInterceptors;
            }

            if (!prioritizedInterceptors.TryGetValue(priority, out List<object> interceptors))
            {
                interceptors = new List<object>();
                prioritizedInterceptors[priority] = interceptors;
            }

            if (
                !_uniqueInterceptorsAndPriorities.TryGetValue(
                    interceptor,
                    out Dictionary<int, int> priorityCount
                )
            )
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

            _log.Log(
                new MessagingRegistration(
                    InstanceId.EmptyId,
                    type,
                    RegistrationType.Register,
                    RegistrationMethod.Interceptor
                )
            );

            return () =>
            {
                _log.Log(
                    new MessagingRegistration(
                        InstanceId.EmptyId,
                        type,
                        RegistrationType.Deregister,
                        RegistrationMethod.Interceptor
                    )
                );
                bool removed = false;
                if (_uniqueInterceptorsAndPriorities.TryGetValue(interceptor, out priorityCount))
                {
                    if (priorityCount.TryGetValue(priority, out count))
                    {
                        if (1 < count)
                        {
                            priorityCount[priority] = count - 1;
                        }
                        else
                        {
                            removed = true;
                            _ = priorityCount.Remove(priority);
                        }
                    }

                    if (priorityCount.Count <= 0)
                    {
                        _uniqueInterceptorsAndPriorities.Remove(interceptor);
                    }
                }
                else if (MessagingDebug.enabled)
                {
                    MessagingDebug.Log(
                        LogLevel.Error,
                        "Received over-deregistration of Interceptor {0}. Check to make sure you're not calling (de)registration multiple times.",
                        interceptor
                    );
                }

                bool complete = false;
                if (removed)
                {
                    if (
                        _interceptsByType.TryGetValue(type, out prioritizedInterceptors)
                        && prioritizedInterceptors.TryGetValue(priority, out interceptors)
                    )
                    {
                        complete = interceptors.Remove(interceptor);
                    }

                    if (!complete && MessagingDebug.enabled)
                    {
                        MessagingDebug.Log(
                            LogLevel.Error,
                            "Received over-deregistration of Interceptor {0}. Check to make sure you're not calling (de)registration multiple times.",
                            interceptor
                        );
                    }
                }
            };
        }

        public void UntypedUntargetedBroadcast(IUntargetedMessage typedMessage)
        {
            Type messageType = typedMessage.MessageType;
            if (!_broadcastMethodsByType.TryGetValue(messageType, out object untargetedMethod))
            {
                // ReSharper disable once PossibleNullReferenceException
                MethodInfo broadcastMethod = MessageBusType
                    .GetMethod(nameof(UntargetedBroadcast))
                    .MakeGenericMethod(messageType);
                // ReSharper disable once PossibleNullReferenceException
                MethodInfo helperMethod = MessageBusType
                    .GetMethod(
                        nameof(UntargetedBroadcastReflectionHelper),
                        ReflectionHelperBindingFlags
                    )
                    .MakeGenericMethod(messageType);

                ReflectionMethodArgumentsCache[0] = this;
                ReflectionMethodArgumentsCache[1] = broadcastMethod;
                untargetedMethod = helperMethod.Invoke(null, ReflectionMethodArgumentsCache);
                _broadcastMethodsByType[messageType] = untargetedMethod;
            }

            Action<IUntargetedMessage> broadcast = (Action<IUntargetedMessage>)untargetedMethod;
            broadcast.Invoke(typedMessage);
        }

        public void UntargetedBroadcast<TMessage>(ref TMessage typedMessage)
            where TMessage : IUntargetedMessage
        {
            Type type = typeof(TMessage);
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

            if (
                _postProcessingSinks.TryGetValue(
                    type,
                    out SortedList<int, SortedList<MessageHandler, int>> sortedHandlers
                )
                && 0 < sortedHandlers.Count
            )
            {
                foundAnyHandlers = true;
                if (sortedHandlers.Count == 1)
                {
                    RunUntargetedPostProcessing(
                        ref typedMessage,
                        sortedHandlers.Keys[0],
                        sortedHandlers.Values[0]
                    );
                }
                else
                {
                    List<KeyValuePair<int, SortedList<MessageHandler, int>>> handlerList =
                        GetOrAddMessageHandlerStack(sortedHandlers);
                    try
                    {
                        foreach (
                            KeyValuePair<int, SortedList<MessageHandler, int>> entry in handlerList
                        )
                        {
                            RunUntargetedPostProcessing(ref typedMessage, entry.Key, entry.Value);
                        }
                    }
                    finally
                    {
                        _sortedHandlers.Push(handlerList);
                    }
                }
            }

            if (!foundAnyHandlers && MessagingDebug.enabled)
            {
                MessagingDebug.Log(
                    LogLevel.Info,
                    "Could not find a matching untargeted broadcast handler for Message: {0}.",
                    typedMessage
                );
            }
        }

        private void RunUntargetedPostProcessing<TMessage>(
            ref TMessage typedMessage,
            int priority,
            SortedList<MessageHandler, int> handlers
        )
            where TMessage : IUntargetedMessage
        {
            switch (handlers.Count)
            {
                case <= 0:
                {
                    return;
                }
                case 1:
                {
                    MessageHandler handler = handlers.Keys[0];
                    handler.HandleUntargetedPostProcessing(ref typedMessage, this, priority);
                    return;
                }
                default:
                {
                    List<MessageHandler> messageHandlers = GetOrAddMessageHandlerStack(
                        handlers.Keys
                    );
                    try
                    {
                        foreach (MessageHandler handler in messageHandlers)
                        {
                            handler.HandleUntargetedPostProcessing(
                                ref typedMessage,
                                this,
                                priority
                            );
                        }
                    }
                    finally
                    {
                        _messageHandlers.Push(messageHandlers);
                    }

                    break;
                }
            }
        }

        public void UntypedTargetedBroadcast(InstanceId target, ITargetedMessage typedMessage)
        {
            Type messageType = typedMessage.MessageType;
            if (!_broadcastMethodsByType.TryGetValue(messageType, out object targetedMethod))
            {
                // ReSharper disable once PossibleNullReferenceException
                MethodInfo broadcastMethod = MessageBusType
                    .GetMethod(nameof(TargetedBroadcast))
                    .MakeGenericMethod(messageType);
                // ReSharper disable once PossibleNullReferenceException
                MethodInfo helperMethod = MessageBusType
                    .GetMethod(
                        nameof(TargetedBroadcastReflectionHelper),
                        ReflectionHelperBindingFlags
                    )
                    .MakeGenericMethod(messageType);

                ReflectionMethodArgumentsCache[0] = this;
                ReflectionMethodArgumentsCache[1] = broadcastMethod;
                targetedMethod = helperMethod.Invoke(null, ReflectionMethodArgumentsCache);
                _broadcastMethodsByType[messageType] = targetedMethod;
            }

            Action<InstanceId, ITargetedMessage> broadcast =
                (Action<InstanceId, ITargetedMessage>)targetedMethod;
            broadcast.Invoke(target, typedMessage);
        }

        public void TargetedBroadcast<TMessage>(ref InstanceId target, ref TMessage typedMessage)
            where TMessage : ITargetedMessage
        {
            Type type = typeof(TMessage);
            if (!RunTargetedInterceptors(type, ref typedMessage, ref target))
            {
                return;
            }

            if (0 < _globalSinks.Count)
            {
                ITargetedMessage targetedMessage = typedMessage;
                BroadcastGlobalTargeted(ref target, ref targetedMessage);
            }

            bool foundAnyHandlers = false;
            if (
                _targetedSinks.TryGetValue(
                    type,
                    out Dictionary<
                        InstanceId,
                        SortedList<int, SortedList<MessageHandler, int>>
                    > targetedHandlers
                )
                && targetedHandlers.TryGetValue(
                    target,
                    out SortedList<int, SortedList<MessageHandler, int>> sortedHandlers
                )
                && 0 < sortedHandlers.Count
            )
            {
                foundAnyHandlers = true;
                if (sortedHandlers.Count == 1)
                {
                    RunTargetedBroadcast(
                        ref target,
                        ref typedMessage,
                        sortedHandlers.Keys[0],
                        sortedHandlers.Values[0]
                    );
                }
                else
                {
                    List<KeyValuePair<int, SortedList<MessageHandler, int>>> handlerList =
                        GetOrAddMessageHandlerStack(sortedHandlers);
                    try
                    {
                        foreach (
                            KeyValuePair<int, SortedList<MessageHandler, int>> entry in handlerList
                        )
                        {
                            RunTargetedBroadcast(
                                ref target,
                                ref typedMessage,
                                entry.Key,
                                entry.Value
                            );
                        }
                    }
                    finally
                    {
                        _sortedHandlers.Push(handlerList);
                    }
                }
            }

            _ = InternalTargetedWithoutTargetingBroadcast(ref target, ref typedMessage, type);

            if (
                _postProcessingTargetedSinks.TryGetValue(type, out targetedHandlers)
                && targetedHandlers.TryGetValue(target, out sortedHandlers)
                && 0 < sortedHandlers.Count
            )
            {
                foundAnyHandlers = true;
                if (sortedHandlers.Count == 1)
                {
                    RunTargetedPostProcessing(
                        ref target,
                        ref typedMessage,
                        sortedHandlers.Keys[0],
                        sortedHandlers.Values[0]
                    );
                }
                else
                {
                    List<KeyValuePair<int, SortedList<MessageHandler, int>>> handlerList =
                        GetOrAddMessageHandlerStack(sortedHandlers);
                    try
                    {
                        foreach (
                            KeyValuePair<int, SortedList<MessageHandler, int>> entry in handlerList
                        )
                        {
                            RunTargetedPostProcessing(
                                ref target,
                                ref typedMessage,
                                entry.Key,
                                entry.Value
                            );
                        }
                    }
                    finally
                    {
                        _sortedHandlers.Push(handlerList);
                    }
                }
            }

            if (
                _postProcessingTargetedWithoutTargetingSinks.TryGetValue(type, out sortedHandlers)
                && 0 < sortedHandlers.Count
            )
            {
                if (sortedHandlers.Count == 1)
                {
                    RunTargetedWithoutTargetingPostProcessing(
                        ref target,
                        ref typedMessage,
                        sortedHandlers.Keys[0],
                        sortedHandlers.Values[0]
                    );
                }
                else
                {
                    List<KeyValuePair<int, SortedList<MessageHandler, int>>> handlerList =
                        GetOrAddMessageHandlerStack(sortedHandlers);
                    try
                    {
                        foreach (
                            KeyValuePair<int, SortedList<MessageHandler, int>> entry in handlerList
                        )
                        {
                            RunTargetedWithoutTargetingPostProcessing(
                                ref target,
                                ref typedMessage,
                                entry.Key,
                                entry.Value
                            );
                        }
                    }
                    finally
                    {
                        _sortedHandlers.Push(handlerList);
                    }
                }
            }

            if (!foundAnyHandlers && MessagingDebug.enabled)
            {
                MessagingDebug.Log(
                    LogLevel.Info,
                    "Could not find a matching targeted broadcast handler for Id: {0}, Message: {1}.",
                    target,
                    typedMessage
                );
            }
        }

        private void RunTargetedWithoutTargetingPostProcessing<TMessage>(
            ref InstanceId target,
            ref TMessage typedMessage,
            int priority,
            SortedList<MessageHandler, int> handlers
        )
            where TMessage : ITargetedMessage
        {
            switch (handlers.Count)
            {
                case <= 0:
                {
                    return;
                }
                case 1:
                {
                    MessageHandler handler = handlers.Keys[0];
                    handler.HandleTargetedWithoutTargetingPostProcessing(
                        ref target,
                        ref typedMessage,
                        this,
                        priority
                    );
                    return;
                }
                default:
                {
                    List<MessageHandler> messageHandlers = GetOrAddMessageHandlerStack(
                        handlers.Keys
                    );
                    try
                    {
                        foreach (MessageHandler handler in messageHandlers)
                        {
                            handler.HandleTargetedWithoutTargetingPostProcessing(
                                ref target,
                                ref typedMessage,
                                this,
                                priority
                            );
                        }
                    }
                    finally
                    {
                        _messageHandlers.Push(messageHandlers);
                    }

                    break;
                }
            }
        }

        private void RunTargetedPostProcessing<TMessage>(
            ref InstanceId target,
            ref TMessage typedMessage,
            int priority,
            SortedList<MessageHandler, int> handlers
        )
            where TMessage : ITargetedMessage
        {
            switch (handlers.Count)
            {
                case <= 0:
                {
                    return;
                }
                case 1:
                {
                    MessageHandler handler = handlers.Keys[0];
                    handler.HandleTargetedPostProcessing(
                        ref target,
                        ref typedMessage,
                        this,
                        priority
                    );
                    return;
                }
                default:
                {
                    List<MessageHandler> messageHandlers = GetOrAddMessageHandlerStack(
                        handlers.Keys
                    );
                    try
                    {
                        foreach (MessageHandler handler in messageHandlers)
                        {
                            handler.HandleTargetedPostProcessing(
                                ref target,
                                ref typedMessage,
                                this,
                                priority
                            );
                        }
                    }
                    finally
                    {
                        _messageHandlers.Push(messageHandlers);
                    }

                    break;
                }
            }
        }

        private void RunTargetedBroadcast<TMessage>(
            ref InstanceId target,
            ref TMessage typedMessage,
            int priority,
            SortedList<MessageHandler, int> handlers
        )
            where TMessage : ITargetedMessage
        {
            switch (handlers.Count)
            {
                case <= 0:
                {
                    return;
                }
                case 1:
                {
                    MessageHandler handler = handlers.Keys[0];
                    handler.HandleTargeted(ref target, ref typedMessage, this, priority);
                    return;
                }
                default:
                {
                    List<MessageHandler> messageHandlers = GetOrAddMessageHandlerStack(
                        handlers.Keys
                    );
                    try
                    {
                        foreach (MessageHandler handler in messageHandlers)
                        {
                            handler.HandleTargeted(ref target, ref typedMessage, this, priority);
                        }
                    }
                    finally
                    {
                        _messageHandlers.Push(messageHandlers);
                    }

                    break;
                }
            }
        }

        public void UntypedSourcedBroadcast(InstanceId source, IBroadcastMessage typedMessage)
        {
            Type messageType = typedMessage.MessageType;
            if (
                !_broadcastMethodsByType.TryGetValue(messageType, out object sourcedBroadcastMethod)
            )
            {
                // ReSharper disable once PossibleNullReferenceException
                MethodInfo broadcastMethod = MessageBusType
                    .GetMethod(nameof(SourcedBroadcast))
                    .MakeGenericMethod(messageType);
                // ReSharper disable once PossibleNullReferenceException
                MethodInfo helperMethod = MessageBusType
                    .GetMethod(
                        nameof(SourcedBroadcastReflectionHelper),
                        ReflectionHelperBindingFlags
                    )
                    .MakeGenericMethod(messageType);

                ReflectionMethodArgumentsCache[0] = this;
                ReflectionMethodArgumentsCache[1] = broadcastMethod;
                sourcedBroadcastMethod = helperMethod.Invoke(null, ReflectionMethodArgumentsCache);

                _broadcastMethodsByType[messageType] = sourcedBroadcastMethod;
            }

            Action<InstanceId, IBroadcastMessage> broadcast =
                (Action<InstanceId, IBroadcastMessage>)sourcedBroadcastMethod;
            broadcast.Invoke(source, typedMessage);
        }

        public void SourcedBroadcast<TMessage>(ref InstanceId source, ref TMessage typedMessage)
            where TMessage : IBroadcastMessage
        {
            Type type = typeof(TMessage);
            if (!RunBroadcastInterceptors(type, ref typedMessage, ref source))
            {
                return;
            }

            if (0 < _globalSinks.Count)
            {
                IBroadcastMessage broadcastMessage = typedMessage;
                BroadcastGlobalSourcedBroadcast(ref source, ref broadcastMessage);
            }

            bool foundAnyHandlers = false;
            if (
                _broadcastSinks.TryGetValue(
                    type,
                    out Dictionary<
                        InstanceId,
                        SortedList<int, SortedList<MessageHandler, int>>
                    > broadcastHandlers
                )
                && broadcastHandlers.TryGetValue(
                    source,
                    out SortedList<int, SortedList<MessageHandler, int>> sortedHandlers
                )
                && 0 < sortedHandlers.Count
            )
            {
                foundAnyHandlers = true;
                if (sortedHandlers.Count == 1)
                {
                    RunBroadcast(
                        ref source,
                        ref typedMessage,
                        sortedHandlers.Keys[0],
                        sortedHandlers.Values[0]
                    );
                }
                else
                {
                    List<KeyValuePair<int, SortedList<MessageHandler, int>>> handlerList =
                        GetOrAddMessageHandlerStack(sortedHandlers);
                    try
                    {
                        foreach (
                            KeyValuePair<int, SortedList<MessageHandler, int>> entry in handlerList
                        )
                        {
                            RunBroadcast(ref source, ref typedMessage, entry.Key, entry.Value);
                        }
                    }
                    finally
                    {
                        _sortedHandlers.Push(handlerList);
                    }
                }
            }

            _ = InternalBroadcastWithoutSource(ref source, ref typedMessage, type);

            if (
                _postProcessingBroadcastSinks.TryGetValue(type, out broadcastHandlers)
                && broadcastHandlers.TryGetValue(source, out sortedHandlers)
                && 0 < sortedHandlers.Count
            )
            {
                foundAnyHandlers = true;
                if (sortedHandlers.Count == 1)
                {
                    RunBroadcastPostProcessing(
                        ref source,
                        ref typedMessage,
                        sortedHandlers.Keys[0],
                        sortedHandlers.Values[0]
                    );
                }
                else
                {
                    List<KeyValuePair<int, SortedList<MessageHandler, int>>> handlerList =
                        GetOrAddMessageHandlerStack(sortedHandlers);
                    try
                    {
                        foreach (
                            KeyValuePair<int, SortedList<MessageHandler, int>> entry in handlerList
                        )
                        {
                            RunBroadcastPostProcessing(
                                ref source,
                                ref typedMessage,
                                entry.Key,
                                entry.Value
                            );
                        }
                    }
                    finally
                    {
                        _sortedHandlers.Push(handlerList);
                    }
                }
            }

            if (
                _postProcessingBroadcastWithoutSourceSinks.TryGetValue(type, out sortedHandlers)
                && 0 < sortedHandlers.Count
            )
            {
                if (sortedHandlers.Count == 1)
                {
                    RunBroadcastWithoutSourcePostProcessing(
                        ref source,
                        ref typedMessage,
                        sortedHandlers.Keys[0],
                        sortedHandlers.Values[0]
                    );
                }
                else
                {
                    List<KeyValuePair<int, SortedList<MessageHandler, int>>> handlerList =
                        GetOrAddMessageHandlerStack(sortedHandlers);
                    try
                    {
                        foreach (
                            KeyValuePair<int, SortedList<MessageHandler, int>> entry in handlerList
                        )
                        {
                            RunBroadcastWithoutSourcePostProcessing(
                                ref source,
                                ref typedMessage,
                                entry.Key,
                                entry.Value
                            );
                        }
                    }
                    finally
                    {
                        _sortedHandlers.Push(handlerList);
                    }
                }
            }

            if (!foundAnyHandlers && MessagingDebug.enabled)
            {
                MessagingDebug.Log(
                    LogLevel.Info,
                    "Could not find a matching sourced broadcast handler for Id: {0}, Message: {1}.",
                    source,
                    typedMessage
                );
            }
        }

        private void RunBroadcastWithoutSourcePostProcessing<TMessage>(
            ref InstanceId source,
            ref TMessage typedMessage,
            int priority,
            SortedList<MessageHandler, int> handlers
        )
            where TMessage : IBroadcastMessage
        {
            switch (handlers.Count)
            {
                case <= 0:
                {
                    return;
                }
                case 1:
                {
                    MessageHandler handler = handlers.Keys[0];
                    handler.HandleSourcedBroadcastWithoutSourcePostProcessing(
                        ref source,
                        ref typedMessage,
                        this,
                        priority
                    );
                    return;
                }
                default:
                {
                    List<MessageHandler> messageHandlers = GetOrAddMessageHandlerStack(
                        handlers.Keys
                    );
                    try
                    {
                        foreach (MessageHandler handler in messageHandlers)
                        {
                            handler.HandleSourcedBroadcastWithoutSourcePostProcessing(
                                ref source,
                                ref typedMessage,
                                this,
                                priority
                            );
                        }
                    }
                    finally
                    {
                        _messageHandlers.Push(messageHandlers);
                    }

                    break;
                }
            }
        }

        private void RunBroadcastPostProcessing<TMessage>(
            ref InstanceId source,
            ref TMessage typedMessage,
            int priority,
            SortedList<MessageHandler, int> handlers
        )
            where TMessage : IBroadcastMessage
        {
            switch (handlers.Count)
            {
                case <= 0:
                {
                    return;
                }
                case 1:
                {
                    MessageHandler handler = handlers.Keys[0];
                    handler.HandleSourcedBroadcastPostProcessing(
                        ref source,
                        ref typedMessage,
                        this,
                        priority
                    );
                    return;
                }
                default:
                {
                    List<MessageHandler> messageHandlers = GetOrAddMessageHandlerStack(
                        handlers.Keys
                    );
                    try
                    {
                        foreach (MessageHandler handler in messageHandlers)
                        {
                            handler.HandleSourcedBroadcastPostProcessing(
                                ref source,
                                ref typedMessage,
                                this,
                                priority
                            );
                        }
                    }
                    finally
                    {
                        _messageHandlers.Push(messageHandlers);
                    }

                    break;
                }
            }
        }

        private void RunBroadcast<TMessage>(
            ref InstanceId source,
            ref TMessage typedMessage,
            int priority,
            SortedList<MessageHandler, int> handlers
        )
            where TMessage : IBroadcastMessage
        {
            switch (handlers.Count)
            {
                case <= 0:
                {
                    return;
                }
                case 1:
                {
                    MessageHandler handler = handlers.Keys[0];
                    handler.HandleSourcedBroadcast(ref source, ref typedMessage, this, priority);
                    return;
                }
                default:
                {
                    List<MessageHandler> messageHandlers = GetOrAddMessageHandlerStack(
                        handlers.Keys
                    );
                    try
                    {
                        foreach (MessageHandler handler in messageHandlers)
                        {
                            handler.HandleSourcedBroadcast(
                                ref source,
                                ref typedMessage,
                                this,
                                priority
                            );
                        }
                    }
                    finally
                    {
                        _messageHandlers.Push(messageHandlers);
                    }

                    break;
                }
            }
        }

        private void BroadcastGlobalUntargeted(ref IUntargetedMessage message)
        {
            switch (_globalSinks.Count)
            {
                case <= 0:
                {
                    return;
                }
                case 1:
                {
                    MessageHandler handler = _globalSinks.Keys[0];
                    handler.HandleGlobalUntargetedMessage(ref message, this);
                    return;
                }
                default:
                {
                    List<MessageHandler> messageHandlers = GetOrAddMessageHandlerStack(
                        _globalSinks.Keys
                    );
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

                    break;
                }
            }
        }

        private void BroadcastGlobalTargeted(ref InstanceId target, ref ITargetedMessage message)
        {
            switch (_globalSinks.Count)
            {
                case <= 0:
                {
                    return;
                }
                case 1:
                {
                    MessageHandler handler = _globalSinks.Keys[0];
                    handler.HandleGlobalTargetedMessage(ref target, ref message, this);
                    return;
                }
                default:
                {
                    List<MessageHandler> messageHandlers = GetOrAddMessageHandlerStack(
                        _globalSinks.Keys
                    );
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

                    break;
                }
            }
        }

        private void BroadcastGlobalSourcedBroadcast(
            ref InstanceId source,
            ref IBroadcastMessage message
        )
        {
            switch (_globalSinks.Count)
            {
                case <= 0:
                {
                    return;
                }
                case 1:
                {
                    MessageHandler handler = _globalSinks.Keys[0];
                    handler.HandleGlobalSourcedBroadcastMessage(ref source, ref message, this);
                    return;
                }
                default:
                {
                    List<MessageHandler> messageHandlers = GetOrAddMessageHandlerStack(
                        _globalSinks.Keys
                    );
                    try
                    {
                        foreach (MessageHandler handler in messageHandlers)
                        {
                            handler.HandleGlobalSourcedBroadcastMessage(
                                ref source,
                                ref message,
                                this
                            );
                        }
                    }
                    finally
                    {
                        _messageHandlers.Push(messageHandlers);
                    }

                    break;
                }
            }
        }

        private bool TryGetInterceptorCaches(
            Type type,
            out List<List<object>> interceptorStack,
            out List<object> interceptorObjects
        )
        {
            if (
                !_interceptsByType.TryGetValue(type, out SortedList<int, List<object>> interceptors)
                || interceptors.Count <= 0
            )
            {
                interceptorStack = default;
                interceptorObjects = default;
                return false;
            }

            if (!_interceptors.TryPop(out interceptorStack))
            {
                interceptorStack = new List<List<object>>(interceptors.Values);
            }
            else
            {
                interceptorStack.Clear();
                switch (interceptors.Values)
                {
                    case List<List<object>> list:
                    {
                        foreach (List<object> interceptor in list)
                        {
                            interceptorStack.Add(interceptor);
                        }

                        break;
                    }
                    case List<object>[] array:
                    {
                        foreach (List<object> interceptor in array)
                        {
                            interceptorStack.Add(interceptor);
                        }

                        break;
                    }
                    default:
                    {
                        // ReSharper disable once ForCanBeConvertedToForeach
                        // ReSharper disable once LoopCanBeConvertedToQuery
                        for (int i = 0; i < interceptors.Values.Count; i++)
                        {
                            List<object> interceptor = interceptors.Values[i];
                            interceptorStack.Add(interceptor);
                        }

                        break;
                    }
                }
            }

            if (!_innerInterceptorsStack.TryPop(out interceptorObjects))
            {
                interceptorObjects = new List<object>();
            }

            return true;
        }

        private bool RunUntargetedInterceptors<T>(Type type, ref T message)
            where T : IUntargetedMessage
        {
            if (
                !TryGetInterceptorCaches(
                    type,
                    out List<List<object>> interceptorStack,
                    out List<object> interceptorObjects
                )
            )
            {
                return true;
            }

            try
            {
                foreach (List<object> stack in interceptorStack)
                {
                    interceptorObjects.Clear();
                    foreach (object interceptor in stack)
                    {
                        interceptorObjects.Add(interceptor);
                    }

                    foreach (object transformer in interceptorObjects)
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
                _innerInterceptorsStack.Push(interceptorObjects);
            }

            return true;
        }

        private bool RunTargetedInterceptors<T>(Type type, ref T message, ref InstanceId target)
            where T : ITargetedMessage
        {
            if (
                !TryGetInterceptorCaches(
                    type,
                    out List<List<object>> interceptorStack,
                    out List<object> interceptorObjects
                )
            )
            {
                return true;
            }

            try
            {
                foreach (List<object> stack in interceptorStack)
                {
                    interceptorObjects.Clear();
                    foreach (object interceptor in stack)
                    {
                        interceptorObjects.Add(interceptor);
                    }

                    foreach (object transformer in interceptorObjects)
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
                _innerInterceptorsStack.Push(interceptorObjects);
            }

            return true;
        }

        private bool RunBroadcastInterceptors<T>(Type type, ref T message, ref InstanceId source)
            where T : IBroadcastMessage
        {
            if (
                !TryGetInterceptorCaches(
                    type,
                    out List<List<object>> interceptorStack,
                    out List<object> interceptorObjects
                )
            )
            {
                return true;
            }

            try
            {
                foreach (List<object> stack in interceptorStack)
                {
                    interceptorObjects.Clear();
                    foreach (object interceptor in stack)
                    {
                        interceptorObjects.Add(interceptor);
                    }

                    foreach (object transformer in interceptorObjects)
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
                _innerInterceptorsStack.Push(interceptorObjects);
            }

            return true;
        }

        private bool InternalUntargetedBroadcast<TMessage>(ref TMessage message, Type type)
            where TMessage : IMessage
        {
            if (
                !_sinks.TryGetValue(
                    type,
                    out SortedList<int, SortedList<MessageHandler, int>> sortedHandlers
                )
                || sortedHandlers.Count <= 0
            )
            {
                return false;
            }

            if (sortedHandlers.Count == 1)
            {
                RunUntargetedBroadcast(
                    ref message,
                    sortedHandlers.Keys[0],
                    sortedHandlers.Values[0]
                );
                return true;
            }

            List<KeyValuePair<int, SortedList<MessageHandler, int>>> handlerList =
                GetOrAddMessageHandlerStack(sortedHandlers);
            try
            {
                foreach (KeyValuePair<int, SortedList<MessageHandler, int>> entry in handlerList)
                {
                    RunUntargetedBroadcast(ref message, entry.Key, entry.Value);
                }
            }
            finally
            {
                _sortedHandlers.Push(handlerList);
            }

            return true;
        }

        private void RunUntargetedBroadcast<TMessage>(
            ref TMessage message,
            int priority,
            SortedList<MessageHandler, int> handlers
        )
            where TMessage : IMessage
        {
            switch (handlers.Count)
            {
                case <= 0:
                {
                    return;
                }
                case 1:
                {
                    MessageHandler handler = handlers.Keys[0];
                    handler.HandleUntargetedMessage(ref message, this, priority);
                    return;
                }
                default:
                {
                    List<MessageHandler> messageHandlers = GetOrAddMessageHandlerStack(
                        handlers.Keys
                    );
                    try
                    {
                        foreach (MessageHandler handler in messageHandlers)
                        {
                            handler.HandleUntargetedMessage(ref message, this, priority);
                        }
                    }
                    finally
                    {
                        _messageHandlers.Push(messageHandlers);
                    }

                    break;
                }
            }
        }

        private bool InternalTargetedWithoutTargetingBroadcast<TMessage>(
            ref InstanceId target,
            ref TMessage message,
            Type type
        )
            where TMessage : ITargetedMessage
        {
            if (
                !_sinks.TryGetValue(
                    type,
                    out SortedList<int, SortedList<MessageHandler, int>> sortedHandlers
                )
                || sortedHandlers.Count <= 0
            )
            {
                return false;
            }

            if (sortedHandlers.Count == 1)
            {
                RunTargetedWithoutTargeting(
                    ref target,
                    ref message,
                    sortedHandlers.Keys[0],
                    sortedHandlers.Values[0]
                );
                return true;
            }

            List<KeyValuePair<int, SortedList<MessageHandler, int>>> handlerList =
                GetOrAddMessageHandlerStack(sortedHandlers);
            try
            {
                foreach (KeyValuePair<int, SortedList<MessageHandler, int>> entry in handlerList)
                {
                    RunTargetedWithoutTargeting(ref target, ref message, entry.Key, entry.Value);
                }
            }
            finally
            {
                _sortedHandlers.Push(handlerList);
            }

            return true;
        }

        private void RunTargetedWithoutTargeting<TMessage>(
            ref InstanceId target,
            ref TMessage message,
            int priority,
            SortedList<MessageHandler, int> handlers
        )
            where TMessage : ITargetedMessage
        {
            switch (handlers.Count)
            {
                case <= 0:
                {
                    return;
                }
                case 1:
                {
                    MessageHandler handler = handlers.Keys[0];
                    handler.HandleTargetedWithoutTargeting(ref target, ref message, this, priority);
                    return;
                }
                default:
                {
                    List<MessageHandler> messageHandlers = GetOrAddMessageHandlerStack(
                        handlers.Keys
                    );
                    try
                    {
                        foreach (MessageHandler handler in messageHandlers)
                        {
                            handler.HandleTargetedWithoutTargeting(
                                ref target,
                                ref message,
                                this,
                                priority
                            );
                        }
                    }
                    finally
                    {
                        _messageHandlers.Push(messageHandlers);
                    }

                    break;
                }
            }
        }

        private bool InternalBroadcastWithoutSource<TMessage>(
            ref InstanceId source,
            ref TMessage message,
            Type type
        )
            where TMessage : IBroadcastMessage
        {
            if (
                !_sinks.TryGetValue(
                    type,
                    out SortedList<int, SortedList<MessageHandler, int>> sortedHandlers
                )
                || sortedHandlers.Count <= 0
            )
            {
                return false;
            }

            if (sortedHandlers.Count == 1)
            {
                RunBroadcastWithoutSource(
                    ref source,
                    ref message,
                    sortedHandlers.Keys[0],
                    sortedHandlers.Values[0]
                );
                return true;
            }

            List<KeyValuePair<int, SortedList<MessageHandler, int>>> handlerList =
                GetOrAddMessageHandlerStack(sortedHandlers);
            try
            {
                foreach (KeyValuePair<int, SortedList<MessageHandler, int>> entry in handlerList)
                {
                    RunBroadcastWithoutSource(ref source, ref message, entry.Key, entry.Value);
                }
            }
            finally
            {
                _sortedHandlers.Push(handlerList);
            }

            return true;
        }

        private void RunBroadcastWithoutSource<TMessage>(
            ref InstanceId source,
            ref TMessage message,
            int priority,
            SortedList<MessageHandler, int> handlers
        )
            where TMessage : IBroadcastMessage
        {
            switch (handlers.Count)
            {
                case <= 0:
                {
                    return;
                }
                case 1:
                {
                    MessageHandler handler = handlers.Keys[0];
                    handler.HandleSourcedBroadcastWithoutSource(
                        ref source,
                        ref message,
                        this,
                        priority
                    );
                    return;
                }
                default:
                {
                    List<MessageHandler> messageHandlers = GetOrAddMessageHandlerStack(
                        handlers.Keys
                    );
                    try
                    {
                        foreach (MessageHandler handler in messageHandlers)
                        {
                            handler.HandleSourcedBroadcastWithoutSource(
                                ref source,
                                ref message,
                                this,
                                priority
                            );
                        }
                    }
                    finally
                    {
                        _messageHandlers.Push(messageHandlers);
                    }

                    break;
                }
            }
        }

        private Action InternalRegisterUntargeted<T>(
            MessageHandler messageHandler,
            Dictionary<Type, SortedList<int, SortedList<MessageHandler, int>>> sinks,
            RegistrationMethod registrationMethod,
            int priority
        )
            where T : IMessage
        {
            if (messageHandler == null)
            {
                throw new ArgumentNullException(nameof(messageHandler));
            }

            InstanceId handlerOwnerId = messageHandler.owner;
            Type type = typeof(T);

            if (
                !sinks.TryGetValue(
                    type,
                    out SortedList<int, SortedList<MessageHandler, int>> handlers
                )
            )
            {
                handlers = new SortedList<int, SortedList<MessageHandler, int>>();
                sinks[type] = handlers;
            }

            if (!handlers.TryGetValue(priority, out SortedList<MessageHandler, int> handler))
            {
                handler = new SortedList<MessageHandler, int>();
                handlers[priority] = handler;
            }

            int count = handler.GetValueOrDefault(messageHandler, 0);

            handler[messageHandler] = count + 1;
            _log.Log(
                new MessagingRegistration(
                    handlerOwnerId,
                    type,
                    RegistrationType.Register,
                    registrationMethod
                )
            );

            return () =>
            {
                _log.Log(
                    new MessagingRegistration(
                        handlerOwnerId,
                        type,
                        RegistrationType.Deregister,
                        registrationMethod
                    )
                );
                if (
                    !sinks.TryGetValue(type, out handlers)
                    || !handlers.TryGetValue(priority, out handler)
                    || !handler.TryGetValue(messageHandler, out count)
                )
                {
                    if (MessagingDebug.enabled)
                    {
                        MessagingDebug.Log(
                            LogLevel.Error,
                            "Received over-deregistration of {0} for {1}. Check to make sure you're not calling (de)registration multiple times.",
                            type,
                            messageHandler
                        );
                    }

                    return;
                }

                if (count <= 1)
                {
                    bool complete = handler.Remove(messageHandler);

                    if (handler.Count <= 0)
                    {
                        _ = handlers.Remove(priority);
                    }

                    if (handlers.Count <= 0)
                    {
                        _ = sinks.Remove(type);
                    }

                    if (!complete && MessagingDebug.enabled)
                    {
                        MessagingDebug.Log(
                            LogLevel.Error,
                            "Received over-deregistration of {0} for {1}. Check to make sure you're not calling (de)registration multiple times.",
                            type,
                            messageHandler
                        );
                    }
                }
                else
                {
                    handler[messageHandler] = count - 1;
                }
            };
        }

        private Action InternalRegisterWithContext<T>(
            InstanceId context,
            MessageHandler messageHandler,
            Dictionary<
                Type,
                Dictionary<InstanceId, SortedList<int, SortedList<MessageHandler, int>>>
            > sinks,
            RegistrationMethod registrationMethod,
            int priority
        )
        {
            if (messageHandler == null)
            {
                throw new ArgumentNullException(nameof(messageHandler));
            }

            Type type = typeof(T);
            if (
                !sinks.TryGetValue(
                    type,
                    out Dictionary<
                        InstanceId,
                        SortedList<int, SortedList<MessageHandler, int>>
                    > broadcastHandlers
                )
            )
            {
                broadcastHandlers =
                    new Dictionary<InstanceId, SortedList<int, SortedList<MessageHandler, int>>>();
                sinks[type] = broadcastHandlers;
            }

            if (
                !broadcastHandlers.TryGetValue(
                    context,
                    out SortedList<int, SortedList<MessageHandler, int>> handlers
                )
            )
            {
                handlers = new SortedList<int, SortedList<MessageHandler, int>>();
                broadcastHandlers[context] = handlers;
            }

            if (!handlers.TryGetValue(priority, out SortedList<MessageHandler, int> handler))
            {
                handler = new SortedList<MessageHandler, int>();
                handlers[priority] = handler;
            }

            int count = handler.GetValueOrDefault(messageHandler, 0);

            handler[messageHandler] = count + 1;
            _log.Log(
                new MessagingRegistration(
                    context,
                    type,
                    RegistrationType.Register,
                    registrationMethod
                )
            );

            return () =>
            {
                _log.Log(
                    new MessagingRegistration(
                        context,
                        type,
                        RegistrationType.Deregister,
                        registrationMethod
                    )
                );
                if (
                    !sinks.TryGetValue(type, out broadcastHandlers)
                    || !broadcastHandlers.TryGetValue(context, out handlers)
                    || !handlers.TryGetValue(priority, out handler)
                    || !handler.TryGetValue(messageHandler, out count)
                )
                {
                    if (MessagingDebug.enabled)
                    {
                        MessagingDebug.Log(
                            LogLevel.Error,
                            "Received over-deregistration of {0} for {1}. Check to make sure you're not calling (de)registration multiple times.",
                            type,
                            messageHandler
                        );
                    }

                    return;
                }

                if (count <= 1)
                {
                    bool complete = handler.Remove(messageHandler);
                    if (handler.Count <= 0)
                    {
                        _ = handlers.Remove(priority);
                    }

                    if (handlers.Count <= 0)
                    {
                        _ = broadcastHandlers.Remove(context);
                    }

                    if (broadcastHandlers.Count <= 0)
                    {
                        _ = sinks.Remove(type);
                    }

                    if (!complete && MessagingDebug.enabled)
                    {
                        MessagingDebug.Log(
                            LogLevel.Error,
                            "Received over-deregistration of {0} for {1}. Check to make sure you're not calling (de)registration multiple times.",
                            type,
                            messageHandler
                        );
                    }
                }
                else
                {
                    handler[messageHandler] = count - 1;
                }
            };
        }

        private List<
            KeyValuePair<int, SortedList<MessageHandler, int>>
        > GetOrAddMessageHandlerStack(
            IEnumerable<KeyValuePair<int, SortedList<MessageHandler, int>>> handlers
        )
        {
            if (
                !_sortedHandlers.TryPop(
                    out List<KeyValuePair<int, SortedList<MessageHandler, int>>> messageHandlers
                )
            )
            {
                return new List<KeyValuePair<int, SortedList<MessageHandler, int>>>(handlers);
            }

            messageHandlers.Clear();
            if (handlers is SortedList<int, SortedList<MessageHandler, int>> sortedList)
            {
                for (int i = 0; i < sortedList.Count; ++i)
                {
                    messageHandlers.Add(
                        new KeyValuePair<int, SortedList<MessageHandler, int>>(
                            sortedList.Keys[i],
                            sortedList.Values[i]
                        )
                    );
                }
            }
            else
            {
                messageHandlers.AddRange(handlers);
            }
            return messageHandlers;
        }

        private List<MessageHandler> GetOrAddMessageHandlerStack(
            IEnumerable<MessageHandler> handlers
        )
        {
            if (!_messageHandlers.TryPop(out List<MessageHandler> messageHandlers))
            {
                return new List<MessageHandler>(handlers);
            }

            messageHandlers.Clear();
            // Try to avoid allocations if at all possible
            switch (handlers)
            {
                case List<MessageHandler> list:
                {
                    foreach (MessageHandler handler in list)
                    {
                        messageHandlers.Add(handler);
                    }

                    break;
                }
                case MessageHandler[] array:
                {
                    foreach (MessageHandler handler in array)
                    {
                        messageHandlers.Add(handler);
                    }

                    break;
                }
                case IList<MessageHandler> interfaceList:
                {
                    for (int i = 0; i < interfaceList.Count; ++i)
                    {
                        messageHandlers.Add(interfaceList[i]);
                    }

                    break;
                }
                case HashSet<MessageHandler> set:
                {
                    foreach (MessageHandler handler in set)
                    {
                        messageHandlers.Add(handler);
                    }

                    break;
                }
                default:
                {
                    messageHandlers.AddRange(handlers);
                    break;
                }
            }

            return messageHandlers;
        }

        // https://blogs.msmvps.com/jonskeet/2008/08/09/making-reflection-fly-and-exploring-delegates/
        private static Action<IUntargetedMessage> UntargetedBroadcastReflectionHelper<T>(
            IMessageBus messageBus,
            MethodInfo methodInfo
        )
            where T : IUntargetedMessage
        {
            FastUntargetedBroadcast<T> untargetedBroadcast =
                (FastUntargetedBroadcast<T>)
                    Delegate.CreateDelegate(
                        typeof(FastUntargetedBroadcast<T>),
                        messageBus,
                        methodInfo
                    );

            return UntypedBroadcast;

            void UntypedBroadcast(IUntargetedMessage message)
            {
                T typedMessage = (T)message;
                untargetedBroadcast(ref typedMessage);
            }
        }

        private static Action<InstanceId, ITargetedMessage> TargetedBroadcastReflectionHelper<T>(
            IMessageBus messageBus,
            MethodInfo methodInfo
        )
            where T : ITargetedMessage
        {
            FastTargetedBroadcast<T> targetedBroadcast =
                (FastTargetedBroadcast<T>)
                    Delegate.CreateDelegate(
                        typeof(FastTargetedBroadcast<T>),
                        messageBus,
                        methodInfo
                    );

            return UntypedBroadcast;

            void UntypedBroadcast(InstanceId target, ITargetedMessage message)
            {
                T typedMessage = (T)message;
                targetedBroadcast(ref target, ref typedMessage);
            }
        }

        private static Action<InstanceId, IBroadcastMessage> SourcedBroadcastReflectionHelper<T>(
            IMessageBus messageBus,
            MethodInfo methodInfo
        )
            where T : IBroadcastMessage
        {
            FastSourcedBroadcast<T> sourcedBroadcast =
                (FastSourcedBroadcast<T>)
                    Delegate.CreateDelegate(
                        typeof(FastSourcedBroadcast<T>),
                        messageBus,
                        methodInfo
                    );

            return UntypedBroadcast;

            void UntypedBroadcast(InstanceId target, IBroadcastMessage message)
            {
                T typedMessage = (T)message;
                sourcedBroadcast(ref target, ref typedMessage);
            }
        }
    }
}
