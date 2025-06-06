﻿namespace DxMessaging.Tests.Runtime.Scripts.Components
{
    using Messages;
    using Unity;
    using UnityEngine;

    [DisallowMultipleComponent]
    public sealed class GenericMessageAwareComponent : MessageAwareComponent
    {
        protected override void RegisterMessageHandlers()
        {
            base.RegisterMessageHandlers();
            _ = _messageRegistrationToken.RegisterUntargeted(
                (ref GenericUntargetedMessage<int> message) =>
                    Debug.Log("Received generic int message.")
            );
        }
    }
}
