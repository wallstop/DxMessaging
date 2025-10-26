namespace DxMessaging.Unity
{
    using DxMessaging.Core;
    using DxMessaging.Core.MessageBus;
    using UnityEngine;

    /// <summary>
    /// Serialized provider that returns the current global <see cref="MessageHandler.MessageBus"/> instance.
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
