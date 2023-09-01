namespace DxMessaging.Core.Messages
{
    /// <summary>
    ///     Message from a specific entity but for any listener
    /// </summary>
    /// <note>
    /// BroadcastMessages should be thought of as reactionary. Things like "EntityX has died", "EntityX has picked up ItemZ",
    /// or "EntityX has lost 1 health".
    /// Inheritance should be completely flat. Ie, IBroadcastMessage should be the direct parent of every implementer.
    /// </note>
    public interface IBroadcastMessage : IMessage
    {
    }
}
