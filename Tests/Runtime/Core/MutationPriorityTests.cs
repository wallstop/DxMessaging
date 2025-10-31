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

    public sealed class MutationPriorityTests : MessagingTestBase
    {
        [UnityTest]
        public IEnumerator TargetedPriorityInsertedNextEmissionOrder()
        {
            GameObject host = new(
                nameof(TargetedPriorityInsertedNextEmissionOrder),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(host);
            MessageRegistrationToken token = GetToken(
                host.GetComponent<EmptyMessageAwareComponent>()
            );

            System.Collections.Generic.List<int> order = new();
            bool added = false;
            MessageRegistrationHandle low = default;

            MessageRegistrationHandle high =
                token.RegisterGameObjectTargeted<SimpleTargetedMessage>(
                    host,
                    _ =>
                    {
                        order.Add(1);
                        if (!added)
                        {
                            added = true;
                            low = token.RegisterGameObjectTargeted<SimpleTargetedMessage>(
                                host,
                                _ => order.Add(0),
                                priority: 0
                            );
                        }
                    },
                    priority: 1
                );

            SimpleTargetedMessage msg = new();
            msg.EmitGameObjectTargeted(host);
            CollectionAssert.AreEqual(new[] { 1 }, order);

            order.Clear();
            msg.EmitGameObjectTargeted(host);
            CollectionAssert.AreEqual(new[] { 0, 1 }, order);

            token.RemoveRegistration(high);
            if (low != default)
            {
                token.RemoveRegistration(low);
            }
            yield break;
        }

        [UnityTest]
        public IEnumerator BroadcastPriorityInsertedNextEmissionOrder()
        {
            GameObject host = new(
                nameof(BroadcastPriorityInsertedNextEmissionOrder),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(host);
            MessageRegistrationToken token = GetToken(
                host.GetComponent<EmptyMessageAwareComponent>()
            );

            System.Collections.Generic.List<int> order = new();
            bool added = false;
            MessageRegistrationHandle low = default;

            MessageRegistrationHandle high =
                token.RegisterGameObjectBroadcast<SimpleBroadcastMessage>(
                    host,
                    _ =>
                    {
                        order.Add(1);
                        if (!added)
                        {
                            added = true;
                            low = token.RegisterGameObjectBroadcast<SimpleBroadcastMessage>(
                                host,
                                _ => order.Add(0),
                                priority: 0
                            );
                        }
                    },
                    priority: 1
                );

            SimpleBroadcastMessage msg = new();
            msg.EmitGameObjectBroadcast(host);
            CollectionAssert.AreEqual(new[] { 1 }, order);

            order.Clear();
            msg.EmitGameObjectBroadcast(host);
            CollectionAssert.AreEqual(new[] { 0, 1 }, order);

            token.RemoveRegistration(high);
            if (low != default)
            {
                token.RemoveRegistration(low);
            }
            yield break;
        }
    }
}

#endif
