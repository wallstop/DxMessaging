#if UNITY_2021_3_OR_NEWER
namespace DxMessaging.Tests.Runtime.Benchmarks
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Reflection;
    using DxMessaging.Core;
    using DxMessaging.Core.MessageBus;
    using DxMessaging.Tests.Runtime.Scripts.Messages;
    using NUnit.Framework;
    using UnityEngine;
    using Debug = UnityEngine.Debug;

    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class PerformanceAttribute : CategoryAttribute
    {
        public PerformanceAttribute()
            : base("Performance") { }
    }

    public enum DispatchBenchmarkScenario
    {
        UntargetedFloodOneHandler,
        UntargetedFloodFourHandlersOnePriority,
        UntargetedFloodFourHandlersFourPriorities,
        TargetedFloodOneListener,
        TargetedFloodSixteenListeners,
        BroadcastFloodOneHandler,
        InterceptorHeavyFourInterceptors,
        PostProcessingHeavyFourPostProcessors,
        RegistrationFlood1000TypesFromColdBus,
    }

    public sealed class DispatchThroughputBenchmarks
    {
        private const int WarmupEmits = 10_000;
        private const int MedianRuns = 5;
        private static readonly TimeSpan MeasurementWindow = TimeSpan.FromSeconds(1);
        private static readonly long MeasurementWindowTicks = (long)(
            Stopwatch.Frequency * MeasurementWindow.TotalSeconds
        );
        private static readonly InstanceId Target = new(31001);
        private static readonly InstanceId Source = new(31002);
        private static Action<MessageRegistrationToken>[] _registrationFloodBuilders;

        [Test, Performance, Category("PerfBench")]
        public void UntargetedFloodOneHandler()
        {
            _ = RunScenario(DispatchBenchmarkScenario.UntargetedFloodOneHandler);
        }

        [Test, Performance, Category("PerfBench")]
        public void UntargetedFloodFourHandlersOnePriority()
        {
            _ = RunScenario(DispatchBenchmarkScenario.UntargetedFloodFourHandlersOnePriority);
        }

        [Test, Performance, Category("PerfBench")]
        public void UntargetedFloodFourHandlersFourPriorities()
        {
            _ = RunScenario(DispatchBenchmarkScenario.UntargetedFloodFourHandlersFourPriorities);
        }

        [Test, Performance, Category("PerfBench")]
        public void TargetedFloodOneListener()
        {
            _ = RunScenario(DispatchBenchmarkScenario.TargetedFloodOneListener);
        }

        [Test, Performance, Category("PerfBench")]
        public void TargetedFloodSixteenListeners()
        {
            _ = RunScenario(DispatchBenchmarkScenario.TargetedFloodSixteenListeners);
        }

        [Test, Performance, Category("PerfBench")]
        public void BroadcastFloodOneHandler()
        {
            _ = RunScenario(DispatchBenchmarkScenario.BroadcastFloodOneHandler);
        }

        [Test, Performance, Category("PerfBench")]
        public void InterceptorHeavyFourInterceptors()
        {
            _ = RunScenario(DispatchBenchmarkScenario.InterceptorHeavyFourInterceptors);
        }

        [Test, Performance, Category("PerfBench")]
        public void PostProcessingHeavyFourPostProcessors()
        {
            _ = RunScenario(DispatchBenchmarkScenario.PostProcessingHeavyFourPostProcessors);
        }

        [Test, Performance, Category("PerfBench")]
        public void RegistrationFlood1000TypesFromColdBus()
        {
            _ = RunScenario(DispatchBenchmarkScenario.RegistrationFlood1000TypesFromColdBus);
        }

        public static DispatchBenchmarkResult RunScenario(
            DispatchBenchmarkScenario scenario,
            bool logResult = true
        )
        {
            DispatchBenchmarkResult[] runs = new DispatchBenchmarkResult[MedianRuns];
            for (int runIndex = 0; runIndex < runs.Length; runIndex++)
            {
                runs[runIndex] =
                    scenario == DispatchBenchmarkScenario.RegistrationFlood1000TypesFromColdBus
                        ? MeasureRegistrationFlood(runIndex)
                        : MeasureEmitScenario(scenario, runIndex);
            }

            DispatchBenchmarkResult median = MedianByPrimaryMetric(runs);
            if (logResult)
            {
                Debug.Log(median.ToStructuredLog());
                TestContext.Out.WriteLine(median.ToCsvRow());
            }

            return median;
        }

        public static string GetScenarioName(DispatchBenchmarkScenario scenario)
        {
            return scenario switch
            {
                DispatchBenchmarkScenario.UntargetedFloodOneHandler => "UntargetedFlood_OneHandler",
                DispatchBenchmarkScenario.UntargetedFloodFourHandlersOnePriority =>
                    "UntargetedFlood_FourHandlers_OnePriority",
                DispatchBenchmarkScenario.UntargetedFloodFourHandlersFourPriorities =>
                    "UntargetedFlood_FourHandlers_FourPriorities",
                DispatchBenchmarkScenario.TargetedFloodOneListener => "TargetedFlood_OneListener",
                DispatchBenchmarkScenario.TargetedFloodSixteenListeners =>
                    "TargetedFlood_SixteenListeners",
                DispatchBenchmarkScenario.BroadcastFloodOneHandler => "BroadcastFlood_OneHandler",
                DispatchBenchmarkScenario.InterceptorHeavyFourInterceptors =>
                    "InterceptorHeavy_FourInterceptors",
                DispatchBenchmarkScenario.PostProcessingHeavyFourPostProcessors =>
                    "PostProcessingHeavy_FourPostProcessors",
                DispatchBenchmarkScenario.RegistrationFlood1000TypesFromColdBus =>
                    "RegistrationFlood_1000Types_FromColdBus",
                _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null),
            };
        }

        private static DispatchBenchmarkResult MeasureEmitScenario(
            DispatchBenchmarkScenario scenario,
            int runIndex
        )
        {
            using BenchmarkRegistrationScope scope = new();
            InvocationCounter handlerInvocations = new();
            ConfigureScenario(scope, scenario, handlerInvocations);

            EmitMany(scope.Bus, scenario, WarmupEmits);

            long beforeAllocatedBytes = GC.GetAllocatedBytesForCurrentThread();
            long startTimestamp = Stopwatch.GetTimestamp();
            long endTimestamp = startTimestamp;
            long emits = 0;
            do
            {
                EmitMany(scope.Bus, scenario, WarmupEmits);
                emits += WarmupEmits;
                endTimestamp = Stopwatch.GetTimestamp();
            } while (endTimestamp - startTimestamp < MeasurementWindowTicks);
            long afterAllocatedBytes = GC.GetAllocatedBytesForCurrentThread();

            Assert.Greater(
                handlerInvocations.Count,
                0,
                "Benchmark scenario did not invoke handlers."
            );
            double elapsedSeconds = TimestampDeltaToSeconds(startTimestamp, endTimestamp);
            double emitsPerSecond = emits / Math.Max(elapsedSeconds, double.Epsilon);
            return DispatchBenchmarkResult.ForEmitScenario(
                GetScenarioName(scenario),
                runIndex,
                emitsPerSecond,
                afterAllocatedBytes - beforeAllocatedBytes,
                elapsedSeconds * 1000d
            );
        }

        private static DispatchBenchmarkResult MeasureRegistrationFlood(int runIndex)
        {
            Action<MessageRegistrationToken>[] builders = GetRegistrationFloodBuilders();
            long beforeAllocatedBytes = GC.GetAllocatedBytesForCurrentThread();
            long startTimestamp = Stopwatch.GetTimestamp();
            using (BenchmarkRegistrationScope scope = new())
            {
                for (int index = 0; index < builders.Length; index++)
                {
                    builders[index](scope.PrimaryToken);
                }
            }
            long endTimestamp = Stopwatch.GetTimestamp();
            long afterAllocatedBytes = GC.GetAllocatedBytesForCurrentThread();

            return DispatchBenchmarkResult.ForRegistrationScenario(
                GetScenarioName(DispatchBenchmarkScenario.RegistrationFlood1000TypesFromColdBus),
                runIndex,
                afterAllocatedBytes - beforeAllocatedBytes,
                TimestampDeltaToSeconds(startTimestamp, endTimestamp) * 1000d
            );
        }

        private static double TimestampDeltaToSeconds(long startTimestamp, long endTimestamp)
        {
            return (endTimestamp - startTimestamp) / (double)Stopwatch.Frequency;
        }

        private static void ConfigureScenario(
            BenchmarkRegistrationScope scope,
            DispatchBenchmarkScenario scenario,
            InvocationCounter handlerInvocations
        )
        {
            switch (scenario)
            {
                case DispatchBenchmarkScenario.UntargetedFloodOneHandler:
                    RegisterUntargeted(scope, handlerInvocations, 0);
                    return;
                case DispatchBenchmarkScenario.UntargetedFloodFourHandlersOnePriority:
                    for (int index = 0; index < 4; index++)
                    {
                        RegisterUntargeted(scope, handlerInvocations, 0);
                    }
                    return;
                case DispatchBenchmarkScenario.UntargetedFloodFourHandlersFourPriorities:
                    for (int priority = 0; priority < 4; priority++)
                    {
                        RegisterUntargeted(scope, handlerInvocations, priority);
                    }
                    return;
                case DispatchBenchmarkScenario.TargetedFloodOneListener:
                    RegisterTargeted(scope, handlerInvocations, 0);
                    return;
                case DispatchBenchmarkScenario.TargetedFloodSixteenListeners:
                    for (int index = 0; index < 16; index++)
                    {
                        RegisterTargeted(scope, handlerInvocations, 0);
                    }
                    return;
                case DispatchBenchmarkScenario.BroadcastFloodOneHandler:
                    RegisterBroadcast(scope, handlerInvocations, 0);
                    return;
                case DispatchBenchmarkScenario.InterceptorHeavyFourInterceptors:
                    for (int priority = 0; priority < 4; priority++)
                    {
                        _ =
                            scope.PrimaryToken.RegisterUntargetedInterceptor<SimpleUntargetedMessage>(
                                AllowUntargeted,
                                priority
                            );
                    }
                    RegisterUntargeted(scope, handlerInvocations, 0);
                    return;
                case DispatchBenchmarkScenario.PostProcessingHeavyFourPostProcessors:
                    for (int priority = 0; priority < 4; priority++)
                    {
                        _ =
                            scope.PrimaryToken.RegisterUntargetedPostProcessor<SimpleUntargetedMessage>(
                                CountPostProcessed,
                                priority
                            );
                    }
                    RegisterUntargeted(scope, handlerInvocations, 0);
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null);
            }

            void CountPostProcessed(ref SimpleUntargetedMessage message)
            {
                handlerInvocations.Increment();
            }
        }

        private static void RegisterUntargeted(
            BenchmarkRegistrationScope scope,
            InvocationCounter handlerInvocations,
            int priority
        )
        {
            MessageRegistrationToken token = scope.CreateToken();
            _ = token.RegisterUntargeted<SimpleUntargetedMessage>(
                (ref SimpleUntargetedMessage message) => handlerInvocations.Increment(),
                priority
            );
        }

        private static void RegisterTargeted(
            BenchmarkRegistrationScope scope,
            InvocationCounter handlerInvocations,
            int priority
        )
        {
            MessageRegistrationToken token = scope.CreateToken();
            _ = token.RegisterTargeted<SimpleTargetedMessage>(
                Target,
                (ref SimpleTargetedMessage message) => handlerInvocations.Increment(),
                priority
            );
        }

        private static void RegisterBroadcast(
            BenchmarkRegistrationScope scope,
            InvocationCounter handlerInvocations,
            int priority
        )
        {
            MessageRegistrationToken token = scope.CreateToken();
            _ = token.RegisterBroadcast<SimpleBroadcastMessage>(
                Source,
                (ref SimpleBroadcastMessage message) => handlerInvocations.Increment(),
                priority
            );
        }

        private static void EmitMany(MessageBus bus, DispatchBenchmarkScenario scenario, int count)
        {
            switch (scenario)
            {
                case DispatchBenchmarkScenario.UntargetedFloodOneHandler:
                case DispatchBenchmarkScenario.UntargetedFloodFourHandlersOnePriority:
                case DispatchBenchmarkScenario.UntargetedFloodFourHandlersFourPriorities:
                case DispatchBenchmarkScenario.InterceptorHeavyFourInterceptors:
                case DispatchBenchmarkScenario.PostProcessingHeavyFourPostProcessors:
                    SimpleUntargetedMessage untargeted = new();
                    for (int index = 0; index < count; index++)
                    {
                        bus.UntargetedBroadcast(ref untargeted);
                    }
                    return;
                case DispatchBenchmarkScenario.TargetedFloodOneListener:
                case DispatchBenchmarkScenario.TargetedFloodSixteenListeners:
                    SimpleTargetedMessage targeted = new();
                    InstanceId target = Target;
                    for (int index = 0; index < count; index++)
                    {
                        bus.TargetedBroadcast(ref target, ref targeted);
                    }
                    return;
                case DispatchBenchmarkScenario.BroadcastFloodOneHandler:
                    SimpleBroadcastMessage broadcast = new();
                    InstanceId source = Source;
                    for (int index = 0; index < count; index++)
                    {
                        bus.SourcedBroadcast(ref source, ref broadcast);
                    }
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null);
            }
        }

        private static bool AllowUntargeted(ref SimpleUntargetedMessage message)
        {
            return true;
        }

        private static DispatchBenchmarkResult MedianByPrimaryMetric(
            DispatchBenchmarkResult[] results
        )
        {
            DispatchBenchmarkResult[] sorted = (DispatchBenchmarkResult[])results.Clone();
            Array.Sort(
                sorted,
                (left, right) =>
                {
                    int comparison = left.IsRegistrationScenario
                        ? left.WallClockMs.CompareTo(right.WallClockMs)
                        : right.EmitsPerSecond.CompareTo(left.EmitsPerSecond);
                    return comparison != 0 ? comparison : left.RunIndex.CompareTo(right.RunIndex);
                }
            );

            return sorted[sorted.Length / 2].AsMedian();
        }

        private static Action<MessageRegistrationToken>[] GetRegistrationFloodBuilders()
        {
            if (_registrationFloodBuilders != null)
            {
                return _registrationFloodBuilders;
            }

            MethodInfo builderMethod = typeof(DispatchThroughputBenchmarks).GetMethod(
                nameof(RegisterFloodMessage),
                BindingFlags.NonPublic | BindingFlags.Static
            );
            if (builderMethod == null)
            {
                throw new MissingMethodException(nameof(RegisterFloodMessage));
            }

            Type[] markerTypes = RegistrationFloodMarkerTypes.All;
            List<Action<MessageRegistrationToken>> builders = new(capacity: 1000);
            for (int outerIndex = 0; outerIndex < markerTypes.Length; outerIndex++)
            {
                for (int innerIndex = 0; innerIndex < markerTypes.Length; innerIndex++)
                {
                    Type markerType = typeof(RegistrationFloodMarker<,>).MakeGenericType(
                        markerTypes[outerIndex],
                        markerTypes[innerIndex]
                    );
                    MethodInfo closedMethod = builderMethod.MakeGenericMethod(markerType);
                    builders.Add(
                        (Action<MessageRegistrationToken>)
                            Delegate.CreateDelegate(
                                typeof(Action<MessageRegistrationToken>),
                                closedMethod
                            )
                    );
                    if (builders.Count == 1000)
                    {
                        break;
                    }
                }

                if (builders.Count == 1000)
                {
                    break;
                }
            }

            if (builders.Count < 1000)
            {
                throw new InvalidOperationException(
                    $"Expected at least 1000 marker types for the registration flood, found {builders.Count}."
                );
            }

            _registrationFloodBuilders = builders.ToArray();
            return _registrationFloodBuilders;
        }

        private static void RegisterFloodMessage<TMarker>(MessageRegistrationToken token)
        {
            _ = token.RegisterUntargeted<RegistrationFloodMessage<TMarker>>(NoOpFloodHandler);
        }

        private static void NoOpFloodHandler<TMarker>(
            ref RegistrationFloodMessage<TMarker> message
        ) { }

        private readonly struct RegistrationFloodMessage<TMarker>
            : DxMessaging.Core.Messages.IUntargetedMessage { }

        private readonly struct RegistrationFloodMarker<TOuter, TInner> { }

        private static class RegistrationFloodMarkerTypes
        {
            public static readonly Type[] All =
            {
                typeof(Marker00),
                typeof(Marker01),
                typeof(Marker02),
                typeof(Marker03),
                typeof(Marker04),
                typeof(Marker05),
                typeof(Marker06),
                typeof(Marker07),
                typeof(Marker08),
                typeof(Marker09),
                typeof(Marker10),
                typeof(Marker11),
                typeof(Marker12),
                typeof(Marker13),
                typeof(Marker14),
                typeof(Marker15),
                typeof(Marker16),
                typeof(Marker17),
                typeof(Marker18),
                typeof(Marker19),
                typeof(Marker20),
                typeof(Marker21),
                typeof(Marker22),
                typeof(Marker23),
                typeof(Marker24),
                typeof(Marker25),
                typeof(Marker26),
                typeof(Marker27),
                typeof(Marker28),
                typeof(Marker29),
                typeof(Marker30),
                typeof(Marker31),
            };

            private readonly struct Marker00 { }

            private readonly struct Marker01 { }

            private readonly struct Marker02 { }

            private readonly struct Marker03 { }

            private readonly struct Marker04 { }

            private readonly struct Marker05 { }

            private readonly struct Marker06 { }

            private readonly struct Marker07 { }

            private readonly struct Marker08 { }

            private readonly struct Marker09 { }

            private readonly struct Marker10 { }

            private readonly struct Marker11 { }

            private readonly struct Marker12 { }

            private readonly struct Marker13 { }

            private readonly struct Marker14 { }

            private readonly struct Marker15 { }

            private readonly struct Marker16 { }

            private readonly struct Marker17 { }

            private readonly struct Marker18 { }

            private readonly struct Marker19 { }

            private readonly struct Marker20 { }

            private readonly struct Marker21 { }

            private readonly struct Marker22 { }

            private readonly struct Marker23 { }

            private readonly struct Marker24 { }

            private readonly struct Marker25 { }

            private readonly struct Marker26 { }

            private readonly struct Marker27 { }

            private readonly struct Marker28 { }

            private readonly struct Marker29 { }

            private readonly struct Marker30 { }

            private readonly struct Marker31 { }
        }

        private sealed class InvocationCounter
        {
            public int Count { get; private set; }

            public void Increment()
            {
                Count++;
            }
        }

        private sealed class BenchmarkRegistrationScope : IDisposable
        {
            private readonly List<MessageRegistrationToken> _tokens = new();
            private int _nextOwner = 32000;

            public BenchmarkRegistrationScope()
            {
                Bus = new MessageBus();
                PrimaryToken = CreateToken();
            }

            public MessageBus Bus { get; }

            public MessageRegistrationToken PrimaryToken { get; }

            public MessageRegistrationToken CreateToken()
            {
                MessageHandler handler = new(new InstanceId(_nextOwner++), Bus) { active = true };
                MessageRegistrationToken token = MessageRegistrationToken.Create(handler, Bus);
                token.Enable();
                _tokens.Add(token);
                return token;
            }

            public void Dispose()
            {
                for (int index = _tokens.Count - 1; index >= 0; index--)
                {
                    _tokens[index].UnregisterAll();
                    _tokens[index].Dispose();
                }
            }
        }
    }

    public readonly struct DispatchBenchmarkResult
    {
        private DispatchBenchmarkResult(
            string scenario,
            string platform,
            string commit,
            int runIndex,
            double emitsPerSecond,
            long allocatedBytesDelta,
            double wallClockMs,
            bool isRegistrationScenario
        )
        {
            Scenario = scenario;
            Platform = platform;
            Commit = commit;
            RunIndex = runIndex;
            EmitsPerSecond = emitsPerSecond;
            AllocatedBytesDelta = allocatedBytesDelta;
            WallClockMs = wallClockMs;
            IsRegistrationScenario = isRegistrationScenario;
        }

        public string Scenario { get; }

        public string Platform { get; }

        public string Commit { get; }

        public int RunIndex { get; }

        public double EmitsPerSecond { get; }

        public long AllocatedBytesDelta { get; }

        public double WallClockMs { get; }

        public bool IsRegistrationScenario { get; }

        public static DispatchBenchmarkResult ForEmitScenario(
            string scenario,
            int runIndex,
            double emitsPerSecond,
            long allocatedBytesDelta,
            double wallClockMs
        )
        {
            return new DispatchBenchmarkResult(
                scenario,
                ResolvePlatform(),
                ResolveCommit(),
                runIndex,
                emitsPerSecond,
                allocatedBytesDelta,
                wallClockMs,
                isRegistrationScenario: false
            );
        }

        public static DispatchBenchmarkResult ForRegistrationScenario(
            string scenario,
            int runIndex,
            long allocatedBytesDelta,
            double wallClockMs
        )
        {
            return new DispatchBenchmarkResult(
                scenario,
                ResolvePlatform(),
                ResolveCommit(),
                runIndex,
                emitsPerSecond: 0,
                allocatedBytesDelta,
                wallClockMs,
                isRegistrationScenario: true
            );
        }

        public DispatchBenchmarkResult AsMedian()
        {
            return new DispatchBenchmarkResult(
                Scenario,
                Platform,
                Commit,
                runIndex: -1,
                EmitsPerSecond,
                AllocatedBytesDelta,
                WallClockMs,
                IsRegistrationScenario
            );
        }

        public string ToCsvRow()
        {
            return string.Join(
                ",",
                EscapeCsv(Scenario),
                EscapeCsv(Platform),
                EscapeCsv(Commit),
                RunIndex.ToString(CultureInfo.InvariantCulture),
                EmitsPerSecond.ToString("F3", CultureInfo.InvariantCulture),
                AllocatedBytesDelta.ToString(CultureInfo.InvariantCulture),
                WallClockMs.ToString("F3", CultureInfo.InvariantCulture)
            );
        }

        public string ToStructuredLog()
        {
            return "{"
                + $"scenario:\"{Scenario}\", "
                + $"platform:\"{Platform}\", "
                + $"commit:\"{Commit}\", "
                + $"runIndex:{RunIndex.ToString(CultureInfo.InvariantCulture)}, "
                + $"emitsPerSec:{EmitsPerSecond.ToString("F3", CultureInfo.InvariantCulture)}, "
                + $"allocatedBytesDelta:{AllocatedBytesDelta.ToString(CultureInfo.InvariantCulture)}, "
                + $"wallClockMs:{WallClockMs.ToString("F3", CultureInfo.InvariantCulture)}"
                + "}";
        }

        private static string ResolvePlatform()
        {
            return $"{ResolveExecutionTarget()} {ResolveScriptingBackend()} {ResolveArchitecture()} {ResolveBuildConfiguration()} ({Application.platform}; Unity {Application.unityVersion})";
        }

        private static string ResolveExecutionTarget()
        {
#if UNITY_EDITOR
            return "Editor";
#elif UNITY_STANDALONE
            return "Standalone";
#else
            return Application.platform.ToString();
#endif
        }

        private static string ResolveScriptingBackend()
        {
#if ENABLE_IL2CPP
            return "IL2CPP";
#elif ENABLE_MONO
            return "Mono";
#else
            return Type.GetType("Mono.Runtime", throwOnError: false) == null
                ? "UnknownBackend"
                : "Mono";
#endif
        }

        private static string ResolveArchitecture()
        {
            return IntPtr.Size == 8 ? "x64" : "x86";
        }

        private static string ResolveBuildConfiguration()
        {
            return Debug.isDebugBuild ? "Development" : "Release";
        }

        private static string ResolveCommit()
        {
            string commit = Environment.GetEnvironmentVariable("DX_PERF_COMMIT");
            if (!string.IsNullOrWhiteSpace(commit))
            {
                return commit;
            }

            commit = Environment.GetEnvironmentVariable("GITHUB_SHA");
            return string.IsNullOrWhiteSpace(commit) ? "local" : commit;
        }

        private static string EscapeCsv(string value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0)
            {
                return value;
            }

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
#endif
