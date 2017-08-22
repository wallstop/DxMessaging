using System;
using DxMessaging.Core;
using UnityEngine;

namespace Assets.Scripts {

    [Serializable]
    [RequireComponent(typeof(MessagingComponent))]
    public abstract class MessageAwareComponent : MonoBehaviour
    {
        protected MessageRegistrationToken MessageRegistrationToken;

        protected virtual void Awake()
        {
            MessagingComponent messenger = GetComponent<MessagingComponent>();
            if (messenger == null)
            {
                throw new ArgumentNullException("messenger");
            }
            MessageRegistrationToken = messenger.Create(this);
            RegisterMessageHandlers();
        }

        protected abstract void RegisterMessageHandlers();

        protected virtual void OnEnable()
        {
            MessageRegistrationToken.Enable();
        }

        protected virtual void OnDisable()
        {
            MessageRegistrationToken.Disable();
        }
    }
}
