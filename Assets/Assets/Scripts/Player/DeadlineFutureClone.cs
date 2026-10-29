using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Deadline Future Clone (Attack Style ≥2, Spread / Machine Gun): born from a dash afterimage.
/// Bodyguards ahead of Deadline, hunts enemies in range, and only fires normal / charged hands.
/// Matches Deadline's move / jump limits; no DA, styles, or other upgrades.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class DeadlineFutureClone : MonoBehaviour, IDamageable
{
    private enum AiMode
    {
        Guard,
        Hunt,
        Evade,
        Reunite
    }

    private static readonly List<Collider2D> Overlaps = new List<Collider2D>(48);
    private static readonly Collider2D[] HazardHits = new Collider2D[12];

    private DeadlinePlayerController owner;
    private Projectile shortHandPrefab;
    private Projectile longHandPrefab;
    private CollectableDeadlineHealClock healClockPrefab;
    private CollectableDeadlineRechargeClock rechargeClockPrefab;
    private SpriteRenderer spriteRenderer;
    private Animator animator;
    private Rigidbody2D rb;
    private BoxCollider2D bodyCollider;
    private Color baseColor;
    private bool isShootingAnim;
    private float shootAnimTimer;
    private int health;
    private int maxHealth;
    private float damageScale = 0.6f;
    private float moveSpeed = 6f;
    private float jumpForce = 12f;
    private float fallMultiplier = 2.5f;
    private float searchRadius = 10f;
    private float guardAheadSpaces = 3f;
    private float shootInterval = 0.55f;
    private float chargeSeconds = 3f;
    private float attackRange = 8f;
    private LayerMask groundLayers;
    private float groundCheckRadius = 0.12f;
    private int maxAirJumps;
    private int airJumpsRemaining;
    private bool clockSupport;
    private float clockPulseSeconds = 5f;
    private int clockPulseCount = 1;
    private float nextClockPulseAt;

    private AiMode mode = AiMode.Guard;
    private float nextShotAt;
    private float chargeTimer;
    private bool isCharging;
    private float hitFlashUntil;
    private float fadeOutStartedAt = -1f;
    private float shimmerPhase;
    private bool artFacesLeft = true;
    private bool isGrounded;
    private float coyoteTimer;
    private float jumpCooldownUntil;
    private float evadeUntil;
    private float facingSign = 1f;
    private Transform huntTarget;
    private Vector2 evadeDir = Vector2.right;
    private float desiredMoveDir;
    private float committedMoveDir;
    private float moveCommitUntil;
    private bool guardArrived;
    private bool samePlatformLatched = true;
    private float samePlatformFlipAt = -1f;
    private float latchedTravelFacing = 1f;
    private float travelFacingLockUntil;
    private float latchedPatrolTravel = 1f;
    private float patrolTravelLockUntil;
    private Vector3 lastProgressPos;
    private float stuckTimer;
    private float nextPhaseRefreshAt;
    private float hazardWaitUntil;
    private float leapCommitUntil;
    private float airSteerUntil;
    private float lastParkourJumpAt;
    private float faceLockUntil;
    private float turnAroundUntil;
    private float pendingTurnDir;
    private bool voluntarilyAirborne;

    private const float FadeOutSeconds = 0.22f;
    private const float CoyoteTime = 0.12f;
    private const float GapLookAhead = 1.15f;
    private const float WallProbe = 0.65f;
    private const float HazardProbe = 1.1f;
    private const float PreferShootDistance = 5.5f;
    private const float EffectiveAttackRange = 6.25f;
    private const float MaxOwnerDistance = 35f;
    private const float StuckSeconds = 2.6f;
    private const float EdgeMargin = 0.7f;
    private const float PlatformProbeDown = 1.6f;
    private const float BesideOffset = 1.0f;
    private const float OwnerAssistRadius = 20f;
    private const float SamePlatformYTolerance = 0.85f;
    private const float PlatformPathStep = 0.55f;
    private const float MoveCommitSeconds = 0.42f;
    private const float GuardArriveDistance = 0.55f;
    private const float GuardLeaveDistance = 1.15f;
    private const float FaceFlipMinSeconds = 0.28f;
    private const float TurnAroundSeconds = 0.24f;
    private const float TurnAroundUrgentSeconds = 0.12f;
    private const float TravelFacingLockSeconds = 0.55f;
    private const float PlatformLatchSeconds = 0.35f;

    public bool IsDead => health <= 0 || fadeOutStartedAt >= 0f;
    public int Health => health;
    public int MaxHealth => maxHealth;

    /// <summary>Used when Deadline absorbs this clone on a killing blow.</summary>
    public Vector3 AbsorbPosition => transform.position;

    public static DeadlineFutureClone Spawn(
        DeadlinePlayerController owner,
        Vector3 position,
        Sprite sprite,
        bool flipX,
        Color tint,
        Projectile shortHand,
        Projectile longHand,
        int health,
        float damageScale,
        float moveSpeed,
        float jumpForce,
        float fallMultiplier,
        float searchRadius,
        float guardAheadSpaces,
        float shootInterval,
        float chargeSeconds,
        LayerMask groundLayers,
        float groundCheckRadius,
        Vector2 bodySize,
        Vector2 bodyOffset,
        int airJumpBudget,
        bool enableClockSupport,
        CollectableDeadlineHealClock healClock,
        CollectableDeadlineRechargeClock rechargeClock,
        float clockPulseInterval,
        int clockPulseCount)
    {
        if (owner == null || sprite == null)
            return null;

        var go = new GameObject($"{owner.name}_FutureClone");
        go.transform.position = position;
        var clone = go.AddComponent<DeadlineFutureClone>();
        clone.Init(
            owner, sprite, flipX, tint, shortHand, longHand, health, damageScale,
            moveSpeed, jumpForce, fallMultiplier, searchRadius, guardAheadSpaces,
            shootInterval, chargeSeconds, groundLayers, groundCheckRadius, bodySize, bodyOffset,
            airJumpBudget, enableClockSupport, healClock, rechargeClock, clockPulseInterval, clockPulseCount);
        return clone;
    }

    private void Init(
        DeadlinePlayerController cloneOwner,
        Sprite sprite,
        bool flipX,
        Color tint,
        Projectile shortHand,
        Projectile longHand,
        int hp,
        float powerScale,
        float speed,
        float jump,
        float fallMul,
        float radius,
        float guardAhead,
        float interval,
        float charge,
        LayerMask grounds,
        float groundRadius,
        Vector2 bodySize,
        Vector2 bodyOffset,
        int airJumpBudget,
        bool enableClockSupport,
        CollectableDeadlineHealClock healClock,
        CollectableDeadlineRechargeClock rechargeClock,
        float clockPulseInterval,
        int clocksPerPulse)
    {
        owner = cloneOwner;
        shortHandPrefab = shortHand;
        longHandPrefab = longHand;
        healClockPrefab = healClock;
        rechargeClockPrefab = rechargeClock;
        health = Mathf.Max(1, hp);
        maxHealth = health;
        damageScale = Mathf.Max(0.05f, powerScale);
        moveSpeed = Mathf.Max(0.5f, speed);
        jumpForce = Mathf.Max(1f, jump);
        fallMultiplier = Mathf.Max(1f, fallMul);
        searchRadius = Mathf.Max(2f, radius);
        guardAheadSpaces = Mathf.Max(0.5f, guardAhead);
        shootInterval = Mathf.Max(0.15f, interval);
        chargeSeconds = Mathf.Max(0.5f, charge);
        groundLayers = grounds;
        groundCheckRadius = Mathf.Max(0.05f, groundRadius);
        maxAirJumps = Mathf.Max(0, airJumpBudget);
        airJumpsRemaining = maxAirJumps;
        clockSupport = enableClockSupport;
        clockPulseSeconds = Mathf.Max(0.5f, clockPulseInterval);
        clockPulseCount = Mathf.Max(1, clocksPerPulse);
        nextClockPulseAt = Time.time + clockPulseSeconds * 0.5f;
        baseColor = tint;
        nextShotAt = Time.time + shootInterval * 0.35f;

        spriteRenderer = GetComponent<SpriteRenderer>();
        spriteRenderer.sprite = sprite;
        spriteRenderer.flipX = flipX;
        spriteRenderer.color = tint;
        artFacesLeft = true;
        facingSign = flipX ? 1f : -1f;
        committedMoveDir = facingSign;
        latchedTravelFacing = facingSign;
        latchedPatrolTravel = facingSign;
        samePlatformLatched = true;
        guardArrived = false;
        turnAroundUntil = 0f;
        pendingTurnDir = 0f;

        SpriteRenderer body = owner.BodySpriteRenderer;
        SortingGroup group = owner.EffectSortingGroup;
        CharacterEffectSorting.ApplyDetachedEffectNearHost(spriteRenderer, group, body, -1);
        if (body != null)
            spriteRenderer.sharedMaterial = body.sharedMaterial;

        // Own Animator copy of Deadline's controller — never borrow Deadline's current sprite frame.
        SetupIndependentAnimator();

        rb = gameObject.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.freezeRotation = true;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.gravityScale = owner.GetComponent<Rigidbody2D>() != null
            ? owner.GetComponent<Rigidbody2D>().gravityScale
            : 3f;

        bodyCollider = gameObject.AddComponent<BoxCollider2D>();
        bodyCollider.isTrigger = false;
        if (bodySize.sqrMagnitude > 0.01f)
        {
            bodyCollider.size = bodySize;
            bodyCollider.offset = bodyOffset;
        }
        else
        {
            bodyCollider.size = new Vector2(0.7f, 1.35f);
        }

        // Soften bumping into the player / other clones.
        Collider2D ownerBody = owner.BodyCollider;
        if (ownerBody != null)
            Physics2D.IgnoreCollision(bodyCollider, ownerBody, true);

        EnemyDetectionZone.IgnoreAgainstAllZones(bodyCollider);
        RefreshPhaseCollisions();
        lastProgressPos = transform.position;
        nextPhaseRefreshAt = Time.time + 1f;
    }

    private void FixedUpdate()
    {
        if (fadeOutStartedAt >= 0f || IsDead || rb == null)
            return;

        UpdateGrounded();
        ApplyFallMultiplier();

        if (owner == null || owner.IsDead)
        {
            BeginFadeOut();
            return;
        }

        if (Time.time >= nextPhaseRefreshAt)
        {
            nextPhaseRefreshAt = Time.time + 1.25f;
            RefreshPhaseCollisions();
        }

        TickAi(Time.fixedDeltaTime);
        TickStuckAndRange(Time.fixedDeltaTime);
    }

    private void Update()
    {
        if (fadeOutStartedAt >= 0f)
        {
            TickFadeOut();
            return;
        }

        if (owner == null || owner.IsDead)
        {
            BeginFadeOut();
            return;
        }

        TickVisuals();
        TickCombat(Time.deltaTime);
        TickClockSupport();
        TickShootAnimTimer(Time.deltaTime);
        UpdateAnimator();
    }

    private void TickClockSupport()
    {
        if (!clockSupport || IsDead || owner == null || owner.IsDead)
            return;
        if (Time.time < nextClockPulseAt)
            return;

        nextClockPulseAt = Time.time + clockPulseSeconds;
        Vector3 center = bodyCollider != null ? bodyCollider.bounds.center : transform.position;
        float seek = owner.HyperClockSeekSpeed;
        int count = Mathf.Max(1, clockPulseCount);

        // Always pulse recharge clocks.
        if (rechargeClockPrefab != null)
            CollectableDeadlineRechargeClock.SpawnFloatingCluster(rechargeClockPrefab, center, count, seek);

        // Heal clocks only while Deadline is below 80% HP.
        if (healClockPrefab != null &&
            owner.MaxHealth > 0 &&
            owner.CurrentHealth < owner.MaxHealth * 0.8f)
        {
            CollectableDeadlineHealClock.SpawnFloatingCluster(healClockPrefab, center, count, seek);
        }
    }

    private void SetupIndependentAnimator()
    {
        Animator ownerAnim = owner != null ? owner.GetComponent<Animator>() : null;
        animator = gameObject.GetComponent<Animator>();
        if (animator == null)
            animator = gameObject.AddComponent<Animator>();

        if (ownerAnim != null && ownerAnim.runtimeAnimatorController != null)
        {
            animator.runtimeAnimatorController = ownerAnim.runtimeAnimatorController;
            animator.avatar = ownerAnim.avatar;
        }

        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.speed = 1f;
        UpdateAnimator();
    }

    private void TickShootAnimTimer(float dt)
    {
        if (!isShootingAnim)
            return;

        shootAnimTimer -= dt;
        if (shootAnimTimer <= 0f)
        {
            isShootingAnim = false;
            shootAnimTimer = 0f;
        }
    }

    private void UpdateAnimator()
    {
        if (animator == null)
            return;

        float verticalSpeed = rb != null ? rb.linearVelocity.y : 0f;
        float horizontalSpeed = rb != null ? rb.linearVelocity.x : 0f;
        // Drive from THIS clone's motion only — never from Deadline's inputs/sprites.
        // Ignore lingering foot overlap while rising so Jump plays on the first leap.
        bool groundedForAnim = isGrounded && !voluntarilyAirborne;
        bool moving = groundedForAnim && Mathf.Abs(horizontalSpeed) > 0.35f;

        animator.SetBool("IsMoving", moving);
        animator.SetBool("IsGrounded", groundedForAnim);
        animator.SetBool("IsInAir", !groundedForAnim);
        animator.SetBool("IsDashing", false);
        animator.SetBool("IsShooting", isShootingAnim);
        animator.SetBool("AimUp", false);
        animator.SetBool("IsStunned", false);
        animator.SetBool("IsFalling", !groundedForAnim && verticalSpeed < -0.01f);
        animator.SetBool("IsJumping", !groundedForAnim && verticalSpeed >= -0.01f);
        animator.SetFloat("VerticalSpeed", verticalSpeed);
    }

    private void TickAi(float dt)
    {
        desiredMoveDir = 0f;

        if (Time.time < evadeUntil)
        {
            mode = AiMode.Evade;
            NavigateToward(evadeDir.x, allowEdgeWalk: true, forceDirection: true);
            return;
        }

        if (TryBeginEvade())
            return;

        Transform enemy = FindClosestEnemy();
        if (enemy != null)
        {
            mode = AiMode.Hunt;
            huntTarget = enemy;
            TickHunt(enemy);
            return;
        }

        huntTarget = null;

        // Out of combat: get back onto Deadline's platform before patrolling ahead.
        if (!EvaluateSamePlatformAsOwner())
        {
            mode = AiMode.Reunite;
            TickReunite();
            return;
        }

        mode = AiMode.Guard;
        TickGuard();
    }

    private void TickGuard()
    {
        Vector3 guardPos = ResolvePatrolGuardPosition();
        float dx = guardPos.x - transform.position.x;
        float absDx = Mathf.Abs(dx);

        // Arrive / leave hysteresis — stops left-right buzzing on the patrol point.
        if (guardArrived)
        {
            if (absDx <= GuardLeaveDistance)
            {
                CommitMove(0f);
                MoveHorizontal(0f);
                FaceStable(GetOwnerTravelFacing());
                return;
            }

            guardArrived = false;
        }
        else if (absDx <= GuardArriveDistance)
        {
            guardArrived = true;
            CommitMove(0f);
            MoveHorizontal(0f);
            FaceStable(GetOwnerTravelFacing());
            return;
        }

        NavigateToward(Mathf.Sign(dx), allowEdgeWalk: false);
    }

    /// <summary>
    /// Parkour toward Deadline's platform/elevation when separated and not fighting.
    /// </summary>
    private void TickReunite()
    {
        if (owner == null)
            return;

        Vector3 ownerPos = owner.transform.position;
        float ownerGroundY = ownerPos.y;
        if (TryGetWorldGroundY(ownerPos.x, ownerPos.y + 1.8f, out float gy))
            ownerGroundY = gy;

        float dx = ownerPos.x - transform.position.x;
        float dir;
        if (Mathf.Abs(dx) > 0.45f)
            dir = Mathf.Sign(dx);
        else
            dir = GetOwnerTravelFacing();

        // Climb / drop toward his floor while closing X.
        NavigateToward(dir, allowEdgeWalk: true, huntTargetY: ownerGroundY);

        if (ownerGroundY > transform.position.y + 0.7f)
            TryJumpToHigherLedge(dir);
        else if (NeedsJumpToReachHeight(ownerGroundY))
            TryJumpToHigherLedge(dir);
    }

    /// <summary>
    /// Latched same-platform check so town geometry / brief gaps don't flip Guard↔Reunite every frame.
    /// </summary>
    private bool EvaluateSamePlatformAsOwner()
    {
        bool raw = IsOnSamePlatformAsOwnerRaw();
        if (raw == samePlatformLatched)
        {
            samePlatformFlipAt = -1f;
            return samePlatformLatched;
        }

        if (samePlatformFlipAt < 0f)
            samePlatformFlipAt = Time.time + PlatformLatchSeconds;

        if (Time.time < samePlatformFlipAt)
            return samePlatformLatched;

        samePlatformLatched = raw;
        samePlatformFlipAt = -1f;
        return samePlatformLatched;
    }

    /// <summary>
    /// True when continuous walkable ground links this clone to Deadline at roughly the same elevation.
    /// </summary>
    private bool IsOnSamePlatformAsOwnerRaw()
    {
        if (owner == null)
            return true;

        Vector3 ownerPos = owner.transform.position;
        Vector3 selfPos = transform.position;

        bool ownerGrounded = TryGetWorldGroundY(ownerPos.x, ownerPos.y + 1.8f, out float ownerY);
        bool selfGrounded = TryGetWorldGroundY(selfPos.x, selfPos.y + 1.8f, out float selfY);

        // Owner mid-air: treat as same platform only if we're near his X and not far below a ledge he just left.
        if (!ownerGrounded)
        {
            return Mathf.Abs(ownerPos.x - selfPos.x) < 2.2f &&
                   Mathf.Abs(ownerPos.y - selfPos.y) < 2.5f;
        }

        if (!selfGrounded)
            return false;

        if (Mathf.Abs(ownerY - selfY) > SamePlatformYTolerance)
            return false;

        float dist = Mathf.Abs(ownerPos.x - selfPos.x);
        if (dist <= 1.25f)
            return true;

        float sign = Mathf.Sign(ownerPos.x - selfPos.x);
        float refY = (ownerY + selfY) * 0.5f;
        for (float d = PlatformPathStep; d < dist; d += PlatformPathStep)
        {
            float x = selfPos.x + sign * d;
            if (!TryGetWorldGroundY(x, refY + 1.6f, out float y))
                return false;
            if (Mathf.Abs(y - ownerY) > 1.15f && Mathf.Abs(y - selfY) > 1.15f)
                return false;
        }

        return true;
    }

    /// <summary>Lock a move direction for a beat so AI doesn't flip-flop every FixedUpdate.</summary>
    private void CommitMove(float dir, bool force = false)
    {
        float sign = Mathf.Abs(dir) < 0.01f ? 0f : Mathf.Sign(dir);
        if (!force &&
            Mathf.Abs(sign) > 0.01f &&
            Mathf.Abs(committedMoveDir) > 0.01f &&
            !Mathf.Approximately(sign, committedMoveDir) &&
            Time.time < moveCommitUntil)
        {
            desiredMoveDir = committedMoveDir;
            return;
        }

        committedMoveDir = sign;
        desiredMoveDir = sign;
        if (Mathf.Abs(sign) > 0.01f)
            moveCommitUntil = Time.time + MoveCommitSeconds;
    }

    private float GetCommittedMoveOr(float wanted, bool force = false)
    {
        CommitMove(wanted, force);
        return committedMoveDir;
    }

    /// <summary>Consume this clone when Deadline transfers into it (killing-blow handoff).</summary>
    public void ConsumeForOwnerTransfer()
    {
        health = 0;
        fadeOutStartedAt = Time.time;
        if (bodyCollider != null)
            bodyCollider.enabled = false;
        if (rb != null)
            rb.linearVelocity = Vector2.zero;
        Destroy(gameObject);
    }

    /// <summary>
    /// Prefer ~3 ahead of Deadline whenever open ground allows; else beside; else his tile.
    /// Patrol slot is stable (no weave) so clones don't buzz on the spot.
    /// </summary>
    private Vector3 ResolvePatrolGuardPosition()
    {
        float travel = GetStablePatrolTravel();
        Vector3 ownerPos = owner.transform.position;
        if (!TryGetWorldGroundY(ownerPos.x, ownerPos.y + 1.5f, out float ownerGroundY))
            ownerGroundY = ownerPos.y;

        float maxSafeAhead = MeasureSafeAheadDistance(ownerPos.x, ownerGroundY, travel);

        // Prefer full ahead patrol whenever we have a real corridor.
        if (maxSafeAhead >= 1.0f)
        {
            float desired = Mathf.Min(guardAheadSpaces, maxSafeAhead);
            return new Vector3(ownerPos.x + travel * desired, ownerGroundY, ownerPos.z);
        }

        // Any usable ahead strip.
        if (maxSafeAhead >= 0.45f)
            return new Vector3(ownerPos.x + travel * maxSafeAhead, ownerGroundY, ownerPos.z);

        // Beside Deadline — prefer latched travel side, then opposite, then on him.
        if (TryGetWorldGroundY(ownerPos.x + travel * BesideOffset, ownerGroundY + 1.5f, out float besideY) &&
            !IsLethalNear(new Vector3(ownerPos.x + travel * BesideOffset, besideY + 0.35f, ownerPos.z)))
            return new Vector3(ownerPos.x + travel * BesideOffset, besideY, ownerPos.z);

        if (TryGetWorldGroundY(ownerPos.x - travel * BesideOffset, ownerGroundY + 1.5f, out float backY) &&
            !IsLethalNear(new Vector3(ownerPos.x - travel * BesideOffset, backY + 0.35f, ownerPos.z)))
            return new Vector3(ownerPos.x - travel * BesideOffset, backY, ownerPos.z);

        return new Vector3(ownerPos.x, ownerGroundY, ownerPos.z);
    }

    private float GetStablePatrolTravel()
    {
        float wanted = GetOwnerTravelFacing();
        if (Time.time < patrolTravelLockUntil &&
            Mathf.Abs(latchedPatrolTravel) > 0.01f &&
            !Mathf.Approximately(wanted, latchedPatrolTravel))
            return latchedPatrolTravel;

        latchedPatrolTravel = wanted;
        patrolTravelLockUntil = Time.time + TravelFacingLockSeconds;
        return latchedPatrolTravel;
    }

    private float GetOwnerTravelFacing()
    {
        if (owner == null)
            return facingSign;

        float wanted = facingSign;
        Rigidbody2D ownerRb = owner.GetComponent<Rigidbody2D>();
        // Strong velocity only — ignore micro jitter while standing in town.
        if (ownerRb != null && Mathf.Abs(ownerRb.linearVelocity.x) > 1.1f)
            wanted = Mathf.Sign(ownerRb.linearVelocity.x);
        else
        {
            float face = owner.FacingSign;
            if (Mathf.Abs(face) > 0.01f)
                wanted = Mathf.Sign(face);
        }

        if (Time.time < travelFacingLockUntil &&
            Mathf.Abs(latchedTravelFacing) > 0.01f &&
            !Mathf.Approximately(wanted, latchedTravelFacing))
            return latchedTravelFacing;

        latchedTravelFacing = wanted;
        travelFacingLockUntil = Time.time + TravelFacingLockSeconds;
        return latchedTravelFacing;
    }

    /// <summary>How far ahead stays on solid world ground with a small ledge margin.</summary>
    private float MeasureSafeAheadDistance(float ownerX, float groundY, float travel)
    {
        float best = 0f;
        float ledgePad = 0.45f;

        for (float d = 0.35f; d <= guardAheadSpaces + 0.01f; d += 0.25f)
        {
            float x = ownerX + travel * d;
            if (!TryGetWorldGroundY(x, groundY + 1.5f, out float y))
                break;
            if (Mathf.Abs(y - groundY) > 1.15f)
                break;
            if (IsLethalNear(new Vector3(x, y + 0.35f, 0f)))
                break;

            // Need a little ground past this point so we don't park on a lip.
            float edgeX = x + travel * ledgePad;
            if (!TryGetWorldGroundY(edgeX, y + 1.2f, out float edgeY))
                break;
            if (Mathf.Abs(edgeY - y) > 1.0f)
                break;

            best = d;
        }

        return best;
    }

    /// <summary>Raycast for real world ground — ignores players, clones, enemies, bosses, ship.</summary>
    private bool TryGetWorldGroundY(float x, float fromY, out float groundY)
    {
        groundY = fromY;
        RaycastHit2D[] hits = Physics2D.RaycastAll(
            new Vector2(x, fromY),
            Vector2.down,
            PlatformProbeDown + 1.2f,
            groundLayers);

        float bestY = float.NegativeInfinity;
        bool found = false;
        for (int i = 0; i < hits.Length; i++)
        {
            Collider2D col = hits[i].collider;
            if (col == null || !IsWorldGroundCollider(col))
                continue;

            float y = hits[i].point.y;
            if (y > bestY)
            {
                bestY = y;
                found = true;
            }
        }

        if (!found)
            return false;

        groundY = bestY;
        return true;
    }

    private static bool IsWorldGroundCollider(Collider2D col)
    {
        if (col == null || col.isTrigger)
            return false;
        if (col.GetComponentInParent<PlayerController>() != null)
            return false;
        if (col.GetComponentInParent<DeadlineFutureClone>() != null)
            return false;
        if (col.GetComponentInParent<CountTimeClone>() != null)
            return false;
        if (col.GetComponentInParent<Boss>() != null)
            return false;
        if (col.GetComponentInParent<ICommonEnemy>() != null)
            return false;
        if (col.GetComponentInParent<KitRocketShip>() != null)
            return false;
        if (col.GetComponentInParent<NPC>() != null)
            return false;
        return true;
    }

    private void TickHunt(Transform enemy)
    {
        Vector3 target = enemy.position;
        Collider2D enemyCol = enemy.GetComponentInChildren<Collider2D>();
        if (enemyCol != null)
            target = enemyCol.bounds.center;

        float dx = target.x - transform.position.x;
        float dy = target.y - transform.position.y;
        float dist = Vector2.Distance(transform.position, target);
        // Wider deadzone so we don't flip when the enemy sits nearly overhead.
        float dir = Mathf.Abs(dx) > 0.55f ? Mathf.Sign(dx) : facingSign;

        bool wantClose = dist > PreferShootDistance;
        if (wantClose)
            NavigateToward(dir, allowEdgeWalk: true, huntTargetY: target.y);
        else
        {
            if (IsLethalInDirection(dir))
                NavigateToward(-dir, allowEdgeWalk: true, huntTargetY: target.y, forceDirection: true);
            else if (Mathf.Abs(dx) < 1.15f)
            {
                CommitMove(0f, force: true);
                MoveHorizontal(0f);
                // Same column but enemy above/below — parkour vertically.
                if (dy > 0.9f)
                    TryJumpToHigherLedge(dir);
                else if (dy < -1.2f && isGrounded && !WouldWalkOffSafePlatform(dir))
                    NavigateToward(dir, allowEdgeWalk: true, huntTargetY: target.y);
            }
            else
                NavigateToward(dir * 0.45f, allowEdgeWalk: true, huntTargetY: target.y);

            if (NeedsJumpToReachHeight(target.y) && dist < attackRange)
                TryJumpToHigherLedge(dir);
        }

        FaceStable(dir);
    }

    /// <summary>
    /// Move in <paramref name="dir"/> with gap / wall / hazard / ledge parkour.
    /// Soft steering respects move commitment; lethal reverses may force a new direction.
    /// </summary>
    private void NavigateToward(float dir, bool allowEdgeWalk, float huntTargetY = float.NaN, bool forceDirection = false)
    {
        if (Mathf.Abs(dir) < 0.01f)
        {
            CommitMove(0f, forceDirection);
            MoveHorizontal(0f);
            return;
        }

        float sign = GetCommittedMoveOr(dir, forceDirection);
        if (Mathf.Abs(sign) < 0.01f)
        {
            MoveHorizontal(0f);
            return;
        }

        bool hunting = !float.IsNaN(huntTargetY);

        // Mid-leap: keep air steer toward the commit direction.
        if (Time.time < leapCommitUntil || Time.time < airSteerUntil)
        {
            MoveHorizontal(sign, urgentTurn: true);
            if (hunting && huntTargetY > transform.position.y + 0.6f && rb != null && rb.linearVelocity.y > 0.5f)
                TryNudgeTowardLedge(sign, huntTargetY);
            return;
        }

        if (Time.time < hazardWaitUntil)
        {
            MoveHorizontal(0f);
            return;
        }

        // Climb toward a higher hunt target when a ledge is nearby.
        if (hunting && huntTargetY > transform.position.y + 0.85f)
        {
            if (TryJumpToHigherLedge(sign))
                return;
        }

        if (IsLethalInDirection(sign) || NeedsJumpOverHazard(sign))
        {
            if (CanClearHazardWithJump(sign))
            {
                if (ShouldWaitBeforeHazardLeap(sign))
                {
                    hazardWaitUntil = Time.time + 0.16f;
                    MoveHorizontal(0f);
                    return;
                }

                CommitLeap(sign, 0.45f, fullJump: true);
                return;
            }

            // Hard reverse — lethal block. Commit the escape so we don't buzz.
            evadeDir = new Vector2(-sign, 0f);
            evadeUntil = Time.time + 0.45f;
            CommitMove(-sign, force: true);
            MoveHorizontal(-sign, urgentTurn: true);
            return;
        }

        // Gap parkour: jump earlier while hunting; pick short/full jump by gap width.
        if (NeedsJumpForGap(sign, hunting ? GapLookAhead * 1.35f : GapLookAhead))
        {
            if (TryGapJump(sign, hunting))
                return;

            if (!allowEdgeWalk)
            {
                MoveHorizontal(0f);
                return;
            }

            // Hunting: drop to a lower route if one exists ahead-down.
            if (hunting && HasLowerLanding(sign))
            {
                MoveHorizontal(sign);
                return;
            }

            MoveHorizontal(0f);
            return;
        }

        if (NeedsJumpOverWall(sign) || NeedsJumpOntoSpecialPlatform(sign))
        {
            CommitLeap(sign, 0.38f, fullJump: true);
            return;
        }

        // Stuck against a ledge / short wall while trying to move — hop and keep going.
        if (CanStartJump() && IsBlockedHorizontally(sign) && Time.time >= lastParkourJumpAt + 0.18f)
        {
            CommitLeap(sign, 0.36f, fullJump: true);
            lastParkourJumpAt = Time.time;
            return;
        }

        if (!allowEdgeWalk && WouldWalkOffSafePlatform(sign))
        {
            // Guard mode: if a crossable gap is right there, take it; else stop.
            if (HasLandingBeyondGap(sign))
                CommitLeap(sign, 0.4f, fullJump: EstimateGapNeedsFullJump(sign));
            else
                MoveHorizontal(0f);
            return;
        }

        MoveHorizontal(sign);
    }

    private bool IsBlockedHorizontally(float sign)
    {
        Vector2 origin = bodyCollider != null ? (Vector2)bodyCollider.bounds.center : (Vector2)transform.position;
        RaycastHit2D[] hits = Physics2D.RaycastAll(origin, new Vector2(Mathf.Sign(sign), 0f), 0.45f, groundLayers);
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].collider != null && IsWorldGroundCollider(hits[i].collider))
                return true;
        }

        return false;
    }

    private bool TryGapJump(float sign, bool hunting)
    {
        if (HasLandingBeyondGap(sign))
        {
            CommitLeap(sign, 0.42f, fullJump: EstimateGapNeedsFullJump(sign));
            return true;
        }

        // Longer hunt leap — check a bit farther for a platform.
        if (hunting && HasLandingAtDistance(sign, Mathf.Clamp(moveSpeed * 0.55f, 2.2f, 4.0f)))
        {
            CommitLeap(sign, 0.5f, fullJump: true);
            return true;
        }

        return false;
    }

    private void CommitLeap(float sign, float commitSeconds, bool fullJump)
    {
        CommitMove(sign, force: true);
        // Face the leap direction immediately so the jump doesn't start moonwalking.
        FaceStable(sign, force: true);
        pendingTurnDir = sign;
        turnAroundUntil = 0f;

        if (fullJump)
            TryJump();
        else
            TryShortJump();

        leapCommitUntil = Time.time + commitSeconds;
        airSteerUntil = Time.time + commitSeconds + 0.12f;
        MoveHorizontal(sign, urgentTurn: true);
    }

    private bool TryJumpToHigherLedge(float preferredDir)
    {
        if (!CanStartJump())
            return false;
        if (Time.time < lastParkourJumpAt + 0.2f)
            return false;

        float sign = Mathf.Abs(preferredDir) > 0.01f ? Mathf.Sign(preferredDir) : facingSign;
        // Probe for a ledge slightly ahead and above.
        float[] offsets = { 0.6f, 1.1f, 1.7f, 2.3f };
        for (int i = 0; i < offsets.Length; i++)
        {
            float x = transform.position.x + sign * offsets[i];
            if (!TryGetWorldGroundY(x, transform.position.y + 2.8f, out float ledgeY))
                continue;
            if (ledgeY < transform.position.y + 0.55f)
                continue;
            if (ledgeY > transform.position.y + 3.4f)
                continue;
            if (IsLethalNear(new Vector3(x, ledgeY + 0.35f, 0f)))
                continue;

            CommitLeap(sign, 0.48f, fullJump: true);
            lastParkourJumpAt = Time.time;
            return true;
        }

        // Straight jump if something is directly above-ish.
        if (TryGetWorldGroundY(transform.position.x + sign * 0.35f, transform.position.y + 2.6f, out float upY) &&
            upY > transform.position.y + 0.7f)
        {
            CommitLeap(sign, 0.4f, fullJump: true);
            lastParkourJumpAt = Time.time;
            return true;
        }

        return false;
    }

    private void TryNudgeTowardLedge(float sign, float targetY)
    {
        if (rb == null)
            return;

        // Soft air correction so jumps land on the intended platform.
        float vx = Mathf.Clamp(sign * moveSpeed, -moveSpeed, moveSpeed);
        rb.linearVelocity = new Vector2(vx, rb.linearVelocity.y);
        if (targetY > transform.position.y + 1.5f && rb.linearVelocity.y < 1.5f && isGrounded)
            TryJump();
    }

    private bool HasLowerLanding(float sign)
    {
        float x = transform.position.x + sign * 1.4f;
        if (!TryGetWorldGroundY(x, transform.position.y + 0.2f, out float y))
            return false;
        return y < transform.position.y - 0.6f && !IsLethalNear(new Vector3(x, y + 0.35f, 0f));
    }

    private bool HasLandingAtDistance(float sign, float dist)
    {
        float x = FeetOrigin().x + Mathf.Sign(sign) * dist;
        if (!TryGetWorldGroundY(x, transform.position.y + 1.4f, out float y))
            return false;
        if (IsLethalNear(new Vector3(x, y + 0.35f, 0f)))
            return false;
        return Mathf.Abs(y - transform.position.y) < 2.6f;
    }

    private bool EstimateGapNeedsFullJump(float sign)
    {
        // Close landing → short hop; anything farther needs a full jump.
        return !HasLandingAtDistance(sign, 1.55f);
    }

    private bool TryBeginEvade()
    {
        if (IsLethalNear(transform.position))
        {
            float away = -facingSign;
            if (!IsLethalInDirection(away))
                evadeDir = new Vector2(away, 0f);
            else if (!IsLethalInDirection(-away))
                evadeDir = new Vector2(-away, 0f);
            else
                evadeDir = Vector2.up;

            evadeUntil = Time.time + 0.45f;
            mode = AiMode.Evade;
            if (evadeDir.y > 0.5f || CanClearHazardWithJump(evadeDir.x))
                TryJump();
            NavigateToward(evadeDir.x, allowEdgeWalk: true, forceDirection: true);
            return true;
        }

        return false;
    }

    private void TickStuckAndRange(float dt)
    {
        bool fighting = huntTarget != null || FindClosestEnemy() != null;
        float distOwner = Vector2.Distance(transform.position, owner.transform.position);

        if (fighting)
        {
            stuckTimer = 0f;
            lastProgressPos = transform.position;
            return;
        }

        // Mid-parkour leaps count as progress while reuniting.
        if (Time.time < leapCommitUntil || Time.time < airSteerUntil)
        {
            stuckTimer = 0f;
            lastProgressPos = transform.position;
            return;
        }

        // Too far and not in a fight → despawn (blocked / left behind).
        if (distOwner > MaxOwnerDistance)
        {
            BeginFadeOut();
            return;
        }

        if ((transform.position - lastProgressPos).sqrMagnitude > 0.55f * 0.55f)
        {
            lastProgressPos = transform.position;
            stuckTimer = 0f;
            return;
        }

        // Only count stuck time when we're trying to close distance / move.
        bool tryingToMove = Mathf.Abs(desiredMoveDir) > 0.01f || distOwner > 6f || mode == AiMode.Reunite;
        if (!tryingToMove)
        {
            stuckTimer = 0f;
            return;
        }

        stuckTimer += dt;
        float stuckLimit = mode == AiMode.Reunite ? StuckSeconds * 1.6f : StuckSeconds;
        if (stuckTimer >= stuckLimit && distOwner > 7f)
            BeginFadeOut();
    }

    /// <summary>Recall teleport: snap beside Deadline on solid ground.</summary>
    public void TeleportBesideOwner(float sideSign, float offset = 1.25f)
    {
        if (owner == null || IsDead)
            return;

        float sign = Mathf.Abs(sideSign) > 0.01f ? Mathf.Sign(sideSign) : owner.FacingSign;
        Vector3 probe = owner.transform.position + new Vector3(sign * Mathf.Max(0.6f, offset), 1.2f, 0f);
        RaycastHit2D hit = Physics2D.Raycast(probe, Vector2.down, 4f, groundLayers);
        Vector3 pos = hit.collider
            ? new Vector3(probe.x, hit.point.y + (bodyCollider != null ? bodyCollider.size.y * 0.5f : 0.7f), owner.transform.position.z)
            : owner.transform.position + new Vector3(sign * offset, 0.15f, 0f);

        transform.position = pos;
        if (rb != null)
            rb.linearVelocity = Vector2.zero;
        lastProgressPos = pos;
        stuckTimer = 0f;
        evadeUntil = 0f;
        leapCommitUntil = 0f;
        FaceStable(sign, force: true);
        RefreshPhaseCollisions();
    }

    private void TickCombat(float dt)
    {
        Transform enemy = huntTarget != null ? huntTarget : FindClosestEnemy();
        if (enemy == null)
        {
            // No active fight: charge in the background for a ready long hand.
            if (isCharging)
            {
                chargeTimer += dt;
                if (chargeTimer >= chargeSeconds)
                    chargeTimer = chargeSeconds;
            }
            else if (Time.time >= nextShotAt)
            {
                isCharging = true;
                chargeTimer = 0f;
            }

            return;
        }

        Vector2 aim = AimAt(enemy);
        float dist = Vector2.Distance(transform.position, enemy.position);
        // Combat facing may break the idle flip lock so shots track the target.
        FaceStable(aim.x, force: true);

        bool inEffectiveRange = dist <= EffectiveAttackRange;

        // In a live firefight / effective range: short hands only — don't waste time charging.
        if (inEffectiveRange || mode == AiMode.Hunt)
        {
            if (isCharging && chargeTimer < chargeSeconds * 0.95f)
            {
                isCharging = false;
                chargeTimer = 0f;
            }

            if (Time.time >= nextShotAt)
                FireShot(false, aim);
            return;
        }

        // Target exists but out of effective range: keep charging while closing in.
        if (!isCharging)
        {
            isCharging = true;
            chargeTimer = 0f;
        }

        chargeTimer += dt;
        if (chargeTimer >= chargeSeconds && Time.time >= nextShotAt)
        {
            FireShot(true, aim);
            isCharging = false;
            chargeTimer = 0f;
        }
    }

    private Vector2 AimAt(Transform enemy)
    {
        Vector3 muzzle = MuzzlePosition();
        Vector3 center = enemy.position;
        Collider2D col = enemy.GetComponentInChildren<Collider2D>();
        if (col != null)
            center = col.bounds.center;

        Vector2 aim = (Vector2)(center - muzzle);
        if (aim.sqrMagnitude < 0.0001f)
            aim = new Vector2(facingSign, 0f);
        return aim.normalized;
    }

    private void FireShot(bool charged, Vector2 dir)
    {
        Projectile prefab = charged ? longHandPrefab : shortHandPrefab;
        if (prefab == null)
            prefab = shortHandPrefab;
        if (prefab == null || owner == null)
            return;

        float damage = Mathf.Max(1f, prefab.Damage * damageScale);
        Projectile shot = Instantiate(prefab, MuzzlePosition(), Quaternion.identity);
        shot.Launch(dir, prefab.Speed, damage, owner.transform);
        SoundManager.Instance?.PlayCountFire(charged ? ProjectileShotType.Big : ProjectileShotType.Small);
        nextShotAt = Time.time + shootInterval * (charged ? 1.1f : 0.85f);

        isShootingAnim = true;
        shootAnimTimer = charged ? 0.34f : 0.26f;
        PlayShootAnimationTriggers();
    }

    private void PlayShootAnimationTriggers()
    {
        if (animator == null || !animator.isActiveAndEnabled)
            return;

        animator.SetBool("IsShooting", true);
        animator.ResetTrigger("Shoot");
        animator.ResetTrigger("ShootUp");
        animator.ResetTrigger("JumpShot");
        animator.ResetTrigger("FallShot");
        animator.ResetTrigger("RunShoot");

        float vy = rb != null ? rb.linearVelocity.y : 0f;
        float vx = rb != null ? Mathf.Abs(rb.linearVelocity.x) : 0f;
        bool airborne = !isGrounded && coyoteTimer <= 0f;

        string trigger;
        if (airborne && vy >= -0.01f)
            trigger = "JumpShot";
        else if (airborne)
            trigger = "FallShot";
        else if (vx > 0.45f || Mathf.Abs(desiredMoveDir) > 0.01f)
            trigger = "RunShoot";
        else
            trigger = "Shoot";

        animator.SetTrigger(trigger);

        // Force the matching state when present — avoids missed trigger frames on rapid fire.
        string stateName = trigger == "RunShoot" ? "Run Shoot"
            : trigger == "JumpShot" || trigger == "FallShot" ? "Jump Shoot"
            : "Ground shoot";
        int hash = Animator.StringToHash(stateName);
        if (animator.HasState(0, hash))
            animator.Play(hash, 0, 0f);
    }

    private Vector3 MuzzlePosition()
    {
        Vector3 center = bodyCollider != null ? (Vector3)bodyCollider.bounds.center : transform.position;
        return center + new Vector3(facingSign * 0.7f, -0.1f, 0f);
    }

    private void MoveHorizontal(float dir, bool urgentTurn = false)
    {
        if (rb == null)
            return;

        float sign = Mathf.Abs(dir) < 0.01f ? 0f : Mathf.Sign(dir);
        if (Mathf.Abs(sign) < 0.01f)
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            return;
        }

        // Never moonwalk: stop, face the new way, wait, then walk.
        if (!IsFacingReadyToMove(sign, urgentTurn))
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            return;
        }

        rb.linearVelocity = new Vector2(sign * moveSpeed, rb.linearVelocity.y);
    }

    /// <summary>
    /// Returns true only when the clone already faces <paramref name="moveSign"/>.
    /// Otherwise begins a turn-around (face first, stand still) and returns false until it finishes.
    /// </summary>
    private bool IsFacingReadyToMove(float moveSign, bool urgentTurn = false)
    {
        if (Mathf.Abs(moveSign) < 0.01f)
            return true;

        // Already facing the travel direction and not mid-turn.
        if (Mathf.Approximately(moveSign, facingSign) && Time.time >= turnAroundUntil)
        {
            pendingTurnDir = 0f;
            return true;
        }

        float wait = urgentTurn ? TurnAroundUrgentSeconds : TurnAroundSeconds;

        // Start / refresh turn toward moveSign.
        if (!Mathf.Approximately(pendingTurnDir, moveSign) || turnAroundUntil <= 0f || Time.time >= turnAroundUntil + 0.5f)
        {
            pendingTurnDir = moveSign;
            turnAroundUntil = Time.time + wait;
        }

        FaceStable(moveSign, force: true);
        return Time.time >= turnAroundUntil && Mathf.Approximately(facingSign, moveSign);
    }

    private void TryJump()
    {
        ApplyJump(jumpForce);
    }

    private void TryShortJump()
    {
        ApplyJump(jumpForce * 0.72f);
    }

    private bool CanStartJump() =>
        isGrounded || coyoteTimer > 0f || airJumpsRemaining > 0;

    private void RefreshAirJumpBudget()
    {
        if (owner != null)
            maxAirJumps = Mathf.Max(0, owner.GetFutureCloneAirJumpBudget());
        airJumpsRemaining = maxAirJumps;
    }

    private void ApplyJump(float force)
    {
        if (Time.time < jumpCooldownUntil || rb == null)
            return;

        bool fromGroundOrCoyote = isGrounded || coyoteTimer > 0f;
        if (!fromGroundOrCoyote)
        {
            if (airJumpsRemaining <= 0)
                return;
            airJumpsRemaining--;
        }

        rb.linearVelocity = new Vector2(rb.linearVelocity.x, Mathf.Max(0.1f, force));
        isGrounded = false;
        coyoteTimer = 0f;
        voluntarilyAirborne = true;
        jumpCooldownUntil = Time.time + 0.22f;
        PlayJumpAnimation();
    }

    private void PlayJumpAnimation()
    {
        if (animator == null || isShootingAnim)
            return;

        animator.SetBool("IsDashing", false);
        animator.SetBool("IsGrounded", false);
        animator.SetBool("IsInAir", true);
        animator.SetBool("IsJumping", true);
        animator.SetBool("IsFalling", false);
        animator.SetBool("IsMoving", false);

        int jumpState = Animator.StringToHash("Jump");
        if (animator.HasState(0, jumpState))
            animator.Play(jumpState, 0, 0f);
    }

    private void UpdateGrounded()
    {
        Vector2 origin = bodyCollider != null
            ? new Vector2(bodyCollider.bounds.center.x, bodyCollider.bounds.min.y + 0.02f)
            : (Vector2)transform.position;

        bool grounded = false;
        Collider2D[] hits = Physics2D.OverlapCircleAll(origin, groundCheckRadius, groundLayers);
        for (int i = 0; i < hits.Length; i++)
        {
            if (IsWorldGroundCollider(hits[i]))
            {
                grounded = true;
                break;
            }
        }

        float vy = rb != null ? rb.linearVelocity.y : 0f;

        // Still overlapping the floor on takeoff — stay airborne until we leave or start falling.
        if (grounded && voluntarilyAirborne && vy > 0.05f)
        {
            isGrounded = false;
            coyoteTimer = 0f;
            return;
        }

        if (grounded)
        {
            bool wasAirborne = !isGrounded || voluntarilyAirborne;
            isGrounded = true;
            coyoteTimer = CoyoteTime;
            voluntarilyAirborne = false;
            if (wasAirborne)
                RefreshAirJumpBudget();
        }
        else
        {
            if (isGrounded)
                coyoteTimer = CoyoteTime;
            isGrounded = false;
            coyoteTimer = Mathf.Max(0f, coyoteTimer - Time.fixedDeltaTime);
        }
    }

    private void ApplyFallMultiplier()
    {
        if (rb == null || isGrounded || rb.linearVelocity.y >= 0f)
            return;

        rb.linearVelocity += Vector2.up * Physics2D.gravity.y * (fallMultiplier - 1f) * Time.fixedDeltaTime;
    }

    private bool NeedsJumpForGap(float dir, float lookAhead = -1f)
    {
        if (Mathf.Abs(dir) < 0.01f || !CanStartJump())
            return false;

        float maxAhead = lookAhead > 0f ? lookAhead : GapLookAhead;
        float sign = Mathf.Sign(dir);
        // Probe a few distances so we commit earlier on wide gaps / parkour runs.
        float[] probes = { maxAhead * 0.55f, maxAhead, maxAhead * 1.25f };
        for (int i = 0; i < probes.Length; i++)
        {
            Vector2 origin = FeetOrigin() + new Vector2(sign * probes[i], 0.05f);
            if (!TryGetWorldGroundY(origin.x, origin.y + 0.8f, out _))
                return true;
        }

        return false;
    }

    private bool NeedsJumpOverWall(float dir)
    {
        if (Mathf.Abs(dir) < 0.01f)
            return false;

        Vector2 origin = bodyCollider != null ? (Vector2)bodyCollider.bounds.center : (Vector2)transform.position;
        RaycastHit2D[] hits = Physics2D.RaycastAll(origin, new Vector2(Mathf.Sign(dir), 0f), WallProbe, groundLayers);
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].collider != null && IsWorldGroundCollider(hits[i].collider))
                return true;
        }

        return false;
    }

    private bool NeedsJumpOverHazard(float dir)
    {
        Vector2 probe = FeetOrigin() + new Vector2(Mathf.Sign(dir) * HazardProbe, 0.4f);
        return IsLethalNear(probe);
    }

    private bool NeedsJumpToReachHeight(float targetY)
    {
        return targetY > transform.position.y + 0.85f && CanStartJump();
    }

    private bool CanClearHazardWithJump(float dir)
    {
        if (Mathf.Abs(dir) < 0.01f)
            return false;

        float sign = Mathf.Sign(dir);
        float leapDist = Mathf.Clamp(moveSpeed * 0.38f, 1.6f, 3.2f);
        return HasLandingAtDistance(sign, leapDist);
    }

    private bool ShouldWaitBeforeHazardLeap(float dir)
    {
        // Spiked platforms mid-slam: wait a beat. Shredders: usually leap immediately.
        Vector2 probe = FeetOrigin() + new Vector2(Mathf.Sign(dir) * HazardProbe, 0.35f);
        int count = Physics2D.OverlapCircleNonAlloc(probe, 0.7f, HazardHits);
        for (int i = 0; i < count; i++)
        {
            if (HazardHits[i] != null && HazardHits[i].GetComponentInParent<SpikedPlatform>() != null)
                return true;
        }

        return false;
    }

    private bool HasLandingBeyondGap(float dir)
    {
        float sign = Mathf.Sign(dir);
        float leapDist = Mathf.Clamp(moveSpeed * 0.4f, 1.5f, 3.4f);
        return HasLandingAtDistance(sign, leapDist);
    }

    private bool NeedsJumpOntoSpecialPlatform(float dir)
    {
        if (Mathf.Abs(dir) < 0.01f || !CanStartJump())
            return false;

        Vector2 origin = bodyCollider != null ? (Vector2)bodyCollider.bounds.center : (Vector2)transform.position;
        RaycastHit2D hit = Physics2D.Raycast(origin, new Vector2(Mathf.Sign(dir), 0.35f).normalized, 1.4f, groundLayers);
        if (!hit.collider)
            return false;

        return hit.collider.GetComponentInParent<MovingFactoryPlatform>() != null
               || hit.collider.GetComponentInParent<SlimFactoryPlatform>() != null
               || hit.collider.GetComponentInParent<SpikedPlatform>() != null;
    }

    private bool WouldWalkOffSafePlatform(float dir)
    {
        Vector2 origin = FeetOrigin() + new Vector2(Mathf.Sign(dir) * EdgeMargin, 0.05f);
        return !TryGetWorldGroundY(origin.x, origin.y + 0.8f, out _);
    }

    private bool IsLethalInDirection(float dir)
    {
        if (Mathf.Abs(dir) < 0.01f)
            return false;
        return IsLethalNear(transform.position + new Vector3(Mathf.Sign(dir) * HazardProbe, 0.2f, 0f));
    }

    private void RefreshPhaseCollisions()
    {
        if (bodyCollider == null)
            return;

        Collider2D[] myCols = { bodyCollider };

        // Owner + other future clones.
        if (owner != null && owner.BodyCollider != null)
            IgnoreSolidPair(bodyCollider, owner.BodyCollider);

        DeadlineFutureClone[] clones = FindObjectsByType<DeadlineFutureClone>(FindObjectsSortMode.None);
        for (int i = 0; i < clones.Length; i++)
        {
            if (clones[i] == null || clones[i] == this)
                continue;
            BoxCollider2D other = clones[i].GetComponent<BoxCollider2D>();
            if (other != null)
                IgnoreSolidPair(bodyCollider, other);
        }

        // NPCs (town) — must phase through.
        NPC[] npcs = FindObjectsByType<NPC>(FindObjectsSortMode.None);
        for (int n = 0; n < npcs.Length; n++)
        {
            if (npcs[n] == null)
                continue;
            IgnoreSolidArrays(myCols, npcs[n].GetComponentsInChildren<Collider2D>(true));
        }

        PhaseThroughPlayers[] phaseProps = FindObjectsByType<PhaseThroughPlayers>(FindObjectsSortMode.None);
        for (int p = 0; p < phaseProps.Length; p++)
        {
            if (phaseProps[p] == null)
                continue;
            IgnoreSolidArrays(myCols, phaseProps[p].GetComponentsInChildren<Collider2D>(true));
        }

        // Combat solids (triggers still hurt / detect).
        Boss[] bosses = EnemyTypeCache.Bosses;
        for (int b = 0; b < bosses.Length; b++)
        {
            if (bosses[b] == null)
                continue;
            IgnoreSolidArrays(myCols, bosses[b].GetComponentsInChildren<Collider2D>(true));
        }

        IgnoreEnemySolids(myCols, EnemyTypeCache.Crankies);
        IgnoreEnemySolids(myCols, EnemyTypeCache.Lasers);
        IgnoreEnemySolids(myCols, EnemyTypeCache.Tankers);
        IgnoreEnemySolids(myCols, EnemyTypeCache.Scraps);
        IgnoreEnemySolids(myCols, EnemyTypeCache.Blockers);
        IgnoreEnemySolids(myCols, EnemyTypeCache.CheckPoints);

        ShredderBlade[] shredders = EnemyTypeCache.Shredders;
        for (int s = 0; s < shredders.Length; s++)
        {
            if (shredders[s] == null)
                continue;
            // Phase shredder box solids so clones can leap through / land beyond; triggers still damage.
            IgnoreSolidArrays(myCols, shredders[s].GetShredderHierarchyColliders());
        }

        KitRocketShip[] ships = FindObjectsByType<KitRocketShip>(FindObjectsSortMode.None);
        for (int s = 0; s < ships.Length; s++)
        {
            if (ships[s] == null)
                continue;
            IgnoreSolidArrays(myCols, ships[s].GetComponentsInChildren<Collider2D>(true));
        }
    }

    private static void IgnoreEnemySolids<T>(Collider2D[] myCols, T[] list) where T : Component
    {
        if (list == null)
            return;
        for (int i = 0; i < list.Length; i++)
        {
            if (list[i] == null)
                continue;
            IgnoreSolidArrays(myCols, list[i].GetComponentsInChildren<Collider2D>(true));
        }
    }

    private static void IgnoreSolidArrays(Collider2D[] aCols, Collider2D[] bCols)
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

    private static void IgnoreSolidPair(Collider2D a, Collider2D b)
    {
        if (a == null || b == null || a.isTrigger || b.isTrigger)
            return;
        Physics2D.IgnoreCollision(a, b, true);
    }

    private bool IsLethalNear(Vector3 worldPos)
    {
        int count = Physics2D.OverlapCircleNonAlloc(worldPos, 0.55f, HazardHits);
        for (int i = 0; i < count; i++)
        {
            Collider2D col = HazardHits[i];
            if (col == null)
                continue;
            if (col == bodyCollider)
                continue;
            if (col.GetComponentInParent<ShredderBlade>() != null)
                return true;
            if (col.GetComponentInParent<SpikedPlatform>() != null)
                return true;
        }

        return false;
    }

    private Vector2 FeetOrigin()
    {
        if (bodyCollider != null)
            return new Vector2(bodyCollider.bounds.center.x, bodyCollider.bounds.min.y + 0.02f);
        return transform.position;
    }

    private Transform FindClosestEnemy()
    {
        // Engage if near Deadline (assist radius) OR within the clone's own hunt radius.
        ContactFilter2D filter = new ContactFilter2D();
        filter.useTriggers = true;
        filter.useLayerMask = false;

        Overlaps.Clear();
        float selfRange = Mathf.Max(2f, searchRadius);
        AppendEnemyScan(transform.position, selfRange + 1f, filter);
        if (owner != null)
            AppendEnemyScan(owner.transform.position, OwnerAssistRadius + 0.75f, filter);

        Transform best = null;
        float bestSqr = float.MaxValue;
        Vector3 ownerPos = owner != null ? owner.transform.position : transform.position;
        float ownerRangeSqr = OwnerAssistRadius * OwnerAssistRadius;
        float selfRangeSqr = selfRange * selfRange;

        for (int i = 0; i < Overlaps.Count; i++)
        {
            Collider2D col = Overlaps[i];
            if (!IsValidTarget(col))
                continue;

            Vector3 center = col.bounds.center;
            float toOwnerSqr = ((Vector2)(center - ownerPos)).sqrMagnitude;
            float toSelfSqr = ((Vector2)(center - transform.position)).sqrMagnitude;
            if (toOwnerSqr > ownerRangeSqr && toSelfSqr > selfRangeSqr)
                continue;

            if (toSelfSqr < bestSqr)
            {
                bestSqr = toSelfSqr;
                best = col.transform;
            }
        }

        return best;
    }

    private void AppendEnemyScan(Vector3 origin, float radius, ContactFilter2D filter)
    {
        List<Collider2D> extra = new List<Collider2D>(24);
        Physics2D.OverlapCircle(origin, radius, filter, extra);
        for (int i = 0; i < extra.Count; i++)
        {
            Collider2D col = extra[i];
            if (col == null)
                continue;
            bool listed = false;
            for (int j = 0; j < Overlaps.Count; j++)
            {
                if (Overlaps[j] == col)
                {
                    listed = true;
                    break;
                }
            }

            if (!listed)
                Overlaps.Add(col);
        }
    }

    private static bool IsValidTarget(Collider2D col)
    {
        if (col == null || !col.enabled || !col.gameObject.activeInHierarchy)
            return false;
        if (EnemyDetectionZone.IsDetectionOnlyCollider(col) || col.GetComponent<AttackHitbox>() != null)
            return false;

        Boss boss = col.GetComponentInParent<Boss>();
        if (boss != null)
            return !boss.IsDead;

        ICommonEnemy enemy = col.GetComponentInParent<ICommonEnemy>();
        return enemy != null && !enemy.IsDead;
    }

    private void Face(float x) => FaceStable(x, force: false);

    private void FaceStable(float x, bool force = false)
    {
        if (Mathf.Abs(x) < 0.01f)
            return;

        float sign = x > 0f ? 1f : -1f;
        if (!force &&
            !Mathf.Approximately(sign, facingSign) &&
            Time.time < faceLockUntil)
            return;

        facingSign = sign;
        faceLockUntil = Time.time + FaceFlipMinSeconds;
        if (spriteRenderer == null)
            return;

        bool right = facingSign > 0f;
        spriteRenderer.flipX = artFacesLeft ? right : !right;
    }

    private void TickVisuals()
    {
        if (spriteRenderer == null)
            return;

        // Tint only — sprite frames come from the clone's own Animator.
        shimmerPhase += Time.deltaTime * 2.4f;
        float shimmer = 0.5f + 0.5f * Mathf.Sin(shimmerPhase * Mathf.PI * 2f);
        Color c = Time.time < hitFlashUntil
            ? Color.white
            : Color.Lerp(baseColor, new Color(1f, 1f, 0.65f, baseColor.a), shimmer * 0.22f);
        c.a = baseColor.a * Mathf.Lerp(0.82f, 1f, shimmer);
        spriteRenderer.color = c;
    }

    public void TakeDamage(int amount)
    {
        if (amount <= 0 || IsDead)
            return;

        health = Mathf.Max(0, health - amount);
        hitFlashUntil = Time.time + 0.12f;

        // Pain evade: hop away from the last known hunt target / facing.
        evadeDir = new Vector2(-facingSign, 0f);
        evadeUntil = Time.time + 0.4f;
        TryJump();

        if (health <= 0)
            BeginFadeOut();
    }

    public void Vanish()
    {
        health = 0;
        BeginFadeOut();
    }

    private void BeginFadeOut()
    {
        if (fadeOutStartedAt >= 0f)
            return;

        fadeOutStartedAt = Time.time;
        isCharging = false;
        if (bodyCollider != null)
            bodyCollider.enabled = false;
        if (rb != null)
            rb.linearVelocity = Vector2.zero;
    }

    private void TickFadeOut()
    {
        float t = Mathf.Clamp01((Time.time - fadeOutStartedAt) / FadeOutSeconds);
        if (spriteRenderer != null)
        {
            Color c = spriteRenderer.color;
            c.a = baseColor.a * (1f - t);
            spriteRenderer.color = c;
        }

        transform.localScale = Vector3.one * (1f + 0.2f * t);
        if (t >= 1f)
            Destroy(gameObject);
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        if (IsDead || collision == null)
            return;

        // Treat solid hazard contacts as damage + evade (mirrors player hazard pressure).
        if (collision.collider.GetComponentInParent<ShredderBlade>() != null ||
            collision.collider.GetComponentInParent<SpikedPlatform>() != null)
        {
            TakeDamage(2);
        }
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        if (IsDead || other == null)
            return;

        if (other.GetComponentInParent<ShredderBlade>() != null ||
            other.GetComponentInParent<SpikedPlatform>() != null)
        {
            TakeDamage(2);
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, searchRadius);
    }
#endif
}
