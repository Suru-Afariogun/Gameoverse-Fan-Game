using UnityEngine;

/// <summary>
/// Rolls enemy-only consumable drops: 50% chance to spawn, then 50% Juice Box or Hot Dog.
/// </summary>
public static class EnemyConsumableDrop
{
    public static void TrySpawnFromEnemy(
        Vector3 center,
        CollectableConsumable juiceBoxPrefab,
        CollectableConsumable hotDogPrefab,
        float spawnChance = 0.5f)
    {
        if (Random.value >= spawnChance)
            return;

        CollectableConsumable prefab = Random.value < 0.5f ? juiceBoxPrefab : hotDogPrefab;
        if (prefab == null)
            prefab = juiceBoxPrefab != null ? juiceBoxPrefab : hotDogPrefab;

        if (prefab == null)
            return;

        SoundManager.Instance?.PlayItemDrop();
        CollectablePickupBase.SpawnSingleBurst(prefab, center);
    }
}
