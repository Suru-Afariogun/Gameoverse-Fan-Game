using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Harlie — melee fighter ported in feel from Little Program Harlie.
/// Ground combo: Kick → Slash 1 → Slash 2 → Slash 3 (cycles).
/// Air: air slash. Uses this game's PlayerController + AttackHitbox systems.
/// Hyper Ability Lv3+ at full health: 8 Silver Swords float beside her. Up + Attack: their base attack;
/// Down + Attack: Grandmother Silk needles.
/// </summary>
public class HarliePlayerController : PlayerController
{
    private enum MeleeState
    {
        Idle,
        Kick,
        Slash1,
        Slash2,
        Slash3,
        AirSlash
    }

    private const int ComboKick = 0;
    private const int ComboSlash1 = 1;
    private const int ComboSlash2 = 2;
    private const int ComboSlash3 = 3;
    private const int ComboStepCount = 4;

    [Header("Harlie - References")]
    [Tooltip("Melee hurtbox (trigger + AttackHitbox). Authored left-facing; mirrored at runtime.")]
    [SerializeField] private AttackHitbox attackHitbox;

    [Header("Harlie - Dash")]
    [Tooltip("Longer than Malice's 5.9.")]
    [SerializeField] private float harlieDashDistance = 7f;
    [SerializeField] private float harlieDashSpeed = 19f;
    [SerializeField] private float harlieDashSmoothStop = 0.12f;
    [Tooltip("Air dashes at Ariel Action level 0.")]
    [SerializeField] private int harlieMaxAirDashes = 3;
    [Tooltip("Can't be hurt for the whole dash.")]
    [SerializeField] private bool dashInvincible = true;
    [SerializeField] private bool harlieAllowDashJump = true;
    [SerializeField] private float harlieDashJumpMomentumDuration = 0.55f;
    [SerializeField] private bool harlieDashJumpMomentumUntilLanded = true;

    [Header("Harlie - Double Jump")]
    [SerializeField] private int harlieMaxAirJumps = 1;
    [Tooltip("Kit's double-jump launch speed; Harlie's height is measured against it.")]
    [SerializeField] private float doubleJumpReferenceForce = 16f;
    [Tooltip("Fraction of Kit's max double-jump height Harlie reaches (1 = as high as Kit).")]
    [SerializeField] [Range(0.05f, 1f)] private float doubleJumpHeightFraction = 1f;
    [SerializeField] private bool pogoRefreshesDoubleJump = true;
    [Tooltip("Seconds she can't be hurt after each pogo bounce.")]
    [SerializeField] private float pogoGuardSeconds = 0.3f;

    [Header("Harlie - Rapid Attack Speed (same as Malice)")]
    [Tooltip("Every N attack presses (within the idle window) increases attack speed by Attack Speed Per Tier.")]
    [SerializeField] private int pressesPerSpeedTier = 6;
    [SerializeField] private float attackSpeedPerTier = 0.5f;
    [SerializeField] private int maxAttackSpeedTier = 1;
    [Tooltip("If no attack press for this long, speed returns to normal.")]
    [SerializeField] private float attackSpeedIdleResetSeconds = 0.7f;

    [Header("Harlie - Dash Cancel Slash (same as Malice)")]
    [Tooltip("Attack speed multiplier when Attack is pressed mid-dash or right after a dash.")]
    [SerializeField] private float dashCancelSlashSpeedMul = 1.5f;
    [Tooltip("How long after a dash ends that an attack still gets the dash-cancel speed boost.")]
    [SerializeField] private float dashCancelSlashWindow = 0.2f;

    [Header("Harlie - Slash Guard (same as Malice)")]
    [Tooltip("While the attack box is active, enemy contact that only touches the part of her body inside the attack box deals no damage.")]
    [SerializeField] private bool slashGuardEnabled = true;
    [Tooltip("Extra world-unit margin around the attack box when deciding a contact is covered.")]
    [SerializeField] private float slashGuardTolerance = 0.8f;

    [Header("Harlie - Projectile Slash (one yellow wave)")]
    [SerializeField] private HarlieSlashWaveSettings slashWave = new HarlieSlashWaveSettings();
    [Tooltip("Charge slash slide and charged air slash fire three waves: straight, angled up and angled down (Hex).")]
    [SerializeField] private bool tripleWaveOnChargedSlash = false;
    [SerializeField] [Range(5f, 85f)] private float tripleWaveAngle = 45f;

    [Header("Harlie - Damage")]
    [Tooltip("Multiplies every melee hit and wave after upgrades (Hex = 1.5).")]
    [SerializeField] private float outgoingDamageMultiplier = 1f;

    [Header("Harlie - Upgrades: Ariel Action")]
    [Tooltip("Air dashes added per level (each level also adds one air jump).")]
    [SerializeField] private int aerialAirDashesPerLevel = 2;

    [Header("Harlie - Upgrades: Hyper Ability (attack / speed / wave only at full health)")]
    [SerializeField] private int hyperDamagePerLevel = 2;
    [Tooltip("Max HP added per level. Always on while equipped.")]
    [SerializeField] private int hyperMaxHealthPerLevel = 2;
    [SerializeField] private float hyperMoveSpeedPerLevel = 0.8f;
    [Tooltip("Wave size added per level (0.4 = +40%).")]
    [SerializeField] private float hyperWaveSizePerLevel = 0.4f;
    [SerializeField] private float hyperWaveDistancePerLevel = 2f;

    [Header("Harlie - Upgrades: Speed Style")]
    [SerializeField] private int speedStyleDamagePerLevel = 1;
    [SerializeField] private float speedStyleWaveDistancePerLevel = 2f;
    [SerializeField] private float speedStyleWaveSpeedPerLevel = 2f;
    [SerializeField] private float speedStyleDashDistancePerLevel = 1f;
    [SerializeField] private float speedStyleDashSpeedPerLevel = 1f;

    [Header("Harlie - Upgrades: Heavy Style")]
    [Tooltip("Wave size added per level (0.2 = +20%).")]
    [SerializeField] private float heavyWaveSizePerLevel = 0.2f;
    [SerializeField] private int heavyWaveDamagePerLevel = 1;
    [SerializeField] private int heavyMaxHealthPerLevel = 2;
    [SerializeField] private float heavyFallMultiplierPerLevel = 1f;
    [Tooltip("Extra slash push (spaces) per level, melee and wave.")]
    [SerializeField] private float heavyKnockbackPerLevel = 1f;
    [Tooltip("From this level, an enemy pushed into another moving enemy destroys both.")]
    [SerializeField] private int heavyChainKillMinLevel = 1;
    [Tooltip("Dash push on moving common enemies (spaces) per level.")]
    [SerializeField] private float heavyDashPushPerLevel = 1f;
    [SerializeField] private int heavyDashPushDamage = 1;
    [SerializeField] private float heavyDashPushDuration = 0.12f;

    [Header("Harlie - Afterimages")]
    [SerializeField] private bool harlieEnableDashAfterimages = true;
    [SerializeField] private int harlieAfterimageCount = 5;
    [SerializeField] private float harlieAfterimageSpacing = 0.1f;
    [SerializeField] private Color harlieAfterimageColor = new Color(1f, 0.92f, 0.02f, 1f);
    [SerializeField] [Range(0f, 1f)] private float harlieAfterimageAlphaStart = 0.65f;
    [SerializeField] [Range(0f, 1f)] private float harlieAfterimageAlphaEnd = 0.2f;
    [SerializeField] private float harlieAfterimageFadeStagger = 0.07f;
    [SerializeField] private float harlieAfterimageFadeDuration = 0.22f;

    [Header("Harlie - Yellow Charge Aura")]
    [SerializeField] private bool showChargeAura = true;
    [SerializeField] private Color chargeAuraColor = new Color(1f, 0.92f, 0.08f, 1f);
    [SerializeField] private Color chargeAuraStrongColor = new Color(1f, 0.78f, 0.02f, 1f);
    [SerializeField] private Color chargeAuraFlickerColor = new Color(1f, 1f, 0.72f, 1f);
    [SerializeField] private float auraBaseScale = 1.18f;
    [SerializeField] private float auraPulseAmount = 0.05f;
    [SerializeField] private float auraPulseSpeed = 6f;
    [SerializeField] private float auraFlickerSpeed = 1.25f;
    [SerializeField] [Range(0.05f, 0.5f)] private float auraFlickerStrength = 0.28f;
    [SerializeField] [Range(0f, 1f)] private float auraAlphaStart = 0.25f;
    [SerializeField] [Range(0f, 1f)] private float auraAlphaAtHalf = 0.65f;
    [SerializeField] [Range(0f, 1f)] private float auraAlphaAtFull = 0.95f;

    [Header("Harlie - Combo")]
    [Tooltip("Window to continue Kick → Slash1 → Slash2 → Slash3.")]
    [SerializeField] private float comboInputWindow = 0.8f;
    [SerializeField] private float kickDuration = 0.38f;
    [SerializeField] private float slash1Duration = 0.38f;
    [SerializeField] private float slash2Duration = 0.38f;
    [SerializeField] private float slash3Duration = 0.38f;
    [SerializeField] private float airSlashDuration = 0.38f;
    [SerializeField] private bool syncAttackDurationToAnimation = true;
    [SerializeField] private float attackDurationPadding = 0.02f;
    [SerializeField] private float timeBetweenAttacks = 0.05f;
    [SerializeField] private int kickDamage = 2;
    [SerializeField] private int slash1Damage = 2;
    [SerializeField] private int slash2Damage = 2;
    [SerializeField] private int slash3Damage = 3;
    [SerializeField] private int airSlashDamage = 2;
    [SerializeField] private float hitboxDelay = 0.05f;
    [SerializeField] private float hitboxActiveTime = 0.22f;
    [SerializeField] private bool lockMoveDuringGroundAttack = true;
    [Tooltip("After an air slash, keep that pose briefly before fall/jump anims resume.")]
    [SerializeField] private float airSlashAnimHold = 0.5f;

    [Header("Harlie - Jump Attack / Pogo")]
    [Tooltip("Automatic falling attack while airborne (Little Program Harlie jump attack).")]
    [SerializeField] private int jumpAttackDamage = 2;
    [Tooltip("Upward speed applied when bounce-hitting a target while falling.")]
    [SerializeField] private float aerialBounceForce = 12f;

