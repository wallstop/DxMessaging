namespace DxMessaging.Tests.Runtime.Benchmarks
{
    using System;
    using System.Diagnostics;
    using Core;
    using DxMessaging.Core;
    using DxMessaging.Core.Extensions;
    using global::Unity.PerformanceTesting;
    using global::Unity.Profiling;
    using NUnit.Framework;
    using Scripts.Components;
    using Scripts.Messages;
    using UnityEngine;
    using UnityEngine.Profiling;
    using Debug = UnityEngine.Debug;
    using Object = UnityEngine.Object;

    public sealed class PerformanceTests : MessagingTestBase
    {
        internal static string FormatBytes(long bytes)
        {
            if (bytes < 1024)
            {
                return $"{bytes} B";
            }

            string[] sizes = { "KB", "MB", "GB", "TB", "PB", "EB" };
            double len = bytes;
            int order = 0;

            while (1024 <= len && order < sizes.Length - 1)
            {
                len /= 1024;
                order++;
            }

            return $"{len:0.##} {sizes[order]}";
        }

        [SetUp]
        public override void Setup()
        {
            base.Setup();
            MessagingDebug.LogFunction = (_, _) => { };
        }

        [Test]
        public void Benchmark()
        {
            TimeSpan timeout = TimeSpan.FromSeconds(2);

            Debug.Log("| Message Tech | Operations / Second | Memory Allocated |");
            Debug.Log("| ------------ | ------------------- | --------------- |");

            ComplexTargetedMessage message = new(Guid.NewGuid());
            Stopwatch timer = Stopwatch.StartNew();
            ProfilerRecorder recorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Memory,
                "GC.Alloc"
            );
            recorder.Stop();
            RunTest(component => Unity(timer, timeout, component.gameObject, message));
            RunTest(component => NormalGameObject(timer, timeout, component, message));
            RunTest(component => NormalComponent(timer, timeout, component, message));
            RunTest(component => NoCopyGameObject(timer, timeout, component, message));
            RunTest(component => NoCopyComponent(timer, timeout, component, message));

            SimpleUntargetedMessage untargetedMessage = new();
            RunTest(component => NoCopyUntargeted(timer, timeout, component, untargetedMessage));
        }

        [Test]
        [Performance]
        public void BenchmarkBroadcast()
        {
            GameObject go = CreateGameObject();
            MessageRegistrationToken token = GetToken(
                go.GetComponent<EmptyMessageAwareComponent>()
            );
            int count = 0;
            token.RegisterGameObjectBroadcast<SimpleBroadcastMessage>(go, Handle);

            SimpleBroadcastMessage message = new();

            Measure
                .Method(() => message.EmitGameObjectBroadcast(go))
                .WarmupCount(10)
                .IterationsPerMeasurement(10)
                .MeasurementCount(1_000)
                .Run();
            return;

            void Handle(SimpleBroadcastMessage _)
            {
                ++count;
            }
        }

        [Test]
        [Performance]
        public void BenchmarkTargeted()
        {
            GameObject go = CreateGameObject();
            MessageRegistrationToken token = GetToken(
                go.GetComponent<EmptyMessageAwareComponent>()
            );
            int count = 0;
            token.RegisterGameObjectTargeted<SimpleTargetedMessage>(go, Handle);

            SimpleTargetedMessage message = new();

            Measure
                .Method(() => message.EmitGameObjectTargeted(go))
                .WarmupCount(10)
                .IterationsPerMeasurement(10)
                .MeasurementCount(1_000)
                .Run();
            return;

            void Handle(SimpleTargetedMessage _)
            {
                ++count;
            }
        }

        [Test]
        [Performance]
        public void BenchmarkUntargeted()
        {
            GameObject go = CreateGameObject();
            MessageRegistrationToken token = GetToken(
                go.GetComponent<EmptyMessageAwareComponent>()
            );
            int count = 0;
            token.RegisterUntargeted<SimpleUntargetedMessage>(Handle);

            SimpleUntargetedMessage message = new();

            Measure
                .Method(() => message.EmitUntargeted())
                .WarmupCount(10)
                .IterationsPerMeasurement(10)
                .MeasurementCount(1_000)
                .Run();
            return;

            void Handle(SimpleUntargetedMessage _)
            {
                ++count;
            }
        }

        private GameObject CreateGameObject()
        {
            GameObject target = new(
                nameof(Benchmark),
                typeof(EmptyMessageAwareComponent),
                typeof(SpriteRenderer),
                typeof(Rigidbody2D),
                typeof(CircleCollider2D),
                typeof(LineRenderer)
            );
            _spawned.Add(target);

            return target;
        }

        private static void DisplayCount(
            string testName,
            int count,
            TimeSpan timeout,
            long memoryUsed
        )
        {
            Debug.Log(
                $"| {testName} | {Math.Floor(count / timeout.TotalSeconds):N0} | {FormatBytes(Math.Max(memoryUsed, 0))} |"
            );
        }

        private void RunTest(Action<EmptyMessageAwareComponent> test)
        {
            GameObject go = CreateGameObject();
            try
            {
                test(go.GetComponent<EmptyMessageAwareComponent>());
            }
            finally
            {
                _spawned.Remove(go);
                Object.Destroy(go);
            }
        }

        private static void Unity(
            Stopwatch timer,
            TimeSpan timeout,
            GameObject target,
            ComplexTargetedMessage message
        )
        {
            int count = 0;
            var component = target.AddComponent<SimpleMessageAwareComponent>();
            component.slowComplexTargetedHandler = () => ++count;
            ProfilerRecorder recorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Memory,
                "GC.Alloc"
            );
            timer.Restart();
            do
            {
                target.SendMessage(
                    nameof(SimpleMessageAwareComponent.HandleSlowComplexTargetedMessage),
                    message
                );
            } while (timer.Elapsed < timeout);
            recorder.Stop();
            DisplayCount("Unity", count, timeout, recorder.LastValue);
        }

        private void NormalGameObject(
            Stopwatch timer,
            TimeSpan timeout,
            EmptyMessageAwareComponent component,
            ComplexTargetedMessage message
        )
        {
            int count = 0;
            var token = GetToken(component);

            GameObject go = component.gameObject;
            token.RegisterGameObjectTargeted<ComplexTargetedMessage>(go, Handle);
            ProfilerRecorder recorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Memory,
                "GC.Alloc"
            );
            timer.Restart();
            do
            {
                message.EmitGameObjectTargeted(go);
            } while (timer.Elapsed < timeout);

            recorder.Stop();
            DisplayCount("DxMessaging (GameObject) - Normal", count, timeout, recorder.LastValue);
            return;

            void Handle(ComplexTargetedMessage _)
            {
                ++count;
            }
        }

        private void NormalComponent(
            Stopwatch timer,
            TimeSpan timeout,
            EmptyMessageAwareComponent component,
            ComplexTargetedMessage message
        )
        {
            int count = 0;
            var token = GetToken(component);

            token.RegisterComponentTargeted<ComplexTargetedMessage>(component, Handle);
            ProfilerRecorder recorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Memory,
                "GC.Alloc"
            );
            timer.Restart();
            do
            {
                message.EmitComponentTargeted(component);
            } while (timer.Elapsed < timeout);
            recorder.Stop();
            DisplayCount("DxMessaging (Component) - Normal", count, timeout, recorder.LastValue);
            return;

            void Handle(ComplexTargetedMessage _)
            {
                ++count;
            }
        }

        private void NoCopyGameObject(
            Stopwatch timer,
            TimeSpan timeout,
            EmptyMessageAwareComponent component,
            ComplexTargetedMessage message
        )
        {
            int count = 0;
            var token = GetToken(component);

            GameObject go = component.gameObject;
            token.RegisterGameObjectTargeted<ComplexTargetedMessage>(go, Handle);
            ProfilerRecorder recorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Memory,
                "GC.Alloc"
            );
            timer.Restart();
            do
            {
                message.EmitGameObjectTargeted(component.gameObject);
            } while (timer.Elapsed < timeout);
            recorder.Stop();
            DisplayCount("DxMessaging (GameObject) - No-Copy", count, timeout, recorder.LastValue);
            return;

            void Handle(ref ComplexTargetedMessage _)
            {
                ++count;
            }
        }

        private void NoCopyComponent(
            Stopwatch timer,
            TimeSpan timeout,
            EmptyMessageAwareComponent component,
            ComplexTargetedMessage message
        )
        {
            int count = 0;
            var token = GetToken(component);

            token.RegisterComponentTargeted<ComplexTargetedMessage>(component, Handle);
            ProfilerRecorder recorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Memory,
                "GC.Alloc"
            );
            timer.Restart();
            do
            {
                message.EmitComponentTargeted(component);
            } while (timer.Elapsed < timeout);
            recorder.Stop();
            DisplayCount("DxMessaging (Component) - No-Copy", count, timeout, recorder.LastValue);
            return;

            void Handle(ref ComplexTargetedMessage _)
            {
                ++count;
            }
        }

        private void NoCopyUntargeted(
            Stopwatch timer,
            TimeSpan timeout,
            EmptyMessageAwareComponent component,
            SimpleUntargetedMessage message
        )
        {
            int count = 0;
            var token = GetToken(component);

            token.RegisterUntargeted<SimpleUntargetedMessage>(Handle);
            ProfilerRecorder recorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Memory,
                "GC.Alloc"
            );
            timer.Restart();
            do
            {
                message.EmitUntargeted();
            } while (timer.Elapsed < timeout);
            recorder.Stop();
            DisplayCount("DxMessaging (Untargeted) - No-Copy", count, timeout, recorder.LastValue);
            return;

            void Handle(ref SimpleUntargetedMessage _)
            {
                ++count;
            }
        }
    }
}
