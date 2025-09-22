namespace DxMessaging.Unity
{
    using System;
    using System.Collections.Generic;
    using Core;
    using UnityEngine;
    using UnityEngine.Serialization;

    [DisallowMultipleComponent]
    public sealed class MessagingComponent : MonoBehaviour
    {
        [FormerlySerializedAs("_emitMessagesWhenDisabled")]
        public bool emitMessagesWhenDisabled;

        private MessageHandler _messageHandler;

        internal readonly Dictionary<MonoBehaviour, MessageRegistrationToken> _registeredListeners =
            new();

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

        private void Awake()
        {
            _messageHandler ??= CreateMessageHandler();
        }

        public void OnEnable()
        {
            ToggleMessageHandler(true);
        }

        public void OnDisable()
        {
            ToggleMessageHandler(false);
        }

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

        private MessageHandler CreateMessageHandler()
        {
            return new MessageHandler(gameObject) { active = true };
        }
    }
}
