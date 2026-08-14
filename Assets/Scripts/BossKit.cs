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
        AirGrabSlash = 10
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
        Recover
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
    [SerializeField] private Projectile smallProjectilePrefab;
    [SerializeField] private Projectile mediumProjectilePrefab;
    [SerializeField] private Projectile bigProjectilePrefab;
    [SerializeField] private int shotsPerBurst = 3;
    [SerializeField] private float timeBetweenShots = 0.22f;
    [SerializeField] private float machineGunShotInterval = 0.14f;
    [SerializeField] private int machineGunMaxShots = 5;
    [SerializeField] private float shootAnimDuration = 0.28f;
    [SerializeField] private float recoverDuration = 0.55f;
    [SerializeField] private float recoverDurationEnraged = 0.38f;
    [SerializeField] private float aimUpThreshold = 0.45f;
    [SerializeField] private float spreadShotAngleDegrees = 45f;
    [Tooltip("Hard cap on live boss-owned projectiles (oldest removed when over).")]
    [SerializeField] private int maxLiveProjectiles = 14;

    [Header("Boss Kit - VFX Prefabs")]
    [SerializeField] private VisualEffect busterBlastSmallPrefab;
    [FormerlySerializedAs("busterBlastMediumPrefab")]
    [SerializeField] private VisualEffect busterBlastMediumBigPrefab;

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
        Pattern.DashJumpTrapDash,
        Pattern.DashJumpCatchUp,
        Pattern.RayHook,
        Pattern.GrabHook,
        Pattern.AirGrabSlash,
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
    private readonly List<Pattern> patternRotation = new List<Pattern>(11);
    private int rotationIndex;
    private Pattern lastUsedPattern = (Pattern)(-1);
    private int lastPatternStreak;

    private int shotsLeft;
    private float nextShotTime;
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

    protected override void OnHitStunStarted()
    {
        CancelAttackImmediate();
        state = AiState.Chase;
        decideTimer = 0f;
        base.OnHitStunStarted();
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
        if (!usePlaceholderAi)
            return;

        TickShootAnimTimer();
        PruneLiveProjectiles();

        if (dashJumpCatchUpCooldownTimer > 0f)
            dashJumpCatchUpCooldownTimer -= Time.deltaTime;

        if (chargeCooldownTimer > 0f)
            chargeCooldownTimer -= Time.deltaTime;

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
        }
    }

    protected override void HandleBossFixedUpdate()
    {
        if (!usePlaceholderAi)
            return;

        if (state == AiState.Dash || state == AiState.DashJump)
        {
            ApplyDashVelocity();
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
    }

    protected override float GetMoveSpeed()
    {
        float speed = base.GetMoveSpeed();

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
        decideTimer -= Time.deltaTime;
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
        rb.linearVelocity = new Vector2(sign * GetMoveSpeed() * Mathf.Clamp(speedMul, 0.15f, 1f), rb.linearVelocity.y);
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
                if (!CanStartChargePattern())
                {
                    StartPattern(Pattern.NeedleDash, player);
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

        if (attackStyle == BossKitAttackStyle.MachineGun)
        {
            shotsLeft = Mathf.Clamp(count > 0 ? count : machineGunMaxShots, 1, machineGunMaxShots);
            burstShotType = ProjectileShotType.Small;
        }
        else
        {
            shotsLeft = Mathf.Max(1, count > 0 ? count : shotsPerBurst);
        }

        nextShotTime = 0f;
    }

    private void TickShootBurst(PlayerController player)
    {
        if (Time.time < nextShotTime)
            return;

        if (shotsLeft <= 0)
        {
            FinishIntoRecover();
            return;
        }

        FireVolley(player, burstShotType);
        shotsLeft--;

        float interval = attackStyle == BossKitAttackStyle.MachineGun
            ? machineGunShotInterval
            : timeBetweenShots;
        nextShotTime = Time.time + Mathf.Max(0.06f, interval);

        if (shotsLeft <= 0)
            FinishIntoRecover();
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

        CapLiveProjectiles();

        Vector2 origin = firePoint != null
            ? (Vector2)firePoint.position
            : (Vector2)transform.position + new Vector2(facingSign * 0.6f, 0.2f);

        Vector2 dir = GetAimDirection(player);
        if (Mathf.Abs(angleOffset) > 0.01f)
            dir = RotateDirection(dir, angleOffset);

        Projectile shot = Instantiate(prefab, origin, Quaternion.identity);
        shot.Launch(dir, prefab.Speed, prefab.Damage, transform);
        liveProjectiles.Add(shot);

        Transform muzzle = firePoint != null ? firePoint : transform;
        VisualEffect blastPrefab = VisualEffects.ResolveBusterBlastPrefab(
            shot.ShotType,
            busterBlastSmallPrefab,
            busterBlastMediumBigPrefab);
        VisualEffects.SpawnBusterBlast(blastPrefab, muzzle, shot);

        if (playSound)
        {
            if (forceNormalStyle)
                SoundManager.Instance?.PlayKitFire(shot.ShotType);
            else
                PlayShotSound(shot.ShotType);
        }

        shootAnimActive = true;
        shootAnimTimer = shootAnimDuration;
        if (animator != null)
            animator.SetTrigger(aimUp ? "ShootUp" : "Shoot");
    }

    private void PlayShotSound(ProjectileShotType type)
    {
        if (attackStyle == BossKitAttackStyle.MachineGun)
            SoundManager.Instance?.PlayKitFireRapid();
        else
            SoundManager.Instance?.PlayKitFire(type);
    }

    private Vector2 GetAimDirection(PlayerController player)
    {
        if (player == null)
            return new Vector2(facingSign, 0f);

        Vector2 origin = firePoint != null ? (Vector2)firePoint.position : (Vector2)transform.position;
        Vector2 toPlayer = (Vector2)player.transform.position - origin;

        if (Mathf.Abs(toPlayer.y) >= aimUpThreshold && toPlayer.y > 0f)
            return new Vector2(Mathf.Sign(facingSign) * 0.35f, 1f).normalized;

        return new Vector2(facingSign, 0f);
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
            Projectile oldest = liveProjectiles[0];
            liveProjectiles.RemoveAt(0);
            if (oldest != null)
                Destroy(oldest.gameObject);
        }
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

        dashSpeedCurrent = dashSpeed * Mathf.Max(0.1f, speedMul);
        if (!isGrounded)
            dashSpeedCurrent *= airDashSpeedMul;

        dashDuration = Mathf.Max(0.05f, dashDistance / dashSpeedCurrent);
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
        dashTimer += Time.deltaTime;

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
            rb.linearVelocity = new Vector2(dashDir * speed, hop);
            isGrounded = false;
        }

        dashSpeedCurrent = speed;
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
        dashTimer += Time.deltaTime;

        if (!isGrounded)
            dashJumpAirTime += Time.deltaTime;

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
        stateTimer = jumpPrepTimeout;
        isAttacking = true;
        state = AiState.JumpPrep;
        StopHorizontal();
        ForceBossJump();
    }

    private void TickJumpPrep(PlayerController player)
    {
        FaceToward(player != null ? player.transform : null);
        stateTimer -= Time.deltaTime;

        if (!isGrounded || (rb != null && rb.linearVelocity.y > 0.5f))
        {
            jumpPrepHasLeftGround = true;
            jumpPrepAirTime += Time.deltaTime;
        }

        if (!jumpPrepHasLeftGround && isGrounded && stateTimer < jumpPrepTimeout - 0.05f)
            ForceBossJump();

        bool ready = jumpPrepHasLeftGround && jumpPrepAirTime >= minAirTimeBeforeAction;

        if (jumpPrepGoal == JumpPrepGoal.HornetDive && ready)
        {
            bool nearApex = rb == null || rb.linearVelocity.y <= 1.5f;
            if (nearApex || jumpPrepAirTime >= minAirTimeBeforeAction * 2f)
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

        chargeTimer += Time.deltaTime;
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
            SoundManager.Instance?.StartChargeLoop();
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
            chargeTimer += Time.deltaTime;
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
        BeginShootBurst(type, type == ProjectileShotType.Big ? 1 : 2);
    }

    private void FinishIntoRecover()
    {
        isAttacking = false;
        isDashing = false;
        isCharging = false;
        isBossDashJumping = false;
        pendingConvertToDashJump = false;
        state = AiState.Recover;
        recoverTimer = IsEnraged() ? recoverDurationEnraged : recoverDuration;
        StopHorizontal();
    }

    private void TickRecover()
    {
        recoverTimer -= Time.deltaTime;
        if (recoverTimer > 0f)
            return;

        state = AiState.Chase;
        decideTimer = 0.1f;
    }

    private void TickShootAnimTimer()
    {
        if (shootAnimTimer <= 0f)
            return;

        shootAnimTimer -= Time.deltaTime;
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
            return;
        }

        aimUp = player.transform.position.y - transform.position.y >= aimUpThreshold;
    }

    private void CancelAttackImmediate()
    {
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
        auraFlickerPhase += Time.deltaTime * flickerSpeed;
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
        machineGunMaxShots = Mathf.Clamp(machineGunMaxShots, 1, 8);
        machineGunShotInterval = Mathf.Max(0.08f, machineGunShotInterval);
        timeBetweenShots = Mathf.Max(0.06f, timeBetweenShots);
        maxLiveProjectiles = Mathf.Max(4, maxLiveProjectiles);
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
    }
#endif
}
