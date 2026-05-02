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

    public sealed class InterceptorCancellationTests : MessagingTestBase
    {
        [UnityTest]
        public IEnumerator InterceptorCancelsHandlersAndPostProcessors(
            [ValueSource(typeof(MessageScenarios), nameof(MessageScenarios.AllKinds))]
                MessageScenario scenario
        )
        {
            GameObject host = new(
                nameof(InterceptorCancelsHandlersAndPostProcessors) + "_" + scenario,
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(host);
            EmptyMessageAwareComponent component = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);
            InstanceId hostId = host;

            int handled = 0;
            int postProcessed = 0;
            int laterRan = 0;

            _ = RegisterHandler(scenario, token, hostId, () => handled++);
            _ = RegisterPostProcessor(scenario, token, hostId, () => postProcessed++);

            // Register a canceling interceptor (always false)
            _ = RegisterInterceptor(scenario, token, () => false);

            // Also register a later interceptor that would be skipped if earlier cancels
            _ = RegisterInterceptor(
                scenario,
                token,
                () =>
                {
                    laterRan++;
                    return true;
                },
                priority: 10
            );

            EmitForScenario(scenario, hostId);

            Assert.AreEqual(0, handled, "Handlers must not run when interceptor cancels.");
            Assert.AreEqual(
                0,
                postProcessed,
                "Post-processors must not run when interceptor cancels."
            );
            Assert.AreEqual(0, laterRan, "Later interceptors must not run after cancellation.");
            yield break;
        }

        private static MessageRegistrationHandle RegisterHandler(
            MessageScenario scenario,
            MessageRegistrationToken token,
            InstanceId target,
            Action onInvoked,
            int priority = 0
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

        private static MessageRegistrationHandle RegisterPostProcessor(
            MessageScenario scenario,
            MessageRegistrationToken token,
            InstanceId target,
            Action onInvoked,
            int priority = 0
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

        private static MessageRegistrationHandle RegisterInterceptor(
            MessageScenario scenario,
            MessageRegistrationToken token,
            Func<bool> body,
            int priority = 0
        )
        {
            switch (scenario.Kind)
            {
                case MessageKind.Untargeted:
                {
                    return ScenarioHarness.RegisterUntargetedInterceptor<SimpleUntargetedMessage>(
                        scenario,
                        token,
                        (ref SimpleUntargetedMessage _) => body(),
                        priority
                    );
                }
                case MessageKind.Targeted:
                {
                    return ScenarioHarness.RegisterTargetedInterceptor<SimpleTargetedMessage>(
                        scenario,
                        token,
                        (ref InstanceId _, ref SimpleTargetedMessage __) => body(),
                        priority
                    );
                }
                case MessageKind.Broadcast:
                {
                    return ScenarioHarness.RegisterBroadcastInterceptor<SimpleBroadcastMessage>(
                        scenario,
                        token,
                        (ref InstanceId _, ref SimpleBroadcastMessage __) => body(),
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
