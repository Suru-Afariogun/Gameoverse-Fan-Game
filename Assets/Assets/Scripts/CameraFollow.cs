using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Player-anchored follow with dead-zone damping (calm when centered) and edge rescue (fast near screen edges).
/// Mega Man–style horizontal look-ahead while moving; stronger vertical catch-up while airborne.
/// </summary>
[RequireComponent(typeof(Camera))]
[DefaultExecutionOrder(-50)]
public class CameraFollow : MonoBehaviour
{
    public static CameraFollow Instance { get; private set; }

    [Header("Target")]
    [SerializeField] private Transform target;
    [Tooltip("If no target is assigned, follows PlayerController.Active.")]
    [SerializeField] private bool autoFindPlayer = true;
    [SerializeField] private Vector3 offset = new Vector3(0f, 0f, -10f);
    [SerializeField] private bool followX = true;
    [SerializeField] private bool followY = true;
    [Tooltip("If true, never snaps instantly — always eases toward the player.")]
    [SerializeField] private bool softAcquireOnly = true;

    [Header("Dead Zone (Celeste / Hollow Knight)")]
    [Tooltip("Player can move inside this viewport band without the camera chasing.")]
    [SerializeField] private float deadZoneLeft = 0.38f;
    [SerializeField] private float deadZoneRight = 0.62f;
    [SerializeField] private float deadZoneBottom = 0.36f;
    [SerializeField] private float deadZoneTop = 0.64f;

    [Header("Edge Rescue (Dead Cells)")]
    [Tooltip("Past these viewport lines follow speeds up to keep the player readable.")]
    [SerializeField] private float edgeZoneLeft = 0.18f;
    [SerializeField] private float edgeZoneRight = 0.82f;
    [SerializeField] private float edgeZoneBottom = 0.24f;
    [SerializeField] private float edgeZoneTop = 0.76f;
    [SerializeField] private float edgeCatchUpSmoothTime = 0.065f;
    [SerializeField] private float edgeCatchUpMaxSpeed = 48f;

    [Header("Follow Damping")]
    [Tooltip("Horizontal lag — lower feels more Mega Man / responsive.")]
    [SerializeField] private float smoothTimeX = 0.085f;
    [Tooltip("Vertical lag on the ground — higher keeps vertical motion softer.")]
    [SerializeField] private float smoothTimeY = 0.15f;
    [Tooltip("Vertical lag while airborne — keep low so jumps/falls stay on screen.")]
    [SerializeField] private float airborneSmoothTimeY = 0.07f;
    [SerializeField] private float maxFollowSpeedX = 30f;
    [SerializeField] private float maxFollowSpeedY = 20f;
    [SerializeField] private float airborneMaxSpeedY = 52f;
    [Tooltip("Slower follow while the player is inside the dead zone (ground only).")]
    [SerializeField] private float deadZoneFollowMultiplier = 1.35f;
    [Tooltip("Extra-soft follow when the camera first finds or switches targets.")]
    [SerializeField] private float acquireSmoothTime = 1.1f;
    [SerializeField] private float acquireBlendDuration = 1.35f;

    [Header("Speed Look-Ahead (Mega Man / Dead Cells)")]
    [SerializeField] private float lookAheadDistance = 0.85f;
    [SerializeField] private float lookAheadSmoothTime = 0.28f;
    [SerializeField] private float speedBoostThreshold = 0.35f;
    [Tooltip("Look-ahead strength while walking (0–1). Dash speed uses full look-ahead.")]
    [SerializeField] private float walkLookAheadStrength = 0.45f;

    [Header("Airborne Center Leash (jumps & falls)")]
    [Tooltip("Player may not drift further than this (world units) from camera center while airborne.")]
    [SerializeField] private float airborneMaxCenterOffset = 1f;
    [SerializeField] private float airborneLeashMinSmoothTime = 0.038f;
    [Tooltip("Extra follow speed margin so the camera can keep the leash taut.")]
    [SerializeField] private float airborneLeashSpeedMargin = 5f;
    [Tooltip("Viewport distance from center that counts as off-center for edge-deferred leash.")]
    [SerializeField] private float viewportCenterTolerance = 0.06f;
    [Tooltip("World-unit difference between unclamped and clamped follow before bounds are treated as active.")]
    [SerializeField] private float boundsClampPositionTolerance = 0.05f;

    [Header("Fall Look-Ahead")]
    [Tooltip("Downward look-ahead (world units) while falling.")]
    [SerializeField] private float fallLookAheadDistance = 0.5f;
    [Tooltip("How quickly fall look-ahead eases in/out (lower = faster).")]
    [SerializeField] private float fallLookAheadEaseTime = 0.085f;
    [SerializeField] private float fallLookAheadMinVelocity = 0.15f;
    [Tooltip("No fall look-ahead when this close to the ground/platform below (world units).")]
    [SerializeField] private float fallLookAheadMinHeightAboveGround = 4f;
    [SerializeField] private float groundProbeMaxDistance = 80f;
    [SerializeField] private float lowestPlatformTierTolerance = 0.75f;

    [Header("Landing")]
    [SerializeField] private float landingReturnDuration = 0.4f;
    [SerializeField] private float landingReturnSmoothTime = 0.3f;
    [Tooltip("Begin easing back toward the background-adjusted follow position when this close to landing (world units).")]
    [SerializeField] private float landingApproachDistance = 1f;
    [Tooltip("Vertical follow speed while easing into the pre-jump bounds adjustment on the way down.")]
    [SerializeField] private float landingApproachSmoothTimeY = 0.055f;
    [SerializeField] private float landingApproachMaxSpeedY = 46f;
    [Tooltip("Stop vertical follow once the camera reaches its bounds-adjusted Y so the player can finish landing slightly off-center.")]
    [SerializeField] private float landingApproachArrivedTolerance = 0.07f;
    [Tooltip("Clears fall look-ahead faster during the landing approach so the existing bounds offset can return.")]
    [SerializeField] private float landingApproachFallLookAheadDecayTime = 0.05f;

    [Header("Manual Vertical Look-Ahead (Up/Down Hold)")]
    [SerializeField] private float manualLookAheadHoldDelay = 1f;
    [SerializeField] private float manualLookAheadMax = 5f;
    [SerializeField] private float manualLookAheadSmoothTime = 0.12f;
    [SerializeField] private float verticalInputThreshold = 0.35f;

    [Header("Bounds (optional)")]
    [Tooltip("Usually your Background SpriteRenderer. Camera stays inside these bounds.")]
    [SerializeField] private SpriteRenderer background;
    [SerializeField] private bool autoFindBackground = true;
    [SerializeField] private string backgroundObjectName = "Background";
    [SerializeField] private float boundsPadding = 0f;
    [Tooltip("Extra inset on top/bottom so the view never peeks past backdrop art.")]
    [SerializeField] private float verticalBoundsInset = 0.9f;

    private Camera cam;
    private Transform backgroundBoundsRoot;
    private SpriteRenderer[] stageBackgroundRenderers;
    private Vector3 velocity;
    private Bounds bounds;
    private bool hasBounds;
    private Transform trackedTarget;
    private float acquireTimer;
    private bool followEnabled = true;

    private float lookAheadX;
    private float lookAheadVelocity;
    private float lookAheadY;
    private float lookAheadYVelocity;
    private float manualLookAheadY;
    private float manualLookAheadYVelocity;
    private float upHoldTime;
    private float downHoldTime;
    private bool catchUpBoost;
    private float baseAspect;
    private float landingReturnTimer;
    private bool playerWasAirborne;
    private float levelLowestPlatformTopY;
    private bool hasLevelLowestPlatformTop;
    private bool airborneFromLowestPlatformRow;
    private bool groundedOffCenterFromBoundsClamp;
    private bool deferAirborneCenterLeash;
    private bool landingApproachVerticalLocked;
    private bool takeoffHadBoundsVerticalAdjust;
    private float takeoffBoundsAdjustedCameraY;
    private bool cachedBoundsVerticalAdjust;
    private float cachedBoundsAdjustedCameraY;

