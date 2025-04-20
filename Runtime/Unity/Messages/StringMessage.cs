namespace DxMessaging.Unity.Messages
{
    using Core.Attributes;

    [DxTargetedMessage]
    public readonly partial struct StringMessage
    {
        public readonly string message;

        public StringMessage(string message)
        {
            this.message = message;
        }
    }
}
