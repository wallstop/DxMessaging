namespace DxMessaging.Core.Pooling
{
    /// <summary>
    /// Snapshot of a single pool's lifetime counters and current cached count.
    /// All values are point-in-time; reading them does not reset the underlying
    /// counters. Useful for leak-watcher style assertions and for debug overlays.
    /// </summary>
    internal readonly struct CollectionPoolDiagnostics
    {
        /// <summary>Number of entries currently retained by the pool.</summary>
        public readonly int Cached;

        /// <summary>Lifetime count of <c>Rent</c> calls that returned a pooled entry.</summary>
        public readonly long Hits;

        /// <summary>Lifetime count of <c>Rent</c> calls that allocated a fresh entry.</summary>
        public readonly long Misses;

        /// <summary>Lifetime count of pooled entries dropped due to cap or LRU eviction.</summary>
        public readonly long Evictions;

        internal CollectionPoolDiagnostics(int cached, long hits, long misses, long evictions)
        {
            Cached = cached;
            Hits = hits;
            Misses = misses;
            Evictions = evictions;
        }
    }
}