    private void Awake()
    {
        Instance = this;
        cam = GetComponent<Camera>();
        if (cam != null)
        {
            baseAspect = cam.aspect;
        }
        ResolveBackground();
    }

    private void Start()
    {
        ResolveBackground();

        if (cam != null && baseAspect <= 0.01f)
            baseAspect = cam.aspect;

        ResolveTarget();

        if (!UsesLevelFollowMode())
        {
            lookAheadY = 0f;
            lookAheadYVelocity = 0f;
        }

        StartCoroutine(RefreshBoundsAfterSceneSettles());
    }

    private System.Collections.IEnumerator RefreshBoundsAfterSceneSettles()
    {
        yield return null;
        yield return null;
        ResolveBackground();
        // Parallax Background components apply their first offset in LateUpdate — refresh once more.
        yield return null;
        ResolveBackground();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void LateUpdate()
    {
        if (!followEnabled)
            return;

        if (target == null && autoFindPlayer)
            ResolveTarget();
        else if (followEnabled && autoFindPlayer)
            TryRebindMissingTarget();

        if (target == null)
            return;

        PlayerController followedPlayer = ResolveFollowedPlayer();
        float followDt = Time.deltaTime;

        TickManualVerticalLookAhead(followedPlayer, followDt);
        TickLandingReturn(followedPlayer, followDt);

        if (trackedTarget != target)
            BeginSoftAcquire(target);

        if (!hasBounds && autoFindBackground)
            ResolveBackground();

        TickFollow(followedPlayer, followDt);

        transform.position = HardClampPosition(transform.position);
    }

    private void TickManualVerticalLookAhead(PlayerController player, float dt)
    {
        if (cam != null && baseAspect <= 0.01f)
            baseAspect = cam.aspect;

        if (cam != null)
            cam.aspect = baseAspect;

        bool holdingDown = IsPlayerHoldingDown(player);
        bool holdingUp = IsPlayerHoldingUp(player);

        downHoldTime = holdingDown && !holdingUp ? downHoldTime + dt : 0f;
        upHoldTime = holdingUp && !holdingDown ? upHoldTime + dt : 0f;

        float targetManual = 0f;
        if (holdingDown && !holdingUp && downHoldTime >= manualLookAheadHoldDelay)
            targetManual = -GetMaxManualLookAhead(player, downward: true);
        else if (holdingUp && !holdingDown && upHoldTime >= manualLookAheadHoldDelay)
            targetManual = GetMaxManualLookAhead(player, downward: false);

        manualLookAheadY = Mathf.SmoothDamp(
            manualLookAheadY,
            targetManual,
            ref manualLookAheadYVelocity,
            manualLookAheadSmoothTime,
            Mathf.Infinity,
            dt);
    }

    private float GetMaxManualLookAhead(PlayerController player, bool downward)
    {
        if (player == null || target == null || cam == null || !cam.orthographic)
            return manualLookAheadMax;

        float baseY = target.position.y + offset.y;
        float halfH = cam.orthographicSize;
        float inset = boundsPadding + (UsesLevelFollowMode() ? verticalBoundsInset : 0f);

        if (!hasBounds)
            return manualLookAheadMax;

        float minCenterY = bounds.min.y + halfH + inset;
        float maxCenterY = bounds.max.y - halfH - inset;

        if (downward)
        {
            float room = baseY - minCenterY;
            return Mathf.Clamp(room, 0f, manualLookAheadMax);
        }

        float roomUp = maxCenterY - baseY;
        return Mathf.Clamp(roomUp, 0f, manualLookAheadMax);
    }

    private bool IsPlayerHoldingDown(PlayerController player)
    {
        if (player == null || player.IsDead)
            return false;

        return player.MoveInput.y <= -verticalInputThreshold;
    }

    private bool IsPlayerHoldingUp(PlayerController player)
    {
        if (player == null || player.IsDead)
            return false;

        return player.MoveInput.y >= verticalInputThreshold;
    }

    private bool IsLandingReturnEase() => landingReturnTimer > 0f;

    private void TickLandingReturn(PlayerController player, float dt)
    {
        if (landingReturnTimer > 0f)
            landingReturnTimer = Mathf.Max(0f, landingReturnTimer - dt);

        if (player == null)
        {
            playerWasAirborne = false;
            airborneFromLowestPlatformRow = false;
            groundedOffCenterFromBoundsClamp = false;
            deferAirborneCenterLeash = false;
            landingApproachVerticalLocked = false;
            takeoffHadBoundsVerticalAdjust = false;
            return;
        }

        bool airborne = !player.IsGrounded;
        if (player.IsGrounded)
        {
            groundedOffCenterFromBoundsClamp = IsOffCenterDueToBoundsClamp();
            cachedBoundsVerticalAdjust = TryGetBoundsVerticalAdjustState(out cachedBoundsAdjustedCameraY);
        }

        if (!playerWasAirborne && airborne)
        {
            airborneFromLowestPlatformRow = IsPlayerOverLowestPlatformRow(player);
            deferAirborneCenterLeash = groundedOffCenterFromBoundsClamp;
            takeoffHadBoundsVerticalAdjust = cachedBoundsVerticalAdjust;
            takeoffBoundsAdjustedCameraY = cachedBoundsAdjustedCameraY;
            landingApproachVerticalLocked = false;
        }

        if (playerWasAirborne && !airborne)
        {
            landingReturnTimer = landingReturnDuration;
            airborneFromLowestPlatformRow = false;
            deferAirborneCenterLeash = false;
            landingApproachVerticalLocked = false;
            takeoffHadBoundsVerticalAdjust = false;
        }

        if (airborne && deferAirborneCenterLeash && IsPlayerNearViewportCenter())
            deferAirborneCenterLeash = false;

        playerWasAirborne = airborne;
    }

    private void TryRebindMissingTarget()
    {
        if (target != null && target.gameObject.activeInHierarchy)
            return;

        PlayerController active = PlayerController.Active;
        if (active == null)
            return;

        target = active.transform;
        trackedTarget = active.transform;
    }

    private bool UsesLevelFollowMode()
    {
        return LevelOneBossEncounter.IsLevelOneScene(SceneManager.GetActiveScene().name);
    }

    private Vector3 ClampDesiredPosition(Vector3 desired)
    {
        desired = UsesLevelFollowMode()
            ? GetClampedPosition(desired)
            : GetArenaClampedPosition(desired);
        return ApplyLowestPointFloor(ApplySceneEdgeLimits(desired));
    }

    private Vector3 HardClampPosition(Vector3 desired)
    {
        desired = UsesLevelFollowMode()
            ? GetHardClampedPosition(desired)
            : GetArenaHardClampedPosition(desired);
        return ApplyLowestPointFloor(ApplySceneEdgeLimits(desired));
    }

    /// <summary>
    /// Failsafe floor from a scene <see cref="LowestPoint"/> marker — separate from Background bounds.
    /// </summary>
    private static Vector3 ApplyLowestPointFloor(Vector3 desired)
    {
        LowestPoint floor = LowestPoint.Active;
        if (floor == null)
            return desired;

        if (desired.y < floor.FloorY)
            desired.y = floor.FloorY;

        return desired;
    }

    /// <summary>
    /// Failsafe camera-center clamps from <see cref="SceneEdge"/> markers (top/bottom/left/right).
    /// </summary>
    private static Vector3 ApplySceneEdgeLimits(Vector3 desired)
    {
        SceneEdge.GetCameraLimits(
            out bool hasFloor, out float floorY,
            out bool hasCeiling, out float ceilingY,
            out bool hasLeft, out float leftX,
            out bool hasRight, out float rightX);

        if (hasFloor && desired.y < floorY)
            desired.y = floorY;
        if (hasCeiling && desired.y > ceilingY)
            desired.y = ceilingY;
        if (hasLeft && desired.x < leftX)
            desired.x = leftX;
        if (hasRight && desired.x > rightX)
            desired.x = rightX;

        return desired;
    }

    private void TickFollow(PlayerController player, float followDt)
    {
        bool airborne = player != null && !player.IsGrounded;

        float targetLookAheadX = airborne ? 0f : ComputeHorizontalLookAhead(player);
        lookAheadX = Mathf.SmoothDamp(
            lookAheadX,
            targetLookAheadX,
            ref lookAheadVelocity,
            lookAheadSmoothTime,
            Mathf.Infinity,
            followDt);

        Vector3 desired = target.position + offset;
        if (followX)
            desired.x += lookAheadX;
        if (followY)
            desired.y += lookAheadY + manualLookAheadY;
        desired.z = offset.z;

        if (!followX)
            desired.x = transform.position.x;
        if (!followY)
            desired.y = transform.position.y;

        desired = ClampDesiredPosition(desired);

        bool approachingLanding = IsApproachingLanding(player);
        bool landingApproachActive = airborne && approachingLanding && takeoffHadBoundsVerticalAdjust;
        float landingBoundsAdjustedY = landingApproachActive
            ? GetLandingBoundsAdjustedCameraY()
            : desired.y;

        if (landingApproachActive && !landingApproachVerticalLocked
            && HasReachedLandingBoundsAdjustedY(landingBoundsAdjustedY))
        {
            landingApproachVerticalLocked = true;
        }

        TickFallLookAhead(player, followDt, landingApproachVerticalLocked);

        if (landingApproachActive && followY)
        {
            desired.y = landingApproachVerticalLocked
                ? transform.position.y
                : landingBoundsAdjustedY;
        }

        float urgencyX = 0f;
        float urgencyY = 0f;
        if (!airborne && TryGetTargetViewport(cam, target, out Vector3 viewport))
        {
            if (followX)
            {
                urgencyX = ComputeAxisUrgency(
                    viewport.x,
                    deadZoneLeft,
                    deadZoneRight,
                    edgeZoneLeft,
                    edgeZoneRight);
            }

            if (followY)
            {
                urgencyY = ComputeAxisUrgency(
                    viewport.y,
                    deadZoneBottom,
                    deadZoneTop,
                    edgeZoneBottom,
                    edgeZoneTop);
            }
        }

        float baseSmoothX = GetAcquireSmoothTime(smoothTimeX);
        float baseSmoothY = IsLandingReturnEase()
            ? landingReturnSmoothTime
            : GetAcquireSmoothTime(airborne ? airborneSmoothTimeY : smoothTimeY);

        float groundedDamp = airborne ? 1f : deadZoneFollowMultiplier;
        float smoothX = Mathf.Lerp(baseSmoothX * groundedDamp, edgeCatchUpSmoothTime, urgencyX);
        float smoothY = Mathf.Lerp(baseSmoothY * groundedDamp, edgeCatchUpSmoothTime, urgencyY);
        float maxSpeedX = Mathf.Lerp(maxFollowSpeedX, edgeCatchUpMaxSpeed, urgencyX);
        float maxSpeedY = Mathf.Lerp(
            airborne ? airborneMaxSpeedY : maxFollowSpeedY,
            edgeCatchUpMaxSpeed,
            urgencyY);

        if (airborne && !deferAirborneCenterLeash && !landingApproachActive)
        {
            ApplyAirborneCenterLeash(player, followDt, ref desired, ref smoothX, ref smoothY, ref maxSpeedX, ref maxSpeedY);
            desired = ClampDesiredPosition(desired);
        }
        else if (player != null)
        {
            float absVx = Mathf.Abs(player.Velocity.x);
            float speedHint = player.GetCameraFollowSpeedHint();
            if (absVx > 0.5f || speedHint > 0.5f)
            {
                float runSpeed = Mathf.Max(absVx, speedHint);
                maxSpeedX = Mathf.Max(maxSpeedX, runSpeed * 1.35f + 10f);
                smoothX = Mathf.Min(smoothX, smoothTimeX);
            }
        }

        if (catchUpBoost)
        {
            smoothX = Mathf.Min(smoothX, edgeCatchUpSmoothTime);
            smoothY = Mathf.Min(smoothY, edgeCatchUpSmoothTime);
            maxSpeedX = Mathf.Max(maxSpeedX, edgeCatchUpMaxSpeed);
            maxSpeedY = Mathf.Max(maxSpeedY, edgeCatchUpMaxSpeed);
        }

        if (landingApproachActive && !landingApproachVerticalLocked && followY)
        {
            smoothY = Mathf.Min(smoothY, landingApproachSmoothTimeY);
            maxSpeedY = Mathf.Max(maxSpeedY, landingApproachMaxSpeedY);
        }

        float posX = followX
            ? Mathf.SmoothDamp(transform.position.x, desired.x, ref velocity.x, smoothX, maxSpeedX, followDt)
            : transform.position.x;
        float posY;
        if (followY && landingApproachVerticalLocked)
        {
            posY = transform.position.y;
            velocity.y = 0f;
        }
        else if (followY)
        {
            posY = Mathf.SmoothDamp(transform.position.y, desired.y, ref velocity.y, smoothY, maxSpeedY, followDt);
        }
        else
        {
            posY = transform.position.y;
        }

        transform.position = new Vector3(posX, posY, desired.z);

        if (airborne && !deferAirborneCenterLeash && !landingApproachActive)
            EnforceAirborneCenterLeash();

        if (acquireTimer > 0f)
            acquireTimer -= followDt;

        if (catchUpBoost && HasCaughtUpToTarget(0.55f))
            catchUpBoost = false;
    }

    private float ComputeHorizontalLookAhead(PlayerController player)
    {
        if (player == null)
            return 0f;

        float vx = player.Velocity.x;
        float absVx = Mathf.Max(Mathf.Abs(vx), player.GetCameraFollowSpeedHint());
        if (absVx < 0.12f && !player.IsDashing && !player.IsDashJumping)
            return 0f;

        float sign = absVx > 0.05f ? Mathf.Sign(vx) : player.FacingSign;
        if (Mathf.Abs(sign) < 0.01f)
            return 0f;

        float normal = Mathf.Max(0.01f, player.NormalMoveSpeed);
        float speedT = Mathf.Clamp01(absVx / (normal + speedBoostThreshold));
        if (player.IsDashing || player.IsDashJumping)
            speedT = 1f;

        float strength = Mathf.Lerp(walkLookAheadStrength, 1f, speedT);
        return lookAheadDistance * sign * strength;
    }

    private static float ComputeAxisUrgency(
        float viewportCoord,
        float deadMin,
        float deadMax,
        float edgeMin,
        float edgeMax)
    {
        if (viewportCoord >= deadMin && viewportCoord <= deadMax)
            return 0f;

        if (viewportCoord <= edgeMin || viewportCoord >= edgeMax)
            return 1f;

        if (viewportCoord < deadMin)
        {
            float span = Mathf.Max(0.0001f, deadMin - edgeMin);
            return SmoothEdgeUrgency((deadMin - viewportCoord) / span);
        }

        float upperSpan = Mathf.Max(0.0001f, edgeMax - deadMax);
        return SmoothEdgeUrgency((viewportCoord - deadMax) / upperSpan);
    }

    private bool IsOffCenterDueToBoundsClamp()
    {
        if (target == null || cam == null)
            return false;

        if (!TryGetTargetViewport(cam, target, out Vector3 viewport))
            return false;

        bool offCenterX = followX && Mathf.Abs(viewport.x - 0.5f) > viewportCenterTolerance;
        bool offCenterY = followY && Mathf.Abs(viewport.y - 0.5f) > viewportCenterTolerance;
        if (!offCenterX && !offCenterY)
            return false;

        Vector3 unclamped = BuildUnclampedFollowPosition();
        Vector3 clamped = ClampDesiredPosition(unclamped);

        float tolerance = Mathf.Max(0.001f, boundsClampPositionTolerance);
        bool boundsClampX = followX && Mathf.Abs(unclamped.x - clamped.x) > tolerance;
        bool boundsClampY = followY && Mathf.Abs(unclamped.y - clamped.y) > tolerance;

        return (offCenterX && boundsClampX) || (offCenterY && boundsClampY);
    }

    private bool IsPlayerNearViewportCenter()
    {
        if (target == null || cam == null)
            return false;

        if (!TryGetTargetViewport(cam, target, out Vector3 viewport))
            return false;

        bool xOk = !followX || Mathf.Abs(viewport.x - 0.5f) <= viewportCenterTolerance;
        bool yOk = !followY || Mathf.Abs(viewport.y - 0.5f) <= viewportCenterTolerance;
        return xOk && yOk;
    }

    private Vector3 BuildUnclampedFollowPosition()
    {
        Vector3 unclamped = target.position + offset;
        if (followX)
            unclamped.x += lookAheadX;
        if (followY)
            unclamped.y += lookAheadY + manualLookAheadY;
        unclamped.z = offset.z;
        return unclamped;
    }

    private bool TryGetBoundsVerticalAdjustState(out float boundsAdjustedCameraY)
    {
        boundsAdjustedCameraY = transform.position.y;
        if (!followY || target == null)
            return false;

        Vector3 probe = GetGroundedBoundsAdjustProbe();
        Vector3 clamped = ClampDesiredPosition(probe);
        bool adjusts = Mathf.Abs(probe.y - clamped.y) > boundsClampPositionTolerance;
        boundsAdjustedCameraY = adjusts ? clamped.y : transform.position.y;
        return adjusts;
    }

    private Vector3 GetGroundedBoundsAdjustProbe()
    {
        Vector3 probe = target.position + offset;
        if (followX)
            probe.x += lookAheadX;
        if (followY)
            probe.y += manualLookAheadY;
        probe.z = offset.z;
        return probe;
    }

    private float GetLandingBoundsAdjustedCameraY()
    {
        if (target == null)
            return transform.position.y;

        Vector3 probe = target.position + offset;
        if (followX)
            probe.x += lookAheadX;
        probe.z = offset.z;

        float clampedY = ClampDesiredPosition(probe).y;
        if (!takeoffHadBoundsVerticalAdjust)
            return clampedY;

        return Mathf.Max(clampedY, takeoffBoundsAdjustedCameraY);
    }

    private bool HasReachedLandingBoundsAdjustedY(float landingBoundsAdjustedY)
    {
        return Mathf.Abs(transform.position.y - landingBoundsAdjustedY) <= landingApproachArrivedTolerance
            || Mathf.Abs(transform.position.y - takeoffBoundsAdjustedCameraY) <= landingApproachArrivedTolerance;
    }

    /// <summary>
    /// Keeps the player within airborneMaxCenterOffset of camera center; speeds scale to the gap and player velocity.
    /// </summary>
    private void ApplyAirborneCenterLeash(
        PlayerController player,
        float followDt,
        ref Vector3 desired,
        ref float smoothX,
        ref float smoothY,
        ref float maxSpeedX,
        ref float maxSpeedY)
    {
        if (target == null)
            return;

        float leash = Mathf.Max(0.1f, airborneMaxCenterOffset);
        Vector2 rel = new Vector2(
            target.position.x - transform.position.x,
            target.position.y - transform.position.y);
        float dist = rel.magnitude;

        Vector3 ideal = target.position + offset;
        if (followY)
            ideal.y += lookAheadY + manualLookAheadY;

        if (dist > leash)
        {
            Vector2 dir = rel / dist;
            ideal.x = target.position.x - dir.x * leash;
            ideal.y = target.position.y - dir.y * leash;
            if (followY)
                ideal.y += lookAheadY + manualLookAheadY;
        }

        if (!followX)
            ideal.x = transform.position.x;
        if (!followY)
            ideal.y = transform.position.y;

        desired = ideal;

        float distRatio = Mathf.Clamp01(dist / leash);
        float leashUrgency = dist > leash ? 1f : distRatio * distRatio;

        smoothX = Mathf.Lerp(smoothX, airborneLeashMinSmoothTime, leashUrgency);
        smoothY = Mathf.Lerp(smoothY, airborneLeashMinSmoothTime, leashUrgency);

        float gapCloseSpeed = dist / Mathf.Max(followDt, 0.0001f);
        float requiredSpeed = gapCloseSpeed + airborneLeashSpeedMargin;

        if (player != null)
        {
            Vector2 vel = player.Velocity;
            if (dist > 0.01f)
            {
                Vector2 dir = rel / dist;
                float movingAway = Mathf.Max(0f, Vector2.Dot(vel, dir));
                requiredSpeed = Mathf.Max(requiredSpeed, movingAway + airborneLeashSpeedMargin);
            }

            requiredSpeed = Mathf.Max(requiredSpeed, vel.magnitude + airborneLeashSpeedMargin);
        }

        maxSpeedX = Mathf.Max(maxSpeedX, requiredSpeed);
        maxSpeedY = Mathf.Max(maxSpeedY, requiredSpeed);
    }

    private void EnforceAirborneCenterLeash()
    {
        if (target == null)
            return;

        float leash = Mathf.Max(0.1f, airborneMaxCenterOffset);
        Vector2 rel = new Vector2(
            target.position.x - transform.position.x,
            target.position.y - transform.position.y);
        float dist = rel.magnitude;

        if (dist <= leash + 0.0001f)
            return;

        Vector2 dir = rel / dist;
        float posX = followX ? target.position.x - dir.x * leash : transform.position.x;
        float posY = followY ? target.position.y - dir.y * leash : transform.position.y;
        transform.position = new Vector3(posX, posY, transform.position.z);
        velocity.x = 0f;
        velocity.y = 0f;
    }

    private void TickFallLookAhead(PlayerController player, float dt, bool freezeForLandingLock)
    {
        if (freezeForLandingLock)
        {
            lookAheadY = Mathf.SmoothDamp(
                lookAheadY,
                0f,
                ref lookAheadYVelocity,
                landingApproachFallLookAheadDecayTime,
                Mathf.Infinity,
                dt);
            return;
        }

        float targetLookAhead = GetClampedFallLookAheadTarget(player);
        float easeTime = fallLookAheadEaseTime;
        if (player != null && IsApproachingLanding(player) && takeoffHadBoundsVerticalAdjust)
            easeTime = landingApproachFallLookAheadDecayTime;

        lookAheadY = Mathf.SmoothDamp(
            lookAheadY,
            targetLookAhead,
            ref lookAheadYVelocity,
            easeTime,
            Mathf.Infinity,
            dt);
    }

    private float GetClampedFallLookAheadTarget(PlayerController player)
    {
        if (!ShouldApplyFallLookAhead(player))
            return 0f;

        float lookAheadTarget = -fallLookAheadDistance;
        if (!hasLevelLowestPlatformTop || cam == null || !cam.orthographic || this.target == null)
            return lookAheadTarget;

        float halfH = cam.orthographicSize;
        float camBaseY = this.target.position.y + offset.y;
        float minLookAheadY = levelLowestPlatformTopY + halfH - camBaseY;
        return Mathf.Max(lookAheadTarget, minLookAheadY);
    }

    /// <summary>
    /// Fall look-ahead only when high enough above ground: max jump height + look-ahead distance.
    /// </summary>
    private bool ShouldApplyFallLookAhead(PlayerController player)
    {
        if (player == null || player.IsGrounded || player.Velocity.y >= -fallLookAheadMinVelocity)
            return false;

        if (airborneFromLowestPlatformRow)
            return false;

        if (!TryProbeGroundBelow(player, out float heightAboveGround, out float groundTopY))
            return true;

        if (heightAboveGround < fallLookAheadMinHeightAboveGround)
            return false;

        if (IsOnLowestPlatformRow(groundTopY))
            return false;

        float minHeight = player.GetMaxJumpHeight() + fallLookAheadDistance;
        return heightAboveGround >= minHeight;
    }

    private bool IsOnLowestPlatformRow(float groundTopY)
    {
        if (!hasLevelLowestPlatformTop)
            return false;

        return groundTopY <= levelLowestPlatformTopY + lowestPlatformTierTolerance;
    }

    private bool IsApproachingLanding(PlayerController player)
    {
        if (player == null || player.IsGrounded || player.Velocity.y >= -fallLookAheadMinVelocity)
            return false;

        if (!TryProbeGroundBelow(player, out float heightAboveGround, out _))
            return false;

        return heightAboveGround <= landingApproachDistance;
    }

    private bool IsPlayerOverLowestPlatformRow(PlayerController player)
    {
        return TryProbeGroundBelow(player, out _, out float groundTopY) && IsOnLowestPlatformRow(groundTopY);
    }

    private bool TryProbeGroundBelow(PlayerController player, out float distance, out float groundTopY)
    {
        distance = float.PositiveInfinity;
        groundTopY = float.NaN;

        if (player == null || target == null)
            return false;

        Vector2 origin = target.position;
        RaycastHit2D[] hits = Physics2D.RaycastAll(origin, Vector2.down, groundProbeMaxDistance);
        float closest = float.PositiveInfinity;
        float closestTop = float.NaN;

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit2D hit = hits[i];
            if (hit.collider == null)
                continue;

            Transform hitTransform = hit.collider.transform;
            if (hitTransform == target || hitTransform.IsChildOf(target))
                continue;

            if (hit.distance < closest)
            {
                closest = hit.distance;
                closestTop = hit.collider.bounds.max.y;
            }
        }

        if (float.IsPositiveInfinity(closest))
            return false;

        distance = closest;
        groundTopY = closestTop;
        return true;
    }

