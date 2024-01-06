namespace DxMessaging.Tests.Runtime.Benchmarks
{
    using System;
    using System.Collections;
    using System.Diagnostics;
    using Core;
    using DxMessaging.Core;
    using DxMessaging.Core.Extensions;
    using NUnit.Framework;
    using Scripts.Components;
    using Scripts.Messages;
    using UnityEngine;
    using UnityEngine.TestTools;
    using Debug = UnityEngine.Debug;
    using Object = UnityEngine.Object;

    public sealed class PerformanceTests : MessagingTestBase
    {
        [SetUp]
        public override void Setup()
        {
            base.Setup();
            MessagingDebug.LogFunction = (_1, _2) => { };
        }

        [UnityTest]
        public IEnumerator Benchmark()
        {
            TimeSpan timeout = TimeSpan.FromSeconds(1);

            Debug.Log("| Message Tech | Operations / Second |");
            Debug.Log("| ------------ | ------------------- |");

            ComplexTargetedMessage message = new(Guid.NewGuid());
            Stopwatch timer = Stopwatch.StartNew();

            RunTest(component => Unity(timer, timeout, component.gameObject, message));
            RunTest(component => NormalGameObject(timer, timeout, component, message));
            RunTest(component => NoCopyGameObject(timer, timeout, component, message));
            RunTest(component => NoCopyComponent(timer, timeout, component, message));
            
            SimpleUntargetedMessage untargetedMessage = new();
            RunTest(component => NoCopyUntargeted(timer, timeout, component, untargetedMessage));
            yield break;
        }

        private GameObject CreateGameObject()
        {
            GameObject target = new(
                nameof(Benchmark), typeof(EmptyMessageAwareComponent), typeof(SpriteRenderer),
                typeof(Rigidbody2D), typeof(CircleCollider2D), typeof(LineRenderer));
            _spawned.Add(target);

            return target;
        }

        private void DisplayCount(string testName, int count, TimeSpan timeout)
        {
            Debug.Log($"| {testName} | {Math.Floor(count / timeout.TotalSeconds):N0} |");
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

        private void Unity(Stopwatch timer, TimeSpan timeout, GameObject target,  ComplexTargetedMessage message)
        {
            int count = 0;
            var component = target.AddComponent<SimpleMessageAwareComponent>();
            component.slowComplexTargetedHandler = () => ++count;
            timer.Restart();
            do
            {
                target.SendMessage(nameof(SimpleMessageAwareComponent.HandleSlowComplexTargetedMessage), message);
            }
            while (timer.Elapsed < timeout);
            DisplayCount("Unity", count, timeout);
        }

        private void NormalGameObject(Stopwatch timer, TimeSpan timeout, EmptyMessageAwareComponent component, ComplexTargetedMessage message)
        {
            int count = 0;
            var token = GetToken(component);

            void Handle(ComplexTargetedMessage _)
            {
                ++count;
            }

            GameObject go = component.gameObject;
            token.RegisterGameObjectTargeted<ComplexTargetedMessage>(go, Handle);
            timer.Restart();
            do
            {
                message.EmitGameObjectTargeted(go);
            }
            while (timer.Elapsed < timeout);
            DisplayCount("DxMessaging (GameObject) - Normal", count, timeout);
        }

        private void NoCopyGameObject(Stopwatch timer, TimeSpan timeout, EmptyMessageAwareComponent component, ComplexTargetedMessage message)
        {
            int count = 0;
            var token = GetToken(component);

            void Handle(ref ComplexTargetedMessage _)
            {
                ++count;
            }

            GameObject go = component.gameObject;
            token.RegisterGameObjectTargeted<ComplexTargetedMessage>(go, Handle);

            timer.Restart();
            do
            {
                message.EmitGameObjectTargeted(component.gameObject);
            }
            while (timer.Elapsed < timeout);
            DisplayCount("DxMessaging (GameObject) - No-Copy", count, timeout);
        }

        private void NoCopyComponent(Stopwatch timer, TimeSpan timeout, EmptyMessageAwareComponent component, ComplexTargetedMessage message)
        {
            int count = 0;
            var token = GetToken(component);

            void Handle(ref ComplexTargetedMessage _)
            {
                ++count;
            }

            token.RegisterComponentTargeted<ComplexTargetedMessage>(component, Handle);

            timer.Restart();
            do
            {
                message.EmitComponentTargeted(component);
            }
            while (timer.Elapsed < timeout);
            DisplayCount("DxMessaging (Component) - No-Copy", count, timeout);
        }

        private void NoCopyUntargeted(Stopwatch timer, TimeSpan timeout, EmptyMessageAwareComponent component, SimpleUntargetedMessage message)
        {
            int count = 0;
            var token = GetToken(component);

            void Handle(ref SimpleUntargetedMessage _)
            {
                ++count;
            }

            token.RegisterUntargeted<SimpleUntargetedMessage>(Handle);

            timer.Restart();
            do
            {
                message.EmitUntargeted();
            }
            while (timer.Elapsed < timeout);
            DisplayCount("DxMessaging (Untargeted) - No-Copy", count, timeout);
        }
    }
}