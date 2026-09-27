using UnityEngine;

/// <summary>
/// Common ground enemy: idle until the player enters the detection radius, then hop-chases with an active attack box.
/// Phases through players and other enemies (solid colliders ignored); triggers still detect and deal damage.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class CrankyClanky : MonoBehaviour, ICommonEnemy, IEnemyCrystalBoostable
{
    private static readonly int IdleStateHash = Animator.StringToHash("Idle");
    private static readonly int ChaseStateHash = Animator.StringToHash("Chase");
    private static readonly int JumpStateHash = Animator.StringToHash("Jump");

    [Header("Health")]
    [SerializeField] private int maxHealth = 3;
    [SerializeField] private int contactDamage = 1;

    [Header("Hop Chase")]
    [Tooltip("Seconds between hops while actively chasing.")]
    [SerializeField] private float hopInterval = 0.28f;
    [Tooltip("Maximum horizontal travel per hop (world units).")]
    [SerializeField] private float maxHopDistance = 6f;
    [Tooltip("Peak height when the player is above or below this enemy.")]
    [SerializeField] private float maxHopHeight = 4f;
    [Tooltip("Peak height when the player is roughly on the same Y level.")]
    [SerializeField] private float sameLevelMaxHopHeight = 1f;
    [Tooltip("Y difference treated as same level for the lower hop arc.")]
    [SerializeField] private float sameLevelYTolerance = 1.25f;
    [Tooltip("Maximum horizontal speed during a hop (world units / second).")]
    [SerializeField] private float maxHopSpeed = 14f;

    [Header("Hit Reaction")]
    [SerializeField] private float projectileStunDuration = 1f;
    [Tooltip("How long the red stunned VFX stays up on hits that do not cause a full stun.")]
    [SerializeField] private float hitVfxFlashDuration = 0.15f;
    [Tooltip("Same red Default Stunned prefab Malice uses.")]
    [SerializeField] private GameVisualEffect stunnedEffectPrefab;

    [Header("Detection")]
    [Tooltip("Uses the CircleCollider2D radius on the Detection Radius child when assigned.")]
    [SerializeField] private Transform detectionCenter;
    [SerializeField] private float detectionRadius = 9f;
    [SerializeField] private LayerMask playerLayers = ~0;

    [Header("Ground")]
    [SerializeField] private Transform groundCheck;
    [SerializeField] private float groundCheckRadius = 0.2f;
    [SerializeField] private LayerMask groundLayers;
    [SerializeField] private float fallMultiplier = 2.5f;
    [SerializeField] private float platformRescueSeconds = 4f;
    [Tooltip("Ignore damage briefly after spawn so shredder overlap / bad placement cannot insta-kill.")]
    [SerializeField] private float spawnGraceSeconds = 2f;

    [Header("References")]
    [SerializeField] private AttackHitbox attackHitbox;
    [SerializeField] private Transform explosionSpawnPoint;
    [SerializeField] private GameVisualEffect deathExplosionPrefab;
    [SerializeField] private CollectableCrystal collectableCrystalPrefab;
    [SerializeField] private int deathCollectableMin = 1;
    [SerializeField] private int deathCollectableMax = 5;
    [Header("Consumable Drops")]
    [SerializeField] private CollectableConsumable juiceBoxPickupPrefab;
    [SerializeField] private CollectableConsumable hotDogPickupPrefab;
    [SerializeField] [Range(0f, 1f)] private float consumableDropChance = 0.5f;
    [SerializeField] private Animator animator;
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private OrangePowerBoostAura enemyCrystalBoostAura;

    private Rigidbody2D rb;
    private Collider2D bodyCollider;
    private CircleCollider2D detectionCollider;
    private int currentHealth;
    private bool isChasing;
    private bool isGrounded;
    private bool isStunned;
    private int currentAnimStateHash;
    private float facingSign = 1f;
    private float hopTimer;
    private float stunTimer;
    private float hitVfxTimer;
    private float invincibilityTimer;
    private int lastDamageFrame = -1;
    private int enemyCrystalAttackBonus;
    private PlayerController targetPlayer;
    private float spawnTime;

    public int CurrentHealth => currentHealth;
    public int MaxHealth => maxHealth;
    public int ContactDamage => contactDamage;
    public bool IsDead => currentHealth <= 0;
    public bool IsChasing => isChasing;
    public bool IsStunned => isStunned;
    public bool IsInSpawnGrace => Time.time - spawnTime < spawnGraceSeconds;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        bodyCollider = GetComponent<Collider2D>();
        if (animator == null)
            animator = GetComponent<Animator>();
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();

        CacheChildReferences();
        ConfigureUtilityColliders();
        ConfigureAttackHitbox();

        currentHealth = Mathf.Max(1, maxHealth);
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;

        if (groundLayers.value == 0)
            groundLayers = LayerMask.GetMask("Ground");

        if (enemyCrystalBoostAura == null)
            enemyCrystalBoostAura = GetComponent<OrangePowerBoostAura>();
        if (enemyCrystalBoostAura == null)
            enemyCrystalBoostAura = gameObject.AddComponent<OrangePowerBoostAura>();
        if (enemyCrystalBoostAura != null && spriteRenderer != null)
            enemyCrystalBoostAura.Bind(spriteRenderer);
    }

    private void Start()
    {
        spawnTime = Time.time;
        PlatformEmbedRescue.TryResolveBadSpawnOverlap(transform, bodyCollider, groundLayers);
        DeferredEnemyPhaseRefresh.Request();
        PlayAnimationState(IdleStateHash);
    }

    private void OnEnable()
    {
        spawnTime = Time.time;
        DeferredEnemyPhaseRefresh.Request();
    }

    private void Update()
    {
        if (IsDead)
            return;

        ResolveTargetPlayer();
        UpdateDetection();
        TickHitReaction(HyperSpeedWorldSlow.WorldDeltaTime);
        UpdateAnimatorState();
        UpdateAttackHitbox();
    }

    private void FixedUpdate()
    {
        if (IsDead)
            return;

        if (Time.time - spawnTime <= platformRescueSeconds)
            PlatformEmbedRescue.TryResolveBadSpawnOverlap(transform, bodyCollider, groundLayers);

        UpdateGrounded();
        ApplyFallMultiplier();

        if (isStunned || !isChasing || targetPlayer == null)
        {
            if (isGrounded)
                StopHorizontal();
            return;
        }

        UpdateHopChase();
    }

    public void TakeDamage(int amount)
    {
        ApplyIncomingDamage(amount, stunFromProjectile: false);
    }

    public void TakeDamage(int amount, ProjectileShotType shotType)
    {
        bool stunFromProjectile = shotType == ProjectileShotType.Medium || shotType == ProjectileShotType.Big;
        ApplyIncomingDamage(amount, stunFromProjectile);
    }

    private void ApplyIncomingDamage(int amount, bool stunFromProjectile)
    {
        if (amount <= 0 || IsDead || invincibilityTimer > 0f || IsInSpawnGrace)
            return;

        int frame = Time.frameCount;
        if (frame == lastDamageFrame)
            return;

        lastDamageFrame = frame;
        currentHealth = Mathf.Max(0, currentHealth - amount);
        invincibilityTimer = Mathf.Max(invincibilityTimer, 0.25f);
        PlayHitVisual();
        SoundManager.Instance?.PlayCrankyClankyHit();

        if (currentHealth <= 0)
        {
            Die();
            return;
        }

        if (stunFromProjectile)
            BeginStun();
        else
            hitVfxTimer = Mathf.Max(0.05f, hitVfxFlashDuration);
    }

    /// <summary>
    /// Solid colliders ignore players and other common enemies; trigger tools still overlap.
    /// </summary>
    public void RefreshPhaseCollisions()
    {
        Collider2D[] myCols = GetComponentsInChildren<Collider2D>(true);

        PlayerController player = PlayerController.ResolveActive();
        if (player != null)
            IgnoreSolidColliders(myCols, player.GetComponentsInChildren<Collider2D>(true));

        CrankyClanky[] enemies = EnemyTypeCache.Crankies;
        for (int i = 0; i < enemies.Length; i++)
        {
            CrankyClanky other = enemies[i];
            if (other == null || other == this)
                continue;

            IgnoreSolidColliders(myCols, other.GetComponentsInChildren<Collider2D>(true));
        }

        Boss[] bosses = EnemyTypeCache.Bosses;
        for (int i = 0; i < bosses.Length; i++)
        {
            Boss boss = bosses[i];
            if (boss == null)
                continue;

            IgnoreSolidColliders(myCols, boss.GetComponentsInChildren<Collider2D>(true));
        }

        BlockerBot[] blockers = EnemyTypeCache.Blockers;
        for (int i = 0; i < blockers.Length; i++)
        {
            if (blockers[i] == null)
                continue;

            IgnoreSolidColliders(myCols, blockers[i].GetComponentsInChildren<Collider2D>(true));
        }

        LaserBot[] laserBots = EnemyTypeCache.Lasers;
        for (int i = 0; i < laserBots.Length; i++)
        {
            if (laserBots[i] == null)
                continue;

            IgnoreSolidColliders(myCols, laserBots[i].GetComponentsInChildren<Collider2D>(true));
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

        ShredderBlade[] shredderBlades = EnemyTypeCache.Shredders;
        for (int i = 0; i < shredderBlades.Length; i++)
        {
            ShredderBlade blade = shredderBlades[i];
            if (blade == null)
                continue;

            IgnoreSolidColliders(myCols, blade.GetComponentsInChildren<Collider2D>(true));
        }

        RefreshDetectionColliderPhasing();
    }

    private void RefreshDetectionColliderPhasing()
    {
        if (detectionCenter == null)
            return;

        EnemyDetectionZone zone = detectionCenter.GetComponent<EnemyDetectionZone>();
        if (zone == null)
            zone = detectionCenter.gameObject.AddComponent<EnemyDetectionZone>();

        if (!DeferredEnemyPhaseRefresh.IsBatchRunning)
            zone.RefreshPhaseCollisions();
    }

    private void CacheChildReferences()
    {
        if (detectionCenter == null)
        {
            Transform found = transform.Find("Detection Radius");
            if (found != null)
                detectionCenter = found;
        }

        if (detectionCenter != null)
            detectionCollider = detectionCenter.GetComponent<CircleCollider2D>();

        if (groundCheck == null)
        {
            Transform found = transform.Find("Ground Check");
            if (found != null)
                groundCheck = found;
        }

        if (explosionSpawnPoint == null)
        {
            Transform found = transform.Find("Explosion spawn box");
            if (found != null)
                explosionSpawnPoint = found;
        }

        if (attackHitbox == null)
        {
            Transform found = transform.Find("Attack Box");
            if (found != null)
            {
                attackHitbox = found.GetComponent<AttackHitbox>();
                if (attackHitbox == null)
                    attackHitbox = found.gameObject.AddComponent<AttackHitbox>();
            }
        }
    }

    private void ConfigureUtilityColliders()
    {
        if (detectionCenter != null)
        {
            if (detectionCenter.GetComponent<EnemyDetectionZone>() == null)
                detectionCenter.gameObject.AddComponent<EnemyDetectionZone>();

            SpriteRenderer detectionVisual = detectionCenter.GetComponent<SpriteRenderer>();
            if (detectionVisual != null)
                detectionVisual.enabled = false;
        }

        if (detectionCollider != null)
        {
            detectionRadius = detectionCollider.radius;
            detectionCollider.isTrigger = true;
        }

        Collider2D[] cols = GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            Collider2D col = cols[i];
            if (col == null || col == bodyCollider || col == detectionCollider)
                continue;

            if (attackHitbox != null && col.gameObject == attackHitbox.gameObject)
                continue;

            // Editor markers like Explosion spawn box should never block movement.
            col.enabled = false;
        }
    }

    private void ConfigureAttackHitbox()
    {
        if (attackHitbox == null)
            return;

        attackHitbox.SetOwner(this);
        attackHitbox.Deactivate();
    }

    private void ResolveTargetPlayer()
    {
        if (targetPlayer != null && !targetPlayer.IsDead)
            return;

        targetPlayer = PlayerController.ResolveActive();
    }

    private void UpdateDetection()
    {
        bool wasChasing = isChasing;
        isChasing = !isStunned && IsPlayerInsideDetectionRadius();

        if (!wasChasing && isChasing)
            hopTimer = 0f;

        if (wasChasing && !isChasing && isGrounded)
            StopHorizontal();
    }

    private bool IsPlayerInsideDetectionRadius()
    {
        if (targetPlayer == null || targetPlayer.IsDead)
            return false;

        Vector2 center = detectionCenter != null
            ? detectionCenter.position
            : transform.position;

        float radius = detectionCollider != null ? detectionCollider.radius : detectionRadius;
        float dist = Vector2.Distance(center, targetPlayer.transform.position);
        if (dist > radius)
            return false;

        if (playerLayers != ~0)
        {
            Collider2D playerCol = targetPlayer.GetComponent<Collider2D>();
            if (playerCol == null)
                playerCol = targetPlayer.GetComponentInChildren<Collider2D>();

            if (playerCol != null && ((1 << playerCol.gameObject.layer) & playerLayers) == 0)
                return false;
        }

        return true;
    }

    private void UpdateHopChase()
    {
        float deltaX = targetPlayer.transform.position.x - transform.position.x;
        if (Mathf.Abs(deltaX) > 0.05f)
            facingSign = Mathf.Sign(deltaX);

        ApplyFacingVisual();

        // Feet can still overlap ground for a frame after launch — keep hop velocity until rising.
        if (!isGrounded || rb.linearVelocity.y > 0.1f)
            return;

        hopTimer += HyperSpeedWorldSlow.WorldFixedDeltaTime;
        if (hopTimer < hopInterval)
        {
            StopHorizontal();
            return;
        }

        hopTimer = 0f;
        LaunchHop(Mathf.Abs(deltaX));
    }

    private void LaunchHop(float horizontalDistanceToPlayer)
    {
        float hopDistance = Mathf.Min(maxHopDistance, Mathf.Max(horizontalDistanceToPlayer, 0.75f));
        if (hopDistance <= 0.01f)
            return;

        float hopHeight = GetEffectiveHopHeight();
        float gravity = Mathf.Abs(Physics2D.gravity.y * rb.gravityScale);
        if (gravity <= 0.0001f)
            gravity = 9.81f;

        float verticalSpeed = Mathf.Sqrt(2f * gravity * hopHeight);
        float airTime = 2f * verticalSpeed / gravity;
        float horizontalSpeed = airTime > 0.0001f ? hopDistance / airTime : 0f;
        horizontalSpeed = Mathf.Min(maxHopSpeed, horizontalSpeed);

        // Scale launch by world time; ApplyFallMultiplier matches gravity with s² so
        // hop height stays while airtime stretches (slower, not lower).
        rb.linearVelocity = new Vector2(
            facingSign * HyperSpeedWorldSlow.ScaleSpeed(horizontalSpeed),
            HyperSpeedWorldSlow.ScaleSpeed(verticalSpeed));
        SoundManager.Instance?.PlayCrankyClankyHop();
    }

    private float GetEffectiveHopHeight()
    {
        if (targetPlayer == null)
            return maxHopHeight;

        float yDelta = Mathf.Abs(targetPlayer.transform.position.y - transform.position.y);
        if (yDelta <= sameLevelYTolerance)
            return sameLevelMaxHopHeight;

        return maxHopHeight;
    }

    private void StopHorizontal()
    {
        if (rb == null)
            return;

        float newVx = Mathf.MoveTowards(rb.linearVelocity.x, 0f, 24f * HyperSpeedWorldSlow.WorldFixedDeltaTime);
        rb.linearVelocity = new Vector2(newVx, rb.linearVelocity.y);
    }

    private void UpdateGrounded()
    {
        if (groundCheck == null)
        {
            isGrounded = false;
            return;
        }

        isGrounded = Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayers);
    }

    private void ApplyFallMultiplier()
    {
        if (rb == null || isGrounded)
            return;

        if (HyperSpeedWorldSlow.IsActive)
        {
            // With launch scaled by s, gravity must use s² or hops shrink and still feel snappy.
            float s = HyperSpeedWorldSlow.WorldTimeScale;
            float mult = Mathf.Clamp(s * s, 0.05f, 1f);
            rb.linearVelocity += Vector2.up * Physics2D.gravity.y * (mult - 1f) * Time.fixedDeltaTime;
            return;
        }

        if (rb.linearVelocity.y < 0f)
        {
            rb.linearVelocity += Vector2.up * Physics2D.gravity.y * (fallMultiplier - 1f) * Time.fixedDeltaTime;
        }
    }

    private void UpdateAnimatorState()
    {
        if (animator == null)
            return;

        animator.speed = HyperSpeedWorldSlow.IsActive ? HyperSpeedWorldSlow.WorldTimeScale : 1f;

        if (isStunned || !isChasing)
        {
            PlayAnimationState(IdleStateHash);
            return;
        }

        bool waitingBetweenHops = isGrounded && hopTimer > 0f && hopTimer < hopInterval;
        PlayAnimationState(isGrounded ? (waitingBetweenHops ? IdleStateHash : ChaseStateHash) : JumpStateHash);
    }

    private void PlayAnimationState(int stateHash)
    {
        if (animator == null || currentAnimStateHash == stateHash)
            return;

        if (!animator.HasState(0, stateHash))
            return;

        animator.Play(stateHash, 0, 0f);
        currentAnimStateHash = stateHash;
    }

    private void UpdateAttackHitbox()
    {
        if (attackHitbox == null)
            return;

        if (isChasing && !isStunned)
            attackHitbox.Activate(GetEffectiveContactDamage());
        else
            attackHitbox.Deactivate();
    }

    public void SetEnemyCrystalBoost(int attackBonus)
    {
        enemyCrystalAttackBonus = Mathf.Max(0, attackBonus);
        if (enemyCrystalBoostAura != null)
            enemyCrystalBoostAura.SetVisible(enemyCrystalAttackBonus > 0);
    }

    public void ClearEnemyCrystalBoost()
    {
        enemyCrystalAttackBonus = 0;
        if (enemyCrystalBoostAura != null)
            enemyCrystalBoostAura.SetVisible(false);
    }

    private int GetEffectiveContactDamage()
    {
        return Mathf.Max(0, contactDamage + enemyCrystalAttackBonus);
    }

    private void ApplyFacingVisual()
    {
        if (spriteRenderer != null)
            spriteRenderer.flipX = facingSign > 0f;
    }

    private void PlayHitVisual()
    {
        VisualEffects.PlayStunned(stunnedEffectPrefab, this);
    }

    private void BeginStun()
    {
        isStunned = true;
        stunTimer = Mathf.Max(0f, projectileStunDuration);
        invincibilityTimer = stunTimer;
        hitVfxTimer = 0f;
        hopTimer = 0f;

        if (rb != null)
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
    }

    private void TickHitReaction(float dt)
    {
        if (stunTimer > 0f)
        {
            stunTimer -= dt;
            if (stunTimer <= 0f)
            {
                stunTimer = 0f;
                if (isStunned)
                    EndStun();
            }
        }

        if (invincibilityTimer > 0f)
            invincibilityTimer = Mathf.Max(0f, invincibilityTimer - dt);

        if (hitVfxTimer > 0f && !isStunned)
        {
            hitVfxTimer -= dt;
            if (hitVfxTimer <= 0f)
            {
                hitVfxTimer = 0f;
                VisualEffects.StopStunned(this);
            }
        }
    }

    private void EndStun()
    {
        isStunned = false;
        stunTimer = 0f;
        VisualEffects.StopStunned(this);
    }

    private void Die()
    {
        isStunned = false;
        stunTimer = 0f;
        hitVfxTimer = 0f;
        ClearEnemyCrystalBoost();
        VisualEffects.StopStunned(this);

        Vector3 spawnPos = explosionSpawnPoint != null
            ? explosionSpawnPoint.position
            : transform.position;

        if (collectableCrystalPrefab != null)
            CollectableCrystal.SpawnRandomBurst(collectableCrystalPrefab, spawnPos, deathCollectableMin, deathCollectableMax);

        EnemyConsumableDrop.TrySpawnFromEnemy(spawnPos, juiceBoxPickupPrefab, hotDogPickupPrefab, consumableDropChance);

        if (deathExplosionPrefab != null)
            VisualEffects.SpawnEnemyExplosionSmallPurple(deathExplosionPrefab, spawnPos, spriteRenderer);

        SoundManager.Instance?.PlayCrankyClankyDeath();
        Destroy(gameObject);
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
        maxHealth = Mathf.Max(1, maxHealth);
        contactDamage = Mathf.Max(0, contactDamage);
        hopInterval = Mathf.Max(0.05f, hopInterval);
        maxHopDistance = Mathf.Max(0.1f, maxHopDistance);
        maxHopHeight = Mathf.Max(0.1f, maxHopHeight);
        sameLevelMaxHopHeight = Mathf.Max(0.1f, sameLevelMaxHopHeight);
        sameLevelYTolerance = Mathf.Max(0.1f, sameLevelYTolerance);
        maxHopSpeed = Mathf.Max(0.1f, maxHopSpeed);
        projectileStunDuration = Mathf.Max(0f, projectileStunDuration);
        hitVfxFlashDuration = Mathf.Max(0.05f, hitVfxFlashDuration);
        detectionRadius = Mathf.Max(0.1f, detectionRadius);
        groundCheckRadius = Mathf.Max(0.01f, groundCheckRadius);
        fallMultiplier = Mathf.Max(1f, fallMultiplier);
        deathCollectableMin = Mathf.Max(1, deathCollectableMin);
        deathCollectableMax = Mathf.Max(deathCollectableMin, deathCollectableMax);

        if (detectionCenter != null)
        {
            CircleCollider2D col = detectionCenter.GetComponent<CircleCollider2D>();
            if (col != null)
                col.isTrigger = true;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 center = detectionCenter != null ? detectionCenter.position : transform.position;
        float radius = detectionCollider != null ? detectionCollider.radius : detectionRadius;
        Gizmos.color = new Color(1f, 0.35f, 0.85f, 0.35f);
        Gizmos.DrawWireSphere(center, radius);

        if (groundCheck != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
        }
    }
#endif
}
