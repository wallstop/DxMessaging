namespace DxMessaging.Unity
{
    using DxMessaging.Core;
    using DxMessaging.Core.MessageBus;
    using UnityEngine;

    /// <summary>
    /// Serialized provider that returns the global <see cref="MessageHandler.MessageBus"/> instance.
    /// </summary>
    [CreateAssetMenu(
        fileName = "GlobalMessageBusProvider",
        menuName = "Wallstop Studios/DxMessaging/Message Bus Providers/Global Message Bus"
    )]
    public sealed class GlobalMessageBusProviderAsset : ScriptableMessageBusProvider
    {
        /// <inheritdoc />
        public override IMessageBus Resolve()
        {
            return MessageHandler.MessageBus;
        }
    }
}
