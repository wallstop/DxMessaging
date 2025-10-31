#if UNITY_2021_3_OR_NEWER
namespace DxMessaging.Tests.Runtime.Benchmarks
{
    using System;
    using System.Diagnostics;
    using DxMessaging.Core;
    using DxMessaging.Core.Extensions;
    using NUnit.Framework;
    using Scripts.Messages;
    using UnityEngine.TestTools.Constraints;
    using Debug = UnityEngine.Debug;
    using Is = NUnit.Framework.Is;
#if UNIRX_PRESENT
    using UniRx;
#endif
#if MESSAGEPIPE_PRESENT
    using MessagePipe;
#endif
#if ZENJECT_PRESENT
    using Zenject;
#endif

    public sealed class ComparisonPerformanceTests : BenchmarkTestBase
    {
        protected override bool MessagingDebugEnabled => false;

        [Test]
        public void Benchmark()
        {
            string operatingSystemSection = BenchmarkDocumentation.GetOperatingSystemSection();
            string sectionName = string.IsNullOrEmpty(operatingSystemSection)
                ? null
                : $"Comparisons ({operatingSystemSection})";

            BenchmarkSession session = new(
                sectionName,
                "### ",
                new Func<string>[]
                {
                    BenchmarkDocumentation.TryFindComparisonsDocPath,
                    BenchmarkDocumentation.TryFindPerformanceDocPath,
                    BenchmarkDocumentation.TryFindReadmePath,
                }
            );

            RunWithSession(
                session,
                () =>
                {
                    TimeSpan timeout = TimeSpan.FromSeconds(5);
                    BenchmarkDxMessaging(timeout);
                    BenchmarkUniRx(timeout);
                    BenchmarkMessagePipe(timeout);
                    BenchmarkZenjectSignals(timeout);
                }
            );
        }

        private void BenchmarkDxMessaging(TimeSpan timeout)
        {
            Stopwatch timer = Stopwatch.StartNew();
            SimpleUntargetedMessage message = new();

            RunWithComponent(component =>
            {
                int count = 0;
                MessageRegistrationToken token = GetToken(component);
                token.RegisterUntargeted<SimpleUntargetedMessage>(Handle);

                message.EmitUntargeted();

                timer.Restart();
                do
                {
                    for (int i = 0; i < NumInvocationsPerIteration; ++i)
                    {
                        message.EmitUntargeted();
                    }
                } while (timer.Elapsed < timeout);

                bool allocating;
                try
                {
                    Assert.That(() => message.EmitUntargeted(), Is.Not.AllocatingGCMemory());
                    allocating = false;
                }
                catch
                {
                    allocating = true;
                }

                RecordBenchmark("DxMessaging (Untargeted) - No-Copy", count, timeout, allocating);
                return;

                void Handle(ref SimpleUntargetedMessage _)
                {
                    ++count;
                }
            });
        }

#if ZENJECT_PRESENT
        private void BenchmarkZenjectSignals(TimeSpan timeout)
        {
            ZenjectBridge bridge = ZenjectBridge.Create();
            if (bridge == null)
            {
                Debug.LogWarning("Zenject SignalBus not found. Skipping comparison benchmark.");
                return;
            }

            SimpleUntargetedMessage message = new();
            int count = 0;
            using IDisposable subscription = bridge.Subscribe(_ => ++count);

            bridge.Publish(message);

            Stopwatch timer = Stopwatch.StartNew();
            do
            {
                for (int i = 0; i < NumInvocationsPerIteration; ++i)
                {
                    bridge.Publish(message);
                }
            } while (timer.Elapsed < timeout);

            bool allocating;
            try
            {
                Assert.That(() => bridge.Publish(message), Is.Not.AllocatingGCMemory());
                allocating = false;
            }
            catch
            {
                allocating = true;
            }

            RecordBenchmark("Zenject SignalBus", count, timeout, allocating);
        }
#else
        private void BenchmarkZenjectSignals(TimeSpan timeout)
        {
            Debug.LogWarning("Zenject package not detected. Skipping comparison benchmark.");
        }
#endif

#if UNIRX_PRESENT
        private void BenchmarkUniRx(TimeSpan timeout)
        {
            UniRxBridge bridge = UniRxBridge.Create();
            if (bridge == null)
            {
                Debug.LogWarning("UniRx.MessageBroker not found. Skipping comparison benchmark.");
                return;
            }

            SimpleUntargetedMessage message = new();
            int count = 0;
            using IDisposable subscription = bridge.Subscribe(_ => ++count);

            bridge.Publish(message);

            Stopwatch timer = Stopwatch.StartNew();
            do
            {
                for (int i = 0; i < NumInvocationsPerIteration; ++i)
                {
                    bridge.Publish(message);
                }
            } while (timer.Elapsed < timeout);

            bool allocating;
            try
            {
                Assert.That(() => bridge.Publish(message), Is.Not.AllocatingGCMemory());
                allocating = false;
            }
            catch
            {
                allocating = true;
            }

            RecordBenchmark("UniRx MessageBroker", count, timeout, allocating);
        }
#else
        private void BenchmarkUniRx(TimeSpan timeout)
        {
            Debug.LogWarning("UniRx package not detected. Skipping comparison benchmark.");
        }
#endif

#if MESSAGEPIPE_PRESENT
        private void BenchmarkMessagePipe(TimeSpan timeout)
        {
            MessagePipeBridge bridge = MessagePipeBridge.Create();
            if (bridge == null)
            {
                Debug.LogWarning("Cysharp.MessagePipe not found. Skipping comparison benchmark.");
                return;
            }

            SimpleUntargetedMessage message = new();
            int count = 0;
            using IDisposable subscription = bridge.Subscribe(_ => ++count);

            bridge.Publish(message);

            Stopwatch timer = Stopwatch.StartNew();
            do
            {
                for (int i = 0; i < NumInvocationsPerIteration; ++i)
                {
                    bridge.Publish(message);
                }
            } while (timer.Elapsed < timeout);

            bool allocating;
            try
            {
                Assert.That(() => bridge.Publish(message), Is.Not.AllocatingGCMemory());
                allocating = false;
            }
            catch
            {
                allocating = true;
            }

            RecordBenchmark("MessagePipe (Global)", count, timeout, allocating);
        }
#else
        private void BenchmarkMessagePipe(TimeSpan timeout)
        {
            Debug.LogWarning("MessagePipe package not detected. Skipping comparison benchmark.");
        }
#endif

#if UNIRX_PRESENT
        private sealed class UniRxBridge
        {
            private readonly IMessageBroker _broker;

