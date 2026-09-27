using UnityEngine;

/// <summary>
/// Currency pickup spawned from shattered enemy crystals / defeated enemies.
/// Bursts outward, settles into a hover above ground, then collects on player touch.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class CollectableCrystal : CollectablePickupBase
{
    public static void SpawnBurst(CollectableCrystal prefab, Vector3 center, int count)
    {
        if (prefab == null || count <= 0)
            return;

        for (int i = 0; i < count; i++)
        {
            float t = count <= 1 ? 0.5f : i / (float)(count - 1);
            float angleDeg = Mathf.Lerp(25f, 155f, t) + Random.Range(-18f, 18f);
            angleDeg = Mathf.Clamp(angleDeg, 15f, 165f);
            float rad = angleDeg * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)).normalized;

            CollectableCrystal instance = Instantiate(prefab, center, Quaternion.identity);
            instance.LaunchBurst(dir);
        }
    }

    public static void SpawnRandomBurst(CollectableCrystal prefab, Vector3 center, int minCount, int maxCount)
    {
        if (prefab == null)
            return;

        int count = Random.Range(Mathf.Max(1, minCount), Mathf.Max(minCount, maxCount) + 1);
        SpawnBurst(prefab, center, count);
    }

    protected override void ApplyPickup(PlayerController player)
    {
        if (PlayerInventory.Instance != null)
            PlayerInventory.Instance.AddCrystals(1);
    }
}
