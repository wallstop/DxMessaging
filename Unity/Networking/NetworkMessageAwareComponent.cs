namespace DxMessaging.Unity.Networking
{
    using System;
    using Core;
    using global::Unity.Netcode;
    using UnityEngine;

    [Serializable]
    [RequireComponent(typeof(MessagingComponent))]
    public abstract class NetworkMessageAwareComponent : NetworkBehaviour
    {
        protected MessageRegistrationToken _messageRegistrationToken;

        protected virtual void Awake()
        {
            SetupMessageHandlers();
        }

        protected void SetupMessageHandlers()
        {
            if (_messageRegistrationToken == null)
            {
                var messenger = GetComponent<MessagingComponent>();
                _messageRegistrationToken = messenger.Create(this);
            }

            RegisterMessageHandlers();
            _messageRegistrationToken.Enable();
        }

        protected virtual void RegisterMessageHandlers()
        {
        }

        protected virtual void OnEnable()
        {
            _messageRegistrationToken?.Enable();
        }

        protected virtual void OnDisable()
        {
            _messageRegistrationToken?.Disable();
        }

        public override void OnDestroy()
        {
            _messageRegistrationToken?.Disable();
            _messageRegistrationToken = null;
        }
    }

}
