namespace DxMessaging.Tests.Runtime.VContainer
{
#if VCONTAINER_PRESENT
    using System;
    using DxMessaging.Core;
    using DxMessaging.Core.Extensions;
    using DxMessaging.Core.MessageBus;
    using DxMessaging.Tests.Runtime;
    using DxMessaging.Tests.Runtime.Scripts.Messages;
    using DxMessaging.Unity;
    using DxMessaging.Unity.Integrations.VContainer;
    using global::VContainer;
    using global::VContainer.Unity;
    using NUnit.Framework;
    using UnityEngine;

    public sealed class VContainerIntegrationTests : UnityFixtureBase
    {
        [Test]
        public void ContainerInjectsMessagingComponentWithCustomBus()
        {
            ContainerBuilder builder = new();
            builder.Register<MessageBus>(Lifetime.Singleton).As<IMessageBus>();

            IObjectResolver resolver = TrackDisposable(builder.Build());
            IMessageBus bus = resolver.Resolve<IMessageBus>();
            Assert.NotNull(bus);

            GameObject go = Track(
                new GameObject(
                    nameof(ContainerInjectsMessagingComponentWithCustomBus),
                    typeof(MessagingComponent),
                    typeof(VContainerConfiguredListener)
                )
            );

            resolver.InjectGameObject(go);
            VContainerConfiguredListener listener = go.GetComponent<VContainerConfiguredListener>();
            listener.Initialize(bus);

            SimpleUntargetedMessage message = new();
            message.EmitUntargeted(bus);

            Assert.AreEqual(
                1,
                listener.ReceivedCount,
                "Listener should receive messages emitted via the VContainer-resolved bus."
            );
        }

        [Test]
        public void RegistrationExtensionsExposeBuilder()
        {
            ContainerBuilder builder = new();
            builder.Register<MessageBus>(Lifetime.Singleton).As<IMessageBus>();
            builder.RegisterMessageRegistrationBuilder();

            IObjectResolver resolver = TrackDisposable(builder.Build());
            IMessageRegistrationBuilder registrationBuilder =
                resolver.Resolve<IMessageRegistrationBuilder>();
            MessageRegistrationLease lease = registrationBuilder.Build(
                new MessageRegistrationBuildOptions()
            );

            Assert.AreSame(
                resolver.Resolve<IMessageBus>(),
                lease.MessageBus,
                "Registration extension should resolve a builder backed by the container bus."
            );

            lease.Dispose();
        }

        [Test]
        public void RegistrationExtensionsPreferResolvedProvider()
        {
            ContainerBuilder builder = new();
            builder.Register<MessageBus>(Lifetime.Singleton).As<IMessageBus>();

            MessageBus providerBus = new MessageBus();
            builder
                .RegisterInstance<IMessageBusProvider>(new StaticMessageBusProvider(providerBus))
                .As<IMessageBusProvider>();
            builder.RegisterMessageRegistrationBuilder();

            IObjectResolver resolver = TrackDisposable(builder.Build());
            IMessageRegistrationBuilder registrationBuilder =
                resolver.Resolve<IMessageRegistrationBuilder>();
            MessageRegistrationLease lease = registrationBuilder.Build(
                new MessageRegistrationBuildOptions()
            );

            Assert.AreSame(
                providerBus,
                lease.MessageBus,
                "Registration extensions should prefer an explicitly registered IMessageBusProvider."
            );

            lease.Dispose();
        }

        private sealed class VContainerConfiguredListener : MonoBehaviour, IDisposable
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

            public void Dispose()
            {
                _token?.Disable();
                _token = null;
                _messageBus = null;
            }
        }

        private sealed class StaticMessageBusProvider : IMessageBusProvider
        {
            private readonly IMessageBus _bus;

            public StaticMessageBusProvider(IMessageBus bus)
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
