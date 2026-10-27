using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// One leaf of Bass's Metal Leaf shield ring. Orbits like Hex V-Bots, blocks enemy projectiles
/// (Bass's own blades/shots pass through), takes projectile damage to its own HP, and deals
/// light contact damage to enemies.
/// </summary>
public class BassLeafShield : MonoBehaviour
{
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private int maxHealth = 15;
    [SerializeField] private int contactDamage = 1;
    [SerializeField] private float orbitRehitSeconds = 0.45f;
    [SerializeField] private Vector2 hitboxScale = new Vector2(0.9f, 0.9f);
    [SerializeField] private LayerMask hitLayers = ~0;
    [SerializeField] private int sortingOffset = 2;

    private static readonly List<Collider2D> Overlaps = new List<Collider2D>(24);
    private readonly Dictionary<int, float> contactHitTimes = new Dictionary<int, float>();

    private Transform owner;
    private PlayerController ownerPlayer;
    private Boss ownerBoss;
    private Collider2D ownerBody;
    private float orbitAngle;
    private float orbitRadius;
    private float orbitDegreesPerSecond;
    private float health;
    private bool active;
    private Collider2D leafCollider;

    public bool IsAlive => active && health > 0f;
    public float Health => health;

    private void Awake()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        leafCollider = GetComponent<Collider2D>();
        if (leafCollider != null)
            leafCollider.isTrigger = true;

        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        }
    }

    public void BeginOrbit(
        Transform summoner,
        float startAngleDegrees,
        float radius,
        float degreesPerSecond,
        int hp,
        int touchDamage)
    {
        owner = summoner;
        ownerPlayer = summoner != null ? summoner.GetComponentInParent<PlayerController>() : null;
        // Boss Bass shield: blocks the player's shots, touches the player, lets his own shots through.
        ownerBoss = summoner != null ? summoner.GetComponentInParent<Boss>() : null;
        if (ownerPlayer != null)
        {
            ownerBody = ownerPlayer.BodyCollider != null
                ? ownerPlayer.BodyCollider
                : ownerPlayer.GetComponent<Collider2D>();
        }
        else if (ownerBoss != null)
        {
            // Prefer the boss body collider so the ring orbits his sprite center, not a child trigger.
            ownerBody = ownerBoss.GetComponent<Collider2D>();
            if (ownerBody == null)
                ownerBody = summoner.GetComponentInChildren<Collider2D>();
        }
        else if (summoner != null)
        {
            ownerBody = summoner.GetComponent<Collider2D>();
        }

        orbitAngle = startAngleDegrees;
        orbitRadius = Mathf.Max(0.1f, radius);
        orbitDegreesPerSecond = degreesPerSecond;
        health = Mathf.Max(1, hp);
        maxHealth = Mathf.Max(1, hp);
        contactDamage = Mathf.Max(0, touchDamage);
        contactHitTimes.Clear();
        active = true;

        if (leafCollider != null)
            leafCollider.isTrigger = true;

        PlaceOnOrbit();
        ApplySorting();
    }

    private void Update()
    {
        if (!active)
            return;

        if (owner == null || (ownerPlayer != null && ownerPlayer.IsDead) || (ownerBoss != null && ownerBoss.IsDead))
        {
            DestroyLeaf();
            return;
        }

        float dt = Time.deltaTime;
        if (HyperSpeedWorldSlow.IsActive && !HyperSpeedWorldSlow.SlowsPlayer)
            dt = Time.deltaTime; // Bass-owned: full speed during world slow
        else if (HyperSpeedWorldSlow.IsActive)
            dt = HyperSpeedWorldSlow.WorldDeltaTime;

        orbitAngle = Mathf.Repeat(orbitAngle + orbitDegreesPerSecond * dt, 360f);
        PlaceOnOrbit();
        DetectEnemyContact();
    }

    private void PlaceOnOrbit()
    {
        Vector3 center = ownerBody != null
            ? ownerBody.bounds.center
            : (owner != null ? owner.position : transform.position);
        float rad = orbitAngle * Mathf.Deg2Rad;
        transform.position = new Vector3(
            center.x + Mathf.Cos(rad) * orbitRadius,
            center.y + Mathf.Sin(rad) * orbitRadius,
            0f);
        transform.rotation = Quaternion.identity;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!active || other == null)
            return;

        Projectile shot = other.GetComponentInParent<Projectile>();
        if (shot == null)
            return;

        // Bass's own shots (blades / buster) pass through the leaf ring.
        if (ownerBoss != null)
        {
            // Boss Bass: only his own shots pass; the player's shots (even Bass's) are blocked.
            if (shot.Owner != null && shot.Owner.GetComponentInParent<Boss>() != null)
                return;
        }
        else if (IsOwnedByBass(shot))
        {
            return;
        }

        if (shot.IsResolvingHit || !shot.IsLaunched)
            return;

        health -= Mathf.Max(0f, shot.Damage);
        shot.ClashAndDespawn();

        if (health <= 0f)
            DestroyLeaf();
    }

    private static bool IsOwnedByBass(Projectile shot)
    {
        if (shot == null || shot.Owner == null)
            return false;
        if (shot.GetComponent<BassMetalBlade>() != null)
            return true;
        return shot.Owner.GetComponentInParent<BassPlayerController>() != null;
    }

    private void DetectEnemyContact()
    {
        if (spriteRenderer == null || contactDamage <= 0)
            return;

        Bounds b = spriteRenderer.bounds;
        Vector2 size = new Vector2(b.size.x * hitboxScale.x, b.size.y * hitboxScale.y);
        if (size.x <= 0.001f || size.y <= 0.001f)
            return;

        ContactFilter2D filter = new ContactFilter2D
        {
            useTriggers = true
        };
        filter.SetLayerMask(hitLayers);

        Overlaps.Clear();
        Physics2D.OverlapBox(b.center, size, 0f, filter, Overlaps);
        for (int i = 0; i < Overlaps.Count; i++)
            TryContactHit(Overlaps[i]);
    }

    private void TryContactHit(Collider2D other)
    {
        if (other == null || owner == null)
            return;
        if (other.transform == owner || other.transform.IsChildOf(owner))
            return;
        if (EnemyDetectionZone.IsDetectionOnlyCollider(other) || other.GetComponent<AttackHitbox>() != null)
            return;
        if (other.GetComponentInParent<Projectile>() != null)
            return;
        if (other.GetComponentInParent<BassLeafShield>() != null)
            return;

        if (ownerBoss != null)
        {
            // Boss Bass leaves only scrape the player.
            PlayerController touched = other.GetComponentInParent<PlayerController>();
            if (touched != null && !touched.IsDead && Claim(touched.GetInstanceID()))
                touched.TakeDamage(contactDamage, owner);
            return;
        }

        Crystal crystal = other.GetComponentInParent<Crystal>();
        if (crystal != null)
        {
            if (!crystal.IsDead && Claim(crystal.GetInstanceID()))
                crystal.TakeDamage(contactDamage);
            return;
        }

        Boss boss = other.GetComponentInParent<Boss>();
        if (boss != null)
        {
            if (!boss.IsDead && Claim(boss.GetInstanceID()))
                boss.TakeDamage(contactDamage);
            return;
        }

        ICommonEnemy enemy = other.GetComponentInParent<ICommonEnemy>();
        if (enemy is Component enemyComponent)
        {
            if (!enemy.IsDead && Claim(enemyComponent.GetInstanceID()))
                enemy.TakeDamage(contactDamage);
        }
    }

    private bool Claim(int targetId)
    {
        if (contactHitTimes.TryGetValue(targetId, out float last) && Time.time - last < orbitRehitSeconds)
            return false;
        contactHitTimes[targetId] = Time.time;
        return true;
    }

    private void ApplySorting()
    {
        if (spriteRenderer == null)
            return;
        if (ownerPlayer == null && ownerBoss == null)
            return;
        BassBossHostility.ResolveHost(owner, out SpriteRenderer hostBody, out SortingGroup hostGroup);
        CharacterEffectSorting.ApplyDetachedEffectNearHost(
            spriteRenderer,
            hostGroup,
            hostBody,
            sortingOffset);
    }

    public void DestroyLeaf()
    {
        active = false;
        Destroy(gameObject);
    }
}
