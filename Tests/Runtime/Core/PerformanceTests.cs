using System;
using DxMessaging.Core.MessageBus;
using DxMessaging.Core;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using DxMessaging.Core.Extensions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;
using Tests.Runtime.Scripts.Components;
using Tests.Runtime.Scripts.Messages;

public sealed class PerformanceTests
{
    private readonly List<GameObject> _spawned = new();

    [SetUp]
    public void Setup()
    {
        MessagingDebug.LogFunction = null;
        MessageBus messageBus = MessageHandler.MessageBus;
        Assert.IsNotNull(messageBus);
        messageBus.Log.Enabled = false;
    }

    [TearDown]
    public void Cleanup()
    {
        foreach (GameObject spawned in _spawned)
        {
            if (spawned == null)
            {
                continue;
            }

            Object.Destroy(spawned);
        }

        _spawned.Clear();
    }

    [UnityTest]
    public IEnumerator BenchmarkTargeted()
    {
        // Add some components in for good measure
        GameObject target = new(
            nameof(BenchmarkTargeted), typeof(SimpleMessageAwareComponent), typeof(SpriteRenderer),
            typeof(Rigidbody2D), typeof(CircleCollider2D));

        SimpleMessageAwareComponent component = target.GetComponent<SimpleMessageAwareComponent>();

        TimeSpan timeout = TimeSpan.FromSeconds(5);
        Debug.Log("| Message Tech | Operations / Second |");
        Debug.Log("| ------------ | ------------------- |");

        ComplexTargetedMessage message = new(Guid.NewGuid());
        Stopwatch timer = Stopwatch.StartNew();
        Unity(timer, timeout, target, component, message);
        Normal(timer, timeout, component, message);
        NoAlloc(timer, timeout, component, message);
        yield break;
    }

    void DisplayCount(string testName, int count, TimeSpan timeout)
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

    private void Normal(Stopwatch timer, TimeSpan timeout, SimpleMessageAwareComponent component, ComplexTargetedMessage message)
    {
        int count = 0;
        component.slowComplexTargetedHandler = () => ++count;
        component.complexTargetedHandler = null;
        component.SlowComplexTargetingEnabled = true;
        component.FastComplexTargetingEnabled = false;
        InstanceId target = component.gameObject;

        timer.Restart();
        do
        {
            message.EmitTargeted(target);
        }
        while (timer.Elapsed < timeout);
        DisplayCount("DxMessaging - Normal", count, timeout);
    }

    private void NoAlloc(Stopwatch timer, TimeSpan timeout, SimpleMessageAwareComponent component, ComplexTargetedMessage message)
    {
        int count = 0;
        component.slowComplexTargetedHandler = null;
        component.complexTargetedHandler = () => ++count;
        component.SlowComplexTargetingEnabled = false;
        component.FastComplexTargetingEnabled = true;
        InstanceId target = component.gameObject;

        timer.Restart();
        do
        {
            message.EmitTargeted(target);
        }
        while (timer.Elapsed < timeout);
        DisplayCount("DxMessaging - No-Alloc", count, timeout);
    }
}