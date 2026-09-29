using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Boss version of Kit. Malice-style multi-pattern AI adapted for blaster shots.
/// Auto-charges between patterns; peppers small shots while charging. 3s cooldown after medium/big releases.
/// Dashes close distance for shots (no damage dashes — no Metal Sonic air-dash pattern).
/// </summary>
public class BossKit : Boss
{
    private enum Pattern
    {
        ZeroRush = 0,
        DashSlash = 1,
        NeedleDash = 2,
        HornetDive = 3,
        DashJumpDive = 4,
        RayHook = 5,
        GrabHook = 6,
        AirGrappleChase = 7,
        DashJumpTrapDash = 8,
        DashJumpCatchUp = 9,
        AirGrabSlash = 10,
        HoverBarrage = 11,
        HoverDashStrike = 12
    }

    private enum HoverPhase
    {
        GroundJump,
        DoubleJump,
        Hovering,
        HoverDash
    }

    private enum HoverMode
    {
        Barrage,
        DashStrike
    }

    private enum DashJumpMode
    {
        DiveCut,
        TrapThenDash,
        CatchUp
    }

    private enum AiState
    {
        Chase,
        Dash,
        DashJump,
        JumpPrep,
        ChargeWindup,
        ShootBurst,
        Recover,
        Hover
    }

    private enum DashFollowUp
    {
        None,
        ShootBurst,
        SingleShot,
        Recover,
        GoChase
    }

    private enum JumpPrepGoal
    {
        HornetDive
    }

    private enum BossKitAttackStyle
    {
        Normal,
        SpreadShot,
        MachineGun
    }

    [Header("Boss Kit - Placeholder AI (temporary)")]
    [SerializeField] private bool usePlaceholderAi = true;

    [Header("Spacing")]
    [SerializeField] private float preferredDistance = 5.5f;
    [SerializeField] private float chaseStopDistance = 2.2f;
    [SerializeField] private float midRange = 5f;
    [SerializeField] private float farRange = 9f;
    [SerializeField] private float decideIntervalFullHp = 0.55f;
    [SerializeField] private float decideIntervalLowHp = 0.2f;

    [Header("Dash")]
    [Tooltip("Slightly above playable Kit (5.25 / 17).")]
    [SerializeField] private float dashDistance = 6f;
    [SerializeField] private float dashSpeed = 19.5f;
    [SerializeField] private float airDashSpeedMul = 0.95f;

    [Header("Stay In Frame")]
    [SerializeField] private bool tryStayInFrame = true;
    [SerializeField] private float frameEdgeViewportX = 0.14f;
    [SerializeField] [Range(0.2f, 1f)] private float frameNudgeMoveSpeedMul = 0.5f;
    [SerializeField] [Range(0f, 1f)] private float frameDashBiasChance = 0.55f;

    [Header("Dash Jump")]
    [SerializeField] private float dashJumpHorizontalSpeed = 0f;
    [SerializeField] private float dashJumpForce = 11f;
    [SerializeField] private float dashJumpTrapLead = 2f;
    [SerializeField] private float dashJumpAssaultCloseMin = 1f;
    [SerializeField] private float dashJumpAssaultCloseMax = 2f;
    [SerializeField] private float dashJumpMinTravel = 1f;
    [SerializeField] private float dashJumpCatchUpCooldown = 0.55f;
    [SerializeField] private float dashJumpConvertDelay = 0.06f;
    [SerializeField] private float dashJumpHeightMul = 1.15f;
    [SerializeField] private float dashJumpSpeedMul = 1.25f;
    [SerializeField] private float dashJumpDistanceMul = 1.1f;
    [SerializeField] private float minAirTimeBeforeDashJumpAction = 0.1f;
    [SerializeField] private float shootLandRange = 2.75f;

    [Header("Jump")]
    [SerializeField] private float jumpPrepTimeout = 1.4f;
    [SerializeField] private float minAirTimeBeforeAction = 0.12f;
    [SerializeField] private float diveAttackRangeX = 4.5f;

    [Header("Double Jump + Hover (matches playable Kit)")]
    [Tooltip("Upward speed of the double jump. 0 = same as the normal jump.")]
    [SerializeField] private float doubleJumpForce = 0f;
    [Tooltip("If the player is at least this much higher at the jump apex, Hornet Dive / Air Grab Slash add a double jump.")]
    [SerializeField] private float doubleJumpChaseHeight = 1.5f;
    [SerializeField] private float hoverLevelSeconds = 2.4f;
    [SerializeField] private float hoverLevelSecondsEnraged = 1.8f;
    [SerializeField] private float hoverDescendSpeed = 1.5f;
    [SerializeField] private float hoverDescendAcceleration = 4f;
    [Tooltip("Safety cap on one hover pattern (seconds airborne) before she drops.")]
    [SerializeField] private float hoverMaxSeconds = 6f;
    [SerializeField] private float hoverShotInterval = 0.6f;
    [SerializeField] private float hoverShotIntervalEnraged = 0.42f;
    [SerializeField] private float hoverFirstShotDelay = 0.25f;
    [SerializeField] [Range(0.1f, 1f)] private float hoverDriftSpeedMul = 0.45f;
    [Tooltip("Hover Dash Strike: seconds hovering before the boosted dash.")]
    [SerializeField] private float hoverDashDelay = 0.55f;
    [Tooltip("45° down-shots only while airborne and the player is at least this far below.")]
    [SerializeField] private float aimDownThreshold = 1f;

    [Header("Boosted Dash")]
    [Tooltip("Used at max charge and for the hover dash.")]
    [SerializeField] private float boostedDashSpeedMultiplier = 1.3f;
    [SerializeField] private float boostedDashDistanceMultiplier = 1.45f;

    [Header("Auto Charge")]
    [SerializeField] private float chargeCooldownSeconds = 3f;
    [SerializeField] private float chargePepperShotInterval = 0.38f;
    [SerializeField] private float mediumChargeSeconds = 1.5f;
    [SerializeField] private float bigChargeSeconds = 3f;
    [SerializeField] private float aiChargeHoldMin = 1.55f;
    [SerializeField] private float aiChargeHoldMax = 2.5f;
    [SerializeField] private float mediumChargeMoveSpeedBonus = 3f;
    [SerializeField] private float bigChargeMoveSpeedBonus = 6f;

    [Header("Kit Boss - Pink Charge Aura")]
    [SerializeField] private bool showChargeAura = true;
    [SerializeField] private Color chargeAuraColor = new Color(1f, 0.45f, 0.75f, 1f);
    [SerializeField] private Color chargeAuraStrongColor = new Color(1f, 0.25f, 0.7f, 1f);
    [SerializeField] private Color chargeAuraFlickerColor = new Color(1f, 0.92f, 0.55f, 1f);
    [SerializeField] private float auraBaseScale = 1.18f;
    [SerializeField] private float auraPulseAmount = 0.05f;
    [SerializeField] private float auraPulseSpeed = 6f;
    [SerializeField] private float auraFlickerSpeed = 1.25f;
    [SerializeField] [Range(0.05f, 0.5f)] private float auraFlickerStrength = 0.28f;
    [SerializeField] [Range(0f, 1f)] private float auraAlphaStart = 0.25f;
    [SerializeField] [Range(0f, 1f)] private float auraAlphaAtMedium = 0.65f;
    [SerializeField] [Range(0f, 1f)] private float auraAlphaAtBig = 0.95f;

    [Header("Kit Boss - Dash Afterimages")]
    [SerializeField] private bool enableDashAfterimages = true;
    [SerializeField] private int dashAfterimageCount = 3;
    [SerializeField] private float dashAfterimageSpacing = 0.1f;
    [SerializeField] private Color dashAfterimageColor = Color.white;
    [SerializeField] [Range(0f, 1f)] private float dashAfterimageAlphaStart = 0.55f;
    [SerializeField] [Range(0f, 1f)] private float dashAfterimageAlphaEnd = 0.15f;
    [SerializeField] private float dashAfterimageLifetimePadding = 0.15f;

    [Header("Shooting")]
    [SerializeField] private Transform firePoint;
    [Tooltip("Left-facing FirePoint keys are mirrored across this local X when she faces right (same as playable Kit).")]
    [SerializeField] private Vector2 firePointFlipCenterLocal = Vector2.zero;
    [SerializeField] private Projectile smallProjectilePrefab;
    [SerializeField] private Projectile mediumProjectilePrefab;
    [SerializeField] private Projectile bigProjectilePrefab;
    [SerializeField] private int shotsPerBurst = 3;
    [SerializeField] private float timeBetweenShots = 0.22f;
    [SerializeField] private float machineGunShotInterval = 0.14f;
    [SerializeField] private int machineGunMaxShots = 4;
    [SerializeField] private float shootAnimDuration = 0.28f;
    [SerializeField] private float recoverDuration = 0.55f;
    [SerializeField] private float recoverDurationEnraged = 0.38f;
    [SerializeField] private float aimUpThreshold = 0.45f;
    [SerializeField] private float spreadShotAngleDegrees = 45f;
    [Tooltip("Hard cap on live boss-owned projectiles (oldest removed when over).")]
    [SerializeField] private int maxLiveProjectiles = 14;
    [Tooltip("Boss Kit shots fly at prefab speed × this (below 1 = easier to dodge).")]
    [SerializeField] [Range(0.4f, 1f)] private float projectileSpeedMultiplier = 0.8f;

    [Header("Attack Spacing (readability)")]
    [Tooltip("World-unit gap between consecutive shots, rows, and spread volleys.")]
    [SerializeField] private float attackSpacingWorldUnits = 2f;
    [SerializeField] private int burstGroupMaxShots = 4;
    [SerializeField] private int spreadVolleyMaxCount = 3;
    [SerializeField] private float attackBreakMinSeconds = 0.4f;
    [SerializeField] private float attackBreakMaxSeconds = 0.8f;
    [Tooltip("Slightly below 1 — tiny movement slowdown for Kit boss and copy bot.")]
    [SerializeField] [Range(0.85f, 1f)] private float bossMovementSpeedScale = 0.94f;

    [Header("Boss Kit - VFX Prefabs")]
    [SerializeField] private GameVisualEffect busterBlastSmallPrefab;
    [FormerlySerializedAs("busterBlastMediumPrefab")]
    [SerializeField] private GameVisualEffect busterBlastMediumBigPrefab;

    [Header("Enrage")]
    [SerializeField] private float enrageHpFraction = 0.5f;
    [SerializeField] private float enrageDecideInterval = 0.12f;

    private static readonly Pattern[] OpeningCycle =
    {
        Pattern.ZeroRush,
        Pattern.DashSlash,
        Pattern.NeedleDash,
        Pattern.HornetDive,
        Pattern.DashJumpDive,
        Pattern.HoverBarrage,
        Pattern.DashJumpTrapDash,
        Pattern.DashJumpCatchUp,
        Pattern.RayHook,
        Pattern.GrabHook,
        Pattern.AirGrabSlash,
        Pattern.HoverDashStrike,
        Pattern.AirGrappleChase
    };

