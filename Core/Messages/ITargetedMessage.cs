namespace DxMessaging.Core.Messages
{
    /// <inheritdoc cref="IMessage" />
    /// <summary>
    /// Used to specify general-purposes messages that are meant to be sent to a specific entity.
    /// </summary>
    /// <note>
    /// TargetedMessages should be thought of as commands.
    /// Inheritance should be completely flat. Ie, TargetedMessages should be the direct parent of every implementer.
    /// </note>
    public interface ITargetedMessage : IMessage { }
}
