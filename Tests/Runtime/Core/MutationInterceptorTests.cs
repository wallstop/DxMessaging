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

    public sealed class MutationInterceptorTests : MessagingTestBase
    {
        [UnityTest]
        public IEnumerator UntargetedAddBlockingInterceptorDuringInterceptor()
        {
            GameObject host = new(
                nameof(UntargetedAddBlockingInterceptorDuringInterceptor),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(host);
            EmptyMessageAwareComponent comp = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);

            int first = 0;
            int second = 0;
            MessageRegistrationHandle? secondHandle = null;

            MessageRegistrationHandle firstHandle = token.RegisterUntargetedInterceptor(
                (ref SimpleUntargetedMessage _) =>
                {
                    first++;
                    if (secondHandle == null)
                    {
                        secondHandle = token.RegisterUntargetedInterceptor(
                            (ref SimpleUntargetedMessage __) =>
                            {
                                second++;
                                return true;
                            }
                        );
                    }

                    return false; // block pipeline
                }
            );

            SimpleUntargetedMessage msg = new();
            msg.EmitUntargeted();
            Assert.AreEqual(1, first);
            Assert.AreEqual(
                0,
                second,
                "New interceptor must not run in the same emission when blocked."
            );

            msg.EmitUntargeted();
            Assert.AreEqual(2, first);
            Assert.AreEqual(0, second, "Pipeline remains blocked by first; second not invoked.");

            // Unblock by removing the first
            token.RemoveRegistration(firstHandle);
            msg.EmitUntargeted();
            Assert.AreEqual(2, first);
            Assert.AreEqual(1, second, "Second runs once first no longer blocks.");

            token.RemoveRegistration(firstHandle);
            if (secondHandle.HasValue)
            {
                token.RemoveRegistration(secondHandle.Value);
            }
            yield break;
        }

        [UnityTest]
        public IEnumerator TargetedAddBlockingInterceptorDuringInterceptor()
        {
            GameObject host = new(
                nameof(TargetedAddBlockingInterceptorDuringInterceptor),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(host);
            EmptyMessageAwareComponent comp = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);

            int first = 0;
            int second = 0;
            MessageRegistrationHandle? secondHandle = null;

            MessageRegistrationHandle firstHandle = token.RegisterTargetedInterceptor(
                (ref InstanceId _, ref SimpleTargetedMessage __) =>
                {
                    first++;
                    if (secondHandle == null)
                    {
                        secondHandle = token.RegisterTargetedInterceptor(
                            (ref InstanceId __1, ref SimpleTargetedMessage __2) =>
                            {
                                second++;
                                return true;
                            }
                        );
                    }

                    return false;
                }
            );

            SimpleTargetedMessage msg = new();
            msg.EmitGameObjectTargeted(host);
            Assert.AreEqual(1, first);
            Assert.AreEqual(0, second);

            msg.EmitGameObjectTargeted(host);
            Assert.AreEqual(2, first);
            Assert.AreEqual(0, second);

            token.RemoveRegistration(firstHandle);
            msg.EmitGameObjectTargeted(host);
            Assert.AreEqual(2, first);
            Assert.AreEqual(1, second);

            token.RemoveRegistration(firstHandle);
            if (secondHandle.HasValue)
            {
                token.RemoveRegistration(secondHandle.Value);
            }
            yield break;
        }

        [UnityTest]
        public IEnumerator BroadcastAddBlockingInterceptorDuringInterceptor()
        {
            GameObject host = new(
                nameof(BroadcastAddBlockingInterceptorDuringInterceptor),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(host);
            EmptyMessageAwareComponent comp = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);

            int first = 0;
            int second = 0;
            MessageRegistrationHandle? secondHandle = null;

            MessageRegistrationHandle firstHandle = token.RegisterBroadcastInterceptor(
                (ref InstanceId _, ref SimpleBroadcastMessage __) =>
                {
                    first++;
                    if (secondHandle == null)
                    {
                        secondHandle = token.RegisterBroadcastInterceptor(
                            (ref InstanceId __1, ref SimpleBroadcastMessage __2) =>
                            {
                                second++;
                                return true;
                            }
                        );
                    }

                    return false;
                }
            );

            SimpleBroadcastMessage msg = new();
            msg.EmitGameObjectBroadcast(host);
            Assert.AreEqual(1, first);
            Assert.AreEqual(0, second);

            msg.EmitGameObjectBroadcast(host);
            Assert.AreEqual(2, first);
            Assert.AreEqual(0, second);

            token.RemoveRegistration(firstHandle);
            msg.EmitGameObjectBroadcast(host);
            Assert.AreEqual(2, first);
            Assert.AreEqual(1, second);

            token.RemoveRegistration(firstHandle);
            if (secondHandle.HasValue)
            {
                token.RemoveRegistration(secondHandle.Value);
            }
            yield break;
        }
    }
}
