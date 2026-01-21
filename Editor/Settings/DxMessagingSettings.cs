namespace DxMessaging.Editor.Settings
{
#if UNITY_EDITOR
    using System.Linq;
    using Core.MessageBus;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.Serialization;

    /// <summary>
    /// Project-wide DxMessaging settings asset (Editor-only).
    /// </summary>
    /// <remarks>
    /// Stored at <c>Assets/Editor/DxMessagingSettings.asset</c>. Controls global diagnostics defaults applied in the
    /// editor through <see cref="DxMessaging.Editor.DxMessagingEditorInitializer"/>.
    /// </remarks>
    public sealed class DxMessagingSettings : ScriptableObject
    {
        private const string SettingsPath = "Assets/Editor/DxMessagingSettings.asset";

        [SerializeField]
        internal DiagnosticsTarget _diagnosticsTargets = DiagnosticsTarget.Off;

        [SerializeField]
        [HideInInspector]
        [FormerlySerializedAs("_enableDiagnosticsInEditor")]
        private bool _legacyEnableDiagnosticsInEditor;

        [SerializeField]
        internal int _messageBufferSize = IMessageBus.DefaultMessageBufferSize;

        [SerializeField]
        internal bool _suppressDomainReloadWarning = true;

        /// <summary>
        /// Controls <see cref="DiagnosticsTarget"/> values applied to <see cref="IMessageBus.GlobalDiagnosticsTargets"/>.
        /// </summary>
        public DiagnosticsTarget DiagnosticsTargets
        {
            get => _diagnosticsTargets;
            set => _diagnosticsTargets = value;
        }

        /// <summary>
        /// Sets <see cref="Core.MessageBus.IMessageBus.GlobalMessageBufferSize"/> for Editor sessions.
        /// </summary>
        public int MessageBufferSize
        {
            get => _messageBufferSize;
            set => _messageBufferSize = value;
        }

        /// <summary>
        /// When true, suppresses the Enter Play Mode Options domain reload warning in the Editor.
        /// </summary>
        public bool SuppressDomainReloadWarning
        {
            get => _suppressDomainReloadWarning;
            set => _suppressDomainReloadWarning = value;
        }

        /// <summary>
        /// Loads the settings asset if present, otherwise creates it with sensible defaults.
        /// </summary>
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
                settings._diagnosticsTargets = DiagnosticsTarget.Off;
                settings._messageBufferSize = IMessageBus.DefaultMessageBufferSize;
                settings._suppressDomainReloadWarning = true;
                if (!AssetDatabase.IsValidFolder("Assets/Editor"))
                {
                    AssetDatabase.CreateFolder("Assets", "Editor");
                }
                AssetDatabase.CreateAsset(settings, SettingsPath);
                AssetDatabase.SaveAssets();
            }

            if (
                settings._diagnosticsTargets == DiagnosticsTarget.Off
                && settings._legacyEnableDiagnosticsInEditor
            )
            {
                settings._diagnosticsTargets = DiagnosticsTarget.Editor;
                settings._legacyEnableDiagnosticsInEditor = false;
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
            }

            return settings;
        }

        /// <summary>
        /// Returns a serialized wrapper for use in SettingsProvider inspectors.
        /// </summary>
        internal static SerializedObject GetSerializedSettings()
        {
            return new SerializedObject(GetOrCreateSettings());
        }
    }
#endif
}
