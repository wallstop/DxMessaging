namespace DxMessaging.Editor
{
#if UNITY_EDITOR
    using Core;
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
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            ApplySettingsWithReset();
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange stateChange)
        {
            if (
                stateChange == PlayModeStateChange.EnteredEditMode
                || stateChange == PlayModeStateChange.EnteredPlayMode
            )
            {
                ApplySettingsWithReset();
            }
        }

        private static void ApplySettingsWithReset()
        {
            DxMessagingStaticState.Reset();
            DxMessagingSettings settings = DxMessagingSettings.GetOrCreateSettings();
            IMessageBus.GlobalDiagnosticsMode = settings.EnableDiagnosticsInEditor;
            IMessageBus.GlobalMessageBufferSize = settings.MessageBufferSize;
        }
    }
#endif
}
