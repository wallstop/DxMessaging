#if UNITY_2021_3_OR_NEWER
namespace DxMessaging.Tests.Runtime.Core
{
    using System;
    using System.Reflection;
    using DxMessaging.Core;
    using DxMessaging.Core.Helper;
    using DxMessaging.Core.MessageBus;
    using DxMessaging.Core.Messages;
    using NUnit.Framework;

    public sealed class DxMessagingStaticStateTests
    {
        [Test]
        public void ResetRestoresDefaultStaticState()
        {
            DxMessagingStaticState.Reset();
            IMessageBus baselineBus = MessageHandler.MessageBus;
            MessageRegistrationHandle baselineHandle =
                MessageRegistrationHandle.CreateMessageRegistrationHandle();

            MessageRegistrationHandle.CreateMessageRegistrationHandle();
            MessagingDebug.enabled = true;
            MessagingDebug.LogFunction = (logLevel, message) => { };

            IMessageBus.GlobalDiagnosticsTargets = DiagnosticsTarget.All;
            IMessageBus.GlobalMessageBufferSize = 128;
            IMessageBus.GlobalSequentialIndex = 7;

            Type builderType = typeof(MessageRegistrationBuilder);
            FieldInfo syntheticOwnerField = builderType.GetField(
                "_syntheticOwnerCounter",
                BindingFlags.NonPublic | BindingFlags.Static
            );
            Assert.IsNotNull(syntheticOwnerField);
            syntheticOwnerField.SetValue(null, 3);

            MessageCache<object> cache = new MessageCache<object>();
            cache.GetOrAdd<DummyUntargetedMessage>();

            MessageHandler.SetGlobalMessageBus(new MessageBus());

            DxMessagingStaticState.Reset();

            Assert.IsFalse(MessagingDebug.enabled);
            Assert.IsNull(MessagingDebug.LogFunction);
            Assert.AreEqual(DiagnosticsTarget.Off, IMessageBus.GlobalDiagnosticsTargets);
            Assert.AreEqual(0, IMessageBus.GlobalMessageBufferSize);
            Assert.AreEqual(-1, IMessageBus.GlobalSequentialIndex);

            MessageRegistrationHandle resetHandle =
                MessageRegistrationHandle.CreateMessageRegistrationHandle();
            Assert.AreEqual(baselineHandle, resetHandle);

            object syntheticOwnerValue = syntheticOwnerField.GetValue(null);
            Assert.AreEqual(0, (int)syntheticOwnerValue);

            Assert.AreEqual(0, MessageHelperIndexer.TotalMessages);
            Assert.AreEqual(-1, MessageHelperIndexer<DummyUntargetedMessage>.SequentialId);
            Assert.AreSame(MessageHandler.InitialGlobalMessageBus, MessageHandler.MessageBus);
            Assert.AreSame(baselineBus, MessageHandler.MessageBus);

            DxMessagingStaticState.Reset();
        }

        private struct DummyUntargetedMessage : IUntargetedMessage { }
    }
}
#endif
