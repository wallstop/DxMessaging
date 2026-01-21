#if UNITY_2021_3_OR_NEWER
namespace DxMessaging.Tests.Runtime.Zenject
{
#if ZENJECT_PRESENT
    using DxMessaging.Core;
    using DxMessaging.Core.Extensions;
    using DxMessaging.Core.MessageBus;
    using DxMessaging.Tests.Runtime;
    using DxMessaging.Tests.Runtime.Scripts.Messages;
    using DxMessaging.Unity;
    using DxMessaging.Unity.Integrations.Zenject;
    using global::Zenject;
    using NUnit.Framework;
    using UnityEngine;

    public sealed class ZenjectIntegrationTests : UnityFixtureBase
    {
        [Test]
        public void InstallerBindsMessageBusAndConfiguratorAppliesIt()
        {
            DiContainer container = new();
            container.BindInterfacesAndSelfTo<MessageBus>().AsSingle();

            IMessageBus resolvedBus = container.Resolve<IMessageBus>();
            Assert.IsNotNull(resolvedBus, "Zenject installer should bind IMessageBus.");

            GameObject go = Track(
                new GameObject(
                    nameof(InstallerBindsMessageBusAndConfiguratorAppliesIt),
                    typeof(MessagingComponent),
                    typeof(ZenjectConfiguredListener)
                )
            );

            ZenjectConfiguredListener listener = go.GetComponent<ZenjectConfiguredListener>();
            listener.Initialize(resolvedBus);

            SimpleUntargetedMessage message = new();
            message.EmitUntargeted(resolvedBus);

            Assert.AreEqual(
                1,
                listener.ReceivedCount,
                "Listener should observe messages emitted through the container-provided bus."
            );
        }

        [Test]
        public void RegistrationInstallerProvidesBuilderBoundToContainerBus()
        {
            DiContainer container = new();
            container.BindInterfacesAndSelfTo<MessageBus>().AsSingle();

            DxMessagingRegistrationInstaller installer = new DxMessagingRegistrationInstaller();
            installer.RunInstallBindings(container);

            IMessageRegistrationBuilder registrationBuilder =
                container.Resolve<IMessageRegistrationBuilder>();
            Assert.NotNull(registrationBuilder);

            using MessageRegistrationLease lease = registrationBuilder.Build(
                new MessageRegistrationBuildOptions()
            );
            Assert.AreSame(
                container.Resolve<IMessageBus>(),
                lease.MessageBus,
                "Builder resolved by the installer should default to the container-provided bus."
            );
        }

        [Test]
        public void RegistrationInstallerPrefersBoundProvider()
        {
            DiContainer container = new();
            MessageBus providerBus = new();
            container
                .Bind<IMessageBusProvider>()
                .FromInstance(new FixedMessageBusProvider(providerBus))
                .AsSingle();
            container.BindInterfacesAndSelfTo<MessageBus>().AsSingle();

            DxMessagingRegistrationInstaller installer = new DxMessagingRegistrationInstaller();
            installer.RunInstallBindings(container);

            IMessageRegistrationBuilder registrationBuilder =
                container.Resolve<IMessageRegistrationBuilder>();
            using MessageRegistrationLease lease = registrationBuilder.Build(
                new MessageRegistrationBuildOptions()
            );

            Assert.AreSame(
                providerBus,
                lease.MessageBus,
                "Builder should prefer the container-provided IMessageBusProvider when available."
            );
        }

        private sealed class ZenjectConfiguredListener : MonoBehaviour
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

        private sealed class FixedMessageBusProvider : IMessageBusProvider
        {
            private readonly IMessageBus _bus;

            public FixedMessageBusProvider(IMessageBus bus)
            {
                _bus = bus;
            }

            public IMessageBus Resolve()
            {
                return _bus;
            }
        }
    }
#endif
}

#endif
