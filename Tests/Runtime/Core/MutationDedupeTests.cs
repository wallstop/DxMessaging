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

    public sealed class MutationDedupeTests : MessagingTestBase
    {
        [UnityTest]
        public IEnumerator TargetedAddSameDelegateDoesNotDuplicateInvocation()
        {
            GameObject host = new("TargetedSameDelegateHost", typeof(EmptyMessageAwareComponent));
            _spawned.Add(host);
            MessageRegistrationToken token = GetToken(
                host.GetComponent<EmptyMessageAwareComponent>()
            );

            int count = 0;
            MessageRegistrationHandle? secondHandle = null;

            void Local(SimpleTargetedMessage _)
            {
                count++;
                if (secondHandle == null)
                {
                    secondHandle = token.RegisterGameObjectTargeted<SimpleTargetedMessage>(
                        host,
                        Local
                    );
                }
            }

            MessageRegistrationHandle firstHandle =
                token.RegisterGameObjectTargeted<SimpleTargetedMessage>(host, Local);

            SimpleTargetedMessage msg = new();
            msg.EmitGameObjectTargeted(host);
            Assert.AreEqual(1, count);

            msg.EmitGameObjectTargeted(host);
            Assert.AreEqual(2, count);

            token.RemoveRegistration(firstHandle);
            if (secondHandle.HasValue)
            {
                token.RemoveRegistration(secondHandle.Value);
            }
            yield break;
        }

        [UnityTest]
        public IEnumerator BroadcastAddSameDelegateDoesNotDuplicateInvocation()
        {
            GameObject host = new("BroadcastSameDelegateHost", typeof(EmptyMessageAwareComponent));
            _spawned.Add(host);
            MessageRegistrationToken token = GetToken(
                host.GetComponent<EmptyMessageAwareComponent>()
            );

            int count = 0;
            MessageRegistrationHandle? secondHandle = null;

            void Local(SimpleBroadcastMessage _)
            {
                count++;
                if (secondHandle == null)
                {
                    secondHandle = token.RegisterGameObjectBroadcast<SimpleBroadcastMessage>(
                        host,
                        Local
                    );
                }
            }

            MessageRegistrationHandle firstHandle =
                token.RegisterGameObjectBroadcast<SimpleBroadcastMessage>(host, Local);

            SimpleBroadcastMessage msg = new();
            msg.EmitGameObjectBroadcast(host);
            Assert.AreEqual(1, count);

            msg.EmitGameObjectBroadcast(host);
            Assert.AreEqual(2, count);

            token.RemoveRegistration(firstHandle);
            if (secondHandle.HasValue)
            {
                token.RemoveRegistration(secondHandle.Value);
            }
            yield break;
        }
    }
}

#endif
