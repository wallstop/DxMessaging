namespace DxMessaging.Core.Messages
{
    using System;

    /// <summary>
    /// General-purpose messages not addressed to a specific entity.
    /// </summary>
    /// <remarks>
    /// Think of untargeted messages as global notifications, for example: "the world regenerated" or "settings changed".
    /// Avoid deep inheritance; message types should be flat, immutable data.
    /// <para>
    /// You can implement this interface directly or annotate your type with
    /// <see cref="Attributes.DxUntargetedMessageAttribute"/>. Prefer the generic variant
    /// <see cref="IUntargetedMessage{T}"/> for structs to avoid boxing and to provide a stable <see cref="IMessage.MessageType"/>.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // With generic interface (no boxing for structs)
    /// public readonly struct VideoSettingsChanged : DxMessaging.Core.Messages.IUntargetedMessage&lt;VideoSettingsChanged&gt;
    /// {
    ///     public readonly int width, height;
    ///     public VideoSettingsChanged(int width, int height) { this.width = width; this.height = height; }
    /// }
    ///
    /// // Or with attribute + DxAutoConstructor
    /// [DxMessaging.Core.Attributes.DxUntargetedMessage]
    /// [DxMessaging.Core.Attributes.DxAutoConstructor]
    /// public readonly struct WorldRegenerated { public readonly int seed; }
    /// </code>
    /// </example>
    public interface IUntargetedMessage : IMessage { }

    /// <summary>
    /// No-alloc UntargetedMessages. Implement to avoid boxing for struct messages.
    /// </summary>
    /// <typeparam name="T">Concrete type of the derived. Should be the derived type and nothing else.</typeparam>
    public interface IUntargetedMessage<T> : IUntargetedMessage
        where T : IUntargetedMessage
    {
        Type IMessage.MessageType => typeof(T);
    }
}
