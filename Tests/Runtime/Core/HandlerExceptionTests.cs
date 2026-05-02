#if UNITY_2021_3_OR_NEWER
namespace DxMessaging.Tests.Runtime.Core
{
    using System;
    using System.Collections;
    using DxMessaging.Core;
    using DxMessaging.Tests.Runtime;
    using DxMessaging.Tests.Runtime.Scripts.Components;
    using DxMessaging.Tests.Runtime.Scripts.Messages;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;

    // Bus does not emit framework-level logs on handler/interceptor/post-processor throws; LogAssert.Expect intentionally not used.

    /// <summary>
    /// Pins the current behavior of the message bus when handlers, interceptors, and
    /// post-processors throw. The bus does not wrap dispatched delegates in try/catch,
    /// so exceptions propagate out of the emit call and any siblings scheduled to run
    /// after the throwing delegate are skipped for the current dispatch. These tests
    /// capture that contract so any future change to swallow-and-log behavior fails
    /// loudly and forces a deliberate review.
    /// </summary>
    public sealed class HandlerExceptionTests : MessagingTestBase
    {
        private const string ThrowingHandlerMessage = "DxMessaging-test-handler-throw";
        private const string ThrowingInterceptorMessage = "DxMessaging-test-interceptor-throw";
        private const string ThrowingPostProcessorMessage = "DxMessaging-test-post-processor-throw";

        /// <summary>
        /// Pins that a throwing handler aborts the rest of the current dispatch:
        /// previously ordered handlers run, the throwing handler runs, and any
        /// subsequent handler scheduled after it is skipped for that emission.
        /// </summary>
        [UnityTest]
        public IEnumerator HandlerThrowPreventsSubsequentHandlers(
            [ValueSource(typeof(MessageScenarios), nameof(MessageScenarios.AllKinds))]
                MessageScenario scenario
        )
        {
            GameObject host = new(
                nameof(HandlerThrowPreventsSubsequentHandlers) + scenario.Kind,
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(host);
            EmptyMessageAwareComponent component = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);
            InstanceId hostId = host;

            int firstCount = 0;
            int secondCount = 0;
            int thirdCount = 0;

            RegisterCountingHandler(
                scenario,
                token,
                hostId,
                priority: 0,
                onInvoked: () => ++firstCount
            );
            RegisterCountingHandler(
                scenario,
                token,
                hostId,
                priority: 0,
                onInvoked: () =>
                {
                    ++secondCount;
                    throw new InvalidOperationException(ThrowingHandlerMessage);
                }
            );
            RegisterCountingHandler(
                scenario,
                token,
                hostId,
                priority: 0,
                onInvoked: () => ++thirdCount
            );

            InvalidOperationException captured = Assert.Throws<InvalidOperationException>(() =>
                EmitForScenario(scenario, hostId)
            );

            Assert.AreEqual(ThrowingHandlerMessage, captured.Message);
            Assert.AreEqual(1, firstCount, "First handler must run before the throwing handler.");
            Assert.AreEqual(1, secondCount, "Throwing handler must execute before propagating.");
            // Pinning current behavior: the bus does not wrap handlers in try/catch, so
            // siblings scheduled after the throwing one are skipped during this dispatch.
            // If that ever changes (e.g. the bus starts swallow-and-log) update this assertion.
            Assert.AreEqual(
                0,
                thirdCount,
                "Subsequent handler must not run once propagation begins."
            );
            yield break;
        }

        [UnityTest]
        public IEnumerator HandlerThrowDoesNotCorruptDispatchPool(
            [ValueSource(typeof(MessageScenarios), nameof(MessageScenarios.AllKinds))]
                MessageScenario scenario
        )
        {
            GameObject host = new(
                nameof(HandlerThrowDoesNotCorruptDispatchPool) + scenario.Kind,
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(host);
            EmptyMessageAwareComponent component = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);
            InstanceId hostId = host;

            int safeCount = 0;
            int throwingCount = 0;

            RegisterCountingHandler(
                scenario,
                token,
                hostId,
                priority: 0,
                onInvoked: () => ++safeCount
            );
            RegisterCountingHandler(
                scenario,
                token,
                hostId,
                priority: 1,
                onInvoked: () =>
                {
                    ++throwingCount;
                    throw new InvalidOperationException(ThrowingHandlerMessage);
                }
            );

            const int Iterations = 10;
            for (int i = 0; i < Iterations; ++i)
            {
                InvalidOperationException captured = Assert.Throws<InvalidOperationException>(() =>
                    EmitForScenario(scenario, hostId)
                );
                Assert.AreEqual(ThrowingHandlerMessage, captured.Message);
            }

            Assert.AreEqual(
                Iterations,
                safeCount,
                "Safe handler must run on every emission even when later handler throws."
            );
            Assert.AreEqual(
                Iterations,
                throwingCount,
                "Throwing handler must execute on every emission with no double-fire or skip."
            );
            yield break;
        }