    [Header("Harlie - Attack Knockback (same rule as Malice's slashes)")]
    [Tooltip("Gentle push on hit moving enemies (world units). Bosses and stationary enemies (Laser Bot, Chaotic Tanker, Blocker Bot) are never pushed.")]
    [SerializeField] private float attackKnockbackDistance = 2f;
    [SerializeField] private float attackKnockbackDuration = 0.12f;

    [Header("Harlie - Attack Lunge (world units)")]
    [SerializeField] private float kickLungeDistance = 1f;
    [SerializeField] private float slash1LungeDistance = 0.3f;
    [SerializeField] private float slash2LungeDistance = 0.5f;
    [SerializeField] private float slash3LungeDistance = 0.7f;
    [Tooltip("Kick lunge speed relative to dash speed (2 = twice as fast).")]
    [SerializeField] private float kickSpeedVsDash = 2f;

    [Header("Harlie - Attack Styles")]
    [Tooltip("Speed Style: move / dash / attack timing × this.")]
    [SerializeField] private float speedStyleMultiplier = 1.4f;
    [Tooltip("Speed Style: dash-jump horizontal speed × this (lower than move so she does not cross the whole stage).")]
    [SerializeField] private float speedStyleDashJumpMultiplier = 1.12f;
    [Tooltip("Heavy Style: move / dash / attack timing × this.")]
    [SerializeField] private float heavyStyleMultiplier = 0.5f;
    [Tooltip("Heavy Style: added to every hit (kick, slashes, air slash, charge kick).")]
    [SerializeField] private int heavyStyleDamageBonus = 3;

    [Header("Harlie - Charge Attack (ground and air)")]
    [Tooltip("Hold Attack this long for maximum travel.")]
    [SerializeField] private float chargeKickMaxSeconds = 2f;
    [Tooltip("Max charged travel in spaces (× space size).")]
    [SerializeField] private float chargeKickMaxSpaces = 24f;
    [Tooltip("Hold ratio at or below this uses Slash 1; above uses charge kick.")]
    [SerializeField] [Range(0.1f, 0.9f)] private float chargeSlashMaxRatio = 0.5f;
    [Tooltip("World units per space.")]
    [SerializeField] private float chargeKickSpaceSize = 1f;
    [SerializeField] private float chargeKickMinHoldSeconds = 0.08f;
    [Tooltip("Short hold (half charge or less).")]
    [SerializeField] private int chargeSlashDamage = 2;
    [Tooltip("Long hold.")]
    [SerializeField] private int chargeKickDamage = 4;
    [Tooltip("Air release at or above this fraction of full charge = charged air slash; below it = normal air slash.")]
    [SerializeField] [Range(0f, 1f)] private float chargedAirSlashMinRatio = 0.5f;
    [Tooltip("Charged air slash: sword and wave damage × this.")]
    [SerializeField] private int chargedAirSlashDamageMultiplier = 3;
    [Tooltip("Charged air slash: wave base travel distance × this.")]
    [SerializeField] private float chargedAirSlashDistanceMultiplier = 3f;
    [Tooltip("Charge slide speed = dash speed × this (slightly faster than dash).")]
    [SerializeField] private float chargeAttackSpeedVsDash = 1.12f;
    [Tooltip("Brief pose hold after the slide ends.")]
    [SerializeField] private float chargeKickHoldAfterSlide = 0.3f;

    [Header("Harlie - Silver Swords (Hyper Ability, full health)")]
    [Tooltip("Swords float beside her from this Hyper Ability level while she's at full health. " +
             "Up + Attack: base attack. Down + Attack: Grandmother Silk needles. Usable again once they're back in place.")]
    [SerializeField] private int silverSwordMinHyperLevel = 3;
    [SerializeField] private SilverSwordSettings silverSwords = new SilverSwordSettings();
    [Tooltip("Grandmother Silk aims at the closest enemy within this many spaces (otherwise ahead of her).")]
    [SerializeField] private float silverSwordTargetRadius = 18f;

    private MeleeState meleeState = MeleeState.Idle;
    private float meleeTimer;
    private float meleeDuration;
    private float attackCooldownTimer;
    private float comboTimer;
    private int nextComboIndex;
    private int currentComboIndex;
    private int pendingHitDamage;
    private bool hitboxArmed;
    private bool hitboxOpened;
    private bool hitboxClosed;
    private bool currentAttackWasAir;
    private float airSlashHoldUntil;
    private bool hasAttackComboParam;

    private bool isJumpAttackActive;
    private bool isChargingKick;
    private float chargeKickTimer;
    private SilverSwordSquad silverSwordSquad;
    private bool isChargeSlideActive;
    private bool chargeSlideUsesKick;
    private float chargeSlideTimer;
    private float chargeSlideDuration;
    private float chargeSlideStartX;
    private float chargeSlideEndX;
    private float chargeSlideVelocityX;

    private GameObject chargeAuraObject;
    private SpriteRenderer chargeAuraRenderer;
    private Material chargeAuraMaterial;
    private float auraFlickerPhase;

    private int airJumpsRemaining;

    private int rapidAttackPressCount;
    private float lastAttackPressTime = -999f;
    private int attackSpeedTier;
    private float currentMeleeSpeed = 1f;
    private float dashEndedAt = -999f;
    private bool dashCancelSlashQueued;

    private bool slashWaveFired;
    private bool chargeSlideFiresWave;

    private Transform slashGuardedSource;
    private float slashGuardedAt = -999f;
    private const float SlashGuardContactSkin = 0.03f;
    private static readonly List<Collider2D> SlashGuardSourceColliders = new List<Collider2D>();

    private float pogoInvincibleUntil = -999f;
    private bool pogoInvincibleUntilLanded;
    private bool currentChargedAirSlash;
    private int currentDamageMultiplier = 1;

    private int baseMaxHealth;
    private float baseFallMultiplier;
    private readonly HashSet<int> dashPushedIds = new HashSet<int>();
    private static readonly List<Collider2D> DashPushOverlaps = new List<Collider2D>(16);

    private bool isAttackLunging;
    private float attackLungeTimer;
    private float attackLungeDuration;
    private float attackLungeVelocityX;
    private float attackLungeEndX;

    private float attackLungeStartX;

    private Vector3 attackBoxLeftFacingLocal;
    private Vector3 attackBoxLastWrittenLocal;
    private bool attackBoxHasLastWritten;

    public bool IsMeleeAttacking => meleeState != MeleeState.Idle;
    public bool IsChargingKick => isChargingKick;

    /// <summary>Character id set on Awake (upgrades / HUD are keyed by it).</summary>
    protected virtual string HarlieCharacterId => "Harlie";
    protected AttackHitbox MeleeHitbox => attackHitbox;
    protected bool IsChargeSlideActive => isChargeSlideActive;
    /// <summary>0 when not charging, 1 at full charge.</summary>
    protected float ChargeRatio =>
        isChargingKick ? Mathf.Clamp01(chargeKickTimer / Mathf.Max(0.05f, chargeKickMaxSeconds)) : 0f;
    /// <summary>Damage of the automatic falling attack (pogo).</summary>
    protected virtual int JumpAttackDamage => ScaleOutgoingDamage(jumpAttackDamage);

    public override float GetCameraFollowSpeedHint()
    {
        if (isChargeSlideActive && chargeSlideDuration > 0.0001f)
            return Mathf.Abs(chargeSlideVelocityX);

        if (isAttackLunging && attackLungeDuration > 0.0001f)
            return Mathf.Abs(attackLungeVelocityX);

        return base.GetCameraFollowSpeedHint();
    }

    public override float CameraFastSmoothMultiplier => 1.55f;

    public override bool WantsExtraCameraSmoothing =>
        base.WantsExtraCameraSmoothing || isChargeSlideActive || isChargingKick || isAttackLunging;

    protected override float AfterimageFadeStaggerSeconds => harlieAfterimageFadeStagger;

    protected override float AfterimageFadeDurationSeconds => harlieAfterimageFadeDuration;

    protected override bool BlocksActionCancel()
    {
        return isChargeSlideActive;
    }

    protected override bool CanBeHitStunned => false;

    protected override float GetCurrentMoveSpeed()
    {
        return base.GetCurrentMoveSpeed() * StyleSpeedMultiplier()
               + Mathf.Max(0f, hyperMoveSpeedPerLevel) * ActiveHyperLevel;
    }

    protected override int MaxAirDashesWithUpgrades =>
        Mathf.Max(0, maxAirDashes) + Mathf.Max(0, aerialAirDashesPerLevel) * AerialLevel;

    private int AerialLevel => PlayerUpgrades.GetActiveLevel(this, UpgradeType.AerialAction);
    protected int HyperLevel => PlayerUpgrades.GetActiveLevel(this, UpgradeType.HyperAbility);
    protected int StyleLevel => PlayerUpgrades.GetActiveLevel(this, UpgradeType.AttackStyle);
    /// <summary>Hyper attack / speed / wave bonuses only count at full health.</summary>
    protected int ActiveHyperLevel => PlayerUpgrades.IsHyperActive(this) ? HyperLevel : 0;
    private int SpeedStyleLevel => UsesSpeedStyle() ? StyleLevel : 0;
    private int HeavyStyleLevel => UsesHeavyStyle() ? StyleLevel : 0;
    private bool ChainKillActive => HeavyStyleLevel > 0 && HeavyStyleLevel >= Mathf.Max(1, heavyChainKillMinLevel);
    private float SlashKnockbackDistance =>
        Mathf.Max(0f, attackKnockbackDistance) + Mathf.Max(0f, heavyKnockbackPerLevel) * HeavyStyleLevel;

    /// <summary>Dash speed before the attack-style multiplier (Speed Style upgrades add to it).</summary>
    private float UnstyledDashSpeed =>
        Mathf.Max(0.1f, harlieDashSpeed + Mathf.Max(0f, speedStyleDashSpeedPerLevel) * SpeedStyleLevel);

