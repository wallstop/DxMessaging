namespace DxMessaging.Unity
{
    using System;
    using DxMessaging.Core.MessageBus;
    using UnityEngine;

    /// <summary>
    /// Serializable handle that references a <see cref="ScriptableMessageBusProvider"/> or runtime provider.
    /// </summary>
    [Serializable]
    public struct MessageBusProviderHandle
    {
        [SerializeField]
        private ScriptableMessageBusProvider providerAsset;

        [NonSerialized]
        private IMessageBusProvider runtimeProvider;

        /// <summary>
        /// Initializes a new instance referencing the supplied provider asset.
        /// </summary>
        /// <param name="provider">Serialized provider asset.</param>
        public MessageBusProviderHandle(ScriptableMessageBusProvider provider)
        {
            providerAsset = provider;
            runtimeProvider = provider;
        }

        /// <summary>
        /// Gets an empty handle.
        /// </summary>
        public static MessageBusProviderHandle Empty => default;

        /// <summary>
        /// Creates a handle that wraps a runtime-only provider instance.
        /// </summary>
        /// <param name="provider">Runtime provider.</param>
        /// <returns>Handle referencing the provider.</returns>
        public static MessageBusProviderHandle FromProvider(IMessageBusProvider provider)
        {
            MessageBusProviderHandle handle = default;
            handle.runtimeProvider = provider;
            return handle;
        }

        /// <summary>
        /// Associates the handle with a runtime provider, returning a new handle.
        /// </summary>
        /// <param name="provider">Provider to associate.</param>
        /// <returns>New handle referencing the provider.</returns>
        public MessageBusProviderHandle WithRuntimeProvider(IMessageBusProvider provider)
        {
            MessageBusProviderHandle handle = this;
            handle.runtimeProvider = provider;
            return handle;
        }

        /// <summary>
        /// Attempts to resolve the provider referenced by this handle.
        /// </summary>
        /// <param name="provider">Resolved provider.</param>
        /// <returns><c>true</c> when a provider exists; otherwise <c>false</c>.</returns>
        public bool TryGetProvider(out IMessageBusProvider provider)
        {
            if (runtimeProvider != null)
            {
                provider = runtimeProvider;
                return true;
            }

            if (providerAsset != null)
            {
                provider = providerAsset;
                return true;
            }

            provider = null;
            return false;
        }

        /// <summary>
        /// Resolves the effective message bus for this handle.
        /// </summary>
        /// <returns>The resolved bus, or <see langword="null"/> if none available.</returns>
        public IMessageBus ResolveBus()
        {
            if (!TryGetProvider(out IMessageBusProvider provider))
            {
                return null;
            }

            return provider.Resolve();
        }
    }
}
