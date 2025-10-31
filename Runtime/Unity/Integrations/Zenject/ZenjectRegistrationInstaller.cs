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

            public ContainerMessageBusProvider(DiContainer container, IMessageBus cachedBus)
            {
                _container = container;
                _cachedBus = cachedBus;
            }

            public IMessageBus Resolve()
            {
                return _cachedBus ?? _container.Resolve<IMessageBus>();
            }
        }
    }
#endif
}
#endif
