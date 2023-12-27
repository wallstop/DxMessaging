using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using DxMessaging.Core;
using DxMessaging.Core.Extensions;
using DxMessaging.Core.MessageBus;
using DxMessaging.Core.Messages;
using DxMessaging.Unity;
using NUnit.Framework;
using Tests.Runtime.Scripts.Components;
using Tests.Runtime.Scripts.Messages;
using UnityEngine;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public sealed class NominalTests : IPrebuildSetup, IPostBuildCleanup
{
    private readonly List<GameObject> _spawned = new();

    public void Setup()
    {
        MessagingDebug.LogFunction = Debug.Log;
        MessageBus messageBus = MessageHandler.MessageBus as MessageBus;
        Assert.IsNotNull(messageBus);
        messageBus.Log.Enabled = true;
    }

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
    public IEnumerator Nominal()
    {
        try
        {
            IEnumerator waitUntilMessageHandlerIsFresh = WaitUntilMessageHandlerIsFresh();
            while (waitUntilMessageHandlerIsFresh.MoveNext())
            {
                yield return waitUntilMessageHandlerIsFresh.Current;
            }

            GameObject test = new(nameof(Nominal), typeof(SimpleMessageAwareComponent));
            _spawned.Add(test);
            GameObject nonTest = new("Non-Test", typeof(SimpleMessageAwareComponent));
            _spawned.Add(nonTest);

            SimpleMessageAwareComponent firstComponent = test.GetComponent<SimpleMessageAwareComponent>();

            int untargetedWorks = 0;
            int targetedWorks = 0;
            int broadcastWorks = 0;
            Dictionary<object, int> componentTargetedWorks = new();
            Dictionary<object, int> componentBroadcastWorks = new();

            void SetupComponent(SimpleMessageAwareComponent toSetup)
            {
                toSetup.untargetedHandler = _ => untargetedWorks = ++untargetedWorks;
                toSetup.targetedHandler = _ => targetedWorks = ++targetedWorks;
                toSetup.broadcastHandler = _ => broadcastWorks = ++broadcastWorks;
                toSetup.componentTargetedHandler = _ =>
                {
                    if (!componentTargetedWorks.TryGetValue(toSetup, out int existing))
                    {
                        existing = 0;
                    }

                    componentTargetedWorks[toSetup] = ++existing;
                };
                toSetup.componentBroadcastHandler = _ =>
                {
                    if (!componentBroadcastWorks.TryGetValue(toSetup, out int existing))
                    {
                        existing = 0;
                    }

                    componentBroadcastWorks[toSetup] = ++existing;
                };
            }

            SetupComponent(firstComponent);

            // Generate non-component targeted methods
            SimpleUntargetedMessage untargetedMessage = new();
            untargetedMessage.EmitUntargeted();
            Assert.AreEqual(1, untargetedWorks);
            Assert.AreEqual(0, targetedWorks);
            Assert.AreEqual(0, broadcastWorks);
            Assert.AreEqual(0, componentTargetedWorks.Count);
            Assert.AreEqual(0, componentBroadcastWorks.Count);

            untargetedMessage.EmitUntargeted();
            Assert.AreEqual(2, untargetedWorks);
            Assert.AreEqual(0, targetedWorks);
            Assert.AreEqual(0, broadcastWorks);
            Assert.AreEqual(0, componentTargetedWorks.Count);
            Assert.AreEqual(0, componentBroadcastWorks.Count);

            SimpleTargetedMessage targetedMessage = new();
            targetedMessage.EmitGameObjectTargeted(test);
            Assert.AreEqual(2, untargetedWorks);
            Assert.AreEqual(1, targetedWorks);
            Assert.AreEqual(0, broadcastWorks);
            Assert.AreEqual(0, componentTargetedWorks.Count);
            Assert.AreEqual(0, componentBroadcastWorks.Count);

            SimpleBroadcastMessage broadcastMessage = new();
            broadcastMessage.EmitGameObjectBroadcast(test);
            Assert.AreEqual(2, untargetedWorks);
            Assert.AreEqual(1, targetedWorks);
            Assert.AreEqual(1, broadcastWorks);
            Assert.AreEqual(0, componentTargetedWorks.Count);
            Assert.AreEqual(0, componentBroadcastWorks.Count);

            yield return null;

            /*
                If we add a second component, our Untargeted, GameObjectTargeted,
                and GameObjectBroadcast receivers will be called *twice* for each
                message emission, once per component.
             */
            SimpleMessageAwareComponent secondComponent = test.AddComponent<SimpleMessageAwareComponent>();
            SetupComponent(secondComponent);

            // Targeted
            targetedMessage.EmitGameObjectTargeted(test);
            Assert.AreEqual(2, untargetedWorks);
            Assert.AreEqual(3, targetedWorks);
            Assert.AreEqual(1, broadcastWorks);
            Assert.AreEqual(0, componentTargetedWorks.Count);
            Assert.AreEqual(0, componentBroadcastWorks.Count);

            targetedMessage.EmitGameObjectTargeted(nonTest);
            Assert.AreEqual(2, untargetedWorks);
            Assert.AreEqual(3, targetedWorks);
            Assert.AreEqual(1, broadcastWorks);
            Assert.AreEqual(0, componentTargetedWorks.Count);
            Assert.AreEqual(0, componentBroadcastWorks.Count);

            // Component Targeted
            targetedMessage.EmitComponentTargeted(firstComponent);
            Assert.AreEqual(2, untargetedWorks);
            Assert.AreEqual(3, targetedWorks);
            Assert.AreEqual(1, broadcastWorks);
            Assert.AreEqual(1, componentTargetedWorks.Count);
            Assert.AreEqual(1, componentTargetedWorks[firstComponent]);
            Assert.AreEqual(0, componentBroadcastWorks.Count);

            targetedMessage.EmitComponentTargeted(secondComponent);
            Assert.AreEqual(2, untargetedWorks);
            Assert.AreEqual(3, targetedWorks);
            Assert.AreEqual(1, broadcastWorks);
            Assert.AreEqual(2, componentTargetedWorks.Count);
            Assert.AreEqual(1, componentTargetedWorks[firstComponent]);
            Assert.AreEqual(1, componentTargetedWorks[secondComponent]);
            Assert.AreEqual(0, componentBroadcastWorks.Count);

            targetedMessage.EmitComponentTargeted(firstComponent);
            Assert.AreEqual(2, untargetedWorks);
            Assert.AreEqual(3, targetedWorks);
            Assert.AreEqual(1, broadcastWorks);
            Assert.AreEqual(2, componentTargetedWorks.Count);
            Assert.AreEqual(2, componentTargetedWorks[firstComponent]);
            Assert.AreEqual(1, componentTargetedWorks[secondComponent]);
            Assert.AreEqual(0, componentBroadcastWorks.Count);

            targetedMessage.EmitComponentTargeted(secondComponent);
            Assert.AreEqual(2, untargetedWorks);
            Assert.AreEqual(3, targetedWorks);
            Assert.AreEqual(1, broadcastWorks);
            Assert.AreEqual(2, componentTargetedWorks.Count);
            Assert.AreEqual(2, componentTargetedWorks[firstComponent]);
            Assert.AreEqual(2, componentTargetedWorks[secondComponent]);
            Assert.AreEqual(0, componentBroadcastWorks.Count);

            // Broadcast
            broadcastMessage.EmitGameObjectBroadcast(nonTest);
            Assert.AreEqual(2, untargetedWorks);
            Assert.AreEqual(3, targetedWorks);
            Assert.AreEqual(1, broadcastWorks);
            Assert.AreEqual(2, componentTargetedWorks.Count);
            Assert.AreEqual(2, componentTargetedWorks[firstComponent]);
            Assert.AreEqual(2, componentTargetedWorks[secondComponent]);
            Assert.AreEqual(0, componentBroadcastWorks.Count);

            broadcastMessage.EmitGameObjectBroadcast(test);
            Assert.AreEqual(2, untargetedWorks);
            Assert.AreEqual(3, targetedWorks);
            Assert.AreEqual(3, broadcastWorks);
            Assert.AreEqual(2, componentTargetedWorks.Count);
            Assert.AreEqual(2, componentTargetedWorks[firstComponent]);
            Assert.AreEqual(2, componentTargetedWorks[secondComponent]);
            Assert.AreEqual(0, componentBroadcastWorks.Count);

            // Component Broadcast
            broadcastMessage.EmitComponentBroadcast(firstComponent);
            Assert.AreEqual(2, untargetedWorks);
            Assert.AreEqual(3, targetedWorks);
            Assert.AreEqual(3, broadcastWorks);
            Assert.AreEqual(2, componentTargetedWorks.Count);
            Assert.AreEqual(2, componentTargetedWorks[firstComponent]);
            Assert.AreEqual(2, componentTargetedWorks[secondComponent]);
            Assert.AreEqual(1, componentBroadcastWorks.Count);
            Assert.AreEqual(1, componentBroadcastWorks[firstComponent]);

            broadcastMessage.EmitComponentBroadcast(secondComponent);
            Assert.AreEqual(2, untargetedWorks);
            Assert.AreEqual(3, targetedWorks);
            Assert.AreEqual(3, broadcastWorks);
            Assert.AreEqual(2, componentTargetedWorks.Count);
            Assert.AreEqual(2, componentTargetedWorks[firstComponent]);
            Assert.AreEqual(2, componentTargetedWorks[secondComponent]);
            Assert.AreEqual(2, componentBroadcastWorks.Count);
            Assert.AreEqual(1, componentBroadcastWorks[firstComponent]);
            Assert.AreEqual(1, componentBroadcastWorks[secondComponent]);

            broadcastMessage.EmitComponentBroadcast(firstComponent);
            Assert.AreEqual(2, untargetedWorks);
            Assert.AreEqual(3, targetedWorks);
            Assert.AreEqual(3, broadcastWorks);
            Assert.AreEqual(2, componentTargetedWorks.Count);
            Assert.AreEqual(2, componentTargetedWorks[firstComponent]);
            Assert.AreEqual(2, componentTargetedWorks[secondComponent]);
            Assert.AreEqual(2, componentBroadcastWorks.Count);
            Assert.AreEqual(2, componentBroadcastWorks[firstComponent]);
            Assert.AreEqual(1, componentBroadcastWorks[secondComponent]);

            broadcastMessage.EmitComponentBroadcast(secondComponent);
            Assert.AreEqual(2, untargetedWorks);
            Assert.AreEqual(3, targetedWorks);
            Assert.AreEqual(3, broadcastWorks);
            Assert.AreEqual(2, componentTargetedWorks.Count);
            Assert.AreEqual(2, componentTargetedWorks[firstComponent]);
            Assert.AreEqual(2, componentTargetedWorks[secondComponent]);
            Assert.AreEqual(2, componentBroadcastWorks.Count);
            Assert.AreEqual(2, componentBroadcastWorks[firstComponent]);
            Assert.AreEqual(2, componentBroadcastWorks[secondComponent]);

            // Finally, re-emit the targeted message - it should be received by both components
            untargetedMessage.EmitUntargeted();
            Assert.AreEqual(4, untargetedWorks);
            Assert.AreEqual(3, targetedWorks);
            Assert.AreEqual(3, broadcastWorks);
            Assert.AreEqual(2, componentTargetedWorks.Count);
            Assert.AreEqual(2, componentTargetedWorks[firstComponent]);
            Assert.AreEqual(2, componentTargetedWorks[secondComponent]);
            Assert.AreEqual(2, componentBroadcastWorks.Count);
            Assert.AreEqual(2, componentBroadcastWorks[firstComponent]);
            Assert.AreEqual(2, componentBroadcastWorks[secondComponent]);
        }
        finally
        {
            Cleanup();
        }
    }

    [UnityTest]
    public IEnumerator Lifetime()
    {
        try
        {
            IEnumerator waitUntilMessageHandlerIsFresh = WaitUntilMessageHandlerIsFresh();
            while (waitUntilMessageHandlerIsFresh.MoveNext())
            {
                yield return waitUntilMessageHandlerIsFresh.Current;
            }

            MessageBus messageBus = MessageHandler.MessageBus as MessageBus;
            Assert.IsNotNull(messageBus);

            GameObject test = new(nameof(Lifetime), typeof(SimpleMessageAwareComponent));
            _spawned.Add(test);

            SimpleMessageAwareComponent firstComponent = test.GetComponent<SimpleMessageAwareComponent>();

            Assert.AreEqual(1, messageBus.RegisteredUntargeted);
            // One for the game object, one for the component = 2
            Assert.AreEqual(2, messageBus.RegisteredTargeted);
            Assert.AreEqual(2, messageBus.RegisteredBroadcast);

            yield return null;

            SimpleMessageAwareComponent secondComponent = test.AddComponent<SimpleMessageAwareComponent>();
            Assert.AreEqual(1, messageBus.RegisteredUntargeted);
            // One for the game object, one for the first component, one for the second component = 3
            Assert.AreEqual(3, messageBus.RegisteredTargeted);
            Assert.AreEqual(3, messageBus.RegisteredBroadcast);

            secondComponent.enabled = false;
            yield return null;

            // 3 - one component (disabled)
            Assert.AreEqual(1, messageBus.RegisteredUntargeted);
            Assert.AreEqual(2, messageBus.RegisteredTargeted);
            Assert.AreEqual(2, messageBus.RegisteredBroadcast);

            firstComponent.enabled = false;
            yield return null;

            // No active scripts, no active handlers
            Assert.AreEqual(0, messageBus.RegisteredUntargeted);
            Assert.AreEqual(0, messageBus.RegisteredTargeted);
            Assert.AreEqual(0, messageBus.RegisteredBroadcast);

            test.SetActive(false);
            yield return null;

            Assert.AreEqual(0, messageBus.RegisteredUntargeted);
            Assert.AreEqual(0, messageBus.RegisteredTargeted);
            Assert.AreEqual(0, messageBus.RegisteredBroadcast);

            firstComponent.enabled = true;
            yield return null;

            // Game object is still disabled - shouldn't have active child scripts
            Assert.AreEqual(0, messageBus.RegisteredUntargeted);
            Assert.AreEqual(0, messageBus.RegisteredTargeted);
            Assert.AreEqual(0, messageBus.RegisteredBroadcast);

            test.SetActive(true);
            yield return null;

            Assert.AreEqual(1, messageBus.RegisteredUntargeted);
            Assert.AreEqual(2, messageBus.RegisteredTargeted);
            Assert.AreEqual(2, messageBus.RegisteredBroadcast);

            Object.Destroy(firstComponent);
            yield return null;

            Assert.AreEqual(0, messageBus.RegisteredUntargeted);
            Assert.AreEqual(0, messageBus.RegisteredTargeted);
            Assert.AreEqual(0, messageBus.RegisteredBroadcast);

            secondComponent.enabled = true;
            yield return null;

            Assert.AreEqual(1, messageBus.RegisteredUntargeted);
            Assert.AreEqual(2, messageBus.RegisteredTargeted);
            Assert.AreEqual(2, messageBus.RegisteredBroadcast);

            Object.Destroy(test);
            yield return null;

            Assert.AreEqual(0, messageBus.RegisteredUntargeted);
            Assert.AreEqual(0, messageBus.RegisteredTargeted);
            Assert.AreEqual(0, messageBus.RegisteredBroadcast);
        }
        finally
        {
            Cleanup();
        }
    }

    [UnityTest]
    public IEnumerator NonMessagingObjects()
    {
        try
        {
            IEnumerator waitUntilMessageHandlerIsFresh = WaitUntilMessageHandlerIsFresh();
            while (waitUntilMessageHandlerIsFresh.MoveNext())
            {
                yield return waitUntilMessageHandlerIsFresh.Current;
            }

            MessageBus messageBus = MessageHandler.MessageBus as MessageBus;
            Assert.IsNotNull(messageBus);

            GameObject test1 = new("NonMessaging1");
            _spawned.Add(test1);

            Assert.AreEqual(0, messageBus.RegisteredUntargeted);
            Assert.AreEqual(0, messageBus.RegisteredTargeted);
            Assert.AreEqual(0, messageBus.RegisteredBroadcast);

            GameObject test2 = new("NonMessaging1", typeof(SpriteRenderer), typeof(MessageHandler));
            _spawned.Add(test2);

            Assert.AreEqual(0, messageBus.RegisteredUntargeted);
            Assert.AreEqual(0, messageBus.RegisteredTargeted);
            Assert.AreEqual(0, messageBus.RegisteredBroadcast);
        }
        finally
        {
            Cleanup();
        }
    }

    [UnityTest]
    public IEnumerator DedupedRegistration()
    {
        try
        {
            IEnumerator waitUntilMessageHandlerIsFresh = WaitUntilMessageHandlerIsFresh();
            while (waitUntilMessageHandlerIsFresh.MoveNext())
            {
                yield return waitUntilMessageHandlerIsFresh.Current;
            }

            GameObject test = new(nameof(DedupedRegistration), typeof(SimpleMessageAwareComponent));
            _spawned.Add(test);

            SimpleMessageAwareComponent component = test.GetComponent<SimpleMessageAwareComponent>();

            int unTargetedCount = 0;
            int targetedCount = 0;
            int broadcastCount = 0;
            int componentTargetedCount = 0;
            int componentBroadcastCount = 0;
            component.untargetedHandler = _ => ++unTargetedCount;
            component.targetedHandler = _ => ++targetedCount;
            component.broadcastHandler = _ => ++broadcastCount;
            component.componentTargetedHandler = _ => ++componentTargetedCount;
            component.componentBroadcastHandler = _ => ++componentBroadcastCount;

            MessageRegistrationToken token = GetToken(component);
            HashSet<MessageRegistrationHandle> handles = new();
            try
            {
                IEnumerator TestLifecycle(Action emitMessage, MessageRegistrationHandle handle, Func<int> count)
                {
                    _ = handles.Add(handle);
                    emitMessage();
                    Assert.AreEqual(1, count());

                    component.enabled = false;
                    yield return null;

                    emitMessage();
                    Assert.AreEqual(1, count());
                    emitMessage();
                    Assert.AreEqual(1, count());
                    component.enabled = true;
                    yield return null;

                    emitMessage();
                    Assert.AreEqual(2, count());

                    _ = handles.Remove(handle);
                    token.RemoveRegistration(handle);

                    emitMessage();
                    // The existing handler should have picked it up
                    Assert.AreEqual(3, count());

                    emitMessage();
                    // The existing handler should have picked it up
                    Assert.AreEqual(4, count());
                }

                // Untargeted
                SimpleUntargetedMessage untargetedMessage = new();
                MessageRegistrationHandle untargetedHandle =
                    token.RegisterUntargeted<SimpleUntargetedMessage>(component.HandleSimpleUntargetedMessage);
                IEnumerator untargetedLifecycle = TestLifecycle(
                    () => untargetedMessage.EmitUntargeted(), untargetedHandle, () => unTargetedCount);
                while (untargetedLifecycle.MoveNext())
                {
                    yield return untargetedLifecycle.Current;
                }

                // Targeted
                MessageRegistrationHandle targetedHandle =
                    token.RegisterGameObjectTargeted<SimpleTargetedMessage>(
                        test, component.HandleSimpleTargetedMessage);
                SimpleTargetedMessage targetedMessage = new();
                IEnumerator targetedLifecycle = TestLifecycle(
                    () => targetedMessage.EmitGameObjectTargeted(test), targetedHandle, () => targetedCount);
                while (targetedLifecycle.MoveNext())
                {
                    yield return targetedLifecycle.Current;
                }

                // Broadcast
                MessageRegistrationHandle broadcastHandle =
                    token.RegisterGameObjectBroadcast<SimpleBroadcastMessage>(
                        test, component.HandleSimpleBroadcastMessage);
                SimpleBroadcastMessage broadcastMessage = new();
                IEnumerator broadcastLifecycle = TestLifecycle(
                    () => broadcastMessage.EmitGameObjectBroadcast(test), broadcastHandle, () => broadcastCount);
                while (broadcastLifecycle.MoveNext())
                {
                    yield return broadcastLifecycle.Current;
                }

                // Component Targeted
                MessageRegistrationHandle componentTargetedHandle =
                    token.RegisterComponentTargeted<SimpleTargetedMessage>(
                        component, component.HandleSimpleComponentTargetedMessage);
                IEnumerator componentTargetedLifecycle = TestLifecycle(
                    () => targetedMessage.EmitComponentTargeted(component), componentTargetedHandle,
                    () => componentTargetedCount);
                while (componentTargetedLifecycle.MoveNext())
                {
                    yield return componentTargetedLifecycle.Current;
                }

                // Component Broadcast
                MessageRegistrationHandle componentBroadcastHandle =
                    token.RegisterGameObjectBroadcast<SimpleBroadcastMessage>(
                        test, component.HandleSimpleBroadcastMessage);
                IEnumerator componentBroadcastLifecycle = TestLifecycle(
                    () => broadcastMessage.EmitComponentBroadcast(component), componentBroadcastHandle,
                    () => componentBroadcastCount);
                while (componentBroadcastLifecycle.MoveNext())
                {
                    yield return componentBroadcastLifecycle.Current;
                }
            }
            finally
            {
                foreach (MessageRegistrationHandle handle in handles)
                {
                    token.RemoveRegistration(handle);
                }
            }
        }
        finally
        {
            Cleanup();
        }
    }

    [UnityTest]
    public IEnumerator Interceptors()
    {
        try
        {
            IEnumerator waitUntilMessageHandlerIsFresh = WaitUntilMessageHandlerIsFresh();
            while (waitUntilMessageHandlerIsFresh.MoveNext())
            {
                yield return waitUntilMessageHandlerIsFresh.Current;
            }

            GameObject test = new(nameof(Interceptors), typeof(SimpleMessageAwareComponent));
            _spawned.Add(test);

            SimpleMessageAwareComponent component = test.GetComponent<SimpleMessageAwareComponent>();

            int unTargetedCount = 0;
            int targetedCount = 0;
            int broadcastCount = 0;
            int componentTargetedCount = 0;
            int componentBroadcastCount = 0;
            component.untargetedHandler = _ => ++unTargetedCount;
            component.targetedHandler = _ => ++targetedCount;
            component.broadcastHandler = _ => ++broadcastCount;
            component.componentTargetedHandler = _ => ++componentTargetedCount;
            component.componentBroadcastHandler = _ => ++componentBroadcastCount;

            MessageRegistrationToken token = GetToken(component);
            HashSet<MessageRegistrationHandle> handles = new();
            try
            {
                bool allowed = true;

                bool UntargetedInterceptor(ref SimpleUntargetedMessage message)
                {
                    return allowed;
                }

                bool TargetedInterceptor(ref InstanceId target, ref SimpleTargetedMessage message)
                {
                    return allowed;
                }

                bool BroadcastInterceptor(ref InstanceId source, ref SimpleBroadcastMessage message)
                {
                    return allowed;
                }

                // Double register to ensure no bugs
                MessageRegistrationHandle untargetedInterceptor1 = token.RegisterUntargetedInterceptor<SimpleUntargetedMessage>(UntargetedInterceptor);
                handles.Add(untargetedInterceptor1);
                MessageRegistrationHandle untargetedInterceptor2 = token.RegisterUntargetedInterceptor<SimpleUntargetedMessage>(UntargetedInterceptor);
                handles.Add(untargetedInterceptor2);
                MessageRegistrationHandle targetedInterceptor1 = token.RegisterTargetedInterceptor<SimpleTargetedMessage>(TargetedInterceptor);
                handles.Add(targetedInterceptor1);
                MessageRegistrationHandle targetedInterceptor2 = token.RegisterTargetedInterceptor<SimpleTargetedMessage>(TargetedInterceptor);
                handles.Add(targetedInterceptor2);
                MessageRegistrationHandle broadcastInterceptor1 = token.RegisterBroadcastInterceptor<SimpleBroadcastMessage>(BroadcastInterceptor);
                handles.Add(broadcastInterceptor1);
                MessageRegistrationHandle broadcastInterceptor2 = token.RegisterBroadcastInterceptor<SimpleBroadcastMessage>(BroadcastInterceptor);
                handles.Add(broadcastInterceptor2);

                void RunTest(Action emitMessage, Func<int> count)
                {
                    allowed = true;
                    emitMessage();
                    Assert.AreEqual(1, count());
                    allowed = false;
                    emitMessage();
                    Assert.AreEqual(1, count());
                    emitMessage();
                    Assert.AreEqual(1, count());
                    allowed = true;
                    emitMessage();
                    Assert.AreEqual(2, count());
                    emitMessage();
                    Assert.AreEqual(3, count());
                    allowed = false;
                    emitMessage();
                    Assert.AreEqual(3, count());
                }

                SimpleUntargetedMessage untargetedMessage = new();
                RunTest(() => untargetedMessage.EmitUntargeted(), () => unTargetedCount);
                SimpleTargetedMessage targetedMessage = new();
                RunTest(() => targetedMessage.EmitGameObjectTargeted(test), () => targetedCount);
                RunTest(() => targetedMessage.EmitComponentTargeted(component), () => componentTargetedCount);
                SimpleBroadcastMessage broadcastMessage = new();
                RunTest(() => broadcastMessage.EmitGameObjectBroadcast(test), () => broadcastCount);
                RunTest(() => broadcastMessage.EmitComponentBroadcast(component), () => componentBroadcastCount);
            }
            finally
            {
                foreach (MessageRegistrationHandle handle in handles)
                {
                    token.RemoveRegistration(handle);
                }
            }
        }
        finally
        {
            Cleanup();
        }
    }

    [UnityTest]
    public IEnumerator PostProcessors()
    {
        try
        {
            IEnumerator waitUntilMessageHandlerIsFresh = WaitUntilMessageHandlerIsFresh();
            while (waitUntilMessageHandlerIsFresh.MoveNext())
            {
                yield return waitUntilMessageHandlerIsFresh.Current;
            }

            GameObject test = new(nameof(PostProcessors), typeof(SimpleMessageAwareComponent));
            _spawned.Add(test);

            SimpleMessageAwareComponent component = test.GetComponent<SimpleMessageAwareComponent>();

            int unTargetedCount = 0;
            int targetedCount = 0;
            int broadcastCount = 0;
            int componentTargetedCount = 0;
            int componentBroadcastCount = 0;
            int expectedTargetedWithoutTargetingCount = 0;
            int expectedBroadcastWithoutSourceCount = 0;
            int? lastSeenUntargetedCount = null;
            int? lastSeenTargetedCount = null;
            int? lastSeenComponentTargetedCount = null;
            int? lastSeenBroadcastCount = null;
            int? lastSeenComponentBroadcastCount = null;
            component.untargetedHandler = _ => ++unTargetedCount;
            component.targetedHandler = _ =>
            {
                ++targetedCount;
                ++expectedTargetedWithoutTargetingCount;
            };
            component.broadcastHandler = _ =>
            {
                ++broadcastCount;
                ++expectedBroadcastWithoutSourceCount;
            };
            component.componentTargetedHandler = _ =>
            {
                ++componentTargetedCount;
                ++expectedTargetedWithoutTargetingCount;
            };
            component.componentBroadcastHandler = _ =>
            {
                ++componentBroadcastCount;
                ++expectedBroadcastWithoutSourceCount;
            };

            MessageRegistrationToken token = GetToken(component);
            HashSet<MessageRegistrationHandle> handles = new();
            try
            {
                void UntargetedPostProcessor(ref SimpleUntargetedMessage message)
                {
                    if (lastSeenUntargetedCount == null)
                    {
                        lastSeenUntargetedCount = unTargetedCount;
                        return;
                    }

                    Assert.AreEqual(lastSeenUntargetedCount + 1, unTargetedCount);
                    lastSeenUntargetedCount = unTargetedCount;
                }

                void GameObjectTargetedPostProcessor(ref SimpleTargetedMessage message)
                {
                    if (lastSeenTargetedCount == null)
                    {
                        return;
                    }

                    Assert.AreEqual(lastSeenTargetedCount + 1, targetedCount);
                }

                void ComponentTargetedPostProcessor(ref SimpleTargetedMessage message)
                {
                    if (lastSeenComponentTargetedCount == null)
                    {
                        return;
                    }

                    Assert.AreEqual(lastSeenComponentTargetedCount + 1, componentTargetedCount);
                }

                void TargetedWithoutTargetingPostProcessor(ref InstanceId target, ref SimpleTargetedMessage message)
                {
                    switch (target.Object)
                    {
                        case GameObject _:
                        {
                            if (lastSeenTargetedCount != null)
                            {
                                Assert.AreEqual(lastSeenTargetedCount + 1, targetedCount);
                            }
                            lastSeenTargetedCount = targetedCount;
                            break;
                        }
                        case Component _:
                        {
                            if (lastSeenComponentTargetedCount != null)
                            {
                                Assert.AreEqual(lastSeenComponentTargetedCount + 1, componentTargetedCount);
                            }

                            lastSeenComponentTargetedCount = componentTargetedCount;
                            break;
                        }
                        default:
                            Assert.Fail("Unexpected Object type - {0}.", target.Object?.GetType());
                            break;
                    }
                    Assert.AreEqual(expectedTargetedWithoutTargetingCount, targetedCount + componentTargetedCount);
                }

                void GameObjectBroadcastPostProcessor(ref SimpleBroadcastMessage message)
                {
                    if (lastSeenBroadcastCount == null)
                    {
                        return;
                    }

                    Assert.AreEqual(lastSeenBroadcastCount + 1, broadcastCount);
                }

                void ComponentBroadcastPostProcessor(ref SimpleBroadcastMessage message)
                {
                    if (lastSeenComponentBroadcastCount == null)
                    {
                        return;
                    }
                    Assert.AreEqual(lastSeenComponentBroadcastCount + 1, componentBroadcastCount);
                }

                void BroadcastWithoutSourcePostProcessor(ref InstanceId source, ref SimpleBroadcastMessage message)
                {
                    switch (source.Object)
                    {
                        case GameObject _:
                        {
                            if (lastSeenBroadcastCount != null)
                            {
                                Assert.AreEqual(lastSeenBroadcastCount + 1, broadcastCount);
                            }
                            lastSeenBroadcastCount = broadcastCount;
                            break;
                        }
                        case Component _:
                        {
                            if (lastSeenComponentBroadcastCount != null)
                            {
                                Assert.AreEqual(lastSeenComponentBroadcastCount + 1, componentBroadcastCount);
                            }

                            lastSeenComponentBroadcastCount = componentBroadcastCount;
                            break;
                        }
                        default:
                            Assert.Fail("Unexpected Object type - {0}.", source.Object?.GetType());
                            break;
                    }
                    Assert.AreEqual(expectedBroadcastWithoutSourceCount, broadcastCount + componentBroadcastCount);
                }

                MessageRegistrationHandle untargetedPostProcessor =
                    token.RegisterUntargetedPostProcessor<SimpleUntargetedMessage>(UntargetedPostProcessor);
                handles.Add(untargetedPostProcessor);
                MessageRegistrationHandle gameObjectTargetedPostProcessor =
                    token.RegisterGameObjectTargetedPostProcessor<SimpleTargetedMessage>(
                        test, GameObjectTargetedPostProcessor);
                handles.Add(gameObjectTargetedPostProcessor);
                MessageRegistrationHandle componentTargetedPostProcessor =
                    token.RegisterComponentTargetedPostProcessor<SimpleTargetedMessage>(
                        component, ComponentTargetedPostProcessor);
                handles.Add(componentTargetedPostProcessor);
                MessageRegistrationHandle targetedWithoutTargetingPostProcessor =
                    token.RegisterTargetedWithoutTargetingPostProcessor<SimpleTargetedMessage>(
                        TargetedWithoutTargetingPostProcessor);
                handles.Add(targetedWithoutTargetingPostProcessor);
                MessageRegistrationHandle gameObjectBroadcastPostProcessor =
                    token.RegisterGameObjectBroadcastPostProcessor<SimpleBroadcastMessage>(
                        test, GameObjectBroadcastPostProcessor);
                handles.Add(gameObjectBroadcastPostProcessor);
                MessageRegistrationHandle componentBroadcastPostProcessor =
                    token.RegisterComponentBroadcastPostProcessor<SimpleBroadcastMessage>(
                        component, ComponentBroadcastPostProcessor);
                handles.Add(componentBroadcastPostProcessor);
                MessageRegistrationHandle broadcastWithoutSourcePostProcessor =
                    token.RegisterBroadcastWithoutSourcePostProcessor<SimpleBroadcastMessage>(
                        BroadcastWithoutSourcePostProcessor);
                handles.Add(broadcastWithoutSourcePostProcessor);

                void RunTest(Action emitMessage, Func<int> count)
                {
                    Assert.AreEqual(0, count());
                    component.enabled = true;
                    emitMessage();
                    Assert.AreEqual(1, count());
                    component.enabled = false;
                    emitMessage();
                    Assert.AreEqual(1, count());
                    emitMessage();
                    Assert.AreEqual(1, count());
                    component.enabled = true;
                    emitMessage();
                    Assert.AreEqual(2, count());
                    emitMessage();
                    Assert.AreEqual(3, count());
                    component.enabled = false;
                    emitMessage();
                    Assert.AreEqual(3, count());
                }

                SimpleUntargetedMessage untargetedMessage = new();
                RunTest(() => untargetedMessage.EmitUntargeted(), () => unTargetedCount);
                SimpleTargetedMessage targetedMessage = new();
                RunTest(() => targetedMessage.EmitGameObjectTargeted(test), () => targetedCount);
                RunTest(() => targetedMessage.EmitComponentTargeted(component), () => componentTargetedCount);
                SimpleBroadcastMessage broadcastMessage = new();
                RunTest(() => broadcastMessage.EmitGameObjectBroadcast(test), () => broadcastCount);
                RunTest(() => broadcastMessage.EmitComponentBroadcast(component), () => componentBroadcastCount);
            }
            finally
            {
                foreach (MessageRegistrationHandle handle in handles)
                {
                    token.RemoveRegistration(handle);
                }
            }
        }
        finally
        {
            Cleanup();
        }
    }

    [UnityTest]
    public IEnumerator InstanceId()
    {
        try
        {
            IEnumerator waitUntilMessageHandlerIsFresh = WaitUntilMessageHandlerIsFresh();
            while (waitUntilMessageHandlerIsFresh.MoveNext())
            {
                yield return waitUntilMessageHandlerIsFresh.Current;
            }

            GameObject test = new(nameof(InstanceId), typeof(SimpleMessageAwareComponent));
            _spawned.Add(test);

            // Message-aware instance ids
            SimpleMessageAwareComponent component = test.GetComponent<SimpleMessageAwareComponent>();
            Assert.AreEqual((InstanceId)test, (InstanceId)test);
            Assert.AreEqual((InstanceId)component, (InstanceId)component);
            Assert.AreNotEqual((InstanceId)test, (InstanceId)component);
            Assert.AreNotEqual((InstanceId)component, (InstanceId)test);

            // Non-message-aware instance ids
            GameObject test2 = new(nameof(InstanceId) + " - 2");
            _spawned.Add(test2);
            Assert.AreNotEqual((InstanceId)test2, (InstanceId)test2.transform);
            Assert.AreNotEqual((InstanceId)test2.transform, (InstanceId)test2);

            // Null checks
            bool caught = false;
            try
            {
                _ = (InstanceId)(GameObject)null;
            }
            catch
            {
                caught = true;
            }

            Assert.IsTrue(caught);

            caught = false;
            try
            {
                _ = (InstanceId)(Component)null;
            }
            catch
            {
                caught = true;
            }

            Assert.IsTrue(caught);
        }
        finally
        {
            Cleanup();
        }
    }

    [UnityTest]
    public IEnumerator GlobalAcceptAll()
    {
        try
        {
            IEnumerator waitUntilMessageHandlerIsFresh = WaitUntilMessageHandlerIsFresh();
            while (waitUntilMessageHandlerIsFresh.MoveNext())
            {
                yield return waitUntilMessageHandlerIsFresh.Current;
            }

            GameObject test = new(nameof(GlobalAcceptAll), typeof(SimpleMessageAwareComponent));
            _spawned.Add(test);
            SimpleMessageAwareComponent component = test.GetComponent<SimpleMessageAwareComponent>();

            MessageRegistrationToken token = GetToken(component);

            int untargetedCount = 0;
            int targetedCount = 0;
            int broadcastCount = 0;

            void HandleUntargeted(IUntargetedMessage message)
            {
                ++untargetedCount;
            }

            void HandleFastUntargeted(ref IUntargetedMessage message)
            {
                ++untargetedCount;
            }

            void HandleTargeted(InstanceId target, ITargetedMessage message)
            {
                ++targetedCount;
            }

            void HandleFastTargeted(ref InstanceId target, ref ITargetedMessage message)
            {
                ++targetedCount;
            }

            void HandleBroadcast(InstanceId source, IBroadcastMessage message)
            {
                ++broadcastCount;
            }

            void HandleFastBroadcast(ref InstanceId source, ref IBroadcastMessage message)
            {
                ++broadcastCount;
            }

            HashSet<MessageRegistrationHandle> handles = new();
            try
            {
                MessageRegistrationHandle firstHandle = token.RegisterGlobalAcceptAll(HandleUntargeted, HandleTargeted, HandleBroadcast);
                _ = handles.Add(firstHandle);

                // Untargeted
                SimpleUntargetedMessage untargetedMessage = new();
                untargetedMessage.EmitUntargeted();
                Assert.AreEqual(1, untargetedCount);
                Assert.AreEqual(0, targetedCount);
                Assert.AreEqual(0, broadcastCount);
                untargetedMessage.EmitUntargeted();
                Assert.AreEqual(2, untargetedCount);
                Assert.AreEqual(0, targetedCount);
                Assert.AreEqual(0, broadcastCount);

                // Targeted
                SimpleTargetedMessage targetedMessage = new();
                targetedMessage.EmitGameObjectTargeted(test);
                Assert.AreEqual(2, untargetedCount);
                Assert.AreEqual(1, targetedCount);
                Assert.AreEqual(0, broadcastCount);
                targetedMessage.EmitComponentTargeted(component);
                Assert.AreEqual(2, untargetedCount);
                Assert.AreEqual(2, targetedCount);
                Assert.AreEqual(0, broadcastCount);

                // Broadcast
                SimpleBroadcastMessage broadcastMessage = new();
                broadcastMessage.EmitGameObjectBroadcast(test);
                Assert.AreEqual(2, untargetedCount);
                Assert.AreEqual(2, targetedCount);
                Assert.AreEqual(1, broadcastCount);
                broadcastMessage.EmitComponentBroadcast(component);
                Assert.AreEqual(2, untargetedCount);
                Assert.AreEqual(2, targetedCount);
                Assert.AreEqual(2, broadcastCount);

                component.enabled = false;
                untargetedMessage.EmitUntargeted();
                targetedMessage.EmitGameObjectTargeted(test);
                targetedMessage.EmitComponentTargeted(component);
                broadcastMessage.EmitGameObjectBroadcast(test);
                broadcastMessage.EmitComponentBroadcast(component);
                Assert.AreEqual(2, untargetedCount);
                Assert.AreEqual(2, targetedCount);
                Assert.AreEqual(2, broadcastCount);

                component.enabled = true;
                MessageRegistrationHandle secondHandle = token.RegisterGlobalAcceptAll(HandleUntargeted, HandleTargeted, HandleBroadcast);
                _ = handles.Add(secondHandle);
                untargetedMessage.EmitUntargeted();
                targetedMessage.EmitGameObjectTargeted(test);
                targetedMessage.EmitComponentTargeted(component);
                broadcastMessage.EmitGameObjectBroadcast(test);
                broadcastMessage.EmitComponentBroadcast(component);
                Assert.AreEqual(3, untargetedCount);
                Assert.AreEqual(4, targetedCount);
                Assert.AreEqual(4, broadcastCount);

                MessageRegistrationHandle thirdHandle = token.RegisterGlobalAcceptAll(HandleFastUntargeted, HandleFastTargeted, HandleFastBroadcast);
                _ = handles.Add(thirdHandle);
                untargetedMessage.EmitUntargeted();
                targetedMessage.EmitGameObjectTargeted(test);
                targetedMessage.EmitComponentTargeted(component);
                broadcastMessage.EmitGameObjectBroadcast(test);
                broadcastMessage.EmitComponentBroadcast(component);
                Assert.AreEqual(5, untargetedCount);
                Assert.AreEqual(8, targetedCount);
                Assert.AreEqual(8, broadcastCount);

                MessageRegistrationHandle fourthHandle = token.RegisterGlobalAcceptAll(HandleFastUntargeted, HandleFastTargeted, HandleFastBroadcast);
                _ = handles.Add(fourthHandle);
                untargetedMessage.EmitUntargeted();
                targetedMessage.EmitGameObjectTargeted(test);
                targetedMessage.EmitComponentTargeted(component);
                broadcastMessage.EmitGameObjectBroadcast(test);
                broadcastMessage.EmitComponentBroadcast(component);
                Assert.AreEqual(7, untargetedCount);
                Assert.AreEqual(12, targetedCount);
                Assert.AreEqual(12, broadcastCount);
            }
            finally
            {
                foreach (MessageRegistrationHandle handle in handles)
                {
                    token.RemoveRegistration(handle);
                }
            }
        }
        finally
        {
            Cleanup();
        }
    }

    private MessageRegistrationToken GetToken(MessageAwareComponent component)
    {
        // Reach inside and grab the token
        FieldInfo field = typeof(MessageAwareComponent).GetField(
            "_messageRegistrationToken", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.IsNotNull(field);
        MessageRegistrationToken token = field.GetValue(component) as MessageRegistrationToken;
        Assert.IsNotNull(token);
        return token;
    }

    private IEnumerator WaitUntilMessageHandlerIsFresh()
    {
        Setup();
        MessageBus messageBus = MessageHandler.MessageBus as MessageBus;
        Assert.IsNotNull(messageBus);

        Stopwatch timer = Stopwatch.StartNew();

        bool IsStale()
        {
            return messageBus.RegisteredUntargeted != 0 || messageBus.RegisteredTargeted != 0 ||
                   messageBus.RegisteredBroadcast != 0;
        }

        while (IsStale() && timer.Elapsed < TimeSpan.FromSeconds(2.5))
        {
            yield return null;
        }

        Assert.IsFalse(
            IsStale(),
            "MessageHandler had {0} Untargeted registrations, {1} Targeted registrations, {2} Broadcast registrations. Registration log: {3}.",
            messageBus.RegisteredUntargeted, messageBus.RegisteredTargeted, messageBus.RegisteredBroadcast,
            messageBus.Log);
    }
}
