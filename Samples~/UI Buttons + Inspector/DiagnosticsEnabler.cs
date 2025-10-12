using DxMessaging.Core;
using UnityEngine;

public sealed class DiagnosticsEnabler : MonoBehaviour
{
    [SerializeField]
    private bool enableOnStart = true;

    private void Start()
    {
        if (enableOnStart)
        {
            MessageHandler.MessageBus.DiagnosticsMode = true;
            Debug.Log("DxMessaging global diagnostics enabled.");
        }
    }
}
