#if UNITY_2021_3_OR_NEWER
namespace DxMessaging.Unity.Integrations.Zenject
{
#if ZENJECT_PRESENT
    using global::Zenject;
    using Core.MessageBus;

    /// <summary>
    /// Optional installer that exposes <see cref="IMessageRegistrationBuilder"/> using the scoped Zenject container.
    /// </summary>
    public sealed class DxMessagingRegistrationInstaller : MonoInstaller
    {
        internal void RunInstallBindings(DiContainer container)
        {
            Container = container;
            InstallBindings();
        }

        /// <summary>
        /// Registers the DxMessaging builder within the Zenject container.
        /// </summary>
        public override void InstallBindings()
        {
            Container.Bind<IMessageRegistrationBuilder>().FromMethod(CreateBuilder).AsTransient();
        }

        private static IMessageRegistrationBuilder CreateBuilder(InjectContext context)
        {
            IMessageBusProvider provider = context.Container.TryResolve<IMessageBusProvider>();
            if (provider != null)
            {
                return new MessageRegistrationBuilder(provider);
            }

            IMessageBus messageBus = context.Container.Resolve<IMessageBus>();
            return new MessageRegistrationBuilder(
                new ContainerMessageBusProvider(context.Container, messageBus)
            );
        }

        public sealed class ContainerMessageBusProvider : IMessageBusProvider
        {
            private readonly DiContainer _container;
            private readonly IMessageBus _cachedBus;

            /// <summary>
            /// Creates a provider that uses the container-supplied bus, falling back to resolving on demand.
            /// </summary>
            /// <param name="container">Zenject container used to resolve services.</param>
            /// <param name="cachedBus">Cached bus instance to return if available.</param>
            public ContainerMessageBusProvider(DiContainer container, IMessageBus cachedBus)
            {
                _container = container;
                _cachedBus = cachedBus;
            }

            /// <summary>
            /// Resolves the message bus for the current scope.
            /// </summary>
            /// <returns>Cached bus if provided; otherwise resolves from the container.</returns>
            public IMessageBus Resolve()
            {
                return _cachedBus ?? _container.Resolve<IMessageBus>();
            }
        }
    }
#endif
}
#endif