    private float GetAcquireSmoothTime(float normalSmooth)
    {
        if (acquireTimer <= 0f || acquireBlendDuration <= 0.01f)
            return normalSmooth;

        float t = 1f - Mathf.Clamp01(acquireTimer / acquireBlendDuration);
        t = t * t * (3f - 2f * t);
        return Mathf.Lerp(acquireSmoothTime, normalSmooth, t);
    }

    private Vector3 GetArenaClampedPosition(Vector3 desired)
    {
        if (!hasBounds || cam == null || !cam.orthographic)
            return desired;

        float halfHeight = cam.orthographicSize;
        float halfWidth = halfHeight * cam.aspect;

        float minX = bounds.min.x + halfWidth + boundsPadding;
        float maxX = bounds.max.x - halfWidth - boundsPadding;
        float minY = bounds.min.y + halfHeight + boundsPadding;
        float maxY = bounds.max.y - halfHeight - boundsPadding;

        if (minX > maxX)
            desired.x = bounds.center.x;
        else
            desired.x = Mathf.Clamp(desired.x, minX, maxX);

        if (minY > maxY)
            desired.y = bounds.center.y;
        else
            desired.y = Mathf.Clamp(desired.y, minY, maxY);

        return desired;
    }

    private Vector3 GetArenaHardClampedPosition(Vector3 desired)
    {
        if (!hasBounds || cam == null || !cam.orthographic)
            return desired;

        float halfHeight = cam.orthographicSize;
        float halfWidth = halfHeight * cam.aspect;

        float minX = bounds.min.x + halfWidth;
        float maxX = bounds.max.x - halfWidth;
        float minY = bounds.min.y + halfHeight;
        float maxY = bounds.max.y - halfHeight;

        if (minX > maxX)
            desired.x = bounds.center.x;
        else
            desired.x = Mathf.Clamp(desired.x, minX, maxX);

        if (minY > maxY)
            desired.y = bounds.center.y;
        else
            desired.y = Mathf.Clamp(desired.y, minY, maxY);

        return desired;
    }

