#if UNITY_2021_3_OR_NEWER
namespace DxMessaging.Tests.Editor.Benchmarks
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using DxMessaging.Tests.Runtime.Benchmarks;
    using NUnit.Framework;

    public sealed class PerfRegressionSmokeTests
    {
        private const string PerfGateEnvVar = "DX_PERF_GATE";
        private const string BaselinePath = "progress/perf-baseline-2026-05-05.csv";
        private const string BaselineCommit = "25a4dcc";
        private const double RegressionMultiplier = 1.5d;

        [Test, Explicit, Category("PerfGate")]
        public void UntargetedFloodOneHandler()
        {
            RunGate(DispatchBenchmarkScenario.UntargetedFloodOneHandler);
        }

        [Test, Explicit, Category("PerfGate")]
        public void UntargetedFloodFourHandlersOnePriority()
        {
            RunGate(DispatchBenchmarkScenario.UntargetedFloodFourHandlersOnePriority);
        }

        [Test, Explicit, Category("PerfGate")]
        public void UntargetedFloodFourHandlersFourPriorities()
        {
            RunGate(DispatchBenchmarkScenario.UntargetedFloodFourHandlersFourPriorities);
        }

        [Test, Explicit, Category("PerfGate")]
        public void TargetedFloodOneListener()
        {
            RunGate(DispatchBenchmarkScenario.TargetedFloodOneListener);
        }

        [Test, Explicit, Category("PerfGate")]
        public void TargetedFloodSixteenListeners()
        {
            RunGate(DispatchBenchmarkScenario.TargetedFloodSixteenListeners);
        }

        [Test, Explicit, Category("PerfGate")]
        public void BroadcastFloodOneHandler()
        {
            RunGate(DispatchBenchmarkScenario.BroadcastFloodOneHandler);
        }

        [Test, Explicit, Category("PerfGate")]
        public void InterceptorHeavyFourInterceptors()
        {
            RunGate(DispatchBenchmarkScenario.InterceptorHeavyFourInterceptors);
        }

        [Test, Explicit, Category("PerfGate")]
        public void PostProcessingHeavyFourPostProcessors()
        {
            RunGate(DispatchBenchmarkScenario.PostProcessingHeavyFourPostProcessors);
        }

        [Test, Explicit, Category("PerfGate")]
        public void RegistrationFlood1000TypesFromColdBus()
        {
            RunGate(DispatchBenchmarkScenario.RegistrationFlood1000TypesFromColdBus);
        }

        private static void RunGate(DispatchBenchmarkScenario scenario)
        {
            if (Environment.GetEnvironmentVariable(PerfGateEnvVar) != "1")
            {
                Assert.Ignore($"{PerfGateEnvVar}=1 is required to run the perf smoke gate.");
            }

            DispatchBenchmarkResult current = DispatchThroughputBenchmarks.RunScenario(scenario);
            IReadOnlyList<BaselineRow> baselines = LoadBaselines();
            string scenarioName = DispatchThroughputBenchmarks.GetScenarioName(scenario);
            BaselineRow baseline = FindBaseline(baselines, scenarioName, current.Platform);

            if (current.IsRegistrationScenario)
            {
                Assert.LessOrEqual(
                    current.WallClockMs,
                    baseline.WallClockMs * RegressionMultiplier,
                    $"{scenarioName} registration wall-clock regressed more than {RegressionMultiplier:0.0}x."
                );
                return;
            }

            double minimumAllowedEmitsPerSecond = baseline.EmitsPerSecond / RegressionMultiplier;
            Assert.GreaterOrEqual(
                current.EmitsPerSecond,
                minimumAllowedEmitsPerSecond,
                $"{scenarioName} throughput regressed more than {RegressionMultiplier:0.0}x."
            );

            long allocationBudgetBytes = Math.Max(0, baseline.AllocatedBytesDelta);
            Assert.LessOrEqual(
                current.AllocatedBytesDelta,
                allocationBudgetBytes,
                $"{scenarioName} allocated {current.AllocatedBytesDelta.ToString(CultureInfo.InvariantCulture)} bytes, exceeding the baseline allocation budget of {allocationBudgetBytes.ToString(CultureInfo.InvariantCulture)} bytes."
            );
        }

        private static IReadOnlyList<BaselineRow> LoadBaselines()
        {
            string path = FindRepoRelativePath(BaselinePath);
            if (!File.Exists(path))
            {
                Assert.Ignore(
                    $"Performance baseline file not found: {BaselinePath}. Capture T0.3 baselines before enforcing PerfGate."
                );
            }

            List<BaselineRow> rows = new();
            foreach (string line in File.ReadAllLines(path))
            {
                if (
                    string.IsNullOrWhiteSpace(line)
                    || line.StartsWith("scenario,", StringComparison.OrdinalIgnoreCase)
                )
                {
                    continue;
                }

                rows.Add(BaselineRow.Parse(line));
            }

            Assert.Greater(rows.Count, 0, "Performance baseline file contains no data rows.");
            return rows;
        }

        private static BaselineRow FindBaseline(
            IReadOnlyList<BaselineRow> rows,
            string scenario,
            string platform
        )
        {
            for (int index = 0; index < rows.Count; index++)
            {
                BaselineRow row = rows[index];
                if (
                    string.Equals(row.Scenario, scenario, StringComparison.Ordinal)
                    && string.Equals(row.Platform, platform, StringComparison.Ordinal)
                    && string.Equals(row.Commit, BaselineCommit, StringComparison.OrdinalIgnoreCase)
                )
                {
                    return row;
                }
            }

            Assert.Fail(
                $"No {BaselineCommit} baseline row found for scenario {scenario} on platform {platform}."
            );
            return default;
        }

        private static string FindRepoRelativePath(string relativePath)
        {
            DirectoryInfo current = new(Directory.GetCurrentDirectory());
            while (current != null)
            {
                string candidate = Path.Combine(current.FullName, relativePath);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                current = current.Parent;
            }

            return Path.Combine(Directory.GetCurrentDirectory(), relativePath);
        }

        private readonly struct BaselineRow
        {
            private BaselineRow(
                string scenario,
                string platform,
                string commit,
                double emitsPerSecond,
                long allocatedBytesDelta,
                double wallClockMs
            )
            {
                Scenario = scenario;
                Platform = platform;
                Commit = commit;
                EmitsPerSecond = emitsPerSecond;
                AllocatedBytesDelta = allocatedBytesDelta;
                WallClockMs = wallClockMs;
            }

            public string Scenario { get; }

            public string Platform { get; }

            public string Commit { get; }

            public double EmitsPerSecond { get; }

            public long AllocatedBytesDelta { get; }

            public double WallClockMs { get; }

            public static BaselineRow Parse(string line)
            {
                string[] parts = ParseCsvFields(line);
                if (parts.Length < 7)
                {
                    throw new FormatException($"Invalid baseline row: {line}");
                }

                return new BaselineRow(
                    parts[0],
                    parts[1],
                    parts[2],
                    double.Parse(parts[4], CultureInfo.InvariantCulture),
                    long.Parse(parts[5], CultureInfo.InvariantCulture),
                    double.Parse(parts[6], CultureInfo.InvariantCulture)
                );
            }

            private static string[] ParseCsvFields(string line)
            {
                List<string> fields = new();
                System.Text.StringBuilder builder = new();
                bool inQuotes = false;

                for (int index = 0; index < line.Length; index++)
                {
                    char value = line[index];
                    if (value == '"')
                    {
                        if (inQuotes && index + 1 < line.Length && line[index + 1] == '"')
                        {
                            builder.Append('"');
                            index++;
                            continue;
                        }

                        inQuotes = !inQuotes;
                        continue;
                    }

                    if (value == ',' && !inQuotes)
                    {
                        fields.Add(builder.ToString());
                        builder.Clear();
                        continue;
                    }

                    builder.Append(value);
                }

                fields.Add(builder.ToString());
                return fields.ToArray();
            }
        }
    }
}
#endif
