#if UNITY_2021_3_OR_NEWER
namespace DxMessaging.Tests.Runtime.Core
{
    using System.Collections;
    using System.Collections.Generic;
    using DxMessaging.Core;
    using DxMessaging.Core.DataStructure;
    using DxMessaging.Core.Diagnostics;
    using DxMessaging.Core.Extensions;
    using DxMessaging.Core.MessageBus;
    using DxMessaging.Tests.Runtime.Scripts.Components;
    using DxMessaging.Tests.Runtime.Scripts.Messages;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;

    public sealed class DiagnosticsTests : MessagingTestBase
    {
        [UnityTest]
        public IEnumerator TokenDiagnosticModeTracksEmissions()
        {
            GameObject host = new(
                nameof(TokenDiagnosticModeTracksEmissions),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(host);
            EmptyMessageAwareComponent component = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);
            token.DiagnosticMode = true;

            int count = 0;
            MessageRegistrationHandle handle = token.RegisterUntargeted<SimpleUntargetedMessage>(
                _ => ++count
            );

            SimpleUntargetedMessage message = new();
            message.EmitUntargeted();
            Assert.AreEqual(1, count);

            Dictionary<MessageRegistrationHandle, int> callCounts = GetCallCounts(token);
            Assert.IsTrue(callCounts.TryGetValue(handle, out int recordedCount));
            Assert.AreEqual(1, recordedCount);

            CyclicBuffer<MessageEmissionData> emissions = GetEmissionBuffer(token);
            Assert.AreEqual(1, emissions.Count);

            token.RemoveRegistration(handle);
            yield break;
        }

        [UnityTest]
        public IEnumerator MessageBusDiagnosticsRespectBufferSize()
        {
            DiagnosticsTarget originalDiagnostics = IMessageBus.GlobalDiagnosticsTargets;
            int originalBufferSize = IMessageBus.GlobalMessageBufferSize;
            try
            {
                IMessageBus.GlobalDiagnosticsTargets = DiagnosticsTarget.All;
                IMessageBus.GlobalMessageBufferSize = 2;

                GameObject host = new(nameof(MessageBusDiagnosticsRespectBufferSize));
                _spawned.Add(host);
                MessageHandler handler = new(host) { active = true };
                MessageBus customBus = new() { DiagnosticsMode = true };

                MessageRegistrationToken token = MessageRegistrationToken.Create(
                    handler,
                    customBus
                );
                token.DiagnosticMode = true;
                token.Enable();

                int count = 0;
                MessageRegistrationHandle handle =
                    token.RegisterUntargeted<SimpleUntargetedMessage>(_ => ++count);

                SimpleUntargetedMessage message = new();
                for (int i = 0; i < 3; ++i)
                {
                    message.EmitUntargeted(customBus);
                }
                Assert.AreEqual(3, count);

                CyclicBuffer<MessageEmissionData> busBuffer = GetEmissionBuffer(customBus);
                Assert.AreEqual(2, busBuffer.Count);

                token.RemoveRegistration(handle);
                token.Disable();
                handler.active = false;
            }
            finally
            {
                IMessageBus.GlobalDiagnosticsTargets = originalDiagnostics;
                IMessageBus.GlobalMessageBufferSize = originalBufferSize;
            }

            yield break;
        }

        private static Dictionary<MessageRegistrationHandle, int> GetCallCounts(
            MessageRegistrationToken token
        )
        {
            return token._callCounts;
        }

        private static CyclicBuffer<MessageEmissionData> GetEmissionBuffer(
            MessageRegistrationToken token
        )
        {
            return token._emissionBuffer;
        }

        private static CyclicBuffer<MessageEmissionData> GetEmissionBuffer(MessageBus bus)
        {
            return bus._emissionBuffer;
        }
    }
}

#endif
