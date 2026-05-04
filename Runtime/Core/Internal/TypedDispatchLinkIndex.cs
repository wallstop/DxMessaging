namespace DxMessaging.Core.Internal
{
    /// <summary>
    /// Const-int positions into <c>TypedHandler&lt;T&gt;._dispatchLinks[]</c>.
    /// Indices are hand-written so call sites inline as immediate operands.
    /// Array length and per-index <c>null</c>-ness are validated in DEBUG
    /// builds via <c>TypedHandler&lt;T&gt;.ValidateSlotArrays()</c>.
    /// </summary>
    /// <remarks>
    /// Positions are laid out in lex-(<c>Kind</c>, <c>Phase</c>, <c>Variant</c>) order:
    /// Untargeted -&gt; Targeted -&gt; Broadcast within Kind, Handle before
    /// PostProcess within Phase, and with-context before WithoutContext
    /// within Variant. The xmldoc on each constant names the legacy
    /// <c>TypedHandler&lt;T&gt;</c> dispatch-link field whose storage role
    /// the slot will assume in the P3.3 storage migration.
    /// </remarks>
    internal static class TypedDispatchLinkIndex
    {
        /// <summary>Legacy field: <c>_untargetedLink</c>.</summary>
        public const int UntargetedHandle = 0;

        /// <summary>Legacy field: <c>_untargetedPostLink</c>.</summary>
        public const int UntargetedPostProcess = 1;

        /// <summary>Legacy field: <c>_targetedLink</c>.</summary>
        public const int TargetedHandle = 2;

        /// <summary>Legacy field: <c>_targetedWithoutTargetingLink</c>.</summary>
        public const int TargetedHandleWithoutContext = 3;

        /// <summary>Legacy field: <c>_targetedPostLink</c>.</summary>
        public const int TargetedPostProcess = 4;

        /// <summary>Legacy field: <c>_targetedWithoutTargetingPostLink</c>.</summary>
        public const int TargetedPostProcessWithoutContext = 5;

        /// <summary>Legacy field: <c>_broadcastLink</c>.</summary>
        public const int BroadcastHandle = 6;

        /// <summary>Legacy field: <c>_broadcastWithoutSourceLink</c>.</summary>
        public const int BroadcastHandleWithoutContext = 7;

        /// <summary>Legacy field: <c>_broadcastPostLink</c>.</summary>
        public const int BroadcastPostProcess = 8;

        /// <summary>Legacy field: <c>_broadcastWithoutSourcePostLink</c>.</summary>
        public const int BroadcastPostProcessWithoutContext = 9;

        public const int Length = 10;
    }
}
