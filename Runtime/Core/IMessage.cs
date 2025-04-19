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
        /// Gets the globally unique, sequential, zero-based ID assigned
        /// to this message type during runtime initialization.
        /// Returns -1 if runtime initialization has not completed or failed for this type.
        /// </summary>
        /// <remarks>
        /// This provides extremely fast runtime access to the type's ID
        /// without dictionary lookups.
        /// </remarks>
        [global::System.Runtime.CompilerServices.MethodImpl(
            global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining
        )]
        int GetSequentialId() => -1;
    }
}
