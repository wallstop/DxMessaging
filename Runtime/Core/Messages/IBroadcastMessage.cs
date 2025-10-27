namespace DxMessaging.Core.Messages
{
    using System;

    /// <summary>
    /// Messages emitted by a specific source for any listener to consume.
    /// </summary>
    /// <remarks>
    /// Broadcast messages are reactionary: for example, "Entity X took damage" or "Entity X picked up item Z".
    /// Keep message types flat and immutable.
    /// <para>
    /// Implement this interface or annotate your type with <see cref="Attributes.DxBroadcastMessageAttribute"/>.
    /// Prefer the generic variant <see cref="IBroadcastMessage{T}"/> for structs to avoid boxing.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Generic interface form (no boxing for structs)
    /// public readonly struct TookDamage : DxMessaging.Core.Messages.IBroadcastMessage&lt;TookDamage&gt;
    /// {
    ///     public readonly int amount;
    ///     public TookDamage(int amount) { this.amount = amount; }
    /// }
    ///
    /// // Attribute + auto constructor
    /// [DxMessaging.Core.Attributes.DxBroadcastMessage]
    /// [DxMessaging.Core.Attributes.DxAutoConstructor]
    /// public readonly partial struct PickedUpItem { public readonly int itemId; }
    /// </code>
    /// </example>
    public interface IBroadcastMessage : IMessage { }

    /// <summary>
    /// No-alloc BroadcastMessages. Implement to avoid boxing for struct messages.
    /// </summary>
    /// <typeparam name="T">Concrete type of the derived. Should be the derived type and nothing else.</typeparam>
    public interface IBroadcastMessage<T> : IBroadcastMessage
        where T : IBroadcastMessage
    {
        Type IMessage.MessageType => typeof(T);
    }
}
