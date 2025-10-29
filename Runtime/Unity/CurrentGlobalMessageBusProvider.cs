#if UNITY_2021_3_OR_NEWER
namespace DxMessaging.Unity
{
    using DxMessaging.Core;
    using DxMessaging.Core.MessageBus;
    using UnityEngine;

    /// <summary>
    /// Serialized provider that returns the current global <see cref="MessageHandler.MessageBus"/> instance.
    /// </summary>
    /// <remarks>
    /// This asset mirrors whatever bus is currently configured via <see cref="MessageHandler.SetGlobalMessageBus(IMessageBus)"/>.
    /// Pair it with <see cref="InitialGlobalMessageBusProvider"/> when you need to compare the original and overridden buses.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Configure a MessagingComponent at design time by dragging the asset into a MessagingComponentInstaller
    /// messagingComponentInstaller.SetProvider(CurrentGlobalMessageBusProviderHandle);
    /// </code>
    /// </summary>
    [CreateAssetMenu(
        fileName = "CurrentGlobalMessageBusProvider",
        menuName = "Wallstop Studios/DxMessaging/Message Bus Providers/Current Global Message Bus"
    )]
    public sealed class CurrentGlobalMessageBusProvider : ScriptableMessageBusProvider
    {
        public override IMessageBus Resolve()
        {
            return MessageHandler.MessageBus;
        }
    }
}
#endif
