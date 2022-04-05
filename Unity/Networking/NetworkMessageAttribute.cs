namespace DxMessaging.Unity.Networking
{
    using System;
    using JetBrains.Annotations;

    [AttributeUsage(AttributeTargets.Struct)]
    [MeansImplicitUse]
    public sealed class NetworkMessageAttribute : Attribute
    {
    }
}