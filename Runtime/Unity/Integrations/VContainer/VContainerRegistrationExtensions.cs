#if VCONTAINER_PRESENT
namespace DxMessaging.Unity.Integrations.VContainer
{
    using global::VContainer;
    using Core.MessageBus;
    using global::VContainer.Unity;

    /// <summary>
    /// Provides convenience extension methods for wiring <see cref="IMessageRegistrationBuilder"/> inside VContainer scopes.
    /// </summary>
    public static class VContainerRegistrationExtensions
    {
        /// <summary>
        /// Registers <see cref="IMessageRegistrationBuilder"/> as a transient service backed by the scoped message bus.
        /// </summary>
        public static void RegisterMessageRegistrationBuilder(this IContainerBuilder builder)
        {
            builder.Register<IMessageRegistrationBuilder>(CreateBuilder, Lifetime.Transient);
        }

        private static IMessageRegistrationBuilder CreateBuilder(IObjectResolver resolver)
        {
            if (resolver.TryResolve(out IMessageBusProvider provider))
            {
                return new MessageRegistrationBuilder(provider);
            }

            return new MessageRegistrationBuilder(new ResolverMessageBusProvider(resolver));
        }

        private sealed class ResolverMessageBusProvider : IMessageBusProvider
        {
            private readonly IObjectResolver _resolver;

            public ResolverMessageBusProvider(IObjectResolver resolver)
            {
                _resolver = resolver;
            }

            public IMessageBus Resolve()
            {
                return _resolver.Resolve<IMessageBus>();
            }
        }
    }
}
#endif
