#if UNITY_2021_3_OR_NEWER
namespace DxMessaging.Tests.Runtime.Core
{
    using System.Collections;
    using DxMessaging.Core;
    using DxMessaging.Core.Extensions;
    using DxMessaging.Tests.Runtime.Scripts.Components;
    using DxMessaging.Tests.Runtime.Scripts.Messages;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;

    public sealed class MutationPostProcessorMoreTests : MessagingTestBase
    {
        [UnityTest]
        public IEnumerator TargetedWithoutTargetingAddPostProcessorDuringHandler()
        {
            GameObject host = new(
                nameof(TargetedWithoutTargetingAddPostProcessorDuringHandler),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(host);
            MessageRegistrationToken token = GetToken(
                host.GetComponent<EmptyMessageAwareComponent>()
            );

            int[] ppCounts = new int[2];
            MessageRegistrationHandle ppNew = default;
            bool added = false;

            // Ensure we have handlers to trigger post-processing
            _ = token.RegisterTargetedWithoutTargeting<SimpleTargetedMessage>(
                (_, _) =>
                {
                    if (!added)
                    {
                        added = true;
                        ppNew = token.RegisterTargetedWithoutTargetingPostProcessor(
                            (ref InstanceId _, ref SimpleTargetedMessage __) => ppCounts[1]++
                        );
                    }
                }
            );

            _ = token.RegisterTargetedWithoutTargetingPostProcessor(
                (ref InstanceId _, ref SimpleTargetedMessage __) => ppCounts[0]++
            );

            SimpleTargetedMessage msg = new();
            msg.EmitGameObjectTargeted(host);
            Assert.AreEqual(1, ppCounts[0]);
            Assert.AreEqual(0, ppCounts[1]);

            msg.EmitGameObjectTargeted(host);
            Assert.AreEqual(2, ppCounts[0]);
            Assert.AreEqual(1, ppCounts[1]);

            if (ppNew != default)
            {
                token.RemoveRegistration(ppNew);
            }
            yield break;
        }

        [UnityTest]
        public IEnumerator BroadcastWithoutSourceAddPostProcessorDuringHandler()
        {
            GameObject host = new(
                nameof(BroadcastWithoutSourceAddPostProcessorDuringHandler),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(host);
            MessageRegistrationToken token = GetToken(
                host.GetComponent<EmptyMessageAwareComponent>()
            );

            int[] ppCounts = new int[2];
            MessageRegistrationHandle ppNew = default;
            bool added = false;

            // Ensure we have handlers to trigger post-processing
            _ = token.RegisterBroadcastWithoutSource<SimpleBroadcastMessage>(
                (_, _) =>
                {
                    if (!added)
                    {
                        added = true;
                        ppNew = token.RegisterBroadcastWithoutSourcePostProcessor(
                            (ref InstanceId _, ref SimpleBroadcastMessage __) => ppCounts[1]++
                        );
                    }
                }
            );

            _ = token.RegisterBroadcastWithoutSourcePostProcessor(
                (ref InstanceId _, ref SimpleBroadcastMessage __) => ppCounts[0]++
            );

            SimpleBroadcastMessage msg = new();
            msg.EmitComponentBroadcast(host.GetComponent<EmptyMessageAwareComponent>());
            Assert.AreEqual(1, ppCounts[0]);
            Assert.AreEqual(0, ppCounts[1]);

            msg.EmitComponentBroadcast(host.GetComponent<EmptyMessageAwareComponent>());
            Assert.AreEqual(2, ppCounts[0]);
            Assert.AreEqual(1, ppCounts[1]);

            if (ppNew != default)
            {
                token.RemoveRegistration(ppNew);
            }
            yield break;
        }
    }
}

#endif
