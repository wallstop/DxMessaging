namespace DxMessaging.Tests.Runtime.Scripts.Components
{
    using DxMessaging.Unity;
    using Messages;
    using UnityEngine;

    [DisallowMultipleComponent]
    public sealed class GenericMessageAwareComponent : MessageAwareComponent
    {
        protected override void RegisterMessageHandlers()
        {
            base.RegisterMessageHandlers();
            _ = _messageRegistrationToken.RegisterUntargeted(
                (ref GenericUntargetedMessage<int> _) => Debug.Log("Received generic int message.")
            );
        }
    }
}
