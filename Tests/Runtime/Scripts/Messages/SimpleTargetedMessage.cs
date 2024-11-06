namespace DxMessaging.Tests.Runtime.Scripts.Messages
{
    using DxMessaging.Core.Attributes;
    using DxMessaging.Core.Messages;

    [DxTargetedMessage]
    public partial struct SimpleTargetedMessage : ITargetedMessage
    {
    }
}
