#if UNITY_2021_3_OR_NEWER
namespace DxMessaging.Tests.Runtime.Unity
{
    using System.Collections;
    using System.Collections.Generic;
    using DxMessaging.Core.MessageBus;
    using DxMessaging.Unity;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;

    public sealed class MessagingComponentInstallerSceneTests
    {
        private readonly List<Object> _createdObjects = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object instance in _createdObjects)
            {
                if (instance != null)
                {
                    Object.DestroyImmediate(instance);
                }
            }
            _createdObjects.Clear();
        }

        [UnityTest]
        public IEnumerator InstallerAppliesSerializedProviderToChildren()
        {
            MessageBus messageBus = new();
            TestScriptableMessageBusProvider provider = Track(
                ScriptableObject.CreateInstance<TestScriptableMessageBusProvider>()
            );
            provider.Configure(messageBus);

            GameObject root = Track(new GameObject("InstallerRoot"));
            MessagingComponentInstaller installer =
                root.AddComponent<MessagingComponentInstaller>();
            installer.SetProvider(new MessageBusProviderHandle(provider));

            GameObject child = Track(new GameObject("InstallerChild"));
            child.transform.SetParent(root.transform);
            MessagingComponent component = child.AddComponent<MessagingComponent>();

            SerializedObject serializedComponent = new(component);
            SerializedProperty autoConfigureProperty = serializedComponent.FindProperty(
                "autoConfigureSerializedProviderOnAwake"
            );
            Assert.That(autoConfigureProperty, Is.Not.Null, "Expected auto configure property.");
            autoConfigureProperty.boolValue = true;
            serializedComponent.ApplyModifiedPropertiesWithoutUndo();

            installer.ApplyConfiguration();

            yield return null;

            Assert.That(
                component.SerializedProviderHandle.SerializedProvider,
                Is.EqualTo(provider),
                "Serialized provider handle should reference the installer provider."
            );
            Assert.That(
                component.SerializedProviderHandle.ResolveBus(),
                Is.EqualTo(messageBus),
                "Serialized provider should resolve to the configured bus."
            );

            component.enabled = false;
            yield return null;
            component.enabled = true;
            yield return null;

            Assert.That(
                component.SerializedProviderHandle.ResolveBus(),
                Is.EqualTo(messageBus),
                "Provider resolution should persist after enable/disable cycles."
            );
        }

        private T Track<T>(T unityObject)
            where T : Object
        {
            if (unityObject != null)
            {
                _createdObjects.Add(unityObject);
            }
            return unityObject;
        }

        private sealed class TestScriptableMessageBusProvider : ScriptableMessageBusProvider
        {
            private IMessageBus _bus;

            internal void Configure(IMessageBus bus)
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
#endif
