namespace DxMessaging.Editor
{
#if UNITY_EDITOR
    using Core;
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
            Core.MessageBus.IMessageBus.GlobalDiagnosticsMode = !Core.MessageBus
                .IMessageBus
                .GlobalDiagnosticsMode;
            Debug.Log(
                $"[DxMessaging] Global diagnostics mode set to {Core.MessageBus.IMessageBus.GlobalDiagnosticsMode}."
            );
        }
    }
#endif
}
