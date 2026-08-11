using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Malice — melee fighter.
/// Slash combo on tap. Hold attack to charge Grapple Arm (Kit-style timing/aura, purple flicker).
/// Grapple is melee via AttackHitbox. Down+release = grab targets to the box.
/// Up during grapple = pull Malice toward the AttackBox pose at a chosen animation frame.
/// If the box overlaps something while pulling, latch onto that object and pull Malice to it.
/// </summary>
public class MalicePlayerController : PlayerController
{
    private enum MeleeState
    {
        Idle,
        Slash1,
        Slash2,
        Slash3,
        AirSlash,
        GrappleArm
    }

    [Header("Malice - References")]
    [Tooltip("Melee / dash hurtbox (trigger + AttackHitbox). Overlaps only — no solid collision. On during slash, grapple, and dash.")]
    [SerializeField] private AttackHitbox attackHitbox;
    [Tooltip("Optional separate dash hurtbox. If empty, Attack Hitbox is used for dash (dash counts as an attack).")]
    [SerializeField] private AttackHitbox dashHitbox;
    [Tooltip("Kept for future projectile attacks — not used yet.")]
    [SerializeField] private Transform maliceFirePoint;

    [Header("Malice - Dash (slightly stronger than Kit)")]
    [SerializeField] private float maliceDashDistance = 5.9f;
    [SerializeField] private float maliceDashSpeed = 19f;
    [SerializeField] private float maliceDashSmoothStop = 0.12f;
    [SerializeField] private int maliceMaxAirDashes = 2;
    [SerializeField] private bool maliceAllowDashJump = true;
    [SerializeField] private float maliceDashJumpHorizontalSpeed = 0f;
    [SerializeField] private float maliceDashJumpForce = 0f;
    [SerializeField] private float maliceDashJumpMomentumDuration = 0.55f;
    [SerializeField] private bool maliceDashJumpMomentumUntilLanded = true;
    [SerializeField] private int dashContactDamage = 2;

    [Header("Malice - Afterimages")]
    [SerializeField] private bool maliceEnableDashAfterimages = true;
    [SerializeField] private int maliceAfterimageCount = 3;
    [SerializeField] private float maliceAfterimageSpacing = 0.1f;
    [SerializeField] private Color maliceAfterimageColor = Color.white;
    [SerializeField] [Range(0f, 1f)] private float maliceAfterimageAlphaStart = 0.55f;
    [SerializeField] [Range(0f, 1f)] private float maliceAfterimageAlphaEnd = 0.15f;

    [Header("Malice - Slash Combo")]
    [SerializeField] private float comboInputWindow = 0.8f;
    [Tooltip("Fallback if the Animator clip cannot be found. Prefer syncing to the clip length.")]
    [SerializeField] private float slash1Duration = 0.46f;
    [SerializeField] private float slash2Duration = 0.46f;
    [SerializeField] private float slash3Duration = 0.5f;
    [SerializeField] private float airSlashDuration = 0.34f;
    [Tooltip("When enabled, attack lockout lasts as long as the matching Animator clip so the anim is not cut short.")]
    [SerializeField] private bool syncAttackDurationToAnimation = true;
    [Tooltip("Tiny extra time after the clip so the last frame is visible before IsAttacking clears.")]
    [SerializeField] private float attackDurationPadding = 0.02f;
    [SerializeField] private int slash1Damage = 1;
    [SerializeField] private int slash2Damage = 1;
    [SerializeField] private int slash3Damage = 2;
    [SerializeField] private int airSlashDamage = 1;
    [SerializeField] private float slashHitboxDelay = 0.06f;
    [SerializeField] private float slashHitboxActiveTime = 0.14f;
    [SerializeField] private bool lockMoveDuringGroundSlash = true;
    [Tooltip("How far Malice steps forward (world units) on Slash 1/2/3. Air slash does not step.")]
    [SerializeField] private float groundSlashStepDistance = 0.3f;
    [Tooltip("Downward speed while diving from an air slash (hold Down).")]
    [SerializeField] private float airSlashDiveSpeed = 22f;
    [SerializeField] [Range(0.1f, 1f)] private float airSlashDiveDownThreshold = 0.35f;
    [Tooltip("Once Down is pressed during air slash, keep diving/attacking until landing.")]
    [SerializeField] private bool airSlashDiveUntilLand = true;

    [Header("Malice - Rapid Attack Speed")]
    [Tooltip("Every N attack presses (within the idle window) increases attack speed by Attack Speed Per Tier.")]
    [SerializeField] private int pressesPerSpeedTier = 6;
    [Tooltip("Added to base speed (1) each tier. Tier 1 → animator/attack speed 2, tier 2 → 3, etc.")]
    [SerializeField] private float attackSpeedPerTier = 1f;
    [SerializeField] private int maxAttackSpeedTier = 4;
    [Tooltip("If no attack press for this long, speed returns to normal.")]
    [SerializeField] private float attackSpeedIdleResetSeconds = 0.7f;

    [Header("Malice - Dash Cancel Slash")]
    [Tooltip("Slash speed multiplier when attack is pressed mid-dash or right after a dash.")]
    [SerializeField] private float dashCancelSlashSpeedMul = 1.5f;
    [Tooltip("How long after a dash ends that a slash still gets the dash-cancel speed boost.")]
    [SerializeField] private float dashCancelSlashWindow = 0.2f;

    [Header("Malice - Grapple Charge (Kit-style timing)")]
    [SerializeField] private float mediumChargeSeconds = 1.5f;
    [SerializeField] private float bigChargeSeconds = 3f;
    [Tooltip("Delay after press before charge aura can appear (same idea as Kit's post-shot delay).")]
    [SerializeField] private float auraDelayAfterPress = 0.2f;
    [SerializeField] private float mediumChargeMoveSpeedBonus = 3f;
    [SerializeField] private float bigChargeMoveSpeedBonus = 6f;
    [SerializeField] private int grappleMaxDamage = 3;
    [Tooltip("Damage when releasing before full charge (defaults to half of max).")]
    [SerializeField] private int grapplePartialDamage = 1;
    [Tooltip("Fallback grapple lockout if the Animator clip cannot be found.")]
    [SerializeField] private float grappleDuration = 0.5f;
    [SerializeField] private float grappleHitboxDelay = 0.08f;
    [SerializeField] private float grappleHitboxActiveTime = 0.35f;
    [Tooltip("Fallback reel speed if the grapple clip cannot drive pull timing.")]
    [SerializeField] private float grapplePullSpeed = 18f;
    [Tooltip("How close Malice's body must get to the AttackBox front edge to count as fully reeled in.")]
    [SerializeField] private float grapplePullArriveDistance = 0.03f;
    [Tooltip("While holding Up, AttackBox can latch onto these layers and pull Malice to that object.")]
    [SerializeField] private LayerMask grapplePullLatchLayers = ~0;
    [Header("Malice - Grapple Pull Anchor Frame")]
    [Tooltip("0-based full-extension frame in 'Grapple Arm'. Up-pull waits until this frame, then reels Malice to that AttackBox pose. At 60fps, frame 8 ≈ 0.133s.")]
    [SerializeField] private int grapplePullAnchorFrame = 8;
    [Tooltip("0-based full-extension frame in 'Grapple Arm Air' (same rules as ground).")]
    [SerializeField] private int grappleAirPullAnchorFrame = 8;
    [Tooltip("Used only if the clip reports no frame rate.")]
    [SerializeField] private float grapplePullAnchorFrameRateFallback = 60f;
    [Tooltip("If enabled, ignore the frame sample and use Manual Pull Anchor Local (left-facing AttackBox local pos).")]
    [SerializeField] private bool useManualGrapplePullAnchorLocal;
    [SerializeField] private Vector3 manualGrapplePullAnchorLocal = new Vector3(-6.26f, 0.09f, 0f);
    [SerializeField] [Range(0.1f, 1f)] private float grappleUpThreshold = 0.35f;
    [SerializeField] [Range(0.1f, 1f)] private float grappleDownThreshold = 0.35f;
    [SerializeField] private bool lockMoveDuringGrapple = true;

