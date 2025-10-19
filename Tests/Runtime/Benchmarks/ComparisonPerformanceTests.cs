namespace DxMessaging.Tests.Runtime.Benchmarks
{
    using System;
    using System.Diagnostics;
    using System.Linq;
    using System.Reflection;
    using DxMessaging.Core;
    using DxMessaging.Core.Messages;
    using NUnit.Framework;
    using Scripts.Components;
    using Scripts.Messages;
    using UnityEngine;
    using UnityEngine.TestTools.Constraints;
    using Debug = UnityEngine.Debug;
    using Is = NUnit.Framework.Is;

    public sealed class ComparisonPerformanceTests : BenchmarkTestBase
    {
        protected override bool MessagingDebugEnabled => false;

        [Test]
        public void Benchmark()
        {
            string? operatingSystemSection = BenchmarkDocumentation.GetOperatingSystemSection();
            string? sectionName = string.IsNullOrEmpty(operatingSystemSection)
                ? null
                : $"Comparisons ({operatingSystemSection})";

            BenchmarkSession session = new(
                sectionName,
                "### ",
                new Func<string?>[]
                {
                    BenchmarkDocumentation.TryFindComparisonsDocPath,
                    BenchmarkDocumentation.TryFindPerformanceDocPath,
                    BenchmarkDocumentation.TryFindReadmePath,
                }
            );

            RunWithSession(session, () =>
            {
                TimeSpan timeout = TimeSpan.FromSeconds(5);
                BenchmarkDxMessaging(timeout);
                BenchmarkUniRx(timeout);
                BenchmarkMessagePipe(timeout);
                BenchmarkZenjectSignals(timeout);
            });
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
                }
                while (timer.Elapsed < timeout);

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

                void Handle(ref SimpleUntargetedMessage _)
                {
                    ++count;
                }
            });
        }

        private void BenchmarkZenjectSignals(TimeSpan timeout)
        {
            ZenjectBridge? bridge = ZenjectBridge.Create();
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
            }
            while (timer.Elapsed < timeout);

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

        private void BenchmarkUniRx(TimeSpan timeout)
        {
            UniRxBridge? bridge = UniRxBridge.Create();
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
            }
            while (timer.Elapsed < timeout);

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

        private void BenchmarkMessagePipe(TimeSpan timeout)
        {
            MessagePipeBridge? bridge = MessagePipeBridge.Create();
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
            }
            while (timer.Elapsed < timeout);

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

        private sealed class UniRxBridge
        {
            private readonly Action<SimpleUntargetedMessage> _publish;
            private readonly Func<Action<SimpleUntargetedMessage>, IDisposable> _subscribe;

            private UniRxBridge(
                Action<SimpleUntargetedMessage> publish,
                Func<Action<SimpleUntargetedMessage>, IDisposable> subscribe
            )
            {
                _publish = publish;
                _subscribe = subscribe;
            }

            internal static UniRxBridge? Create()
            {
                Type? messageBrokerType = Type.GetType("UniRx.MessageBroker, UniRx");
                if (messageBrokerType == null)
                {
                    return null;
                }

                PropertyInfo? defaultProperty = messageBrokerType.GetProperty(
                    "Default",
                    BindingFlags.Public | BindingFlags.Static
                );
                if (defaultProperty == null)
                {
                    Debug.LogWarning("UniRx.MessageBroker.Default property could not be located.");
                    return null;
                }

                object? broker = defaultProperty.GetValue(null);
                if (broker == null)
                {
                    Debug.LogWarning("UniRx.MessageBroker.Default returned null.");
                    return null;
                }

                MethodInfo? publishDefinition = messageBrokerType
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(
                        method =>
                            string.Equals(method.Name, "Publish", StringComparison.Ordinal)
                            && method.IsGenericMethodDefinition
                            && method.GetGenericArguments().Length == 1
                            && method.GetParameters().Length == 1
                    );
                MethodInfo? subscribeDefinition = messageBrokerType
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(
                        method =>
                            string.Equals(method.Name, "Subscribe", StringComparison.Ordinal)
                            && method.IsGenericMethodDefinition
                            && method.GetGenericArguments().Length == 1
                            && method.GetParameters().Length == 1
                    );

                if (publishDefinition == null || subscribeDefinition == null)
                {
                    Debug.LogWarning("UniRx.MessageBroker methods could not be located.");
                    return null;
                }

                MethodInfo publishMethod = publishDefinition.MakeGenericMethod(typeof(SimpleUntargetedMessage));
                MethodInfo subscribeMethod = subscribeDefinition.MakeGenericMethod(typeof(SimpleUntargetedMessage));

                Action<SimpleUntargetedMessage> publish = (Action<SimpleUntargetedMessage>)publishMethod.CreateDelegate(
                    typeof(Action<SimpleUntargetedMessage>),
                    broker
                );
                Func<Action<SimpleUntargetedMessage>, IDisposable> subscribe =
                    (Func<Action<SimpleUntargetedMessage>, IDisposable>)subscribeMethod.CreateDelegate(
                        typeof(Func<Action<SimpleUntargetedMessage>, IDisposable>),
                        broker
                    );

                return new UniRxBridge(publish, subscribe);
            }

            internal IDisposable Subscribe(Action<SimpleUntargetedMessage> handler)
            {
                return _subscribe(handler);
            }

            internal void Publish(in SimpleUntargetedMessage message)
            {
                _publish(message);
            }
        }

        private sealed class MessagePipeBridge
        {
            private readonly Action<SimpleUntargetedMessage> _publish;
            private readonly object _subscriber;
            private readonly MethodInfo _subscribeMethod;
            private readonly Array _emptyFilters;

            private MessagePipeBridge(
                Action<SimpleUntargetedMessage> publish,
                object subscriber,
                MethodInfo subscribeMethod,
                Array emptyFilters
            )
            {
                _publish = publish;
                _subscriber = subscriber;
                _subscribeMethod = subscribeMethod;
                _emptyFilters = emptyFilters;
            }

            internal static MessagePipeBridge? Create()
            {
                Type? builderType = Type.GetType("MessagePipe.BuiltinContainerBuilder, MessagePipe");
                Type? globalType = Type.GetType("MessagePipe.GlobalMessagePipe, MessagePipe");
                if (builderType == null || globalType == null)
                {
                    return null;
                }

                object? builder = Activator.CreateInstance(builderType);
                if (builder == null)
                {
                    return null;
                }

                MethodInfo? addMessagePipe = builderType.GetMethod("AddMessagePipe", Type.EmptyTypes);
                addMessagePipe?.Invoke(builder, Array.Empty<object>());

                MethodInfo? addMessageBrokerDefinition = builderType
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(
                        method =>
                            string.Equals(method.Name, "AddMessageBroker", StringComparison.Ordinal)
                            && method.IsGenericMethodDefinition
                            && method.GetGenericArguments().Length == 1
                    );
                if (addMessageBrokerDefinition == null)
                {
                    Debug.LogWarning("MessagePipe BuiltinContainerBuilder.AddMessageBroker<T> could not be located.");
                    return null;
                }

                addMessageBrokerDefinition.MakeGenericMethod(typeof(SimpleUntargetedMessage)).Invoke(
                    builder,
                    Array.Empty<object>()
                );

                MethodInfo? buildMethod = builderType.GetMethod("BuildServiceProvider", Type.EmptyTypes);
                if (buildMethod == null)
                {
                    Debug.LogWarning("MessagePipe BuiltinContainerBuilder.BuildServiceProvider could not be located.");
                    return null;
                }

                object? provider = buildMethod.Invoke(builder, Array.Empty<object>());
                if (provider == null)
                {
                    return null;
                }

                MethodInfo? setProvider = globalType.GetMethod("SetProvider", BindingFlags.Public | BindingFlags.Static);
                setProvider?.Invoke(null, new[] { provider });

                MethodInfo? getPublisherMethod = globalType.GetMethod("GetPublisher", BindingFlags.Public | BindingFlags.Static);
                MethodInfo? getSubscriberMethod = globalType.GetMethod("GetSubscriber", BindingFlags.Public | BindingFlags.Static);
                if (getPublisherMethod == null || getSubscriberMethod == null)
                {
                    Debug.LogWarning("MessagePipe.GlobalMessagePipe.GetPublisher/GetSubscriber could not be located.");
                    return null;
                }

                object? publisher = getPublisherMethod.MakeGenericMethod(typeof(SimpleUntargetedMessage)).Invoke(null, Array.Empty<object>());
                object? subscriber = getSubscriberMethod.MakeGenericMethod(typeof(SimpleUntargetedMessage)).Invoke(null, Array.Empty<object>());
                if (publisher == null || subscriber == null)
                {
                    return null;
                }

                MethodInfo? publishMethod = publisher.GetType().GetMethod(
                    "Publish",
                    new[] { typeof(SimpleUntargetedMessage) }
                );
                if (publishMethod == null)
                {
                    Debug.LogWarning("MessagePipe publish method could not be located.");
                    return null;
                }

                Action<SimpleUntargetedMessage> publish = (Action<SimpleUntargetedMessage>)publishMethod.CreateDelegate(
                    typeof(Action<SimpleUntargetedMessage>),
                    publisher
                );

                Type? subscriberExtensions = Type.GetType("MessagePipe.SubscriberExtensions, MessagePipe");
                if (subscriberExtensions == null)
                {
                    Debug.LogWarning("MessagePipe.SubscriberExtensions could not be located.");
                    return null;
                }

                MethodInfo? subscribeDefinition = subscriberExtensions
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(
                        method =>
                            string.Equals(method.Name, "Subscribe", StringComparison.Ordinal)
                            && method.IsGenericMethodDefinition
                            && method.GetGenericArguments().Length == 1
                            && method.GetParameters().Length == 3
                            && method.GetParameters()[0].ParameterType.IsGenericType
                            && string.Equals(
                                method.GetParameters()[0].ParameterType.GetGenericTypeDefinition().FullName,
                                "MessagePipe.ISubscriber`1",
                                StringComparison.Ordinal
                            )
                    );
                if (subscribeDefinition == null)
                {
                    Debug.LogWarning("MessagePipe subscriber Subscribe<T> extension could not be located.");
                    return null;
                }

                MethodInfo subscribeMethod = subscribeDefinition.MakeGenericMethod(typeof(SimpleUntargetedMessage));

                Type? filterGenericType = Type.GetType("MessagePipe.MessageHandlerFilter`1, MessagePipe");
                if (filterGenericType == null)
                {
                    Debug.LogWarning("MessagePipe.MessageHandlerFilter<T> type could not be located.");
                    return null;
                }

                Array emptyFilters = Array.CreateInstance(
                    filterGenericType.MakeGenericType(typeof(SimpleUntargetedMessage)),
                    0
                );

                return new MessagePipeBridge(publish, subscriber, subscribeMethod, emptyFilters);
            }

            internal IDisposable Subscribe(Action<SimpleUntargetedMessage> handler)
            {
                object? result = _subscribeMethod.Invoke(null, new object[] { _subscriber, handler, _emptyFilters });
                return result as IDisposable ?? throw new InvalidOperationException("MessagePipe Subscribe did not return IDisposable.");
            }

            internal void Publish(in SimpleUntargetedMessage message)
            {
                _publish(message);
            }
        }

        private sealed class ZenjectBridge
        {
            private readonly object _container;
            private readonly object _signalBus;
            private readonly MethodInfo _subscribeMethod;
            private readonly MethodInfo _unsubscribeMethod;
            private readonly MethodInfo _fireMethod;

            private ZenjectBridge(
                object container,
                object signalBus,
                MethodInfo subscribeMethod,
                MethodInfo unsubscribeMethod,
                MethodInfo fireMethod
            )
            {
                _container = container;
                _signalBus = signalBus;
                _subscribeMethod = subscribeMethod;
                _unsubscribeMethod = unsubscribeMethod;
                _fireMethod = fireMethod;
            }

            internal static ZenjectBridge? Create()
            {
                Type? containerType = Type.GetType("Zenject.DiContainer, Zenject");
                Type? installerType = Type.GetType("Zenject.SignalBusInstaller, Zenject");
                Type? extensionsType = Type.GetType("Zenject.SignalExtensions, Zenject");
                Type? signalBusType = Type.GetType("Zenject.SignalBus, Zenject");
                if (containerType == null || installerType == null || extensionsType == null || signalBusType == null)
                {
                    return null;
                }

                object? container = Activator.CreateInstance(containerType);
                if (container == null)
                {
                    return null;
                }

                MethodInfo? installMethod = installerType
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(method =>
                        string.Equals(method.Name, "Install", StringComparison.Ordinal)
                        && method.GetParameters().Length == 1
                        && method.GetParameters()[0].ParameterType.IsAssignableFrom(containerType));
                installMethod?.Invoke(null, new[] { container });

                MethodInfo? declareSignalMethod = extensionsType
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(method =>
                        string.Equals(method.Name, "DeclareSignal", StringComparison.Ordinal)
                        && method.IsGenericMethodDefinition
                        && method.GetParameters().Length == 1
                        && method.GetParameters()[0].ParameterType.IsAssignableFrom(containerType));
                if (declareSignalMethod == null)
                {
                    return null;
                }

                declareSignalMethod
                    .MakeGenericMethod(typeof(SimpleUntargetedMessage))
                    .Invoke(null, new[] { container });

                MethodInfo? resolveMethod = containerType
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(method =>
                        string.Equals(method.Name, "Resolve", StringComparison.Ordinal)
                        && method.IsGenericMethodDefinition
                        && method.GetParameters().Length == 0);
                if (resolveMethod == null)
                {
                    return null;
                }

                object? signalBus = resolveMethod
                    .MakeGenericMethod(signalBusType)
                    .Invoke(container, Array.Empty<object>());
                if (signalBus == null)
                {
                    return null;
                }

                MethodInfo? subscribeDefinition = signalBusType
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(method =>
                        string.Equals(method.Name, "Subscribe", StringComparison.Ordinal)
                        && method.IsGenericMethodDefinition
                        && method.GetParameters().Length == 1
                        && method.GetParameters()[0].ParameterType.IsGenericType);
                MethodInfo? unsubscribeDefinition = signalBusType
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(method =>
                        string.Equals(method.Name, "Unsubscribe", StringComparison.Ordinal)
                        && method.IsGenericMethodDefinition
                        && method.GetParameters().Length == 1
                        && method.GetParameters()[0].ParameterType.IsGenericType);
                MethodInfo? fireDefinition = signalBusType
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(method =>
                        string.Equals(method.Name, "Fire", StringComparison.Ordinal)
                        && method.IsGenericMethodDefinition
                        && method.GetParameters().Length == 1);

                if (subscribeDefinition == null || unsubscribeDefinition == null || fireDefinition == null)
                {
                    Debug.LogWarning("Zenject SignalBus methods could not be located.");
                    return null;
                }

                MethodInfo subscribeMethod = subscribeDefinition.MakeGenericMethod(typeof(SimpleUntargetedMessage));
                MethodInfo unsubscribeMethod = unsubscribeDefinition.MakeGenericMethod(typeof(SimpleUntargetedMessage));
                MethodInfo fireMethod = fireDefinition.MakeGenericMethod(typeof(SimpleUntargetedMessage));

                return new ZenjectBridge(container, signalBus, subscribeMethod, unsubscribeMethod, fireMethod);
            }

            internal IDisposable Subscribe(Action<SimpleUntargetedMessage> handler)
            {
                _subscribeMethod.Invoke(_signalBus, new object[] { handler });
                return new Subscription(_signalBus, _unsubscribeMethod, handler);
            }

            internal void Publish(in SimpleUntargetedMessage message)
            {
                _fireMethod.Invoke(_signalBus, new object[] { message });
            }

            private sealed class Subscription : IDisposable
            {
                private readonly object _signalBus;
                private readonly MethodInfo _unsubscribeMethod;
                private readonly Delegate _handler;
                private bool _disposed;

                internal Subscription(object signalBus, MethodInfo unsubscribeMethod, Delegate handler)
                {
                    _signalBus = signalBus;
                    _unsubscribeMethod = unsubscribeMethod;
                    _handler = handler;
                }

                public void Dispose()
                {
                    if (_disposed)
                    {
                        return;
                    }

                    try
                    {
                        _unsubscribeMethod.Invoke(_signalBus, new object[] { _handler });
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning($"Zenject SignalBus unsubscribe failed: {exception}");
                    }
                    finally
                    {
                        _disposed = true;
                    }
                }
            }
        }
    }
}
