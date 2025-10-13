namespace DxMessaging.Tests.Runtime.Core
{
    using System.Collections;
    using DxMessaging.Core;
    using DxMessaging.Core.Extensions;
    using DxMessaging.Core.Messages;
    using DxMessaging.Tests.Runtime.Scripts.Components;
    using DxMessaging.Tests.Runtime.Scripts.Messages;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;

    public sealed class MutationGlobalAddTests : MessagingTestBase
    {
        [UnityTest]
        public IEnumerator GlobalAcceptAllAddDuringTargetedEmission()
        {
            GameObject host = new(
                nameof(GlobalAcceptAllAddDuringTargetedEmission),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(host);
            EmptyMessageAwareComponent comp = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);

            int[] counts = new int[2];
            MessageRegistrationHandle adder =
                token.RegisterGameObjectTargeted<SimpleTargetedMessage>(
                    host,
                    _ =>
                    {
                        if (counts[1] == 0)
                        {
                            token.RegisterGlobalAcceptAll(
                                (ref IUntargetedMessage _) => counts[1]++,
                                (ref InstanceId _, ref ITargetedMessage _) => counts[1]++,
                                (ref InstanceId _, ref IBroadcastMessage _) => counts[1]++
                            );
                        }
                        counts[0]++;
                    }
                );

            SimpleTargetedMessage msg = new();
            msg.EmitGameObjectTargeted(host);
            Assert.AreEqual(1, counts[0]);
            Assert.AreEqual(0, counts[1]);

            msg.EmitGameObjectTargeted(host);
            Assert.AreEqual(2, counts[0]);
            Assert.AreEqual(3, counts[1]);

            token.RemoveRegistration(adder);
            yield break;
        }

        [UnityTest]
        public IEnumerator GlobalAcceptAllAddDuringBroadcastEmission()
        {
            GameObject host = new(
                nameof(GlobalAcceptAllAddDuringBroadcastEmission),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(host);
            EmptyMessageAwareComponent comp = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);

            int[] counts = new int[2];
            MessageRegistrationHandle adder =
                token.RegisterGameObjectBroadcast<SimpleBroadcastMessage>(
                    host,
                    _ =>
                    {
                        if (counts[1] == 0)
                        {
                            token.RegisterGlobalAcceptAll(
                                (ref IUntargetedMessage _) => counts[1]++,
                                (ref InstanceId _, ref ITargetedMessage _) => counts[1]++,
                                (ref InstanceId _, ref IBroadcastMessage _) => counts[1]++
                            );
                        }
                        counts[0]++;
                    }
                );

            SimpleBroadcastMessage msg = new();
            msg.EmitGameObjectBroadcast(host);
            Assert.AreEqual(1, counts[0]);
            Assert.AreEqual(0, counts[1]);

            msg.EmitGameObjectBroadcast(host);
            Assert.AreEqual(2, counts[0]);
            Assert.AreEqual(3, counts[1]);

            token.RemoveRegistration(adder);
            yield break;
        }
    }
}
