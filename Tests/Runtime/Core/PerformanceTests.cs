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
        MessageBus messageBus = MessageHandler.MessageBus as MessageBus;
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
        GameObject test = new(
            nameof(BenchmarkTargeted), typeof(SimpleMessageAwareComponent), typeof(SpriteRenderer),
            typeof(Rigidbody2D), typeof(CircleCollider2D));

        SimpleMessageAwareComponent component = test.GetComponent<SimpleMessageAwareComponent>();
        ComplexTargetedMessage message = new(Guid.NewGuid());
        int count = 0;
        component.slowComplexTargetedHandler = () => ++count;
        TimeSpan timeout = TimeSpan.FromSeconds(5);
        Debug.Log("| Message Tech | Operations / Second |");
        Debug.Log("| ------------ | ------------------- |");
        void DisplayCount(string testName)
        {
            Debug.Log($"| {testName} | {(Math.Floor(count / timeout.TotalSeconds)):N0} |");
        }

        Stopwatch timer = Stopwatch.StartNew();
        do
        {
            test.SendMessage(nameof(SimpleMessageAwareComponent.HandleSlowComplexTargetedMessage), message);
        }
        while (timer.Elapsed < timeout);
        DisplayCount("Unity");

        count = 0;
        component.slowComplexTargetedHandler = () => ++count;
        component.SlowComplexTargetingEnabled = true;
        component.FastComplexTargetingEnabled = false;
        timer.Restart();

        do
        {
            message.EmitTargeted(test);
        }
        while (timer.Elapsed < timeout);
        DisplayCount("DxMessaging - Normal");

        count = 0;
        component.slowComplexTargetedHandler = null;
        component.complexTargetedHandler = () => ++count;
        component.SlowComplexTargetingEnabled = false;
        component.FastComplexTargetingEnabled = true;
        timer.Restart();

        do
        {
            message.EmitTargeted(test);
        }
        while (timer.Elapsed < timeout);
        DisplayCount("DxMessaging - No-Alloc");
        yield break;
    }
}