using System;
using DxMessaging.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace Assets.Scripts {

    [Serializable]
    [RequireComponent(typeof(MessagingComponent))]
    public abstract class MessageAwareNetworkedComponent : NetworkBehaviour
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
