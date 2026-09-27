using UnityEngine;

/// <summary>
/// Solid moving-wall hazard: on detect, flies once to form a vertical wall 1.5 spaces
/// from the player (shared session with other Blocker Bots), pushes the player back
/// on contact without damage, and returns home when the player leaves detection.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public sealed class BlockerBot : MonoBehaviour, ICommonEnemy
{
    private static readonly int IdleStateHash = Animator.StringToHash("Blocker Bot Idle");
    private static readonly int BlockingStateHash = Animator.StringToHash("Blocker Bot Blocking");
    private static readonly int HitStateHash = Animator.StringToHash("Blocker Bot Hit");
    private static readonly int IsBlockingHash = Animator.StringToHash("IsBlocking");
    private static readonly int IsGettingHitHash = Animator.StringToHash("IsGettingHit");
    private static readonly int HitTriggerHash = Animator.StringToHash("Hit");

    private enum MoveState
    {
        HomeIdle,
        Approaching,
        HoldingWall,
        Returning,
        WaitingForRoute
    }

    [Header("Health")]
    [SerializeField] private int maxHealth = 5;

    [Header("Wall Move")]
    [SerializeField] private float standOffSpaces = 1.5f;
    [SerializeField] private float approachDuration = 0.25f;
    [SerializeField] private float returnDuration = 0.7f;

    [Header("Detection")]
    [SerializeField] private Transform detectionCenter;
    [SerializeField] private Collider2D detectionCollider;
    [SerializeField] private LayerMask playerLayers = ~0;

    [Header("Body Bounce (no damage)")]
    [Tooltip("Hard cap: player is never shoved more than this many spaces (1 = one tile).")]
    [SerializeField] private float bounceDistance = 1f;
    [SerializeField] private float bounceDuration = 0.18f;
    [SerializeField] private float bounceCooldown = 0.2f;
    [Tooltip("|contact normal.x| must exceed this to count as a side hit (top/bottom do not bounce).")]
    [SerializeField] [Range(0.2f, 0.95f)] private float sideBounceNormalMin = 0.45f;

    [Header("Hit Sparks")]
    [SerializeField] private GameVisualEffect hitSparkPrefab;
    [SerializeField] private Transform sparkSpawnPoint;

    [Header("Death Smoke")]
    [SerializeField] private Transform explosionSpawnPoint;
    [SerializeField] private GameVisualEffect smokeParticle1;
    [SerializeField] private GameVisualEffect smokeParticle2;
    [SerializeField] private GameVisualEffect smokeParticle3;
    [SerializeField] private GameVisualEffect smokeParticle4;
    [SerializeField] private int smokeCopiesPerParticle = 2;
    [SerializeField] private float smokeRiseDistance = 4.5f;
    [SerializeField] private float smokeRiseSpeed = 3.2f;
    [SerializeField] private float smokeScatterRadius = 0.55f;

    [Header("Platform Check")]
    [Tooltip("Solid walls / thick ground that block wall-slot placement. Special slim/moving platforms are ignored.")]
    [SerializeField] private LayerMask groundLayers;

    [Header("Hit Anim")]
    [SerializeField] private float hitAnimDuration = 0.18f;

    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private SpriteRenderer spriteRenderer;

    private const float RamSkin = 0.02f;
    private readonly RaycastHit2D[] moveCastHits = new RaycastHit2D[8];

    private Rigidbody2D rb;
    private Collider2D bodyCollider;
    private int currentHealth;
    private MoveState moveState = MoveState.HomeIdle;
    private bool deployedThisVisit;
    private bool playerInRange;
    private Vector2 homePosition;
    private Vector2 wallSlot;
    private Vector2 moveFrom;
    private Vector2 moveTo;
    private float moveElapsed;
    private float moveDuration;
    private float bounceCooldownTimer;
    private float hitAnimTimer;
    private int currentAnimStateHash;
    private PlayerController targetPlayer;

    public int CurrentHealth => currentHealth;
    public int MaxHealth => maxHealth;
    public bool IsDead => currentHealth <= 0;
    public Vector2 HomePosition => homePosition;
    /// <summary>True while this bot currently has the player inside its detection radius.</summary>
    public bool SeesPlayerNow => !IsDead && IsPlayerInsideDetection();
    /// <summary>True while approaching / holding / waiting on a wall session.</summary>
    public bool IsActivelyBlocking =>
        moveState == MoveState.Approaching ||
        moveState == MoveState.HoldingWall ||
        moveState == MoveState.WaitingForRoute ||
        (deployedThisVisit && BlockerBotWallSession.Contains(this));

    public Vector2 BodySize
    {
        get
        {
            if (bodyCollider != null)
                return bodyCollider.bounds.size;
            return new Vector2(1.46f, 1.45f);
        }
    }

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
        ApplyGroundLayer();

        homePosition = rb != null ? rb.position : (Vector2)transform.position;
        currentHealth = Mathf.Max(1, maxHealth);

        if (groundLayers.value == 0)
            groundLayers = LayerMask.GetMask("Ground");
    }

    private void Start()
    {
        HideSceneTemplates();
        PlayAnimationState(IdleStateHash);
        SetBlockingAnimator(false);
    }

    private void OnEnable()
    {
        DeferredEnemyPhaseRefresh.Request();
    }

    private void OnDisable()
    {
        BlockerBotWallSession.Leave(this);
    }

    private void Update()
    {
        if (IsDead)
            return;

        float dt = HyperSpeedWorldSlow.WorldDeltaTime;
        ResolveTargetPlayer();
        UpdateDetection();
        TickBounceCooldown(dt);
        TickHitAnim(dt);
        UpdateAnimator();
    }

    private void FixedUpdate()
    {
        if (IsDead || rb == null)
            return;

        TickMovement(HyperSpeedWorldSlow.WorldFixedDeltaTime);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision == null || collision.collider == null)
            return;

        if (ShouldPhaseThrough(collision.collider))
        {
            IgnoreSolidCollidersAgainst(GetComponentsInChildren<Collider2D>(true), collision.collider);
            return;
        }

        TryBouncePlayer(collision);
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        if (collision == null || collision.collider == null || !BelongsToPlayer(collision.collider))
            return;

        TryBouncePlayer(collision);
    }

    public void TakeDamage(int amount)
    {
        ApplyIncomingDamage(amount);
    }

    public void TakeDamage(int amount, ProjectileShotType shotType)
    {
        // Not stunnable — shot type ignored.
        ApplyIncomingDamage(amount);
    }

    public void AssignWallSlot(Vector2 worldSlot)
    {
        AssignWallSlot(worldSlot, allowReshuffle: false);
    }

    public void AssignWallSlot(Vector2 worldSlot, bool allowReshuffle)
    {
        wallSlot = worldSlot;

        if (moveState == MoveState.HoldingWall && !allowReshuffle)
        {
            // Keep formation stable — do not shuffle holders when someone joins/leaves.
            MoveWithoutRammingPlayer(wallSlot);
            return;
        }

        if (moveState == MoveState.WaitingForRoute ||
            moveState == MoveState.HomeIdle ||
            moveState == MoveState.Returning)
        {
            BeginMove(rb.position, wallSlot, approachDuration, MoveState.Approaching);
            return;
        }

        if (moveState == MoveState.Approaching)
            moveTo = wallSlot;
        else if (moveState == MoveState.HoldingWall && allowReshuffle)
            BeginMove(rb.position, wallSlot, approachDuration * 0.65f, MoveState.Approaching);
    }

    /// <summary>Called when every wall slot/route is blocked — wait in place until one opens.</summary>
    public void EnterWaitingForRoute()
    {
        moveState = MoveState.WaitingForRoute;
        if (rb != null)
            rb.linearVelocity = Vector2.zero;
    }

    /// <summary>Session disbanded because nobody sees the player anymore.</summary>
    public void ForceReturnHomeFromSession()
    {
        deployedThisVisit = false;
        playerInRange = false;
        BeginMove(rb.position, homePosition, returnDuration, MoveState.Returning);
    }

    /// <summary>
    /// True if a clear ground-free line exists from current position to the proposed wall slot.
    /// </summary>
    public bool CanReachWallSlot(Vector2 proposedCenter)
    {
        if (rb == null)
            return true;

        Vector2 from = rb.position;
        Vector2 delta = proposedCenter - from;
        float dist = delta.magnitude;
        if (dist < 0.05f)
            return true;

        RaycastHit2D hit = Physics2D.CircleCast(
            from,
            Mathf.Max(0.08f, Mathf.Min(BodySize.x, BodySize.y) * 0.28f),
            delta / dist,
            dist,
            groundLayers);

        if (hit.collider == null)
            return true;

        if (IsOwnCollider(GetComponentsInChildren<Collider2D>(true), hit.collider))
            return true;
        if (IsSpecialPassablePlatform(hit.collider))
            return true;
        if (hit.collider.GetComponentInParent<BlockerBot>() != null)
            return true;
        if (hit.collider.GetComponentInParent<PlayerController>() != null)
            return true;

        return false;
    }

    /// <summary>
    /// Solid vs the player and vs walls. Phases through slim/moving platforms, other enemies,
    /// other Blocker Bots, and bosses. Projectiles stay triggers so they still hit.
    /// </summary>
    public void RefreshPhaseCollisions()
    {
        Collider2D[] myCols = GetComponentsInChildren<Collider2D>(true);

        IgnoreCachedFamily(myCols, EnemyTypeCache.Blockers, this);
        IgnoreCachedFamily(myCols, EnemyTypeCache.Crankies);
        IgnoreCachedFamily(myCols, EnemyTypeCache.Lasers);
        IgnoreCachedFamily(myCols, EnemyTypeCache.Tankers);
        IgnoreCachedFamily(myCols, EnemyTypeCache.Scraps);
        IgnoreCachedFamily(myCols, EnemyTypeCache.CheckPoints);
        IgnoreCachedFamily(myCols, EnemyTypeCache.Bosses);

        MovingFactoryPlatform[] movers = EnemyTypeCache.Movers;
        for (int i = 0; i < movers.Length; i++)
        {
            if (movers[i] == null)
                continue;
            IgnoreSolidColliders(myCols, movers[i].GetComponentsInChildren<Collider2D>(true));
        }

        SlimFactoryPlatform[] slims = EnemyTypeCache.Slims;
        for (int i = 0; i < slims.Length; i++)
        {
            if (slims[i] == null)
                continue;
            IgnoreSolidColliders(myCols, slims[i].GetComponentsInChildren<Collider2D>(true));
        }

        RefreshDetectionColliderPhasing();
    }

    private static void IgnoreCachedFamily<T>(Collider2D[] myCols, T[] others, T self = null) where T : Component
    {
        if (others == null)
            return;

        for (int i = 0; i < others.Length; i++)
        {
            T other = others[i];
            if (other == null || (self != null && ReferenceEquals(other, self)))
                continue;
            IgnoreSolidColliders(myCols, other.GetComponentsInChildren<Collider2D>(true));
        }
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

    /// <summary>
    /// True if a wall-slot would sit inside solid wall/ground (not slim/moving platforms).
    /// </summary>
    public bool WouldOverlapPlatform(Vector2 proposedCenter)
    {
        if (bodyCollider == null)
            return false;

        Vector2 size = bodyCollider.bounds.size * 0.92f;
        Collider2D[] hits = Physics2D.OverlapBoxAll(proposedCenter, size, 0f, groundLayers);
        if (hits == null || hits.Length == 0)
            return false;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider2D hit = hits[i];
            if (hit == null || hit.isTrigger)
                continue;
            if (IsOwnCollider(GetComponentsInChildren<Collider2D>(true), hit))
                continue;
            if (IsSpecialPassablePlatform(hit))
                continue;

            return true;
        }

        return false;
    }

    private void ApplyIncomingDamage(int amount)
    {
        if (amount <= 0 || IsDead)
            return;

        // No i-frames — every shot that resolves may damage (same-frame multi-collider
        // hits from one projectile are already deduped by Projectile.hasResolvedCombatHit).
        currentHealth = Mathf.Max(0, currentHealth - amount);
        PlayHitSparks();
        SoundManager.Instance?.PlayBlockerBotHit();
        BeginHitAnim();

        if (currentHealth <= 0)
            Die();
    }

    private void UpdateDetection()
    {
        bool playerSeen = IsPlayerInsideDetection();
        bool allySeen = IsDeployedAllyInsideDetection();
        playerInRange = playerSeen;

        if (playerSeen || allySeen)
        {
            if (!deployedThisVisit)
            {
                deployedThisVisit = true;
                BlockerBotWallSession.TryBeginOrJoin(this, targetPlayer, standOffSpaces);
                // If no open route, session puts this bot into WaitingForRoute.
            }
            else if (moveState == MoveState.WaitingForRoute)
            {
                BlockerBotWallSession.TryAssignWaiting(this);
            }

            return;
        }

        // Lost local player + ally sight. Stay in formation if others still see the player.
        if (!deployedThisVisit)
            return;

        if (BlockerBotWallSession.Contains(this))
        {
            BlockerBotWallSession.DisbandIfPlayerLost();
            return;
        }

        deployedThisVisit = false;
        BeginMove(rb.position, homePosition, returnDuration, MoveState.Returning);
    }

    /// <summary>
    /// Join a wall when another Blocker Bot in detection saw the player or is already blocking.
    /// </summary>
    private bool IsDeployedAllyInsideDetection()
    {
        if (detectionCollider == null || IsDead)
            return false;

        BlockerBot[] bots = EnemyTypeCache.Blockers;
        Bounds myVision = detectionCollider.bounds;

        for (int i = 0; i < bots.Length; i++)
        {
            BlockerBot other = bots[i];
            if (other == null || other == this || other.IsDead)
                continue;

            // Must have detected the player themselves, or already be in/holding a wall.
            if (!other.SeesPlayerNow && !other.IsActivelyBlocking)
                continue;

            Collider2D otherBody = other.GetComponent<Collider2D>();
            if (otherBody == null)
                otherBody = other.GetComponentInChildren<Collider2D>();
            if (otherBody == null)
                continue;

            if (myVision.Intersects(otherBody.bounds))
                return true;
        }

        return false;
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

    private void BeginMove(Vector2 from, Vector2 to, float duration, MoveState next)
    {
        moveFrom = from;
        moveTo = to;
        moveElapsed = 0f;
        moveDuration = Mathf.Max(0.05f, duration);
        moveState = next;
        SoundManager.Instance?.PlayBlockerBotDash();
    }

    private void TickMovement(float dt)
    {
        if (moveState == MoveState.WaitingForRoute)
        {
            // Stay put until a wall slot opens.
            return;
        }

        if (moveState != MoveState.Approaching && moveState != MoveState.Returning)
        {
            if (moveState == MoveState.HoldingWall)
                MoveWithoutRammingPlayer(wallSlot);
            else if (moveState == MoveState.HomeIdle)
                MoveWithoutRammingPlayer(homePosition);
            return;
        }

        moveElapsed += dt;
        float t = Mathf.Clamp01(moveElapsed / moveDuration);
        float s = t * t * t * (t * (t * 6f - 15f) + 10f);
        Vector2 pos = Vector2.LerpUnclamped(moveFrom, moveTo, s);
        MoveWithoutRammingPlayer(pos);

        if (t < 1f)
            return;

        if (moveState == MoveState.Approaching)
        {
            moveState = MoveState.HoldingWall;
            MoveWithoutRammingPlayer(wallSlot);
        }
        else
        {
            moveState = MoveState.HomeIdle;
            MoveWithoutRammingPlayer(homePosition);
        }
    }

    /// <summary>
    /// Kinematic bodies shove dynamic ones with unlimited force, which launched the player
    /// (sometimes through platforms). Stop just short of the player instead; contact still
    /// gives the normal capped soft bounce.
    /// </summary>
    private void MoveWithoutRammingPlayer(Vector2 target)
    {
        Vector2 from = rb.position;
        Vector2 delta = target - from;
        float dist = delta.magnitude;
        if (dist < 0.0001f || bodyCollider == null)
        {
            rb.MovePosition(target);
            return;
        }

        Vector2 dir = delta / dist;
        ContactFilter2D filter = new ContactFilter2D();
        filter.useTriggers = false;

        int hitCount = bodyCollider.Cast(dir, filter, moveCastHits, dist + RamSkin, true);
        float allowed = dist;
        for (int i = 0; i < hitCount; i++)
        {
            Collider2D hit = moveCastHits[i].collider;
            if (hit == null || !BelongsToPlayer(hit))
                continue;

            allowed = Mathf.Min(allowed, Mathf.Max(0f, moveCastHits[i].distance - RamSkin));
        }

        rb.MovePosition(from + dir * allowed);
    }

    private void TryBouncePlayer(Collision2D collision)
    {
        if (collision == null || bounceCooldownTimer > 0f || IsDead)
            return;

        PlayerController player = collision.collider.GetComponent<PlayerController>();
        if (player == null)
            player = collision.collider.GetComponentInParent<PlayerController>();

        if (player == null || player.IsDead)
            return;

        // Walkable blocks: only side contact bounces (top / bottom never shove).
        if (!TryGetSideBounceDirection(collision, out float dir))
            return;

        float distance = Mathf.Clamp(bounceDistance, 0.05f, 1f);
        player.ApplySoftBounce(dir, distance, bounceDuration);
        bounceCooldownTimer = Mathf.Max(0.05f, bounceCooldown);
    }

    /// <summary>
    /// True when the strongest contact is lateral. Uses the contact normal on this body
    /// (from the other collider toward this one).
    /// </summary>
    private bool TryGetSideBounceDirection(Collision2D collision, out float dir)
    {
        dir = 0f;
        if (collision == null || collision.contactCount <= 0)
            return false;

        float bestAbsX = 0f;
        Vector2 bestNormal = Vector2.zero;
        for (int i = 0; i < collision.contactCount; i++)
        {
            Vector2 n = collision.GetContact(i).normal;
            float ax = Mathf.Abs(n.x);
            if (ax > bestAbsX)
            {
                bestAbsX = ax;
                bestNormal = n;
            }
        }

        if (bestAbsX < sideBounceNormalMin || bestAbsX <= Mathf.Abs(bestNormal.y))
            return false;

        // Normal points toward this blocker from the player → shove player the opposite way.
        dir = -Mathf.Sign(bestNormal.x);
        if (Mathf.Abs(dir) < 0.01f)
            return false;

        return true;
    }

    private void ApplyGroundLayer()
    {
        int ground = LayerMask.NameToLayer("Ground");
        if (ground < 0)
            return;

        // Body only — keep detection / FX children off Ground so probes stay clean.
        gameObject.layer = ground;
    }

    private void TickBounceCooldown(float dt)
    {
        if (bounceCooldownTimer > 0f)
            bounceCooldownTimer = Mathf.Max(0f, bounceCooldownTimer - dt);
    }

    private void PlayHitSparks()
    {
        VisualEffects.SpawnBlockerBotHitSparks(hitSparkPrefab, sparkSpawnPoint != null
            ? sparkSpawnPoint.position
            : transform.position);
    }

    private void BeginHitAnim()
    {
        hitAnimTimer = Mathf.Max(0.05f, hitAnimDuration);
        if (animator == null)
            return;

        animator.SetBool(IsGettingHitHash, true);
        animator.ResetTrigger(HitTriggerHash);
        animator.SetTrigger(HitTriggerHash);
        PlayAnimationState(HitStateHash);
    }

    private void TickHitAnim(float dt)
    {
        if (hitAnimTimer <= 0f)
            return;

        hitAnimTimer -= dt;
        if (hitAnimTimer > 0f)
            return;

        hitAnimTimer = 0f;
        if (animator != null)
            animator.SetBool(IsGettingHitHash, false);
    }

    private void UpdateAnimator()
    {
        bool blocking = moveState == MoveState.Approaching || moveState == MoveState.HoldingWall;
        SetBlockingAnimator(blocking);

        if (hitAnimTimer > 0f)
            return;

        PlayAnimationState(blocking ? BlockingStateHash : IdleStateHash);
    }

    private void SetBlockingAnimator(bool blocking)
    {
        if (animator == null)
            return;

        animator.SetBool(IsBlockingHash, blocking);
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

    private void Die()
    {
        BlockerBotWallSession.Leave(this);

        Vector3 center = explosionSpawnPoint != null
            ? explosionSpawnPoint.position
            : transform.position;

        VisualEffects.SpawnBlockerSmokeDeath(
            new[] { smokeParticle1, smokeParticle2, smokeParticle3, smokeParticle4 },
            center,
            smokeCopiesPerParticle,
            smokeScatterRadius,
            smokeRiseDistance,
            smokeRiseSpeed,
            spriteRenderer);

        SoundManager.Instance?.PlayBlockerBotDeath();
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

        if (sparkSpawnPoint == null)
        {
            Transform found = transform.Find("Spark Spawn Box");
            if (found != null)
                sparkSpawnPoint = found;
        }

        if (explosionSpawnPoint == null)
        {
            Transform found = transform.Find("Death explosion Spawn Box");
            if (found == null)
                found = transform.Find("Death Explosion Spawn Box");
            if (found != null)
                explosionSpawnPoint = found;
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
            rb.useFullKinematicContacts = true;
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
        HideIfSceneObject(hitSparkPrefab != null ? hitSparkPrefab.gameObject : null);
        HideIfSceneObject(smokeParticle1 != null ? smokeParticle1.gameObject : null);
        HideIfSceneObject(smokeParticle2 != null ? smokeParticle2.gameObject : null);
        HideIfSceneObject(smokeParticle3 != null ? smokeParticle3.gameObject : null);
        HideIfSceneObject(smokeParticle4 != null ? smokeParticle4.gameObject : null);
    }

    private static void HideIfSceneObject(GameObject go)
    {
        if (go == null || !go.scene.IsValid())
            return;

        go.SetActive(false);
    }

    private static bool IsOwnCollider(Collider2D[] myCols, Collider2D other)
    {
        if (myCols == null || other == null)
            return false;

        for (int i = 0; i < myCols.Length; i++)
        {
            if (myCols[i] == other)
                return true;
        }

        return false;
    }

    private static bool BelongsToPlayer(Collider2D col)
    {
        return col != null && col.GetComponentInParent<PlayerController>() != null;
    }

    private static bool IsSpecialPassablePlatform(Collider2D col)
    {
        if (col == null)
            return false;

        return col.GetComponentInParent<MovingFactoryPlatform>() != null
               || col.GetComponentInParent<SlimFactoryPlatform>() != null;
    }

    /// <summary>
    /// Phase through special platforms + combatants. Stay solid vs walls / thick ground / player.
    /// </summary>
    private static bool ShouldPhaseThrough(Collider2D col)
    {
        if (col == null || BelongsToPlayer(col))
            return false;

        if (IsSpecialPassablePlatform(col))
            return true;

        if (col.GetComponentInParent<BlockerBot>() != null)
            return true;
        if (col.GetComponentInParent<CrankyClanky>() != null)
            return true;
        if (col.GetComponentInParent<LaserBot>() != null)
            return true;
        if (col.GetComponentInParent<ChaoticTanker>() != null)
            return true;
        if (col.GetComponentInParent<ScrapNit>() != null)
            return true;
        if (col.GetComponentInParent<Boss>() != null)
            return true;
        if (col.GetComponentInParent<CheckPointBot>() != null)
            return true;
        if (col.GetComponentInParent<Crystal>() != null)
            return true;
        if (col.GetComponentInParent<EnemyCrystal>() != null)
            return true;
        if (col.GetComponentInParent<PhaseThroughPlayers>() != null)
            return true;

        return false;
    }

    private static void IgnoreSolidCollidersAgainst(Collider2D[] aCols, Collider2D other)
    {
        if (aCols == null || other == null || other.isTrigger)
            return;

        for (int i = 0; i < aCols.Length; i++)
        {
            Collider2D a = aCols[i];
            if (a == null || a.isTrigger)
                continue;

            Physics2D.IgnoreCollision(a, other, true);
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        maxHealth = Mathf.Max(1, maxHealth);
        standOffSpaces = Mathf.Max(0.1f, standOffSpaces);
        approachDuration = Mathf.Max(0.05f, approachDuration);
        returnDuration = Mathf.Max(0.05f, returnDuration);
        bounceDistance = Mathf.Clamp(bounceDistance, 0.05f, 1f);
        bounceDuration = Mathf.Max(0.05f, bounceDuration);
        bounceCooldown = Mathf.Max(0.05f, bounceCooldown);
        sideBounceNormalMin = Mathf.Clamp(sideBounceNormalMin, 0.2f, 0.95f);
        smokeCopiesPerParticle = Mathf.Max(1, smokeCopiesPerParticle);
        smokeRiseDistance = Mathf.Max(0.1f, smokeRiseDistance);
        smokeRiseSpeed = Mathf.Max(0.1f, smokeRiseSpeed);
        hitAnimDuration = Mathf.Max(0.05f, hitAnimDuration);
    }
#endif
}
