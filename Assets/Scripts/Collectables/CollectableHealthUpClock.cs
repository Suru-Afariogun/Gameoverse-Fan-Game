using System;
using UnityEngine;

/// <summary>
/// Count-only health pickup spawned when he damages enemies while fully charged.
/// Pops straight up one tile, then floats down like other collectables.
/// </summary>
public class CollectableHealthUpClock : CollectablePickupBase
{
    [SerializeField] private int healAmount = 1;

    public static void SpawnFromEnemyHit(CollectableHealthUpClock prefab, Vector3 enemyCenter)
    {
        if (prefab == null || !IsCountActive())
            return;

        SoundManager.Instance?.PlayItemDrop();
        SpawnVerticalPop(prefab, enemyCenter, 1f);
    }

    private static bool IsCountActive()
    {
        if (PlayerController.Active != null)
            return string.Equals(PlayerController.Active.CharacterId, "Count", StringComparison.OrdinalIgnoreCase);

        if (PlayerSpawner.Instance != null && PlayerSpawner.Instance.CurrentPlayer != null)
            return string.Equals(PlayerSpawner.Instance.CurrentPlayer.CharacterId, "Count", StringComparison.OrdinalIgnoreCase);

        return string.Equals(PlayerSpawner.SelectedCharacterId, "Count", StringComparison.OrdinalIgnoreCase);
    }

    protected override void ApplyPickup(PlayerController player)
    {
        if (player == null)
            return;

        if (!string.Equals(player.CharacterId, "Count", StringComparison.OrdinalIgnoreCase))
            return;

        player.Heal(Mathf.Max(1, healAmount));
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        healAmount = Mathf.Max(1, healAmount);
    }
#endif
}
