namespace DxMessaging.Core
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using Helper;
    using MessageBus;
    using Messages;

    /// <summary>
    /// Per-owner handler that executes registered message callbacks.
    /// </summary>
    /// <remarks>
    /// A <see cref="MessageHandler"/> is typically created and managed by <see cref="Unity.MessagingComponent"/> in Unity.
    /// Most user code interacts with the handler through <see cref="MessageRegistrationToken"/>, which stages
    /// registrations and ensures correct enable/disable lifecycles.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Plain .NET usage without Unity
    /// var owner = new DxMessaging.Core.InstanceId(1);
    /// var handler = new DxMessaging.Core.MessageHandler(owner) { active = true };
    /// var token = DxMessaging.Core.MessageRegistrationToken.Create(handler);
    /// _ = token.RegisterUntargeted&lt;WorldRegenerated&gt;((ref WorldRegenerated m) =&gt; Console.WriteLine(m.seed));
    /// token.Enable();
    ///
    /// var bus = DxMessaging.Core.MessageHandler.MessageBus;
    /// var msg = new WorldRegenerated(42);
    /// bus.UntargetedBroadcast(ref msg);
    /// </code>
    /// </example>
    public sealed class MessageHandler
        : IEquatable<MessageHandler>,
            IComparable,
            IComparable<MessageHandler>
    {
        /// <summary>
        /// Pre-freezes this handler's broadcast post-processor caches for the given message type, source, and priority
        /// for the specified emission id, so registrations during the same emission are not observed.
        /// </summary>
        /// <typeparam name="T">Broadcast message type.</typeparam>
        /// <param name="source">Source instance id.</param>
        /// <param name="priority">Priority bucket to freeze.</param>
        /// <param name="emissionId">Current emission id.</param>
        /// <param name="messageBus">Bus whose typed handler mapping to use.</param>
        internal void PrefreezeBroadcastPostProcessorsForEmission<T>(
            InstanceId source,
            int priority,
            long emissionId,
            IMessageBus messageBus
        )
            where T : IBroadcastMessage
        {
            if (!GetHandlerForType(messageBus, out TypedHandler<T> handler))
            {
                return;
            }

            if (
                handler._broadcastPostProcessingFastHandlers != null
                && handler._broadcastPostProcessingFastHandlers.TryGetValue(
                    source,
                    out Dictionary<int, HandlerActionCache<FastHandler<T>>> fastByPriority
                )
                && fastByPriority.TryGetValue(
                    priority,
                    out HandlerActionCache<FastHandler<T>> fastCache
                )
            )
            {
                _ = TypedHandler<T>.GetOrAddNewHandlerStack(fastCache, emissionId);
            }

            if (
                handler._broadcastPostProcessingHandlers != null
                && handler._broadcastPostProcessingHandlers.TryGetValue(
                    source,
                    out Dictionary<int, HandlerActionCache<Action<T>>> byPriority
                )
                && byPriority.TryGetValue(priority, out HandlerActionCache<Action<T>> cache)
            )
            {
                _ = TypedHandler<T>.GetOrAddNewHandlerStack(cache, emissionId);
            }
        }

        /// <summary>
        /// Pre-freezes this handler's targeted post-processor caches for the given message type, target, and priority
        /// for the specified emission id, so registrations during the same emission are not observed.
        /// </summary>
        /// <typeparam name="T">Targeted message type.</typeparam>
        /// <param name="target">Target instance id.</param>
        /// <param name="priority">Priority bucket to freeze.</param>
        /// <param name="emissionId">Current emission id.</param>
        /// <param name="messageBus">Bus whose typed handler mapping to use.</param>
        internal void PrefreezeTargetedPostProcessorsForEmission<T>(
            InstanceId target,
            int priority,
            long emissionId,
            IMessageBus messageBus
        )
            where T : ITargetedMessage
        {
            if (!GetHandlerForType(messageBus, out TypedHandler<T> handler))
            {
                return;
            }

            if (
                handler._targetedPostProcessingFastHandlers != null
                && handler._targetedPostProcessingFastHandlers.TryGetValue(
                    target,
                    out Dictionary<int, HandlerActionCache<FastHandler<T>>> fastByPriority
                )
                && fastByPriority.TryGetValue(
                    priority,
                    out HandlerActionCache<FastHandler<T>> fastCache
                )
            )
            {
                _ = TypedHandler<T>.GetOrAddNewHandlerStack(fastCache, emissionId);
            }

            if (
                handler._targetedPostProcessingHandlers != null
                && handler._targetedPostProcessingHandlers.TryGetValue(
                    target,
                    out Dictionary<int, HandlerActionCache<Action<T>>> byPriority
                )
                && byPriority.TryGetValue(priority, out HandlerActionCache<Action<T>> cache)
            )
            {
                _ = TypedHandler<T>.GetOrAddNewHandlerStack(cache, emissionId);
            }
        }

        /// <summary>
        /// Pre-freezes this handler's targeted-without-targeting handler caches for the given message type and priority
        /// so that removals/additions during the same emission are not observed.
        /// </summary>
        internal void PrefreezeTargetedWithoutTargetingHandlersForEmission<T>(
            int priority,
            long emissionId,
            IMessageBus messageBus
        )
            where T : ITargetedMessage
        {
            if (!GetHandlerForType(messageBus, out TypedHandler<T> handler))
            {
                return;
            }

            if (
                handler._fastTargetedWithoutTargetingHandlers != null
                && handler._fastTargetedWithoutTargetingHandlers.TryGetValue(
                    priority,
                    out HandlerActionCache<FastHandlerWithContext<T>> fastCache
                )
            )
            {
                _ = TypedHandler<T>.GetOrAddNewHandlerStack(fastCache, emissionId);
            }

            if (
                handler._targetedWithoutTargetingHandlers != null
                && handler._targetedWithoutTargetingHandlers.TryGetValue(
                    priority,
                    out HandlerActionCache<Action<InstanceId, T>> cache
                )
            )
            {
                _ = TypedHandler<T>.GetOrAddNewHandlerStack(cache, emissionId);
            }
        }

        /// <summary>
        /// Pre-freezes this handler's targeted-without-targeting post-processor caches for a given priority.
        /// </summary>
        internal void PrefreezeTargetedWithoutTargetingPostProcessorsForEmission<T>(
            int priority,
            long emissionId,
            IMessageBus messageBus
        )
            where T : ITargetedMessage
        {
            if (!GetHandlerForType(messageBus, out TypedHandler<T> handler))
            {
                return;
            }

            if (
                handler._fastTargetedWithoutTargetingPostProcessingHandlers != null
                && handler._fastTargetedWithoutTargetingPostProcessingHandlers.TryGetValue(
                    priority,
                    out HandlerActionCache<FastHandlerWithContext<T>> fastCache
                )
            )
            {
                _ = TypedHandler<T>.GetOrAddNewHandlerStack(fastCache, emissionId);
            }

            if (
                handler._targetedWithoutTargetingPostProcessingHandlers != null
                && handler._targetedWithoutTargetingPostProcessingHandlers.TryGetValue(
                    priority,
                    out HandlerActionCache<Action<InstanceId, T>> cache
                )
            )
            {
                _ = TypedHandler<T>.GetOrAddNewHandlerStack(cache, emissionId);
            }
        }

        /// <summary>
        /// Pre-freezes this handler's untargeted post-processor caches for a given priority.
        /// </summary>
        internal void PrefreezeUntargetedPostProcessorsForEmission<T>(
            int priority,
            long emissionId,
            IMessageBus messageBus
        )
            where T : IUntargetedMessage
        {
            if (!GetHandlerForType(messageBus, out TypedHandler<T> handler))
            {
                return;
            }

            if (
                handler._untargetedPostProcessingFastHandlers != null
                && handler._untargetedPostProcessingFastHandlers.TryGetValue(
                    priority,
                    out HandlerActionCache<FastHandler<T>> fastCache
                )
            )
            {
                _ = TypedHandler<T>.GetOrAddNewHandlerStack(fastCache, emissionId);
            }

            if (
                handler._untargetedPostProcessingHandlers != null
                && handler._untargetedPostProcessingHandlers.TryGetValue(
                    priority,
                    out HandlerActionCache<Action<T>> cache
                )
            )
            {
                _ = TypedHandler<T>.GetOrAddNewHandlerStack(cache, emissionId);
            }
        }

        /// <summary>
        /// Pre-freezes this handler's broadcast-without-source post-processor caches for a given priority.
        /// </summary>
        internal void PrefreezeBroadcastWithoutSourcePostProcessorsForEmission<T>(
            int priority,
            long emissionId,
            IMessageBus messageBus
        )
            where T : IBroadcastMessage
        {
            if (!GetHandlerForType(messageBus, out TypedHandler<T> handler))
            {
                return;
            }

            if (
                handler._fastBroadcastWithoutSourcePostProcessingHandlers != null
                && handler._fastBroadcastWithoutSourcePostProcessingHandlers.TryGetValue(
                    priority,
                    out HandlerActionCache<FastHandlerWithContext<T>> fastCache
                )
            )
            {
                _ = TypedHandler<T>.GetOrAddNewHandlerStack(fastCache, emissionId);
            }

            if (
                handler._broadcastWithoutSourcePostProcessingHandlers != null
                && handler._broadcastWithoutSourcePostProcessingHandlers.TryGetValue(
                    priority,
                    out HandlerActionCache<Action<InstanceId, T>> cache
                )
            )
            {
                _ = TypedHandler<T>.GetOrAddNewHandlerStack(cache, emissionId);
            }
        }

        /// <summary>
        /// Pre-freezes this handler's broadcast-without-source handler caches for the given message type and priority
        /// for the specified emission id, so removals during the same emission are not observed.
        /// </summary>
        /// <typeparam name="T">Broadcast message type.</typeparam>
        /// <param name="priority">Priority bucket to freeze.</param>
        /// <param name="emissionId">Current emission id.</param>
        /// <param name="messageBus">Bus whose typed handler mapping to use.</param>
        internal void PrefreezeBroadcastWithoutSourceHandlersForEmission<T>(
            int priority,
            long emissionId,
            IMessageBus messageBus
        )
            where T : IBroadcastMessage
        {
            if (!GetHandlerForType(messageBus, out TypedHandler<T> handler))
            {
                return;
            }

            if (
                handler._fastBroadcastWithoutSourceHandlers != null
                && handler._fastBroadcastWithoutSourceHandlers.TryGetValue(
                    priority,
                    out HandlerActionCache<FastHandlerWithContext<T>> fastCache
                )
            )
            {
                _ = TypedHandler<T>.GetOrAddNewHandlerStack(fastCache, emissionId);
            }

            if (
                handler._broadcastWithoutSourceHandlers != null
                && handler._broadcastWithoutSourceHandlers.TryGetValue(
                    priority,
                    out HandlerActionCache<Action<InstanceId, T>> cache
                )
            )
            {
                _ = TypedHandler<T>.GetOrAddNewHandlerStack(cache, emissionId);
            }
        }

        /// <summary>
        /// High-performance handler that receives the message by reference (no boxing/copies).
        /// </summary>
        public delegate void FastHandler<TMessage>(ref TMessage message)
            where TMessage : IMessage;

        /// <summary>
        /// High-performance handler with an additional context value (e.g., target/source) by reference.
        /// </summary>
        public delegate void FastHandlerWithContext<TMessage>(
            ref InstanceId context,
            ref TMessage message
        )
            where TMessage : IMessage;

        private static readonly object GlobalResetLock = new object();

        /// <summary>
        /// Global message bus used when no explicit bus is provided.
        /// </summary>
        private static IMessageBus _globalMessageBus;

        private static MessageBus.MessageBus _defaultGlobalMessageBus = new MessageBus.MessageBus();

        /// <summary>
        /// Gets the process-wide <see cref="IMessageBus"/> used when no explicit bus is supplied.
        /// </summary>
        /// <remarks>
        /// This mirrors the legacy singleton so existing code continues to function. Use
        /// <see cref="SetGlobalMessageBus(MessageBus.MessageBus)"/> to replace the instance (for example from a DI container) and
        /// <see cref="ResetGlobalMessageBus"/> to restore the stock configuration afterwards.
        /// </remarks>
        public static IMessageBus MessageBus => _globalMessageBus;

        /// <summary>
        /// Gets the baseline global <see cref="IMessageBus"/> instance used when no custom bus is configured.
        /// </summary>
        /// <remarks>
        /// The instance is recreated when <see cref="DxMessagingStaticState.Reset"/> runs so that domain-reload-disabled
        /// environments can obtain a clean slate.
        /// </remarks>
        public static IMessageBus InitialGlobalMessageBus => _defaultGlobalMessageBus;

        static MessageHandler()
        {
            ResetStatics();
        }

        /// <summary>
        /// Replaces the global <see cref="MessageBus.MessageBus"/> instance returned by <see cref="MessageBus"/>.
        /// </summary>
        /// <param name="messageBus">Instance to expose globally.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="messageBus"/> is <see langword="null"/>.
        /// </exception>
        /// <remarks>
        /// This is primarily intended for integration tests or dependency injection bootstrap code. Invoke
        /// <see cref="ResetGlobalMessageBus"/> when the customisation is no longer required.
        /// </remarks>
        public static void SetGlobalMessageBus(MessageBus.MessageBus messageBus)
        {
            if (messageBus == null)
            {
                throw new ArgumentNullException(nameof(messageBus));
            }

            _globalMessageBus = messageBus;
        }

        /// <summary>
        /// Replaces the global message bus with an arbitrary <see cref="IMessageBus"/> implementation.
        /// </summary>
        /// <param name="messageBus">Instance to expose globally.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="messageBus"/> is <see langword="null"/>.
        /// </exception>
        public static void SetGlobalMessageBus(IMessageBus messageBus)
        {
            if (messageBus == null)
            {
                throw new ArgumentNullException(nameof(messageBus));
            }

            _globalMessageBus = messageBus;
        }

        /// <summary>
        /// Restores the global <see cref="MessageBus.MessageBus"/> to the built-in default instance.
        /// </summary>
        /// <remarks>
        /// The default instance is recreated by <see cref="ResetStatics"/> when the static state reset utility runs.
        /// </remarks>
        public static void ResetGlobalMessageBus()
        {
            lock (GlobalResetLock)
            {
                _globalMessageBus = _defaultGlobalMessageBus;
            }
        }

        /// <summary>
        /// Temporarily overrides the global message bus until the returned scope is disposed.
        /// </summary>
        /// <param name="messageBus">Message bus to expose for the duration of the scope.</param>
        /// <returns>An <see cref="IDisposable"/> scope that restores the previous bus on dispose.</returns>
        public static GlobalMessageBusScope OverrideGlobalMessageBus(IMessageBus messageBus)
        {
            return new GlobalMessageBusScope(messageBus);
        }

        /// <summary>
        /// Recreates the built-in global <see cref="MessageBus.MessageBus"/> and assigns it as the active global bus.
        /// </summary>
        /// <remarks>
        /// Invoked by <see cref="DxMessagingStaticState.Reset"/> to provide a clean slate when domain reloads are disabled.
        /// </remarks>
        internal static void ResetStatics()
        {
            lock (GlobalResetLock)
            {
                _defaultGlobalMessageBus.ResetState();
                _globalMessageBus = _defaultGlobalMessageBus;
            }
        }

        /// <summary>
        /// Represents a disposable override scope for the global message bus.
        /// </summary>
        public struct GlobalMessageBusScope : IDisposable
        {
            private readonly IMessageBus _previous;
            private bool _disposed;

            internal GlobalMessageBusScope(IMessageBus messageBus)
            {
                if (messageBus == null)
                {
                    throw new ArgumentNullException(nameof(messageBus));
                }

                _previous = MessageBus;
                _disposed = false;

                if (messageBus is MessageBus.MessageBus concrete)
                {
                    SetGlobalMessageBus(concrete);
                }
                else
                {
                    SetGlobalMessageBus(messageBus);
                }
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                if (_previous is MessageBus.MessageBus concrete)
                {
                    SetGlobalMessageBus(concrete);
                }
                else if (_previous != null)
                {
                    SetGlobalMessageBus(_previous);
                }
                else
                {
                    ResetGlobalMessageBus();
                }

                _disposed = true;
            }
        }

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
        internal readonly List<MessageCache<object>> _handlersByTypeByMessageBus;
        private IMessageBus _defaultMessageBus;

        /// <summary>
        /// Gets the <see cref="IMessageBus"/> that will be used when a registration does not specify one explicitly.
        /// </summary>
        /// <remarks>
        /// When no override has been provided via <see cref="SetDefaultMessageBus"/>, this value defers to the global
        /// <see cref="MessageBus"/> singleton.
        /// </remarks>
        public IMessageBus DefaultMessageBus => _defaultMessageBus ?? MessageBus;

        public MessageHandler(InstanceId owner, IMessageBus defaultMessageBus = null)
        {
            this.owner = owner;
            _handlersByTypeByMessageBus = new List<MessageCache<object>>();
            _defaultMessageBus = defaultMessageBus;
        }

        /// <summary>
        /// Assigns an <see cref="IMessageBus"/> for registrations that omit an explicit bus parameter.
        /// </summary>
        /// <param name="messageBus">
        /// Bus to use; pass <see langword="null"/> to revert to the global <see cref="MessageBus"/> singleton.
        /// </param>
        /// <remarks>
        /// This allows a handler to participate in dependency injection scenarios without forcing every caller to supply
        /// a bus manually.
        /// </remarks>
        public void SetDefaultMessageBus(IMessageBus messageBus)
        {
            _defaultMessageBus = messageBus;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private IMessageBus ResolveMessageBus(IMessageBus messageBus)
        {
            return messageBus ?? _defaultMessageBus ?? MessageBus;
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
                long emissionId = messageBus.EmissionId;
                handler.HandleUntargeted(ref message, priority, emissionId);
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
                long emissionId = messageBus.EmissionId;
                handler.HandleUntargetedPostProcessing(ref message, priority, emissionId);
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
                long emissionId = messageBus.EmissionId;
                handler.HandleTargeted(ref target, ref message, priority, emissionId);
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
                long emissionId = messageBus.EmissionId;
                handler.HandleTargetedWithoutTargeting(
                    ref target,
                    ref message,
                    priority,
                    emissionId
                );
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
                long emissionId = messageBus.EmissionId;
                handler.HandleTargetedPostProcessing(ref target, ref message, priority, emissionId);
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
                long emissionId = messageBus.EmissionId;
                handler.HandleTargetedWithoutTargetingPostProcessing(
                    ref target,
                    ref message,
                    priority,
                    emissionId
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
                long emissionId = messageBus.EmissionId;
                handler.HandleSourcedBroadcast(ref source, ref message, priority, emissionId);
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
                long emissionId = messageBus.EmissionId;
                handler.HandleSourcedBroadcastWithoutSource(
                    ref source,
                    ref message,
                    priority,
                    emissionId
                );
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
                long emissionId = messageBus.EmissionId;
                handler.HandleSourcedBroadcastPostProcessing(
                    ref source,
                    ref message,
                    priority,
                    emissionId
                );
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
                long emissionId = messageBus.EmissionId;
                handler.HandleBroadcastWithoutSourcePostProcessing(
                    ref source,
                    ref message,
                    priority,
                    emissionId
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
                long emissionId = messageBus.EmissionId;
                handler.HandleGlobalUntargeted(ref message, emissionId);
            }
        }

        /// <summary>
        /// Pre-freezes this handler's GlobalAcceptAll untargeted caches for this emission.
        /// </summary>
        internal void PrefreezeGlobalUntargetedForEmission(long emissionId, IMessageBus messageBus)
        {
            if (!GetHandlerForType(messageBus, out TypedHandler<IMessage> handler))
            {
                return;
            }

            if (handler._globalUntargetedFastHandlers != null)
            {
                _ = TypedHandler<IMessage>.GetOrAddNewHandlerStack(
                    handler._globalUntargetedFastHandlers,
                    emissionId
                );
            }
            if (handler._globalUntargetedHandlers != null)
            {
                _ = TypedHandler<IMessage>.GetOrAddNewHandlerStack(
                    handler._globalUntargetedHandlers,
                    emissionId
                );
            }
        }

        /// <summary>
        /// Pre-freezes this handler's GlobalAcceptAll targeted caches for this emission.
        /// </summary>
        internal void PrefreezeGlobalTargetedForEmission(long emissionId, IMessageBus messageBus)
        {
            if (!GetHandlerForType(messageBus, out TypedHandler<IMessage> handler))
            {
                return;
            }

            if (handler._globalTargetedFastHandlers != null)
            {
                _ = TypedHandler<IMessage>.GetOrAddNewHandlerStack(
                    handler._globalTargetedFastHandlers,
                    emissionId
                );
            }
            if (handler._globalTargetedHandlers != null)
            {
                _ = TypedHandler<IMessage>.GetOrAddNewHandlerStack(
                    handler._globalTargetedHandlers,
                    emissionId
                );
            }
        }

        /// <summary>
        /// Pre-freezes this handler's GlobalAcceptAll broadcast caches for this emission.
        /// </summary>
        internal void PrefreezeGlobalBroadcastForEmission(long emissionId, IMessageBus messageBus)
        {
            if (!GetHandlerForType(messageBus, out TypedHandler<IMessage> handler))
            {
                return;
            }

            if (handler._globalBroadcastFastHandlers != null)
            {
                _ = TypedHandler<IMessage>.GetOrAddNewHandlerStack(
                    handler._globalBroadcastFastHandlers,
                    emissionId
                );
            }
            if (handler._globalBroadcastHandlers != null)
            {
                _ = TypedHandler<IMessage>.GetOrAddNewHandlerStack(
                    handler._globalBroadcastHandlers,
                    emissionId
                );
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
                long emissionId = messageBus.EmissionId;
                handler.HandleGlobalTargeted(ref target, ref message, emissionId);
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
                long emissionId = messageBus.EmissionId;
                handler.HandleGlobalBroadcast(ref source, ref message, emissionId);
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
            messageBus = ResolveMessageBus(messageBus);
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
            messageBus = ResolveMessageBus(messageBus);
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
            messageBus = ResolveMessageBus(messageBus);
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
                priority,
                messageBus.EmissionId
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
            messageBus = ResolveMessageBus(messageBus);
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
                priority,
                messageBus.EmissionId
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
            messageBus = ResolveMessageBus(messageBus);
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
                priority,
                messageBus.EmissionId
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
            messageBus = ResolveMessageBus(messageBus);
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
                priority,
                messageBus.EmissionId
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
            messageBus = ResolveMessageBus(messageBus);
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
                priority,
                messageBus.EmissionId
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
            messageBus = ResolveMessageBus(messageBus);
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
                priority,
                messageBus.EmissionId
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
            messageBus = ResolveMessageBus(messageBus);
            Action messageBusDeregistration = messageBus.RegisterTargetedWithoutTargeting<T>(
                this,
                priority: priority
            );
            TypedHandler<T> typedHandler = GetOrCreateHandlerForType<T>(messageBus);
            return typedHandler.AddTargetedWithoutTargetingHandler(
                originalHandler,
                messageHandler,
                messageBusDeregistration,
                priority,
                messageBus.EmissionId
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
            messageBus = ResolveMessageBus(messageBus);
            Action messageBusDeregistration = messageBus.RegisterTargetedWithoutTargeting<T>(
                this,
                priority: priority
            );
            TypedHandler<T> typedHandler = GetOrCreateHandlerForType<T>(messageBus);
            return typedHandler.AddTargetedWithoutTargetingHandler(
                originalHandler,
                messageHandler,
                messageBusDeregistration,
                priority,
                messageBus.EmissionId
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
            messageBus = ResolveMessageBus(messageBus);
            Action messageBusDeregistration = messageBus.RegisterUntargeted<T>(
                this,
                priority: priority
            );
            TypedHandler<T> typedHandler = GetOrCreateHandlerForType<T>(messageBus);
            return typedHandler.AddUntargetedHandler(
                originalHandler,
                messageHandler,
                messageBusDeregistration,
                priority,
                messageBus.EmissionId
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
            messageBus = ResolveMessageBus(messageBus);
            Action messageBusDeregistration = messageBus.RegisterUntargeted<T>(
                this,
                priority: priority
            );
            TypedHandler<T> typedHandler = GetOrCreateHandlerForType<T>(messageBus);
            return typedHandler.AddUntargetedHandler(
                originalHandler,
                messageHandler,
                messageBusDeregistration,
                priority,
                messageBus.EmissionId
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
            messageBus = ResolveMessageBus(messageBus);
            Action messageBusDeregistration = messageBus.RegisterUntargetedPostProcessor<T>(
                priority: priority,
                messageHandler: this
            );
            TypedHandler<T> typedHandler = GetOrCreateHandlerForType<T>(messageBus);
            return typedHandler.AddUntargetedPostProcessor(
                originalHandler,
                messageHandler,
                messageBusDeregistration,
                priority,
                messageBus.EmissionId
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
            messageBus = ResolveMessageBus(messageBus);
            Action messageBusDeregistration = messageBus.RegisterUntargetedPostProcessor<T>(
                priority: priority,
                messageHandler: this
            );
            TypedHandler<T> typedHandler = GetOrCreateHandlerForType<T>(messageBus);
            return typedHandler.AddUntargetedPostProcessor(
                originalHandler,
                messageHandler,
                messageBusDeregistration,
                priority,
                messageBus.EmissionId
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
            messageBus = ResolveMessageBus(messageBus);
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
                priority,
                messageBus.EmissionId
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
            messageBus = ResolveMessageBus(messageBus);
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
                priority,
                messageBus.EmissionId
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
            messageBus = ResolveMessageBus(messageBus);
            Action messageBusDeregistration = messageBus.RegisterSourcedBroadcastWithoutSource<T>(
                this,
                priority: priority
            );
            TypedHandler<T> typedHandler = GetOrCreateHandlerForType<T>(messageBus);
            return typedHandler.AddSourcedBroadcastWithoutSourceHandler(
                originalHandler,
                messageHandler,
                messageBusDeregistration,
                priority,
                messageBus.EmissionId
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
            messageBus = ResolveMessageBus(messageBus);
            Action messageBusDeregistration = messageBus.RegisterSourcedBroadcastWithoutSource<T>(
                this,
                priority: priority
            );
            TypedHandler<T> typedHandler = GetOrCreateHandlerForType<T>(messageBus);
            return typedHandler.AddSourcedBroadcastWithoutSourceHandler(
                originalHandler,
                messageHandler,
                messageBusDeregistration,
                priority,
                messageBus.EmissionId
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
            messageBus = ResolveMessageBus(messageBus);
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
                priority,
                messageBus.EmissionId
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
            messageBus = ResolveMessageBus(messageBus);
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
                priority,
                messageBus.EmissionId
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
            messageBus = ResolveMessageBus(messageBus);
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
                priority,
                messageBus.EmissionId
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
            messageBus = ResolveMessageBus(messageBus);
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
                priority,
                messageBus.EmissionId
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
            messageBus = ResolveMessageBus(messageBus);
            return messageBus.RegisterUntargetedInterceptor(interceptor, priority);
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
            messageBus = ResolveMessageBus(messageBus);
            return messageBus.RegisterBroadcastInterceptor(interceptor, priority);
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
            messageBus = ResolveMessageBus(messageBus);
            return messageBus.RegisterTargetedInterceptor(interceptor, priority);
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
                return Unsafe.As<TypedHandler<T>>(untypedHandler);
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
                existingTypedHandler = Unsafe.As<TypedHandler<T>>(untypedHandler);
                return true;
            }

            existingTypedHandler = default;
            return false;
        }

        internal int GetUntargetedPostProcessingPrefreezeCount<T>(
            IMessageBus messageBus,
            int priority
        )
            where T : IMessage
        {
            if (
                !GetHandlerForType(messageBus, out TypedHandler<T> handler)
                || handler._untargetedPostProcessingFastHandlers == null
            )
            {
                return 0;
            }

            if (
                handler._untargetedPostProcessingFastHandlers.TryGetValue(
                    priority,
                    out HandlerActionCache<FastHandler<T>> cache
                )
            )
            {
                return cache.prefreezeInvocationCount;
            }

            return 0;
        }

        private sealed class HandlerActionCache<T>
        {
            internal readonly struct Entry
            {
                public Entry(T handler, int count)
                {
                    this.handler = handler;
                    this.count = count;
                }

                public readonly T handler;
                public readonly int count;
            }

            public readonly Dictionary<T, Entry> entries = new();
            public readonly List<T> cache = new();
            public long version;
            public long lastSeenVersion = -1;
            public long lastSeenEmissionId;
            internal int prefreezeInvocationCount;
        }

        /// <summary>
        /// One-size-fits-all wrapper around all possible Messaging sinks for a particular MessageHandler & MessageType.
        /// </summary>
        /// <typeparam name="T">Message type that this Handler exists to serve.</typeparam>
        private sealed class TypedHandler<T>
            where T : IMessage
        {
            internal Dictionary<
                InstanceId,
                Dictionary<int, HandlerActionCache<Action<T>>>
            > _targetedHandlers;
            internal Dictionary<int, HandlerActionCache<Action<T>>> _untargetedHandlers;
            internal Dictionary<
                InstanceId,
                Dictionary<int, HandlerActionCache<Action<T>>>
            > _broadcastHandlers;
            internal Dictionary<
                InstanceId,
                Dictionary<int, HandlerActionCache<Action<T>>>
            > _targetedPostProcessingHandlers;
            internal Dictionary<
                int,
                HandlerActionCache<Action<T>>
            > _untargetedPostProcessingHandlers;
            internal Dictionary<
                InstanceId,
                Dictionary<int, HandlerActionCache<Action<T>>>
            > _broadcastPostProcessingHandlers;
            internal Dictionary<
                InstanceId,
                Dictionary<int, HandlerActionCache<FastHandler<T>>>
            > _targetedFastHandlers;
            internal Dictionary<int, HandlerActionCache<FastHandler<T>>> _untargetedFastHandlers;
            internal Dictionary<
                InstanceId,
                Dictionary<int, HandlerActionCache<FastHandler<T>>>
            > _broadcastFastHandlers;
            internal Dictionary<
                InstanceId,
                Dictionary<int, HandlerActionCache<FastHandler<T>>>
            > _targetedPostProcessingFastHandlers;
            internal Dictionary<
                int,
                HandlerActionCache<FastHandler<T>>
            > _untargetedPostProcessingFastHandlers;
            internal Dictionary<
                InstanceId,
                Dictionary<int, HandlerActionCache<FastHandler<T>>>
            > _broadcastPostProcessingFastHandlers;

            internal HandlerActionCache<Action<IUntargetedMessage>> _globalUntargetedHandlers;

            internal HandlerActionCache<
                Action<InstanceId, ITargetedMessage>
            > _globalTargetedHandlers;

            internal HandlerActionCache<
                Action<InstanceId, IBroadcastMessage>
            > _globalBroadcastHandlers;

            internal HandlerActionCache<
                FastHandler<IUntargetedMessage>
            > _globalUntargetedFastHandlers;

            internal HandlerActionCache<
                FastHandlerWithContext<ITargetedMessage>
            > _globalTargetedFastHandlers;

            internal HandlerActionCache<
                FastHandlerWithContext<IBroadcastMessage>
            > _globalBroadcastFastHandlers;
            internal Dictionary<
                int,
                HandlerActionCache<Action<InstanceId, T>>
            > _targetedWithoutTargetingHandlers;
            internal Dictionary<
                int,
                HandlerActionCache<FastHandlerWithContext<T>>
            > _fastTargetedWithoutTargetingHandlers;
            internal Dictionary<
                int,
                HandlerActionCache<Action<InstanceId, T>>
            > _broadcastWithoutSourceHandlers;
            internal Dictionary<
                int,
                HandlerActionCache<FastHandlerWithContext<T>>
            > _fastBroadcastWithoutSourceHandlers;
            internal Dictionary<
                int,
                HandlerActionCache<Action<InstanceId, T>>
            > _targetedWithoutTargetingPostProcessingHandlers;
            internal Dictionary<
                int,
                HandlerActionCache<FastHandlerWithContext<T>>
            > _fastTargetedWithoutTargetingPostProcessingHandlers;
            internal Dictionary<
                int,
                HandlerActionCache<Action<InstanceId, T>>
            > _broadcastWithoutSourcePostProcessingHandlers;
            internal Dictionary<
                int,
                HandlerActionCache<FastHandlerWithContext<T>>
            > _fastBroadcastWithoutSourcePostProcessingHandlers;

            /// <summary>
            /// Emits the UntargetedMessage to all subscribed listeners.
            /// </summary>
            /// <param name="message">Message to emit.</param>
            /// <param name="priority">Priority at which to run the handlers.</param>
            public void HandleUntargeted(ref T message, int priority, long emissionId)
            {
                PrefreezeHandlersForEmission(
                    _untargetedPostProcessingFastHandlers,
                    priority,
                    emissionId
                );
                PrefreezeHandlersForEmission(
                    _untargetedPostProcessingHandlers,
                    priority,
                    emissionId
                );

                RunFastHandlers(_untargetedFastHandlers, ref message, priority, emissionId);
                RunHandlers(_untargetedHandlers, ref message, priority, emissionId);
            }

            /// <summary>
            /// Emits the TargetedMessage to all subscribed listeners.
            /// </summary>
            /// <param name="target">Target the message is for.</param>
            /// <param name="message">Message to emit.</param>
            /// <param name="priority">Priority at which to run the handlers.</param>
            public void HandleTargeted(
                ref InstanceId target,
                ref T message,
                int priority,
                long emissionId
            )
            {
                RunFastHandlersWithContext(
                    ref target,
                    _targetedFastHandlers,
                    ref message,
                    priority,
                    emissionId
                );
                RunHandlersWithContext(
                    ref target,
                    _targetedHandlers,
                    ref message,
                    priority,
                    emissionId
                );
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
                int priority,
                long emissionId
            )
            {
                RunFastHandlers(
                    ref target,
                    _fastTargetedWithoutTargetingHandlers,
                    ref message,
                    priority,
                    emissionId
                );
                RunHandlers(
                    ref target,
                    _targetedWithoutTargetingHandlers,
                    ref message,
                    priority,
                    emissionId
                );
            }

            /// <summary>
            /// Emits the BroadcastMessage to all subscribed listeners.
            /// </summary>
            /// <param name="source">Source the message is from.</param>
            /// <param name="message">Message to emit.</param>
            /// <param name="priority">Priority at which to run the handlers.</param>
            public void HandleSourcedBroadcast(
                ref InstanceId source,
                ref T message,
                int priority,
                long emissionId
            )
            {
                RunFastHandlersWithContext(
                    ref source,
                    _broadcastFastHandlers,
                    ref message,
                    priority,
                    emissionId
                );
                RunHandlersWithContext(
                    ref source,
                    _broadcastHandlers,
                    ref message,
                    priority,
                    emissionId
                );
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
                int priority,
                long emissionId
            )
            {
                RunFastHandlers(
                    ref source,
                    _fastBroadcastWithoutSourceHandlers,
                    ref message,
                    priority,
                    emissionId
                );
                RunHandlers(
                    ref source,
                    _broadcastWithoutSourceHandlers,
                    ref message,
                    priority,
                    emissionId
                );
            }

            /// <summary>
            /// Emits the UntargetedMessage to all global listeners.
            /// </summary>
            /// <param name="message">Message to emit.</param>
            public void HandleGlobalUntargeted(ref IUntargetedMessage message, long emissionId)
            {
                RunFastHandlers(_globalUntargetedFastHandlers, ref message, emissionId);
                if (_globalUntargetedHandlers?.entries is not { Count: > 0 })
                {
                    return;
                }

                List<Action<IUntargetedMessage>> handlers = GetOrAddNewHandlerStack(
                    _globalUntargetedHandlers,
                    emissionId
                );
                int handlersCount = handlers.Count;
                for (int i = 0; i < handlersCount; ++i)
                {
                    handlers[i](message);
                }
            }

            /// <summary>
            /// Emits the TargetedMessage to all global listeners.
            /// </summary>
            /// <param name="target">Target that this message is intended for.</param>
            /// <param name="message">Message to emit.</param>
            public void HandleGlobalTargeted(
                ref InstanceId target,
                ref ITargetedMessage message,
                long emissionId
            )
            {
                RunFastHandlers(ref target, _globalTargetedFastHandlers, ref message, emissionId);

                if (_globalTargetedHandlers?.entries is not { Count: > 0 })
                {
                    return;
                }

                List<Action<InstanceId, ITargetedMessage>> handlers = GetOrAddNewHandlerStack(
                    _globalTargetedHandlers,
                    emissionId
                );
                int handlersCount = handlers.Count;
                for (int i = 0; i < handlersCount; ++i)
                {
                    handlers[i](target, message);
                }
            }

            /// <summary>
            /// Emits the BroadcastMessage to all global listeners.
            /// </summary>
            /// <param name="source">Source that this message is from.</param>
            /// <param name="message">Message to emit.</param>
            public void HandleGlobalBroadcast(
                ref InstanceId source,
                ref IBroadcastMessage message,
                long emissionId
            )
            {
                RunFastHandlers(ref source, _globalBroadcastFastHandlers, ref message, emissionId);

                if (_globalBroadcastHandlers?.entries is not { Count: > 0 })
                {
                    return;
                }

                List<Action<InstanceId, IBroadcastMessage>> handlers = GetOrAddNewHandlerStack(
                    _globalBroadcastHandlers,
                    emissionId
                );
                int handlersCount = handlers.Count;
                switch (handlersCount)
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

                for (int i = 0; i < handlersCount; ++i)
                {
                    handlers[i](source, message);
                }
            }

            public void HandleUntargetedPostProcessing(ref T message, int priority, long emissionId)
            {
                RunFastHandlers(
                    _untargetedPostProcessingFastHandlers,
                    ref message,
                    priority,
                    emissionId
                );
                RunHandlers(_untargetedPostProcessingHandlers, ref message, priority, emissionId);
            }

            public void HandleTargetedPostProcessing(
                ref InstanceId target,
                ref T message,
                int priority,
                long emissionId
            )
            {
                RunFastHandlersWithContext(
                    ref target,
                    _targetedPostProcessingFastHandlers,
                    ref message,
                    priority,
                    emissionId
                );
                RunHandlersWithContext(
                    ref target,
                    _targetedPostProcessingHandlers,
                    ref message,
                    priority,
                    emissionId
                );
            }

            public void HandleTargetedWithoutTargetingPostProcessing(
                ref InstanceId target,
                ref T message,
                int priority,
                long emissionId
            )
            {
                RunFastHandlersWithContext(
                    ref target,
                    _fastTargetedWithoutTargetingPostProcessingHandlers,
                    ref message,
                    priority,
                    emissionId
                );
                RunHandlers(
                    ref target,
                    _targetedWithoutTargetingPostProcessingHandlers,
                    ref message,
                    priority,
                    emissionId
                );
            }

            public void HandleSourcedBroadcastPostProcessing(
                ref InstanceId source,
                ref T message,
                int priority,
                long emissionId
            )
            {
                RunFastHandlersWithContext(
                    ref source,
                    _broadcastPostProcessingFastHandlers,
                    ref message,
                    priority,
                    emissionId
                );
                RunHandlersWithContext(
                    ref source,
                    _broadcastPostProcessingHandlers,
                    ref message,
                    priority,
                    emissionId
                );
            }

            public void HandleBroadcastWithoutSourcePostProcessing(
                ref InstanceId source,
                ref T message,
                int priority,
                long emissionId
            )
            {
                RunFastHandlersWithContext(
                    ref source,
                    _fastBroadcastWithoutSourcePostProcessingHandlers,
                    ref message,
                    priority,
                    emissionId
                );
                RunHandlers(
                    ref source,
                    _broadcastWithoutSourcePostProcessingHandlers,
                    ref message,
                    priority,
                    emissionId
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
                int priority,
                long emissionId
            )
            {
                return AddHandler(
                    target,
                    ref _targetedHandlers,
                    originalHandler,
                    handler,
                    deregistration,
                    priority,
                    emissionId
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
                int priority,
                long emissionId
            )
            {
                return AddHandler(
                    target,
                    ref _targetedFastHandlers,
                    originalHandler,
                    handler,
                    deregistration,
                    priority,
                    emissionId
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
                int priority,
                long emissionId
            )
            {
                return AddHandlerPreservingPriorityKey(
                    ref _targetedWithoutTargetingHandlers,
                    originalHandler,
                    handler,
                    deregistration,
                    priority,
                    emissionId
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
                int priority,
                long emissionId
            )
            {
                return AddHandlerPreservingPriorityKey(
                    ref _fastTargetedWithoutTargetingHandlers,
                    originalHandler,
                    handler,
                    deregistration,
                    priority,
                    emissionId
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
                int priority,
                long emissionId
            )
            {
                return AddHandler(
                    ref _untargetedHandlers,
                    originalHandler,
                    handler,
                    deregistration,
                    priority,
                    emissionId
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
                int priority,
                long emissionId
            )
            {
                return AddHandler(
                    ref _untargetedFastHandlers,
                    originalHandler,
                    handler,
                    deregistration,
                    priority,
                    emissionId
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
                int priority,
                long emissionId
            )
            {
                return AddHandler(
                    source,
                    ref _broadcastHandlers,
                    originalHandler,
                    handler,
                    deregistration,
                    priority,
                    emissionId
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
                int priority,
                long emissionId
            )
            {
                return AddHandler(
                    source,
                    ref _broadcastFastHandlers,
                    originalHandler,
                    handler,
                    deregistration,
                    priority,
                    emissionId
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
                int priority,
                long emissionId
            )
            {
                // Preserve the priority bucket during the current emission so frozen snapshots remain valid
                return AddHandlerPreservingPriorityKey(
                    ref _broadcastWithoutSourceHandlers,
                    originalHandler,
                    handler,
                    deregistration,
                    priority,
                    emissionId
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
                int priority,
                long emissionId
            )
            {
                // Preserve the priority bucket during the current emission so frozen snapshots remain valid
                return AddHandlerPreservingPriorityKey(
                    ref _fastBroadcastWithoutSourceHandlers,
                    originalHandler,
                    handler,
                    deregistration,
                    priority,
                    emissionId
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
                int priority,
                long emissionId
            )
            {
                return AddHandlerPreservingPriorityKey(
                    ref _untargetedPostProcessingHandlers,
                    originalHandler,
                    handler,
                    deregistration,
                    priority,
                    emissionId
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
                int priority,
                long emissionId
            )
            {
                return AddHandlerPreservingPriorityKey(
                    ref _untargetedPostProcessingFastHandlers,
                    originalHandler,
                    handler,
                    deregistration,
                    priority,
                    emissionId
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
                int priority,
                long emissionId
            )
            {
                return AddHandlerPreservingPriorityKey(
                    target,
                    ref _targetedPostProcessingHandlers,
                    originalHandler,
                    handler,
                    deregistration,
                    priority,
                    emissionId
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
                int priority,
                long emissionId
            )
            {
                return AddHandlerPreservingPriorityKey(
                    target,
                    ref _targetedPostProcessingFastHandlers,
                    originalHandler,
                    handler,
                    deregistration,
                    priority,
                    emissionId
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
                int priority,
                long emissionId
            )
            {
                return AddHandlerPreservingPriorityKey(
                    ref _targetedWithoutTargetingPostProcessingHandlers,
                    originalHandler,
                    handler,
                    deregistration,
                    priority,
                    emissionId
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
                int priority,
                long emissionId
            )
            {
                return AddHandlerPreservingPriorityKey(
                    ref _fastTargetedWithoutTargetingPostProcessingHandlers,
                    originalHandler,
                    handler,
                    deregistration,
                    priority,
                    emissionId
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
                int priority,
                long emissionId
            )
            {
                return AddHandlerPreservingPriorityKey(
                    source,
                    ref _broadcastPostProcessingHandlers,
                    originalHandler,
                    handler,
                    deregistration,
                    priority,
                    emissionId
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
                int priority,
                long emissionId
            )
            {
                return AddHandlerPreservingPriorityKey(
                    source,
                    ref _broadcastPostProcessingFastHandlers,
                    originalHandler,
                    handler,
                    deregistration,
                    priority,
                    emissionId
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
                int priority,
                long emissionId
            )
            {
                return AddHandlerPreservingPriorityKey(
                    ref _broadcastWithoutSourcePostProcessingHandlers,
                    originalHandler,
                    handler,
                    deregistration,
                    priority,
                    emissionId
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
                int priority,
                long emissionId
            )
            {
                return AddHandlerPreservingPriorityKey(
                    ref _fastBroadcastWithoutSourcePostProcessingHandlers,
                    originalHandler,
                    handler,
                    deregistration,
                    priority,
                    emissionId
                );
            }

            // Context-aware variant that preserves the priority key mapping on deregistration for the current emission.
            private static Action AddHandlerPreservingPriorityKey<TU>(
                InstanceId context,
                ref Dictionary<
                    InstanceId,
                    Dictionary<int, HandlerActionCache<TU>>
                > handlersByContext,
                TU originalHandler,
                TU augmentedHandler,
                Action deregistration,
                int priority,
                long emissionId
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

                if (
                    !cache.entries.TryGetValue(
                        originalHandler,
                        out HandlerActionCache<TU>.Entry entry
                    )
                )
                {
                    entry = new HandlerActionCache<TU>.Entry(augmentedHandler, 0);
                }

                bool firstRegistration = entry.count == 0;
                entry = firstRegistration
                    ? new HandlerActionCache<TU>.Entry(augmentedHandler, 1)
                    : new HandlerActionCache<TU>.Entry(entry.handler, entry.count + 1);

                cache.entries[originalHandler] = entry;
                cache.version++;

                Dictionary<
                    InstanceId,
                    Dictionary<int, HandlerActionCache<TU>>
                > localHandlersByContext = handlersByContext;

                return () =>
                {
                    if (!localHandlersByContext.TryGetValue(context, out sortedHandlers))
                    {
                        return;
                    }

                    if (
                        !sortedHandlers.TryGetValue(priority, out HandlerActionCache<TU> localCache)
                    )
                    {
                        return;
                    }

                    if (
                        !localCache.entries.TryGetValue(
                            originalHandler,
                            out HandlerActionCache<TU>.Entry localEntry
                        )
                    )
                    {
                        return;
                    }

                    localCache.version++;

                    deregistration?.Invoke();

                    if (localEntry.count <= 1)
                    {
                        _ = localCache.entries.Remove(originalHandler);
                        localCache.version++;
                        // Deliberately keep the priority and context mappings to preserve
                        // frozen snapshots for the current emission.
                        return;
                    }

                    localEntry = new HandlerActionCache<TU>.Entry(
                        localEntry.handler,
                        localEntry.count - 1
                    );

                    localCache.entries[originalHandler] = localEntry;
                };
            }

            private static void RunFastHandlersWithContext<TMessage>(
                ref InstanceId context,
                Dictionary<
                    int,
                    HandlerActionCache<FastHandlerWithContext<T>>
                > fastHandlersByContext,
                ref TMessage message,
                int priority,
                long emissionId
            )
                where TMessage : IMessage
            {
                if (fastHandlersByContext is not { Count: > 0 })
                {
                    return;
                }

                RunFastHandlers(
                    ref context,
                    fastHandlersByContext,
                    ref message,
                    priority,
                    emissionId
                );
            }

            private static void RunFastHandlersWithContext<TMessage>(
                ref InstanceId context,
                Dictionary<
                    InstanceId,
                    Dictionary<int, HandlerActionCache<FastHandler<T>>>
                > fastHandlersByContext,
                ref TMessage message,
                int priority,
                long emissionId
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

                RunFastHandlers(cache, ref message, priority, emissionId);
            }

            private static void RunFastHandlers<TMessage>(
                Dictionary<int, HandlerActionCache<FastHandler<T>>> fastHandlers,
                ref TMessage message,
                int priority,
                long emissionId
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
                List<FastHandler<T>> handlers = GetOrAddNewHandlerStack(cache, emissionId);
                int handlersCount = handlers.Count;
                switch (handlersCount)
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

                for (int i = 0; i < handlersCount; ++i)
                {
                    handlers[i](ref typedMessage);
                }
            }

            private static void RunFastHandlers<TMessage, TU>(
                HandlerActionCache<FastHandler<TU>> cache,
                ref TMessage message,
                long emissionId
            )
                where TMessage : IMessage
                where TU : IMessage
            {
                if (cache?.entries is not { Count: > 0 })
                {
                    return;
                }

                ref TU typedMessage = ref Unsafe.As<TMessage, TU>(ref message);
                List<FastHandler<TU>> handlers = GetOrAddNewHandlerStack(cache, emissionId);
                int handlersCount = handlers.Count;
                switch (handlersCount)
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

                for (int i = 0; i < handlersCount; ++i)
                {
                    handlers[i](ref typedMessage);
                }
            }

            private static void RunFastHandlers<TMessage, TU>(
                ref InstanceId context,
                HandlerActionCache<FastHandlerWithContext<TU>> cache,
                ref TMessage message,
                long emissionId
            )
                where TMessage : IMessage
                where TU : IMessage
            {
                if (cache?.entries is not { Count: > 0 })
                {
                    return;
                }

                ref TU typedMessage = ref Unsafe.As<TMessage, TU>(ref message);
                List<FastHandlerWithContext<TU>> handlers = GetOrAddNewHandlerStack(
                    cache,
                    emissionId
                );
                int handlersCount = handlers.Count;
                switch (handlersCount)
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

                for (int i = 0; i < handlersCount; ++i)
                {
                    handlers[i](ref context, ref typedMessage);
                }
            }

            private static void RunFastHandlers<TMessage, TU>(
                ref InstanceId context,
                Dictionary<int, HandlerActionCache<FastHandlerWithContext<TU>>> fastHandlers,
                ref TMessage message,
                int priority,
                long emissionId
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
                List<FastHandlerWithContext<TU>> handlers = GetOrAddNewHandlerStack(
                    cache,
                    emissionId
                );
                int handlersCount = handlers.Count;
                switch (handlersCount)
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

                for (int i = 0; i < handlersCount; ++i)
                {
                    handlers[i](ref context, ref typedMessage);
                }
            }

            private static void RunHandlersWithContext<TMessage>(
                ref InstanceId context,
                Dictionary<
                    InstanceId,
                    Dictionary<int, HandlerActionCache<Action<T>>>
                > handlersByContext,
                ref TMessage message,
                int priority,
                long emissionId
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

                RunHandlers(cache, ref message, priority, emissionId);
            }

            private static void RunHandlers<TMessage>(
                Dictionary<int, HandlerActionCache<Action<T>>> sortedHandlers,
                ref TMessage message,
                int priority,
                long emissionId
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

                List<Action<T>> handlers = GetOrAddNewHandlerStack(cache, emissionId);
                ref T typedMessage = ref Unsafe.As<TMessage, T>(ref message);
                int handlersCount = handlers.Count;
                switch (handlersCount)
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

                for (int i = 0; i < handlersCount; ++i)
                {
                    handlers[i](typedMessage);
                }
            }

            private static void RunHandlers<TMessage>(
                ref InstanceId context,
                Dictionary<int, HandlerActionCache<Action<InstanceId, T>>> handlers,
                ref TMessage message,
                int priority,
                long emissionId
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

                List<Action<InstanceId, T>> typedHandlers = GetOrAddNewHandlerStack(
                    cache,
                    emissionId
                );
                ref T typedMessage = ref Unsafe.As<TMessage, T>(ref message);
                int handlersCount = typedHandlers.Count;
                switch (handlersCount)
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

                for (int i = 0; i < handlersCount; ++i)
                {
                    typedHandlers[i](context, typedMessage);
                }
            }

            internal static List<TU> GetOrAddNewHandlerStack<TU>(
                HandlerActionCache<TU> actionCache,
                long emissionId
            )
            {
                if (actionCache.lastSeenEmissionId != emissionId)
                {
                    if (actionCache.version != actionCache.lastSeenVersion)
                    {
                        List<TU> list = actionCache.cache;
                        list.Clear();
                        foreach (
                            KeyValuePair<
                                TU,
                                HandlerActionCache<TU>.Entry
                            > kvp in actionCache.entries
                        )
                        {
                            list.Add(kvp.Value.handler);
                        }
                        actionCache.lastSeenVersion = actionCache.version;
                    }
                    actionCache.lastSeenEmissionId = emissionId;
                }
                return actionCache.cache;
            }

            private static void PrefreezeHandlersForEmission<THandler>(
                Dictionary<int, HandlerActionCache<THandler>> handlers,
                int priority,
                long emissionId
            )
            {
                if (
                    handlers != null
                    && handlers.TryGetValue(priority, out HandlerActionCache<THandler> cache)
                )
                {
                    cache.prefreezeInvocationCount++;
                    _ = GetOrAddNewHandlerStack(cache, emissionId);
                }
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
                int priority,
                long emissionId
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

                if (
                    !cache.entries.TryGetValue(
                        originalHandler,
                        out HandlerActionCache<TU>.Entry entry
                    )
                )
                {
                    entry = new HandlerActionCache<TU>.Entry(augmentedHandler, 0);
                }

                bool firstRegistration = entry.count == 0;
                entry = firstRegistration
                    ? new HandlerActionCache<TU>.Entry(augmentedHandler, 1)
                    : new HandlerActionCache<TU>.Entry(entry.handler, entry.count + 1);

                cache.entries[originalHandler] = entry;
                cache.version++;

                Dictionary<
                    InstanceId,
                    Dictionary<int, HandlerActionCache<TU>>
                > localHandlersByContext = handlersByContext;

                return () =>
                {
                    if (!localHandlersByContext.TryGetValue(context, out sortedHandlers))
                    {
                        return;
                    }

                    if (
                        !sortedHandlers.TryGetValue(priority, out HandlerActionCache<TU> localCache)
                    )
                    {
                        return;
                    }

                    if (
                        !localCache.entries.TryGetValue(
                            originalHandler,
                            out HandlerActionCache<TU>.Entry localEntry
                        )
                    )
                    {
                        return;
                    }

                    localCache.version++;

                    deregistration?.Invoke();

                    if (localEntry.count <= 1)
                    {
                        _ = localCache.entries.Remove(originalHandler);
                        localCache.version++;
                        if (localCache.entries.Count == 0)
                        {
                            _ = sortedHandlers.Remove(priority);
                            if (sortedHandlers.Count == 0)
                            {
                                localHandlersByContext.Remove(context);
                            }
                        }

                        return;
                    }

                    localEntry = new HandlerActionCache<TU>.Entry(
                        localEntry.handler,
                        localEntry.count - 1
                    );

                    localCache.entries[originalHandler] = localEntry;
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

                if (
                    !cache.entries.TryGetValue(
                        originalHandler,
                        out HandlerActionCache<TU>.Entry entry
                    )
                )
                {
                    entry = new HandlerActionCache<TU>.Entry(augmentedHandler, 0);
                }

                bool firstRegistration = entry.count == 0;
                entry = firstRegistration
                    ? new HandlerActionCache<TU>.Entry(augmentedHandler, 1)
                    : new HandlerActionCache<TU>.Entry(entry.handler, entry.count + 1);

                cache.entries[originalHandler] = entry;
                cache.version++;

                HandlerActionCache<TU> localCache = cache;

                return () =>
                {
                    if (
                        !localCache.entries.TryGetValue(
                            originalHandler,
                            out HandlerActionCache<TU>.Entry localEntry
                        )
                    )
                    {
                        return;
                    }

                    localCache.version++;

                    deregistration?.Invoke();

                    if (localEntry.count <= 1)
                    {
                        _ = localCache.entries.Remove(originalHandler);
                        localCache.version++;
                        return;
                    }

                    localEntry = new HandlerActionCache<TU>.Entry(
                        localEntry.handler,
                        localEntry.count - 1
                    );
                    localCache.entries[originalHandler] = localEntry;
                };
            }

            private static Action AddHandler<TU>(
                ref Dictionary<int, HandlerActionCache<TU>> handlers,
                TU originalHandler,
                TU augmentedHandler,
                Action deregistration,
                int priority,
                long emissionId
            )
            {
                handlers ??= new Dictionary<int, HandlerActionCache<TU>>();

                if (!handlers.TryGetValue(priority, out HandlerActionCache<TU> cache))
                {
                    cache = new HandlerActionCache<TU>();
                    handlers[priority] = cache;
                }

                if (
                    !cache.entries.TryGetValue(
                        originalHandler,
                        out HandlerActionCache<TU>.Entry entry
                    )
                )
                {
                    entry = new HandlerActionCache<TU>.Entry(augmentedHandler, 0);
                }

                bool firstRegistration = entry.count == 0;
                entry = firstRegistration
                    ? new HandlerActionCache<TU>.Entry(augmentedHandler, 1)
                    : new HandlerActionCache<TU>.Entry(entry.handler, entry.count + 1);

                cache.entries[originalHandler] = entry;
                cache.version++;

                Dictionary<int, HandlerActionCache<TU>> localHandlers = handlers;

                return () =>
                {
                    if (!localHandlers.TryGetValue(priority, out HandlerActionCache<TU> localCache))
                    {
                        return;
                    }

                    if (
                        !localCache.entries.TryGetValue(
                            originalHandler,
                            out HandlerActionCache<TU>.Entry localEntry
                        )
                    )
                    {
                        return;
                    }

                    localCache.version++;

                    deregistration?.Invoke();

                    if (localEntry.count <= 1)
                    {
                        _ = localCache.entries.Remove(originalHandler);
                        localCache.version++;
                        if (localCache.entries.Count == 0)
                        {
                            _ = localHandlers.Remove(priority);
                        }

                        return;
                    }

                    localEntry = new HandlerActionCache<TU>.Entry(
                        localEntry.handler,
                        localEntry.count - 1
                    );

                    localCache.entries[originalHandler] = localEntry;
                };
            }

            // Variant of AddHandler that preserves the priority key in the dictionary when the last entry is removed.
            // This ensures that during an in-flight emission (where handler stacks are already frozen),
            // subsequent removals do not cause lookups to fail for the current pass.
            private static Action AddHandlerPreservingPriorityKey<TU>(
                ref Dictionary<int, HandlerActionCache<TU>> handlers,
                TU originalHandler,
                TU augmentedHandler,
                Action deregistration,
                int priority,
                long emissionId
            )
            {
                handlers ??= new Dictionary<int, HandlerActionCache<TU>>();

                if (!handlers.TryGetValue(priority, out HandlerActionCache<TU> cache))
                {
                    cache = new HandlerActionCache<TU>();
                    handlers[priority] = cache;
                }

                if (
                    !cache.entries.TryGetValue(
                        originalHandler,
                        out HandlerActionCache<TU>.Entry entry
                    )
                )
                {
                    entry = new HandlerActionCache<TU>.Entry(augmentedHandler, 0);
                }

                bool firstRegistration = entry.count == 0;
                entry = firstRegistration
                    ? new HandlerActionCache<TU>.Entry(augmentedHandler, 1)
                    : new HandlerActionCache<TU>.Entry(entry.handler, entry.count + 1);

                cache.entries[originalHandler] = entry;
                cache.version++;

                Dictionary<int, HandlerActionCache<TU>> localHandlers = handlers;

                return () =>
                {
                    if (!localHandlers.TryGetValue(priority, out HandlerActionCache<TU> localCache))
                    {
                        return;
                    }

                    if (
                        !localCache.entries.TryGetValue(
                            originalHandler,
                            out HandlerActionCache<TU>.Entry localEntry
                        )
                    )
                    {
                        return;
                    }

                    localCache.version++;

                    deregistration?.Invoke();

                    if (localEntry.count <= 1)
                    {
                        _ = localCache.entries.Remove(originalHandler);
                        localCache.version++;
                        // Intentionally DO NOT remove the priority key here to preserve
                        // the cache handle during an in-flight emission.
                        return;
                    }

                    localEntry = new HandlerActionCache<TU>.Entry(
                        localEntry.handler,
                        localEntry.count - 1
                    );

                    localCache.entries[originalHandler] = localEntry;
                };
            }
        }
    }
}
