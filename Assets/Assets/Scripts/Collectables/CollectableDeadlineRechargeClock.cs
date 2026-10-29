using System;
using UnityEngine;

/// <summary>
/// Deadline-only Deterioration Acceleration recharge (+3s pool each).
/// Drifts out then bobs in place when spawned from shot explosions.
/// </summary>
public class CollectableDeadlineRechargeClock : CollectablePickupBase
{
    [SerializeField] private float rechargeSeconds = 3f;
    [SerializeField] private float driftDistance = 2f;
    [SerializeField] private float bobAmplitude = 1f;
    [SerializeField] private float driftSpeed = 2f;

    public static void SpawnFloatingCluster(
        CollectableDeadlineRechargeClock prefab,
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
            Vector3 spawn = center + (Vector3)(dir * 0.2f);
            CollectableDeadlineRechargeClock clock = Instantiate(prefab, spawn, Quaternion.identity);
            clock.PlaceFloatingDriftThenBob(dir, clock.driftDistance, clock.bobAmplitude, clock.driftSpeed);
            if (deadlineSeekSpeed > 0.01f)
                clock.EnableDeadlineSeek(deadlineSeekSpeed);
        }
    }

    protected override void ApplyPickup(PlayerController player)
    {
        if (player is not DeadlinePlayerController deadline)
            return;
        if (!string.Equals(player.CharacterId, DeadlinePlayerController.DeadlineCharacterId, StringComparison.OrdinalIgnoreCase))
            return;

        deadline.AddDeteriorationAccelerationTime(Mathf.Max(0.1f, rechargeSeconds));
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        rechargeSeconds = Mathf.Max(0.1f, rechargeSeconds);
        driftDistance = Mathf.Max(0.1f, driftDistance);
        bobAmplitude = Mathf.Max(0.05f, bobAmplitude);
        driftSpeed = Mathf.Max(0.1f, driftSpeed);
    }
#endif
}
