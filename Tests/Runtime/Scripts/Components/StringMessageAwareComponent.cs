namespace DxMessaging.Tests.Runtime.Scripts.Components
{
    using DxMessaging.Core;
    using DxMessaging.Core.Messages;
    using DxMessaging.Unity;

    public sealed class StringMessageAwareComponent : MessageAwareComponent
    {
        public int gameObjectTargetedCount;
        public int componentTargetedCount;
        public int untargetedGlobalCount;
        public int gameObjectBroadcastCount;
        public int componentBroadcastCount;
        public int broadcastWithoutSourceCount;
        public int targetedWithoutTargetingCount;

        protected override void RegisterMessageHandlers()
        {
            base.RegisterMessageHandlers();

            _ = Token.RegisterGameObjectTargeted<StringMessage>(
                gameObject,
                HandleStringGameObjectTargeted
            );
            _ = Token.RegisterComponentTargeted<StringMessage>(this, HandleStringComponentTargeted);

            _ = Token.RegisterTargetedWithoutTargeting<StringMessage>(HandleAnyStringTargeted);

            _ = Token.RegisterGameObjectBroadcast<SourcedStringMessage>(
                gameObject,
                HandleSourcedStringGameObjectBroadcast
            );
            _ = Token.RegisterComponentBroadcast<SourcedStringMessage>(
                this,
                HandleSourcedStringComponentBroadcast
            );
            _ = Token.RegisterBroadcastWithoutSource<SourcedStringMessage>(HandleAnySourcedString);
        }

        private void HandleStringGameObjectTargeted(ref StringMessage message)
        {
            gameObjectTargetedCount++;
        }

        private void HandleStringComponentTargeted(ref StringMessage message)
        {
            componentTargetedCount++;
        }

        private void HandleAnyStringTargeted(ref InstanceId target, ref StringMessage message)
        {
            targetedWithoutTargetingCount++;
        }

        protected override void HandleGlobalStringMessage(ref GlobalStringMessage message)
        {
            untargetedGlobalCount++;
        }

        private void HandleSourcedStringGameObjectBroadcast(ref SourcedStringMessage message)
        {
            gameObjectBroadcastCount++;
        }

        private void HandleSourcedStringComponentBroadcast(ref SourcedStringMessage message)
        {
            componentBroadcastCount++;
        }

        private void HandleAnySourcedString(ref InstanceId source, ref SourcedStringMessage message)
        {
            broadcastWithoutSourceCount++;
        }
    }
}
