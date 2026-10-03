using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Floating patrol drone: bob ±1 while drifting home ±4, then chase when detected.
/// On contact: 3 damage + soft knockback, then slowly fall back to orbit range and circle.
/// Phasable, stunnable, no i-frames. Leave detection → fly home immediately.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public sealed class ScrapNit : MonoBehaviour, ICommonEnemy, IKnockbackReceiver, IForceKillable
{
    private enum AiState
    {
        PatrolRight,
        WaitAtRight,
        PatrolHomeFromRight,
        WaitAtHomeBeforeLeft,
        PatrolLeft,
        WaitAtLeft,
        PatrolHomeFromLeft,
        WaitAtHomeBeforeRight,
        Chase,
        OrbitEnter,
        Orbit,
        ReturnHome,
        Regroup
    }

    private static readonly int FlyingStateHash = Animator.StringToHash("ScrapNit Enemy flying");

    [Header("Health")]
    [SerializeField] private int maxHealth = 4;

    [Header("Movement")]
    [Tooltip("Subtracted from the player's NormalMoveSpeed (default player 6 → 2).")]
    [SerializeField] private float speedBelowPlayer = 4f;
    [SerializeField] private float fallbackMoveSpeed = 4f;
    [SerializeField] private float patrolDistance = 4f;
    [SerializeField] private float patrolWaitSeconds = 1f;
    [SerializeField] private float bobAmplitude = 1f;
    [SerializeField] private float bobCyclesPerSecond = 0.55f;
    [SerializeField] private float arriveDistance = 0.08f;

    [Header("Combat")]
    [SerializeField] private int contactDamage = 3;
    [SerializeField] private float knockbackDistance = 1f;
    [SerializeField] private float knockbackDuration = 0.18f;
    [SerializeField] private float orbitRadius = 4f;
    [Tooltip("How long ScrapNit takes to fall back to orbit range after a hit (higher = slower bounce).")]
    [SerializeField] private float orbitEnterDuration = 1.25f;
    [SerializeField] private float orbitSeconds = 2f;
    [Tooltip("Full circles completed during the orbit wait.")]
    [SerializeField] private float orbitRevolutions = 1f;
    [Tooltip("+1 = counter-clockwise, -1 = clockwise. Flips when the orbit path is blocked.")]
    [SerializeField] private float orbitDirectionSign = 1f;
    [Tooltip("How fast the orbit's center drifts after the player (0 = normal move speed, which is slower than the player so they can escape).")]
    [SerializeField] private float orbitCenterFollowSpeed = 0f;
    [Tooltip("How fast the orbit radius eases back out to Orbit Radius after regrouping.")]
    [SerializeField] private float orbitRadiusEaseSpeed = 3f;
    [SerializeField] private LayerMask orbitBlockLayers;
    [SerializeField] private AttackHitbox attackHitbox;

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
    [SerializeField] [Range(0f, 1f)] private float crystalDropChance = 0.9f;
    [SerializeField] private int crystalDropCount = 2;
    [SerializeField] private CollectableConsumable juiceBoxPickupPrefab;
    [SerializeField] [Range(0f, 1f)] private float juiceDropChance = 0.8f;
    [SerializeField] private CollectableConsumable hotDogPickupPrefab;
    [SerializeField] [Range(0f, 1f)] private float hotDogDropChance = 0.7f;
    [SerializeField] private int maxDropTypes = 2;

    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private SpriteRenderer spriteRenderer;

    private Rigidbody2D rb;
    private Collider2D bodyCollider;
    private int currentHealth;
    private bool isStunned;
    private float stunTimer;
    private int lastDamageFrame = -1;
    private int currentAnimStateHash;
    private float facingSign = -1f;
    private PlayerController targetPlayer;

    private Vector2 homePosition;
    private Vector2 logicalPosition;
    private float bobPhase;
    private AiState state = AiState.PatrolRight;
    private float waitTimer;
    private float orbitTimer;
    private float orbitEnterTimer;
    private float orbitAngle;
    private Vector2 orbitEnterStart;
    private Vector2 orbitEnterEnd;
    private float orbitDirSign = 1f;
    private Vector2 orbitCenter;
    private float orbitCurrentRadius;
    private bool hitboxArmed;

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

        if (orbitBlockLayers.value == 0)
            orbitBlockLayers = LayerMask.GetMask("Ground");
        orbitDirSign = Mathf.Sign(orbitDirectionSign);
        if (Mathf.Abs(orbitDirSign) < 0.01f)
            orbitDirSign = 1f;

        homePosition = rb != null ? rb.position : (Vector2)transform.position;
        logicalPosition = homePosition;
        currentHealth = Mathf.Max(1, maxHealth);
        facingSign = -1f;
        ApplyFacingVisual();
        state = AiState.PatrolRight;
    }

    private void Start()
    {
        HideSceneTemplates();
        DeferredEnemyPhaseRefresh.Request();
        PlayFlyingAnimation();
        ApplyWorldPosition();
    }

    private void OnEnable()
    {
        DeferredEnemyPhaseRefresh.Request();
        if (attackHitbox != null)
        {
            attackHitbox.OnTargetAcquired -= OnContactTargetAcquired;
            attackHitbox.OnTargetAcquired += OnContactTargetAcquired;
        }
    }

    private void OnDisable()
    {
        if (attackHitbox != null)
            attackHitbox.OnTargetAcquired -= OnContactTargetAcquired;

        VisualEffects.StopStunned(this);
    }

    private void Update()
    {
        if (IsDead)
            return;

        float dt = HyperSpeedWorldSlow.WorldDeltaTime;
        ResolveTargetPlayer();
        UpdateDetection();
        TickStun(dt);
        UpdateBob(dt);
        UpdateFacingFromMotionIntent();
        UpdateAnimator();
        RefreshChaseHitMemory();
    }

    private void FixedUpdate()
    {
        if (IsDead)
            return;

        float dt = HyperSpeedWorldSlow.WorldFixedDeltaTime;
        if (!isStunned)
            TickAi(dt);

        ApplyWorldPosition();
    }

    public void TakeDamage(int amount)
    {
        ApplyIncomingDamage(amount, stunFromProjectile: false);
    }

    public void ForceKill()
    {
        if (IsDead)
            return;

        lastDamageFrame = Time.frameCount;
        currentHealth = 0;
        PlayHitSpark();
        SoundManager.Instance?.PlayScrapNitHit();
        Die();
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
        SoundManager.Instance?.PlayScrapNitHit();

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
            if (tankers[i] == null)
                continue;
            IgnoreSolidColliders(myCols, tankers[i].GetComponentsInChildren<Collider2D>(true));
        }

        ScrapNit[] scraps = EnemyTypeCache.Scraps;
        for (int i = 0; i < scraps.Length; i++)
        {
            if (scraps[i] == null || scraps[i] == this)
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

    private void UpdateDetection()
    {
        if (isStunned || IsDead)
            return;

        bool inRange = IsPlayerInsideDetection();

        if (inRange)
        {
            if (state == AiState.Orbit || state == AiState.OrbitEnter || state == AiState.Regroup)
                return;

            if (state != AiState.Chase)
                EnterChase();
            return;
        }

        // Left detection immediately → fly home (even mid-chase / mid-patrol engage).
        if (state == AiState.Chase || state == AiState.Orbit || state == AiState.OrbitEnter || state == AiState.Regroup)
            EnterReturnHome();
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

    private void TickAi(float dt)
    {
        switch (state)
        {
            case AiState.PatrolRight:
                MoveToward(homePosition + Vector2.right * patrolDistance, dt, AiState.WaitAtRight);
                break;
            case AiState.WaitAtRight:
                TickWait(dt, AiState.PatrolHomeFromRight);
                break;
            case AiState.PatrolHomeFromRight:
                MoveToward(homePosition, dt, AiState.WaitAtHomeBeforeLeft);
                break;
            case AiState.WaitAtHomeBeforeLeft:
                TickWait(dt, AiState.PatrolLeft);
                break;
            case AiState.PatrolLeft:
                MoveToward(homePosition + Vector2.left * patrolDistance, dt, AiState.WaitAtLeft);
                break;
            case AiState.WaitAtLeft:
                TickWait(dt, AiState.PatrolHomeFromLeft);
                break;
            case AiState.PatrolHomeFromLeft:
                MoveToward(homePosition, dt, AiState.WaitAtHomeBeforeRight);
                break;
            case AiState.WaitAtHomeBeforeRight:
                TickWait(dt, AiState.PatrolRight);
                break;
            case AiState.Chase:
                TickChase(dt);
                break;
            case AiState.OrbitEnter:
                TickOrbitEnter(dt);
                break;
            case AiState.Orbit:
                TickOrbit(dt);
                break;
            case AiState.Regroup:
                TickRegroup(dt);
                break;
            case AiState.ReturnHome:
                MoveToward(homePosition, dt, AiState.WaitAtHomeBeforeRight);
                break;
        }
    }

    private void TickWait(float dt, AiState next)
    {
        SetHitboxArmed(false);
        waitTimer += dt;
        if (waitTimer >= Mathf.Max(0f, patrolWaitSeconds))
        {
            waitTimer = 0f;
            state = next;
        }
    }

    private void MoveToward(Vector2 target, float dt, AiState arriveState)
    {
        SetHitboxArmed(false);
        float speed = GetMoveSpeed();
        Vector2 delta = target - logicalPosition;
        float dist = delta.magnitude;
        if (dist <= arriveDistance)
        {
            logicalPosition = target;
            waitTimer = 0f;
            state = arriveState;
            return;
        }

        float step = speed * dt;
        if (step >= dist)
            logicalPosition = target;
        else
            logicalPosition += delta / dist * step;

        if (Mathf.Abs(delta.x) > 0.01f)
            facingSign = Mathf.Sign(delta.x);
    }

    private void TickChase(float dt)
    {
        SetHitboxArmed(true);
        if (targetPlayer == null || targetPlayer.IsDead)
        {
            EnterReturnHome();
            return;
        }

        Vector2 target = targetPlayer.transform.position;
        float speed = GetMoveSpeed();
        Vector2 delta = target - logicalPosition;
        float dist = delta.magnitude;
        if (dist > 0.001f)
        {
            float step = speed * dt;
            if (step >= dist)
                logicalPosition = target;
            else
                logicalPosition += delta / dist * step;

            if (Mathf.Abs(delta.x) > 0.01f)
                facingSign = Mathf.Sign(delta.x);
        }
    }

    private void TickOrbitEnter(float dt)
    {
        SetHitboxArmed(false);
        if (targetPlayer == null || targetPlayer.IsDead)
        {
            EnterReturnHome();
            return;
        }

        float duration = Mathf.Max(0.05f, orbitEnterDuration);
        orbitEnterTimer += dt;
        float t = Mathf.Clamp01(orbitEnterTimer / duration);
        // Smoothstep ease — slow start and end so bounce-back feels soft.
        float s = t * t * (3f - 2f * t);

        // The end point rides the slow orbit center, not the player, so she can outrun the fall-back.
        Vector2 player = targetPlayer.transform.position;
        DriftOrbitCenter(player, dt);
        float radius = Mathf.Max(0.1f, orbitRadius);
        Vector2 away = new Vector2(Mathf.Cos(orbitAngle), Mathf.Sin(orbitAngle));
        orbitEnterEnd = orbitCenter + away * radius;

        logicalPosition = Vector2.Lerp(orbitEnterStart, orbitEnterEnd, s);
        FaceToward(player);

        if (t >= 1f)
        {
            if ((player - orbitCenter).sqrMagnitude > radius * radius)
                EnterRegroup();
            else
                BeginOrbitCircle();
        }
    }

    private void TickOrbit(float dt)
    {
        SetHitboxArmed(false);
        if (targetPlayer == null || targetPlayer.IsDead)
        {
            EnterReturnHome();
            return;
        }

        // Circle a center that drifts after the player slower than they move, so the orbit can be escaped.
        Vector2 player = targetPlayer.transform.position;
        DriftOrbitCenter(player, dt);
        float maxRadius = Mathf.Max(0.1f, orbitRadius);
        if ((player - orbitCenter).sqrMagnitude > maxRadius * maxRadius)
        {
            EnterRegroup();
            return;
        }

        orbitCurrentRadius = Mathf.MoveTowards(orbitCurrentRadius, maxRadius, Mathf.Max(0.1f, orbitRadiusEaseSpeed) * dt);

        Vector2 center = orbitCenter;
        float duration = Mathf.Max(0.05f, orbitSeconds);
        float angSpeed = (Mathf.PI * 2f * Mathf.Max(0.1f, orbitRevolutions)) / duration;
        float step = angSpeed * dt * orbitDirSign;
        float nextAngle = orbitAngle + step;
        float radius = Mathf.Max(0.1f, orbitCurrentRadius);
        Vector2 nextPos = center + new Vector2(Mathf.Cos(nextAngle), Mathf.Sin(nextAngle)) * radius;

        if (IsOrbitPathBlocked(logicalPosition, nextPos))
        {
            // Obstacle ahead — reverse direction (prefer counter-clockwise first from default CW).
            orbitDirSign = -orbitDirSign;
            step = angSpeed * dt * orbitDirSign;
            nextAngle = orbitAngle + step;
            nextPos = center + new Vector2(Mathf.Cos(nextAngle), Mathf.Sin(nextAngle)) * radius;

            // If the other way is also blocked, stay put this frame.
            if (IsOrbitPathBlocked(logicalPosition, nextPos))
            {
                orbitTimer += dt;
                if (orbitTimer >= duration)
                    EnterChase();
                return;
            }
        }

        orbitAngle = nextAngle;
        orbitTimer += dt;
        logicalPosition = nextPos;
        FaceToward(player);

        if (orbitTimer >= duration)
            EnterChase();
    }

    /// <summary>
    /// Player escaped the orbit ring: fly back at normal (slower-than-player) speed without
    /// attacking, then resume the remaining orbit before the next attack.
    /// </summary>
    private void TickRegroup(float dt)
    {
        SetHitboxArmed(false);
        if (targetPlayer == null || targetPlayer.IsDead)
        {
            EnterReturnHome();
            return;
        }

        Vector2 player = targetPlayer.transform.position;
        float radius = Mathf.Max(0.1f, orbitRadius);
        Vector2 delta = player - logicalPosition;
        float dist = delta.magnitude;

        if (dist <= radius)
        {
            Vector2 away = dist > 0.001f ? -delta / dist : Vector2.right * -facingSign;
            orbitCenter = player;
            orbitCurrentRadius = Mathf.Max(0.1f, dist);
            orbitAngle = Mathf.Atan2(away.y, away.x);
            state = AiState.Orbit;
            return;
        }

        float step = GetMoveSpeed() * dt;
        logicalPosition += delta / dist * Mathf.Min(step, dist);
        FaceToward(player);
    }

    private void EnterRegroup()
    {
        // orbitTimer is kept so the orbit resumes where it left off.
        state = AiState.Regroup;
        waitTimer = 0f;
        orbitEnterTimer = 0f;
        SetHitboxArmed(false);
    }

    private void DriftOrbitCenter(Vector2 player, float dt)
    {
        float speed = orbitCenterFollowSpeed > 0f ? orbitCenterFollowSpeed : GetMoveSpeed();
        orbitCenter = Vector2.MoveTowards(orbitCenter, player, speed * dt);
    }

    private void FaceToward(Vector2 point)
    {
        float faceDx = point.x - logicalPosition.x;
        if (Mathf.Abs(faceDx) > 0.01f)
            facingSign = Mathf.Sign(faceDx);
    }

    private bool IsOrbitPathBlocked(Vector2 from, Vector2 to)
    {
        Vector2 delta = to - from;
        float dist = delta.magnitude;
        if (dist < 0.01f)
            return false;

        float probeRadius = bodyCollider != null
            ? Mathf.Max(0.08f, Mathf.Min(bodyCollider.bounds.extents.x, bodyCollider.bounds.extents.y) * 0.85f)
            : 0.2f;

        RaycastHit2D hit = Physics2D.CircleCast(
            from,
            probeRadius,
            delta / dist,
            dist,
            orbitBlockLayers);

        if (hit.collider == null)
            return false;

        // Don't treat the player / other phasable combatants as orbit walls.
        if (hit.collider.GetComponentInParent<PlayerController>() != null)
            return false;
        if (hit.collider.GetComponentInParent<ICommonEnemy>() != null)
            return false;
        if (hit.collider.GetComponentInParent<Boss>() != null)
            return false;
        if (IsSpecialPassablePlatform(hit.collider))
            return false;

        return true;
    }

    private static bool IsSpecialPassablePlatform(Collider2D col)
    {
        if (col == null)
            return false;
        return col.GetComponentInParent<MovingFactoryPlatform>() != null
               || col.GetComponentInParent<SlimFactoryPlatform>() != null;
    }

    private void EnterChase()
    {
        state = AiState.Chase;
        waitTimer = 0f;
        orbitTimer = 0f;
        orbitEnterTimer = 0f;
        SetHitboxArmed(true);
    }

    private void EnterOrbit()
    {
        state = AiState.OrbitEnter;
        waitTimer = 0f;
        orbitTimer = 0f;
        orbitEnterTimer = 0f;
        SetHitboxArmed(false);

        Vector2 center = targetPlayer != null
            ? (Vector2)targetPlayer.transform.position
            : logicalPosition;
        Vector2 away = logicalPosition - center;
        if (away.sqrMagnitude < 0.0001f)
            away = Vector2.right * facingSign;
        away.Normalize();

        orbitCenter = center;
        orbitEnterStart = logicalPosition;
        orbitEnterEnd = center + away * Mathf.Max(0.1f, orbitRadius);
        orbitAngle = Mathf.Atan2(away.y, away.x);
    }

    private void BeginOrbitCircle()
    {
        state = AiState.Orbit;
        orbitTimer = 0f;
        orbitEnterTimer = 0f;
        orbitCurrentRadius = Mathf.Max(0.1f, Vector2.Distance(logicalPosition, orbitCenter));
        // Default orbit is counter-clockwise; obstacles flip this in TickOrbit.
        orbitDirSign = Mathf.Abs(orbitDirectionSign) > 0.01f ? Mathf.Sign(orbitDirectionSign) : 1f;
        SetHitboxArmed(false);
    }

    private void EnterReturnHome()
    {
        state = AiState.ReturnHome;
        waitTimer = 0f;
        orbitTimer = 0f;
        orbitEnterTimer = 0f;
        SetHitboxArmed(false);
    }

    private float GetMoveSpeed()
    {
        float playerSpeed = fallbackMoveSpeed + speedBelowPlayer;
        if (targetPlayer != null)
            playerSpeed = targetPlayer.NormalMoveSpeed;

        return Mathf.Max(0.1f, playerSpeed - speedBelowPlayer);
    }

    private void UpdateBob(float dt)
    {
        bobPhase += dt * Mathf.PI * 2f * Mathf.Max(0.05f, bobCyclesPerSecond);
    }

    public void ApplyKnockbackDisplacement(Vector2 delta)
    {
        if (IsDead)
            return;

        // Shift the whole flight frame so orbit / fall-back paths don't snap it back.
        logicalPosition += delta;
        orbitCenter += delta;
        orbitEnterStart += delta;
        orbitEnterEnd += delta;
    }

    private void ApplyWorldPosition()
    {
        float bob = Mathf.Sin(bobPhase) * bobAmplitude;
        Vector2 world = new Vector2(logicalPosition.x, logicalPosition.y + bob);
        if (rb != null)
            rb.MovePosition(world);
        else
            transform.position = world;

        ApplyFacingVisual();
    }

    private void UpdateFacingFromMotionIntent()
    {
        // Facing is updated during MoveToward / Chase / Orbit; keep sprite synced.
        ApplyFacingVisual();
    }

    private void ApplyFacingVisual()
    {
        // Authored art faces left. flipX when looking right.
        if (spriteRenderer != null)
            spriteRenderer.flipX = facingSign > 0f;
    }

    private void SetHitboxArmed(bool armed)
    {
        if (attackHitbox == null)
            return;

        if (armed == hitboxArmed && armed)
            return;

        hitboxArmed = armed;
        if (armed)
            attackHitbox.Activate(Mathf.Max(0, contactDamage), applyDamage: true);
        else
            attackHitbox.Deactivate();
    }

    private void RefreshChaseHitMemory()
    {
        // Keep hitbox armed during chase so a new pass can hit after orbit.
        if (!hitboxArmed || attackHitbox == null || targetPlayer == null)
            return;

        // Only forget between orbits — don't multi-hit during one chase approach.
    }

    private void OnContactTargetAcquired(Collider2D other, PlayerController player)
    {
        if (player == null || player.IsDead || IsDead || isStunned)
            return;

        if (state != AiState.Chase)
            return;

        player.ApplySoftBounce(transform, knockbackDistance, knockbackDuration);
        EnterOrbit();
    }

    private void PlayHitSpark()
    {
        VisualEffects.PlayBriefHitSpark(hitSparkPrefab, this, sparkSpawnPoint, 0.15f);
    }

    private void BeginStun()
    {
        isStunned = true;
        stunTimer = Mathf.Max(0f, projectileStunDuration);
        SetHitboxArmed(false);
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
        SetHitboxArmed(false);
        VisualEffects.StopStunned(this);

        Vector3 spawnPos = explosionSpawnPoint != null
            ? explosionSpawnPoint.position
            : transform.position;

        SpawnDrops(spawnPos);

        if (deathExplosionPrefab != null)
            VisualEffects.SpawnEnemyExplosionSmallPurple(deathExplosionPrefab, spawnPos, spriteRenderer);

        SoundManager.Instance?.PlayScrapNitDeath();
        Destroy(gameObject);
    }

    private void SpawnDrops(Vector3 spawnPos)
    {
        // Independent rolls, then keep at most maxDropTypes successes.
        var winners = new List<int>(3);
        if (Random.value < juiceDropChance && juiceBoxPickupPrefab != null)
            winners.Add(0);
        if (Random.value < hotDogDropChance && hotDogPickupPrefab != null)
            winners.Add(1);
        if (Random.value < crystalDropChance && collectableCrystalPrefab != null)
            winners.Add(2);

        int keep = Mathf.Min(Mathf.Max(0, maxDropTypes), winners.Count);
        for (int i = winners.Count - 1; i >= 1; i--)
        {
            int j = Random.Range(0, i + 1);
            int tmp = winners[i];
            winners[i] = winners[j];
            winners[j] = tmp;
        }

        bool playedItemDrop = false;
        for (int i = 0; i < keep; i++)
        {
            switch (winners[i])
            {
                case 0:
                    if (!playedItemDrop)
                    {
                        SoundManager.Instance?.PlayItemDrop();
                        playedItemDrop = true;
                    }
                    CollectablePickupBase.SpawnSingleBurst(juiceBoxPickupPrefab, spawnPos);
                    break;
                case 1:
                    if (!playedItemDrop)
                    {
                        SoundManager.Instance?.PlayItemDrop();
                        playedItemDrop = true;
                    }
                    CollectablePickupBase.SpawnSingleBurst(hotDogPickupPrefab, spawnPos);
                    break;
                case 2:
                    CollectableCrystal.SpawnBurst(
                        collectableCrystalPrefab,
                        spawnPos,
                        Mathf.Max(1, crystalDropCount));
                    break;
            }
        }
    }

    private void ResolveTargetPlayer()
    {
        if (targetPlayer != null && !targetPlayer.IsDead)
            return;

        targetPlayer = PlayerController.ResolveActive();
    }

    private void UpdateAnimator()
    {
        PlayFlyingAnimation();
    }

    private void PlayFlyingAnimation()
    {
        if (animator == null || currentAnimStateHash == FlyingStateHash)
            return;

        if (!animator.HasState(0, FlyingStateHash))
            return;

        animator.Play(FlyingStateHash, 0, 0f);
        currentAnimStateHash = FlyingStateHash;
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

        if (sparkSpawnPoint == null)
        {
            Transform found = transform.Find("Spark Spawn Box");
            if (found == null)
                found = transform.Find("Spark spawn box");
            if (found != null)
                sparkSpawnPoint = found;
        }

        if (explosionSpawnPoint == null)
        {
            Transform found = transform.Find("Death Explosion Spawn Box");
            if (found == null)
                found = transform.Find("Death Explosion spawn box");
            if (found == null)
                found = transform.Find("Death explosion Spawn Box");
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
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.linearVelocity = Vector2.zero;
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
        attackHitbox.Deactivate();
        hitboxArmed = false;
    }

    private void HideSceneTemplates()
    {
        HideIfSceneObject(hitSparkPrefab != null ? hitSparkPrefab.gameObject : null);
        HideIfSceneObject(deathExplosionPrefab != null ? deathExplosionPrefab.gameObject : null);
    }

    private static void HideIfSceneObject(GameObject go)
    {
        if (go == null || !go.scene.IsValid())
            return;

        go.SetActive(false);
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
        speedBelowPlayer = Mathf.Max(0f, speedBelowPlayer);
        fallbackMoveSpeed = Mathf.Max(0.1f, fallbackMoveSpeed);
        patrolDistance = Mathf.Max(0.1f, patrolDistance);
        patrolWaitSeconds = Mathf.Max(0f, patrolWaitSeconds);
        bobAmplitude = Mathf.Max(0f, bobAmplitude);
        bobCyclesPerSecond = Mathf.Max(0.05f, bobCyclesPerSecond);
        contactDamage = Mathf.Max(0, contactDamage);
        knockbackDistance = Mathf.Max(0f, knockbackDistance);
        orbitRadius = Mathf.Max(0.1f, orbitRadius);
        orbitEnterDuration = Mathf.Max(0.05f, orbitEnterDuration);
        orbitSeconds = Mathf.Max(0.05f, orbitSeconds);
        orbitCenterFollowSpeed = Mathf.Max(0f, orbitCenterFollowSpeed);
        orbitRadiusEaseSpeed = Mathf.Max(0.1f, orbitRadiusEaseSpeed);
        if (Mathf.Abs(orbitDirectionSign) < 0.01f)
            orbitDirectionSign = 1f;
        crystalDropCount = Mathf.Max(1, crystalDropCount);
        maxDropTypes = Mathf.Max(0, maxDropTypes);
    }
#endif
}
