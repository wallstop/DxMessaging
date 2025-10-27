using DxMessaging.Core;
using DxMessaging.Core.MessageBus;
using UnityEngine;

public sealed class DiagnosticsEnabler : MonoBehaviour
{
    [SerializeField]
    private bool enableOnStart = true;

    private void Start()
    {
        if (enableOnStart)
        {
            if (MessageHandler.MessageBus is MessageBus concreteBus)
            {
                concreteBus.DiagnosticsMode = true;
                Debug.Log("DxMessaging global diagnostics enabled.");
            }
            else
            {
                Debug.LogWarning(
                    "Global diagnostics are unavailable because the active global bus is not the default DxMessaging implementation."
                );
            }
        }
    }
}
