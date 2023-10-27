namespace DxMessaging.Core.Messages
{
    using System;

    /// <summary>
    /// Used to specify general-purposes messages that are meant to be sent to a specific entity.
    /// </summary>
    /// <note>
    /// TargetedMessages should be thought of as commands. Things like "EntityX, pick up this rock" or
    /// "EntityX, you successfully completed an active reload".
    /// Inheritance should be completely flat. Ie, TargetedMessages should be the direct parent of every implementer.
    /// </note>
    public interface ITargetedMessage : IMessage { }

    /// <summary>
    /// No-alloc TargetedMessages. Derive from this type to not have your messages boxed (if they are structs).
    /// <code>
    /// public readonly MyCoolStruct : ITargetedMessage{MyCoolStruct}
    /// </code>
    /// </summary>
    /// <typeparam name="T">Concrete type of the derived. Should be the derived type and nothing else.</typeparam>
    public interface ITargetedMessage<T> : ITargetedMessage where T: ITargetedMessage
    {
        Type IMessage.MessageType => typeof(T);
    }
}
