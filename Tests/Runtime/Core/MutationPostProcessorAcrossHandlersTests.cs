namespace DxMessaging.Tests.Runtime.Core
{
    using System.Collections;
    using System.Collections.Generic;
    using DxMessaging.Core;
    using DxMessaging.Core.Extensions;
    using DxMessaging.Tests.Runtime.Scripts.Components;
    using DxMessaging.Tests.Runtime.Scripts.Messages;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;

    public sealed class MutationPostProcessorAcrossHandlersTests : MessagingTestBase
    {
        [UnityTest]
        public IEnumerator TargetedWithoutTargetingRemoveOtherAcrossHandlersDuringPostProcessing()
        {
            List<(EmptyMessageAwareComponent comp, MessageRegistrationToken token)> listeners =
                new System.Collections.Generic.List<(
                    EmptyMessageAwareComponent comp,
                    MessageRegistrationToken token
                )>();
            for (int i = 0; i < 2; i++)
            {
                GameObject go = new($"TWT_PP_Rem_{i}", typeof(EmptyMessageAwareComponent));
                _spawned.Add(go);
                EmptyMessageAwareComponent c = go.GetComponent<EmptyMessageAwareComponent>();
                listeners.Add((c, GetToken(c)));
            }

            MessageRegistrationHandle[] pp = new MessageRegistrationHandle[2];
            int[] counts = new int[2];

            // Ensure post-processing runs
            _ = listeners[0]
                .token.RegisterTargetedWithoutTargeting<SimpleTargetedMessage>((_, _) => { });

            pp[0] = listeners[0]
                .token.RegisterTargetedWithoutTargetingPostProcessor(
                    (ref InstanceId _, ref SimpleTargetedMessage __) =>
                    {
                        counts[0]++;
                        listeners[1].token.RemoveRegistration(pp[1]);
                    }
                );
            pp[1] = listeners[1]
                .token.RegisterTargetedWithoutTargetingPostProcessor(
                    (ref InstanceId _, ref SimpleTargetedMessage __) => counts[1]++
                );

            GameObject target = new("TWT_PP_Target");
            _spawned.Add(target);

            SimpleTargetedMessage msg = new();
            msg.EmitGameObjectTargeted(target);
            Assert.AreEqual(1, counts[0]);
            Assert.AreEqual(1, counts[1]);

            msg.EmitGameObjectTargeted(target);
            Assert.AreEqual(2, counts[0]);
            Assert.AreEqual(1, counts[1]);
            yield break;
        }

        [UnityTest]
        public IEnumerator BroadcastWithoutSourceRemoveOtherAcrossHandlersDuringPostProcessing()
        {
            List<(EmptyMessageAwareComponent comp, MessageRegistrationToken token)> listeners =
                new System.Collections.Generic.List<(
                    EmptyMessageAwareComponent comp,
                    MessageRegistrationToken token
                )>();
            for (int i = 0; i < 2; i++)
            {
                GameObject go = new($"BWO_PP_Rem_{i}", typeof(EmptyMessageAwareComponent));
                _spawned.Add(go);
                EmptyMessageAwareComponent c = go.GetComponent<EmptyMessageAwareComponent>();
                listeners.Add((c, GetToken(c)));
            }

            MessageRegistrationHandle[] pp = new MessageRegistrationHandle[2];
            int[] counts = new int[2];

            // Ensure post-processing runs
            _ = listeners[0]
                .token.RegisterBroadcastWithoutSource<SimpleBroadcastMessage>((_, _) => { });

            pp[0] = listeners[0]
                .token.RegisterBroadcastWithoutSourcePostProcessor(
                    (ref InstanceId _, ref SimpleBroadcastMessage __) =>
                    {
                        counts[0]++;
                        listeners[1].token.RemoveRegistration(pp[1]);
                    }
                );
            pp[1] = listeners[1]
                .token.RegisterBroadcastWithoutSourcePostProcessor(
                    (ref InstanceId _, ref SimpleBroadcastMessage __) => counts[1]++
                );

            SimpleBroadcastMessage msg = new();
            msg.EmitComponentBroadcast(listeners[0].comp);
            Assert.AreEqual(1, counts[0]);
            Assert.AreEqual(1, counts[1]);

            msg.EmitComponentBroadcast(listeners[0].comp);
            Assert.AreEqual(2, counts[0]);
            Assert.AreEqual(1, counts[1]);
            yield break;
        }
    }
}
