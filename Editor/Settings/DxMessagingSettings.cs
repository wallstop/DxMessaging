namespace DxMessaging.Editor.Settings
{
#if UNITY_EDITOR
    using System.Linq;
    using UnityEditor;
    using UnityEngine;

    public sealed class DxMessagingSettings : ScriptableObject
    {
        private const int DefaultBufferSize = 100;
        private const string SettingsPath = "Assets/Editor/DxMessagingSettings.asset";

        [SerializeField]
        internal bool _enableDiagnosticsInEditor;

        [SerializeField]
        internal int _messageBufferSize = DefaultBufferSize;

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
            DxMessagingSettings settings = AssetDatabase.LoadAssetAtPath<DxMessagingSettings>(
                SettingsPath
            );

            if (settings == null)
            {
                settings = AssetDatabase
                    .FindAssets($"t:{nameof(DxMessagingSettings)}")
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Select(AssetDatabase.LoadAssetAtPath<DxMessagingSettings>)
                    .FirstOrDefault(asset => asset != null);
            }

            if (settings == null)
            {
                settings = CreateInstance<DxMessagingSettings>();
                settings._enableDiagnosticsInEditor = false;
                settings._messageBufferSize = DefaultBufferSize;
                if (!AssetDatabase.IsValidFolder("Assets/Editor"))
                {
                    AssetDatabase.CreateFolder("Assets", "Editor");
                }
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