            private UniRxBridge(IMessageBroker broker)
            {
                _broker = broker;
            }

            internal static UniRxBridge Create()
            {
                IMessageBroker broker = MessageBroker.Default;
                if (broker == null)
                {
                    return null;
                }

                return new UniRxBridge(broker);
            }

            internal IDisposable Subscribe(Action<SimpleUntargetedMessage> handler)
            {
                return _broker.Receive<SimpleUntargetedMessage>().Subscribe(handler);
            }

            internal void Publish(in SimpleUntargetedMessage message)
            {
                _broker.Publish(message);
            }
        }
#endif

#if MESSAGEPIPE_PRESENT
        private sealed class MessagePipeBridge
        {
            private readonly IPublisher<SimpleUntargetedMessage> _publisher;
            private readonly ISubscriber<SimpleUntargetedMessage> _subscriber;

            private MessagePipeBridge(
                IPublisher<SimpleUntargetedMessage> publisher,
                ISubscriber<SimpleUntargetedMessage> subscriber
            )
            {
                _publisher = publisher;
                _subscriber = subscriber;
            }

            internal static MessagePipeBridge Create()
            {
                try
                {
                    BuiltinContainerBuilder builder = new();
                    builder.AddMessagePipe();
                    builder.AddMessageBroker<SimpleUntargetedMessage>();
                    IServiceProvider provider = builder.BuildServiceProvider();
                    GlobalMessagePipe.SetProvider(provider);
                    IPublisher<SimpleUntargetedMessage> publisher =
                        GlobalMessagePipe.GetPublisher<SimpleUntargetedMessage>();
                    ISubscriber<SimpleUntargetedMessage> subscriber =
                        GlobalMessagePipe.GetSubscriber<SimpleUntargetedMessage>();
                    if (publisher == null || subscriber == null)
                    {
                        if (provider is IDisposable disposable)
                        {
                            disposable.Dispose();
                        }
                        Debug.LogWarning(
                            "MessagePipe publisher or subscriber could not be resolved."
                        );
                        return null;
                    }

                    return new MessagePipeBridge(publisher, subscriber);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"MessagePipe setup failed: {exception}");
                    return null;
                }
            }

            internal IDisposable Subscribe(Action<SimpleUntargetedMessage> handler)
            {
                return _subscriber.Subscribe(handler);
            }

            internal void Publish(in SimpleUntargetedMessage message)
            {
                _publisher.Publish(message);
            }
        }
#endif

#if ZENJECT_PRESENT
        private sealed class ZenjectBridge
        {
            private readonly SignalBus _signalBus;

            private ZenjectBridge(SignalBus signalBus)
            {
                _signalBus = signalBus;
            }

            internal static ZenjectBridge Create()
            {
                try
                {
                    DiContainer container = new();
                    SignalBusInstaller.Install(container);
                    container.DeclareSignal<SimpleUntargetedMessage>();
                    container.ResolveRoots();
                    SignalBus signalBus = container.Resolve<SignalBus>();
                    return new ZenjectBridge(signalBus);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"Zenject SignalBus setup failed: {exception}");
                    return null;
                }
            }

            internal IDisposable Subscribe(Action<SimpleUntargetedMessage> handler)
            {
                return new Subscription(_signalBus, handler);
            }

            internal void Publish(in SimpleUntargetedMessage message)
            {
                _signalBus.Fire(message);
            }

            private sealed class Subscription : IDisposable
            {
                private readonly SignalBus _signalBus;
                private readonly Action<SimpleUntargetedMessage> _handler;
                private bool _disposed;

                internal Subscription(SignalBus signalBus, Action<SimpleUntargetedMessage> handler)
                {
                    _signalBus = signalBus;
                    _handler = handler;
                    _signalBus.Subscribe(handler);
                }

                public void Dispose()
                {
                    if (_disposed)
                    {
                        return;
                    }

                    _signalBus.TryUnsubscribe(_handler);
                    _disposed = true;
                }
            }
        }
#endif
    }
}

#endif
