#if UNITY_2021_3_OR_NEWER
namespace DxMessaging.Tests.Runtime.Core
{
    using System.Collections;
    using System.Collections.Generic;
    using DxMessaging.Core;
    using DxMessaging.Core.Extensions;
    using DxMessaging.Core.Messages;
    using DxMessaging.Tests.Runtime.Scripts.Components;
    using DxMessaging.Tests.Runtime.Scripts.Messages;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;

    public sealed class OrderingManyRegistrationsTests : MessagingTestBase
    {
        private const int ManyRegistrationCount = 32;

        private static int[] BuildSequentialExpected(int count)
        {
            int[] expected = new int[count];
            for (int i = 0; i < count; i++)
            {
                expected[i] = i;
            }

            return expected;
        }

        private static void AssertSequence(IList<int> actual, string message)
        {
            int[] expected = BuildSequentialExpected(actual.Count);
            int[] actualCopy = new int[actual.Count];
            for (int i = 0; i < actual.Count; i++)
            {
                actualCopy[i] = actual[i];
            }

            Assert.AreEqual(expected, actualCopy, message);
        }

        [UnityTest]
        public IEnumerator UntargetedHandlersManyRegistrationsMaintainOrder()
        {
            GameObject host = new(
                nameof(UntargetedHandlersManyRegistrationsMaintainOrder),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(host);
            EmptyMessageAwareComponent component = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);

            List<int> fastOrder = new(ManyRegistrationCount);
            List<int> actionOrder = new(ManyRegistrationCount);

            for (int i = 0; i < ManyRegistrationCount; i++)
            {
                int index = i;
                _ = token.RegisterUntargeted(
                    (ref SimpleUntargetedMessage _) => fastOrder.Add(index)
                );
            }

            for (int i = 0; i < ManyRegistrationCount; i++)
            {
                int index = i;
                _ = token.RegisterUntargeted((SimpleUntargetedMessage _) => actionOrder.Add(index));
            }

            SimpleUntargetedMessage message = new();
            message.EmitUntargeted();

            AssertSequence(
                fastOrder,
                "Untargeted fast handlers should run in registration order even with many entries."
            );
            AssertSequence(
                actionOrder,
                "Untargeted action handlers should run in registration order even with many entries."
            );
            yield break;
        }

        [UnityTest]
        public IEnumerator UntargetedPostProcessorsManyRegistrationsMaintainOrder()
        {
            GameObject host = new(
                nameof(UntargetedPostProcessorsManyRegistrationsMaintainOrder),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(host);
            EmptyMessageAwareComponent component = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);

            List<int> postProcessorOrder = new(ManyRegistrationCount);

            for (int i = 0; i < ManyRegistrationCount; i++)
            {
                int index = i;
                _ = token.RegisterUntargetedPostProcessor(
                    (ref SimpleUntargetedMessage _) => postProcessorOrder.Add(index)
                );
            }

            SimpleUntargetedMessage message = new();
            message.EmitUntargeted();

            AssertSequence(
                postProcessorOrder,
                "Untargeted post-processors should run in registration order even with many entries."
            );
            yield break;
        }

        [UnityTest]
        public IEnumerator TargetedHandlersManyRegistrationsMaintainOrder()
        {
            GameObject host = new(
                nameof(TargetedHandlersManyRegistrationsMaintainOrder),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(host);
            EmptyMessageAwareComponent component = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);

            List<int> fastOrder = new(ManyRegistrationCount);
            List<int> actionOrder = new(ManyRegistrationCount);

            for (int i = 0; i < ManyRegistrationCount; i++)
            {
                int index = i;
                _ = token.RegisterComponentTargeted(
                    component,
                    (ref SimpleTargetedMessage _) => fastOrder.Add(index)
                );
            }

            for (int i = 0; i < ManyRegistrationCount; i++)
            {
                int index = i;
                _ = token.RegisterComponentTargeted(
                    component,
                    (SimpleTargetedMessage _) => actionOrder.Add(index)
                );
            }

            SimpleTargetedMessage message = new();
            message.EmitComponentTargeted(component);

            AssertSequence(
                fastOrder,
                "Targeted fast handlers should run in registration order even with many entries."
            );
            AssertSequence(
                actionOrder,
                "Targeted action handlers should run in registration order even with many entries."
            );
            yield break;
        }

