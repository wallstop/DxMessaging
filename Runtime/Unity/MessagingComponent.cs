namespace DxMessaging.Unity
{
    using System;
    using System.Collections.Generic;
    using Core;
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
            return new MessageHandler(gameObject) { active = true };
        }
    }
}
