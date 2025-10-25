namespace DxMessaging.Tests.Runtime.Core
{
    using System.Collections;
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
        [Test]
        public void HandlerUsesInjectedDefaultBusWhenTokenOmitsBus()
        {
            MessageBus customBus = new();
            MessageHandler handler = new(new InstanceId(42), customBus) { active = true };
            MessageRegistrationToken token = MessageRegistrationToken.Create(handler);

            int callCount = 0;
            MessageRegistrationHandle handle = token.RegisterUntargeted<SimpleUntargetedMessage>(
                _ => ++callCount
            );

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

            token.RemoveRegistration(handle);
            token.Disable();
            handler.active = false;
        }

        [Test]
        public void HandlerUsesInjectedDefaultBusForTargetedMessages()
        {
            MessageBus customBus = new();
            MessageHandler handler = new(new InstanceId(99), customBus) { active = true };
            MessageRegistrationToken token = MessageRegistrationToken.Create(handler);

            InstanceId target = new InstanceId(1234);
            int callCount = 0;
            MessageRegistrationHandle handle = token.RegisterTargeted<SimpleTargetedMessage>(
                target,
                _ => ++callCount
            );

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

            token.RemoveRegistration(handle);
            token.Disable();
            handler.active = false;
        }

        [Test]
        public void HandlerUsesInjectedDefaultBusForBroadcastMessages()
        {
            MessageBus customBus = new();
            MessageHandler handler = new(new InstanceId(1337), customBus) { active = true };
            MessageRegistrationToken token = MessageRegistrationToken.Create(handler);

            int callCount = 0;
            MessageRegistrationHandle handle =
                token.RegisterBroadcastWithoutSource<SimpleBroadcastMessage>(
                    (ref InstanceId _, ref SimpleBroadcastMessage _) => ++callCount
                );

            token.Enable();

            SimpleBroadcastMessage message = new();
            InstanceId source = new InstanceId(777);
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

            token.RemoveRegistration(handle);
            token.Disable();
            handler.active = false;
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
            messagingComponent.Configure(customBus);

            ManualListenerComponent listener = go.GetComponent<ManualListenerComponent>();
            MessageRegistrationToken token = listener.RequestToken(messagingComponent);

            int callCount = 0;
            MessageRegistrationHandle handle = token.RegisterUntargeted<SimpleUntargetedMessage>(
                _ => ++callCount
            );
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

            token.RemoveRegistration(handle);
            token.Disable();
            yield break;
        }
    }
}
