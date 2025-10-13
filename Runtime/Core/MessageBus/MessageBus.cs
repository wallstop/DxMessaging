namespace DxMessaging.Core.MessageBus
{
    using System;
    using System.Collections.Generic;
    using System.Linq.Expressions;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using DataStructure;
    using Diagnostics;
    using Extensions;
    using Helper;
    using Messages;
    using static IMessageBus;
#if UNITY_2017_1_OR_NEWER
    using UnityEngine;
#endif

    /// <summary>
    /// Instanced MessageBus for use cases where you want distinct islands of MessageBuses.
    /// </summary>
    public sealed class MessageBus : IMessageBus
    {
        private long _emissionId;
        public long EmissionId => _emissionId;

        private sealed class HandlerCache<TKey, TValue>
        {
            public readonly Dictionary<TKey, TValue> handlers = new();
            public readonly List<TKey> order = new();
            public readonly List<KeyValuePair<TKey, TValue>> cache = new();
            public long version;
            public long lastSeenVersion = -1;
            public long lastSeenEmissionId;
        }

        private sealed class HandlerCache
        {
            public readonly Dictionary<MessageHandler, int> handlers = new();
            public readonly List<MessageHandler> cache = new();
            public long version;
            public long lastSeenVersion = -1;
            public long lastSeenEmissionId;
        }

        public int RegisteredTargeted
        {
            get
            {
                int count = 0;
                using MessageCache<
                    Dictionary<InstanceId, HandlerCache<int, HandlerCache>>
                >.MessageCacheEnumerator enumeratorT = _targetedSinks.GetEnumerator();
                while (enumeratorT.MoveNext())
                {
                    Dictionary<InstanceId, HandlerCache<int, HandlerCache>> entry =
                        enumeratorT.Current;
                    count += entry.Count;
                }

                return count;
            }
        }

        public int RegisteredGlobalSequentialIndex { get; } = GenerateNewGlobalSequentialIndex();

        public int RegisteredBroadcast
        {
            get
            {
                int count = 0;
                using MessageCache<
                    Dictionary<InstanceId, HandlerCache<int, HandlerCache>>
                >.MessageCacheEnumerator enumeratorB = _broadcastSinks.GetEnumerator();
                while (enumeratorB.MoveNext())
                {
                    Dictionary<InstanceId, HandlerCache<int, HandlerCache>> entry =
                        enumeratorB.Current;
                    count += entry.Count;
                }

                return count;
            }
        }

        public int RegisteredUntargeted
        {
            get
            {
                int count = 0;
                using MessageCache<
                    HandlerCache<int, HandlerCache>
                >.MessageCacheEnumerator enumeratorU = _sinks.GetEnumerator();
                while (enumeratorU.MoveNext())
                {
                    HandlerCache<int, HandlerCache> entry = enumeratorU.Current;
                    count += entry.handlers.Count;
                }

                return count;
            }
        }

        public bool DiagnosticsMode
        {
            get => _diagnosticsMode;
            set => _diagnosticsMode = value;
        }

        private static readonly Type MessageBusType = typeof(MessageBus);

        // For use with re-broadcasting to generic methods
        private static readonly object[] ReflectionMethodArgumentsCache = new object[2];
        private static readonly List<Expression> ArgumentExpressionsCache = new();

        private const BindingFlags ReflectionHelperBindingFlags =
            BindingFlags.Static | BindingFlags.NonPublic;
        private const BindingFlags ReflexiveMethodBindingFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private delegate void FastUntargetedBroadcast<T>(ref T message)
            where T : IUntargetedMessage;
        private delegate void FastTargetedBroadcast<T>(ref InstanceId target, ref T message)
            where T : ITargetedMessage;
        private delegate void FastSourcedBroadcast<T>(ref InstanceId target, ref T message)
            where T : IBroadcastMessage;

        public RegistrationLog Log => _log;

        private readonly MessageCache<HandlerCache<int, HandlerCache>> _sinks = new();
        private readonly MessageCache<
            Dictionary<InstanceId, HandlerCache<int, HandlerCache>>
        > _targetedSinks = new();
        private readonly MessageCache<
            Dictionary<InstanceId, HandlerCache<int, HandlerCache>>
        > _broadcastSinks = new();
        private readonly MessageCache<HandlerCache<int, HandlerCache>> _postProcessingSinks = new();
        private readonly MessageCache<
            Dictionary<InstanceId, HandlerCache<int, HandlerCache>>
        > _postProcessingTargetedSinks = new();
        private readonly MessageCache<
            Dictionary<InstanceId, HandlerCache<int, HandlerCache>>
        > _postProcessingBroadcastSinks = new();
        private readonly MessageCache<
            HandlerCache<int, HandlerCache>
        > _postProcessingTargetedWithoutTargetingSinks = new();
        private readonly MessageCache<
            HandlerCache<int, HandlerCache>
        > _postProcessingBroadcastWithoutSourceSinks = new();
        private readonly HandlerCache _globalSinks = new();

        // Interceptors split by category to avoid mixing types
        private readonly MessageCache<HandlerCache<int, List<object>>> _untargetedInterceptsByType =
            new();
        private readonly MessageCache<HandlerCache<int, List<object>>> _targetedInterceptsByType =
            new();
        private readonly MessageCache<HandlerCache<int, List<object>>> _broadcastInterceptsByType =
            new();
        private readonly Dictionary<object, Dictionary<int, int>> _uniqueInterceptorsAndPriorities =
            new();

        private readonly Dictionary<Type, object> _broadcastMethodsByType = new();
        private readonly Stack<List<object>> _innerInterceptorsStack = new();

        private readonly Dictionary<
            Type,
            Dictionary<MethodSignatureKey, Action<MonoBehaviour, object[]>>
        > _methodCache = new();

#if UNITY_2017_1_OR_NEWER
        private readonly HashSet<MonoBehaviour> _recipientCache = new();
        private readonly List<MonoBehaviour> _componentCache = new();
#endif

        private readonly RegistrationLog _log = new();
        internal readonly CyclicBuffer<MessageEmissionData> _emissionBuffer = new(
            GlobalMessageBufferSize
        );

        private bool _diagnosticsMode = GlobalDiagnosticsMode;

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

                if (count <= 1)
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
            HandlerCache<int, List<object>> prioritizedInterceptors =
                _untargetedInterceptsByType.GetOrAdd<T>();

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
                // maintain sorted order
                List<int> order = prioritizedInterceptors.order;
                int idx = 0;
                while (idx < order.Count && order[idx] < priority)
                {
                    idx++;
                }
                order.Insert(idx, priority);
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

            Type type = typeof(T);
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
                    if (_untargetedInterceptsByType.TryGetValue<T>(out prioritizedInterceptors))
                    {
                        prioritizedInterceptors.version++;
                        if (
                            prioritizedInterceptors.handlers.TryGetValue(priority, out interceptors)
                        )
                        {
                            complete = interceptors.Remove(interceptor);
                            if (interceptors.Count == 0)
                            {
                                _ = prioritizedInterceptors.handlers.Remove(priority);
                                int removeIdx = prioritizedInterceptors.order.IndexOf(priority);
                                if (removeIdx >= 0)
                                {
                                    prioritizedInterceptors.order.RemoveAt(removeIdx);
                                }
                            }
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

        public Action RegisterTargetedInterceptor<T>(
            TargetedInterceptor<T> interceptor,
            int priority = 0
        )
            where T : ITargetedMessage
        {
            HandlerCache<int, List<object>> prioritizedInterceptors =
                _targetedInterceptsByType.GetOrAdd<T>();

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
                // maintain sorted order
                List<int> order = prioritizedInterceptors.order;
                int idx = 0;
                while (idx < order.Count && order[idx] < priority)
                {
                    idx++;
                }
                order.Insert(idx, priority);
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

            Type type = typeof(T);
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
                    if (_targetedInterceptsByType.TryGetValue<T>(out prioritizedInterceptors))
                    {
                        prioritizedInterceptors.version++;
                        if (
                            prioritizedInterceptors.handlers.TryGetValue(priority, out interceptors)
                        )
                        {
                            complete = interceptors.Remove(interceptor);
                            if (interceptors.Count == 0)
                            {
                                _ = prioritizedInterceptors.handlers.Remove(priority);
                                int removeIdx = prioritizedInterceptors.order.IndexOf(priority);
                                if (removeIdx >= 0)
                                {
                                    prioritizedInterceptors.order.RemoveAt(removeIdx);
                                }
                            }
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

        public Action RegisterBroadcastInterceptor<T>(
            BroadcastInterceptor<T> interceptor,
            int priority = 0
        )
            where T : IBroadcastMessage
        {
            HandlerCache<int, List<object>> prioritizedInterceptors =
                _broadcastInterceptsByType.GetOrAdd<T>();

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
                // maintain sorted order
                List<int> order = prioritizedInterceptors.order;
                int idx = 0;
                while (idx < order.Count && order[idx] < priority)
                {
                    idx++;
                }
                order.Insert(idx, priority);
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

            Type type = typeof(T);
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
                    if (_broadcastInterceptsByType.TryGetValue<T>(out prioritizedInterceptors))
                    {
                        prioritizedInterceptors.version++;
                        if (
                            prioritizedInterceptors.handlers.TryGetValue(priority, out interceptors)
                        )
                        {
                            complete = interceptors.Remove(interceptor);
                            if (interceptors.Count == 0)
                            {
                                _ = prioritizedInterceptors.handlers.Remove(priority);
                                int removeIdx = prioritizedInterceptors.order.IndexOf(priority);
                                if (removeIdx >= 0)
                                {
                                    prioritizedInterceptors.order.RemoveAt(removeIdx);
                                }
                            }
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

        // Legacy RegisterInterceptor removed in favor of split implementations above

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

            Action<IUntargetedMessage> broadcast = Unsafe.As<Action<IUntargetedMessage>>(
                untargetedMethod
            );
            broadcast.Invoke(typedMessage);
        }

        public void UntargetedBroadcast<TMessage>(ref TMessage typedMessage)
            where TMessage : IUntargetedMessage
        {
            unchecked
            {
                _emissionId++;
            }
            if (_diagnosticsMode)
            {
                _emissionBuffer.Add(new MessageEmissionData(typedMessage));
            }

            // Pre-freeze post-processing stacks for this emission so mutations during
            // handlers/post-processors are not observed until the next emission.
            if (
                _postProcessingSinks.TryGetValue<TMessage>(
                    out HandlerCache<int, HandlerCache> prefreezeUntargetedPost
                )
                && prefreezeUntargetedPost.handlers.Count > 0
            )
            {
                List<KeyValuePair<int, HandlerCache>> frozen = GetOrAddMessageHandlerStack(
                    prefreezeUntargetedPost,
                    _emissionId
                );
                int frozenCount = frozen.Count;
                for (int i = 0; i < frozenCount; ++i)
                {
                    _ = GetOrAddMessageHandlerStack(frozen[i].Value, _emissionId);
                }
            }

            if (!RunUntargetedInterceptors(ref typedMessage))
            {
                return;
            }

            if (0 < _globalSinks.handlers.Count)
            {
                IUntargetedMessage untargetedMessage = typedMessage;
                BroadcastGlobalUntargeted(ref untargetedMessage);
            }

            bool foundAnyHandlers = InternalUntargetedBroadcast(ref typedMessage);

            if (
                _postProcessingSinks.TryGetValue<TMessage>(
                    out HandlerCache<int, HandlerCache> sortedHandlers
                )
                && 0 < sortedHandlers.handlers.Count
            )
            {
                List<KeyValuePair<int, HandlerCache>> handlerList = GetOrAddMessageHandlerStack(
                    sortedHandlers,
                    _emissionId
                );
                int handlerListCount = handlerList.Count;
                switch (handlerListCount)
                {
                    case 1:
                    {
                        KeyValuePair<int, HandlerCache> entry = handlerList[0];
                        RunUntargetedPostProcessing(ref typedMessage, entry.Key, entry.Value);
                        break;
                    }
                    case 2:
                    {
                        KeyValuePair<int, HandlerCache> entry = handlerList[0];
                        RunUntargetedPostProcessing(ref typedMessage, entry.Key, entry.Value);
                        entry = handlerList[1];
                        RunUntargetedPostProcessing(ref typedMessage, entry.Key, entry.Value);
                        break;
                    }
                    case 3:
                    {
                        KeyValuePair<int, HandlerCache> entry = handlerList[0];
                        RunUntargetedPostProcessing(ref typedMessage, entry.Key, entry.Value);
                        entry = handlerList[1];
                        RunUntargetedPostProcessing(ref typedMessage, entry.Key, entry.Value);
                        entry = handlerList[2];
                        RunUntargetedPostProcessing(ref typedMessage, entry.Key, entry.Value);
                        break;
                    }
                    case 4:
                    {
                        KeyValuePair<int, HandlerCache> entry = handlerList[0];
                        RunUntargetedPostProcessing(ref typedMessage, entry.Key, entry.Value);
                        entry = handlerList[1];
                        RunUntargetedPostProcessing(ref typedMessage, entry.Key, entry.Value);
                        entry = handlerList[2];
                        RunUntargetedPostProcessing(ref typedMessage, entry.Key, entry.Value);
                        entry = handlerList[3];
                        RunUntargetedPostProcessing(ref typedMessage, entry.Key, entry.Value);
                        break;
                    }
                    case 5:
                    {
                        KeyValuePair<int, HandlerCache> entry = handlerList[0];
                        RunUntargetedPostProcessing(ref typedMessage, entry.Key, entry.Value);
                        entry = handlerList[1];
                        RunUntargetedPostProcessing(ref typedMessage, entry.Key, entry.Value);
                        entry = handlerList[2];
                        RunUntargetedPostProcessing(ref typedMessage, entry.Key, entry.Value);
                        entry = handlerList[3];
                        RunUntargetedPostProcessing(ref typedMessage, entry.Key, entry.Value);
                        entry = handlerList[4];
                        RunUntargetedPostProcessing(ref typedMessage, entry.Key, entry.Value);
                        break;
                    }
                    default:
                    {
                        for (int i = 0; i < handlerListCount; ++i)
                        {
                            KeyValuePair<int, HandlerCache> entry = handlerList[i];
                            RunUntargetedPostProcessing(ref typedMessage, entry.Key, entry.Value);
                        }
                        break;
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
            HandlerCache cache
        )
            where TMessage : IUntargetedMessage
        {
            if (cache.handlers.Count == 0)
            {
                return;
            }

            List<MessageHandler> list = GetOrAddMessageHandlerStack(cache, _emissionId);
            switch (list.Count)
            {
                case 1:
                {
                    list[0].HandleUntargetedPostProcessing(ref typedMessage, this, priority);
                    return;
                }
                case 2:
                {
                    list[0].HandleUntargetedPostProcessing(ref typedMessage, this, priority);
                    list[1].HandleUntargetedPostProcessing(ref typedMessage, this, priority);
                    return;
                }
                case 3:
                {
                    list[0].HandleUntargetedPostProcessing(ref typedMessage, this, priority);
                    list[1].HandleUntargetedPostProcessing(ref typedMessage, this, priority);
                    list[2].HandleUntargetedPostProcessing(ref typedMessage, this, priority);
                    return;
                }
                case 4:
                {
                    list[0].HandleUntargetedPostProcessing(ref typedMessage, this, priority);
                    list[1].HandleUntargetedPostProcessing(ref typedMessage, this, priority);
                    list[2].HandleUntargetedPostProcessing(ref typedMessage, this, priority);
                    list[3].HandleUntargetedPostProcessing(ref typedMessage, this, priority);
                    return;
                }
                case 5:
                {
                    list[0].HandleUntargetedPostProcessing(ref typedMessage, this, priority);
                    list[1].HandleUntargetedPostProcessing(ref typedMessage, this, priority);
                    list[2].HandleUntargetedPostProcessing(ref typedMessage, this, priority);
                    list[3].HandleUntargetedPostProcessing(ref typedMessage, this, priority);
                    list[4].HandleUntargetedPostProcessing(ref typedMessage, this, priority);
                    return;
                }
            }

            for (int i = 0; i < cache.cache.Count; ++i)
            {
                MessageHandler handler = cache.cache[i];
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

            Action<InstanceId, ITargetedMessage> broadcast = Unsafe.As<
                Action<InstanceId, ITargetedMessage>
            >(targetedMethod);
            broadcast.Invoke(target, typedMessage);
        }

        public void TargetedBroadcast<TMessage>(ref InstanceId target, ref TMessage typedMessage)
            where TMessage : ITargetedMessage
        {
            unchecked
            {
                _emissionId++;
            }
            if (_diagnosticsMode)
            {
                _emissionBuffer.Add(new MessageEmissionData(typedMessage, target));
            }

            // Pre-freeze targeted post-processing for this emission (target-specific and without targeting)
            if (
                _postProcessingTargetedSinks.TryGetValue<TMessage>(
                    out Dictionary<InstanceId, HandlerCache<int, HandlerCache>> prefreezeTargeted
                )
                && prefreezeTargeted.TryGetValue(
                    target,
                    out HandlerCache<int, HandlerCache> prefreezeTargetedByPriority
                )
                && prefreezeTargetedByPriority.handlers.Count > 0
            )
            {
                List<KeyValuePair<int, HandlerCache>> frozen = GetOrAddMessageHandlerStack(
                    prefreezeTargetedByPriority,
                    _emissionId
                );
                int frozenCount = frozen.Count;
                for (int i = 0; i < frozenCount; ++i)
                {
                    _ = GetOrAddMessageHandlerStack(frozen[i].Value, _emissionId);
                }
            }
            if (
                _postProcessingTargetedWithoutTargetingSinks.TryGetValue<TMessage>(
                    out HandlerCache<int, HandlerCache> prefreezeTwt
                )
                && prefreezeTwt.handlers.Count > 0
            )
            {
                List<KeyValuePair<int, HandlerCache>> frozen = GetOrAddMessageHandlerStack(
                    prefreezeTwt,
                    _emissionId
                );
                int frozenCount = frozen.Count;
                for (int i = 0; i < frozenCount; ++i)
                {
                    _ = GetOrAddMessageHandlerStack(frozen[i].Value, _emissionId);
                }
            }

            if (!RunTargetedInterceptors(ref typedMessage, ref target))
            {
                return;
            }

            if (0 < _globalSinks.handlers.Count)
            {
                ITargetedMessage targetedMessage = typedMessage;
                BroadcastGlobalTargeted(ref target, ref targetedMessage);
            }

            bool foundAnyHandlers = false;

            if (typeof(TMessage) == typeof(ReflexiveMessage))
            {
#if UNITY_2017_1_OR_NEWER
                ref ReflexiveMessage reflexiveMessage = ref Unsafe.As<TMessage, ReflexiveMessage>(
                    ref typedMessage
                );

                GameObject go;
                bool found;
                UnityEngine.Object targetObject = target.Object;
                switch (targetObject)
                {
                    case GameObject gameObject:
                    {
                        found = true;
                        go = gameObject;
                        break;
                    }
                    case Component component:
                    {
                        found = true;
                        go = component.gameObject;
                        break;
                    }
                    default:
                    {
                        go = null;
                        found = false;
                        break;
                    }
                }

                if (found)
                {
                    _recipientCache.Clear();
                    bool sentInADirection = false;
                    ReflexiveSendMode sendMode = reflexiveMessage.sendMode;
                    if (sendMode.HasFlagNoAlloc(ReflexiveSendMode.Upwards))
                    {
                        sentInADirection = true;
                        if (
                            !sendMode.HasFlagNoAlloc(ReflexiveSendMode.Downwards)
                            && !sendMode.HasFlagNoAlloc(ReflexiveSendMode.Flat)
                            && !sendMode.HasFlagNoAlloc(ReflexiveSendMode.OnlyIncludeActive)
                        )
                        {
                            switch (reflexiveMessage.parameters.Length)
                            {
                                case 0:
                                {
                                    go.SendMessageUpwards(reflexiveMessage.method);
                                    break;
                                }
                                case 1:
                                {
                                    go.SendMessageUpwards(
                                        reflexiveMessage.method,
                                        reflexiveMessage.parameters[0]
                                    );
                                    break;
                                }
                                default:
                                {
                                    Transform current = go.transform;
                                    do
                                    {
                                        _componentCache.Clear();
                                        current.GetComponents(_componentCache);
                                        for (int i = 0; i < _componentCache.Count; ++i)
                                        {
                                            MonoBehaviour script = _componentCache[i];
                                            SendMessage(script, ref reflexiveMessage, false);
                                        }
                                        current = current.parent;
                                    } while (current != null);

                                    break;
                                }
                            }
                        }
                        else
                        {
                            Transform current = go.transform;
                            do
                            {
                                _componentCache.Clear();
                                current.GetComponents(_componentCache);
                                for (int i = 0; i < _componentCache.Count; ++i)
                                {
                                    MonoBehaviour script = _componentCache[i];
                                    SendMessage(script, ref reflexiveMessage, true);
                                }
                                current = current.parent;
                            } while (current != null);
                        }
                    }
                    if (sendMode.HasFlagNoAlloc(ReflexiveSendMode.Downwards))
                    {
                        if (
                            !sendMode.HasFlagNoAlloc(ReflexiveSendMode.Upwards)
                            && !sendMode.HasFlagNoAlloc(ReflexiveSendMode.Flat)
                            && !sendMode.HasFlagNoAlloc(ReflexiveSendMode.OnlyIncludeActive)
                        )
                        {
                            switch (reflexiveMessage.parameters.Length)
                            {
                                case 0:
                                {
                                    go.BroadcastMessage(reflexiveMessage.method);
                                    break;
                                }
                                case 1:
                                {
                                    go.BroadcastMessage(
                                        reflexiveMessage.method,
                                        reflexiveMessage.parameters[0]
                                    );
                                    break;
                                }
                                default:
                                {
                                    _componentCache.Clear();
                                    go.GetComponentsInChildren(true, _componentCache);
                                    for (int i = 0; i < _componentCache.Count; ++i)
                                    {
                                        MonoBehaviour parentComponent = _componentCache[i];
                                        SendMessage(parentComponent, ref reflexiveMessage, false);
                                    }

                                    break;
                                }
                            }
                        }
                        else
                        {
                            _componentCache.Clear();
                            go.GetComponentsInChildren(_componentCache);
                            for (int i = 0; i < _componentCache.Count; ++i)
                            {
                                MonoBehaviour parentComponent = _componentCache[i];
                                SendMessage(parentComponent, ref reflexiveMessage, true);
                            }
                        }
                    }
                    else if (!sentInADirection && sendMode.HasFlagNoAlloc(ReflexiveSendMode.Flat))
                    {
                        if (!sendMode.HasFlagNoAlloc(ReflexiveSendMode.OnlyIncludeActive))
                        {
                            switch (reflexiveMessage.parameters.Length)
                            {
                                case 0:
                                {
                                    go.SendMessage(reflexiveMessage.method);
                                    break;
                                }
                                case 1:
                                {
                                    go.SendMessage(
                                        reflexiveMessage.method,
                                        reflexiveMessage.parameters[0]
                                    );
                                    break;
                                }
                                default:
                                {
                                    _componentCache.Clear();
                                    go.GetComponents(_componentCache);
                                    for (int i = 0; i < _componentCache.Count; ++i)
                                    {
                                        MonoBehaviour component = _componentCache[i];
                                        SendMessage(component, ref reflexiveMessage, false);
                                    }

                                    break;
                                }
                            }
                        }
                        else
                        {
                            _componentCache.Clear();
                            go.GetComponents(_componentCache);
                            for (int i = 0; i < _componentCache.Count; ++i)
                            {
                                MonoBehaviour component = _componentCache[i];
                                SendMessage(component, ref reflexiveMessage, true);
                            }
                        }
                    }
                }
#else
                MessagingDebug.Log(
                    LogLevel.Error,
                    "Reflexive messages are not supported in this build."
                );
#endif
            }

            if (
                _targetedSinks.TryGetValue<TMessage>(
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
                    sortedHandlers,
                    _emissionId
                );
                int handlerListCount = handlerList.Count;
                switch (handlerListCount)
                {
                    case 1:
                    {
                        KeyValuePair<int, HandlerCache> entry = handlerList[0];
                        RunTargetedBroadcast(ref target, ref typedMessage, entry.Key, entry.Value);
                        break;
                    }
                    case 2:
                    {
                        KeyValuePair<int, HandlerCache> entry = handlerList[0];
                        RunTargetedBroadcast(ref target, ref typedMessage, entry.Key, entry.Value);
                        entry = handlerList[1];
                        RunTargetedBroadcast(ref target, ref typedMessage, entry.Key, entry.Value);
                        break;
                    }
                    case 3:
                    {
                        KeyValuePair<int, HandlerCache> entry = handlerList[0];
                        RunTargetedBroadcast(ref target, ref typedMessage, entry.Key, entry.Value);
                        entry = handlerList[1];
                        RunTargetedBroadcast(ref target, ref typedMessage, entry.Key, entry.Value);
                        entry = handlerList[2];
                        RunTargetedBroadcast(ref target, ref typedMessage, entry.Key, entry.Value);
                        break;
                    }
                    case 4:
                    {
                        KeyValuePair<int, HandlerCache> entry = handlerList[0];
                        RunTargetedBroadcast(ref target, ref typedMessage, entry.Key, entry.Value);
                        entry = handlerList[1];
                        RunTargetedBroadcast(ref target, ref typedMessage, entry.Key, entry.Value);
                        entry = handlerList[2];
                        RunTargetedBroadcast(ref target, ref typedMessage, entry.Key, entry.Value);
                        entry = handlerList[3];
                        RunTargetedBroadcast(ref target, ref typedMessage, entry.Key, entry.Value);
                        break;
                    }
                    case 5:
                    {
                        KeyValuePair<int, HandlerCache> entry = handlerList[0];
                        RunTargetedBroadcast(ref target, ref typedMessage, entry.Key, entry.Value);
                        entry = handlerList[1];
                        RunTargetedBroadcast(ref target, ref typedMessage, entry.Key, entry.Value);
                        entry = handlerList[2];
                        RunTargetedBroadcast(ref target, ref typedMessage, entry.Key, entry.Value);
                        entry = handlerList[3];
                        RunTargetedBroadcast(ref target, ref typedMessage, entry.Key, entry.Value);
                        entry = handlerList[4];
                        RunTargetedBroadcast(ref target, ref typedMessage, entry.Key, entry.Value);
                        break;
                    }
                    default:
                    {
                        for (int i = 0; i < handlerListCount; ++i)
                        {
                            KeyValuePair<int, HandlerCache> entry = handlerList[i];
                            RunTargetedBroadcast(
                                ref target,
                                ref typedMessage,
                                entry.Key,
                                entry.Value
                            );
                        }

                        break;
                    }
                }
            }

            _ = InternalTargetedWithoutTargetingBroadcast(ref target, ref typedMessage);

            if (
                _postProcessingTargetedSinks.TryGetValue<TMessage>(out targetedHandlers)
                && targetedHandlers.TryGetValue(target, out sortedHandlers)
                && 0 < sortedHandlers.handlers.Count
            )
            {
                foundAnyHandlers = true;
                List<KeyValuePair<int, HandlerCache>> handlerList = GetOrAddMessageHandlerStack(
                    sortedHandlers,
                    _emissionId
                );
                int handlerListCount = handlerList.Count;
                switch (handlerListCount)
                {
                    case 1:
                    {
                        KeyValuePair<int, HandlerCache> entry = handlerList[0];
                        RunTargetedPostProcessing(
                            ref target,
                            ref typedMessage,
                            entry.Key,
                            entry.Value
                        );
                        break;
                    }
                    case 2:
                    {
                        KeyValuePair<int, HandlerCache> entry = handlerList[0];
                        RunTargetedPostProcessing(
                            ref target,
                            ref typedMessage,
                            entry.Key,
                            entry.Value
                        );
                        entry = handlerList[1];
                        RunTargetedPostProcessing(
                            ref target,
                            ref typedMessage,
                            entry.Key,
                            entry.Value
                        );
                        break;
                    }
                    case 3:
                    {
                        KeyValuePair<int, HandlerCache> entry = handlerList[0];
                        RunTargetedPostProcessing(
                            ref target,
                            ref typedMessage,
                            entry.Key,
                            entry.Value
                        );
                        entry = handlerList[1];
                        RunTargetedPostProcessing(
                            ref target,
                            ref typedMessage,
                            entry.Key,
                            entry.Value
                        );
                        entry = handlerList[2];
                        RunTargetedPostProcessing(
                            ref target,
                            ref typedMessage,
                            entry.Key,
                            entry.Value
                        );
                        break;
                    }
                    case 4:
                    {
                        KeyValuePair<int, HandlerCache> entry = handlerList[0];
                        RunTargetedPostProcessing(
                            ref target,
                            ref typedMessage,
                            entry.Key,
                            entry.Value
                        );
                        entry = handlerList[1];
                        RunTargetedPostProcessing(
                            ref target,
                            ref typedMessage,
                            entry.Key,
                            entry.Value
                        );
                        entry = handlerList[2];
                        RunTargetedPostProcessing(
                            ref target,
                            ref typedMessage,
                            entry.Key,
                            entry.Value
                        );
                        entry = handlerList[3];
                        RunTargetedPostProcessing(
                            ref target,
                            ref typedMessage,
                            entry.Key,
                            entry.Value
                        );
                        break;
                    }
                    case 5:
                    {
                        KeyValuePair<int, HandlerCache> entry = handlerList[0];
                        RunTargetedPostProcessing(
                            ref target,
                            ref typedMessage,
                            entry.Key,
                            entry.Value
                        );
                        entry = handlerList[1];
                        RunTargetedPostProcessing(
                            ref target,
                            ref typedMessage,
                            entry.Key,
                            entry.Value
                        );
                        entry = handlerList[2];
                        RunTargetedPostProcessing(
                            ref target,
                            ref typedMessage,
                            entry.Key,
                            entry.Value
                        );
                        entry = handlerList[3];
                        RunTargetedPostProcessing(
                            ref target,
                            ref typedMessage,
                            entry.Key,
                            entry.Value
                        );
                        entry = handlerList[4];
                        RunTargetedPostProcessing(
                            ref target,
                            ref typedMessage,
                            entry.Key,
                            entry.Value
                        );
                        break;
                    }
                    default:
                    {
                        for (int i = 0; i < handlerListCount; ++i)
                        {
                            KeyValuePair<int, HandlerCache> entry = handlerList[i];
                            RunTargetedPostProcessing(
                                ref target,
                                ref typedMessage,
                                entry.Key,
                                entry.Value
                            );
                        }

                        break;
                    }
                }
            }

            if (
                _postProcessingTargetedWithoutTargetingSinks.TryGetValue<TMessage>(
                    out HandlerCache<int, HandlerCache> postTwt
                )
                && postTwt.handlers.Count > 0
            )
            {
                List<KeyValuePair<int, HandlerCache>> handlerList = GetOrAddMessageHandlerStack(
                    postTwt,
                    _emissionId
                );
                int handlerListCount = handlerList.Count;
                switch (handlerListCount)
                {
                    case 1:
                    {
                        KeyValuePair<int, HandlerCache> entry = handlerList[0];
                        RunTargetedWithoutTargetingPostProcessing(
                            ref target,
                            ref typedMessage,
                            entry.Key,
                            entry.Value
                        );
                        break;
                    }
                    case 2:
                    {
                        KeyValuePair<int, HandlerCache> entry = handlerList[0];
                        RunTargetedWithoutTargetingPostProcessing(
                            ref target,
                            ref typedMessage,
                            entry.Key,
                            entry.Value
                        );
                        entry = handlerList[1];
                        RunTargetedWithoutTargetingPostProcessing(
                            ref target,
                            ref typedMessage,
                            entry.Key,
                            entry.Value
                        );
                        break;
                    }
                    case 3:
                    {
                        KeyValuePair<int, HandlerCache> entry = handlerList[0];
                        RunTargetedWithoutTargetingPostProcessing(
                            ref target,
                            ref typedMessage,
                            entry.Key,
                            entry.Value
                        );
                        entry = handlerList[1];
                        RunTargetedWithoutTargetingPostProcessing(
                            ref target,
                            ref typedMessage,
                            entry.Key,
                            entry.Value
                        );
                        entry = handlerList[2];
                        RunTargetedWithoutTargetingPostProcessing(
                            ref target,
                            ref typedMessage,
                            entry.Key,
                            entry.Value
                        );
                        break;
                    }
                    case 4:
                    {
                        KeyValuePair<int, HandlerCache> entry = handlerList[0];
                        RunTargetedWithoutTargetingPostProcessing(
                            ref target,
                            ref typedMessage,
                            entry.Key,
                            entry.Value
                        );
                        entry = handlerList[1];
                        RunTargetedWithoutTargetingPostProcessing(
                            ref target,
                            ref typedMessage,
                            entry.Key,
                            entry.Value
                        );
                        entry = handlerList[2];
                        RunTargetedWithoutTargetingPostProcessing(
                            ref target,
                            ref typedMessage,
                            entry.Key,
                            entry.Value
                        );
                        entry = handlerList[3];
                        RunTargetedWithoutTargetingPostProcessing(
                            ref target,
                            ref typedMessage,
                            entry.Key,
                            entry.Value
                        );
                        break;
                    }
                    case 5:
                    {
                        KeyValuePair<int, HandlerCache> entry = handlerList[0];
                        RunTargetedWithoutTargetingPostProcessing(
                            ref target,
                            ref typedMessage,
                            entry.Key,
                            entry.Value
                        );
                        entry = handlerList[1];
                        RunTargetedWithoutTargetingPostProcessing(
                            ref target,
                            ref typedMessage,
                            entry.Key,
                            entry.Value
                        );
                        entry = handlerList[2];
                        RunTargetedWithoutTargetingPostProcessing(
                            ref target,
                            ref typedMessage,
                            entry.Key,
                            entry.Value
                        );
                        entry = handlerList[3];
                        RunTargetedWithoutTargetingPostProcessing(
                            ref target,
                            ref typedMessage,
                            entry.Key,
                            entry.Value
                        );
                        entry = handlerList[4];
                        RunTargetedWithoutTargetingPostProcessing(
                            ref target,
                            ref typedMessage,
                            entry.Key,
                            entry.Value
                        );
                        break;
                    }
                    default:
                    {
                        for (int i = 0; i < handlerListCount; ++i)
                        {
                            KeyValuePair<int, HandlerCache> entry = handlerList[i];
                            RunTargetedWithoutTargetingPostProcessing(
                                ref target,
                                ref typedMessage,
                                entry.Key,
                                entry.Value
                            );
                        }

                        break;
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
            HandlerCache cache
        )
            where TMessage : ITargetedMessage
        {
            if (cache.handlers.Count == 0)
            {
                return;
            }

            List<MessageHandler> messageHandlers = GetOrAddMessageHandlerStack(cache, _emissionId);
            int messageHandlersCount = messageHandlers.Count;
            switch (messageHandlersCount)
            {
                case 1:
                {
                    messageHandlers[0]
                        .HandleTargetedWithoutTargetingPostProcessing(
                            ref target,
                            ref typedMessage,
                            this,
                            priority
                        );
                    return;
                }
                case 2:
                {
                    messageHandlers[0]
                        .HandleTargetedWithoutTargetingPostProcessing(
                            ref target,
                            ref typedMessage,
                            this,
                            priority
                        );
                    messageHandlers[1]
                        .HandleTargetedWithoutTargetingPostProcessing(
                            ref target,
                            ref typedMessage,
                            this,
                            priority
                        );
                    return;
                }
                case 3:
                {
                    messageHandlers[0]
                        .HandleTargetedWithoutTargetingPostProcessing(
                            ref target,
                            ref typedMessage,
                            this,
                            priority
                        );
                    messageHandlers[1]
                        .HandleTargetedWithoutTargetingPostProcessing(
                            ref target,
                            ref typedMessage,
                            this,
                            priority
                        );
                    messageHandlers[2]
                        .HandleTargetedWithoutTargetingPostProcessing(
                            ref target,
                            ref typedMessage,
                            this,
                            priority
                        );
                    return;
                }
                case 4:
                {
                    messageHandlers[0]
                        .HandleTargetedWithoutTargetingPostProcessing(
                            ref target,
                            ref typedMessage,
                            this,
                            priority
                        );
                    messageHandlers[1]
                        .HandleTargetedWithoutTargetingPostProcessing(
                            ref target,
                            ref typedMessage,
                            this,
                            priority
                        );
                    messageHandlers[2]
                        .HandleTargetedWithoutTargetingPostProcessing(
                            ref target,
                            ref typedMessage,
                            this,
                            priority
                        );
                    messageHandlers[3]
                        .HandleTargetedWithoutTargetingPostProcessing(
                            ref target,
                            ref typedMessage,
                            this,
                            priority
                        );
                    return;
                }
                case 5:
                {
                    messageHandlers[0]
                        .HandleTargetedWithoutTargetingPostProcessing(
                            ref target,
                            ref typedMessage,
                            this,
                            priority
                        );
                    messageHandlers[1]
                        .HandleTargetedWithoutTargetingPostProcessing(
                            ref target,
                            ref typedMessage,
                            this,
                            priority
                        );
                    messageHandlers[2]
                        .HandleTargetedWithoutTargetingPostProcessing(
                            ref target,
                            ref typedMessage,
                            this,
                            priority
                        );
                    messageHandlers[3]
                        .HandleTargetedWithoutTargetingPostProcessing(
                            ref target,
                            ref typedMessage,
                            this,
                            priority
                        );
                    messageHandlers[4]
                        .HandleTargetedWithoutTargetingPostProcessing(
                            ref target,
                            ref typedMessage,
                            this,
                            priority
                        );
                    return;
                }
            }

            for (int i = 0; i < messageHandlersCount; ++i)
            {
                MessageHandler handler = messageHandlers[i];
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
            if (cache.handlers.Count == 0)
            {
                return;
            }

            List<MessageHandler> messageHandlers = GetOrAddMessageHandlerStack(cache, _emissionId);
            int messageHandlersCount = messageHandlers.Count;
            switch (messageHandlersCount)
            {
                case 1:
                {
                    messageHandlers[0]
                        .HandleTargetedPostProcessing(ref target, ref typedMessage, this, priority);
                    return;
                }
                case 2:
                {
                    messageHandlers[0]
                        .HandleTargetedPostProcessing(ref target, ref typedMessage, this, priority);
                    messageHandlers[1]
                        .HandleTargetedPostProcessing(ref target, ref typedMessage, this, priority);
                    return;
                }
                case 3:
                {
                    messageHandlers[0]
                        .HandleTargetedPostProcessing(ref target, ref typedMessage, this, priority);
                    messageHandlers[1]
                        .HandleTargetedPostProcessing(ref target, ref typedMessage, this, priority);
                    messageHandlers[2]
                        .HandleTargetedPostProcessing(ref target, ref typedMessage, this, priority);
                    return;
                }
                case 4:
                {
                    messageHandlers[0]
                        .HandleTargetedPostProcessing(ref target, ref typedMessage, this, priority);
                    messageHandlers[1]
                        .HandleTargetedPostProcessing(ref target, ref typedMessage, this, priority);
                    messageHandlers[2]
                        .HandleTargetedPostProcessing(ref target, ref typedMessage, this, priority);
                    messageHandlers[3]
                        .HandleTargetedPostProcessing(ref target, ref typedMessage, this, priority);
                    return;
                }
                case 5:
                {
                    messageHandlers[0]
                        .HandleTargetedPostProcessing(ref target, ref typedMessage, this, priority);
                    messageHandlers[1]
                        .HandleTargetedPostProcessing(ref target, ref typedMessage, this, priority);
                    messageHandlers[2]
                        .HandleTargetedPostProcessing(ref target, ref typedMessage, this, priority);
                    messageHandlers[3]
                        .HandleTargetedPostProcessing(ref target, ref typedMessage, this, priority);
                    messageHandlers[4]
                        .HandleTargetedPostProcessing(ref target, ref typedMessage, this, priority);
                    return;
                }
            }

            for (int i = 0; i < messageHandlersCount; ++i)
            {
                MessageHandler handler = messageHandlers[i];
                handler.HandleTargetedPostProcessing(ref target, ref typedMessage, this, priority);
            }
        }

        private void RunTargetedBroadcast<TMessage>(
            ref InstanceId target,
            ref TMessage typedMessage,
            int priority,
            HandlerCache cache
        )
            where TMessage : ITargetedMessage
        {
            if (cache.handlers.Count == 0)
            {
                return;
            }

            List<MessageHandler> messageHandlers = GetOrAddMessageHandlerStack(cache, _emissionId);
            int messageHandlersCount = messageHandlers.Count;
            switch (messageHandlersCount)
            {
                case 1:
                {
                    messageHandlers[0].HandleTargeted(ref target, ref typedMessage, this, priority);
                    return;
                }
                case 2:
                {
                    messageHandlers[0].HandleTargeted(ref target, ref typedMessage, this, priority);
                    messageHandlers[1].HandleTargeted(ref target, ref typedMessage, this, priority);
                    return;
                }
                case 3:
                {
                    messageHandlers[0].HandleTargeted(ref target, ref typedMessage, this, priority);
                    messageHandlers[1].HandleTargeted(ref target, ref typedMessage, this, priority);
                    messageHandlers[2].HandleTargeted(ref target, ref typedMessage, this, priority);
                    return;
                }
                case 4:
                {
                    messageHandlers[0].HandleTargeted(ref target, ref typedMessage, this, priority);
                    messageHandlers[1].HandleTargeted(ref target, ref typedMessage, this, priority);
                    messageHandlers[2].HandleTargeted(ref target, ref typedMessage, this, priority);
                    messageHandlers[3].HandleTargeted(ref target, ref typedMessage, this, priority);
                    return;
                }
                case 5:
                {
                    messageHandlers[0].HandleTargeted(ref target, ref typedMessage, this, priority);
                    messageHandlers[1].HandleTargeted(ref target, ref typedMessage, this, priority);
                    messageHandlers[2].HandleTargeted(ref target, ref typedMessage, this, priority);
                    messageHandlers[3].HandleTargeted(ref target, ref typedMessage, this, priority);
                    messageHandlers[4].HandleTargeted(ref target, ref typedMessage, this, priority);
                    return;
                }
            }

            for (int i = 0; i < messageHandlersCount; ++i)
            {
                MessageHandler handler = messageHandlers[i];
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

            Action<InstanceId, IBroadcastMessage> broadcast = Unsafe.As<
                Action<InstanceId, IBroadcastMessage>
            >(sourcedBroadcastMethod);
            broadcast.Invoke(source, typedMessage);
        }

        public void SourcedBroadcast<TMessage>(ref InstanceId source, ref TMessage typedMessage)
            where TMessage : IBroadcastMessage
        {
            unchecked
            {
                _emissionId++;
            }
            if (_diagnosticsMode)
            {
                _emissionBuffer.Add(new MessageEmissionData(typedMessage, source));
            }

            // Pre-freeze broadcast post-processing for this emission (source-specific and without source)
            if (
                _postProcessingBroadcastSinks.TryGetValue<TMessage>(
                    out Dictionary<InstanceId, HandlerCache<int, HandlerCache>> prefreezeBroadcast
                )
                && prefreezeBroadcast.TryGetValue(
                    source,
                    out HandlerCache<int, HandlerCache> prefreezeBroadcastByPriority
                )
                && prefreezeBroadcastByPriority.handlers.Count > 0
            )
            {
                List<KeyValuePair<int, HandlerCache>> frozen = GetOrAddMessageHandlerStack(
                    prefreezeBroadcastByPriority,
                    _emissionId
                );
                int frozenCount = frozen.Count;
                for (int i = 0; i < frozenCount; ++i)
                {
                    KeyValuePair<int, HandlerCache> entry = frozen[i];
                    List<MessageHandler> mhList = GetOrAddMessageHandlerStack(
                        entry.Value,
                        _emissionId
                    );
                    for (int h = 0; h < mhList.Count; ++h)
                    {
                        mhList[h]
                            .PrefreezeBroadcastPostProcessorsForEmission<TMessage>(
                                source,
                                entry.Key,
                                _emissionId,
                                this
                            );
                    }
                }
            }
            if (
                _postProcessingBroadcastWithoutSourceSinks.TryGetValue<TMessage>(
                    out HandlerCache<int, HandlerCache> prefreezeBws
                )
                && prefreezeBws.handlers.Count > 0
            )
            {
                List<KeyValuePair<int, HandlerCache>> frozen = GetOrAddMessageHandlerStack(
                    prefreezeBws,
                    _emissionId
                );
                int frozenCount = frozen.Count;
                for (int i = 0; i < frozenCount; ++i)
                {
                    _ = GetOrAddMessageHandlerStack(frozen[i].Value, _emissionId);
                }
            }

            if (!RunBroadcastInterceptors(ref typedMessage, ref source))
            {
                return;
            }

            if (0 < _globalSinks.handlers.Count)
            {
                IBroadcastMessage broadcastMessage = typedMessage;
                BroadcastGlobalSourcedBroadcast(ref source, ref broadcastMessage);
            }

            bool foundAnyHandlers = false;
            Dictionary<InstanceId, HandlerCache<int, HandlerCache>> broadcastHandlers;
            _ = _broadcastSinks.TryGetValue<TMessage>(out broadcastHandlers);
            if (
                broadcastHandlers != null
                && broadcastHandlers.TryGetValue(
                    source,
                    out HandlerCache<int, HandlerCache> sortedHandlers
                )
                && 0 < sortedHandlers.handlers.Count
            )
            {
                foundAnyHandlers = true;
                List<KeyValuePair<int, HandlerCache>> handlerList = GetOrAddMessageHandlerStack(
                    sortedHandlers,
                    _emissionId
                );
                int handlerListCount = handlerList.Count;
                switch (handlerListCount)
                {
                    case 1:
                    {
                        KeyValuePair<int, HandlerCache> entry = handlerList[0];
                        RunBroadcast(ref source, ref typedMessage, entry.Key, entry.Value);
                        break;
                    }
                    case 2:
                    {
                        KeyValuePair<int, HandlerCache> entry = handlerList[0];
                        RunBroadcast(ref source, ref typedMessage, entry.Key, entry.Value);
                        entry = handlerList[1];
                        RunBroadcast(ref source, ref typedMessage, entry.Key, entry.Value);
                        break;
                    }
                    case 3:
                    {
                        KeyValuePair<int, HandlerCache> entry = handlerList[0];
                        RunBroadcast(ref source, ref typedMessage, entry.Key, entry.Value);
                        entry = handlerList[1];
                        RunBroadcast(ref source, ref typedMessage, entry.Key, entry.Value);
                        entry = handlerList[2];
                        RunBroadcast(ref source, ref typedMessage, entry.Key, entry.Value);
                        break;
                    }
                    case 4:
                    {
                        KeyValuePair<int, HandlerCache> entry = handlerList[0];
                        RunBroadcast(ref source, ref typedMessage, entry.Key, entry.Value);
                        entry = handlerList[1];
                        RunBroadcast(ref source, ref typedMessage, entry.Key, entry.Value);
                        entry = handlerList[2];
                        RunBroadcast(ref source, ref typedMessage, entry.Key, entry.Value);
                        entry = handlerList[3];
                        RunBroadcast(ref source, ref typedMessage, entry.Key, entry.Value);
                        break;
                    }
                    case 5:
                    {
                        KeyValuePair<int, HandlerCache> entry = handlerList[0];
                        RunBroadcast(ref source, ref typedMessage, entry.Key, entry.Value);
                        entry = handlerList[1];
                        RunBroadcast(ref source, ref typedMessage, entry.Key, entry.Value);
                        entry = handlerList[2];
                        RunBroadcast(ref source, ref typedMessage, entry.Key, entry.Value);
                        entry = handlerList[3];
                        RunBroadcast(ref source, ref typedMessage, entry.Key, entry.Value);
                        entry = handlerList[4];
                        RunBroadcast(ref source, ref typedMessage, entry.Key, entry.Value);
                        break;
                    }
                    default:
                    {
                        for (int i = 0; i < handlerListCount; ++i)
                        {
                            KeyValuePair<int, HandlerCache> entry = handlerList[i];
                            RunBroadcast(ref source, ref typedMessage, entry.Key, entry.Value);
                        }

                        break;
                    }
                }
            }

            _ = InternalBroadcastWithoutSource(ref source, ref typedMessage);

            if (
                _postProcessingBroadcastSinks.TryGetValue<TMessage>(out broadcastHandlers)
                && broadcastHandlers.TryGetValue(source, out sortedHandlers)
                && 0 < sortedHandlers.handlers.Count
            )
            {
                foundAnyHandlers = true;
                List<KeyValuePair<int, HandlerCache>> handlerList = GetOrAddMessageHandlerStack(
                    sortedHandlers,
                    _emissionId
                );
                int handlerListCount = handlerList.Count;
                switch (handlerListCount)
                {
                    case 1:
                    {
                        KeyValuePair<int, HandlerCache> entry = handlerList[0];
                        RunBroadcastPostProcessing(
                            ref source,
                            ref typedMessage,
                            entry.Key,
                            entry.Value
                        );
                        break;
                    }
                    case 2:
                    {
                        KeyValuePair<int, HandlerCache> entry = handlerList[0];
                        RunBroadcastPostProcessing(
                            ref source,
                            ref typedMessage,
                            entry.Key,
                            entry.Value
                        );
                        entry = handlerList[1];
                        RunBroadcastPostProcessing(
                            ref source,
                            ref typedMessage,
                            entry.Key,
                            entry.Value
                        );
                        break;
                    }
                    case 3:
                    {
                        KeyValuePair<int, HandlerCache> entry = handlerList[0];
                        RunBroadcastPostProcessing(
                            ref source,
                            ref typedMessage,
                            entry.Key,
                            entry.Value
                        );
                        entry = handlerList[1];
                        RunBroadcastPostProcessing(
                            ref source,
                            ref typedMessage,
                            entry.Key,
                            entry.Value
                        );
                        entry = handlerList[2];
                        RunBroadcastPostProcessing(
                            ref source,
                            ref typedMessage,
                            entry.Key,
                            entry.Value
                        );
                        break;
                    }
                    case 4:
                    {
                        KeyValuePair<int, HandlerCache> entry = handlerList[0];
                        RunBroadcastPostProcessing(
                            ref source,
                            ref typedMessage,
                            entry.Key,
                            entry.Value
                        );
                        entry = handlerList[1];
                        RunBroadcastPostProcessing(
                            ref source,
                            ref typedMessage,
                            entry.Key,
                            entry.Value
                        );
                        entry = handlerList[2];
                        RunBroadcastPostProcessing(
                            ref source,
                            ref typedMessage,
                            entry.Key,
                            entry.Value
                        );
                        entry = handlerList[3];
                        RunBroadcastPostProcessing(
                            ref source,
                            ref typedMessage,
                            entry.Key,
                            entry.Value
                        );
                        break;
                    }
                    case 5:
                    {
                        KeyValuePair<int, HandlerCache> entry = handlerList[0];
                        RunBroadcastPostProcessing(
                            ref source,
                            ref typedMessage,
                            entry.Key,
                            entry.Value
                        );
                        entry = handlerList[1];
                        RunBroadcastPostProcessing(
                            ref source,
                            ref typedMessage,
                            entry.Key,
                            entry.Value
                        );
                        entry = handlerList[2];
                        RunBroadcastPostProcessing(
                            ref source,
                            ref typedMessage,
                            entry.Key,
                            entry.Value
                        );
                        entry = handlerList[3];
                        RunBroadcastPostProcessing(
                            ref source,
                            ref typedMessage,
                            entry.Key,
                            entry.Value
                        );
                        entry = handlerList[4];
                        RunBroadcastPostProcessing(
                            ref source,
                            ref typedMessage,
                            entry.Key,
                            entry.Value
                        );
                        break;
                    }
                    default:
                    {
                        for (int i = 0; i < handlerListCount; ++i)
                        {
                            KeyValuePair<int, HandlerCache> entry = handlerList[i];
                            RunBroadcastPostProcessing(
                                ref source,
                                ref typedMessage,
                                entry.Key,
                                entry.Value
                            );
                        }

                        break;
                    }
                }
            }

            if (
                _postProcessingBroadcastWithoutSourceSinks.TryGetValue<TMessage>(out sortedHandlers)
                && 0 < sortedHandlers.handlers.Count
            )
            {
                List<KeyValuePair<int, HandlerCache>> handlerList = GetOrAddMessageHandlerStack(
                    sortedHandlers,
                    _emissionId
                );
                int handlerListCount = handlerList.Count;
                for (int i = 0; i < handlerListCount; ++i)
                {
                    KeyValuePair<int, HandlerCache> entry = handlerList[i];
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
            List<MessageHandler> messageHandlers = GetOrAddMessageHandlerStack(cache, _emissionId);
            int messageHandlersCount = messageHandlers.Count;
            if (messageHandlersCount == 0)
            {
                return;
            }
            for (int i = 0; i < messageHandlersCount; ++i)
            {
                MessageHandler handler = messageHandlers[i];
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
            if (cache.handlers.Count == 0)
            {
                return;
            }
            List<MessageHandler> messageHandlers = GetOrAddMessageHandlerStack(cache, _emissionId);
            int messageHandlersCount = messageHandlers.Count;
            switch (messageHandlersCount)
            {
                case 1:
                {
                    messageHandlers[0]
                        .HandleSourcedBroadcastPostProcessing(
                            ref source,
                            ref typedMessage,
                            this,
                            priority
                        );
                    return;
                }
                case 2:
                {
                    messageHandlers[0]
                        .HandleSourcedBroadcastPostProcessing(
                            ref source,
                            ref typedMessage,
                            this,
                            priority
                        );
                    messageHandlers[1]
                        .HandleSourcedBroadcastPostProcessing(
                            ref source,
                            ref typedMessage,
                            this,
                            priority
                        );
                    return;
                }
                case 3:
                {
                    messageHandlers[0]
                        .HandleSourcedBroadcastPostProcessing(
                            ref source,
                            ref typedMessage,
                            this,
                            priority
                        );
                    messageHandlers[1]
                        .HandleSourcedBroadcastPostProcessing(
                            ref source,
                            ref typedMessage,
                            this,
                            priority
                        );
                    messageHandlers[2]
                        .HandleSourcedBroadcastPostProcessing(
                            ref source,
                            ref typedMessage,
                            this,
                            priority
                        );
                    return;
                }
                case 4:
                {
                    messageHandlers[0]
                        .HandleSourcedBroadcastPostProcessing(
                            ref source,
                            ref typedMessage,
                            this,
                            priority
                        );
                    messageHandlers[1]
                        .HandleSourcedBroadcastPostProcessing(
                            ref source,
                            ref typedMessage,
                            this,
                            priority
                        );
                    messageHandlers[2]
                        .HandleSourcedBroadcastPostProcessing(
                            ref source,
                            ref typedMessage,
                            this,
                            priority
                        );
                    messageHandlers[3]
                        .HandleSourcedBroadcastPostProcessing(
                            ref source,
                            ref typedMessage,
                            this,
                            priority
                        );
                    return;
                }
                case 5:
                {
                    messageHandlers[0]
                        .HandleSourcedBroadcastPostProcessing(
                            ref source,
                            ref typedMessage,
                            this,
                            priority
                        );
                    messageHandlers[1]
                        .HandleSourcedBroadcastPostProcessing(
                            ref source,
                            ref typedMessage,
                            this,
                            priority
                        );
                    messageHandlers[2]
                        .HandleSourcedBroadcastPostProcessing(
                            ref source,
                            ref typedMessage,
                            this,
                            priority
                        );
                    messageHandlers[3]
                        .HandleSourcedBroadcastPostProcessing(
                            ref source,
                            ref typedMessage,
                            this,
                            priority
                        );
                    messageHandlers[4]
                        .HandleSourcedBroadcastPostProcessing(
                            ref source,
                            ref typedMessage,
                            this,
                            priority
                        );
                    return;
                }
            }

            for (int i = 0; i < messageHandlersCount; ++i)
            {
                MessageHandler handler = messageHandlers[i];
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
            if (cache.handlers.Count == 0)
            {
                return;
            }

            List<MessageHandler> messageHandlers = GetOrAddMessageHandlerStack(cache, _emissionId);
            int messageHandlersCount = messageHandlers.Count;
            switch (messageHandlersCount)
            {
                case 1:
                {
                    messageHandlers[0]
                        .HandleSourcedBroadcast(ref source, ref typedMessage, this, priority);
                    return;
                }
                case 2:
                {
                    messageHandlers[0]
                        .HandleSourcedBroadcast(ref source, ref typedMessage, this, priority);
                    messageHandlers[1]
                        .HandleSourcedBroadcast(ref source, ref typedMessage, this, priority);
                    return;
                }
                case 3:
                {
                    messageHandlers[0]
                        .HandleSourcedBroadcast(ref source, ref typedMessage, this, priority);
                    messageHandlers[1]
                        .HandleSourcedBroadcast(ref source, ref typedMessage, this, priority);
                    messageHandlers[2]
                        .HandleSourcedBroadcast(ref source, ref typedMessage, this, priority);
                    return;
                }
                case 4:
                {
                    messageHandlers[0]
                        .HandleSourcedBroadcast(ref source, ref typedMessage, this, priority);
                    messageHandlers[1]
                        .HandleSourcedBroadcast(ref source, ref typedMessage, this, priority);
                    messageHandlers[2]
                        .HandleSourcedBroadcast(ref source, ref typedMessage, this, priority);
                    messageHandlers[3]
                        .HandleSourcedBroadcast(ref source, ref typedMessage, this, priority);
                    return;
                }
                case 5:
                {
                    messageHandlers[0]
                        .HandleSourcedBroadcast(ref source, ref typedMessage, this, priority);
                    messageHandlers[1]
                        .HandleSourcedBroadcast(ref source, ref typedMessage, this, priority);
                    messageHandlers[2]
                        .HandleSourcedBroadcast(ref source, ref typedMessage, this, priority);
                    messageHandlers[3]
                        .HandleSourcedBroadcast(ref source, ref typedMessage, this, priority);
                    messageHandlers[4]
                        .HandleSourcedBroadcast(ref source, ref typedMessage, this, priority);
                    return;
                }
            }

            for (int i = 0; i < messageHandlersCount; ++i)
            {
                MessageHandler handler = messageHandlers[i];
                handler.HandleSourcedBroadcast(ref source, ref typedMessage, this, priority);
            }
        }

        private void BroadcastGlobalUntargeted(ref IUntargetedMessage message)
        {
            if (_globalSinks.handlers.Count == 0)
            {
                return;
            }

            List<MessageHandler> messageHandlers = GetOrAddMessageHandlerStack(
                _globalSinks,
                _emissionId
            );
            int messageHandlersCount = messageHandlers.Count;
            switch (messageHandlersCount)
            {
                case 1:
                {
                    messageHandlers[0].HandleGlobalUntargetedMessage(ref message, this);
                    return;
                }
                case 2:
                {
                    messageHandlers[0].HandleGlobalUntargetedMessage(ref message, this);
                    messageHandlers[1].HandleGlobalUntargetedMessage(ref message, this);
                    return;
                }
                case 3:
                {
                    messageHandlers[0].HandleGlobalUntargetedMessage(ref message, this);
                    messageHandlers[1].HandleGlobalUntargetedMessage(ref message, this);
                    messageHandlers[2].HandleGlobalUntargetedMessage(ref message, this);
                    return;
                }
                case 4:
                {
                    messageHandlers[0].HandleGlobalUntargetedMessage(ref message, this);
                    messageHandlers[1].HandleGlobalUntargetedMessage(ref message, this);
                    messageHandlers[2].HandleGlobalUntargetedMessage(ref message, this);
                    messageHandlers[3].HandleGlobalUntargetedMessage(ref message, this);
                    return;
                }
                case 5:
                {
                    messageHandlers[0].HandleGlobalUntargetedMessage(ref message, this);
                    messageHandlers[1].HandleGlobalUntargetedMessage(ref message, this);
                    messageHandlers[2].HandleGlobalUntargetedMessage(ref message, this);
                    messageHandlers[3].HandleGlobalUntargetedMessage(ref message, this);
                    messageHandlers[4].HandleGlobalUntargetedMessage(ref message, this);
                    return;
                }
            }

            for (int i = 0; i < messageHandlersCount; ++i)
            {
                MessageHandler handler = messageHandlers[i];
                handler.HandleGlobalUntargetedMessage(ref message, this);
            }
        }

        private void BroadcastGlobalTargeted(ref InstanceId target, ref ITargetedMessage message)
        {
            if (_globalSinks.handlers.Count == 0)
            {
                return;
            }

            List<MessageHandler> messageHandlers = GetOrAddMessageHandlerStack(
                _globalSinks,
                _emissionId
            );
            int messageHandlersCount = messageHandlers.Count;
            switch (messageHandlersCount)
            {
                case 1:
                {
                    messageHandlers[0].HandleGlobalTargetedMessage(ref target, ref message, this);
                    return;
                }
                case 2:
                {
                    messageHandlers[0].HandleGlobalTargetedMessage(ref target, ref message, this);
                    messageHandlers[1].HandleGlobalTargetedMessage(ref target, ref message, this);
                    return;
                }
                case 3:
                {
                    messageHandlers[0].HandleGlobalTargetedMessage(ref target, ref message, this);
                    messageHandlers[1].HandleGlobalTargetedMessage(ref target, ref message, this);
                    messageHandlers[2].HandleGlobalTargetedMessage(ref target, ref message, this);
                    return;
                }
                case 4:
                {
                    messageHandlers[0].HandleGlobalTargetedMessage(ref target, ref message, this);
                    messageHandlers[1].HandleGlobalTargetedMessage(ref target, ref message, this);
                    messageHandlers[2].HandleGlobalTargetedMessage(ref target, ref message, this);
                    messageHandlers[3].HandleGlobalTargetedMessage(ref target, ref message, this);
                    return;
                }
                case 5:
                {
                    messageHandlers[0].HandleGlobalTargetedMessage(ref target, ref message, this);
                    messageHandlers[1].HandleGlobalTargetedMessage(ref target, ref message, this);
                    messageHandlers[2].HandleGlobalTargetedMessage(ref target, ref message, this);
                    messageHandlers[3].HandleGlobalTargetedMessage(ref target, ref message, this);
                    messageHandlers[4].HandleGlobalTargetedMessage(ref target, ref message, this);
                    return;
                }
            }

            for (int i = 0; i < messageHandlersCount; ++i)
            {
                MessageHandler handler = messageHandlers[i];
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

            List<MessageHandler> messageHandlers = GetOrAddMessageHandlerStack(
                _globalSinks,
                _emissionId
            );
            int messageHandlersCount = messageHandlers.Count;
            switch (messageHandlersCount)
            {
                case 1:
                {
                    messageHandlers[0]
                        .HandleGlobalSourcedBroadcastMessage(ref source, ref message, this);
                    return;
                }
                case 2:
                {
                    messageHandlers[0]
                        .HandleGlobalSourcedBroadcastMessage(ref source, ref message, this);
                    messageHandlers[1]
                        .HandleGlobalSourcedBroadcastMessage(ref source, ref message, this);
                    return;
                }
                case 3:
                {
                    messageHandlers[0]
                        .HandleGlobalSourcedBroadcastMessage(ref source, ref message, this);
                    messageHandlers[1]
                        .HandleGlobalSourcedBroadcastMessage(ref source, ref message, this);
                    messageHandlers[2]
                        .HandleGlobalSourcedBroadcastMessage(ref source, ref message, this);
                    return;
                }
                case 4:
                {
                    messageHandlers[0]
                        .HandleGlobalSourcedBroadcastMessage(ref source, ref message, this);
                    messageHandlers[1]
                        .HandleGlobalSourcedBroadcastMessage(ref source, ref message, this);
                    messageHandlers[2]
                        .HandleGlobalSourcedBroadcastMessage(ref source, ref message, this);
                    messageHandlers[3]
                        .HandleGlobalSourcedBroadcastMessage(ref source, ref message, this);
                    return;
                }
                case 5:
                {
                    messageHandlers[0]
                        .HandleGlobalSourcedBroadcastMessage(ref source, ref message, this);
                    messageHandlers[1]
                        .HandleGlobalSourcedBroadcastMessage(ref source, ref message, this);
                    messageHandlers[2]
                        .HandleGlobalSourcedBroadcastMessage(ref source, ref message, this);
                    messageHandlers[3]
                        .HandleGlobalSourcedBroadcastMessage(ref source, ref message, this);
                    messageHandlers[4]
                        .HandleGlobalSourcedBroadcastMessage(ref source, ref message, this);
                    return;
                }
            }

            for (int i = 0; i < messageHandlersCount; ++i)
            {
                MessageHandler handler = messageHandlers[i];
                handler.HandleGlobalSourcedBroadcastMessage(ref source, ref message, this);
            }
        }

        private bool TryGetUntargetedInterceptorCaches<TMessage>(
            out List<KeyValuePair<int, List<object>>> interceptorStack,
            out List<object> interceptorObjects
        )
            where TMessage : IUntargetedMessage
        {
            if (
                !_untargetedInterceptsByType.TryGetValue<TMessage>(
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
                List<int> keys = interceptors.order;
                for (int i = 0; i < keys.Count; ++i)
                {
                    int key = keys[i];
                    if (interceptors.handlers.TryGetValue(key, out List<object> values))
                    {
                        interceptorStack.Add(new KeyValuePair<int, List<object>>(key, values));
                    }
                }

                interceptors.lastSeenVersion = interceptors.version;
            }

            if (!_innerInterceptorsStack.TryPop(out interceptorObjects))
            {
                interceptorObjects = new List<object>();
            }

            return true;
        }

        private bool TryGetTargetedInterceptorCaches<TMessage>(
            out List<KeyValuePair<int, List<object>>> interceptorStack,
            out List<object> interceptorObjects
        )
            where TMessage : ITargetedMessage
        {
            if (
                !_targetedInterceptsByType.TryGetValue<TMessage>(
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
                List<int> keys = interceptors.order;
                for (int i = 0; i < keys.Count; ++i)
                {
                    int key = keys[i];
                    if (interceptors.handlers.TryGetValue(key, out List<object> values))
                    {
                        interceptorStack.Add(new KeyValuePair<int, List<object>>(key, values));
                    }
                }

                interceptors.lastSeenVersion = interceptors.version;
            }

            if (!_innerInterceptorsStack.TryPop(out interceptorObjects))
            {
                interceptorObjects = new List<object>();
            }

            return true;
        }

        private bool TryGetBroadcastInterceptorCaches<TMessage>(
            out List<KeyValuePair<int, List<object>>> interceptorStack,
            out List<object> interceptorObjects
        )
            where TMessage : IBroadcastMessage
        {
            if (
                !_broadcastInterceptsByType.TryGetValue<TMessage>(
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
                List<int> keys = interceptors.order;
                for (int i = 0; i < keys.Count; ++i)
                {
                    int key = keys[i];
                    if (interceptors.handlers.TryGetValue(key, out List<object> values))
                    {
                        interceptorStack.Add(new KeyValuePair<int, List<object>>(key, values));
                    }
                }

                interceptors.lastSeenVersion = interceptors.version;
            }

            if (!_innerInterceptorsStack.TryPop(out interceptorObjects))
            {
                interceptorObjects = new List<object>();
            }

            return true;
        }

        private bool RunUntargetedInterceptors<T>(ref T message)
            where T : IUntargetedMessage
        {
            if (
                !TryGetUntargetedInterceptorCaches<T>(
                    out List<KeyValuePair<int, List<object>>> interceptorStack,
                    out List<object> interceptorObjects
                )
            )
            {
                return true;
            }

            try
            {
                for (int s = 0; s < interceptorStack.Count; ++s)
                {
                    KeyValuePair<int, List<object>> entry = interceptorStack[s];
                    interceptorObjects.Clear();
                    List<object> interceptors = entry.Value;
                    interceptorObjects.AddRange(interceptors);

                    for (int i = 0; i < interceptorObjects.Count; ++i)
                    {
                        UntargetedInterceptor<T> typedTransformer = Unsafe.As<
                            UntargetedInterceptor<T>
                        >(interceptorObjects[i]);
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

        private bool RunTargetedInterceptors<T>(ref T message, ref InstanceId target)
            where T : ITargetedMessage
        {
            if (
                !TryGetTargetedInterceptorCaches<T>(
                    out List<KeyValuePair<int, List<object>>> interceptorStack,
                    out List<object> interceptorObjects
                )
            )
            {
                return true;
            }

            try
            {
                for (int s = 0; s < interceptorStack.Count; ++s)
                {
                    KeyValuePair<int, List<object>> entry = interceptorStack[s];
                    interceptorObjects.Clear();
                    List<object> interceptors = entry.Value;
                    interceptorObjects.AddRange(interceptors);

                    for (int i = 0; i < interceptorObjects.Count; ++i)
                    {
                        TargetedInterceptor<T> typedTransformer = Unsafe.As<TargetedInterceptor<T>>(
                            interceptorObjects[i]
                        );
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

        private bool RunBroadcastInterceptors<T>(ref T message, ref InstanceId source)
            where T : IBroadcastMessage
        {
            if (
                !TryGetBroadcastInterceptorCaches<T>(
                    out List<KeyValuePair<int, List<object>>> interceptorStack,
                    out List<object> interceptorObjects
                )
            )
            {
                return true;
            }

            try
            {
                for (int s = 0; s < interceptorStack.Count; ++s)
                {
                    KeyValuePair<int, List<object>> entry = interceptorStack[s];
                    interceptorObjects.Clear();
                    List<object> interceptors = entry.Value;
                    interceptorObjects.AddRange(interceptors);

                    for (int i = 0; i < interceptorObjects.Count; ++i)
                    {
                        BroadcastInterceptor<T> typedTransformer = Unsafe.As<
                            BroadcastInterceptor<T>
                        >(interceptorObjects[i]);
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

        private bool InternalUntargetedBroadcast<TMessage>(ref TMessage message)
            where TMessage : IMessage
        {
            if (
                !_sinks.TryGetValue<TMessage>(out HandlerCache<int, HandlerCache> sortedHandlers)
                || sortedHandlers.handlers.Count == 0
            )
            {
                return false;
            }

            List<KeyValuePair<int, HandlerCache>> handlerList = GetOrAddMessageHandlerStack(
                sortedHandlers,
                _emissionId
            );

            int handlerListCount = handlerList.Count;
            switch (handlerListCount)
            {
                case 1:
                {
                    KeyValuePair<int, HandlerCache> entry = handlerList[0];
                    RunUntargetedBroadcast(ref message, entry.Key, entry.Value);
                    return true;
                }
                case 2:
                {
                    KeyValuePair<int, HandlerCache> entry = handlerList[0];
                    RunUntargetedBroadcast(ref message, entry.Key, entry.Value);
                    entry = handlerList[1];
                    RunUntargetedBroadcast(ref message, entry.Key, entry.Value);
                    return true;
                }
                case 3:
                {
                    KeyValuePair<int, HandlerCache> entry = handlerList[0];
                    RunUntargetedBroadcast(ref message, entry.Key, entry.Value);
                    entry = handlerList[1];
                    RunUntargetedBroadcast(ref message, entry.Key, entry.Value);
                    entry = handlerList[2];
                    RunUntargetedBroadcast(ref message, entry.Key, entry.Value);
                    return true;
                }
                case 4:
                {
                    KeyValuePair<int, HandlerCache> entry = handlerList[0];
                    RunUntargetedBroadcast(ref message, entry.Key, entry.Value);
                    entry = handlerList[1];
                    RunUntargetedBroadcast(ref message, entry.Key, entry.Value);
                    entry = handlerList[2];
                    RunUntargetedBroadcast(ref message, entry.Key, entry.Value);
                    entry = handlerList[3];
                    RunUntargetedBroadcast(ref message, entry.Key, entry.Value);
                    return true;
                }
                case 5:
                {
                    KeyValuePair<int, HandlerCache> entry = handlerList[0];
                    RunUntargetedBroadcast(ref message, entry.Key, entry.Value);
                    entry = handlerList[1];
                    RunUntargetedBroadcast(ref message, entry.Key, entry.Value);
                    entry = handlerList[2];
                    RunUntargetedBroadcast(ref message, entry.Key, entry.Value);
                    entry = handlerList[3];
                    RunUntargetedBroadcast(ref message, entry.Key, entry.Value);
                    entry = handlerList[4];
                    RunUntargetedBroadcast(ref message, entry.Key, entry.Value);
                    return true;
                }
            }

            for (int i = 0; i < handlerListCount; ++i)
            {
                KeyValuePair<int, HandlerCache> entry = handlerList[i];
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
            if (cache.handlers.Count == 0)
            {
                return;
            }
            List<MessageHandler> messageHandlers = GetOrAddMessageHandlerStack(cache, _emissionId);
            int messageHandlersCount = messageHandlers.Count;
            if (messageHandlersCount == 0)
            {
                return;
            }
            switch (messageHandlersCount)
            {
                case 1:
                {
                    messageHandlers[0].HandleUntargetedMessage(ref message, this, priority);
                    return;
                }
                case 2:
                {
                    messageHandlers[0].HandleUntargetedMessage(ref message, this, priority);
                    messageHandlers[1].HandleUntargetedMessage(ref message, this, priority);
                    return;
                }
                case 3:
                {
                    messageHandlers[0].HandleUntargetedMessage(ref message, this, priority);
                    messageHandlers[1].HandleUntargetedMessage(ref message, this, priority);
                    messageHandlers[2].HandleUntargetedMessage(ref message, this, priority);
                    return;
                }
                case 4:
                {
                    messageHandlers[0].HandleUntargetedMessage(ref message, this, priority);
                    messageHandlers[1].HandleUntargetedMessage(ref message, this, priority);
                    messageHandlers[2].HandleUntargetedMessage(ref message, this, priority);
                    messageHandlers[3].HandleUntargetedMessage(ref message, this, priority);
                    return;
                }
                case 5:
                {
                    messageHandlers[0].HandleUntargetedMessage(ref message, this, priority);
                    messageHandlers[1].HandleUntargetedMessage(ref message, this, priority);
                    messageHandlers[2].HandleUntargetedMessage(ref message, this, priority);
                    messageHandlers[3].HandleUntargetedMessage(ref message, this, priority);
                    messageHandlers[4].HandleUntargetedMessage(ref message, this, priority);
                    return;
                }
            }

            for (int i = 0; i < messageHandlersCount; ++i)
            {
                MessageHandler handler = messageHandlers[i];
                handler.HandleUntargetedMessage(ref message, this, priority);
            }
        }

        private bool InternalTargetedWithoutTargetingBroadcast<TMessage>(
            ref InstanceId target,
            ref TMessage message
        )
            where TMessage : ITargetedMessage
        {
            if (
                !_sinks.TryGetValue<TMessage>(out HandlerCache<int, HandlerCache> sortedHandlers)
                || sortedHandlers.handlers.Count == 0
            )
            {
                return false;
            }

            List<KeyValuePair<int, HandlerCache>> handlerList = GetOrAddMessageHandlerStack(
                sortedHandlers,
                _emissionId
            );

            int handlerListCount = handlerList.Count;
            switch (handlerListCount)
            {
                case 1:
                {
                    KeyValuePair<int, HandlerCache> entry = handlerList[0];
                    RunTargetedWithoutTargeting(ref target, ref message, entry.Key, entry.Value);
                    return true;
                }
                case 2:
                {
                    KeyValuePair<int, HandlerCache> entry = handlerList[0];
                    RunTargetedWithoutTargeting(ref target, ref message, entry.Key, entry.Value);
                    entry = handlerList[1];
                    RunTargetedWithoutTargeting(ref target, ref message, entry.Key, entry.Value);
                    return true;
                }
                case 3:
                {
                    KeyValuePair<int, HandlerCache> entry = handlerList[0];
                    RunTargetedWithoutTargeting(ref target, ref message, entry.Key, entry.Value);
                    entry = handlerList[1];
                    RunTargetedWithoutTargeting(ref target, ref message, entry.Key, entry.Value);
                    entry = handlerList[2];
                    RunTargetedWithoutTargeting(ref target, ref message, entry.Key, entry.Value);
                    return true;
                }
                case 4:
                {
                    KeyValuePair<int, HandlerCache> entry = handlerList[0];
                    RunTargetedWithoutTargeting(ref target, ref message, entry.Key, entry.Value);
                    entry = handlerList[1];
                    RunTargetedWithoutTargeting(ref target, ref message, entry.Key, entry.Value);
                    entry = handlerList[2];
                    RunTargetedWithoutTargeting(ref target, ref message, entry.Key, entry.Value);
                    entry = handlerList[3];
                    RunTargetedWithoutTargeting(ref target, ref message, entry.Key, entry.Value);
                    return true;
                }
                case 5:
                {
                    KeyValuePair<int, HandlerCache> entry = handlerList[0];
                    RunTargetedWithoutTargeting(ref target, ref message, entry.Key, entry.Value);
                    entry = handlerList[1];
                    RunTargetedWithoutTargeting(ref target, ref message, entry.Key, entry.Value);
                    entry = handlerList[2];
                    RunTargetedWithoutTargeting(ref target, ref message, entry.Key, entry.Value);
                    entry = handlerList[3];
                    RunTargetedWithoutTargeting(ref target, ref message, entry.Key, entry.Value);
                    entry = handlerList[4];
                    RunTargetedWithoutTargeting(ref target, ref message, entry.Key, entry.Value);
                    return true;
                }
            }

            for (int i = 0; i < handlerListCount; ++i)
            {
                KeyValuePair<int, HandlerCache> entry = handlerList[i];
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
            if (cache.handlers.Count == 0)
            {
                return;
            }

            List<MessageHandler> messageHandlers = GetOrAddMessageHandlerStack(cache, _emissionId);
            int messageHandlersCount = messageHandlers.Count;
            switch (messageHandlersCount)
            {
                case 1:
                {
                    messageHandlers[0]
                        .HandleTargetedWithoutTargeting(ref target, ref message, this, priority);
                    return;
                }
                case 2:
                {
                    messageHandlers[0]
                        .HandleTargetedWithoutTargeting(ref target, ref message, this, priority);
                    messageHandlers[1]
                        .HandleTargetedWithoutTargeting(ref target, ref message, this, priority);
                    return;
                }
                case 3:
                {
                    messageHandlers[0]
                        .HandleTargetedWithoutTargeting(ref target, ref message, this, priority);
                    messageHandlers[1]
                        .HandleTargetedWithoutTargeting(ref target, ref message, this, priority);
                    messageHandlers[2]
                        .HandleTargetedWithoutTargeting(ref target, ref message, this, priority);
                    return;
                }
                case 4:
                {
                    messageHandlers[0]
                        .HandleTargetedWithoutTargeting(ref target, ref message, this, priority);
                    messageHandlers[1]
                        .HandleTargetedWithoutTargeting(ref target, ref message, this, priority);
                    messageHandlers[2]
                        .HandleTargetedWithoutTargeting(ref target, ref message, this, priority);
                    messageHandlers[3]
                        .HandleTargetedWithoutTargeting(ref target, ref message, this, priority);
                    return;
                }
                case 5:
                {
                    messageHandlers[0]
                        .HandleTargetedWithoutTargeting(ref target, ref message, this, priority);
                    messageHandlers[1]
                        .HandleTargetedWithoutTargeting(ref target, ref message, this, priority);
                    messageHandlers[2]
                        .HandleTargetedWithoutTargeting(ref target, ref message, this, priority);
                    messageHandlers[3]
                        .HandleTargetedWithoutTargeting(ref target, ref message, this, priority);
                    messageHandlers[4]
                        .HandleTargetedWithoutTargeting(ref target, ref message, this, priority);
                    return;
                }
            }

            for (int i = 0; i < messageHandlersCount; ++i)
            {
                MessageHandler handler = messageHandlers[i];
                handler.HandleTargetedWithoutTargeting(ref target, ref message, this, priority);
            }
        }

        private bool InternalBroadcastWithoutSource<TMessage>(
            ref InstanceId source,
            ref TMessage message
        )
            where TMessage : IBroadcastMessage
        {
            if (
                !_sinks.TryGetValue<TMessage>(out HandlerCache<int, HandlerCache> sortedHandlers)
                || sortedHandlers.handlers.Count == 0
            )
            {
                return false;
            }

            List<KeyValuePair<int, HandlerCache>> handlerList = GetOrAddMessageHandlerStack(
                sortedHandlers,
                _emissionId
            );
            int handlerListCount = handlerList.Count;
            switch (handlerListCount)
            {
                case 1:
                {
                    KeyValuePair<int, HandlerCache> entry = handlerList[0];
                    RunBroadcastWithoutSource(ref source, ref message, entry.Key, entry.Value);
                    return true;
                }
                case 2:
                {
                    KeyValuePair<int, HandlerCache> entry = handlerList[0];
                    RunBroadcastWithoutSource(ref source, ref message, entry.Key, entry.Value);
                    entry = handlerList[1];
                    RunBroadcastWithoutSource(ref source, ref message, entry.Key, entry.Value);
                    return true;
                }
                case 3:
                {
                    KeyValuePair<int, HandlerCache> entry = handlerList[0];
                    RunBroadcastWithoutSource(ref source, ref message, entry.Key, entry.Value);
                    entry = handlerList[1];
                    RunBroadcastWithoutSource(ref source, ref message, entry.Key, entry.Value);
                    entry = handlerList[2];
                    RunBroadcastWithoutSource(ref source, ref message, entry.Key, entry.Value);
                    return true;
                }
                case 4:
                {
                    KeyValuePair<int, HandlerCache> entry = handlerList[0];
                    RunBroadcastWithoutSource(ref source, ref message, entry.Key, entry.Value);
                    entry = handlerList[1];
                    RunBroadcastWithoutSource(ref source, ref message, entry.Key, entry.Value);
                    entry = handlerList[2];
                    RunBroadcastWithoutSource(ref source, ref message, entry.Key, entry.Value);
                    entry = handlerList[3];
                    RunBroadcastWithoutSource(ref source, ref message, entry.Key, entry.Value);
                    return true;
                }
                case 5:
                {
                    KeyValuePair<int, HandlerCache> entry = handlerList[0];
                    RunBroadcastWithoutSource(ref source, ref message, entry.Key, entry.Value);
                    entry = handlerList[1];
                    RunBroadcastWithoutSource(ref source, ref message, entry.Key, entry.Value);
                    entry = handlerList[2];
                    RunBroadcastWithoutSource(ref source, ref message, entry.Key, entry.Value);
                    entry = handlerList[3];
                    RunBroadcastWithoutSource(ref source, ref message, entry.Key, entry.Value);
                    entry = handlerList[4];
                    RunBroadcastWithoutSource(ref source, ref message, entry.Key, entry.Value);
                    return true;
                }
            }

            for (int i = 0; i < handlerListCount; ++i)
            {
                KeyValuePair<int, HandlerCache> entry = handlerList[i];
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
            if (cache.handlers.Count == 0)
            {
                return;
            }

            List<MessageHandler> messageHandlers = GetOrAddMessageHandlerStack(cache, _emissionId);
            int messageHandlersCount = messageHandlers.Count;
            switch (messageHandlersCount)
            {
                case 1:
                {
                    messageHandlers[0]
                        .HandleSourcedBroadcastWithoutSource(
                            ref source,
                            ref message,
                            this,
                            priority
                        );
                    return;
                }
                case 2:
                {
                    messageHandlers[0]
                        .HandleSourcedBroadcastWithoutSource(
                            ref source,
                            ref message,
                            this,
                            priority
                        );
                    messageHandlers[1]
                        .HandleSourcedBroadcastWithoutSource(
                            ref source,
                            ref message,
                            this,
                            priority
                        );
                    return;
                }
                case 3:
                {
                    messageHandlers[0]
                        .HandleSourcedBroadcastWithoutSource(
                            ref source,
                            ref message,
                            this,
                            priority
                        );
                    messageHandlers[1]
                        .HandleSourcedBroadcastWithoutSource(
                            ref source,
                            ref message,
                            this,
                            priority
                        );
                    messageHandlers[2]
                        .HandleSourcedBroadcastWithoutSource(
                            ref source,
                            ref message,
                            this,
                            priority
                        );
                    return;
                }
                case 4:
                {
                    messageHandlers[0]
                        .HandleSourcedBroadcastWithoutSource(
                            ref source,
                            ref message,
                            this,
                            priority
                        );
                    messageHandlers[1]
                        .HandleSourcedBroadcastWithoutSource(
                            ref source,
                            ref message,
                            this,
                            priority
                        );
                    messageHandlers[2]
                        .HandleSourcedBroadcastWithoutSource(
                            ref source,
                            ref message,
                            this,
                            priority
                        );
                    messageHandlers[3]
                        .HandleSourcedBroadcastWithoutSource(
                            ref source,
                            ref message,
                            this,
                            priority
                        );
                    return;
                }
                case 5:
                {
                    messageHandlers[0]
                        .HandleSourcedBroadcastWithoutSource(
                            ref source,
                            ref message,
                            this,
                            priority
                        );
                    messageHandlers[1]
                        .HandleSourcedBroadcastWithoutSource(
                            ref source,
                            ref message,
                            this,
                            priority
                        );
                    messageHandlers[2]
                        .HandleSourcedBroadcastWithoutSource(
                            ref source,
                            ref message,
                            this,
                            priority
                        );
                    messageHandlers[3]
                        .HandleSourcedBroadcastWithoutSource(
                            ref source,
                            ref message,
                            this,
                            priority
                        );
                    messageHandlers[4]
                        .HandleSourcedBroadcastWithoutSource(
                            ref source,
                            ref message,
                            this,
                            priority
                        );
                    return;
                }
            }

            for (int i = 0; i < messageHandlersCount; ++i)
            {
                MessageHandler handler = messageHandlers[i];
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
            MessageCache<HandlerCache<int, HandlerCache>> sinks,
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
            HandlerCache<int, HandlerCache> handlers = sinks.GetOrAdd<T>();

            if (!handlers.handlers.TryGetValue(priority, out HandlerCache cache))
            {
                handlers.version++;
                cache = new HandlerCache();
                handlers.handlers[priority] = cache;
                // insert priority in sorted order
                List<int> order = handlers.order;
                int idx = 0;
                while (idx < order.Count && order[idx] < priority)
                {
                    idx++;
                }
                order.Insert(idx, priority);
            }

            Dictionary<MessageHandler, int> handler = cache.handlers;
            cache.version++;
            int count = handler.GetValueOrDefault(messageHandler, 0);

            handler[messageHandler] = count + 1;
            Type type = typeof(T);
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
                    !sinks.TryGetValue<T>(out handlers)
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
                if (count <= 1)
                {
                    bool complete = handler.Remove(messageHandler);
                    cache.version++;
                    // do not mutate cache.cache here; let next read rebuild from handlers

                    if (handler.Count == 0)
                    {
                        _ = handlers.handlers.Remove(priority);
                        // remove priority from order
                        List<int> order = handlers.order;
                        int removeIdx = order.IndexOf(priority);
                        if (removeIdx >= 0)
                        {
                            order.RemoveAt(removeIdx);
                        }
                    }

                    if (handlers.handlers.Count == 0)
                    {
                        sinks.Remove<T>();
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
            MessageCache<Dictionary<InstanceId, HandlerCache<int, HandlerCache>>> sinks,
            RegistrationMethod registrationMethod,
            int priority
        )
            where T : IMessage
        {
            if (messageHandler == null)
            {
                throw new ArgumentNullException(nameof(messageHandler));
            }

            Dictionary<InstanceId, HandlerCache<int, HandlerCache>> broadcastHandlers =
                sinks.GetOrAdd<T>();

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
                // insert priority in sorted order
                List<int> order = handlers.order;
                int idx = 0;
                while (idx < order.Count && order[idx] < priority)
                {
                    idx++;
                }
                order.Insert(idx, priority);
            }

            cache.version++;
            Dictionary<MessageHandler, int> handler = cache.handlers;
            int count = handler.GetValueOrDefault(messageHandler, 0);

            handler[messageHandler] = count + 1;

            Type type = typeof(T);
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
                    !sinks.TryGetValue<T>(out broadcastHandlers)
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
                if (count <= 1)
                {
                    bool complete = handler.Remove(messageHandler);
                    cache.version++;
                    // do not mutate cache.cache here; let next read rebuild from handlers
                    if (handler.Count == 0)
                    {
                        handlers.version++;
                        _ = handlers.handlers.Remove(priority);
                        // remove priority from order
                        List<int> order = handlers.order;
                        int removeIdx = order.IndexOf(priority);
                        if (removeIdx >= 0)
                        {
                            order.RemoveAt(removeIdx);
                        }
                    }

                    if (handlers.handlers.Count == 0)
                    {
                        _ = broadcastHandlers.Remove(context);
                    }

                    if (broadcastHandlers.Count == 0)
                    {
                        sinks.Remove<T>();
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static List<KeyValuePair<int, HandlerCache>> GetOrAddMessageHandlerStack(
            HandlerCache<int, HandlerCache> cache,
            long emissionId
        )
        {
            if (cache.lastSeenEmissionId != emissionId)
            {
                if (cache.version != cache.lastSeenVersion)
                {
                    List<KeyValuePair<int, HandlerCache>> list = cache.cache;
                    list.Clear();
                    List<int> keys = cache.order;
                    for (int i = 0; i < keys.Count; i++)
                    {
                        int key = keys[i];
                        if (cache.handlers.TryGetValue(key, out HandlerCache value))
                        {
                            list.Add(new KeyValuePair<int, HandlerCache>(key, value));
                        }
                    }
                    cache.lastSeenVersion = cache.version;
                }
                cache.lastSeenEmissionId = emissionId;
            }
            return cache.cache;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static List<MessageHandler> GetOrAddMessageHandlerStack(
            HandlerCache cache,
            long emissionId
        )
        {
            if (cache.lastSeenEmissionId != emissionId)
            {
                if (cache.version != cache.lastSeenVersion)
                {
                    List<MessageHandler> list = cache.cache;
                    list.Clear();
                    Dictionary<MessageHandler, int>.KeyCollection keys = cache.handlers.Keys;
                    list.AddRange(keys);
                    cache.lastSeenVersion = cache.version;
                }
                cache.lastSeenEmissionId = emissionId;
            }
            return cache.cache;
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
                if (typeof(T).IsValueType)
                {
                    object box = message;
                    ref T typedRef = ref Unsafe.As<object, T>(ref box);
                    untargetedBroadcast(ref typedRef);
                }
                else
                {
                    T typedMessage = (T)message;
                    untargetedBroadcast(ref typedMessage);
                }
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
                if (typeof(T).IsValueType)
                {
                    object box = message;
                    ref T typedRef = ref Unsafe.As<object, T>(ref box);
                    targetedBroadcast(ref target, ref typedRef);
                }
                else
                {
                    T typedMessage = (T)message;
                    targetedBroadcast(ref target, ref typedMessage);
                }
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
                if (typeof(T).IsValueType)
                {
                    object box = message;
                    ref T typedRef = ref Unsafe.As<object, T>(ref box);
                    sourcedBroadcast(ref target, ref typedRef);
                }
                else
                {
                    T typedMessage = (T)message;
                    sourcedBroadcast(ref target, ref typedMessage);
                }
            }
        }

#if UNITY_2017_1_OR_NEWER
        private static Action<MonoBehaviour, object[]> CompileMethodAction(MethodInfo methodInfo)
        {
            ParameterExpression componentParameter = Expression.Parameter(
                typeof(MonoBehaviour),
                "targetComponent"
            );
            ParameterExpression argsParameter = Expression.Parameter(typeof(object[]), "args");
            ParameterInfo[] methodParams = methodInfo.GetParameters();

            ArgumentExpressionsCache.Clear();
            for (int i = 0; i < methodParams.Length; ++i)
            {
                Expression indexAccess = Expression.ArrayIndex(
                    argsParameter,
                    Expression.Constant(i)
                );
                Expression convertedArg = Expression.Convert(
                    indexAccess,
                    methodParams[i].ParameterType
                );
                ArgumentExpressionsCache.Add(convertedArg);
            }

            // ReSharper disable once AssignNullToNotNullAttribute
            Expression instanceExpression = methodInfo.IsStatic
                ? null
                : Expression.Convert(componentParameter, methodInfo.DeclaringType);
            MethodCallExpression callExpression = Expression.Call(
                instanceExpression,
                methodInfo,
                ArgumentExpressionsCache
            );
            Expression<Action<MonoBehaviour, object[]>> lambda = Expression.Lambda<
                Action<MonoBehaviour, object[]>
            >(callExpression, componentParameter, argsParameter);

            return lambda.Compile();
        }
#endif

        private void SendMessage(
            MonoBehaviour recipient,
            ref ReflexiveMessage message,
            bool onlyActive
        )
        {
            if (onlyActive && !recipient.enabled)
            {
                return;
            }

            if (!_recipientCache.Add(recipient))
            {
                return;
            }

            Type componentType = recipient.GetType();
            if (
                !_methodCache.TryGetValue(
                    componentType,
                    out Dictionary<MethodSignatureKey, Action<MonoBehaviour, object[]>> methodCache
                )
            )
            {
                _methodCache[componentType] = methodCache =
                    new Dictionary<MethodSignatureKey, Action<MonoBehaviour, object[]>>();
            }

            MethodSignatureKey lookupKey = message.signatureKey;
            if (!methodCache.TryGetValue(lookupKey, out Action<MonoBehaviour, object[]> method))
            {
                MethodInfo methodInfo = null;
                try
                {
                    methodInfo = componentType.GetMethod(
                        message.method,
                        ReflexiveMethodBindingFlags,
                        null,
                        message.parameterTypes,
                        null
                    );
                }
                catch (AmbiguousMatchException)
                {
                    MethodInfo[] matchingMethods = componentType.GetMethods(
                        ReflexiveMethodBindingFlags
                    );
                    Span<MethodInfo> span = matchingMethods.AsSpan();
                    for (int i = 0; i < span.Length; ++i)
                    {
                        MethodInfo matchingMethod = span[i];
                        if (
                            !string.Equals(
                                matchingMethod.Name,
                                message.method,
                                StringComparison.Ordinal
                            )
                            || !ParameterTypesMatch(
                                matchingMethod.GetParameters(),
                                message.parameterTypes
                            )
                        )
                        {
                            continue;
                        }

                        methodInfo = matchingMethod;
                        break;
                    }
                }
                catch
                {
                    methodInfo = null;
                }

                if (methodInfo != null)
                {
                    method = CompileMethodAction(methodInfo);
                }
                methodCache[lookupKey] = method;
            }

            method?.Invoke(recipient, message.parameters);
        }

        private static bool ParameterTypesMatch(ParameterInfo[] methodParams, Type[] expectedTypes)
        {
            if (methodParams.Length != expectedTypes.Length)
            {
                return false;
            }

            for (int i = 0; i < methodParams.Length; ++i)
            {
                if (methodParams[i].ParameterType != expectedTypes[i])
                {
                    return false;
                }
            }
            return true;
        }
    }
}
