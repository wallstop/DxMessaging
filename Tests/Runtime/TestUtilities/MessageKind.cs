#if UNITY_2021_3_OR_NEWER
namespace DxMessaging.Tests.Runtime
{
    /// <summary>
    /// Identifies one of the three DxMessaging dispatch categories. Used by the
    /// parameterized test harness so a single test method can cover all kinds.
    /// </summary>
    public enum MessageKind
    {
        Untargeted,
        Targeted,
        Broadcast,
    }
}
#endif
