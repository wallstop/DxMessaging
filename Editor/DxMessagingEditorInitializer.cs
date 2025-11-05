namespace DxMessaging.Editor
{
#if UNITY_EDITOR
    using Core;
    using Core.MessageBus;
    using Settings;
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// Applies DxMessaging Editor settings to global runtime defaults on domain load.
    /// </summary>
    [InitializeOnLoad]
    public static class DxMessagingEditorInitializer
    {
        private static bool s_playModeWarningIssued;

        static DxMessagingEditorInitializer()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            ApplyEditorSettings();
            WarnIfDomainReloadDisabled();
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange stateChange)
        {
            if (
                stateChange == PlayModeStateChange.EnteredEditMode
                || stateChange == PlayModeStateChange.EnteredPlayMode
            )
            {
                ApplyEditorSettings();
                WarnIfDomainReloadDisabled();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ApplySettingsBeforeSceneLoad()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            ApplyEditorSettings();
        }

        private static void ApplyEditorSettings()
        {
            DxMessagingStaticState.Reset();
            DxMessagingSettings settings = DxMessagingSettings.GetOrCreateSettings();
            IMessageBus.GlobalDiagnosticsMode = settings.EnableDiagnosticsInEditor;
            IMessageBus.GlobalMessageBufferSize = settings.MessageBufferSize;
        }

        private static void WarnIfDomainReloadDisabled()
        {
            DxMessagingSettings settings = DxMessagingSettings.GetOrCreateSettings();
            if (
                s_playModeWarningIssued
                || settings.SuppressDomainReloadWarning
                || !EditorSettings.enterPlayModeOptionsEnabled
                || (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableDomainReload)
                    == 0
            )
            {
                return;
            }

            s_playModeWarningIssued = true;
            Debug.LogWarning(
                "[DxMessaging] Enter Play Mode Options are disabling domain reload. "
                    + "DxMessaging resets its internal statics, but third-party static state will persist. "
                    + "Audit integration code or re-enable domain reload if inconsistent behaviour occurs."
            );
        }
    }
#endif
}
