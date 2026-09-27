using UnityEngine;

/// <summary>
/// Boss version of Malice. PLACEHOLDER multi-pattern AI for feel-testing:
/// Zero rush, Hornet dive, Ray grapple hook, dash-slash, dash-jump→dive,
/// and Metal Sonic–style triple air dashes. Enrage kicks in at ≤50% HP.
/// Replace when final Boss Malice patterns are locked.
/// </summary>
public class BossMalice : Boss
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
        MetalSonicTriDash = 7,
        AirGrappleChase = 8,
        DashJumpTrapDash = 9,
        DashJumpCatchUp = 10,
        AirGrabSlash = 11
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
        ApproachForSlash,
        JumpPrep,
        ChargeWindup,
        Slash1,
        Slash2,
        Slash3,
        AirSlash,
        Grapple,
        Dash,
        DashJump,
        AirHang,
        Dive,
        Recover
    }

    [Header("Boss Malice - Placeholder AI (temporary)")]
    [Tooltip("Feel-test AI. Turn off when real patterns replace this.")]
    [SerializeField] private bool usePlaceholderAi = true;

    [Header("Spacing")]
    [SerializeField] private float chaseStopDistance = 1.6f;
    [SerializeField] private float closeRange = 2.2f;
    [Tooltip("Ground slash combo only starts when the player is within this X distance.")]
    [SerializeField] private float slashAttackRange = 2.15f;
    [Tooltip("If she cannot close into slash range by this time, she dash-slashes instead of whiffing.")]
    [SerializeField] private float slashApproachTimeout = 1.75f;
    [SerializeField] private float midRange = 4.5f;
    [SerializeField] private float farRange = 7.5f;
    [SerializeField] private float decideIntervalFullHp = 0.55f;
    [SerializeField] private float decideIntervalLowHp = 0.18f;

    [Header("Slash")]
    [SerializeField] private float slash1Duration = 0.46f;
    [SerializeField] private float slash2Duration = 0.46f;
    [SerializeField] private float slash3Duration = 0.5f;
    [SerializeField] private float airSlashDuration = 0.34f;
    [SerializeField] private float slashHitboxDelay = 0.06f;
    [SerializeField] private float slashHitboxActiveTime = 0.14f;
    [SerializeField] private int slash1Damage = 1;
    [SerializeField] private int slash2Damage = 1;
    [SerializeField] private int slash3Damage = 2;
    [SerializeField] private int airSlashDamage = 1;
    [SerializeField] private float groundSlashStep = 1f;
    [Tooltip("How quickly the ground-slash slide covers its step (units/sec).")]
    [SerializeField] private float groundSlashSlideSpeed = 9f;
    [SerializeField] private float dashCancelSlashSpeedMul = 1.5f;
    [SerializeField] private float airSlashDiveSpeed = 22f;

    [Header("Boss Malice - Projectile Slashes")]
    [SerializeField] private MaliceSlashProjectileSettings slashProjectiles = new MaliceSlashProjectileSettings();

    [Header("Boss Malice - Shot Defend (bonus)")]
    [Tooltip("Start a short parry burst mainly when this many live player small shots are on screen.")]
    [SerializeField] private int shotDefendMinSmallShots = 8;
    [SerializeField] private float shotDefendMaxSeconds = 2.5f;
    [Tooltip("Chance to start a burst when the shot threshold is met (kept low — bonus, not a new mode).")]
    [SerializeField] [Range(0.05f, 0.5f)] private float shotDefendChance = 0.22f;
    [Tooltip("Minimum wait after a check (pass or fail) before another roll.")]
    [SerializeField] private float shotDefendCheckCooldown = 10f;
    [Tooltip("Extra wait after a burst actually happens.")]
    [SerializeField] private float shotDefendAfterBurstCooldown = 16f;
    [Tooltip("Player must be within this X range for Hornet dive / dive air slash.")]
    [SerializeField] private float diveAttackRangeX = 4.25f;
    [Tooltip("Spaces (world units) past the player to land when trapping with a dash-jump.")]
    [SerializeField] private float dashJumpTrapLead = 2f;
    [Tooltip("Land this many units short of the player on catch-up / assault dash-jumps (min).")]
    [SerializeField] private float dashJumpAssaultCloseMin = 1f;
    [Tooltip("Land this many units short of the player on catch-up / assault dash-jumps (max).")]
    [SerializeField] private float dashJumpAssaultCloseMax = 2f;
    [Tooltip("Never plan a dash-jump shorter than this.")]
    [SerializeField] private float dashJumpMinTravel = 1f;
    [Tooltip("After a catch-up hop, wait this long before forcing another (stops overshoot loops).")]
    [SerializeField] private float dashJumpCatchUpCooldown = 0.55f;
    [Tooltip("Soft ease-in/out window at the start/end of ground dashes (seconds).")]
    [SerializeField] private float dashEdgeEaseSeconds = 0.1f;
    [SerializeField] private float grapplePullSpeed = 18f;
    [SerializeField] private float grapplePullArriveDistance = 0.03f;
    [SerializeField] private int grapplePullAnchorFrame = 8;
    [SerializeField] private int grappleAirPullAnchorFrame = 8;
    [SerializeField] private float grapplePullAnchorFrameRateFallback = 60f;

    [Header("Grapple / Grab (Ray Hook)")]
    [SerializeField] private float grappleChargeTelegraph = 0.35f;
    [SerializeField] private float grappleDuration = 0.7f;
    [SerializeField] private float grappleHitboxDelay = 0.08f;
    [SerializeField] private float grappleHitboxActiveTime = 0.22f;
    [SerializeField] private int grappleDamage = 2;
    [SerializeField] private int grapplePartialDamage = 1;
    [SerializeField] private int grappleMaxDamage = 3;
    [SerializeField] private float grabWeightInRotation = 2f;

    [Header("Charge (Kit/Malice-style)")]
    [SerializeField] private float mediumChargeSeconds = 1.5f;
    [SerializeField] private float bigChargeSeconds = 3f;
    [SerializeField] private float auraDelayAfterPress = 0.2f;
    [SerializeField] private float mediumChargeMoveSpeedBonus = 3f;
    [SerializeField] private float bigChargeMoveSpeedBonus = 6f;
    [Tooltip("How long AI holds charge before releasing (clamped to medium→big).")]
    [SerializeField] private float aiChargeHoldMin = 1.55f;
    [SerializeField] private float aiChargeHoldMax = 2.85f;
    [SerializeField] private float airGrappleChaseTimeout = 2.2f;

    [Header("Purple Charge Aura")]
    [SerializeField] private bool showChargeAura = true;
    [SerializeField] private Color chargeAuraColor = new Color(0.35f, 0.08f, 0.45f, 1f);
    [SerializeField] private Color chargeAuraStrongColor = new Color(0.22f, 0.02f, 0.35f, 1f);
    [SerializeField] private Color chargeAuraFlickerColor = new Color(0.72f, 0.35f, 1f, 1f);
    [SerializeField] private float auraBaseScale = 1.18f;
    [SerializeField] private float auraPulseAmount = 0.05f;
    [SerializeField] private float auraPulseSpeed = 6f;
    [SerializeField] private float auraFlickerSpeed = 1.25f;
    [SerializeField] [Range(0.05f, 0.5f)] private float auraFlickerStrength = 0.28f;
    [SerializeField] [Range(0f, 1f)] private float auraAlphaStart = 0.25f;
    [SerializeField] [Range(0f, 1f)] private float auraAlphaAtMedium = 0.65f;
    [SerializeField] [Range(0f, 1f)] private float auraAlphaAtBig = 0.95f;
    [Tooltip("Unused — aura now matches the character sorting plane so backgrounds cannot cover it.")]
    [SerializeField] private int auraSortingBehind = 2;

    [Header("Dash Afterimages (match playable Malice)")]
    [SerializeField] private bool enableDashAfterimages = true;
    [SerializeField] private int dashAfterimageCount = 3;
    [SerializeField] private float dashAfterimageSpacing = 0.1f;
    [SerializeField] private Color dashAfterimageColor = Color.white;
    [SerializeField] [Range(0f, 1f)] private float dashAfterimageAlphaStart = 0.55f;
    [SerializeField] [Range(0f, 1f)] private float dashAfterimageAlphaEnd = 0.15f;
    [SerializeField] private float dashAfterimageLifetimePadding = 0.15f;
    [Tooltip("Grounded dash runs this long before converting into a dash-jump (player-style).")]
    [SerializeField] private float dashJumpConvertDelay = 0.06f;
    [SerializeField] private float diveStraightDownXThreshold = 0.45f;

    [Header("Slash Afterimages")]
    [SerializeField] private bool enableSlashAfterimages = true;
    [SerializeField] private int slashAfterimageCount = 2;
    [SerializeField] private float slashAfterimageLifetime = 0.18f;
    [SerializeField] private Color slashAfterimageColor = new Color(0.85f, 0.55f, 1f, 1f);
    [SerializeField] [Range(0f, 1f)] private float slashAfterimageAlphaStart = 0.45f;
    [SerializeField] [Range(0f, 1f)] private float slashAfterimageAlphaEnd = 0.1f;

    [Header("Dash / Jump (boss edge over playable Malice)")]
    [SerializeField] private float dashDistance = 5.9f;
    [SerializeField] private float dashSpeed = 19f;
    [SerializeField] private float airDashSpeedMul = 1.15f;
    [Tooltip("0 = use dash speed × Dash Jump Speed Mul.")]
    [SerializeField] private float dashJumpHorizontalSpeed = 0f;
    [Tooltip("0 = use jump force × Dash Jump Height Mul.")]
    [SerializeField] private float dashJumpForce = 0f;
    [Tooltip("Boss dash-jump hop vs playable Malice (slightly higher).")]
    [SerializeField] private float dashJumpHeightMul = 1.2f;
    [Tooltip("Boss dash-jump horizontal speed vs playable Malice (faster catch-up).")]
    [SerializeField] private float dashJumpSpeedMul = 1.3f;
    [Tooltip("Extra max travel vs the physics estimate (slightly farther).")]
    [SerializeField] private float dashJumpDistanceMul = 1.2f;
    [SerializeField] private int dashContactDamage = 2;
    [SerializeField] private float recoverDurationFullHp = 0.55f;
    [SerializeField] private float recoverDurationLowHp = 0.18f;
    [SerializeField] private float backdashChance = 0.45f;

    [Header("Metal Sonic Triple Air Dash")]
    [SerializeField] private int metalSonicDashCount = 3;
    [SerializeField] private float metalSonicHangSeconds = 0.2f;
    [Tooltip("Absolute air-dash speed toward the player (fast Metal Sonic feel).")]
    [SerializeField] private float metalSonicDashSpeed = 28f;
    [SerializeField] private float metalSonicDashSpeedMul = 1.85f;
    [SerializeField] private float metalSonicBetweenDashPause = 0.05f;
    [Tooltip("Max horizontal distance to the airborne player to allow this pattern.")]
    [SerializeField] private float metalSonicMaxRangeX = 6.5f;
    [Tooltip("Max vertical distance to the airborne player to allow this pattern.")]
    [SerializeField] private float metalSonicMaxRangeY = 4.5f;

    [Header("Enrage (≤50% HP)")]
    [SerializeField] private float animatorBaseSpeed = 1f;
    [Tooltip("Enrage triggers at this HP fraction or below (0.5 = 50%).")]
    [SerializeField] private float enrageHealthRatio = 0.5f;
    [Tooltip("At enrage HP, ADD this much action/decision tempo (same units as moveSpeed; +3 on base 5 → +0.6).")]
    [SerializeField] private float enrageActionSpeedBonus = 3f;
    [SerializeField] private float enrageMoveSpeedBonus = 3f;
    [SerializeField] private int enrageBonusDamage = 1;
    [SerializeField] private float enrageDecideInterval = 0.08f;
    [SerializeField] private float enrageRecoverDuration = 0.08f;

    [Header("Enrage Red Aura")]
    [SerializeField] private bool showEnrageAura = true;
    [SerializeField] private Color enrageAuraColor = new Color(0.55f, 0.04f, 0.06f, 1f);
    [SerializeField] private Color enrageAuraStrongColor = new Color(0.85f, 0.08f, 0.05f, 1f);
    [SerializeField] private Color enrageAuraFlickerColor = new Color(1f, 0.35f, 0.2f, 1f);
    [SerializeField] [Range(0f, 1f)] private float enrageAuraAlpha = 0.7f;

    private AiState state = AiState.Chase;
    private Pattern activePattern;
    private float stateTimer;
    private float stateDuration;
    private float decideTimer;
    private int pendingDamage;
    private bool slashProjectilesFired;
    private bool hitboxWasActive;
    private bool dashCancelBoost;
    private bool diving;
    private float dashTimer;
    private float dashDuration;
    private float dashDir;
    private float dashSpeedCurrent;
    private int metalSonicDashesLeft;
    private float airHangGravitySaved = 1f;
    private bool airHangActive;
    private float defaultAnimatorSpeed = 1f;

    private readonly System.Collections.Generic.List<Pattern> patternRotation =
        new System.Collections.Generic.List<Pattern>(8);
    private int rotationIndex;
    private Pattern lastUsedPattern = (Pattern)(-1);
    private int lastPatternStreak;
    private JumpPrepGoal jumpPrepGoal;
    private float jumpPrepTimeout = 0.85f;
    [SerializeField] private float minAirTimeBeforeAirAttack = 0.22f;
    private float jumpPrepAirTime;
    private bool jumpPrepHasLeftGround;
    private float hitVfxTimer;
    private bool grappleIsGrab;
    private bool pendingGrappleIsGrab;
    private bool pendingGrappleAimUp;
    private bool isGrappleCharging;
    private float chargeTimer;
    private float auraAllowedAfterTime = float.PositiveInfinity;
    private float chargeReleaseAt;
    private bool chargeStartedInAir;
    private Transform grabbedTarget;
    private Rigidbody2D grabbedBody;
    private GameObject chargeAuraObject;
    private SpriteRenderer chargeAuraRenderer;
    private Material chargeAuraMaterial;
    private float auraFlickerPhase;
    private GameObject enrageAuraObject;
    private SpriteRenderer enrageAuraRenderer;
    private Material enrageAuraMaterial;
    private float enrageAuraFlickerPhase;
    private readonly System.Collections.Generic.List<SlashGhost> slashGhosts =
        new System.Collections.Generic.List<SlashGhost>(4);

    private bool openingCycleActive = true;
    private int openingIndex;
    private DashJumpMode dashJumpMode;
    private float dashJumpTravelTarget;
    private float dashJumpStartX;
    private float dashJumpTrapX;
    private float dashJumpTargetX;
    private float dashJumpCatchUpCooldownTimer;
    private bool grappleAirMove;
    private bool pendingAirSlashAfterGrapple;
    private bool shotDefendActive;
    private float shotDefendUntil;
    private float shotDefendNextCheckAt;
    private float slashSlideRemaining;
    private float slashSlideDir;
    private bool pendingConvertToDashJump;
    private bool isBossDashJumping;
    private float diveVelocityX;
    private float grappleLockY;
    private bool grappleYLocked;
    private bool grapplePullActive;
    private bool grappleReelSfxPlayed;
    private bool grapplePullArrived;
    private bool grappleAnchorLocked;
    private Vector2 grapplePullStartBodyPos;
    private Vector2 grapplePullTargetWorld;
    private float grapplePullExtendTime;

    private GameObject[] dashAfterimageObjects;
    private SpriteRenderer[] dashAfterimageRenderers;
    private float dashAfterimagesVisibleUntil;
    private readonly System.Collections.Generic.List<PoseSample> poseHistory =
        new System.Collections.Generic.List<PoseSample>(64);

    private struct PoseSample
    {
        public float time;
        public Vector3 position;
        public Sprite sprite;
        public bool flipX;
    }

    private static readonly Pattern[] OpeningCycle =
    {
        Pattern.ZeroRush,
        Pattern.DashSlash,
        Pattern.NeedleDash,
        Pattern.RayHook,
        Pattern.GrabHook,
        Pattern.DashJumpTrapDash,
        Pattern.DashJumpDive,
        Pattern.HornetDive,
        Pattern.AirGrabSlash,
        Pattern.AirGrappleChase,
        Pattern.MetalSonicTriDash,
        Pattern.DashJumpCatchUp
    };

    private struct SlashGhost
    {
        public GameObject go;
        public SpriteRenderer sr;
        public float dieAt;
        public float bornAt;
        public float startAlpha;
    }

    private enum JumpPrepGoal
    {
        HornetDive,
        MetalSonicHang
    }

    protected override void Awake()
    {
        SetBossId("BossMalice");
        base.Awake();
        if (animator != null)
            defaultAnimatorSpeed = animator.speed;

        SetupChargeAura();
        SetupEnrageAura();
        SetupDashAfterimages();
        if (attackHitbox != null)
            attackHitbox.OnTargetAcquired += HandleGrappleTargetAcquired;

        RebuildPatternRotation();
        openingCycleActive = true;
        openingIndex = 0;
    }

    private void OnDestroy()
    {
        if (attackHitbox != null)
            attackHitbox.OnTargetAcquired -= HandleGrappleTargetAcquired;
        ClearSlashGhosts(immediate: true);
        DestroyDashAfterimages();
        if (chargeAuraObject != null)
            Destroy(chargeAuraObject);
        if (chargeAuraMaterial != null)
            Destroy(chargeAuraMaterial);
        if (enrageAuraObject != null)
            Destroy(enrageAuraObject);
        if (enrageAuraMaterial != null)
            Destroy(enrageAuraMaterial);
    }

    protected override void OnHitStunStarted()
    {
        CancelAttackImmediate();
        EndAirHang();
        EndDash();
        EndGrappleCharge(clearAura: true);
        ClearGrabbedTarget();
        diving = false;
        pendingMetalSonicContinue = false;
        skipNextBackdash = false;
        hitVfxTimer = 0f;
        state = AiState.Chase;
        stateTimer = 0f;
        decideTimer = 0f;
        ApplyAggressionAnimatorSpeed();
        base.OnHitStunStarted();
    }

    protected override void OnDamageApplied(int healthBefore, int healthAfter, bool enteredStun, bool fromBigShot)
    {
        // Always show the stunned VFX on hit — even when she does not actually flinch.
        VisualEffects.PlayStunned(stunnedEffectPrefab, this);
        if (!enteredStun)
            hitVfxTimer = Mathf.Max(0.05f, hitStunDuration);
        else
            hitVfxTimer = 0f;
    }

    protected override void OnHitStunEnded()
    {
        base.OnHitStunEnded();
        hitVfxTimer = 0f;
    }

    private void TickHitVfx(float dt)
    {
        if (hitVfxTimer <= 0f || isStunned)
            return;

        hitVfxTimer -= dt;
        if (hitVfxTimer <= 0f)
        {
            hitVfxTimer = 0f;
            VisualEffects.StopStunned(this);
        }
    }

    /// <summary>
    /// Only flinch on a Big shot, or when damage crosses the next 10% HP mark (90/80/.../10).
    /// </summary>
    protected override bool ShouldEnterHitStun(int healthBefore, int healthAfter, bool fromBigShot)
    {
        if (fromBigShot)
            return true;

        return CrossedTenPercentLifeMark(healthBefore, healthAfter);
    }

    private bool CrossedTenPercentLifeMark(int healthBefore, int healthAfter)
    {
        float max = Mathf.Max(1, maxHealth);
        for (int pct = 90; pct >= 10; pct -= 10)
        {
            float mark = max * (pct / 100f);
            if (healthBefore > mark && healthAfter <= mark)
                return true;
        }

        return false;
    }

    protected override float GetMoveSpeed()
    {
        float speed = moveSpeed;
        if (isGrappleCharging && IsChargeAuraActive())
        {
            if (chargeTimer >= bigChargeSeconds)
                speed += bigChargeMoveSpeedBonus;
            else if (chargeTimer >= mediumChargeSeconds)
                speed += mediumChargeMoveSpeedBonus;
        }

        if (IsEnraged())
            speed += enrageMoveSpeedBonus;

        speed += crystalMoveSpeedBonus;
        return speed;
    }

    protected override void TickPostHitVisuals(float dt)
    {
        TickHitVfx(dt);
        TickSlashGhosts();
        RecordPoseHistory();
        UpdateDashAfterimages();
        UpdateEnrageAura();
    }

    private bool IsEnraged()
    {
        float max = Mathf.Max(1, maxHealth);
        return currentHealth / max <= enrageHealthRatio + 0.0001f;
    }

    /// <summary>Enrage action add in “multiplier units” (+3 moveSpeed → +3/moveSpeed).</summary>
    private float EnrageActionSpeedAdd()
    {
        if (!IsEnraged())
            return 0f;

        return Mathf.Max(0f, enrageActionSpeedBonus) / Mathf.Max(1f, moveSpeed);
    }

    private int ScaleDamage(int baseDamage)
    {
        return ApplyCrystalAttackBonus(baseDamage + (IsEnraged() ? enrageBonusDamage : 0));
    }

    private float CurrentDecideInterval()
    {
        if (IsEnraged())
        {
            float enrageT = Mathf.Clamp01(EnrageActionSpeedAdd());
            return Mathf.Max(0.05f, Mathf.Lerp(decideIntervalFullHp, enrageDecideInterval, enrageT));
        }

        return Mathf.Max(0.05f, decideIntervalFullHp);
    }

    private float CurrentRecoverDuration()
    {
        if (IsEnraged())
        {
            float enrageT = Mathf.Clamp01(EnrageActionSpeedAdd());
            return Mathf.Max(0.05f, Mathf.Lerp(recoverDurationFullHp, enrageRecoverDuration, enrageT));
        }

        return Mathf.Max(0.05f, recoverDurationFullHp);
    }

    protected override void HandleBossUpdate()
    {
        if (!usePlaceholderAi)
            return;

        // Grapple is uninterruptible by other attacks (same as playable Malice).
        bool grappleLocked = state == AiState.Grapple;

        TickGrappleChargeVisuals();
        FollowGrabbedTarget();

        if (dashJumpCatchUpCooldownTimer > 0f)
            dashJumpCatchUpCooldownTimer -= BossDeltaTime;

        ApplyAggressionAnimatorSpeed();

        PlayerController player = FindPlayer();
        if (player == null || player.IsDead)
        {
            if (!grappleLocked)
            {
                StopHorizontal();
                CancelAttackImmediate();
                EndAirHang();
                EndDash();
                EndGrappleCharge(clearAura: true);
                ClearGrabbedTarget();
                state = AiState.Chase;
            }

            return;
        }

        if (state != AiState.Dash && state != AiState.DashJump && state != AiState.AirHang)
            FaceToward(player.transform);

        switch (state)
        {
            case AiState.Chase:
                TickChase(player);
                break;
            case AiState.ApproachForSlash:
                TickApproachForSlash(player);
                break;
            case AiState.JumpPrep:
                TickJumpPrep(player);
                break;
            case AiState.ChargeWindup:
                TickChargeWindup(player);
                break;
            case AiState.Slash1:
            case AiState.Slash2:
            case AiState.Slash3:
                TickGroundSlash();
                break;
            case AiState.AirSlash:
                TickAirSlash(player);
                break;
            case AiState.Grapple:
                TickGrapple();
                break;
            case AiState.Dash:
                TickDash(player);
                break;
            case AiState.DashJump:
                TickDashJump(player);
                break;
            case AiState.AirHang:
                TickAirHang(player);
                break;
            case AiState.Dive:
                TickDive(player);
                break;
            case AiState.Recover:
                TickRecover(player);
                break;
        }
    }

    protected override void HandleBossFixedUpdate()
    {
        if (!usePlaceholderAi)
            return;

        if (state == AiState.Grapple)
        {
            TickGrapplePullMotionFixed();
            return;
        }

        if (airHangActive && rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }

        if (state == AiState.Dash || state == AiState.DashJump)
        {
            ApplyDashVelocity();
            return;
        }

        if ((state == AiState.Dive || (state == AiState.AirSlash && diving)) && rb != null)
        {
            PlayerController diveTarget = FindPlayer();
            bool straightDown = diveTarget != null &&
                Mathf.Abs(diveTarget.transform.position.x - transform.position.x) <= diveStraightDownXThreshold;
            float vx = straightDown ? 0f : diveVelocityX;
            rb.linearVelocity = new Vector2(vx, -Mathf.Max(0.1f, airSlashDiveSpeed));
            return;
        }

        if (state == AiState.JumpPrep)
        {
            wantsMoveAnim = false;
            if (rb != null)
                rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            return;
        }

        if (IsGroundSlashState())
        {
            TickSlashSlideFixed();
            return;
        }

        // Air slash without dive: don't kill horizontal until we decide dive.
        if (state == AiState.AirSlash && !diving)
        {
            wantsMoveAnim = false;
            return;
        }

        bool canChaseWalk =
            state == AiState.Chase ||
            state == AiState.ApproachForSlash ||
            state == AiState.ChargeWindup;
        if (!canChaseWalk || isDashing)
        {
            if (!canChaseWalk)
                StopHorizontal();
            return;
        }

        // Approach-for-slash / charge chase always walks in; normal chase respects stop distance.
        PlayerController player = FindPlayer();
        if (player == null)
        {
            StopHorizontal();
            return;
        }

        float dx = player.transform.position.x - transform.position.x;
        float abs = Mathf.Abs(dx);
        if (state == AiState.ApproachForSlash || state == AiState.ChargeWindup)
        {
            float stop = state == AiState.ChargeWindup ? slashAttackRange * 1.15f : slashAttackRange * 0.85f;
            if (abs > stop)
                MoveHorizontal(Mathf.Sign(dx));
            else
                StopHorizontal();

            // Air charge chase: hop / keep height when player is above.
            if (state == AiState.ChargeWindup && pendingGrappleAimUp && isGrounded &&
                player.transform.position.y > transform.position.y + 0.6f)
                ForceBossJump();
            return;
        }

        float chaseStop = chaseStopDistance;
        if (IsEnraged())
            chaseStop *= 0.55f;
        if (abs > chaseStop)
            MoveHorizontal(Mathf.Sign(dx));
        else
            StopHorizontal();
    }

    private void TickChase(PlayerController player)
    {
        decideTimer -= BossDeltaTime;
        if (decideTimer > 0f)
            return;

        decideTimer = CurrentDecideInterval();
        if (TryBeginShotDefend(player))
            return;

        PickAndStartPattern(player);
    }

    private void PickAndStartPattern(PlayerController player)
    {
        // Guaranteed opening: each pattern once, in order, before any shuffle rotation.
        if (openingCycleActive)
        {
            if (openingIndex >= OpeningCycle.Length)
            {
                openingCycleActive = false;
                RebuildPatternRotation();
            }
            else
            {
                Pattern opener = OpeningCycle[openingIndex++];
                StartPattern(opener, player);
                return;
            }
        }

        bool enraged = IsEnraged();
        float airGrabRoll = enraged ? 0.7f : 0.4f;
        float trapRoll = enraged ? 0.65f : 0.35f;

        // Situational catch-up (far / off-camera) before random picks.
        if (ShouldDashJumpCatchUp(player))
        {
            StartPattern(Pattern.DashJumpCatchUp, player);
            return;
        }

        if (CanAirGrabSlash(player) && Random.value < airGrabRoll)
        {
            StartPattern(Pattern.AirGrabSlash, player);
            return;
        }

        if (CanDashJumpTrap(player) && Random.value < trapRoll)
        {
            StartPattern(Pattern.DashJumpTrapDash, player);
            return;
        }

        // Enrage: often force a pressure pattern instead of the rotation shuffle.
        if (enraged && Random.value < 0.55f)
        {
            Pattern pressure = Random.value < 0.45f
                ? Pattern.DashSlash
                : (Random.value < 0.5f ? Pattern.ZeroRush : Pattern.NeedleDash);
            StartPattern(pressure, player);
            return;
        }

        Pattern pick = TakeNextPatternFromRotation();
        StartPattern(pick, player);
    }

    private Pattern TakeNextPatternFromRotation()
    {
        if (patternRotation.Count == 0 || rotationIndex >= patternRotation.Count)
            RebuildPatternRotation();

        Pattern pick = patternRotation[rotationIndex];
        rotationIndex++;

        if (pick == lastUsedPattern)
            lastPatternStreak++;
        else
        {
            lastUsedPattern = pick;
            lastPatternStreak = 1;
        }

        return pick;
    }

    /// <summary>
    /// Every unique move once per rotation, shuffled. Reshuffles when finished.
    /// </summary>
    private void RebuildPatternRotation()
    {
        patternRotation.Clear();
        patternRotation.Add(Pattern.ZeroRush);
        patternRotation.Add(Pattern.DashSlash);
        patternRotation.Add(Pattern.NeedleDash);
        patternRotation.Add(Pattern.HornetDive);
        patternRotation.Add(Pattern.DashJumpDive);
        patternRotation.Add(Pattern.DashJumpTrapDash);
        patternRotation.Add(Pattern.DashJumpCatchUp);
        patternRotation.Add(Pattern.RayHook);
        patternRotation.Add(Pattern.GrabHook);
        patternRotation.Add(Pattern.AirGrabSlash);
        patternRotation.Add(Pattern.AirGrappleChase);
        patternRotation.Add(Pattern.MetalSonicTriDash);

        for (int i = patternRotation.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            Pattern tmp = patternRotation[i];
            patternRotation[i] = patternRotation[j];
            patternRotation[j] = tmp;
        }

        if (patternRotation.Count > 1 &&
            lastUsedPattern != (Pattern)(-1) &&
            patternRotation[0] == lastUsedPattern &&
            lastPatternStreak >= 2)
        {
            int swapWith = 1;
            for (int i = 1; i < patternRotation.Count; i++)
            {
                if (patternRotation[i] != lastUsedPattern)
                {
                    swapWith = i;
                    break;
                }
            }

            Pattern tmp = patternRotation[0];
            patternRotation[0] = patternRotation[swapWith];
            patternRotation[swapWith] = tmp;
        }

        rotationIndex = 0;
    }

    private void StartPattern(Pattern pattern, PlayerController player)
    {
        activePattern = pattern;
        dashCancelBoost = false;
        diving = false;
        metalSonicDashesLeft = 0;
        pendingMetalSonicContinue = false;
        pendingGrappleIsGrab = false;
        pendingGrappleAimUp = false;
        pendingAirSlashAfterGrapple = false;
        grappleAirMove = false;
        slashSlideRemaining = 0f;

        switch (pattern)
        {
            case Pattern.ZeroRush:
                TryBeginGroundSlashCombo(player, dashBoost: false);
                break;

            case Pattern.DashSlash:
                dashCancelBoost = true;
                BeginDash(towardPlayer: true, player, speedMul: 1f, followUp: DashFollowUp.Slash1);
                break;

            case Pattern.NeedleDash:
                BeginDash(towardPlayer: true, player, speedMul: 1.05f, followUp: DashFollowUp.Recover);
                break;

            case Pattern.HornetDive:
                if (!IsPlayerInDiveAttackRange(player))
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
                BeginDashJump(player, DashJumpMode.CatchUp);
                break;

            case Pattern.RayHook:
                BeginChargeWindup(player, grab: false, aimUpPreferred: false);
                break;

            case Pattern.GrabHook:
                BeginChargeWindup(player, grab: true, aimUpPreferred: false);
                break;

            case Pattern.AirGrabSlash:
                BeginAirGrabSlash(player);
                break;

            case Pattern.AirGrappleChase:
                if (player == null || player.IsGrounded)
                {
                    BeginChargeWindup(player, grab: true, aimUpPreferred: false);
                    return;
                }

                BeginChargeWindup(player, grab: Random.value < 0.35f, aimUpPreferred: true);
                break;

            case Pattern.MetalSonicTriDash:
                if (!CanUseMetalSonicTriDash(player))
                {
                    StartPattern(Pattern.DashSlash, player);
                    return;
                }

                BeginJumpPrep(JumpPrepGoal.MetalSonicHang, player);
                break;
        }
    }

    private void BeginAirGrabSlash(PlayerController player)
    {
        if (player == null)
        {
            FinishIntoRecover();
            return;
        }

        if (player.IsGrounded || !IsWithinMaxDashJumpRange(player))
        {
            BeginChargeWindup(player, grab: true, aimUpPreferred: !player.IsGrounded);
            return;
        }

        pendingAirSlashAfterGrapple = true;
        pendingGrappleIsGrab = true;
        if (isGrounded)
            ForceBossJump();

        int damage = Mathf.Max(grapplePartialDamage, grappleDamage);
        BeginGrappleSwing(damage, air: true, grab: true, useAimUp: false);
    }

    private bool IsGroundSlashState()
    {
        return state == AiState.Slash1 || state == AiState.Slash2 || state == AiState.Slash3;
    }

    /// <summary>
    /// Ground / air slash hitbox can deflect small shots (reflect) and dissipate medium shots.
    /// Big shots are unaffected.
    /// </summary>
    public bool CanDeflectProjectilesWithSlash()
    {
        if (!usePlaceholderAi || attackHitbox == null || !attackHitbox.IsActive)
            return false;

        return IsGroundSlashState() || state == AiState.AirSlash;
    }

    private bool TryBeginShotDefend(PlayerController player)
    {
        if (openingCycleActive || shotDefendActive || player == null || player.IsDead)
            return false;

        if (Time.time < shotDefendNextCheckAt)
            return false;

        if (CountLivePlayerSmallShots() < Mathf.Max(1, shotDefendMinSmallShots))
            return false;

        shotDefendNextCheckAt = Time.time + Mathf.Max(4f, shotDefendCheckCooldown);

        if (Random.value > Mathf.Clamp01(shotDefendChance))
            return false;

        float hold = Random.Range(1.4f, Mathf.Max(1.5f, shotDefendMaxSeconds));
        shotDefendActive = true;
        shotDefendUntil = Time.time + hold;
        BeginSlash(AiState.Slash1, slash1Duration, slash1Damage, "Slash1", step: false);
        return true;
    }

    private void EndShotDefend()
    {
        shotDefendActive = false;
        shotDefendNextCheckAt = Time.time + Mathf.Max(shotDefendCheckCooldown, shotDefendAfterBurstCooldown);
    }

    private static int CountLivePlayerSmallShots()
    {
        Projectile[] shots = FindObjectsByType<Projectile>(FindObjectsSortMode.None);
        int count = 0;
        for (int i = 0; i < shots.Length; i++)
        {
            Projectile shot = shots[i];
            if (shot == null || shot.ShotType != ProjectileShotType.Small)
                continue;

            Transform shotOwner = shot.Owner;
            if (shotOwner == null)
                continue;

            if (shotOwner.GetComponentInParent<PlayerController>() == null)
                continue;

            if (shotOwner.GetComponentInParent<Boss>() != null)
                continue;

            count++;
        }

        return count;
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

    /// <summary>
    /// Approximate max horizontal travel of a playable-Malice-style dash jump (speed held until land).
    /// </summary>
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

    private float EstimateDashJumpAirTime()
    {
        float up = GetBossDashJumpForce();
        float g = Mathf.Abs(Physics2D.gravity.y) * Mathf.Max(0.1f, rb != null ? rb.gravityScale : defaultGravityScale);
        float apex = up / Mathf.Max(0.01f, g);
        float height = (up * up) / (2f * Mathf.Max(0.01f, g));
        float fallT = Mathf.Sqrt(2f * height / (g * Mathf.Max(1f, fallMultiplier)));
        return Mathf.Max(0.2f, apex + fallT);
    }

    private float PickAssaultCloseGap()
    {
        float min = Mathf.Min(dashJumpAssaultCloseMin, dashJumpAssaultCloseMax);
        float max = Mathf.Max(dashJumpAssaultCloseMin, dashJumpAssaultCloseMax);
        return Random.Range(min, max);
    }

    /// <summary>
    /// Aim to land 1–2 spaces from the player (or trap lead). Only uses max travel when truly needed.
    /// </summary>
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

        // Catch-up / dive assault: land 1–2 spaces short of the player, not past them.
        float gap = PickAssaultCloseGap();
        float absDx = Mathf.Abs(dx);
        float desiredTravel = Mathf.Max(minTravel, absDx - gap);

        // Already close — tiny hop / step in, never a max-distance leap.
        if (absDx <= gap + slashAttackRange)
            desiredTravel = Mathf.Min(desiredTravel, Mathf.Max(minTravel, absDx * 0.55f));

        travel = Mathf.Clamp(desiredTravel, minTravel, maxTravel);
        targetX = startX + dir * travel;

        // Keep the landing on the approach side of the player (don't plan to overshoot).
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

    private bool ShouldDashJumpCatchUp(PlayerController player)
    {
        if (player == null || dashJumpCatchUpCooldownTimer > 0f)
            return false;

        float dx = Mathf.Abs(player.transform.position.x - transform.position.x);

        // Already in assault range — walk / slash instead of another hop.
        if (dx <= midRange)
            return false;

        // Off-screen only forces catch-up when she's also meaningfully far on X
        // (stops the middle-camp overshoot loop).
        if (IsOffCamera())
            return dx >= midRange * 0.9f;

        return dx >= farRange;
    }

    private bool CanAirGrabSlash(PlayerController player)
    {
        if (player == null || player.IsGrounded)
            return false;

        return IsWithinMaxDashJumpRange(player);
    }

    private bool IsOffCamera()
    {
        Camera cam = Camera.main;
        if (cam == null)
            return false;

        Vector3 vp = cam.WorldToViewportPoint(transform.position);
        return vp.z < 0f || vp.x < -0.02f || vp.x > 1.02f || vp.y < -0.02f || vp.y > 1.02f;
    }

    private bool IsPlayerInDiveAttackRange(PlayerController player)
    {
        if (player == null)
            return false;

        float dx = Mathf.Abs(player.transform.position.x - transform.position.x);
        float dy = Mathf.Abs(player.transform.position.y - transform.position.y);
        float range = Mathf.Max(diveAttackRangeX, GetMaxDashJumpTravel());
        return dx <= range && dy <= range * 1.15f;
    }

    private bool IsPlayerInDashJumpRange(PlayerController player)
    {
        return IsWithinMaxDashJumpRange(player);
    }

    /// <summary>
    /// Metal Sonic triple dash only when the player is airborne and within dash reach.
    /// </summary>
    private bool CanUseMetalSonicTriDash(PlayerController player)
    {
        if (player == null || player.IsDead || player.IsGrounded)
            return false;

        float dx = Mathf.Abs(player.transform.position.x - transform.position.x);
        float dy = Mathf.Abs(player.transform.position.y - transform.position.y);
        return dx <= metalSonicMaxRangeX && dy <= metalSonicMaxRangeY;
    }

    private void BeginJumpPrep(JumpPrepGoal goal, PlayerController player)
    {
        jumpPrepGoal = goal;
        state = AiState.JumpPrep;
        stateTimer = jumpPrepTimeout;
        jumpPrepAirTime = 0f;
        jumpPrepHasLeftGround = false;
        isAttacking = true;
        isDashing = false;
        StopHorizontal();
        FaceToward(player != null ? player.transform : null);

        // Always launch — don't rely on flaky grounded checks alone.
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

        // Relight the jump if she never left the ground (sticky ground check / failed impulse).
        if (!jumpPrepHasLeftGround && isGrounded && stateTimer < jumpPrepTimeout - 0.05f)
            ForceBossJump();

        bool readyForAirMove =
            jumpPrepHasLeftGround &&
            jumpPrepAirTime >= minAirTimeBeforeAirAttack;

        // Hornet: dive after she's been airborne a bit (near apex is ideal).
        if (jumpPrepGoal == JumpPrepGoal.HornetDive && readyForAirMove)
        {
            bool nearApex = rb == null || rb.linearVelocity.y <= 1.5f;
            if (nearApex || jumpPrepAirTime >= minAirTimeBeforeAirAttack * 2f)
            {
                ResolveJumpPrep(player);
                return;
            }
        }

        // Metal Sonic: hang once she's clearly airborne — only while player is still a valid target.
        if (jumpPrepGoal == JumpPrepGoal.MetalSonicHang)
        {
            if (!CanUseMetalSonicTriDash(player))
            {
                isAttacking = false;
                FinishIntoRecover();
                return;
            }

            if (readyForAirMove)
            {
                ResolveJumpPrep(player);
                return;
            }
        }

        if (stateTimer > 0f)
            return;

        // Timeout: still force the air follow-up so the pattern isn't skipped.
        if (jumpPrepGoal == JumpPrepGoal.MetalSonicHang && !CanUseMetalSonicTriDash(player))
        {
            isAttacking = false;
            FinishIntoRecover();
            return;
        }

        if (!jumpPrepHasLeftGround)
            ForceBossJump();
        ResolveJumpPrep(player);
    }

    private void ResolveJumpPrep(PlayerController player)
    {
        switch (jumpPrepGoal)
        {
            case JumpPrepGoal.HornetDive:
                if (!IsPlayerInDiveAttackRange(player))
                {
                    // Don't dive into empty space — close with a dash instead.
                    BeginDash(towardPlayer: true, player, speedMul: 1.05f, followUp: DashFollowUp.Slash1);
                    break;
                }

                BeginAirSlash(dive: true);
                break;

            case JumpPrepGoal.MetalSonicHang:
                metalSonicDashesLeft = Mathf.Max(1, metalSonicDashCount);
                BeginAirHangThenDash(player);
                break;
        }
    }

    /// <summary>
    /// Guaranteed upward launch for boss AI (AddForce alone was often cancelled / unread by grounded).
    /// </summary>
    private void ForceBossJump()
    {
        if (rb == null || IsDead)
            return;

        float up = Mathf.Max(jumpForce, 10f);
        Vector2 v = rb.linearVelocity;
        v.y = up;
        rb.linearVelocity = v;
        isGrounded = false;
    }

    private void TryBeginGroundSlashCombo(PlayerController player, bool dashBoost)
    {
        dashCancelBoost = dashBoost;

        if (!isGrounded)
        {
            // No whiff air-slashes from Zero Rush — close with a dive only if already near.
            if (IsPlayerInSlashRange(player))
                BeginAirSlash(dive: true);
            else
                BeginDash(towardPlayer: true, player, speedMul: 1f, followUp: DashFollowUp.Slash1);
            return;
        }

        if (IsPlayerInSlashRange(player))
        {
            BeginSlash(AiState.Slash1, slash1Duration, slash1Damage, "Slash1", step: true);
            return;
        }

        BeginApproachForSlash(player);
    }

    private void BeginApproachForSlash(PlayerController player)
    {
        state = AiState.ApproachForSlash;
        stateTimer = Mathf.Max(0.2f, slashApproachTimeout);
        isAttacking = false;
        isCharging = false;
        StopHorizontal();
        FaceToward(player != null ? player.transform : null);
    }

    private void TickApproachForSlash(PlayerController player)
    {
        FaceToward(player != null ? player.transform : null);
        stateTimer -= BossDeltaTime;

        if (IsPlayerInSlashRange(player))
        {
            BeginSlash(AiState.Slash1, slash1Duration, slash1Damage, "Slash1", step: true);
            return;
        }

        if (stateTimer > 0f)
            return;

        // Still too far — dash in and slash instead of swinging at empty air.
        dashCancelBoost = true;
        BeginDash(towardPlayer: true, player, speedMul: 1.05f, followUp: DashFollowUp.Slash1);
    }

    private bool IsPlayerInSlashRange(PlayerController player)
    {
        if (player == null)
            return false;

        float dx = Mathf.Abs(player.transform.position.x - transform.position.x);
        float dy = Mathf.Abs(player.transform.position.y - transform.position.y);
        // Must be close horizontally; don't slash if they're way above/below.
        return dx <= slashAttackRange && dy <= slashAttackRange * 1.35f;
    }

    // --- Slash / air slash / grapple ---

    private void BeginSlash(AiState slashState, float duration, int damage, string trigger, bool step)
    {
        StopHorizontal();
        float speedMul = AttackSpeedMul() * (dashCancelBoost ? dashCancelSlashSpeedMul : 1f);
        dashCancelBoost = false;

        state = slashState;
        stateDuration = Mathf.Max(0.05f, duration / speedMul);
        stateTimer = stateDuration;
        pendingDamage = ScaleDamage(damage);
        slashProjectilesFired = false;
        BeginAttack(trigger);

        if (step)
            ApplySlashStep();

        PlayBossMaliceSlashSound(slashState);
    }

    private static void PlayBossMaliceSlashSound(AiState slashState)
    {
        if (SoundManager.Instance == null)
            return;

        if (slashState == AiState.Slash3)
            SoundManager.Instance.PlayBossMaliceSlashHeavy();
        else if (slashState == AiState.Slash1 || slashState == AiState.Slash2)
            SoundManager.Instance.PlayBossMaliceSlashLight();
    }

    private void BeginAirSlash(bool dive)
    {
        float speedMul = AttackSpeedMul();
        state = AiState.AirSlash;
        stateDuration = Mathf.Max(0.05f, airSlashDuration / speedMul);
        stateTimer = stateDuration;
        pendingDamage = ScaleDamage(airSlashDamage);
        slashProjectilesFired = false;
        diving = dive;

        if (dive)
        {
            // Keep dash-jump fall momentum horizontally; only go pure vertical when atop the player.
            diveVelocityX = rb != null ? rb.linearVelocity.x : dashDir * GetBossDashJumpHorizontalSpeed();
            if (Mathf.Abs(diveVelocityX) < 0.35f)
                diveVelocityX = dashDir * GetBossDashJumpHorizontalSpeed() * 0.75f;

            PlayerController p = FindPlayer();
            if (p != null &&
                Mathf.Abs(p.transform.position.x - transform.position.x) <= diveStraightDownXThreshold)
                diveVelocityX = 0f;
        }
        else
        {
            StopHorizontal();
        }

        if (!isGrounded && jumpPrepAirTime < minAirTimeBeforeAirAttack)
            jumpPrepAirTime = minAirTimeBeforeAirAttack;
        BeginAttack("AirSlash");
        SoundManager.Instance?.PlayBossMaliceSlashLight();
    }

    private void BeginRayHook()
    {
        BeginChargeWindup(FindPlayer(), grab: false, aimUpPreferred: false);
    }

    private void BeginChargeWindup(PlayerController player, bool grab, bool aimUpPreferred)
    {
        pendingGrappleIsGrab = grab;
        pendingGrappleAimUp = aimUpPreferred ||
                              (player != null && !player.IsGrounded &&
                               player.transform.position.y > transform.position.y + 0.35f);

        isGrappleCharging = true;
        isCharging = true;
        chargeTimer = 0f;
        chargeStartedInAir = !isGrounded;
        auraAllowedAfterTime = Time.time + Mathf.Max(0f, auraDelayAfterPress);
        chargeReleaseAt = Time.time + Random.Range(
            Mathf.Max(mediumChargeSeconds, aiChargeHoldMin),
            Mathf.Max(aiChargeHoldMin, aiChargeHoldMax));

        state = AiState.ChargeWindup;
        stateTimer = airGrappleChaseTimeout;
        isAttacking = false;
        SoundManager.Instance?.StartChargeLoop(SoundManager.ChargeLoopId.BossMalice);
        SetChargeAuraVisible(false);
        FaceToward(player != null ? player.transform : null);
        aimUp = pendingGrappleAimUp;
    }

    private void TickChargeWindup(PlayerController player)
    {
        FaceToward(player != null ? player.transform : null);

        // Keep AimUp on while chasing an airborne player above her.
        pendingGrappleAimUp =
            pendingGrappleAimUp ||
            (player != null && !player.IsGrounded &&
             player.transform.position.y > transform.position.y + 0.25f);
        aimUp = pendingGrappleAimUp;

        chargeTimer += BossDeltaTime;
        stateTimer -= BossDeltaTime;

        if (Time.time >= auraAllowedAfterTime)
            UpdateChargeAura();
        else
            SetChargeAuraVisible(false);

        bool readyToRelease = Time.time >= chargeReleaseAt && IsChargeAuraActive();
        bool timedOut = stateTimer <= 0f && chargeTimer >= mediumChargeSeconds;

        if (!readyToRelease && !timedOut)
            return;

        ReleaseGrappleCharge(player);
    }

    private void ReleaseGrappleCharge(PlayerController player)
    {
        float held = chargeTimer;
        bool fullCharge = held >= bigChargeSeconds;
        int damage = fullCharge
            ? grappleMaxDamage
            : (held >= mediumChargeSeconds ? Mathf.Max(grapplePartialDamage, grappleDamage) : grapplePartialDamage);

        bool air = chargeStartedInAir || !isGrounded || pendingGrappleAimUp;
        bool grab = pendingGrappleIsGrab;
        // Air chase prefers AimUp grapple arm more often than grab.
        if (pendingGrappleAimUp && Random.value < 0.7f)
            grab = false;

        EndGrappleCharge(clearAura: true);
        BeginGrappleSwing(damage, air, grab, pendingGrappleAimUp);
    }

    private void BeginGrappleSwing(int damage, bool air, bool grab, bool useAimUp)
    {
        StopHorizontal();
        ClearGrabbedTarget();
        ClearBossGrapplePullState();
        grappleIsGrab = grab;
        aimUp = useAimUp;
        isCharging = false;
        isGrappleCharging = false;
        isAttacking = true;
        state = AiState.Grapple;
        stateDuration = Mathf.Max(0.05f, grappleDuration / AttackSpeedMul());
        stateTimer = stateDuration;
        pendingDamage = ScaleDamage(damage);
        hitboxWasActive = false;

        grappleAirMove = air || !isGrounded;
        grapplePullExtendTime = GetBossGrapplePullAnchorTimeSeconds(grappleAirMove);
        if (grappleAirMove)
        {
            grappleLockY = transform.position.y;
            grappleYLocked = true;
            StartAirHang();
        }
        else
        {
            grappleYLocked = false;
        }

        if (attackHitbox != null)
            attackHitbox.Deactivate();

        if (animator != null)
            animator.SetTrigger(grappleAirMove ? "GrappleArmAir" : "GrappleArm");

        grappleReelSfxPlayed = false;
        float animSpeed = animator != null ? animator.speed : AttackSpeedMul();
        SoundManager.Instance?.PlayBossMaliceGrappleExtend(animSpeed);
    }

    private float GetBossGrapplePullAnchorTimeSeconds(bool air)
    {
        int frame = air ? grappleAirPullAnchorFrame : grapplePullAnchorFrame;
        float fps = Mathf.Max(1f, grapplePullAnchorFrameRateFallback);
        return Mathf.Max(0f, frame) / fps;
    }

    private Vector2 ComputeBossAttackBoxFrontEdge()
    {
        if (attackHitbox == null)
            return transform.position;

        Collider2D col = attackHitbox.GetComponent<Collider2D>();
        if (col == null || !col.enabled)
            return attackHitbox.transform.position;

        Bounds b = col.bounds;
        float frontX = facingSign >= 0f ? b.max.x : b.min.x;
        return new Vector2(frontX, b.center.y);
    }

    private void BeginBossGrapplePullAnchor()
    {
        grapplePullStartBodyPos = rb != null ? rb.position : (Vector2)transform.position;
        // Always reel to the AttackBox tip — never latch onto the player
        // (hitting the player still damages via the hitbox; pulling to them teleports past them).
        grapplePullTargetWorld = ComputeBossAttackBoxFrontEdge();

        // Player-style: ground slides on X only; air keeps hang height (no arc).
        if (!grappleAirMove && rb != null)
            grapplePullTargetWorld.y = rb.position.y;
        else if (grappleYLocked)
            grapplePullTargetWorld.y = grappleLockY;

        grapplePullArrived = false;
        grappleAnchorLocked = true;
        grapplePullActive = true;
    }

    private void ClearBossGrapplePullState()
    {
        grapplePullActive = false;
        grapplePullArrived = false;
        grappleAnchorLocked = false;
        grapplePullStartBodyPos = Vector2.zero;
        grapplePullTargetWorld = Vector2.zero;
        grapplePullExtendTime = 0f;
    }

    private void TickBossGrapplePull(float elapsed)
    {
        // AimUp: reel to AttackBox tip after extension (same idea as playable Malice Up-pull).
        // Grab without AimUp: hang / stick targets only — no body reel.
        bool wantsPull = aimUp || pendingGrappleAimUp;
        if (!wantsPull)
            return;

        if (!HasReachedBossGrappleExtension(elapsed))
        {
            grapplePullActive = false;
            return;
        }

        if (!grappleAnchorLocked)
            BeginBossGrapplePullAnchor();

        grapplePullActive = true;
        // Target stays locked to the AttackBox tip from BeginBossGrapplePullAnchor.
        // Hitting the player damages via hitbox only — never retarget the reel onto them.
    }

    private bool HasReachedBossGrappleExtension(float elapsed)
    {
        return elapsed + 0.0001f >= grapplePullExtendTime;
    }

    private void TickBossGrappleReelSfx(float elapsed)
    {
        if (grappleReelSfxPlayed || state != AiState.Grapple)
            return;

        float clipTime = elapsed * (animator != null && animator.speed > 0.01f ? animator.speed : 1f);
        float reelAt = grappleAirMove ? 0.333f : 0.2083f;
        if (clipTime + 0.0001f < reelAt)
            return;

        grappleReelSfxPlayed = true;
        float animSpeed = animator != null ? animator.speed : AttackSpeedMul();
        SoundManager.Instance?.PlayBossMaliceGrappleReel(animSpeed);
    }

    private void TickSlashProjectiles(float elapsed)
    {
        if (slashProjectilesFired || attackHitbox == null || !slashProjectiles.Enabled)
            return;

        MaliceSlashKind kind;
        switch (state)
        {
            case AiState.Slash1: kind = MaliceSlashKind.Slash1; break;
            case AiState.Slash2: kind = MaliceSlashKind.Slash2; break;
            case AiState.Slash3: kind = MaliceSlashKind.Slash3; break;
            case AiState.AirSlash: kind = MaliceSlashKind.AirSlash; break;
            default: return;
        }

        float clipTime = elapsed * (animator != null && animator.speed > 0.01f ? animator.speed : AttackSpeedMul());
        if (clipTime + 0.0001f < slashProjectiles.GetFireClipTime(kind))
            return;

        slashProjectilesFired = true;
        slashProjectiles.Fire(
            kind,
            transform,
            attackHitbox.GetComponent<Collider2D>(),
            facingSign,
            pendingDamage,
            spriteRenderer);
    }

    private void TickGroundSlash()
    {
        float elapsed = stateDuration - stateTimer;
        stateTimer -= BossDeltaTime;
        TickSlashProjectiles(elapsed);
        float hitboxActive = shotDefendActive
            ? Mathf.Max(slashHitboxActiveTime, stateDuration * 0.85f)
            : slashHitboxActiveTime;
        UpdateTimedHitbox(elapsed, slashHitboxDelay / AttackSpeedMul(), hitboxActive / AttackSpeedMul());

        if (shotDefendActive && Time.time >= shotDefendUntil)
        {
            EndShotDefend();
            FinishIntoRecover();
            return;
        }

        if (stateTimer > 0f)
            return;

        DeactivateHitbox();

        if (shotDefendActive)
        {
            BeginSlash(AiState.Slash1, slash1Duration, slash1Damage, "Slash1", step: false);
            return;
        }

        if (state == AiState.Slash1)
        {
            PlayerController player = FindPlayer();
            if (!IsPlayerInSlashRange(player))
            {
                FinishIntoRecover();
                return;
            }

            BeginSlash(AiState.Slash2, slash2Duration, slash2Damage, "Slash2", step: true);
            return;
        }

        if (state == AiState.Slash2)
        {
            PlayerController player = FindPlayer();
            if (!IsPlayerInSlashRange(player))
            {
                FinishIntoRecover();
                return;
            }

            BeginSlash(AiState.Slash3, slash3Duration, slash3Damage, "Slash3", step: true);
            return;
        }

        FinishIntoRecover();
    }

    private void TickAirSlash(PlayerController player)
    {
        float elapsed = stateDuration - stateTimer;
        stateTimer -= BossDeltaTime;
        TickSlashProjectiles(elapsed);

        if (diving || (!isGrounded && player != null &&
                        player.transform.position.y < transform.position.y - 0.15f))
            diving = true;

        if (!isGrounded)
            jumpPrepAirTime += BossDeltaTime;

        float active = diving
            ? 999f
            : slashHitboxActiveTime / AttackSpeedMul();
        UpdateTimedHitbox(elapsed, slashHitboxDelay / AttackSpeedMul(), active);

        // Only "land slash" after a real airborne dive — ignore sticky grounded flicker.
        if (diving && isGrounded && jumpPrepAirTime >= minAirTimeBeforeAirAttack * 0.5f)
        {
            DeactivateHitbox();
            if (IsPlayerInSlashRange(player))
                BeginSlash(AiState.Slash1, slash1Duration * 0.85f, slash1Damage, "Slash1", step: true);
            else
                FinishIntoRecover();
            return;
        }

        if (stateTimer > 0f && !(diving && !isGrounded))
            return;

        if (diving && !isGrounded)
        {
            state = AiState.Dive;
            return;
        }

        DeactivateHitbox();
        FinishIntoRecover();
    }

    private void TickDive(PlayerController player)
    {
        diving = true;
        isAttacking = true;
        if (attackHitbox != null && !hitboxWasActive)
        {
            attackHitbox.Activate(airSlashDamage, applyDamage: true);
            hitboxWasActive = true;
        }

        if (!isGrounded)
            return;

        DeactivateHitbox();
        diving = false;
        if (IsPlayerInSlashRange(player))
            BeginSlash(AiState.Slash1, slash1Duration * 0.85f, slash1Damage, "Slash1", step: true);
        else
            FinishIntoRecover();
    }

    private void TickGrapple()
    {
        // Legacy short telegraph path (if somehow entered without ChargeWindup).
        if (isCharging && !isGrappleCharging)
        {
            stateTimer -= BossDeltaTime;
            if (stateTimer > 0f)
                return;

            isCharging = false;
            isAttacking = true;
            stateDuration = Mathf.Max(0.05f, grappleDuration / AttackSpeedMul());
            stateTimer = stateDuration;
            hitboxWasActive = false;
            grapplePullExtendTime = GetBossGrapplePullAnchorTimeSeconds(!isGrounded);
            if (animator != null)
                animator.SetTrigger(isGrounded ? "GrappleArm" : "GrappleArmAir");
            return;
        }

        float elapsed = stateDuration - stateTimer;
        stateTimer -= BossDeltaTime;
        UpdateTimedHitbox(
            elapsed,
            grappleHitboxDelay / AttackSpeedMul(),
            grappleHitboxActiveTime / AttackSpeedMul());

        TickBossGrappleReelSfx(elapsed);
        TickBossGrapplePull(elapsed);
        FollowGrabbedTarget();

        // Finish only after the move clock ends AND any active tip-reel has arrived.
        bool pullBlocking =
            grapplePullActive &&
            !grapplePullArrived &&
            elapsed < stateDuration + 0.85f;

        if (stateTimer > 0f || pullBlocking)
            return;

        DeactivateHitbox();
        ClearGrabbedTarget();
        SoundManager.Instance?.StopMaliceGrapple();
        ClearBossGrapplePullState();
        grappleIsGrab = false;
        aimUp = false;

        bool followUpAirSlash = pendingAirSlashAfterGrapple;
        pendingAirSlashAfterGrapple = false;
        EndAirHang();
        grappleAirMove = false;
        grappleYLocked = false;

        if (followUpAirSlash && !isGrounded)
            BeginAirSlash(dive: false);
        else
            FinishIntoRecover();
    }

    // --- Dash / dash-jump / Metal Sonic ---

    private enum DashFollowUp
    {
        None,
        Slash1,
        Recover,
        AirSlashDive,
        MetalSonicContinue,
        GoChase
    }

    private DashFollowUp dashFollowUp;
    private bool pendingMetalSonicContinue;
    private bool skipNextBackdash;

    private void BeginDash(bool towardPlayer, PlayerController player, float speedMul, DashFollowUp followUp)
    {
        FaceToward(player != null ? player.transform : null);
        dashDir = facingSign;
        if (towardPlayer && player != null)
        {
            float dx = player.transform.position.x - transform.position.x;
            if (Mathf.Abs(dx) > 0.05f)
                dashDir = Mathf.Sign(dx);
        }
        else if (!towardPlayer)
        {
            dashDir = facingSign;
        }

        float speed = dashSpeed * Mathf.Max(0.1f, speedMul);
        // Air dash uses same peak speed as ground (player zeros Y instead of speeding up).
        if (!isGrounded && !pendingConvertToDashJump)
            speed *= airDashSpeedMul;

        dashSpeedCurrent = BossSpeed(speed);
        dashDuration = Mathf.Max(0.05f, dashDistance / dashSpeedCurrent);
        dashTimer = 0f;
        isDashing = true;
        isBossDashJumping = false;
        isAttacking = true;
        state = AiState.Dash;
        dashFollowUp = followUp;
        StopHorizontal();

        BeginDashAfterimageTrail(dashDuration);
        if (animator != null)
            animator.SetTrigger("Dash");

        // Apply velocity immediately so dash-jump cancels don't show a one-frame hover.
        if (rb != null)
            rb.linearVelocity = new Vector2(dashDir * dashSpeedCurrent, 0f);

        if (attackHitbox != null)
            attackHitbox.Activate(ScaleDamage(dashContactDamage), applyDamage: true);
        hitboxWasActive = true;
    }

    /// <summary>
    /// Fast air dash aimed at the player's current X — used by Metal Sonic triple dash.
    /// </summary>
    private void BeginMetalSonicDash(PlayerController player)
    {
        FaceToward(player != null ? player.transform : null);
        dashDir = facingSign;
        if (player != null)
        {
            float dx = player.transform.position.x - transform.position.x;
            if (Mathf.Abs(dx) > 0.05f)
                dashDir = Mathf.Sign(dx);
            facingSign = dashDir;
            ApplyFacingVisual();
        }

        float speed = Mathf.Max(
            metalSonicDashSpeed,
            dashSpeed * airDashSpeedMul * metalSonicDashSpeedMul);
        int dashIndex = metalSonicDashCount - metalSonicDashesLeft;
        speed *= 1f + 0.06f * Mathf.Max(0, dashIndex);

        dashSpeedCurrent = BossSpeed(speed);
        dashDuration = Mathf.Max(0.05f, dashDistance / dashSpeedCurrent);
        dashTimer = 0f;
        isDashing = true;
        isBossDashJumping = false;
        isAttacking = true;
        state = AiState.Dash;
        dashFollowUp = DashFollowUp.MetalSonicContinue;
        pendingConvertToDashJump = false;
        StopHorizontal();

        BeginDashAfterimageTrail(dashDuration);
        if (animator != null)
            animator.SetTrigger("Dash");

        if (rb != null)
            rb.linearVelocity = new Vector2(dashDir * dashSpeedCurrent, 0f);

        if (attackHitbox != null)
            attackHitbox.Activate(ScaleDamage(dashContactDamage), applyDamage: true);
        hitboxWasActive = true;
    }

    private void BeginDashJump(PlayerController player, DashJumpMode mode)
    {
        FaceToward(player != null ? player.transform : null);
        dashDir = facingSign;
        dashJumpMode = mode;
        dashJumpStartX = transform.position.x;

        PlanDashJumpLanding(player, mode, out float travel, out float targetX);
        dashJumpTravelTarget = travel;
        dashJumpTargetX = targetX;
        if (mode == DashJumpMode.TrapThenDash)
            dashJumpTrapX = targetX;

        dashFollowUp = DashFollowUp.AirSlashDive;
        jumpPrepAirTime = 0f;

        // Player-style: grounded dash first (Dash anim + afterimages), then Jump convert.
        if (isGrounded)
        {
            pendingConvertToDashJump = true;
            BeginDash(towardPlayer: true, player, speedMul: 1f, followUp: DashFollowUp.None);
            return;
        }

        // Already airborne — launch with the same Jump impulse/trail as playable Malice.
        PerformBossDashJumpImpulse();
    }

    /// <summary>Matches playable Malice PerformDashJump: Jump trigger, afterimages, same speeds.</summary>
    private void PerformBossDashJumpImpulse()
    {
        pendingConvertToDashJump = false;
        isDashing = false;

        float maxSpeed = GetBossDashJumpHorizontalSpeed();
        float hop = GetBossDashJumpForce();
        float airTime = EstimateDashJumpAirTime();
        // Scale horizontal speed to the planned travel — max speed only when the hop needs it.
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
        jumpPrepAirTime = 0f;

        BeginDashAfterimageTrail(Mathf.Max(0.55f, GetMaxAfterimageDelay()));
        if (animator != null)
            animator.SetTrigger("Jump");

        if (attackHitbox != null)
            attackHitbox.Activate(ScaleDamage(dashContactDamage), applyDamage: true);
        hitboxWasActive = true;
    }

    private void BeginMetalSonicTriDash(PlayerController player)
    {
        // Entry is via JumpPrep → hang. Kept for clarity / safety.
        BeginJumpPrep(JumpPrepGoal.MetalSonicHang, player);
    }

    private void BeginAirHangThenDash(PlayerController player)
    {
        FaceToward(player != null ? player.transform : null);
        EndDash();

        // Snap a bit upward so hang is visibly in the air, then freeze.
        if (rb != null && isGrounded)
            ForceBossJump();

        StartAirHang();
        state = AiState.AirHang;
        stateTimer = Mathf.Max(0.05f, metalSonicHangSeconds);
        isAttacking = true;
        isCharging = true;
    }

    private void TickDash(PlayerController player)
    {
        dashTimer += BossDeltaTime;

        // Player-style dash-jump: short grounded dash, then Jump convert.
        if (pendingConvertToDashJump)
        {
            if (dashTimer >= dashJumpConvertDelay || !isGrounded)
            {
                dashJumpStartX = transform.position.x;
                // Re-aim from the convert point so travel matches current spacing.
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

        EndDash();
        ResolveDashFollowUp(player);
    }

    private void TickDashJump(PlayerController player)
    {
        dashTimer += BossDeltaTime;
        if (!isGrounded)
            jumpPrepAirTime += BossDeltaTime;

        bool enoughAir = jumpPrepAirTime >= minAirTimeBeforeAirAttack;
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
            {
                // Cut horizontal once she reaches the planned land X (prevents overshoot).
                if (HasReachedDashJumpTargetX() && rb != null)
                    rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);

                // No mid-air cancel — wait until she lands, then act.
                if (!isGrounded || !enoughAir)
                    return;

                EndDashJumpImmediate();
                dashJumpCatchUpCooldownTimer = Mathf.Max(0.1f, dashJumpCatchUpCooldown);

                bool closeEnough = player != null &&
                                   Mathf.Abs(player.transform.position.x - transform.position.x) <= slashAttackRange * 1.75f;
                if (closeEnough)
                    BeginDash(towardPlayer: true, player, speedMul: 1f, followUp: DashFollowUp.Slash1);
                else if (player != null &&
                         Mathf.Abs(player.transform.position.x - transform.position.x) <= midRange)
                    TryBeginGroundSlashCombo(player, dashBoost: false);
                else
                    FinishIntoRecover();
                break;
            }

            case DashJumpMode.TrapThenDash:
            {
                bool pastTrap = HasReachedDashJumpTargetX() || (dashDir > 0f
                    ? transform.position.x >= dashJumpTrapX - 0.2f
                    : transform.position.x <= dashJumpTrapX + 0.2f);

                // Mid-air cancel into dash must be instant (no hang / random pause).
                if (pastTrap && enoughAir)
                {
                    EndDashJumpImmediate();
                    BeginDash(towardPlayer: true, player, speedMul: 1.05f, followUp: DashFollowUp.Recover);
                    ApplyDashVelocity();
                    break;
                }

                // Otherwise land first, then dash immediately on touchdown.
                if (isGrounded && enoughAir)
                {
                    EndDashJumpImmediate();
                    BeginDash(towardPlayer: true, player, speedMul: 1.05f, followUp: DashFollowUp.Recover);
                    ApplyDashVelocity();
                }

                break;
            }

            case DashJumpMode.DiveCut:
            default:
            {
                // Stop sliding past the planned assault X before diving.
                if (HasReachedDashJumpTargetX() && rb != null)
                    rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);

                bool inDiveRange = IsPlayerInDiveAttackRange(player);

                // Mid-air dive cancel: instant, no hover.
                if (inDiveRange && enoughAir && peaked)
                {
                    EndDashJumpImmediate();
                    BeginAirSlash(dive: true);
                    break;
                }

                // No cancel planned — wait until she actually lands.
                if (isGrounded && enoughAir)
                {
                    EndDashJumpImmediate();
                    if (player != null &&
                        Mathf.Abs(player.transform.position.x - transform.position.x) <= slashAttackRange * 1.5f)
                        TryBeginGroundSlashCombo(player, dashBoost: false);
                    else
                        FinishIntoRecover();
                }

                break;
            }
        }
    }

    private void EndDashJumpImmediate()
    {
        EndDash();
        isBossDashJumping = false;
        pendingConvertToDashJump = false;
    }

    private void TickAirHang(PlayerController player)
    {
        FaceToward(player != null ? player.transform : null);
        stateTimer -= BossDeltaTime;
        if (stateTimer > 0f)
            return;

        isCharging = false;
        EndAirHang();

        // Abort remaining triple-dashes if the player landed or left reach.
        if (!CanUseMetalSonicTriDash(player))
        {
            metalSonicDashesLeft = 0;
            pendingMetalSonicContinue = false;
            FinishIntoRecover();
            return;
        }

        BeginMetalSonicDash(player);
    }

    private void ResolveDashFollowUp(PlayerController player)
    {
        switch (dashFollowUp)
        {
            case DashFollowUp.None:
                FinishIntoRecover();
                break;

            case DashFollowUp.Slash1:
                dashCancelBoost = true;
                TryBeginGroundSlashCombo(player, dashBoost: true);
                break;

            case DashFollowUp.AirSlashDive:
                BeginAirSlash(dive: true);
                break;

            case DashFollowUp.MetalSonicContinue:
                metalSonicDashesLeft--;
                if (metalSonicDashesLeft > 0)
                {
                    pendingMetalSonicContinue = true;
                    state = AiState.Recover;
                    stateTimer = metalSonicBetweenDashPause;
                    isAttacking = true;
                }
                else
                {
                    pendingMetalSonicContinue = false;
                    FinishIntoRecover();
                }
                break;

            case DashFollowUp.GoChase:
                isAttacking = false;
                DeactivateHitbox();
                state = AiState.Chase;
                decideTimer = CurrentDecideInterval() * 0.35f;
                break;

            case DashFollowUp.Recover:
            default:
                FinishIntoRecover();
                break;
        }
    }

    private void TickRecover(PlayerController player)
    {
        StopHorizontal();
        stateTimer -= BossDeltaTime;
        if (stateTimer > 0f)
            return;

        if (pendingMetalSonicContinue && metalSonicDashesLeft > 0)
        {
            pendingMetalSonicContinue = false;
            if (!CanUseMetalSonicTriDash(player))
            {
                metalSonicDashesLeft = 0;
                FinishIntoRecover();
                return;
            }

            if (isGrounded)
                ForceBossJump();
            BeginAirHangThenDash(player);
            return;
        }

        pendingMetalSonicContinue = false;

        float backChance = backdashChance * (IsEnraged() ? 0.25f : 0.75f);

        if (!skipNextBackdash &&
            player != null &&
            Random.value < backChance)
        {
            skipNextBackdash = true;
            FaceToward(player.transform);
            facingSign = -facingSign;
            ApplyFacingVisual();
            BeginDash(towardPlayer: false, player, speedMul: 0.9f, followUp: DashFollowUp.GoChase);
            return;
        }

        skipNextBackdash = false;
        isAttacking = false;
        state = AiState.Chase;
        decideTimer = CurrentDecideInterval() * 0.5f;
    }

    private void FinishIntoRecover()
    {
        isAttacking = false;
        isCharging = false;
        diving = false;
        pendingMetalSonicContinue = false;
        pendingAirSlashAfterGrapple = false;
        slashSlideRemaining = 0f;
        pendingConvertToDashJump = false;
        isBossDashJumping = false;
        grappleYLocked = false;
        SoundManager.Instance?.StopMaliceGrapple();
        ClearBossGrapplePullState();
        EndGrappleCharge(clearAura: true);
        EndAirHang();
        grappleAirMove = false;
        ClearGrabbedTarget();
        grappleIsGrab = false;
        aimUp = false;
        DeactivateHitbox();
        state = AiState.Recover;
        stateTimer = CurrentRecoverDuration();
    }

    // --- Shared helpers ---

    private void BeginAttack(string triggerName)
    {
        isAttacking = true;
        hitboxWasActive = false;
        if (attackHitbox != null)
            attackHitbox.Deactivate();

        if (animator != null && !string.IsNullOrEmpty(triggerName))
            animator.SetTrigger(triggerName);
    }

    private void UpdateTimedHitbox(float elapsed, float delay, float activeTime)
    {
        if (attackHitbox == null)
            return;

        bool shouldBeActive = elapsed >= delay && elapsed <= delay + activeTime;
        if (shouldBeActive && !hitboxWasActive)
        {
            attackHitbox.Activate(pendingDamage, applyDamage: true);
            hitboxWasActive = true;
        }
        else if (!shouldBeActive && hitboxWasActive)
        {
            attackHitbox.Deactivate();
            hitboxWasActive = false;
        }
    }

    private void DeactivateHitbox()
    {
        hitboxWasActive = false;
        if (attackHitbox != null)
            attackHitbox.Deactivate();
    }

    private void ApplySlashStep()
    {
        // Smooth slide instead of a teleport snap.
        slashSlideDir = facingSign;
        slashSlideRemaining = Mathf.Max(0f, groundSlashStep);
        SpawnSlashAfterimageBurst();
    }

    private void TickSlashSlideFixed()
    {
        if (rb == null)
            return;

        if (slashSlideRemaining <= 0.0001f)
        {
            StopHorizontal();
            return;
        }

        float step = Mathf.Min(slashSlideRemaining, groundSlashSlideSpeed * BossFixedDeltaTime);
        Vector2 p = rb.position;
        p.x += slashSlideDir * step;
        rb.MovePosition(p);
        rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
        slashSlideRemaining -= step;
        wantsMoveAnim = false;
    }

    private void TickGrapplePullMotionFixed()
    {
        if (rb == null)
            return;

        if (grappleAirMove && !grappleYLocked)
        {
            grappleLockY = rb.position.y;
            grappleYLocked = true;
        }

        float elapsed = stateDuration - stateTimer;

        // Before extension / no active tip-reel: hang in air, or hold on ground.
        if (!grapplePullActive || !HasReachedBossGrappleExtension(elapsed))
        {
            if (grappleAirMove)
            {
                rb.MovePosition(new Vector2(rb.position.x, grappleLockY));
                rb.linearVelocity = Vector2.zero;
            }
            else
            {
                rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            }

            return;
        }

        Vector2 target = grapplePullTargetWorld;
        if (!grappleAirMove)
            target.y = rb.position.y;
        else
            target.y = grappleLockY;

        float arrive = Mathf.Max(0.005f, grapplePullArriveDistance);
        float distX = Mathf.Abs(rb.position.x - target.x);
        if (grapplePullArrived || distX <= arrive)
        {
            Vector2 snap = new Vector2(target.x, grappleAirMove ? grappleLockY : rb.position.y);
            rb.MovePosition(snap);
            rb.linearVelocity = Vector2.zero;
            grapplePullArrived = true;
            return;
        }

        float extendTime = grapplePullExtendTime;
        float endTime = Mathf.Max(extendTime + 0.05f, stateDuration);
        float curTime = Mathf.Clamp(elapsed, 0f, endTime);
        float t = endTime <= extendTime + 0.0001f
            ? 1f
            : Mathf.Clamp01(Mathf.InverseLerp(extendTime, endTime, curTime));

        Vector2 desired;
        if (t < 0.999f)
        {
            desired = Vector2.Lerp(grapplePullStartBodyPos, target, t);
        }
        else
        {
            float stepDist = Mathf.Max(0.1f, grapplePullSpeed) * BossFixedDeltaTime;
            desired = Vector2.MoveTowards(rb.position, target, stepDist);
        }

        desired.y = grappleAirMove ? grappleLockY : rb.position.y;
        rb.MovePosition(desired);
        rb.linearVelocity = Vector2.zero;

        if (Mathf.Abs(rb.position.x - target.x) <= arrive)
        {
            rb.MovePosition(new Vector2(target.x, desired.y));
            grapplePullArrived = true;
        }
    }

    private void ApplyDashVelocity()
    {
        if (rb == null)
            return;

        // Dash jump: keep gravity. Air/ground dash: lock Y so she does not fall mid-dash.
        float y = state == AiState.DashJump ? rb.linearVelocity.y : 0f;

        // Planned land X reached — kill horizontal so she drops into the player instead of overshooting.
        if (state == AiState.DashJump && HasReachedDashJumpTargetX())
        {
            rb.linearVelocity = new Vector2(0f, y);
            wantsMoveAnim = false;
            return;
        }

        float speedMul = 1f;
        if (state == AiState.Dash && !pendingConvertToDashJump)
        {
            float ease = Mathf.Max(0.02f, dashEdgeEaseSeconds);
            // Ease-in only — avoid a soft start that feels like a mid-air pause after dash-jump cancel.
            if (dashTimer < ease)
                speedMul = Mathf.SmoothStep(0.55f, 1f, dashTimer / ease);
        }

        rb.linearVelocity = new Vector2(dashDir * dashSpeedCurrent * speedMul, y);
        facingSign = dashDir;
        ApplyFacingVisual();
        wantsMoveAnim = false;
    }

    private void EndDash()
    {
        if (!isDashing && state != AiState.Dash && state != AiState.DashJump)
            return;

        isDashing = false;
        pendingConvertToDashJump = false;
        dashTimer = 0f;
        if (hitboxWasActive && attackHitbox != null &&
            (state == AiState.Dash || state == AiState.DashJump))
        {
            attackHitbox.Deactivate();
            hitboxWasActive = false;
        }
    }

    private void StartAirHang()
    {
        if (rb == null)
            return;

        airHangGravitySaved = rb.gravityScale;
        rb.gravityScale = 0f;
        rb.linearVelocity = Vector2.zero;
        airHangActive = true;
    }

    private void EndAirHang()
    {
        if (!airHangActive)
            return;

        airHangActive = false;
        isCharging = false;
        if (rb != null)
            rb.gravityScale = airHangGravitySaved > 0f ? airHangGravitySaved : defaultGravityScale;
    }

    private void CancelAttackImmediate()
    {
        isAttacking = false;
        isCharging = false;
        diving = false;
        dashCancelBoost = false;
        metalSonicDashesLeft = 0;
        hitboxWasActive = false;
        pendingDamage = 0;
        pendingAirSlashAfterGrapple = false;
        slashSlideRemaining = 0f;
        shotDefendActive = false;
        pendingConvertToDashJump = false;
        isBossDashJumping = false;
        SoundManager.Instance?.StopMaliceGrapple();
        ClearBossGrapplePullState();
        grappleYLocked = false;
        EndGrappleCharge(clearAura: true);
        EndAirHang();
        EndDash();
        grappleAirMove = false;
        ClearGrabbedTarget();
        grappleIsGrab = false;
        aimUp = false;
        if (attackHitbox != null)
            attackHitbox.Deactivate();
        if (animator != null)
            animator.speed = defaultAnimatorSpeed * animatorBaseSpeed;
    }

    private float AttackSpeedMul()
    {
        return Mathf.Max(0.1f, 1f + EnrageActionSpeedAdd());
    }

    private void ApplyAggressionAnimatorSpeed()
    {
        if (animator == null)
            return;

        animator.speed = defaultAnimatorSpeed * animatorBaseSpeed * AttackSpeedMul();
    }

    // --- Charge aura / grab / slash afterimages ---

    private bool IsChargeAuraActive()
    {
        return isGrappleCharging && Time.time >= auraAllowedAfterTime;
    }

    private void TickGrappleChargeVisuals()
    {
        if (!isGrappleCharging)
            return;

        if (IsChargeAuraActive())
            UpdateChargeAura();
    }

    private void EndGrappleCharge(bool clearAura)
    {
        if (isGrappleCharging || isCharging)
            SoundManager.Instance?.StopChargeLoop();

        isGrappleCharging = false;
        isCharging = false;
        chargeTimer = 0f;
        auraAllowedAfterTime = float.PositiveInfinity;
        if (clearAura)
            SetChargeAuraVisible(false);
    }

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
        // Stay on the character's sorting plane so mid-order backgrounds cannot cover the aura.
        CharacterEffectSorting.ApplyAuraBehindBody(chargeAuraRenderer, spriteRenderer, EffectSortingGroup);

        float alpha;
        if (chargeTimer >= bigChargeSeconds)
            alpha = auraAlphaAtBig;
        else if (chargeTimer >= mediumChargeSeconds)
            alpha = Mathf.Lerp(auraAlphaAtMedium, auraAlphaAtBig, Mathf.InverseLerp(mediumChargeSeconds, bigChargeSeconds, chargeTimer));
        else
            alpha = Mathf.Lerp(auraAlphaStart, auraAlphaAtMedium, chargeTimer / Mathf.Max(0.01f, mediumChargeSeconds));

        Color purple = Color.Lerp(chargeAuraColor, chargeAuraStrongColor, charge01);
        float flickerSpeed = Mathf.Lerp(auraFlickerSpeed, Mathf.Min(auraFlickerSpeed * 1.75f, 2.4f), charge01);
        auraFlickerPhase += BossDeltaTime * flickerSpeed;
        float shimmer = 0.5f + 0.5f * Mathf.Sin(auraFlickerPhase * Mathf.PI * 2f);
        float violetMix = shimmer * auraFlickerStrength * Mathf.Lerp(0.55f, 1f, charge01);
        Color auraColor = Color.Lerp(purple, chargeAuraFlickerColor, violetMix);
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

    private void SetupEnrageAura()
    {
        if (!showEnrageAura || spriteRenderer == null)
            return;

        if (enrageAuraObject != null)
            Destroy(enrageAuraObject);
        if (enrageAuraMaterial != null)
            Destroy(enrageAuraMaterial);

        enrageAuraObject = new GameObject($"{name}_EnrageAura");
        enrageAuraObject.transform.SetParent(transform, false);
        enrageAuraObject.transform.localPosition = Vector3.zero;
        enrageAuraObject.transform.localScale = Vector3.one * auraBaseScale;
        // Keep behind charge aura sibling order when both are active.
        enrageAuraObject.transform.SetAsFirstSibling();

        enrageAuraRenderer = enrageAuraObject.AddComponent<SpriteRenderer>();
        enrageAuraRenderer.sprite = spriteRenderer.sprite;
        CharacterEffectSorting.ApplyAuraBehindBody(enrageAuraRenderer, spriteRenderer, EffectSortingGroup);
        enrageAuraRenderer.sortingOrder = CharacterEffectSorting.AuraOrderInGroup - 1;
        enrageAuraRenderer.flipX = spriteRenderer.flipX;

        Shader solidShader = Shader.Find("Gameoverse/SpriteSolidColor");
        if (solidShader != null)
        {
            enrageAuraMaterial = new Material(solidShader);
            enrageAuraRenderer.sharedMaterial = enrageAuraMaterial;
        }

        Color c = enrageAuraColor;
        c.a = 0f;
        enrageAuraRenderer.color = c;
        enrageAuraObject.SetActive(false);
    }

    private void UpdateEnrageAura()
    {
        if (!showEnrageAura)
            return;

        if (!IsEnraged())
        {
            SetEnrageAuraVisible(false);
            return;
        }

        if (enrageAuraRenderer == null || enrageAuraObject == null)
            SetupEnrageAura();

        if (enrageAuraRenderer == null || spriteRenderer == null)
            return;

        enrageAuraObject.SetActive(true);
        enrageAuraRenderer.sprite = spriteRenderer.sprite;
        enrageAuraRenderer.flipX = spriteRenderer.flipX;
        CharacterEffectSorting.ApplyAuraBehindBody(enrageAuraRenderer, spriteRenderer, EffectSortingGroup);
        // Same size as charge; sit one step further back so purple charge can stack on top.
        enrageAuraRenderer.sortingOrder = CharacterEffectSorting.AuraOrderInGroup - 1;

        // Same footprint as charge aura; red palette / flicker distinct from purple charge.
        enrageAuraFlickerPhase += BossDeltaTime * auraFlickerSpeed * 1.15f;
        float shimmer = 0.5f + 0.5f * Mathf.Sin(enrageAuraFlickerPhase * Mathf.PI * 2f);
        Color red = Color.Lerp(enrageAuraColor, enrageAuraStrongColor, 0.55f + 0.45f * shimmer);
        Color auraColor = Color.Lerp(red, enrageAuraFlickerColor, shimmer * auraFlickerStrength);
        auraColor.a = enrageAuraAlpha;
        enrageAuraRenderer.color = auraColor;

        float pulse = 1f + Mathf.Sin(Time.time * auraPulseSpeed) * auraPulseAmount;
        enrageAuraObject.transform.localScale = Vector3.one * (auraBaseScale * pulse);
    }

    private void SetEnrageAuraVisible(bool visible)
    {
        if (enrageAuraObject == null)
            return;

        if (!visible)
        {
            enrageAuraObject.SetActive(false);
            enrageAuraFlickerPhase = 0f;
        }
    }

    private void HandleGrappleTargetAcquired(Collider2D other, PlayerController player)
    {
        if (!grappleIsGrab || state != AiState.Grapple)
            return;

        Transform target = player != null ? player.transform : (other != null ? other.transform : null);
        if (target == null || target == transform || target.IsChildOf(transform))
            return;

        grabbedTarget = target;
        grabbedBody = player != null
            ? player.GetComponent<Rigidbody2D>()
            : (other != null ? other.attachedRigidbody : null);
    }

    private void FollowGrabbedTarget()
    {
        if (!grappleIsGrab || grabbedTarget == null || attackHitbox == null)
            return;

        Vector3 point = attackHitbox.transform.position;
        if (grabbedBody != null)
        {
            grabbedBody.linearVelocity = Vector2.zero;
            grabbedBody.MovePosition(point);
        }
        else
        {
            grabbedTarget.position = point;
        }
    }

    private void ClearGrabbedTarget()
    {
        grabbedTarget = null;
        grabbedBody = null;
    }

    // --- Dash afterimages (player parity) ---

    public void SetCopyBotAfterimageStyle(float grayLevel)
    {
        grayLevel = Mathf.Clamp(grayLevel, 0.05f, 1f);
        dashAfterimageColor = new Color(grayLevel, grayLevel, grayLevel, 1f);
        slashAfterimageColor = new Color(grayLevel, grayLevel, grayLevel, 1f);
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
            dashAfterimagesVisibleUntil = Mathf.Max(
                dashAfterimagesVisibleUntil,
                Time.time + GetMaxAfterimageDelay() + dashAfterimageLifetimePadding);

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

    private void SpawnSlashAfterimageBurst()
    {
        if (!enableSlashAfterimages || spriteRenderer == null)
            return;

        int count = Mathf.Max(1, slashAfterimageCount);
        for (int i = 0; i < count; i++)
        {
            float t = count == 1 ? 0f : i / (float)(count - 1);
            float alpha = Mathf.Lerp(slashAfterimageAlphaStart, slashAfterimageAlphaEnd, t);
            float back = facingSign * (-0.12f - 0.18f * i);

            var go = new GameObject($"{name}_SlashGhost_{i}");
            go.transform.SetParent(transform, false);
            go.transform.position = transform.position + new Vector3(back, 0f, 0f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = spriteRenderer.sprite;
            sr.flipX = spriteRenderer.flipX;
            CharacterEffectSorting.ApplyTrailBehindBody(sr, spriteRenderer, EffectSortingGroup, i);
            Color c = slashAfterimageColor;
            c.a = alpha;
            sr.color = c;

            slashGhosts.Add(new SlashGhost
            {
                go = go,
                sr = sr,
                bornAt = Time.time,
                dieAt = Time.time + slashAfterimageLifetime + 0.04f * i,
                startAlpha = alpha
            });
        }
    }

    private void TickSlashGhosts()
    {
        for (int i = slashGhosts.Count - 1; i >= 0; i--)
        {
            SlashGhost g = slashGhosts[i];
            if (g.go == null)
            {
                slashGhosts.RemoveAt(i);
                continue;
            }

            float life = Mathf.Max(0.01f, g.dieAt - g.bornAt);
            float u = Mathf.Clamp01((Time.time - g.bornAt) / life);
            if (g.sr != null)
            {
                Color c = g.sr.color;
                c.a = Mathf.Lerp(g.startAlpha, 0f, u);
                g.sr.color = c;
            }

            if (Time.time >= g.dieAt)
            {
                Destroy(g.go);
                slashGhosts.RemoveAt(i);
            }
        }
    }

    private void ClearSlashGhosts(bool immediate)
    {
        for (int i = 0; i < slashGhosts.Count; i++)
        {
            if (slashGhosts[i].go != null)
                Destroy(slashGhosts[i].go);
        }

        slashGhosts.Clear();
    }

    protected override void OnBossDefeated()
    {
        EndAirHang();
        EndDash();
        EndGrappleCharge(clearAura: true);
        ClearGrabbedTarget();
        ClearSlashGhosts(immediate: true);
        hitVfxTimer = 0f;
        VisualEffects.StopStunned(this);
        if (animator != null)
            animator.speed = defaultAnimatorSpeed;
        base.OnBossDefeated();
    }

    protected override void OnCombatPaused()
    {
        CancelAttackImmediate();
        state = AiState.Recover;
        stateTimer = Mathf.Max(0.05f, CurrentRecoverDuration());
        base.OnCombatPaused();
    }

    protected override void OnRevived()
    {
        CancelAttackImmediate();
        state = AiState.Chase;
        decideTimer = 0.15f;
        stateTimer = 0f;
        isAttacking = false;
        diving = false;
        pendingMetalSonicContinue = false;
        pendingConvertToDashJump = false;
        isBossDashJumping = false;
        if (animator != null)
            animator.speed = defaultAnimatorSpeed * animatorBaseSpeed;
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        chaseStopDistance = Mathf.Max(0.1f, chaseStopDistance);
        closeRange = Mathf.Max(chaseStopDistance, closeRange);
        slashAttackRange = Mathf.Max(0.5f, slashAttackRange);
        minAirTimeBeforeAirAttack = Mathf.Max(0.05f, minAirTimeBeforeAirAttack);
        jumpPrepTimeout = Mathf.Max(minAirTimeBeforeAirAttack + 0.2f, jumpPrepTimeout);
        midRange = Mathf.Max(closeRange, midRange);
        farRange = Mathf.Max(midRange, farRange);
        slashProjectiles?.Validate();
        slash1Duration = Mathf.Max(0.05f, slash1Duration);
        slash2Duration = Mathf.Max(0.05f, slash2Duration);
        slash3Duration = Mathf.Max(0.05f, slash3Duration);
        airSlashDuration = Mathf.Max(0.05f, airSlashDuration);
        grappleDuration = Mathf.Max(0.05f, grappleDuration);
        grappleChargeTelegraph = Mathf.Max(0.05f, grappleChargeTelegraph);
        dashDistance = Mathf.Max(0.1f, dashDistance);
        dashSpeed = Mathf.Max(0.1f, dashSpeed);
        airDashSpeedMul = Mathf.Max(1f, airDashSpeedMul);
        metalSonicHangSeconds = Mathf.Max(0.05f, metalSonicHangSeconds);
        metalSonicDashCount = Mathf.Max(1, metalSonicDashCount);
        metalSonicDashSpeed = Mathf.Max(dashSpeed, metalSonicDashSpeed);
        metalSonicDashSpeedMul = Mathf.Max(1f, metalSonicDashSpeedMul);
        metalSonicMaxRangeX = Mathf.Max(1f, metalSonicMaxRangeX);
        metalSonicMaxRangeY = Mathf.Max(1f, metalSonicMaxRangeY);
        diveAttackRangeX = Mathf.Max(1f, diveAttackRangeX);
        dashJumpTrapLead = Mathf.Max(0.5f, dashJumpTrapLead);
        dashJumpAssaultCloseMin = Mathf.Max(0.25f, dashJumpAssaultCloseMin);
        dashJumpAssaultCloseMax = Mathf.Max(dashJumpAssaultCloseMin, dashJumpAssaultCloseMax);
        dashJumpMinTravel = Mathf.Max(0.25f, dashJumpMinTravel);
        dashJumpCatchUpCooldown = Mathf.Max(0.05f, dashJumpCatchUpCooldown);
        shotDefendMinSmallShots = Mathf.Max(4, shotDefendMinSmallShots);
        shotDefendMaxSeconds = Mathf.Clamp(shotDefendMaxSeconds, 0.8f, 2.5f);
        shotDefendChance = Mathf.Clamp(shotDefendChance, 0.05f, 0.5f);
        shotDefendCheckCooldown = Mathf.Max(4f, shotDefendCheckCooldown);
        shotDefendAfterBurstCooldown = Mathf.Max(8f, shotDefendAfterBurstCooldown);
        groundSlashStep = Mathf.Max(0f, groundSlashStep);
        groundSlashSlideSpeed = Mathf.Max(0.5f, groundSlashSlideSpeed);
        dashEdgeEaseSeconds = Mathf.Max(0.02f, dashEdgeEaseSeconds);
        grapplePullSpeed = Mathf.Max(1f, grapplePullSpeed);
        mediumChargeSeconds = Mathf.Max(0.05f, mediumChargeSeconds);
        bigChargeSeconds = Mathf.Max(mediumChargeSeconds, bigChargeSeconds);
        aiChargeHoldMin = Mathf.Max(mediumChargeSeconds, aiChargeHoldMin);
        aiChargeHoldMax = Mathf.Max(aiChargeHoldMin, aiChargeHoldMax);
        slashAfterimageCount = Mathf.Max(1, slashAfterimageCount);
        slashAfterimageLifetime = Mathf.Max(0.05f, slashAfterimageLifetime);
        decideIntervalFullHp = Mathf.Max(0.05f, decideIntervalFullHp);
        decideIntervalLowHp = Mathf.Max(0.05f, decideIntervalLowHp);
        recoverDurationFullHp = Mathf.Max(0.05f, recoverDurationFullHp);
        recoverDurationLowHp = Mathf.Max(0.05f, recoverDurationLowHp);
        enrageHealthRatio = Mathf.Clamp01(enrageHealthRatio);
    }
#endif
}
