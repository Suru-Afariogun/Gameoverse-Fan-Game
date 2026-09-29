using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.InputSystem;

/// <summary>
/// Kit — Mega Man style blaster character.
/// Grounded shooting locks movement. Charging unlocks movement only once the charge aura appears.
/// Small / medium / big shots wait for shoot animation before spawning.
/// </summary>
public class KitPlayerController : PlayerController
{
    [Header("Kit - Projectile Prefabs")]
    [SerializeField] private Projectile smallProjectilePrefab;
    [SerializeField] private Projectile mediumProjectilePrefab;
    [SerializeField] private Projectile bigProjectilePrefab;
    [SerializeField] private float shootCooldown = 0.12f;

    [Header("Kit - VFX Prefabs")]
    [Tooltip("Small Buster Blast prefab for small shots.")]
    [SerializeField] private GameVisualEffect busterBlastSmallPrefab;
    [Tooltip("Medium + big Buster Blast prefab (big shots scale up from this art).")]
    [FormerlySerializedAs("busterBlastMediumPrefab")]
    [FormerlySerializedAs("busterBlastBigPrefab")]
    [SerializeField] private GameVisualEffect busterBlastMediumBigPrefab;

    [Header("Kit - Charge Thresholds")]
    [SerializeField] private float mediumChargeSeconds = 1.5f;
    [SerializeField] private float bigChargeSeconds = 3f;
    [Tooltip("Extra move speed once Kit reaches half (medium) charge and can move with the aura.")]
    [SerializeField] private float mediumChargeMoveSpeedBonus = 3f;
    [Tooltip("Extra move speed once Kit reaches max (big) charge.")]
    [SerializeField] private float bigChargeMoveSpeedBonus = 6f;

    [Header("Kit - Pink Charge Aura")]
    [SerializeField] private bool showChargeAura = true;
    [SerializeField] private Color chargeAuraColor = new Color(1f, 0.45f, 0.75f, 1f);
    [Tooltip("More solid pink near full charge.")]
    [SerializeField] private Color chargeAuraStrongColor = new Color(1f, 0.25f, 0.7f, 1f);
    [Tooltip("Soft yellow tint used in the gentle pink↔yellow shimmer.")]
    [SerializeField] private Color chargeAuraFlickerColor = new Color(1f, 0.92f, 0.55f, 1f);
    [SerializeField] private float auraBaseScale = 1.18f;
    [SerializeField] private float auraPulseAmount = 0.05f;
    [SerializeField] private float auraPulseSpeed = 6f;
    [Tooltip("Base shimmer cycles per second. Kept slow for photosensitivity safety.")]
    [SerializeField] private float auraFlickerSpeed = 1.25f;
    [Tooltip("How strongly yellow mixes in at peak shimmer (keep low for a subtle look).")]
    [SerializeField] [Range(0.05f, 0.5f)] private float auraFlickerStrength = 0.28f;
    [Tooltip("Delay after a small shot fires before the charge aura can appear.")]
    [SerializeField] private float auraDelayAfterSmallShot = 0.2f;
    [SerializeField] [Range(0f, 1f)] private float auraAlphaStart = 0.25f;
    [SerializeField] [Range(0f, 1f)] private float auraAlphaAtMedium = 0.65f;
    [SerializeField] [Range(0f, 1f)] private float auraAlphaAtBig = 0.95f;
    [Tooltip("Unused — aura now matches the character sorting plane so backgrounds cannot cover it.")]
    [SerializeField] private int auraSortingBehind = 2;

    [Header("Kit - Animation / Ground Lock")]
    [SerializeField] private float shootAnimDuration = 0.28f;
    [Tooltip("While grounded and firing (or charging before the aura appears), Kit cannot walk/run.")]
    [SerializeField] private bool lockMovementWhileGroundShooting = true;
    [Tooltip("How long to wait for a shoot anim state before forcing the shot spawn.")]
    [SerializeField] private float shotAnimWaitTimeout = 0.4f;
    [Tooltip("Animator state names that count as Kit being in a shot animation.")]
    [SerializeField] private string[] shootAnimStateNames =
    {
        "Shooting on ground",
        "Shooting upward",
        "Shooting downward on ground",
        "Jump Shot",
        "Fall Shot",
        "Shooting upward in air",
        "Shooting downward in air",
        "Hovering while shooting",
        "Hovering Shooting upward",
        "Hovering Shooting downward"
    };

    [Header("Kit - Double Jump")]
    [Tooltip("Extra jumps allowed in the air. Refills on landing.")]
    [SerializeField] private int kitMaxAirJumps = 1;
    [Tooltip("Upward speed of the double jump. <= 0 uses normal Jump Force.")]
    [SerializeField] private float kitDoubleJumpForce = 0f;

    [Header("Kit - Hover")]
    [Tooltip("Seconds Kit stays perfectly level after hover starts.")]
    [SerializeField] private float hoverHoldSeconds = 5f;
    [Tooltip("Downward speed once the level hover time is over.")]
    [SerializeField] private float hoverDescendSpeed = 1.5f;
    [Tooltip("How quickly Kit eases into the slow descent.")]
    [SerializeField] private float hoverDescendAcceleration = 4f;
    [Tooltip("Off: the level-hover time is shared across every hover in one air trip (resets on landing). " +
             "On: each re-press of jump gives a fresh level-hover time.")]
    [SerializeField] private bool hoverLevelTimeRefreshesOnReactivate = false;

