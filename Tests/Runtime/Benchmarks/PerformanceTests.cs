#if UNITY_2021_3_OR_NEWER
namespace DxMessaging.Tests.Runtime.Benchmarks
{
    using System;
    using System.Diagnostics;
    using DxMessaging.Core;
    using DxMessaging.Core.Extensions;
    using DxMessaging.Core.Messages;
    using NUnit.Framework;
    using Scripts.Components;
    using Scripts.Messages;
    using UnityEngine;
    using UnityEngine.TestTools.Constraints;
    using Is = NUnit.Framework.Is;

    public sealed class PerformanceTests : BenchmarkTestBase
    {
        protected override bool MessagingDebugEnabled => false;

        [Test]
        public void Benchmark()
        {
            string operatingSystemSection = BenchmarkDocumentation.GetOperatingSystemSection();
            BenchmarkSession session = new(
                operatingSystemSection,
                "## ",
                new Func<string>[]
                {
                    BenchmarkDocumentation.TryFindPerformanceDocPath,
                    BenchmarkDocumentation.TryFindReadmePath,
                }
            );

            RunWithSession(
                session,
                () =>
                {
                    TimeSpan timeout = TimeSpan.FromSeconds(5);
                    Stopwatch timer = Stopwatch.StartNew();

                    ComplexTargetedMessage message = new(Guid.NewGuid());
                    ReflexiveMessage reflexiveMessage = new(
                        nameof(SimpleMessageAwareComponent.HandleSlowComplexTargetedMessage),
                        ReflexiveSendMode.Flat,
                        message
                    );

                    RunWithComponent(component =>
                        Unity(timer, timeout, component.gameObject, message)
                    );

                    RunWithComponent(component =>
                        NormalGameObject(timer, timeout, component, message)
                    );
                    RunWithComponent(component =>
                        NormalComponent(timer, timeout, component, message)
                    );
                    RunWithComponent(component =>
                        NoCopyGameObject(timer, timeout, component, message)
                    );
                    RunWithComponent(component =>
                        NoCopyComponent(timer, timeout, component, message)
                    );

                    SimpleUntargetedMessage untargetedMessage = new();
                    RunWithComponent(component =>
                        NoCopyUntargeted(timer, timeout, component, untargetedMessage)
                    );
                    RunWithComponent(component =>
                        InterceptorHeavyUntargeted(timer, timeout, component, untargetedMessage)
                    );
                    RunWithComponent(component =>
                        PostProcessorHeavyUntargeted(timer, timeout, component, untargetedMessage)
                    );
                    RunWithComponent(component =>
                        ReflexiveOneArgument(timer, timeout, component.gameObject, reflexiveMessage)
                    );
                    RunWithComponent(component =>
                        ReflexiveTwoArguments(timer, timeout, component.gameObject)
                    );
                    RunWithComponent(component =>
                        ReflexiveThreeArguments(timer, timeout, component.gameObject)
                    );
                }
            );
        }

        private void DisplayCount(string testName, int count, TimeSpan timeout, bool allocating)
        {
            RecordBenchmark(testName, count, timeout, allocating);
        }

        private void Unity(
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

        private void ReflexiveThreeArguments(Stopwatch timer, TimeSpan timeout, GameObject go)
        {
            int count = 0;
            if (!go.TryGetComponent(out SimpleMessageAwareComponent component))
            {
                component = go.AddComponent<SimpleMessageAwareComponent>();
            }

            component.reflexiveThreeArgumentHandler = () => ++count;
            ReflexiveMessage message = new(
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

        private void ReflexiveTwoArguments(Stopwatch timer, TimeSpan timeout, GameObject go)
        {
            int count = 0;
            if (!go.TryGetComponent(out SimpleMessageAwareComponent component))
            {
                component = go.AddComponent<SimpleMessageAwareComponent>();
            }

            component.reflexiveTwoArgumentHandler = () => ++count;
            ReflexiveMessage message = new(
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

        private void ReflexiveOneArgument(
            Stopwatch timer,
            TimeSpan timeout,
            GameObject go,
            ReflexiveMessage message
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
            MessageRegistrationToken token = GetToken(component);

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
            MessageRegistrationToken token = GetToken(component);
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
            MessageRegistrationToken token = GetToken(component);

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
            MessageRegistrationToken token = GetToken(component);
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
            MessageRegistrationToken token = GetToken(component);

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

        private void InterceptorHeavyUntargeted(
            Stopwatch timer,
            TimeSpan timeout,
            EmptyMessageAwareComponent component,
            SimpleUntargetedMessage message
        )
        {
            int handlerInvocationCount = 0;
            int interceptorInvocationCount = 0;
            MessageRegistrationToken token = GetToken(component);

            const int InterceptorCount = 8;
            for (int i = 0; i < InterceptorCount; ++i)
            {
                token.RegisterUntargetedInterceptor<SimpleUntargetedMessage>(
                    (ref SimpleUntargetedMessage _) =>
                    {
                        ++interceptorInvocationCount;
                        return true;
                    }
                );
            }

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

            Assert.Greater(
                interceptorInvocationCount,
                0,
                "Interceptor-heavy benchmark should invoke registered interceptors."
            );

            DisplayCount(
                "DxMessaging (Untargeted) - Interceptors",
                handlerInvocationCount,
                timeout,
                allocating
            );
            return;

            void Handle(ref SimpleUntargetedMessage _)
            {
                ++handlerInvocationCount;
            }
        }

        private void PostProcessorHeavyUntargeted(
            Stopwatch timer,
            TimeSpan timeout,
            EmptyMessageAwareComponent component,
            SimpleUntargetedMessage message
        )
        {
            int handlerInvocationCount = 0;
            int postProcessorInvocationCount = 0;
            MessageRegistrationToken token = GetToken(component);

            const int PostProcessorCount = 8;
            for (int i = 0; i < PostProcessorCount; ++i)
            {
                token.RegisterUntargetedPostProcessor<SimpleUntargetedMessage>(
                    (ref SimpleUntargetedMessage _) => ++postProcessorInvocationCount
                );
            }

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

            Assert.Greater(
                postProcessorInvocationCount,
                0,
                "Post-processor benchmark should invoke registered post-processors."
            );

            DisplayCount(
                "DxMessaging (Untargeted) - Post-Processors",
                handlerInvocationCount,
                timeout,
                allocating
            );
            return;

            void Handle(ref SimpleUntargetedMessage _)
            {
                ++handlerInvocationCount;
            }
        }
    }
}

#endif
