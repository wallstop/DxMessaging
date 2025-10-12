using DxMessaging.Core;
using DxMessaging.Core.Messages;
using DxMessaging.Unity;
using UnityEngine;

public sealed class MessagingObserver : MessageAwareComponent
{
    protected override void RegisterMessageHandlers()
    {
        base.RegisterMessageHandlers();
        _ = Token.RegisterUntargeted<ButtonClicked>(OnButtonClicked);
        _ = Token.RegisterGlobalAcceptAll(OnAnyUntargeted, OnAnyTargeted, OnAnyBroadcast);
    }

    private void OnButtonClicked(ref ButtonClicked m) =>
        Debug.Log($"Untargeted ButtonClicked: {m.id}");

    private void OnAnyUntargeted(IUntargetedMessage m) =>
        Debug.Log($"[Any Untargeted] {m.MessageType.Name}");

    private void OnAnyTargeted(InstanceId target, ITargetedMessage m) =>
        Debug.Log($"[Any Targeted] {m.MessageType.Name} to {target}");

    private void OnAnyBroadcast(InstanceId source, IBroadcastMessage m) =>
        Debug.Log($"[Any Broadcast] {m.MessageType.Name} from {source}");
}
