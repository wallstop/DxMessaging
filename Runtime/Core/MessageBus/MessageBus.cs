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
        private sealed class HandlerCache<TKey, TValue>
        {
            public readonly SortedList<TKey, TValue> handlers = new();
            public readonly List<KeyValuePair<TKey, TValue>> cache = new();
            public long version;
            public long lastSeenVersion = -1;
        }

        private sealed class HandlerCache
        {
            public readonly Dictionary<MessageHandler, int> handlers = new();
            public readonly List<MessageHandler> cache = new();
            public long version;
            public long lastSeenVersion = -1;
        }

        public int RegisteredTargeted
        {
            get
            {
                int count = 0;
                foreach (var entry in _targetedSinks)
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
                    count += entry.Value.handlers.Count;
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

        private readonly Dictionary<Type, HandlerCache<int, HandlerCache>> _sinks = new();
        private readonly Dictionary<int, HandlerCache<int, HandlerCache>> _optimizedSinks = new();
        private readonly Dictionary<
            Type,
            Dictionary<InstanceId, HandlerCache<int, HandlerCache>>
        > _targetedSinks = new();
        private readonly Dictionary<
            int,
            Dictionary<InstanceId, HandlerCache<int, HandlerCache>>
        > _optimizedTargetedSinks = new();
        private readonly Dictionary<
            Type,
            Dictionary<InstanceId, HandlerCache<int, HandlerCache>>
        > _broadcastSinks = new();
        private readonly Dictionary<
            int,
            Dictionary<InstanceId, HandlerCache<int, HandlerCache>>
        > _optimizedBroadcastSinks = new();
        private readonly Dictionary<Type, HandlerCache<int, HandlerCache>> _postProcessingSinks =
            new();
        private readonly Dictionary<
            int,
            HandlerCache<int, HandlerCache>
        > _optimizedPostProcessingSinks = new();
        private readonly Dictionary<
            Type,
            Dictionary<InstanceId, HandlerCache<int, HandlerCache>>
        > _postProcessingTargetedSinks = new();
        private readonly Dictionary<
            int,
            Dictionary<InstanceId, HandlerCache<int, HandlerCache>>
        > _optimizedPostProcessingTargetedSinks = new();
        private readonly Dictionary<
            Type,
            Dictionary<InstanceId, HandlerCache<int, HandlerCache>>
        > _postProcessingBroadcastSinks = new();
        private readonly Dictionary<
            int,
            Dictionary<InstanceId, HandlerCache<int, HandlerCache>>
        > _optimizedPostProcessingBroadcastSinks = new();
        private readonly Dictionary<
            Type,
            HandlerCache<int, HandlerCache>
        > _postProcessingTargetedWithoutTargetingSinks = new();
        private readonly Dictionary<
            int,
            HandlerCache<int, HandlerCache>
        > _optimizedPostProcessingTargetedWithoutTargetingSinks = new();
        private readonly Dictionary<
            Type,
            HandlerCache<int, HandlerCache>
        > _postProcessingBroadcastWithoutSourceSinks = new();
        private readonly Dictionary<
            int,
            HandlerCache<int, HandlerCache>
        > _optimizedPostProcessingBroadcastWithoutSourceSinks = new();
        private readonly HandlerCache _globalSinks = new();
        private readonly Dictionary<Type, HandlerCache<int, List<object>>> _interceptsByType =
            new();
        private readonly Dictionary<
            int,
            HandlerCache<int, List<object>>
        > _optimizedInterceptsByType = new();
        private readonly Dictionary<object, Dictionary<int, int>> _uniqueInterceptorsAndPriorities =
            new();

        private readonly Dictionary<Type, object> _broadcastMethodsByType = new();
        private readonly Stack<List<object>> _innerInterceptorsStack = new();

        private readonly RegistrationLog _log = new();

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
            _globalSinks.version++;
            int count = _globalSinks.handlers.GetValueOrDefault(messageHandler, 0);

            Type type = typeof(IMessage);
            _globalSinks.handlers[messageHandler] = count + 1;
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
                _globalSinks.version++;
                _log.Log(
                    new MessagingRegistration(
                        messageHandler.owner,
                        type,
                        RegistrationType.Deregister,
                        RegistrationMethod.GlobalAcceptAll
                    )
                );
                if (!_globalSinks.handlers.TryGetValue(messageHandler, out count))
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

                if (count == 1)
                {
                    _ = _globalSinks.handlers.Remove(messageHandler);
                }
                else
                {
                    _globalSinks.handlers[messageHandler] = count - 1;
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
                    out HandlerCache<int, List<object>> prioritizedInterceptors
                )
            )
            {
                prioritizedInterceptors = new HandlerCache<int, List<object>>();
                _interceptsByType[type] = prioritizedInterceptors;
            }

            if (
                !prioritizedInterceptors.handlers.TryGetValue(
                    priority,
                    out List<object> interceptors
                )
            )
            {
                prioritizedInterceptors.version++;
                interceptors = new List<object>();
                prioritizedInterceptors.handlers[priority] = interceptors;
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

                    if (priorityCount.Count == 0)
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
                    if (_interceptsByType.TryGetValue(type, out prioritizedInterceptors))
                    {
                        prioritizedInterceptors.version++;
                        if (
                            prioritizedInterceptors.handlers.TryGetValue(priority, out interceptors)
                        )
                        {
                            complete = interceptors.Remove(interceptor);
                        }
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

            if (0 < _globalSinks.handlers.Count)
            {
                IUntargetedMessage untargetedMessage = typedMessage;
                BroadcastGlobalUntargeted(ref untargetedMessage);
            }

            bool foundAnyHandlers = InternalUntargetedBroadcast(ref typedMessage, type);

            if (
                _postProcessingSinks.TryGetValue(
                    type,
                    out HandlerCache<int, HandlerCache> sortedHandlers
                )
                && 0 < sortedHandlers.handlers.Count
            )
            {
                List<KeyValuePair<int, HandlerCache>> handlerList = GetOrAddMessageHandlerStack(
                    sortedHandlers
                );
                foreach (KeyValuePair<int, HandlerCache> entry in handlerList)
                {
                    RunUntargetedPostProcessing(ref typedMessage, entry.Key, entry.Value);
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
            HandlerCache cache
        )
            where TMessage : IUntargetedMessage
        {
            if (cache.version != cache.lastSeenVersion)
            {
                List<MessageHandler> list = cache.cache;
                list.Clear();
                Dictionary<MessageHandler, int>.KeyCollection keys = cache.handlers.Keys;
                foreach (MessageHandler handler in keys)
                {
                    list.Add(handler);
                }
                cache.lastSeenVersion = cache.version;
            }

            foreach (MessageHandler handler in cache.cache)
            {
                handler.HandleUntargetedPostProcessing(ref typedMessage, this, priority);
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

            if (0 < _globalSinks.handlers.Count)
            {
                ITargetedMessage targetedMessage = typedMessage;
                BroadcastGlobalTargeted(ref target, ref targetedMessage);
            }

            bool foundAnyHandlers = false;
            if (
                _targetedSinks.TryGetValue(
                    type,
                    out Dictionary<InstanceId, HandlerCache<int, HandlerCache>> targetedHandlers
                )
                && targetedHandlers.TryGetValue(
                    target,
                    out HandlerCache<int, HandlerCache> sortedHandlers
                )
                && 0 < sortedHandlers.handlers.Count
            )
            {
                foundAnyHandlers = true;
                List<KeyValuePair<int, HandlerCache>> handlerList = GetOrAddMessageHandlerStack(
                    sortedHandlers
                );
                foreach (KeyValuePair<int, HandlerCache> entry in handlerList)
                {
                    RunTargetedBroadcast(ref target, ref typedMessage, entry.Key, entry.Value);
                }
            }

            _ = InternalTargetedWithoutTargetingBroadcast(ref target, ref typedMessage, type);

            if (
                _postProcessingTargetedSinks.TryGetValue(type, out targetedHandlers)
                && targetedHandlers.TryGetValue(target, out sortedHandlers)
                && 0 < sortedHandlers.handlers.Count
            )
            {
                foundAnyHandlers = true;
                List<KeyValuePair<int, HandlerCache>> handlerList = GetOrAddMessageHandlerStack(
                    sortedHandlers
                );
                foreach (KeyValuePair<int, HandlerCache> entry in handlerList)
                {
                    RunTargetedPostProcessing(ref target, ref typedMessage, entry.Key, entry.Value);
                }
            }

            if (
                _postProcessingTargetedWithoutTargetingSinks.TryGetValue(type, out sortedHandlers)
                && 0 < sortedHandlers.handlers.Count
            )
            {
                List<KeyValuePair<int, HandlerCache>> handlerList = GetOrAddMessageHandlerStack(
                    sortedHandlers
                );
                foreach (KeyValuePair<int, HandlerCache> entry in handlerList)
                {
                    RunTargetedWithoutTargetingPostProcessing(
                        ref target,
                        ref typedMessage,
                        entry.Key,
                        entry.Value
                    );
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
            HandlerCache cache
        )
            where TMessage : ITargetedMessage
        {
            List<MessageHandler> messageHandlers = GetOrAddMessageHandlerStack(cache);
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

        private void RunTargetedPostProcessing<TMessage>(
            ref InstanceId target,
            ref TMessage typedMessage,
            int priority,
            HandlerCache cache
        )
            where TMessage : ITargetedMessage
        {
            List<MessageHandler> messageHandlers = GetOrAddMessageHandlerStack(cache);
            foreach (MessageHandler handler in messageHandlers)
            {
                handler.HandleTargetedPostProcessing(ref target, ref typedMessage, this, priority);
            }
        }

        private void RunTargetedBroadcast<TMessage>(
            ref InstanceId target,
            ref TMessage typedMessage,
            int priority,
            HandlerCache handlers
        )
            where TMessage : ITargetedMessage
        {
            List<MessageHandler> messageHandlers = GetOrAddMessageHandlerStack(handlers);
            foreach (MessageHandler handler in messageHandlers)
            {
                handler.HandleTargeted(ref target, ref typedMessage, this, priority);
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

            if (0 < _globalSinks.handlers.Count)
            {
                IBroadcastMessage broadcastMessage = typedMessage;
                BroadcastGlobalSourcedBroadcast(ref source, ref broadcastMessage);
            }

            bool foundAnyHandlers = false;
            if (
                _broadcastSinks.TryGetValue(
                    type,
                    out Dictionary<InstanceId, HandlerCache<int, HandlerCache>> broadcastHandlers
                )
                && broadcastHandlers.TryGetValue(
                    source,
                    out HandlerCache<int, HandlerCache> sortedHandlers
                )
                && 0 < sortedHandlers.handlers.Count
            )
            {
                foundAnyHandlers = true;
                List<KeyValuePair<int, HandlerCache>> handlerList = GetOrAddMessageHandlerStack(
                    sortedHandlers
                );
                foreach (KeyValuePair<int, HandlerCache> entry in handlerList)
                {
                    RunBroadcast(ref source, ref typedMessage, entry.Key, entry.Value);
                }
            }

            _ = InternalBroadcastWithoutSource(ref source, ref typedMessage, type);

            if (
                _postProcessingBroadcastSinks.TryGetValue(type, out broadcastHandlers)
                && broadcastHandlers.TryGetValue(source, out sortedHandlers)
                && 0 < sortedHandlers.handlers.Count
            )
            {
                foundAnyHandlers = true;
                List<KeyValuePair<int, HandlerCache>> handlerList = GetOrAddMessageHandlerStack(
                    sortedHandlers
                );
                foreach (KeyValuePair<int, HandlerCache> entry in handlerList)
                {
                    RunBroadcastPostProcessing(
                        ref source,
                        ref typedMessage,
                        entry.Key,
                        entry.Value
                    );
                }
            }

            if (
                _postProcessingBroadcastWithoutSourceSinks.TryGetValue(type, out sortedHandlers)
                && 0 < sortedHandlers.handlers.Count
            )
            {
                List<KeyValuePair<int, HandlerCache>> handlerList = GetOrAddMessageHandlerStack(
                    sortedHandlers
                );
                foreach (KeyValuePair<int, HandlerCache> entry in handlerList)
                {
                    RunBroadcastWithoutSourcePostProcessing(
                        ref source,
                        ref typedMessage,
                        entry.Key,
                        entry.Value
                    );
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
            HandlerCache cache
        )
            where TMessage : IBroadcastMessage
        {
            List<MessageHandler> messageHandlers = GetOrAddMessageHandlerStack(cache);
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

        private void RunBroadcastPostProcessing<TMessage>(
            ref InstanceId source,
            ref TMessage typedMessage,
            int priority,
            HandlerCache cache
        )
            where TMessage : IBroadcastMessage
        {
            List<MessageHandler> messageHandlers = GetOrAddMessageHandlerStack(cache);
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

        private void RunBroadcast<TMessage>(
            ref InstanceId source,
            ref TMessage typedMessage,
            int priority,
            HandlerCache cache
        )
            where TMessage : IBroadcastMessage
        {
            List<MessageHandler> messageHandlers = GetOrAddMessageHandlerStack(cache);
            foreach (MessageHandler handler in messageHandlers)
            {
                handler.HandleSourcedBroadcast(ref source, ref typedMessage, this, priority);
            }
        }

        private void BroadcastGlobalUntargeted(ref IUntargetedMessage message)
        {
            if (_globalSinks.handlers.Count == 0)
            {
                return;
            }

            List<MessageHandler> messageHandlers = GetOrAddMessageHandlerStack(_globalSinks);
            foreach (MessageHandler handler in messageHandlers)
            {
                handler.HandleGlobalUntargetedMessage(ref message, this);
            }
        }

        private void BroadcastGlobalTargeted(ref InstanceId target, ref ITargetedMessage message)
        {
            if (_globalSinks.handlers.Count == 0)
            {
                return;
            }

            List<MessageHandler> messageHandlers = GetOrAddMessageHandlerStack(_globalSinks);
            foreach (MessageHandler handler in messageHandlers)
            {
                handler.HandleGlobalTargetedMessage(ref target, ref message, this);
            }
        }

        private void BroadcastGlobalSourcedBroadcast(
            ref InstanceId source,
            ref IBroadcastMessage message
        )
        {
            if (_globalSinks.handlers.Count == 0)
            {
                return;
            }

            List<MessageHandler> messageHandlers = GetOrAddMessageHandlerStack(_globalSinks);
            foreach (MessageHandler handler in messageHandlers)
            {
                handler.HandleGlobalSourcedBroadcastMessage(ref source, ref message, this);
            }
        }

        private bool TryGetInterceptorCaches(
            Type type,
            out List<KeyValuePair<int, List<object>>> interceptorStack,
            out List<object> interceptorObjects
        )
        {
            if (
                !_interceptsByType.TryGetValue(
                    type,
                    out HandlerCache<int, List<object>> interceptors
                )
                || interceptors.handlers.Count == 0
            )
            {
                interceptorStack = default;
                interceptorObjects = default;
                return false;
            }

            interceptorStack = interceptors.cache;
            if (interceptors.version != interceptors.lastSeenVersion)
            {
                interceptorStack.Clear();
                IList<int> keys = interceptors.handlers.Keys;
                IList<List<object>> values = interceptors.handlers.Values;
                for (int i = 0; i < interceptors.handlers.Count; ++i)
                {
                    interceptorStack.Add(new KeyValuePair<int, List<object>>(keys[i], values[i]));
                }

                interceptors.lastSeenVersion = interceptors.version;
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
                    out List<KeyValuePair<int, List<object>>> interceptorStack,
                    out List<object> interceptorObjects
                )
            )
            {
                return true;
            }

            try
            {
                foreach (KeyValuePair<int, List<object>> entry in interceptorStack)
                {
                    interceptorObjects.Clear();
                    foreach (object interceptor in entry.Value)
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
                    out List<KeyValuePair<int, List<object>>> interceptorStack,
                    out List<object> interceptorObjects
                )
            )
            {
                return true;
            }

            try
            {
                foreach (KeyValuePair<int, List<object>> entry in interceptorStack)
                {
                    interceptorObjects.Clear();
                    foreach (object interceptor in entry.Value)
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
                    out List<KeyValuePair<int, List<object>>> interceptorStack,
                    out List<object> interceptorObjects
                )
            )
            {
                return true;
            }

            try
            {
                foreach (KeyValuePair<int, List<object>> entry in interceptorStack)
                {
                    interceptorObjects.Clear();
                    foreach (object interceptor in entry.Value)
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
                _innerInterceptorsStack.Push(interceptorObjects);
            }

            return true;
        }

        private bool InternalUntargetedBroadcast<TMessage>(ref TMessage message, Type type)
            where TMessage : IMessage
        {
            if (
                !_sinks.TryGetValue(type, out HandlerCache<int, HandlerCache> sortedHandlers)
                || sortedHandlers.handlers.Count == 0
            )
            {
                return false;
            }

            List<KeyValuePair<int, HandlerCache>> handlerList = GetOrAddMessageHandlerStack(
                sortedHandlers
            );
            foreach (KeyValuePair<int, HandlerCache> entry in handlerList)
            {
                RunUntargetedBroadcast(ref message, entry.Key, entry.Value);
            }
            return true;
        }

        private void RunUntargetedBroadcast<TMessage>(
            ref TMessage message,
            int priority,
            HandlerCache cache
        )
            where TMessage : IMessage
        {
            List<MessageHandler> messageHandlers = GetOrAddMessageHandlerStack(cache);
            foreach (MessageHandler handler in messageHandlers)
            {
                handler.HandleUntargetedMessage(ref message, this, priority);
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
                !_sinks.TryGetValue(type, out HandlerCache<int, HandlerCache> sortedHandlers)
                || sortedHandlers.handlers.Count == 0
            )
            {
                return false;
            }

            List<KeyValuePair<int, HandlerCache>> handlerList = GetOrAddMessageHandlerStack(
                sortedHandlers
            );
            foreach (KeyValuePair<int, HandlerCache> entry in handlerList)
            {
                RunTargetedWithoutTargeting(ref target, ref message, entry.Key, entry.Value);
            }

            return true;
        }

        private void RunTargetedWithoutTargeting<TMessage>(
            ref InstanceId target,
            ref TMessage message,
            int priority,
            HandlerCache cache
        )
            where TMessage : ITargetedMessage
        {
            List<MessageHandler> messageHandlers = GetOrAddMessageHandlerStack(cache);
            foreach (MessageHandler handler in messageHandlers)
            {
                handler.HandleTargetedWithoutTargeting(ref target, ref message, this, priority);
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
                !_sinks.TryGetValue(type, out HandlerCache<int, HandlerCache> sortedHandlers)
                || sortedHandlers.handlers.Count == 0
            )
            {
                return false;
            }

            List<KeyValuePair<int, HandlerCache>> handlerList = GetOrAddMessageHandlerStack(
                sortedHandlers
            );
            foreach (KeyValuePair<int, HandlerCache> entry in handlerList)
            {
                RunBroadcastWithoutSource(ref source, ref message, entry.Key, entry.Value);
            }

            return true;
        }

        private void RunBroadcastWithoutSource<TMessage>(
            ref InstanceId source,
            ref TMessage message,
            int priority,
            HandlerCache cache
        )
            where TMessage : IBroadcastMessage
        {
            List<MessageHandler> messageHandlers = GetOrAddMessageHandlerStack(cache);
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

        private Action InternalRegisterUntargeted<T>(
            MessageHandler messageHandler,
            Dictionary<Type, HandlerCache<int, HandlerCache>> sinks,
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

            if (!sinks.TryGetValue(type, out HandlerCache<int, HandlerCache> handlers))
            {
                handlers = new HandlerCache<int, HandlerCache>();
                sinks[type] = handlers;
            }

            if (!handlers.handlers.TryGetValue(priority, out HandlerCache cache))
            {
                handlers.version++;
                cache = new HandlerCache();
                handlers.handlers[priority] = cache;
            }

            Dictionary<MessageHandler, int> handler = cache.handlers;
            cache.version++;
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
                cache.version++;
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
                    || !handlers.handlers.TryGetValue(priority, out cache)
                    || !cache.handlers.TryGetValue(messageHandler, out count)
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

                handlers.version++;
                handler = cache.handlers;
                if (count == 1)
                {
                    bool complete = handler.Remove(messageHandler);

                    if (handler.Count == 0)
                    {
                        _ = handlers.handlers.Remove(priority);
                    }

                    if (handlers.handlers.Count == 0)
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
            Dictionary<Type, Dictionary<InstanceId, HandlerCache<int, HandlerCache>>> sinks,
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
                    out Dictionary<InstanceId, HandlerCache<int, HandlerCache>> broadcastHandlers
                )
            )
            {
                broadcastHandlers = new Dictionary<InstanceId, HandlerCache<int, HandlerCache>>();
                sinks[type] = broadcastHandlers;
            }

            if (
                !broadcastHandlers.TryGetValue(
                    context,
                    out HandlerCache<int, HandlerCache> handlers
                )
            )
            {
                handlers = new HandlerCache<int, HandlerCache>();
                broadcastHandlers[context] = handlers;
            }

            if (!handlers.handlers.TryGetValue(priority, out HandlerCache cache))
            {
                handlers.version++;
                cache = new HandlerCache();
                handlers.handlers[priority] = cache;
            }

            cache.version++;
            Dictionary<MessageHandler, int> handler = cache.handlers;
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
                cache.version++;
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
                    || !handlers.handlers.TryGetValue(priority, out cache)
                    || !cache.handlers.TryGetValue(messageHandler, out count)
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

                handler = cache.handlers;
                if (count == 1)
                {
                    bool complete = handler.Remove(messageHandler);
                    if (handler.Count == 0)
                    {
                        handlers.version++;
                        _ = handlers.handlers.Remove(priority);
                    }

                    if (handlers.handlers.Count == 0)
                    {
                        _ = broadcastHandlers.Remove(context);
                    }

                    if (broadcastHandlers.Count == 0)
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

        private static List<KeyValuePair<int, HandlerCache>> GetOrAddMessageHandlerStack(
            HandlerCache<int, HandlerCache> cache
        )
        {
            if (cache.version == cache.lastSeenVersion)
            {
                return cache.cache;
            }

            List<KeyValuePair<int, HandlerCache>> list = cache.cache;
            list.Clear();
            SortedList<int, HandlerCache> handlers = cache.handlers;
            IList<int> keys = handlers.Keys;
            IList<HandlerCache> values = handlers.Values;
            for (int i = 0; i < handlers.Count; i++)
            {
                list.Add(new KeyValuePair<int, HandlerCache>(keys[i], values[i]));
            }

            cache.lastSeenVersion = cache.version;
            return list;
        }

        private static List<MessageHandler> GetOrAddMessageHandlerStack(HandlerCache cache)
        {
            if (cache.version == cache.lastSeenVersion)
            {
                return cache.cache;
            }

            List<MessageHandler> list = cache.cache;
            list.Clear();
            Dictionary<MessageHandler, int>.KeyCollection keys = cache.handlers.Keys;
            foreach (MessageHandler key in keys)
            {
                list.Add(key);
            }
            cache.lastSeenVersion = cache.version;
            return list;
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
