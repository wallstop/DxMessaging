namespace DxMessaging.Editor
{
#if UNITY_EDITOR
    using Core;
    using Core.MessageBus;
    using UnityEditor;
    using UnityEngine;

    internal static class DxMessagingMenu
    {
        private const string Root = "Tools/Wallstop Studios/DxMessaging/";

        [MenuItem(Root + "Reset Static State")]
        private static void ResetStatics()
        {
            DxMessagingStaticState.Reset();
            Debug.Log("[DxMessaging] Static state reset.");
        }

        [MenuItem(Root + "Toggle Global Diagnostics")]
        private static void ToggleGlobalDiagnostics()
        {
            DiagnosticsTarget current = IMessageBus.GlobalDiagnosticsTargets;
            DiagnosticsTarget next = current switch
            {
                DiagnosticsTarget.Off => DiagnosticsTarget.Editor,
                DiagnosticsTarget.Editor => DiagnosticsTarget.Runtime,
                DiagnosticsTarget.Runtime => DiagnosticsTarget.All,
                DiagnosticsTarget.All => DiagnosticsTarget.Off,
                _ => DiagnosticsTarget.Off,
            };

            IMessageBus.GlobalDiagnosticsTargets = next;
            Debug.Log($"[DxMessaging] Global diagnostics targets set to {next}.");
        }
    }
#endif
}
