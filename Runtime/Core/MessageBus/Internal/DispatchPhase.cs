namespace DxMessaging.Core.MessageBus.Internal
{
    /// <summary>
    /// Second axis of the dispatch slot key. Distinguishes the two phases of
    /// message processing: the primary handle phase and the post-process phase
    /// that runs after every primary handler has returned.
    /// </summary>
    /// <remarks>
    /// Encoded into <see cref="SlotKey.Packed"/> at bit 3. Packed value range is
    /// 0..1.
    /// </remarks>
    internal enum DispatchPhase : byte
    {
        /// <summary>Primary handler phase.</summary>
        Handle = 0,

        /// <summary>Post-processor phase. Runs after the handle phase completes.</summary>
        PostProcess = 1,
    }
}
