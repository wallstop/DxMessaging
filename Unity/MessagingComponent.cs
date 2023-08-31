namespace DxMessaging.Unity
{
    using Core;
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    
    [DisallowMultipleComponent]
    public sealed class MessagingComponent : MonoBehaviour
    {
        private MessageHandler _messageHandler;
        private readonly Dictionary<MonoBehaviour, MessageRegistrationToken> _registeredListeners = new Dictionary<MonoBehaviour, MessageRegistrationToken>();

        public MessageRegistrationToken Create(MonoBehaviour listener)
        {
            if (listener == null)
            {
                throw new ArgumentNullException(nameof(listener));
            }

            if (gameObject.GetInstanceID() != listener.gameObject.GetInstanceID())
            {
                throw new ArgumentException(string.Format(
                    "Cannot create a RegistrationToken without an valid owner. {0}.",
                    listener.gameObject.GetInstanceID()));
            }

            if (_registeredListeners.TryGetValue(listener, out MessageRegistrationToken createdToken))
            {
                MessagingDebug.Log("Ignoring double RegistrationToken request for {0}.", listener);
                return createdToken;
            }

            if (_messageHandler == null)
            {
                _messageHandler = new MessageHandler(gameObject)
                {
                    active = true
                };
                MessagingDebug.Log("Creating MessageHandler for componentType {0}, GameObject name: {1}, InstanceId: {2}.",
                    listener.GetType(), listener.gameObject.name, (InstanceId) gameObject);
            }
            else
            {
                MessagingDebug.Log(
                    "Using existing MessageHandler for componentType {0}, GameObject name: {1}, InstanceId: {2}.",
                    listener.GetType(), listener.gameObject.name, (InstanceId) gameObject);
            }

            createdToken = MessageRegistrationToken.Create(_messageHandler);
            _registeredListeners[listener] = createdToken;
            return createdToken;
        }

        private void OnEnable()
        {
            ToggleMessageHandler(true);
        }

        private void OnDisable()
        {
            ToggleMessageHandler(false);
        }

        private void ToggleMessageHandler(bool newActive)
        {
            if (_messageHandler != null && _messageHandler.active != newActive)
            {
                _messageHandler.active = newActive;
            }
        }
    }
}