    private AiState state = AiState.Chase;
    private Pattern activePattern;
    private BossKitAttackStyle attackStyle = BossKitAttackStyle.Normal;

    private float decideTimer;
    private float stateTimer;
    private float recoverTimer;

    private bool openingCycleActive = true;
    private int openingIndex;
    private readonly List<Pattern> patternRotation = new List<Pattern>(15);
    private int rotationIndex;
    private Pattern lastUsedPattern = (Pattern)(-1);
    private int lastPatternStreak;

    private int shotsLeft;
    private float nextShotTime;
    private float burstPauseUntil;
    private int shotsFiredInCurrentGroup;
    private ProjectileShotType burstShotType = ProjectileShotType.Small;
    private float shootAnimTimer;
    private bool shootAnimActive;

    private float dashTimer;
    private float dashDuration;
    private float dashSpeedCurrent;
    private float dashDir = 1f;
    private DashFollowUp dashFollowUp;

    private DashJumpMode dashJumpMode;
    private float dashJumpStartX;
    private float dashJumpTravelTarget;
    private float dashJumpTargetX;
    private float dashJumpTrapX;
    private float dashJumpCatchUpCooldownTimer;
    private float dashJumpAirTime;
    private bool pendingConvertToDashJump;
    private bool isBossDashJumping;

    private JumpPrepGoal jumpPrepGoal;
    private float jumpPrepAirTime;
    private bool jumpPrepHasLeftGround;
    private bool jumpPrepDoubleJumped;

    private int bossAirJumpsRemaining = 1;
    private bool isBossHovering;
    private HoverPhase hoverPhase;
    private HoverMode hoverMode;
    private float hoverPatternTimer;
    private float hoverPhaseTimer;
    private float nextHoverShotTime;
    private bool hoverHasLeftGround;
    private bool bossBoostedDashActive;
    private bool aimDown;

    private float chargeTimer;
    private float chargeReleaseAt;
    private bool chargeWantsBig;
    private bool chargeStartedInAir;
    private float chargeCooldownTimer;
    private float nextChargePepperTime;
    private bool chargeLoopActive;

    private GameObject chargeAuraObject;
    private SpriteRenderer chargeAuraRenderer;
    private Material chargeAuraMaterial;
    private float auraFlickerPhase;

    private GameObject[] dashAfterimageObjects;
    private SpriteRenderer[] dashAfterimageRenderers;
    private float dashAfterimagesVisibleUntil;
    private readonly List<PoseSample> poseHistory = new List<PoseSample>(64);

    private struct PoseSample
    {
        public float time;
        public Vector3 position;
        public Sprite sprite;
        public bool flipX;
    }

    private readonly List<Projectile> liveProjectiles = new List<Projectile>(16);

    private bool copyBotMode;
    private bool copyBotDecoyMode;
    private float copyBotDecoyCooldownTimer;
    private float copyBotDecoyGrayLevel = 0.52f;
    private float decoyContactStunCooldown;
    private readonly List<Boss> liveCopyBotDecoys = new List<Boss>(2);
    private const int CopyBotMaxLiveBigShots = 4;
    private float copyBotAirStuckTimer;
    private const float CopyBotMaxAirStateSeconds = 2.6f;
    private BossSpawner cachedBossSpawner;

    private Vector3 firePointLeftFacingLocal;
    private Vector3 firePointLastWrittenLocal;
    private bool firePointHasLastWritten;

    protected override void Awake()
    {
        SetBossId("BossKit");
        base.Awake();

        if (firePoint == null)
        {
            Transform found = transform.Find("FirePoint");
            if (found != null)
                firePoint = found;
        }

        RebuildPatternRotation();
        SetupChargeAura();
        SetupDashAfterimages();
    }

    private void OnDestroy()
    {
        DestroyDashAfterimages();
        if (chargeAuraObject != null)
            Destroy(chargeAuraObject);
        if (chargeAuraMaterial != null)
            Destroy(chargeAuraMaterial);
    }

    private void LateUpdate()
    {
        MirrorFirePointForFacing();
    }

    /// <summary>
    /// Same as playable Kit: FirePoint keys are authored left-facing, then X is reflected when facing right.
    /// </summary>
    private void MirrorFirePointForFacing()
    {
        if (firePoint == null)
            return;

        Vector3 currentLocal = firePoint.parent == transform
            ? firePoint.localPosition
            : transform.InverseTransformPoint(firePoint.position);

        bool unchangedSinceWeWrote = firePointHasLastWritten &&
            (currentLocal - firePointLastWrittenLocal).sqrMagnitude < 1e-10f;

        if (!unchangedSinceWeWrote)
            firePointLeftFacingLocal = currentLocal;

        Vector3 target = firePointLeftFacingLocal;
        bool mirror = spriteFacesLeft ? facingSign > 0f : facingSign < 0f;
        if (mirror)
        {
            float centerX = firePointFlipCenterLocal.x;
            target.x = centerX - (firePointLeftFacingLocal.x - centerX);
        }

        if (firePoint.parent == transform)
            firePoint.localPosition = target;
        else
            firePoint.position = transform.TransformPoint(target);

        firePointLastWrittenLocal = target;
        firePointHasLastWritten = true;
    }

    protected override void OnHitStunStarted()
    {
        CancelAttackImmediate();
        state = AiState.Chase;
        decideTimer = 0f;
        base.OnHitStunStarted();
    }

    protected override void Die()
    {
        EndBossHover();
        base.Die();
    }

    protected override void OnCombatPaused()
    {
        SoundManager.Instance?.StopChargeLoopImmediate();
        base.OnCombatPaused();
        CancelAttackImmediate();
    }

    protected override void OnRevived()
    {
        CancelAttackImmediate();
        state = AiState.Chase;
        openingCycleActive = true;
        openingIndex = 0;
        decideTimer = 0f;
        chargeCooldownTimer = 0f;
        chargeTimer = 0f;
    }

    protected override void HandleBossUpdate()
    {
        if (copyBotDecoyMode)
        {
            TickCopyBotDecoyBehavior();
            return;
        }

        TickCopyBotAirStuckRecovery();

        if (!usePlaceholderAi)
            return;

        TickShootAnimTimer();
        PruneLiveProjectiles();

        if (isGrounded && !isBossHovering && (rb == null || rb.linearVelocity.y <= 0.1f))
            bossAirJumpsRemaining = 1;

        if (dashJumpCatchUpCooldownTimer > 0f)
            dashJumpCatchUpCooldownTimer -= BossDeltaTime;

        if (chargeCooldownTimer > 0f)
            chargeCooldownTimer -= BossDeltaTime;

        PlayerController player = FindPlayer();
        if (player == null || player.IsDead)
        {
            StopHorizontal();
            CancelAttackImmediate();
            state = AiState.Chase;
            return;
        }

        FaceToward(player.transform);
        UpdateAimFlag(player);

        if (state == AiState.Chase)
            TickPassiveAutoCharge(player);

        switch (state)
        {
            case AiState.Chase:
                TickChase(player);
                break;
            case AiState.Dash:
                TickDash(player);
                break;
            case AiState.DashJump:
                TickDashJump(player);
                break;
            case AiState.JumpPrep:
                TickJumpPrep(player);
                break;
            case AiState.ChargeWindup:
                TickChargeWindup(player);
                break;
            case AiState.ShootBurst:
                TickShootBurst(player);
                break;
            case AiState.Recover:
                TickRecover();
                break;
            case AiState.Hover:
                TickHoverPattern(player);
                break;
        }

        TickCopyBotDecoys(player);
    }

    protected override void HandleBossFixedUpdate()
    {
        if (copyBotDecoyMode)
        {
            TickCopyBotDecoyMovement();
            TickCopyBotAirStuckRecovery();
            return;
        }

        if (!usePlaceholderAi)
            return;

        if (state == AiState.Dash || state == AiState.DashJump)
        {
            ApplyDashVelocity();
            return;
        }

        if (state == AiState.Hover && rb != null)
        {
            ApplyHoverVelocity();
            return;
        }

        if (state == AiState.ChargeWindup && rb != null)
        {
            PlayerController chargePlayer = FindPlayer();
            if (chargeStartedInAir && chargePlayer != null && !isGrounded)
                MoveHorizontal(Mathf.Sign(chargePlayer.transform.position.x - transform.position.x));
            else if (chargePlayer != null && isGrounded)
            {
                float chargeDx = chargePlayer.transform.position.x - transform.position.x;
                float chargeDist = Mathf.Abs(chargeDx);
                if (chargeDist > preferredDistance)
                    MoveHorizontal(Mathf.Sign(chargeDx));
                else if (chargeDist < chaseStopDistance)
                    MoveHorizontal(-Mathf.Sign(chargeDx));
            }

            ApplyStayInFrameDrift(0.65f);
            return;
        }

        if ((state == AiState.ShootBurst || state == AiState.Recover) && rb != null)
        {
            ApplyStayInFrameDrift(1f);
            return;
        }

        if (state == AiState.JumpPrep && rb != null)
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            return;
        }

        if (state != AiState.Chase || isAttacking)
        {
            if (state != AiState.DashJump)
                StopHorizontal();
            return;
        }

        PlayerController player = FindPlayer();
        if (player == null)
        {
            StopHorizontal();
            return;
        }

        float dx = player.transform.position.x - transform.position.x;
        float dist = Mathf.Abs(dx);
        float stop = IsEnraged() ? chaseStopDistance * 0.65f : chaseStopDistance;

        float frameNudge = GetStayInFrameMoveSign();
        if (Mathf.Abs(frameNudge) > 0.01f && (IsOffCamera() || dist > midRange))
        {
            MoveHorizontal(frameNudge);
            return;
        }