    private PlayerController ResolveFollowedPlayer()
    {
        if (PlayerController.Active != null &&
            (target == null || target == PlayerController.Active.transform))
        {
            return PlayerController.Active;
        }

        if (target == null)
            return null;

        PlayerController player = target.GetComponent<PlayerController>();
        if (player == null)
            player = target.GetComponentInParent<PlayerController>();

        return player;
    }

    private static bool TryGetTargetViewport(Camera camera, Transform followTarget, out Vector3 viewport)
    {
        viewport = default;
        if (camera == null || followTarget == null)
            return false;

        viewport = camera.WorldToViewportPoint(followTarget.position);
        return viewport.z >= 0f;
    }

    private static float SmoothEdgeUrgency(float overshoot)
    {
        overshoot = Mathf.Max(0f, overshoot);
        return 1f - Mathf.Exp(-overshoot * 3.5f);
    }

    public void ForceFollowPlayer(Transform playerTransform)
    {
        if (playerTransform == null)
            return;

        followEnabled = true;
        target = playerTransform;
        trackedTarget = playerTransform;

        if (!hasBounds && autoFindBackground)
            ResolveBackground();

        SnapToTarget();
        BeginRespawnCatchUp();
    }

    public void SetFollowTarget(Transform newTarget)
    {
        if (newTarget == null)
        {
            target = null;
            trackedTarget = null;
            return;
        }

        target = newTarget;
        BeginSoftAcquire(newTarget);
    }

