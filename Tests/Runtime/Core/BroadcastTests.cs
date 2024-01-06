namespace DxMessaging.Tests.Runtime.Core
{
    using System.Collections;
    using System.Collections.Generic;
    using DxMessaging.Core;
    using DxMessaging.Core.Extensions;
    using NUnit.Framework;
    using Scripts.Components;
    using Scripts.Messages;
    using UnityEngine;
    using UnityEngine.TestTools;

    public sealed class BroadcastTests : TestBase
    {
        [UnityTest]
        public IEnumerator SimpleGameObjectBroadcastNormal()
        {
            GameObject test1 = new(nameof(SimpleGameObjectBroadcastNormal), typeof(EmptyMessageAwareComponent));
            _spawned.Add(test1);
            GameObject test2 = new(nameof(SimpleGameObjectBroadcastNormal), typeof(EmptyMessageAwareComponent));
            _spawned.Add(test2);
            EmptyMessageAwareComponent component1 = test1.GetComponent<EmptyMessageAwareComponent>();
            EmptyMessageAwareComponent component2 = test2.GetComponent<EmptyMessageAwareComponent>();

            int test1ReceiveCount = 0;
            int test2ReceiveCount = 0;

            MessageRegistrationToken token1 = GetToken(component1);
            MessageRegistrationToken token2 = GetToken(component2);

            HashSet<MessageRegistrationHandle> handles = new();
            var handle = token1.RegisterGameObjectBroadcast<SimpleBroadcastMessage>(test1, _ => ++test1ReceiveCount);
            _ = handles.Add(handle);
            handle = token2.RegisterGameObjectBroadcast<SimpleBroadcastMessage>(test2, _ => ++test2ReceiveCount);
            _ = handles.Add(handle);

            SimpleBroadcastMessage message = new();
            message.EmitGameObjectBroadcast(test1);
            Assert.AreEqual(1, test1ReceiveCount);
            Assert.AreEqual(0, test2ReceiveCount);

            message.EmitGameObjectBroadcast(test2);
            Assert.AreEqual(1, test1ReceiveCount);
            Assert.AreEqual(1, test2ReceiveCount);

            GameObject test3 = new(nameof(SimpleGameObjectBroadcastNormal));
            _spawned.Add(test3);
            message.EmitGameObjectBroadcast(test3);
            Assert.AreEqual(1, test1ReceiveCount);
            Assert.AreEqual(1, test2ReceiveCount);

            _ = test3.AddComponent<EmptyMessageAwareComponent>();
            message.EmitGameObjectBroadcast(test3);
            Assert.AreEqual(1, test1ReceiveCount);
            Assert.AreEqual(1, test2ReceiveCount);
            yield break;
        }

        [UnityTest]
        public IEnumerator SimpleGameObjectBroadcastNoAlloc()
        {
            GameObject test1 = new(nameof(SimpleGameObjectBroadcastNoAlloc), typeof(EmptyMessageAwareComponent));
            _spawned.Add(test1);
            GameObject test2 = new(nameof(SimpleGameObjectBroadcastNoAlloc), typeof(EmptyMessageAwareComponent));
            _spawned.Add(test2);
            EmptyMessageAwareComponent component1 = test1.GetComponent<EmptyMessageAwareComponent>();
            EmptyMessageAwareComponent component2 = test2.GetComponent<EmptyMessageAwareComponent>();

            int test1ReceiveCount = 0;
            void Test1Receive(ref SimpleBroadcastMessage message)
            {
                ++test1ReceiveCount;
            }

            int test2ReceiveCount = 0;
            void Test2Receive(ref SimpleBroadcastMessage message)
            {
                ++test2ReceiveCount;
            }

            MessageRegistrationToken token1 = GetToken(component1);
            MessageRegistrationToken token2 = GetToken(component2);

            HashSet<MessageRegistrationHandle> handles = new();
            var handle = token1.RegisterGameObjectBroadcast<SimpleBroadcastMessage>(test1, Test1Receive);
            _ = handles.Add(handle);
            handle = token2.RegisterGameObjectBroadcast<SimpleBroadcastMessage>(test2, Test2Receive);
            _ = handles.Add(handle);

            SimpleBroadcastMessage message = new();
            message.EmitGameObjectBroadcast(test1);
            Assert.AreEqual(1, test1ReceiveCount);
            Assert.AreEqual(0, test2ReceiveCount);

            message.EmitGameObjectBroadcast(test2);
            Assert.AreEqual(1, test1ReceiveCount);
            Assert.AreEqual(1, test2ReceiveCount);

            GameObject test3 = new(nameof(SimpleGameObjectBroadcastNoAlloc));
            message.EmitGameObjectBroadcast(test3);
            Assert.AreEqual(1, test1ReceiveCount);
            Assert.AreEqual(1, test2ReceiveCount);

            _ = test3.AddComponent<EmptyMessageAwareComponent>();
            _spawned.Add(test3);
            message.EmitGameObjectBroadcast(test3);
            Assert.AreEqual(1, test1ReceiveCount);
            Assert.AreEqual(1, test2ReceiveCount);
            yield break;
        }

        [UnityTest]
        public IEnumerator SimpleGameObjectBroadcastDualMode()
        {
            GameObject test1 = new(nameof(SimpleGameObjectBroadcastNoAlloc), typeof(EmptyMessageAwareComponent));
            _spawned.Add(test1);
            GameObject test2 = new(nameof(SimpleGameObjectBroadcastNoAlloc), typeof(EmptyMessageAwareComponent));
            _spawned.Add(test2);
            EmptyMessageAwareComponent component1 = test1.GetComponent<EmptyMessageAwareComponent>();
            EmptyMessageAwareComponent component2 = test2.GetComponent<EmptyMessageAwareComponent>();

            int test1ReceiveCount = 0;
            void Test1Receive(ref SimpleBroadcastMessage message)
            {
                ++test1ReceiveCount;
            }

            int test2ReceiveCount = 0;
            void Test2Receive(ref SimpleBroadcastMessage message)
            {
                ++test2ReceiveCount;
            }

            MessageRegistrationToken token1 = GetToken(component1);
            MessageRegistrationToken token2 = GetToken(component2);

            HashSet<MessageRegistrationHandle> handles = new();
            var handle = token1.RegisterGameObjectBroadcast<SimpleBroadcastMessage>(test1, Test1Receive);
            _ = handles.Add(handle);
            handle = token1.RegisterGameObjectBroadcast<SimpleBroadcastMessage>(test1, _ => ++test1ReceiveCount);
            handle = token2.RegisterGameObjectBroadcast<SimpleBroadcastMessage>(test2, Test2Receive);
            _ = handles.Add(handle);

            SimpleBroadcastMessage message = new();
            message.EmitGameObjectBroadcast(test1);
            Assert.AreEqual(2, test1ReceiveCount);
            Assert.AreEqual(0, test2ReceiveCount);

            message.EmitGameObjectBroadcast(test2);
            Assert.AreEqual(2, test1ReceiveCount);
            Assert.AreEqual(1, test2ReceiveCount);

            GameObject test3 = new(nameof(SimpleGameObjectBroadcastNoAlloc));
            _spawned.Add(test3);
            message.EmitGameObjectBroadcast(test3);
            Assert.AreEqual(2, test1ReceiveCount);
            Assert.AreEqual(1, test2ReceiveCount);

            _ = test3.AddComponent<EmptyMessageAwareComponent>();
            message.EmitGameObjectBroadcast(test3);
            Assert.AreEqual(2, test1ReceiveCount);
            Assert.AreEqual(1, test2ReceiveCount);
            yield break;
        }
    }
}
