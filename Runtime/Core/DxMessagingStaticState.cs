namespace DxMessaging.Core
{
    using System;
    using MessageBus;

    /// <summary>
    /// Centralised utility for resetting DxMessaging static state when Domain Reload is disabled.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class is designed for Unity's Enter Play Mode Settings with Domain Reload disabled.
    /// When Domain Reload is disabled, static fields persist between play mode sessions, which
    /// can cause issues if stale state is not cleared.
    /// </para>
    /// <para>
    /// <strong>Important:</strong> Message type sequential IDs (managed by MessageHelperIndexer)
    /// are intentionally NOT reset. Once a message type is assigned an ID, it retains that ID
    /// for the lifetime of the application domain. This prevents ID collisions that would occur
    /// if a new message type were assigned an ID that was previously used by a different type.
    /// Resetting IDs could cause messages to be routed to the wrong handlers.
    /// </para>
    /// </remarks>
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
        /// <remarks>
        /// Message type IDs are NOT reset by this method. See the class remarks for details.
        /// </remarks>
        public static void Reset()
        {
            lock (ResetLock)
            {
                MessagingDebug.enabled = Baseline.MessagingDebugEnabled;
                MessagingDebug.LogFunction = Baseline.MessagingDebugLogFunction;

                IMessageBus.GlobalDiagnosticsTargets = Baseline.GlobalDiagnosticsTargets;
                IMessageBus.GlobalMessageBufferSize = Baseline.GlobalMessageBufferSize;
                IMessageBus.GlobalSequentialIndex = Baseline.GlobalSequentialIndex;

                MessageRegistrationHandle.SetIdSeed(Baseline.MessageRegistrationHandleSeed);
                MessageRegistrationBuilder.SetSyntheticOwnerCounter(Baseline.SyntheticOwnerCounter);

                MessageHandler.ResetStatics();
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

            return new BaselineState(
                messagingDebugEnabled,
                messagingDebugLogFunction,
                globalDiagnosticsTargets,
                globalMessageBufferSize,
                globalSequentialIndex,
                messageRegistrationHandleSeed,
                syntheticOwnerCounter
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
                int syntheticOwnerCounter
            )
            {
                MessagingDebugEnabled = messagingDebugEnabled;
                MessagingDebugLogFunction = messagingDebugLogFunction;
                GlobalDiagnosticsTargets = globalDiagnosticsTargets;
                GlobalMessageBufferSize = globalMessageBufferSize;
                GlobalSequentialIndex = globalSequentialIndex;
                MessageRegistrationHandleSeed = messageRegistrationHandleSeed;
                SyntheticOwnerCounter = syntheticOwnerCounter;
            }

            internal bool MessagingDebugEnabled { get; }

            internal Action<LogLevel, string> MessagingDebugLogFunction { get; }

            internal DiagnosticsTarget GlobalDiagnosticsTargets { get; }

            internal int GlobalMessageBufferSize { get; }

            internal int GlobalSequentialIndex { get; }

            internal long MessageRegistrationHandleSeed { get; }

            internal int SyntheticOwnerCounter { get; }
        }
    }
}
