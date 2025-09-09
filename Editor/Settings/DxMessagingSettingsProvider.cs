namespace DxMessaging.Editor.Settings
{
#if UNITY_EDITOR
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;

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
                new GUIContent("Global Diagnostics Mode")
            );
            EditorGUILayout.PropertyField(
                _messagingSettings.FindProperty(nameof(DxMessagingSettings._messageBufferSize)),
                new GUIContent("Message Buffer Size")
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