    /// <summary>
    /// When false, camera holds still (used during life-loss fade-to-black).
    /// </summary>
    public void SetFollowEnabled(bool enabled)
    {
        followEnabled = enabled;
        if (!enabled)
        {
            velocity = Vector3.zero;
            lookAheadVelocity = 0f;
            lookAheadYVelocity = 0f;
            catchUpBoost = false;
        }
    }

    public bool FollowEnabled => followEnabled;

    /// <summary>
    /// Instantly places the camera on the target without enabling follow/tracking.
    /// Used while the screen is black so the reveal is framed correctly.
    /// </summary>
    public void SnapToTarget(Transform newTarget = null)
    {
        if (newTarget != null)
        {
            target = newTarget;
            trackedTarget = newTarget;
        }

        if (target == null)
            return;

        if (!hasBounds && autoFindBackground)
            ResolveBackground();

        Vector3 desired = target.position + offset;
        desired = HardClampPosition(ClampDesiredPosition(desired));
        if (!followX) desired.x = transform.position.x;
        if (!followY) desired.y = transform.position.y;
        desired.z = offset.z;

        transform.position = desired;
        velocity = Vector3.zero;
        lookAheadX = 0f;
        lookAheadVelocity = 0f;
        lookAheadY = 0f;
        lookAheadYVelocity = 0f;
        acquireTimer = 0f;
        catchUpBoost = false;
        downHoldTime = 0f;
        upHoldTime = 0f;
        manualLookAheadY = 0f;
        manualLookAheadYVelocity = 0f;
        landingReturnTimer = 0f;
        playerWasAirborne = false;
        airborneFromLowestPlatformRow = false;
        groundedOffCenterFromBoundsClamp = false;
        deferAirborneCenterLeash = false;
        landingApproachVerticalLocked = false;
        takeoffHadBoundsVerticalAdjust = false;
    }

