using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Big Heat Ball (Heat Flare Ball art) — 5 damage, flies slowly 7 spaces, phasable, HP 8.
/// Destroyed by enemy contact or player shots.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class BassHeatFlareBall : MonoBehaviour, IDamageable
{
    [SerializeField] private float damage = 5f;
    [SerializeField] private float travelSpaces = 7f;
    [SerializeField] private float flySpeed = 3.2f;
    [SerializeField] private int maxHealth = 8;

    private Collider2D hitCollider;
    private SpriteRenderer spriteRenderer;
    private SortingGroup hostGroup;
    private SpriteRenderer hostBody;
    private Vector2 direction;
    private float spaceSize = 1f;
    private float traveled;
    private int currentHealth;
    private bool launched;
    private bool hostileToPlayer;
    private float hostileDamageScale = 1f;
    private readonly Collider2D[] overlaps = new Collider2D[12];
    private readonly System.Collections.Generic.HashSet<int> hitIds =
        new System.Collections.Generic.HashSet<int>();

    public bool IsDead => currentHealth <= 0;
    public int CurrentHealth => currentHealth;

    private void Awake()
    {
        hitCollider = GetComponent<Collider2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (hitCollider != null)
        {
            hitCollider.isTrigger = true;
            EnemyDetectionZone.IgnoreAgainstAllZones(hitCollider);
        }
    }

    public void ConfigureHostSorting(SortingGroup group, SpriteRenderer body)
    {
        hostGroup = group;
        hostBody = body;
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();
        CharacterEffectSorting.ApplyDetachedEffectBehindHost(spriteRenderer, hostGroup, hostBody);
    }

    /// <summary>Boss Bass heat balls hurt the player (and ignore bosses / enemies).</summary>
    public void SetHostile(bool hostile, float damageScale = 1f)
    {
        hostileToPlayer = hostile;
        hostileDamageScale = Mathf.Max(0f, damageScale);
    }

    public void Launch(Vector2 dir, float worldSpace)
    {
        direction = dir.sqrMagnitude > 0.001f ? dir.normalized : Vector2.right;
        spaceSize = Mathf.Max(0.01f, worldSpace);
        traveled = 0f;
        currentHealth = Mathf.Max(1, maxHealth);
        launched = true;
        CharacterEffectSorting.ApplyDetachedEffectBehindHost(spriteRenderer, hostGroup, hostBody);
    }

    public void TakeDamage(int amount)
    {
        if (!launched || IsDead || amount <= 0)
            return;

        currentHealth = Mathf.Max(0, currentHealth - amount);
        if (IsDead)
            Destroy(gameObject);
    }

    private void Update()
    {
        if (!launched || IsDead)
            return;

        float step = flySpeed * Time.deltaTime;
        transform.position += (Vector3)(direction * step);
        traveled += step;

        DealPassDamage();

        if (traveled >= travelSpaces * spaceSize)
            Destroy(gameObject);
    }

    private void DealPassDamage()
    {
        if (hitCollider == null)
            return;

        ContactFilter2D filter = new ContactFilter2D();
        filter.useTriggers = true;
        filter.NoFilter();
        int count = Physics2D.OverlapCollider(hitCollider, filter, overlaps);
        for (int i = 0; i < count; i++)
        {
            Collider2D other = overlaps[i];
            if (other == null)
                continue;

            if (hostileToPlayer)
            {
                PlayerController player = other.GetComponentInParent<PlayerController>();
                if (player != null && !player.IsDead && hitIds.Add(player.GetInstanceID()))
                    BassCrashHeatCombat.ApplyDamageOnly(other, damage * hostileDamageScale, false, true, transform);
                continue;
            }

            // Enemy contact damages the ball AND the enemy.
            ICommonEnemy enemy = other.GetComponentInParent<ICommonEnemy>();
            if (enemy != null && !enemy.IsDead)
            {
                Component host = enemy as Component;
                int id = host.GetInstanceID();
                if (hitIds.Add(id))
                    BassCrashHeatCombat.ApplyDamageOnly(other, damage);

                TakeDamage(1);
                if (IsDead)
                    return;
                continue;
            }

            Boss boss = other.GetComponentInParent<Boss>();
            if (boss != null && !boss.IsDead)
            {
                int id = boss.GetInstanceID();
                if (hitIds.Add(id))
                    BassCrashHeatCombat.ApplyDamageOnly(other, damage);

                TakeDamage(1);
                if (IsDead)
                    return;
            }
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!launched || IsDead || other == null)
            return;

        // Player projectiles destroy / damage the ball.
        Projectile shot = other.GetComponentInParent<Projectile>();
        if (shot != null && shot.Owner != null && shot.Owner.GetComponentInParent<PlayerController>() != null)
        {
            TakeDamage(Mathf.Max(1, Mathf.CeilToInt(shot.Damage)));
            return;
        }
    }
}
