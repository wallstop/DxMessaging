namespace DxMessaging.Unity.Integrations.Reflex
{
#if REFLEX_PRESENT
    using Core.MessageBus;
    using global::Reflex.Attributes;
    using global::Reflex.Core;

    /// <summary>
    /// Optional installer that binds <see cref="IMessageRegistrationBuilder"/> for Reflex containers.
    /// </summary>
    public sealed class DxMessagingRegistrationInstaller : IInstaller
    {
        public void InstallBindings(ContainerBuilder containerBuilder)
        {
            containerBuilder.AddSingleton(
                typeof(IMessageRegistrationBuilder),
                typeof(ContainerMessageRegistrationBuilder)
            );
        }

        public sealed class ContainerMessageRegistrationBuilder : IMessageRegistrationBuilder
        {
            [Inject]
            private Container _container;

            public MessageRegistrationLease Build(MessageRegistrationBuildOptions options)
            {
                MessageRegistrationBuilder innerBuilder = ResolveInnerBuilder();
                return innerBuilder.Build(options);
            }

            private MessageRegistrationBuilder ResolveInnerBuilder()
            {
                IMessageBusProvider provider = TryResolveProvider();
                if (provider != null)
                {
                    return new MessageRegistrationBuilder(provider);
                }

                return new MessageRegistrationBuilder(new ContainerMessageBusProvider(_container));
            }

            private IMessageBusProvider TryResolveProvider()
            {
                try
                {
                    return _container.Resolve<IMessageBusProvider>();
                }
                catch
                {
                    return null;
                }
            }
        }

        public sealed class ContainerMessageBusProvider : IMessageBusProvider
        {
            private readonly Container _container;

            public ContainerMessageBusProvider(Container container)
            {
                _container = container;
            }

            public IMessageBus Resolve()
            {
                return _container.Resolve<IMessageBus>();
            }
        }
    }
#endif
}
