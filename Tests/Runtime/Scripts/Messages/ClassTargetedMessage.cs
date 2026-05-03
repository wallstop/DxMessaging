namespace DxMessaging.Tests.Runtime.Scripts.Messages
{
    using DxMessaging.Core.Messages;

    // Class-based targeted message for testing class overloads.
    public sealed class ClassTargetedMessage : ITargetedMessage<ClassTargetedMessage>
    {
        public readonly string text;

        public ClassTargetedMessage() { }

        public ClassTargetedMessage(string text)
        {
            this.text = text;
        }
    }
}
