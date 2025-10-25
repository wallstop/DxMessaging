namespace DxMessaging.Tests.Runtime.Core
{
    using DxMessaging.Core;
    using NUnit.Framework;
    using GlobalMessageBus = DxMessaging.Core.MessageBus.MessageBus;

    [TestFixture]
    public sealed class MessageHandlerGlobalBusTests
    {
        private GlobalMessageBus _originalBus;

        [SetUp]
        public void CaptureOriginalBus()
        {
            _originalBus = MessageHandler.MessageBus;
        }

        [TearDown]
        public void RestoreOriginalBus()
        {
            MessageHandler.SetGlobalMessageBus(_originalBus);
        }

        [Test]
        public void SetGlobalMessageBusReplacesGlobalInstance()
        {
            GlobalMessageBus customBus = new();
            MessageHandler.SetGlobalMessageBus(customBus);

            Assert.AreSame(customBus, MessageHandler.MessageBus);
        }

        [Test]
        public void ResetGlobalMessageBusRestoresDefaultInstance()
        {
            MessageHandler.ResetGlobalMessageBus();
            GlobalMessageBus expectedDefault = MessageHandler.MessageBus;

            GlobalMessageBus customBus = new();
            MessageHandler.SetGlobalMessageBus(customBus);
            Assert.AreSame(customBus, MessageHandler.MessageBus);

            MessageHandler.ResetGlobalMessageBus();
            Assert.AreSame(expectedDefault, MessageHandler.MessageBus);
        }
    }
}
