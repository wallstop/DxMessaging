namespace DxMessaging.Core.MessageBus
{
    using System;
    using System.Buffers;
    using System.Collections.Generic;
    using System.Linq.Expressions;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using DataStructure;
    using Diagnostics;
    using DxMessaging.Core;
    using Extensions;
    using Helper;
    using Messages;
    using static IMessageBus;
#if UNITY_2021_3_OR_NEWER
    using UnityEngine;
#endif

    /// <summary>
    /// Instanced MessageBus for use cases where you want distinct islands of MessageBuses.
    /// </summary>
    public sealed class MessageBus : IMessageBus
    {
        private long _emissionId;
        public long EmissionId => _emissionId;

        private readonly struct PrefreezeDescriptor
        {
            public PrefreezeDescriptor(byte kind, int priority)
            {
                this.kind = kind;
                this.priority = priority;
            }

            public static readonly PrefreezeDescriptor Empty = new PrefreezeDescriptor(0, 0);
            public readonly byte kind;
            public readonly int priority;
        }

        private enum DispatchCategory : byte
        {
            None = 0,
            Untargeted = 1,
            UntargetedPost = 2,
            Targeted = 3,
            TargetedPost = 4,
            TargetedWithoutTargeting = 5,
            TargetedWithoutTargetingPost = 6,
            Broadcast = 7,
            BroadcastPost = 8,
            BroadcastWithoutSource = 9,
            BroadcastWithoutSourcePost = 10,
            GlobalUntargeted = 11,
            GlobalTargeted = 12,
            GlobalBroadcast = 13,
        }

        private const byte PrefreezeKindNone = 0;
        private const byte PrefreezeKindTargetedWithoutTargetingHandlers = 1;
        private const byte PrefreezeKindBroadcastWithoutSourceHandlers = 2;
        private const byte PrefreezeKindGlobalUntargetedHandlers = 3;
        private const byte PrefreezeKindGlobalTargetedHandlers = 4;
        private const byte PrefreezeKindGlobalBroadcastHandlers = 5;

        private static readonly ArrayPool<DispatchBucket> DispatchBucketPool =
            ArrayPool<DispatchBucket>.Shared;
        private static readonly ArrayPool<DispatchEntry> DispatchEntryPool =
            ArrayPool<DispatchEntry>.Shared;

        private readonly struct DispatchEntry
        {
            public DispatchEntry(
                MessageHandler handler,
                object dispatch,
                PrefreezeDescriptor prefreeze
            )
            {
                this.handler = handler;
                this.dispatch = dispatch;
                this.prefreeze = prefreeze;
            }

            public readonly MessageHandler handler;
            public readonly object dispatch;
            public readonly PrefreezeDescriptor prefreeze;
        }

        private struct DispatchBucket
        {
            public DispatchBucket(
                int priority,
                DispatchEntry[] entries,
                int entryCount,
                bool pooledEntries
            )
            {
                this.priority = priority;
                this.entries = entries;
                this.entryCount = entryCount;
                this.pooledEntries = pooledEntries;
            }

            public int priority;
            public DispatchEntry[] entries;
            public int entryCount;
            public bool pooledEntries;

            public static DispatchBucket CreateEmpty(int priority)
            {
                return new DispatchBucket(priority, Array.Empty<DispatchEntry>(), 0, false);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void ReleaseEntries()
            {
                if (!pooledEntries || entries == null)
                {
                    return;
                }

                Array.Clear(entries, 0, entryCount);
                DispatchEntryPool.Return(entries);
                entries = Array.Empty<DispatchEntry>();
                entryCount = 0;
                pooledEntries = false;
            }
        }

        private sealed class DispatchSnapshot
        {
            public static readonly DispatchSnapshot Empty = new DispatchSnapshot(
                Array.Empty<DispatchBucket>(),
                0,
                false
            );

            public DispatchSnapshot(DispatchBucket[] buckets, int count, bool pooled)
            {
                this.buckets = buckets;
                bucketCount = count;
                this.pooled = pooled;
            }

            public DispatchBucket[] buckets;
            public int bucketCount;
            private bool pooled;

            public bool IsEmpty => bucketCount == 0;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Release()
            {
                if (!pooled || buckets == null)
                {
                    return;
                }

                for (int i = 0; i < bucketCount; ++i)
                {
                    buckets[i].ReleaseEntries();
                }

                Array.Clear(buckets, 0, bucketCount);
                DispatchBucketPool.Return(buckets);
                buckets = Array.Empty<DispatchBucket>();
                bucketCount = 0;
                pooled = false;
            }
        }

        private sealed class HandlerCache<TKey, TValue>
        {
            internal sealed class DispatchState
            {
                public DispatchSnapshot active = DispatchSnapshot.Empty;
                public DispatchSnapshot pending = DispatchSnapshot.Empty;
                public bool hasPending;
                public bool pendingDirty;
                public long snapshotEmissionId = -1;

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public void Reset()
                {
                    ReleaseSnapshot(ref active);
                    ReleaseSnapshot(ref pending);
                    hasPending = false;
                    pendingDirty = false;
                    snapshotEmissionId = -1;
                }
            }

            public readonly Dictionary<TKey, TValue> handlers = new();
            public readonly List<TKey> order = new();
            public readonly List<KeyValuePair<TKey, TValue>> cache = new();
            public long version;
            public long lastSeenVersion = -1;
            public long lastSeenEmissionId;
            private readonly Dictionary<DispatchCategory, DispatchState> dispatchStates = new();

            /// <summary>
            /// Clears all cached handler references and resets the version tracking metadata.
            /// </summary>
            public void Clear()
            {
                handlers.Clear();
                order.Clear();
                cache.Clear();
                version = 0;
                lastSeenVersion = -1;
                lastSeenEmissionId = 0;
                if (dispatchStates.Count > 0)
                {
                    foreach (DispatchState state in dispatchStates.Values)
                    {
                        state.Reset();
                    }
                    dispatchStates.Clear();
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public DispatchState GetOrCreateDispatchState(DispatchCategory category)
            {
                if (!dispatchStates.TryGetValue(category, out DispatchState state))
                {
                    state = new DispatchState();
                    dispatchStates[category] = state;
                }

                return state;
            }
        }

        private sealed class InterceptorCache<TValue>
        {
            public readonly SortedList<int, List<TValue>> handlers = new();
            public long lastSeenEmissionId;

            public void Clear()
            {
                handlers.Clear();
                lastSeenEmissionId = 0;
            }
        }

        private sealed class HandlerCache
        {
            internal sealed class DispatchState
            {
                public DispatchSnapshot active = DispatchSnapshot.Empty;
                public DispatchSnapshot pending = DispatchSnapshot.Empty;
                public bool hasPending;
                public bool pendingDirty;
                public long snapshotEmissionId = -1;

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public void Reset()
                {
                    ReleaseSnapshot(ref active);
                    ReleaseSnapshot(ref pending);
                    hasPending = false;
                    pendingDirty = false;
                    snapshotEmissionId = -1;
                }
            }

            public readonly Dictionary<MessageHandler, int> handlers = new();
            public readonly List<MessageHandler> cache = new();
            public long version;
            public long lastSeenVersion = -1;
            public long lastSeenEmissionId;
            private readonly Dictionary<DispatchCategory, DispatchState> dispatchStates = new();

            /// <summary>
            /// Clears all cached handler references and resets the version tracking metadata.
            /// </summary>
            public void Clear()
            {
                handlers.Clear();
                cache.Clear();
                version = 0;
                lastSeenVersion = -1;
                lastSeenEmissionId = 0;
                if (dispatchStates.Count > 0)
                {
                    foreach (DispatchState state in dispatchStates.Values)
                    {
                        state.Reset();
                    }
                    dispatchStates.Clear();
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public DispatchState GetOrCreateDispatchState(DispatchCategory category)
            {
                if (!dispatchStates.TryGetValue(category, out DispatchState state))
                {
                    state = new DispatchState();
                    dispatchStates[category] = state;
                }

                return state;
            }
        }

        public int RegisteredTargeted
        {
            get
            {
                int count = 0;
                foreach (
                    Dictionary<InstanceId, HandlerCache<int, HandlerCache>> entry in _targetedSinks
                )
                {
                    count += entry?.Count ?? 0;
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
                foreach (
                    Dictionary<InstanceId, HandlerCache<int, HandlerCache>> entry in _broadcastSinks
                )
                {
                    count += entry?.Count ?? 0;
                }

                return count;
            }
        }

        public int RegisteredUntargeted
        {
            get
            {
                int count = 0;
                foreach (HandlerCache<int, HandlerCache> entry in _sinks)
                {
                    count += entry?.handlers?.Count ?? 0;
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
        private readonly MessageCache<InterceptorCache<object>> _untargetedInterceptsByType = new();
        private readonly MessageCache<InterceptorCache<object>> _targetedInterceptsByType = new();
        private readonly MessageCache<InterceptorCache<object>> _broadcastInterceptsByType = new();
        private readonly Dictionary<object, Dictionary<int, int>> _uniqueInterceptorsAndPriorities =
            new();

        private readonly Dictionary<Type, object> _broadcastMethodsByType = new();
        private readonly Stack<List<object>> _innerInterceptorsStack = new();

        private readonly Dictionary<
            Type,
            Dictionary<MethodSignatureKey, Action<MonoBehaviour, object[]>>
        > _methodCache = new();

#if UNITY_2021_3_OR_NEWER
        private readonly HashSet<MonoBehaviour> _recipientCache = new();
        private readonly List<MonoBehaviour> _componentCache = new();
#endif

        private readonly RegistrationLog _log = new();
        internal readonly CyclicBuffer<MessageEmissionData> _emissionBuffer = new(
            GlobalMessageBufferSize
        );

        private bool _diagnosticsMode = ShouldEnableDiagnostics();
        private bool _loggedReflexiveWarning;

        internal void ResetState()
        {
            _emissionId = 0;
            _diagnosticsMode = ShouldEnableDiagnostics();
            _loggedReflexiveWarning = false;

            _sinks.Clear();
            _targetedSinks.Clear();
            _broadcastSinks.Clear();
            _postProcessingSinks.Clear();
            _postProcessingTargetedSinks.Clear();
            _postProcessingBroadcastSinks.Clear();
            _postProcessingTargetedWithoutTargetingSinks.Clear();
            _postProcessingBroadcastWithoutSourceSinks.Clear();
            _globalSinks.Clear();

            _untargetedInterceptsByType.Clear();
            _targetedInterceptsByType.Clear();
            _broadcastInterceptsByType.Clear();
            _uniqueInterceptorsAndPriorities.Clear();
            _broadcastMethodsByType.Clear();
            _innerInterceptorsStack.Clear();
            _methodCache.Clear();

#if UNITY_2021_3_OR_NEWER
            _recipientCache.Clear();
            _componentCache.Clear();
#endif

            bool enabled = _log.Enabled;
            _log.Clear();
            _log.Enabled = enabled;
            _emissionBuffer.Resize(GlobalMessageBufferSize);
            _emissionBuffer.Clear();
        }

        /// <inheritdoc />
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

        /// <inheritdoc />
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

        /// <inheritdoc />
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

        /// <inheritdoc />
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

        /// <inheritdoc />
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

        /// <inheritdoc />
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

            StageGlobalDispatchSnapshot<IUntargetedMessage>(
                this,
                _globalSinks,
                DispatchCategory.GlobalUntargeted
            );
            StageGlobalDispatchSnapshot<ITargetedMessage>(
                this,
                _globalSinks,
                DispatchCategory.GlobalTargeted
            );
            StageGlobalDispatchSnapshot<IBroadcastMessage>(
                this,
                _globalSinks,
                DispatchCategory.GlobalBroadcast
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

                StageGlobalDispatchSnapshot<IUntargetedMessage>(
                    this,
                    _globalSinks,
                    DispatchCategory.GlobalUntargeted
                );
                StageGlobalDispatchSnapshot<ITargetedMessage>(
                    this,
                    _globalSinks,
                    DispatchCategory.GlobalTargeted
                );
                StageGlobalDispatchSnapshot<IBroadcastMessage>(
                    this,
                    _globalSinks,
                    DispatchCategory.GlobalBroadcast
                );
            };
        }

        /// <inheritdoc />
        public Action RegisterUntargetedInterceptor<T>(
            UntargetedInterceptor<T> interceptor,
            int priority = 0
        )
            where T : IUntargetedMessage
        {
            InterceptorCache<object> prioritizedInterceptors =
                _untargetedInterceptsByType.GetOrAdd<T>();

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

            if (
                !prioritizedInterceptors.handlers.TryGetValue(
                    priority,
                    out List<object> interceptors
                )
            )
            {
                interceptors = new List<object>();
                prioritizedInterceptors.handlers.Add(priority, interceptors);
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
                        if (
                            prioritizedInterceptors.handlers.TryGetValue(
                                priority,
                                out List<object> interceptors
                            )
                        )
                        {
                            complete = interceptors.Remove(interceptor);
                            if (interceptors.Count == 0)
                            {
                                _ = prioritizedInterceptors.handlers.Remove(priority);
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

        /// <inheritdoc />
        public Action RegisterTargetedInterceptor<T>(
            TargetedInterceptor<T> interceptor,
            int priority = 0
        )
            where T : ITargetedMessage
        {
            InterceptorCache<object> prioritizedInterceptors =
                _targetedInterceptsByType.GetOrAdd<T>();

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

            if (
                !prioritizedInterceptors.handlers.TryGetValue(
                    priority,
                    out List<object> interceptors
                )
            )
            {
                interceptors = new List<object>();
                prioritizedInterceptors.handlers.Add(priority, interceptors);
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
                        if (
                            prioritizedInterceptors.handlers.TryGetValue(
                                priority,
                                out List<object> interceptors
                            )
                        )
                        {
                            complete = interceptors.Remove(interceptor);
                            if (interceptors.Count == 0)
                            {
                                _ = prioritizedInterceptors.handlers.Remove(priority);
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

        /// <inheritdoc />
        public Action RegisterBroadcastInterceptor<T>(
            BroadcastInterceptor<T> interceptor,
            int priority = 0
        )
            where T : IBroadcastMessage
        {
            InterceptorCache<object> prioritizedInterceptors =
                _broadcastInterceptsByType.GetOrAdd<T>();

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

            if (
                !prioritizedInterceptors.handlers.TryGetValue(
                    priority,
                    out List<object> interceptors
                )
            )
            {
                interceptors = new List<object>();
                prioritizedInterceptors.handlers.Add(priority, interceptors);
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
                        if (
                            prioritizedInterceptors.handlers.TryGetValue(
                                priority,
                                out List<object> interceptors
                            )
                        )
                        {
                            complete = interceptors.Remove(interceptor);
                            if (interceptors.Count == 0)
                            {
                                _ = prioritizedInterceptors.handlers.Remove(priority);
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

        /// <inheritdoc />
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

        /// <inheritdoc />
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

        /// <inheritdoc />
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

        /// <inheritdoc />
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

        /// <inheritdoc />
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

        /// <inheritdoc />
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

        /// <inheritdoc />
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
            DispatchSnapshot untargetedPostSnapshot = DispatchSnapshot.Empty;
            if (
                _postProcessingSinks.TryGetValue<TMessage>(
                    out HandlerCache<int, HandlerCache> untargetedPostHandlers
                )
                && untargetedPostHandlers.handlers.Count > 0
            )
            {
                untargetedPostSnapshot = AcquireDispatchSnapshot<TMessage>(
                    this,
                    untargetedPostHandlers,
                    DispatchCategory.UntargetedPost,
                    _emissionId
                );
                PrefreezeUntargetedPostSnapshot<TMessage>(untargetedPostSnapshot);
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
                DispatchSnapshot snapshot = untargetedPostSnapshot.IsEmpty
                    ? AcquireDispatchSnapshot<TMessage>(
                        this,
                        sortedHandlers,
                        DispatchCategory.UntargetedPost,
                        _emissionId
                    )
                    : untargetedPostSnapshot;
                DispatchBucket[] buckets = snapshot.buckets;
                int bucketCount = snapshot.bucketCount;
                for (int bucketIndex = 0; bucketIndex < bucketCount; ++bucketIndex)
                {
                    DispatchBucket bucket = buckets[bucketIndex];
                    DispatchEntry[] entries = bucket.entries;
                    int entryCount = bucket.entryCount;
                    if (entryCount == 0)
                    {
                        continue;
                    }

                    foundAnyHandlers = true;
                    int priority = bucket.priority;
                    switch (entryCount)
                    {
                        case 1:
                        {
                            InvokeUntargetedPostEntry(ref typedMessage, priority, entries[0]);
                            continue;
                        }
                        case 2:
                        {
                            InvokeUntargetedPostEntry(ref typedMessage, priority, entries[0]);
                            InvokeUntargetedPostEntry(ref typedMessage, priority, entries[1]);
                            continue;
                        }
                        case 3:
                        {
                            InvokeUntargetedPostEntry(ref typedMessage, priority, entries[0]);
                            InvokeUntargetedPostEntry(ref typedMessage, priority, entries[1]);
                            InvokeUntargetedPostEntry(ref typedMessage, priority, entries[2]);
                            continue;
                        }
                        case 4:
                        {
                            InvokeUntargetedPostEntry(ref typedMessage, priority, entries[0]);
                            InvokeUntargetedPostEntry(ref typedMessage, priority, entries[1]);
                            InvokeUntargetedPostEntry(ref typedMessage, priority, entries[2]);
                            InvokeUntargetedPostEntry(ref typedMessage, priority, entries[3]);
                            continue;
                        }
                        case 5:
                        {
                            InvokeUntargetedPostEntry(ref typedMessage, priority, entries[0]);
                            InvokeUntargetedPostEntry(ref typedMessage, priority, entries[1]);
                            InvokeUntargetedPostEntry(ref typedMessage, priority, entries[2]);
                            InvokeUntargetedPostEntry(ref typedMessage, priority, entries[3]);
                            InvokeUntargetedPostEntry(ref typedMessage, priority, entries[4]);
                            continue;
                        }
                    }

                    for (int entryIndex = 0; entryIndex < entryCount; ++entryIndex)
                    {
                        InvokeUntargetedPostEntry(ref typedMessage, priority, entries[entryIndex]);
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

        /// <inheritdoc />
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

        /// <inheritdoc />
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
            DispatchSnapshot targetedPostSnapshot = DispatchSnapshot.Empty;
            DispatchSnapshot targetedWithoutTargetingPostSnapshot = DispatchSnapshot.Empty;
            if (
                _postProcessingTargetedSinks.TryGetValue<TMessage>(
                    out Dictionary<InstanceId, HandlerCache<int, HandlerCache>> targetedPostHandlers
                )
                && targetedPostHandlers.TryGetValue(
                    target,
                    out HandlerCache<int, HandlerCache> targetedPostByPriority
                )
                && targetedPostByPriority.handlers.Count > 0
            )
            {
                targetedPostSnapshot = AcquireDispatchSnapshot<TMessage>(
                    this,
                    targetedPostByPriority,
                    DispatchCategory.TargetedPost,
                    _emissionId
                );
                PrefreezeTargetedPostSnapshot<TMessage>(ref target, targetedPostSnapshot);
            }
            if (
                _postProcessingTargetedWithoutTargetingSinks.TryGetValue<TMessage>(
                    out HandlerCache<int, HandlerCache> targetedWithoutTargetingHandlers
                )
                && targetedWithoutTargetingHandlers.handlers.Count > 0
            )
            {
                targetedWithoutTargetingPostSnapshot = AcquireDispatchSnapshot<TMessage>(
                    this,
                    targetedWithoutTargetingHandlers,
                    DispatchCategory.TargetedWithoutTargetingPost,
                    _emissionId
                );
                PrefreezeTargetedWithoutTargetingPostSnapshot<TMessage>(
                    targetedWithoutTargetingPostSnapshot
                );
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
                if (!_loggedReflexiveWarning)
                {
                    _loggedReflexiveWarning = true;
                    if (MessagingDebug.enabled)
                    {
                        MessagingDebug.Log(
                            LogLevel.Warn,
                            "ReflexiveMessage dispatch traverses the Unity hierarchy and is significantly slower than typed messages. Prefer targeted or broadcast messages where possible."
                        );
                    }
                }
#if UNITY_2021_3_OR_NEWER
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
                && sortedHandlers.handlers.Count > 0
            )
            {
                DispatchSnapshot snapshot = AcquireDispatchSnapshot<TMessage>(
                    this,
                    sortedHandlers,
                    DispatchCategory.Targeted,
                    _emissionId
                );
                DispatchBucket[] buckets = snapshot.buckets;
                int bucketCount = snapshot.bucketCount;
                for (int bucketIndex = 0; bucketIndex < bucketCount; ++bucketIndex)
                {
                    DispatchBucket bucket = buckets[bucketIndex];
                    DispatchEntry[] entries = bucket.entries;
                    int entryCount = bucket.entryCount;
                    if (entryCount == 0)
                    {
                        continue;
                    }

                    foundAnyHandlers = true;
                    int priority = bucket.priority;
                    switch (entryCount)
                    {
                        case 1:
                        {
                            InvokeTargetedEntry(ref target, ref typedMessage, priority, entries[0]);
                            continue;
                        }
                        case 2:
                        {
                            InvokeTargetedEntry(ref target, ref typedMessage, priority, entries[0]);
                            InvokeTargetedEntry(ref target, ref typedMessage, priority, entries[1]);
                            continue;
                        }
                        case 3:
                        {
                            InvokeTargetedEntry(ref target, ref typedMessage, priority, entries[0]);
                            InvokeTargetedEntry(ref target, ref typedMessage, priority, entries[1]);
                            InvokeTargetedEntry(ref target, ref typedMessage, priority, entries[2]);
                            continue;
                        }
                        case 4:
                        {
                            InvokeTargetedEntry(ref target, ref typedMessage, priority, entries[0]);
                            InvokeTargetedEntry(ref target, ref typedMessage, priority, entries[1]);
                            InvokeTargetedEntry(ref target, ref typedMessage, priority, entries[2]);
                            InvokeTargetedEntry(ref target, ref typedMessage, priority, entries[3]);
                            continue;
                        }
                        case 5:
                        {
                            InvokeTargetedEntry(ref target, ref typedMessage, priority, entries[0]);
                            InvokeTargetedEntry(ref target, ref typedMessage, priority, entries[1]);
                            InvokeTargetedEntry(ref target, ref typedMessage, priority, entries[2]);
                            InvokeTargetedEntry(ref target, ref typedMessage, priority, entries[3]);
                            InvokeTargetedEntry(ref target, ref typedMessage, priority, entries[4]);
                            continue;
                        }
                    }

                    for (int entryIndex = 0; entryIndex < entryCount; ++entryIndex)
                    {
                        InvokeTargetedEntry(
                            ref target,
                            ref typedMessage,
                            priority,
                            entries[entryIndex]
                        );
                    }
                }
            }

            if (InternalTargetedWithoutTargetingBroadcast(ref target, ref typedMessage))
            {
                foundAnyHandlers = true;
            }

            if (
                _postProcessingTargetedSinks.TryGetValue<TMessage>(out targetedHandlers)
                && targetedHandlers.TryGetValue(target, out sortedHandlers)
                && sortedHandlers.handlers.Count > 0
            )
            {
                DispatchSnapshot snapshot = targetedPostSnapshot.IsEmpty
                    ? AcquireDispatchSnapshot<TMessage>(
                        this,
                        sortedHandlers,
                        DispatchCategory.TargetedPost,
                        _emissionId
                    )
                    : targetedPostSnapshot;
                DispatchBucket[] buckets = snapshot.buckets;
                int bucketCount = snapshot.bucketCount;
                for (int bucketIndex = 0; bucketIndex < bucketCount; ++bucketIndex)
                {
                    DispatchBucket bucket = buckets[bucketIndex];
                    DispatchEntry[] entries = bucket.entries;
                    int entryCount = bucket.entryCount;
                    if (entryCount == 0)
                    {
                        continue;
                    }

                    foundAnyHandlers = true;
                    int priority = bucket.priority;
                    switch (entryCount)
                    {
                        case 1:
                        {
                            InvokeTargetedPostEntry(
                                ref target,
                                ref typedMessage,
                                priority,
                                entries[0]
                            );
                            continue;
                        }
                        case 2:
                        {
                            InvokeTargetedPostEntry(
                                ref target,
                                ref typedMessage,
                                priority,
                                entries[0]
                            );
                            InvokeTargetedPostEntry(
                                ref target,
                                ref typedMessage,
                                priority,
                                entries[1]
                            );
                            continue;
                        }
                        case 3:
                        {
                            InvokeTargetedPostEntry(
                                ref target,
                                ref typedMessage,
                                priority,
                                entries[0]
                            );
                            InvokeTargetedPostEntry(
                                ref target,
                                ref typedMessage,
                                priority,
                                entries[1]
                            );
                            InvokeTargetedPostEntry(
                                ref target,
                                ref typedMessage,
                                priority,
                                entries[2]
                            );
                            continue;
                        }
                        case 4:
                        {
                            InvokeTargetedPostEntry(
                                ref target,
                                ref typedMessage,
                                priority,
                                entries[0]
                            );
                            InvokeTargetedPostEntry(
                                ref target,
                                ref typedMessage,
                                priority,
                                entries[1]
                            );
                            InvokeTargetedPostEntry(
                                ref target,
                                ref typedMessage,
                                priority,
                                entries[2]
                            );
                            InvokeTargetedPostEntry(
                                ref target,
                                ref typedMessage,
                                priority,
                                entries[3]
                            );
                            continue;
                        }
                        case 5:
                        {
                            InvokeTargetedPostEntry(
                                ref target,
                                ref typedMessage,
                                priority,
                                entries[0]
                            );
                            InvokeTargetedPostEntry(
                                ref target,
                                ref typedMessage,
                                priority,
                                entries[1]
                            );
                            InvokeTargetedPostEntry(
                                ref target,
                                ref typedMessage,
                                priority,
                                entries[2]
                            );
                            InvokeTargetedPostEntry(
                                ref target,
                                ref typedMessage,
                                priority,
                                entries[3]
                            );
                            InvokeTargetedPostEntry(
                                ref target,
                                ref typedMessage,
                                priority,
                                entries[4]
                            );
                            continue;
                        }
                    }

                    for (int entryIndex = 0; entryIndex < entryCount; ++entryIndex)
                    {
                        InvokeTargetedPostEntry(
                            ref target,
                            ref typedMessage,
                            priority,
                            entries[entryIndex]
                        );
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
                DispatchSnapshot snapshot = targetedWithoutTargetingPostSnapshot.IsEmpty
                    ? AcquireDispatchSnapshot<TMessage>(
                        this,
                        postTwt,
                        DispatchCategory.TargetedWithoutTargetingPost,
                        _emissionId
                    )
                    : targetedWithoutTargetingPostSnapshot;
                DispatchBucket[] buckets = snapshot.buckets;
                int bucketCount = snapshot.bucketCount;
                for (int bucketIndex = 0; bucketIndex < bucketCount; ++bucketIndex)
                {
                    DispatchBucket bucket = buckets[bucketIndex];
                    DispatchEntry[] entries = bucket.entries;
                    int entryCount = bucket.entryCount;
                    if (entryCount == 0)
                    {
                        continue;
                    }

                    foundAnyHandlers = true;
                    int priority = bucket.priority;
                    switch (entryCount)
                    {
                        case 1:
                        {
                            InvokeTargetedWithoutTargetingPostEntry(
                                ref target,
                                ref typedMessage,
                                priority,
                                entries[0]
                            );
                            continue;
                        }
                        case 2:
                        {
                            InvokeTargetedWithoutTargetingPostEntry(
                                ref target,
                                ref typedMessage,
                                priority,
                                entries[0]
                            );
                            InvokeTargetedWithoutTargetingPostEntry(
                                ref target,
                                ref typedMessage,
                                priority,
                                entries[1]
                            );
                            continue;
                        }
                        case 3:
                        {
                            InvokeTargetedWithoutTargetingPostEntry(
                                ref target,
                                ref typedMessage,
                                priority,
                                entries[0]
                            );
                            InvokeTargetedWithoutTargetingPostEntry(
                                ref target,
                                ref typedMessage,
                                priority,
                                entries[1]
                            );
                            InvokeTargetedWithoutTargetingPostEntry(
                                ref target,
                                ref typedMessage,
                                priority,
                                entries[2]
                            );
                            continue;
                        }
                        case 4:
                        {
                            InvokeTargetedWithoutTargetingPostEntry(
                                ref target,
                                ref typedMessage,
                                priority,
                                entries[0]
                            );
                            InvokeTargetedWithoutTargetingPostEntry(
                                ref target,
                                ref typedMessage,
                                priority,
                                entries[1]
                            );
                            InvokeTargetedWithoutTargetingPostEntry(
                                ref target,
                                ref typedMessage,
                                priority,
                                entries[2]
                            );
                            InvokeTargetedWithoutTargetingPostEntry(
                                ref target,
                                ref typedMessage,
                                priority,
                                entries[3]
                            );
                            continue;
                        }
                        case 5:
                        {
                            InvokeTargetedWithoutTargetingPostEntry(
                                ref target,
                                ref typedMessage,
                                priority,
                                entries[0]
                            );
                            InvokeTargetedWithoutTargetingPostEntry(
                                ref target,
                                ref typedMessage,
                                priority,
                                entries[1]
                            );
                            InvokeTargetedWithoutTargetingPostEntry(
                                ref target,
                                ref typedMessage,
                                priority,
                                entries[2]
                            );
                            InvokeTargetedWithoutTargetingPostEntry(
                                ref target,
                                ref typedMessage,
                                priority,
                                entries[3]
                            );
                            InvokeTargetedWithoutTargetingPostEntry(
                                ref target,
                                ref typedMessage,
                                priority,
                                entries[4]
                            );
                            continue;
                        }
                    }

                    for (int entryIndex = 0; entryIndex < entryCount; ++entryIndex)
                    {
                        InvokeTargetedWithoutTargetingPostEntry(
                            ref target,
                            ref typedMessage,
                            priority,
                            entries[entryIndex]
                        );
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

        /// <inheritdoc />
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

        /// <inheritdoc />
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
            DispatchSnapshot broadcastPostSnapshot = DispatchSnapshot.Empty;
            DispatchSnapshot broadcastWithoutSourcePostSnapshot = DispatchSnapshot.Empty;
            if (
                _postProcessingBroadcastSinks.TryGetValue<TMessage>(
                    out Dictionary<
                        InstanceId,
                        HandlerCache<int, HandlerCache>
                    > broadcastPostHandlers
                )
                && broadcastPostHandlers.TryGetValue(
                    source,
                    out HandlerCache<int, HandlerCache> broadcastPostByPriority
                )
                && broadcastPostByPriority.handlers.Count > 0
            )
            {
                broadcastPostSnapshot = AcquireDispatchSnapshot<TMessage>(
                    this,
                    broadcastPostByPriority,
                    DispatchCategory.BroadcastPost,
                    _emissionId
                );
                PrefreezeBroadcastPostSnapshot<TMessage>(ref source, broadcastPostSnapshot);
            }
            if (
                _postProcessingBroadcastWithoutSourceSinks.TryGetValue<TMessage>(
                    out HandlerCache<int, HandlerCache> broadcastWithoutSourceHandlers
                )
                && broadcastWithoutSourceHandlers.handlers.Count > 0
            )
            {
                broadcastWithoutSourcePostSnapshot = AcquireDispatchSnapshot<TMessage>(
                    this,
                    broadcastWithoutSourceHandlers,
                    DispatchCategory.BroadcastWithoutSourcePost,
                    _emissionId
                );
                PrefreezeBroadcastWithoutSourcePostSnapshot<TMessage>(
                    broadcastWithoutSourcePostSnapshot
                );
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

            // Pre-freeze broadcast-without-source handler stacks for this emission
            if (
                _sinks.TryGetValue<TMessage>(out HandlerCache<int, HandlerCache> bwsHandlers)
                && bwsHandlers.handlers.Count > 0
            )
            {
                List<KeyValuePair<int, HandlerCache>> frozen = GetOrAddMessageHandlerStack(
                    bwsHandlers,
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
                            .PrefreezeBroadcastWithoutSourceHandlersForEmission<TMessage>(
                                entry.Key,
                                _emissionId,
                                this
                            );
                    }
                }
            }

            bool foundAnyHandlers = false;
            _ = _broadcastSinks.TryGetValue<TMessage>(
                out Dictionary<InstanceId, HandlerCache<int, HandlerCache>> broadcastHandlers
            );
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

            bool bwsFound = InternalBroadcastWithoutSource(ref source, ref typedMessage);

            if (
                _postProcessingBroadcastSinks.TryGetValue<TMessage>(out broadcastHandlers)
                && broadcastHandlers.TryGetValue(source, out sortedHandlers)
                && 0 < sortedHandlers.handlers.Count
            )
            {
                foundAnyHandlers = true;
                DispatchSnapshot snapshot = AcquireDispatchSnapshot<TMessage>(
                    this,
                    sortedHandlers,
                    DispatchCategory.BroadcastPost,
                    _emissionId
                );
                DispatchBucket[] buckets = snapshot.buckets;
                int bucketCount = snapshot.bucketCount;
                for (int bucketIndex = 0; bucketIndex < bucketCount; ++bucketIndex)
                {
                    DispatchBucket bucket = buckets[bucketIndex];
                    DispatchEntry[] entries = bucket.entries;
                    int entryCount = bucket.entryCount;
                    if (entryCount == 0)
                    {
                        continue;
                    }

                    foundAnyHandlers = true;
                    int priority = bucket.priority;
                    switch (entryCount)
                    {
                        case 1:
                        {
                            InvokeBroadcastPostEntry(
                                ref source,
                                ref typedMessage,
                                priority,
                                entries[0]
                            );
                            continue;
                        }
                        case 2:
                        {
                            InvokeBroadcastPostEntry(
                                ref source,
                                ref typedMessage,
                                priority,
                                entries[0]
                            );
                            InvokeBroadcastPostEntry(
                                ref source,
                                ref typedMessage,
                                priority,
                                entries[1]
                            );
                            continue;
                        }
                        case 3:
                        {
                            InvokeBroadcastPostEntry(
                                ref source,
                                ref typedMessage,
                                priority,
                                entries[0]
                            );
                            InvokeBroadcastPostEntry(
                                ref source,
                                ref typedMessage,
                                priority,
                                entries[1]
                            );
                            InvokeBroadcastPostEntry(
                                ref source,
                                ref typedMessage,
                                priority,
                                entries[2]
                            );
                            continue;
                        }
                        case 4:
                        {
                            InvokeBroadcastPostEntry(
                                ref source,
                                ref typedMessage,
                                priority,
                                entries[0]
                            );
                            InvokeBroadcastPostEntry(
                                ref source,
                                ref typedMessage,
                                priority,
                                entries[1]
                            );
                            InvokeBroadcastPostEntry(
                                ref source,
                                ref typedMessage,
                                priority,
                                entries[2]
                            );
                            InvokeBroadcastPostEntry(
                                ref source,
                                ref typedMessage,
                                priority,
                                entries[3]
                            );
                            continue;
                        }
                        case 5:
                        {
                            InvokeBroadcastPostEntry(
                                ref source,
                                ref typedMessage,
                                priority,
                                entries[0]
                            );
                            InvokeBroadcastPostEntry(
                                ref source,
                                ref typedMessage,
                                priority,
                                entries[1]
                            );
                            InvokeBroadcastPostEntry(
                                ref source,
                                ref typedMessage,
                                priority,
                                entries[2]
                            );
                            InvokeBroadcastPostEntry(
                                ref source,
                                ref typedMessage,
                                priority,
                                entries[3]
                            );
                            InvokeBroadcastPostEntry(
                                ref source,
                                ref typedMessage,
                                priority,
                                entries[4]
                            );
                            continue;
                        }
                    }

                    for (int entryIndex = 0; entryIndex < entryCount; ++entryIndex)
                    {
                        InvokeBroadcastPostEntry(
                            ref source,
                            ref typedMessage,
                            priority,
                            entries[entryIndex]
                        );
                    }
                }
            }

            if (
                _postProcessingBroadcastWithoutSourceSinks.TryGetValue<TMessage>(out sortedHandlers)
                && 0 < sortedHandlers.handlers.Count
            )
            {
                DispatchSnapshot snapshot = AcquireDispatchSnapshot<TMessage>(
                    this,
                    sortedHandlers,
                    DispatchCategory.BroadcastWithoutSourcePost,
                    _emissionId
                );
                DispatchBucket[] buckets = snapshot.buckets;
                int bucketCount = snapshot.bucketCount;
                for (int bucketIndex = 0; bucketIndex < bucketCount; ++bucketIndex)
                {
                    DispatchBucket bucket = buckets[bucketIndex];
                    DispatchEntry[] entries = bucket.entries;
                    int entryCount = bucket.entryCount;
                    if (entryCount == 0)
                    {
                        continue;
                    }

                    bwsFound = true;
                    int priority = bucket.priority;
                    switch (entryCount)
                    {
                        case 1:
                        {
                            InvokeBroadcastWithoutSourcePostEntry(
                                ref source,
                                ref typedMessage,
                                priority,
                                entries[0]
                            );
                            continue;
                        }
                        case 2:
                        {
                            InvokeBroadcastWithoutSourcePostEntry(
                                ref source,
                                ref typedMessage,
                                priority,
                                entries[0]
                            );
                            InvokeBroadcastWithoutSourcePostEntry(
                                ref source,
                                ref typedMessage,
                                priority,
                                entries[1]
                            );
                            continue;
                        }
                        case 3:
                        {
                            InvokeBroadcastWithoutSourcePostEntry(
                                ref source,
                                ref typedMessage,
                                priority,
                                entries[0]
                            );
                            InvokeBroadcastWithoutSourcePostEntry(
                                ref source,
                                ref typedMessage,
                                priority,
                                entries[1]
                            );
                            InvokeBroadcastWithoutSourcePostEntry(
                                ref source,
                                ref typedMessage,
                                priority,
                                entries[2]
                            );
                            continue;
                        }
                        case 4:
                        {
                            InvokeBroadcastWithoutSourcePostEntry(
                                ref source,
                                ref typedMessage,
                                priority,
                                entries[0]
                            );
                            InvokeBroadcastWithoutSourcePostEntry(
                                ref source,
                                ref typedMessage,
                                priority,
                                entries[1]
                            );
                            InvokeBroadcastWithoutSourcePostEntry(
                                ref source,
                                ref typedMessage,
                                priority,
                                entries[2]
                            );
                            InvokeBroadcastWithoutSourcePostEntry(
                                ref source,
                                ref typedMessage,
                                priority,
                                entries[3]
                            );
                            continue;
                        }
                        case 5:
                        {
                            InvokeBroadcastWithoutSourcePostEntry(
                                ref source,
                                ref typedMessage,
                                priority,
                                entries[0]
                            );
                            InvokeBroadcastWithoutSourcePostEntry(
                                ref source,
                                ref typedMessage,
                                priority,
                                entries[1]
                            );
                            InvokeBroadcastWithoutSourcePostEntry(
                                ref source,
                                ref typedMessage,
                                priority,
                                entries[2]
                            );
                            InvokeBroadcastWithoutSourcePostEntry(
                                ref source,
                                ref typedMessage,
                                priority,
                                entries[3]
                            );
                            InvokeBroadcastWithoutSourcePostEntry(
                                ref source,
                                ref typedMessage,
                                priority,
                                entries[4]
                            );
                            continue;
                        }
                    }

                    for (int entryIndex = 0; entryIndex < entryCount; ++entryIndex)
                    {
                        InvokeBroadcastWithoutSourcePostEntry(
                            ref source,
                            ref typedMessage,
                            priority,
                            entries[entryIndex]
                        );
                    }
                }
            }

            if (!(foundAnyHandlers || bwsFound) && MessagingDebug.enabled)
            {
                MessagingDebug.Log(
                    LogLevel.Info,
                    "Could not find a matching sourced broadcast handler for Id: {0}, Message: {1}.",
                    source,
                    typedMessage
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
            DispatchSnapshot snapshot = AcquireGlobalDispatchSnapshot<IUntargetedMessage>(
                this,
                _globalSinks,
                DispatchCategory.GlobalUntargeted,
                _emissionId
            );
            DispatchBucket[] buckets = snapshot.buckets;
            int bucketCount = snapshot.bucketCount;
            if (bucketCount == 0)
            {
                return;
            }

            for (int bucketIndex = 0; bucketIndex < bucketCount; ++bucketIndex)
            {
                DispatchBucket bucket = buckets[bucketIndex];
                DispatchEntry[] entries = bucket.entries;
                int entryCount = bucket.entryCount;
                if (entryCount == 0)
                {
                    continue;
                }

                switch (entryCount)
                {
                    case 1:
                    {
                        InvokeGlobalUntargetedEntry(ref message, entries[0]);
                        continue;
                    }
                    case 2:
                    {
                        InvokeGlobalUntargetedEntry(ref message, entries[0]);
                        InvokeGlobalUntargetedEntry(ref message, entries[1]);
                        continue;
                    }
                    case 3:
                    {
                        InvokeGlobalUntargetedEntry(ref message, entries[0]);
                        InvokeGlobalUntargetedEntry(ref message, entries[1]);
                        InvokeGlobalUntargetedEntry(ref message, entries[2]);
                        continue;
                    }
                    case 4:
                    {
                        InvokeGlobalUntargetedEntry(ref message, entries[0]);
                        InvokeGlobalUntargetedEntry(ref message, entries[1]);
                        InvokeGlobalUntargetedEntry(ref message, entries[2]);
                        InvokeGlobalUntargetedEntry(ref message, entries[3]);
                        continue;
                    }
                    case 5:
                    {
                        InvokeGlobalUntargetedEntry(ref message, entries[0]);
                        InvokeGlobalUntargetedEntry(ref message, entries[1]);
                        InvokeGlobalUntargetedEntry(ref message, entries[2]);
                        InvokeGlobalUntargetedEntry(ref message, entries[3]);
                        InvokeGlobalUntargetedEntry(ref message, entries[4]);
                        continue;
                    }
                }

                for (int entryIndex = 0; entryIndex < entryCount; ++entryIndex)
                {
                    InvokeGlobalUntargetedEntry(ref message, entries[entryIndex]);
                }
            }
        }

        private void BroadcastGlobalTargeted(ref InstanceId target, ref ITargetedMessage message)
        {
            DispatchSnapshot snapshot = AcquireGlobalDispatchSnapshot<ITargetedMessage>(
                this,
                _globalSinks,
                DispatchCategory.GlobalTargeted,
                _emissionId
            );
            DispatchBucket[] buckets = snapshot.buckets;
            int bucketCount = snapshot.bucketCount;
            if (bucketCount == 0)
            {
                return;
            }

            for (int bucketIndex = 0; bucketIndex < bucketCount; ++bucketIndex)
            {
                DispatchBucket bucket = buckets[bucketIndex];
                DispatchEntry[] entries = bucket.entries;
                int entryCount = bucket.entryCount;
                if (entryCount == 0)
                {
                    continue;
                }

                switch (entryCount)
                {
                    case 1:
                    {
                        InvokeGlobalTargetedEntry(ref target, ref message, entries[0]);
                        continue;
                    }
                    case 2:
                    {
                        InvokeGlobalTargetedEntry(ref target, ref message, entries[0]);
                        InvokeGlobalTargetedEntry(ref target, ref message, entries[1]);
                        continue;
                    }
                    case 3:
                    {
                        InvokeGlobalTargetedEntry(ref target, ref message, entries[0]);
                        InvokeGlobalTargetedEntry(ref target, ref message, entries[1]);
                        InvokeGlobalTargetedEntry(ref target, ref message, entries[2]);
                        continue;
                    }
                    case 4:
                    {
                        InvokeGlobalTargetedEntry(ref target, ref message, entries[0]);
                        InvokeGlobalTargetedEntry(ref target, ref message, entries[1]);
                        InvokeGlobalTargetedEntry(ref target, ref message, entries[2]);
                        InvokeGlobalTargetedEntry(ref target, ref message, entries[3]);
                        continue;
                    }
                    case 5:
                    {
                        InvokeGlobalTargetedEntry(ref target, ref message, entries[0]);
                        InvokeGlobalTargetedEntry(ref target, ref message, entries[1]);
                        InvokeGlobalTargetedEntry(ref target, ref message, entries[2]);
                        InvokeGlobalTargetedEntry(ref target, ref message, entries[3]);
                        InvokeGlobalTargetedEntry(ref target, ref message, entries[4]);
                        continue;
                    }
                }

                for (int entryIndex = 0; entryIndex < entryCount; ++entryIndex)
                {
                    InvokeGlobalTargetedEntry(ref target, ref message, entries[entryIndex]);
                }
            }
        }

        private void BroadcastGlobalSourcedBroadcast(
            ref InstanceId source,
            ref IBroadcastMessage message
        )
        {
            DispatchSnapshot snapshot = AcquireGlobalDispatchSnapshot<IBroadcastMessage>(
                this,
                _globalSinks,
                DispatchCategory.GlobalBroadcast,
                _emissionId
            );
            DispatchBucket[] buckets = snapshot.buckets;
            int bucketCount = snapshot.bucketCount;
            if (bucketCount == 0)
            {
                return;
            }

            for (int bucketIndex = 0; bucketIndex < bucketCount; ++bucketIndex)
            {
                DispatchBucket bucket = buckets[bucketIndex];
                DispatchEntry[] entries = bucket.entries;
                int entryCount = bucket.entryCount;
                if (entryCount == 0)
                {
                    continue;
                }

                switch (entryCount)
                {
                    case 1:
                    {
                        InvokeGlobalBroadcastEntry(ref source, ref message, entries[0]);
                        continue;
                    }
                    case 2:
                    {
                        InvokeGlobalBroadcastEntry(ref source, ref message, entries[0]);
                        InvokeGlobalBroadcastEntry(ref source, ref message, entries[1]);
                        continue;
                    }
                    case 3:
                    {
                        InvokeGlobalBroadcastEntry(ref source, ref message, entries[0]);
                        InvokeGlobalBroadcastEntry(ref source, ref message, entries[1]);
                        InvokeGlobalBroadcastEntry(ref source, ref message, entries[2]);
                        continue;
                    }
                    case 4:
                    {
                        InvokeGlobalBroadcastEntry(ref source, ref message, entries[0]);
                        InvokeGlobalBroadcastEntry(ref source, ref message, entries[1]);
                        InvokeGlobalBroadcastEntry(ref source, ref message, entries[2]);
                        InvokeGlobalBroadcastEntry(ref source, ref message, entries[3]);
                        continue;
                    }
                    case 5:
                    {
                        InvokeGlobalBroadcastEntry(ref source, ref message, entries[0]);
                        InvokeGlobalBroadcastEntry(ref source, ref message, entries[1]);
                        InvokeGlobalBroadcastEntry(ref source, ref message, entries[2]);
                        InvokeGlobalBroadcastEntry(ref source, ref message, entries[3]);
                        InvokeGlobalBroadcastEntry(ref source, ref message, entries[4]);
                        continue;
                    }
                }

                for (int entryIndex = 0; entryIndex < entryCount; ++entryIndex)
                {
                    InvokeGlobalBroadcastEntry(ref source, ref message, entries[entryIndex]);
                }
            }
        }

        private bool TryGetUntargetedInterceptorCaches<TMessage>(
            out SortedList<int, List<object>> interceptorHandlers,
            out List<object> interceptorObjects
        )
            where TMessage : IUntargetedMessage
        {
            if (
                !_untargetedInterceptsByType.TryGetValue<TMessage>(
                    out InterceptorCache<object> interceptors
                )
                || interceptors.handlers.Count == 0
            )
            {
                interceptorHandlers = default;
                interceptorObjects = default;
                return false;
            }

            interceptorHandlers = interceptors.handlers;

            if (!_innerInterceptorsStack.TryPop(out interceptorObjects))
            {
                interceptorObjects = new List<object>();
            }

            return true;
        }

        private bool TryGetTargetedInterceptorCaches<TMessage>(
            out SortedList<int, List<object>> interceptorHandlers,
            out List<object> interceptorObjects
        )
            where TMessage : ITargetedMessage
        {
            if (
                !_targetedInterceptsByType.TryGetValue<TMessage>(
                    out InterceptorCache<object> interceptors
                )
                || interceptors.handlers.Count == 0
            )
            {
                interceptorHandlers = default;
                interceptorObjects = default;
                return false;
            }

            interceptorHandlers = interceptors.handlers;

            if (!_innerInterceptorsStack.TryPop(out interceptorObjects))
            {
                interceptorObjects = new List<object>();
            }

            return true;
        }

        private bool TryGetBroadcastInterceptorCaches<TMessage>(
            out SortedList<int, List<object>> interceptorHandlers,
            out List<object> interceptorObjects
        )
            where TMessage : IBroadcastMessage
        {
            if (
                !_broadcastInterceptsByType.TryGetValue<TMessage>(
                    out InterceptorCache<object> interceptors
                )
                || interceptors.handlers.Count == 0
            )
            {
                interceptorHandlers = default;
                interceptorObjects = default;
                return false;
            }

            interceptorHandlers = interceptors.handlers;

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
                    out SortedList<int, List<object>> interceptorHandlers,
                    out List<object> interceptorObjects
                )
            )
            {
                return true;
            }

            try
            {
                IList<List<object>> prioritizedInterceptors = interceptorHandlers.Values;
                for (int s = 0; s < prioritizedInterceptors.Count; ++s)
                {
                    interceptorObjects.Clear();
                    List<object> interceptors = prioritizedInterceptors[s];
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
                    out SortedList<int, List<object>> interceptorHandlers,
                    out List<object> interceptorObjects
                )
            )
            {
                return true;
            }

            try
            {
                IList<List<object>> prioritizedInterceptors = interceptorHandlers.Values;
                for (int s = 0; s < prioritizedInterceptors.Count; ++s)
                {
                    interceptorObjects.Clear();
                    List<object> interceptors = prioritizedInterceptors[s];
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
                    out SortedList<int, List<object>> interceptorHandlers,
                    out List<object> interceptorObjects
                )
            )
            {
                return true;
            }

            try
            {
                IList<List<object>> prioritizedInterceptors = interceptorHandlers.Values;
                for (int s = 0; s < prioritizedInterceptors.Count; ++s)
                {
                    interceptorObjects.Clear();
                    List<object> interceptors = prioritizedInterceptors[s];
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

            DispatchSnapshot snapshot = AcquireDispatchSnapshot<TMessage>(
                this,
                sortedHandlers,
                DispatchCategory.Untargeted,
                _emissionId
            );
            DispatchBucket[] buckets = snapshot.buckets;
            int bucketCount = snapshot.bucketCount;

            if (bucketCount == 0)
            {
                return false;
            }

            bool invoked = false;

            for (int i = 0; i < bucketCount; ++i)
            {
                DispatchBucket bucket = buckets[i];
                DispatchEntry[] entries = bucket.entries;
                int entryCount = bucket.entryCount;
                if (entryCount == 0)
                {
                    continue;
                }

                invoked = true;
                int priority = bucket.priority;
                switch (entryCount)
                {
                    case 1:
                    {
                        InvokeUntargetedEntry(ref message, priority, entries[0]);
                        continue;
                    }
                    case 2:
                    {
                        InvokeUntargetedEntry(ref message, priority, entries[0]);
                        InvokeUntargetedEntry(ref message, priority, entries[1]);
                        continue;
                    }
                    case 3:
                    {
                        InvokeUntargetedEntry(ref message, priority, entries[0]);
                        InvokeUntargetedEntry(ref message, priority, entries[1]);
                        InvokeUntargetedEntry(ref message, priority, entries[2]);
                        continue;
                    }
                    case 4:
                    {
                        InvokeUntargetedEntry(ref message, priority, entries[0]);
                        InvokeUntargetedEntry(ref message, priority, entries[1]);
                        InvokeUntargetedEntry(ref message, priority, entries[2]);
                        InvokeUntargetedEntry(ref message, priority, entries[3]);
                        continue;
                    }
                    case 5:
                    {
                        InvokeUntargetedEntry(ref message, priority, entries[0]);
                        InvokeUntargetedEntry(ref message, priority, entries[1]);
                        InvokeUntargetedEntry(ref message, priority, entries[2]);
                        InvokeUntargetedEntry(ref message, priority, entries[3]);
                        InvokeUntargetedEntry(ref message, priority, entries[4]);
                        continue;
                    }
                }

                for (int handlerIndex = 0; handlerIndex < entryCount; ++handlerIndex)
                {
                    InvokeUntargetedEntry(ref message, priority, entries[handlerIndex]);
                }
            }

            return invoked;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void InvokeUntargetedEntry<TMessage>(
            ref TMessage message,
            int priority,
            DispatchEntry entry
        )
            where TMessage : IMessage
        {
            MessageHandler messageHandler = entry.handler;
            if (!messageHandler.active)
            {
                return;
            }

            MessageHandler.UntargetedDispatchLink<TMessage> link =
                Unsafe.As<MessageHandler.UntargetedDispatchLink<TMessage>>(entry.dispatch);
            link.Invoke(messageHandler, ref message, priority, _emissionId);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void InvokeUntargetedPostEntry<TMessage>(
            ref TMessage message,
            int priority,
            DispatchEntry entry
        )
            where TMessage : IUntargetedMessage
        {
            MessageHandler handler = entry.handler;
            if (!handler.active)
            {
                return;
            }

            MessageHandler.UntargetedPostDispatchLink<TMessage> link =
                Unsafe.As<MessageHandler.UntargetedPostDispatchLink<TMessage>>(entry.dispatch);
            link.Invoke(handler, ref message, priority, _emissionId);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void PrefreezeUntargetedPostSnapshot<TMessage>(DispatchSnapshot snapshot)
            where TMessage : IUntargetedMessage
        {
            if (snapshot.IsEmpty)
            {
                return;
            }

            DispatchBucket[] buckets = snapshot.buckets;
            int bucketCount = snapshot.bucketCount;
            for (int bucketIndex = 0; bucketIndex < bucketCount; ++bucketIndex)
            {
                DispatchBucket bucket = buckets[bucketIndex];
                DispatchEntry[] entries = bucket.entries;
                int entryCount = bucket.entryCount;
                if (entryCount == 0)
                {
                    continue;
                }

                int priority = bucket.priority;
                for (int entryIndex = 0; entryIndex < entryCount; ++entryIndex)
                {
                    entries[entryIndex]
                        .handler.PrefreezeUntargetedPostProcessorsForEmission<TMessage>(
                            priority,
                            _emissionId,
                            this
                        );
                }
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

            DispatchSnapshot snapshot = AcquireDispatchSnapshot<TMessage>(
                this,
                sortedHandlers,
                DispatchCategory.TargetedWithoutTargeting,
                _emissionId
            );
            DispatchBucket[] buckets = snapshot.buckets;
            int bucketCount = snapshot.bucketCount;
            bool invoked = false;

            for (int bucketIndex = 0; bucketIndex < bucketCount; ++bucketIndex)
            {
                DispatchBucket bucket = buckets[bucketIndex];
                DispatchEntry[] entries = bucket.entries;
                int entryCount = bucket.entryCount;
                if (entryCount == 0)
                {
                    continue;
                }

                invoked = true;
                int priority = bucket.priority;
                if (entries[0].prefreeze.kind == PrefreezeKindTargetedWithoutTargetingHandlers)
                {
                    PrefreezeTargetedWithoutTargetingEntries<TMessage>(
                        entries,
                        entryCount,
                        priority
                    );
                }
                switch (entryCount)
                {
                    case 1:
                    {
                        InvokeTargetedWithoutTargetingEntry(
                            ref target,
                            ref message,
                            priority,
                            entries[0]
                        );
                        continue;
                    }
                    case 2:
                    {
                        InvokeTargetedWithoutTargetingEntry(
                            ref target,
                            ref message,
                            priority,
                            entries[0]
                        );
                        InvokeTargetedWithoutTargetingEntry(
                            ref target,
                            ref message,
                            priority,
                            entries[1]
                        );
                        continue;
                    }
                    case 3:
                    {
                        InvokeTargetedWithoutTargetingEntry(
                            ref target,
                            ref message,
                            priority,
                            entries[0]
                        );
                        InvokeTargetedWithoutTargetingEntry(
                            ref target,
                            ref message,
                            priority,
                            entries[1]
                        );
                        InvokeTargetedWithoutTargetingEntry(
                            ref target,
                            ref message,
                            priority,
                            entries[2]
                        );
                        continue;
                    }
                    case 4:
                    {
                        InvokeTargetedWithoutTargetingEntry(
                            ref target,
                            ref message,
                            priority,
                            entries[0]
                        );
                        InvokeTargetedWithoutTargetingEntry(
                            ref target,
                            ref message,
                            priority,
                            entries[1]
                        );
                        InvokeTargetedWithoutTargetingEntry(
                            ref target,
                            ref message,
                            priority,
                            entries[2]
                        );
                        InvokeTargetedWithoutTargetingEntry(
                            ref target,
                            ref message,
                            priority,
                            entries[3]
                        );
                        continue;
                    }
                    case 5:
                    {
                        InvokeTargetedWithoutTargetingEntry(
                            ref target,
                            ref message,
                            priority,
                            entries[0]
                        );
                        InvokeTargetedWithoutTargetingEntry(
                            ref target,
                            ref message,
                            priority,
                            entries[1]
                        );
                        InvokeTargetedWithoutTargetingEntry(
                            ref target,
                            ref message,
                            priority,
                            entries[2]
                        );
                        InvokeTargetedWithoutTargetingEntry(
                            ref target,
                            ref message,
                            priority,
                            entries[3]
                        );
                        InvokeTargetedWithoutTargetingEntry(
                            ref target,
                            ref message,
                            priority,
                            entries[4]
                        );
                        continue;
                    }
                }

                for (int entryIndex = 0; entryIndex < entryCount; ++entryIndex)
                {
                    InvokeTargetedWithoutTargetingEntry(
                        ref target,
                        ref message,
                        priority,
                        entries[entryIndex]
                    );
                }
            }

            return invoked;
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
            // Freeze each handler's typed caches for this emission/priority to ensure snapshot semantics
            for (int j = 0; j < messageHandlers.Count; ++j)
            {
                messageHandlers[j]
                    .PrefreezeTargetedWithoutTargetingHandlersForEmission<TMessage>(
                        priority,
                        _emissionId,
                        this
                    );
            }
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
            // Ensure each handler's typed no-source caches are frozen for this emission/priority
            for (int j = 0; j < messageHandlersCount; ++j)
            {
                messageHandlers[j]
                    .PrefreezeBroadcastWithoutSourceHandlersForEmission<TMessage>(
                        priority,
                        _emissionId,
                        this
                    );
            }
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
            HandlerCache<int, HandlerCache> capturedHandlers = handlers;
            DispatchCategory dispatchCategory = GetDispatchCategory(registrationMethod);

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
            StageDispatchSnapshot<T>(this, capturedHandlers, dispatchCategory);
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
                StageDispatchSnapshot<T>(this, handlers, dispatchCategory);
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
            DispatchCategory dispatchCategory = GetDispatchCategory(registrationMethod);

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
            StageDispatchSnapshot<T>(this, handlers, dispatchCategory);

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
                StageDispatchSnapshot<T>(this, handlers, dispatchCategory);
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void StageDispatchSnapshot<TMessage>(
            MessageBus messageBus,
            HandlerCache<int, HandlerCache> handlers,
            DispatchCategory category
        )
            where TMessage : IMessage
        {
            if (handlers == null || category == DispatchCategory.None)
            {
                return;
            }

            HandlerCache<int, HandlerCache>.DispatchState state = handlers.GetOrCreateDispatchState(
                category
            );
            if (state.hasPending)
            {
                ReleaseSnapshot(ref state.pending);
            }
            state.hasPending = true;
            state.pendingDirty = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void StageGlobalDispatchSnapshot<TMessage>(
            MessageBus messageBus,
            HandlerCache handlers,
            DispatchCategory category
        )
            where TMessage : IMessage
        {
            if (handlers == null || category == DispatchCategory.None)
            {
                return;
            }

            HandlerCache.DispatchState state = handlers.GetOrCreateDispatchState(category);
            if (state.hasPending)
            {
                ReleaseSnapshot(ref state.pending);
            }

            state.hasPending = true;
            state.pendingDirty = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ReleaseSnapshot(ref DispatchSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            snapshot.Release();
            snapshot = DispatchSnapshot.Empty;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static DispatchSnapshot AcquireDispatchSnapshot<TMessage>(
            MessageBus messageBus,
            HandlerCache<int, HandlerCache> handlers,
            DispatchCategory category,
            long emissionId
        )
            where TMessage : IMessage
        {
            if (handlers == null)
            {
                return DispatchSnapshot.Empty;
            }

            if (category == DispatchCategory.None)
            {
                return DispatchSnapshot.Empty;
            }

            HandlerCache<int, HandlerCache>.DispatchState state = handlers.GetOrCreateDispatchState(
                category
            );

            bool hasHandlers = handlers.handlers.Count > 0;

            if (state.hasPending)
            {
                if (state.pendingDirty || (hasHandlers && state.pending.IsEmpty))
                {
                    ReleaseSnapshot(ref state.pending);
                    if (hasHandlers)
                    {
                        state.pending = BuildDispatchSnapshot<TMessage>(
                            messageBus,
                            handlers,
                            category
                        );
                    }
                    else
                    {
                        state.pending = DispatchSnapshot.Empty;
                    }

                    state.pendingDirty = false;
                }
            }
            else if (state.active.IsEmpty && hasHandlers)
            {
                ReleaseSnapshot(ref state.pending);
                state.pending = BuildDispatchSnapshot<TMessage>(messageBus, handlers, category);
                state.hasPending = true;
                state.pendingDirty = false;
            }

            if (state.snapshotEmissionId != emissionId)
            {
                if (state.hasPending)
                {
                    ReleaseSnapshot(ref state.active);
                    if (state.pendingDirty || (hasHandlers && state.pending.IsEmpty))
                    {
                        ReleaseSnapshot(ref state.pending);
                        if (hasHandlers)
                        {
                            state.pending = BuildDispatchSnapshot<TMessage>(
                                messageBus,
                                handlers,
                                category
                            );
                        }
                        else
                        {
                            state.pending = DispatchSnapshot.Empty;
                        }

                        state.pendingDirty = false;
                    }

                    state.active = state.pending ?? DispatchSnapshot.Empty;
                    state.pending = DispatchSnapshot.Empty;
                    state.hasPending = false;
                    state.pendingDirty = false;
                }
                else if (!hasHandlers && !state.active.IsEmpty)
                {
                    ReleaseSnapshot(ref state.active);
                    state.active = DispatchSnapshot.Empty;
                }

                state.snapshotEmissionId = emissionId;
            }

            return state.active;
        }

        private static DispatchSnapshot BuildDispatchSnapshot<TMessage>(
            MessageBus messageBus,
            HandlerCache<int, HandlerCache> handlers,
            DispatchCategory category
        )
            where TMessage : IMessage
        {
            if (handlers == null || handlers.order.Count == 0)
            {
                return DispatchSnapshot.Empty;
            }

            List<int> orderedPriorities = handlers.order;
            int priorityCount = orderedPriorities.Count;
            DispatchBucket[] buckets = DispatchBucketPool.Rent(priorityCount);

            for (int i = 0; i < priorityCount; ++i)
            {
                int priority = orderedPriorities[i];
                if (
                    !handlers.handlers.TryGetValue(priority, out HandlerCache cache)
                    || cache == null
                )
                {
                    buckets[i] = DispatchBucket.CreateEmpty(priority);
                    continue;
                }

                Dictionary<MessageHandler, int> handlerLookup = cache.handlers;
                if (handlerLookup == null || handlerLookup.Count == 0)
                {
                    buckets[i] = DispatchBucket.CreateEmpty(priority);
                    continue;
                }

                int entryCount = handlerLookup.Count;
                DispatchEntry[] entries = DispatchEntryPool.Rent(entryCount);
                FillDispatchEntries<TMessage>(
                    messageBus,
                    handlerLookup,
                    category,
                    priority,
                    entries
                );
                buckets[i] = new DispatchBucket(priority, entries, entryCount, pooledEntries: true);
            }

            return new DispatchSnapshot(buckets, priorityCount, pooled: true);
        }

        private static void FillDispatchEntries<TMessage>(
            MessageBus messageBus,
            Dictionary<MessageHandler, int> handlerLookup,
            DispatchCategory category,
            int priority,
            DispatchEntry[] entries
        )
            where TMessage : IMessage
        {
            if (handlerLookup == null)
            {
                return;
            }

            PrefreezeDescriptor prefreeze = CreatePrefreezeDescriptor(category, priority);
            int index = 0;
            foreach (KeyValuePair<MessageHandler, int> kvp in handlerLookup)
            {
                MessageHandler messageHandler = kvp.Key;
                object dispatch = GetDispatchLink<TMessage>(messageBus, messageHandler, category);
                entries[index++] = new DispatchEntry(messageHandler, dispatch, prefreeze);
            }
            if (index < entries.Length)
            {
                Array.Clear(entries, index, entries.Length - index);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static DispatchSnapshot AcquireGlobalDispatchSnapshot<TMessage>(
            MessageBus messageBus,
            HandlerCache handlers,
            DispatchCategory category,
            long emissionId
        )
            where TMessage : IMessage
        {
            if (handlers == null || category == DispatchCategory.None)
            {
                return DispatchSnapshot.Empty;
            }

            HandlerCache.DispatchState state = handlers.GetOrCreateDispatchState(category);
            bool hasHandlers = handlers.handlers.Count > 0;

            if (state.hasPending)
            {
                if (state.pendingDirty || (hasHandlers && state.pending.IsEmpty))
                {
                    ReleaseSnapshot(ref state.pending);
                    if (hasHandlers)
                    {
                        state.pending = BuildGlobalDispatchSnapshot<TMessage>(
                            messageBus,
                            handlers,
                            category
                        );
                    }
                    else
                    {
                        state.pending = DispatchSnapshot.Empty;
                    }

                    state.pendingDirty = false;
                }
            }
            else if (state.active.IsEmpty && hasHandlers)
            {
                ReleaseSnapshot(ref state.pending);
                state.pending = BuildGlobalDispatchSnapshot<TMessage>(
                    messageBus,
                    handlers,
                    category
                );
                state.hasPending = true;
                state.pendingDirty = false;
            }

            if (state.snapshotEmissionId != emissionId)
            {
                if (state.hasPending)
                {
                    ReleaseSnapshot(ref state.active);
                    if (state.pendingDirty || (hasHandlers && state.pending.IsEmpty))
                    {
                        ReleaseSnapshot(ref state.pending);
                        if (hasHandlers)
                        {
                            state.pending = BuildGlobalDispatchSnapshot<TMessage>(
                                messageBus,
                                handlers,
                                category
                            );
                        }
                        else
                        {
                            state.pending = DispatchSnapshot.Empty;
                        }

                        state.pendingDirty = false;
                    }

                    state.active = state.pending ?? DispatchSnapshot.Empty;
                    state.pending = DispatchSnapshot.Empty;
                    state.hasPending = false;
                    state.pendingDirty = false;
                }
                else if (!hasHandlers && !state.active.IsEmpty)
                {
                    ReleaseSnapshot(ref state.active);
                    state.active = DispatchSnapshot.Empty;
                }

                state.snapshotEmissionId = emissionId;
            }

            return state.active;
        }

        private static DispatchSnapshot BuildGlobalDispatchSnapshot<TMessage>(
            MessageBus messageBus,
            HandlerCache handlers,
            DispatchCategory category
        )
            where TMessage : IMessage
        {
            if (handlers == null || handlers.handlers.Count == 0)
            {
                return DispatchSnapshot.Empty;
            }

            DispatchBucket[] buckets = DispatchBucketPool.Rent(1);
            Dictionary<MessageHandler, int> handlerLookup = handlers.handlers;
            int entryCount = handlerLookup.Count;
            DispatchEntry[] entries = DispatchEntryPool.Rent(entryCount);
            PrefreezeDescriptor prefreeze = CreatePrefreezeDescriptor(category, 0);
            int index = 0;
            foreach (KeyValuePair<MessageHandler, int> kvp in handlerLookup)
            {
                MessageHandler messageHandler = kvp.Key;
                object dispatch = GetDispatchLink<TMessage>(messageBus, messageHandler, category);
                entries[index++] = new DispatchEntry(messageHandler, dispatch, prefreeze);
            }

            buckets[0] = new DispatchBucket(0, entries, entryCount, pooledEntries: true);
            return new DispatchSnapshot(buckets, 1, pooled: true);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static PrefreezeDescriptor CreatePrefreezeDescriptor(
            DispatchCategory category,
            int priority
        )
        {
            switch (category)
            {
                case DispatchCategory.TargetedWithoutTargeting:
                    return new PrefreezeDescriptor(
                        PrefreezeKindTargetedWithoutTargetingHandlers,
                        priority
                    );
                case DispatchCategory.BroadcastWithoutSource:
                    return new PrefreezeDescriptor(
                        PrefreezeKindBroadcastWithoutSourceHandlers,
                        priority
                    );
                case DispatchCategory.GlobalUntargeted:
                    return new PrefreezeDescriptor(PrefreezeKindGlobalUntargetedHandlers, priority);
                case DispatchCategory.GlobalTargeted:
                    return new PrefreezeDescriptor(PrefreezeKindGlobalTargetedHandlers, priority);
                case DispatchCategory.GlobalBroadcast:
                    return new PrefreezeDescriptor(PrefreezeKindGlobalBroadcastHandlers, priority);
                default:
                    return PrefreezeDescriptor.Empty;
            }
        }

        private static object GetDispatchLink<TMessage>(
            MessageBus messageBus,
            MessageHandler handler,
            DispatchCategory category
        )
            where TMessage : IMessage
        {
            switch (category)
            {
                case DispatchCategory.Untargeted:
                    return handler.GetOrCreateUntargetedDispatchLink<TMessage>(messageBus);
                case DispatchCategory.UntargetedPost:
                    return handler.GetOrCreateUntargetedPostDispatchLink<TMessage>(messageBus);
                case DispatchCategory.Targeted:
                    return handler.GetOrCreateTargetedDispatchLink<TMessage>(messageBus);
                case DispatchCategory.TargetedPost:
                    return handler.GetOrCreateTargetedPostDispatchLink<TMessage>(messageBus);
                case DispatchCategory.TargetedWithoutTargeting:
                    return handler.GetOrCreateTargetedWithoutTargetingDispatchLink<TMessage>(
                        messageBus
                    );
                case DispatchCategory.TargetedWithoutTargetingPost:
                    return handler.GetOrCreateTargetedWithoutTargetingPostDispatchLink<TMessage>(
                        messageBus
                    );
                case DispatchCategory.Broadcast:
                    return handler.GetOrCreateBroadcastDispatchLink<TMessage>(messageBus);
                case DispatchCategory.BroadcastPost:
                    return handler.GetOrCreateBroadcastPostDispatchLink<TMessage>(messageBus);
                case DispatchCategory.BroadcastWithoutSource:
                    return handler.GetOrCreateBroadcastWithoutSourceDispatchLink<TMessage>(
                        messageBus
                    );
                case DispatchCategory.BroadcastWithoutSourcePost:
                    return handler.GetOrCreateBroadcastWithoutSourcePostDispatchLink<TMessage>(
                        messageBus
                    );
                case DispatchCategory.GlobalUntargeted:
                case DispatchCategory.GlobalTargeted:
                case DispatchCategory.GlobalBroadcast:
                    return null;
                default:
                    return handler.GetOrCreateUntargetedDispatchLink<TMessage>(messageBus);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static DispatchCategory GetDispatchCategory(RegistrationMethod registrationMethod)
        {
            switch (registrationMethod)
            {
                case RegistrationMethod.Untargeted:
                    return DispatchCategory.Untargeted;
                case RegistrationMethod.UntargetedPostProcessor:
                    return DispatchCategory.UntargetedPost;
                case RegistrationMethod.Targeted:
                    return DispatchCategory.Targeted;
                case RegistrationMethod.TargetedPostProcessor:
                    return DispatchCategory.TargetedPost;
                case RegistrationMethod.TargetedWithoutTargeting:
                    return DispatchCategory.TargetedWithoutTargeting;
                case RegistrationMethod.TargetedWithoutTargetingPostProcessor:
                    return DispatchCategory.TargetedWithoutTargetingPost;
                case RegistrationMethod.Broadcast:
                    return DispatchCategory.Broadcast;
                case RegistrationMethod.BroadcastPostProcessor:
                    return DispatchCategory.BroadcastPost;
                case RegistrationMethod.BroadcastWithoutSource:
                    return DispatchCategory.BroadcastWithoutSource;
                case RegistrationMethod.BroadcastWithoutSourcePostProcessor:
                    return DispatchCategory.BroadcastWithoutSourcePost;
                default:
                    return DispatchCategory.None;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void InvokeTargetedEntry<TMessage>(
            ref InstanceId target,
            ref TMessage message,
            int priority,
            DispatchEntry entry
        )
            where TMessage : ITargetedMessage
        {
            MessageHandler handler = entry.handler;
            if (!handler.active)
            {
                return;
            }

            MessageHandler.TargetedDispatchLink<TMessage> link =
                Unsafe.As<MessageHandler.TargetedDispatchLink<TMessage>>(entry.dispatch);
            link.Invoke(handler, ref target, ref message, priority, _emissionId);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void InvokeTargetedPostEntry<TMessage>(
            ref InstanceId target,
            ref TMessage message,
            int priority,
            DispatchEntry entry
        )
            where TMessage : ITargetedMessage
        {
            MessageHandler handler = entry.handler;
            if (!handler.active)
            {
                return;
            }

            MessageHandler.TargetedPostDispatchLink<TMessage> link =
                Unsafe.As<MessageHandler.TargetedPostDispatchLink<TMessage>>(entry.dispatch);
            link.Invoke(handler, ref target, ref message, priority, _emissionId);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void PrefreezeTargetedPostSnapshot<TMessage>(
            ref InstanceId target,
            DispatchSnapshot snapshot
        )
            where TMessage : ITargetedMessage
        {
            if (snapshot.IsEmpty)
            {
                return;
            }

            DispatchBucket[] buckets = snapshot.buckets;
            int bucketCount = snapshot.bucketCount;
            for (int bucketIndex = 0; bucketIndex < bucketCount; ++bucketIndex)
            {
                DispatchBucket bucket = buckets[bucketIndex];
                DispatchEntry[] entries = bucket.entries;
                int entryCount = bucket.entryCount;
                if (entryCount == 0)
                {
                    continue;
                }

                int priority = bucket.priority;
                for (int entryIndex = 0; entryIndex < entryCount; ++entryIndex)
                {
                    entries[entryIndex]
                        .handler.PrefreezeTargetedPostProcessorsForEmission<TMessage>(
                            target,
                            priority,
                            _emissionId,
                            this
                        );
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void InvokeGlobalUntargetedEntry<TMessage>(
            ref TMessage message,
            DispatchEntry entry
        )
            where TMessage : IUntargetedMessage
        {
            MessageHandler handler = entry.handler;
            if (entry.prefreeze.kind == PrefreezeKindGlobalUntargetedHandlers)
            {
                handler.PrefreezeGlobalUntargetedForEmission(_emissionId, this);
            }

            if (!handler.active)
            {
                return;
            }

            ref IUntargetedMessage interfaceMessage = ref Unsafe.As<TMessage, IUntargetedMessage>(
                ref message
            );
            handler.HandleGlobalUntargetedMessage(ref interfaceMessage, this);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void InvokeGlobalTargetedEntry<TMessage>(
            ref InstanceId target,
            ref TMessage message,
            DispatchEntry entry
        )
            where TMessage : ITargetedMessage
        {
            MessageHandler handler = entry.handler;
            if (entry.prefreeze.kind == PrefreezeKindGlobalTargetedHandlers)
            {
                handler.PrefreezeGlobalTargetedForEmission(_emissionId, this);
            }

            if (!handler.active)
            {
                return;
            }

            ref ITargetedMessage interfaceMessage = ref Unsafe.As<TMessage, ITargetedMessage>(
                ref message
            );
            handler.HandleGlobalTargetedMessage(ref target, ref interfaceMessage, this);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void InvokeGlobalBroadcastEntry<TMessage>(
            ref InstanceId source,
            ref TMessage message,
            DispatchEntry entry
        )
            where TMessage : IBroadcastMessage
        {
            MessageHandler handler = entry.handler;
            if (entry.prefreeze.kind == PrefreezeKindGlobalBroadcastHandlers)
            {
                handler.PrefreezeGlobalBroadcastForEmission(_emissionId, this);
            }

            if (!handler.active)
            {
                return;
            }

            ref IBroadcastMessage interfaceMessage = ref Unsafe.As<TMessage, IBroadcastMessage>(
                ref message
            );
            handler.HandleGlobalSourcedBroadcastMessage(ref source, ref interfaceMessage, this);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void PrefreezeTargetedWithoutTargetingEntries<TMessage>(
            DispatchEntry[] entries,
            int entryCount,
            int priority
        )
            where TMessage : ITargetedMessage
        {
            for (int entryIndex = 0; entryIndex < entryCount; ++entryIndex)
            {
                entries[entryIndex]
                    .handler.PrefreezeTargetedWithoutTargetingHandlersForEmission<TMessage>(
                        priority,
                        _emissionId,
                        this
                    );
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void InvokeTargetedWithoutTargetingEntry<TMessage>(
            ref InstanceId target,
            ref TMessage message,
            int priority,
            DispatchEntry entry
        )
            where TMessage : ITargetedMessage
        {
            MessageHandler handler = entry.handler;
            if (!handler.active)
            {
                return;
            }

            MessageHandler.TargetedWithoutTargetingDispatchLink<TMessage> link =
                Unsafe.As<MessageHandler.TargetedWithoutTargetingDispatchLink<TMessage>>(
                    entry.dispatch
                );
            link.Invoke(handler, ref target, ref message, priority, _emissionId);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void InvokeTargetedWithoutTargetingPostEntry<TMessage>(
            ref InstanceId target,
            ref TMessage message,
            int priority,
            DispatchEntry entry
        )
            where TMessage : ITargetedMessage
        {
            MessageHandler handler = entry.handler;
            if (!handler.active)
            {
                return;
            }

            MessageHandler.TargetedWithoutTargetingPostDispatchLink<TMessage> link =
                Unsafe.As<MessageHandler.TargetedWithoutTargetingPostDispatchLink<TMessage>>(
                    entry.dispatch
                );
            link.Invoke(handler, ref target, ref message, priority, _emissionId);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void PrefreezeTargetedWithoutTargetingPostSnapshot<TMessage>(
            DispatchSnapshot snapshot
        )
            where TMessage : ITargetedMessage
        {
            if (snapshot.IsEmpty)
            {
                return;
            }

            DispatchBucket[] buckets = snapshot.buckets;
            int bucketCount = snapshot.bucketCount;
            for (int bucketIndex = 0; bucketIndex < bucketCount; ++bucketIndex)
            {
                DispatchBucket bucket = buckets[bucketIndex];
                DispatchEntry[] entries = bucket.entries;
                int entryCount = bucket.entryCount;
                if (entryCount == 0)
                {
                    continue;
                }

                int priority = bucket.priority;
                for (int entryIndex = 0; entryIndex < entryCount; ++entryIndex)
                {
                    entries[entryIndex]
                        .handler.PrefreezeTargetedWithoutTargetingPostProcessorsForEmission<TMessage>(
                            priority,
                            _emissionId,
                            this
                        );
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void InvokeBroadcastEntry<TMessage>(
            ref InstanceId source,
            ref TMessage message,
            int priority,
            DispatchEntry entry
        )
            where TMessage : IBroadcastMessage
        {
            MessageHandler handler = entry.handler;
            if (!handler.active)
            {
                return;
            }

            MessageHandler.BroadcastDispatchLink<TMessage> link =
                Unsafe.As<MessageHandler.BroadcastDispatchLink<TMessage>>(entry.dispatch);
            link.Invoke(handler, ref source, ref message, priority, _emissionId);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void InvokeBroadcastPostEntry<TMessage>(
            ref InstanceId source,
            ref TMessage message,
            int priority,
            DispatchEntry entry
        )
            where TMessage : IBroadcastMessage
        {
            MessageHandler handler = entry.handler;
            if (!handler.active)
            {
                return;
            }

            MessageHandler.BroadcastPostDispatchLink<TMessage> link =
                Unsafe.As<MessageHandler.BroadcastPostDispatchLink<TMessage>>(entry.dispatch);
            link.Invoke(handler, ref source, ref message, priority, _emissionId);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void PrefreezeBroadcastPostSnapshot<TMessage>(
            ref InstanceId source,
            DispatchSnapshot snapshot
        )
            where TMessage : IBroadcastMessage
        {
            if (snapshot.IsEmpty)
            {
                return;
            }

            DispatchBucket[] buckets = snapshot.buckets;
            int bucketCount = snapshot.bucketCount;
            for (int bucketIndex = 0; bucketIndex < bucketCount; ++bucketIndex)
            {
                DispatchBucket bucket = buckets[bucketIndex];
                DispatchEntry[] entries = bucket.entries;
                int entryCount = bucket.entryCount;
                if (entryCount == 0)
                {
                    continue;
                }

                int priority = bucket.priority;
                for (int entryIndex = 0; entryIndex < entryCount; ++entryIndex)
                {
                    entries[entryIndex]
                        .handler.PrefreezeBroadcastPostProcessorsForEmission<TMessage>(
                            source,
                            priority,
                            _emissionId,
                            this
                        );
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void InvokeBroadcastWithoutSourceEntry<TMessage>(
            ref InstanceId source,
            ref TMessage message,
            int priority,
            DispatchEntry entry
        )
            where TMessage : IBroadcastMessage
        {
            MessageHandler handler = entry.handler;
            if (entry.prefreeze.kind == PrefreezeKindBroadcastWithoutSourceHandlers)
            {
                handler.PrefreezeBroadcastWithoutSourceHandlersForEmission<TMessage>(
                    entry.prefreeze.priority,
                    _emissionId,
                    this
                );
            }

            if (!handler.active)
            {
                return;
            }

            MessageHandler.BroadcastWithoutSourceDispatchLink<TMessage> link =
                Unsafe.As<MessageHandler.BroadcastWithoutSourceDispatchLink<TMessage>>(
                    entry.dispatch
                );
            link.Invoke(handler, ref source, ref message, priority, _emissionId);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void InvokeBroadcastWithoutSourcePostEntry<TMessage>(
            ref InstanceId source,
            ref TMessage message,
            int priority,
            DispatchEntry entry
        )
            where TMessage : IBroadcastMessage
        {
            MessageHandler handler = entry.handler;
            if (!handler.active)
            {
                return;
            }

            MessageHandler.BroadcastWithoutSourcePostDispatchLink<TMessage> link =
                Unsafe.As<MessageHandler.BroadcastWithoutSourcePostDispatchLink<TMessage>>(
                    entry.dispatch
                );
            link.Invoke(handler, ref source, ref message, priority, _emissionId);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void PrefreezeBroadcastWithoutSourcePostSnapshot<TMessage>(
            DispatchSnapshot snapshot
        )
            where TMessage : IBroadcastMessage
        {
            if (snapshot.IsEmpty)
            {
                return;
            }

            DispatchBucket[] buckets = snapshot.buckets;
            int bucketCount = snapshot.bucketCount;
            for (int bucketIndex = 0; bucketIndex < bucketCount; ++bucketIndex)
            {
                DispatchBucket bucket = buckets[bucketIndex];
                DispatchEntry[] entries = bucket.entries;
                int entryCount = bucket.entryCount;
                if (entryCount == 0)
                {
                    continue;
                }

                int priority = bucket.priority;
                for (int entryIndex = 0; entryIndex < entryCount; ++entryIndex)
                {
                    entries[entryIndex]
                        .handler.PrefreezeBroadcastWithoutSourcePostProcessorsForEmission<TMessage>(
                            priority,
                            _emissionId,
                            this
                        );
                }
            }
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

#if UNITY_2021_3_OR_NEWER
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
