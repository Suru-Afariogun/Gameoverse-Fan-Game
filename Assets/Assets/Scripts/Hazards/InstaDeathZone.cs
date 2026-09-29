using UnityEngine;

/// <summary>
/// Kills the player the instant they touch this zone (pits, crushers, bottomless drops).
/// Ignores hurt i-frames; only Kit's rocket ship ride / scripted cutscenes protect the player.
/// The collider is forced to a trigger so the player falls into it instead of standing on it.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public sealed class InstaDeathZone : MonoBehaviour
{
    private Collider2D zoneCollider;

    private void Awake()
    {
        zoneCollider = GetComponent<Collider2D>();
        if (zoneCollider != null)
            zoneCollider.isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryKill(other);
    }

    // Covers spawning / respawning already inside the zone.
    private void OnTriggerStay2D(Collider2D other)
    {
        TryKill(other);
    }

    private static void TryKill(Collider2D other)
    {
        if (other == null)
            return;

        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null || player.IsDead || player.IsRidingVehicle || player.IsScriptedInvulnerable)
            return;

        player.SetHealth(0);
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Collider2D col = zoneCollider != null ? zoneCollider : GetComponent<Collider2D>();
        if (col == null)
            return;

        Bounds b = col.bounds;
        Gizmos.color = new Color(1f, 0.1f, 0.1f, 0.25f);
        Gizmos.DrawCube(b.center, b.size);
        Gizmos.color = new Color(1f, 0.1f, 0.1f, 0.9f);
        Gizmos.DrawWireCube(b.center, b.size);
    }
#endif
}