    [Header("Kit - Boosted Dash (max charge)")]
    [Tooltip("Dash speed multiplier while Kit is at max charge.")]
    [SerializeField] private float boostedDashSpeedMultiplier = 1.4f;
    [Tooltip("Dash distance multiplier while Kit is at max charge.")]
    [SerializeField] private float boostedDashDistanceMultiplier = 1.6f;

    [Header("Kit - Dash Override")]
    [SerializeField] private float kitDashDistance = 5.25f;
    [SerializeField] private float kitDashSpeed = 17f;
    [SerializeField] private float kitDashSmoothStop = 0.14f;
    [SerializeField] private int kitMaxAirDashes = 2;
    [SerializeField] private bool kitAllowDashJump = true;
    [SerializeField] private float kitDashJumpHorizontalSpeed = 0f;
    [SerializeField] private float kitDashJumpForce = 0f;
    [SerializeField] private float kitDashJumpMomentumDuration = 0.55f;
    [SerializeField] private bool kitDashJumpMomentumUntilLanded = true;

    [Header("Kit - Afterimages")]
    [SerializeField] private bool kitEnableDashAfterimages = true;
    [SerializeField] private int kitAfterimageCount = 3;
    [SerializeField] private float kitAfterimageSpacing = 0.1f;
    [SerializeField] private Color kitAfterimageColor = Color.white;
    [SerializeField] [Range(0f, 1f)] private float kitAfterimageAlphaStart = 0.55f;
    [SerializeField] [Range(0f, 1f)] private float kitAfterimageAlphaEnd = 0.15f;

    [Header("Kit - Attack Styles (temporary)")]
    [Tooltip("Degrees above/below aim for Spread Shot side pellets.")]
    [SerializeField] private float spreadShotAngleDegrees = 45f;
    [Tooltip("Seconds between Machine Gun rapid-fire pellets after the first (charged) shot.")]
    [SerializeField] private float machineGunFireInterval = 0.12f;
    [Tooltip("Max live Machine Gun pellets. Oldest are removed when over cap.")]
    [SerializeField] private int machineGunMaxLiveShots = 8;
    [Tooltip("How often rapid pellets refresh the shoot animator (1 = every shot).")]
    [SerializeField] private int machineGunAnimEveryNthShot = 2;
    [Tooltip("Machine Gun medium opener speed as a multiple of the small pellet speed (must be > 1).")]
    [SerializeField] private float machineGunMediumSpeedMultiplier = 1.35f;
    [Tooltip("Machine Gun big opener speed as a multiple of the small pellet speed (must be > medium).")]
    [SerializeField] private float machineGunBigSpeedMultiplier = 1.6f;

    [Header("Kit - Upgrades (Scratch's shop)")]
    [Tooltip("Ariel Action: extra level-hover seconds per level. At max level every air hover starts with a free air jump.")]
    [SerializeField] private float hoverSecondsPerAerialLevel = 2f;
    [Tooltip("Attack Style: Machine Gun / Spread Shot reach max charge this many seconds sooner per level.")]
    [SerializeField] private float styleChargeSecondsPerLevel = 0.5f;
    [SerializeField] private float minUpgradedChargeSeconds = 0.25f;
    [Tooltip("Attack Style level where Machine Gun / Spread Shot keep auto-charging while firing.")]
    [SerializeField] private int styleChargeWhileFiringLevel = 2;
    [Tooltip("Spread Shot bullets = 3 + Attack Style level, capped here.")]
    [SerializeField] private int spreadMaxBullets = 8;
    [Tooltip("Hyper Ability (full health): world units between shots in the Normal-style volley row.")]
    [SerializeField] private float hyperShotSpacing = 1f;
    [Tooltip("Hyper Ability shots travel this many times faster than a regular max charge shot.")]
    [SerializeField] private float hyperShotSpeedMultiplier = 2.5f;
    [Tooltip("Hyper Ability + Machine Gun: seconds between volleys while Attack is held.")]
    [SerializeField] private float hyperMachineGunVolleyInterval = 0.2f;

    private bool isCharging;
    private float chargeTimer;
    private float shootCooldownTimer;

    private bool pendingShot;
    private bool pendingShotHyper;
    private ProjectileShotType pendingShotType;
    private float pendingShotDeadline;
    private Vector2 pendingShotAim;

    private GameObject chargeAuraObject;
    private SpriteRenderer chargeAuraRenderer;
    private Material chargeAuraMaterial;
    private float auraAllowedAfterTime;
    private float auraFlickerPhase;

    // Machine Gun: passive auto-charge while not firing; hold = rapid small shots.
    private bool machineGunHolding;
    private float machineGunAutoCharge;
    private bool machineGunFirstShotQueued;
    private int machineGunPelletIndex;
    private readonly System.Collections.Generic.List<Projectile> liveMachineGunShots =
        new System.Collections.Generic.List<Projectile>(16);

    private int airJumpsRemaining;
    private bool isHovering;
    private float hoverTimer;
    // Jump still held from the double-jump press: hover starts when the double jump peaks.
    private bool hoverQueuedAtApex;
    private bool boostedDashActive;

