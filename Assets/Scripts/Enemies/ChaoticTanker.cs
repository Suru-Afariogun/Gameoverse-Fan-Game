using UnityEngine;

/// <summary>
/// Stationary dual-cannon enemy: faces the player and alternates FirePoint1 / FirePoint2
/// Big Red Volt shots while detected (1s grace after leave). Phasable, stunnable, no i-frames.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public sealed class ChaoticTanker : MonoBehaviour, ICommonEnemy
{
    [Header("Health")]
    [SerializeField] private int maxHealth = 8;

    [Header("Shooting")]
    [SerializeField] private float shotInterval = 0.8f;
    [SerializeField] private float stopShootingGraceSeconds = 1f;
    [SerializeField] private int voltDamage = 2;
    [SerializeField] private float voltSpeed = 9f;
    [SerializeField] private float voltTravelDistance = 8f;
    [SerializeField] private Projectile voltShotPrefab;
    [SerializeField] private Transform firePoint1;
    [SerializeField] private Transform firePoint2;

    [Header("Hit Reaction")]
    [SerializeField] private float projectileStunDuration = 1f;
    [SerializeField] private GameVisualEffect hitSparkPrefab;
    [SerializeField] private Transform sparkSpawnPoint;

    [Header("Detection")]
    [SerializeField] private Transform detectionCenter;
    [SerializeField] private Collider2D detectionCollider;
    [SerializeField] private LayerMask playerLayers = ~0;

    [Header("Death / Drops")]
    [SerializeField] private Transform explosionSpawnPoint;
    [SerializeField] private GameVisualEffect deathExplosionPrefab;
    [SerializeField] private CollectableCrystal collectableCrystalPrefab;
    [SerializeField] [Range(0f, 1f)] private float crystalDropChance = 0.2f;
    [SerializeField] private int crystalDropCount = 6;
    [SerializeField] private CollectableConsumable hotDogPickupPrefab;
    [SerializeField] [Range(0f, 1f)] private float hotDogDropChance = 0.6f;

    [Header("References")]
    [SerializeField] private SpriteRenderer spriteRenderer;

    private Rigidbody2D rb;
    private Collider2D bodyCollider;
    private int currentHealth;
    private bool playerInRange;
    private bool isShooting;
    private bool isStunned;
    private bool nextShotIsFirePoint1 = true;
    private float fireTimer;
    private float leaveRangeTimer;
    private float stunTimer;
    private int lastDamageFrame = -1;
    private float facingSign = -1f;
    private Vector3 firePoint1RestLocal;
    private Vector3 firePoint2RestLocal;
    private bool firePointRestsCached;
    private PlayerController targetPlayer;
    private bool chargeSfxActive;

    public int CurrentHealth => currentHealth;
    public int MaxHealth => maxHealth;
    public bool IsDead => currentHealth <= 0;
    public bool IsStunned => isStunned;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        bodyCollider = GetComponent<Collider2D>();
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();

        CacheChildReferences();
        CacheFirePointRests();
        ConfigureBody();
        ConfigureUtilityColliders();

        currentHealth = Mathf.Max(1, maxHealth);
        facingSign = -1f;
        ApplyFacingVisual();
    }

    private void Start()
    {
        HideSceneTemplates();
        DeferredEnemyPhaseRefresh.Request();
    }

    private void OnEnable()
    {
        DeferredEnemyPhaseRefresh.Request();
    }

    private void OnDisable()
    {
        StopTankerChargeSfx();
        VisualEffects.StopStunned(this);
    }

    private void Update()
    {
        if (IsDead)
            return;

        float dt = HyperSpeedWorldSlow.WorldDeltaTime;
        ResolveTargetPlayer();
        // Face first so FirePoints are mirrored before any shot this frame.
        UpdateFacing();
        TickStun(dt);
        UpdateDetection(dt);
        UpdateShooting(dt);
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
        SoundManager.Instance?.PlayChaoticTankerHit();

        if (currentHealth <= 0)
        {
            StopTankerChargeSfx();
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

        IgnoreEnemyFamily(myCols);
        RefreshDetectionColliderPhasing();
    }

    private void IgnoreEnemyFamily(Collider2D[] myCols)
    {
        CrankyClanky[] crankies = EnemyTypeCache.Crankies;
        for (int i = 0; i < crankies.Length; i++)
        {
            if (crankies[i] == null)
                continue;
            IgnoreSolidColliders(myCols, crankies[i].GetComponentsInChildren<Collider2D>(true));
        }

        LaserBot[] lasers = EnemyTypeCache.Lasers;
        for (int i = 0; i < lasers.Length; i++)
        {
            if (lasers[i] == null)
                continue;
            IgnoreSolidColliders(myCols, lasers[i].GetComponentsInChildren<Collider2D>(true));
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
            if (tankers[i] == null || tankers[i] == this)
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
            if (bosses[i] == null)
                continue;
            IgnoreSolidColliders(myCols, bosses[i].GetComponentsInChildren<Collider2D>(true));
        }
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
                // Do not force FirePoint1 here — after stun we continue the 1↔2 rhythm.
                // Leave-grace stop already resets to FP1 for a fresh engagement.
                fireTimer = 0f;
                FireVolt();
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
                nextShotIsFirePoint1 = true;
                StopTankerChargeSfx();
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

    private void UpdateFacing()
    {
        if (isStunned || targetPlayer == null || targetPlayer.IsDead)
            return;

        float dx = targetPlayer.transform.position.x - transform.position.x;
        if (Mathf.Abs(dx) < 0.05f)
            return;

        float sign = Mathf.Sign(dx);
        if (Mathf.Approximately(sign, facingSign))
            return;

        facingSign = sign;
        ApplyFacingVisual();
    }

    private void ApplyFacingVisual()
    {
        // Authored art faces left. flipX when looking right.
        if (spriteRenderer != null)
            spriteRenderer.flipX = facingSign > 0f;

        if (!firePointRestsCached)
            return;

        if (firePoint1 != null)
        {
            Vector3 lp = firePoint1RestLocal;
            firePoint1.localPosition = facingSign < 0f
                ? lp
                : new Vector3(-lp.x, lp.y, lp.z);
        }

        if (firePoint2 != null)
        {
            Vector3 lp = firePoint2RestLocal;
            firePoint2.localPosition = facingSign < 0f
                ? lp
                : new Vector3(-lp.x, lp.y, lp.z);
        }
    }

    private void UpdateShooting(float dt)
    {
        if (!isShooting || isStunned || IsDead)
        {
            StopTankerChargeSfx();
            return;
        }

        fireTimer += dt;
        float interval = Mathf.Max(0.05f, shotInterval);

        // Charge / wind-up while waiting for the next volt.
        if (fireTimer > 0.02f && fireTimer < interval)
            BeginTankerChargeSfx();

        // One shot per frame max — hitch catch-up was double-firing and made FP1 feel skipped.
        if (fireTimer >= interval)
        {
            fireTimer -= interval;
            if (fireTimer >= interval)
                fireTimer = interval * 0.25f;
            FireVolt();
        }
    }

    private void FireVolt()
    {
        if (voltShotPrefab == null)
            return;

        StopTankerChargeSfx();

        bool wantFirePoint1 = nextShotIsFirePoint1;
        Transform muzzle = wantFirePoint1 ? firePoint1 : firePoint2;
        if (muzzle == null)
        {
            // Fall back to the other muzzle, but still advance the alternation from what we wanted
            // so a missing reference cannot lock onto a single cannon forever.
            wantFirePoint1 = !wantFirePoint1;
            muzzle = wantFirePoint1 ? firePoint1 : firePoint2;
        }

        if (muzzle == null)
            return;

        // Advance only after a valid muzzle is chosen so neither point gets stuck as "next".
        nextShotIsFirePoint1 = !wantFirePoint1;

        Vector2 dir = new Vector2(facingSign, 0f);
        if (dir.sqrMagnitude < 0.001f)
            dir = Vector2.left;
        dir.Normalize();

        float speed = Mathf.Max(0.1f, voltSpeed);
        float lifetime = Mathf.Max(0.05f, voltTravelDistance / speed);

        // Keep each cannon's authored muzzle (distinct Y / depth). Nudge forward along aim only —
        // never collapse both spawns onto the same front-edge point.
        Vector3 spawnPos = ResolveVoltSpawnPosition(muzzle, dir);

        Projectile shot = Instantiate(voltShotPrefab, spawnPos, Quaternion.identity);
        shot.gameObject.SetActive(true);

        Collider2D shotCol = shot.GetComponent<Collider2D>();
        if (shotCol != null)
        {
            shotCol.isTrigger = true;
            // Live hitbox smaller than the big volt art so muzzle/floor overlap cannot pin the shot.
            if (shotCol is BoxCollider2D box)
            {
                box.size = new Vector2(0.7f, 0.7f);
                box.offset = new Vector2(0f, 0.05f);
            }
        }

        // Ignore tanker body before Launch so frame-0 contacts never treat chassis as a wall.
        IgnoreShotVsSelf(shot);

        shot.SetShotType(ProjectileShotType.Medium);
        // Grace set inside Launch so ground under FirePoint1 cannot despawn on the same frame.
        float grace = wantFirePoint1 ? 0.28f : 0.2f;
        shot.Launch(dir, speed, Mathf.Max(1, voltDamage), lifetime, transform, grace);
        SoundManager.Instance?.PlayChaoticTankerVolt();
    }

    private void BeginTankerChargeSfx()
    {
        if (chargeSfxActive)
            return;

        chargeSfxActive = true;
        SoundManager.Instance?.StartChargeLoop(SoundManager.ChargeLoopId.ChaoticTanker);
    }

    private void StopTankerChargeSfx()
    {
        if (!chargeSfxActive)
            return;

        chargeSfxActive = false;
        SoundManager.Instance?.StopChargeLoop();
    }

    /// <summary>
    /// Spawn at the muzzle with a short forward nudge. If this muzzle sits behind the tank
    /// center along aim (old FirePoint1 was inside the chassis), push just far enough to clear
    /// the body without teleporting onto the other cannon's position.
    /// </summary>
    private Vector3 ResolveVoltSpawnPosition(Transform muzzle, Vector2 dir)
    {
        Vector2 tank = transform.position;
        Vector2 muzzlePos = muzzle.position;
        float along = Vector2.Dot(muzzlePos - tank, dir);

        // Front of collider is ~3 units out; keep authored depth when already forward enough.
        const float minClearAlongFacing = 1.15f;
        float forwardNudge = 0.35f;
        if (along < minClearAlongFacing)
            forwardNudge += minClearAlongFacing - along;

        Vector2 spawn = muzzlePos + dir * forwardNudge;
        // Slight lift so low cannons clear floor lips without changing aim.
        spawn.y += 0.12f;
        return spawn;
    }

    /// <summary>Volts must pass through the Tanker's own solid body without despawning.</summary>
    private void IgnoreShotVsSelf(Projectile shot)
    {
        if (shot == null)
            return;

        Collider2D[] shotCols = shot.GetComponentsInChildren<Collider2D>(true);
        Collider2D[] myCols = GetComponentsInChildren<Collider2D>(true);
        if (shotCols == null || myCols == null)
            return;

        for (int i = 0; i < shotCols.Length; i++)
        {
            Collider2D a = shotCols[i];
            if (a == null)
                continue;

            for (int j = 0; j < myCols.Length; j++)
            {
                Collider2D b = myCols[j];
                if (b == null)
                    continue;

                Physics2D.IgnoreCollision(a, b, true);
            }
        }
    }

    private void PlayHitSpark()
    {
        VisualEffects.PlayBriefHitSpark(hitSparkPrefab, this, sparkSpawnPoint, 0.15f);
    }

    private void BeginStun()
    {
        isStunned = true;
        stunTimer = Mathf.Max(0f, projectileStunDuration);
        isShooting = false;
        playerInRange = false;
        fireTimer = 0f;
        leaveRangeTimer = 0f;
        StopTankerChargeSfx();
        // Keep nextShotIsFirePoint1 — resetting to FP1 on every stun made FP2 feel neglected.
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

    private void Die()
    {
        isStunned = false;
        stunTimer = 0f;
        isShooting = false;
        StopTankerChargeSfx();
        VisualEffects.StopStunned(this);

        Vector3 spawnPos = explosionSpawnPoint != null
            ? explosionSpawnPoint.position
            : transform.position;

        if (Random.value < crystalDropChance && collectableCrystalPrefab != null)
            CollectableCrystal.SpawnBurst(collectableCrystalPrefab, spawnPos, Mathf.Max(1, crystalDropCount));

        if (Random.value < hotDogDropChance && hotDogPickupPrefab != null)
        {
            SoundManager.Instance?.PlayItemDrop();
            CollectablePickupBase.SpawnSingleBurst(hotDogPickupPrefab, spawnPos);
        }

        if (deathExplosionPrefab != null)
            VisualEffects.SpawnEnemyExplosionSmallPurple(deathExplosionPrefab, spawnPos, spriteRenderer);

        SoundManager.Instance?.PlayChaoticTankerDeath();
        Destroy(gameObject);
    }

    private void ResolveTargetPlayer()
    {
        if (targetPlayer != null && !targetPlayer.IsDead)
            return;

        targetPlayer = PlayerController.ResolveActive();
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

        if (firePoint1 == null)
        {
            Transform found = transform.Find("FirePoint1");
            if (found != null)
                firePoint1 = found;
        }

        if (firePoint2 == null)
        {
            Transform found = transform.Find("FirePoint2");
            if (found != null)
                firePoint2 = found;
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
            if (found != null)
                explosionSpawnPoint = found;
        }
    }

    private void CacheFirePointRests()
    {
        if (firePoint1 != null)
            firePoint1RestLocal = firePoint1.localPosition;
        if (firePoint2 != null)
            firePoint2RestLocal = firePoint2.localPosition;
        firePointRestsCached = firePoint1 != null || firePoint2 != null;
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

        // Attack Box is unused for this phasable turret — disable if present.
        Transform attack = transform.Find("Attack Box");
        if (attack != null)
        {
            Collider2D col = attack.GetComponent<Collider2D>();
            if (col != null)
                col.enabled = false;
        }
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

    private void HideSceneTemplates()
    {
        // Only hide scene-placed template copies — never toggle prefab assets
        // (that can leave a ghost volt stuck on a fire point).
        HideIfSceneObject(voltShotPrefab != null ? voltShotPrefab.gameObject : null);
        HideIfSceneObject(hitSparkPrefab != null ? hitSparkPrefab.gameObject : null);
        HideIfSceneObject(deathExplosionPrefab != null ? deathExplosionPrefab.gameObject : null);
    }

    private static void HideIfSceneObject(GameObject go)
    {
        if (go == null)
            return;

        if (!go.scene.IsValid())
            return;

        go.SetActive(false);
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
        shotInterval = Mathf.Max(0.05f, shotInterval);
        stopShootingGraceSeconds = Mathf.Max(0f, stopShootingGraceSeconds);
        voltDamage = Mathf.Max(1, voltDamage);
        voltSpeed = Mathf.Max(0.1f, voltSpeed);
        voltTravelDistance = Mathf.Max(0.1f, voltTravelDistance);
        crystalDropCount = Mathf.Max(1, crystalDropCount);
        projectileStunDuration = Mathf.Max(0f, projectileStunDuration);
    }
#endif
}
