namespace DxMessaging.Tests.Runtime.Scripts.Messages
{
    using DxMessaging.Core.Attributes;

    [DxUntargetedMessage]
    public readonly partial struct GenericUntargetedMessage<T> { }
}