    protected override void Awake()
    {
        base.Awake();
        SetCharacterId("Kit");

        dashDistance = kitDashDistance;
        dashSpeed = kitDashSpeed;
        dashSmoothStopDuration = kitDashSmoothStop;
        maxAirDashes = kitMaxAirDashes;
        allowDashJump = kitAllowDashJump;
        dashJumpHorizontalSpeed = kitDashJumpHorizontalSpeed;
        dashJumpForce = kitDashJumpForce;
        dashJumpMomentumDuration = kitDashJumpMomentumDuration;
        dashJumpMomentumUntilLanded = kitDashJumpMomentumUntilLanded;
        enableDashAfterimages = kitEnableDashAfterimages;
        dashAfterimageCount = kitAfterimageCount;
        dashAfterimageSpacing = kitAfterimageSpacing;
        dashAfterimageColor = kitAfterimageColor;
        dashAfterimageAlphaStart = kitAfterimageAlphaStart;
        dashAfterimageAlphaEnd = kitAfterimageAlphaEnd;
        airDashesRemaining = MaxAirDashesWithUpgrades;
        airJumpsRemaining = Mathf.Max(0, kitMaxAirJumps);

        SetupDashAfterimages();
        SetupChargeAura();
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        EndHover();
        hoverQueuedAtApex = false;
        boostedDashActive = false;
        machineGunHolding = false;
        machineGunFirstShotQueued = false;
        liveMachineGunShots.Clear();
        CancelPendingShot();
        SetChargeAuraVisible(false);
    }

    protected override void OnDestroy()
    {
        if (chargeAuraObject != null)
            Destroy(chargeAuraObject);

        if (chargeAuraMaterial != null)
            Destroy(chargeAuraMaterial);

        base.OnDestroy();
    }

    protected override void HandleCharacterUpdate()
    {
        if (shootCooldownTimer > 0f)
            shootCooldownTimer -= Time.deltaTime;

        if (UsesAutoCharge())
            TickAutoChargeStyle();
        else if (HyperActive)
            UpdateChargeAura(BigCharge);
        else if (isCharging)
        {
            chargeTimer += Time.deltaTime;
            // Aura waits until after the small shot has fired + delay.
            if (Time.time >= auraAllowedAfterTime)
            {
                // Aura is visible → charge movement unlock. Drop shoot-lock anim flag.
                isShooting = false;
                shootAnimTimer = 0f;
                UpdateChargeAura(chargeTimer);
            }
            else
            {
                SetChargeAuraVisible(false);
            }
        }
        else
        {
            SetChargeAuraVisible(false);
        }

        TickPendingShot();
    }

    private bool UsesMachineGunStyle()
    {
        return PlayerAttackStyle.Is(AttackStyleId.MachineGun);
    }

    private bool UsesSpreadShotStyle()
    {
        return PlayerAttackStyle.Is(AttackStyleId.SpreadShot);
    }

    private int AerialLevel => PlayerUpgrades.GetActiveLevel(this, UpgradeType.AerialAction);
    private int HyperLevel => PlayerUpgrades.GetActiveLevel(this, UpgradeType.HyperAbility);
    private int StyleLevel => PlayerUpgrades.GetActiveLevel(this, UpgradeType.AttackStyle);
    private bool HyperActive => PlayerUpgrades.IsHyperActive(this);
    private bool ChargesWhileFiring => StyleLevel >= Mathf.Max(0, styleChargeWhileFiringLevel);

    /// <summary>Machine Gun always auto-charges; Spread Shot does too once it can charge while firing.</summary>
    private bool UsesAutoCharge()
    {
        return UsesMachineGunStyle() || (UsesSpreadShotStyle() && ChargesWhileFiring);
    }

    private float BigCharge
    {
        get
        {
            if (!UsesMachineGunStyle() && !UsesSpreadShotStyle())
                return bigChargeSeconds;

            float faster = bigChargeSeconds - Mathf.Max(0f, styleChargeSecondsPerLevel) * StyleLevel;
            return Mathf.Max(Mathf.Max(0.05f, minUpgradedChargeSeconds), faster);
        }
    }

    private float MediumCharge => mediumChargeSeconds * (BigCharge / Mathf.Max(0.01f, bigChargeSeconds));

    private void TickAutoChargeStyle()
    {
        if (InputLocked || isStunned)
        {
            EndMachineGunFire();
            SetChargeAuraVisible(false);
            return;
        }

        if (HyperActive)
        {
            UpdateChargeAura(BigCharge);
            if (machineGunHolding && UsesMachineGunStyle() && !pendingShot && shootCooldownTimer <= 0f)
                QueueShot(ProjectileShotType.Big, replacePending: true, hyper: true);
            return;
        }

        if (machineGunHolding)
        {
            // After the opening (auto-charge level) shot, keep spraying smalls while held.
            if (!machineGunFirstShotQueued && !pendingShot && shootCooldownTimer <= 0f)
                FireMachineGunPellet();

            if (!ChargesWhileFiring)
            {
                SetChargeAuraVisible(false);
                return;
            }
        }

        machineGunAutoCharge += Time.deltaTime;
        machineGunAutoCharge = Mathf.Min(machineGunAutoCharge, BigCharge);
        chargeTimer = machineGunAutoCharge;
        isCharging = false;

        if (machineGunAutoCharge >= 0.12f)
            UpdateChargeAura(machineGunAutoCharge);
        else
            SetChargeAuraVisible(false);
    }

    private void FireMachineGunPellet()
    {
        if (GetPrefab(ProjectileShotType.Small) == null)
            return;

        // Every pellet: projectile + buster blast + meow/laser (all hard-capped elsewhere).
        SpawnShotBurst(
            GetAimDirection(),
            ProjectileShotType.Small,
            spawnBusterBlast: true,
            trackAsMachineGunShot: true);

        shootCooldownTimer = Mathf.Max(0.05f, machineGunFireInterval);
        SoundManager.Instance?.PlayKitFireRapid();

        machineGunPelletIndex++;
        int animEvery = Mathf.Max(1, machineGunAnimEveryNthShot);
        if (machineGunPelletIndex % animEvery == 0)
        {
            BeginShootAnimation(Mathf.Min(shootAnimDuration, machineGunFireInterval * 1.5f));
            PlayShootAnimatorTriggers();
        }
        else
        {
            isShooting = true;
            shootAnimTimer = Mathf.Max(shootAnimTimer, machineGunFireInterval);
        }
    }

