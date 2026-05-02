#if UNITY_2021_3_OR_NEWER
namespace DxMessaging.Tests.Runtime.Benchmarks
{
    using System;
    using System.Collections;
    using DxMessaging.Core;
    using DxMessaging.Core.Extensions;
    using DxMessaging.Core.MessageBus;
    using DxMessaging.Core.Messages;
    using NUnit.Framework;
    using Scripts.Components;
    using Scripts.Messages;
    using UnityEngine;
    using UnityEngine.TestTools;

    [Category("Performance")]
    public sealed class BenchmarkHarnessRobustnessTests : BenchmarkTestBase
    {
        [TestCase("untargeted")]
        [TestCase("targeted-game-object")]
        [TestCase("targeted-component")]
        public void RunWithComponentProvidesEnabledTokenForCoreRegistrationShapes(string mode)
        {
            RunWithComponent(
                (component, token) =>
                {
                    Assert.IsNotNull(token, "Benchmark harness should always provide a token.");
                    Assert.IsTrue(
                        token.Enabled,
                        "Benchmark token should be enabled before registration."
                    );

                    int count = 0;
                    switch (mode)
                    {
                        case "untargeted":
                            token.RegisterUntargeted<SimpleUntargetedMessage>(
                                (ref SimpleUntargetedMessage _) => ++count
                            );
                            SimpleUntargetedMessage untargetedMessage = new();
                            untargetedMessage.EmitUntargeted();
                            break;
                        case "targeted-game-object":
                            token.RegisterGameObjectTargeted<SimpleTargetedMessage>(
                                component.gameObject,
                                (ref SimpleTargetedMessage _) => ++count
                            );
                            SimpleTargetedMessage targetedGameObjectMessage = new();
                            targetedGameObjectMessage.EmitGameObjectTargeted(component.gameObject);
                            break;
                        case "targeted-component":
                            token.RegisterComponentTargeted<SimpleTargetedMessage>(
                                component,
                                (ref SimpleTargetedMessage _) => ++count
                            );
                            SimpleTargetedMessage targetedComponentMessage = new();
                            targetedComponentMessage.EmitComponentTargeted(component);
                            break;
                        default:
                            Assert.Fail($"Unhandled benchmark registration mode '{mode}'.");
                            break;
                    }

                    Assert.AreEqual(
                        1,
                        count,
                        $"Expected mode '{mode}' to receive exactly one message."
                    );
                }
            );
        }

        [UnityTest]
        public IEnumerator RunWithComponentUnregistersHandlersBetweenInvocationsSinglePass()
        {
            yield return RunWithComponentUnregistersHandlersBetweenInvocationsCore(1);
        }

        [UnityTest]
        public IEnumerator RunWithComponentUnregistersHandlersBetweenInvocationsTwoPasses()
        {
            yield return RunWithComponentUnregistersHandlersBetweenInvocationsCore(2);
        }

        [UnityTest]
        public IEnumerator RunWithComponentUnregistersHandlersBetweenInvocationsFourPasses()
        {
            yield return RunWithComponentUnregistersHandlersBetweenInvocationsCore(4);
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void RunWithComponentUnregistersHandlersBetweenInvocationsRejectsNonPositiveCounts(
            int invocations
        )
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ValidateInvocationCount(invocations));
        }

        [Test]
        public void RunWithComponentUnregistersHandlersWhenBenchmarkActionThrows()
        {
            int leakedInvocationCount = 0;
            SimpleUntargetedMessage message = new();

            Assert.Throws<InvalidOperationException>(() =>
                RunWithComponent(
                    (_, token) =>
                    {
                        token.RegisterUntargeted<SimpleUntargetedMessage>(
                            (ref SimpleUntargetedMessage _) => ++leakedInvocationCount
                        );
                        throw new InvalidOperationException(
                            "Intentional benchmark action failure."
                        );
                    }
                )
            );

            message.EmitUntargeted();
            Assert.Zero(
                leakedInvocationCount,
                $"RunWithComponent should unregister handlers even when the benchmark action throws. {DescribeMessageBusState(MessageHandler.MessageBus, includeLog: true)}"
            );
            AssertMessageBusCounts(
                expectedUntargeted: 0,
                expectedTargeted: 0,
                expectedBroadcast: 0,
                "after benchmark action exception"
            );
        }

