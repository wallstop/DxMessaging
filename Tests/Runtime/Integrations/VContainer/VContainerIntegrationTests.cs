namespace DxMessaging.Tests.Runtime.VContainer
{
#if VCONTAINER_PRESENT
    using System;
    using DxMessaging.Core;
    using DxMessaging.Core.Extensions;
    using DxMessaging.Core.MessageBus;
    using DxMessaging.Tests.Runtime.Scripts.Messages;
    using DxMessaging.Unity;
    using global::VContainer;
    using global::VContainer.Unity;
    using NUnit.Framework;
    using UnityEngine;

    public sealed class VContainerIntegrationTests
    {
        [Test]
        public void ContainerInjectsMessagingComponentWithCustomBus()
        {
            ContainerBuilder builder = new();
            builder.Register<MessageBus>(Lifetime.Singleton).As<IMessageBus>();

            using IObjectResolver resolver = builder.Build();
            IMessageBus bus = resolver.Resolve<IMessageBus>();
            Assert.NotNull(bus);

            GameObject go = new(
                nameof(ContainerInjectsMessagingComponentWithCustomBus),
                typeof(MessagingComponent),
                typeof(VContainerConfiguredListener)
            );

            try
            {
                resolver.InjectGameObject(go);
                VContainerConfiguredListener listener =
                    go.GetComponent<VContainerConfiguredListener>();
                listener.Initialize(bus);

                SimpleUntargetedMessage message = new();
                message.EmitUntargeted(bus);

                Assert.AreEqual(
                    1,
                    listener.ReceivedCount,
                    "Listener should receive messages emitted via the VContainer-resolved bus."
                );
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
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
                messagingComponent.Configure(_messageBus);

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
    }
#endif
}