        [UnityTest]
        public IEnumerator TargetedPostProcessorsManyRegistrationsMaintainOrder()
        {
            GameObject host = new(
                nameof(TargetedPostProcessorsManyRegistrationsMaintainOrder),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(host);
            EmptyMessageAwareComponent component = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);

            List<int> fastOrder = new(ManyRegistrationCount);
            List<int> actionOrder = new(ManyRegistrationCount);

            InstanceId target = component;

            for (int i = 0; i < ManyRegistrationCount; i++)
            {
                int index = i;
                _ = token.RegisterTargetedPostProcessor(
                    target,
                    (ref SimpleTargetedMessage _) => fastOrder.Add(index)
                );
            }

            for (int i = 0; i < ManyRegistrationCount; i++)
            {
                int index = i;
                _ = token.RegisterTargetedPostProcessor(
                    target,
                    (SimpleTargetedMessage _) => actionOrder.Add(index)
                );
            }

            SimpleTargetedMessage message = new();
            message.EmitComponentTargeted(component);

            AssertSequence(
                fastOrder,
                "Targeted fast post-processors should run in registration order even with many entries."
            );
            AssertSequence(
                actionOrder,
                "Targeted action post-processors should run in registration order even with many entries."
            );
            yield break;
        }

        [UnityTest]
        public IEnumerator TargetedWithoutTargetingManyRegistrationsMaintainOrder()
        {
            GameObject host = new(
                nameof(TargetedWithoutTargetingManyRegistrationsMaintainOrder),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(host);
            EmptyMessageAwareComponent component = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);

            List<int> fastOrder = new(ManyRegistrationCount);
            List<int> actionOrder = new(ManyRegistrationCount);

            for (int i = 0; i < ManyRegistrationCount; i++)
            {
                int index = i;
                _ = token.RegisterTargetedWithoutTargeting(
                    (ref InstanceId _, ref SimpleTargetedMessage _) => fastOrder.Add(index)
                );
            }

            for (int i = 0; i < ManyRegistrationCount; i++)
            {
                int index = i;
                _ = token.RegisterTargetedWithoutTargeting(
                    (InstanceId _, SimpleTargetedMessage _) => actionOrder.Add(index)
                );
            }

            SimpleTargetedMessage message = new();
            message.EmitComponentTargeted(component);

            AssertSequence(
                fastOrder,
                "Targeted-without-targeting fast handlers should run in registration order even with many entries."
            );
            AssertSequence(
                actionOrder,
                "Targeted-without-targeting action handlers should run in registration order even with many entries."
            );
            yield break;
        }

        [UnityTest]
        public IEnumerator TargetedWithoutTargetingPostProcessorsManyRegistrationsMaintainOrder()
        {
            GameObject host = new(
                nameof(TargetedWithoutTargetingPostProcessorsManyRegistrationsMaintainOrder),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(host);
            EmptyMessageAwareComponent component = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);

            List<int> fastOrder = new(ManyRegistrationCount);
            List<int> actionOrder = new(ManyRegistrationCount);

            for (int i = 0; i < ManyRegistrationCount; i++)
            {
                int index = i;
                _ = token.RegisterTargetedWithoutTargetingPostProcessor(
                    (ref InstanceId _, ref SimpleTargetedMessage _) => fastOrder.Add(index)
                );
            }

            for (int i = 0; i < ManyRegistrationCount; i++)
            {
                int index = i;
                _ = token.RegisterTargetedWithoutTargetingPostProcessor(
                    (InstanceId _, SimpleTargetedMessage _) => actionOrder.Add(index)
                );
            }

            SimpleTargetedMessage message = new();
            message.EmitComponentTargeted(component);

            AssertSequence(
                fastOrder,
                "Targeted-without-targeting fast post-processors should run in registration order even with many entries."
            );
            AssertSequence(
                actionOrder,
                "Targeted-without-targeting action post-processors should run in registration order even with many entries."
            );
            yield break;
        }

        [UnityTest]
        public IEnumerator BroadcastHandlersManyRegistrationsMaintainOrder()
        {
            GameObject host = new(
                nameof(BroadcastHandlersManyRegistrationsMaintainOrder),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(host);
            EmptyMessageAwareComponent component = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);

            List<int> fastOrder = new(ManyRegistrationCount);
            List<int> actionOrder = new(ManyRegistrationCount);

            for (int i = 0; i < ManyRegistrationCount; i++)
            {
                int index = i;
                _ = token.RegisterComponentBroadcast(
                    component,
                    (ref SimpleBroadcastMessage _) => fastOrder.Add(index)
                );
            }

            for (int i = 0; i < ManyRegistrationCount; i++)
            {
                int index = i;
                _ = token.RegisterComponentBroadcast(
                    component,
                    (SimpleBroadcastMessage _) => actionOrder.Add(index)
                );
            }

            SimpleBroadcastMessage message = new();
            message.EmitComponentBroadcast(component);

            AssertSequence(
                fastOrder,
                "Broadcast fast handlers should run in registration order even with many entries."
            );
            AssertSequence(
                actionOrder,
                "Broadcast action handlers should run in registration order even with many entries."
            );
            yield break;
        }

