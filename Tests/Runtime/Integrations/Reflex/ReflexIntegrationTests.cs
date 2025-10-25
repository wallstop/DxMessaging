namespace DxMessaging.Tests.Runtime.Reflex
{
#if REFLEX_PRESENT
    using DxMessaging.Core;
    using DxMessaging.Core.Extensions;
    using DxMessaging.Core.MessageBus;
    using DxMessaging.Tests.Runtime.Scripts.Messages;
    using DxMessaging.Unity;
    using global::Reflex.Core;
    using global::Reflex.Injectors;
    using NUnit.Framework;
    using UnityEngine;

    public sealed class ReflexIntegrationTests
    {
        [Test]
        public void InstallerRegistersMessageBusAndConfiguratorAppliesIt()
        {
            ContainerBuilder builder = new();
            DxMessagingInstaller installer = new();
            installer.InstallBindings(builder);

            Container container = builder.Build();
            IMessageBus bus = container.Resolve<IMessageBus>();
            Assert.NotNull(bus, "Reflex installer should bind IMessageBus.");

            GameObject go = new(
                nameof(InstallerRegistersMessageBusAndConfiguratorAppliesIt),
                typeof(MessagingComponent),
                typeof(ReflexConfiguredListener)
            );

            try
            {
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
            finally
            {
                Object.DestroyImmediate(go);
                container.Dispose();
            }
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
    }
#endif
}
