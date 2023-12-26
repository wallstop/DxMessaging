namespace Tests.Runtime.Scripts.Components
{
    using System;
    using DxMessaging.Core.Messages;
    using DxMessaging.Unity;
    using Messages;

    public sealed class SimpleMessageAwareComponent : MessageAwareComponent
    {
        public Action<IUntargetedMessage> untargetedHandler;
        public Action<ITargetedMessage> targetedHandler;
        public Action<IBroadcastMessage> broadcastHandler;
        public Action<ITargetedMessage> componentTargetedHandler;
        public Action<IBroadcastMessage> componentBroadcastHandler;

        protected override void RegisterMessageHandlers()
        {
            _ = _messageRegistrationToken.RegisterUntargeted<SimpleUntargetedMessage>(HandleSimpleUntargetedMessage);
            _ = _messageRegistrationToken.RegisterGameObjectTargeted<SimpleTargetedMessage>(gameObject, HandleSimpleTargetedMessage);
            _ = _messageRegistrationToken.RegisterGameObjectBroadcast<SimpleBroadcastMessage>(gameObject, HandleSimpleBroadcastMessage);
            _ = _messageRegistrationToken.RegisterComponentTargeted<SimpleTargetedMessage>(this, HandleSimpleComponentTargetedMessage);
            _ = _messageRegistrationToken.RegisterComponentBroadcast<SimpleBroadcastMessage>(this, HandleSimpleComponentBroadcastMessage);
        }

        public void HandleSimpleUntargetedMessage(ref SimpleUntargetedMessage message)
        {
            untargetedHandler?.Invoke(message);
        }

        public void HandleSimpleTargetedMessage(ref SimpleTargetedMessage message)
        {
            targetedHandler?.Invoke(message);
        }

        public void HandleSimpleBroadcastMessage(ref SimpleBroadcastMessage message)
        {
            broadcastHandler?.Invoke(message);
        }

        public void HandleSimpleComponentTargetedMessage(ref SimpleTargetedMessage message)
        {
            componentTargetedHandler?.Invoke(message);
        }

        public void HandleSimpleComponentBroadcastMessage(ref SimpleBroadcastMessage message)
        {
            componentBroadcastHandler?.Invoke(message);
        }
    }
}
