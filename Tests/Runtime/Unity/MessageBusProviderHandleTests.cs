namespace DxMessaging.Tests.Runtime.Unity
{
    using DxMessaging.Core.MessageBus;
    using DxMessaging.Unity;
    using NUnit.Framework;
    using UnityEngine;

    [TestFixture]
    public sealed class MessageBusProviderHandleTests
    {
        [Test]
        public void TryGetProviderReturnsRuntimeProvider()
        {
            TestMessageBusProvider runtimeProvider = new(new MessageBus());
            MessageBusProviderHandle handle = MessageBusProviderHandle.FromProvider(
                runtimeProvider
            );

            bool resolved = handle.TryGetProvider(out IMessageBusProvider provider);

            Assert.IsTrue(resolved);
            Assert.AreSame(runtimeProvider, provider);
        }

        [Test]
        public void TryGetProviderReturnsAssetProvider()
        {
            TestScriptableMessageBusProvider providerAsset =
                ScriptableObject.CreateInstance<TestScriptableMessageBusProvider>();
            providerAsset.Configure(new MessageBus());
            MessageBusProviderHandle handle = new(providerAsset);

            bool resolved = handle.TryGetProvider(out IMessageBusProvider provider);

            Assert.IsTrue(resolved);
            Assert.AreSame(providerAsset, provider);

            Object.DestroyImmediate(providerAsset);
        }

        [Test]
        public void ResolveBusReturnsNullWhenUnassigned()
        {
            MessageBusProviderHandle handle = MessageBusProviderHandle.Empty;

            IMessageBus bus = handle.ResolveBus();

            Assert.IsNull(bus);
        }

        [Test]
        public void HandleSurvivesSerializationCycle()
        {
            TestScriptableMessageBusProvider providerAsset =
                ScriptableObject.CreateInstance<TestScriptableMessageBusProvider>();
            providerAsset.Configure(new MessageBus());
            MessageBusProviderHandle handle = new(providerAsset);

            HandleWrapper wrapper = new() { Handle = handle };
            string json = JsonUtility.ToJson(wrapper);
            HandleWrapper deserialized = JsonUtility.FromJson<HandleWrapper>(json);

            bool resolved = deserialized.Handle.TryGetProvider(out IMessageBusProvider provider);

            Assert.IsTrue(resolved);
            Assert.AreSame(providerAsset, provider);

            Object.DestroyImmediate(providerAsset);
        }

        private sealed class TestMessageBusProvider : IMessageBusProvider
        {
            private readonly IMessageBus _bus;

            public TestMessageBusProvider(IMessageBus bus)
            {
                _bus = bus;
            }

            public IMessageBus Resolve()
            {
                return _bus;
            }
        }

        [System.Serializable]
        private struct HandleWrapper
        {
            public MessageBusProviderHandle Handle;
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
    }
}
