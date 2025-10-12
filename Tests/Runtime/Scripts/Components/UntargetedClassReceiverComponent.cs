namespace DxMessaging.Tests.Runtime.Scripts.Components
{
    using DxMessaging.Tests.Runtime.Scripts.Messages;
    using DxMessaging.Unity;

    public sealed class UntargetedClassReceiverComponent : MessageAwareComponent
    {
        public int count;

        protected override void RegisterMessageHandlers()
        {
            base.RegisterMessageHandlers();
            _ = Token.RegisterUntargeted<ClassUntargetedMessage>(_ => count++);
        }
    }
}
