using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Overlap-only hurtbox (always a trigger). Phases through everything visually/physically,
/// but still detects overlaps for damage and grab when activated.
/// Hits PlayerController and Boss (any IDamageable), except its owner.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class AttackHitbox : MonoBehaviour
{
    [SerializeField] private Collider2D hitCollider;
    [Tooltip("If empty, hits any damageable that is not the owner.")]
    [SerializeField] private LayerMask hittableLayers = ~0;

    private PlayerController ownerPlayer;
    private Boss ownerBoss;
    private ICommonEnemy ownerCommonEnemy;
    private Transform ownerRoot;
    private int damage = 1;
    private bool active;
    private bool dealDamage = true;
    private readonly HashSet<int> hitInstanceIds = new HashSet<int>();

    public bool IsActive => active;
    public PlayerController Owner => ownerPlayer;
    public Boss OwnerBoss => ownerBoss;
    public ICommonEnemy OwnerCommonEnemy => ownerCommonEnemy;

    /// <summary>Fired once per target when first overlapped during this activation.</summary>
    public event Action<Collider2D, PlayerController> OnTargetAcquired;

    private void OnEnable()
    {
        EnsureTriggerCollider();
        Deactivate();
    }

    private void Awake()
    {
        EnsureTriggerCollider();
        Deactivate();
    }

    public void SetOwner(PlayerController newOwner)
    {
        ownerPlayer = newOwner;
        ownerBoss = null;
        ownerCommonEnemy = null;
        ownerRoot = newOwner != null ? newOwner.transform : null;
    }

    public void SetOwner(Boss newOwner)
    {
        ownerBoss = newOwner;
        ownerPlayer = null;
        ownerCommonEnemy = null;
        ownerRoot = newOwner != null ? newOwner.transform : null;
    }

    public void SetOwner(ICommonEnemy newOwner)
    {
        ownerCommonEnemy = newOwner;
        ownerPlayer = null;
        ownerBoss = null;
        ownerRoot = newOwner is Component component && component != null
            ? component.transform
            : null;
    }

    public void Activate(int hitDamage, bool applyDamage = true)
    {
        EnsureTriggerCollider();

        damage = Mathf.Max(0, hitDamage);
        dealDamage = applyDamage;
        hitInstanceIds.Clear();
        active = true;

        if (hitCollider != null)
            hitCollider.enabled = true;
    }

    /// <summary>Changes the damage of an active swing without clearing who it already hit.</summary>
    public void SetDamage(int hitDamage)
    {
        damage = Mathf.Max(0, hitDamage);
    }

    public void Deactivate()
    {
        active = false;
        hitInstanceIds.Clear();

        EnsureTriggerCollider();

        if (hitCollider != null)
            hitCollider.enabled = false;
    }

    /// <summary>Lets Harlie pogo off the same target again during one activation.</summary>
    public void ForgetHitInstance(int instanceId)
    {
        hitInstanceIds.Remove(instanceId);
    }

    /// <summary>
    /// Force overlap-only behavior: trigger collider, no Rigidbody push, no solid blocking.
    /// </summary>
    private void EnsureTriggerCollider()
    {
        if (hitCollider == null)
            hitCollider = GetComponent<Collider2D>();

        if (hitCollider == null)
            return;

        hitCollider.isTrigger = true;

        // If someone added a Rigidbody2D on the hitbox, keep it kinematic so it never shoves objects.
        Rigidbody2D rb = hitCollider.attachedRigidbody;
        if (rb != null && rb.gameObject == gameObject)
        {
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.simulated = true;
            rb.useFullKinematicContacts = false;
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (hitCollider == null)
            hitCollider = GetComponent<Collider2D>();

        // Keep trigger-only. Do NOT force enabled=false here — that hides Scene
        // collider handles and blocks per-frame size/offset editing in Animation.
        // Play mode still turns the box on/off via Activate() / Deactivate().
        if (hitCollider != null)
            hitCollider.isTrigger = true;
    }
#endif

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryHit(other);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        // Overlap while active (includes targets already inside when the box turns on).
        TryHit(other);
    }

    private void TryHit(Collider2D other)
    {
        if (!active || other == null)
            return;

        if (((1 << other.gameObject.layer) & hittableLayers) == 0)
            return;

        if (ownerRoot != null &&
            (other.transform == ownerRoot || other.transform.IsChildOf(ownerRoot)))
            return;

        // Enemy detection radius is AI-only — never count it as a body hit.
        if (EnemyDetectionZone.IsDetectionOnlyCollider(other))
            return;

        // Player melee boxes — reflect, clash, or block shots (never pass through while active).
        if (ownerPlayer != null)
        {
            Projectile projectile = other.GetComponent<Projectile>();
            if (projectile == null)
                projectile = other.GetComponentInParent<Projectile>();

            if (projectile != null)
            {
                int projectileId = projectile.GetInstanceID();
                if (!hitInstanceIds.Add(projectileId))
                    return;

                ownerPlayer.TryHandleProjectileContact(projectile, other);
                return;
            }
        }

        Crystal crystal = other.GetComponent<Crystal>();
        if (crystal == null)
            crystal = other.GetComponentInParent<Crystal>();
        if (crystal != null && !crystal.IsDead)
        {
            // Boss attacks must never damage the crystal — only the player can.
            if (ownerBoss != null)
                return;

            int crystalId = crystal.GetInstanceID();
            if (!hitInstanceIds.Add(crystalId))
                return;

            if (dealDamage && damage > 0)
            {
                int before = crystal.CurrentHealth;
                crystal.TakeDamage(damage);
                NotifyOwnerDamageDealt(before - crystal.CurrentHealth);
            }
            return;
        }

        PlayerController player = other.GetComponent<PlayerController>();
        if (player == null)
            player = other.GetComponentInParent<PlayerController>();

        Boss boss = null;
        ICommonEnemy enemy = null;
        if (player == null)
        {
            // Bosses and common enemies take damage on their body collider only — never via their AttackBox tool.
            if (!IsBossAttackToolCollider(other) && !IsEnemyAttackToolCollider(other))
            {
                boss = other.GetComponent<Boss>();
                if (boss == null)
                    boss = other.GetComponentInParent<Boss>();

                if (boss == null)
                {
                    enemy = other.GetComponent<ICommonEnemy>();
                    if (enemy == null)
                        enemy = other.GetComponentInParent<ICommonEnemy>();
                }
            }
        }

        int id;
        if (player != null)
            id = player.GetInstanceID();
        else if (boss != null)
            id = boss.GetInstanceID();
        else if (enemy is Component enemyComponent)
            id = enemyComponent.GetInstanceID();
        else if (IsBossAttackToolCollider(other) || IsEnemyAttackToolCollider(other))
        {
            AttackHitbox tool = other.GetComponent<AttackHitbox>();
            if (tool == null)
                tool = other.GetComponentInParent<AttackHitbox>();
            id = tool != null ? tool.GetInstanceID() : other.GetInstanceID();
        }
        else if (other.transform.root != null)
            id = other.transform.root.GetInstanceID();
        else
            id = other.GetInstanceID();

        if (!hitInstanceIds.Add(id))
            return;

        if (dealDamage && damage > 0)
        {
            if (player != null && player != ownerPlayer)
            {
                int before = player.CurrentHealth;
                player.TakeDamage(damage, ownerRoot != null ? ownerRoot : transform);
                NotifyOwnerDamageDealt(before - player.CurrentHealth);
            }
            else if (boss != null && boss != ownerBoss)
            {
                if (ownerBoss != null && ownerBoss.IsCopyBot && boss.IsCopyBot)
                    return;

                int before = boss.CurrentHealth;
                boss.TakeDamage(damage);
                NotifyOwnerDamageDealt(before - boss.CurrentHealth);
            }
            else if (enemy != null && enemy != ownerCommonEnemy)
            {
                // Common enemies must never friendly-fire each other when attack boxes overlap in clusters.
                if (ownerCommonEnemy != null)
                    return;

                int before = enemy.CurrentHealth;
                enemy.TakeDamage(damage);
                NotifyOwnerDamageDealt(before - enemy.CurrentHealth);
            }
            else
            {
                IDamageable damageable = other.GetComponent<IDamageable>();
                if (damageable == null)
                    damageable = other.GetComponentInParent<IDamageable>();

                if (damageable != null &&
                    damageable is not Crystal &&
                    damageable is not Boss &&
                    damageable is not PlayerController &&
                    damageable is not ICommonEnemy)
                {
                    damageable.TakeDamage(damage);
                }
            }
        }

        NotifyOwnerMeleeContact(other, player, boss, enemy, crystal);

        OnTargetAcquired?.Invoke(other, player);
    }

    private void NotifyOwnerMeleeContact(
        Collider2D other,
        PlayerController player,
        Boss boss,
        ICommonEnemy enemy,
        Crystal crystal)
    {
        if (ownerPlayer is not HarliePlayerController harlie || other == null)
            return;

        bool bossAttackTool = IsBossAttackToolCollider(other);
        bool enemyAttackTool = IsEnemyAttackToolCollider(other);
        bool hitProjectile = other.GetComponent<Projectile>() != null
            || other.GetComponentInParent<Projectile>() != null;
        bool meaningful = boss != null || enemy != null || crystal != null || player != null
            || bossAttackTool || enemyAttackTool || hitProjectile;
        if (!meaningful)
            return;

        harlie.NotifyMeleeContact(other, bossAttackTool || enemyAttackTool);
    }

    private void NotifyOwnerDamageDealt(int amountDealt)
    {
        if (amountDealt <= 0)
            return;

        if (ownerPlayer is MalicePlayerController malice)
            malice.NotifyDamageDealt(amountDealt);
    }

    /// <summary>
    /// True when this collider belongs to a boss AttackHitbox (offensive tool), not the boss body.
    /// </summary>
    private static bool IsBossAttackToolCollider(Collider2D other)
    {
        if (other == null)
            return false;

        AttackHitbox box = other.GetComponent<AttackHitbox>();
        if (box == null)
            box = other.GetComponentInParent<AttackHitbox>();

        if (box == null)
            return false;

        Boss boss = box.GetComponentInParent<Boss>();
        if (boss == null)
            return false;

        // Body collider lives on the boss root; AttackBox is a child with AttackHitbox.
        return box.gameObject != boss.gameObject;
    }

    /// <summary>
    /// True when this collider belongs to a common enemy AttackHitbox (offensive tool), not the enemy body.
    /// </summary>
    private static bool IsEnemyAttackToolCollider(Collider2D other)
    {
        if (other == null)
            return false;

        AttackHitbox box = other.GetComponent<AttackHitbox>();
        if (box == null)
            box = other.GetComponentInParent<AttackHitbox>();

        if (box == null || box.OwnerCommonEnemy == null)
            return false;

        if (box.OwnerCommonEnemy is not Component ownerComponent)
            return false;

        return box.gameObject != ownerComponent.gameObject;
    }
}
