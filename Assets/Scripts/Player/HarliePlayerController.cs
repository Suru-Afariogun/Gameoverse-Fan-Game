using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Harlie — melee fighter ported in feel from Little Program Harlie.
/// Ground combo: Kick → Slash 1 → Slash 2 → Slash 3 (cycles).
/// Air: air slash. Uses this game's PlayerController + AttackHitbox systems.
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
    [SerializeField] private float harlieDashDistance = 5f;
    [SerializeField] private float harlieDashSpeed = 24f;
    [SerializeField] private float harlieDashSmoothStop = 0.24f;
    [SerializeField] private float harlieDashEaseInFraction = 0.12f;
    [SerializeField] private int harlieMaxAirDashes = 3;
    [SerializeField] private bool harlieAllowDashJump = true;
    [SerializeField] private float harlieDashJumpMomentumDuration = 0.22f;
    [SerializeField] private bool harlieDashJumpMomentumUntilLanded = true;

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
    [SerializeField] private float comboInputWindow = 0.5f;
    [SerializeField] private float kickDuration = 0.38f;
    [SerializeField] private float slash1Duration = 0.38f;
    [SerializeField] private float slash2Duration = 0.38f;
    [SerializeField] private float slash3Duration = 0.38f;
    [SerializeField] private float airSlashDuration = 0.38f;
    [SerializeField] private bool syncAttackDurationToAnimation = true;
    [SerializeField] private float attackDurationPadding = 0.02f;
    [SerializeField] private float timeBetweenAttacks = 0.05f;
    [SerializeField] private int kickDamage = 3;
    [SerializeField] private int slash1Damage = 3;
    [SerializeField] private int slash2Damage = 3;
    [SerializeField] private int slash3Damage = 3;
    [SerializeField] private int airSlashDamage = 3;
    [SerializeField] private float hitboxDelay = 0.05f;
    [SerializeField] private float hitboxActiveTime = 0.22f;
    [SerializeField] private bool lockMoveDuringGroundAttack = true;
    [Tooltip("After an air slash, keep that pose briefly before fall/jump anims resume.")]
    [SerializeField] private float airSlashAnimHold = 0.5f;

    [Header("Harlie - Jump Attack / Pogo")]
    [Tooltip("Automatic falling attack while airborne (Little Program Harlie jump attack).")]
    [SerializeField] private int jumpAttackDamage = 3;
    [Tooltip("Upward speed applied when bounce-hitting a target while falling.")]
    [SerializeField] private float aerialBounceForce = 12f;

    [Header("Harlie - Attack Knockback")]
    [Tooltip("How far targets are pushed on a successful kick or slash hit (spaces). Original Harlie used 4.")]
    [SerializeField] private float attackKnockbackSpaces = 5f;
    [Tooltip("World units per space.")]
    [SerializeField] private float knockbackSpaceSize = 1f;
    [Tooltip("How long the knockback slide takes (higher = smoother).")]
    [SerializeField] private float attackKnockbackDuration = 0.34f;

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

    [Header("Harlie - Charge Attack (ground only)")]
    [Tooltip("Hold Attack this long for maximum travel.")]
    [SerializeField] private float chargeKickMaxSeconds = 2f;
    [Tooltip("Max charged travel in spaces (× space size).")]
    [SerializeField] private float chargeKickMaxSpaces = 24f;
    [Tooltip("Hold ratio at or below this uses Slash 1; above uses charge kick.")]
    [SerializeField] [Range(0.1f, 0.9f)] private float chargeSlashMaxRatio = 0.5f;
    [Tooltip("World units per space.")]
    [SerializeField] private float chargeKickSpaceSize = 1f;
    [SerializeField] private float chargeKickMinHoldSeconds = 0.08f;
    [SerializeField] private int chargeKickDamage = 5;
    [Tooltip("Charge slide speed = dash speed × this (slightly faster than dash).")]
    [SerializeField] private float chargeAttackSpeedVsDash = 1.12f;
    [Tooltip("Brief pose hold after the slide ends.")]
    [SerializeField] private float chargeKickHoldAfterSlide = 0.3f;

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

    private readonly Dictionary<int, Coroutine> activeKnockbacks = new Dictionary<int, Coroutine>();

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
        return isChargingKick || isChargeSlideActive;
    }

    protected override float GetCurrentDashVelocityX()
    {
        float peak = dashDirSign * dashSpeed;
        float easeInWindow = Mathf.Min(harlieDashEaseInFraction, dashDuration * 0.35f);
        float stopWindow = Mathf.Min(dashSmoothStopDuration, dashDuration * 0.55f);

        if (easeInWindow > 0.0001f && dashTimer < easeInWindow)
        {
            float t = Mathf.Clamp01(dashTimer / easeInWindow);
            return peak * SmootherStep(t);
        }

        if (stopWindow <= 0.0001f || dashTimer < dashDuration - stopWindow)
            return peak;

        float stopT = Mathf.InverseLerp(dashDuration - stopWindow, dashDuration, dashTimer);
        float end = dashDirSign * dashSpeed * dashEndSpeedMultiplier;
        return Mathf.Lerp(peak, end, SmootherStep(stopT));
    }

    protected override float GetCurrentMoveSpeed()
    {
        return base.GetCurrentMoveSpeed() * StyleSpeedMultiplier();
    }

    protected override void TryBeginDash()
    {
        float saved = dashSpeed;
        dashSpeed *= StyleSpeedMultiplier();
        base.TryBeginDash();
        dashSpeed = saved;
    }

    protected override float GetDashJumpHorizontalSpeed()
    {
        float speed = base.GetDashJumpHorizontalSpeed();
        if (UsesSpeedStyle())
            return speed * Mathf.Max(0.1f, speedStyleDashJumpMultiplier);
        if (UsesHeavyStyle())
            return speed * Mathf.Clamp(heavyStyleMultiplier, 0.1f, 1f);
        return speed;
    }

    private static bool UsesSpeedStyle()
    {
        return PlayerAttackStyle.Is(AttackStyleId.SpreadShot);
    }

    private static bool UsesHeavyStyle()
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

    private int ScaleOutgoingDamage(int baseDamage)
    {
        int dmg = Mathf.Max(0, baseDamage);
        if (UsesHeavyStyle())
            dmg += Mathf.Max(0, heavyStyleDamageBonus);
        return dmg;
    }

    private int GetChargedAttackDamage()
    {
        return ScaleOutgoingDamage(chargeKickDamage);
    }

    protected override int UpgradeAirJumps => PlayerUpgrades.GetLevel(this, UpgradeType.AerialAction);

    protected override void Awake()
    {
        base.Awake();
        SetCharacterId("Harlie");

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
        airDashesRemaining = Mathf.Max(0, maxAirDashes);

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
        StopHarlieChargeAudio();
        SetChargeAuraVisible(false);
        StopAllKnockbacks();
        EndJumpAttack();
        CancelChargeKick();
        EndMeleeImmediate();
        SoundManager.Instance?.StopHarlieSwordSwingImmediate();
        base.OnDisable();
    }

    protected override void LateUpdate()
    {
        base.LateUpdate();
        MirrorAttackBoxForFacing();
    }

    protected override void UpdateAnimator()
    {
        if (animator != null)
            animator.speed = StyleSpeedMultiplier();

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
            if (!isGrounded)
                CancelChargeKick();
            else
            {
                chargeKickTimer = Mathf.Min(chargeKickTimer + Time.deltaTime, Mathf.Max(0.05f, chargeKickMaxSeconds));
                // Hyper Ability (full health): past a tap, the kick is instantly at max charge.
                if (PlayerUpgrades.IsHyperActive(this) && chargeKickTimer >= chargeKickMinHoldSeconds)
                    chargeKickTimer = Mathf.Max(0.05f, chargeKickMaxSeconds);
                UpdateChargeAura(chargeKickTimer);
            }
        }
        else
        {
            SetChargeAuraVisible(false);
        }

        TickMelee(Time.deltaTime);
        UpdateJumpAttack();
    }

    protected override void HandleCharacterFixedUpdate()
    {
        if (isChargeSlideActive)
            UpdateChargeSlideFixed();

        if (isAttackLunging)
            UpdateAttackLungeFixed();
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

        if (attackCooldownTimer > 0f && !IsMeleeAttacking && !isChargingKick)
            return;

        if (isDashing)
            CancelDash(keepHorizontalMomentum: true);

        // Mid-combo tap chains the next ground swing immediately.
        if (IsMeleeAttacking && !currentAttackWasAir && comboTimer > 0f)
        {
            BeginAttack();
            return;
        }

        // Ground idle: hold to charge kick (release fires via OnAttackCanceled).
        if (isGrounded && !IsMeleeAttacking)
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

        if (!isGrounded)
            return;

        if (held >= chargeKickMinHoldSeconds)
            BeginChargedRelease(held);
        else
            BeginAttack();
    }

    protected override void OnDashStarted()
    {
        StopHarlieChargeAudio();
        CancelChargeKick();
        EndJumpAttack();
        if (IsMeleeAttacking)
            EndMeleeImmediate();
    }

    private void CancelChargeKick()
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

    private void BeginAttack()
    {
        EndJumpAttack();

        bool continuingGroundCombo = IsMeleeAttacking && !currentAttackWasAir && comboTimer > 0f;
        bool inAir = !isGrounded && !continuingGroundCombo;

        EndMeleeImmediate();

        currentAttackWasAir = inAir;
        meleeTimer = 0f;
        hitboxArmed = true;
        hitboxOpened = false;
        hitboxClosed = false;
        airSlashHoldUntil = 0f;

        if (inAir)
        {
            currentComboIndex = ComboKick;
            nextComboIndex = ComboKick;
            comboTimer = 0f;
            meleeState = MeleeState.AirSlash;
            pendingHitDamage = ScaleOutgoingDamage(airSlashDamage);
            meleeDuration = ResolveDuration("air slash", airSlashDuration) / StyleSpeedMultiplier();
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
                    meleeDuration = ResolveDuration("slash 1", slash1Duration) / StyleSpeedMultiplier();
                    break;
                case ComboSlash2:
                    meleeState = MeleeState.Slash2;
                    pendingHitDamage = ScaleOutgoingDamage(slash2Damage);
                    meleeDuration = ResolveDuration("slash 2", slash2Duration) / StyleSpeedMultiplier();
                    break;
                case ComboSlash3:
                    meleeState = MeleeState.Slash3;
                    pendingHitDamage = ScaleOutgoingDamage(slash3Damage);
                    meleeDuration = ResolveDuration("slash 3", slash3Duration) / StyleSpeedMultiplier();
                    break;
                default:
                    meleeState = MeleeState.Kick;
                    pendingHitDamage = ScaleOutgoingDamage(kickDamage);
                    meleeDuration = ResolveDuration("kick", kickDuration) / StyleSpeedMultiplier();
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
    }

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
        pendingHitDamage = GetChargedAttackDamage();
        chargeSlideUsesKick = true;
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
        pendingHitDamage = GetChargedAttackDamage();
        chargeSlideUsesKick = false;
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

    private float ResolveDuration(string stateName, float fallback)
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

        if (!isChargeSlideActive)
        {
            float activeStart = Mathf.Max(0f, hitboxDelay) / StyleSpeedMultiplier();
            float activeEnd = activeStart + Mathf.Max(0.01f, hitboxActiveTime) / StyleSpeedMultiplier();

            if (hitboxArmed && !hitboxOpened && meleeTimer >= activeStart)
            {
                hitboxOpened = true;
                attackHitbox?.Activate(Mathf.Max(0, pendingHitDamage), applyDamage: true);
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
        attackCooldownTimer = Mathf.Max(0f, timeBetweenAttacks) / StyleSpeedMultiplier();

        if (wasAir)
            airSlashHoldUntil = Time.time + Mathf.Max(0f, airSlashAnimHold) / StyleSpeedMultiplier();
    }

    private void EndMeleeImmediate()
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
        float speedMul = StyleSpeedMultiplier();
        if (combo == ComboKick)
        {
            float kickSpeed = Mathf.Max(0.01f, dashSpeed * Mathf.Max(1.01f, kickSpeedVsDash) * speedMul);
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
        bool wantJumpAttack = !isGrounded && !isDashing && !IsMeleeAttacking && !isChargingKick
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
        attackHitbox.Activate(ScaleOutgoingDamage(jumpAttackDamage), applyDamage: true);
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

    private void PushTargetOnHit(Collider2D hitCollider)
    {
        if (hitCollider == null || attackKnockbackSpaces <= 0f)
            return;

        if (hitCollider.GetComponentInParent<Projectile>() != null)
            return;

        Transform pushRoot = ResolveKnockbackRoot(hitCollider);
        if (pushRoot == null)
            return;

        Vector2 knockback = new Vector2(FacingSign * attackKnockbackSpaces * knockbackSpaceSize, 0f);
        if (knockback.sqrMagnitude <= 0.0001f)
            return;

        StartSmoothKnockback(pushRoot, knockback);
    }

    private void StartSmoothKnockback(Transform root, Vector2 delta)
    {
        if (root == null)
            return;

        int id = root.GetInstanceID();
        if (activeKnockbacks.TryGetValue(id, out Coroutine running) && running != null)
            StopCoroutine(running);

        activeKnockbacks[id] = StartCoroutine(CoSmoothKnockback(root, delta));
    }

    private void StopAllKnockbacks()
    {
        foreach (KeyValuePair<int, Coroutine> pair in activeKnockbacks)
        {
            if (pair.Value != null)
                StopCoroutine(pair.Value);
        }

        activeKnockbacks.Clear();
    }

    private IEnumerator CoSmoothKnockback(Transform root, Vector2 delta)
    {
        if (root == null)
            yield break;

        int id = root.GetInstanceID();
        Rigidbody2D body = root.GetComponent<Rigidbody2D>();
        if (body == null)
            body = root.GetComponentInParent<Rigidbody2D>();

        Vector2 start = body != null ? body.position : (Vector2)root.position;
        Vector2 end = start + delta;
        float duration = Mathf.Max(0.08f, attackKnockbackDuration);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            if (root == null)
                yield break;

            elapsed += Time.fixedDeltaTime;
            float t = SmootherStep(Mathf.Clamp01(elapsed / duration));
            Vector2 pos = Vector2.Lerp(start, end, t);

            if (body != null)
            {
                body.MovePosition(pos);
                body.linearVelocity = new Vector2(0f, body.linearVelocity.y);
            }
            else
            {
                root.position = pos;
            }

            yield return new WaitForFixedUpdate();
        }

        if (root != null)
        {
            if (body != null)
            {
                body.MovePosition(end);
                body.linearVelocity = new Vector2(0f, body.linearVelocity.y);
            }
            else
            {
                root.position = end;
            }
        }

        activeKnockbacks.Remove(id);
    }

    private Transform ResolveKnockbackRoot(Collider2D hitCollider)
    {
        AttackHitbox tool = hitCollider.GetComponent<AttackHitbox>();
        if (tool == null)
            tool = hitCollider.GetComponentInParent<AttackHitbox>();

        if (tool != null && tool.OwnerBoss != null)
            return tool.OwnerBoss.transform;

        Boss boss = hitCollider.GetComponent<Boss>();
        if (boss == null)
            boss = hitCollider.GetComponentInParent<Boss>();

        if (boss != null)
            return boss.transform;

        if (hitCollider.GetComponentInParent<Crystal>() != null)
            return null;

        PlayerController otherPlayer = hitCollider.GetComponent<PlayerController>();
        if (otherPlayer == null)
            otherPlayer = hitCollider.GetComponentInParent<PlayerController>();

        if (otherPlayer != null && otherPlayer != this)
            return otherPlayer.transform;

        return hitCollider.attachedRigidbody != null
            ? hitCollider.attachedRigidbody.transform
            : hitCollider.transform.root;
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

    private bool CanPogoNow()
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

    private void BounceOffTarget(Collider2D hitCollider)
    {
        if (rb == null)
            return;

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
        harlieDashSmoothStop = Mathf.Max(0f, harlieDashSmoothStop);
        harlieDashEaseInFraction = Mathf.Max(0f, harlieDashEaseInFraction);
        harlieAfterimageFadeStagger = Mathf.Max(0f, harlieAfterimageFadeStagger);
        harlieAfterimageFadeDuration = Mathf.Max(0.05f, harlieAfterimageFadeDuration);
        chargeSlashMaxRatio = Mathf.Clamp(chargeSlashMaxRatio, 0.1f, 0.9f);
        chargeAttackSpeedVsDash = Mathf.Max(1.01f, chargeAttackSpeedVsDash);
        attackKnockbackDuration = Mathf.Max(0.08f, attackKnockbackDuration);
        auraBaseScale = Mathf.Max(1f, auraBaseScale);
        jumpAttackDamage = Mathf.Max(0, jumpAttackDamage);
        aerialBounceForce = Mathf.Max(0.1f, aerialBounceForce);
        attackKnockbackSpaces = Mathf.Max(0f, attackKnockbackSpaces);
        knockbackSpaceSize = Mathf.Max(0.01f, knockbackSpaceSize);
    }
#endif
}