        [UnityTest]
        public IEnumerator BroadcastPostProcessorsManyRegistrationsMaintainOrder()
        {
            GameObject host = new(
                nameof(BroadcastPostProcessorsManyRegistrationsMaintainOrder),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(host);
            EmptyMessageAwareComponent component = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);

            List<int> fastOrder = new(ManyRegistrationCount);
            List<int> actionOrder = new(ManyRegistrationCount);

            InstanceId source = component;

            for (int i = 0; i < ManyRegistrationCount; i++)
            {
                int index = i;
                _ = token.RegisterBroadcastPostProcessor(
                    source,
                    (ref SimpleBroadcastMessage _) => fastOrder.Add(index)
                );
            }

            for (int i = 0; i < ManyRegistrationCount; i++)
            {
                int index = i;
                _ = token.RegisterBroadcastPostProcessor(
                    source,
                    (SimpleBroadcastMessage _) => actionOrder.Add(index)
                );
            }

            SimpleBroadcastMessage message = new();
            message.EmitComponentBroadcast(component);

            AssertSequence(
                fastOrder,
                "Broadcast fast post-processors should run in registration order even with many entries."
            );
            AssertSequence(
                actionOrder,
                "Broadcast action post-processors should run in registration order even with many entries."
            );
            yield break;
        }

        [UnityTest]
        public IEnumerator BroadcastWithoutSourceManyRegistrationsMaintainOrder()
        {
            GameObject host = new(
                nameof(BroadcastWithoutSourceManyRegistrationsMaintainOrder),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(host);
            EmptyMessageAwareComponent component = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);

            List<int> fastOrder = new(ManyRegistrationCount);
            List<int> actionOrder = new(ManyRegistrationCount);

            for (int i = 0; i < ManyRegistrationCount; i++)
            {
                int index = i;
                _ = token.RegisterBroadcastWithoutSource(
                    (ref InstanceId _, ref SimpleBroadcastMessage _) => fastOrder.Add(index)
                );
            }

            for (int i = 0; i < ManyRegistrationCount; i++)
            {
                int index = i;
                _ = token.RegisterBroadcastWithoutSource(
                    (InstanceId _, SimpleBroadcastMessage _) => actionOrder.Add(index)
                );
            }

            SimpleBroadcastMessage message = new();
            message.EmitComponentBroadcast(component);

            AssertSequence(
                fastOrder,
                "Broadcast-without-source fast handlers should run in registration order even with many entries."
            );
            AssertSequence(
                actionOrder,
                "Broadcast-without-source action handlers should run in registration order even with many entries."
            );
            yield break;
        }

        [UnityTest]
        public IEnumerator BroadcastWithoutSourcePostProcessorsManyRegistrationsMaintainOrder()
        {
            GameObject host = new(
                nameof(BroadcastWithoutSourcePostProcessorsManyRegistrationsMaintainOrder),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(host);
            EmptyMessageAwareComponent component = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);

            List<int> fastOrder = new(ManyRegistrationCount);
            List<int> actionOrder = new(ManyRegistrationCount);

            for (int i = 0; i < ManyRegistrationCount; i++)
            {
                int index = i;
                _ = token.RegisterBroadcastWithoutSourcePostProcessor(
                    (ref InstanceId _, ref SimpleBroadcastMessage _) => fastOrder.Add(index)
                );
            }

            for (int i = 0; i < ManyRegistrationCount; i++)
            {
                int index = i;
                _ = token.RegisterBroadcastWithoutSourcePostProcessor(
                    (InstanceId _, SimpleBroadcastMessage _) => actionOrder.Add(index)
                );
            }

            SimpleBroadcastMessage message = new();
            message.EmitComponentBroadcast(component);

            AssertSequence(
                fastOrder,
                "Broadcast-without-source fast post-processors should run in registration order even with many entries."
            );
            AssertSequence(
                actionOrder,
                "Broadcast-without-source action post-processors should run in registration order even with many entries."
            );
            yield break;
        }

        [UnityTest]
        public IEnumerator GlobalAcceptAllActionsManyRegistrationsMaintainOrder()
        {
            GameObject host = new(
                nameof(GlobalAcceptAllActionsManyRegistrationsMaintainOrder),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(host);
            EmptyMessageAwareComponent component = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);

            List<int> untargetedOrder = new(ManyRegistrationCount);
            List<int> targetedOrder = new(ManyRegistrationCount);
            List<int> broadcastOrder = new(ManyRegistrationCount);

            for (int i = 0; i < ManyRegistrationCount; i++)
            {
                int index = i;
                _ = token.RegisterGlobalAcceptAll(
                    _ => untargetedOrder.Add(index),
                    (_, _) => targetedOrder.Add(index),
                    (_, _) => broadcastOrder.Add(index)
                );
            }

            SimpleUntargetedMessage untargeted = new();
            untargeted.EmitUntargeted();

            SimpleTargetedMessage targeted = new();
            targeted.EmitComponentTargeted(component);

            SimpleBroadcastMessage broadcast = new();
            broadcast.EmitComponentBroadcast(component);

            AssertSequence(
                untargetedOrder,
                "Global accept-all action handlers for untargeted messages should run in registration order even with many entries."
            );
            AssertSequence(
                targetedOrder,
                "Global accept-all action handlers for targeted messages should run in registration order even with many entries."
            );
            AssertSequence(
                broadcastOrder,
                "Global accept-all action handlers for broadcast messages should run in registration order even with many entries."
            );
            yield break;
        }