    /// <summary>Applies upgrade-driven stats. Dash values are left alone mid-dash.</summary>
    private void RefreshUpgradeStats()
    {
        if (!isDashing)
        {
            dashDistance = Mathf.Max(0.1f, harlieDashDistance + Mathf.Max(0f, speedStyleDashDistancePerLevel) * SpeedStyleLevel);
            dashSpeed = UnstyledDashSpeed * StyleSpeedMultiplier();
        }

        fallMultiplier = baseFallMultiplier + Mathf.Max(0f, heavyFallMultiplierPerLevel) * HeavyStyleLevel;
        SetMaxHealthRuntime(baseMaxHealth
                            + Mathf.Max(0, hyperMaxHealthPerLevel) * HyperLevel
                            + Mathf.Max(0, heavyMaxHealthPerLevel) * HeavyStyleLevel);
    }

    protected override float GetDashJumpHorizontalSpeed()
    {
        float speed = dashJumpHorizontalSpeed > 0f ? dashJumpHorizontalSpeed : UnstyledDashSpeed;
        if (UsesSpeedStyle())
            return speed * Mathf.Max(0.1f, speedStyleDashJumpMultiplier);
        if (UsesHeavyStyle())
            return speed * Mathf.Clamp(heavyStyleMultiplier, 0.1f, 1f);
        return speed;
    }

    protected static bool UsesSpeedStyle()
    {
        return PlayerAttackStyle.Is(AttackStyleId.SpreadShot);
    }

    protected static bool UsesHeavyStyle()
    {
        return PlayerAttackStyle.Is(AttackStyleId.MachineGun);
    }

    private float StyleSpeedMultiplier()
    {
        if (UsesSpeedStyle())
            return Mathf.Max(0.1f, speedStyleMultiplier);
        if (UsesHeavyStyle())
            return Mathf.Clamp(heavyStyleMultiplier, 0.1f, 1f);
        return 1f;
    }

    protected int ScaleOutgoingDamage(int baseDamage)
    {
        int dmg = Mathf.Max(0, baseDamage);
        if (UsesHeavyStyle())
            dmg += Mathf.Max(0, heavyStyleDamageBonus);
        dmg += Mathf.Max(0, speedStyleDamagePerLevel) * SpeedStyleLevel;
        dmg += Mathf.Max(0, hyperDamagePerLevel) * ActiveHyperLevel;
        dmg += PlayerGear.WeaponDamageBonus(this);
        return Mathf.FloorToInt(dmg * Mathf.Max(0.1f, outgoingDamageMultiplier) + 0.5f);
    }

    /// <summary>Attack style × rapid-press tier × dash-cancel boost for the current swing.</summary>
    private float AttackSpeed()
    {
        return StyleSpeedMultiplier() * Mathf.Max(0.01f, currentMeleeSpeed);
    }

    private int MaxAirJumps => Mathf.Max(0, harlieMaxAirJumps) + AerialLevel;

    // ---------- Double jump (same as Malice) ----------

    protected override void ApplyJump()
    {
        if (!jumpRequested)
            return;

        // Ground jump, drop-through, wall jump and action locks stay in the base.
        if (isStunned || BlocksActionCancel() || IsGrounded || CanWallJumpNow() || airJumpsRemaining <= 0)
        {
            base.ApplyJump();
            return;
        }

        jumpRequested = false;
        PerformDoubleJump();
    }

    private void PerformDoubleJump()
    {
        if (rb == null)
            return;

        airJumpsRemaining--;
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, GetDoubleJumpForce());
        SoundManager.Instance?.PlayJump();
        ReportTutorialAction(TutorialAction.AirJump);

