using System;
using System.Collections.Generic;
using System.Text;

namespace DxMessaging.Core.MessageBus
{
    /**
        <summary>
            Logs all MessageHandler registrations from the beginning of time 
        </summary>
    */
    public sealed class RegistrationLog
    {
        private readonly List<MessagingRegistration> _finalizedRegistrations;

        public RegistrationLog()
        {
            _finalizedRegistrations = new List<MessagingRegistration>();
        }

        public void Log(MessagingRegistration registration)
        {
            _finalizedRegistrations.Add(registration);
        }

        public string ToString(Func<MessagingRegistration, string> serializer = null)
        {
            if (_finalizedRegistrations.Count == 0)
            {
                return "[]";
            }

            if (serializer == null)
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

        /**
            <summary>
                Removes all MessagingRegistrations that satisfy the provided function, or all registrations if no function is provided.
            </summary>
            <returns>
                Number of registrations removed.
            </returns>
        */
        public int Clear(Predicate<MessagingRegistration> shouldRemove = null)
        {
            if (shouldRemove == null)
            {
                shouldRemove = _ => true;
            }
            return _finalizedRegistrations.RemoveAll(shouldRemove);
        }
    }
}
