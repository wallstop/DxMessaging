namespace DxMessaging.Tests.Runtime.Scripts.Components
{
    using DxMessaging.Tests.Runtime.Scripts.Messages;
    using DxMessaging.Unity;

    public sealed class UntargetedReceiverComponent : MessageAwareComponent
    {
        public int count;

        protected override void RegisterMessageHandlers()
        {
            base.RegisterMessageHandlers();
            _ = Token.RegisterUntargeted<SimpleUntargetedMessage>(_ => count++);
        }
    }
}
