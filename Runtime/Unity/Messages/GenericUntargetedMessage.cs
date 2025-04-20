namespace DxMessaging.Unity.Messages
{
    using Core.Attributes;

    [DxUntargetedMessage]
    public readonly partial struct GenericUntargetedMessage
    {
        public readonly string message;

        public GenericUntargetedMessage(string message)
        {
            this.message = message;
        }
    }
}
