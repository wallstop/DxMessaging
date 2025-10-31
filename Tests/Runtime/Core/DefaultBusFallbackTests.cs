#if UNITY_2021_3_OR_NEWER
namespace DxMessaging.Tests.Runtime.Core
{
    using System.Collections;
    using System.Collections.Generic;
    using DxMessaging.Core;
    using DxMessaging.Core.Extensions;
    using DxMessaging.Core.MessageBus;
    using DxMessaging.Tests.Runtime.Scripts.Components;
    using DxMessaging.Tests.Runtime.Scripts.Messages;
    using DxMessaging.Unity;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;

    public sealed class DefaultBusFallbackTests : MessagingTestBase
    {
        private readonly List<MessageRegistrationHandle> _handles = new();
        private readonly List<MessageRegistrationToken> _tokens = new();

        [TearDown]
        public void TearDown()
        {
            foreach (MessageRegistrationToken token in _tokens)
            {
                foreach (MessageRegistrationHandle handle in _handles)
                {
                    token.RemoveRegistration(handle);
                }
                token.Disable();
            }

            _handles.Clear();
            _tokens.Clear();
        }

        [Test]
        public void HandlerUsesInjectedDefaultBusWhenTokenOmitsBus()
        {
            MessageBus customBus = new();
            MessageHandler handler = new(new InstanceId(42), customBus) { active = true };
            MessageRegistrationToken token = MessageRegistrationToken.Create(handler);
            _tokens.Add(token);

            int callCount = 0;
            MessageRegistrationHandle handle = token.RegisterUntargeted<SimpleUntargetedMessage>(
                _ => ++callCount
            );
            _handles.Add(handle);

            token.Enable();

            SimpleUntargetedMessage message = new();
            message.EmitUntargeted(customBus);
            Assert.AreEqual(
                1,
                callCount,
                "Handler should observe messages emitted through its injected bus."
            );

            message.EmitUntargeted();
            Assert.AreEqual(
                1,
                callCount,
                "Handler should remain isolated from the global bus when using an injected default."
            );
        }

        [Test]
        public void HandlerUsesInjectedDefaultBusForTargetedMessages()
        {
            MessageBus customBus = new();
            MessageHandler handler = new(new InstanceId(99), customBus) { active = true };
            MessageRegistrationToken token = MessageRegistrationToken.Create(handler);
            _tokens.Add(token);

            InstanceId target = new(1234);
            int callCount = 0;
            MessageRegistrationHandle handle = token.RegisterTargeted<SimpleTargetedMessage>(
                target,
                _ => ++callCount
            );
            _handles.Add(handle);

            token.Enable();

            SimpleTargetedMessage message = new();
            message.EmitTargeted(target, customBus);
            Assert.AreEqual(
                1,
                callCount,
                "Targeted handlers should fire when messages emit on the injected bus."
            );

            message.EmitTargeted(target);
            Assert.AreEqual(
                1,
                callCount,
                "Targeted handler should ignore global bus emissions when a default bus is injected."
            );
        }

        [Test]
        public void HandlerUsesInjectedDefaultBusForBroadcastMessages()
        {
            MessageBus customBus = new();
            MessageHandler handler = new(new InstanceId(1337), customBus) { active = true };
            MessageRegistrationToken token = MessageRegistrationToken.Create(handler);
            _tokens.Add(token);

            int callCount = 0;
            MessageRegistrationHandle handle = token.RegisterBroadcastWithoutSource(
                (ref InstanceId _, ref SimpleBroadcastMessage _) => ++callCount
            );
            _handles.Add(handle);

            token.Enable();

            SimpleBroadcastMessage message = new();
            InstanceId source = new(777);
            message.EmitBroadcast(source, customBus);
            Assert.AreEqual(
                1,
                callCount,
                "Broadcast handlers should observe emissions on the injected bus."
            );

            message.EmitBroadcast(source);
            Assert.AreEqual(
                1,
                callCount,
                "Broadcast handler should remain isolated from global emissions when using an injected default bus."
            );
        }

        [UnityTest]
        public IEnumerator MessagingComponentConfigureRebindsExistingHandlerToCustomBus()
        {
            MessageBus customBus = new();
            GameObject go = new(
                nameof(MessagingComponentConfigureRebindsExistingHandlerToCustomBus),
                typeof(MessagingComponent),
                typeof(ManualListenerComponent)
            );
            _spawned.Add(go);

            MessagingComponent messagingComponent = go.GetComponent<MessagingComponent>();
            messagingComponent.Configure(customBus, MessageBusRebindMode.RebindActive);

            ManualListenerComponent listener = go.GetComponent<ManualListenerComponent>();
            MessageRegistrationToken token = listener.RequestToken(messagingComponent);
            _tokens.Add(token);

            int callCount = 0;
            MessageRegistrationHandle handle = token.RegisterUntargeted<SimpleUntargetedMessage>(
                _ => ++callCount
            );
            _handles.Add(handle);
            token.Enable();

            SimpleUntargetedMessage message = new();
            message.EmitUntargeted(customBus);
            Assert.AreEqual(
                1,
                callCount,
                "Configure should redirect tokens to the provided MessageBus."
            );

            message.EmitUntargeted();
            Assert.AreEqual(
                1,
                callCount,
                "Global emissions should not reach tokens configured to use a custom bus."
            );
            yield break;
        }