        if (dist > farRange)
            MoveHorizontal(Mathf.Sign(dx));
        else if (dist > preferredDistance + 0.8f)
            MoveHorizontal(Mathf.Sign(dx));
        else if (dist < stop)
            MoveHorizontal(-Mathf.Sign(dx));
        else
            StopHorizontal();
    }

    protected override void UpdateAnimator()
    {
        base.UpdateAnimator();
        if (animator == null)
            return;

        animator.SetBool("IsShooting", shootAnimActive);
        animator.SetBool("AimDown", aimDown);
        animator.SetBool("IsHovering", isBossHovering);
        animator.SetBool("IsBoostedDashing", isDashing && bossBoostedDashActive);
    }

    protected override void ApplyFallMultiplier()
    {
        if (isBossHovering)
            return;
        base.ApplyFallMultiplier();
    }

    protected override float GetMoveSpeed()
    {
        float speed = base.GetMoveSpeed() * bossMovementSpeedScale;

        if (isCharging)
        {
            if (chargeTimer >= bigChargeSeconds)
                speed += bigChargeMoveSpeedBonus;
            else if (chargeTimer >= mediumChargeSeconds)
                speed += mediumChargeMoveSpeedBonus;
        }

        if (IsEnraged())
            speed += 2f;
        return speed;
    }

    protected override void TickPostHitVisuals(float dt)
    {
        RecordPoseHistory();
        UpdateDashAfterimages();

        if (isCharging && chargeTimer > 0.08f && showChargeAura)
            UpdateChargeAura();
        else
            SetChargeAuraVisible(false);
    }

    private bool IsEnraged()
    {
        return maxHealth > 0 && (float)currentHealth / maxHealth <= enrageHpFraction;
    }

    private float CurrentDecideInterval()
    {
        if (IsEnraged())
            return enrageDecideInterval;
        return Mathf.Lerp(decideIntervalLowHp, decideIntervalFullHp,
            (float)currentHealth / Mathf.Max(1, maxHealth));
    }

    private void TickChase(PlayerController player)
    {
        decideTimer -= BossDeltaTime;
        if (decideTimer > 0f)
            return;

        decideTimer = CurrentDecideInterval();
        PickAndStartPattern(player);
    }

    private void PickAndStartPattern(PlayerController player)
    {
        if (openingCycleActive)
        {
            if (openingIndex >= OpeningCycle.Length)
            {
                openingCycleActive = false;
                RebuildPatternRotation();
            }
            else
            {
                StartPattern(OpeningCycle[openingIndex++], player);
                return;
            }
        }

        if (IsEnraged() && Random.value < 0.45f)
        {
            Pattern pressure = Random.value < 0.5f ? Pattern.DashSlash : Pattern.ZeroRush;
            StartPattern(pressure, player);
            return;
        }

        if (ShouldDashJumpCatchUp(player))
        {
            StartPattern(Pattern.DashJumpCatchUp, player);
            return;
        }

        if (tryStayInFrame && IsOffCamera() && Random.value < 0.5f)
        {
            StartPattern(Random.value < 0.6f ? Pattern.DashSlash : Pattern.DashJumpCatchUp, player);
            return;
        }

        if (copyBotMode &&
            CanStartChargePattern() &&
            CanStartCopyBotBigShotPattern() &&
            Random.value < 0.38f)
        {
            StartPattern(Pattern.GrabHook, player);
            return;
        }

        StartPattern(TakeNextPatternFromRotation(), player);
    }

    private bool ShouldDashJumpCatchUp(PlayerController player)
    {
        if (player == null || dashJumpCatchUpCooldownTimer > 0f)
            return false;

        float dx = Mathf.Abs(player.transform.position.x - transform.position.x);
        if (dx <= midRange)
            return false;

        if (IsOffCamera())
            return dx >= midRange * 0.75f;

        return dx >= farRange * 0.85f && IsWithinMaxDashJumpRange(player);
    }

    private bool IsOffCamera()
    {
        Camera cam = Camera.main;
        if (cam == null)
            return false;

        Vector3 vp = cam.WorldToViewportPoint(transform.position);
        return vp.z < 0f || vp.x < -0.02f || vp.x > 1.02f || vp.y < -0.02f || vp.y > 1.02f;
    }

    private float GetStayInFrameMoveSign()
    {
        if (!tryStayInFrame)
            return 0f;

        Camera cam = Camera.main;
        if (cam == null)
            return 0f;

        Vector3 vp = cam.WorldToViewportPoint(transform.position);
        if (vp.z < 0f)
            return 0f;

        if (vp.x < frameEdgeViewportX || vp.x < 0f)
            return 1f;

        if (vp.x > 1f - frameEdgeViewportX || vp.x > 1f)
            return -1f;

        return 0f;
    }

    private void ApplyStayInFrameDrift(float strengthMul)
    {
        float nudge = GetStayInFrameMoveSign();
        if (Mathf.Abs(nudge) < 0.01f)
        {
            StopHorizontal();
            return;
        }

        DriftHorizontal(nudge, frameNudgeMoveSpeedMul * Mathf.Max(0.1f, strengthMul));
    }

    private void DriftHorizontal(float directionSign, float speedMul)
    {
        if (rb == null || isStunned || IsDead)
            return;

        float sign = Mathf.Sign(directionSign);
        if (Mathf.Abs(sign) < 0.01f)
            return;

        facingSign = sign;
        ApplyFacingVisual();
        wantsMoveAnim = true;
        rb.linearVelocity = new Vector2(sign * GetMoveSpeed() * Mathf.Clamp(speedMul, 0.15f, 1f), rb.linearVelocity.y);
    }

    private void TickCopyBotAirStuckRecovery()
    {
        if (!copyBotMode && !copyBotDecoyMode)
            return;

        if (isGrounded || state == AiState.Hover)
        {
            copyBotAirStuckTimer = 0f;
            return;
        }

        copyBotAirStuckTimer += BossDeltaTime;
        if (copyBotAirStuckTimer < CopyBotMaxAirStateSeconds)
            return;

        bool stuckAirState = state == AiState.DashJump ||
                             state == AiState.Dash ||
                             state == AiState.JumpPrep ||
                             state == AiState.ChargeWindup ||
                             isDashing ||
                             isBossDashJumping;

        if (!stuckAirState)
            return;

        CancelAttackImmediate();
        state = AiState.Chase;
        decideTimer = 0.1f;
        copyBotAirStuckTimer = 0f;
    }

    private Pattern TakeNextPatternFromRotation()
    {
        if (patternRotation.Count == 0 || rotationIndex >= patternRotation.Count)
            RebuildPatternRotation();

        Pattern pick = patternRotation[rotationIndex++];
        if (pick == lastUsedPattern)
            lastPatternStreak++;
        else
        {
            lastUsedPattern = pick;
            lastPatternStreak = 1;
        }

        return pick;
    }

    private void RebuildPatternRotation()
    {
        patternRotation.Clear();
        for (int i = 0; i < OpeningCycle.Length; i++)
            patternRotation.Add(OpeningCycle[i]);

        if (copyBotMode)
        {
            patternRotation.Add(Pattern.GrabHook);
            patternRotation.Add(Pattern.GrabHook);
        }

        for (int i = patternRotation.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            Pattern tmp = patternRotation[i];
            patternRotation[i] = patternRotation[j];
            patternRotation[j] = tmp;
        }

        rotationIndex = 0;
    }

    private void RollAttackStyle()
    {
        if (Random.value < 0.35f && attackStyle != BossKitAttackStyle.Normal)
            return;

        float r = Random.value;
        if (r < 0.45f)
            attackStyle = BossKitAttackStyle.Normal;
        else if (r < 0.75f)
            attackStyle = BossKitAttackStyle.SpreadShot;
        else
            attackStyle = BossKitAttackStyle.MachineGun;
    }

    private void StartPattern(Pattern pattern, PlayerController player)
    {
        activePattern = pattern;
        RollAttackStyle();

        switch (pattern)
        {
            case Pattern.ZeroRush:
                BeginShootBurst(ProjectileShotType.Small, shotsPerBurst);
                break;

            case Pattern.DashSlash:
                BeginDash(towardPlayer: true, player, 1f, DashFollowUp.ShootBurst);
                break;

            case Pattern.NeedleDash:
                BeginDash(towardPlayer: true, player, 1.05f, DashFollowUp.SingleShot);
                break;

            case Pattern.HornetDive:
                if (Mathf.Abs(player.transform.position.x - transform.position.x) > diveAttackRangeX)
                {
                    StartPattern(Pattern.DashSlash, player);
                    return;
                }

                BeginJumpPrep(JumpPrepGoal.HornetDive, player);
                break;

            case Pattern.DashJumpDive:
                if (!IsWithinMaxDashJumpRange(player))
                {
                    StartPattern(Pattern.DashSlash, player);
                    return;
                }

                BeginDashJump(player, DashJumpMode.DiveCut);
                break;

            case Pattern.DashJumpTrapDash:
                if (!CanDashJumpTrap(player))
                {
                    if (IsWithinMaxDashJumpRange(player))
                        BeginDashJump(player, DashJumpMode.DiveCut);
                    else
                        StartPattern(Pattern.DashSlash, player);
                    return;
                }

                BeginDashJump(player, DashJumpMode.TrapThenDash);
                break;

            case Pattern.DashJumpCatchUp:
                if (!IsWithinMaxDashJumpRange(player))
                {
                    StartPattern(Pattern.ZeroRush, player);
                    return;
                }

                BeginDashJump(player, DashJumpMode.CatchUp);
                break;

            case Pattern.RayHook:
                if (!CanStartChargePattern())
                {
                    StartPattern(Pattern.DashSlash, player);
                    return;
                }

                BeginChargeWindup(player, wantBig: false);
                break;

            case Pattern.GrabHook:
                if (!CanStartChargePattern() ||
                    (copyBotMode && !CanStartCopyBotBigShotPattern()))
                {
                    StartPattern(copyBotMode ? Pattern.RayHook : Pattern.NeedleDash, player);
                    return;
                }

                BeginChargeWindup(player, wantBig: true);
                break;

            case Pattern.AirGrappleChase:
                if (!CanStartChargePattern())
                {
                    StartPattern(Pattern.HornetDive, player);
                    return;
                }

                BeginChargeWindup(player, wantBig: false, airChase: true);
                break;

            case Pattern.AirGrabSlash:
                BeginJumpPrep(JumpPrepGoal.HornetDive, player);
                break;

            case Pattern.HoverBarrage:
                BeginHoverPattern(HoverMode.Barrage, player);
                break;

            case Pattern.HoverDashStrike:
                BeginHoverPattern(HoverMode.DashStrike, player);
                break;

            default:
                BeginShootBurst(ProjectileShotType.Small, shotsPerBurst);
                break;
        }
    }

    private void BeginShootBurst(ProjectileShotType type, int count)
    {
        SetChargingVisual(false);
        StopHorizontal();
        isAttacking = true;
        state = AiState.ShootBurst;
        burstShotType = type;
        shotsFiredInCurrentGroup = 0;
        burstPauseUntil = 0f;

        if (attackStyle == BossKitAttackStyle.MachineGun)
        {
            int requested = count > 0 ? count : machineGunMaxShots;
            shotsLeft = Mathf.Clamp(requested, 1, burstGroupMaxShots);
            burstShotType = ProjectileShotType.Small;
        }
        else if (attackStyle == BossKitAttackStyle.SpreadShot)
        {
            int requested = count > 0 ? count : shotsPerBurst;
            shotsLeft = Mathf.Clamp(requested, 1, spreadVolleyMaxCount);
        }
        else
        {
            shotsLeft = Mathf.Max(1, count > 0 ? count : shotsPerBurst);
        }

        nextShotTime = 0f;
    }

    private void TickShootBurst(PlayerController player)
    {
        if (Time.time < burstPauseUntil)
            return;

        if (Time.time < nextShotTime)
            return;

        if (shotsLeft <= 0)
        {
            FinishIntoRecover();
            return;
        }

        FireVolley(player, burstShotType);
        shotsLeft--;
        shotsFiredInCurrentGroup++;

        float interval = GetSpacingIntervalForCurrentBurst();
        bool needsGroupPause = ShouldPauseBetweenBurstGroups();

        if (needsGroupPause && shotsLeft > 0)
        {
            burstPauseUntil = Time.time + interval;
            shotsFiredInCurrentGroup = 0;
        }
        else
        {
            nextShotTime = Time.time + interval;
        }

        if (shotsLeft <= 0)
            FinishIntoRecover();
    }

    private bool ShouldPauseBetweenBurstGroups()
    {
        if (attackStyle == BossKitAttackStyle.SpreadShot)
            return true;

        if (attackStyle == BossKitAttackStyle.MachineGun)
            return shotsFiredInCurrentGroup >= burstGroupMaxShots;

        return burstShotType == ProjectileShotType.Big;
    }

    private float GetSpacingIntervalForCurrentBurst()
    {
        float shotSpeed = GetEffectiveProjectileSpeed(burstShotType);
        float spacingTime = attackSpacingWorldUnits / Mathf.Max(0.5f, shotSpeed);

        if (attackStyle == BossKitAttackStyle.MachineGun)
            return Mathf.Max(machineGunShotInterval, spacingTime);

        return Mathf.Max(timeBetweenShots, spacingTime);
    }

    private float GetEffectiveProjectileSpeed(ProjectileShotType type)
    {
        Projectile prefab = GetProjectilePrefab(type);
        if (prefab == null)
            return 10f;

        return prefab.Speed * Mathf.Clamp(projectileSpeedMultiplier, 0.4f, 1f);
    }

    private void FireVolley(PlayerController player, ProjectileShotType type)
    {
        if (attackStyle == BossKitAttackStyle.SpreadShot)
        {
            FireSingle(player, type, 0f, playSound: false);
            FireSingle(player, type, spreadShotAngleDegrees, playSound: false);
            FireSingle(player, type, -spreadShotAngleDegrees, playSound: false);
        }
        else
        {
            FireSingle(player, type, 0f, playSound: false);
        }

        PlayShotSound(type);
    }

    private void FireSingle(PlayerController player, ProjectileShotType type, float angleOffset, bool playSound = true,
        bool forceNormalStyle = false)
    {
        Projectile prefab = GetProjectilePrefab(type);
        if (prefab == null)
            return;

        if (copyBotMode && type == ProjectileShotType.Big)
        {
            PruneLiveProjectiles();
            if (GetLiveBigShotCount() >= CopyBotMaxLiveBigShots)
                return;
        }
        else
        {
            CapLiveProjectiles();
        }

        Vector2 origin = firePoint != null
            ? (Vector2)firePoint.position
            : (Vector2)transform.position + new Vector2(facingSign * 0.6f, 0.2f);

        Vector2 aimDir = GetAimDirection(player);
        Vector2 dir = aimDir;
        if (Mathf.Abs(angleOffset) > 0.01f)
            dir = RotateDirection(dir, angleOffset);

        Projectile shot = Instantiate(prefab, origin, Quaternion.identity);
        float shotSpeed = prefab.Speed * Mathf.Clamp(projectileSpeedMultiplier, 0.4f, 1f);
        shot.Launch(dir, shotSpeed, prefab.Damage, transform);
        liveProjectiles.Add(shot);

        Transform muzzle = firePoint != null ? firePoint : transform;
        GameVisualEffect blastPrefab = VisualEffects.ResolveBusterBlastPrefab(
            shot.ShotType,
            busterBlastSmallPrefab,
            busterBlastMediumBigPrefab);
        VisualEffects.SpawnBusterBlast(blastPrefab, muzzle, shot);

        if (playSound)
        {
            if (forceNormalStyle)
                SoundManager.Instance?.PlayBossKitFire(shot.ShotType);
            else
                PlayShotSound(shot.ShotType);
        }

        shootAnimActive = true;
        shootAnimTimer = shootAnimDuration;
        if (animator != null)
        {
            string trigger = aimDir.y < -0.3f ? "ShootDown" : aimDir.y > 0.3f ? "ShootUp" : "Shoot";
            animator.SetTrigger(trigger);
        }
    }

    private void PlayShotSound(ProjectileShotType type)
    {
        if (attackStyle == BossKitAttackStyle.MachineGun)
            SoundManager.Instance?.PlayBossKitFireRapid();
        else
            SoundManager.Instance?.PlayBossKitFire(type);
    }

    private Vector2 GetAimDirection(PlayerController player)
    {
        if (player == null)
            return new Vector2(facingSign, 0f);

        Vector2 origin = firePoint != null ? (Vector2)firePoint.position : (Vector2)transform.position;
        Vector2 toPlayer = (Vector2)player.transform.position - origin;

        if (Mathf.Abs(toPlayer.y) >= aimUpThreshold && toPlayer.y > 0f)
            return new Vector2(Mathf.Sign(facingSign) * 0.35f, 1f).normalized;

        if (ShouldAimDownAt(toPlayer))
            return new Vector2(Mathf.Sign(facingSign), -1f).normalized;

        return new Vector2(facingSign, 0f);
    }

    private bool ShouldAimDownAt(Vector2 toPlayer)
    {
        if (isGrounded && !isBossHovering)
            return false;

        float below = -toPlayer.y;
        if (below < aimDownThreshold)
            return false;

        return Mathf.Abs(toPlayer.x) <= below * 2f + 1.5f;
    }

    private static Vector2 RotateDirection(Vector2 dir, float degrees)
    {
        Vector2 d = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.right;
        float rad = degrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad);
        float sin = Mathf.Sin(rad);
        return new Vector2(d.x * cos - d.y * sin, d.x * sin + d.y * cos).normalized;
    }

    private Projectile GetProjectilePrefab(ProjectileShotType type)
    {
        switch (type)
        {
            case ProjectileShotType.Big:
                return bigProjectilePrefab != null ? bigProjectilePrefab : mediumProjectilePrefab;
            case ProjectileShotType.Medium:
                return mediumProjectilePrefab != null ? mediumProjectilePrefab : smallProjectilePrefab;
            default:
                return smallProjectilePrefab;
        }
    }

    private void CapLiveProjectiles()
    {
        PruneLiveProjectiles();
        int cap = Mathf.Max(4, maxLiveProjectiles);
        while (liveProjectiles.Count >= cap)
        {
            int removeIndex = -1;
            for (int i = 0; i < liveProjectiles.Count; i++)
            {
                Projectile candidate = liveProjectiles[i];
                if (candidate == null)
                {
                    removeIndex = i;
                    break;
                }

                if (copyBotMode && candidate.ShotType == ProjectileShotType.Big)
                    continue;

                removeIndex = i;
                break;
            }

            if (removeIndex < 0)
                break;

            Projectile oldest = liveProjectiles[removeIndex];
            liveProjectiles.RemoveAt(removeIndex);
            if (oldest != null)
                Destroy(oldest.gameObject);
        }
    }

    private int GetLiveBigShotCount()
    {
        PruneLiveProjectiles();
        int count = 0;
        for (int i = 0; i < liveProjectiles.Count; i++)
        {
            Projectile shot = liveProjectiles[i];
            if (shot != null && shot.ShotType == ProjectileShotType.Big)
                count++;
        }

        return count;
    }

    private bool CanStartCopyBotBigShotPattern()
    {
        return !copyBotMode || GetLiveBigShotCount() == 0;
    }

    private void TickCopyBotDecoys(PlayerController player)
    {
        if (!copyBotMode || copyBotDecoyMode || player == null || player.IsDead || CombatPaused)
            return;

        PruneLiveCopyBotDecoys();

        if (copyBotDecoyCooldownTimer > 0f)
        {
            copyBotDecoyCooldownTimer -= BossDeltaTime;
            return;
        }

        if (liveCopyBotDecoys.Count >= 2)
            return;

        BossSpawner spawner = GetBossSpawner();
        if (spawner == null)
            return;

        Vector3 spawnPos = transform.position + new Vector3(liveCopyBotDecoys.Count == 0 ? -3f : 3f, 0f, 0f);
        Boss decoy = spawner.SpawnCopyBotDecoyNear(spawnPos, 1);
        if (decoy == null)
            return;

        float darkerGray = Mathf.Clamp(copyBotDecoyGrayLevel - 0.12f, 0.08f, 1f);
        CopyBossAppearance.Apply(decoy, darkerGray);
        liveCopyBotDecoys.Add(decoy);
        copyBotDecoyCooldownTimer = 10f;

        decoy.RefreshPhaseCollisionsWithCopyBots();
        RefreshPhaseCollisionsWithCopyBots();
    }

    private void PruneLiveCopyBotDecoys()
    {
        for (int i = liveCopyBotDecoys.Count - 1; i >= 0; i--)
        {
            Boss decoy = liveCopyBotDecoys[i];
            if (decoy == null || decoy.IsDead)
                liveCopyBotDecoys.RemoveAt(i);
        }
    }

    private BossSpawner GetBossSpawner()
    {
        if (cachedBossSpawner == null)
            cachedBossSpawner = FindFirstObjectByType<BossSpawner>();

        return cachedBossSpawner;
    }

    private void TickCopyBotDecoyBehavior()
    {
        isAttacking = false;
        isCharging = false;
        isDashing = false;

        if (attackHitbox != null && attackHitbox.IsActive)
            attackHitbox.Deactivate();

        if (decoyContactStunCooldown > 0f)
            decoyContactStunCooldown -= BossDeltaTime;

        PlayerController player = FindPlayer();
        if (player == null || player.IsDead)
        {
            StopHorizontal();
            return;
        }

        FaceToward(player.transform);
        float dx = player.transform.position.x - transform.position.x;
        wantsMoveAnim = Mathf.Abs(dx) > 0.12f;
        TryCopyBotDecoyContactStun(player);
    }

    private void TickCopyBotDecoyMovement()
    {
        PlayerController player = FindPlayer();
        if (player == null || player.IsDead)
        {
            StopHorizontal();
            return;
        }

        float dx = player.transform.position.x - transform.position.x;
        float dist = Mathf.Abs(dx);

        if (dist > 0.12f)
            MoveHorizontal(Mathf.Sign(dx));
        else
            StopHorizontal();
    }

    private void TryCopyBotDecoyContactStun(PlayerController player)
    {
        if (player == null || decoyContactStunCooldown > 0f)
            return;

        Collider2D playerCol = player.GetComponent<Collider2D>();
        Collider2D myCol = bodyCollider != null ? bodyCollider : GetComponent<Collider2D>();
        if (playerCol == null || myCol == null)
            return;

        if (!myCol.IsTouching(playerCol))
            return;

        player.ApplyStunOnly(0.55f);
        decoyContactStunCooldown = 0.45f;
    }

    private void PruneLiveProjectiles()
    {
        for (int i = liveProjectiles.Count - 1; i >= 0; i--)
        {
            if (liveProjectiles[i] == null)
                liveProjectiles.RemoveAt(i);
        }
    }

    private void BeginDash(bool towardPlayer, PlayerController player, float speedMul, DashFollowUp followUp)
    {
        SetChargingVisual(false);
        dashDir = facingSign;
        if (towardPlayer && player != null)
        {
            float dx = player.transform.position.x - transform.position.x;
            if (Mathf.Abs(dx) > 0.05f)
                dashDir = Mathf.Sign(dx);
        }

        if (tryStayInFrame && IsOffCamera())
        {
            float frameNudge = GetStayInFrameMoveSign();
            if (Mathf.Abs(frameNudge) > 0.01f && Random.value < frameDashBiasChance)
                dashDir = frameNudge;
        }

        dashSpeedCurrent = dashSpeed * bossMovementSpeedScale * Mathf.Max(0.1f, speedMul);
        if (!isGrounded)
            dashSpeedCurrent *= airDashSpeedMul;
        float distance = dashDistance;
        bossBoostedDashActive = !pendingConvertToDashJump && IsAtMaxCharge();
        if (bossBoostedDashActive)
        {
            dashSpeedCurrent *= Mathf.Max(1f, boostedDashSpeedMultiplier);
            distance *= Mathf.Max(1f, boostedDashDistanceMultiplier);
        }

        dashSpeedCurrent = BossSpeed(dashSpeedCurrent);

        dashDuration = Mathf.Max(0.05f, distance / dashSpeedCurrent);
        dashTimer = 0f;
        isDashing = true;
        isAttacking = true;
        state = AiState.Dash;
        dashFollowUp = followUp;
        StopHorizontal();

        if (animator != null)
            animator.SetTrigger("Dash");

        BeginDashAfterimageTrail(dashDuration);

        if (rb != null && !pendingConvertToDashJump)
            rb.linearVelocity = new Vector2(dashDir * dashSpeedCurrent, rb.linearVelocity.y);
    }

    private void TickDash(PlayerController player)
    {
        dashTimer += BossDeltaTime;

        if (pendingConvertToDashJump)
        {
            if (dashTimer >= dashJumpConvertDelay || !isGrounded)
            {
                dashJumpStartX = transform.position.x;
                PlanDashJumpLanding(player, dashJumpMode, out float travel, out float targetX);
                dashJumpTravelTarget = travel;
                dashJumpTargetX = targetX;
                if (dashJumpMode == DashJumpMode.TrapThenDash)
                    dashJumpTrapX = targetX;
                PerformBossDashJumpImpulse();
            }

            return;
        }

        if (dashTimer < dashDuration)
            return;

        isDashing = false;
        ResolveDashFollowUp(player);
    }

    private void ResolveDashFollowUp(PlayerController player)
    {
        switch (dashFollowUp)
        {
            case DashFollowUp.ShootBurst:
                BeginShootBurst(ProjectileShotType.Small, shotsPerBurst);
                break;
            case DashFollowUp.SingleShot:
                BeginShootBurst(ProjectileShotType.Medium, 1);
                break;
            case DashFollowUp.Recover:
                FinishIntoRecover();
                break;
            default:
                isAttacking = false;
                state = AiState.Chase;
                decideTimer = 0.15f;
                break;
        }
    }

    private void BeginDashJump(PlayerController player, DashJumpMode mode)
    {
        SetChargingVisual(false);
        FaceToward(player != null ? player.transform : null);
        dashDir = facingSign;
        dashJumpMode = mode;
        dashJumpStartX = transform.position.x;
        dashJumpAirTime = 0f;

        PlanDashJumpLanding(player, mode, out float travel, out float targetX);
        dashJumpTravelTarget = travel;
        dashJumpTargetX = targetX;
        if (mode == DashJumpMode.TrapThenDash)
            dashJumpTrapX = targetX;

        isAttacking = true;

        if (isGrounded)
        {
            pendingConvertToDashJump = true;
            BeginDash(towardPlayer: true, player, speedMul: 1f, followUp: DashFollowUp.None);
            return;
        }

        PerformBossDashJumpImpulse();
    }

    private void PerformBossDashJumpImpulse()
    {
        pendingConvertToDashJump = false;
        isDashing = false;

        float maxSpeed = GetBossDashJumpHorizontalSpeed();
        float hop = GetBossDashJumpForce();
        float airTime = EstimateDashJumpAirTime();
        float neededSpeed = dashJumpTravelTarget / Mathf.Max(0.15f, airTime);
        float speed = Mathf.Clamp(neededSpeed, maxSpeed * 0.28f, maxSpeed);

        if (rb != null)
        {
            rb.linearVelocity = new Vector2(dashDir * BossSpeed(speed), hop);
            isGrounded = false;
        }

        dashSpeedCurrent = BossSpeed(speed);
        dashDuration = Mathf.Max(0.2f, airTime);
        dashTimer = 0f;
        isBossDashJumping = true;
        isAttacking = true;
        state = AiState.DashJump;
        dashJumpAirTime = 0f;

        BeginDashAfterimageTrail(Mathf.Max(0.55f, GetMaxAfterimageDelay()));

        if (animator != null)
            animator.SetTrigger("Jump");
    }

    private void TickDashJump(PlayerController player)
    {
        FaceToward(player != null ? player.transform : null);
        dashTimer += BossDeltaTime;

        float maxDashJumpSeconds = Mathf.Max(2.75f, dashDuration + 1.75f);
        if (copyBotMode && dashTimer >= maxDashJumpSeconds)
        {
            EndDashJumpImmediate();
            FinishIntoRecover();
            return;
        }

        if (!isGrounded)
            dashJumpAirTime += BossDeltaTime;

        bool enoughAir = dashJumpAirTime >= minAirTimeBeforeDashJumpAction;
        bool peaked = rb != null && rb.linearVelocity.y <= 0.85f && enoughAir;

        if (!enoughAir && isGrounded)
        {
            float hop = GetBossDashJumpForce();
            float speed = dashSpeedCurrent > 0.1f ? dashSpeedCurrent : GetBossDashJumpHorizontalSpeed();
            if (rb != null)
                rb.linearVelocity = new Vector2(dashDir * speed, hop);
            return;
        }

        switch (dashJumpMode)
        {
            case DashJumpMode.CatchUp:
                if (HasReachedDashJumpTargetX() && rb != null)
                    rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);

                if (!isGrounded || !enoughAir)
                    return;

                EndDashJumpImmediate();
                dashJumpCatchUpCooldownTimer = Mathf.Max(0.1f, dashJumpCatchUpCooldown);
                BeginShootBurst(ProjectileShotType.Medium, shotsPerBurst);
                break;

            case DashJumpMode.TrapThenDash:
            {
                bool pastTrap = HasReachedDashJumpTargetX() || (dashDir > 0f
                    ? transform.position.x >= dashJumpTrapX - 0.2f
                    : transform.position.x <= dashJumpTrapX + 0.2f);

                if (pastTrap && enoughAir)
                {
                    EndDashJumpImmediate();
                    BeginDash(towardPlayer: true, player, speedMul: 1.05f, DashFollowUp.ShootBurst);
                    return;
                }

                if (isGrounded && enoughAir)
                {
                    EndDashJumpImmediate();
                    BeginDash(towardPlayer: true, player, speedMul: 1.05f, DashFollowUp.ShootBurst);
                }

                break;
            }

            case DashJumpMode.DiveCut:
            default:
            {
                if (HasReachedDashJumpTargetX() && rb != null)
                    rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);

                bool inShootRange = player != null &&
                    Mathf.Abs(player.transform.position.x - transform.position.x) <= diveAttackRangeX;

                if (inShootRange && enoughAir && peaked)
                {
                    EndDashJumpImmediate();
                    BeginShootBurst(ProjectileShotType.Medium, 2);
                    break;
                }

                if (isGrounded && enoughAir)
                {
                    EndDashJumpImmediate();
                    if (player != null &&
                        Mathf.Abs(player.transform.position.x - transform.position.x) <= shootLandRange)
                        BeginShootBurst(ProjectileShotType.Medium, 2);
                    else
                        FinishIntoRecover();
                }

                break;
            }
        }
    }

    private void EndDashJumpImmediate()
    {
        isDashing = false;
        isBossDashJumping = false;
        pendingConvertToDashJump = false;
    }

    private void ApplyDashVelocity()
    {
        if (rb == null)
            return;

        float y = state == AiState.DashJump ? rb.linearVelocity.y : 0f;

        if (state == AiState.DashJump && HasReachedDashJumpTargetX())
        {
            rb.linearVelocity = new Vector2(0f, y);
            return;
        }

        rb.linearVelocity = new Vector2(dashDir * dashSpeedCurrent, y);
        facingSign = dashDir;
        ApplyFacingVisual();
    }

    private float GetBossDashJumpHorizontalSpeed()
    {
        float baseSpeed = dashJumpHorizontalSpeed > 0.01f ? dashJumpHorizontalSpeed : dashSpeed;
        return baseSpeed * Mathf.Max(0.1f, dashJumpSpeedMul);
    }

    private float GetBossDashJumpForce()
    {
        float baseForce = dashJumpForce > 0.01f ? dashJumpForce : jumpForce;
        return baseForce * Mathf.Max(0.1f, dashJumpHeightMul);
    }

    private float GetMaxDashJumpTravel()
    {
        float speed = GetBossDashJumpHorizontalSpeed();
        float up = GetBossDashJumpForce();
        float g = Mathf.Abs(Physics2D.gravity.y) * Mathf.Max(0.1f, rb != null ? rb.gravityScale : defaultGravityScale);
        float apex = up / g;
        float height = (up * up) / (2f * g);
        float fallT = Mathf.Sqrt(2f * height / (g * Mathf.Max(1f, fallMultiplier)));
        float travel = Mathf.Max(dashDistance, speed * (apex + fallT));
        return travel * Mathf.Max(0.1f, dashJumpDistanceMul);
    }

    private float EstimateDashJumpAirTime()
    {
        float up = GetBossDashJumpForce();
        float g = Mathf.Abs(Physics2D.gravity.y) * Mathf.Max(0.1f, rb != null ? rb.gravityScale : defaultGravityScale);
        float apex = up / Mathf.Max(0.01f, g);
        float height = (up * up) / (2f * Mathf.Max(0.01f, g));
        float fallT = Mathf.Sqrt(2f * height / (g * Mathf.Max(1f, fallMultiplier)));
        return Mathf.Max(0.2f, apex + fallT);
    }

    private bool IsWithinMaxDashJumpRange(PlayerController player)
    {
        if (player == null)
            return false;

        float dx = Mathf.Abs(player.transform.position.x - transform.position.x);
        float dy = Mathf.Abs(player.transform.position.y - transform.position.y);
        float max = GetMaxDashJumpTravel();
        return dx <= max && dy <= max * 0.85f;
    }

    private bool CanDashJumpTrap(PlayerController player)
    {
        if (player == null || !isGrounded)
            return false;

        float max = GetMaxDashJumpTravel();
        float dir = Mathf.Sign(player.transform.position.x - transform.position.x);
        if (Mathf.Abs(dir) < 0.01f)
            dir = facingSign;

        float trapX = player.transform.position.x + dir * dashJumpTrapLead;
        float needed = Mathf.Abs(trapX - transform.position.x);
        return needed >= 1f && needed <= max;
    }

    private float PickAssaultCloseGap()
    {
        float min = Mathf.Min(dashJumpAssaultCloseMin, dashJumpAssaultCloseMax);
        float max = Mathf.Max(dashJumpAssaultCloseMin, dashJumpAssaultCloseMax);
        return Random.Range(min, max);
    }

    private void PlanDashJumpLanding(PlayerController player, DashJumpMode mode, out float travel, out float targetX)
    {
        float maxTravel = GetMaxDashJumpTravel();
        float minTravel = Mathf.Max(0.35f, dashJumpMinTravel);
        float startX = transform.position.x;
        float dir = dashDir;

        if (player == null)
        {
            travel = Mathf.Min(maxTravel, Mathf.Max(minTravel, midRange));
            targetX = startX + dir * travel;
            return;
        }

        float playerX = player.transform.position.x;
        float dx = playerX - startX;
        if (Mathf.Abs(dx) > 0.05f)
            dir = Mathf.Sign(dx);

        dashDir = dir;

        if (mode == DashJumpMode.TrapThenDash)
        {
            targetX = playerX + dir * dashJumpTrapLead;
            travel = Mathf.Clamp(Mathf.Abs(targetX - startX), minTravel, maxTravel);
            targetX = startX + dir * travel;
            dashJumpTrapX = targetX;
            return;
        }

        float gap = PickAssaultCloseGap();
        float absDx = Mathf.Abs(dx);
        float desiredTravel = Mathf.Max(minTravel, absDx - gap);

        if (absDx <= gap + shootLandRange)
            desiredTravel = Mathf.Min(desiredTravel, Mathf.Max(minTravel, absDx * 0.55f));

        travel = Mathf.Clamp(desiredTravel, minTravel, maxTravel);
        targetX = startX + dir * travel;

        if (dir > 0f)
            targetX = Mathf.Min(targetX, playerX - Mathf.Min(gap, absDx * 0.5f));
        else
            targetX = Mathf.Max(targetX, playerX + Mathf.Min(gap, absDx * 0.5f));

        travel = Mathf.Clamp(Mathf.Abs(targetX - startX), minTravel, maxTravel);
        targetX = startX + dir * travel;
    }

    private bool HasReachedDashJumpTargetX()
    {
        if (dashDir > 0f)
            return transform.position.x >= dashJumpTargetX - 0.12f;
        return transform.position.x <= dashJumpTargetX + 0.12f;
    }

    private void BeginJumpPrep(JumpPrepGoal goal, PlayerController player)
    {
        jumpPrepGoal = goal;
        jumpPrepAirTime = 0f;
        jumpPrepHasLeftGround = false;
        jumpPrepDoubleJumped = false;
        stateTimer = jumpPrepTimeout;
        isAttacking = true;
        state = AiState.JumpPrep;
        StopHorizontal();
        ForceBossJump();
    }

    private void TickJumpPrep(PlayerController player)
    {
        FaceToward(player != null ? player.transform : null);
        stateTimer -= BossDeltaTime;

        if (!isGrounded || (rb != null && rb.linearVelocity.y > 0.5f))
        {
            jumpPrepHasLeftGround = true;
            jumpPrepAirTime += BossDeltaTime;
        }

        if (!jumpPrepHasLeftGround && isGrounded && stateTimer < jumpPrepTimeout - 0.05f)
            ForceBossJump();

        bool ready = jumpPrepHasLeftGround && jumpPrepAirTime >= minAirTimeBeforeAction;

        if (jumpPrepGoal == JumpPrepGoal.HornetDive && ready)
        {
            bool nearApex = rb == null || rb.linearVelocity.y <= 1.5f;

            if (nearApex && !jumpPrepDoubleJumped && bossAirJumpsRemaining > 0 && player != null &&
                player.transform.position.y - transform.position.y >= doubleJumpChaseHeight)
            {
                PerformBossDoubleJump();
                jumpPrepDoubleJumped = true;
                stateTimer = Mathf.Max(stateTimer, 0f) + 0.8f;
                return;
            }

            if (nearApex || (!jumpPrepDoubleJumped && jumpPrepAirTime >= minAirTimeBeforeAction * 2f))
            {
                ResolveJumpPrep(player);
                return;
            }
        }

        if (stateTimer <= 0f)
        {
            if (!jumpPrepHasLeftGround)
                ForceBossJump();
            ResolveJumpPrep(player);
        }
    }

    private void ResolveJumpPrep(PlayerController player)
    {
        BeginShootBurst(
            activePattern == Pattern.AirGrabSlash ? ProjectileShotType.Medium : ProjectileShotType.Small,
            activePattern == Pattern.AirGrabSlash ? 1 : 3);
    }

    private void ForceBossJump()
    {
        if (rb == null)
            return;

        Vector2 v = rb.linearVelocity;
        v.y = Mathf.Max(jumpForce, 10f);
        rb.linearVelocity = v;
        isGrounded = false;
    }

    private void PerformBossDoubleJump()
    {
        if (rb == null || bossAirJumpsRemaining <= 0)
            return;

        bossAirJumpsRemaining--;
        float force = doubleJumpForce > 0.01f ? doubleJumpForce : Mathf.Max(jumpForce, 10f);
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, force);
        isGrounded = false;

        if (animator != null && !shootAnimActive && !isDashing)
            animator.Play("Jump", 0, 0f);
    }

    // --- Hover patterns (double jump → hover, like playable Kit) ---

    private void BeginHoverPattern(HoverMode mode, PlayerController player)
    {
        SetChargingVisual(false);
        isAttacking = true;
        state = AiState.Hover;
        hoverMode = mode;
        hoverPatternTimer = 0f;
        hoverPhaseTimer = 0f;
        hoverHasLeftGround = !isGrounded;
        FaceToward(player != null ? player.transform : null);

        if (isGrounded)
        {
            hoverPhase = HoverPhase.GroundJump;
            ForceBossJump();
        }
        else if (bossAirJumpsRemaining > 0)
        {
            hoverPhase = HoverPhase.DoubleJump;
            PerformBossDoubleJump();
        }
        else
        {
            StartBossHover();
        }
    }

    private void TickHoverPattern(PlayerController player)
    {
        float dt = BossDeltaTime;
        hoverPatternTimer += dt;
        hoverPhaseTimer += dt;

        if (hoverPatternTimer >= hoverMaxSeconds)
        {
            EndHoverIntoRecover();
            return;
        }

        switch (hoverPhase)
        {
            case HoverPhase.GroundJump:
                if (!isGrounded || (rb != null && rb.linearVelocity.y > 0.5f))
                    hoverHasLeftGround = true;

                if (!hoverHasLeftGround)
                {
                    if (hoverPhaseTimer > 0.15f && isGrounded)
                        ForceBossJump();
                    return;
                }

                if ((rb != null && rb.linearVelocity.y <= 1.5f) || hoverPhaseTimer >= 0.9f)
                {
                    hoverPhase = HoverPhase.DoubleJump;
                    hoverPhaseTimer = 0f;
                    PerformBossDoubleJump();
                }
                break;

            case HoverPhase.DoubleJump:
                if (isGrounded && hoverPhaseTimer > 0.2f)
                {
                    EndHoverIntoRecover();
                    return;
                }

                if ((rb != null && rb.linearVelocity.y <= 0f) || hoverPhaseTimer >= 0.9f)
                    StartBossHover();
                break;

            case HoverPhase.Hovering:
                if (isGrounded && hoverPhaseTimer > 0.2f)
                {
                    EndHoverIntoRecover();
                    return;
                }

                if (hoverMode == HoverMode.DashStrike && hoverPhaseTimer >= hoverDashDelay)
                {
                    BeginHoverBoostedDash(player);
                    return;
                }

                if (Time.time >= nextHoverShotTime)
                {
                    FireVolley(player, ProjectileShotType.Small);
                    nextHoverShotTime = Time.time + (IsEnraged() ? hoverShotIntervalEnraged : hoverShotInterval);
                }
                break;

            case HoverPhase.HoverDash:
                dashTimer += dt;
                if (dashTimer < dashDuration)
                    return;

                isDashing = false;
                FaceToward(player != null ? player.transform : null);
                FireVolley(player, ProjectileShotType.Medium);
                EndHoverIntoRecover();
                break;
        }
    }

    private void StartBossHover()
    {
        if (rb == null || isStunned)
            return;

        isBossHovering = true;
        hoverPhase = HoverPhase.Hovering;
        hoverPhaseTimer = 0f;
        rb.gravityScale = 0f;
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f);
        nextHoverShotTime = Time.time + hoverFirstShotDelay;

        if (animator != null)
            animator.SetTrigger("Hover");
    }

    private void EndBossHover()
    {
        if (!isBossHovering)
            return;

        isBossHovering = false;
        if (rb != null)
            rb.gravityScale = defaultGravityScale;
        if (animator != null)
            animator.ResetTrigger("Hover");
    }

    private void EndHoverIntoRecover()
    {
        EndBossHover();
        FinishIntoRecover();
    }

    private void BeginHoverBoostedDash(PlayerController player)
    {
        dashDir = facingSign;
        if (player != null)
        {
            float dx = player.transform.position.x - transform.position.x;
            if (Mathf.Abs(dx) > 0.05f)
                dashDir = Mathf.Sign(dx);
        }

        dashSpeedCurrent = BossSpeed(dashSpeed * bossMovementSpeedScale * Mathf.Max(1f, boostedDashSpeedMultiplier));
        dashDuration = Mathf.Max(0.05f, dashDistance * Mathf.Max(1f, boostedDashDistanceMultiplier) / dashSpeedCurrent);
        dashTimer = 0f;
        isDashing = true;
        bossBoostedDashActive = true;
        hoverPhase = HoverPhase.HoverDash;
        hoverPhaseTimer = 0f;

        if (animator != null)
            animator.SetTrigger("Dash");

        BeginDashAfterimageTrail(dashDuration);
    }

    private void ApplyHoverVelocity()
    {
        PlayerController player = FindPlayer();

        switch (hoverPhase)
        {
            case HoverPhase.HoverDash:
                rb.linearVelocity = new Vector2(dashDir * dashSpeedCurrent, 0f);
                facingSign = dashDir;
                ApplyFacingVisual();
                return;

            case HoverPhase.Hovering:
            {
                float levelSeconds = IsEnraged() ? hoverLevelSecondsEnraged : hoverLevelSeconds;
                float vy = 0f;
                if (hoverMode == HoverMode.Barrage && hoverPhaseTimer >= levelSeconds)
                {
                    vy = Mathf.MoveTowards(rb.linearVelocity.y, -BossSpeed(hoverDescendSpeed),
                        hoverDescendAcceleration * Time.fixedDeltaTime);
                }

                rb.linearVelocity = new Vector2(GetHoverDriftVelocityX(player), vy);
                return;
            }

            default:
                rb.linearVelocity = new Vector2(GetHoverDriftVelocityX(player), rb.linearVelocity.y);
                return;
        }
    }

    /// <summary>Drifts toward a spot diagonally above the player so 45° down-shots line up.</summary>
    private float GetHoverDriftVelocityX(PlayerController player)
    {
        if (player == null)
            return 0f;

        Vector3 p = player.transform.position;
        float height = transform.position.y - p.y;
        float side = Mathf.Abs(transform.position.x - p.x) > 0.05f
            ? Mathf.Sign(transform.position.x - p.x)
            : -facingSign;
        float targetX = p.x + side * Mathf.Clamp(height, 1.5f, 4f);
        float diff = targetX - transform.position.x;
        if (Mathf.Abs(diff) < 0.25f)
            return 0f;

        return Mathf.Sign(diff) * GetMoveSpeed() * hoverDriftSpeedMul;
    }

    private bool IsAtMaxCharge()
    {
        return chargeTimer >= bigChargeSeconds && chargeCooldownTimer <= 0f;
    }

    private bool CanStartChargePattern()
    {
        return chargeCooldownTimer <= 0f;
    }

    private void TickPassiveAutoCharge(PlayerController player)
    {
        if (chargeCooldownTimer > 0f)
        {
            SetChargingVisual(false);
            return;
        }

        chargeTimer += BossDeltaTime;
        chargeTimer = Mathf.Min(chargeTimer, bigChargeSeconds);
        SetChargingVisual(chargeTimer > 0.08f);

        if (chargeTimer > 0.08f && Time.time >= nextChargePepperTime)
        {
            FirePepperShot(player);
            nextChargePepperTime = Time.time + chargePepperShotInterval;
        }
    }

    private void SetChargingVisual(bool charging)
    {
        isCharging = charging;
        UpdateChargeLoopAudio(charging);
        if (!charging)
            SetChargeAuraVisible(false);
    }

    private void UpdateChargeLoopAudio(bool shouldPlay)
    {
        if (shouldPlay && !chargeLoopActive)
        {
            SoundManager.Instance?.StartChargeLoop(SoundManager.ChargeLoopId.BossKit);
            chargeLoopActive = true;
        }
        else if (!shouldPlay && chargeLoopActive)
        {
            SoundManager.Instance?.StopChargeLoop();
            chargeLoopActive = false;
        }
    }

    private void FirePepperShot(PlayerController player)
    {
        FireSingle(player, ProjectileShotType.Small, 0f, playSound: true, forceNormalStyle: true);
    }

    private void BeginChargeWindup(PlayerController player, bool wantBig, bool airChase = false)
    {
        chargeWantsBig = wantBig;
        chargeStartedInAir = airChase || !isGrounded;
        isAttacking = true;
        state = AiState.ChargeWindup;

        float minHold = wantBig ? bigChargeSeconds : mediumChargeSeconds;
        chargeReleaseAt = Time.time + Random.Range(
            Mathf.Max(minHold, aiChargeHoldMin),
            Mathf.Max(aiChargeHoldMin, aiChargeHoldMax));

        SetChargingVisual(chargeTimer > 0.08f);
        FaceToward(player != null ? player.transform : null);
    }

    private void TickChargeWindup(PlayerController player)
    {
        FaceToward(player != null ? player.transform : null);

        if (chargeCooldownTimer <= 0f)
        {
            chargeTimer += BossDeltaTime;
            chargeTimer = Mathf.Min(chargeTimer, bigChargeSeconds);
        }

        SetChargingVisual(chargeTimer > 0.08f);

        if (chargeTimer > 0.08f && Time.time >= nextChargePepperTime)
        {
            FirePepperShot(player);
            nextChargePepperTime = Time.time + chargePepperShotInterval;
        }

        bool ready = chargeTimer >= (chargeWantsBig ? bigChargeSeconds : mediumChargeSeconds) &&
                     Time.time >= chargeReleaseAt;

        if (!ready && chargeTimer < bigChargeSeconds + 0.5f)
            return;

        ReleaseChargeShot();
    }

    private void ReleaseChargeShot()
    {
        UpdateChargeLoopAudio(false);
        isCharging = false;
        SetChargeAuraVisible(false);

        ProjectileShotType type;
        if (chargeTimer >= bigChargeSeconds)
            type = ProjectileShotType.Big;
        else if (chargeTimer >= mediumChargeSeconds)
            type = ProjectileShotType.Medium;
        else
            type = ProjectileShotType.Small;

        if (type != ProjectileShotType.Small)
            chargeCooldownTimer = chargeCooldownSeconds;

        chargeTimer = 0f;
        int burstCount = 2;
        if (type == ProjectileShotType.Big)
            burstCount = copyBotMode ? CopyBotMaxLiveBigShots : 1;
        BeginShootBurst(type, burstCount);
    }

    private void FinishIntoRecover()
    {
        EndBossHover();
        isAttacking = false;
        isDashing = false;
        isCharging = false;
        isBossDashJumping = false;
        pendingConvertToDashJump = false;
        state = AiState.Recover;
        burstPauseUntil = 0f;
        shotsFiredInCurrentGroup = 0;
        float minBreak = Mathf.Min(attackBreakMinSeconds, attackBreakMaxSeconds);
        float maxBreak = Mathf.Max(attackBreakMinSeconds, attackBreakMaxSeconds);
        recoverTimer = Random.Range(minBreak, maxBreak);
        if (IsEnraged())
            recoverTimer = Mathf.Min(recoverTimer, recoverDurationEnraged);
        StopHorizontal();
    }

    private void TickRecover()
    {
        recoverTimer -= BossDeltaTime;
        if (recoverTimer > 0f)
            return;

        state = AiState.Chase;
        decideTimer = 0.1f;
    }

    private void TickShootAnimTimer()
    {
        if (shootAnimTimer <= 0f)
            return;

        shootAnimTimer -= BossDeltaTime;
        if (shootAnimTimer <= 0f)
        {
            shootAnimTimer = 0f;
            shootAnimActive = false;
        }
    }

    private void UpdateAimFlag(PlayerController player)
    {
        if (player == null)
        {
            aimUp = false;
            aimDown = false;
            return;
        }

        Vector2 toPlayer = (Vector2)(player.transform.position - transform.position);
        aimUp = toPlayer.y >= aimUpThreshold;
        aimDown = !aimUp && ShouldAimDownAt(toPlayer);
    }

    private void CancelAttackImmediate()
    {
        EndBossHover();
        isAttacking = false;
        isDashing = false;
        isCharging = false;
        isBossDashJumping = false;
        pendingConvertToDashJump = false;
        shotsLeft = 0;
        shootAnimActive = false;
        shootAnimTimer = 0f;
        chargeTimer = 0f;
        UpdateChargeLoopAudio(false);
        SetChargeAuraVisible(false);
        SoundManager.Instance?.StopChargeLoopImmediate();

        StopHorizontal();
    }

    // --- Charge aura (Kit pink — matches playable Kit / Malice boss pipeline) ---

    private void SetupChargeAura()
    {
        if (!showChargeAura || spriteRenderer == null)
            return;

        if (chargeAuraObject != null)
            Destroy(chargeAuraObject);
        if (chargeAuraMaterial != null)
            Destroy(chargeAuraMaterial);

        chargeAuraObject = new GameObject($"{name}_ChargeAura");
        chargeAuraObject.transform.SetParent(transform, false);
        chargeAuraObject.transform.localPosition = Vector3.zero;
        chargeAuraObject.transform.localScale = Vector3.one * auraBaseScale;
        chargeAuraObject.transform.SetAsFirstSibling();

        chargeAuraRenderer = chargeAuraObject.AddComponent<SpriteRenderer>();
        chargeAuraRenderer.sprite = spriteRenderer.sprite;
        CharacterEffectSorting.ApplyAuraBehindBody(chargeAuraRenderer, spriteRenderer, EffectSortingGroup);
        chargeAuraRenderer.flipX = spriteRenderer.flipX;

        Shader solidShader = Shader.Find("Gameoverse/SpriteSolidColor");
        if (solidShader != null)
        {
            chargeAuraMaterial = new Material(solidShader);
            chargeAuraRenderer.sharedMaterial = chargeAuraMaterial;
        }

        Color c = chargeAuraColor;
        c.a = 0f;
        chargeAuraRenderer.color = c;
        chargeAuraObject.SetActive(false);
    }

    private void UpdateChargeAura()
    {
        if (!showChargeAura)
            return;

        if (chargeAuraRenderer == null || chargeAuraObject == null)
            SetupChargeAura();

        if (chargeAuraRenderer == null || spriteRenderer == null)
            return;

        float charge01 = Mathf.Clamp01(chargeTimer / Mathf.Max(0.01f, bigChargeSeconds));

        chargeAuraObject.SetActive(true);
        chargeAuraRenderer.sprite = spriteRenderer.sprite;
        chargeAuraRenderer.flipX = spriteRenderer.flipX;
        CharacterEffectSorting.ApplyAuraBehindBody(chargeAuraRenderer, spriteRenderer, EffectSortingGroup);

        float alpha;
        if (chargeTimer >= bigChargeSeconds)
            alpha = auraAlphaAtBig;
        else if (chargeTimer >= mediumChargeSeconds)
            alpha = Mathf.Lerp(auraAlphaAtMedium, auraAlphaAtBig,
                Mathf.InverseLerp(mediumChargeSeconds, bigChargeSeconds, chargeTimer));
        else
            alpha = Mathf.Lerp(auraAlphaStart, auraAlphaAtMedium,
                chargeTimer / Mathf.Max(0.01f, mediumChargeSeconds));

        Color pink = Color.Lerp(chargeAuraColor, chargeAuraStrongColor, charge01);
        float flickerSpeed = Mathf.Lerp(auraFlickerSpeed, Mathf.Min(auraFlickerSpeed * 1.75f, 2.4f), charge01);
        auraFlickerPhase += BossDeltaTime * flickerSpeed;
        float shimmer = 0.5f + 0.5f * Mathf.Sin(auraFlickerPhase * Mathf.PI * 2f);
        float yellowMix = shimmer * auraFlickerStrength * Mathf.Lerp(0.55f, 1f, charge01);
        Color auraColor = Color.Lerp(pink, chargeAuraFlickerColor, yellowMix);
        auraColor.a = alpha;
        chargeAuraRenderer.color = auraColor;

        float pulse = 1f + Mathf.Sin(Time.time * auraPulseSpeed) * auraPulseAmount * charge01;
        chargeAuraObject.transform.localScale = Vector3.one * (auraBaseScale * pulse);
    }

    private void SetChargeAuraVisible(bool visible)
    {
        if (chargeAuraObject == null)
            return;

        if (!visible)
        {
            chargeAuraObject.SetActive(false);
            auraFlickerPhase = 0f;
        }
    }

    // --- Dash afterimages (player / Malice boss parity) ---

    public void SetCopyBotAfterimageStyle(float grayLevel)
    {
        grayLevel = Mathf.Clamp(grayLevel, 0.05f, 1f);
        copyBotDecoyGrayLevel = grayLevel;
        dashAfterimageColor = new Color(grayLevel, grayLevel, grayLevel, 1f);
    }

    /// <summary>Copy-bot boss fight: more big shots, decoy copies, big-shot cap.</summary>
    public void EnableCopyBotMode()
    {
        copyBotMode = true;
    }

    /// <summary>1-HP decoy: chase + stun only.</summary>
    public void EnableCopyBotDecoyMode()
    {
        copyBotDecoyMode = true;
        copyBotMode = false;
        usePlaceholderAi = false;
        isAttacking = false;
        isCharging = false;
        isDashing = false;

        if (attackHitbox != null)
            attackHitbox.Deactivate();
    }

    private void SetupDashAfterimages()
    {
        DestroyDashAfterimages();
        if (!enableDashAfterimages || spriteRenderer == null)
            return;

        int count = Mathf.Max(1, dashAfterimageCount);
        dashAfterimageObjects = new GameObject[count];
        dashAfterimageRenderers = new SpriteRenderer[count];

        for (int i = 0; i < count; i++)
        {
            var go = new GameObject($"{name}_DashAfterimage_{i + 1}");
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            CharacterEffectSorting.ApplyTrailBehindBody(sr, spriteRenderer, EffectSortingGroup, i);

            float t = count == 1 ? 0f : i / (float)(count - 1);
            Color c = dashAfterimageColor;
            c.a = Mathf.Lerp(dashAfterimageAlphaStart, dashAfterimageAlphaEnd, t);
            sr.color = c;
            go.SetActive(false);
            dashAfterimageObjects[i] = go;
            dashAfterimageRenderers[i] = sr;
        }
    }

    private void DestroyDashAfterimages()
    {
        if (dashAfterimageObjects == null)
            return;

        for (int i = 0; i < dashAfterimageObjects.Length; i++)
        {
            if (dashAfterimageObjects[i] != null)
                Destroy(dashAfterimageObjects[i]);
        }

        dashAfterimageObjects = null;
        dashAfterimageRenderers = null;
    }

    private void BeginDashAfterimageTrail(float activeDuration)
    {
        if (!enableDashAfterimages || dashAfterimageRenderers == null)
            return;

        float keep = Mathf.Max(activeDuration, GetMaxAfterimageDelay()) + dashAfterimageLifetimePadding;
        dashAfterimagesVisibleUntil = Time.time + keep;
        SetDashAfterimagesActive(true);
    }

    private float GetAfterimageDelay(int index)
    {
        return Mathf.Max(0.01f, dashAfterimageSpacing) * (index + 1);
    }

    private float GetMaxAfterimageDelay()
    {
        return GetAfterimageDelay(Mathf.Max(1, dashAfterimageCount) - 1);
    }

    private void RecordPoseHistory()
    {
        if (!enableDashAfterimages || spriteRenderer == null)
            return;

        poseHistory.Add(new PoseSample
        {
            time = Time.time,
            position = transform.position,
            sprite = spriteRenderer.sprite,
            flipX = spriteRenderer.flipX
        });

        float cutoff = Time.time - (GetMaxAfterimageDelay() + 0.35f);
        while (poseHistory.Count > 0 && poseHistory[0].time < cutoff)
            poseHistory.RemoveAt(0);
    }

    private bool TrySamplePose(float delay, out PoseSample sample)
    {
        sample = default;
        if (poseHistory.Count == 0)
            return false;

        float targetTime = Time.time - delay;
        for (int i = poseHistory.Count - 1; i >= 0; i--)
        {
            if (poseHistory[i].time <= targetTime)
            {
                sample = poseHistory[i];
                return true;
            }
        }

        sample = poseHistory[0];
        return true;
    }

    private void UpdateDashAfterimages()
    {
        if (!enableDashAfterimages || dashAfterimageRenderers == null || spriteRenderer == null)
            return;

        bool trailActive = isDashing || isBossDashJumping || Time.time <= dashAfterimagesVisibleUntil;
        if (!trailActive)
        {
            SetDashAfterimagesActive(false);
            return;
        }

        if (isDashing || isBossDashJumping)
        {
            dashAfterimagesVisibleUntil = Mathf.Max(
                dashAfterimagesVisibleUntil,
                Time.time + GetMaxAfterimageDelay() + dashAfterimageLifetimePadding);
        }

        for (int i = 0; i < dashAfterimageRenderers.Length; i++)
        {
            if (!TrySamplePose(GetAfterimageDelay(i), out PoseSample sample))
            {
                dashAfterimageObjects[i].SetActive(false);
                continue;
            }

            dashAfterimageObjects[i].SetActive(true);
            dashAfterimageObjects[i].transform.position = sample.position;
            dashAfterimageRenderers[i].sprite = sample.sprite != null ? sample.sprite : spriteRenderer.sprite;
            dashAfterimageRenderers[i].flipX = sample.flipX;
            CharacterEffectSorting.ApplyTrailBehindBody(
                dashAfterimageRenderers[i],
                spriteRenderer,
                EffectSortingGroup,
                i);

            float t = dashAfterimageRenderers.Length == 1
                ? 0f
                : i / (float)(dashAfterimageRenderers.Length - 1);
            Color c = dashAfterimageColor;
            c.a = Mathf.Lerp(dashAfterimageAlphaStart, dashAfterimageAlphaEnd, t);
            dashAfterimageRenderers[i].color = c;
        }
    }

    private void SetDashAfterimagesActive(bool active)
    {
        if (dashAfterimageObjects == null)
            return;

        for (int i = 0; i < dashAfterimageObjects.Length; i++)
        {
            if (dashAfterimageObjects[i] != null)
                dashAfterimageObjects[i].SetActive(active);
        }
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        preferredDistance = Mathf.Max(1f, preferredDistance);
        chaseStopDistance = Mathf.Max(0.5f, chaseStopDistance);
        midRange = Mathf.Max(chaseStopDistance, midRange);
        farRange = Mathf.Max(midRange, farRange);
        dashDistance = Mathf.Max(0.1f, dashDistance);
        dashSpeed = Mathf.Max(0.1f, dashSpeed);
        shotsPerBurst = Mathf.Max(1, shotsPerBurst);
        machineGunMaxShots = Mathf.Clamp(machineGunMaxShots, 1, burstGroupMaxShots);
        burstGroupMaxShots = Mathf.Clamp(burstGroupMaxShots, 1, 8);
        spreadVolleyMaxCount = Mathf.Clamp(spreadVolleyMaxCount, 1, 6);
        attackSpacingWorldUnits = Mathf.Max(0.5f, attackSpacingWorldUnits);
        attackBreakMinSeconds = Mathf.Max(0.1f, attackBreakMinSeconds);
        attackBreakMaxSeconds = Mathf.Max(attackBreakMinSeconds, attackBreakMaxSeconds);
        bossMovementSpeedScale = Mathf.Clamp(bossMovementSpeedScale, 0.85f, 1f);
        machineGunShotInterval = Mathf.Max(0.08f, machineGunShotInterval);
        timeBetweenShots = Mathf.Max(0.06f, timeBetweenShots);
        maxLiveProjectiles = Mathf.Max(4, maxLiveProjectiles);
        projectileSpeedMultiplier = Mathf.Clamp(projectileSpeedMultiplier, 0.4f, 1f);
        spreadShotAngleDegrees = Mathf.Clamp(spreadShotAngleDegrees, 10f, 75f);
        enrageHpFraction = Mathf.Clamp(enrageHpFraction, 0.05f, 0.95f);
        dashJumpTrapLead = Mathf.Max(0.5f, dashJumpTrapLead);
        dashJumpAssaultCloseMin = Mathf.Max(0.25f, dashJumpAssaultCloseMin);
        dashJumpAssaultCloseMax = Mathf.Max(dashJumpAssaultCloseMin, dashJumpAssaultCloseMax);
        dashJumpMinTravel = Mathf.Max(0.25f, dashJumpMinTravel);
        dashJumpCatchUpCooldown = Mathf.Max(0.05f, dashJumpCatchUpCooldown);
        recoverDurationEnraged = Mathf.Max(0.15f, recoverDurationEnraged);
        shootLandRange = Mathf.Max(1f, shootLandRange);
        chargeCooldownSeconds = Mathf.Max(0.5f, chargeCooldownSeconds);
        chargePepperShotInterval = Mathf.Max(0.2f, chargePepperShotInterval);
        dashAfterimageCount = Mathf.Max(1, dashAfterimageCount);
        dashAfterimageSpacing = Mathf.Max(0.01f, dashAfterimageSpacing);
        dashAfterimageLifetimePadding = Mathf.Max(0f, dashAfterimageLifetimePadding);
        auraBaseScale = Mathf.Max(0.5f, auraBaseScale);
        frameEdgeViewportX = Mathf.Clamp(frameEdgeViewportX, 0.05f, 0.4f);
        frameNudgeMoveSpeedMul = Mathf.Clamp(frameNudgeMoveSpeedMul, 0.2f, 1f);
        frameDashBiasChance = Mathf.Clamp01(frameDashBiasChance);
        doubleJumpForce = Mathf.Max(0f, doubleJumpForce);
        doubleJumpChaseHeight = Mathf.Max(0.25f, doubleJumpChaseHeight);
        hoverLevelSeconds = Mathf.Max(0.2f, hoverLevelSeconds);
        hoverLevelSecondsEnraged = Mathf.Max(0.2f, hoverLevelSecondsEnraged);
        hoverDescendSpeed = Mathf.Max(0.1f, hoverDescendSpeed);
        hoverDescendAcceleration = Mathf.Max(0.1f, hoverDescendAcceleration);
        hoverMaxSeconds = Mathf.Max(1f, hoverMaxSeconds);
        hoverShotInterval = Mathf.Max(0.15f, hoverShotInterval);
        hoverShotIntervalEnraged = Mathf.Max(0.15f, hoverShotIntervalEnraged);
        hoverFirstShotDelay = Mathf.Max(0f, hoverFirstShotDelay);
        hoverDashDelay = Mathf.Max(0.1f, hoverDashDelay);
        aimDownThreshold = Mathf.Max(0.25f, aimDownThreshold);
        boostedDashSpeedMultiplier = Mathf.Max(1f, boostedDashSpeedMultiplier);
        boostedDashDistanceMultiplier = Mathf.Max(1f, boostedDashDistanceMultiplier);
    }
#endif
}
