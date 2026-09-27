using UnityEngine;

/// <summary>
/// Nudges enemies/crystals out of bad spawn overlap (solid ground + shredder hazard triggers).
/// </summary>
public static class PlatformEmbedRescue
{
    public static void TryResolveBadSpawnOverlap(
        Transform target,
        Collider2D body,
        LayerMask groundLayers,
        int maxAttempts = 10)
    {
        TryLiftOutOfGround(target, body, groundLayers, maxAttempts);
        TryEscapeShredderTriggers(target, body, maxAttempts);
    }

    public static void TryLiftOutOfGround(Transform target, Collider2D body, LayerMask groundLayers, int maxAttempts = 10)
    {
        if (target == null || body == null || groundLayers.value == 0)
            return;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (!TryGetRequiredLift(body, groundLayers, out float lift))
                return;

            target.position += Vector3.up * lift;
        }
    }

    private static bool TryGetRequiredLift(Collider2D body, LayerMask groundLayers, out float lift)
    {
        lift = 0f;
        Bounds bounds = body.bounds;
        Vector2 size = bounds.size;
        size.x = Mathf.Max(0.05f, size.x * 0.92f);
        size.y = Mathf.Max(0.05f, size.y * 0.92f);

        Collider2D[] hits = Physics2D.OverlapBoxAll(bounds.center, size, 0f, groundLayers);
        for (int i = 0; i < hits.Length; i++)
        {
            Collider2D hit = hits[i];
            if (hit == null || hit.isTrigger || hit == body)
                continue;

            Bounds platform = hit.bounds;
            if (bounds.min.y >= platform.max.y - 0.08f)
                continue;

            if (!bounds.Intersects(platform))
                continue;

            float needed = platform.max.y - bounds.min.y + 0.18f;
            if (needed > lift)
                lift = needed;
        }

        return lift > 0.01f;
    }

    /// <summary>
    /// Level one shredder boxes use trigger colliders — lifting out of ground alone does not escape them.
    /// </summary>
    public static void TryEscapeShredderTriggers(Transform target, Collider2D body, int maxAttempts = 8)
    {
        if (target == null || body == null)
            return;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (!TryGetShredderEscapeOffset(body, out Vector3 offset))
                return;

            target.position += offset;
        }
    }

    private static bool TryGetShredderEscapeOffset(Collider2D body, out Vector3 offset)
    {
        offset = Vector3.zero;
        Bounds bounds = body.bounds;
        Vector2 size = bounds.size;
        size.x = Mathf.Max(0.05f, size.x * 0.95f);
        size.y = Mathf.Max(0.05f, size.y * 0.95f);

        Collider2D[] hits = Physics2D.OverlapBoxAll(bounds.center, size, 0f);
        float bestUp = 0f;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider2D hit = hits[i];
            if (hit == null || hit == body || !hit.isTrigger)
                continue;

            if (hit.GetComponent<ShredderBlade>() == null && hit.GetComponentInParent<ShredderBlade>() == null)
                continue;

            Bounds hazard = hit.bounds;
            if (!bounds.Intersects(hazard))
                continue;

            float up = hazard.max.y - bounds.min.y + 0.35f;
            if (up > bestUp)
                bestUp = up;
        }

        if (bestUp <= 0.01f)
            return false;

        offset = Vector3.up * bestUp;
        return true;
    }
}
