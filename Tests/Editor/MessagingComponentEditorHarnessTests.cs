#if UNITY_EDITOR && UNITY_2021_3_OR_NEWER
namespace DxMessaging.Tests.Editor
{
    using System.Collections.Generic;
    using Core;
    using Core.MessageBus;
    using Core.Messages;
    using DxMessaging.Editor.Testing;
    using DxMessaging.Unity;
    using NUnit.Framework;
    using UnityEngine;
    using Object = UnityEngine.Object;

    [TestFixture]
    public sealed class MessagingComponentEditorHarnessTests
    {
        private readonly List<GameObject> _createdGameObjects = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject gameObject in _createdGameObjects)
            {
                if (gameObject != null)
                {
                    Object.DestroyImmediate(gameObject);
                }
            }
            _createdGameObjects.Clear();

            if (MessageHandler.MessageBus is MessageBus messageBus)
            {
                messageBus.DiagnosticsMode = false;
                messageBus._emissionBuffer.Clear();
            }
        }

        [Test]
        public void CaptureReflectsListenerDiagnostics()
        {
            GameObject host = CreateTrackedObject("HarnessHost");
            MessagingComponent messagingComponent = host.AddComponent<MessagingComponent>();
            TestListener listener = host.AddComponent<TestListener>();

            MessageRegistrationToken token = messagingComponent.Create(listener);
            token.DiagnosticMode = true;
            token.RegisterUntargeted<TestHarnessMessage>(listener.OnMessage);
            token.Enable();

            MessageBus messageBus = MessageHandler.MessageBus as MessageBus;
            Assert.That(
                messageBus,
                Is.Not.Null,
                "Default message bus should be the DxMessaging MessageBus implementation."
            );
            messageBus.DiagnosticsMode = true;
            messageBus._emissionBuffer.Clear();

            try
            {
                TestHarnessMessage message = default;
                MessageHandler.MessageBus.UntargetedBroadcast(ref message);

                MessagingComponentInspectorState state = MessagingComponentEditorHarness.Capture(
                    messagingComponent
                );

                Assert.That(state.GlobalDiagnosticsEnabled, Is.True);
                Assert.That(
                    state.GlobalEmissionHistory.Count,
                    Is.GreaterThan(0),
                    "Global emission buffer should contain recorded messages."
                );
                Assert.That(state.Listeners.Count, Is.EqualTo(1));

                ListenerDiagnosticsView listenerView = state.Listeners[0];
                Assert.That(listenerView.Listener, Is.EqualTo(listener));
                Assert.That(listenerView.DiagnosticsEnabled, Is.True);
                Assert.That(listenerView.TokenEnabled, Is.True);
                Assert.That(listenerView.Registrations.Count, Is.EqualTo(1));
                Assert.That(listenerView.Registrations[0].CallCount, Is.EqualTo(1));
                Assert.That(listenerView.EmissionHistory.Count, Is.GreaterThan(0));
            }
            finally
            {
                token.Disable();
                messageBus.DiagnosticsMode = false;
            }
        }

        private GameObject CreateTrackedObject(string name)
        {
            GameObject gameObject = new(name);
            _createdGameObjects.Add(gameObject);
            return gameObject;
        }

        private sealed class TestListener : MonoBehaviour
        {
            public void OnMessage(ref TestHarnessMessage message) { }
        }

        private readonly struct TestHarnessMessage : IUntargetedMessage { }
    }
}
#endif
