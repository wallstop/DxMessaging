namespace DxMessaging.Core.Pooling
{
#if UNITY_2021_3_OR_NEWER
    using System.Runtime.CompilerServices;
    using UnityEngine;

    /// <summary>
    /// Unity-only <see cref="IDxMessagingClock"/> backed by
    /// <see cref="Time.realtimeSinceStartupAsDouble"/>. Use this when sweep cadence
    /// should follow Unity wall time rather than the AppDomain Stopwatch (Stopwatch
    /// keeps running across editor pause; Time.realtimeSinceStartupAsDouble also
    /// runs across pause but is the canonical Unity clock).
    /// </summary>
    /// <remarks>
    /// Must be invoked from the Unity main thread; the underlying <c>Time</c>
    /// API throws when called from worker threads.
    /// </remarks>
    public sealed class UnityRealtimeClock : IDxMessagingClock
    {
        /// <summary>Shared instance.</summary>
        public static readonly UnityRealtimeClock Instance = new();

        /// <inheritdoc />
        public double NowSeconds
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Time.realtimeSinceStartupAsDouble;
        }
    }
#endif
}
