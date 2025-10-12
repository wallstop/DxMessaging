using DxMessaging.Core.Extensions;
using UnityEngine;

public sealed class Boot : MonoBehaviour
{
    public Player player;
    public Enemy enemy;

    private void Start()
    {
        // Global settings change
        var settings = new VideoSettingsChanged(1920, 1080);
        settings.Emit();

        // Heal player (targeted)
        var heal = new Heal(10);
        heal.EmitComponentTargeted(player);

        // Damage enemy (broadcast)
        enemy.ApplyDamage(5);
    }
}
