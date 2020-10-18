namespace DxMessaging.Core
{
    using System;

    /// <inheritdoc />
    /// <summary>
    /// Used to specify general-purposes messages that aren't meant to be sent to specific entity.
    /// </summary>
    /// <note>
    /// UntargetedMessages should be thought of as game-wide. Things like "EntityX has died", or "EntityY has picked up ItemZ".
    /// They should describe things that have happened to something, in case someone is listening.
    /// Inheritance should be completely flat. Ie, UntargetedMessages should be the direct parent of every implementer.
    /// </note>
    [Serializable]
    public abstract class UntargetedMessage : AbstractMessage { }
}
