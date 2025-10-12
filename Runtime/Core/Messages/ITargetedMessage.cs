namespace DxMessaging.Core.Messages
{
    using System;

    /// <summary>
    /// Messages addressed to a specific entity.
    /// </summary>
    /// <remarks>
    /// Targeted messages are best used for commands or directed events such as "Entity X, pick up item" or
    /// "Entity X completed an active reload". Keep message types flat and immutable.
    /// <para>
    /// Implement this interface or mark your type with <see cref="Attributes.DxTargetedMessageAttribute"/>.
    /// Prefer the generic variant <see cref="ITargetedMessage{T}"/> for structs to avoid boxing and ensure a stable
    /// <see cref="IMessage.MessageType"/>.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Generic interface form (no boxing for structs)
    /// public readonly struct Heal : DxMessaging.Core.Messages.ITargetedMessage&lt;Heal&gt;
    /// {
    ///     public readonly int amount;
    ///     public Heal(int amount) { this.amount = amount; }
    /// }
    ///
    /// // Attribute form with auto constructor
    /// [DxMessaging.Core.Attributes.DxTargetedMessage]
    /// [DxMessaging.Core.Attributes.DxAutoConstructor]
    /// public readonly struct EquipWeapon { public readonly int weaponId; }
    /// </code>
    /// </example>
    public interface ITargetedMessage : IMessage { }

    /// <summary>
    /// No-alloc TargetedMessages. Implement to avoid boxing for struct messages.
    /// </summary>
    /// <typeparam name="T">Concrete type of the derived. Should be the derived type and nothing else.</typeparam>
    public interface ITargetedMessage<T> : ITargetedMessage
        where T : ITargetedMessage
    {
        Type IMessage.MessageType => typeof(T);
    }
}
