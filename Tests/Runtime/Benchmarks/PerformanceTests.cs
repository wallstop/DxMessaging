namespace DxMessaging.Tests.Runtime.Benchmarks
{
    using System;
    using System.Diagnostics;
    using Core;
    using DxMessaging.Core;
    using DxMessaging.Core.Extensions;
    using DxMessaging.Core.Messages;
    using global::Unity.PerformanceTesting;
    using NUnit.Framework;
    using Scripts.Components;
    using Scripts.Messages;
    using UnityEngine;
    using UnityEngine.TestTools.Constraints;
    using Debug = UnityEngine.Debug;
    using Is = NUnit.Framework.Is;
    using Object = UnityEngine.Object;

    public sealed class PerformanceTests : MessagingTestBase
    {
        private const int NumInvocationsPerIteration = 1_000;

        protected override bool MessagingDebugEnabled => false;

        [Test]
        public void Benchmark()
        {
            TimeSpan timeout = TimeSpan.FromSeconds(5);

            Debug.Log("| Message Tech | Operations / Second | Allocations? |");
            Debug.Log("| ------------ | ------------------- | ------------ | ");

            ComplexTargetedMessage message = new(Guid.NewGuid());
            DxReflexiveMessage reflexiveMessage = new(
                nameof(SimpleMessageAwareComponent.HandleSlowComplexTargetedMessage),
                ReflexiveSendMode.Flat,
                message
            );

            Stopwatch timer = Stopwatch.StartNew();

            RunTest(component => Unity(timer, timeout, component.gameObject, message));

            RunTest(component => NormalGameObject(timer, timeout, component, message));
            RunTest(component => NormalComponent(timer, timeout, component, message));
            RunTest(component => NoCopyGameObject(timer, timeout, component, message));
            RunTest(component => NoCopyComponent(timer, timeout, component, message));

            SimpleUntargetedMessage untargetedMessage = new();
            RunTest(component => NoCopyUntargeted(timer, timeout, component, untargetedMessage));
            RunTest(component =>
                ReflexiveOneArgument(timer, timeout, component.gameObject, reflexiveMessage)
            );
            RunTest(component => ReflexiveTwoArguments(timer, timeout, component.gameObject));
            RunTest(component => ReflexiveThreeArguments(timer, timeout, component.gameObject));
        }

        [Test]
        [Performance]
        public void BenchmarkBroadcast()
        {
            GameObject go = CreateGameObject();
            MessageRegistrationToken token = GetToken(
                go.GetComponent<EmptyMessageAwareComponent>()
            );
            // ReSharper disable once NotAccessedVariable
            int count = 0;
            token.RegisterGameObjectBroadcast<SimpleBroadcastMessage>(go, Handle);

            SimpleBroadcastMessage message = new();
            SampleGroup time = new("Time", SampleUnit.Nanosecond);

            Measure
                .Method(() => message.EmitGameObjectBroadcast(go))
                .SampleGroup(time)
                .WarmupCount(10)
                .IterationsPerMeasurement(1)
                .MeasurementCount(10_000)
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
            // ReSharper disable once NotAccessedVariable
            int count = 0;
            token.RegisterGameObjectTargeted<SimpleTargetedMessage>(go, Handle);

            SimpleTargetedMessage message = new();
            SampleGroup time = new("Time", SampleUnit.Nanosecond);

            Measure
                .Method(() => message.EmitGameObjectTargeted(go))
                .SampleGroup(time)
                .WarmupCount(10)
                .IterationsPerMeasurement(1)
                .MeasurementCount(10_000)
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
            // ReSharper disable once NotAccessedVariable
            int count = 0;
            token.RegisterUntargeted<SimpleUntargetedMessage>(Handle);

            SimpleUntargetedMessage message = new();
            SampleGroup time = new("Time", SampleUnit.Nanosecond);

            Measure
                .Method(() => message.EmitUntargeted())
                .SampleGroup(time)
                .WarmupCount(10)
                .IterationsPerMeasurement(1)
                .MeasurementCount(10_000)
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
            bool allocating
        )
        {
            Debug.Log(
                $"| {testName} | {Math.Floor(count / timeout.TotalSeconds):N0} | {(allocating ? "Yes" : "No")} |"
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
            if (!target.TryGetComponent(out SimpleMessageAwareComponent component))
            {
                component = target.AddComponent<SimpleMessageAwareComponent>();
            }
            component.slowComplexTargetedHandler = () => ++count;
            // Pre-warm
            target.SendMessage(
                nameof(SimpleMessageAwareComponent.HandleSlowComplexTargetedMessage),
                message
            );

            timer.Restart();
            do
            {
                for (int i = 0; i < NumInvocationsPerIteration; ++i)
                {
                    target.SendMessage(
                        nameof(SimpleMessageAwareComponent.HandleSlowComplexTargetedMessage),
                        message
                    );
                }
            } while (timer.Elapsed < timeout);

            bool allocating;
            try
            {
                Assert.That(
                    () =>
                        target.SendMessage(
                            nameof(SimpleMessageAwareComponent.HandleSlowComplexTargetedMessage),
                            message
                        ),
                    Is.Not.AllocatingGCMemory()
                );
                allocating = false;
            }
            catch
            {
                allocating = true;
            }
            DisplayCount("Unity", count, timeout, allocating);
        }

        private static void ReflexiveThreeArguments(
            Stopwatch timer,
            TimeSpan timeout,
            GameObject go
        )
        {
            int count = 0;
            if (!go.TryGetComponent(out SimpleMessageAwareComponent component))
            {
                component = go.AddComponent<SimpleMessageAwareComponent>();
            }
            component.reflexiveThreeArgumentHandler = () => ++count;
            DxReflexiveMessage message = new(
                nameof(SimpleMessageAwareComponent.HandleReflexiveMessageThreeArguments),
                ReflexiveSendMode.Flat,
                1,
                2,
                3
            );
            InstanceId target = go;
            // Pre-warm
            message.EmitTargeted(target);

            timer.Restart();
            do
            {
                for (int i = 0; i < NumInvocationsPerIteration; ++i)
                {
                    message.EmitTargeted(target);
                }
            } while (timer.Elapsed < timeout);
            bool allocating;
            try
            {
                Assert.That(() => message.EmitTargeted(target), Is.Not.AllocatingGCMemory());
                allocating = false;
            }
            catch
            {
                allocating = true;
            }

            DisplayCount("Reflexive (Three Arguments)", count, timeout, allocating);
        }

        private static void ReflexiveTwoArguments(Stopwatch timer, TimeSpan timeout, GameObject go)
        {
            int count = 0;
            if (!go.TryGetComponent(out SimpleMessageAwareComponent component))
            {
                component = go.AddComponent<SimpleMessageAwareComponent>();
            }
            component.reflexiveTwoArgumentHandler = () => ++count;
            DxReflexiveMessage message = new(
                nameof(SimpleMessageAwareComponent.HandleReflexiveMessageTwoArguments),
                ReflexiveSendMode.Flat,
                1,
                2
            );
            InstanceId target = go;
            // Pre-warm
            message.EmitTargeted(target);

            timer.Restart();
            do
            {
                for (int i = 0; i < NumInvocationsPerIteration; ++i)
                {
                    message.EmitTargeted(target);
                }
            } while (timer.Elapsed < timeout);
            bool allocating;
            try
            {
                Assert.That(() => message.EmitTargeted(target), Is.Not.AllocatingGCMemory());
                allocating = false;
            }
            catch
            {
                allocating = true;
            }

            DisplayCount("Reflexive (Two Arguments)", count, timeout, allocating);
        }

        private static void ReflexiveOneArgument(
            Stopwatch timer,
            TimeSpan timeout,
            GameObject go,
            DxReflexiveMessage message
        )
        {
            int count = 0;
            if (!go.TryGetComponent(out SimpleMessageAwareComponent component))
            {
                component = go.AddComponent<SimpleMessageAwareComponent>();
            }
            component.slowComplexTargetedHandler = () => ++count;
            InstanceId target = go;
            // Pre-warm
            message.EmitTargeted(target);

            timer.Restart();
            do
            {
                for (int i = 0; i < NumInvocationsPerIteration; ++i)
                {
                    message.EmitTargeted(target);
                }
            } while (timer.Elapsed < timeout);
            bool allocating;
            try
            {
                Assert.That(() => message.EmitTargeted(target), Is.Not.AllocatingGCMemory());
                allocating = false;
            }
            catch
            {
                allocating = true;
            }

            DisplayCount("Reflexive (One Argument)", count, timeout, allocating);
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
            InstanceId target = go;
            token.RegisterGameObjectTargeted<ComplexTargetedMessage>(go, Handle);
            // Pre-warm
            message.EmitTargeted(target);

            timer.Restart();
            do
            {
                for (int i = 0; i < NumInvocationsPerIteration; ++i)
                {
                    message.EmitTargeted(target);
                }
            } while (timer.Elapsed < timeout);
            bool allocating;
            try
            {
                Assert.That(() => message.EmitTargeted(target), Is.Not.AllocatingGCMemory());
                allocating = false;
            }
            catch
            {
                allocating = true;
            }

            DisplayCount("DxMessaging (GameObject) - Normal", count, timeout, allocating);
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
            InstanceId target = component;

            token.RegisterComponentTargeted<ComplexTargetedMessage>(component, Handle);
            // Pre-warm
            message.EmitTargeted(target);

            timer.Restart();
            do
            {
                for (int i = 0; i < NumInvocationsPerIteration; ++i)
                {
                    message.EmitTargeted(target);
                }
            } while (timer.Elapsed < timeout);

            bool allocating;
            try
            {
                Assert.That(() => message.EmitTargeted(target), Is.Not.AllocatingGCMemory());
                allocating = false;
            }
            catch
            {
                allocating = true;
            }
            DisplayCount("DxMessaging (Component) - Normal", count, timeout, allocating);
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
            InstanceId target = go;
            token.RegisterGameObjectTargeted<ComplexTargetedMessage>(go, Handle);
            // Pre-warm
            message.EmitTargeted(target);

            timer.Restart();
            do
            {
                for (int i = 0; i < NumInvocationsPerIteration; ++i)
                {
                    message.EmitTargeted(target);
                }
            } while (timer.Elapsed < timeout);
            bool allocating;
            try
            {
                Assert.That(() => message.EmitTargeted(target), Is.Not.AllocatingGCMemory());
                allocating = false;
            }
            catch
            {
                allocating = true;
            }
            DisplayCount("DxMessaging (GameObject) - No-Copy", count, timeout, allocating);
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
            InstanceId target = component;

            token.RegisterComponentTargeted<ComplexTargetedMessage>(component, Handle);
            // Pre-warm
            message.EmitComponentTargeted(component);

            timer.Restart();
            do
            {
                for (int i = 0; i < NumInvocationsPerIteration; ++i)
                {
                    message.EmitTargeted(target);
                }
            } while (timer.Elapsed < timeout);

            bool allocating;
            try
            {
                Assert.That(() => message.EmitTargeted(target), Is.Not.AllocatingGCMemory());
                allocating = false;
            }
            catch
            {
                allocating = true;
            }
            DisplayCount("DxMessaging (Component) - No-Copy", count, timeout, allocating);
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
            // Pre-warm
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
            DisplayCount("DxMessaging (Untargeted) - No-Copy", count, timeout, allocating);
            return;

            void Handle(ref SimpleUntargetedMessage _)
            {
                ++count;
            }
        }
    }
}
