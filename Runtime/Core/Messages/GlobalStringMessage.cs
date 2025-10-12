namespace DxMessaging.Core.Messages
{
    using System;

    /// <summary>
    /// Convenience untargeted message carrying a simple string payload.
    /// </summary>
    /// <remarks>
    /// Useful in examples and tests for demonstrating untargeted/global notifications.
    /// In production, define domain-specific message structs for clarity.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Broadcast globally
    /// var msg = new DxMessaging.Core.Messages.GlobalStringMessage("Settings saved");
    /// msg.Emit();
    /// </code>
    /// </example>
    public readonly struct GlobalStringMessage : IUntargetedMessage<GlobalStringMessage>
    {
        public Type MessageType => typeof(GlobalStringMessage);

        public readonly string message;

        /// <summary>
        /// Creates a new message wrapping the provided string.
        /// </summary>
        /// <param name="message">The string payload.</param>
        public GlobalStringMessage(string message)
        {
            this.message = message;
        }
    }
}
