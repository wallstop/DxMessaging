#if UNITY_2021_3_OR_NEWER
namespace DxMessaging.Tests.Runtime.Core
{
    using System.Collections;
    using DxMessaging.Core;
    using DxMessaging.Core.MessageBus;
    using DxMessaging.Tests.Runtime;
    using DxMessaging.Tests.Runtime.Scripts.Components;
    using DxMessaging.Tests.Runtime.Scripts.Messages;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;

    /// <summary>
    /// Self-tests for <see cref="LeakWatcher"/>. Confirms the watcher detects a
    /// known leak (a registration that escapes its <c>using</c> region) and
    /// does not flag clean code (a registration removed before
    /// <see cref="LeakWatcher.Dispose"/>).
    /// </summary>
    public sealed class LeakWatcherSelfTests : MessagingTestBase
    {
        [UnityTest]
        public IEnumerator WatcherPassesWhenAllHandlesAreRemoved(
            [ValueSource(typeof(MessageScenarios), nameof(MessageScenarios.AllKinds))]
                MessageScenario scenario
        )
        {
            GameObject host = new(
                nameof(WatcherPassesWhenAllHandlesAreRemoved) + scenario.Kind,
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(host);
            EmptyMessageAwareComponent component = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);
            InstanceId hostId = host;

            using (LeakWatcher watcher = LeakWatcher.Watch(label: scenario.DisplayName))
            {
                int initial = watcher.InitialSnapshot;
                MessageRegistrationHandle handle = RegisterCountingHandler(scenario, token, hostId);
                Assert.GreaterOrEqual(
                    watcher.Snapshot,
                    initial + 1,
                    "[{0}] Watcher.Snapshot must reflect the new registration in real time.",
                    scenario.Kind
                );
                token.RemoveRegistration(handle);
                Assert.AreEqual(
                    initial,
                    watcher.Snapshot,
                    "[{0}] Watcher.Snapshot must return to the initial value after removal.",
                    scenario.Kind
                );
            }

            yield break;
        }

        [UnityTest]
        public IEnumerator WatcherDetectsLeakedRegistrationWhenNotThrowing(
            [ValueSource(typeof(MessageScenarios), nameof(MessageScenarios.AllKinds))]
                MessageScenario scenario
        )
        {
            GameObject host = new(
                nameof(WatcherDetectsLeakedRegistrationWhenNotThrowing) + scenario.Kind,
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(host);
            EmptyMessageAwareComponent component = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);
            InstanceId hostId = host;

            MessageRegistrationHandle leaked = default;
            bool leakedRegistered = false;
            int observedLeak = 0;
            try
            {
                using (
                    LeakWatcher watcher = new LeakWatcher(
                        bus: MessageHandler.MessageBus,
                        throwOnLeak: false,
                        label: scenario.DisplayName
                    )
                )
                {
                    leaked = RegisterCountingHandler(scenario, token, hostId);
                    leakedRegistered = true;
                    // Intentionally NOT removing the registration before Dispose so
                    // the watcher records the leak.
                    Assert.GreaterOrEqual(
                        watcher.LeakedRegistrations,
                        1,
                        "[{0}] LeakedRegistrations must report >=1 while a leaked handle is still live.",
                        scenario.Kind
                    );
                    observedLeak = watcher.LeakedRegistrations;
                }

                Assert.GreaterOrEqual(
                    observedLeak,
                    1,
                    "[{0}] Watcher must observe at least one leaked registration before disposal.",
                    scenario.Kind
                );
            }
            finally
            {
                // Clean up the leaked handle outside the using block, in a
                // finally that runs even if any of the assertions above
                // throw (so the next test does not inherit the leaked
                // registration). The cleanup is best-effort: a registration
                // wiped by a Reset triggered earlier is a no-op here.
                if (leakedRegistered)
                {
                    token.RemoveRegistration(leaked);
                }
            }
            yield break;
        }

        [UnityTest]
        public IEnumerator WatcherThrowsOnLeakWhenConfiguredTo(
            [ValueSource(typeof(MessageScenarios), nameof(MessageScenarios.AllKinds))]
                MessageScenario scenario
        )
        {
            GameObject host = new(
                nameof(WatcherThrowsOnLeakWhenConfiguredTo) + scenario.Kind,
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(host);
            EmptyMessageAwareComponent component = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);
            InstanceId hostId = host;

            LeakWatcher watcher = LeakWatcher.Watch(label: scenario.DisplayName);
            MessageRegistrationHandle leaked = RegisterCountingHandler(scenario, token, hostId);

            try
            {
                Assert.Throws<AssertionException>(
                    watcher.Dispose,
                    "[{0}] LeakWatcher.Dispose with throwOnLeak=true must surface a failed assertion when registrations leak.",
                    scenario.Kind
                );
            }
            finally
            {
                token.RemoveRegistration(leaked);
            }

            yield break;
        }

        private static MessageRegistrationHandle RegisterCountingHandler(
            MessageScenario scenario,
            MessageRegistrationToken token,
            InstanceId target
        )
        {
            switch (scenario.Kind)
            {
                case MessageKind.Untargeted:
                {
                    return ScenarioHarness.RegisterUntargeted<SimpleUntargetedMessage>(
                        scenario,
                        token,
                        (ref SimpleUntargetedMessage _) => { }
                    );
                }
                case MessageKind.Targeted:
                {
                    return ScenarioHarness.RegisterTargeted<SimpleTargetedMessage>(
                        scenario,
                        token,
                        target,
                        (ref SimpleTargetedMessage _) => { }
                    );
                }
                case MessageKind.Broadcast:
                {
                    return ScenarioHarness.RegisterBroadcast<SimpleBroadcastMessage>(
                        scenario,
                        token,
                        target,
                        (ref SimpleBroadcastMessage _) => { }
                    );
                }
                default:
                {
                    throw new System.ArgumentOutOfRangeException(
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
