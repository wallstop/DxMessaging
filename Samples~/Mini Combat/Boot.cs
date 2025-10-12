using DxMessaging.Core.Extensions;
using UnityEngine;

public sealed class Boot : MonoBehaviour
{
    public Player player;
    public Enemy enemy;

    private void Start()
    {
        var settings = new VideoSettingsChanged(1920, 1080);
        settings.Emit();

        var heal = new Heal(10);
        heal.EmitComponentTargeted(player);

        enemy.ApplyDamage(5);
    }
}
