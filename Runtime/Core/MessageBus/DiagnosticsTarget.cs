namespace DxMessaging.Core.MessageBus
{
    using System;

    /// <summary>
    /// Flags describing which execution targets should enable diagnostics by default.
    /// </summary>
    [Flags]
    public enum DiagnosticsTarget
    {
        Off = 0,
        Editor = 1,
        Runtime = 2,
        All = Editor | Runtime,
    }
}