        [UnityTest]
        public IEnumerator GlobalAcceptAllFastManyRegistrationsMaintainOrder()
        {
            GameObject host = new(
                nameof(GlobalAcceptAllFastManyRegistrationsMaintainOrder),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(host);
            EmptyMessageAwareComponent component = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);

            List<int> untargetedOrder = new(ManyRegistrationCount);
            List<int> targetedOrder = new(ManyRegistrationCount);
            List<int> broadcastOrder = new(ManyRegistrationCount);

            for (int i = 0; i < ManyRegistrationCount; i++)
            {
                int index = i;
                _ = token.RegisterGlobalAcceptAll(
                    (ref IUntargetedMessage _) => untargetedOrder.Add(index),
                    (ref InstanceId _, ref ITargetedMessage _) => targetedOrder.Add(index),
                    (ref InstanceId _, ref IBroadcastMessage _) => broadcastOrder.Add(index)
                );
            }

            SimpleUntargetedMessage untargeted = new();
            untargeted.EmitUntargeted();

            SimpleTargetedMessage targeted = new();
            targeted.EmitComponentTargeted(component);

            SimpleBroadcastMessage broadcast = new();
            broadcast.EmitComponentBroadcast(component);

            AssertSequence(
                untargetedOrder,
                "Global accept-all fast handlers for untargeted messages should run in registration order even with many entries."
            );
            AssertSequence(
                targetedOrder,
                "Global accept-all fast handlers for targeted messages should run in registration order even with many entries."
            );
            AssertSequence(
                broadcastOrder,
                "Global accept-all fast handlers for broadcast messages should run in registration order even with many entries."
            );
            yield break;
        }

        [UnityTest]
        public IEnumerator UntargetedInterceptorsManyRegistrationsMaintainOrder()
        {
            GameObject host = new(
                nameof(UntargetedInterceptorsManyRegistrationsMaintainOrder),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(host);
            EmptyMessageAwareComponent component = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);

            List<int> order = new(ManyRegistrationCount);

            for (int i = 0; i < ManyRegistrationCount; i++)
            {
                int index = i;
                _ = token.RegisterUntargetedInterceptor(
                    (ref SimpleUntargetedMessage _) =>
                    {
                        order.Add(index);
                        return true;
                    }
                );
            }

            SimpleUntargetedMessage message = new();
            message.EmitUntargeted();

            AssertSequence(
                order,
                "Untargeted interceptors should run in registration order even with many entries."
            );
            yield break;
        }

        [UnityTest]
        public IEnumerator TargetedInterceptorsManyRegistrationsMaintainOrder()
        {
            GameObject host = new(
                nameof(TargetedInterceptorsManyRegistrationsMaintainOrder),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(host);
            EmptyMessageAwareComponent component = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);

            List<int> order = new(ManyRegistrationCount);

            for (int i = 0; i < ManyRegistrationCount; i++)
            {
                int index = i;
                _ = token.RegisterTargetedInterceptor(
                    (ref InstanceId _, ref SimpleTargetedMessage _) =>
                    {
                        order.Add(index);
                        return true;
                    }
                );
            }

            SimpleTargetedMessage message = new();
            message.EmitComponentTargeted(component);

            AssertSequence(
                order,
                "Targeted interceptors should run in registration order even with many entries."
            );
            yield break;
        }

        [UnityTest]
        public IEnumerator BroadcastInterceptorsManyRegistrationsMaintainOrder()
        {
            GameObject host = new(
                nameof(BroadcastInterceptorsManyRegistrationsMaintainOrder),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(host);
            EmptyMessageAwareComponent component = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);

            List<int> order = new(ManyRegistrationCount);

            for (int i = 0; i < ManyRegistrationCount; i++)
            {
                int index = i;
                _ = token.RegisterBroadcastInterceptor(
                    (ref InstanceId _, ref SimpleBroadcastMessage _) =>
                    {
                        order.Add(index);
                        return true;
                    }
                );
            }

            SimpleBroadcastMessage message = new();
            message.EmitComponentBroadcast(component);

            AssertSequence(
                order,
                "Broadcast interceptors should run in registration order even with many entries."
            );
            yield break;
        }
    }
}
#endif