        /// <summary>
        /// Pins that a throwing handler aborts the dispatch before post-processors
        /// run. Handler exceptions propagate out of the emit call without invoking
        /// any post-processors registered for the same message. If post-processors
        /// are later moved into a finally block this contract must be revisited.
        /// </summary>
        [UnityTest]
        public IEnumerator HandlerThrowPreventsPostProcessorsFromRunning(
            [ValueSource(typeof(MessageScenarios), nameof(MessageScenarios.AllKinds))]
                MessageScenario scenario
        )
        {
            GameObject host = new(
                nameof(HandlerThrowPreventsPostProcessorsFromRunning) + scenario.Kind,
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(host);
            EmptyMessageAwareComponent component = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);
            InstanceId hostId = host;

            int handlerCount = 0;
            int postProcessorCount = 0;

            RegisterCountingHandler(
                scenario,
                token,
                hostId,
                priority: 0,
                onInvoked: () =>
                {
                    ++handlerCount;
                    throw new InvalidOperationException(ThrowingHandlerMessage);
                }
            );
            RegisterCountingPostProcessor(
                scenario,
                token,
                hostId,
                priority: 0,
                onInvoked: () => ++postProcessorCount
            );

            InvalidOperationException captured = Assert.Throws<InvalidOperationException>(() =>
                EmitForScenario(scenario, hostId)
            );

            Assert.AreEqual(ThrowingHandlerMessage, captured.Message);
            Assert.AreEqual(1, handlerCount, "Throwing handler must execute exactly once.");
            // Pinning current behavior: a handler exception aborts the dispatch before
            // post-processors run. If post-processors are later moved into a finally
            // block the assertion below will need to be inverted.
            Assert.AreEqual(
                0,
                postProcessorCount,
                "Post-processor must not run when an earlier handler throws."
            );
            yield break;
        }

        [UnityTest]
        public IEnumerator HandlerThrowDoesNotPreventDeregistration(
            [ValueSource(typeof(MessageScenarios), nameof(MessageScenarios.AllKinds))]
                MessageScenario scenario
        )
        {
            GameObject host = new(
                nameof(HandlerThrowDoesNotPreventDeregistration) + scenario.Kind,
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(host);
            EmptyMessageAwareComponent component = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);
            InstanceId hostId = host;

            int throwingCount = 0;
            MessageRegistrationHandle handle = RegisterCountingHandler(
                scenario,
                token,
                hostId,
                priority: 0,
                onInvoked: () =>
                {
                    ++throwingCount;
                    throw new InvalidOperationException(ThrowingHandlerMessage);
                }
            );

            InvalidOperationException firstCaptured = Assert.Throws<InvalidOperationException>(() =>
                EmitForScenario(scenario, hostId)
            );
            Assert.AreEqual(ThrowingHandlerMessage, firstCaptured.Message);
            Assert.AreEqual(1, throwingCount);

            token.RemoveRegistration(handle);

            EmitForScenario(scenario, hostId);
            Assert.AreEqual(
                1,
                throwingCount,
                "Handler must not fire after RemoveRegistration even if a previous emit threw."
            );

            // After deregistering the throwing handler, registering a fresh
            // non-throwing handler must produce a clean dispatch with no residue
            // from the previous failure.
            int replacementCount = 0;
            MessageRegistrationHandle replacementHandle = RegisterCountingHandler(
                scenario,
                token,
                hostId,
                priority: 0,
                onInvoked: () => ++replacementCount
            );

            EmitForScenario(scenario, hostId);
            Assert.AreEqual(
                1,
                replacementCount,
                "Replacement handler registered after the throw must run on the next emission."
            );
            Assert.AreEqual(
                1,
                throwingCount,
                "Removed throwing handler must remain inert after replacement is registered."
            );

            token.RemoveRegistration(replacementHandle);
            yield break;
        }

