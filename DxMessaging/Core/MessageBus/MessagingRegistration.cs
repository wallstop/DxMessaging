using System;

namespace DxMessaging.Core.MessageBus
{
    public enum RegistrationType
    {
        Add,
        Remove
    }

    public enum MessageType
    {
        Targeted,
        Untargeted,
        TargetedWithoutTargeting,
        GlobalAcceptAll
    }

    [Serializable]
    public struct MessagingRegistration
    {
        public readonly InstanceId Id;
        public readonly string Type;
        public readonly RegistrationType RegistrationType;
        public readonly MessageType MessageType;

        // TODO: Time

        public MessagingRegistration(InstanceId id, Type type, RegistrationType registrationType,
            MessageType messageType) : this()
        {
            Id = id;
            Type = type.Name;
            RegistrationType = registrationType;
            MessageType = messageType;
        }

        public override string ToString()
        {
            // Poor man's JSON
            return string.Format("{{{{InstanceId}}: {0}, {{Type}}: {1}, {{RegistrationType}}: {2}, {{MessageType}}: {3}}}",
                Id, Type, RegistrationType, MessageType);
        }
    }
}
