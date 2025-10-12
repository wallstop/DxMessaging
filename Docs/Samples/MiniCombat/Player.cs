using DxMessaging.Unity;
using DxMessaging.Core.Messages;
using UnityEngine;

public sealed class Player : MessageAwareComponent
{
    private int _hp;

    protected override void RegisterMessageHandlers()
    {
        _ = Token.RegisterComponentTargeted<Heal>(this, OnHeal);
    }

    private void OnHeal(ref Heal m)
    {
        _hp += m.amount;
        Debug.Log($"Player healed: +{m.amount}, HP={_hp}");
    }
}
