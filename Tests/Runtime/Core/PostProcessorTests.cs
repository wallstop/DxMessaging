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

            yield break;
        }
    }
}
