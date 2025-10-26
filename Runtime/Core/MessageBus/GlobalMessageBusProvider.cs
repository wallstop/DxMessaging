namespace DxMessaging.Core.MessageBus
{
    using DxMessaging.Core;

    /// <summary>
    /// Default provider that returns the process-wide <see cref="MessageHandler.MessageBus"/>.
    /// </summary>
    public sealed class GlobalMessageBusProvider : IMessageBusProvider
    {
        /// <summary>
        /// Shared instance to avoid repeated allocations when consumers need a provider.
        /// </summary>
        public static GlobalMessageBusProvider Instance { get; } = new();

        private GlobalMessageBusProvider() { }

        /// <inheritdoc />
        public IMessageBus Resolve()
        {
            return MessageHandler.MessageBus;
        }
    }
}
