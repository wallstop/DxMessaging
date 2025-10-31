#if UNITY_2021_3_OR_NEWER
namespace DxMessaging.Unity
{
    using System;
    using System.Collections.Generic;
    using Core;
    using Core.MessageBus;
    using UnityEngine;
    using UnityEngine.Serialization;

    /// <summary>
    /// Unity bridge that hosts a <see cref="Core.MessageHandler"/> for a <see cref="UnityEngine.GameObject"/>.
    /// </summary>
    /// <remarks>
    /// - Create tokens with <see cref="Create(MonoBehaviour)"/> and bind message registrations in <c>Awake</c>/<c>Start</c>.
    /// - Call <see cref="MessageRegistrationToken.Enable"/> in <c>OnEnable</c> and <see cref="MessageRegistrationToken.Disable"/> in <c>OnDisable</c>.
    /// - Use <see cref="emitMessagesWhenDisabled"/> if you want to keep emitting while the GameObject is disabled.
    ///
    /// Multiple tokens can be created for different components on the same GameObject; a single underlying
    /// handler instance is shared.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class MessagingComponent : MonoBehaviour
    {
        /// <summary>
        /// If true, this component will continue emitting messages while disabled.
        /// </summary>
        [FormerlySerializedAs("_emitMessagesWhenDisabled")]
        public bool emitMessagesWhenDisabled;

        [SerializeField]
        private bool autoConfigureSerializedProviderOnAwake;

        [SerializeField]
        private MessageBusProviderHandle _serializedProviderHandle;

        private MessageHandler _messageHandler;

        [NonSerialized]
        private IMessageBus _messageBusOverride;

        [NonSerialized]
        private IMessageBusProvider _messageBusProvider;

        internal readonly Dictionary<MonoBehaviour, MessageRegistrationToken> _registeredListeners =
            new();

        internal bool AutoConfigureSerializedProviderOnAwake =>
            autoConfigureSerializedProviderOnAwake;

        internal bool HasRuntimeProvider => _messageBusProvider != null;

        internal bool HasMessageBusOverride => _messageBusOverride != null;

        internal bool HasSerializedProvider => _serializedProviderHandle.TryGetProvider(out _);

        internal MessageBusProviderHandle SerializedProviderHandle => _serializedProviderHandle;

        internal ScriptableMessageBusProvider SerializedProviderAsset =>
            _serializedProviderHandle.SerializedProvider;

        /// <summary>
        /// Creates a <see cref="IMessageRegistrationBuilder"/> aligned with this component's configured bus or provider.
        /// </summary>
        public IMessageRegistrationBuilder CreateRegistrationBuilder()
        {
            IMessageBusProvider provider = ResolveRegistrationProvider();
            if (provider != null)
            {
                return new MessageRegistrationBuilder(provider);
            }

            return new MessageRegistrationBuilder();
        }

        /// <summary>
        /// Creates (or returns existing) registration token for the given component on this GameObject.
        /// </summary>
        /// <param name="listener">A component attached to the same GameObject.</param>
        /// <returns>A <see cref="Core.MessageRegistrationToken"/> bound to the underlying <see cref="Core.MessageHandler"/>.</returns>
        /// <exception cref="ArgumentNullException">If <paramref name="listener"/> is null.</exception>
        /// <exception cref="ArgumentException">If <paramref name="listener"/> is not attached to this GameObject.</exception>
        public MessageRegistrationToken Create(MonoBehaviour listener)
        {
            if (listener == null)
            {
                throw new ArgumentNullException(nameof(listener));
            }

            if (gameObject != listener.gameObject)
            {
                throw new ArgumentException(
                    $"Cannot create a RegistrationToken without a mismatched owner. {listener.gameObject} != existing {gameObject}."
                );
            }

            if (
                _registeredListeners.TryGetValue(
                    listener,
                    out MessageRegistrationToken createdToken
                )
            )
            {
                if (MessagingDebug.enabled)
                {
                    MessagingDebug.Log(
                        LogLevel.Warn,
                        "Ignoring double RegistrationToken request for {0}.",
                        listener
                    );
                }

                return createdToken;
            }

            _messageHandler ??= CreateMessageHandler();
            createdToken = MessageRegistrationToken.Create(_messageHandler);
            _registeredListeners[listener] = createdToken;
            return createdToken;
        }

        /// <summary>
        /// Overrides the default message bus used when new handlers are created.
        /// </summary>
        /// <param name="messageBus">Message bus to prefer. Pass <c>null</c> to fall back to the global bus.</param>
        /// <param name="rebindMode">Controls whether existing registrations move to the new bus immediately.</param>
        public void Configure(IMessageBus messageBus, MessageBusRebindMode rebindMode)
        {
            _messageBusOverride = messageBus;
            _messageBusProvider = null;
            _serializedProviderHandle = MessageBusProviderHandle.Empty;
            ApplyMessageBusConfiguration(rebindMode);
        }

        /// <summary>
        /// Configures the component to resolve message buses via the supplied provider.
        /// </summary>
        /// <param name="messageBusProvider">Provider to use for subsequent handler/token resolution.</param>
        /// <param name="rebindMode">Controls whether existing listeners should migrate immediately.</param>
        public void Configure(
            IMessageBusProvider messageBusProvider,
            MessageBusRebindMode rebindMode
        )
        {
            _messageBusProvider = messageBusProvider;
            if (messageBusProvider != null)
            {
                _messageBusOverride = null;
            }

            _serializedProviderHandle =
                messageBusProvider != null
                    ? MessageBusProviderHandle.FromProvider(messageBusProvider)
                    : MessageBusProviderHandle.Empty;
            ApplyMessageBusConfiguration(rebindMode);
        }

        /// <summary>
        /// Configures the component using a serialized provider handle.
        /// </summary>
        /// <param name="providerHandle">Handle that resolves the preferred provider.</param>
        /// <param name="rebindMode">Controls whether existing listeners should migrate immediately.</param>
        public void Configure(
            MessageBusProviderHandle providerHandle,
            MessageBusRebindMode rebindMode
        )
        {
            _serializedProviderHandle = providerHandle;
            if (providerHandle.TryGetProvider(out IMessageBusProvider provider))
            {
                _messageBusProvider = provider;
                _messageBusOverride = null;
            }
            else
            {
                _messageBusProvider = null;
            }

            ApplyMessageBusConfiguration(rebindMode);
        }

        /// <summary>
        /// Releases the registration token previously created for <paramref name="listener"/>.
        /// </summary>
        /// <param name="listener">Listener whose token should be released.</param>
        /// <remarks>
        /// Invokes <see cref="MessageRegistrationToken.Disable"/> and removes the listener from the internal cache.
        /// Safe to call multiple times.
        /// </remarks>
        public bool Release(MonoBehaviour listener)
        {
            if (listener == null)
            {
                return false;
            }

            if (_registeredListeners.Remove(listener, out MessageRegistrationToken token))
            {
                token?.Disable();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Ensures the underlying <see cref="Core.MessageHandler"/> exists.
        /// </summary>
        private void Awake()
        {
            if (
                autoConfigureSerializedProviderOnAwake
                && _messageBusOverride == null
                && _messageBusProvider == null
                && _serializedProviderHandle.TryGetProvider(out IMessageBusProvider provider)
            )
            {
                _messageBusProvider = provider;
            }

            _messageHandler ??= CreateMessageHandler();
        }

        /// <summary>
        /// Activates the underlying handler when this component becomes enabled.
        /// </summary>
        public void OnEnable()
        {
            ToggleMessageHandler(true);
        }

        /// <summary>
        /// Deactivates the underlying handler when this component becomes disabled.
        /// </summary>
        public void OnDisable()
        {
            ToggleMessageHandler(false);
        }

        /// <summary>
        /// Explicitly toggle the underlying handler's active state.
        /// </summary>
        /// <param name="newActive">Desired active state.</param>
        public void ToggleMessageHandler(bool newActive)
        {
            if (!newActive && emitMessagesWhenDisabled)
            {
                return;
            }

            if (_messageHandler != null && _messageHandler.active != newActive)
            {
                _messageHandler.active = newActive;
            }
        }

        /// <summary>
        /// Creates the underlying <see cref="Core.MessageHandler"/> bound to this GameObject.
        /// </summary>
        private MessageHandler CreateMessageHandler()
        {
            IMessageBus resolvedBus = ResolveConfiguredBus();
            MessageHandler handler = new(gameObject, resolvedBus) { active = true };
            return handler;
        }

        private void ApplyMessageBusConfiguration(MessageBusRebindMode rebindMode)
        {
            IMessageBus resolvedBus = ResolveConfiguredBus();
            _messageHandler?.SetDefaultMessageBus(resolvedBus);
            if (_registeredListeners.Count == 0)
            {
                return;
            }

            MessageBusRebindMode effectiveMode =
#pragma warning disable CS0618 // Type or member is obsolete
                rebindMode == MessageBusRebindMode.Unknown
#pragma warning restore CS0618 // Type or member is obsolete
                    ? MessageBusRebindMode.RebindActive
                    : rebindMode;

            foreach (MessageRegistrationToken token in _registeredListeners.Values)
            {
                token.RetargetMessageBus(resolvedBus, effectiveMode);
            }
        }

        private IMessageBus ResolveConfiguredBus()
        {
            if (_messageBusOverride != null)
            {
                return _messageBusOverride;
            }

            if (_messageBusProvider != null)
            {
                IMessageBus providedBus = _messageBusProvider.Resolve();
                if (providedBus != null)
                {
                    return providedBus;
                }
            }

            return null;
        }

        private IMessageBusProvider ResolveRegistrationProvider()
        {
            if (_messageBusProvider != null)
            {
                return _messageBusProvider;
            }

            if (
                _serializedProviderHandle.TryGetProvider(out IMessageBusProvider providerFromHandle)
            )
            {
                return providerFromHandle;
            }

            if (_messageBusOverride != null)
            {
                return new FixedMessageBusProvider(_messageBusOverride);
            }

            return null;
        }
    }
}
#endif
