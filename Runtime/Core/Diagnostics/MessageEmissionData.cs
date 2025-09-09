namespace DxMessaging.Core.Diagnostics
{
    using System;
#if UNITY_2017_1_OR_NEWER
    using UnityEngine;
#endif

    public readonly struct MessageEmissionData
    {
        private static readonly string[] NewlineSeparators = { "\r\n", "\n", "\r" };
        private static readonly string JoinSeparator = Environment.NewLine;

        public readonly IMessage message;
        public readonly InstanceId? context;
        public readonly string stackTrace;

        public MessageEmissionData(IMessage message, InstanceId? context = null)
        {
            this.message = message;
            this.context = context;
            stackTrace = GetAccurateStackTrace();
        }

        private static string GetAccurateStackTrace()
        {
            string fullStackTrace;
#if UNITY_2017_1_OR_NEWER
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
