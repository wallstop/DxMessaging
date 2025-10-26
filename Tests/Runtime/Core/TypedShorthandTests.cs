namespace DxMessaging.Tests.Runtime.Core
{
    using System;
    using System.Collections;
    using DxMessaging.Core;
    using DxMessaging.Core.Extensions;
    using DxMessaging.Core.MessageBus;
    using DxMessaging.Tests.Runtime.Scripts.Components;
    using DxMessaging.Tests.Runtime.Scripts.Messages;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;

    public sealed class TypedShorthandTests : MessagingTestBase
    {
        [UnityTest]
        public IEnumerator EmitAtTargetsGameObjectAndComponent()
        {
            GameObject go = new(
                nameof(EmitAtTargetsGameObjectAndComponent),
                typeof(ShorthandTargetedBroadcastComponent)
            );
            _spawned.Add(go);
            ShorthandTargetedBroadcastComponent comp =
                go.GetComponent<ShorthandTargetedBroadcastComponent>();

            SimpleTargetedMessage msg = new();
            msg.EmitAt((InstanceId)go);
            Assert.AreEqual(1, comp.gameObjectTargetedCount);
            Assert.AreEqual(0, comp.componentTargetedCount);
            Assert.AreEqual(1, comp.targetedWithoutTargetingCount);

            msg.EmitAt((InstanceId)comp);
            Assert.AreEqual(1, comp.gameObjectTargetedCount);
            Assert.AreEqual(1, comp.componentTargetedCount);
            Assert.AreEqual(2, comp.targetedWithoutTargetingCount);
            yield break;
        }

        [UnityTest]
        public IEnumerator EmitFromSourcesGameObjectAndComponent()
        {
            GameObject go = new(
                nameof(EmitFromSourcesGameObjectAndComponent),
                typeof(ShorthandTargetedBroadcastComponent)
            );
            _spawned.Add(go);
            ShorthandTargetedBroadcastComponent comp =
                go.GetComponent<ShorthandTargetedBroadcastComponent>();

            SimpleBroadcastMessage msg = new();
            msg.EmitFrom((InstanceId)go);
            Assert.AreEqual(1, comp.gameObjectBroadcastCount);
            Assert.AreEqual(0, comp.componentBroadcastCount);
            Assert.AreEqual(1, comp.broadcastWithoutSourceCount);

            msg.EmitFrom((InstanceId)comp);
            Assert.AreEqual(1, comp.gameObjectBroadcastCount);
            Assert.AreEqual(1, comp.componentBroadcastCount);
            Assert.AreEqual(2, comp.broadcastWithoutSourceCount);
            yield break;
        }

        [UnityTest]
        public IEnumerator EmitAtMismatchDoesNotHitOtherShape()
        {
            GameObject go = new(
                nameof(EmitAtMismatchDoesNotHitOtherShape),
                typeof(ShorthandTargetedBroadcastComponent)
            );
            _spawned.Add(go);
            ShorthandTargetedBroadcastComponent comp =
                go.GetComponent<ShorthandTargetedBroadcastComponent>();

            SimpleTargetedMessage msg = new();
            msg.EmitAt((InstanceId)comp); // component
            Assert.AreEqual(0, comp.gameObjectTargetedCount);
            Assert.AreEqual(1, comp.componentTargetedCount);
            Assert.AreEqual(1, comp.targetedWithoutTargetingCount);

            msg.EmitAt((InstanceId)go); // gameobject
            Assert.AreEqual(1, comp.gameObjectTargetedCount);
            Assert.AreEqual(1, comp.componentTargetedCount);
            Assert.AreEqual(2, comp.targetedWithoutTargetingCount);
            yield break;
        }

        [UnityTest]
        public IEnumerator EmitFromMismatchDoesNotHitOtherShape()
        {
            GameObject go = new(
                nameof(EmitFromMismatchDoesNotHitOtherShape),
                typeof(ShorthandTargetedBroadcastComponent)
            );
            _spawned.Add(go);
            ShorthandTargetedBroadcastComponent comp =
                go.GetComponent<ShorthandTargetedBroadcastComponent>();

            SimpleBroadcastMessage msg = new();
            msg.EmitFrom((InstanceId)comp); // component
            Assert.AreEqual(0, comp.gameObjectBroadcastCount);
            Assert.AreEqual(1, comp.componentBroadcastCount);
            Assert.AreEqual(1, comp.broadcastWithoutSourceCount);

            msg.EmitFrom((InstanceId)go); // gameobject
            Assert.AreEqual(1, comp.gameObjectBroadcastCount);
            Assert.AreEqual(1, comp.componentBroadcastCount);
            Assert.AreEqual(2, comp.broadcastWithoutSourceCount);
            yield break;
        }

        [UnityTest]
        public IEnumerator EmitAtEquivalentToExplicitHelpers()
        {
            GameObject go = new(
                nameof(EmitAtEquivalentToExplicitHelpers),
                typeof(ShorthandTargetedBroadcastComponent)
            );
            _spawned.Add(go);
            ShorthandTargetedBroadcastComponent comp =
                go.GetComponent<ShorthandTargetedBroadcastComponent>();

            SimpleTargetedMessage msg = new();
            msg.EmitAt((InstanceId)go);
            Assert.AreEqual(1, comp.gameObjectTargetedCount);
            msg.EmitGameObjectTargeted(go);
            Assert.AreEqual(2, comp.gameObjectTargetedCount);

            msg.EmitAt((InstanceId)comp);
            Assert.AreEqual(1, comp.componentTargetedCount);
            msg.EmitComponentTargeted(comp);
            Assert.AreEqual(2, comp.componentTargetedCount);
            yield break;
        }

        [UnityTest]
        public IEnumerator EmitFromEquivalentToExplicitHelpers()
        {
            GameObject go = new(
                nameof(EmitFromEquivalentToExplicitHelpers),
                typeof(ShorthandTargetedBroadcastComponent)
            );
            _spawned.Add(go);
            ShorthandTargetedBroadcastComponent comp =
                go.GetComponent<ShorthandTargetedBroadcastComponent>();

            SimpleBroadcastMessage msg = new();
            msg.EmitFrom((InstanceId)go);
            Assert.AreEqual(1, comp.gameObjectBroadcastCount);
            msg.EmitGameObjectBroadcast(go);
            Assert.AreEqual(2, comp.gameObjectBroadcastCount);

            msg.EmitFrom((InstanceId)comp);
            Assert.AreEqual(1, comp.componentBroadcastCount);
            msg.EmitComponentBroadcast(comp);
            Assert.AreEqual(2, comp.componentBroadcastCount);
            yield break;
        }

        [UnityTest]
        public IEnumerator EmitAtRespectsInterceptors()
        {
            GameObject go = new(
                nameof(EmitAtRespectsInterceptors),
                typeof(ShorthandTargetedBroadcastComponent)
            );
            _spawned.Add(go);
            ShorthandTargetedBroadcastComponent comp =
                go.GetComponent<ShorthandTargetedBroadcastComponent>();

            IMessageBus bus = MessageHandler.MessageBus;
            bool cancel = true;
            Action dereg = bus.RegisterTargetedInterceptor(
                (ref InstanceId t, ref SimpleTargetedMessage m) => !cancel
            );
            try
            {
                SimpleTargetedMessage msg = new();
                msg.EmitAt((InstanceId)go); // cancelled
                Assert.AreEqual(0, comp.gameObjectTargetedCount);
                Assert.AreEqual(0, comp.targetedWithoutTargetingCount); // not observed either because cancelled

                cancel = false;
                msg.EmitAt((InstanceId)go); // allowed
                Assert.AreEqual(1, comp.gameObjectTargetedCount);
                Assert.AreEqual(1, comp.targetedWithoutTargetingCount);
            }
            finally
            {
                dereg();
            }
            yield break;
        }

        [UnityTest]
        public IEnumerator EmitFromRespectsInterceptors()
        {
            GameObject go = new(
                nameof(EmitFromRespectsInterceptors),
                typeof(ShorthandTargetedBroadcastComponent)
            );
            _spawned.Add(go);
            ShorthandTargetedBroadcastComponent comp =
                go.GetComponent<ShorthandTargetedBroadcastComponent>();

            IMessageBus bus = MessageHandler.MessageBus;
            bool cancel = true;
            Action dereg = bus.RegisterBroadcastInterceptor(
                (ref InstanceId s, ref SimpleBroadcastMessage m) => !cancel
            );
            try
            {
                SimpleBroadcastMessage msg = new();
                msg.EmitFrom((InstanceId)go); // cancelled
                Assert.AreEqual(0, comp.gameObjectBroadcastCount);
                Assert.AreEqual(0, comp.broadcastWithoutSourceCount); // not observed either because cancelled

                cancel = false;
                msg.EmitFrom((InstanceId)go); // allowed
                Assert.AreEqual(1, comp.gameObjectBroadcastCount);
                Assert.AreEqual(1, comp.broadcastWithoutSourceCount);
            }
            finally
            {
                dereg();
            }
            yield break;
        }
    }
}
