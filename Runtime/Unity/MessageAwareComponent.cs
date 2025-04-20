namespace DxMessaging.Unity
{
    using Core;
    using Messages;
    using UnityEngine;

    [RequireComponent(typeof(MessagingComponent))]
    public abstract class MessageAwareComponent : MonoBehaviour
    {
        public virtual MessageRegistrationToken Token => _messageRegistrationToken;

        protected MessageRegistrationToken _messageRegistrationToken;

        /// <summary>
        ///     If true, will register/unregister handles when the component is enabled or disabled.
        /// </summary>
        protected virtual bool MessageRegistrationTiedToEnableStatus => true;

        protected bool _isQuitting;

        protected MessagingComponent _messagingComponent;

        protected virtual void Awake()
        {
            _messagingComponent = GetComponent<MessagingComponent>();
            _messageRegistrationToken = _messagingComponent.Create(this);
            RegisterMessageHandlers();
        }

        protected virtual void RegisterMessageHandlers()
        {
            _ = _messageRegistrationToken.RegisterGameObjectTargeted<GenericTargetedMessage>(
                gameObject,
                HandleGenericGameObjectMessage
            );
            _ = _messageRegistrationToken.RegisterComponentTargeted<GenericTargetedMessage>(
                this,
                HandleGenericComponentMessage
            );
            _ = _messageRegistrationToken.RegisterUntargeted<GenericUntargetedMessage>(
                HandleGenericUntargetedMessage
            );
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

        protected virtual void HandleGenericGameObjectMessage(ref GenericTargetedMessage message)
        {
            // No-op by default
        }

        protected virtual void HandleGenericComponentMessage(ref GenericTargetedMessage message)
        {
            // No-op by default
        }

        protected virtual void HandleGenericUntargetedMessage(ref GenericUntargetedMessage message)
        {
            // No-op by default
        }
    }
}
