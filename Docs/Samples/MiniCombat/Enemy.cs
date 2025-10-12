using DxMessaging.Core.Extensions;
using DxMessaging.Core.Messages;
using DxMessaging.Unity;
using UnityEngine;

[RequireComponent(typeof(MessagingComponent))]
public sealed class Enemy : MonoBehaviour
{
    public void ApplyDamage(int amount)
    {
        var took = new TookDamage(amount);
        took.EmitGameObjectBroadcast(gameObject);
        Debug.Log($"Enemy took damage: {amount}");
    }
}
