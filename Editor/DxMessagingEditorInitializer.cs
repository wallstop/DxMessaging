namespace DxMessaging.Editor
{
#if UNITY_EDITOR
    using Core.MessageBus;
    using Settings;
    using UnityEditor;

    /// <summary>
    /// Applies DxMessaging Editor settings to global runtime defaults on domain load.
    /// </summary>
    [InitializeOnLoad]
    public static class DxMessagingEditorInitializer
    {
        static DxMessagingEditorInitializer()
        {
            DxMessagingSettings settings = DxMessagingSettings.GetOrCreateSettings();
            IMessageBus.GlobalDiagnosticsMode = settings.EnableDiagnosticsInEditor;
            IMessageBus.GlobalMessageBufferSize = settings.MessageBufferSize;
        }
    }
#endif
}
