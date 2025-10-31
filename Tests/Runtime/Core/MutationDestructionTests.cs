#if UNITY_2021_3_OR_NEWER
namespace DxMessaging.Tests.Runtime.Core
{
    using System.Collections;
    using DxMessaging.Core;
    using DxMessaging.Core.Extensions;
    using DxMessaging.Tests.Runtime.Scripts.Components;
    using DxMessaging.Tests.Runtime.Scripts.Messages;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;

    /// <summary>
    /// Validates mutation semantics when a listener is explicitly destroyed during dispatch.
    ///
    /// Snapshot semantics should only freeze additions; explicit Unity destruction should prevent
    /// later listeners from acting in the same emission. If a destroyed listener is still invoked
    /// by the frozen snapshot, its handlers should effectively no-op (invalid Unity object state).
    ///
    /// Each test wires two listeners:
    /// - First handler runs at lower priority and destroys the second listener's GameObject
    /// - Second handler guards on Unity null to avoid side-effects if invoked after destruction
    ///
    /// Expectations per emission:
    /// - First handler runs exactly once
    /// - Second handler does not increment its counters
    /// </summary>
    public sealed class MutationDestructionTests : MessagingTestBase
    {
        private const int DestroyerPriority = -10; // ensure it runs before default priority 0

        [UnityTest]
        public IEnumerator UntargetedDestroyOtherListenerDoesNotRun()
        {
            GameObject a = new(
                nameof(UntargetedDestroyOtherListenerDoesNotRun) + "_A",
                typeof(EmptyMessageAwareComponent)
            );
            GameObject b = new(
                nameof(UntargetedDestroyOtherListenerDoesNotRun) + "_B",
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(a);
            _spawned.Add(b);

            EmptyMessageAwareComponent compA = a.GetComponent<EmptyMessageAwareComponent>();
            EmptyMessageAwareComponent compB = b.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken tokenA = GetToken(compA);
            MessageRegistrationToken tokenB = GetToken(compB);

            int firstCount = 0;
            int secondCount = 0;

            _ = tokenA.RegisterUntargeted(
                (ref SimpleUntargetedMessage _) =>
                {
                    firstCount++;
                    Object.Destroy(b);
                },
                DestroyerPriority
            );

            _ = tokenB.RegisterUntargeted(
                (ref SimpleUntargetedMessage _) =>
                {
                    secondCount++;
                }
            );

            SimpleUntargetedMessage msg = new();
            msg.EmitUntargeted();

            Assert.AreEqual(1, firstCount, "First handler should run exactly once.");
            Assert.AreEqual(0, secondCount, "Second handler must not act after it is destroyed.");
            yield break;
        }

        [UnityTest]
        public IEnumerator TargetedGameObjectDestroyOtherListenerDoesNotRun()
        {
            GameObject a = new(
                nameof(TargetedGameObjectDestroyOtherListenerDoesNotRun) + "_A",
                typeof(EmptyMessageAwareComponent)
            );
            GameObject b = new(
                nameof(TargetedGameObjectDestroyOtherListenerDoesNotRun) + "_B",
                typeof(EmptyMessageAwareComponent)
            );
            GameObject target = new(
                nameof(TargetedGameObjectDestroyOtherListenerDoesNotRun) + "_Target"
            );
            _spawned.Add(a);
            _spawned.Add(b);
            _spawned.Add(target);

            EmptyMessageAwareComponent compA = a.GetComponent<EmptyMessageAwareComponent>();
            EmptyMessageAwareComponent compB = b.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken tokenA = GetToken(compA);
            MessageRegistrationToken tokenB = GetToken(compB);

            int firstCount = 0;
            int secondCount = 0;

            _ = tokenA.RegisterGameObjectTargeted(
                target,
                (ref SimpleTargetedMessage _) =>
                {
                    firstCount++;
                    Object.Destroy(b);
                },
                DestroyerPriority
            );

            _ = tokenB.RegisterGameObjectTargeted(
                target,
                (ref SimpleTargetedMessage _) =>
                {
                    secondCount++;
                }
            );

            SimpleTargetedMessage msg = new();
            msg.EmitGameObjectTargeted(target);

            Assert.AreEqual(1, firstCount, "First targeted handler should run once.");
            Assert.AreEqual(
                0,
                secondCount,
                "Second targeted handler must not act after destruction."
            );
            yield break;
        }

        [UnityTest]
        public IEnumerator TargetedComponentDestroyOtherListenerDoesNotRun()
        {
            GameObject a = new(
                nameof(TargetedComponentDestroyOtherListenerDoesNotRun) + "_A",
                typeof(EmptyMessageAwareComponent)
            );
            GameObject b = new(
                nameof(TargetedComponentDestroyOtherListenerDoesNotRun) + "_B",
                typeof(EmptyMessageAwareComponent)
            );
            GameObject targetGo = new(
                nameof(TargetedComponentDestroyOtherListenerDoesNotRun) + "_Target",
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(a);
            _spawned.Add(b);
            _spawned.Add(targetGo);

            EmptyMessageAwareComponent compA = a.GetComponent<EmptyMessageAwareComponent>();
            EmptyMessageAwareComponent compB = b.GetComponent<EmptyMessageAwareComponent>();
            EmptyMessageAwareComponent targetComp =
                targetGo.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken tokenA = GetToken(compA);
            MessageRegistrationToken tokenB = GetToken(compB);

            int firstCount = 0;
            int secondCount = 0;

            _ = tokenA.RegisterComponentTargeted(
                targetComp,
                (ref SimpleTargetedMessage _) =>
                {
                    firstCount++;
                    Object.Destroy(b);
                },
                DestroyerPriority
            );

            _ = tokenB.RegisterComponentTargeted(
                targetComp,
                (ref SimpleTargetedMessage _) =>
                {
                    secondCount++;
                }
            );

            SimpleTargetedMessage msg = new();
            msg.EmitComponentTargeted(targetComp);

            Assert.AreEqual(1, firstCount, "First component-targeted handler should run once.");
            Assert.AreEqual(
                0,
                secondCount,
                "Second component-targeted handler must not act after destruction."
            );
            yield break;
        }

        [UnityTest]
        public IEnumerator TargetedWithoutTargetingDestroyOtherListenerDoesNotRun()
        {
            GameObject a = new(
                nameof(TargetedWithoutTargetingDestroyOtherListenerDoesNotRun) + "_A",
                typeof(EmptyMessageAwareComponent)
            );
            GameObject b = new(
                nameof(TargetedWithoutTargetingDestroyOtherListenerDoesNotRun) + "_B",
                typeof(EmptyMessageAwareComponent)
            );
            GameObject target = new(
                nameof(TargetedWithoutTargetingDestroyOtherListenerDoesNotRun) + "_Target"
            );
            _spawned.Add(a);
            _spawned.Add(b);
            _spawned.Add(target);

            EmptyMessageAwareComponent compA = a.GetComponent<EmptyMessageAwareComponent>();
            EmptyMessageAwareComponent compB = b.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken tokenA = GetToken(compA);
            MessageRegistrationToken tokenB = GetToken(compB);

            int firstCount = 0;
            int secondCount = 0;

            _ = tokenA.RegisterTargetedWithoutTargeting(
                (ref InstanceId _, ref SimpleTargetedMessage _) =>
                {
                    firstCount++;
                    Object.Destroy(b);
                },
                DestroyerPriority
            );

            _ = tokenB.RegisterTargetedWithoutTargeting(
                (ref InstanceId _, ref SimpleTargetedMessage _) =>
                {
                    secondCount++;
                }
            );

            SimpleTargetedMessage msg = new();
            msg.EmitGameObjectTargeted(target);

            Assert.AreEqual(
                1,
                firstCount,
                "First targeted-without-targeting handler should run once."
            );
            Assert.AreEqual(
                0,
                secondCount,
                "Second targeted-without-targeting handler must not act after destruction."
            );
            yield break;
        }

        [UnityTest]
        public IEnumerator BroadcastGameObjectDestroyOtherListenerDoesNotRun()
        {
            GameObject a = new(
                nameof(BroadcastGameObjectDestroyOtherListenerDoesNotRun) + "_A",
                typeof(EmptyMessageAwareComponent)
            );
            GameObject b = new(
                nameof(BroadcastGameObjectDestroyOtherListenerDoesNotRun) + "_B",
                typeof(EmptyMessageAwareComponent)
            );
            GameObject source = new(
                nameof(BroadcastGameObjectDestroyOtherListenerDoesNotRun) + "_Source"
            );
            _spawned.Add(a);
            _spawned.Add(b);
            _spawned.Add(source);

            EmptyMessageAwareComponent compA = a.GetComponent<EmptyMessageAwareComponent>();
            EmptyMessageAwareComponent compB = b.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken tokenA = GetToken(compA);
            MessageRegistrationToken tokenB = GetToken(compB);

            int firstCount = 0;
            int secondCount = 0;

            _ = tokenA.RegisterGameObjectBroadcast(
                source,
                (ref SimpleBroadcastMessage _) =>
                {
                    firstCount++;
                    Object.Destroy(b);
                },
                DestroyerPriority
            );

            _ = tokenB.RegisterGameObjectBroadcast(
                source,
                (ref SimpleBroadcastMessage _) =>
                {
                    secondCount++;
                }
            );

            SimpleBroadcastMessage msg = new();
            msg.EmitGameObjectBroadcast(source);

            Assert.AreEqual(
                1,
                firstCount,
                "First GameObject-sourced broadcast handler should run once."
            );
            Assert.AreEqual(
                0,
                secondCount,
                "Second GameObject-sourced broadcast handler must not act after destruction."
            );
            yield break;
        }

        [UnityTest]
        public IEnumerator BroadcastComponentDestroyOtherListenerDoesNotRun()
        {
            GameObject a = new(
                nameof(BroadcastComponentDestroyOtherListenerDoesNotRun) + "_A",
                typeof(EmptyMessageAwareComponent)
            );
            GameObject b = new(
                nameof(BroadcastComponentDestroyOtherListenerDoesNotRun) + "_B",
                typeof(EmptyMessageAwareComponent)
            );
            GameObject sourceGo = new(
                nameof(BroadcastComponentDestroyOtherListenerDoesNotRun) + "_Source",
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(a);
            _spawned.Add(b);
            _spawned.Add(sourceGo);

            EmptyMessageAwareComponent compA = a.GetComponent<EmptyMessageAwareComponent>();
            EmptyMessageAwareComponent compB = b.GetComponent<EmptyMessageAwareComponent>();
            EmptyMessageAwareComponent sourceComp =
                sourceGo.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken tokenA = GetToken(compA);
            MessageRegistrationToken tokenB = GetToken(compB);

            int firstCount = 0;
            int secondCount = 0;

            _ = tokenA.RegisterComponentBroadcast(
                sourceComp,
                (ref SimpleBroadcastMessage _) =>
                {
                    firstCount++;
                    Object.Destroy(b);
                },
                DestroyerPriority
            );

            _ = tokenB.RegisterComponentBroadcast(
                sourceComp,
                (ref SimpleBroadcastMessage _) =>
                {
                    secondCount++;
                }
            );

            SimpleBroadcastMessage msg = new();
            msg.EmitComponentBroadcast(sourceComp);

            Assert.AreEqual(
                1,
                firstCount,
                "First Component-sourced broadcast handler should run once."
            );
            Assert.AreEqual(
                0,
                secondCount,
                "Second Component-sourced broadcast handler must not act after destruction."
            );
            yield break;
        }

        [UnityTest]
        public IEnumerator BroadcastWithoutSourceDestroyOtherListenerDoesNotRun()
        {
            GameObject a = new(
                nameof(BroadcastWithoutSourceDestroyOtherListenerDoesNotRun) + "_A",
                typeof(EmptyMessageAwareComponent)
            );
            GameObject b = new(
                nameof(BroadcastWithoutSourceDestroyOtherListenerDoesNotRun) + "_B",
                typeof(EmptyMessageAwareComponent)
            );
            GameObject source = new(
                nameof(BroadcastWithoutSourceDestroyOtherListenerDoesNotRun) + "_Source",
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(a);
            _spawned.Add(b);
            _spawned.Add(source);

            EmptyMessageAwareComponent compA = a.GetComponent<EmptyMessageAwareComponent>();
            EmptyMessageAwareComponent compB = b.GetComponent<EmptyMessageAwareComponent>();
            EmptyMessageAwareComponent sourceComp =
                source.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken tokenA = GetToken(compA);
            MessageRegistrationToken tokenB = GetToken(compB);

            int firstCount = 0;
            int secondCount = 0;

            _ = tokenA.RegisterBroadcastWithoutSource(
                (ref InstanceId _, ref SimpleBroadcastMessage _) =>
                {
                    firstCount++;
                    Object.Destroy(b);
                },
                DestroyerPriority
            );

            _ = tokenB.RegisterBroadcastWithoutSource(
                (ref InstanceId _, ref SimpleBroadcastMessage _) =>
                {
                    secondCount++;
                }
            );

            SimpleBroadcastMessage msg = new();
            msg.EmitComponentBroadcast(sourceComp);

            Assert.AreEqual(
                1,
                firstCount,
                "First broadcast-without-source handler should run once."
            );
            Assert.AreEqual(
                0,
                secondCount,
                "Second broadcast-without-source handler must not act after destruction."
            );
            yield break;
        }
    }
}

#endif
