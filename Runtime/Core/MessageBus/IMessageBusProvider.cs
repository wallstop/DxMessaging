namespace DxMessaging.Core.MessageBus
{
    /// <summary>
    /// Provides an <see cref="IMessageBus"/> instance for DI-friendly scenarios.
    /// </summary>
    public interface IMessageBusProvider
    {
        /// <summary>
        /// Resolves the <see cref="IMessageBus"/> that should be used for the current context.
        /// </summary>
        /// <returns>The resolved message bus, or <see langword="null"/> to defer to fallbacks.</returns>
        IMessageBus Resolve();
    }
}
