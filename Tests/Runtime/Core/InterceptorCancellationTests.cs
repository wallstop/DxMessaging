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

    public sealed class InterceptorCancellationTests : MessagingTestBase
    {
        [UnityTest]
        public IEnumerator UntargetedInterceptorCancelsHandlersAndPostProcessors()
        {
            GameObject host = new(
                nameof(UntargetedInterceptorCancelsHandlersAndPostProcessors),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(host);
            EmptyMessageAwareComponent component = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);

            int handled = 0;
            int postProcessed = 0;

            _ = token.RegisterUntargeted<SimpleUntargetedMessage>(_ => handled++);
            _ = token.RegisterUntargetedPostProcessor(
                (ref SimpleUntargetedMessage _) => postProcessed++
            );

            // Register a canceling interceptor (always false)
            _ = token.RegisterUntargetedInterceptor((ref SimpleUntargetedMessage _) => false);

            // Also register a later interceptor that would be skipped if earlier cancels
            int laterRan = 0;
            _ = token.RegisterUntargetedInterceptor(
                (ref SimpleUntargetedMessage _) =>
                {
                    laterRan++;
                    return true;
                },
                priority: 10
            );

            SimpleUntargetedMessage msg = new();
            msg.EmitUntargeted();

            Assert.AreEqual(0, handled, "Handlers must not run when interceptor cancels.");
            Assert.AreEqual(
                0,
                postProcessed,
                "Post-processors must not run when interceptor cancels."
            );
            Assert.AreEqual(0, laterRan, "Later interceptors must not run after cancellation.");
            yield break;
        }

        [UnityTest]
        public IEnumerator TargetedInterceptorCancelsHandlersAndPostProcessors()
        {
            GameObject host = new(
                nameof(TargetedInterceptorCancelsHandlersAndPostProcessors),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(host);
            EmptyMessageAwareComponent component = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);

            int handled = 0;
            int postProcessed = 0;

            _ = token.RegisterGameObjectTargeted<SimpleTargetedMessage>(host, _ => handled++);
            _ = token.RegisterGameObjectTargetedPostProcessor(
                host,
                (ref SimpleTargetedMessage _) => postProcessed++
            );

            // Cancel targeted messages
            _ = token.RegisterTargetedInterceptor(
                (ref InstanceId _, ref SimpleTargetedMessage _) => false
            );

            // A later interceptor that would not execute after cancellation
            int laterRan = 0;
            _ = token.RegisterTargetedInterceptor(
                (ref InstanceId _, ref SimpleTargetedMessage _) =>
                {
                    laterRan++;
                    return true;
                },
                priority: 10
            );

            SimpleTargetedMessage msg = new();
            msg.EmitGameObjectTargeted(host);

            Assert.AreEqual(0, handled, "Targeted handlers must not run when interceptor cancels.");
            Assert.AreEqual(
                0,
                postProcessed,
                "Targeted post-processors must not run when interceptor cancels."
            );
            Assert.AreEqual(
                0,
                laterRan,
                "Later targeted interceptors must not run after cancellation."
            );
            yield break;
        }

        [UnityTest]
        public IEnumerator BroadcastInterceptorCancelsHandlersAndPostProcessors()
        {
            GameObject host = new(
                nameof(BroadcastInterceptorCancelsHandlersAndPostProcessors),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(host);
            EmptyMessageAwareComponent component = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);

            int handled = 0;
            int postProcessed = 0;

            _ = token.RegisterGameObjectBroadcast<SimpleBroadcastMessage>(host, _ => handled++);
            _ = token.RegisterGameObjectBroadcastPostProcessor<SimpleBroadcastMessage>(
                host,
                _ => postProcessed++
            );

            // Cancel broadcast messages
            _ = token.RegisterBroadcastInterceptor(
                (ref InstanceId _, ref SimpleBroadcastMessage _) => false
            );

            // A later interceptor that would not execute after cancellation
            int laterRan = 0;
            _ = token.RegisterBroadcastInterceptor(
                (ref InstanceId _, ref SimpleBroadcastMessage _) =>
                {
                    laterRan++;
                    return true;
                },
                priority: 10
            );

            SimpleBroadcastMessage msg = new();
            msg.EmitGameObjectBroadcast(host);

            Assert.AreEqual(
                0,
                handled,
                "Broadcast handlers must not run when interceptor cancels."
            );
            Assert.AreEqual(
                0,
                postProcessed,
                "Broadcast post-processors must not run when interceptor cancels."
            );
            Assert.AreEqual(
                0,
                laterRan,
                "Later broadcast interceptors must not run after cancellation."
            );
            yield break;
        }
    }
}

#endif
