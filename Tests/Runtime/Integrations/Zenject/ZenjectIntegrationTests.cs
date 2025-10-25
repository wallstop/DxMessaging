namespace DxMessaging.Tests.Runtime.Zenject
{
#if ZENJECT_PRESENT
    using DxMessaging.Core;
    using DxMessaging.Core.Extensions;
    using DxMessaging.Core.MessageBus;
    using DxMessaging.Tests.Runtime.Scripts.Messages;
    using DxMessaging.Unity;
    using global::Zenject;
    using NUnit.Framework;
    using UnityEngine;

    public sealed class ZenjectIntegrationTests
    {
        [Test]
        public void InstallerBindsMessageBusAndConfiguratorAppliesIt()
        {
            DiContainer container = new();
            container.BindInterfacesAndSelfTo<MessageBus>().AsSingle();

            IMessageBus resolvedBus = container.Resolve<IMessageBus>();
            Assert.IsNotNull(resolvedBus, "Zenject installer should bind IMessageBus.");

            GameObject go = new(
                nameof(InstallerBindsMessageBusAndConfiguratorAppliesIt),
                typeof(MessagingComponent),
                typeof(ZenjectConfiguredListener)
            );

            try
            {
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
            finally
            {
                Object.DestroyImmediate(go);
            }
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
    }
#endif
}
