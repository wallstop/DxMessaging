namespace DxMessaging.Core
{
    using System;
    using Helper;
    using MessageBus;

    /// <summary>
    /// Centralised utility for resetting DxMessaging static state when Domain Reload is disabled.
    /// </summary>
    public static class DxMessagingStaticState
    {
        private static readonly object ResetLock = new object();
        private static readonly BaselineState Baseline;

        static DxMessagingStaticState()
        {
            Baseline = CaptureBaseline();
        }

        /// <summary>
        /// Resets all static variables in DxMessaging to their default values.
        /// </summary>
        public static void Reset()
        {
            lock (ResetLock)
            {
                MessagingDebug.enabled = Baseline.MessagingDebugEnabled;
                MessagingDebug.LogFunction = Baseline.MessagingDebugLogFunction;

                IMessageBus.GlobalDiagnosticsTargets = Baseline.GlobalDiagnosticsTargets;
                IMessageBus.GlobalMessageBufferSize = Baseline.GlobalMessageBufferSize;
                IMessageBus.GlobalSequentialIndex = Baseline.GlobalSequentialIndex;

                MessageHelperIndexer.RestoreState(Baseline.HelperState);

                MessageRegistrationHandle.SetIdSeed(Baseline.MessageRegistrationHandleSeed);
                MessageRegistrationBuilder.SetSyntheticOwnerCounter(Baseline.SyntheticOwnerCounter);

                MessageHandler.ResetStatics();
                IMessageBus.GlobalSequentialIndex = Baseline.GlobalSequentialIndex;
            }
        }

        private static BaselineState CaptureBaseline()
        {
            bool messagingDebugEnabled = MessagingDebug.enabled;
            Action<LogLevel, string> messagingDebugLogFunction = MessagingDebug.LogFunction;
            DiagnosticsTarget globalDiagnosticsTargets = IMessageBus.GlobalDiagnosticsTargets;
            int globalMessageBufferSize = IMessageBus.GlobalMessageBufferSize;
            int globalSequentialIndex = IMessageBus.GlobalSequentialIndex;
            long messageRegistrationHandleSeed = MessageRegistrationHandle.GetCurrentIdSeed();
            int syntheticOwnerCounter = MessageRegistrationBuilder.GetSyntheticOwnerCounter();
            MessageHelperIndexer.MessageHelperIndexerState helperState =
                MessageHelperIndexer.CaptureState();

            return new BaselineState(
                messagingDebugEnabled,
                messagingDebugLogFunction,
                globalDiagnosticsTargets,
                globalMessageBufferSize,
                globalSequentialIndex,
                messageRegistrationHandleSeed,
                syntheticOwnerCounter,
                helperState
            );
        }

        private sealed class BaselineState
        {
            internal BaselineState(
                bool messagingDebugEnabled,
                Action<LogLevel, string> messagingDebugLogFunction,
                DiagnosticsTarget globalDiagnosticsTargets,
                int globalMessageBufferSize,
                int globalSequentialIndex,
                long messageRegistrationHandleSeed,
                int syntheticOwnerCounter,
                MessageHelperIndexer.MessageHelperIndexerState helperState
            )
            {
                MessagingDebugEnabled = messagingDebugEnabled;
                MessagingDebugLogFunction = messagingDebugLogFunction;
                GlobalDiagnosticsTargets = globalDiagnosticsTargets;
                GlobalMessageBufferSize = globalMessageBufferSize;
                GlobalSequentialIndex = globalSequentialIndex;
                MessageRegistrationHandleSeed = messageRegistrationHandleSeed;
                SyntheticOwnerCounter = syntheticOwnerCounter;
                HelperState = helperState;
            }

            internal bool MessagingDebugEnabled { get; }

            internal Action<LogLevel, string> MessagingDebugLogFunction { get; }

            internal DiagnosticsTarget GlobalDiagnosticsTargets { get; }

            internal int GlobalMessageBufferSize { get; }

            internal int GlobalSequentialIndex { get; }

            internal long MessageRegistrationHandleSeed { get; }

            internal int SyntheticOwnerCounter { get; }

            internal MessageHelperIndexer.MessageHelperIndexerState HelperState { get; }
        }
    }
}
