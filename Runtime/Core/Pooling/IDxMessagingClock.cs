namespace DxMessaging.Core.Pooling
{
    /// <summary>
    /// Abstraction over a monotonic wall-clock used by the eviction sweeper to
    /// decide whether enough time has elapsed since the last sweep. Implementations
    /// must be cheap (single field read or call) because sampled idle-sweep gates
    /// sit on the emit path.
    /// </summary>
    public interface IDxMessagingClock
    {
        /// <summary>
        /// Current time in seconds. Must be non-decreasing within a single
        /// AppDomain. Implementations may have frame-grained or millisecond-grained
        /// resolution; the eviction sweeper tolerates either.
        /// </summary>
        double NowSeconds { get; }
    }
}
