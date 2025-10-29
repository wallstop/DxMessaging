namespace DxMessaging.Core.Diagnostics
{
    using System;
#if UNITY_2021_3_OR_NEWER
    using UnityEngine;
#else
    using System.Diagnostics;
#endif

    /// <summary>
    /// Captures a snapshot of a message emission for diagnostics.
    /// </summary>
    /// <remarks>
    /// When diagnostics are enabled (see <see cref="MessageBus.IMessageBus.GlobalDiagnosticsMode"/>),
    /// the bus and tokens record recent emissions in ring buffers along with a trimmed stack trace
    /// that excludes DxMessaging internals for easier debugging.
    ///
    /// The <see cref="context"/> contains the relevant <see cref="InstanceId"/> for targeted/broadcast messages
    /// (target or source respectively) and is null for untargeted messages.
    /// </remarks>
    public readonly struct MessageEmissionData
    {
        private static readonly string[] NewlineSeparators = { "\r\n", "\n", "\r" };
        private static readonly string JoinSeparator = Environment.NewLine;

        /// <summary>Emitted message payload.</summary>
        public readonly IMessage message;

        /// <summary>Relevant context (target/source) for the emission; null for untargeted.</summary>
        public readonly InstanceId? context;

        /// <summary>Trimmed stack trace captured at the emission site.</summary>
        public readonly string stackTrace;

        /// <summary>
        /// Creates a new diagnostic record for an emitted message.
        /// </summary>
        /// <param name="message">The message that was emitted.</param>
        /// <param name="context">Target or source depending on message category; null for untargeted.</param>
        public MessageEmissionData(IMessage message, InstanceId? context = null)
        {
            this.message = message;
            this.context = context;
            stackTrace = GetAccurateStackTrace();
        }

        private static string GetAccurateStackTrace()
        {
            string fullStackTrace;
#if UNITY_2021_3_OR_NEWER
            fullStackTrace = StackTraceUtility.ExtractStackTrace();
#else
            fullStackTrace = new StackTrace(true).ToString();
#endif
            if (string.IsNullOrWhiteSpace(fullStackTrace))
            {
                return fullStackTrace;
            }

            string[] lines = fullStackTrace.Split(NewlineSeparators, StringSplitOptions.None);

            int startIndex = 1;
            while (
                startIndex < lines.Length
                && lines[startIndex].Contains("DxMessaging", StringComparison.OrdinalIgnoreCase)
            )
            {
                ++startIndex;
            }

            return lines.Length <= startIndex
                ? string.Empty
                : string.Join(JoinSeparator, lines, startIndex, lines.Length - startIndex);
        }
    }
}