        private IEnumerator RunWithComponentUnregistersHandlersBetweenInvocationsCore(
            int invocations
        )
        {
            ValidateInvocationCount(invocations);
            string scenario = $"invocations={invocations}";

            yield return WaitUntilMessageHandlerIsFresh();
            AssertMessageBusCounts(
                expectedUntargeted: 0,
                expectedTargeted: 0,
                expectedBroadcast: 0,
                $"before scenario {scenario}"
            );

            int cumulativeInvocationCount = 0;
            for (int i = 0; i < invocations; ++i)
            {
                SimpleUntargetedMessage message = new();
                int invocationStart = cumulativeInvocationCount;
                RunWithComponent(
                    (_, token) =>
                    {
                        token.RegisterUntargeted<SimpleUntargetedMessage>(
                            (ref SimpleUntargetedMessage _) => ++cumulativeInvocationCount
                        );
                        message.EmitUntargeted();

                        Assert.AreEqual(
                            invocationStart + 1,
                            cumulativeInvocationCount,
                            $"Expected exactly one invocation for pass {i + 1}/{invocations} ({scenario})."
                        );
                    }
                );

                // Explicitly verify cross-invocation isolation so stale bus state is caught at the source.
                yield return WaitUntilMessageHandlerIsFresh();
                Assert.AreEqual(
                    i + 1,
                    cumulativeInvocationCount,
                    $"Invocation count drift after pass {i + 1}/{invocations} ({scenario}). {DescribeMessageBusState(MessageHandler.MessageBus, includeLog: true)}"
                );
                AssertMessageBusCounts(
                    expectedUntargeted: 0,
                    expectedTargeted: 0,
                    expectedBroadcast: 0,
                    $"after invocation {i + 1}/{invocations} ({scenario})"
                );
            }
        }

        [TestCase(1)]
        [TestCase(8)]
        [TestCase(32)]
        public void RunWithComponentInvokesUntargetedHandlersDeterministically(int emissions)
        {
            RunWithComponent(
                (_, token) =>
                {
                    int count = 0;
                    SimpleUntargetedMessage message = new();
                    token.RegisterUntargeted<SimpleUntargetedMessage>(
                        (ref SimpleUntargetedMessage _) => ++count
                    );

                    for (int i = 0; i < emissions; ++i)
                    {
                        message.EmitUntargeted();
                    }

                    Assert.AreEqual(
                        emissions,
                        count,
                        "Benchmark harness should invoke untargeted handlers exactly once per emission."
                    );
                }
            );
        }

        [Test]
        public void RunWithComponentSupportsTokenDisableEnableCycle()
        {
            RunWithComponent(
                (_, token) =>
                {
                    int count = 0;
                    SimpleUntargetedMessage message = new();
                    token.RegisterUntargeted<SimpleUntargetedMessage>(
                        (ref SimpleUntargetedMessage _) => ++count
                    );

                    token.Disable();
                    Assert.IsFalse(token.Enabled);

                    message.EmitUntargeted();
                    Assert.AreEqual(0, count);

                    token.Enable();
                    Assert.IsTrue(token.Enabled);

                    message.EmitUntargeted();
                    Assert.AreEqual(
                        1,
                        count,
                        "Handler should resume after the benchmark token is re-enabled."
                    );
                }
            );
        }

        [Test]
        public void RunWithComponentPreparesMonoBehavioursForSendMessageInEditMode()
        {
            RunWithComponent(
                (component, _) =>
                {
                    MonoBehaviour[] behaviours =
                        component.gameObject.GetComponents<MonoBehaviour>();
                    Assert.Greater(
                        behaviours.Length,
                        0,
                        "Benchmark harness should create at least one MonoBehaviour on the target GameObject."
                    );

                    foreach (MonoBehaviour behaviour in behaviours)
                    {
                        Assert.IsTrue(
                            behaviour.enabled,
                            $"Expected benchmark MonoBehaviour '{behaviour.GetType().Name}' to be enabled before dispatch."
                        );

#if UNITY_EDITOR
                        if (!Application.isPlaying)
                        {
                            Assert.IsTrue(
                                behaviour.runInEditMode,
                                $"Expected benchmark MonoBehaviour '{behaviour.GetType().Name}' to run in EditMode for SendMessage-based dispatch."
                            );
                        }
#endif
                    }
                }
            );
        }

