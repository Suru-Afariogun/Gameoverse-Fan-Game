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
    private Transform ownerRoot;
    private int damage = 1;
    private bool active;
    private bool dealDamage = true;
    private readonly HashSet<int> hitInstanceIds = new HashSet<int>();

    public bool IsActive => active;
    public PlayerController Owner => ownerPlayer;
    public Boss OwnerBoss => ownerBoss;

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
        ownerRoot = newOwner != null ? newOwner.transform : null;
    }

    public void SetOwner(Boss newOwner)
    {
        ownerBoss = newOwner;
        ownerPlayer = null;
        ownerRoot = newOwner != null ? newOwner.transform : null;
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

    public void Deactivate()
    {
        active = false;
        hitInstanceIds.Clear();

        EnsureTriggerCollider();

        if (hitCollider != null)
            hitCollider.enabled = false;
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

        // Only playable Malice dissipates projectiles with her active melee / grapple box.
        // BossMalice (and any other owner) leaves shots alone.
        if (ownerPlayer is MalicePlayerController)
        {
            Projectile projectile = other.GetComponent<Projectile>();
            if (projectile == null)
                projectile = other.GetComponentInParent<Projectile>();

            if (projectile != null)
            {
                Destroy(projectile.gameObject);
                return;
            }
        }

        Crystal crystal = other.GetComponent<Crystal>();
        if (crystal == null)
            crystal = other.GetComponentInParent<Crystal>();
        if (crystal != null && !crystal.IsDead)
        {
            int crystalId = crystal.GetInstanceID();
            if (!hitInstanceIds.Add(crystalId))
                return;

            if (dealDamage && damage > 0)
                crystal.TakeDamage(damage);
            return;
        }

        PlayerController player = other.GetComponent<PlayerController>();
        if (player == null)
            player = other.GetComponentInParent<PlayerController>();

        Boss boss = null;
        if (player == null)
        {
            // Bosses take damage on their body collider only — never via their AttackBox tool.
            if (!IsBossAttackToolCollider(other))
            {
                boss = other.GetComponent<Boss>();
                if (boss == null)
                    boss = other.GetComponentInParent<Boss>();
            }
        }

        int id;
        if (player != null)
            id = player.GetInstanceID();
        else if (boss != null)
            id = boss.GetInstanceID();
        else if (other.transform.root != null)
            id = other.transform.root.GetInstanceID();
        else
            id = other.GetInstanceID();

        if (!hitInstanceIds.Add(id))
            return;

        if (dealDamage && damage > 0)
        {
            if (player != null && player != ownerPlayer)
                player.TakeDamage(damage);
            else if (boss != null && boss != ownerBoss)
                boss.TakeDamage(damage);
        }

        OnTargetAcquired?.Invoke(other, player);
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
}
