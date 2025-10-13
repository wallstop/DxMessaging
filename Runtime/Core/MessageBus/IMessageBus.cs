namespace DxMessaging.Core.MessageBus
{
    using System;
    using System.Threading;
    using Core;
    using Messages;

    /// <summary>
    /// Contract for a general-purpose message bus handling registration and dispatch.
    /// </summary>
    /// <remarks>
    /// <para>Concepts:</para>
    /// <list type="bullet">
    /// <item><description><b>Handlers</b>: register to receive messages of specific types.</description></item>
    /// <item><description><b>Interceptors</b>: mutate or cancel messages before handlers run.</description></item>
    /// <item><description><b>Post-processors</b>: observe messages after handlers complete.</description></item>
    /// <item><description><b>Priority</b>: most registration APIs accept a <c>priority</c> parameter (lower runs first).</description></item>
    /// </list>
    /// <para>Message categories:</para>
    /// <list type="bullet">
    /// <item><description><see cref="Messages.IUntargetedMessage"/> — Global notifications.</description></item>
    /// <item><description><see cref="Messages.ITargetedMessage"/> — Directed at a specific <see cref="InstanceId"/>.</description></item>
    /// <item><description><see cref="Messages.IBroadcastMessage"/> — Emitted from a source for any listener.</description></item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Unity: register then send a targeted message
    /// var token = messagingComponent.Create(this);
    /// token.RegisterComponentTargeted&lt;TookDamage&gt;(this, (ref TookDamage m) =&gt; OnDamage(m));
    /// token.Enable();
    ///
    /// // Later, somewhere else
    /// DxMessaging.Core.InstanceId me = GetComponent&lt;UnityEngine.Component&gt;();
    /// var msg = new TookDamage(5);
    /// DxMessaging.Core.MessageHandler.MessageBus.TargetedBroadcast(ref me, ref msg);
    /// </code>
    /// </example>
    public interface IMessageBus
    {
        /// <summary>
        /// Default diagnostics mode for newly created buses and tokens.
        /// </summary>
        public static bool GlobalDiagnosticsMode { get; set; }

        long EmissionId { get; }

        /// <summary>
        /// Default ring buffer size for emission history when diagnostics are enabled.
        /// </summary>
        public static int GlobalMessageBufferSize { get; set; }

        internal static int GlobalSequentialIndex = -1;

        protected static int GenerateNewGlobalSequentialIndex() =>
            Interlocked.Increment(ref GlobalSequentialIndex);

        /// <summary>
        /// Whether diagnostics are recorded for this bus instance.
        /// </summary>
        public bool DiagnosticsMode { get; }

        public int RegisteredGlobalSequentialIndex { get; }
        public int RegisteredBroadcast { get; }

        public int RegisteredTargeted { get; }

        public int RegisteredUntargeted { get; }

        /// <summary>
        /// Interceptor delegate for untargeted messages to transform or cancel them.
        /// </summary>
        /// <typeparam name="TMessage">Specific type of message.</typeparam>
        /// <param name="message">Message to consider.</param>
        /// <returns>True if the message should be processed, false if it should be skipped.</returns>
        public delegate bool UntargetedInterceptor<TMessage>(ref TMessage message)
            where TMessage : IUntargetedMessage;

        /// <summary>
        /// Interceptor delegate for targeted messages to transform or cancel them.
        /// </summary>
        /// <typeparam name="TMessage">Specific type of message.</typeparam>
        /// <param name="target">Target of the message.</param>
        /// <param name="message">Message to consider.</param>
        /// <returns>True if the message should be processed, false if it should be skipped.</returns>
        public delegate bool TargetedInterceptor<TMessage>(
            ref InstanceId target,
            ref TMessage message
        )
            where TMessage : ITargetedMessage;

        /// <summary>
        /// Interceptor delegate for broadcast messages to transform or cancel them.
        /// </summary>
        /// <typeparam name="TMessage">Specific type of message.</typeparam>
        /// <param name="source">Source of the message.</param>
        /// <param name="message">Message to consider.</param>
        /// <returns>True if the message should be processed, false if it should be skipped.</returns>
        public delegate bool BroadcastInterceptor<TMessage>(
            ref InstanceId source,
            ref TMessage message
        )
            where TMessage : IBroadcastMessage;

        /// <summary>
        /// The registration log of all messaging registrations for this MessageBus.
        /// </summary>
        RegistrationLog Log { get; }

        /// <summary>
        /// Registers the specified MessageHandler to receive UntargetedMessages of the specified type.
        /// </summary>
        /// <typeparam name="T">Specific type of UntargetedMessages to register for.</typeparam>
        /// <param name="messageHandler">MessageHandler to register to accept UntargetedMessages of the specified type.</param>
        /// <param name="priority">Priority at which to run; lower runs earlier.</param>
        /// <returns>The deregistration action. Invoke when the handler no longer wants to receive messages.</returns>
        Action RegisterUntargeted<T>(MessageHandler messageHandler, int priority = 0)
            where T : IUntargetedMessage;

        /// <summary>
        /// Registers the specified MessageHandler to receive TargetedMessages of the specified type for the specified target.
        /// </summary>
        /// <typeparam name="T">Specific type of TargetedMessages to register for.</typeparam>
        /// <param name="target">Target of messages to listen for.</param>
        /// <param name="messageHandler">MessageHandler to register the TargetedMessages of the specified type.</param>
        /// <param name="priority">Priority at which to run; lower runs earlier.</param>
        /// <returns>The deregistration action. Invoke when the handler no longer wants to receive the messages.</returns>
        Action RegisterTargeted<T>(
            InstanceId target,
            MessageHandler messageHandler,
            int priority = 0
        )
            where T : ITargetedMessage;

        /// <summary>
        /// Registers the specified MessageHandler to receive TargetedMessages of the specified type.
        /// This registration IGNORES the targeting of the TargetedMessage.
        /// </summary>
        /// <typeparam name="T">Specific type of TargetedMessages to register for.</typeparam>
        /// <param name="messageHandler">MessageHandler to register to accept all TargetedMessages of the specified type.</param>
        /// <param name="priority">Priority at which to run; lower runs earlier.</param>
        /// <returns>The deregistration action. Invoke when the handler no longer wants to receive messages.</returns>
        Action RegisterTargetedWithoutTargeting<T>(MessageHandler messageHandler, int priority = 0)
            where T : ITargetedMessage;

        /// <summary>
        /// Registers the specified MessageHandler to receive BroadcastMessages of the specified type from the provided source.
        /// </summary>
        /// <typeparam name="T">Type of the BroadcastMessage to register.</typeparam>
        /// <param name="source">InstanceId of the source for BroadcastMessages to listen to.</param>
        /// <param name="messageHandler">MessageHandler to register to accept BroadcastMessages.</param>
        /// <returns>The deregistration action. Should be invoked when the handler no longer wants to receive messages.</returns>
        Action RegisterSourcedBroadcast<T>(
            InstanceId source,
            MessageHandler messageHandler,
            int priority = 0
        )
            where T : IBroadcastMessage;

        /// <summary>
        /// Registers the specified MessageHandler to receive BroadcastMessages of the specified type from ALL sources.
        /// This registration IGNORES the source of the BroadcastMessage.
        /// </summary>
        /// <typeparam name="T">Type of the BroadcastMessage to register.</typeparam>
        /// <param name="messageHandler">MessageHandler to register to accept BroadcastMessages.</param>
        /// <returns>The deregistration action. Should be invoked when the handler no longer wants to receive messages.</returns>
        Action RegisterSourcedBroadcastWithoutSource<T>(
            MessageHandler messageHandler,
            int priority = 0
        )
            where T : IBroadcastMessage;

        /// <summary>
        /// Registers the specified MessageHandler to receive ALL messages.
        /// It doesn't matter if the message is Targeted or Untargeted, this MessageHandler will be invoked for it.
        /// </summary>
        /// <param name="messageHandler">MessageHandler to register to accept all messages.</param>
        /// <returns>The deregistration action. Should be invoked when the handler no longer wants to receive messages.</returns>
        Action RegisterGlobalAcceptAll(MessageHandler messageHandler);

        /// <summary>
        /// Registers the specified MessageHandler and transformer function as an interceptor for Messages of type T.
        /// Whenever messages of that type are sent, interceptors will be ran in order by priority and then order of registration within
        /// that priority, transforming that message into new, mutated types.
        /// If any interceptor returns false, message handling is immediately stopped.
        /// The message at the end of the transformations will be then sent to registered message handlers.
        /// </summary>
        /// <note>
        /// Transformer function can return false to "cancel" the message being sent.
        /// </note>
        /// <typeparam name="T">Type of message to intercept.</typeparam>
        /// <param name="interceptor">Transformation function to run on messages of the chosen type.</param>
        /// <param name="priority">Priority of the interceptor to run at. Handlers run in order of priority from low -> high.</param>
        /// <note>
        ///     The transform function takes:
        ///         param1: Current message instance by reference
        ///     And returns: true if message handling should continue, false if message handling should be stopped.
        /// </note>
        /// <returns>The deregistration action. Should be invoked when the handler no longer wants to intercept messages.</returns>
        Action RegisterUntargetedInterceptor<T>(
            UntargetedInterceptor<T> interceptor,
            int priority = 0
        )
            where T : IUntargetedMessage;

        /// <summary>
        /// Registers the specified MessageHandler and transformer function as an interceptor for Messages of type T.
        /// Whenever messages of that type are sent, interceptors will be ran in order by priority and then order of registration within
        /// that priority, transforming that message into new, mutated types.
        /// If any interceptor returns false, message handling is immediately stopped.
        /// The message at the end of the transformations will be then sent to registered message handlers.
        /// </summary>
        /// <note>
        /// Transformer function can return false to "cancel" the message being sent.
        /// </note>
        /// <typeparam name="T">Type of message to intercept.</typeparam>
        /// <param name="interceptor">Transformation function to run on messages of the chosen type.</param>
        /// <param name="priority">Priority of the interceptor to run at. Handlers run in order of priority from low -> high.</param>
        /// <note>
        ///     The transform function takes:
        ///         param1: Current message instance by reference
        ///     And returns: true if message handling should continue, false if message handling should be stopped.
        /// </note>
        /// <returns>The deregistration action. Should be invoked when the handler no longer wants to intercept messages.</returns>
        Action RegisterTargetedInterceptor<T>(TargetedInterceptor<T> interceptor, int priority = 0)
            where T : ITargetedMessage;

        /// <summary>
        /// Registers the specified MessageHandler and transformer function as an interceptor for Messages of type T.
        /// Whenever messages of that type are sent, interceptors will be ran in order by priority and then order of registration within
        /// that priority, transforming that message into new, mutated types.
        /// If any interceptor returns false, message handling is immediately stopped.
        /// The message at the end of the transformations will be then sent to registered message handlers.
        /// </summary>
        /// <note>
        /// Transformer function can return false to "cancel" the message being sent.
        /// </note>
        /// <typeparam name="T">Type of message to intercept.</typeparam>
        /// <param name="interceptor">Transformation function to run on messages of the chosen type.</param>
        /// <param name="priority">Priority of the interceptor to run at. Handlers run in order of priority from low -> high.</param>
        /// <note>
        ///     The transform function takes:
        ///         param1: Current message instance by reference
        ///     And returns: true if message handling should continue, false if message handling should be stopped.
        /// </note>
        /// <returns>The deregistration action. Should be invoked when the handler no longer wants to intercept messages.</returns>
        Action RegisterBroadcastInterceptor<T>(
            BroadcastInterceptor<T> interceptor,
            int priority = 0
        )
            where T : IBroadcastMessage;

        /// <summary>
        /// Registers the provided MessageHandler to post-process Untargeted messages of the given type.
        /// (This will run after all handlers run for the provided message).
        /// </summary>
        /// <typeparam name="T">Type of UntargetedMessage to post-process.</typeparam>
        /// <param name="messageHandler">MessageHandler to post-process messages for.</param>
        /// <param name="priority">Priority of the interceptor to run at. Handlers run in order of priority from low -> high.</param>
        /// <returns>The deregistration action. Should be invoked when the handler no longer wants to post-process messages.</returns>
        Action RegisterUntargetedPostProcessor<T>(MessageHandler messageHandler, int priority = 0)
            where T : IUntargetedMessage;

        /// <summary>
        /// Registers the provided MessageHandler to post-process Targeted messages of the given type.
        /// (This will run after all handlers run for the provided message).
        /// </summary>
        /// <typeparam name="T">Type of TargetedMessage to post-process.</typeparam>
        /// <param name="target">Target of messages to listen for.</param>
        /// <param name="messageHandler">MessageHandler to post-process messages for.</param>
        /// <param name="priority">Priority of the interceptor to run at. Handlers run in order of priority from low -> high.</param>
        /// <returns>The deregistration action. Should be invoked when the handler no longer wants to post-process messages.</returns>
        Action RegisterTargetedPostProcessor<T>(
            InstanceId target,
            MessageHandler messageHandler,
            int priority = 0
        )
            where T : ITargetedMessage;

        /// <summary>
        /// Registers the provided MessageHandler to post-process Targeted messages of the given type for all targets.
        /// (This will run after all handlers run for the provided message).
        /// </summary>
        /// <typeparam name="T">Type of TargetedMessage to post-process.</typeparam>
        /// <param name="messageHandler">MessageHandler to post-process messages for.</param>
        /// <param name="priority">Priority of the interceptor to run at. Handlers run in order of priority from low -> high.</param>
        /// <returns>The deregistration action. Should be invoked when the handler no longer wants to post-process messages.</returns>
        Action RegisterTargetedWithoutTargetingPostProcessor<T>(
            MessageHandler messageHandler,
            int priority = 0
        )
            where T : ITargetedMessage;

        /// <summary>
        /// Registers the provided MessageHandler to post-process Targeted messages of the given type.
        /// (This will run after all handlers run for the provided message).
        /// </summary>
        /// <typeparam name="T">Type of TargetedMessage to post-process.</typeparam>
        /// <param name="source">Source of messages to listen for.</param>
        /// <param name="messageHandler">MessageHandler to post-process messages for.</param>
        /// <param name="priority">Priority of the interceptor to run at. Handlers run in order of priority from low -> high.</param>
        /// <returns>The deregistration action. Should be invoked when the handler no longer wants to post-process messages.</returns>
        Action RegisterBroadcastPostProcessor<T>(
            InstanceId source,
            MessageHandler messageHandler,
            int priority = 0
        )
            where T : IBroadcastMessage;

        /// <summary>
        /// Registers the provided MessageHandler to post-process Targeted messages of the given type for all sources.
        /// (This will run after all handlers run for the provided message).
        /// </summary>
        /// <typeparam name="T">Type of TargetedMessage to post-process.</typeparam>
        /// <param name="messageHandler">MessageHandler to post-process messages for.</param>
        /// <param name="priority">Priority of the interceptor to run at. Handlers run in order of priority from low -> high.</param>
        /// <returns>The deregistration action. Should be invoked when the handler no longer wants to post-process messages.</returns>
        Action RegisterBroadcastWithoutSourcePostProcessor<T>(
            MessageHandler messageHandler,
            int priority = 0
        )
            where T : IBroadcastMessage;

        /// <summary>
        /// Broadcasts an Untargeted message to all listeners registered to this bus.
        /// </summary>
        /// <param name="typedMessage">Message to broadcast.</param>
        void UntypedUntargetedBroadcast(IUntargetedMessage typedMessage);

        /// <summary>
        /// Broadcasts a fast Untargeted message to all listeners registered to this bus.
        /// </summary>
        /// <param name="typedMessage">Message to broadcast.</param>
        void UntargetedBroadcast<TMessage>(ref TMessage typedMessage)
            where TMessage : IUntargetedMessage;

        /// <summary>
        /// Broadcasts a TargetedMessage to all listeners registered to this bus.
        /// </summary>
        /// <param name="target">Target to send the message to.</param>
        /// <param name="typedMessage">Message to broadcast.</param>
        void UntypedTargetedBroadcast(InstanceId target, ITargetedMessage typedMessage);

        /// <summary>
        /// Broadcasts a fast TargetedMessage to all listeners registered to this bus.
        /// </summary>
        /// <param name="target">Target to send the message to.</param>
        /// <param name="typedMessage">Message to broadcast.</param>
        void TargetedBroadcast<TMessage>(ref InstanceId target, ref TMessage typedMessage)
            where TMessage : ITargetedMessage;

        /// <summary>
        /// Broadcasts a BroadcastMessage to all listeners registered to this bus.
        /// </summary>
        /// <param name="source">Source of the message.</param>
        /// <param name="typedMessage">Message to broadcast.</param>
        void UntypedSourcedBroadcast(InstanceId source, IBroadcastMessage typedMessage);

        /// <summary>
        /// Broadcasts a fast BroadcastMessage to all listeners registered to this bus.
        /// </summary>
        /// <param name="source">Source of the message.</param>
        /// <param name="typedMessage">Message to broadcast.</param>
        void SourcedBroadcast<TMessage>(ref InstanceId source, ref TMessage typedMessage)
            where TMessage : IBroadcastMessage;
    }
}
