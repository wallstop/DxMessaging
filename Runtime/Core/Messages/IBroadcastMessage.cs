namespace DxMessaging.Core.Messages
{
    using System;

    /// <summary>
    /// Message from a specific entity but for any listener.
    /// </summary>
    /// <note>
    /// BroadcastMessages should be thought of as reactionary. Things like "EntityX has died", "EntityX has picked up ItemZ",
    /// or "EntityX has lost 1 health".
    /// Inheritance should be completely flat. Ie, IBroadcastMessage should be the direct parent of every implementer.
    /// </note>
    public interface IBroadcastMessage : IMessage { }

    /// <summary>
    /// No-alloc BroadcastMessages. Derive from this type to not have your messages boxed (if they are structs).
    /// <code>
    /// public readonly MyCoolStruct : IBroadCastMessage{MyCoolStruct}
    /// </code>
    /// </summary>
    /// <typeparam name="T">Concrete type of the derived. Should be the derived type and nothing else.</typeparam>
    public interface IBroadcastMessage<T> : IBroadcastMessage where T: IBroadcastMessage
    {
        Type IMessage.MessageType => typeof(T);
    }

}
