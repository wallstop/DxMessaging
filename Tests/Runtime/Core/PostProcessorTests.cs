namespace DxMessaging.Tests.Runtime.Core
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using DxMessaging.Core;
    using DxMessaging.Core.Extensions;
    using Scripts.Messages;
    using NUnit.Framework;
    using Scripts.Components;
    using UnityEngine;
    using UnityEngine.TestTools;

    public sealed class PostProcessorTests : TestBase
    {
        [UnityTest]
        public IEnumerator Untargeted()
        {
            GameObject test = new(nameof(Untargeted), typeof(EmptyMessageAwareComponent));
            _spawned.Add(test);

            EmptyMessageAwareComponent component = test.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);

            int lastSeenCount = 0;
            int count = 0;

            int finalCount = 0;
            void ResetCount()
            {
                lastSeenCount = 0;
                count = 0;
                finalCount = 0;
            }

            Action assertion;

            void PostProcessor(ref SimpleUntargetedMessage message)
            {
                assertion.Invoke();
            }

            assertion = () =>
            {
                Assert.AreEqual(lastSeenCount, count++);
                lastSeenCount = count;
            };

            SimpleUntargetedMessage message = new();
            Run(() => new[]{token.RegisterUntargetedPostProcessor<SimpleUntargetedMessage>(PostProcessor)},
                () => message.EmitUntargeted(),
                () =>
                {
                    Assert.AreEqual(lastSeenCount, count);
                    finalCount = count;
                },
                () =>
                {
                    Assert.AreEqual(finalCount, lastSeenCount);
                    Assert.AreEqual(finalCount, count);
                }, 
                token);

            ResetCount();
            assertion = () =>
            {
                Assert.AreEqual(lastSeenCount + 1, count);
                lastSeenCount = count;
            };
            Run(() => new[] { token.RegisterUntargetedPostProcessor<SimpleUntargetedMessage>(PostProcessor), token.RegisterUntargeted<SimpleUntargetedMessage>(_ => ++count)},
                () => message.EmitUntargeted(),
                () =>
                {
                    Assert.AreEqual(lastSeenCount, count);
                    finalCount = count;
                },
                () =>
                {
                    Assert.AreEqual(finalCount, lastSeenCount);
                    Assert.AreEqual(finalCount, count);
                },
                token,
                synchronizeDeregistrations: true);

            ResetCount();
            assertion = () =>
            {
                ++count;
            };
            Run(() => new[] { token.RegisterUntargetedPostProcessor<SimpleUntargetedMessage>(PostProcessor), token.RegisterUntargetedPostProcessor<SimpleUntargetedMessage>(PostProcessor)},
                () => message.EmitUntargeted(),
                () => Assert.AreEqual(++lastSeenCount, count),
                () => { Assert.AreEqual(lastSeenCount, count);},
                token,
                synchronizeDeregistrations: true);

            yield break;
        }

        [UnityTest]
        public IEnumerator GameObjectTargeted()
        {
            GameObject test = new(nameof(Untargeted), typeof(EmptyMessageAwareComponent));
            _spawned.Add(test);

            EmptyMessageAwareComponent component = test.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);

            int lastSeenCount = 0;
            int count = 0;

            int finalCount = 0;
            void ResetCount()
            {
                lastSeenCount = 0;
                count = 0;
                finalCount = 0;
            }

            Action assertion;

            void PostProcessor(ref SimpleTargetedMessage message)
            {
                assertion.Invoke();
            }

            assertion = () =>
            {
                Assert.AreEqual(lastSeenCount, count++);
                lastSeenCount = count;
            };

            SimpleTargetedMessage message = new();
            Run(() => new[] { token.RegisterGameObjectTargetedPostProcessor<SimpleTargetedMessage>(test, PostProcessor) },
                () => message.EmitGameObjectTargeted(test),
                () =>
                {
                    Assert.AreEqual(lastSeenCount, count);
                    finalCount = count;
                },
                () =>
                {
                    Assert.AreEqual(finalCount, lastSeenCount);
                    Assert.AreEqual(finalCount, count);
                },
                token);

            ResetCount();
            assertion = () =>
            {
                Assert.AreEqual(lastSeenCount + 1, count);
                lastSeenCount = count;
            };
            Run(() => new[] { token.RegisterGameObjectTargetedPostProcessor<SimpleTargetedMessage>(test, PostProcessor), token.RegisterGameObjectTargeted<SimpleTargetedMessage>(test, _ => ++count) },
                () => message.EmitGameObjectTargeted(test),
                () =>
                {
                    Assert.AreEqual(lastSeenCount, count);
                    finalCount = count;
                },
                () =>
                {
                    Assert.AreEqual(finalCount, lastSeenCount);
                    Assert.AreEqual(finalCount, count);
                },
                token,
                synchronizeDeregistrations: true);

            ResetCount();
            assertion = () =>
            {
                ++count;
            };
            Run(() => new[] { token.RegisterGameObjectTargetedPostProcessor<SimpleTargetedMessage>(test, PostProcessor), token.RegisterGameObjectTargetedPostProcessor<SimpleTargetedMessage>(test, PostProcessor) },
                () => message.EmitGameObjectTargeted(test),
                () => Assert.AreEqual(++lastSeenCount, count),
                () => { Assert.AreEqual(lastSeenCount, count); },
                token,
                synchronizeDeregistrations: true);

            ResetCount();
            assertion = () =>
            {
                Assert.Fail("Should never be called, we're emitting the wrong thing");
            };
            Run(() => new[] { token.RegisterGameObjectTargetedPostProcessor<SimpleTargetedMessage>(test, PostProcessor) },
                () => message.EmitComponentTargeted(component),
                () =>
                {
                    Assert.AreEqual(0, count);
                    Assert.AreEqual(0, lastSeenCount);
                },
                () =>
                {
                    Assert.AreEqual(0, count);
                    Assert.AreEqual(0, lastSeenCount);
                },
                token);

            yield break;
        }

        [UnityTest]
        public IEnumerator ComponentTargeted()
        {
            GameObject test = new(nameof(Untargeted), typeof(EmptyMessageAwareComponent));
            _spawned.Add(test);

            EmptyMessageAwareComponent component = test.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);

            int lastSeenCount = 0;
            int count = 0;

            int finalCount = 0;
            void ResetCount()
            {
                lastSeenCount = 0;
                count = 0;
                finalCount = 0;
            }

            Action assertion;

            void PostProcessor(ref SimpleTargetedMessage message)
            {
                assertion.Invoke();
            }

            assertion = () =>
            {
                Assert.AreEqual(lastSeenCount, count++);
                lastSeenCount = count;
            };

            SimpleTargetedMessage message = new();
            Run(() => new[] { token.RegisterComponentTargetedPostProcessor<SimpleTargetedMessage>(component, PostProcessor) },
                () => message.EmitComponentTargeted(component),
                () =>
                {
                    Assert.AreEqual(lastSeenCount, count);
                    finalCount = count;
                },
                () =>
                {
                    Assert.AreEqual(finalCount, lastSeenCount);
                    Assert.AreEqual(finalCount, count);
                },
                token);

            ResetCount();
            assertion = () =>
            {
                Assert.AreEqual(lastSeenCount + 1, count);
                lastSeenCount = count;
            };
            Run(() => new[] { token.RegisterComponentTargetedPostProcessor<SimpleTargetedMessage>(component, PostProcessor), token.RegisterComponentTargeted<SimpleTargetedMessage>(component, _ => ++count) },
                () => message.EmitComponentTargeted(component),
                () =>
                {
                    Assert.AreEqual(lastSeenCount, count);
                    finalCount = count;
                },
                () =>
                {
                    Assert.AreEqual(finalCount, lastSeenCount);
                    Assert.AreEqual(finalCount, count);
                },
                token,
                synchronizeDeregistrations: true);

            ResetCount();
            assertion = () =>
            {
                ++count;
            };
            Run(() => new[] { token.RegisterComponentTargetedPostProcessor<SimpleTargetedMessage>(component, PostProcessor), token.RegisterComponentTargetedPostProcessor<SimpleTargetedMessage>(component, PostProcessor) },
                () => message.EmitComponentTargeted(component),
                () => Assert.AreEqual(++lastSeenCount, count),
                () => { Assert.AreEqual(lastSeenCount, count); },
                token,
                synchronizeDeregistrations: true);

            ResetCount();
            assertion = () =>
            {
                Assert.Fail("Should never be called, we're emitting the wrong thing");
            };
            Run(() => new[] { token.RegisterComponentTargetedPostProcessor<SimpleTargetedMessage>(component, PostProcessor)},
                () => message.EmitGameObjectTargeted(test),
                () =>
                {
                    Assert.AreEqual(0, count);
                    Assert.AreEqual(0, lastSeenCount);
                },
                () =>
                {
                    Assert.AreEqual(0, count);
                    Assert.AreEqual(0, lastSeenCount);
                },
                token);

            yield break;
        }

        [UnityTest]
        public IEnumerator GameObjectBroadcast()
        {
            GameObject test = new(nameof(Untargeted), typeof(EmptyMessageAwareComponent));
            _spawned.Add(test);

            EmptyMessageAwareComponent component = test.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);

            int lastSeenCount = 0;
            int count = 0;

            int finalCount = 0;
            void ResetCount()
            {
                lastSeenCount = 0;
                count = 0;
                finalCount = 0;
            }

            Action assertion;

            void PostProcessor(ref SimpleBroadcastMessage message)
            {
                assertion.Invoke();
            }

            assertion = () =>
            {
                Assert.AreEqual(lastSeenCount, count++);
                lastSeenCount = count;
            };

            SimpleBroadcastMessage message = new();
            Run(() => new[] { token.RegisterGameObjectBroadcastPostProcessor<SimpleBroadcastMessage>(test, PostProcessor) },
                () => message.EmitGameObjectBroadcast(test),
                () =>
                {
                    Assert.AreEqual(lastSeenCount, count);
                    finalCount = count;
                },
                () =>
                {
                    Assert.AreEqual(finalCount, lastSeenCount);
                    Assert.AreEqual(finalCount, count);
                },
                token);

            ResetCount();
            assertion = () =>
            {
                Assert.AreEqual(lastSeenCount + 1, count);
                lastSeenCount = count;
            };
            Run(() => new[] { token.RegisterGameObjectBroadcastPostProcessor<SimpleBroadcastMessage>(test, PostProcessor), token.RegisterGameObjectBroadcast<SimpleBroadcastMessage>(test, _ => ++count) },
                () => message.EmitGameObjectBroadcast(test),
                () =>
                {
                    Assert.AreEqual(lastSeenCount, count);
                    finalCount = count;
                },
                () =>
                {
                    Assert.AreEqual(finalCount, lastSeenCount);
                    Assert.AreEqual(finalCount, count);
                },
                token,
                synchronizeDeregistrations: true);

            ResetCount();
            assertion = () =>
            {
                ++count;
            };
            Run(() => new[] { token.RegisterGameObjectBroadcastPostProcessor<SimpleBroadcastMessage>(test, PostProcessor), token.RegisterGameObjectBroadcast<SimpleBroadcastMessage>(test, PostProcessor) },
                () => message.EmitGameObjectBroadcast(test),
                () => Assert.AreEqual(++lastSeenCount, count),
                () => { Assert.AreEqual(lastSeenCount, count); },
                token,
                synchronizeDeregistrations: true);

            ResetCount();
            assertion = () =>
            {
                Assert.Fail("Should never be called, we're emitting the wrong thing");
            };
            Run(() => new[] { token.RegisterGameObjectBroadcastPostProcessor<SimpleBroadcastMessage>(test, PostProcessor)},
                () => message.EmitComponentBroadcast(component),
                () =>
                {
                    Assert.AreEqual(0, count);
                    Assert.AreEqual(0, lastSeenCount);
                },
                () =>
                {
                    Assert.AreEqual(0, count);
                    Assert.AreEqual(0, lastSeenCount);
                },
                token);

            yield break;
        }

        [UnityTest]
        public IEnumerator ComponentBroadcast()
        {
            GameObject test = new(nameof(Untargeted), typeof(EmptyMessageAwareComponent));
            _spawned.Add(test);

            EmptyMessageAwareComponent component = test.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);

            int lastSeenCount = 0;
            int count = 0;

            int finalCount = 0;
            void ResetCount()
            {
                lastSeenCount = 0;
                count = 0;
                finalCount = 0;
            }

            Action assertion;

            void PostProcessor(ref SimpleBroadcastMessage message)
            {
                assertion.Invoke();
            }

            assertion = () =>
            {
                Assert.AreEqual(lastSeenCount, count++);
                lastSeenCount = count;
            };

            SimpleBroadcastMessage message = new();
            Run(() => new[] { token.RegisterComponentBroadcastPostProcessor<SimpleBroadcastMessage>(component, PostProcessor) },
                () => message.EmitComponentBroadcast(component),
                () =>
                {
                    Assert.AreEqual(lastSeenCount, count);
                    finalCount = count;
                },
                () =>
                {
                    Assert.AreEqual(finalCount, lastSeenCount);
                    Assert.AreEqual(finalCount, count);
                },
                token);

            ResetCount();
            assertion = () =>
            {
                Assert.AreEqual(lastSeenCount + 1, count);
                lastSeenCount = count;
            };
            Run(() => new[] { token.RegisterComponentBroadcastPostProcessor<SimpleBroadcastMessage>(component, PostProcessor), token.RegisterComponentBroadcast<SimpleBroadcastMessage>(component, _ => ++count) },
                () => message.EmitComponentBroadcast(component),
                () =>
                {
                    Assert.AreEqual(lastSeenCount, count);
                    finalCount = count;
                },
                () =>
                {
                    Assert.AreEqual(finalCount, lastSeenCount);
                    Assert.AreEqual(finalCount, count);
                },
                token,
                synchronizeDeregistrations: true);

            ResetCount();
            assertion = () =>
            {
                ++count;
            };
            Run(() => new[] { token.RegisterComponentBroadcastPostProcessor<SimpleBroadcastMessage>(component, PostProcessor), token.RegisterComponentBroadcast<SimpleBroadcastMessage>(component, PostProcessor) },
                () => message.EmitComponentBroadcast(component),
                () => Assert.AreEqual(++lastSeenCount, count),
                () => { Assert.AreEqual(lastSeenCount, count); },
                token,
                synchronizeDeregistrations: true);

            ResetCount();
            assertion = () =>
            {
                Assert.Fail("Should never be called, we're emitting the wrong thing");
            };
            Run(() => new[] { token.RegisterGameObjectBroadcastPostProcessor<SimpleBroadcastMessage>(test, PostProcessor) },
                () => message.EmitGameObjectBroadcast(test),
                () =>
                {
                    Assert.AreEqual(0, count);
                    Assert.AreEqual(0, lastSeenCount);
                },
                () =>
                {
                    Assert.AreEqual(0, count);
                    Assert.AreEqual(0, lastSeenCount);
                },
                token);

            yield break;
        }
    }
}
