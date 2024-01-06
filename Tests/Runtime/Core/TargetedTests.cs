namespace DxMessaging.Tests.Runtime.Core
{
    using DxMessaging.Core;
    using Scripts.Components;
    using Scripts.Messages;
    using System.Collections;
    using DxMessaging.Core.Extensions;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;

    public sealed class TargetedTests : MessagingTestBase
    {
        [UnityTest]
        public IEnumerator SimpleGameObjectTargetedNormal()
        {
            GameObject test1 = new(nameof(SimpleGameObjectTargetedNormal) + "1", typeof(EmptyMessageAwareComponent));
            _spawned.Add(test1);
            GameObject test2 = new(nameof(SimpleGameObjectTargetedNormal) + "2", typeof(EmptyMessageAwareComponent));
            _spawned.Add(test2);
            EmptyMessageAwareComponent component1 = test1.GetComponent<EmptyMessageAwareComponent>();
            EmptyMessageAwareComponent component2 = test2.GetComponent<EmptyMessageAwareComponent>();

            int test1TargetedCount = 0;
            int test2TargetedCount = 0;

            MessageRegistrationToken token1 = GetToken(component1);
            MessageRegistrationToken token2 = GetToken(component2);

            _ = token1.RegisterGameObjectTargeted<SimpleTargetedMessage>(test1, _ => ++test1TargetedCount);
            _ = token2.RegisterGameObjectTargeted<SimpleTargetedMessage>(test2, _ => ++test2TargetedCount);

            SimpleTargetedMessage message = new();
            message.EmitGameObjectTargeted(test1);
            Assert.AreEqual(1, test1TargetedCount);
            Assert.AreEqual(0, test2TargetedCount);

            message.EmitGameObjectTargeted(test2);
            Assert.AreEqual(1, test1TargetedCount);
            Assert.AreEqual(1, test2TargetedCount);

            GameObject test3 = new(nameof(SimpleGameObjectTargetedNormal) + "3");
            _spawned.Add(test3);
            message.EmitGameObjectTargeted(test3);
            Assert.AreEqual(1, test1TargetedCount);
            Assert.AreEqual(1, test2TargetedCount);

            _ = test3.AddComponent<EmptyMessageAwareComponent>();
            message.EmitGameObjectTargeted(test3);
            Assert.AreEqual(1, test1TargetedCount);
            Assert.AreEqual(1, test2TargetedCount);

            for (int i = 0; i < 100; ++i)
            {
                message.EmitGameObjectTargeted(test1);
                Assert.AreEqual(2 + i, test1TargetedCount);
                Assert.AreEqual(1 + i, test2TargetedCount);

                message.EmitGameObjectTargeted(test2);
                Assert.AreEqual(2 + i, test1TargetedCount);
                Assert.AreEqual(2 + i, test2TargetedCount);
            }

            yield break;
        }

        [UnityTest]
        public IEnumerator SimpleGameObjectTargetedNoCopy()
        {
            GameObject test1 = new(nameof(SimpleGameObjectTargetedNoCopy) + "1", typeof(EmptyMessageAwareComponent));
            _spawned.Add(test1);
            GameObject test2 = new(nameof(SimpleGameObjectTargetedNoCopy) + "2", typeof(EmptyMessageAwareComponent));
            _spawned.Add(test2);
            EmptyMessageAwareComponent component1 = test1.GetComponent<EmptyMessageAwareComponent>();
            EmptyMessageAwareComponent component2 = test2.GetComponent<EmptyMessageAwareComponent>();

            int test1TargetedCount = 0;
            void Test1Receive(ref SimpleTargetedMessage message)
            {
                ++test1TargetedCount;
            }

            int test2TargetedCount = 0;
            void Test2Receive(ref SimpleTargetedMessage message)
            {
                ++test2TargetedCount;
            }

            MessageRegistrationToken token1 = GetToken(component1);
            MessageRegistrationToken token2 = GetToken(component2);

            _ = token1.RegisterGameObjectTargeted<SimpleTargetedMessage>(test1, Test1Receive);
            _ = token2.RegisterGameObjectTargeted<SimpleTargetedMessage>(test2, Test2Receive);

            SimpleTargetedMessage message = new();
            message.EmitGameObjectTargeted(test1);
            Assert.AreEqual(1, test1TargetedCount);
            Assert.AreEqual(0, test2TargetedCount);

            message.EmitGameObjectTargeted(test2);
            Assert.AreEqual(1, test1TargetedCount);
            Assert.AreEqual(1, test2TargetedCount);

            GameObject test3 = new(nameof(SimpleGameObjectTargetedNoCopy) + "3");
            message.EmitGameObjectTargeted(test3);
            Assert.AreEqual(1, test1TargetedCount);
            Assert.AreEqual(1, test2TargetedCount);

            _ = test3.AddComponent<EmptyMessageAwareComponent>();
            _spawned.Add(test3);
            message.EmitGameObjectTargeted(test3);
            Assert.AreEqual(1, test1TargetedCount);
            Assert.AreEqual(1, test2TargetedCount);

            for (int i = 0; i < 100; ++i)
            {
                message.EmitGameObjectTargeted(test1);
                Assert.AreEqual(2 + i, test1TargetedCount);
                Assert.AreEqual(1 + i, test2TargetedCount);

                message.EmitGameObjectTargeted(test2);
                Assert.AreEqual(2 + i, test1TargetedCount);
                Assert.AreEqual(2 + i, test2TargetedCount);
            }
            yield break;
        }
    }
}
