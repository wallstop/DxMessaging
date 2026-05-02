#if UNITY_2021_3_OR_NEWER
namespace DxMessaging.Tests.Runtime.Core
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using DxMessaging.Core;
    using DxMessaging.Core.MessageBus;
    using DxMessaging.Tests.Runtime;
    using NUnit.Framework;
    using UnityEngine.TestTools;

    public sealed class TestAttributeContractTests
    {
        [Test]
        public void UnityTestsDoNotUseTestCaseAttributes()
        {
            List<string> offenders = FindMethods(method =>
                    HasAttribute<UnityTestAttribute>(method)
                    && (
                        HasAttribute<TestCaseAttribute>(method)
                        || HasAttribute<TestCaseSourceAttribute>(method)
                    )
                )
                .Select(FormatMethod)
                .ToList();

            Assert.That(
                offenders,
                Is.Empty,
                "Found [UnityTest] methods decorated with [TestCase] or [TestCaseSource]. Use [ValueSource] for parameterized coroutine tests.\n"
                    + string.Join("\n", offenders)
            );
        }

        [Test]
        public void NonUnityTestsDoNotReturnIEnumerator()
        {
            List<string> offenders = FindMethods(method =>
                    method.ReturnType == typeof(IEnumerator)
                    && !HasAttribute<UnityTestAttribute>(method)
                    && (
                        HasAttribute<TestAttribute>(method)
                        || HasAttribute<TestCaseAttribute>(method)
                        || HasAttribute<TestCaseSourceAttribute>(method)
                    )
                )
                .Select(FormatMethod)
                .ToList();

            Assert.That(
                offenders,
                Is.Empty,
                "Found non-[UnityTest] methods returning IEnumerator. Use [UnityTest] for coroutine tests.\n"
                    + string.Join("\n", offenders)
            );
        }

        [Test]
        public void UnityTestsReturnIEnumerator()
        {
            List<string> offenders = FindMethods(method =>
                    HasAttribute<UnityTestAttribute>(method)
                    && method.ReturnType != typeof(IEnumerator)
                )
                .Select(FormatMethod)
                .ToList();

            Assert.That(
                offenders,
                Is.Empty,
                "Found [UnityTest] methods that do not return IEnumerator.\n"
                    + string.Join("\n", offenders)
            );
        }

        /// <summary>
        /// Flags TRUE TRIPLETS: <c>[UnityTest]</c> methods whose names start
        /// with <c>Untargeted</c>, <c>Targeted</c>, AND <c>Broadcast</c> for
        /// the same kind-stripped base name within a single fixture, none of
        /// which are parameterized over <see cref="MessageScenario"/>. Such
        /// triplets should be consolidated via
        /// <c>[ValueSource(typeof(MessageScenarios), nameof(MessageScenarios.AllKinds))]</c>.
        /// Methods that exist in only one or two kind variants (legitimate
        /// kind-asymmetric tests, like <c>RemoveRegistrationInsideUntargetedHandler</c>)
        /// are intentionally not flagged, because their counterpart kinds
        /// either do not exist or test materially different mechanics.
        /// Kind-specific fixtures named <c>*Specific*Tests</c> (for example
        /// <c>EmitUntargetedSpecificTests</c>) are exempt because their
        /// assertion semantics do not translate across kinds. The contract
        /// pin lives in the <c>tests-must-be-parameterized-by-message-kind</c>
        /// skill.
        /// </summary>
        [Test]
        public void TripletEmitTestsUseScenarioParameterization()
        {
            // Group [UnityTest] methods by their declaring fixture and by the
            // kind-stripped base name. A "triplet" is a base name that has
            // Untargeted, Targeted, AND Broadcast siblings in the same
            // fixture, none of which already accept a MessageScenario
            // parameter. Already-consolidated methods short-circuit out so
            // they cannot accidentally satisfy the triplet criterion.
            Dictionary<Type, Dictionary<string, HashSet<string>>> tripletsByFixture = new();

            foreach (MethodInfo method in GetRuntimeTestMethods())
            {
                if (!HasAttribute<UnityTestAttribute>(method))
                {
                    continue;
                }

                Type fixture = method.DeclaringType;
                if (fixture == null)
                {
                    continue;
                }

                if (fixture.Name.IndexOf("Specific", StringComparison.Ordinal) >= 0)
                {
                    // Kind-specific fixtures are exempt by design.
                    continue;
                }

                string name = method.Name;
                string kind;
                string kindStripped;

                if (name.StartsWith("Untargeted", StringComparison.Ordinal))
                {
                    kind = "Untargeted";
                    kindStripped = name.Substring("Untargeted".Length);
                }
                else if (name.StartsWith("Targeted", StringComparison.Ordinal))
                {
                    kind = "Targeted";
                    kindStripped = name.Substring("Targeted".Length);
                }
                else if (name.StartsWith("Broadcast", StringComparison.Ordinal))
                {
                    kind = "Broadcast";
                    kindStripped = name.Substring("Broadcast".Length);
                }
                else
                {
                    continue;
                }

                if (HasMessageScenarioParameter(method))
                {
                    // Already consolidated; do not contribute to triplet bucket.
                    continue;
                }

                if (
                    !tripletsByFixture.TryGetValue(
                        fixture,
                        out Dictionary<string, HashSet<string>> nameMap
                    )
                )
                {
                    nameMap = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
                    tripletsByFixture[fixture] = nameMap;
                }

                if (!nameMap.TryGetValue(kindStripped, out HashSet<string> kindsSet))
                {
                    kindsSet = new HashSet<string>(StringComparer.Ordinal);
                    nameMap[kindStripped] = kindsSet;
                }

                kindsSet.Add(kind);
            }

            // Triplets intentionally not consolidated due to kind-asymmetric behavior.
            // Each entry must include a justification comment explaining why
            // consolidation is unsafe. Future maintainers should be able to remove
            // an exemption if they consolidate the triplet later.
            HashSet<string> exemptedTriplets = new HashSet<string>(StringComparer.Ordinal)
            {
                // NominalTests.RemoveOrder: the Untargeted variant exercises three
                // Run blocks, while Targeted and Broadcast each exercise five Run
                // blocks (extra ComponentTargeted/ComponentBroadcast permutations
                // only available for those kinds). The bodies are not structurally
                // identical, and consolidating would weaken the kind-asymmetric
                // coverage the longer variants provide.
                "DxMessaging.Tests.Runtime.Core.NominalTests.RemoveOrder",
                // OrderingManyRegistrationsTests.PostProcessorsManyRegistrationsMaintainOrder:
                // the Untargeted variant registers only fast post-processors with
                // a single ordering list, while Targeted and Broadcast register
                // both fast and action post-processors with two lists. The number
                // of register loops and assertion shape differs across kinds, so
                // consolidation would either drop assertions or test a code path
                // (action post-processors) that is not exercised today on the
                // untargeted bus.
                "DxMessaging.Tests.Runtime.Core.OrderingManyRegistrationsTests.PostProcessorsManyRegistrationsMaintainOrder",
                // RegistrationTests.Interceptor: the Untargeted variant emits via
                // a single EmitUntargeted path, while Targeted and Broadcast each
                // exercise BOTH the GameObject-targeted and Component-targeted
                // (or GameObject-broadcast and Component-broadcast) emit paths in
                // the post-deregistration assertion to prove deregistration applies
                // across both targeting/source variants. Consolidation would
                // reduce the targeted/broadcast assertions to a single emit path.
                "DxMessaging.Tests.Runtime.Core.RegistrationTests.Interceptor",
            };

            List<string> offenders = new();
            foreach (
                KeyValuePair<
                    Type,
                    Dictionary<string, HashSet<string>>
                > fixturePair in tripletsByFixture
            )
            {
                foreach (KeyValuePair<string, HashSet<string>> namePair in fixturePair.Value)
                {
                    if (namePair.Value.Count == 3)
                    {
                        string fullKey = $"{fixturePair.Key.FullName}.{namePair.Key}";
                        if (exemptedTriplets.Contains(fullKey))
                        {
                            continue;
                        }

                        offenders.Add(
                            $"{fixturePair.Key.FullName}: triplet '*{namePair.Key}' "
                                + "(Untargeted, Targeted, Broadcast variants all exist; "
                                + "consolidate via [ValueSource(typeof(MessageScenarios), nameof(MessageScenarios.AllKinds))])"
                        );
                    }
                }
            }

            Assert.That(
                offenders,
                Is.Empty,
                "Found triplet [UnityTest] methods (Untargeted/Targeted/Broadcast variants of the same base name in the same fixture) "
                    + "that are not parameterized by MessageScenario. Consolidate via "
                    + "[ValueSource(typeof(MessageScenarios), nameof(MessageScenarios.AllKinds))]. See "
                    + ".llm/skills/testing/tests-must-be-parameterized-by-message-kind.md. Offenders:\n"
                    + string.Join("\n", offenders)
            );
        }

        private static bool HasMessageScenarioParameter(MethodInfo method)
        {
            return method
                .GetParameters()
                .Any(parameter => parameter.ParameterType == typeof(MessageScenario));
        }

        /// <summary>
        /// Pins the allocation-coverage contract: every value of
        /// <see cref="MessageKind"/> must be represented in
        /// <see cref="MessageScenarios.AllKinds"/>. Adding a new kind without
        /// updating the scenario source - and therefore the allocation matrix
        /// that consumes it - will trip this guard. The contract pin lives in
        /// the <c>allocation-coverage-required-for-dispatch</c> skill.
        /// </summary>
        [Test]
        public void EveryEmitPathHasAllocationCoverage()
        {
            HashSet<MessageKind> covered = new(MessageScenarios.AllKinds.Select(s => s.Kind));
            List<string> missing = new();

            foreach (MessageKind kind in Enum.GetValues(typeof(MessageKind)))
            {
                if (!covered.Contains(kind))
                {
                    missing.Add(kind.ToString());
                }
            }

            Assert.That(
                missing,
                Is.Empty,
                "MessageScenarios.AllKinds must yield every MessageKind so the allocation matrix "
                    + "and parameterized tests cover all dispatch paths. Missing kinds: "
                    + string.Join(", ", missing)
                    + ". See .llm/skills/testing/allocation-coverage-required-for-dispatch.md."
            );
        }

        /// <summary>
        /// Smoke-checks that <see cref="DxMessagingStaticState.Reset"/> - which
        /// every <c>MessagingTestBase.Setup</c> invokes - actually wipes the
        /// global bus counters back to zero. If a future change splits the
        /// reset into pieces this guard will fail before contaminated state
        /// leaks into the rest of the suite.
        /// </summary>
        [Test]
        public void DxMessagingStaticStateResetClearsBusCounts()
        {
            // Pollute the global bus so we can prove Reset clears it.
            MessageHandler pollutingHandler = new(
                new InstanceId(unchecked((int)0x517E0001)),
                MessageHandler.MessageBus
            )
            {
                active = true,
            };
            MessageRegistrationToken pollutingToken = MessageRegistrationToken.Create(
                pollutingHandler,
                MessageHandler.MessageBus
            );
            _ =
                pollutingToken.RegisterUntargeted<DxMessaging.Tests.Runtime.Scripts.Messages.SimpleUntargetedMessage>(
                    (
                        ref DxMessaging.Tests.Runtime.Scripts.Messages.SimpleUntargetedMessage _
                    ) => { }
                );
            pollutingToken.Enable();

            try
            {
                DxMessagingStaticState.Reset();

                IMessageBus bus = MessageHandler.MessageBus;
                Assert.IsNotNull(bus, "MessageHandler.MessageBus must not be null after Reset.");
                Assert.Zero(bus.RegisteredUntargeted, "Setup must leave Untargeted count at zero.");
                Assert.Zero(bus.RegisteredTargeted, "Setup must leave Targeted count at zero.");
                Assert.Zero(bus.RegisteredBroadcast, "Setup must leave Broadcast count at zero.");
            }
            finally
            {
                // Best-effort cleanup; Reset above already cleared the bus,
                // but disposing the token is a no-op on a fresh state.
                pollutingToken.Disable();
                DxMessagingStaticState.Reset();
            }
        }

        private static IEnumerable<MethodInfo> FindMethods(Func<MethodInfo, bool> predicate)
        {
            return GetRuntimeTestMethods().Where(predicate);
        }

        private static IEnumerable<MethodInfo> GetRuntimeTestMethods()
        {
            Assembly assembly = typeof(TestAttributeContractTests).Assembly;
            BindingFlags methodFlags =
                BindingFlags.Instance
                | BindingFlags.Static
                | BindingFlags.Public
                | BindingFlags.NonPublic;

            foreach (Type type in assembly.GetTypes())
            {
                if (
                    type.Namespace == null
                    || !type.Namespace.StartsWith(
                        "DxMessaging.Tests.Runtime",
                        StringComparison.Ordinal
                    )
                )
                {
                    continue;
                }

                foreach (MethodInfo method in type.GetMethods(methodFlags))
                {
                    if (method.IsSpecialName)
                    {
                        continue;
                    }

                    bool isTestMethod =
                        HasAttribute<TestAttribute>(method)
                        || HasAttribute<UnityTestAttribute>(method)
                        || HasAttribute<TestCaseAttribute>(method)
                        || HasAttribute<TestCaseSourceAttribute>(method);
                    // ValueSource is parameter data only and is always paired with a test-defining attribute.

                    if (isTestMethod)
                    {
                        yield return method;
                    }
                }
            }
        }

        private static bool HasAttribute<TAttribute>(MemberInfo method)
            where TAttribute : Attribute
        {
            return method.GetCustomAttributes(typeof(TAttribute), inherit: false).Length > 0;
        }

        private static string FormatMethod(MethodInfo method)
        {
            Type declaringType = method.DeclaringType;
            string declaringTypeName = declaringType == null ? "<unknown>" : declaringType.FullName;
            return $"{declaringTypeName}.{method.Name} returns {method.ReturnType.FullName}";
        }
    }
}

#endif
