namespace DxMessaging.Core.MessageBus
{
    using System;

    /// <summary>
    /// Defines how existing registrations should react when a token receives a new bus override.
    /// </summary>
    public enum MessageBusRebindMode
    {
        /// <summary>
        /// Legacy default. Prefer specifying an explicit mode.
        /// </summary>
        [Obsolete("Specify PreserveRegistrations or RebindActive to clarify intent.")]
        Unknown = 0,

        /// <summary>
        /// Update the override for future registrations but leave existing registrations on their current bus.
        /// </summary>
        PreserveRegistrations = 1,

        /// <summary>
        /// Rebind active registrations to the supplied bus immediately.
        /// </summary>
        RebindActive = 2,
    }
}
