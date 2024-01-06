namespace DxMessaging.Tests.Runtime.Core
{
    using System.Collections;
    using DxMessaging.Core;
    using DxMessaging.Core.Extensions;
    using NUnit.Framework;
    using Scripts.Components;
    using Scripts.Messages;
    using UnityEngine;
    using UnityEngine.TestTools;

    public sealed class UntargetedTests : MessagingTestBase
    {
        [UnityTest]
        public IEnumerator SimpleNormal()
        {
            GameObject test1 = new(nameof(SimpleNormal) + "1", typeof(EmptyMessageAwareComponent));
            _spawned.Add(test1);
            GameObject test2 = new(nameof(SimpleNormal) + "2", typeof(EmptyMessageAwareComponent));
            _spawned.Add(test2);

            EmptyMessageAwareComponent component1 = test1.GetComponent<EmptyMessageAwareComponent>();
            EmptyMessageAwareComponent component2 = test2.GetComponent<EmptyMessageAwareComponent>();

            int count1 = 0;
            MessageRegistrationToken token1 = GetToken(component1);
            _ = token1.RegisterUntargeted<SimpleUntargetedMessage>(_ => ++count1);
            int count2 = 0;
            MessageRegistrationToken token2 = GetToken(component2);
            _ = token2.RegisterUntargeted<SimpleUntargetedMessage>(_ => ++count2);
            SimpleUntargetedMessage message = new();
            for (int i = 0; i < 100; ++i)
            {
                Assert.AreEqual(i, count1);
                Assert.AreEqual(i, count2);
                message.EmitUntargeted();
            }

            yield break;
        }

        [UnityTest]
        public IEnumerator SimpleNoCopy()
        {
            GameObject test1 = new(nameof(SimpleNormal) + "1", typeof(EmptyMessageAwareComponent));
            _spawned.Add(test1);
            GameObject test2 = new(nameof(SimpleNormal) + "2", typeof(EmptyMessageAwareComponent));
            _spawned.Add(test2);

            EmptyMessageAwareComponent component1 = test1.GetComponent<EmptyMessageAwareComponent>();
            EmptyMessageAwareComponent component2 = test2.GetComponent<EmptyMessageAwareComponent>();

            int count1 = 0;
            void Receive1(ref SimpleUntargetedMessage message)
            {
                ++count1;
            }
            MessageRegistrationToken token1 = GetToken(component1);
            _ = token1.RegisterUntargeted<SimpleUntargetedMessage>(Receive1);

            int count2 = 0;
            void Receive2(ref SimpleUntargetedMessage message)
            {
                ++count2;
            }
            MessageRegistrationToken token2 = GetToken(component2);
            _ = token2.RegisterUntargeted<SimpleUntargetedMessage>(Receive2);

            SimpleUntargetedMessage message = new();
            for (int i = 0; i < 100; ++i)
            {
                Assert.AreEqual(i, count1);
                Assert.AreEqual(i, count2);
                message.EmitUntargeted();
            }

            yield break;
        }

        [UnityTest]
        public IEnumerator SimpleDualMode()
        {
            GameObject test1 = new(nameof(SimpleNormal) + "1", typeof(EmptyMessageAwareComponent));
            _spawned.Add(test1);
            GameObject test2 = new(nameof(SimpleNormal) + "2", typeof(EmptyMessageAwareComponent));
            _spawned.Add(test2);

            EmptyMessageAwareComponent component1 = test1.GetComponent<EmptyMessageAwareComponent>();
            EmptyMessageAwareComponent component2 = test2.GetComponent<EmptyMessageAwareComponent>();

            int count1 = 0;
            void Receive1(ref SimpleUntargetedMessage message)
            {
                ++count1;
            }
            MessageRegistrationToken token1 = GetToken(component1);
            _ = token1.RegisterUntargeted<SimpleUntargetedMessage>(Receive1);
            _ = token1.RegisterUntargeted<SimpleUntargetedMessage>(_ => ++count1);

            int count2 = 0;
            void Receive2(ref SimpleUntargetedMessage message)
            {
                ++count2;
            }
            MessageRegistrationToken token2 = GetToken(component2);
            _ = token2.RegisterUntargeted<SimpleUntargetedMessage>(Receive2);

            SimpleUntargetedMessage message = new();
            for (int i = 0; i < 100; ++i)
            {
                Assert.AreEqual(i * 2, count1);
                Assert.AreEqual(i, count2);
                message.EmitUntargeted();
            }

            yield break;
        }
    }
}
