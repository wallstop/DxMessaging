namespace DxMessaging.Unity.Messages
{
    using System;
    using Core.Messages;

    public readonly struct GlobalStringMessage : IUntargetedMessage<GlobalStringMessage>
    {
        public Type MessageType => typeof(GlobalStringMessage);

        public readonly string message;

        public GlobalStringMessage(string message)
        {
            this.message = message;
        }
    }
}