        if (!IsMeleeAttacking && !isDashing)
            PlayJumpAnimation();
    }

    private void PlayJumpAnimation()
    {
        if (animator == null)
            return;

        int jumpState = Animator.StringToHash("Jump");
        if (animator.HasState(0, jumpState))
            animator.Play(jumpState, 0, 0f);
    }

    /// <summary>Jump height scales with v², so v = reference × √(height fraction).</summary>
    private float GetDoubleJumpForce()
    {
        float reference = doubleJumpReferenceForce > 0f ? doubleJumpReferenceForce : jumpForce;
        return reference * Mathf.Sqrt(Mathf.Clamp01(doubleJumpHeightFraction));
    }

    protected override void OnLanded()
    {
        base.OnLanded();
        airJumpsRemaining = MaxAirJumps;
        pogoInvincibleUntilLanded = false;
    }

    // ---------- Rapid attack speed + dash cancel (same as Malice) ----------

    private void RegisterRapidAttackPress()
    {
        lastAttackPressTime = Time.time;
        rapidAttackPressCount = Mathf.Max(0, rapidAttackPressCount) + 1;

        int perTier = Mathf.Max(1, pressesPerSpeedTier);
        attackSpeedTier = Mathf.Min(Mathf.Max(0, maxAttackSpeedTier), rapidAttackPressCount / perTier);
    }

    private void TickRapidAttackSpeedIdleReset()
    {
        if (attackSpeedTier <= 0 && rapidAttackPressCount <= 0)
            return;

        if (Time.time - lastAttackPressTime < Mathf.Max(0.01f, attackSpeedIdleResetSeconds))
            return;

        ResetRapidAttackSpeed();
    }

    private void ResetRapidAttackSpeed()
    {
        rapidAttackPressCount = 0;
        attackSpeedTier = 0;
    }

    private float ResolveTapAttackSpeed()
    {
        int tier = Mathf.Clamp(attackSpeedTier, 0, Mathf.Max(0, maxAttackSpeedTier));
        float speed = 1f + tier * Mathf.Max(0f, attackSpeedPerTier);

        if (dashCancelSlashQueued)
        {
            dashCancelSlashQueued = false;
            speed *= Mathf.Max(1f, dashCancelSlashSpeedMul);
        }

        return Mathf.Max(0.01f, speed);
    }

    protected override void OnDashEnded()
    {
        dashEndedAt = Time.time;
    }

    protected override void Awake()
    {
        base.Awake();
        SetCharacterId(HarlieCharacterId);

        dashDistance = harlieDashDistance;
        dashSpeed = harlieDashSpeed;
        dashSmoothStopDuration = harlieDashSmoothStop;
        maxAirDashes = harlieMaxAirDashes;
        allowDashJump = harlieAllowDashJump;
        dashJumpMomentumDuration = harlieDashJumpMomentumDuration;
        dashJumpMomentumUntilLanded = harlieDashJumpMomentumUntilLanded;
        enableDashAfterimages = harlieEnableDashAfterimages;
        dashAfterimageCount = harlieAfterimageCount;
        dashAfterimageSpacing = harlieAfterimageSpacing;
        dashAfterimageColor = harlieAfterimageColor;
        dashAfterimageAlphaStart = harlieAfterimageAlphaStart;
        dashAfterimageAlphaEnd = harlieAfterimageAlphaEnd;

        baseMaxHealth = maxHealth;
        baseFallMultiplier = fallMultiplier;
        RefreshUpgradeStats();

        airDashesRemaining = MaxAirDashesWithUpgrades;
        airJumpsRemaining = MaxAirJumps;

        CacheAttackComboParam();
        SetupChargeAura();
        SetupDashAfterimages();
        SetupHitbox();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        SetupHitbox();
    }

    protected override void OnDisable()
    {
        ClearHarlieExtras(instant: false);
        base.OnDisable();
    }

    protected override void ClearForVehicleRide()
    {
        ClearHarlieExtras(instant: true);
        base.ClearForVehicleRide();
    }

    private void ClearHarlieExtras(bool instant)
    {
        StopHarlieChargeAudio();
        SetChargeAuraVisible(false);
        EndJumpAttack();
        CancelChargeKick();
        EndMeleeImmediate();
        SoundManager.Instance?.StopHarlieSwordSwingImmediate();
        DismissSilverSwords(instant);
    }

    protected override void LateUpdate()
    {
        base.LateUpdate();
        MirrorAttackBoxForFacing();
    }

    protected override void UpdateAnimator()
    {
        if (animator != null)
            animator.speed = IsMeleeAttacking ? AttackSpeed() : StyleSpeedMultiplier();

        base.UpdateAnimator();
        if (animator == null)
            return;

        bool attacking = IsMeleeAttacking || Time.time < airSlashHoldUntil;
        animator.SetBool("IsAttacking", attacking);

        if (hasAttackComboParam)
            animator.SetInteger("AttackCombo", IsMeleeAttacking ? currentComboIndex : nextComboIndex);
    }

    protected override bool BlocksRunAnimationWhileShooting()
    {
        return IsMeleeAttacking && !currentAttackWasAir && lockMoveDuringGroundAttack;
    }

    protected override void OnHitStunStarted()
    {
        StopHarlieChargeAudio();
        SetChargeAuraVisible(false);
        EndJumpAttack();
        CancelChargeKick();
        EndMeleeImmediate();
        base.OnHitStunStarted();
    }

    protected override void HandleCharacterUpdate()
    {
        RefreshUpgradeStats();

        if (attackCooldownTimer > 0f)
            attackCooldownTimer -= Time.deltaTime;

        if (comboTimer > 0f)
        {
            comboTimer -= Time.deltaTime;
            if (comboTimer <= 0f)
                nextComboIndex = ComboKick;
        }

        if (isChargingKick)
        {
            chargeKickTimer = Mathf.Min(chargeKickTimer + Time.deltaTime, Mathf.Max(0.05f, chargeKickMaxSeconds));
            UpdateChargeAura(chargeKickTimer);
        }
        else
        {
            SetChargeAuraVisible(false);
        }

        TickRapidAttackSpeedIdleReset();
        TickMelee(Time.deltaTime);
        UpdateJumpAttack();
        UpdateSilverSwords();
    }

    // ---------- Silver Swords ----------

    /// <summary>Hex has her own Up / Down + Attack moves.</summary>
    protected virtual bool SilverSwordsAllowed => true;

    private bool SilverSwordsUnlocked =>
        SilverSwordsAllowed && silverSwords.swordPrefab != null &&
        HyperLevel >= Mathf.Max(1, silverSwordMinHyperLevel) && PlayerUpgrades.IsHyperActive(this);

    private void UpdateSilverSwords()
    {
        bool unlocked = SilverSwordsUnlocked;
        if (silverSwordSquad == null)
        {
            if (unlocked)
                silverSwordSquad = SilverSwordSquad.Create(transform, silverSwords, groundLayers);
            return;
        }

        if (!unlocked && !silverSwordSquad.IsAttacking)
            DismissSilverSwords();
    }

    private void DismissSilverSwords(bool instant = false)
    {
        if (silverSwordSquad != null)
        {
            if (instant)
                silverSwordSquad.DespawnNow();
            else
                silverSwordSquad.Dismiss();
        }
        silverSwordSquad = null;
    }

    /// <summary>Up + Attack: base attack. Down + Attack: Grandmother Silk. Only while the swords are in place.</summary>
    private bool TryUseSilverSwords()
    {
        if (silverSwordSquad == null || isChargingKick || !silverSwordSquad.IsReady)
            return false;

        if (IsAimingUp())
            return silverSwordSquad.TryBaseAttack();
        if (IsAimingDown())
            return silverSwordSquad.TryGrandmotherSilk(FindSilverSwordTarget);
        return false;
    }

    private Bounds FindSilverSwordTarget()
    {
        Bounds body = bodyCollider != null ? bodyCollider.bounds : new Bounds(transform.position, Vector3.one);
        if (SilverSwordSquad.TryFindClosestEnemy(body.center, silverSwordTargetRadius, out Bounds enemy))
            return enemy;
        return new Bounds(body.center + Vector3.right * (facingSign * 5f), body.size);
    }

    protected override void HandleCharacterFixedUpdate()
    {
        if (isChargeSlideActive)
            UpdateChargeSlideFixed();

        if (isAttackLunging)
            UpdateAttackLungeFixed();

        if (isDashing && HeavyStyleLevel > 0)
            TickHeavyDashPush();
    }

    /// <summary>Heavy Style: the dash shoves (and chips) moving common enemies it passes through. Bosses never move.</summary>
    private void TickHeavyDashPush()
    {
        if (bodyCollider == null || heavyDashPushPerLevel <= 0f)
            return;

        ContactFilter2D filter = new ContactFilter2D();
        filter.useTriggers = true;
        filter.NoFilter();

        DashPushOverlaps.Clear();
        Bounds body = bodyCollider.bounds;
        Physics2D.OverlapBox(body.center, body.size, 0f, filter, DashPushOverlaps);
        float distance = Mathf.Max(0f, heavyDashPushPerLevel) * HeavyStyleLevel;
        for (int i = 0; i < DashPushOverlaps.Count; i++)
        {
            Collider2D c = DashPushOverlaps[i];
            if (c == null || c.transform.IsChildOf(transform) || EnemyDetectionZone.IsDetectionOnlyCollider(c))
                continue;
            if (c.GetComponent<AttackHitbox>() != null || c.GetComponentInParent<Boss>() != null)
                continue;

            ICommonEnemy enemy = c.GetComponentInParent<ICommonEnemy>();
            if (enemy is not Component enemyComponent || enemy.IsDead || MaliceSlashProjectile.IsStationaryEnemy(enemy))
                continue;
            if (!dashPushedIds.Add(enemyComponent.GetInstanceID()))
                continue;

            if (heavyDashPushDamage > 0)
                enemy.TakeDamage(heavyDashPushDamage);
            if (!enemy.IsDead)
                GentleKnockback.Apply(enemyComponent, dashDirSign, distance, heavyDashPushDuration, ChainKillActive);
        }
    }

    protected override void ApplyHorizontalMove()
    {
        if (isChargeSlideActive)
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            return;
        }

        if (isAttackLunging)
            return;

        if (lockMoveDuringGroundAttack && isGrounded && IsMeleeAttacking && !currentAttackWasAir)
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            return;
        }

        // Air slash keeps jump / dash drift — only overwrite X when the player steers.
        if (IsAirSlashMomentumActive())
        {
            if (Mathf.Abs(moveInput.x) > 0.01f || isDashJumping)
                base.ApplyHorizontalMove();
            return;
        }

        base.ApplyHorizontalMove();
    }

    private bool IsAirSlashMomentumActive()
    {
        if (IsMeleeAttacking && currentAttackWasAir)
            return true;

        return !isGrounded && Time.time < airSlashHoldUntil;
    }

    protected override void OnAttackStarted(InputAction.CallbackContext context)
    {
        if (InputLocked || isStunned)
            return;

        if (TryUseSilverSwords())
            return;

        if (attackCooldownTimer > 0f && !IsMeleeAttacking && !isChargingKick)
            return;

        // Mid-dash or right after a dash: the next swing is faster (and the dash is canceled).
        if (isDashing || Time.time - dashEndedAt <= Mathf.Max(0f, dashCancelSlashWindow))
            dashCancelSlashQueued = true;

        if (isDashing)
            CancelDash(keepHorizontalMomentum: true);

        RegisterRapidAttackPress();

        // Mid-combo tap chains the next ground swing immediately.
        if (IsMeleeAttacking && !currentAttackWasAir && comboTimer > 0f)
        {
            BeginAttack();
            return;
        }

        // Not mid-swing (ground or air): hold to charge (release fires via OnAttackCanceled).
        if (!IsMeleeAttacking)
        {
            CancelChargeKick();
            isChargingKick = true;
            chargeKickTimer = 0f;
            SetChargeAuraVisible(false);
            SoundManager.Instance?.StartHarlieChargeKickLoop();
            return;
        }

        BeginAttack();
    }

    protected override void OnAttackCanceled(InputAction.CallbackContext context)
    {
        if (InputLocked || isStunned)
            return;

        if (!isChargingKick)
            return;

        isChargingKick = false;
        float held = chargeKickTimer;
        chargeKickTimer = 0f;
        StopHarlieChargeAudio();
        SetChargeAuraVisible(false);

        if (!isGrounded)
        {
            float ratio = held / Mathf.Max(0.05f, chargeKickMaxSeconds);
            BeginAttack(chargedAirSlash: held >= chargeKickMinHoldSeconds && ratio >= chargedAirSlashMinRatio);
            return;
        }

        if (held >= chargeKickMinHoldSeconds)
            BeginChargedRelease(held);
        else
            BeginAttack();
    }

    protected override void OnDashStarted()
    {
        // A held charge carries through the dash.
        ResetRapidAttackSpeed();
        dashPushedIds.Clear();
        EndJumpAttack();
        if (IsMeleeAttacking)
            EndMeleeImmediate();
    }

    protected void CancelChargeKick()
    {
        isChargingKick = false;
        chargeKickTimer = 0f;
        SetChargeAuraVisible(false);
        StopHarlieChargeAudio();
    }

    private void StopHarlieChargeAudio()
    {
        SoundManager.Instance?.StopHarlieChargeKickLoopImmediate();
    }

    private void CacheAttackComboParam()
    {
        hasAttackComboParam = false;
        if (animator == null)
            return;

        foreach (AnimatorControllerParameter param in animator.parameters)
        {
            if (param.name == "AttackCombo")
            {
                hasAttackComboParam = true;
                break;
            }
        }
    }

    private void SetupHitbox()
    {
        if (attackHitbox == null)
            attackHitbox = GetComponentInChildren<AttackHitbox>(true);

        if (attackHitbox == null)
            return;

        attackHitbox.SetOwner(this);
        attackHitbox.Deactivate();
        attackBoxHasLastWritten = false;
    }

    private void MirrorAttackBoxForFacing()
    {
        if (attackHitbox == null)
            return;

        MirrorChildLocalForFacing(
            attackHitbox.transform,
            ref attackBoxLeftFacingLocal,
            ref attackBoxLastWrittenLocal,
            ref attackBoxHasLastWritten);
    }

    private void BeginAttack(bool chargedAirSlash = false)
    {
        EndJumpAttack();

        bool continuingGroundCombo = IsMeleeAttacking && !currentAttackWasAir && comboTimer > 0f;
        bool inAir = !isGrounded && !continuingGroundCombo;

        EndMeleeImmediate();

        currentMeleeSpeed = ResolveTapAttackSpeed();
        float attackSpeed = AttackSpeed();

        currentAttackWasAir = inAir;
        meleeTimer = 0f;
        hitboxArmed = true;

        ReportTutorialAction(TutorialAction.Attack);
        if (inAir)
            ReportTutorialAction(TutorialAction.AirAttack);
        if (chargedAirSlash)
            ReportTutorialAction(TutorialAction.ChargeAttack);
        hitboxOpened = false;
        hitboxClosed = false;
        slashWaveFired = false;
        chargeSlideFiresWave = false;
        airSlashHoldUntil = 0f;

        if (inAir)
        {
            currentComboIndex = ComboKick;
            nextComboIndex = ComboKick;
            comboTimer = 0f;
            meleeState = MeleeState.AirSlash;
            currentChargedAirSlash = chargedAirSlash;
            currentDamageMultiplier = chargedAirSlash ? Mathf.Max(1, chargedAirSlashDamageMultiplier) : 1;
            pendingHitDamage = ScaleOutgoingDamage(airSlashDamage) * currentDamageMultiplier;
            meleeDuration = ResolveDuration("air slash", airSlashDuration) / attackSpeed;
        }
        else
        {
            if (comboTimer <= 0f)
                nextComboIndex = ComboKick;

            currentComboIndex = nextComboIndex;
            nextComboIndex = (nextComboIndex + 1) % ComboStepCount;
            comboTimer = Mathf.Max(0.05f, comboInputWindow);

            switch (currentComboIndex)
            {
                case ComboSlash1:
                    meleeState = MeleeState.Slash1;
                    pendingHitDamage = ScaleOutgoingDamage(slash1Damage);
                    meleeDuration = ResolveDuration("slash 1", slash1Duration) / attackSpeed;
                    break;
                case ComboSlash2:
                    meleeState = MeleeState.Slash2;
                    pendingHitDamage = ScaleOutgoingDamage(slash2Damage);
                    meleeDuration = ResolveDuration("slash 2", slash2Duration) / attackSpeed;
                    break;
                case ComboSlash3:
                    meleeState = MeleeState.Slash3;
                    pendingHitDamage = ScaleOutgoingDamage(slash3Damage);
                    meleeDuration = ResolveDuration("slash 3", slash3Duration) / attackSpeed;
                    break;
                default:
                    meleeState = MeleeState.Kick;
                    pendingHitDamage = ScaleOutgoingDamage(kickDamage);
                    meleeDuration = ResolveDuration("kick", kickDuration) / attackSpeed;
                    break;
            }

            BeginAttackLunge(currentComboIndex);
        }

        if (animator != null)
        {
            if (hasAttackComboParam)
                animator.SetInteger("AttackCombo", currentComboIndex);

            animator.SetBool("IsAttacking", true);
            animator.SetBool("IsGrounded", !inAir);
            animator.SetBool("IsInAir", inAir);
            animator.ResetTrigger("Attack");
            animator.SetTrigger("Attack");
            animator.Play(GetAttackStateName(inAir, currentComboIndex), 0, 0f);
        }

        PlayHarlieSlashSound(inAir, currentComboIndex);
        attackBoxHasLastWritten = false;

        if (!chargedAirSlash)
            OnRegularAttackStarted();
    }

    /// <summary>A tap slash, air slash or kick just started (not charged attacks or the pogo).</summary>
    protected virtual void OnRegularAttackStarted() { }

    private void BeginChargedRelease(float heldSeconds)
    {
        EndJumpAttack();
        EndMeleeImmediate();

        float ratio = Mathf.Clamp01(heldSeconds / Mathf.Max(0.05f, chargeKickMaxSeconds));
        float distance = ratio * chargeKickMaxSpaces * chargeKickSpaceSize;
        if (distance <= 0.01f)
        {
            BeginAttack();
            return;
        }

        ResetRapidAttackSpeed();
        dashCancelSlashQueued = false;
        slashWaveFired = false;
        ReportTutorialAction(TutorialAction.ChargeAttack);

        float slideDuration = ComputeChargeSlideDuration(distance);
        if (ratio <= chargeSlashMaxRatio)
            BeginChargeSlashSlide(distance, slideDuration);
        else
            BeginChargeKickSlide(distance, slideDuration);
    }

    private float ComputeChargeSlideDuration(float distance)
    {
        float slideSpeed = Mathf.Max(0.01f, harlieDashSpeed * chargeAttackSpeedVsDash * StyleSpeedMultiplier());
        return Mathf.Max(0.06f, distance / slideSpeed);
    }

    private void BeginChargeKickSlide(float distance, float slideDuration)
    {
        currentAttackWasAir = false;
        currentComboIndex = ComboKick;
        nextComboIndex = ComboSlash1;
        comboTimer = Mathf.Max(0.05f, comboInputWindow);
        meleeState = MeleeState.Kick;
        pendingHitDamage = ScaleOutgoingDamage(chargeKickDamage);
        chargeSlideUsesKick = true;
        chargeSlideFiresWave = false;
        BeginChargeSlide(distance, slideDuration, useKickAnim: true);
        PlayHarlieSlashSound(inAir: false, ComboKick);
    }

    private void BeginChargeSlashSlide(float distance, float slideDuration)
    {
        currentAttackWasAir = false;
        currentComboIndex = ComboSlash1;
        nextComboIndex = ComboSlash2;
        comboTimer = Mathf.Max(0.05f, comboInputWindow);
        meleeState = MeleeState.Slash1;
        pendingHitDamage = ScaleOutgoingDamage(chargeSlashDamage);
        chargeSlideUsesKick = false;
        chargeSlideFiresWave = true;
        BeginChargeSlide(distance, slideDuration, useKickAnim: false);
        PlayHarlieSlashSound(inAir: false, ComboSlash1);
    }

    private void BeginChargeSlide(float distance, float slideDuration, bool useKickAnim)
    {
        if (rb == null)
            return;

        chargeSlideDuration = Mathf.Max(Time.fixedDeltaTime * 2f, slideDuration);
        chargeSlideStartX = rb.position.x;
        chargeSlideEndX = chargeSlideStartX + FacingSign * distance;
        chargeSlideTimer = 0f;
        isChargeSlideActive = true;

        meleeTimer = 0f;
        meleeDuration = chargeSlideDuration + Mathf.Max(0f, chargeKickHoldAfterSlide) / StyleSpeedMultiplier();
        hitboxArmed = true;
        hitboxOpened = true;
        hitboxClosed = false;
        attackHitbox?.Activate(pendingHitDamage, applyDamage: true);

        if (animator != null)
        {
            if (hasAttackComboParam)
                animator.SetInteger("AttackCombo", useKickAnim ? ComboKick : ComboSlash1);

            animator.SetBool("IsAttacking", true);
            animator.SetBool("IsGrounded", true);
            animator.SetBool("IsInAir", false);
            animator.ResetTrigger("Attack");
            animator.SetTrigger("Attack");
            animator.Play(useKickAnim ? "kick" : "slash 1", 0, 0f);
        }

        attackBoxHasLastWritten = false;
    }

    private void UpdateChargeSlideFixed()
    {
        if (rb == null || !isChargeSlideActive)
            return;

        chargeSlideTimer += Time.fixedDeltaTime;
        float t = chargeSlideDuration <= 0f
            ? 1f
            : Mathf.Clamp01(chargeSlideTimer / chargeSlideDuration);
        float eased = ChargeEaseOut(t);

        Vector2 pos = rb.position;
        float prevX = pos.x;
        pos.x = Mathf.Lerp(chargeSlideStartX, chargeSlideEndX, eased);
        rb.MovePosition(pos);
        chargeSlideVelocityX = (pos.x - prevX) / Mathf.Max(Time.fixedDeltaTime, 0.0001f);
        rb.linearVelocity = new Vector2(chargeSlideVelocityX, rb.linearVelocity.y);

        if (chargeSlideTimer < chargeSlideDuration)
            return;

        pos.x = chargeSlideEndX;
        rb.MovePosition(pos);
        chargeSlideVelocityX = 0f;
        rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
        isChargeSlideActive = false;
    }

    private static void PlayHarlieSlashSound(bool inAir, int combo)
    {
        if (SoundManager.Instance == null)
            return;

        // Match Little Program Harlie: kick is silent; slashes + air slash use sword swing.
        if (inAir || combo == ComboSlash1 || combo == ComboSlash2 || combo == ComboSlash3)
            SoundManager.Instance.PlayHarlieSwordSwing();
    }

    protected float ResolveDuration(string stateName, float fallback)
    {
        if (!syncAttackDurationToAnimation || animator == null)
            return Mathf.Max(0.05f, fallback);

        RuntimeAnimatorController rac = animator.runtimeAnimatorController;
        if (rac == null)
            return Mathf.Max(0.05f, fallback);

        AnimationClip[] clips = rac.animationClips;
        for (int i = 0; i < clips.Length; i++)
        {
            AnimationClip clip = clips[i];
            if (clip == null)
                continue;
            if (!string.Equals(clip.name, stateName, System.StringComparison.OrdinalIgnoreCase))
                continue;
            return Mathf.Max(0.05f, clip.length + Mathf.Max(0f, attackDurationPadding));
        }

        return Mathf.Max(0.05f, fallback);
    }

    private static string GetAttackStateName(bool inAir, int combo)
    {
        if (inAir)
            return "air slash";

        return combo switch
        {
            ComboSlash1 => "slash 1",
            ComboSlash2 => "slash 2",
            ComboSlash3 => "slash 3",
            _ => "kick"
        };
    }

    private void TickMelee(float dt)
    {
        if (!IsMeleeAttacking)
            return;

        meleeTimer += dt;
        float attackSpeed = AttackSpeed();

        if (chargeSlideFiresWave && !slashWaveFired && !isChargeSlideActive && chargeSlideTimer >= chargeSlideDuration)
            FireSlashWave();

        if (!isChargeSlideActive)
        {
            float activeStart = Mathf.Max(0f, hitboxDelay) / attackSpeed;
            float activeEnd = activeStart + Mathf.Max(0.01f, hitboxActiveTime) / attackSpeed;

            if (hitboxArmed && !hitboxOpened && meleeTimer >= activeStart)
            {
                hitboxOpened = true;
                attackHitbox?.Activate(Mathf.Max(0, pendingHitDamage), applyDamage: true);
                if (UsesSlashForCombat())
                    FireSlashWave();
            }

            if (hitboxOpened && !hitboxClosed && meleeTimer >= activeEnd)
            {
                hitboxClosed = true;
                attackHitbox?.Deactivate();
            }
        }
        else if (chargeSlideTimer >= chargeSlideDuration && hitboxOpened && !hitboxClosed)
        {
            hitboxClosed = true;
            attackHitbox?.Deactivate();
        }

        if (meleeTimer < meleeDuration)
            return;

        bool wasAir = currentAttackWasAir;
        EndMeleeImmediate();
        attackCooldownTimer = Mathf.Max(0f, timeBetweenAttacks) / attackSpeed;

        if (wasAir)
            airSlashHoldUntil = Time.time + Mathf.Max(0f, airSlashAnimHold) / attackSpeed;
    }

    private void FireSlashWave()
    {
        if (slashWaveFired)
            return;

        slashWaveFired = true;
        if (slashWave == null)
            return;

        int damage = Mathf.Max(0, pendingHitDamage)
                     + Mathf.Max(0, heavyWaveDamagePerLevel) * HeavyStyleLevel * currentDamageMultiplier;
        float distanceMul = currentChargedAirSlash ? Mathf.Max(1f, chargedAirSlashDistanceMultiplier) : 1f;
        Collider2D attackBox = attackHitbox != null ? attackHitbox.GetComponent<Collider2D>() : null;

        bool chargedSlash = currentChargedAirSlash || chargeSlideFiresWave;
        if (!tripleWaveOnChargedSlash || !chargedSlash)
        {
            FireWaveWithUpgrades(attackBox, null, damage, distanceMul, null, null);
            return;
        }

        MaliceSlashVolley volley = new MaliceSlashVolley();
        float rad = tripleWaveAngle * Mathf.Deg2Rad;
        Vector2 up = new Vector2(FacingSign * Mathf.Cos(rad), Mathf.Sin(rad));
        Vector2 down = new Vector2(up.x, -up.y);
        FireWaveWithUpgrades(attackBox, null, damage, distanceMul, null, volley);
        FireWaveWithUpgrades(attackBox, null, damage, distanceMul, up, volley);
        FireWaveWithUpgrades(attackBox, null, damage, distanceMul, down, volley);
    }

    /// <summary>
    /// One slash wave with Hyper / Speed / Heavy wave bonuses applied. Fires from the attack box front edge
    /// unless a spawn point is given; direction null = straight ahead.
    /// </summary>
    protected MaliceSlashProjectile FireWaveWithUpgrades(Collider2D attackBox, Vector3? spawn, int damage,
        float distanceMultiplier, Vector2? direction, MaliceSlashVolley volley,
        float bonusDistance = 0f, float speedMultiplier = 1f)
    {
        if (slashWave == null)
            return null;

        int hyper = ActiveHyperLevel;
        int speedLevel = SpeedStyleLevel;
        int heavyLevel = HeavyStyleLevel;
        float extraDistance = Mathf.Max(0f, hyperWaveDistancePerLevel) * hyper
                              + Mathf.Max(0f, speedStyleWaveDistancePerLevel) * speedLevel
                              + Mathf.Max(0f, bonusDistance);
        float extraSpeed = Mathf.Max(0f, speedStyleWaveSpeedPerLevel) * speedLevel;
        float size = 1f + Mathf.Max(0f, hyperWaveSizePerLevel) * hyper
                        + Mathf.Max(0f, heavyWaveSizePerLevel) * heavyLevel;

        return slashWave.Fire(transform, attackBox, FacingSign, damage, spriteRenderer,
            distanceMultiplier, extraDistance, extraSpeed, size, SlashKnockbackDistance, ChainKillActive,
            direction, volley, spawn, speedMultiplier);
    }

    protected void EndMeleeImmediate()
    {
        StopAttackLunge();
        isChargeSlideActive = false;
        chargeSlideVelocityX = 0f;
        if (!isJumpAttackActive)
            attackHitbox?.Deactivate();
        meleeState = MeleeState.Idle;
        meleeTimer = 0f;
        meleeDuration = 0f;
        pendingHitDamage = 0;
        hitboxArmed = false;
        hitboxOpened = false;
        hitboxClosed = false;
        currentAttackWasAir = false;
        chargeSlideFiresWave = false;
        currentMeleeSpeed = 1f;
        currentChargedAirSlash = false;
        currentDamageMultiplier = 1;
    }

    private void BeginAttackLunge(int combo)
    {
        float distance = GetAttackLungeDistance(combo);
        if (distance <= 0f || rb == null)
        {
            StopAttackLunge();
            return;
        }

        float duration;
        float speedMul = AttackSpeed();
        if (combo == ComboKick)
        {
            float kickSpeed = Mathf.Max(0.01f, harlieDashSpeed * Mathf.Max(1.01f, kickSpeedVsDash) * speedMul);
            duration = distance / kickSpeed;
        }
        else
        {
            duration = Mathf.Min(meleeDuration * 0.45f, Mathf.Max(0.03f, distance / (moveSpeed * 4f * speedMul)));
        }

        duration = Mathf.Max(Time.fixedDeltaTime * 2f, duration);

        isAttackLunging = true;
        attackLungeTimer = 0f;
        attackLungeDuration = duration;
        attackLungeStartX = rb.position.x;
        attackLungeEndX = rb.position.x + FacingSign * distance;
        attackLungeVelocityX = FacingSign * (distance / attackLungeDuration);
    }

    private float GetAttackLungeDistance(int combo)
    {
        return combo switch
        {
            ComboSlash1 => slash1LungeDistance,
            ComboSlash2 => slash2LungeDistance,
            ComboSlash3 => slash3LungeDistance,
            _ => kickLungeDistance
        };
    }

    private void UpdateAttackLungeFixed()
    {
        if (rb == null)
        {
            StopAttackLunge();
            return;
        }

        attackLungeTimer += Time.fixedDeltaTime;
        float t = attackLungeDuration <= 0f
            ? 1f
            : Mathf.Clamp01(attackLungeTimer / attackLungeDuration);
        float eased = SmootherStep(t);

        Vector2 pos = rb.position;
        float prevX = pos.x;
        pos.x = Mathf.Lerp(attackLungeStartX, attackLungeEndX, eased);
        if (!currentAttackWasAir)
            pos.y = rb.position.y;

        rb.MovePosition(pos);
        attackLungeVelocityX = (pos.x - prevX) / Mathf.Max(Time.fixedDeltaTime, 0.0001f);
        rb.linearVelocity = new Vector2(attackLungeVelocityX, currentAttackWasAir ? rb.linearVelocity.y : 0f);

        if (attackLungeTimer < attackLungeDuration)
            return;

        pos.x = attackLungeEndX;
        rb.MovePosition(pos);
        StopAttackLunge();
    }

    private void StopAttackLunge()
    {
        if (!isAttackLunging)
            return;

        isAttackLunging = false;
        attackLungeVelocityX = 0f;
        attackLungeTimer = 0f;

        if (rb != null && IsMeleeAttacking && !currentAttackWasAir)
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
    }

    private void UpdateJumpAttack()
    {
        bool wantJumpAttack = !isGrounded && !isDashing && !IsMeleeAttacking
            && !isChargeSlideActive && Time.time >= airSlashHoldUntil;

        if (wantJumpAttack)
            BeginJumpAttack();
        else
            EndJumpAttack();
    }

    private void BeginJumpAttack()
    {
        if (isJumpAttackActive || attackHitbox == null)
            return;

        isJumpAttackActive = true;
        attackHitbox.Activate(JumpAttackDamage, applyDamage: true);
    }

    /// <summary>Re-arms the falling attack with the current JumpAttackDamage and a fresh hit list.</summary>
    protected void RestartJumpAttack()
    {
        EndJumpAttack();
        UpdateJumpAttack();
    }

    /// <summary>Ends the post-air-slash pose hold so fall / jump attack resume right away.</summary>
    protected void ClearAirSlashHold()
    {
        airSlashHoldUntil = 0f;
    }

    private void EndJumpAttack()
    {
        if (!isJumpAttackActive)
            return;

        isJumpAttackActive = false;
        if (!IsMeleeAttacking && !isChargeSlideActive)
            attackHitbox?.Deactivate();
    }

    public bool CanReflectProjectilesWithSlash()
    {
        return attackHitbox != null && attackHitbox.IsActive && UsesSlashForReflect();
    }

    public override bool TryHandleProjectileContact(Projectile projectile, Collider2D hitCollider)
    {
        if (projectile == null || attackHitbox == null || !attackHitbox.IsActive)
            return false;

        if (UsesSlashForReflect())
        {
            projectile.ReflectFromDeflector(transform);
            PlaySlashClashSound();
            return true;
        }

        // Jump attack / kick / charge kick — clash only (no reflect, no damage to Harlie).
        projectile.ClashAndDespawn();
        PlaySlashClashSound();

        if (isJumpAttackActive)
        {
            if (CanPogoNow())
            {
                Collider2D pogoCollider = hitCollider != null
                    ? hitCollider
                    : projectile.GetComponent<Collider2D>();
                BounceOffTarget(pogoCollider);
                attackHitbox.ForgetHitInstance(projectile.GetInstanceID());
            }
        }

        return true;
    }

    /// <summary>Called from AttackHitbox after a meaningful melee contact.</summary>
    public void NotifyMeleeContact(Collider2D other, bool bossAttackTool)
    {
        if (other == null)
            return;

        if (UsesSlashForCombat())
            PlaySlashClashSound();

        if (ShouldApplyAttackKnockback())
            PushTargetOnHit(other);

        if (!CanPogoNow())
            return;

        BounceOffTarget(other);
        attackHitbox?.ForgetHitInstance(ResolvePogoTargetId(other));
    }

    private bool ShouldApplyAttackKnockback()
    {
        return isJumpAttackActive || IsMeleeAttacking;
    }

    /// <summary>Malice's push rule: a short shove on moving common enemies only (never bosses or stationary enemies).</summary>
    private void PushTargetOnHit(Collider2D hitCollider)
    {
        float distance = SlashKnockbackDistance;
        if (hitCollider == null || distance <= 0f)
            return;

        if (hitCollider.GetComponentInParent<Projectile>() != null
            || hitCollider.GetComponentInParent<Boss>() != null
            || hitCollider.GetComponentInParent<Crystal>() != null
            || hitCollider.GetComponentInParent<PlayerController>() != null)
            return;

        AttackHitbox tool = hitCollider.GetComponentInParent<AttackHitbox>();
        if (tool != null && tool.OwnerBoss != null)
            return;

        ICommonEnemy enemy = hitCollider.GetComponentInParent<ICommonEnemy>();
        if (enemy is not Component enemyComponent || enemy.IsDead || MaliceSlashProjectile.IsStationaryEnemy(enemy))
            return;

        GentleKnockback.Apply(enemyComponent, FacingSign, distance, attackKnockbackDuration, ChainKillActive);
    }

    private bool UsesSlashForCombat()
    {
        return UsesSlashForReflect();
    }

    private bool UsesSlashForReflect()
    {
        if (!IsMeleeAttacking)
            return false;

        if (currentAttackWasAir)
            return true;

        return currentComboIndex == ComboSlash1
            || currentComboIndex == ComboSlash2
            || currentComboIndex == ComboSlash3;
    }

    protected virtual bool CanPogoNow()
    {
        if (isGrounded || rb == null)
            return false;

        if (rb.linearVelocity.y > 0.05f)
            return false;

        bool airSlashActive = (IsMeleeAttacking && currentAttackWasAir) || Time.time < airSlashHoldUntil;
        if (airSlashActive)
            return false;

        return isJumpAttackActive;
    }

    private static int ResolvePogoTargetId(Collider2D other)
    {
        if (other == null)
            return 0;

        Boss boss = other.GetComponent<Boss>();
        if (boss == null)
            boss = other.GetComponentInParent<Boss>();

        if (boss != null)
            return boss.GetInstanceID();

        Crystal crystal = other.GetComponent<Crystal>();
        if (crystal == null)
            crystal = other.GetComponentInParent<Crystal>();

        if (crystal != null)
            return crystal.GetInstanceID();

        Projectile projectile = other.GetComponent<Projectile>();
        if (projectile == null)
            projectile = other.GetComponentInParent<Projectile>();

        if (projectile != null)
            return projectile.GetInstanceID();

        AttackHitbox tool = other.GetComponent<AttackHitbox>();
        if (tool == null)
            tool = other.GetComponentInParent<AttackHitbox>();

        if (tool != null)
            return tool.GetInstanceID();

        return other.transform.root != null
            ? other.transform.root.GetInstanceID()
            : other.GetInstanceID();
    }

    public override void TakeDamage(int amount, Transform hitSource)
    {
        TakeDamage(amount, hitSource, applyKnockback: true);
    }

    public override void TakeDamage(int amount, Transform hitSource, bool applyKnockback)
    {
        if (IsInvincibleFromMovement())
            return;

        if (IsHitCoveredByActiveAttackBox(hitSource))
        {
            slashGuardedSource = hitSource;
            slashGuardedAt = Time.time;
            return;
        }

        // Harlie is never pushed back by hits.
        base.TakeDamage(amount, hitSource, applyKnockback: false);
    }

    /// <summary>Dash, pogo guard, and (max Ariel Action) the post-pogo fall.</summary>
    private bool IsInvincibleFromMovement()
    {
        return (dashInvincible && isDashing) || Time.time < pogoInvincibleUntil || pogoInvincibleUntilLanded;
    }

    protected override bool ShouldIgnoreSoftBounceFrom(Transform source)
    {
        if (IsInvincibleFromMovement())
            return true;

        if (source == null || slashGuardedSource == null || Time.time - slashGuardedAt > 0.1f)
            return false;

        return source == slashGuardedSource
               || source.IsChildOf(slashGuardedSource)
               || slashGuardedSource.IsChildOf(source);
    }

    /// <summary>
    /// True when an enemy/boss only touches the part of Harlie's body that her active
    /// attack box overlaps — she's winning that exchange, so the contact can't hurt her.
    /// </summary>
    private bool IsHitCoveredByActiveAttackBox(Transform hitSource)
    {
        if (!slashGuardEnabled || hitSource == null || bodyCollider == null ||
            attackHitbox == null || !attackHitbox.IsActive || GetGuardEnemyRoot(hitSource) == null)
            return false;

        Collider2D attackCollider = attackHitbox.GetComponent<Collider2D>();
        if (attackCollider == null || !attackCollider.enabled)
            return false;

        Bounds guardZone = attackCollider.bounds;
        guardZone.Expand(slashGuardTolerance * 2f);
        Bounds body = bodyCollider.bounds;

        CollectGuardSourceColliders(hitSource);

        bool anyContact = false;
        for (int i = 0; i < SlashGuardSourceColliders.Count; i++)
        {
            Collider2D c = SlashGuardSourceColliders[i];
            if (c.Distance(bodyCollider).distance > SlashGuardContactSkin)
                continue;

            Bounds cb = c.bounds;
            cb.Expand(SlashGuardContactSkin * 2f);
            if (!cb.Intersects(body))
                continue;

            Vector3 min = Vector3.Max(cb.min, body.min);
            Vector3 max = Vector3.Min(cb.max, body.max);
            anyContact = true;

            bool insideGuard = min.x >= guardZone.min.x && max.x <= guardZone.max.x &&
                               min.y >= guardZone.min.y && max.y <= guardZone.max.y;
            if (!insideGuard)
                return false;
        }

        return anyContact;
    }

    private static Transform GetGuardEnemyRoot(Transform t)
    {
        Boss boss = t.GetComponentInParent<Boss>();
        if (boss != null)
            return boss.transform;

        return t.GetComponentInParent<ICommonEnemy>() is Component enemy ? enemy.transform : null;
    }

    private static void CollectGuardSourceColliders(Transform hitSource)
    {
        SlashGuardSourceColliders.Clear();
        hitSource.GetComponentsInChildren(false, SlashGuardSourceColliders);
        for (int i = SlashGuardSourceColliders.Count - 1; i >= 0; i--)
        {
            Collider2D c = SlashGuardSourceColliders[i];
            if (c == null || !c.enabled || EnemyDetectionZone.IsDetectionOnlyCollider(c))
                SlashGuardSourceColliders.RemoveAt(i);
        }
    }

    private void BounceOffTarget(Collider2D hitCollider)
    {
        if (rb == null)
            return;

        ReportTutorialAction(TutorialAction.Pogo);
        if (pogoRefreshesDoubleJump)
            airJumpsRemaining = MaxAirJumps;

        pogoInvincibleUntil = Time.time + Mathf.Max(0f, pogoGuardSeconds);
        if (AerialLevel >= PlayerUpgrades.MaxLevel)
            pogoInvincibleUntilLanded = true;

        SeparateFromTargetForPogo(hitCollider);

        float bounce = Mathf.Max(0.1f, aerialBounceForce);
        float awayX = rb.linearVelocity.x;

        Transform targetTransform = hitCollider != null ? hitCollider.transform : null;
        if (targetTransform != null)
        {
            float away = Mathf.Sign(transform.position.x - targetTransform.position.x);
            if (Mathf.Abs(away) < 0.01f)
                away = -FacingSign;
            awayX = away * moveSpeed * 0.35f;
        }

        rb.linearVelocity = new Vector2(awayX, bounce);
    }

    private void SeparateFromTargetForPogo(Collider2D hitCollider)
    {
        if (rb == null)
            return;

        Collider2D targetCol = ResolvePogoSeparationCollider(hitCollider);
        Collider2D ourCol = bodyCollider != null ? bodyCollider : GetComponent<Collider2D>();
        if (targetCol == null || ourCol == null)
        {
            rb.position = rb.position + Vector2.up * 0.12f;
            return;
        }

        Bounds targetBounds = targetCol.bounds;
        Bounds ourBounds = ourCol.bounds;
        Vector2 pos = rb.position;

        float targetTop = targetBounds.max.y;
        float ourBottom = ourBounds.min.y;
        if (ourBottom < targetTop + 0.02f)
            pos.y += targetTop - ourBottom + 0.1f;

        float away = Mathf.Sign(pos.x - targetBounds.center.x);
        if (Mathf.Abs(away) < 0.01f)
            away = -FacingSign;

        float horizontalOverlap = (targetBounds.extents.x + ourBounds.extents.x)
            - Mathf.Abs(pos.x - targetBounds.center.x);
        if (horizontalOverlap > 0f)
            pos.x += away * (horizontalOverlap * 0.5f + 0.04f);

        rb.position = pos;
    }

    private static Collider2D ResolvePogoSeparationCollider(Collider2D hitCollider)
    {
        if (hitCollider == null)
            return null;

        if (!hitCollider.isTrigger)
            return hitCollider;

        Boss boss = hitCollider.GetComponentInParent<Boss>();
        if (boss != null)
        {
            Collider2D[] cols = boss.GetComponentsInChildren<Collider2D>();
            for (int i = 0; i < cols.Length; i++)
            {
                Collider2D col = cols[i];
                if (col != null && col.enabled && !col.isTrigger)
                    return col;
            }
        }

        if (hitCollider.GetComponentInParent<Projectile>() != null)
            return hitCollider;

        Transform root = hitCollider.transform.root;
        if (root != null)
        {
            Collider2D[] cols = root.GetComponentsInChildren<Collider2D>();
            for (int i = 0; i < cols.Length; i++)
            {
                Collider2D col = cols[i];
                if (col != null && col.enabled && !col.isTrigger)
                    return col;
            }
        }

        return hitCollider;
    }

    private static void PlaySlashClashSound()
    {
        SoundManager.Instance?.PlayHarlieBigSwordClash();
    }

    private static float SmootherStep(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * t * (t * (t * 6f - 15f) + 10f);
    }

    /// <summary>Fast explosive start, decelerates near the end of the charge travel.</summary>
    private static float ChargeEaseOut(float t)
    {
        t = Mathf.Clamp01(t);
        float inv = 1f - t;
        return 1f - inv * inv * inv;
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

    private void UpdateChargeAura(float chargeSeconds)
    {
        if (!showChargeAura)
            return;

        if (chargeAuraRenderer == null || chargeAuraObject == null)
            SetupChargeAura();

        if (chargeAuraRenderer == null || spriteRenderer == null)
            return;

        float charge01 = Mathf.Clamp01(chargeSeconds / Mathf.Max(0.01f, chargeKickMaxSeconds));

        chargeAuraObject.SetActive(true);
        chargeAuraRenderer.sprite = spriteRenderer.sprite;
        chargeAuraRenderer.flipX = spriteRenderer.flipX;
        CharacterEffectSorting.ApplyAuraBehindBody(chargeAuraRenderer, spriteRenderer, EffectSortingGroup);

        float alpha;
        float halfSeconds = chargeKickMaxSeconds * chargeSlashMaxRatio;
        if (chargeSeconds >= chargeKickMaxSeconds)
            alpha = auraAlphaAtFull;
        else if (chargeSeconds >= halfSeconds)
            alpha = Mathf.Lerp(auraAlphaAtHalf, auraAlphaAtFull, Mathf.InverseLerp(halfSeconds, chargeKickMaxSeconds, chargeSeconds));
        else
            alpha = Mathf.Lerp(auraAlphaStart, auraAlphaAtHalf, chargeSeconds / Mathf.Max(0.01f, halfSeconds));

        Color yellow = Color.Lerp(chargeAuraColor, chargeAuraStrongColor, charge01);
        float flickerSpeed = Mathf.Lerp(auraFlickerSpeed, Mathf.Min(auraFlickerSpeed * 1.75f, 2.4f), charge01);
        auraFlickerPhase += Time.deltaTime * flickerSpeed;
        float shimmer = 0.5f + 0.5f * Mathf.Sin(auraFlickerPhase * Mathf.PI * 2f);
        float brightMix = shimmer * auraFlickerStrength * Mathf.Lerp(0.55f, 1f, charge01);
        Color auraColor = Color.Lerp(yellow, chargeAuraFlickerColor, brightMix);
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

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        silverSwordMinHyperLevel = Mathf.Clamp(silverSwordMinHyperLevel, 1, PlayerUpgrades.MaxLevel);
        silverSwordTargetRadius = Mathf.Max(1f, silverSwordTargetRadius);
        silverSwords?.Validate();
        harlieDashDistance = Mathf.Max(0.1f, harlieDashDistance);
        harlieDashSpeed = Mathf.Max(0.1f, harlieDashSpeed);
        harlieMaxAirDashes = Mathf.Max(0, harlieMaxAirDashes);
        comboInputWindow = Mathf.Max(0.05f, comboInputWindow);
        kickLungeDistance = Mathf.Max(0f, kickLungeDistance);
        slash1LungeDistance = Mathf.Max(0f, slash1LungeDistance);
        slash2LungeDistance = Mathf.Max(0f, slash2LungeDistance);
        slash3LungeDistance = Mathf.Max(0f, slash3LungeDistance);
        kickSpeedVsDash = Mathf.Max(1.01f, kickSpeedVsDash);
        speedStyleMultiplier = Mathf.Max(0.1f, speedStyleMultiplier);
        speedStyleDashJumpMultiplier = Mathf.Clamp(speedStyleDashJumpMultiplier, 0.1f, speedStyleMultiplier);
        heavyStyleMultiplier = Mathf.Clamp(heavyStyleMultiplier, 0.1f, 1f);
        heavyStyleDamageBonus = Mathf.Max(0, heavyStyleDamageBonus);
        chargeKickMaxSeconds = Mathf.Max(0.05f, chargeKickMaxSeconds);
        chargeKickMaxSpaces = Mathf.Max(0f, chargeKickMaxSpaces);
        chargeKickSpaceSize = Mathf.Max(0.01f, chargeKickSpaceSize);
        chargeKickMinHoldSeconds = Mathf.Max(0f, chargeKickMinHoldSeconds);
        chargeKickDamage = Mathf.Max(0, chargeKickDamage);
        chargeSlashDamage = Mathf.Max(0, chargeSlashDamage);
        harlieDashSmoothStop = Mathf.Max(0f, harlieDashSmoothStop);
        harlieDashJumpMomentumDuration = Mathf.Max(0f, harlieDashJumpMomentumDuration);
        harlieMaxAirJumps = Mathf.Max(0, harlieMaxAirJumps);
        doubleJumpReferenceForce = Mathf.Max(0.1f, doubleJumpReferenceForce);
        doubleJumpHeightFraction = Mathf.Max(0.01f, doubleJumpHeightFraction);
        pressesPerSpeedTier = Mathf.Max(1, pressesPerSpeedTier);
        attackSpeedPerTier = Mathf.Max(0f, attackSpeedPerTier);
        maxAttackSpeedTier = Mathf.Max(0, maxAttackSpeedTier);
        attackSpeedIdleResetSeconds = Mathf.Max(0.05f, attackSpeedIdleResetSeconds);
        dashCancelSlashSpeedMul = Mathf.Max(1f, dashCancelSlashSpeedMul);
        dashCancelSlashWindow = Mathf.Max(0f, dashCancelSlashWindow);
        slashGuardTolerance = Mathf.Max(0f, slashGuardTolerance);
        attackKnockbackDistance = Mathf.Max(0f, attackKnockbackDistance);
        pogoGuardSeconds = Mathf.Max(0f, pogoGuardSeconds);
        chargedAirSlashDamageMultiplier = Mathf.Max(1, chargedAirSlashDamageMultiplier);
        chargedAirSlashDistanceMultiplier = Mathf.Max(1f, chargedAirSlashDistanceMultiplier);
        aerialAirDashesPerLevel = Mathf.Max(0, aerialAirDashesPerLevel);
        hyperDamagePerLevel = Mathf.Max(0, hyperDamagePerLevel);
        hyperMaxHealthPerLevel = Mathf.Max(0, hyperMaxHealthPerLevel);
        hyperMoveSpeedPerLevel = Mathf.Max(0f, hyperMoveSpeedPerLevel);
        hyperWaveSizePerLevel = Mathf.Max(0f, hyperWaveSizePerLevel);
        hyperWaveDistancePerLevel = Mathf.Max(0f, hyperWaveDistancePerLevel);
        speedStyleDamagePerLevel = Mathf.Max(0, speedStyleDamagePerLevel);
        speedStyleWaveDistancePerLevel = Mathf.Max(0f, speedStyleWaveDistancePerLevel);
        speedStyleWaveSpeedPerLevel = Mathf.Max(0f, speedStyleWaveSpeedPerLevel);
        speedStyleDashDistancePerLevel = Mathf.Max(0f, speedStyleDashDistancePerLevel);
        speedStyleDashSpeedPerLevel = Mathf.Max(0f, speedStyleDashSpeedPerLevel);
        heavyWaveSizePerLevel = Mathf.Max(0f, heavyWaveSizePerLevel);
        heavyWaveDamagePerLevel = Mathf.Max(0, heavyWaveDamagePerLevel);
        heavyMaxHealthPerLevel = Mathf.Max(0, heavyMaxHealthPerLevel);
        heavyFallMultiplierPerLevel = Mathf.Max(0f, heavyFallMultiplierPerLevel);
        heavyKnockbackPerLevel = Mathf.Max(0f, heavyKnockbackPerLevel);
        heavyChainKillMinLevel = Mathf.Clamp(heavyChainKillMinLevel, 1, PlayerUpgrades.MaxLevel);
        heavyDashPushPerLevel = Mathf.Max(0f, heavyDashPushPerLevel);
        heavyDashPushDamage = Mathf.Max(0, heavyDashPushDamage);
        heavyDashPushDuration = Mathf.Max(0.02f, heavyDashPushDuration);
        harlieAfterimageFadeStagger = Mathf.Max(0f, harlieAfterimageFadeStagger);
        harlieAfterimageFadeDuration = Mathf.Max(0.05f, harlieAfterimageFadeDuration);
        chargeSlashMaxRatio = Mathf.Clamp(chargeSlashMaxRatio, 0.1f, 0.9f);
        chargeAttackSpeedVsDash = Mathf.Max(1.01f, chargeAttackSpeedVsDash);
        attackKnockbackDuration = Mathf.Max(0.02f, attackKnockbackDuration);
        auraBaseScale = Mathf.Max(1f, auraBaseScale);
        jumpAttackDamage = Mathf.Max(0, jumpAttackDamage);
        aerialBounceForce = Mathf.Max(0.1f, aerialBounceForce);
        outgoingDamageMultiplier = Mathf.Max(0.1f, outgoingDamageMultiplier);
        slashWave?.Validate();
    }
#endif
}

