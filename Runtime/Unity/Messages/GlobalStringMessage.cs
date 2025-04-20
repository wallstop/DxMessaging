namespace DxMessaging.Unity.Messages
{
    using Core.Attributes;

    [DxUntargetedMessage]
    public readonly partial struct GlobalStringMessage
    {
        public readonly string message;

        public GlobalStringMessage(string message)
        {
            this.message = message;
        }
    }
}
