using DxMessaging.Core.Extensions;
using DxMessaging.Core.Messages;
using UnityEngine;

public sealed class UIButtonEmitter : MonoBehaviour
{
    [SerializeField]
    private string buttonId = "ButtonA";

    // Hook this to a Unity UI Button via the Inspector
    public void Click()
    {
        var evt = new ButtonClicked(buttonId);
        evt.Emit();

        // Also emit a targeted string message to this GameObject
        var text = new StringMessage($"Clicked {buttonId}");
        text.EmitGameObjectTargeted(gameObject);
    }
}
