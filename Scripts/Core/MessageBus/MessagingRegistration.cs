﻿namespace DxMessaging.Core.MessageBus
{
    using System;
    using System.Runtime.Serialization;

    /// <summary>
    /// How the registration was performed.
    /// </summary>
    public enum RegistrationType
    {
        Register,
        Deregister
    }

    /// <summary>
    /// Exact method of MessagingRegistration.
    /// </summary>
    public enum RegistrationMethod
    {
        Targeted,
        Untargeted,
        Broadcast,
        BroadcastWithoutSource,
        TargetedWithoutTargeting,
        GlobalAcceptAll,
        Interceptor,
        UntargetedPostProcessor,
        TargetedPostProcessor,
        BroadcastPostProcessor,
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
        public readonly InstanceId id;
        /// <summary>
        /// Name of the type of Message that was registered for.
        /// </summary>
        public readonly Type type;
        /// <summary>
        /// New registration? De-registration?
        /// </summary>
        public readonly RegistrationType registrationType;
        /// <summary>
        /// Exact method of the registration.
        /// </summary>
        public readonly RegistrationMethod registrationMethod;

        // TODO: Time

        /// <summary>
        /// Creates a MessagingRegistration with all of the relevant tidbits filled out.
        /// </summary>
        /// <param name="id">Id of the MessageHandler.</param>
        /// <param name="type">Type of Message.</param>
        /// <param name="registrationType">Register? Deregister?</param>
        /// <param name="registrationMethod">How the Message was chosen to be listened for.</param>
        public MessagingRegistration(InstanceId id, Type type, RegistrationType registrationType, RegistrationMethod registrationMethod)
        {
            this.id = id;
            this.type = type;
            this.registrationType = registrationType;
            this.registrationMethod = registrationMethod;
        }

        public override string ToString()
        {
            return new
            {
                id,
                type, registrationType,
                registrationMethod
            }.ToString();
        }
    }
}