namespace DxMessaging.Tests.Runtime.Core
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using DxMessaging.Core;
    using DxMessaging.Core.Extensions;
    using NUnit.Framework;
    using Scripts.Components;
    using Scripts.Messages;
    using UnityEngine;
    using UnityEngine.TestTools;

    public sealed class RegistrationTests : MessagingTestBase
    {
        [UnityTest]
        public IEnumerator UntargetedNormal()
        {
            int count = 0;
            void Handle(SimpleUntargetedMessage message)
            {
                ++count;
            }

            SimpleUntargetedMessage message = new();
            RunRegistrationTest(
                token => token.RegisterUntargeted<SimpleUntargetedMessage>(Handle), i =>
                {
                    Assert.AreEqual(i, count);
                    message.EmitUntargeted();
                }, i =>
                {
                    Assert.AreEqual(i, count);
                    message.EmitUntargeted();
                    Assert.AreEqual(i + 1, count);
                }, () =>
                {
                    message.EmitUntargeted();
                    Assert.AreEqual(0, count);
                }, () =>
                {
                    count = 0;
                });
            yield break;
        }

        [UnityTest]
        public IEnumerator UntargetedNoCopy()
        {
            int count = 0;
            void Handle(ref SimpleUntargetedMessage message)
            {
                ++count;
            }

            SimpleUntargetedMessage message = new();
            RunRegistrationTest(
                token => token.RegisterUntargeted<SimpleUntargetedMessage>(Handle), i =>
                {
                    Assert.AreEqual(i, count);
                    message.EmitUntargeted();
                }, i =>
                {
                    Assert.AreEqual(i, count);
                    message.EmitUntargeted();
                    Assert.AreEqual(i + 1, count);
                }, () =>
                {
                    message.EmitUntargeted();
                    Assert.AreEqual(0, count);
                }, () =>
                {
                    count = 0;
                });
            yield break;
        }

        [UnityTest]
        public IEnumerator UntargetedPostProcessor()
        {
            int count = 0;
            void Handle(ref SimpleUntargetedMessage message)
            {
                ++count;
            }

            SimpleUntargetedMessage message = new();
            RunRegistrationTest(
                token => token.RegisterUntargetedPostProcessor<SimpleUntargetedMessage>(Handle), i =>
                {
                    Assert.AreEqual(i, count);
                    message.EmitUntargeted();
                }, i =>
                {
                    Assert.AreEqual(i, count);
                    message.EmitUntargeted();
                    Assert.AreEqual(i + 1, count);
                }, () =>
                {
                    message.EmitUntargeted();
                    Assert.AreEqual(0, count);
                }, () =>
                {
                    count = 0;
                });
            yield break;
        }

        [UnityTest]
        public IEnumerator UntargetedInterceptor()
        {
            int count = 0;
            bool Intercept(ref SimpleUntargetedMessage message)
            {
                ++count;
                return true;
            }

            SimpleUntargetedMessage message = new();
            RunRegistrationTest(
                token => token.RegisterUntargetedInterceptor<SimpleUntargetedMessage>(Intercept), i =>
                {
                    Assert.AreEqual(i, count);
                    message.EmitUntargeted();
                }, i =>
                {
                    Assert.AreEqual(i, count);
                    message.EmitUntargeted();
                    Assert.AreEqual(i + 1, count);
                }, () =>
                {
                    message.EmitUntargeted();
                    Assert.AreEqual(0, count);
                }, () =>
                {
                    count = 0;
                });
            yield break;
        }

        private void RunRegistrationTest(Func<MessageRegistrationToken, MessageRegistrationHandle> registration, Action<int> normalAssert, Action<int> removalAssert, Action finalAssert, Action reset)
        {
            GameObject test = new(nameof(UntargetedNormal), typeof(EmptyMessageAwareComponent));
            _spawned.Add(test);
            EmptyMessageAwareComponent component = test.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);

            HashSet<MessageRegistrationHandle> handles = new(_numRegistrations);

            for (int i = 0; i < _numRegistrations; ++i)
            {
                MessageRegistrationHandle handle = registration(token);
                bool neverSeen = handles.Add(handle);
                Assert.IsTrue(neverSeen, "Handle {0} at count {1} was a duplicate.", handle, neverSeen);
            }

            for (int i = 0; i < 100; ++i)
            {
                normalAssert(i);
            }

            reset();
            int expected = 0;
            foreach (MessageRegistrationHandle handle in handles.OrderBy(_ => _random.Next()).ToList())
            {
                removalAssert(expected++);
                token.RemoveRegistration(handle);
            }

            reset();
            for (int i = 0; i < 100; ++i)
            {
                finalAssert();
            }
        }
    }
}
