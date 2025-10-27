namespace DxMessaging.Tests.Runtime.Benchmarks
{
    using System;
    using System.Diagnostics;
    using DxMessaging.Core;
    using DxMessaging.Core.Extensions;
    using DxMessaging.Core.MessageBus;
    using DxMessaging.Core.Messages;
    using NUnit.Framework;

    public sealed class ProviderResolutionBenchmarks : BenchmarkTestBase
    {
        private IMessageBus _originalBus;

        [SetUp]
        public void CaptureGlobalBus()
        {
            _originalBus = MessageHandler.MessageBus;
            MessageHandler.ResetGlobalMessageBus();
        }

        [TearDown]
        public void RestoreGlobalBus()
        {
            MessageHandler.SetGlobalMessageBus(_originalBus);
        }

        [Test]
        public void ProviderResolutionPerformance()
        {
            string section = BenchmarkDocumentation.GetOperatingSystemSection();
            BenchmarkSession session = new(section, "### ", Array.Empty<Func<string>>());

            RunWithSession(
                session,
                () =>
                {
                    TimeSpan timeout = TimeSpan.FromSeconds(2);

                    BenchmarkResult direct = RunBenchmark(timeout, useProvider: false);
                    BenchmarkResult provider = RunBenchmark(timeout, useProvider: true);

                    RecordBenchmark(
                        "DxMessaging (Untargeted) - Direct Bus",
                        direct.Count,
                        direct.Duration,
                        allocating: false
                    );
                    RecordBenchmark(
                        "DxMessaging (Untargeted) - Provider",
                        provider.Count,
                        provider.Duration,
                        allocating: false
                    );

                    double directOps = direct.OperationsPerSecond;
                    double providerOps = provider.OperationsPerSecond;
                    Assume.That(providerOps, Is.GreaterThan(0));

                    double slowdown = directOps / providerOps;
                    Assert.That(
                        slowdown,
                        Is.LessThanOrEqualTo(1.15d),
                        $"Provider path must remain within 15% of direct bus dispatch. Direct: {directOps:N0} ops/s, Provider: {providerOps:N0} ops/s."
                    );
                }
            );
        }

        private BenchmarkResult RunBenchmark(TimeSpan timeout, bool useProvider)
        {
            MessageBus bus = new();
            MessageHandler handler = new(new InstanceId(5000), bus) { active = true };
            MessageRegistrationToken token = MessageRegistrationToken.Create(handler, bus);
            _ = token.RegisterUntargeted((ref BenchmarkUntargetedMessage _) => { });
            token.Enable();

            Stopwatch stopwatch = Stopwatch.StartNew();
            int count = 0;
            BenchmarkUntargetedMessage message = new(0);
            IMessageBusProvider provider = useProvider ? new StaticMessageBusProvider(bus) : null;

            while (stopwatch.Elapsed < timeout)
            {
                if (useProvider)
                {
                    message.EmitUntargeted(messageBusProvider: provider);
                }
                else
                {
                    message.EmitUntargeted(bus);
                }

                count++;
            }

            stopwatch.Stop();
            token.Disable();

            return new BenchmarkResult(count, stopwatch.Elapsed);
        }

        private readonly struct BenchmarkResult
        {
            internal BenchmarkResult(int count, TimeSpan duration)
            {
                Count = count;
                Duration = duration;
            }

            internal int Count { get; }
            internal TimeSpan Duration { get; }
            internal double OperationsPerSecond =>
                Duration.TotalSeconds <= 0 ? 0 : Count / Duration.TotalSeconds;
        }

        private sealed class StaticMessageBusProvider : IMessageBusProvider
        {
            private readonly IMessageBus _bus;

            internal StaticMessageBusProvider(IMessageBus bus)
            {
                _bus = bus;
            }

            public IMessageBus Resolve()
            {
                return _bus;
            }
        }

        private readonly struct BenchmarkUntargetedMessage : IUntargetedMessage
        {
            internal BenchmarkUntargetedMessage(int value)
            {
                Value = value;
            }

            internal int Value { get; }
        }
    }
}
