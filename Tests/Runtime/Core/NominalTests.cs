using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using DxMessaging.Core;
using DxMessaging.Core.Extensions;
using DxMessaging.Core.MessageBus;
using DxMessaging.Unity;
using NUnit.Framework;
using NUnit.Framework.Constraints;
using Tests.Runtime.Scripts.Components;
using Tests.Runtime.Scripts.Messages;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class NominalTests
{
    // A UnityTest behaves like a coroutine in Play Mode. In Edit Mode you can use
    // `yield return null;` to skip a frame.
    [UnityTest]
    public IEnumerator Nominal()
    {
        IEnumerator waitUntilMessageHandlerIsFresh = WaitUntilMessageHandlerIsFresh();
        while (waitUntilMessageHandlerIsFresh.MoveNext())
        {
            yield return waitUntilMessageHandlerIsFresh.Current;
        }

        GameObject test = new(nameof(Nominal), typeof(SimpleMessageAwareComponent));
        GameObject nonTest = new("Non-Test", typeof(SimpleMessageAwareComponent));
        try
        {
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
            Object.Destroy(test);
            Object.Destroy(nonTest);
        }
    }

    [UnityTest]
    public IEnumerator Lifetime()
    {
        IEnumerator waitUntilMessageHandlerIsFresh = WaitUntilMessageHandlerIsFresh();
        while (waitUntilMessageHandlerIsFresh.MoveNext())
        {
            yield return waitUntilMessageHandlerIsFresh.Current;
        }

        MessageBus messageBus = MessageHandler.MessageBus as MessageBus;
        Assert.IsNotNull(messageBus);

        GameObject test = new(nameof(Nominal), typeof(SimpleMessageAwareComponent));
        try
        {
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
            Object.Destroy(test);
        }
    }

    [UnityTest]
    public IEnumerator NonMessagingObjects()
    {
        IEnumerator waitUntilMessageHandlerIsFresh = WaitUntilMessageHandlerIsFresh();
        while (waitUntilMessageHandlerIsFresh.MoveNext())
        {
            yield return waitUntilMessageHandlerIsFresh.Current;
        }

        MessageBus messageBus = MessageHandler.MessageBus as MessageBus;
        Assert.IsNotNull(messageBus);

        GameObject test1 = new("NonMessaging1");
        try
        {

            Assert.AreEqual(0, messageBus.RegisteredUntargeted);
            Assert.AreEqual(0, messageBus.RegisteredTargeted);
            Assert.AreEqual(0, messageBus.RegisteredBroadcast);

            GameObject test2 = new("NonMessaging1", typeof(SpriteRenderer), typeof(MessageHandler));
            try
            {
                Assert.AreEqual(0, messageBus.RegisteredUntargeted);
                Assert.AreEqual(0, messageBus.RegisteredTargeted);
                Assert.AreEqual(0, messageBus.RegisteredBroadcast);
            }
            finally
            {
                Object.Destroy(test2);
            }
        }
        finally
        {
            Object.Destroy(test1);
        }
    }

    [UnityTest]
    public IEnumerator DedupedRegistration()
    {
        IEnumerator waitUntilMessageHandlerIsFresh = WaitUntilMessageHandlerIsFresh();
        while (waitUntilMessageHandlerIsFresh.MoveNext())
        {
            yield return waitUntilMessageHandlerIsFresh.Current;
        }

        GameObject test = new(nameof(DedupedRegistration), typeof(SimpleMessageAwareComponent));
        try
        {
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
                MessageRegistrationHandle untargetedHandle = token.RegisterUntargeted<SimpleUntargetedMessage>(component.HandleSimpleUntargetedMessage);
                IEnumerator untargetedLifecycle = TestLifecycle(() => untargetedMessage.EmitUntargeted(), untargetedHandle, () => unTargetedCount);
                while (untargetedLifecycle.MoveNext())
                {
                    yield return untargetedLifecycle.Current;
                }

                // Targeted
                MessageRegistrationHandle targetedHandle = token.RegisterGameObjectTargeted<SimpleTargetedMessage>(test, component.HandleSimpleTargetedMessage);
                SimpleTargetedMessage targetedMessage = new();
                IEnumerator targetedLifecycle = TestLifecycle(() => targetedMessage.EmitGameObjectTargeted(test), targetedHandle, () => targetedCount);
                while (targetedLifecycle.MoveNext())
                {
                    yield return targetedLifecycle.Current;
                }

                // Broadcast
                MessageRegistrationHandle broadcastHandle = token.RegisterGameObjectBroadcast<SimpleBroadcastMessage>(test, component.HandleSimpleBroadcastMessage);
                SimpleBroadcastMessage broadcastMessage = new();
                IEnumerator broadcastLifecycle = TestLifecycle(() => broadcastMessage.EmitGameObjectBroadcast(test), broadcastHandle, () => broadcastCount);
                while (broadcastLifecycle.MoveNext())
                {
                    yield return broadcastLifecycle.Current;
                }

                // Component Targeted
                MessageRegistrationHandle componentTargetedHandle = token.RegisterComponentTargeted<SimpleTargetedMessage>(component, component.HandleSimpleComponentTargetedMessage);
                IEnumerator componentTargetedLifecycle = TestLifecycle(() => targetedMessage.EmitComponentTargeted(component), componentTargetedHandle, () => componentTargetedCount);
                while (componentTargetedLifecycle.MoveNext())
                {
                    yield return componentTargetedLifecycle.Current;
                }

                // Component Broadcast
                MessageRegistrationHandle componentBroadcastHandle = token.RegisterGameObjectBroadcast<SimpleBroadcastMessage>(test, component.HandleSimpleBroadcastMessage);
                IEnumerator componentBroadcastLifecycle = TestLifecycle(() => broadcastMessage.EmitComponentBroadcast(component), componentBroadcastHandle, () => componentBroadcastCount);
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
            Object.Destroy(test);
        }
    }

    [UnityTest]
    public IEnumerator Interceptors()
    {
        IEnumerator waitUntilMessageHandlerIsFresh = WaitUntilMessageHandlerIsFresh();
        while (waitUntilMessageHandlerIsFresh.MoveNext())
        {
            yield return waitUntilMessageHandlerIsFresh.Current;
        }

        GameObject test = new(nameof(DedupedRegistration), typeof(SimpleMessageAwareComponent));
        try
        {
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

                MessageRegistrationHandle untargetedInterceptor = token.RegisterUntargetedInterceptor<SimpleUntargetedMessage>(UntargetedInterceptor);
                handles.Add(untargetedInterceptor);
                MessageRegistrationHandle targetedInterceptor = token.RegisterTargetedInterceptor<SimpleTargetedMessage>(TargetedInterceptor);
                handles.Add(targetedInterceptor);
                MessageRegistrationHandle broadcastInterceptor = token.RegisterBroadcastInterceptor<SimpleBroadcastMessage>(BroadcastInterceptor);
                handles.Add(broadcastInterceptor);


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
            Object.Destroy(test);
        }

        yield return null;
    }

    [UnityTest]
    public IEnumerator InstanceId()
    {
        IEnumerator waitUntilMessageHandlerIsFresh = WaitUntilMessageHandlerIsFresh();
        while (waitUntilMessageHandlerIsFresh.MoveNext())
        {
            yield return waitUntilMessageHandlerIsFresh.Current;
        }

        GameObject test = new(nameof(InstanceId), typeof(SimpleMessageAwareComponent));
        try
        {
            SimpleMessageAwareComponent component = test.GetComponent<SimpleMessageAwareComponent>();
            Assert.AreEqual((InstanceId)test, (InstanceId)test);
            Assert.AreEqual((InstanceId)component, (InstanceId)component);
            Assert.AreNotEqual((InstanceId)test, (InstanceId)component);
            Assert.AreNotEqual((InstanceId)component, (InstanceId)test);

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
            Object.Destroy(test);
        }

        yield return null;
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
        MessageBus messageBus = MessageHandler.MessageBus as MessageBus;
        Assert.IsNotNull(messageBus);
        messageBus.Log.Enabled = true;
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
