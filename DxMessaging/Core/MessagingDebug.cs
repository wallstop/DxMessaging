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
        /// Logs the 
        /// </summary>
        /// <param name="message"></param>
        public static void Log(string message)
        {
            if (LogFunction == null)
            {
                return;
            }
            InternalLog(message);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="message"></param>
        /// <param name="args"></param>
        public static void Log(string message, params object [] args)
        {
            if (LogFunction == null)
            {
                return;
            }
            InternalLog(string.Format(message, args));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="message"></param>
        private static void InternalLog(string message)
        {
            LogFunction(message);
        }
    }
}
