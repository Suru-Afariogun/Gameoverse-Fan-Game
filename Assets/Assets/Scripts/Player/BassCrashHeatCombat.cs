using UnityEngine;

/// <summary>Shared hit helpers for Crashing Heat explosions / flares.</summary>
public static class BassCrashHeatCombat
{
    public static void ApplyExplosionHit(
        Collider2D other,
        float damage,
        float knockbackSpaces,
        float spaceSize,
        Transform source,
        GameVisualEffect stunnedPrefab,
        bool hostileToPlayer = false)
    {
        if (other == null || EnemyDetectionZone.IsDetectionOnlyCollider(other))
            return;
        if (hostileToPlayer)
        {
            // Boss Bass fire: hurts the player only.
            BassBossHostility.TryHurtPlayer(other, damage, source);
            return;
        }

        if (other.GetComponentInParent<PlayerController>() != null)
            return;
        if (other.GetComponentInParent<BassCrashBomb>() != null)
            return;

        float knock = Mathf.Max(0f, knockbackSpaces) * Mathf.Max(0.01f, spaceSize);
        float dir = source != null
            ? Mathf.Sign(other.bounds.center.x - source.position.x)
            : 1f;
        if (Mathf.Abs(dir) < 0.01f)
            dir = 1f;

        Boss boss = other.GetComponentInParent<Boss>();
        if (boss != null && !boss.IsDead)
        {
            int applied = FractionalDamage.Consume(boss, damage);
            if (applied > 0)
                boss.TakeDamage(applied, ProjectileShotType.Medium);
            GentleKnockback.Apply(boss, dir, knock, 0.22f);
            // Bosses already PlayStunned with their own spark in OnHitStunStarted — never force Bass art.
            return;
        }

        ICommonEnemy enemy = other.GetComponentInParent<ICommonEnemy>();
        if (enemy == null || enemy.IsDead)
            return;

        Component host = enemy as Component;
        int whole = FractionalDamage.Consume(host, damage);
        if (whole > 0)
            DealDamageWithGuaranteedStun(host, enemy, whole);

        GentleKnockback.Apply(host, dir, knock, 0.22f);
        // Common enemies spawn their own hit sparks in TakeDamage — never force Bass spark art.
    }

    public static void ApplyDamageOnly(
        Collider2D other,
        float damage,
        bool guaranteeStun = false,
        bool hostileToPlayer = false,
        Transform source = null)
    {
        if (other == null || EnemyDetectionZone.IsDetectionOnlyCollider(other))
            return;
        if (hostileToPlayer)
        {
            // Boss Bass fire: hurts the player only.
            BassBossHostility.TryHurtPlayer(other, damage, source);
            return;
        }

        if (other.GetComponentInParent<PlayerController>() != null)
            return;

        Boss boss = other.GetComponentInParent<Boss>();
        if (boss != null && !boss.IsDead)
        {
            int applied = FractionalDamage.Consume(boss, damage);
            if (applied > 0)
                boss.TakeDamage(applied, guaranteeStun ? ProjectileShotType.Medium : ProjectileShotType.Small);
            return;
        }

        ICommonEnemy enemy = other.GetComponentInParent<ICommonEnemy>();
        if (enemy == null || enemy.IsDead)
            return;

        Component host = enemy as Component;
        int whole = FractionalDamage.Consume(host, damage);
        if (whole <= 0)
            return;

        if (guaranteeStun)
            DealDamageWithGuaranteedStun(host, enemy, whole);
        else
            enemy.TakeDamage(whole);
    }

    private static void DealDamageWithGuaranteedStun(Component host, ICommonEnemy enemy, int amount)
    {
        // Medium/Big shot types trigger stun on ScrapNit / CrankyClanky / similar.
        if (host is ScrapNit scrap)
        {
            scrap.TakeDamage(amount, ProjectileShotType.Medium);
            return;
        }

        if (host is CrankyClanky cranky)
        {
            cranky.TakeDamage(amount, ProjectileShotType.Medium);
            return;
        }

        if (host is LaserBot laser)
        {
            laser.TakeDamage(amount, ProjectileShotType.Medium);
            return;
        }

        if (host is BlockerBot blocker)
        {
            blocker.TakeDamage(amount, ProjectileShotType.Medium);
            return;
        }

        if (host is ChaoticTanker tanker)
        {
            tanker.TakeDamage(amount, ProjectileShotType.Medium);
            return;
        }

        enemy.TakeDamage(amount);
    }
}