    /// <summary>
    /// Speeds follow briefly after a mid-fight respawn / warning finishes.
    /// </summary>
    public void BeginRespawnCatchUp()
    {
        followEnabled = true;
        catchUpBoost = true;
        acquireTimer = 0f;
        velocity = Vector3.zero;
        lookAheadVelocity = 0f;
        lookAheadYVelocity = 0f;
        lookAheadX = 0f;
        lookAheadY = 0f;
    }

    /// <summary>
    /// True when the camera is close enough to the follow target that a fade-in feels settled.
    /// </summary>
    public bool HasCaughtUpToTarget(float maxDistance = 0.65f)
    {
        if (target == null)
            return true;

        Vector3 desired = target.position + offset;
        desired = ClampDesiredPosition(desired);
        if (!followX) desired.x = transform.position.x;
        if (!followY) desired.y = transform.position.y;

        Vector2 delta = new Vector2(desired.x - transform.position.x, desired.y - transform.position.y);
        return delta.sqrMagnitude <= maxDistance * maxDistance;
    }

    public void SetBackground(SpriteRenderer backgroundRenderer)
    {
        background = backgroundRenderer;
        if (UsesLevelFollowMode())
            backgroundBoundsRoot = ResolveBackgroundBoundsRoot();
        else
            backgroundBoundsRoot = null;

        CacheBounds();
    }

    /// <summary>
    /// World rectangle the camera is confined to (cached background bounds).
    /// Characters should stay inside this so they cannot dash into the void.
    /// </summary>
    public bool TryGetPlayableBounds(out Bounds playable)
    {
        if (!hasBounds)
            ResolveBackground();

        playable = bounds;
        return hasBounds;
    }

    /// <summary>
    /// Keeps a body inside the same background edges the camera cannot cross.
    /// Zeros outward velocity on contact so dashes / pulls do not push through.
    /// </summary>
    public bool ClampRigidbodyToPlayableBounds(Rigidbody2D body, Collider2D bodyCollider = null, float extraPadding = 0f)
    {
        if (body == null || !TryGetPlayableBounds(out Bounds playable))
            return false;

        float padX = Mathf.Max(0f, extraPadding);
        float padY = Mathf.Max(0f, extraPadding);
        if (bodyCollider != null)
        {
            Bounds b = bodyCollider.bounds;
            padX += b.extents.x;
            padY += b.extents.y;
        }

        float minX = playable.min.x + padX;
        float maxX = playable.max.x - padX;
        float minY = playable.min.y + padY;
        float maxY = playable.max.y - padY;

        Vector2 pos = body.position;
        Vector2 vel = body.linearVelocity;
        bool clamped = false;

        if (minX <= maxX)
        {
            if (pos.x < minX)
            {
                pos.x = minX;
                if (vel.x < 0f)
                    vel.x = 0f;
                clamped = true;
            }
            else if (pos.x > maxX)
            {
                pos.x = maxX;
                if (vel.x > 0f)
                    vel.x = 0f;
                clamped = true;
            }
        }

        if (minY <= maxY)
        {
            if (pos.y < minY)
            {
                pos.y = minY;
                if (vel.y < 0f)
                    vel.y = 0f;
                clamped = true;
            }
            else if (pos.y > maxY)
            {
                pos.y = maxY;
                if (vel.y > 0f)
                    vel.y = 0f;
                clamped = true;
            }
        }

        if (!clamped)
            return false;

        body.position = pos;
        body.linearVelocity = vel;
        return true;
    }

    private void BeginSoftAcquire(Transform newTarget)
    {
        trackedTarget = newTarget;
        acquireTimer = acquireBlendDuration;
        velocity = Vector3.zero;
        lookAheadVelocity = 0f;
        lookAheadYVelocity = 0f;

        if (!softAcquireOnly && target != null)
            transform.position = UsesLevelFollowMode()
                ? GetClampedPosition(target.position + offset)
                : GetArenaClampedPosition(target.position + offset);
    }

    private void ResolveTarget()
    {
        if (target != null)
            return;

        if (!autoFindPlayer)
            return;

        Transform found = null;

        if (PlayerController.Active != null)
            found = PlayerController.Active.transform;
        else
        {
            GameObject tagged = GameObject.FindWithTag("Player");
            if (tagged != null)
                found = tagged.transform;
        }

        // Waiting for spawn / no checkpoint yet: frame the Player Spawner.
        if (found == null)
            found = CheckPointBot.GetWaitingCameraTrackTarget();

        if (found == null)
            return;

        target = found;
        BeginSoftAcquire(found);
    }

    private void ResolveBackground()
    {
        if (!UsesLevelFollowMode())
        {
            ResolveArenaBackground();
            return;
        }

        backgroundBoundsRoot = ResolveBackgroundBoundsRoot();

        if (background == null && backgroundBoundsRoot != null)
            background = backgroundBoundsRoot.GetComponentInChildren<SpriteRenderer>(true);

        CacheBounds();
    }

    /// <summary>Rebuild playable bounds after parallax layers finish their first LateUpdate.</summary>
    public void RefreshPlayableBounds()
    {
        ResolveBackground();
    }

    private Transform ResolveBackgroundBoundsRoot()
    {
        if (autoFindBackground)
        {
            Transform widest = null;
            float widestSpan = -1f;

            Background[] layers = Object.FindObjectsByType<Background>(FindObjectsSortMode.None);
            for (int i = 0; i < layers.Length; i++)
            {
                Background layer = layers[i];
                if (layer == null)
                    continue;

                Transform root = GetStageBackgroundRoot(layer.transform);
                if (root == null)
                    continue;

                if (TryGetRootBoundsSpan(root, out float span) && span > widestSpan)
                {
                    widestSpan = span;
                    widest = root;
                }
            }

            if (widest != null)
                return widest;

            GameObject factory = GameObject.Find("Factory Background");
            if (factory != null)
                return factory.transform;

            GameObject found = GameObject.Find(backgroundObjectName);
            if (found != null)
                return found.transform;
        }

        if (background == null)
            return null;

        Transform current = background.transform;
        while (current != null)
        {
            if (IsStageBackgroundRootName(current.name))
                return current;

            current = current.parent;
        }

        return background.transform;
    }

    private static Transform GetStageBackgroundRoot(Transform node)
    {
        Transform best = null;
        Transform current = node;
        while (current != null)
        {
            if (IsStageBackgroundRootName(current.name))
                best = current;

            current = current.parent;
        }

        return best;
    }

    private static bool TryGetRootBoundsSpan(Transform root, out float span)
    {
        span = -1f;
        if (root == null)
            return false;

        SpriteRenderer[] renderers = root.GetComponentsInChildren<SpriteRenderer>(true);
        if (renderers == null || renderers.Length == 0)
            return false;

        Bounds segment = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                segment.Encapsulate(renderers[i].bounds);
        }

