namespace DxMessaging.Core.Diagnostics
{
    using System;

    public readonly struct MessageRegistrationMetadata
    {
        public readonly InstanceId? context;
        public readonly MessageRegistrationType registrationType;
        public readonly Type type;
        public readonly int priority;

        public MessageRegistrationMetadata(
            InstanceId? context,
            Type type,
            MessageRegistrationType registrationType,
            int priority
        )
        {
            this.context = context;
            this.type = type;
            this.registrationType = registrationType;
            this.priority = priority;
        }
    }
}
