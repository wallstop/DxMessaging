using System;

namespace DxMessaging.Core
{
    /// <summary>
    /// Debug functionality for all of the Messaging Components.
    /// </summary>
    public static class MessagingDebug
    {
        /// <summary>
        /// Custom log function to use.
        /// </summary>
        /// <note>
        /// For Unity, you could do LogFunction = Debug.Log;
        /// </note>
        public static Action<string> LogFunction = null;

        /// <summary>
        /// Logs a message to the debug log function, if it's not null.
        /// </summary>
        /// <param name="message">Message to log.</param>
        public static void Log(string message)
        {
            if (ReferenceEquals(LogFunction, null))
            {
                return;
            }
            InternalLog(message);
        }

        /// <summary>
        /// Logs a format string + args to the debug log function, if it's not null.
        /// </summary>
        /// <note>
        /// Will call string.Format(message, args)
        /// </note>
        /// <param name="message">Format string.</param>
        /// <param name="args">Args to populate format string with.</param>
        public static void Log(string message, params object [] args)
        {
            if (ReferenceEquals(LogFunction, null))
            {
                return;
            }
            /*
                We can potentially avoid an unecessary string.Format call if the LogFunction is null,
                which is why the null check is outside the InternalLog function.
            */
            InternalLog(string.Format(message, args));
        }

        /// <summary>
        /// Actually handles logging.
        /// </summary>
        /// <param name="message">Message to log.</param>
        private static void InternalLog(string message)
        {
            LogFunction(message);
        }
    }
}
