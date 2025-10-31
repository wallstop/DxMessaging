#if UNITY_2021_3_OR_NEWER
namespace DxMessaging.Unity
{
    using DxMessaging.Core.MessageBus;
    using UnityEngine;

    /// <summary>
    /// Base <see cref="ScriptableObject"/> that resolves an <see cref="IMessageBus"/> instance.
    /// </summary>
    public abstract class ScriptableMessageBusProvider : ScriptableObject, IMessageBusProvider
    {
        /// <inheritdoc />
        public abstract IMessageBus Resolve();
    }
}
#endif
