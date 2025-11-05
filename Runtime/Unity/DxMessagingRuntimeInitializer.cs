#if UNITY_2021_3_OR_NEWER
namespace DxMessaging.Unity
{
    using Core;
    using UnityEngine;

    /// <summary>
    /// Unity-specific hook that resets DxMessaging static state when domain reloads are skipped.
    /// </summary>
    internal static class DxMessagingRuntimeInitializer
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            DxMessagingStaticState.Reset();
        }
    }
}
#endif
