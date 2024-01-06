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
        private GameObject _test;
        private EmptyMessageAwareComponent _component;
        private MessageRegistrationToken _token;

        [UnitySetUp]
        public override IEnumerator UnitySetup()
        {
            IEnumerator baseEnumerator = base.UnitySetup();
            while (baseEnumerator.MoveNext())
            {
                yield return baseEnumerator.Current;
            }

            _test = new(nameof(UntargetedNormal), typeof(EmptyMessageAwareComponent));
            _spawned.Add(_test);
            _component = _test.GetComponent<EmptyMessageAwareComponent>();
            _token = GetToken(_component);
        }

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

        [UnityTest]
        public IEnumerator GameObjectTargetedNormal()
        {
            int count = 0;
            void Handle(SimpleTargetedMessage message)
            {
                ++count;
            }

            SimpleTargetedMessage message = new();
            RunRegistrationTest(
                token => token.RegisterGameObjectTargeted<SimpleTargetedMessage>(_test, Handle), i =>
                {
                    Assert.AreEqual(i, count);
                    message.EmitGameObjectTargeted(_test);
                }, i =>
                {
                    Assert.AreEqual(i, count);
                    message.EmitGameObjectTargeted(_test);
                    Assert.AreEqual(i + 1, count);
                }, () =>
                {
                    message.EmitGameObjectTargeted(_test);
                    message.EmitComponentTargeted(_component);
                    Assert.AreEqual(0, count);
                }, () =>
                {
                    count = 0;
                });
            yield break;
        }

        [UnityTest]
        public IEnumerator GameObjectTargetedNoCopy()
        {
            int count = 0;
            void Handle(ref SimpleTargetedMessage message)
            {
                ++count;
            }

            SimpleTargetedMessage message = new();
            RunRegistrationTest(
                token => token.RegisterGameObjectTargeted<SimpleTargetedMessage>(_test, Handle), i =>
                {
                    Assert.AreEqual(i, count);
                    message.EmitGameObjectTargeted(_test);
                }, i =>
                {
                    Assert.AreEqual(i, count);
                    message.EmitGameObjectTargeted(_test);
                    Assert.AreEqual(i + 1, count);
                }, () =>
                {
                    message.EmitGameObjectTargeted(_test);
                    message.EmitComponentTargeted(_component);
                    Assert.AreEqual(0, count);
                }, () =>
                {
                    count = 0;
                });
            yield break;
        }

        [UnityTest]
        public IEnumerator GameObjectTargetedPostProcessor()
        {
            int count = 0;
            void Handle(ref SimpleTargetedMessage message)
            {
                ++count;
            }

            SimpleTargetedMessage message = new();
            RunRegistrationTest(
                token => token.RegisterGameObjectTargetedPostProcessor<SimpleTargetedMessage>(_test, Handle), i =>
                {
                    Assert.AreEqual(i, count);
                    message.EmitGameObjectTargeted(_test);
                }, i =>
                {
                    Assert.AreEqual(i, count);
                    message.EmitGameObjectTargeted(_test);
                    Assert.AreEqual(i + 1, count);
                }, () =>
                {
                    message.EmitGameObjectTargeted(_test);
                    message.EmitComponentTargeted(_component);
                    Assert.AreEqual(0, count);
                }, () =>
                {
                    count = 0;
                });
            yield break;
        }


        [UnityTest]
        public IEnumerator ComponentTargetedNormal()
        {
            int count = 0;
            void Handle(SimpleTargetedMessage message)
            {
                ++count;
            }

            SimpleTargetedMessage message = new();
            RunRegistrationTest(
                token => token.RegisterComponentTargeted<SimpleTargetedMessage>(_component, Handle), i =>
                {
                    Assert.AreEqual(i, count);
                    message.EmitComponentTargeted(_component);
                }, i =>
                {
                    Assert.AreEqual(i, count);
                    message.EmitComponentTargeted(_component);
                    Assert.AreEqual(i + 1, count);
                }, () =>
                {
                    message.EmitGameObjectTargeted(_test);
                    message.EmitComponentTargeted(_component);
                    Assert.AreEqual(0, count);
                }, () =>
                {
                    count = 0;
                });
            yield break;
        }

        [UnityTest]
        public IEnumerator ComponentTargetedNoCopy()
        {
            int count = 0;
            void Handle(ref SimpleTargetedMessage message)
            {
                ++count;
            }

            SimpleTargetedMessage message = new();
            RunRegistrationTest(
                token => token.RegisterComponentTargeted<SimpleTargetedMessage>(_component, Handle), i =>
                {
                    Assert.AreEqual(i, count);
                    message.EmitComponentTargeted(_component);
                }, i =>
                {
                    Assert.AreEqual(i, count);
                    message.EmitComponentTargeted(_component);
                    Assert.AreEqual(i + 1, count);
                }, () =>
                {
                    message.EmitGameObjectTargeted(_test);
                    message.EmitComponentTargeted(_component);
                    Assert.AreEqual(0, count);
                }, () =>
                {
                    count = 0;
                });
            yield break;
        }

        [UnityTest]
        public IEnumerator ComponentTargetedPostProcessor()
        {
            int count = 0;
            void Handle(ref SimpleTargetedMessage message)
            {
                ++count;
            }

            SimpleTargetedMessage message = new();
            RunRegistrationTest(
                token => token.RegisterComponentTargetedPostProcessor<SimpleTargetedMessage>(_component, Handle), i =>
                {
                    Assert.AreEqual(i, count);
                    message.EmitComponentTargeted(_component);
                }, i =>
                {
                    Assert.AreEqual(i, count);
                    message.EmitComponentTargeted(_component);
                    Assert.AreEqual(i + 1, count);
                }, () =>
                {
                    message.EmitGameObjectTargeted(_test);
                    message.EmitComponentTargeted(_component);
                    Assert.AreEqual(0, count);
                }, () =>
                {
                    count = 0;
                });
            yield break;
        }

        [UnityTest]
        public IEnumerator TargetedInterceptor()
        {
            int count = 0;
            bool Intercept(ref InstanceId target, ref SimpleTargetedMessage message)
            {
                ++count;
                return true;
            }

            SimpleTargetedMessage message = new();
            RunRegistrationTest(
                token => token.RegisterTargetedInterceptor<SimpleTargetedMessage>(Intercept), i =>
                {
                    Assert.AreEqual(i, count);
                    message.EmitGameObjectTargeted(_test);
                }, i =>
                {
                    Assert.AreEqual(i, count);
                    message.EmitComponentTargeted(_component);
                    Assert.AreEqual(i + 1, count);
                }, () =>
                {
                    message.EmitGameObjectTargeted(_test);
                    message.EmitComponentTargeted(_component);
                    Assert.AreEqual(0, count);
                }, () =>
                {
                    count = 0;
                });
            yield break;
        }

        private void RunRegistrationTest(Func<MessageRegistrationToken, MessageRegistrationHandle> registration, Action<int> normalAssert, Action<int> removalAssert, Action finalAssert, Action reset)
        {
            HashSet<MessageRegistrationHandle> handles = new(_numRegistrations);

            for (int i = 0; i < _numRegistrations; ++i)
            {
                MessageRegistrationHandle handle = registration(_token);
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
                _token.RemoveRegistration(handle);
            }

            reset();
            for (int i = 0; i < 100; ++i)
            {
                finalAssert();
            }
        }
    }
}
