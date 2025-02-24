namespace DxMessaging.Unity
{
    using Core;
    using UnityEngine;

    [RequireComponent(typeof(MessagingComponent))]
    public abstract class MessageAwareComponent : MonoBehaviour
    {
        protected MessageRegistrationToken _messageRegistrationToken;

        /// <summary>
        ///     If true, will register/un-register handles when the component is enabled or disabled.
        /// </summary>
        protected virtual bool MessageRegistrationTiedToEnableStatus => true;

        protected bool _isQuitting;

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
            // No-op, expectation is that implementations implement their own logic here
        }

        protected virtual void OnEnable()
        {
            if (MessageRegistrationTiedToEnableStatus)
            {
                _messageRegistrationToken?.Enable();
            }
        }

        protected virtual void OnDisable()
        {
            if (_isQuitting)
            {
                return;
            }

            if (MessageRegistrationTiedToEnableStatus)
            {
                _messageRegistrationToken?.Disable();
            }
        }

        protected virtual void OnDestroy()
        {
            if (_isQuitting)
            {
                return;
            }

            _messageRegistrationToken?.Disable();
            _messageRegistrationToken = null;
        }

        protected virtual void OnApplicationQuit()
        {
            _isQuitting = true;
        }
    }
}
