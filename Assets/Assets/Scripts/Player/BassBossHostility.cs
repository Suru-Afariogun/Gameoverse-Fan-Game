using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Lets the playable Bass Dual weapons (Crash Bomb, Heat Flares, Bubbles, Air Tornado, Leaf Shield…) be fired by
/// Boss Bass. A weapon whose owner is a <see cref="Boss"/> is "hostile": it hurts the player instead of bosses /
/// enemies and ignores its own boss. Player-owned weapons are untouched.
/// </summary>
public static class BassBossHostility
{
    /// <summary>True when the weapon's owner is a boss (so it should hurt the player).</summary>
    public static bool IsHostile(Transform owner)
    {
        return owner != null && owner.GetComponentInParent<Boss>() != null;
    }

    /// <summary>Host body / sorting group of whoever fired the weapon (player or boss).</summary>
    public static void ResolveHost(Transform owner, out SpriteRenderer body, out SortingGroup group)
    {
        body = null;
        group = null;
        if (owner == null)
            return;

        PlayerController player = owner.GetComponentInParent<PlayerController>();
        if (player != null)
        {
            body = player.GetComponent<SpriteRenderer>();
            group = player.EffectSortingGroup;
            return;
        }

        Boss boss = owner.GetComponentInParent<Boss>();
        if (boss != null)
        {
            body = boss.GetComponent<SpriteRenderer>();
            group = boss.EffectSortingGroup;
        }
    }

    /// <summary>True when <paramref name="other"/> belongs to the weapon's owner (never hurt yourself).</summary>
    public static bool IsOwnerCollider(Collider2D other, Transform owner)
    {
        if (other == null || owner == null)
            return false;
        return other.transform == owner || other.transform.IsChildOf(owner);
    }

    /// <summary>Damages the player under <paramref name="other"/>. Returns true when a player was hit / touched.</summary>
    public static bool TryHurtPlayer(Collider2D other, float damage, Transform source)
    {
        if (other == null || EnemyDetectionZone.IsDetectionOnlyCollider(other))
            return false;

        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null || player.IsDead)
            return false;

        int applied = FractionalDamage.Consume(player, damage);
        if (applied > 0)
            player.TakeDamage(applied, source);
        return true;
    }
}