        span = segment.size.x * segment.size.y;
        return span > 0f;
    }

    private static bool IsStageBackgroundRootName(string objectName)
    {
        if (string.IsNullOrEmpty(objectName))
            return false;

        return string.Equals(objectName, "Background", System.StringComparison.OrdinalIgnoreCase)
            || objectName.StartsWith("Factory Background", System.StringComparison.Ordinal)
            || string.Equals(objectName, "Sky Background", System.StringComparison.OrdinalIgnoreCase);
    }

    private void ResolveArenaBackground()
    {
        backgroundBoundsRoot = null;
        stageBackgroundRenderers = null;

        if (background == null && autoFindBackground)
        {
            GameObject found = GameObject.Find(backgroundObjectName);
            if (found != null)
                background = found.GetComponent<SpriteRenderer>();

            if (background == null)
            {
                Background[] layers = Object.FindObjectsByType<Background>(FindObjectsSortMode.None);
                for (int i = 0; i < layers.Length; i++)
                {
                    Background layer = layers[i];
                    if (layer == null)
                        continue;

                    SpriteRenderer renderer = layer.GetComponent<SpriteRenderer>();
                    if (renderer != null)
                    {
                        background = renderer;
                        break;
                    }
                }
            }
        }

        CacheBounds();
    }

    private void CacheBounds()
    {
        if (!UsesLevelFollowMode())
        {
            CacheArenaBounds();
            return;
        }

        hasBounds = false;
        stageBackgroundRenderers = CollectStageBackgroundRenderers();

        for (int i = 0; i < stageBackgroundRenderers.Length; i++)
        {
            SpriteRenderer renderer = stageBackgroundRenderers[i];
            if (renderer == null)
                continue;

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        if (!hasBounds && background != null)
        {
            bounds = background.bounds;
            hasBounds = true;
        }

        // No background art — use Scene Edge markers as the playable / camera box.
        if (!hasBounds && SceneEdge.TryBuildFallbackBounds(out Bounds edgeBounds))
        {
            bounds = edgeBounds;
            hasBounds = true;
        }

        CacheLevelLowestPlatformTop();
    }

    private void CacheArenaBounds()
    {
        hasBounds = false;
        stageBackgroundRenderers = null;

        if (background != null)
        {
            bounds = background.bounds;
            hasBounds = true;
        }

        if (!hasBounds && SceneEdge.TryBuildFallbackBounds(out Bounds edgeBounds))
        {
            bounds = edgeBounds;
            hasBounds = true;
        }

        CacheLevelLowestPlatformTop();
    }

    /// <summary>
    /// Walkable top of the lowest ground collider in the level — fall look-ahead won't frame below this.
    /// </summary>
    private void CacheLevelLowestPlatformTop()
    {
        hasLevelLowestPlatformTop = false;

        LayerMask groundMask = 0;
        PlayerController active = PlayerController.Active;
        if (active != null)
            groundMask = active.GroundLayers;

        if (groundMask == 0)
        {
            if (hasBounds)
            {
                levelLowestPlatformTopY = bounds.min.y;
                hasLevelLowestPlatformTop = true;
            }

            return;
        }

        Collider2D[] colliders = Object.FindObjectsByType<Collider2D>(FindObjectsSortMode.None);
        float lowestColliderMinY = float.PositiveInfinity;

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider2D col = colliders[i];
            if (col == null)
                continue;

            if (((1 << col.gameObject.layer) & groundMask) == 0)
                continue;

            lowestColliderMinY = Mathf.Min(lowestColliderMinY, col.bounds.min.y);
        }

        if (float.IsPositiveInfinity(lowestColliderMinY))
        {
            if (hasBounds)
            {
                levelLowestPlatformTopY = bounds.min.y;
                hasLevelLowestPlatformTop = true;
            }

            return;
        }

        float bottomTierTolerance = lowestPlatformTierTolerance;
        float walkableTop = float.PositiveInfinity;

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider2D col = colliders[i];
            if (col == null)
                continue;

            if (((1 << col.gameObject.layer) & groundMask) == 0)
                continue;

            if (col.bounds.min.y > lowestColliderMinY + bottomTierTolerance)
                continue;

            walkableTop = Mathf.Min(walkableTop, col.bounds.max.y);
        }

        if (!float.IsPositiveInfinity(walkableTop))
        {
            levelLowestPlatformTopY = walkableTop;
            hasLevelLowestPlatformTop = true;
        }
        else if (hasBounds)
        {
            levelLowestPlatformTopY = bounds.min.y;
            hasLevelLowestPlatformTop = true;
        }
    }

    private SpriteRenderer[] CollectStageBackgroundRenderers()
    {
        var collected = new System.Collections.Generic.List<SpriteRenderer>(32);
        var seen = new System.Collections.Generic.HashSet<int>();

        Background[] parallaxLayers = Object.FindObjectsByType<Background>(FindObjectsSortMode.None);
        for (int i = 0; i < parallaxLayers.Length; i++)
        {
            Background layer = parallaxLayers[i];
            if (layer == null)
                continue;

            SpriteRenderer[] renderers = layer.GetComponentsInChildren<SpriteRenderer>(true);
            for (int r = 0; r < renderers.Length; r++)
                TryAddBackgroundRenderer(renderers[r], collected, seen);
        }

        SpriteRenderer[] allRenderers = Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None);
        for (int i = 0; i < allRenderers.Length; i++)
        {
            SpriteRenderer renderer = allRenderers[i];
            if (renderer == null || !IsStageBackgroundRenderer(renderer))
                continue;

            TryAddBackgroundRenderer(renderer, collected, seen);
        }

        return collected.ToArray();
    }

    private static void TryAddBackgroundRenderer(
        SpriteRenderer renderer,
        System.Collections.Generic.List<SpriteRenderer> collected,
        System.Collections.Generic.HashSet<int> seen)
    {
        if (renderer == null || renderer.sprite == null)
            return;

        int id = renderer.GetInstanceID();
        if (!seen.Add(id))
            return;

        collected.Add(renderer);
    }

    private static bool IsStageBackgroundRenderer(SpriteRenderer renderer)
    {
        if (renderer == null)
            return false;

        Transform node = renderer.transform;
        while (node != null)
        {
            if (IsStageBackgroundRootName(node.name))
                return true;

            node = node.parent;
        }

        return false;
    }

    private Vector3 GetClampedPosition(Vector3 desired)
    {
        if (!hasBounds || cam == null || !cam.orthographic)
            return desired;

        float halfHeight = cam.orthographicSize;
        float halfWidth = halfHeight * cam.aspect;

        float minX = bounds.min.x + halfWidth + boundsPadding;
        float maxX = bounds.max.x - halfWidth - boundsPadding;

        if (minX > maxX)
            desired.x = bounds.center.x;
        else
            desired.x = Mathf.Clamp(desired.x, minX, maxX);

        GetVerticalBoundsAt(desired.x, halfWidth, out float minY, out float maxY, out bool hasVertical);
        if (hasVertical)
        {
            float clampMinY = minY + halfHeight + boundsPadding + verticalBoundsInset;
            float clampMaxY = maxY - halfHeight - boundsPadding - verticalBoundsInset;

            if (clampMinY > clampMaxY)
                desired.y = (minY + maxY) * 0.5f;
            else
                desired.y = Mathf.Clamp(desired.y, clampMinY, clampMaxY);
        }

        return desired;
    }

    /// <summary>
    /// Vertical clamp uses background art overlapping the camera column so high/low platforms
    /// do not reveal clear color above or beside shorter backdrop segments.
    /// </summary>
    private void GetVerticalBoundsAt(float viewCenterX, float halfViewWidth, out float minY, out float maxY, out bool found)
    {
        minY = float.PositiveInfinity;
        maxY = float.NegativeInfinity;
        found = false;

        if (stageBackgroundRenderers == null || stageBackgroundRenderers.Length == 0)
        {
            if (hasBounds)
            {
                minY = bounds.min.y + verticalBoundsInset;
                maxY = bounds.max.y - verticalBoundsInset;
                found = true;
            }

            return;
        }

        float sampleMinX = viewCenterX - halfViewWidth;
        float sampleMaxX = viewCenterX + halfViewWidth;

        for (int i = 0; i < stageBackgroundRenderers.Length; i++)
        {
            SpriteRenderer renderer = stageBackgroundRenderers[i];
            if (renderer == null)
                continue;

            Bounds b = GetTightSpriteWorldBounds(renderer);
            if (b.max.x < sampleMinX || b.min.x > sampleMaxX)
                continue;

            minY = Mathf.Min(minY, b.min.y);
            maxY = Mathf.Max(maxY, b.max.y);
            found = true;
        }

        if (!found && hasBounds)
        {
            minY = bounds.min.y + verticalBoundsInset;
            maxY = bounds.max.y - verticalBoundsInset;
            found = true;
        }
        else if (found)
        {
            minY += verticalBoundsInset;
            maxY -= verticalBoundsInset;
        }
    }

    private static Bounds GetTightSpriteWorldBounds(SpriteRenderer renderer)
    {
        if (renderer == null)
            return default;

        if (renderer.sprite == null)
            return renderer.bounds;

        Bounds local = renderer.sprite.bounds;
        Vector3 worldCenter = renderer.transform.TransformPoint(local.center);
        Vector3 worldExtents = renderer.transform.TransformVector(local.extents);
        worldExtents = new Vector3(
            Mathf.Abs(worldExtents.x),
            Mathf.Abs(worldExtents.y),
            Mathf.Abs(worldExtents.z));

        return new Bounds(worldCenter, worldExtents * 2f);
    }

    /// <summary>
    /// Tight clamp so the camera view never crosses background edges (including after SmoothDamp overshoot).
    /// </summary>
    private Vector3 GetHardClampedPosition(Vector3 desired)
    {
        if (!hasBounds || cam == null || !cam.orthographic)
            return desired;

        float halfHeight = cam.orthographicSize;
        float halfWidth = halfHeight * cam.aspect;

        float minX = bounds.min.x + halfWidth;
        float maxX = bounds.max.x - halfWidth;

        if (minX > maxX)
            desired.x = bounds.center.x;
        else
            desired.x = Mathf.Clamp(desired.x, minX, maxX);

        GetVerticalBoundsAt(desired.x, halfWidth, out float minY, out float maxY, out bool hasVertical);
        if (hasVertical)
        {
            float clampMinY = minY + halfHeight;
            float clampMaxY = maxY - halfHeight;

            if (clampMinY > clampMaxY)
                desired.y = (minY + maxY) * 0.5f;
            else
                desired.y = Mathf.Clamp(desired.y, clampMinY, clampMaxY);
        }

        return desired;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        deadZoneLeft = Mathf.Clamp(deadZoneLeft, 0.05f, 0.49f);
        deadZoneRight = Mathf.Clamp(deadZoneRight, 0.51f, 0.95f);
        deadZoneBottom = Mathf.Clamp(deadZoneBottom, 0.05f, 0.49f);
        deadZoneTop = Mathf.Clamp(deadZoneTop, 0.51f, 0.95f);
        edgeZoneLeft = Mathf.Clamp(edgeZoneLeft, 0.02f, deadZoneLeft - 0.01f);
        edgeZoneRight = Mathf.Clamp(edgeZoneRight, deadZoneRight + 0.01f, 0.98f);
        edgeZoneBottom = Mathf.Clamp(edgeZoneBottom, 0.02f, deadZoneBottom - 0.01f);
        edgeZoneTop = Mathf.Clamp(edgeZoneTop, deadZoneTop + 0.01f, 0.98f);
        edgeCatchUpSmoothTime = Mathf.Max(0.02f, edgeCatchUpSmoothTime);
        edgeCatchUpMaxSpeed = Mathf.Max(maxFollowSpeedX, edgeCatchUpMaxSpeed);
        smoothTimeX = Mathf.Max(0.01f, smoothTimeX);
        smoothTimeY = Mathf.Max(0.01f, smoothTimeY);
        airborneSmoothTimeY = Mathf.Max(0.01f, airborneSmoothTimeY);
        maxFollowSpeedX = Mathf.Max(0.5f, maxFollowSpeedX);
        maxFollowSpeedY = Mathf.Max(0.5f, maxFollowSpeedY);
        airborneMaxSpeedY = Mathf.Max(maxFollowSpeedY, airborneMaxSpeedY);
        deadZoneFollowMultiplier = Mathf.Max(1f, deadZoneFollowMultiplier);
        acquireSmoothTime = Mathf.Max(Mathf.Max(smoothTimeX, smoothTimeY), acquireSmoothTime);
        acquireBlendDuration = Mathf.Max(0.05f, acquireBlendDuration);
        lookAheadDistance = Mathf.Max(0f, lookAheadDistance);
        lookAheadSmoothTime = Mathf.Max(0.01f, lookAheadSmoothTime);
        speedBoostThreshold = Mathf.Max(0f, speedBoostThreshold);
        walkLookAheadStrength = Mathf.Clamp(walkLookAheadStrength, 0f, 1f);
        airborneMaxCenterOffset = Mathf.Max(0.1f, airborneMaxCenterOffset);
        airborneLeashMinSmoothTime = Mathf.Max(0.01f, airborneLeashMinSmoothTime);
        airborneLeashSpeedMargin = Mathf.Max(0f, airborneLeashSpeedMargin);
        viewportCenterTolerance = Mathf.Clamp(viewportCenterTolerance, 0.01f, 0.25f);
        boundsClampPositionTolerance = Mathf.Max(0.001f, boundsClampPositionTolerance);
        fallLookAheadDistance = Mathf.Clamp(fallLookAheadDistance, 0f, 2f);
        fallLookAheadEaseTime = Mathf.Max(0.01f, fallLookAheadEaseTime);
        fallLookAheadMinVelocity = Mathf.Max(0f, fallLookAheadMinVelocity);
        fallLookAheadMinHeightAboveGround = Mathf.Max(0f, fallLookAheadMinHeightAboveGround);
        lowestPlatformTierTolerance = Mathf.Max(0.05f, lowestPlatformTierTolerance);
        groundProbeMaxDistance = Mathf.Max(1f, groundProbeMaxDistance);
        landingReturnDuration = Mathf.Max(0f, landingReturnDuration);
        landingReturnSmoothTime = Mathf.Max(0.01f, landingReturnSmoothTime);
        landingApproachDistance = Mathf.Max(0.1f, landingApproachDistance);
        landingApproachSmoothTimeY = Mathf.Max(0.01f, landingApproachSmoothTimeY);
        landingApproachMaxSpeedY = Mathf.Max(maxFollowSpeedY, landingApproachMaxSpeedY);
        landingApproachArrivedTolerance = Mathf.Max(0.01f, landingApproachArrivedTolerance);
        landingApproachFallLookAheadDecayTime = Mathf.Max(0.01f, landingApproachFallLookAheadDecayTime);
        manualLookAheadHoldDelay = Mathf.Max(0f, manualLookAheadHoldDelay);
        manualLookAheadMax = Mathf.Max(0.1f, manualLookAheadMax);
        manualLookAheadSmoothTime = Mathf.Max(0.01f, manualLookAheadSmoothTime);
        verticalInputThreshold = Mathf.Clamp(verticalInputThreshold, 0.05f, 0.95f);
        boundsPadding = Mathf.Max(0f, boundsPadding);
        verticalBoundsInset = Mathf.Max(0f, verticalBoundsInset);
    }
#endif
}
