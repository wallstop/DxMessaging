namespace DxMessaging.Core
{
    using System;

    /// <summary>
    /// Common base for all Messaging needs. A common base lets us share some implementation details with type safety.
    /// </summary>
    public interface IMessage
    {
        Type MessageType => GetType();
    }
}
