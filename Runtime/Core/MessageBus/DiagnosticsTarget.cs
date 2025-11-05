namespace DxMessaging.Core.MessageBus
{
    using System;

    /// <summary>
    /// Flags describing which execution targets should enable diagnostics by default.
    /// </summary>
    [Flags]
    public enum DiagnosticsTarget
    {
        /// <summary>
        /// Diagnostics are disabled.
        /// </summary>
        Off = 0,

        /// <summary>
        /// Diagnostics should run while in the Unity editor.
        /// </summary>
        Editor = 1,

        /// <summary>
        /// Diagnostics should run while in player/runtime builds.
        /// </summary>
        Runtime = 2,

        /// <summary>
        /// Diagnostics should run in both editor and runtime environments.
        /// </summary>
        All = Editor | Runtime,
    }
}
