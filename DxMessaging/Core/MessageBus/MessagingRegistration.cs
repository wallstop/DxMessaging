namespace DxMessaging.Core.MessageBus
{
    using System;
    using System.Runtime.Serialization;

    /// <summary>
    /// How the registration was performed.
    /// </summary>
    public enum RegistrationType
    {
        Unknown,
        Register,
        Deregister
    }

    /// <summary>
    /// Exact method of MessagingRegistration.
    /// </summary>
    public enum RegistrationMethod
    {
        Unknown,
        Targeted,
        Untargeted,
        TargetedWithoutTargeting,
        GlobalAcceptAll
    }

    /// <summary>
    /// Holds relevant information about a MessagingRegistration event
    /// </summary>
    [Serializable]
    [DataContract]
    public readonly struct MessagingRegistration
    {
        /// <summary>
        /// Id of the MessageHandler that was registered.
        /// </summary>
        public readonly InstanceId Id;
        /// <summary>
        /// Name of the type of Message that was registered for.
        /// </summary>
        public readonly string Type;
        /// <summary>
        /// New registration? De-registration?
        /// </summary>
        public readonly RegistrationType RegistrationType;
        /// <summary>
        /// Exact method of the registration.
        /// </summary>
        public readonly RegistrationMethod RegistrationMethod;

        // TODO: Time

        /// <inheritdoc />
        /// <summary>
        /// Creates a MessagingRegistration with all of the relevant tidbits filled out.
        /// </summary>
        /// <param name="id">Id of the MessageHandler.</param>
        /// <param name="type">Type of Message.</param>
        /// <param name="registrationType">Register? Deregister?</param>
        /// <param name="registrationMethod">How the Message was chosen to be listened for.</param>
        public MessagingRegistration(InstanceId id, Type type, RegistrationType registrationType, RegistrationMethod registrationMethod)
            : this(id, type.Name, registrationType, registrationMethod)
        {
        }

        /// <summary>
        /// Creates a MessagingRegistration with all of the relevant tidbits filled out.
        /// </summary>
        /// <param name="id">Id of the MessageHandler.</param>
        /// <param name="typeName">TypeName of Message.</param>
        /// <param name="registrationType">Register? Deregister?</param>
        /// <param name="registrationMethod">How the Message was chosen to be listened for.</param>
        public MessagingRegistration(InstanceId id, string typeName, RegistrationType registrationType,
            RegistrationMethod registrationMethod) : this()
        {
            Id = id;
            Type = typeName;
            RegistrationType = registrationType;
            RegistrationMethod = registrationMethod;
        }

        public override string ToString()
        {
            // Poor man's JSON
            return $"{{{{InstanceId}}: {Id}, {{Type}}: {Type}, {{RegistrationType}}: {RegistrationType}, {{RegistrationMethod}}: {RegistrationMethod}}}";
        }
    }
}
