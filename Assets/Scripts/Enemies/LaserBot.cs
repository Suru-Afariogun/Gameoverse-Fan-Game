using UnityEngine;

/// <summary>
/// Stationary ceiling hazard-enemy: fires laser bullets straight down when the player
/// enters detection, keeps firing for a short grace after they leave, and bounces the
/// player on body contact without dealing contact damage.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public sealed class LaserBot : MonoBehaviour, ICommonEnemy
{
    private static readonly int IdleStateHash = Animator.StringToHash("LaserBot idle");
    private static readonly int AttackingStateHash = Animator.StringToHash("LaserBot attacking");
    private static readonly int IsAttackingHash = Animator.StringToHash("IsAttacking");

    [Header("Health")]
    [SerializeField] private int maxHealth = 4;

    [Header("Shooting")]
    [Tooltip("Seconds between laser pellets. Kit machine gun default is 0.12.")]
    [SerializeField] private float fireInterval = 0.08f;
    [SerializeField] private float stopShootingGraceSeconds = 2f;
    [SerializeField] private int bulletDamage = 1;
    [SerializeField] private float bulletSpeed = 16f;
    [SerializeField] private Projectile laserBulletPrefab;
    [SerializeField] private Transform firePoint;

    [Header("Hit Reaction")]
    [SerializeField] private float projectileStunDuration = 1f;
    [SerializeField] private GameVisualEffect hitSparkPrefab;
    [SerializeField] private Transform sparkSpawnPoint;

    [Header("Detection")]
    [SerializeField] private Transform detectionCenter;
    [SerializeField] private Collider2D detectionCollider;
    [SerializeField] private LayerMask playerLayers = ~0;

    [Header("Body Bounce")]
    [SerializeField] private AttackHitbox attackHitbox;
    [Tooltip("How far the player is shoved (world units / spaces).")]
    [SerializeField] private float bounceDistance = 1f;
    [SerializeField] private float bounceDuration = 0.18f;
    [SerializeField] private float bounceCooldown = 0.2f;

    [Header("Death")]
    [SerializeField] private Transform explosionSpawnPoint;
    [SerializeField] private GameVisualEffect deathExplosionPrefab;

    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private SpriteRenderer spriteRenderer;

    private Rigidbody2D rb;
    private Collider2D bodyCollider;
    private int currentHealth;
    private bool playerInRange;
    private bool isShooting;
    private bool isStunned;
    private float fireTimer;
    private float leaveRangeTimer;
    private float stunTimer;
    private float bounceCooldownTimer;
    private int lastDamageFrame = -1;
    private int currentAnimStateHash;
    private PlayerController targetPlayer;

    public int CurrentHealth => currentHealth;
    public int MaxHealth => maxHealth;
    public bool IsDead => currentHealth <= 0;
    public bool IsStunned => isStunned;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        bodyCollider = GetComponent<Collider2D>();
        if (animator == null)
            animator = GetComponent<Animator>();
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();

        CacheChildReferences();
        ConfigureBody();
        ConfigureUtilityColliders();
        ConfigureAttackHitbox();
        AimFirePointWorldDown();

        currentHealth = Mathf.Max(1, maxHealth);
    }

    private void Start()
    {
        HideSceneTemplates();
        DeferredEnemyPhaseRefresh.Request();
        PlayAnimationState(IdleStateHash);
        SetAttackingAnimator(false);
    }

    private void OnEnable()
    {
        DeferredEnemyPhaseRefresh.Request();
        if (attackHitbox != null)
        {
            attackHitbox.OnTargetAcquired -= OnBounceTargetAcquired;
            attackHitbox.OnTargetAcquired += OnBounceTargetAcquired;
            attackHitbox.Activate(0, applyDamage: false);
        }
    }

    private void OnDisable()
    {
        if (attackHitbox != null)
            attackHitbox.OnTargetAcquired -= OnBounceTargetAcquired;

        VisualEffects.StopStunned(this);
    }

    private void Update()
    {
        if (IsDead)
            return;

        float dt = HyperSpeedWorldSlow.WorldDeltaTime;
        ResolveTargetPlayer();
        UpdateDetection(dt);
        TickStun(dt);
        TickBounceCooldown(dt);
        UpdateShooting(dt);
        UpdateAnimator();
        RefreshBounceHitMemory();
    }

    private void LateUpdate()
    {
        // Keep FirePoint aimed world-down even if a parent/animator nudges it.
        if (!IsDead)
            AimFirePointWorldDown();
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
        if (amount <= 0 || IsDead)
            return;

        int frame = Time.frameCount;
        if (frame == lastDamageFrame)
            return;

        lastDamageFrame = frame;
        currentHealth = Mathf.Max(0, currentHealth - amount);
        PlayHitSpark();
        SoundManager.Instance?.PlayLaserBotHit();

        if (currentHealth <= 0)
        {
            Die();
            return;
        }

        if (stunFromProjectile)
            BeginStun();
    }

    public void RefreshPhaseCollisions()
    {
        Collider2D[] myCols = GetComponentsInChildren<Collider2D>(true);

        PlayerController player = PlayerController.ResolveActive();
        if (player != null)
            IgnoreSolidColliders(myCols, player.GetComponentsInChildren<Collider2D>(true));

        CrankyClanky[] crankies = EnemyTypeCache.Crankies;
        for (int i = 0; i < crankies.Length; i++)
        {
            CrankyClanky other = crankies[i];
            if (other == null)
                continue;

            IgnoreSolidColliders(myCols, other.GetComponentsInChildren<Collider2D>(true));
        }

        LaserBot[] bots = EnemyTypeCache.Lasers;
        for (int i = 0; i < bots.Length; i++)
        {
            LaserBot other = bots[i];
            if (other == null || other == this)
                continue;

            IgnoreSolidColliders(myCols, other.GetComponentsInChildren<Collider2D>(true));
        }

        BlockerBot[] blockers = EnemyTypeCache.Blockers;
        for (int i = 0; i < blockers.Length; i++)
        {
            if (blockers[i] == null)
                continue;

            IgnoreSolidColliders(myCols, blockers[i].GetComponentsInChildren<Collider2D>(true));
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

        Boss[] bosses = EnemyTypeCache.Bosses;
        for (int i = 0; i < bosses.Length; i++)
        {
            Boss boss = bosses[i];
            if (boss == null)
                continue;

            IgnoreSolidColliders(myCols, boss.GetComponentsInChildren<Collider2D>(true));
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
            if (found == null)
                found = transform.Find("Detection radius");
            if (found != null)
                detectionCenter = found;
        }

        if (detectionCenter != null && detectionCollider == null)
            detectionCollider = detectionCenter.GetComponent<Collider2D>();

        if (firePoint == null)
        {
            Transform found = transform.Find("FirePoint");
            if (found != null)
                firePoint = found;
        }

        if (sparkSpawnPoint == null)
        {
            Transform found = transform.Find("Spark Spawn Box");
            if (found != null)
                sparkSpawnPoint = found;
        }

        if (explosionSpawnPoint == null)
        {
            Transform found = transform.Find("Death Explosion Spawn Box");
            if (found == null)
                found = transform.Find("Death explosion Spawn Box");
            if (found == null)
                found = transform.Find("Death Explosion spawn box");
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

    private void ConfigureBody()
    {
        if (rb != null)
        {
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.simulated = true;
            rb.gravityScale = 0f;
            rb.constraints = RigidbodyConstraints2D.FreezeAll;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }

        if (bodyCollider != null)
            bodyCollider.isTrigger = false;
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
            detectionCollider.isTrigger = true;
    }

    private void ConfigureAttackHitbox()
    {
        if (attackHitbox == null)
            return;

        attackHitbox.SetOwner((ICommonEnemy)this);
        Collider2D col = attackHitbox.GetComponent<Collider2D>();
        if (col != null)
            col.isTrigger = true;
        attackHitbox.Activate(0, applyDamage: false);
    }

    private void AimFirePointWorldDown()
    {
        if (firePoint == null)
            return;

        // Local down (-up) = world down, so the FirePoint itself aims straight down.
        firePoint.up = Vector3.up;
    }

    private void HideSceneTemplates()
    {
        // Only hide scene-placed template copies — never toggle prefab assets.
        HideIfSceneObject(laserBulletPrefab != null ? laserBulletPrefab.gameObject : null);
        HideIfSceneObject(hitSparkPrefab != null ? hitSparkPrefab.gameObject : null);
        HideIfSceneObject(deathExplosionPrefab != null ? deathExplosionPrefab.gameObject : null);
    }

    private static void HideIfSceneObject(GameObject go)
    {
        if (go == null || !go.scene.IsValid())
            return;

        go.SetActive(false);
    }

    private void ResolveTargetPlayer()
    {
        if (targetPlayer != null && !targetPlayer.IsDead)
            return;

        targetPlayer = PlayerController.ResolveActive();
    }

    private void UpdateDetection(float dt)
    {
        bool inRange = !isStunned && IsPlayerInsideDetection();

        if (inRange)
        {
            playerInRange = true;
            leaveRangeTimer = 0f;
            if (!isShooting)
            {
                isShooting = true;
                fireTimer = 0f;
                FireLaser();
            }
        }
        else if (isShooting)
        {
            if (playerInRange)
            {
                playerInRange = false;
                leaveRangeTimer = 0f;
            }

            leaveRangeTimer += dt;
            if (leaveRangeTimer >= stopShootingGraceSeconds)
            {
                isShooting = false;
                fireTimer = 0f;
            }
        }
        else
        {
            playerInRange = false;
        }
    }

    private bool IsPlayerInsideDetection()
    {
        if (targetPlayer == null || targetPlayer.IsDead)
            return false;

        if (detectionCollider != null)
        {
            Collider2D playerCol = targetPlayer.GetComponent<Collider2D>();
            if (playerCol == null)
                playerCol = targetPlayer.GetComponentInChildren<Collider2D>();

            if (playerCol != null)
            {
                if (playerLayers != ~0 &&
                    ((1 << playerCol.gameObject.layer) & playerLayers) == 0)
                    return false;

                return detectionCollider.bounds.Intersects(playerCol.bounds);
            }

            return detectionCollider.bounds.Contains(targetPlayer.transform.position);
        }

        Vector2 center = detectionCenter != null ? detectionCenter.position : transform.position;
        return Vector2.Distance(center, targetPlayer.transform.position) <= 3f;
    }

    private void UpdateShooting(float dt)
    {
        if (!isShooting || isStunned || IsDead)
            return;

        fireTimer += dt;
        float interval = Mathf.Max(0.05f, fireInterval);
        while (fireTimer >= interval)
        {
            fireTimer -= interval;
            FireLaser();
        }
    }

    private void FireLaser()
    {
        if (laserBulletPrefab == null || firePoint == null)
            return;

        AimFirePointWorldDown();

        Vector3 spawnPos = firePoint.position;
        // Travel along the FirePoint's local down.
        Vector2 shootDir = -(Vector2)firePoint.up;
        if (shootDir.sqrMagnitude < 0.001f)
            shootDir = Vector2.down;
        else
            shootDir.Normalize();

        // Spawn already matching FirePoint aim (vertical laser art faces along ±Y at this rotation).
        Projectile shot = Instantiate(laserBulletPrefab, spawnPos, firePoint.rotation);
        shot.gameObject.SetActive(true);

        Collider2D shotCol = shot.GetComponent<Collider2D>();
        if (shotCol != null)
            shotCol.isTrigger = true;

        shot.Launch(shootDir, bulletSpeed, Mathf.Max(1, bulletDamage), transform);
        // Laser bullet sprite is drawn vertically; Launch's +X Atan2 would yaw it sideways.
        shot.transform.rotation = firePoint.rotation;
        SoundManager.Instance?.PlayLaserBotShot();
    }

    private void UpdateAnimator()
    {
        bool attacking = isShooting && !isStunned && !IsDead;
        SetAttackingAnimator(attacking);
        PlayAnimationState(attacking ? AttackingStateHash : IdleStateHash);
    }

    private void SetAttackingAnimator(bool attacking)
    {
        if (animator == null)
            return;

        animator.SetBool(IsAttackingHash, attacking);
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

    private void PlayHitSpark()
    {
        VisualEffects.PlayBriefHitSpark(hitSparkPrefab, this, sparkSpawnPoint, 0.15f);
    }

    private void BeginStun()
    {
        isStunned = true;
        stunTimer = Mathf.Max(0f, projectileStunDuration);
        // Stunned bots pause the current burst; detection must re-arm after stun.
        isShooting = false;
        playerInRange = false;
        fireTimer = 0f;
        leaveRangeTimer = 0f;
    }

    private void TickStun(float dt)
    {
        if (stunTimer <= 0f)
            return;

        stunTimer -= dt;
        if (stunTimer > 0f)
            return;

        stunTimer = 0f;
        isStunned = false;
        VisualEffects.StopStunned(this);
    }

    private void TickBounceCooldown(float dt)
    {
        if (bounceCooldownTimer > 0f)
            bounceCooldownTimer = Mathf.Max(0f, bounceCooldownTimer - dt);
    }

    private void RefreshBounceHitMemory()
    {
        if (attackHitbox == null || bounceCooldownTimer > 0f || targetPlayer == null)
            return;

        attackHitbox.ForgetHitInstance(targetPlayer.GetInstanceID());
    }

    private void OnBounceTargetAcquired(Collider2D other, PlayerController player)
    {
        if (player == null || player.IsDead || bounceCooldownTimer > 0f || IsDead)
            return;

        player.ApplySoftBounce(transform, bounceDistance, bounceDuration);
        bounceCooldownTimer = Mathf.Max(0.05f, bounceCooldown);
    }

    private void Die()
    {
        isStunned = false;
        stunTimer = 0f;
        isShooting = false;
        VisualEffects.StopStunned(this);

        Vector3 spawnPos = explosionSpawnPoint != null
            ? explosionSpawnPoint.position
            : transform.position;

        if (deathExplosionPrefab != null)
            VisualEffects.SpawnEnemyExplosionSmallPurple(deathExplosionPrefab, spawnPos, spriteRenderer);

        SoundManager.Instance?.PlayLaserBotDeath();
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
        fireInterval = Mathf.Max(0.05f, fireInterval);
        stopShootingGraceSeconds = Mathf.Max(0f, stopShootingGraceSeconds);
        bulletDamage = Mathf.Max(1, bulletDamage);
        bulletSpeed = Mathf.Max(0.1f, bulletSpeed);
        bounceDistance = Mathf.Max(0f, bounceDistance);
        bounceDuration = Mathf.Max(0.05f, bounceDuration);
        bounceCooldown = Mathf.Max(0.05f, bounceCooldown);
        projectileStunDuration = Mathf.Max(0f, projectileStunDuration);
    }
#endif
}
