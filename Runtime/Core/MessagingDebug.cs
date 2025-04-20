namespace DxMessaging.Core
{
    using System;

    /// <summary>
    /// Severity of the log message
    /// </summary>
    public enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Warn = 2,
        Error = 3,
    }

    /// <summary>
    /// Debug functionality for all the Messaging Components.
    /// </summary>
    public static class MessagingDebug
    {
        public static bool enabled = false;

        /// <summary>
        /// Custom log function to use.
        /// </summary>
        /// <note>
        /// For Unity, you could do LogFunction = Debug.Log;
        /// </note>
        public static Action<LogLevel, string> LogFunction = null;

        /// <summary>
        /// Logs a message to the debug log function if it's not null.
        /// </summary>
        /// <param name="logLevel">Severity of the message.</param>
        /// <param name="message">Message to log.</param>
        public static void Log(LogLevel logLevel, string message)
        {
            LogFunction?.Invoke(logLevel, message);
        }

        /// <summary>
        /// Logs a format string + args to the debug log function, if it's not null.
        /// </summary>
        /// <note>
        /// Will call string.Format(message, args)
        /// </note>
        /// <param name="logLevel">Severity of the message.</param>
        /// <param name="message">Format string.</param>
        /// <param name="args">Args to populate the format string with.</param>
        public static void Log(LogLevel logLevel, string message, params object[] args)
        {
            if (!enabled)
            {
                return;
            }

            Action<LogLevel, string> logFunction = LogFunction;
            /*
                We can potentially avoid an unnecessary string.Format call if the LogFunction is null,
                which is why the null check is outside the InternalLog function.
            */
            if (logFunction == null)
            {
                return;
            }

            if (args.Length <= 0)
            {
                logFunction.Invoke(logLevel, message);
            }
            else
            {
                logFunction.Invoke(logLevel, string.Format(message, args));
            }
        }
    }
}
