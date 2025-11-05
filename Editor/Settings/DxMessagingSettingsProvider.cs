namespace DxMessaging.Editor.Settings
{
#if UNITY_EDITOR
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// Project Settings provider for DxMessaging configuration.
    /// </summary>
    /// <remarks>
    /// Exposes toggles for global diagnostics mode and message buffer size under Project Settings → DxMessaging.
    /// </remarks>
    public sealed class DxMessagingSettingsProvider : SettingsProvider
    {
        private SerializedObject _messagingSettings;

        private DxMessagingSettingsProvider(
            string path,
            SettingsScope scope = SettingsScope.Project
        )
            : base(path, scope) { }

        public override void OnActivate(
            string searchContext,
            UnityEngine.UIElements.VisualElement rootElement
        )
        {
            _messagingSettings = DxMessagingSettings.GetSerializedSettings();
        }

        public override void OnGUI(string searchContext)
        {
            EditorGUILayout.PropertyField(
                _messagingSettings.FindProperty(
                    nameof(DxMessagingSettings._enableDiagnosticsInEditor)
                ),
                new GUIContent(
                    "Global Diagnostics Mode",
                    "When enabled, every new MessageBus records diagnostic history. Recommended only for debugging."
                )
            );
            EditorGUILayout.PropertyField(
                _messagingSettings.FindProperty(nameof(DxMessagingSettings._messageBufferSize)),
                new GUIContent(
                    "Message Buffer Size",
                    "Number of emissions kept per bus/token when diagnostics mode is active."
                )
            );
            EditorGUILayout.PropertyField(
                _messagingSettings.FindProperty(
                    nameof(DxMessagingSettings._suppressDomainReloadWarning)
                ),
                new GUIContent(
                    "Suppress Domain Reload Warning",
                    "Disable the warning shown when Enter Play Mode Options skips domain reload; DxMessaging still resets its statics."
                )
            );

            _messagingSettings.ApplyModifiedProperties();
        }

        [SettingsProvider]
        public static SettingsProvider CreateDxMessagingSettingsProvider()
        {
            DxMessagingSettingsProvider provider = new("Project/DxMessaging")
            {
                keywords = new HashSet<string>(new[] { "DxMessaging", "Diagnostics" }),
            };

            return provider;
        }
    }

#endif
}
