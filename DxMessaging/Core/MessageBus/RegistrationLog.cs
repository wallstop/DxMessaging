using System;
using System.Collections.Generic;
using System.Text;

namespace DxMessaging.Core.MessageBus
{
    /// <summary>
    /// Logs all MessageHandler registrations from the beginning of time.
    /// </summary>
    public sealed class RegistrationLog
    {
        private readonly List<MessagingRegistration> _finalizedRegistrations;

        public RegistrationLog()
        {
            _finalizedRegistrations = new List<MessagingRegistration>();
        }

        /// <summary>
        /// Logs a MessagingRegistration.
        /// </summary>
        /// <param name="registration">MessagingRegistration to record.</param>
        public void Log(MessagingRegistration registration)
        {
            _finalizedRegistrations.Add(registration);
        }

        /// <summary>
        /// Pretty-print all of the logged Messaging registrations using the provided print function.
        /// </summary>
        /// <param name="serializer">Serialization function to use. If null, defaults to MessagingRegistration.ToString.</param>
        /// <returns>The string representing all logged MessagingRegistrations.</returns>
        public string ToString(Func<MessagingRegistration, string> serializer)
        {
            if (_finalizedRegistrations.Count == 0)
            {
                return "[]";
            }

            if (ReferenceEquals(serializer, null))
            {
                serializer = registration => registration.ToString();
            }

            StringBuilder registrations = new StringBuilder("[");
            for (int i = 0; i < _finalizedRegistrations.Count; ++i)
            {
                if (0 < i)
                {
                    registrations.Append(", ");
                }
                MessagingRegistration finalizedRegistration = _finalizedRegistrations[i];
                string prettyFinalizedRegistration = serializer(finalizedRegistration);
                registrations.Append(prettyFinalizedRegistration);
            }
            registrations.Append("]");
            return registrations.ToString();
        }

        public override string ToString()
        {
            return ToString(null);
        }

        /// <summary>
        /// Removes all MessagingRegistrations that satisfy the provided function, or all registrations if no function is provided.
        /// </summary>
        /// <param name="shouldRemove">Null if all MessagingRegistrations should be removed, or a custom function that returns true for any MessagingRegistration that should be removed.</param>
        /// <returns>Number of MessagingRegistrations removed.</returns>
        public int Clear(Predicate<MessagingRegistration> shouldRemove = null)
        {
            if (ReferenceEquals(shouldRemove, null))
            {
                shouldRemove = _ => true;
            }
            return _finalizedRegistrations.RemoveAll(shouldRemove);
        }
    }
}