    [Header("Malice - Purple Charge Aura")]
    [SerializeField] private bool showChargeAura = true;
    [SerializeField] private Color chargeAuraColor = new Color(0.35f, 0.08f, 0.45f, 1f);
    [SerializeField] private Color chargeAuraStrongColor = new Color(0.22f, 0.02f, 0.35f, 1f);
    [Tooltip("Bright violet half of the purple shimmer.")]
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

    private MeleeState meleeState = MeleeState.Idle;
    private float meleeTimer;
    private float meleeDuration;
    private bool queuedSlash2;
    private bool queuedSlash3;
    private float slash1PressTime = -999f;
    private float slash2StartTime = -999f;
    private float slash3GraceUntil = -999f;
    private bool hitboxArmed;
    private bool hitboxWasActive;
    private int pendingHitDamage;
    private bool dashHitboxActive;
    private bool airSlashDiving;

    private int rapidAttackPressCount;
    private float lastAttackPressTime = -999f;
    private int attackSpeedTier;
    private float currentMeleeSpeed = 1f;
    private float dashEndedAt = -999f;
    private bool dashCancelSlashQueued;
    private bool wasChargeAuraActive;

    private bool isCharging;
    private float chargeTimer;
    private float auraAllowedAfterTime = float.PositiveInfinity;
    private bool chargeStartedInAir;

    private bool grappleIsGrab;
    private bool grappleIsAirMove;
    private bool grapplePullActive;
    private Vector3 grappleAnchorWorld;
    private bool grappleAnchorLocked;
    private bool grappleAirHang;
    private bool grapplePullLatchedToObject;
    private Transform grapplePullLatchTarget;
    private Vector3 grapplePullLatchLocalOffset;
    private Vector3 grapplePullAnchorLocalLeft;
    private bool grapplePullAnchorLocalCached;
    private Vector2 grapplePullStartBodyPos;
    private Vector2 grapplePullTargetWorld;
    private bool grapplePullArrived;
    private Transform attackBoxDefaultParent;
    private bool attackBoxWorldAnchored;
    private Vector3 attackBoxAnchoredScale = Vector3.one;
    private readonly List<Transform> grabbedTargets = new List<Transform>(4);
    private readonly List<Rigidbody2D> grabbedBodies = new List<Rigidbody2D>(4);
    private readonly Collider2D[] grappleOverlapBuffer = new Collider2D[24];
    private ContactFilter2D grappleOverlapFilter;
    private bool grappleOverlapFilterReady;

    // AttackBox authored left-facing (same LateUpdate mirror as Kit FirePoint).
    private Vector3 attackBoxLeftFacingLocal;
    private Vector3 attackBoxLastWrittenLocal;
    private bool attackBoxHasLastWritten;

    private GameObject chargeAuraObject;
    private SpriteRenderer chargeAuraRenderer;
    private Material chargeAuraMaterial;
    private float auraFlickerPhase;

    public bool IsMeleeAttacking => meleeState != MeleeState.Idle;
    public bool IsGrappling => meleeState == MeleeState.GrappleArm;
    public bool IsChargingGrapple => isCharging;

    protected override bool BlocksActionCancel()
    {
        return IsGrappling;
    }

    protected override void UpdateAnimator()
    {
        base.UpdateAnimator();
        if (animator == null)
            return;

        animator.SetBool("IsAttacking", IsMeleeAttacking);
        animator.SetBool("IsCharging", isCharging);
        ApplyAnimatorAttackSpeed();
    }

    protected override void OnHitStunStarted()
    {
        isCharging = false;
        chargeTimer = 0f;
        SetChargeAuraVisible(false);
        EndMeleeImmediate();
        SetDashHitboxActive(false);
        ResetRapidAttackSpeed();
        base.OnHitStunStarted();
    }

    protected override void Awake()
    {
        base.Awake();
        SetCharacterId("Malice");

        if (maliceFirePoint != null)
            firePoint = maliceFirePoint;

        dashDistance = maliceDashDistance;
        dashSpeed = maliceDashSpeed;
        dashSmoothStopDuration = maliceDashSmoothStop;
        maxAirDashes = maliceMaxAirDashes;
        allowDashJump = maliceAllowDashJump;
        dashJumpHorizontalSpeed = maliceDashJumpHorizontalSpeed;
        dashJumpForce = maliceDashJumpForce;
        dashJumpMomentumDuration = maliceDashJumpMomentumDuration;
        dashJumpMomentumUntilLanded = maliceDashJumpMomentumUntilLanded;
        enableDashAfterimages = maliceEnableDashAfterimages;
        dashAfterimageCount = maliceAfterimageCount;
        dashAfterimageSpacing = maliceAfterimageSpacing;
        dashAfterimageColor = maliceAfterimageColor;
        dashAfterimageAlphaStart = maliceAfterimageAlphaStart;
        dashAfterimageAlphaEnd = maliceAfterimageAlphaEnd;
        airDashesRemaining = Mathf.Max(0, maxAirDashes);

        if (grapplePartialDamage <= 0)
            grapplePartialDamage = Mathf.Max(1, grappleMaxDamage / 2);

        SetupDashAfterimages();
        SetupHitboxes();
        SetupChargeAura();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        SetupHitboxes();
        RefreshPlayerPhaseCollisions();

        if (attackHitbox != null)
            attackHitbox.OnTargetAcquired += HandleAttackTargetAcquired;
    }

    protected override void OnDisable()
    {
        if (attackHitbox != null)
            attackHitbox.OnTargetAcquired -= HandleAttackTargetAcquired;

        EndMeleeImmediate();
        SetDashHitboxActive(false);
        ClearGrabbedTargets();
        SetChargeAuraVisible(false);
        RestoreAttackBoxParent();
        base.OnDisable();
    }

    protected override void OnDestroy()
    {
        if (attackHitbox != null)
            attackHitbox.OnTargetAcquired -= HandleAttackTargetAcquired;

        if (chargeAuraObject != null)
            Destroy(chargeAuraObject);
        if (chargeAuraMaterial != null)
            Destroy(chargeAuraMaterial);

        base.OnDestroy();
    }

    protected override void LateUpdate()
    {
        base.LateUpdate(); // FirePoint mirror (Kit logic)

        // World-anchored AttackBox is unparented so Animator cannot drag it with Malice.
        if (attackBoxWorldAnchored || grapplePullActive || grappleAnchorLocked)
            PinAttackBoxToGrappleAnchor();
        else
            MirrorAttackBoxForFacing();
    }