    /// <summary>
    /// True once Kit is charging and the pink aura is allowed to show (after small shot + delay).
    /// Machine Gun auto-charge counts as aura-active for move unlock.
    /// </summary>
    private bool IsChargeAuraActive()
    {
        if (HyperActive)
            return true;

        if (UsesAutoCharge())
            return !machineGunHolding && machineGunAutoCharge > 0.01f;

        return isCharging && Time.time >= auraAllowedAfterTime;
    }

    /// <summary>
    /// A shot is going out: always stands in place, even when upgrades keep her charged
    /// (Hyper / auto-charge). Only a held charge with its aura up frees her to walk.
    /// </summary>
    private bool IsGroundShootLocked()
    {
        bool firing = isShooting || pendingShot || machineGunHolding;
        bool chargingBeforeAura = isCharging && !IsChargeAuraActive();
        return firing || chargingBeforeAura;
    }

    protected override void ApplyHorizontalMove()
    {
        if (lockMovementWhileGroundShooting && isGrounded && IsGroundShootLocked())
        {
            ApplyStandInPlaceMove();
            return;
        }

        base.ApplyHorizontalMove();
    }

    protected override float GetCurrentMoveSpeed()
    {
        float speed = base.GetCurrentMoveSpeed();

        // Speed bonus only while charging with aura up (same window she is free to move).
        if (!IsChargeAuraActive())
            return speed;

        float charge = HyperActive ? BigCharge : UsesAutoCharge() ? machineGunAutoCharge : chargeTimer;

        if (charge >= BigCharge)
            return speed + bigChargeMoveSpeedBonus;

        if (charge >= MediumCharge)
            return speed + mediumChargeMoveSpeedBonus;

        return speed;
    }

    protected override bool BlocksRunAnimationWhileShooting()
    {
        return IsGroundShootLocked();
    }

    protected override Vector2 GetAimDirection()
    {
        if (IsAimingDown())
            return new Vector2(facingSign, -1f).normalized;

        return base.GetAimDirection();
    }

    protected override void UpdateAnimator()
    {
        base.UpdateAnimator();
        if (animator == null)
            return;

        animator.SetBool("AimDown", IsAimingDown());
        animator.SetBool("IsHovering", isHovering);
        animator.SetBool("IsBoostedDashing", isDashing && boostedDashActive);
    }

    // ---------- Double jump + hover ----------

    protected override void ApplyJump()
    {
        if (!jumpRequested)
            return;

        // Ground jump, drop-through, wall jump and stun handling stay in the base.
        if (isStunned || BlocksActionCancel() || IsGrounded || CanWallJumpNow())
        {
            if (CanWallJumpNow())
                EndHover();

            base.ApplyJump();
            return;
        }

        jumpRequested = false;
        if (isHovering)
            return;

        if (airJumpsRemaining > 0)
        {
            PerformDoubleJump();
            return;
        }

        // Max Ariel Action: every hover starts with a free air jump (hover kicks in at its peak).
        if (AerialLevel >= PlayerUpgrades.MaxLevel)
        {
            PerformDoubleJump(consumeAirJump: false);
            return;
        }

        StartHover();
    }

    private void PerformDoubleJump(bool consumeAirJump = true)
    {
        if (rb == null)
            return;

        if (consumeAirJump)
            airJumpsRemaining--;
        float force = kitDoubleJumpForce > 0f ? kitDoubleJumpForce : jumpForce;
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, force);
        hoverQueuedAtApex = true;
        SoundManager.Instance?.PlayJump();

