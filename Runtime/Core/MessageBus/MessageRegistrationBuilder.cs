namespace DxMessaging.Core.MessageBus
{
    using System;
    using System.Threading;
    using DxMessaging.Core;

    /// <summary>
    /// Options controlling how <see cref="MessageRegistrationBuilder"/> constructs registration tokens.
    /// </summary>
    /// <remarks>
    /// These options let you override the message bus used by the generated token, opt into diagnostics,
    /// and register lifecycle callbacks that are triggered when the returned <see cref="MessageRegistrationLease"/> is
    /// activated or disposed.
    /// </remarks>
    /// <example>
    /// <code>
    /// var options = new MessageRegistrationBuildOptions
    /// {
    ///     ActivateOnBuild = true,
    ///     EnableDiagnostics = true,
    ///     Configure = token =>
    ///     {
    ///         _ = token.RegisterUntargeted&lt;SimpleUntargetedMessage&gt;(OnMessage);
    ///     },
    ///     Lifecycle = new MessageRegistrationLifecycle(
    ///         onBuild: token => Debug.Log("Configured"),
    ///         onActivate: token => Debug.Log("Enabled"),
    ///         onDeactivate: token => Debug.Log("Disabled"),
    ///         onDispose: token => Debug.Log("Disposed"))
    /// };
    ///
    /// using MessageRegistrationLease lease = registrationBuilder.Build(options);
    /// // token is activated automatically because ActivateOnBuild = true
    /// </code>
    /// </example>
    public sealed class MessageRegistrationBuildOptions
    {
        /// <summary>
        /// Explicit owner identifier for the underlying <see cref="MessageHandler"/>.
        /// When omitted, a synthetic identifier is generated automatically.
        /// </summary>
        public InstanceId? Owner { get; set; }

#if UNITY_2021_3_OR_NEWER
        /// <summary>
        /// Unity object to treat as the owner. Overrides <see cref="Owner"/> when supplied.
        /// </summary>
        public UnityEngine.Object UnityOwner { get; set; }
#endif

        /// <summary>
        /// Overrides the message bus used for registrations. Falls back to provider/global bus when null.
        /// </summary>
        public IMessageBus PreferredMessageBus { get; set; }

        /// <summary>
        /// Overrides the provider used to resolve a message bus when <see cref="PreferredMessageBus"/> is null.
        /// </summary>
        public IMessageBusProvider MessageBusProvider { get; set; }

        /// <summary>
        /// Indicates whether the underlying <see cref="MessageHandler"/> starts in an active state.
        /// </summary>
        public bool HandlerStartsActive { get; set; } = true;

        /// <summary>
        /// Indicates whether <see cref="MessageRegistrationToken.Enable"/> should be invoked immediately.
        /// </summary>
        public bool ActivateOnBuild { get; set; }

        /// <summary>
        /// Enables <see cref="MessageRegistrationToken.DiagnosticMode"/> on the created token.
        /// </summary>
        public bool EnableDiagnostics { get; set; }

        /// <summary>
        /// Optional hook invoked after the token has been constructed but before lifecycle callbacks.
        /// Use this to stage registrations.
        /// </summary>
        public Action<MessageRegistrationToken> Configure { get; set; }

        /// <summary>
        /// Lifecycle hooks that fire during build, activation, deactivation, and disposal.
        /// </summary>
        public MessageRegistrationLifecycle Lifecycle { get; set; }
    }

    /// <summary>
    /// Lifecycle callbacks for registration leases.
    /// </summary>
    /// <remarks>
    /// Callbacks run in the following order:
    /// <list type="number">
    /// <item><description><c>OnBuild</c> (invoked immediately after the lease is created, before activation).</description></item>
    /// <item><description><c>OnActivate</c> (invoked when <see cref="MessageRegistrationLease.Activate"/> runs or when <see cref="MessageRegistrationBuildOptions.ActivateOnBuild"/> is set).</description></item>
    /// <item><description><c>OnDeactivate</c> (invoked when <see cref="MessageRegistrationLease.Deactivate"/> runs or the lease is disposed while active).</description></item>
    /// <item><description><c>OnDispose</c> (invoked during <see cref="MessageRegistrationLease.Dispose"/>).</description></item>
    /// </list>
    /// </remarks>
    public readonly struct MessageRegistrationLifecycle
    {
        public MessageRegistrationLifecycle(
            Action<MessageRegistrationToken> onBuild,
            Action<MessageRegistrationToken> onActivate,
            Action<MessageRegistrationToken> onDeactivate,
            Action<MessageRegistrationToken> onDispose
        )
        {
            OnBuild = onBuild;
            OnActivate = onActivate;
            OnDeactivate = onDeactivate;
            OnDispose = onDispose;
        }

        public Action<MessageRegistrationToken> OnBuild { get; }

        public Action<MessageRegistrationToken> OnActivate { get; }

        public Action<MessageRegistrationToken> OnDeactivate { get; }

        public Action<MessageRegistrationToken> OnDispose { get; }
    }

    /// <summary>
    /// Represents the lifetime of a registration token constructed by the builder.
    /// </summary>
    /// <remarks>
    /// A lease grants direct access to the created <see cref="MessageRegistrationToken"/>, holds a reference to the
    /// underlying <see cref="MessageHandler"/>, and coordinates lifecycle callbacks supplied via
    /// <see cref="MessageRegistrationBuildOptions.Lifecycle"/>.
    /// </remarks>
    public sealed class MessageRegistrationLease : IDisposable
    {
        private readonly MessageRegistrationToken _token;
        private readonly MessageHandler _messageHandler;
        private readonly IMessageBus _messageBus;
        private readonly MessageRegistrationLifecycle _lifecycle;
        private bool _isActive;
        private bool _disposed;

        internal MessageRegistrationLease(
            MessageRegistrationToken token,
            MessageHandler messageHandler,
            IMessageBus messageBus,
            MessageRegistrationLifecycle lifecycle
        )
        {
            _token = token ?? throw new ArgumentNullException(nameof(token));
            _messageHandler =
                messageHandler ?? throw new ArgumentNullException(nameof(messageHandler));
            _messageBus = messageBus;
            _lifecycle = lifecycle;
            _isActive = false;
            _disposed = false;
        }

        /// <summary>
        /// Gets the registration token created for this lease.
        /// </summary>
        public MessageRegistrationToken Token => _token;

        /// <summary>
        /// Gets the handler hosting the lease's registrations.
        /// </summary>
        public MessageHandler Handler => _messageHandler;

        /// <summary>
        /// Gets the message bus that registrations created through this lease will target by default.
        /// </summary>
        public IMessageBus MessageBus => _messageBus;

        /// <summary>
        /// Gets the owner identifier associated with the underlying handler.
        /// </summary>
        public InstanceId Owner => _messageHandler.owner;

        /// <summary>
        /// Gets a value indicating whether the lease is currently active (token enabled).
        /// </summary>
        public bool IsActive => _isActive;

        /// <summary>
        /// Enables the token managed by this lease and raises the activation lifecycle callback if provided.
        /// </summary>
        /// <exception cref="ObjectDisposedException">Thrown if the lease has already been disposed.</exception>
        public void Activate()
        {
            EnsureNotDisposed();
            if (_isActive)
            {
                return;
            }

            _messageHandler.active = true;
            _token.Enable();
            if (_lifecycle.OnActivate != null)
            {
                _lifecycle.OnActivate(_token);
            }

            _isActive = true;
        }

        public void Deactivate()
        {
            if (_disposed || !_isActive)
            {
                return;
            }

            if (_lifecycle.OnDeactivate != null)
            {
                _lifecycle.OnDeactivate(_token);
            }

            _token.Disable();
            _messageHandler.active = false;
            _isActive = false;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Deactivate();
            if (_lifecycle.OnDispose != null)
            {
                _lifecycle.OnDispose(_token);
            }

            _disposed = true;
        }

        internal void InvokeBuildHook()
        {
            if (_lifecycle.OnBuild != null)
            {
                _lifecycle.OnBuild(_token);
            }
        }

        private void EnsureNotDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(MessageRegistrationLease));
            }
        }
    }

    /// <summary>
    /// Default implementation that composes message handlers, registration tokens, and lifecycle hooks.
    /// </summary>
    public sealed class MessageRegistrationBuilder : IMessageRegistrationBuilder
    {
        private readonly IMessageBusProvider _messageBusProvider;
        private static int _syntheticOwnerCounter;

        public MessageRegistrationBuilder()
        {
            _messageBusProvider = null;
        }

        public MessageRegistrationBuilder(IMessageBusProvider messageBusProvider)
        {
            _messageBusProvider = messageBusProvider;
        }

        /// <summary>
        /// Creates a <see cref="MessageRegistrationLease"/> configured according to the supplied <paramref name="options"/>.
        /// </summary>
        /// <param name="options">Options controlling bus selection, diagnostics, and lifecycle behavior.</param>
        /// <returns>A lease that wraps the created <see cref="MessageRegistrationToken"/>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is <see langword="null"/>.</exception>
        public MessageRegistrationLease Build(MessageRegistrationBuildOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            IMessageBus messageBus = ResolveMessageBus(options);
            InstanceId owner = ResolveOwner(options);
            MessageHandler messageHandler = new MessageHandler(owner, messageBus);
            messageHandler.active = options.HandlerStartsActive;
            MessageRegistrationToken token = MessageRegistrationToken.Create(
                messageHandler,
                messageBus
            );
            token.DiagnosticMode = options.EnableDiagnostics;
            if (options.Configure != null)
            {
                options.Configure(token);
            }

            MessageRegistrationLease lease = new MessageRegistrationLease(
                token,
                messageHandler,
                messageBus,
                options.Lifecycle
            );
            lease.InvokeBuildHook();

            if (options.ActivateOnBuild)
            {
                lease.Activate();
            }

            return lease;
        }

        private static InstanceId ResolveOwner(MessageRegistrationBuildOptions options)
        {
#if UNITY_2021_3_OR_NEWER
            if (options.UnityOwner != null)
            {
                if (options.UnityOwner is UnityEngine.GameObject gameObject)
                {
                    return gameObject;
                }

                if (options.UnityOwner is UnityEngine.Component component)
                {
                    return component;
                }

                return new InstanceId(options.UnityOwner.GetInstanceID());
            }
#endif
            if (options.Owner.HasValue)
            {
                return options.Owner.Value;
            }

            int generatedId = Interlocked.Decrement(ref _syntheticOwnerCounter);

            InstanceId syntheticOwner = new InstanceId(generatedId);
            return syntheticOwner;
        }

        private IMessageBus ResolveMessageBus(MessageRegistrationBuildOptions options)
        {
            if (options.PreferredMessageBus != null)
            {
                return options.PreferredMessageBus;
            }

            IMessageBusProvider effectiveProvider =
                options.MessageBusProvider ?? _messageBusProvider;
            if (effectiveProvider != null)
            {
                IMessageBus resolved = effectiveProvider.Resolve();
                if (resolved != null)
                {
                    return resolved;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Simple provider that always resolves to the supplied bus instance.
    /// </summary>
    public sealed class FixedMessageBusProvider : IMessageBusProvider
    {
        private readonly IMessageBus _messageBus;

        public FixedMessageBusProvider(IMessageBus messageBus)
        {
            _messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
        }

        public IMessageBus Resolve()
        {
            return _messageBus;
        }
    }
}
