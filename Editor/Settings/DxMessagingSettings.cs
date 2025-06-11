namespace DxMessaging.Editor.Settings
{
#if UNITY_EDITOR
    using UnityEditor;
    using UnityEngine;

    public sealed class DxMessagingSettings : ScriptableObject
    {
        public const string SettingsPath = "Assets/Editor/DxMessagingSettings.asset";

        [SerializeField]
        internal bool _enableDiagnosticsInEditor;

        [SerializeField]
        internal int _messageBufferSize = 10;

        public bool EnableDiagnosticsInEditor
        {
            get => _enableDiagnosticsInEditor;
            set => _enableDiagnosticsInEditor = value;
        }

        public int MessageBufferSize
        {
            get => _messageBufferSize;
            set => _messageBufferSize = value;
        }

        internal static DxMessagingSettings GetOrCreateSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<DxMessagingSettings>(SettingsPath);
            if (settings == null)
            {
                settings = CreateInstance<DxMessagingSettings>();
                settings._enableDiagnosticsInEditor = false;
                settings._messageBufferSize = 10;
                AssetDatabase.CreateAsset(settings, SettingsPath);
                AssetDatabase.SaveAssets();
            }

            return settings;
        }

        internal static SerializedObject GetSerializedSettings()
        {
            return new SerializedObject(GetOrCreateSettings());
        }
    }
#endif
}