    /// <summary>
    /// AttackBox anim keys are left-facing; mirror X when facing right (same as FirePoint).
    /// </summary>
    private void MirrorAttackBoxForFacing()
    {
        if (attackHitbox == null || attackBoxWorldAnchored)
            return;

        MirrorChildLocalForFacing(
            attackHitbox.transform,
            ref attackBoxLeftFacingLocal,
            ref attackBoxLastWrittenLocal,
            ref attackBoxHasLastWritten);
    }

    private void PinAttackBoxToGrappleAnchor()
    {
        if (attackHitbox == null)
            return;

        RefreshGrapplePullAnchorWorld();
        Transform box = attackHitbox.transform;
        box.position = grappleAnchorWorld;
        box.localScale = attackBoxAnchoredScale;
    }

    private void ResetAttackBoxFacingMirrorCapture()
    {
        attackBoxHasLastWritten = false;
    }

    protected override void HandleCharacterUpdate()
    {
        if (isCharging)
        {
            chargeTimer += Time.deltaTime;
            if (Time.time >= auraAllowedAfterTime)
            {
                // Committed to charge — cancel any accidental slash and show aura.
                if (IsGroundSlashActive() || meleeState == MeleeState.AirSlash)
                    EndMeleeImmediate();

                isShooting = false;
                shootAnimTimer = 0f;
                UpdateChargeAura();

                // Entering real charge resets rapid-attack speed.
                if (!wasChargeAuraActive && IsChargeAuraActive())
                    ResetRapidAttackSpeed();
                wasChargeAuraActive = IsChargeAuraActive();
            }
            else
            {
                SetChargeAuraVisible(false);
                wasChargeAuraActive = false;
            }
        }
        else
        {
            SetChargeAuraVisible(false);
            wasChargeAuraActive = false;
        }

        TickRapidAttackSpeedIdleReset();
        TickMelee(Time.deltaTime);
        TickGrapplePullInput();
        TickAirSlashDiveInput();
        EnforceAttackHitboxOnlyDuringAttacks();
    }

    protected override void HandleCharacterFixedUpdate()
    {
        ApplyAirSlashDiveMotion();
        ApplyGrapplePullMotion(Time.fixedDeltaTime);
    }

    protected override void ApplyHorizontalMove()
    {
        if (IsGrappling && grapplePullActive)
            return;

        if (lockMoveDuringGrapple && IsGrappling)
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            return;
        }

