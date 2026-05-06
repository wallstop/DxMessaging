namespace DxMessaging.Core.Internal
{
    /// <summary>
    /// Const-int positions into <c>TypedHandler&lt;T&gt;._slots[]</c>. Indices
    /// are hand-written so call sites inline as immediate operands. Array
    /// length and per-index <c>null</c>-ness are validated in DEBUG builds via
    /// <c>TypedHandler&lt;T&gt;.ValidateSlotArrays()</c>.
    /// </summary>
    /// <remarks>
    /// Positions are laid out in lex-(<c>Kind</c>, <c>Phase</c>, <c>Variant</c>) order:
    /// Untargeted -&gt; Targeted -&gt; Broadcast within Kind; Handle before
    /// PostProcess within Phase; and Default -&gt; Fast -&gt; WithoutContext -&gt;
    /// WithoutContextFast within Variant. The xmldoc on each constant names
    /// the legacy <c>TypedHandler&lt;T&gt;</c> field whose storage role the
    /// slot assumes in typed storage.
    /// </remarks>
    internal static class TypedSlotIndex
    {
        /// <summary>Legacy field: <c>_untargetedHandlers</c>.</summary>
        public const int UntargetedHandleDefault = 0;

        /// <summary>Legacy field: <c>_untargetedFastHandlers</c>.</summary>
        public const int UntargetedHandleFast = 1;

        /// <summary>Legacy field: <c>_untargetedPostProcessingHandlers</c>.</summary>
        public const int UntargetedPostProcessDefault = 2;

        /// <summary>Legacy field: <c>_untargetedPostProcessingFastHandlers</c>.</summary>
        public const int UntargetedPostProcessFast = 3;

        /// <summary>Legacy field: <c>_targetedHandlers</c>.</summary>
        public const int TargetedHandleDefault = 4;

        /// <summary>Legacy field: <c>_targetedFastHandlers</c>.</summary>
        public const int TargetedHandleFast = 5;

        /// <summary>Legacy field: <c>_targetedWithoutTargetingHandlers</c>.</summary>
        public const int TargetedHandleWithoutContext = 6;

        /// <summary>Legacy field: <c>_fastTargetedWithoutTargetingHandlers</c>.</summary>
        public const int TargetedHandleWithoutContextFast = 7;

        /// <summary>Legacy field: <c>_targetedPostProcessingHandlers</c>.</summary>
        public const int TargetedPostProcessDefault = 8;

        /// <summary>Legacy field: <c>_targetedPostProcessingFastHandlers</c>.</summary>
        public const int TargetedPostProcessFast = 9;

        /// <summary>Legacy field: <c>_targetedWithoutTargetingPostProcessingHandlers</c>.</summary>
        public const int TargetedPostProcessWithoutContext = 10;

        /// <summary>Legacy field: <c>_fastTargetedWithoutTargetingPostProcessingHandlers</c>.</summary>
        public const int TargetedPostProcessWithoutContextFast = 11;

        /// <summary>Legacy field: <c>_broadcastHandlers</c>.</summary>
        public const int BroadcastHandleDefault = 12;

        /// <summary>Legacy field: <c>_broadcastFastHandlers</c>.</summary>
        public const int BroadcastHandleFast = 13;

        /// <summary>Legacy field: <c>_broadcastWithoutSourceHandlers</c>.</summary>
        public const int BroadcastHandleWithoutContext = 14;

        /// <summary>Legacy field: <c>_fastBroadcastWithoutSourceHandlers</c>.</summary>
        public const int BroadcastHandleWithoutContextFast = 15;

        /// <summary>Legacy field: <c>_broadcastPostProcessingHandlers</c>.</summary>
        public const int BroadcastPostProcessDefault = 16;

        /// <summary>Legacy field: <c>_broadcastPostProcessingFastHandlers</c>.</summary>
        public const int BroadcastPostProcessFast = 17;

        /// <summary>Legacy field: <c>_broadcastWithoutSourcePostProcessingHandlers</c>.</summary>
        public const int BroadcastPostProcessWithoutContext = 18;

        /// <summary>Legacy field: <c>_fastBroadcastWithoutSourcePostProcessingHandlers</c>.</summary>
        public const int BroadcastPostProcessWithoutContextFast = 19;

        public const int Length = 20;
    }
}