        [UnityTest]
        public IEnumerator InterceptorThrowFallsBackGracefully(
            [ValueSource(typeof(MessageScenarios), nameof(MessageScenarios.AllKinds))]
                MessageScenario scenario
        )
        {
            GameObject host = new(
                nameof(InterceptorThrowFallsBackGracefully) + scenario.Kind,
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(host);
            EmptyMessageAwareComponent component = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);
            InstanceId hostId = host;

            int handlerCount = 0;
            int interceptorCount = 0;

            RegisterCountingHandler(
                scenario,
                token,
                hostId,
                priority: 0,
                onInvoked: () => ++handlerCount
            );
            RegisterThrowingInterceptor(scenario, token, onInvoked: () => ++interceptorCount);

            InvalidOperationException captured = Assert.Throws<InvalidOperationException>(() =>
                EmitForScenario(scenario, hostId)
            );

            Assert.AreEqual(ThrowingInterceptorMessage, captured.Message);
            Assert.AreEqual(
                1,
                interceptorCount,
                "Interceptor must execute and throw exactly once."
            );
            // Behavior pinned to current implementation: interceptor exceptions
            // propagate before handlers run, so handlers do not see the message.
            Assert.AreEqual(
                0,
                handlerCount,
                "Handler must not run when an interceptor throws during the same emission."
            );

            // Sanity: a follow-up emission after the throwing interceptor still raises again,
            // proving no infinite loop or NullReferenceException is masked behind the throw.
            InvalidOperationException secondCaptured = Assert.Throws<InvalidOperationException>(
                () =>
                    EmitForScenario(scenario, hostId)
            );
            Assert.AreEqual(ThrowingInterceptorMessage, secondCaptured.Message);
            Assert.AreEqual(2, interceptorCount);
            Assert.AreEqual(0, handlerCount);
            yield break;
        }

        [UnityTest]
        public IEnumerator PostProcessorThrowDoesNotAffectNextEmission(
            [ValueSource(typeof(MessageScenarios), nameof(MessageScenarios.AllKinds))]
                MessageScenario scenario
        )
        {
            GameObject host = new(
                nameof(PostProcessorThrowDoesNotAffectNextEmission) + scenario.Kind,
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(host);
            EmptyMessageAwareComponent component = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);
            InstanceId hostId = host;

            int handlerCount = 0;
            int throwingPostProcessorCount = 0;
            int trailingPostProcessorCount = 0;

            RegisterCountingHandler(
                scenario,
                token,
                hostId,
                priority: 0,
                onInvoked: () => ++handlerCount
            );
            // Throwing post-processor at priority 1 (runs after the trailing one
            // at priority 2 if priority is purely lower-first, OR before depending
            // on order). To force a deterministic order where the throwing PP runs
            // first and skips the trailing one, register the throwing PP at the
            // earlier priority and the trailing PP at a later priority.
            RegisterCountingPostProcessor(
                scenario,
                token,
                hostId,
                priority: 0,
                onInvoked: () =>
                {
                    ++throwingPostProcessorCount;
                    throw new InvalidOperationException(ThrowingPostProcessorMessage);
                }
            );
            RegisterCountingPostProcessor(
                scenario,
                token,
                hostId,
                priority: 1,
                onInvoked: () => ++trailingPostProcessorCount
            );

            InvalidOperationException firstCaptured = Assert.Throws<InvalidOperationException>(() =>
                EmitForScenario(scenario, hostId)
            );
            Assert.AreEqual(ThrowingPostProcessorMessage, firstCaptured.Message);
            Assert.AreEqual(1, handlerCount, "Handler must run before throwing post-processor.");
            Assert.AreEqual(1, throwingPostProcessorCount);
            Assert.AreEqual(
                0,
                trailingPostProcessorCount,
                "Trailing post-processor must not run when an earlier post-processor throws."
            );

            InvalidOperationException secondCaptured = Assert.Throws<InvalidOperationException>(
                () =>
                    EmitForScenario(scenario, hostId)
            );
            Assert.AreEqual(ThrowingPostProcessorMessage, secondCaptured.Message);
            Assert.AreEqual(
                2,
                handlerCount,
                "Handler must continue to run on subsequent emissions."
            );
            Assert.AreEqual(2, throwingPostProcessorCount);
            Assert.AreEqual(
                0,
                trailingPostProcessorCount,
                "Trailing post-processor must remain skipped on every emission while the earlier one throws."
            );
            yield break;
        }

