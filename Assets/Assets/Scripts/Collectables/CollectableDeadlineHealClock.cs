using System;
using UnityEngine;

/// <summary>Deadline-only heal pickup (2 HP). Drifts out then bobs in place when spawned from shot explosions.</summary>
public class CollectableDeadlineHealClock : CollectablePickupBase
{
    [SerializeField] private int healAmount = 2;
    [SerializeField] private float driftDistance = 2f;
    [SerializeField] private float bobAmplitude = 1f;
    [SerializeField] private float driftSpeed = 2f;

    public static void SpawnFloatingCluster(
        CollectableDeadlineHealClock prefab,
        Vector3 center,
        int count = 3,
        float deadlineSeekSpeed = 0f)
    {
        if (prefab == null)
            return;

        SoundManager.Instance?.PlayItemDrop();
        int n = Mathf.Max(1, count);
        for (int i = 0; i < n; i++)
        {
            float angle = (360f / n) * i * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            // Start slightly offset so they don't stack on the impact point.
            Vector3 spawn = center + (Vector3)(dir * 0.2f);
            CollectableDeadlineHealClock clock = Instantiate(prefab, spawn, Quaternion.identity);
            clock.PlaceFloatingDriftThenBob(dir, clock.driftDistance, clock.bobAmplitude, clock.driftSpeed);
            if (deadlineSeekSpeed > 0.01f)
                clock.EnableDeadlineSeek(deadlineSeekSpeed);
        }
    }

    protected override void ApplyPickup(PlayerController player)
    {
        if (player == null)
            return;
        if (!string.Equals(player.CharacterId, DeadlinePlayerController.DeadlineCharacterId, StringComparison.OrdinalIgnoreCase))
            return;

        player.Heal(Mathf.Max(1, healAmount));
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        healAmount = Mathf.Max(1, healAmount);
        driftDistance = Mathf.Max(0.1f, driftDistance);
        bobAmplitude = Mathf.Max(0.05f, bobAmplitude);
        driftSpeed = Mathf.Max(0.1f, driftSpeed);
    }
#endif
}
