using System;
using System.Collections.Generic;
using DxMessaging.Core;
using UnityEngine;

namespace Assets.Scripts {

    [Serializable]
    public sealed class MessagingComponent : MonoBehaviour
    {
        private InstanceId _id;
        private MessageHandler _messageHandler;
        private readonly Dictionary<MonoBehaviour, MessageRegistrationToken> _registeredListeners;

        private MessagingComponent()
        {
            _registeredListeners = new Dictionary<MonoBehaviour, MessageRegistrationToken>();
        }

        public MessageRegistrationToken Create(MonoBehaviour listener)
        {
            if (listener == null)
            {
                throw new ArgumentNullException("listener");
            }
            if (gameObject.GetInstanceID() != listener.gameObject.GetInstanceID())
            {
                throw new ArgumentException(string.Format(
                    "Cannot create a RegistrationToken without an valid owner. {0}.",
                    listener.gameObject.GetInstanceID()));
            }

            MessageRegistrationToken createdToken;
            if (_registeredListeners.TryGetValue(listener, out createdToken))
            {
                MessagingDebug.Log("Ignoring double RegistrationToken request for {0}.", listener);
                return createdToken;
            }
            createdToken = MessageRegistrationToken.Create(_messageHandler);
            _registeredListeners[listener] = createdToken;
            return createdToken;
        }

        private void Awake()
        {
            _id = gameObject.GetInstanceID();
            if (gameObject)
            {
                _messageHandler = new MessageHandler(_id);
            }
        }

        private void OnEnable()
        {
            _messageHandler.Active = true;
        }

        private void OnDisable()
        {
            _messageHandler.Active = false;
        }
    }
}
