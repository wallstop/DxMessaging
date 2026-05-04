#if UNITY_2021_3_OR_NEWER
namespace DxMessaging.Tests.Runtime.Core
{
    using System;
    using System.Collections;
    using System.Diagnostics;
    using DxMessaging.Core;
    using DxMessaging.Core.Extensions;
    using NUnit.Framework;
    using Scripts.Components;
    using Scripts.Messages;
    using UnityEngine;
    using UnityEngine.TestTools;

    /// <summary>
    /// In-default-suite speed guard rail. The default Unity Edit + Play mode
    /// test run is supposed to finish in under a minute once the gated suites
    /// are filtered out. The companion <see cref="SuiteWallClockBudgetTest"/>
    /// fixture measures the runtime assembly wall clock directly; this test
    /// keeps a small representative workload in the default suite so local
    /// per-test regressions fail close to the changed code.
    /// </summary>
    /// <remarks>
    /// This test runs as part of the default Unity test suite - it is a fast
    /// guard rail that fails when local performance regresses below the
    /// 60-second whole-suite budget. The category scheme used across the rest
    /// of the test suite is:
    /// <list type="bullet">
    /// <item><description><c>Stress</c> - high-volume registration / emission tests.</description></item>
    /// <item><description><c>Performance</c> - throughput / latency benchmarks.</description></item>
    /// <item><description><c>Allocation</c> - the zero-GC matrix.</description></item>
    /// <item><description><c>MemoryReclaim</c> - explicit trim and idle-sweep reclamation tests.</description></item>
    /// <item><description><c>UnityRuntime</c> - Unity-only runtime lifecycle tests.</description></item>
    /// </list>
    /// CI runs the default suite (tests outside the gated categories, including
    /// this guard rail) on every PR; the gated categories are opt-in.
    /// </remarks>
    public sealed class SuiteSpeedBudgetTest : MessagingTestBase
    {
        private const int RepresentativeCycles = 100;
        private static readonly TimeSpan RepresentativeBudget = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Measures a representative default-suite registration / emit /
        /// deregistration workload under a small per-test budget.
        /// </summary>
        [UnityTest]
        public IEnumerator RepresentativeSubsetCompletesUnderBudget()
        {
            GameObject host = new(
                nameof(RepresentativeSubsetCompletesUnderBudget),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(host);
            EmptyMessageAwareComponent component = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);

            // Warm up so JIT and bus internals do not skew the measurement.
            MessageRegistrationHandle warmupHandle =
                token.RegisterUntargeted<SimpleUntargetedMessage>(_ => { });
            SimpleUntargetedMessage warmup = new();
            warmup.EmitUntargeted();
            token.RemoveRegistration(warmupHandle);

            int total = 0;
            Stopwatch timer = Stopwatch.StartNew();
            for (int round = 0; round < RepresentativeCycles; ++round)
            {
                MessageRegistrationHandle handle =
                    token.RegisterUntargeted<SimpleUntargetedMessage>(_ => ++total);
                SimpleUntargetedMessage message = new();
                message.EmitUntargeted();
                token.RemoveRegistration(handle);
            }

            timer.Stop();

            Assert.AreEqual(
                RepresentativeCycles,
                total,
                "Representative cycle count drifted; the speed budget proxy is no longer representative."
            );
            Assert.That(
                timer.Elapsed,
                Is.LessThan(RepresentativeBudget),
                $"Representative load took {timer.Elapsed.TotalSeconds:0.00}s "
                    + $"(budget: {RepresentativeBudget.TotalSeconds:0.00}s). "
                    + "If this regresses, the default Unity Edit+Play suite is likely to exceed the 60s budget."
            );
            yield break;
        }
    }
}

#endif
