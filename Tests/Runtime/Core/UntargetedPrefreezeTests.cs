namespace DxMessaging.Tests.Runtime.Core
{
    using DxMessaging.Core;
    using DxMessaging.Core.MessageBus;
    using DxMessaging.Tests.Runtime.Scripts.Messages;
    using NUnit.Framework;

    public sealed class UntargetedPrefreezeTests
    {
        [Test]
        public void PrefreezeRunsOncePerEmission()
        {
            MessageHandler handler = new(new InstanceId(123)) { active = true };
            MessageBus messageBus = new();
            MessageRegistrationToken token = MessageRegistrationToken.Create(handler, messageBus);

            int postProcessCount = 0;
            _ = token.RegisterUntargeted((ref SimpleUntargetedMessage _) => { });
            _ = token.RegisterUntargetedPostProcessor(
                (ref SimpleUntargetedMessage _) => postProcessCount++,
                priority: 0
            );

            token.Enable();

            SimpleUntargetedMessage message = new();
            messageBus.UntargetedBroadcast(ref message);
            Assert.AreEqual(1, postProcessCount);
            Assert.AreEqual(
                1,
                handler.GetUntargetedPostProcessingPrefreezeCount<SimpleUntargetedMessage>(
                    messageBus,
                    priority: 0
                )
            );

            messageBus.UntargetedBroadcast(ref message);
            Assert.AreEqual(2, postProcessCount);
            Assert.AreEqual(
                2,
                handler.GetUntargetedPostProcessingPrefreezeCount<SimpleUntargetedMessage>(
                    messageBus,
                    priority: 0
                )
            );

            token.Disable();
        }
    }
}
