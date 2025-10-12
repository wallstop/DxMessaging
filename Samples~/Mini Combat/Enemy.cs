using DxMessaging.Core.Extensions;
using DxMessaging.Core.Messages;
using UnityEngine;

public sealed class Enemy : MonoBehaviour
{
    public void ApplyDamage(int amount)
    {
        var took = new TookDamage(amount);
        took.EmitGameObjectBroadcast(gameObject);
        Debug.Log($"Enemy took damage: {amount}");
    }
}
