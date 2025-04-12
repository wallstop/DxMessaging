namespace DxMessaging.Core
{
    using System;

    /// <summary>
    /// Common base for all Messaging needs. A common base lets us share some implementation details with type safety.
    /// </summary>
    public interface IMessage
    {
        Type MessageType => GetType();

        /// <summary>
        /// Gets the optimized, unique integer ID for this message type, if available.
        /// Returns null if this type uses the fallback mechanism (e.g., due to a compile-time
        /// hash collision or manual implementation without an assigned ID).
        /// </summary>
        /// <remarks>
        /// The ID is generated at compile-time for attributed types and is stable
        /// across builds assuming the type's fully qualified name does not change.
        /// It facilitates faster dictionary lookups compared to using System.Type directly.
        /// Check for HasValue before using the Value.
        /// </remarks>
        int? OptimizedMessageId => null;
    }
}
