namespace DxMessaging.Core.Messages
{
    /// <inheritdoc />
    /// <summary>
    /// Used to specify general-purposes messages that aren't meant to be sent to specific entity.
    /// </summary>
    /// <note>
    /// UntargetedMessages should be thought of as game-wide, global information. Things like
    /// "The world has been regenerated." or "The video settings have been updated to this resolution".
    /// Inheritance should be completely flat. Ie, UntargetedMessages should be the direct parent of every implementer.
    /// </note>
    public interface IUntargetedMessage : IMessage { }
}
