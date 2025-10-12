using DxMessaging.Core;
using DxMessaging.Core.Messages;
using DxMessaging.Unity;
using UnityEngine;

public sealed class UIOverlay : MessageAwareComponent
{
    protected override void RegisterMessageHandlers()
    {
        base.RegisterMessageHandlers();
        _ = Token.RegisterUntargeted<VideoSettingsChanged>(OnSettingsChanged);
        _ = Token.RegisterBroadcastWithoutSource<TookDamage>(OnAnyDamage);
    }

    private void OnSettingsChanged(ref VideoSettingsChanged m) =>
        Debug.Log($"UI rebuild for {m.width}x{m.height}");

    private void OnAnyDamage(ref InstanceId src, ref TookDamage m) =>
        Debug.Log($"Damage from {src}: -{m.amount}");
}
