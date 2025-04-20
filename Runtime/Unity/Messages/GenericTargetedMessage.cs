namespace DxMessaging.Unity.Messages
{
    using Core.Attributes;

    [DxTargetedMessage]
    public readonly partial struct GenericTargetedMessage
    {
        public readonly string message;

        public GenericTargetedMessage(string message)
        {
            this.message = message;
        }
    }
}
