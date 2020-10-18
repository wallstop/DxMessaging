namespace DxMessaging.Core.Extensions
{
    using MessageBus;
    using System;

    /// <summary>
    /// Extensions to smartly go about emitting messages :^)
    /// </summary>
    public static class MessageExtensions
    {
        /// <summary>
        /// Emits a TargetedMessage of the given type.
        /// </summary>
        /// <note>
        /// Do NOT use if you do not have the exact type of the message.
        /// Good: new MyCoolTargetedMessage().EmitTargeted();
        /// Bad: ((TargetedMessage) new MyCoolTargetedMessage()).EmitTargeted();
        /// </note>
        /// <typeparam name="T">Type of the TargetedMessage to emit.</typeparam>
        /// <param name="message">TargetedMessage to emit.</param>
        /// <param name="messageBus">MessageBus to emit to. If null, uses the GlobalMessageBus.</param>
        public static void EmitTargeted<T>(this T message, IMessageBus messageBus = null) where T : TargetedMessage
        {
            if (typeof(T) == typeof(TargetedMessage))
            {
                throw new Exception($"Poorly formed EmitTargeted() called for {message}. Please use the absolute type instead of TargetedMessage.");
            }
            (messageBus ?? MessageHandler.MessageBus).TargetedBroadcast(message);
        }

        /// <summary>
        /// Emits an UntargetedMessage of the given type.
        /// </summary>
        /// <note>
        /// Do NOT use if you do not have the exact type of the message.
        /// Good: new MyCoolUntargetedMessage().UntargetedMessage();
        /// Bad: ((UntargetedMessage) new MyCoolUntargetedMessage()).UntargetedMessage();
        /// </note>
        /// <typeparam name="T">Type of the UntargetedMessage to emit.</typeparam>
        /// <param name="message">UntargetedMessage to emit.</param>
        /// <param name="messageBus">MessageBus to emit to. If null, uses the GlobalMessageBus.</param>
        public static void EmitUntargeted<T>(this T message, IMessageBus messageBus = null) where T : UntargetedMessage
        {
            if (typeof(T) == typeof(UntargetedMessage))
            {
                throw new Exception($"Poorly formed EmitUntargeted() called for {message}. Please use the absolute type instead of UntargetedMessage.");
            }
            (messageBus ?? MessageHandler.MessageBus).UntargetedBroadcast(message);
        }

        /// <summary>
        /// Emits any message, regardless of type.
        /// </summary>
        /// <note>
        /// You can use this if you don't have the exact type of the message. But it's pretty slow.
        /// </note>
        /// <param name="message">Non-null Message to emit.</param>
        /// <param name="messageBus">MessageBus to emit to. If null, uses the GlobalMessageBus.</param>
        public static void EmitUntyped(this AbstractMessage message, IMessageBus messageBus = null)
        {
            (messageBus ?? MessageHandler.MessageBus).Broadcast(message);
        }
    }
}
