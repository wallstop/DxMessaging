#if UNITY_2021_3_OR_NEWER
namespace DxMessaging.Tests.Runtime.Core
{
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using DxMessaging.Core;
    using DxMessaging.Core.MessageBus;
    using NUnit.Framework;
    using Scripts.Components;
    using UnityEngine;
    using UnityEngine.TestTools;
    using Object = UnityEngine.Object;

    public sealed class MessagingTestBaseCleanupRobustnessTests : MessagingTestBase
    {
        private static readonly CleanupScenario[] CleanupScenarios =
        {
            new(
                "sync-zero",
                useUnityCleanup: false,
                trackedObjectCount: 0,
                preDestroyFirstTrackedObject: false
            ),
            new(
                "sync-one",
                useUnityCleanup: false,
                trackedObjectCount: 1,
                preDestroyFirstTrackedObject: false
            ),
            new(
                "sync-one-pre-destroy",
                useUnityCleanup: false,
                trackedObjectCount: 1,
                preDestroyFirstTrackedObject: true
            ),
            new(
                "sync-three",
                useUnityCleanup: false,
                trackedObjectCount: 3,
                preDestroyFirstTrackedObject: false
            ),
            new(
                "sync-three-pre-destroy",
                useUnityCleanup: false,
                trackedObjectCount: 3,
                preDestroyFirstTrackedObject: true
            ),
            new(
                "unity-zero",
                useUnityCleanup: true,
                trackedObjectCount: 0,
                preDestroyFirstTrackedObject: false
            ),
            new(
                "unity-one",
                useUnityCleanup: true,
                trackedObjectCount: 1,
                preDestroyFirstTrackedObject: false
            ),
            new(
                "unity-one-pre-destroy",
                useUnityCleanup: true,
                trackedObjectCount: 1,
                preDestroyFirstTrackedObject: true
            ),
            new(
                "unity-three",
                useUnityCleanup: true,
                trackedObjectCount: 3,
                preDestroyFirstTrackedObject: false
            ),
            new(
                "unity-three-pre-destroy",
                useUnityCleanup: true,
                trackedObjectCount: 3,
                preDestroyFirstTrackedObject: true
            ),
        };

        // Unity Test Framework supports parameterization for UnityTest via ValueSource.
        [UnityTest]
        public IEnumerator CleanupVariantsDestroyTrackedObjectsAndClearRegistrations(
            [ValueSource(nameof(CleanupScenarios))] CleanupScenario scenario
        )
        {
            string scenarioLabel = scenario.ToString();
            TestContext.WriteLine(
                $"Running cleanup scenario: {scenarioLabel}. isPlaying={Application.isPlaying}."
            );

            List<GameObject> created = new(scenario.TrackedObjectCount);
            List<string> names = new(scenario.TrackedObjectCount);

            for (int i = 0; i < scenario.TrackedObjectCount; ++i)
            {
                GameObject go = new(
                    $"MessagingTestBaseCleanup_{scenario.Name}_{scenario.TrackedObjectCount}_{i}",
                    typeof(SimpleMessageAwareComponent)
                );
                _spawned.Add(go);
                created.Add(go);
                names.Add(go.name);
            }

            if (scenario.PreDestroyFirstTrackedObject)
            {
                Assert.Greater(
                    scenario.TrackedObjectCount,
                    0,
                    $"Pre-destroy requires at least one tracked object. {scenarioLabel}"
                );

                DestroyForCleanupScenario(created[0]);
                if (Application.isPlaying)
                {
                    yield return null;
                }

                Assert.IsTrue(
                    created[0] == null,
                    $"Pre-destroyed object should report as destroyed before cleanup runs. {scenarioLabel}"
                );
            }

            IMessageBus messageBus = MessageHandler.MessageBus;
            Assert.IsNotNull(messageBus, $"Message bus must exist before cleanup. {scenarioLabel}");

            int totalBeforeCleanup =
                messageBus.RegisteredUntargeted
                + messageBus.RegisteredTargeted
                + messageBus.RegisteredBroadcast;

            int aliveBeforeCleanup = created.Count(go => go != null);
            if (scenario.TrackedObjectCount == 0)
            {
                Assert.Zero(
                    totalBeforeCleanup,
                    $"Zero tracked objects should not register handlers before cleanup. {scenarioLabel} {DescribeMessageBusState(messageBus, includeLog: true)}"
                );
            }
            else if (aliveBeforeCleanup > 0)
            {
                Assert.Greater(
                    totalBeforeCleanup,
                    0,
                    $"Expected at least one registration before cleanup when tracked objects are alive. {scenarioLabel} {DescribeMessageBusState(messageBus, includeLog: true)}"
                );
            }
            else
            {
                Assert.Zero(
                    totalBeforeCleanup,
                    $"No alive tracked objects should leave no active registrations before cleanup. {scenarioLabel} {DescribeMessageBusState(messageBus, includeLog: true)}"
                );
                TestContext.WriteLine(
                    $"All tracked objects were already destroyed before cleanup. Registrations before cleanup={totalBeforeCleanup}. {scenarioLabel}."
                );
            }

            if (scenario.UseUnityCleanup)
            {
                yield return UnityCleanup();
            }
            else
            {
                Cleanup();
                if (Application.isPlaying)
                {
                    yield return null;
                }
            }

            for (int i = 0; i < created.Count; ++i)
            {
                Assert.IsTrue(
                    created[i] == null,
                    $"Tracked object '{names[i]}' should be destroyed by cleanup. index={i}. {scenarioLabel}"
                );
            }

            Assert.Zero(
                _spawned.Count,
                $"Cleanup should clear tracked spawned objects. {scenarioLabel}"
            );
            yield return WaitUntilMessageHandlerIsFresh();

            IMessageBus finalMessageBus = MessageHandler.MessageBus;
            Assert.IsNotNull(
                finalMessageBus,
                $"Message bus must remain available after cleanup. {scenarioLabel}"
            );

            int totalAfterCleanup =
                finalMessageBus.RegisteredUntargeted
                + finalMessageBus.RegisteredTargeted
                + finalMessageBus.RegisteredBroadcast;
            Assert.Zero(
                totalAfterCleanup,
                $"Cleanup should leave message bus fresh. {scenarioLabel} {DescribeMessageBusState(finalMessageBus, includeLog: true)}"
            );
        }

        private static void DestroyForCleanupScenario(GameObject spawned)
        {
            if (Application.isPlaying)
            {
                Object.Destroy(spawned);
                return;
            }

            Object.DestroyImmediate(spawned);
        }

        public sealed class CleanupScenario
        {
            public CleanupScenario(
                string name,
                bool useUnityCleanup,
                int trackedObjectCount,
                bool preDestroyFirstTrackedObject
            )
            {
                Name = name;
                UseUnityCleanup = useUnityCleanup;
                TrackedObjectCount = trackedObjectCount;
                PreDestroyFirstTrackedObject = preDestroyFirstTrackedObject;
            }

            public string Name { get; }

            public bool UseUnityCleanup { get; }

            public int TrackedObjectCount { get; }

            public bool PreDestroyFirstTrackedObject { get; }

            public override string ToString()
            {
                return $"Name={Name},UseUnityCleanup={UseUnityCleanup},TrackedObjectCount={TrackedObjectCount},PreDestroyFirstTrackedObject={PreDestroyFirstTrackedObject}";
            }
        }
    }
}

#endif
