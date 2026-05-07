namespace DxMessaging.Core.MessageBus.Internal
{
    /// <summary>
    /// Const-int positions into <c>MessageBus._contextSinks[]</c>. Every
    /// position is populated; there are no reserved slots.
    /// </summary>
    internal static class BusContextIndex
    {
        public const int TargetedHandleDefault = 0;
        public const int BroadcastHandleDefault = 1;
        public const int TargetedPostProcessDefault = 2;
        public const int BroadcastPostProcessDefault = 3;

        public const int Length = 4;
    }
}
