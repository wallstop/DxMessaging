#if UNITY_2021_3_OR_NEWER
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
        /// <summary>
        /// Registers the DxMessaging builder services within the Reflex container.
        /// </summary>
        /// <param name="containerBuilder">Container builder receiving the registrations.</param>
        public void InstallBindings(ContainerBuilder containerBuilder)
        {
            containerBuilder.AddSingleton(
                typeof(ContainerMessageRegistrationBuilder),
                typeof(ContainerMessageRegistrationBuilder),
                typeof(IMessageRegistrationBuilder)
            );
        }

        internal sealed class ContainerMessageRegistrationBuilder : IMessageRegistrationBuilder
        {
            [Inject]
            private Container _container;

            /// <summary>
            /// Builds a leasing wrapper using the container-aware provider resolution logic.
            /// </summary>
            /// <param name="options">Build options provided by the caller.</param>
            /// <returns>Lease produced by the underlying <see cref="MessageRegistrationBuilder"/>.</returns>
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

            /// <summary>
            /// Wraps a Reflex container as an <see cref="IMessageBusProvider"/>.
            /// </summary>
            /// <param name="container">Container used to resolve <see cref="IMessageBus"/> instances.</param>
            public ContainerMessageBusProvider(Container container)
            {
                _container = container;
            }

            /// <summary>
            /// Resolves an <see cref="IMessageBus"/> from the underlying container.
            /// </summary>
            /// <returns>Message bus resolved from Reflex.</returns>
            public IMessageBus Resolve()
            {
                return _container.Resolve<IMessageBus>();
            }
        }
    }
#endif
}
#endif
