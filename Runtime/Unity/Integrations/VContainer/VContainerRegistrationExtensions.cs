#if UNITY_2021_3_OR_NEWER
namespace DxMessaging.Unity.Integrations.VContainer
{
#if VCONTAINER_PRESENT
    using global::VContainer;
    using Core.MessageBus;

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

        public sealed class ResolverMessageBusProvider : IMessageBusProvider
        {
            private readonly IObjectResolver _resolver;

            /// <summary>
            /// Wraps a VContainer resolver as an <see cref="IMessageBusProvider"/>.
            /// </summary>
            /// <param name="resolver">Resolver used to obtain <see cref="IMessageBus"/> instances.</param>
            public ResolverMessageBusProvider(IObjectResolver resolver)
            {
                _resolver = resolver;
            }

            /// <summary>
            /// Resolves an <see cref="IMessageBus"/> from the current VContainer scope.
            /// </summary>
            /// <returns>Scoped message bus.</returns>
            public IMessageBus Resolve()
            {
                return _resolver.Resolve<IMessageBus>();
            }
        }
    }
#endif
}
#endif