        if (animator != null && !isShooting && !isDashing)
            animator.Play("Jump", 0, 0f);
    }

    private bool IsJumpHeld()
    {
        return !inputLocked && controls != null && controls.PlayerControls.Jump.IsPressed();
    }

    private void StartHover()
    {
        if (isHovering || rb == null || isStunned)
            return;

        isHovering = true;
        hoverQueuedAtApex = false;
        if (hoverLevelTimeRefreshesOnReactivate)
            hoverTimer = 0f;

        bool hadDashJumpMomentum = isDashJumping;
        EndDashJumpMomentum();

        rb.gravityScale = 0f;
        float vx = hadDashJumpMomentum ? moveInput.x * GetCurrentMoveSpeed() : rb.linearVelocity.x;
        rb.linearVelocity = new Vector2(vx, 0f);

        if (animator != null)
            animator.SetTrigger("Hover");
    }

    private void EndHover()
    {
        if (!isHovering)
            return;

        isHovering = false;
        if (rb != null)
            rb.gravityScale = defaultGravityScale;

        if (animator != null)
            animator.ResetTrigger("Hover");
    }

    private void TickHover(float dt)
    {
        if (rb == null)
            return;

        if (hoverQueuedAtApex)
        {
            // Letting go before the peak cancels; the player can still re-press later.
            if (!IsJumpHeld() || isGrounded || isStunned || IsDead)
                hoverQueuedAtApex = false;
            else if (rb.linearVelocity.y <= 0f)
                StartHover();
        }

        if (!isHovering)
            return;

        if (!IsJumpHeld() || isStunned || isGrounded || IsDead)
        {
            EndHover();
            return;
        }

        hoverTimer += dt;
        float levelHoverSeconds = hoverHoldSeconds + Mathf.Max(0f, hoverSecondsPerAerialLevel) * AerialLevel;
        float vy = hoverTimer < levelHoverSeconds
            ? 0f
            : Mathf.MoveTowards(rb.linearVelocity.y, -hoverDescendSpeed, hoverDescendAcceleration * dt);
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, vy);
    }

    protected override void HandleCharacterFixedUpdate()
    {
        base.HandleCharacterFixedUpdate();
        TickHover(Time.fixedDeltaTime);
    }

    protected override void ApplyFallMultiplier()
    {
        if (isHovering)
            return;

        base.ApplyFallMultiplier();
    }

    protected override void OnLanded()
    {
        base.OnLanded();
        airJumpsRemaining = Mathf.Max(0, kitMaxAirJumps);
        hoverTimer = 0f;
        hoverQueuedAtApex = false;
        EndHover();
    }

    // ---------- Boosted dash (max charge) ----------

    private bool IsAtMaxCharge()
    {
        // Hyper Ability keeps Kit at max charge the whole time she is at full health.
        if (HyperActive)
            return true;

        if (UsesAutoCharge())
            return !machineGunHolding && machineGunAutoCharge >= BigCharge;

        return isCharging && Time.time >= auraAllowedAfterTime && chargeTimer >= BigCharge;
    }

    protected override void OnDashStarted()
    {
        base.OnDashStarted();
        boostedDashActive = IsAtMaxCharge() || isHovering;
        if (!boostedDashActive)
            return;

        float speed = Mathf.Max(0.1f, dashSpeed * boostedDashSpeedMultiplier);
        float distance = Mathf.Max(0.1f, dashDistance * boostedDashDistanceMultiplier);
        dashDuration = distance / speed;
        BeginDashAfterimageTrail(dashDuration);
    }

    protected override void OnDashEnded()
    {
        base.OnDashEnded();
        boostedDashActive = false;
    }

    protected override float GetCurrentDashVelocityX()
    {
        float velocity = base.GetCurrentDashVelocityX();
        return boostedDashActive ? velocity * boostedDashSpeedMultiplier : velocity;
    }

    protected override void OnAttackStarted(InputAction.CallbackContext context)
    {
        if (InputLocked || isStunned)
            return;

        if (HyperActive)
        {
            BeginHyperFire();
            return;
        }

        if (UsesMachineGunStyle())
        {
            BeginMachineGunFire();
            return;
        }

        if (UsesAutoCharge())
        {
            FireAutoChargedSpread();
            return;
        }

        isCharging = true;
        chargeTimer = 0f;
        // Aura (and charge-move unlock) only after a small shot actually fires + delay.
        auraAllowedAfterTime = float.PositiveInfinity;
        SetChargeAuraVisible(false);
        SoundManager.Instance?.StartChargeLoop(SoundManager.ChargeLoopId.Kit);

        QueueShot(ProjectileShotType.Small);
    }

    protected override void OnAttackCanceled(InputAction.CallbackContext context)
    {
        if (UsesMachineGunStyle())
        {
            EndMachineGunFire();
            return;
        }

        if (UsesAutoCharge())
            return;

        if (InputLocked || isStunned)
            return;

        if (!isCharging)
            return;

        float held = chargeTimer;
        isCharging = false;
        chargeTimer = 0f;
        SetChargeAuraVisible(false);
        SoundManager.Instance?.StopChargeLoop();

        if (held >= BigCharge)
            QueueShot(ProjectileShotType.Big, replacePending: true);
        else if (held >= MediumCharge)
            QueueShot(ProjectileShotType.Medium, replacePending: true);
    }

    /// <summary>Hyper Ability: every press fires a max-charge volley; Machine Gun keeps firing while held.</summary>
    private void BeginHyperFire()
    {
        isCharging = false;
        chargeTimer = 0f;
        SoundManager.Instance?.StopChargeLoop();

        if (UsesMachineGunStyle())
        {
            machineGunHolding = true;
            machineGunFirstShotQueued = false;
        }

        if (shootCooldownTimer > 0f || pendingShot)
            return;

        QueueShot(ProjectileShotType.Big, replacePending: true, hyper: true);
    }

    /// <summary>
    /// Upgraded Spread Shot: charge fills on its own. Small spreads don't spend it; a medium/big
    /// spread fires and empties it.
    /// </summary>
    private void FireAutoChargedSpread()
    {
        ProjectileShotType type = ShotTypeFromCharge(machineGunAutoCharge);
        if (type == ProjectileShotType.Small)
        {
            QueueShot(ProjectileShotType.Small);
            return;
        }

        machineGunAutoCharge = 0f;
        chargeTimer = 0f;
        SetChargeAuraVisible(false);
        QueueShot(type, replacePending: true);
    }

    private void BeginMachineGunFire()
    {
        machineGunHolding = true;
        machineGunPelletIndex = 0;
        ProjectileShotType first = ShotTypeFromCharge(machineGunAutoCharge);
        machineGunAutoCharge = 0f;
        chargeTimer = 0f;
        SetChargeAuraVisible(false);
        SoundManager.Instance?.StopChargeLoop();

        machineGunFirstShotQueued = true;
        QueueShot(first, replacePending: true);
    }

    private void EndMachineGunFire()
    {
        machineGunHolding = false;
        machineGunFirstShotQueued = false;
    }

    private ProjectileShotType ShotTypeFromCharge(float charge)
    {
        if (charge >= BigCharge)
            return ProjectileShotType.Big;
        if (charge >= MediumCharge)
            return ProjectileShotType.Medium;
        return ProjectileShotType.Small;
    }

    protected override void OnHitStunStarted()
    {
        isCharging = false;
        chargeTimer = 0f;
        machineGunHolding = false;
        machineGunFirstShotQueued = false;
        pendingShot = false;
        SoundManager.Instance?.StopChargeLoop();
        SetChargeAuraVisible(false);
        isShooting = false;
        shootAnimTimer = 0f;
        EndHover();
        hoverQueuedAtApex = false;
        base.OnHitStunStarted();
    }

    private void QueueShot(ProjectileShotType type, bool replacePending = false, bool hyper = false)
    {
        Projectile prefab = GetPrefab(type);
        if (prefab == null)
            return;

        if (type == ProjectileShotType.Small && shootCooldownTimer > 0f && !replacePending)
            return;

        // Don't stack another small while one is already waiting, unless replacing with charged shot.
        if (pendingShot && type == ProjectileShotType.Small && !replacePending)
            return;

        if (pendingShot && !replacePending && type != ProjectileShotType.Small)
            return;

        CancelPendingShot();

        pendingShot = true;
        pendingShotHyper = hyper;
        pendingShotType = type;
        pendingShotAim = GetAimDirection();
        pendingShotDeadline = Time.time + Mathf.Max(0.05f, shotAnimWaitTimeout);

        BeginShootAnimation(Mathf.Max(shootAnimDuration, shotAnimWaitTimeout));
        PlayShootAnimatorTriggers();
    }

    private void TickPendingShot()
    {
        if (!pendingShot)
            return;

        if (IsInShootAnimation() || Time.time >= pendingShotDeadline)
            FirePendingShot();
    }

    private void FirePendingShot()
    {
        if (!pendingShot)
            return;

        ProjectileShotType firedType = pendingShotType;
        Vector2 aim = pendingShotAim;
        bool hyper = pendingShotHyper;
        pendingShot = false;
        pendingShotHyper = false;

        if (GetPrefab(firedType) == null)
            return;

        if (hyper)
        {
            SpawnHyperVolley(aim);
            shootCooldownTimer = UsesMachineGunStyle() && machineGunHolding
                ? Mathf.Max(0.05f, hyperMachineGunVolleyInterval)
                : shootCooldown;
            SoundManager.Instance?.PlayKitFire(firedType);
            if (UsesMachineGunStyle())
                machineGunFirstShotQueued = false;
            return;
        }

        // Same spawn path for Normal / Spread / Machine Gun openers (medium+big use prefab speeds).
        SpawnShotBurst(aim, firedType);
        shootCooldownTimer = UsesMachineGunStyle() && machineGunHolding
            ? Mathf.Max(0.05f, machineGunFireInterval)
            : shootCooldown;
        SoundManager.Instance?.PlayKitFire(firedType);

        if (UsesMachineGunStyle())
            machineGunFirstShotQueued = false;
        else if (firedType == ProjectileShotType.Small)
            auraAllowedAfterTime = Time.time + Mathf.Max(0f, auraDelayAfterSmallShot);
    }

    private void CancelPendingShot()
    {
        pendingShot = false;
        pendingShotHyper = false;
    }

    private void SpawnShotBurst(
        Vector2 aim,
        ProjectileShotType type,
        bool spawnBusterBlast = true,
        bool trackAsMachineGunShot = false)
    {
        if (UsesSpreadShotStyle())
        {
            SpawnSpread(aim, type, spawnBusterBlast, trackAsMachineGunShot, 1f);
            return;
        }

        SpawnProjectile(aim, type, 0f, spawnBusterBlast, trackAsMachineGunShot);
    }

    private int SpreadBulletCount => Mathf.Clamp(3 + StyleLevel, 3, Mathf.Max(3, spreadMaxBullets));

    /// <summary>Bullets fanned evenly between +/- spread angle. Side bullets get a tiny muzzle offset so none overlap at spawn.</summary>
    private void SpawnSpread(
        Vector2 aim,
        ProjectileShotType type,
        bool spawnBusterBlast,
        bool trackAsMachineGunShot,
        float speedMultiplier)
    {
        int count = SpreadBulletCount;
        float maxAngle = spreadShotAngleDegrees;
        for (int i = 0; i < count; i++)
        {
            float t = count <= 1 ? 0.5f : i / (float)(count - 1);
            float angle = Mathf.Lerp(maxAngle, -maxAngle, t);
            float offset = Mathf.Abs(angle) > 0.01f ? 0.12f : 0f;
            SpawnProjectile(RotateAim(aim, angle), type, offset, spawnBusterBlast, trackAsMachineGunShot, speedMultiplier);
        }
    }

    /// <summary>
    /// Hyper Ability volley: Spread fires its full spread as max charge shots; Normal / Machine Gun
    /// fire 1 + Hyper level max charge shots in a row, one space apart. All travel faster.
    /// </summary>
    private void SpawnHyperVolley(Vector2 aim)
    {
        float speedMultiplier = Mathf.Max(1f, hyperShotSpeedMultiplier);

        if (UsesSpreadShotStyle())
        {
            SpawnSpread(aim, ProjectileShotType.Big, true, false, speedMultiplier);
            return;
        }

        int count = 1 + Mathf.Max(0, HyperLevel);
        float spacing = Mathf.Max(0f, hyperShotSpacing);
        for (int i = 0; i < count; i++)
            SpawnProjectile(aim, ProjectileShotType.Big, -i * spacing, i == 0, false, speedMultiplier);
    }

    private static Vector2 RotateAim(Vector2 direction, float degrees)
    {
        Vector2 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
        float rad = degrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad);
        float sin = Mathf.Sin(rad);
        return new Vector2(dir.x * cos - dir.y * sin, dir.x * sin + dir.y * cos).normalized;
    }

    private bool IsInShootAnimation()
    {
        if (animator == null)
            return true;

        if (shootAnimStateNames == null || shootAnimStateNames.Length == 0)
            return animator.GetBool("IsShooting");

        AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(0);
        if (StateMatchesShoot(current))
            return true;

        if (animator.IsInTransition(0))
        {
            AnimatorStateInfo next = animator.GetNextAnimatorStateInfo(0);
            if (StateMatchesShoot(next))
                return true;
        }

        return false;
    }

    private bool StateMatchesShoot(AnimatorStateInfo info)
    {
        for (int i = 0; i < shootAnimStateNames.Length; i++)
        {
            string stateName = shootAnimStateNames[i];
            if (string.IsNullOrWhiteSpace(stateName))
                continue;

            if (info.IsName(stateName))
                return true;
        }

        return false;
    }

    private Projectile GetPrefab(ProjectileShotType type)
    {
        switch (type)
        {
            case ProjectileShotType.Medium:
                return mediumProjectilePrefab;
            case ProjectileShotType.Big:
                return bigProjectilePrefab;
            default:
                return smallProjectilePrefab;
        }
    }

    /// <summary>
    /// Normal/Spread: prefab speeds. Machine Gun: medium/big openers are faster than small pellets.
    /// </summary>
    private float GetShotSpeed(ProjectileShotType type)
    {
        Projectile prefab = GetPrefab(type);
        float prefabSpeed = prefab != null ? prefab.Speed : 12f;

        if (!UsesMachineGunStyle())
            return prefabSpeed;

        float smallSpeed = smallProjectilePrefab != null ? smallProjectilePrefab.Speed : prefabSpeed;

        switch (type)
        {
            case ProjectileShotType.Medium:
                return smallSpeed * Mathf.Max(1.01f, machineGunMediumSpeedMultiplier);
            case ProjectileShotType.Big:
                return smallSpeed * Mathf.Max(
                    machineGunMediumSpeedMultiplier + 0.01f,
                    machineGunBigSpeedMultiplier);
            default:
                return smallSpeed;
        }
    }

    private int GetShotDamage(ProjectileShotType type)
    {
        Projectile prefab = GetPrefab(type);
        return prefab != null ? prefab.Damage : 1;
    }

    private GameVisualEffect GetBusterBlastPrefab(ProjectileShotType type)
    {
        return VisualEffects.ResolveBusterBlastPrefab(
            type,
            busterBlastSmallPrefab,
            busterBlastMediumBigPrefab);
    }

    private void SpawnProjectile(
        Vector2 direction,
        ProjectileShotType type,
        float muzzleOffset = 0f,
        bool spawnBusterBlast = true,
        bool trackAsMachineGunShot = false,
        float speedMultiplier = 1f)
    {
        Projectile prefab = GetPrefab(type);
        if (prefab == null)
            return;

        if (trackAsMachineGunShot)
            PruneAndCapMachineGunShots();

        // Negative offsets spawn behind the muzzle (Hyper volley rows trail the lead shot).
        Vector2 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
        Vector3 spawnPos = GetFirePosition() + (Vector3)(dir * muzzleOffset);
        Projectile shot = Instantiate(prefab, spawnPos, Quaternion.identity);

        // Canonical stats by shot type. Machine Gun medium/big use boosted speed over small pellets.
        shot.Launch(dir, GetShotSpeed(type) * Mathf.Max(0.01f, speedMultiplier), GetShotDamage(type), transform);

        if (trackAsMachineGunShot)
            liveMachineGunShots.Add(shot);

        if (spawnBusterBlast)
        {
            Transform muzzle = firePoint != null ? firePoint : transform;
            VisualEffects.SpawnBusterBlast(GetBusterBlastPrefab(type), muzzle, shot);
        }
    }

    private void PruneAndCapMachineGunShots()
    {
        for (int i = liveMachineGunShots.Count - 1; i >= 0; i--)
        {
            if (liveMachineGunShots[i] == null)
                liveMachineGunShots.RemoveAt(i);
        }

        int maxLive = Mathf.Max(2, machineGunMaxLiveShots);
        while (liveMachineGunShots.Count >= maxLive)
        {
            Projectile oldest = liveMachineGunShots[0];
            liveMachineGunShots.RemoveAt(0);
            if (oldest != null)
                Destroy(oldest.gameObject);
        }
    }

    private void PlayShootAnimatorTriggers()
    {
        if (animator == null)
            return;

        // Up/Down/Shoot are routed to ground, air or hover states by the Animator's
        // IsGrounded / IsHovering conditions. Straight air shots keep Jump/Fall Shot.
        if (IsAimingUp())
            animator.SetTrigger("ShootUp");
        else if (IsAimingDown())
            animator.SetTrigger("ShootDown");
        else if (IsGrounded || isHovering)
            animator.SetTrigger("Shoot");
        else if (rb != null && rb.linearVelocity.y >= 0f)
            animator.SetTrigger("JumpShot");
        else
            animator.SetTrigger("FallShot");
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
        // Draw before Kit in hierarchy as a small extra hint; sorting order is what matters.
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

        float big = BigCharge;
        float medium = MediumCharge;
        float charge01 = Mathf.Clamp01(chargeSeconds / Mathf.Max(0.01f, big));

        chargeAuraObject.SetActive(true);
        chargeAuraRenderer.sprite = spriteRenderer.sprite;
        chargeAuraRenderer.flipX = spriteRenderer.flipX;
        // Stay on the character's sorting plane so mid-order backgrounds cannot cover the aura.
        CharacterEffectSorting.ApplyAuraBehindBody(chargeAuraRenderer, spriteRenderer, EffectSortingGroup);

        // More solid (less transparent) as charge grows.
        float alpha;
        if (chargeSeconds >= big)
            alpha = auraAlphaAtBig;
        else if (chargeSeconds >= medium)
            alpha = Mathf.Lerp(auraAlphaAtMedium, auraAlphaAtBig, Mathf.InverseLerp(medium, big, chargeSeconds));
        else
            alpha = Mathf.Lerp(auraAlphaStart, auraAlphaAtMedium, chargeSeconds / Mathf.Max(0.01f, medium));

        // Soft equal pink/yellow shimmer — no hard flashes (photosensitivity-safe).
        // Rate stays well under 3 Hz even at full charge; yellow only gently tints pink.
        Color pink = Color.Lerp(chargeAuraColor, chargeAuraStrongColor, charge01);
        float flickerSpeed = Mathf.Lerp(auraFlickerSpeed, Mathf.Min(auraFlickerSpeed * 1.75f, 2.4f), charge01);
        auraFlickerPhase += Time.deltaTime * flickerSpeed;
        // Smooth sine: equal time toward pink and yellow, no abrupt swaps.
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

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        mediumChargeSeconds = Mathf.Max(0.05f, mediumChargeSeconds);
        bigChargeSeconds = Mathf.Max(mediumChargeSeconds, bigChargeSeconds);
        mediumChargeMoveSpeedBonus = Mathf.Max(0f, mediumChargeMoveSpeedBonus);
        bigChargeMoveSpeedBonus = Mathf.Max(mediumChargeMoveSpeedBonus, bigChargeMoveSpeedBonus);
        shootCooldown = Mathf.Max(0f, shootCooldown);
        shotAnimWaitTimeout = Mathf.Max(0.05f, shotAnimWaitTimeout);
        auraBaseScale = Mathf.Max(1f, auraBaseScale);
        auraSortingBehind = Mathf.Max(1, auraSortingBehind);
        auraAlphaStart = Mathf.Min(auraAlphaStart, auraAlphaAtMedium);
        auraAlphaAtMedium = Mathf.Min(auraAlphaAtMedium, auraAlphaAtBig);
        auraDelayAfterSmallShot = Mathf.Max(0f, auraDelayAfterSmallShot);
        // Keep shimmer slow/soft (photosensitivity: avoid hard >3 Hz flashes).
        auraFlickerSpeed = Mathf.Clamp(auraFlickerSpeed, 0.25f, 2.5f);
        auraFlickerStrength = Mathf.Clamp(auraFlickerStrength, 0.05f, 0.5f);
        kitDashDistance = Mathf.Max(0.1f, kitDashDistance);
        kitDashSpeed = Mathf.Max(0.1f, kitDashSpeed);
        kitDashSmoothStop = Mathf.Max(0f, kitDashSmoothStop);
        kitMaxAirDashes = Mathf.Max(0, kitMaxAirDashes);
        kitMaxAirJumps = Mathf.Max(0, kitMaxAirJumps);
        hoverHoldSeconds = Mathf.Max(0f, hoverHoldSeconds);
        hoverDescendSpeed = Mathf.Max(0.1f, hoverDescendSpeed);
        hoverDescendAcceleration = Mathf.Max(0.1f, hoverDescendAcceleration);
        boostedDashSpeedMultiplier = Mathf.Max(1f, boostedDashSpeedMultiplier);
        boostedDashDistanceMultiplier = Mathf.Max(1f, boostedDashDistanceMultiplier);
        kitDashJumpMomentumDuration = Mathf.Max(0.01f, kitDashJumpMomentumDuration);
        kitAfterimageCount = Mathf.Max(1, kitAfterimageCount);
        kitAfterimageSpacing = Mathf.Max(0.01f, kitAfterimageSpacing);
        kitAfterimageAlphaEnd = Mathf.Min(kitAfterimageAlphaEnd, kitAfterimageAlphaStart);
        spreadShotAngleDegrees = Mathf.Clamp(spreadShotAngleDegrees, 1f, 89f);
        machineGunFireInterval = Mathf.Max(0.05f, machineGunFireInterval);
        machineGunMaxLiveShots = Mathf.Max(2, machineGunMaxLiveShots);
        machineGunAnimEveryNthShot = Mathf.Max(1, machineGunAnimEveryNthShot);
        machineGunMediumSpeedMultiplier = Mathf.Max(1.01f, machineGunMediumSpeedMultiplier);
        machineGunBigSpeedMultiplier = Mathf.Max(
            machineGunMediumSpeedMultiplier + 0.01f,
            machineGunBigSpeedMultiplier);
        hoverSecondsPerAerialLevel = Mathf.Max(0f, hoverSecondsPerAerialLevel);
        styleChargeSecondsPerLevel = Mathf.Max(0f, styleChargeSecondsPerLevel);
        minUpgradedChargeSeconds = Mathf.Max(0.05f, minUpgradedChargeSeconds);
        styleChargeWhileFiringLevel = Mathf.Max(0, styleChargeWhileFiringLevel);
        spreadMaxBullets = Mathf.Max(3, spreadMaxBullets);
        hyperShotSpacing = Mathf.Max(0f, hyperShotSpacing);
        hyperShotSpeedMultiplier = Mathf.Max(1f, hyperShotSpeedMultiplier);
        hyperMachineGunVolleyInterval = Mathf.Max(0.05f, hyperMachineGunVolleyInterval);
    }
#endif
}
