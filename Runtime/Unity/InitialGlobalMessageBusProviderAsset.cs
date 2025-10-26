namespace DxMessaging.Unity
{
    using DxMessaging.Core;
    using DxMessaging.Core.MessageBus;
    using UnityEngine;

    /// <summary>
    /// Serialized provider that always returns the original global message bus created during static initialisation.
    /// </summary>
    [CreateAssetMenu(
        fileName = "InitialGlobalMessageBusProvider",
        menuName = "Wallstop Studios/DxMessaging/Message Bus Providers/Initial Global Message Bus"
    )]
    public sealed class InitialGlobalMessageBusProviderAsset : ScriptableMessageBusProvider
    {
        /// <inheritdoc />
        public override IMessageBus Resolve()
        {
            return MessageHandler.InitialGlobalMessageBus;
        }
    }
}