        if (lockMoveDuringGroundSlash && isGrounded && IsGroundSlashActive())
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            return;
        }

        bool chargeMoveUnlocked = IsChargeAuraActive();
        bool shouldLock =
            isGrounded &&
            !chargeMoveUnlocked &&
            (isCharging || IsGroundSlashActive());

        if (shouldLock)
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            return;
        }

        base.ApplyHorizontalMove();
    }

    protected override float GetCurrentMoveSpeed()
    {
        float speed = base.GetCurrentMoveSpeed();
        if (!IsChargeAuraActive())
            return speed;

        if (chargeTimer >= bigChargeSeconds)
            return speed + bigChargeMoveSpeedBonus;
        if (chargeTimer >= mediumChargeSeconds)
            return speed + mediumChargeMoveSpeedBonus;
        return speed;
    }

    protected override bool BlocksRunAnimationWhileShooting()
    {
        if (IsChargeAuraActive())
            return false;

        return isShooting || isCharging || IsGroundSlashActive() || IsGrappling;
    }

    protected override void OnAttackStarted(InputAction.CallbackContext context)
    {
        if (InputLocked || isStunned || IsGrappling)
            return;

        // Mid-dash or right after a dash: next slash is desperation-sped (and dash cancels).
        if (isDashing || Time.time - dashEndedAt <= Mathf.Max(0f, dashCancelSlashWindow))
        {
            dashCancelSlashQueued = true;
            if (isDashing)
                CancelDash(keepHorizontalMomentum: true);
        }

        RegisterRapidAttackPress();

        // Mid-combo taps chain slashes instead of starting a new charge.
        if (IsMeleeAttacking && !isCharging)
        {
            if (!isGrounded)
                return;

            HandleGroundAttackPress();
            return;
        }

        isCharging = true;
        chargeTimer = 0f;
        chargeStartedInAir = !isGrounded;
        auraAllowedAfterTime = Time.time + Mathf.Max(0f, auraDelayAfterPress);
        SetChargeAuraVisible(false);
    }

    protected override void OnAttackCanceled(InputAction.CallbackContext context)
    {
        if (InputLocked || isStunned)
            return;

        if (!isCharging)
            return;

        float held = chargeTimer;
        bool auraWasActive = IsChargeAuraActive();
        bool wantGrab = moveInput.y <= -grappleDownThreshold;
        isCharging = false;
        chargeTimer = 0f;
        SetChargeAuraVisible(false);

        // Held into charge → Grapple Arm. Quick tap → slash / air slash.
        if (auraWasActive || held >= mediumChargeSeconds)
        {
            EndMeleeImmediate();
            bool fullCharge = held >= bigChargeSeconds;
            int damage = fullCharge ? grappleMaxDamage : Mathf.Max(1, grapplePartialDamage);
            bool air = chargeStartedInAir || !isGrounded;
            StartGrapple(damage, air, wantGrab);
            return;
        }

        if (!isGrounded || chargeStartedInAir)
            TryStartAirSlash();
        else
            HandleGroundAttackPress();
    }

    protected override void OnDashStarted()
    {
        // Grapple cannot be canceled by dash (dash is blocked while grappling).
        // Dash is an attack, but it must NOT cancel Grapple Arm charge — keep charging through the dash.
        if (IsGrappling)
            return;

        ResetRapidAttackSpeed();

        // Interrupt slash / air slash only; leave isCharging + aura alone.
        if (!isCharging)
            EndMeleeImmediate();

        ClearGrabbedTargets();
        SetDashHitboxActive(true);
    }

    protected override void OnDashEnded()
    {
        dashEndedAt = Time.time;
        SetDashHitboxActive(false);
    }

    private bool IsChargeAuraActive()
    {
        return isCharging && Time.time >= auraAllowedAfterTime;
    }

    private void SetupHitboxes()
    {
        if (attackHitbox != null)
        {
            attackHitbox.SetOwner(this);
            // Always start off — only slash/grapple active frames turn it on.
            attackHitbox.Deactivate();
        }

        if (dashHitbox != null)
        {
            dashHitbox.SetOwner(this);
            dashHitbox.Deactivate();
        }

        hitboxArmed = false;
        hitboxWasActive = false;
    }

    /// <summary>
    /// Attack Hitbox is overlap-only. On during slash/grapple active frames, and during dash (dash = attack).
    /// </summary>
    private void EnforceAttackHitboxOnlyDuringAttacks()
    {
        if (attackHitbox == null)
            return;

        bool dashUsingAttackBox = isDashing && (dashHitbox == null || dashHitbox == attackHitbox);
        if (dashUsingAttackBox)
            return;

        bool attackMoveActive =
            meleeState == MeleeState.Slash1
            || meleeState == MeleeState.Slash2
            || meleeState == MeleeState.Slash3
            || meleeState == MeleeState.AirSlash
            || meleeState == MeleeState.GrappleArm;

        if (!attackMoveActive || !hitboxArmed)
        {
            if (attackHitbox.IsActive)
            {
                attackHitbox.Deactivate();
                hitboxWasActive = false;
            }
            return;
        }

        float elapsed = meleeDuration - meleeTimer;
        float delay = meleeState == MeleeState.GrappleArm ? grappleHitboxDelay : slashHitboxDelay;
        float activeTime = meleeState == MeleeState.GrappleArm ? grappleHitboxActiveTime : slashHitboxActiveTime;
        bool inActiveWindow = elapsed >= delay && elapsed <= delay + activeTime;

        // Diving air slash keeps the box on past the normal active-frame window.
        if (meleeState == MeleeState.AirSlash && airSlashDiving)
            inActiveWindow = true;

        // Up-pull keeps the box on so it can latch onto overlapped objects.
        if (meleeState == MeleeState.GrappleArm && grapplePullActive)
            inActiveWindow = true;

        if (!inActiveWindow && attackHitbox.IsActive)
        {
            attackHitbox.Deactivate();
            hitboxWasActive = false;
        }
    }

    private void HandleGroundAttackPress()
    {
        switch (meleeState)
        {
            case MeleeState.Idle:
                if (Time.time <= slash3GraceUntil)
                {
                    slash3GraceUntil = -999f;
                    StartMelee(MeleeState.Slash3, slash3Duration, slash3Damage, "Slash3");
                    break;
                }

                slash1PressTime = Time.time;
                slash3GraceUntil = -999f;
                queuedSlash2 = false;
                queuedSlash3 = false;
                StartMelee(MeleeState.Slash1, slash1Duration, slash1Damage, "Slash1");
                break;

            case MeleeState.Slash1:
                if (Time.time - slash1PressTime <= comboInputWindow)
                    queuedSlash2 = true;
                break;

            case MeleeState.Slash2:
                queuedSlash3 = true;
                break;

            case MeleeState.Slash3:
            case MeleeState.AirSlash:
            case MeleeState.GrappleArm:
                break;
        }
    }

    private void TryStartAirSlash()
    {
        if (meleeState == MeleeState.AirSlash || IsGrappling)
            return;

        if (IsGroundSlashActive())
            EndMeleeImmediate();

        StartMelee(MeleeState.AirSlash, airSlashDuration, airSlashDamage, "AirSlash");
        queuedSlash2 = false;
        queuedSlash3 = false;
        slash3GraceUntil = -999f;

        // Already holding Down when the air slash starts → dive immediately.
        if (moveInput.y <= -airSlashDiveDownThreshold)
            airSlashDiving = true;
    }

    private void TickAirSlashDiveInput()
    {
        if (meleeState != MeleeState.AirSlash || isGrounded)
            return;

        if (moveInput.y <= -airSlashDiveDownThreshold)
            airSlashDiving = true;
    }

    private void ApplyAirSlashDiveMotion()
    {
        if (!airSlashDiving || meleeState != MeleeState.AirSlash || isGrounded || rb == null)
            return;

        float diveSpeed = Mathf.Max(0.1f, airSlashDiveSpeed);
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, -diveSpeed);
    }

    private void StartMelee(MeleeState state, float duration, int damage, string triggerName)
    {
        if (attackHitbox != null)
            attackHitbox.Deactivate();

        meleeState = state;
        currentMeleeSpeed = ResolveSlashSpeedMultiplier(state);
        ApplyAnimatorAttackSpeed();

        // Resolve after animator.speed is set so clip length matches wall-clock at this speed.
        meleeDuration = ResolveAttackDuration(duration, GetClipNameForMeleeState(state));
        meleeTimer = meleeDuration;
        pendingHitDamage = damage;
        hitboxArmed = true;
        hitboxWasActive = false;
        grappleIsGrab = false;
        grapplePullActive = false;
        grappleAnchorLocked = false;

        if (state == MeleeState.Slash2)
            slash2StartTime = Time.time;

        airSlashDiving = false;

        // Mega Man Zero–style step-in on ground slashes only.
        if (IsGroundSlashState(state))
            ApplyGroundSlashStep();

        BeginShootAnimation(meleeDuration);

        if (animator != null && !string.IsNullOrEmpty(triggerName))
            animator.SetTrigger(triggerName);

        if (animator != null)
            animator.SetBool("IsAttacking", true);
    }

    private float ResolveSlashSpeedMultiplier(MeleeState state)
    {
        float speed = GetAttackSpeedMultiplier();

        bool isSlash =
            state == MeleeState.Slash1 ||
            state == MeleeState.Slash2 ||
            state == MeleeState.Slash3 ||
            state == MeleeState.AirSlash;

        if (isSlash && dashCancelSlashQueued)
        {
            dashCancelSlashQueued = false;
            speed *= Mathf.Max(1f, dashCancelSlashSpeedMul);
        }
        else if (!isSlash)
        {
            dashCancelSlashQueued = false;
        }

        return Mathf.Max(0.01f, speed);
    }

    private static bool IsGroundSlashState(MeleeState state)
    {
        return state == MeleeState.Slash1
               || state == MeleeState.Slash2
               || state == MeleeState.Slash3;
    }

    private void ApplyGroundSlashStep()
    {
        float step = groundSlashStepDistance;
        if (step <= 0.0001f || rb == null)
            return;

        Vector2 pos = rb.position;
        pos.x += facingSign * step;
        rb.position = pos;
    }

    private void StartGrapple(int damage, bool air, bool grab)
    {
        ResetRapidAttackSpeed();
        dashCancelSlashQueued = false;
        ClearGrabbedTargets();

        if (attackHitbox != null)
            attackHitbox.Deactivate();

        grappleIsGrab = grab;
        grappleIsAirMove = air;
        ClearGrapplePullState();
        pendingHitDamage = damage;
        hitboxArmed = true;
        hitboxWasActive = false;
        currentMeleeSpeed = 1f;

        meleeState = MeleeState.GrappleArm;
        ApplyAnimatorAttackSpeed();
        string clipName = air ? "Grapple Arm Air" : "Grapple Arm";
        meleeDuration = ResolveAttackDuration(grappleDuration, clipName);
        meleeTimer = meleeDuration;

        BeginShootAnimation(meleeDuration);

        // Air grapple hangs until the move finishes — no fall, no cancel into other actions.
        if (air)
        {
            grappleAirHang = true;
            SetAirHang(true);
        }
        else
        {
            grappleAirHang = false;
        }

        if (animator != null)
        {
            animator.SetTrigger(air ? "GrappleArmAir" : "GrappleArm");
            animator.SetBool("IsAttacking", true);
        }
    }

    private static string GetClipNameForMeleeState(MeleeState state)
    {
        switch (state)
        {
            case MeleeState.Slash1: return "Slash 1";
            case MeleeState.Slash2: return "Slash 2";
            case MeleeState.Slash3: return "Slash 3";
            case MeleeState.AirSlash: return "Air Slash";
            case MeleeState.GrappleArm: return "Grapple Arm";
            default: return null;
        }
    }

    /// <summary>
    /// Keep IsAttacking true for the full Animator clip so transitions do not cut the anim short.
    /// </summary>
    private float ResolveAttackDuration(float fallbackSeconds, string clipName)
    {
        float duration = Mathf.Max(0.05f, fallbackSeconds);

        if (!syncAttackDurationToAnimation || string.IsNullOrEmpty(clipName))
            return duration + Mathf.Max(0f, attackDurationPadding);

        float clipLength = GetAnimatorClipLength(clipName);
        if (clipLength > 0.05f)
            duration = Mathf.Max(duration, clipLength);

        return duration + Mathf.Max(0f, attackDurationPadding);
    }

    private float GetAnimatorClipLength(string clipName)
    {
        if (animator == null || animator.runtimeAnimatorController == null)
            return -1f;

        AnimationClip[] clips = animator.runtimeAnimatorController.animationClips;
        if (clips == null)
            return -1f;

        float best = -1f;
        for (int i = 0; i < clips.Length; i++)
        {
            AnimationClip clip = clips[i];
            if (clip == null)
                continue;

            if (!string.Equals(clip.name, clipName, StringComparison.OrdinalIgnoreCase))
                continue;

            float length = clip.length;
            if (animator.speed > 0.01f)
                length /= animator.speed;

            if (length > best)
                best = length;
        }

        return best;
    }

    private void TickMelee(float dt)
    {
        if (meleeState == MeleeState.Idle)
            return;

        if (meleeState == MeleeState.AirSlash && isGrounded && meleeTimer < meleeDuration - 0.02f)
        {
            EndMeleeImmediate();
            return;
        }

        // Diving air slash: stay in the attack until she hits the ground.
        if (meleeState == MeleeState.AirSlash && airSlashDiving && airSlashDiveUntilLand && !isGrounded)
        {
            if (meleeTimer > 0f)
                meleeTimer -= dt;

            UpdateSlashHitbox();
            return;
        }

        meleeTimer -= dt;

        if (meleeState == MeleeState.GrappleArm)
            UpdateGrappleHitbox();
        else
            UpdateSlashHitbox();

        if (meleeTimer > 0f)
            return;

        if (meleeState == MeleeState.GrappleArm)
        {
            // Keep the move alive until an in-progress Up-pull finishes reeling her in.
            bool holdUp = moveInput.y >= grappleUpThreshold;
            if (holdUp && grapplePullActive && !grapplePullArrived)
            {
                meleeTimer = 0.01f;
                if (grappleIsAirMove)
                {
                    grappleAirHang = true;
                    SetAirHang(true);
                }
                return;
            }

            EndMeleeImmediate();
            return;
        }

        if (meleeState == MeleeState.Slash1 && queuedSlash2)
        {
            queuedSlash2 = false;
            StartMelee(MeleeState.Slash2, slash2Duration, slash2Damage, "Slash2");
            return;
        }

        if (meleeState == MeleeState.Slash2 && queuedSlash3)
        {
            queuedSlash3 = false;
            slash3GraceUntil = -999f;
            StartMelee(MeleeState.Slash3, slash3Duration, slash3Damage, "Slash3");
            return;
        }

        if (meleeState == MeleeState.Slash2 && !queuedSlash3)
            slash3GraceUntil = Time.time + comboInputWindow;
        else
            slash3GraceUntil = -999f;

        EndMeleeImmediate(clearSlash3Grace: false);
    }

    private void UpdateSlashHitbox()
    {
        if (attackHitbox == null || !hitboxArmed)
            return;

        // Down-dive air slash keeps the hurtbox on until she lands.
        if (meleeState == MeleeState.AirSlash && airSlashDiving)
        {
            if (!hitboxWasActive)
            {
                attackHitbox.Activate(pendingHitDamage, applyDamage: true);
                hitboxWasActive = true;
            }
            return;
        }

        float speed = Mathf.Max(0.01f, currentMeleeSpeed);
        float delay = slashHitboxDelay / speed;
        float active = slashHitboxActiveTime / speed;
        float elapsed = meleeDuration - meleeTimer;
        bool shouldBeActive = elapsed >= delay && elapsed <= delay + active;

        if (shouldBeActive && !hitboxWasActive)
        {
            attackHitbox.Activate(pendingHitDamage, applyDamage: true);
            hitboxWasActive = true;
        }
        else if (!shouldBeActive && hitboxWasActive)
        {
            attackHitbox.Deactivate();
            hitboxWasActive = false;
        }
    }

    private void UpdateGrappleHitbox()
    {
        if (attackHitbox == null || !hitboxArmed)
            return;

        float elapsed = meleeDuration - meleeTimer;
        bool shouldBeActive = elapsed >= grappleHitboxDelay &&
                              elapsed <= grappleHitboxDelay + grappleHitboxActiveTime;

        // Keep detecting overlaps while reeling in (latch onto grabbed objects).
        if (grapplePullActive)
            shouldBeActive = true;

        if (shouldBeActive && !hitboxWasActive)
        {
            attackHitbox.Activate(pendingHitDamage, applyDamage: true);
            hitboxWasActive = true;
        }
        else if (!shouldBeActive && hitboxWasActive)
        {
            attackHitbox.Deactivate();
            hitboxWasActive = false;
        }
    }

    /// <summary>
    /// Up = after full arm extension, world-anchor the AttackBox and reel Malice to its front edge
    /// at the same timing as the arm-retract portion of the clip. Overlap can latch onto an object.
    /// </summary>
    private void TickGrapplePullInput()
    {
        if (!IsGrappling || attackHitbox == null)
            return;

        bool holdUp = moveInput.y >= grappleUpThreshold;

        if (holdUp)
        {
            // Wait for full arm extension before locking the tip or moving Malice.
            if (!HasReachedGrapplePullExtensionFrame())
            {
                grapplePullActive = false;
                if (grappleIsGrab)
                    FollowGrabbedTargets();
                return;
            }

            if (!grappleAnchorLocked)
                BeginGrapplePullAnchor();

            grapplePullActive = true;

            // Air grapple: stay hung until the reel-in reaches the AttackBox front edge.
            if (grappleIsAirMove && !grapplePullArrived)
            {
                grappleAirHang = true;
                SetAirHang(true);
            }

            TryAcquireGrapplePullLatch();
            RefreshGrapplePullAnchorWorld();
            grapplePullTargetWorld = ComputeGrapplePullBodyTarget();

            PinAttackBoxToGrappleAnchor();
        }
        else
        {
            if (grapplePullActive || grappleAnchorLocked || attackBoxWorldAnchored)
                ResetAttackBoxFacingMirrorCapture();

            ClearGrapplePullState();
        }

        // Down-grab mode still yanks grabbed targets onto the AttackBox.
        if (grappleIsGrab)
            FollowGrabbedTargets();
    }

    private void BeginGrapplePullAnchor()
    {
        EnsureGrapplePullAnchorLocalCached();

        // Freeze tip from the extension-frame pose, then detach so Animator cannot tow it back.
        grappleAnchorWorld = TransformGrapplePullAnchorToWorld(grapplePullAnchorLocalLeft);
        UnparentAttackBoxToWorldAnchor();
        PinAttackBoxToGrappleAnchor();

        grapplePullStartBodyPos = rb != null ? rb.position : (Vector2)transform.position;
        grapplePullTargetWorld = ComputeGrapplePullBodyTarget();
        grapplePullArrived = false;
        grappleAnchorLocked = true;
    }

    private void UnparentAttackBoxToWorldAnchor()
    {
        if (attackHitbox == null || attackBoxWorldAnchored)
            return;

        Transform box = attackHitbox.transform;
        attackBoxDefaultParent = box.parent;
        attackBoxAnchoredScale = box.lossyScale;
        box.SetParent(null, true);
        box.position = grappleAnchorWorld;
        box.localScale = attackBoxAnchoredScale;
        attackBoxWorldAnchored = true;
    }

    private void RestoreAttackBoxParent()
    {
        if (!attackBoxWorldAnchored || attackHitbox == null)
            return;

        Transform box = attackHitbox.transform;
        if (attackBoxDefaultParent != null)
            box.SetParent(attackBoxDefaultParent, true);
        else
            box.SetParent(transform, true);

        attackBoxWorldAnchored = false;
        attackBoxDefaultParent = null;
        ResetAttackBoxFacingMirrorCapture();
        MirrorAttackBoxForFacing();
    }

    /// <summary>
    /// World-space front edge of the AttackBox (edge furthest in Malice's facing direction).
    /// </summary>
    private Vector2 ComputeAttackBoxFrontEdgeWorld()
    {
        if (attackHitbox == null)
            return grappleAnchorWorld;

        Collider2D col = attackHitbox.GetComponent<Collider2D>();
        if (col == null)
            return grappleAnchorWorld;

        // Box is world-anchored; bounds are authoritative.
        Bounds b = col.bounds;
        float frontX = facingSign >= 0f ? b.max.x : b.min.x;
        return new Vector2(frontX, b.center.y);
    }

    /// <summary>
    /// Body reel target: full 2D to the box front edge in air; on ground keep her Y so she slides on the floor.
    /// </summary>
    private Vector2 ComputeGrapplePullBodyTarget()
    {
        if (grapplePullLatchedToObject)
        {
            Vector2 latch = grappleAnchorWorld;
            if (!grappleIsAirMove && rb != null)
                latch.y = rb.position.y;
            return latch;
        }

        Vector2 edge = ComputeAttackBoxFrontEdgeWorld();
        if (!grappleIsAirMove && rb != null)
            edge.y = rb.position.y;
        return edge;
    }

    private bool HasReachedGrapplePullExtensionFrame()
    {
        float anchorTime = GetGrapplePullAnchorTimeSeconds();
        if (anchorTime <= 0f)
            return true;

        // Use the grapple move clock (not whatever Animator state is current).
        // Ground often still blends from another state when Up is pressed; that was locking the tip early.
        float elapsed = Mathf.Max(0f, meleeDuration - meleeTimer);
        return elapsed + 0.0001f >= anchorTime;
    }

    private float GetGrapplePullAnchorTimeSeconds()
    {
        string clipName = grappleIsAirMove ? "Grapple Arm Air" : "Grapple Arm";
        int frame = grappleIsAirMove ? grappleAirPullAnchorFrame : grapplePullAnchorFrame;
        AnimationClip clip = FindAnimationClip(clipName);
        float fps = clip != null && clip.frameRate > 0.01f
            ? clip.frameRate
            : Mathf.Max(1f, grapplePullAnchorFrameRateFallback);

        float time = Mathf.Max(0, frame) / fps;
        if (clip != null && clip.length > 0f)
            time = Mathf.Min(time, clip.length);

        return time;
    }

    private void EnsureGrapplePullAnchorLocalCached()
    {
        if (grapplePullAnchorLocalCached)
            return;

        if (useManualGrapplePullAnchorLocal)
        {
            grapplePullAnchorLocalLeft = manualGrapplePullAnchorLocal;
            grapplePullAnchorLocalCached = true;
            return;
        }

        string clipName = grappleIsAirMove ? "Grapple Arm Air" : "Grapple Arm";
        AnimationClip clip = FindAnimationClip(clipName);

        if (clip == null || attackHitbox == null)
        {
            grapplePullAnchorLocalLeft = GetChildCharacterLocal(attackHitbox.transform);
            grapplePullAnchorLocalCached = true;
            return;
        }

        float time = GetGrapplePullAnchorTimeSeconds();

        int stateHash = 0;
        float normalizedTime = 0f;
        bool restoreAnimator = false;
        if (animator != null && animator.isInitialized)
        {
            AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
            stateHash = info.fullPathHash;
            normalizedTime = info.normalizedTime;
            restoreAnimator = stateHash != 0;
        }

        // Sample the clip pose, read AttackBox local (left-facing authored), then restore current anim.
        clip.SampleAnimation(gameObject, time);
        grapplePullAnchorLocalLeft = GetChildCharacterLocal(attackHitbox.transform);
        grapplePullAnchorLocalCached = true;

        if (restoreAnimator)
        {
            animator.Play(stateHash, 0, normalizedTime);
            animator.Update(0f);
        }
    }

    private Vector3 TransformGrapplePullAnchorToWorld(Vector3 leftFacingLocal)
    {
        Vector3 local = leftFacingLocal;
        bool mirror = spriteFacesLeft ? facingSign > 0f : facingSign < 0f;
        if (mirror)
        {
            float centerX = firePointFlipCenterLocal.x;
            local.x = centerX - (leftFacingLocal.x - centerX);
        }

        return transform.TransformPoint(local);
    }

    private AnimationClip FindAnimationClip(string clipName)
    {
        if (animator == null || animator.runtimeAnimatorController == null || string.IsNullOrEmpty(clipName))
            return null;

        AnimationClip[] clips = animator.runtimeAnimatorController.animationClips;
        if (clips == null)
            return null;

        for (int i = 0; i < clips.Length; i++)
        {
            AnimationClip clip = clips[i];
            if (clip == null)
                continue;

            if (string.Equals(clip.name, clipName, StringComparison.OrdinalIgnoreCase))
                return clip;
        }

        return null;
    }

    private void ApplyGrapplePullMotion(float dt)
    {
        if (!IsGrappling || !grapplePullActive || rb == null)
            return;

        RefreshGrapplePullAnchorWorld();
        PinAttackBoxToGrappleAnchor();
        grapplePullTargetWorld = ComputeGrapplePullBodyTarget();

        if (grappleIsAirMove && !grapplePullArrived)
        {
            grappleAirHang = true;
            SetAirHang(true);
        }

        float arrive = Mathf.Max(0.005f, grapplePullArriveDistance);
        Vector2 target = grapplePullTargetWorld;
        // Ground: never lift/drop her while reeling — only slide along X.
        if (!grappleIsAirMove)
            target.y = rb.position.y;

        float dist = Vector2.Distance(rb.position, target);
        if (grapplePullArrived || dist <= arrive)
        {
            if (dist > 0.001f)
                rb.MovePosition(target);
            rb.linearVelocity = Vector2.zero;
            grapplePullArrived = true;
            return;
        }

        // Reel Malice along the retract window of the clip (extension frame → clip end).
        float extendTime = GetGrapplePullAnchorTimeSeconds();
        AnimationClip clip = FindAnimationClip(grappleIsAirMove ? "Grapple Arm Air" : "Grapple Arm");
        float endTime = clip != null && clip.length > extendTime
            ? clip.length
            : extendTime + 0.2f;
        float curTime = GetCurrentGrappleAnimTimeSeconds();

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
            // Clip finished: close any remaining gap at fallback speed, then snap.
            float stepDist = Mathf.Max(0.1f, grapplePullSpeed) * dt;
            desired = Vector2.MoveTowards(rb.position, target, stepDist);
        }

        if (!grappleIsAirMove)
            desired.y = rb.position.y;

        rb.MovePosition(desired);
        rb.linearVelocity = Vector2.zero;

        if (Vector2.Distance(rb.position, target) <= arrive)
        {
            rb.MovePosition(target);
            grapplePullArrived = true;
        }
    }

    private float GetCurrentGrappleAnimTimeSeconds()
    {
        AnimationClip clip = FindAnimationClip(grappleIsAirMove ? "Grapple Arm Air" : "Grapple Arm");
        float clipLength = clip != null && clip.length > 0.01f
            ? clip.length
            : Mathf.Max(0.01f, meleeDuration);

        // Same clock as the grapple move — keeps ground/air pull timing aligned with the arm.
        float elapsed = Mathf.Max(0f, meleeDuration - meleeTimer);
        return Mathf.Clamp(elapsed, 0f, clipLength);
    }

    private void RefreshGrapplePullAnchorWorld()
    {
        if (!grapplePullLatchedToObject)
            return;

        if (grapplePullLatchTarget == null)
        {
            // Object destroyed — keep last world point frozen.
            grapplePullLatchedToObject = false;
            grapplePullLatchTarget = null;
            return;
        }

        grappleAnchorWorld = grapplePullLatchTarget.TransformPoint(grapplePullLatchLocalOffset);
    }

    private void TryAcquireGrapplePullLatch()
    {
        if (grapplePullLatchedToObject || attackHitbox == null)
            return;

        Collider2D boxCol = attackHitbox.GetComponent<Collider2D>();
        if (boxCol == null || !boxCol.enabled)
            return;

        EnsureGrappleOverlapFilter();
        int count = boxCol.Overlap(grappleOverlapFilter, grappleOverlapBuffer);
        if (count <= 0)
            return;

        Collider2D best = null;
        float bestDistSq = float.PositiveInfinity;
        Vector2 boxPos = attackHitbox.transform.position;

        for (int i = 0; i < count; i++)
        {
            Collider2D other = grappleOverlapBuffer[i];
            if (!IsValidGrapplePullLatchCollider(other))
                continue;

            Vector2 closest = other.ClosestPoint(boxPos);
            float distSq = (closest - boxPos).sqrMagnitude;
            if (distSq >= bestDistSq)
                continue;

            bestDistSq = distSq;
            best = other;
        }

        if (best == null)
            return;

        Vector3 latchPoint = best.ClosestPoint(boxPos);
        LatchGrapplePullTo(best.transform, latchPoint);
    }

    private bool IsValidGrapplePullLatchCollider(Collider2D other)
    {
        if (other == null)
            return false;

        if (other.transform == transform || other.transform.IsChildOf(transform))
            return false;

        // Ignore our own hurtboxes / aura helpers.
        if (other.GetComponent<AttackHitbox>() != null)
            return false;

        return true;
    }

    private void LatchGrapplePullTo(Transform target, Vector3 worldPoint)
    {
        if (target == null || grapplePullLatchedToObject)
            return;

        if (target == transform || target.IsChildOf(transform))
            return;

        // Prefer the root / rigidbody object so moving platforms carry the latch point.
        Transform latchRoot = target;
        Rigidbody2D body = target.GetComponentInParent<Rigidbody2D>();
        if (body != null)
            latchRoot = body.transform;

        PlayerController player = target.GetComponentInParent<PlayerController>();
        if (player != null && player != this)
            latchRoot = player.transform;

        grapplePullLatchTarget = latchRoot;
        grapplePullLatchLocalOffset = latchRoot.InverseTransformPoint(worldPoint);
        grapplePullLatchedToObject = true;
        grappleAnchorWorld = worldPoint;
        grapplePullTargetWorld = ComputeGrapplePullBodyTarget();
        grappleAnchorLocked = true;
        grapplePullActive = true;
        PinAttackBoxToGrappleAnchor();
    }

    private void ClearGrapplePullState()
    {
        RestoreAttackBoxParent();

        grapplePullActive = false;
        grappleAnchorLocked = false;
        grapplePullLatchedToObject = false;
        grapplePullLatchTarget = null;
        grapplePullLatchLocalOffset = Vector3.zero;
        grapplePullAnchorLocalCached = false;
        grapplePullAnchorLocalLeft = Vector3.zero;
        grapplePullArrived = false;
        grapplePullStartBodyPos = Vector2.zero;
        grapplePullTargetWorld = Vector2.zero;
    }

    private void EnsureGrappleOverlapFilter()
    {
        if (grappleOverlapFilterReady)
        {
            grappleOverlapFilter.SetLayerMask(grapplePullLatchLayers);
            return;
        }

        grappleOverlapFilter = new ContactFilter2D
        {
            useTriggers = true,
            useLayerMask = true,
            useDepth = false
        };
        grappleOverlapFilter.SetLayerMask(grapplePullLatchLayers);
        grappleOverlapFilterReady = true;
    }

    private void HandleAttackTargetAcquired(Collider2D other, PlayerController player)
    {
        if (!IsGrappling || other == null)
            return;

        // Up-pull mode: latch onto whatever the AttackBox hits and reel Malice in.
        if ((grapplePullActive || moveInput.y >= grappleUpThreshold) && !grapplePullLatchedToObject)
        {
            if (IsValidGrapplePullLatchCollider(other))
            {
                Vector3 latchPoint = other.ClosestPoint(attackHitbox != null
                    ? attackHitbox.transform.position
                    : transform.position);
                LatchGrapplePullTo(player != null ? player.transform : other.transform, latchPoint);
            }
        }

        // Down-grab mode: stick targets onto the AttackBox.
        if (!grappleIsGrab)
            return;

        Transform target = player != null ? player.transform : other.transform;
        if (target == transform || target.IsChildOf(transform))
            return;

        if (grabbedTargets.Contains(target))
            return;

        grabbedTargets.Add(target);
        Rigidbody2D body = player != null
            ? player.GetComponent<Rigidbody2D>()
            : other.attachedRigidbody;
        grabbedBodies.Add(body);
    }

    private void FollowGrabbedTargets()
    {
        if (attackHitbox == null)
            return;

        Vector3 point = attackHitbox.transform.position;
        for (int i = grabbedTargets.Count - 1; i >= 0; i--)
        {
            Transform t = grabbedTargets[i];
            if (t == null)
            {
                grabbedTargets.RemoveAt(i);
                if (i < grabbedBodies.Count)
                    grabbedBodies.RemoveAt(i);
                continue;
            }

            Rigidbody2D body = i < grabbedBodies.Count ? grabbedBodies[i] : null;
            if (body != null)
            {
                body.linearVelocity = Vector2.zero;
                body.MovePosition(point);
            }
            else
            {
                t.position = point;
            }
        }
    }

    private void ClearGrabbedTargets()
    {
        grabbedTargets.Clear();
        grabbedBodies.Clear();
    }

    private float GetAttackSpeedMultiplier()
    {
        int tier = Mathf.Clamp(attackSpeedTier, 0, Mathf.Max(0, maxAttackSpeedTier));
        return 1f + tier * Mathf.Max(0f, attackSpeedPerTier);
    }

    private void RegisterRapidAttackPress()
    {
        lastAttackPressTime = Time.time;
        rapidAttackPressCount = Mathf.Max(0, rapidAttackPressCount) + 1;

        int perTier = Mathf.Max(1, pressesPerSpeedTier);
        attackSpeedTier = Mathf.Min(
            Mathf.Max(0, maxAttackSpeedTier),
            rapidAttackPressCount / perTier);
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
        currentMeleeSpeed = 1f;
        ApplyAnimatorAttackSpeed();
    }

    private void ApplyAnimatorAttackSpeed()
    {
        if (animator == null)
            return;

        bool slashActive =
            meleeState == MeleeState.Slash1 ||
            meleeState == MeleeState.Slash2 ||
            meleeState == MeleeState.Slash3 ||
            meleeState == MeleeState.AirSlash;

        animator.speed = slashActive ? Mathf.Max(0.01f, currentMeleeSpeed) : 1f;
    }

    private void EndMeleeImmediate(bool clearSlash3Grace = true)
    {
        bool wasGrapple = IsGrappling;

        meleeState = MeleeState.Idle;
        meleeTimer = 0f;
        currentMeleeSpeed = 1f;
        queuedSlash2 = false;
        queuedSlash3 = false;
        hitboxArmed = false;
        hitboxWasActive = false;
        pendingHitDamage = 0;
        grappleIsGrab = false;
        ClearGrapplePullState();
        airSlashDiving = false;

        if (clearSlash3Grace)
            slash3GraceUntil = -999f;

        if (attackHitbox != null)
            attackHitbox.Deactivate();

        ApplyAnimatorAttackSpeed();

        if (wasGrapple)
        {
            ClearGrabbedTargets();
            ResetAttackBoxFacingMirrorCapture();
        }

        if (grappleAirHang || wasGrapple)
        {
            grappleAirHang = false;
            SetAirHang(false);
        }

        isShooting = false;
        shootAnimTimer = 0f;

        if (animator != null)
            animator.SetBool("IsAttacking", false);
    }

    private bool IsGroundSlashActive()
    {
        return meleeState == MeleeState.Slash1
               || meleeState == MeleeState.Slash2
               || meleeState == MeleeState.Slash3;
    }

    private void SetDashHitboxActive(bool active)
    {
        // Dash counts as an attack — use dedicated dash box, or the Attack Hitbox if none is assigned.
        AttackHitbox box = dashHitbox != null ? dashHitbox : attackHitbox;
        if (box == null)
        {
            dashHitboxActive = false;
            return;
        }

        if (active)
        {
            // If dash shares the attack box, end any slash window first so dash owns it cleanly.
            if (box == attackHitbox)
            {
                hitboxArmed = false;
                hitboxWasActive = false;
            }

            box.Activate(dashContactDamage, applyDamage: true);
            dashHitboxActive = true;
        }
        else if (dashHitboxActive)
        {
            box.Deactivate();
            dashHitboxActive = false;
        }
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

        // Soft dark-purple ↔ bright-violet shimmer (photosensitivity-safe).
        Color purple = Color.Lerp(chargeAuraColor, chargeAuraStrongColor, charge01);
        float flickerSpeed = Mathf.Lerp(auraFlickerSpeed, Mathf.Min(auraFlickerSpeed * 1.75f, 2.4f), charge01);
        auraFlickerPhase += Time.deltaTime * flickerSpeed;
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

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        comboInputWindow = Mathf.Max(0.05f, comboInputWindow);
        slash1Duration = Mathf.Max(0.05f, slash1Duration);
        slash2Duration = Mathf.Max(0.05f, slash2Duration);
        slash3Duration = Mathf.Max(0.05f, slash3Duration);
        airSlashDuration = Mathf.Max(0.05f, airSlashDuration);
        attackDurationPadding = Mathf.Max(0f, attackDurationPadding);
        slashHitboxDelay = Mathf.Max(0f, slashHitboxDelay);
        slashHitboxActiveTime = Mathf.Max(0.01f, slashHitboxActiveTime);
        airSlashDiveSpeed = Mathf.Max(0.1f, airSlashDiveSpeed);
        groundSlashStepDistance = Mathf.Max(0f, groundSlashStepDistance);
        dashCancelSlashSpeedMul = Mathf.Max(1f, dashCancelSlashSpeedMul);
        dashCancelSlashWindow = Mathf.Max(0f, dashCancelSlashWindow);
        mediumChargeSeconds = Mathf.Max(0.05f, mediumChargeSeconds);
        bigChargeSeconds = Mathf.Max(mediumChargeSeconds, bigChargeSeconds);
        auraDelayAfterPress = Mathf.Max(0f, auraDelayAfterPress);
        grappleDuration = Mathf.Max(0.05f, grappleDuration);
        grappleHitboxDelay = Mathf.Max(0f, grappleHitboxDelay);
        grappleHitboxActiveTime = Mathf.Max(0.01f, grappleHitboxActiveTime);
        grapplePullSpeed = Mathf.Max(0.1f, grapplePullSpeed);
        grapplePullArriveDistance = Mathf.Max(0.01f, grapplePullArriveDistance);
        grapplePullAnchorFrame = Mathf.Max(0, grapplePullAnchorFrame);
        grappleAirPullAnchorFrame = Mathf.Max(0, grappleAirPullAnchorFrame);
        grapplePullAnchorFrameRateFallback = Mathf.Max(1f, grapplePullAnchorFrameRateFallback);
        grappleMaxDamage = Mathf.Max(1, grappleMaxDamage);
        if (grapplePartialDamage <= 0)
            grapplePartialDamage = Mathf.Max(1, grappleMaxDamage / 2);
        maliceDashDistance = Mathf.Max(0.1f, maliceDashDistance);
        maliceDashSpeed = Mathf.Max(0.1f, maliceDashSpeed);
        maliceDashSmoothStop = Mathf.Max(0f, maliceDashSmoothStop);
        maliceMaxAirDashes = Mathf.Max(0, maliceMaxAirDashes);
        maliceDashJumpMomentumDuration = Mathf.Max(0.01f, maliceDashJumpMomentumDuration);
        maliceAfterimageCount = Mathf.Max(1, maliceAfterimageCount);
        maliceAfterimageSpacing = Mathf.Max(0.01f, maliceAfterimageSpacing);
        maliceAfterimageAlphaEnd = Mathf.Min(maliceAfterimageAlphaEnd, maliceAfterimageAlphaStart);
        dashContactDamage = Mathf.Max(0, dashContactDamage);
        slash1Damage = Mathf.Max(0, slash1Damage);
        slash2Damage = Mathf.Max(0, slash2Damage);
        slash3Damage = Mathf.Max(0, slash3Damage);
        airSlashDamage = Mathf.Max(0, airSlashDamage);
        auraFlickerSpeed = Mathf.Clamp(auraFlickerSpeed, 0.25f, 2.5f);
        auraFlickerStrength = Mathf.Clamp(auraFlickerStrength, 0.05f, 0.5f);
    }
#endif
}
