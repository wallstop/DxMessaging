namespace DxMessaging.Core.Attributes
{
    using System;

    [AttributeUsage(
        AttributeTargets.Class | AttributeTargets.Struct,
        Inherited = false,
        AllowMultiple = false
    )]
    public sealed class DxUntargetedMessageAttribute : Attribute { }
}
