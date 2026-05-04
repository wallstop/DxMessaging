namespace DxMessaging.Core.MessageBus.Internal
{
    /// <summary>
    /// Third axis of the dispatch slot key. Captures handler-shape variants that
    /// share a <see cref="DispatchKind"/> but differ in delegate signature or
    /// fast-path eligibility.
    /// </summary>
    /// <remarks>
    /// Encoded into <see cref="SlotKey.Packed"/> in the low three bits (bits
    /// 0-2). Packed value range is 0..3 in current usage; the three-bit slot
    /// leaves room for four additional variants without disturbing the packed
    /// layout.
    /// </remarks>
    internal enum DispatchVariant : byte
    {
        /// <summary>Default handler shape (full delegate signature with context).</summary>
        Default = 0,

        /// <summary>Fast-path handler shape (specialized signature).</summary>
        Fast = 1,

        /// <summary>Handler shape that elides the context argument.</summary>
        WithoutContext = 2,

        /// <summary>Fast-path handler shape that elides the context argument.</summary>
        WithoutContextFast = 3,
    }
}
