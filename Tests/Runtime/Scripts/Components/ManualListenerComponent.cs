namespace DxMessaging.Tests.Runtime.Scripts.Components
{
    using DxMessaging.Core;
    using DxMessaging.Unity;
    using UnityEngine;

    public sealed class ManualListenerComponent : MonoBehaviour
    {
        public MessageRegistrationToken Token { get; private set; }

        public MessageRegistrationToken RequestToken(MessagingComponent component)
        {
            Token = component.Create(this);
            return Token;
        }

        public void ClearToken()
        {
            Token = null;
        }
    }
}
