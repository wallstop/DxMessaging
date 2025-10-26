#if REFLEX_PRESENT
namespace DxMessaging.Unity.Integrations.Reflex
{
    using Core.MessageBus;
    using global::Reflex.Core;

    /// <summary>
    /// Optional installer that binds <see cref="IMessageRegistrationBuilder"/> for Reflex containers.
    /// </summary>
    public sealed class DxMessagingRegistrationInstaller : IInstaller
    {
        public void InstallBindings(ContainerBuilder containerBuilder)
        {
            containerBuilder.AddSingleton(
                typeof(MessageRegistrationBuilder),
                typeof(IMessageRegistrationBuilder)
            );
        }
    }
}
#endif
