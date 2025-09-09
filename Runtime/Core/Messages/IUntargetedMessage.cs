namespace DxMessaging.Core.Messages
{
    using System;

    /// <summary>
    /// Used to specify general-purposes messages that aren't meant to be sent to specific entity.
    /// </summary>
    /// <note>
    /// UntargetedMessages should be thought of as game-wide, global information. Things like
    /// "The world has been regenerated." or "The video settings have been updated to this resolution".
    /// Inheritance should be completely flat. Ie, UntargetedMessages should be the direct parent of every implementer.
    /// </note>
    public interface IUntargetedMessage : IMessage { }

    /// <summary>
    /// No-alloc UntargetedMessages. Derive from this type to not have your messages boxed (if they are structs).
    /// <code>
    /// public readonly MyCoolStruct : IUntargetedMessage{MyCoolStruct}
    /// </code>
    /// </summary>
    /// <typeparam name="T">Concrete type of the derived. Should be the derived type and nothing else.</typeparam>
    public interface IUntargetedMessage<T> : IUntargetedMessage
        where T : IUntargetedMessage
    {
        Type IMessage.MessageType => typeof(T);
    }
}
