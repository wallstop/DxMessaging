namespace Tests.Runtime.Scripts.Messages
{
    using System;
    using DxMessaging.Core.Messages;

    public readonly struct ComplexTargetedMessage : ITargetedMessage<ComplexTargetedMessage>
    {
        // Guids are a bit beefy structure
        public readonly Guid firstId;
        public readonly Guid secondId;
        public readonly Guid thirdId;
        public readonly Guid fourthId;

        public ComplexTargetedMessage(Guid firstId)
        {
            this.firstId = firstId;
            secondId = Guid.NewGuid();
            thirdId = Guid.NewGuid();
            fourthId = Guid.NewGuid();
        }
    }
}