        [TestCase(ReflexiveSendMode.Flat, false, 0, 1, 0)]
        [TestCase(ReflexiveSendMode.Downwards, false, 0, 1, 1)]
        [TestCase(ReflexiveSendMode.Upwards, true, 1, 1, 1)]
        public void RunWithComponentDeliversReflexiveOneArgumentMessagesAcrossFastPathModes(
            ReflexiveSendMode sendMode,
            bool targetChild,
            int expectedGrandParentCount,
            int expectedParentCount,
            int expectedChildCount
        )
        {
            RunWithComponent(
                (component, _) =>
                {
                    GameObject parent = component.gameObject;
                    if (!parent.TryGetComponent(out SimpleMessageAwareComponent parentReceiver))
                    {
                        parentReceiver = parent.AddComponent<SimpleMessageAwareComponent>();
                    }

                    GameObject grandParent = new(
                        "BenchmarkReflexiveGrandParent",
                        typeof(SimpleMessageAwareComponent)
                    );
                    _spawned.Add(grandParent);
                    parent.transform.SetParent(grandParent.transform);

                    GameObject child = new(
                        "BenchmarkReflexiveChild",
                        typeof(SimpleMessageAwareComponent)
                    );
                    _spawned.Add(child);
                    child.transform.SetParent(parent.transform);

                    SimpleMessageAwareComponent grandParentReceiver =
                        grandParent.GetComponent<SimpleMessageAwareComponent>();
                    SimpleMessageAwareComponent childReceiver =
                        child.GetComponent<SimpleMessageAwareComponent>();
                    PrepareBenchmarkBehaviourForSendMessage(parentReceiver);
                    PrepareBenchmarkBehaviourForSendMessage(grandParentReceiver);
                    PrepareBenchmarkBehaviourForSendMessage(childReceiver);

                    int grandParentCount = 0;
                    int parentCount = 0;
                    int childCount = 0;
                    grandParentReceiver.slowComplexTargetedHandler = () => ++grandParentCount;
                    parentReceiver.slowComplexTargetedHandler = () => ++parentCount;
                    childReceiver.slowComplexTargetedHandler = () => ++childCount;

                    ComplexTargetedMessage payload = new(Guid.NewGuid());
                    ReflexiveMessage message = new(
                        nameof(SimpleMessageAwareComponent.HandleSlowComplexTargetedMessage),
                        sendMode,
                        payload
                    );

                    InstanceId target = targetChild ? child : parent;
                    message.EmitTargeted(target);

                    string scenario = $"sendMode '{sendMode}', targetChild={targetChild}";
                    Assert.AreEqual(
                        expectedGrandParentCount,
                        grandParentCount,
                        $"Unexpected grand-parent invocation count for {scenario}."
                    );
                    Assert.AreEqual(
                        expectedParentCount,
                        parentCount,
                        $"Unexpected parent invocation count for {scenario}."
                    );
                    Assert.AreEqual(
                        expectedChildCount,
                        childCount,
                        $"Unexpected child invocation count for {scenario}."
                    );
                }
            );
        }

        private static void AssertMessageBusCounts(
            int expectedUntargeted,
            int expectedTargeted,
            int expectedBroadcast,
            string context
        )
        {
            IMessageBus messageBus = MessageHandler.MessageBus;
            Assert.IsNotNull(messageBus, $"MessageBus was null while validating {context}.");

            Assert.AreEqual(
                expectedUntargeted,
                messageBus.RegisteredUntargeted,
                $"Unexpected untargeted registration count {context}."
            );
            Assert.AreEqual(
                expectedTargeted,
                messageBus.RegisteredTargeted,
                $"Unexpected targeted registration count {context}."
            );
            Assert.AreEqual(
                expectedBroadcast,
                messageBus.RegisteredBroadcast,
                $"Unexpected broadcast registration count {context}."
            );
        }

        private static void ValidateInvocationCount(int invocations)
        {
            if (invocations <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(invocations),
                    invocations,
                    "Invocation count must be positive."
                );
            }
        }
    }
}

#endif
