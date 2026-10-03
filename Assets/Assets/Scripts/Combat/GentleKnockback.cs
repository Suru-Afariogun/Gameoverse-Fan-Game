using UnityEngine;

/// <summary>
/// Enemies that drive their own position every step (instead of physics velocity) take the push here,
/// otherwise their next MovePosition would erase it.
/// </summary>
public interface IKnockbackReceiver
{
    void ApplyKnockbackDisplacement(Vector2 delta);
}

/// <summary>
/// Short horizontal shove added at runtime to a hit enemy (e.g. Malice projectile slashes).
/// Snaps out fast then settles. Moves by position so enemy AI velocity does not cancel it,
/// and stops at solid walls. A new push replaces the current one instead of stacking.
/// </summary>
public sealed class GentleKnockback : MonoBehaviour
{
    private static readonly RaycastHit2D[] CastHits = new RaycastHit2D[8];
    private static readonly System.Collections.Generic.List<Collider2D> ChainOverlaps =
        new System.Collections.Generic.List<Collider2D>(16);
    private const float WallSkin = 0.02f;
    private const float ChainContactSkin = 0.06f;

    private Rigidbody2D rb;
    private Collider2D body;
    private IKnockbackReceiver receiver;
    private float direction;
    private float totalDistance;
    private float traveled;
    private float duration;
    private float elapsed;
    private bool active;
    private bool chainKill;
    private ICommonEnemy selfEnemy;

    public static void Apply(Component target, float directionSign, float distance, float seconds)
    {
        Apply(target, directionSign, distance, seconds, chainKill: false);
    }

    /// <param name="chainKill">If the pushed enemy runs into another moving common enemy, both are destroyed.</param>
    public static void Apply(Component target, float directionSign, float distance, float seconds, bool chainKill)
    {
        if (target == null || distance <= 0.0001f || Mathf.Abs(directionSign) < 0.01f)
            return;

        Rigidbody2D targetRb = target.GetComponentInParent<Rigidbody2D>();
        GameObject host = targetRb != null ? targetRb.gameObject : target.gameObject;

        GentleKnockback push = host.GetComponent<GentleKnockback>();
        if (push == null)
            push = host.AddComponent<GentleKnockback>();

        push.chainKill = chainKill;
        push.selfEnemy = target as ICommonEnemy ?? target.GetComponentInParent<ICommonEnemy>();
        push.Begin(Mathf.Sign(directionSign), distance, seconds);
    }

    private void Begin(float dirSign, float distance, float seconds)
    {
        if (rb == null)
            rb = GetComponent<Rigidbody2D>();
        if (body == null)
            body = FindBodyCollider();
        if (receiver == null)
            receiver = GetComponent<IKnockbackReceiver>();

        direction = dirSign;
        totalDistance = distance;
        traveled = 0f;
        duration = Mathf.Max(0.02f, seconds);
        elapsed = 0f;
        active = true;
    }

    private Collider2D FindBodyCollider()
    {
        Collider2D[] cols = GetComponents<Collider2D>();
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] != null && !cols[i].isTrigger)
                return cols[i];
        }

        return null;
    }

    private void FixedUpdate()
    {
        if (!active)
            return;

        elapsed += HyperSpeedWorldSlow.IsActive ? HyperSpeedWorldSlow.WorldFixedDeltaTime : Time.fixedDeltaTime;
        float t = Mathf.Clamp01(elapsed / duration);
        // Ease-out: full speed on impact, decelerating to a stop.
        float s = 1f - (1f - t) * (1f - t);
        float step = totalDistance * s - traveled;
        if (step > 0f)
        {
            float allowed = ClampAgainstWalls(step);
            Vector2 delta = new Vector2(direction * allowed, 0f);
            if (receiver != null)
                receiver.ApplyKnockbackDisplacement(delta);
            else if (rb != null)
                rb.position += delta;
            else
                transform.position += (Vector3)delta;

            traveled += step;
        }

        if (chainKill && TryChainKill())
        {
            active = false;
            return;
        }

        if (t >= 1f)
            active = false;
    }

    private bool TryChainKill()
    {
        if (body == null || !body.enabled || selfEnemy == null || selfEnemy.IsDead)
            return false;

        Bounds b = body.bounds;
        Vector2 size = new Vector2(b.size.x + ChainContactSkin * 2f, b.size.y);
        ContactFilter2D filter = new ContactFilter2D();
        filter.useTriggers = true;
        filter.NoFilter();

        ChainOverlaps.Clear();
        Physics2D.OverlapBox(b.center, size, 0f, filter, ChainOverlaps);
        for (int i = 0; i < ChainOverlaps.Count; i++)
        {
            Collider2D c = ChainOverlaps[i];
            if (c == null || c.transform.IsChildOf(transform) || EnemyDetectionZone.IsDetectionOnlyCollider(c))
                continue;
            if (c.GetComponent<AttackHitbox>() != null)
                continue;
            // Only enemies it is being pushed into, not ones behind it.
            if (direction * (c.bounds.center.x - b.center.x) < 0f)
                continue;

            ICommonEnemy other = c.GetComponentInParent<ICommonEnemy>();
            if (other == null || other == selfEnemy || other.IsDead || MaliceSlashProjectile.IsStationaryEnemy(other))
                continue;

            Kill(other);
            Kill(selfEnemy);
            return true;
        }

        return false;
    }

    private static void Kill(ICommonEnemy enemy)
    {
        if (enemy == null || enemy.IsDead)
            return;

        if (enemy is IForceKillable killable)
            killable.ForceKill();
        else
            enemy.TakeDamage(Mathf.Max(1, enemy.CurrentHealth) + 999);
    }

    private float ClampAgainstWalls(float step)
    {
        if (body == null || !body.enabled)
            return step;

        ContactFilter2D filter = new ContactFilter2D();
        filter.useTriggers = false;
        filter.SetLayerMask(Physics2D.GetLayerCollisionMask(body.gameObject.layer));

        int count = body.Cast(new Vector2(direction, 0f), filter, CastHits, step + WallSkin, true);
        float allowed = step;
        for (int i = 0; i < count; i++)
        {
            RaycastHit2D hit = CastHits[i];
            if (hit.collider == null || hit.collider.isTrigger)
                continue;
            if (hit.normal.x * direction > -0.5f)
                continue;
            if (Physics2D.GetIgnoreCollision(body, hit.collider))
                continue;
            if (hit.collider.transform.IsChildOf(transform))
                continue;

            allowed = Mathf.Min(allowed, Mathf.Max(0f, hit.distance - WallSkin));
        }

        return allowed;
    }
}
