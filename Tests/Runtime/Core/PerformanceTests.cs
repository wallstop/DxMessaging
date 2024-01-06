namespace DxMessaging.Tests.Runtime.Core
{
    using System;
    using System.Collections;
    using System.Diagnostics;
    using DxMessaging.Core.Extensions;
    using Scripts.Components;
    using Scripts.Messages;
    using UnityEngine;
    using UnityEngine.TestTools;
    using Debug = UnityEngine.Debug;

    public sealed class PerformanceTests : MessagingTestBase
    {
        [UnityTest]
        public IEnumerator Benchmark()
        {
            // Add some components in for good measure
            GameObject target = new(
                nameof(Benchmark), typeof(SimpleMessageAwareComponent), typeof(SpriteRenderer),
                typeof(Rigidbody2D), typeof(CircleCollider2D), typeof(LineRenderer));
            _spawned.Add(target);

            SimpleMessageAwareComponent component = target.GetComponent<SimpleMessageAwareComponent>();

            TimeSpan timeout = TimeSpan.FromSeconds(1);
            Debug.Log("| Message Tech | Operations / Second |");
            Debug.Log("| ------------ | ------------------- |");

            ComplexTargetedMessage message = new(Guid.NewGuid());
            Stopwatch timer = Stopwatch.StartNew();
            Unity(timer, timeout, target, component, message);
            NormalGameObject(timer, timeout, component, message);
            NoAllocGameObject(timer, timeout, component, message);
            NoAllocComponent(timer, timeout, component, message);

            SimpleUntargetedMessage untargetedMessage = new();
            NoAllocUntargeted(timer, timeout, component, untargetedMessage);
            yield break;
        }

        private void DisplayCount(string testName, int count, TimeSpan timeout)
        {
            Debug.Log($"| {testName} | {Math.Floor(count / timeout.TotalSeconds):N0} |");
        }

        private void Unity(Stopwatch timer, TimeSpan timeout, GameObject target, SimpleMessageAwareComponent component, ComplexTargetedMessage message)
        {
            int count = 0;
            component.slowComplexTargetedHandler = () => ++count;
            timer.Restart();
            do
            {
                target.SendMessage(nameof(SimpleMessageAwareComponent.HandleSlowComplexTargetedMessage), message);
            }
            while (timer.Elapsed < timeout);
            DisplayCount("Unity", count, timeout);
        }

        private void NormalGameObject(Stopwatch timer, TimeSpan timeout, SimpleMessageAwareComponent component, ComplexTargetedMessage message)
        {
            int count = 0;
            component.slowComplexTargetedHandler = () => ++count;
            component.complexTargetedHandler = null;
            component.SlowComplexTargetingEnabled = true;
            component.FastComplexTargetingEnabled = false;

            timer.Restart();
            do
            {
                message.EmitGameObjectTargeted(component.gameObject);
            }
            while (timer.Elapsed < timeout);
            DisplayCount("DxMessaging (GameObject) - Normal", count, timeout);
        }

        private void NoAllocGameObject(Stopwatch timer, TimeSpan timeout, SimpleMessageAwareComponent component, ComplexTargetedMessage message)
        {
            int count = 0;
            component.slowComplexTargetedHandler = null;
            component.complexTargetedHandler = () => ++count;
            component.SlowComplexTargetingEnabled = false;
            component.FastComplexTargetingEnabled = true;

            timer.Restart();
            do
            {
                message.EmitGameObjectTargeted(component.gameObject);
            }
            while (timer.Elapsed < timeout);
            DisplayCount("DxMessaging (GameObject) - No-Alloc", count, timeout);
        }

        private void NoAllocComponent(Stopwatch timer, TimeSpan timeout, SimpleMessageAwareComponent component, ComplexTargetedMessage message)
        {
            int count = 0;
            component.slowComplexTargetedHandler = null;
            component.complexTargetedHandler = null;
            component.complexComponentTargetedHandler = () => ++count;
            component.SlowComplexTargetingEnabled = false;
            component.FastComplexTargetingEnabled = false;

            timer.Restart();
            do
            {
                message.EmitComponentTargeted(component);
            }
            while (timer.Elapsed < timeout);
            DisplayCount("DxMessaging (Component) - No-Alloc", count, timeout);
        }

        private void NoAllocUntargeted(Stopwatch timer, TimeSpan timeout, SimpleMessageAwareComponent component, SimpleUntargetedMessage message)
        {
            int count = 0;
            component.untargetedHandler = () => ++count;

            timer.Restart();
            do
            {
                message.EmitUntargeted();
            }
            while (timer.Elapsed < timeout);
            DisplayCount("DxMessaging (Untargeted) - No-Alloc", count, timeout);
        }
    }
}