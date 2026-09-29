using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spinning hazard blade (Mega Man-style spikes, toned down). Spins in place forever,
/// deals contact damage through a trigger overlap, and phases through players / enemies.
/// Also phases the parent shredder-box solid collider so players cannot get stuck in it.
/// Place on blade objects inside a Shredder box.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class ShredderBlade : MonoBehaviour
{
    [Header("Spin")]
    [Tooltip("Degrees per second. Positive = counter-clockwise.")]
    [SerializeField] private float spinSpeed = 180f;

    [Header("Contact Damage")]
    [SerializeField] private int contactDamage = 4;
    [Tooltip("Minimum seconds between damage ticks on the same target.")]
    [SerializeField] private float damageInterval = 0.5f;
    [SerializeField] private Collider2D damageCollider;
    [SerializeField] private LayerMask hittableLayers = ~0;

    private readonly Dictionary<int, float> nextDamageTimeByTargetId = new Dictionary<int, float>(8);

    public int ContactDamage => contactDamage;
    public float SpinSpeed => spinSpeed;

    private void Awake()
    {
        EnsureTriggerCollider();
        DeferredEnemyPhaseRefresh.Request();
    }

    private void OnEnable()
    {
        DeferredEnemyPhaseRefresh.Request();
    }

    private void Start()
    {
        // Player may spawn after Awake — re-apply box + blade phasing once via the batch.
        DeferredEnemyPhaseRefresh.Request();
    }

    private void Update()
    {
        if (Mathf.Abs(spinSpeed) > 0.01f)
            transform.Rotate(0f, 0f, spinSpeed * HyperSpeedWorldSlow.WorldDeltaTime);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryDamage(other);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        TryDamage(other);
    }

    /// <summary>
    /// Solid colliders on this blade hierarchy (including the shredder box body) ignore
    /// players, bosses, and common enemies. Damage colliders stay triggers so overlap still hurts.
    /// </summary>
    public void RefreshPhaseCollisions()
    {
        Collider2D[] myCols = GetShredderHierarchyColliders();

        PlayerController player = PlayerController.ResolveActive();
        if (player != null)
            IgnoreSolidColliders(myCols, player.GetComponentsInChildren<Collider2D>(true));

        CrankyClanky[] enemies = EnemyTypeCache.Crankies;
        for (int i = 0; i < enemies.Length; i++)
        {
            CrankyClanky enemy = enemies[i];
            if (enemy == null)
                continue;

            IgnoreSolidColliders(myCols, enemy.GetComponentsInChildren<Collider2D>(true));
        }

        LaserBot[] lasers = EnemyTypeCache.Lasers;
        for (int i = 0; i < lasers.Length; i++)
        {
            if (lasers[i] == null)
                continue;
            IgnoreSolidColliders(myCols, lasers[i].GetComponentsInChildren<Collider2D>(true));
        }

        ChaoticTanker[] tankers = EnemyTypeCache.Tankers;
        for (int i = 0; i < tankers.Length; i++)
        {
            if (tankers[i] == null)
                continue;
            IgnoreSolidColliders(myCols, tankers[i].GetComponentsInChildren<Collider2D>(true));
        }

        ScrapNit[] scraps = EnemyTypeCache.Scraps;
        for (int i = 0; i < scraps.Length; i++)
        {
            if (scraps[i] == null)
                continue;
            IgnoreSolidColliders(myCols, scraps[i].GetComponentsInChildren<Collider2D>(true));
        }

        BlockerBot[] blockers = EnemyTypeCache.Blockers;
        for (int i = 0; i < blockers.Length; i++)
        {
            if (blockers[i] == null)
                continue;
            IgnoreSolidColliders(myCols, blockers[i].GetComponentsInChildren<Collider2D>(true));
        }

        Boss[] bosses = EnemyTypeCache.Bosses;
        for (int i = 0; i < bosses.Length; i++)
        {
            Boss boss = bosses[i];
            if (boss == null)
                continue;

            IgnoreSolidColliders(myCols, boss.GetComponentsInChildren<Collider2D>(true));
        }
    }

    /// <summary>
    /// Blades + parent shredder box solids (the box body is what players get stuck in).
    /// </summary>
    public Collider2D[] GetShredderHierarchyColliders()
    {
        Transform boxRoot = transform.parent != null ? transform.parent : transform;
        return boxRoot.GetComponentsInChildren<Collider2D>(true);
    }

    private void TryDamage(Collider2D other)
    {
        if (other == null || contactDamage <= 0)
            return;

        if (((1 << other.gameObject.layer) & hittableLayers) == 0)
            return;

        if (other.transform.IsChildOf(transform))
            return;

        // Detection radius is AI-only — never treat it as the enemy body.
        if (EnemyDetectionZone.IsDetectionOnlyCollider(other))
            return;

        PlayerController player = other.GetComponent<PlayerController>();
        if (player == null)
            player = other.GetComponentInParent<PlayerController>();

        if (player != null)
        {
            if (player.IsDead || player.IsInvincible)
                return;

            if (!CanDamageTarget(player.GetInstanceID()))
                return;

            int before = player.CurrentHealth;
            player.TakeDamage(contactDamage, transform);
            if (before > player.CurrentHealth)
                MarkTargetDamaged(player.GetInstanceID());
            return;
        }

        CrankyClanky enemy = other.GetComponent<CrankyClanky>();
        if (enemy == null)
            enemy = other.GetComponentInParent<CrankyClanky>();

        if (enemy != null && !enemy.IsDead)
        {
            if (!CanDamageTarget(enemy.GetInstanceID()))
                return;

            int before = enemy.CurrentHealth;
            enemy.TakeDamage(contactDamage);
            if (before > enemy.CurrentHealth)
                MarkTargetDamaged(enemy.GetInstanceID());
        }
    }

    private bool CanDamageTarget(int instanceId)
    {
        if (!nextDamageTimeByTargetId.TryGetValue(instanceId, out float nextTime))
            return true;

        return Time.time >= nextTime;
    }

    private void MarkTargetDamaged(int instanceId)
    {
        nextDamageTimeByTargetId[instanceId] = Time.time + Mathf.Max(0.05f, damageInterval);
    }

    private void EnsureTriggerCollider()
    {
        if (damageCollider == null)
            damageCollider = GetComponent<Collider2D>();

        if (damageCollider != null)
            damageCollider.isTrigger = true;
    }

    private static void IgnoreSolidColliders(Collider2D[] aCols, Collider2D[] bCols)
    {
        if (aCols == null || bCols == null)
            return;

        for (int i = 0; i < aCols.Length; i++)
        {
            Collider2D a = aCols[i];
            if (a == null || a.isTrigger)
                continue;

            for (int j = 0; j < bCols.Length; j++)
            {
                Collider2D b = bCols[j];
                if (b == null || b.isTrigger)
                    continue;

                Physics2D.IgnoreCollision(a, b, true);
            }
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        contactDamage = Mathf.Max(0, contactDamage);
        damageInterval = Mathf.Max(0.05f, damageInterval);

        if (damageCollider == null)
            damageCollider = GetComponent<Collider2D>();

        if (damageCollider != null)
            damageCollider.isTrigger = true;
    }
#endif
}