        private static MessageRegistrationHandle RegisterCountingHandler(
            MessageScenario scenario,
            MessageRegistrationToken token,
            InstanceId target,
            int priority,
            Action onInvoked
        )
        {
            switch (scenario.Kind)
            {
                case MessageKind.Untargeted:
                {
                    return ScenarioHarness.RegisterUntargeted<SimpleUntargetedMessage>(
                        scenario,
                        token,
                        (ref SimpleUntargetedMessage _) => onInvoked(),
                        priority
                    );
                }
                case MessageKind.Targeted:
                {
                    return ScenarioHarness.RegisterTargeted<SimpleTargetedMessage>(
                        scenario,
                        token,
                        target,
                        (ref SimpleTargetedMessage _) => onInvoked(),
                        priority
                    );
                }
                case MessageKind.Broadcast:
                {
                    return ScenarioHarness.RegisterBroadcast<SimpleBroadcastMessage>(
                        scenario,
                        token,
                        target,
                        (ref SimpleBroadcastMessage _) => onInvoked(),
                        priority
                    );
                }
                default:
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(scenario),
                        scenario.Kind,
                        "Unsupported message kind."
                    );
                }
            }
        }

        private static MessageRegistrationHandle RegisterCountingPostProcessor(
            MessageScenario scenario,
            MessageRegistrationToken token,
            InstanceId target,
            int priority,
            Action onInvoked
        )
        {
            switch (scenario.Kind)
            {
                case MessageKind.Untargeted:
                {
                    return ScenarioHarness.RegisterUntargetedPostProcessor<SimpleUntargetedMessage>(
                        scenario,
                        token,
                        (ref SimpleUntargetedMessage _) => onInvoked(),
                        priority
                    );
                }
                case MessageKind.Targeted:
                {
                    return ScenarioHarness.RegisterTargetedPostProcessor<SimpleTargetedMessage>(
                        scenario,
                        token,
                        target,
                        (ref SimpleTargetedMessage _) => onInvoked(),
                        priority
                    );
                }
                case MessageKind.Broadcast:
                {
                    return ScenarioHarness.RegisterBroadcastPostProcessor<SimpleBroadcastMessage>(
                        scenario,
                        token,
                        target,
                        (ref SimpleBroadcastMessage _) => onInvoked(),
                        priority
                    );
                }
                default:
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(scenario),
                        scenario.Kind,
                        "Unsupported message kind."
                    );
                }
            }
        }

        private static MessageRegistrationHandle RegisterThrowingInterceptor(
            MessageScenario scenario,
            MessageRegistrationToken token,
            Action onInvoked
        )
        {
            switch (scenario.Kind)
            {
                case MessageKind.Untargeted:
                {
                    return ScenarioHarness.RegisterUntargetedInterceptor<SimpleUntargetedMessage>(
                        scenario,
                        token,
                        (ref SimpleUntargetedMessage _) =>
                        {
                            onInvoked();
                            throw new InvalidOperationException(ThrowingInterceptorMessage);
                        }
                    );
                }
                case MessageKind.Targeted:
                {
                    return ScenarioHarness.RegisterTargetedInterceptor<SimpleTargetedMessage>(
                        scenario,
                        token,
                        (ref InstanceId _, ref SimpleTargetedMessage _) =>
                        {
                            onInvoked();
                            throw new InvalidOperationException(ThrowingInterceptorMessage);
                        }
                    );
                }
                case MessageKind.Broadcast:
                {
                    return ScenarioHarness.RegisterBroadcastInterceptor<SimpleBroadcastMessage>(
                        scenario,
                        token,
                        (ref InstanceId _, ref SimpleBroadcastMessage _) =>
                        {
                            onInvoked();
                            throw new InvalidOperationException(ThrowingInterceptorMessage);
                        }
                    );
                }
                default:
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(scenario),
                        scenario.Kind,
                        "Unsupported message kind."
                    );
                }
            }
        }

        private static void EmitForScenario(MessageScenario scenario, InstanceId target)
        {
            switch (scenario.Kind)
            {
                case MessageKind.Untargeted:
                {
                    SimpleUntargetedMessage message = new();
                    ScenarioHarness.EmitUntargeted(scenario, ref message);
                    return;
                }
                case MessageKind.Targeted:
                {
                    SimpleTargetedMessage message = new();
                    ScenarioHarness.EmitTargeted(scenario, ref message, target);
                    return;
                }
                case MessageKind.Broadcast:
                {
                    SimpleBroadcastMessage message = new();
                    ScenarioHarness.EmitBroadcast(scenario, ref message, target);
                    return;
                }
                default:
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(scenario),
                        scenario.Kind,
                        "Unsupported message kind."
                    );
                }
            }
        }
    }
}
#endif
