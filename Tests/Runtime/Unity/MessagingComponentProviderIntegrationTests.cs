namespace DxMessaging.Tests.Runtime.Unity
{
    using System.Collections;
    using System.Text.RegularExpressions;
    using DxMessaging.Core;
    using DxMessaging.Core.MessageBus;
    using DxMessaging.Core.Messages;
    using DxMessaging.Unity;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;

    public sealed class MessagingComponentProviderIntegrationTests
    {
        [UnityTest]
        public IEnumerator ConfigureWithProviderHandleRoutesThroughProviderBus()
        {
            MessageBus messageBus = new();
            TestScriptableMessageBusProvider provider =
                ScriptableObject.CreateInstance<TestScriptableMessageBusProvider>();
            provider.Configure(messageBus);
            MessageBusProviderHandle handle = new(provider);

            GameObject owner = new("MessagingComponentOwner");
            MessagingComponent messagingComponent = owner.AddComponent<MessagingComponent>();
            messagingComponent.Configure(handle, MessageBusRebindMode.RebindActive);

            TestListener listener = owner.AddComponent<TestListener>();
            listener.Initialize(messagingComponent);

            yield return null;

            TestUntargetedMessage message = new(42);
            messageBus.UntargetedBroadcast(ref message);

            Assert.AreEqual(1, listener.ReceivedCount);

            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(provider);
        }

        [UnityTest]
        public IEnumerator InstallerAppliesConfigurationToChildren()
        {
            MessageBus messageBus = new();
            TestScriptableMessageBusProvider provider =
                ScriptableObject.CreateInstance<TestScriptableMessageBusProvider>();
            provider.Configure(messageBus);
            MessageBusProviderHandle handle = new(provider);

            GameObject root = new("InstallerRoot");
            MessagingComponentInstaller installer =
                root.AddComponent<MessagingComponentInstaller>();
            installer.SetProvider(handle);

            GameObject child = new("InstallerListener");
            child.transform.SetParent(root.transform);
            MessagingComponent messagingComponent = child.AddComponent<MessagingComponent>();

            installer.ApplyConfiguration();

            TestListener listener = child.AddComponent<TestListener>();
            listener.Initialize(messagingComponent);

            yield return null;

            TestUntargetedMessage message = new(19);
            messageBus.UntargetedBroadcast(ref message);

            Assert.AreEqual(1, listener.ReceivedCount);

            Object.DestroyImmediate(root);
            Object.DestroyImmediate(provider);
        }

        [UnityTest]
        public IEnumerator CreateRegistrationBuilderUsesConfiguredProviderBus()
        {
            MessageBus messageBus = new();
            TestProvider provider = new(messageBus);

            GameObject owner = new("BuilderOwner");
            MessagingComponent messagingComponent = owner.AddComponent<MessagingComponent>();
            messagingComponent.Configure(provider, MessageBusRebindMode.RebindActive);

            IMessageRegistrationBuilder builder = messagingComponent.CreateRegistrationBuilder();
            MessageRegistrationLease lease = builder.Build(new MessageRegistrationBuildOptions());

            Assert.AreSame(messageBus, lease.MessageBus);
            Assert.IsFalse(lease.Token.Enabled);

            lease.Dispose();
            Object.DestroyImmediate(owner);
            yield break;
        }

        [UnityTest]
        public IEnumerator CreateRegistrationBuilderUsesOverrideBusWhenNoProvider()
        {
            MessageBus messageBus = new();

            GameObject owner = new("OverrideOwner");
            MessagingComponent messagingComponent = owner.AddComponent<MessagingComponent>();
            messagingComponent.Configure(messageBus, MessageBusRebindMode.RebindActive);

            IMessageRegistrationBuilder builder = messagingComponent.CreateRegistrationBuilder();
            MessageRegistrationLease lease = builder.Build(new MessageRegistrationBuildOptions());

            Assert.AreSame(messageBus, lease.MessageBus);

            lease.Dispose();
            Object.DestroyImmediate(owner);
            yield break;
        }

        [UnityTest]
        public IEnumerator InstallerWithoutConfigurationLogsWarning()
        {
            GameObject root = new("InstallerWarningRoot");
            MessagingComponentInstaller installer =
                root.AddComponent<MessagingComponentInstaller>();

            GameObject child = new("InstallerWarningChild");
            child.transform.SetParent(root.transform);
            _ = child.AddComponent<MessagingComponent>();

            LogAssert.Expect(
                LogType.Warning,
                new Regex(
                    "MessagingComponentInstaller.+has no provider or explicit message bus configured"
                )
            );

            installer.ApplyConfiguration();
            yield return null;

            Object.DestroyImmediate(root);
        }

        private sealed class TestListener : MonoBehaviour
        {
            private MessageRegistrationToken _token;
            public int ReceivedCount { get; private set; }

            public void Initialize(MessagingComponent messagingComponent)
            {
                _token = messagingComponent.Create(this);
                _ = _token.RegisterUntargeted<TestUntargetedMessage>(OnUntargetedMessage);
                _token.Enable();
            }

            private void OnUntargetedMessage(ref TestUntargetedMessage message)
            {
                ReceivedCount++;
            }

            private void OnDestroy()
            {
                _token?.Disable();
            }
        }

        private sealed class TestScriptableMessageBusProvider : ScriptableMessageBusProvider
        {
            private IMessageBus _bus;

            public void Configure(IMessageBus bus)
            {
                _bus = bus;
            }

            public override IMessageBus Resolve()
            {
                return _bus;
            }
        }

        private sealed class TestProvider : IMessageBusProvider
        {
            private readonly IMessageBus _messageBus;

            public TestProvider(IMessageBus messageBus)
            {
                _messageBus = messageBus;
            }

            public IMessageBus Resolve()
            {
                return _messageBus;
            }
        }

        private readonly struct TestUntargetedMessage : IUntargetedMessage
        {
            public TestUntargetedMessage(int value)
            {
                Value = value;
            }

            public int Value { get; }
        }
    }
}
