namespace DxMessaging.Unity.Messages
{
    using System;
    using Core.Messages;

    public readonly struct StringMessage : ITargetedMessage<StringMessage>
    {
        public Type MessageType => typeof(StringMessage);

        public readonly string message;

        public StringMessage(string message)
        {
            this.message = message;
        }
    }
}
