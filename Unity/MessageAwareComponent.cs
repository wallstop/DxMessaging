namespace DxMessaging.Unity
{
    using Core;
    using System;
    using UnityEngine;

    [Serializable]
    [RequireComponent(typeof(MessagingComponent))]
    public abstract class MessageAwareComponent : MonoBehaviour
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
                MessagingComponent messenger = GetComponent<MessagingComponent>();
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

        protected virtual void OnDestroy()
        {
            _messageRegistrationToken?.Disable();
            _messageRegistrationToken = null;
        }
    }
}
