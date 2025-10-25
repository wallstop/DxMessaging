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

        private MessageHandler _messageHandler;

        [NonSerialized]
        private IMessageBus _messageBusOverride;

        internal readonly Dictionary<MonoBehaviour, MessageRegistrationToken> _registeredListeners =
            new();

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

            if (gameObject.GetInstanceID() != listener.gameObject.GetInstanceID())
            {
                throw new ArgumentException(
                    $"Cannot create a RegistrationToken without an valid owner. {listener.gameObject.GetInstanceID()}."
                );
            }

            if (
                _registeredListeners.TryGetValue(
                    listener,
                    out MessageRegistrationToken createdToken
                )
            )
            {
                MessagingDebug.Log(
                    LogLevel.Warn,
                    "Ignoring double RegistrationToken request for {0}.",
                    listener
                );
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
#pragma warning disable CS0618 // Type or member is obsolete
        /// <param name="rebindMode">Controls whether existing registrations move to the new bus immediately.</param>
        public void Configure(IMessageBus messageBus, MessageBusRebindMode rebindMode)
#pragma warning restore CS0618 // Type or member is obsolete
        {
            _messageBusOverride = messageBus;
            _messageHandler?.SetDefaultMessageBus(_messageBusOverride);
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
                token.RetargetMessageBus(_messageBusOverride, effectiveMode);
            }
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
            MessageHandler handler = new(gameObject, _messageBusOverride) { active = true };
            return handler;
        }
    }
}
