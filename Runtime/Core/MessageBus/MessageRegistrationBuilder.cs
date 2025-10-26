namespace DxMessaging.Core.MessageBus
{
    using System;
    using System.Threading;
    using DxMessaging.Core;

    /// <summary>
    /// Options controlling how <see cref="MessageRegistrationBuilder"/> constructs registration tokens.
    /// </summary>
    public sealed class MessageRegistrationBuildOptions
    {
        /// <summary>
        /// Explicit owner identifier for the underlying <see cref="MessageHandler"/>.
        /// When omitted, a synthetic identifier is generated automatically.
        /// </summary>
        public InstanceId? Owner { get; set; }

#if UNITY_2017_1_OR_NEWER
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

        public MessageRegistrationToken Token => _token;

        public MessageHandler Handler => _messageHandler;

        public IMessageBus MessageBus => _messageBus;

        public InstanceId Owner => _messageHandler.owner;

        public bool IsActive => _isActive;

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
#if UNITY_2017_1_OR_NEWER
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
