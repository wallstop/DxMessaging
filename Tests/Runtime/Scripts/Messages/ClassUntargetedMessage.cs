namespace DxMessaging.Tests.Runtime.Scripts.Messages
{
    using DxMessaging.Core.Messages;

    // Class-based untargeted message for testing class overloads
    public sealed class ClassUntargetedMessage : IUntargetedMessage<ClassUntargetedMessage>
    {
        public readonly string text;

        public ClassUntargetedMessage() { }

        public ClassUntargetedMessage(string text)
        {
            this.text = text;
        }
    }
}
