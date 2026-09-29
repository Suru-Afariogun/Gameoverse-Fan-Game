using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stage enemy crystal: boosts nearby common enemies with the boss-style orange power aura,
/// then shatters into collectable currency shards when destroyed.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class EnemyCrystal : MonoBehaviour, IDamageable
{
    [Header("Health")]
    [SerializeField] private int maxHealth = 8;

    [Header("Enemy Boost")]
    [SerializeField] private float boostRadius = 3f;
    [SerializeField] private int orangeAttackBonus = 2;

    [Header("Shatter")]
    [SerializeField] private CollectableCrystal collectableCrystalPrefab;
    [SerializeField] private int shatterCollectableCount = 4;

    [Header("Placement")]
    [SerializeField] private LayerMask groundLayers;
    [SerializeField] private float platformRescueSeconds = 4f;
    [Tooltip("Ignore damage briefly after spawn so stray shots / bad placement cannot insta-break.")]
    [SerializeField] private float spawnGraceSeconds = 1.5f;

    private int currentHealth;
    private Collider2D bodyCollider;
    private float hitInvincibilityTimer;
    private int lastDamageFrame = -1;
    private float spawnTime;
    private readonly HashSet<IEnemyCrystalBoostable> boostedEnemies = new HashSet<IEnemyCrystalBoostable>();

    public int CurrentHealth => currentHealth;
    public bool IsDead => currentHealth <= 0;
    public bool IsInSpawnGrace => Time.time - spawnTime < spawnGraceSeconds;

    private void Awake()
    {
        currentHealth = Mathf.Max(1, maxHealth);
        bodyCollider = GetComponent<Collider2D>();
        EnsurePhaseThroughCollider();

        if (groundLayers.value == 0)
            groundLayers = LayerMask.GetMask("Ground");
    }

    private void Start()
    {
        spawnTime = Time.time;
        PlatformEmbedRescue.TryResolveBadSpawnOverlap(transform, bodyCollider, groundLayers);
    }

    private void OnEnable()
    {
        spawnTime = Time.time;
    }

    private void FixedUpdate()
    {
        if (IsDead)
            return;

        if (Time.time - spawnTime <= platformRescueSeconds)
            PlatformEmbedRescue.TryResolveBadSpawnOverlap(transform, bodyCollider, groundLayers);
    }

    private void OnDisable()
    {
        ClearAllBoosts();
    }

    private void Update()
    {
        if (IsDead)
            return;

        if (hitInvincibilityTimer > 0f)
            hitInvincibilityTimer = Mathf.Max(0f, hitInvincibilityTimer - Time.deltaTime);

        RefreshBoostedEnemies();
    }

    public void TakeDamage(int amount)
    {
        if (amount <= 0 || IsDead || IsInSpawnGrace || hitInvincibilityTimer > 0f)
            return;

        int frame = Time.frameCount;
        if (frame == lastDamageFrame)
            return;

        lastDamageFrame = frame;
        currentHealth = Mathf.Max(0, currentHealth - amount);
        hitInvincibilityTimer = 0.25f;
        if (currentHealth > 0)
        {
            SoundManager.Instance?.PlayCrystalHit();
            return;
        }

        ShatterAndDestroy();
    }

    private void RefreshBoostedEnemies()
    {
        var stillInside = new HashSet<IEnemyCrystalBoostable>();

        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, boostRadius);
        for (int i = 0; i < hits.Length; i++)
        {
            Collider2D hit = hits[i];
            if (hit == null)
                continue;

            IEnemyCrystalBoostable boostable = hit.GetComponent<IEnemyCrystalBoostable>();
            if (boostable == null)
                boostable = hit.GetComponentInParent<IEnemyCrystalBoostable>();

            if (boostable == null || boostable is not Component component || !component.gameObject.activeInHierarchy)
                continue;

            if (boostable is IDamageable damageable && damageable.IsDead)
                continue;

            stillInside.Add(boostable);
            if (boostedEnemies.Add(boostable))
                boostable.SetEnemyCrystalBoost(orangeAttackBonus);
        }

        if (boostedEnemies.Count == 0)
            return;

        var toRemove = new List<IEnemyCrystalBoostable>();
        foreach (IEnemyCrystalBoostable boosted in boostedEnemies)
        {
            if (boosted == null || !stillInside.Contains(boosted))
                toRemove.Add(boosted);
        }

        for (int i = 0; i < toRemove.Count; i++)
            RemoveBoost(toRemove[i]);
    }

    private void ShatterAndDestroy()
    {
        Vector3 center = bodyCollider != null ? bodyCollider.bounds.center : transform.position;
        ClearAllBoosts();

        if (collectableCrystalPrefab != null && shatterCollectableCount > 0)
            CollectableCrystal.SpawnBurst(collectableCrystalPrefab, center, shatterCollectableCount);

        SoundManager.Instance?.PlayCrystalShatter();
        Destroy(gameObject);
    }

    private void ClearAllBoosts()
    {
        foreach (IEnemyCrystalBoostable boosted in boostedEnemies)
            RemoveBoost(boosted, skipSetMutation: true);

        boostedEnemies.Clear();
    }

    private void RemoveBoost(IEnemyCrystalBoostable boostable, bool skipSetMutation = false)
    {
        if (boostable == null)
            return;

        boostable.ClearEnemyCrystalBoost();
        if (!skipSetMutation)
            boostedEnemies.Remove(boostable);
    }

    private void EnsurePhaseThroughCollider()
    {
        if (bodyCollider == null)
            bodyCollider = GetComponent<Collider2D>();

        if (bodyCollider == null)
            return;

        bodyCollider.isTrigger = true;

        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.simulated = true;
            rb.useFullKinematicContacts = false;
            rb.gravityScale = 0f;
        }
        else
        {
            rb = gameObject.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.simulated = true;
            rb.useFullKinematicContacts = false;
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        maxHealth = Mathf.Max(1, maxHealth);
        boostRadius = Mathf.Max(0.1f, boostRadius);
        orangeAttackBonus = Mathf.Max(0, orangeAttackBonus);
        shatterCollectableCount = Mathf.Max(1, shatterCollectableCount);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.45f, 0.1f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, boostRadius);
    }
#endif
}
