namespace DxMessaging.Core.MessageBus.Internal
{
    /// <summary>
    /// Marker interface for bus-level slot containers that the eviction layer
    /// can sweep. Each slot tracks its last-touch tick so the sweep can decide
    /// whether to reclaim it, and exposes a monotonic
    /// <see cref="Version"/> so that staged dispatch closures captured before
    /// eviction can detect they have been invalidated.
    /// </summary>
    /// <remarks>
    /// <see cref="Reset"/> returns inner pooled collections to
    /// <c>DxMessaging.Core.Pooling.DxPools</c>. The sweep policy calls
    /// <see cref="Reset"/> on idle empty slots.
    /// </remarks>
    internal interface IEvictableSlot
    {
        /// <summary>
        /// The bus tick counter value at the most recent register / deregister /
        /// emit operation that touched this slot. Used by the sweep to decide
        /// whether the slot has been idle for long enough to evict.
        /// </summary>
        long LastTouchTicks { get; }

        /// <summary>
        /// True iff the slot currently retains zero live registrations. Cheap
        /// (single integer compare); maintained at register / deregister sites.
        /// Stale-but-non-empty slots are NOT eviction candidates -- only
        /// idle AND empty slots are reclaimed.
        /// </summary>
        bool IsEmpty { get; }

        /// <summary>
        /// Strictly monotonic version counter. Bumped by <see cref="Reset"/>
        /// (and by registration-time mutations on the slot). Allows captured
        /// dispatch closures to detect post-eviction invalidation.
        /// </summary>
        long Version { get; }

        /// <summary>
        /// Reclaim this slot: clear inner state, return any pooled inner
        /// collections to <c>DxPools</c>, and bump <see cref="Version"/>.
        /// Idempotent. <see cref="LastTouchTicks"/> is intentionally preserved
        /// across <see cref="Reset"/> so the sweep can distinguish freshly-reset
        /// slots from never-touched ones.
        /// </summary>
        void Reset();
    }
}
