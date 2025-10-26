namespace DxMessaging.Tests.Runtime.Reflex
{
#if REFLEX_PRESENT
    using DxMessaging.Core;
    using DxMessaging.Core.Extensions;
    using DxMessaging.Core.MessageBus;
    using DxMessaging.Tests.Runtime;
    using DxMessaging.Tests.Runtime.Scripts.Messages;
    using DxMessaging.Unity;
    using DxMessaging.Unity.Integrations.Reflex;
    using global::Reflex.Core;
    using global::Reflex.Injectors;
    using NUnit.Framework;
    using UnityEngine;

    public sealed class ReflexIntegrationTests : UnityFixtureBase
    {
        [Test]
        public void InstallerRegistersMessageBusAndConfiguratorAppliesIt()
        {
            ContainerBuilder builder = new();
            DxMessagingInstaller installer = new();
            installer.InstallBindings(builder);

            Container container = TrackDisposable(builder.Build());
            IMessageBus bus = container.Resolve<IMessageBus>();
            Assert.NotNull(bus, "Reflex installer should bind IMessageBus.");

            GameObject go = Track(
                new GameObject(
                    nameof(InstallerRegistersMessageBusAndConfiguratorAppliesIt),
                    typeof(MessagingComponent),
                    typeof(ReflexConfiguredListener)
                )
            );

            ReflexConfiguredListener listener = go.GetComponent<ReflexConfiguredListener>();
            AttributeInjector.Inject(listener, container);
            listener.Initialize(bus);

            SimpleUntargetedMessage message = new();
            message.EmitUntargeted(bus);

            Assert.AreEqual(
                1,
                listener.ReceivedCount,
                "Listener should observe messages emitted through the Reflex container's bus."
            );
        }

        [Test]
        public void RegistrationInstallerBindsBuilderAgainstContainerBus()
        {
            ContainerBuilder builder = new();
            DxMessagingInstaller coreInstaller = new();
            coreInstaller.InstallBindings(builder);

            builder.AddSingleton(
                typeof(StaticMessageBusProvider),
                typeof(StaticMessageBusProvider),
                typeof(IMessageBusProvider)
            );

            DxMessagingRegistrationInstaller registrationInstaller = new();
            registrationInstaller.InstallBindings(builder);

            Container container = TrackDisposable(builder.Build());

            StaticMessageBusProvider provider = container.Resolve<StaticMessageBusProvider>();

            IMessageRegistrationBuilder registrationBuilder =
                container.Resolve<IMessageRegistrationBuilder>();
            MessageRegistrationLease lease = registrationBuilder.Build(
                new MessageRegistrationBuildOptions()
            );

            Assert.AreSame(
                provider.Bus,
                lease.MessageBus,
                "Reflex registration installer should prefer the registered IMessageBusProvider."
            );

            lease.Dispose();
        }

        [Test]
        public void RegistrationInstallerExposesConcreteBuilder()
        {
            ContainerBuilder builder = new();
            DxMessagingInstaller coreInstaller = new();
            coreInstaller.InstallBindings(builder);

            DxMessagingRegistrationInstaller registrationInstaller = new();
            registrationInstaller.InstallBindings(builder);

            Container container = TrackDisposable(builder.Build());

            var concrete =
                container.Resolve<DxMessagingRegistrationInstaller.ContainerMessageRegistrationBuilder>();
            Assert.IsNotNull(
                concrete,
                "Container should resolve the concrete registration builder type."
            );

            IMessageRegistrationBuilder builderInterface =
                container.Resolve<IMessageRegistrationBuilder>();
            Assert.IsTrue(
                ReferenceEquals(concrete, builderInterface),
                "Container should return the same instance for concrete and interface resolutions."
            );
        }

        private sealed class DxMessagingInstaller : IInstaller
        {
            public void InstallBindings(ContainerBuilder containerBuilder)
            {
                containerBuilder.AddSingleton(
                    typeof(MessageBus),
                    typeof(MessageBus),
                    typeof(IMessageBus)
                );
            }
        }

        private sealed class ReflexConfiguredListener : MonoBehaviour
        {
            private IMessageBus _messageBus;
            private MessageRegistrationToken _token;

            internal int ReceivedCount { get; private set; }

            internal void Initialize(IMessageBus messageBus)
            {
                _messageBus = messageBus;
                MessagingComponent messagingComponent = GetComponent<MessagingComponent>();
                messagingComponent.Configure(_messageBus, MessageBusRebindMode.RebindActive);

                _token = messagingComponent.Create(this);
                _ = _token.RegisterUntargeted<SimpleUntargetedMessage>(_ => ++ReceivedCount);
                _token.Enable();
            }

            private void OnDestroy()
            {
                _token?.Disable();
                _token = null;
                _messageBus = null;
            }
        }

        private sealed class StaticMessageBusProvider : IMessageBusProvider
        {
            private readonly IMessageBus _bus = new MessageBus();

            public IMessageBus Bus => _bus;

            public IMessageBus Resolve()
            {
                return _bus;
            }
        }
    }
#endif
}
