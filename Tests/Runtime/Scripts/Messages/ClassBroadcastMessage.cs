namespace DxMessaging.Tests.Runtime.Scripts.Messages
{
    using DxMessaging.Core.Messages;

    // Class-based broadcast message for testing class overloads.
    public sealed class ClassBroadcastMessage : IBroadcastMessage<ClassBroadcastMessage>
    {
        public readonly string text;

        public ClassBroadcastMessage() { }

        public ClassBroadcastMessage(string text)
        {
            this.text = text;
        }
    }
}