/// <summary>Harlie's single yellow slash wave (reuses Malice's slash projectile).</summary>
[System.Serializable]
public class HarlieSlashWaveSettings
{
    public bool enabled = true;
    [Tooltip("Yellow copy of Malice's purple slash projectile.")]
    public MaliceSlashProjectile prefab;
    [Tooltip("Base travel distance (world units, ~1.5 spaces).")]
    public float travelDistance = 1.5f;
    [Tooltip("Seconds to cover the base distance. Base speed = distance / seconds; longer waves keep that speed.")]
    public float travelSeconds = 0.2f;
    [Tooltip("Push on moving common enemies is Harlie's slash knockback; this is how long it takes.")]
    public float knockbackDuration = 0.12f;
    [Tooltip("Sorting order offset vs. Harlie's body.")]
    public int sortingOffset = 1;
    public float fadeDelay = 0f;

    public MaliceSlashProjectile Fire(
        Transform owner,
        Collider2D attackBox,
        float facingSign,
        int damage,
        SpriteRenderer ownerRenderer,
        float distanceMultiplier,
        float extraDistance,
        float extraSpeed,
        float sizeMultiplier,
        float knockbackDistance,
        bool chainKill,
        Vector2? travelDirection = null,
        MaliceSlashVolley volley = null,
        Vector3? spawnOverride = null,
        float speedMultiplier = 1f)
    {
        if (!enabled || prefab == null || owner == null)
            return null;

        float baseSpeed = Mathf.Max(0.01f, travelDistance) / Mathf.Max(0.02f, travelSeconds);
        float distance = Mathf.Max(0f, travelDistance * Mathf.Max(0f, distanceMultiplier) + Mathf.Max(0f, extraDistance));
        float speed = Mathf.Max(0.01f, (baseSpeed + Mathf.Max(0f, extraSpeed)) * Mathf.Max(0.1f, speedMultiplier));
        float seconds = distance / speed;

        float dir = facingSign >= 0f ? 1f : -1f;
        Vector3 spawn = owner.position;
        if (attackBox is BoxCollider2D box)
        {
            Vector3 center = box.transform.TransformPoint(box.offset);
            float halfWidth = box.size.x * 0.5f * Mathf.Abs(box.transform.lossyScale.x);
            spawn = center + new Vector3(dir * halfWidth, 0f, 0f);
        }
        else if (attackBox != null)
        {
            Bounds b = attackBox.bounds;
            spawn = new Vector3(dir > 0f ? b.max.x : b.min.x, b.center.y, owner.position.z);
        }

        if (spawnOverride.HasValue)
            spawn = spawnOverride.Value;

        spawn.z = owner.position.z;
        MaliceSlashProjectile wave = Object.Instantiate(prefab, spawn, Quaternion.identity);
        wave.transform.localScale = Vector3.Scale(wave.transform.localScale, Vector3.one * Mathf.Max(0.1f, sizeMultiplier));
        wave.ChainKillOnPush = chainKill;
        wave.Launch(dir, distance, seconds, damage, owner, volley ?? new MaliceSlashVolley(),
            ownerRenderer, sortingOffset, fadeDelay, knockbackDistance, knockbackDuration);
        if (travelDirection.HasValue)
            wave.SetTravelDirection(travelDirection.Value);
        return wave;
    }

    public void Validate()
    {
        travelDistance = Mathf.Max(0f, travelDistance);
        travelSeconds = Mathf.Max(0.02f, travelSeconds);
        knockbackDuration = Mathf.Max(0.02f, knockbackDuration);
        fadeDelay = Mathf.Max(0f, fadeDelay);
    }
}
