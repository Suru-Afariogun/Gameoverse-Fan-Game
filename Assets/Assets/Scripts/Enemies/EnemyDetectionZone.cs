using UnityEngine;

/// <summary>
/// Marks a detection-only radius (enemy "vision" / NPC interact).
/// AI reads collider size/bounds for range checks — the collider must stay enabled
/// so <see cref="Collider2D.bounds"/> stays valid. Combat systems already ignore
/// these via <see cref="IsDetectionOnlyCollider"/>, so no scene-wide IgnoreCollision
/// scan is needed on load.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public class EnemyDetectionZone : MonoBehaviour
{
    private Collider2D zoneCollider;

    private void Awake()
    {
        EnsureTrigger();
    }

    private void OnEnable()
    {
        EnsureTrigger();
    }

    /// <summary>
    /// True when this collider (or its parent chain) is detection-only, not a solid/hittable body.
    /// </summary>
    public static bool IsDetectionOnlyCollider(Collider2D col)
    {
        if (col == null)
            return false;

        return col.GetComponent<EnemyDetectionZone>() != null
            || col.GetComponentInParent<EnemyDetectionZone>() != null
            || col.GetComponent<NpcInteractZone>() != null
            || col.GetComponentInParent<NpcInteractZone>() != null;
    }

    /// <summary>
    /// Force this zone to ignore a newly spawned collider (e.g. a projectile).
    /// Cheap: one IgnoreCollision, not a full scene scan.
    /// </summary>
    public static void IgnoreAgainstAllZones(Collider2D other)
    {
        if (other == null)
            return;

        EnemyDetectionZone[] zones = Object.FindObjectsByType<EnemyDetectionZone>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < zones.Length; i++)
        {
            EnemyDetectionZone zone = zones[i];
            if (zone == null)
                continue;

            Collider2D zoneCol = zone.ZoneCollider;
            if (zoneCol == null || zoneCol == other)
                continue;

            Physics2D.IgnoreCollision(zoneCol, other, true);
        }
    }

    public Collider2D ZoneCollider
    {
        get
        {
            if (zoneCollider == null)
                zoneCollider = GetComponent<Collider2D>();
            return zoneCollider;
        }
    }

    /// <summary>
    /// Vision-only: ensure trigger + enabled so bounds/radius queries work.
    /// </summary>
    public void RefreshPhaseCollisions()
    {
        EnsureTrigger();
    }

    private void EnsureTrigger()
    {
        Collider2D col = ZoneCollider;
        if (col == null)
            return;

        col.isTrigger = true;
        col.enabled = true;
    }
}