        [UnityTest]
        public IEnumerator MessagingComponentConfigurePreserveDefersRebindUntilReenabled()
        {
            MessageBus initialBus = new();
            MessageBus newBus = new();
            GameObject go = new(
                nameof(MessagingComponentConfigurePreserveDefersRebindUntilReenabled),
                typeof(MessagingComponent),
                typeof(ManualListenerComponent)
            );
            _spawned.Add(go);

            MessagingComponent messagingComponent = go.GetComponent<MessagingComponent>();
            messagingComponent.Configure(initialBus, MessageBusRebindMode.RebindActive);

            ManualListenerComponent listener = go.GetComponent<ManualListenerComponent>();
            MessageRegistrationToken token = listener.RequestToken(messagingComponent);
            _tokens.Add(token);

            int callCount = 0;
            MessageRegistrationHandle handle = token.RegisterUntargeted<SimpleUntargetedMessage>(
                _ => ++callCount
            );
            _handles.Add(handle);
            token.Enable();

            SimpleUntargetedMessage message = new();
            message.EmitUntargeted(initialBus);
            Assert.AreEqual(1, callCount, "Initial bus should deliver to the registered handler.");

            messagingComponent.Configure(newBus, MessageBusRebindMode.PreserveRegistrations);

            message.EmitUntargeted(newBus);
            Assert.AreEqual(
                1,
                callCount,
                "Preserve mode should avoid rebinding active registrations immediately."
            );

            message.EmitUntargeted(initialBus);
            Assert.AreEqual(
                2,
                callCount,
                "Handlers should continue observing the original bus until re-enabled."
            );

            token.Disable();
            token.Enable();

            message.EmitUntargeted(newBus);
            Assert.AreEqual(
                3,
                callCount,
                "Re-enabling the token should rebind handlers to the new bus."
            );

            yield break;
        }

        [UnityTest]
        public IEnumerator MessageAwareComponentConfigureMessageBusAppliesOverride()
        {
            MessageBus customBus = new();
            GameObject go = new(
                nameof(MessageAwareComponentConfigureMessageBusAppliesOverride),
                typeof(MessagingComponent),
                typeof(BusAwareComponent)
            );
            _spawned.Add(go);

            BusAwareComponent component = go.GetComponent<BusAwareComponent>();
            component.ConfigureMessageBus(customBus, MessageBusRebindMode.RebindActive);

            yield return null;

            SimpleUntargetedMessage message = new();
            message.EmitUntargeted(customBus);
            Assert.AreEqual(
                1,
                component.Received,
                "MessageAwareComponent should route through the configured bus."
            );

            message.EmitUntargeted();
            Assert.AreEqual(
                1,
                component.Received,
                "Global bus should no longer deliver to the component after override."
            );
        }

        [UnityTest]
        public IEnumerator MessageAwareComponentConfigureMessageBusPreserveDefersUntilReenable()
        {
            MessageBus initialBus = new();
            MessageBus newBus = new();
            GameObject go = new(
                nameof(MessageAwareComponentConfigureMessageBusPreserveDefersUntilReenable),
                typeof(MessagingComponent),
                typeof(BusAwareComponent)
            );
            _spawned.Add(go);

            BusAwareComponent component = go.GetComponent<BusAwareComponent>();
            component.ConfigureMessageBus(initialBus, MessageBusRebindMode.RebindActive);

            yield return null;

            SimpleUntargetedMessage message = new();
            message.EmitUntargeted(initialBus);
            Assert.AreEqual(
                1,
                component.Received,
                "Component should receive messages emitted via the initial bus."
            );

            component.ConfigureMessageBus(newBus, MessageBusRebindMode.PreserveRegistrations);

            message.EmitUntargeted(newBus);
            Assert.AreEqual(
                1,
                component.Received,
                "Preserve mode should not rebind active handlers immediately."
            );

            message.EmitUntargeted(initialBus);
            Assert.AreEqual(
                2,
                component.Received,
                "Existing registrations should continue to observe the original bus."
            );

            component.Token.Disable();
            component.Token.Enable();

            message.EmitUntargeted(newBus);
            Assert.AreEqual(
                3,
                component.Received,
                "Re-enabling the token should rebind handlers to the new bus."
            );
        }

        private sealed class BusAwareComponent : MessageAwareComponent
        {
            internal int Received { get; private set; }

            protected override bool RegisterForStringMessages => false;

            protected override void RegisterMessageHandlers()
            {
                base.RegisterMessageHandlers();
                _ = Token.RegisterUntargeted<SimpleUntargetedMessage>(_ => ++Received);
            }
        }
    }
}

#endif
