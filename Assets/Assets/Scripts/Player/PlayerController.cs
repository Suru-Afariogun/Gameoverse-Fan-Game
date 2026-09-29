using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>
/// Shared parent for every playable character.
/// Handles InputActions bootstrap, move vector, ground check, facing, and shared HP.
/// Character-specific combat/movement belongs in child classes (e.g. KitPlayerController).
/// </summary>
public abstract class PlayerController : MonoBehaviour, IDamageable
{
    public static PlayerController Active { get; private set; }

    /// <summary>
    /// Live player without a scene scan. Prefers <see cref="Active"/>, then
    /// <see cref="PlayerSpawner.CurrentPlayer"/>. Use this in Update/FixedUpdate hot paths.
    /// </summary>
    public static PlayerController ResolveActive()
    {
        if (Active != null)
            return Active;

        if (PlayerSpawner.Instance != null && PlayerSpawner.Instance.CurrentPlayer != null)
            return PlayerSpawner.Instance.CurrentPlayer;

        return null;
    }

    /// <summary>
    /// Fills <paramref name="buffer"/> with the active player (0–1 entries). Avoids
    /// <c>FindObjectsByType</c> allocations in gameplay loops.
    /// </summary>
    public static void CollectActivePlayers(List<PlayerController> buffer)
    {
        if (buffer == null)
            return;

        buffer.Clear();
        PlayerController player = ResolveActive();
        if (player != null)
            buffer.Add(player);
    }

    [Header("Character")]
    [Tooltip("ID used by LifeBar and character select to match this character.")]
    [SerializeField] private string characterId = "Kit";

    [Header("References")]
    [SerializeField] protected Transform groundCheck;
    [SerializeField] protected LayerMask groundLayers;
    [SerializeField] protected float groundCheckRadius = 0.12f;
    [SerializeField] protected Animator animator;
    [Tooltip("Animated fire point. Place/key it for LEFT-facing art; it is mirrored at runtime when facing right.")]
    [SerializeField] protected Transform firePoint;
    [Tooltip("Local X mirror axis (usually 0 = character center / sprite pivot).")]
    [SerializeField] protected Vector2 firePointFlipCenterLocal = Vector2.zero;

    [Header("Movement")]
    [SerializeField] protected float moveSpeed = 6f;
    [SerializeField] protected float jumpForce = 12f;
    [Tooltip("Seconds after leaving a ledge where the player is still treated as grounded and can jump.")]
    [SerializeField] protected float coyoteTime = 0.4f;
    [SerializeField] protected float fallMultiplier = 2.5f;
    [Tooltip("All character art faces LEFT by default. flipX turns on when facing right.")]
    [SerializeField] protected bool spriteFacesLeft = true;

    [Header("Aim")]
    [Tooltip("Stick/keyboard Up past this value aims the shot 45° upward in the facing direction.")]
    [SerializeField] [Range(0.1f, 1f)] protected float upAimThreshold = 0.35f;

    [Header("Dash (shared defaults; children may override behavior)")]
    [Tooltip("How far the dash travels in world units.")]
    [SerializeField] protected float dashDistance = 4.5f;
    [Tooltip("Peak dash speed. Distance / speed ≈ dash length in time.")]
    [SerializeField] protected float dashSpeed = 16f;
    [SerializeField] protected float dashCooldown = 0.35f;
    [Tooltip("Ease-out window at the end of the dash (smoother stop).")]
    [SerializeField] protected float dashSmoothStopDuration = 0.12f;
    [Tooltip("Speed multiplier at the very end of the ease-out (0 = full stop).")]
    [SerializeField] [Range(0f, 1f)] protected float dashEndSpeedMultiplier = 0.15f;
    [SerializeField] protected bool allowAirDash = true;
    [Tooltip("How many dashes are allowed after leaving the ground. Refills on landing.")]
    [SerializeField] protected int maxAirDashes = 2;

    [Header("Dash Jump (Mega Man Zero style)")]
    [Tooltip("Jump during a grounded dash to launch with dash horizontal speed.")]
    [SerializeField] protected bool allowDashJump = true;
    [Tooltip("Horizontal speed kept during dash jump. <= 0 uses Dash Speed.")]
    [SerializeField] protected float dashJumpHorizontalSpeed = 0f;
    [Tooltip("Upward force for dash jump. <= 0 uses normal Jump Force.")]
    [SerializeField] protected float dashJumpForce = 0f;
    [Tooltip("If true, dash-jump momentum lasts through the jump AND fall until landing.")]
    [SerializeField] protected bool dashJumpMomentumUntilLanded = true;
    [Tooltip("Only used when Momentum Until Landed is off. How long dash-jump horizontal momentum lasts.")]
    [SerializeField] protected float dashJumpMomentumDuration = 0.55f;
    [Tooltip("How strongly opposite steering can cancel dash-jump momentum (0 = locked forward).")]
    [SerializeField] [Range(0f, 1f)] protected float dashJumpSteerCancel = 0.25f;

    [Header("Dash Afterimages")]
    [SerializeField] protected bool enableDashAfterimages = true;
    [SerializeField] protected int dashAfterimageCount = 3;
    [Tooltip("Seconds between each afterimage (index 0 = newest trail).")]
    [SerializeField] protected float dashAfterimageSpacing = 0.1f;
    [SerializeField] protected Color dashAfterimageColor = Color.white;
    [Tooltip("Alpha of the closest afterimage.")]
    [SerializeField] [Range(0f, 1f)] protected float dashAfterimageAlphaStart = 0.55f;
    [Tooltip("Alpha of the farthest afterimage (most transparent).")]
    [SerializeField] [Range(0f, 1f)] protected float dashAfterimageAlphaEnd = 0.15f;
    [SerializeField] protected float dashAfterimageLifetimePadding = 0.15f;

    [Header("Platform Slide & Wall Bounce")]
    [Tooltip("Gentle separation push off ceilings. Side walls never push — they only stop horizontal momentum.")]
    [SerializeField] protected float platformSeparationDistance = 0.06f;
    [SerializeField] [Range(0f, 1f)] protected float platformTopNormalThreshold = 0.55f;
    [Tooltip("How quickly the player accelerates downward when touching a wall or platform underside.")]
    [SerializeField] protected float wallSlideAcceleration = 20f;
    [SerializeField] protected float wallSlideMaxFallSpeed = 11f;
    [Tooltip("BoxCast distance to detect platform sides when contact callbacks miss a frame.")]
    [SerializeField] protected float wallProbeDistance = 0.55f;
    [SerializeField] protected Vector2 wallProbeSize = new Vector2(0.12f, 1.1f);
    [Tooltip("Grace period after leaving a wall where jump still counts as a wall jump.")]
    [SerializeField] protected float wallJumpCoyoteTime = 0.12f;
    [Header("Wall Bounce (jump / dash into ground surfaces)")]
    [Tooltip("Target horizontal travel at 45° when jumping off the wall (world units).")]
    [SerializeField] protected float wallBounceShallowDistance = 5f;
    [Tooltip("Climb jump (Up + Jump): vertical height gained per wall-kick (world units).")]
    [SerializeField] protected float wallBounceSteepClimbHeight = 2.759f;
    [Tooltip("Extra vertical speed on climb jumps (higher = snappier wall scaling).")]
    [SerializeField] protected float wallBounceSteepClimbSpeedMultiplier = 1.45f;
    [Tooltip("Climb jump: tiny separation off the wall on launch so the body clears the surface.")]
    [SerializeField] protected float wallBounceSteepWallSeparation = 0.2f;
    [Tooltip("Shallow rebound when pressing jump alone.")]
    [SerializeField] protected float wallBounceShallowAngleDeg = 45f;
    [SerializeField] protected float wallBounceCooldown = 0.2f;
    [Tooltip("Scales shallow wall-bounce launch speed only.")]
    [SerializeField] protected float wallBounceLaunchSpeedMultiplier = 1.32f;
    [Tooltip("How quickly away-from-wall speed is restored during commit (high = fast but still eased).")]
    [SerializeField] protected float wallBounceCommitVelocityRestore = 96f;

    [Header("Combat - Hit Reaction")]
    [Tooltip("How long the character is stunned (no control) after taking damage.")]
    [SerializeField] protected float hitStunDuration = 0.5f;
    [Tooltip("How long the character is invincible after taking damage.")]
    [SerializeField] protected float hitInvincibilityDuration = 1.5f;
    [Tooltip("Alpha for the brighter half of the invincibility flicker (0.80 = 80% opaque).")]
    [SerializeField] [Range(0f, 1f)] protected float invincibilityFlickerAlphaHigh = 0.80f;
    [Tooltip("Alpha for the dimmer half of the invincibility flicker (0.75 = 75% opaque).")]
    [SerializeField] [Range(0f, 1f)] protected float invincibilityFlickerAlphaLow = 0.75f;
    [Tooltip("Seconds for each half of the invincibility flicker.")]
    [SerializeField] protected float invincibilityFlickerHalfPeriod = 0.08f;
    [Tooltip("How far the character is knocked back on hit (world units / spaces).")]
    [SerializeField] protected float hitKnockbackDistance = 3f;
    [Tooltip("How long the knockback slide takes (higher = smoother / slower).")]
    [SerializeField] protected float hitKnockbackDuration = 0.6f;

    [Header("Combat - VFX Prefabs")]
    [Tooltip("Assign your Default Stunned prefab. Set Effect Type = Default Stunned on the prefab.")]
    [SerializeField] protected GameVisualEffect stunnedEffectPrefab;
    [Tooltip("Assign Kit/Malice Death Energy Ball prefab. Set Effect Type = Death Energy Ball.")]
    [SerializeField] protected GameVisualEffect deathEnergyBallPrefab;
    [Tooltip("Assign Healed Visual Effect prefab. Set Effect Type = Healed.")]
    [SerializeField] protected GameVisualEffect healedEffectPrefab;

    [Header("Health")]
    [SerializeField] protected int maxHealth = 10;
    [SerializeField] protected int currentHealth = 10;

    public string CharacterId => characterId;
    public int MaxHealth => maxHealth;
    public int CurrentHealth => currentHealth;
    public bool IsDead => currentHealth <= 0;
    public bool IsGrounded => isGrounded || coyoteTimer > 0f;
    public bool IsPhysicallyGrounded => isGrounded;
    public Transform GroundCheckTransform => groundCheck;
    public float GroundCheckRadius => groundCheckRadius;
    public bool IsDashing => isDashing;
    public bool IsDashJumping => isDashJumping;
    /// <summary>True after a jump, dash-jump, or wall jump until the next landing.</summary>
    public bool IsVoluntarilyAirborne { get; private set; }
    /// <summary>True from a wall bounce / wall jump until the next landing.</summary>
    public bool IsInWallBounceArc { get; private set; }
    public bool IsStunned => isStunned;
    public bool IsInvincible => invincibilityTimer > 0f;
    public float FacingSign => facingSign;
    public float NormalMoveSpeed => moveSpeed;
    public Vector2 Velocity => rb != null ? rb.linearVelocity : Vector2.zero;
    public Vector2 MoveInput => moveInput;
    public bool InputLocked => inputLocked;
    public LayerMask GroundLayers => groundLayers;
    public virtual float GetCameraFollowSpeedHint()
    {
        if (rb == null)
            return 0f;

        return Mathf.Abs(rb.linearVelocity.x);
    }

    /// <summary>
    /// Approximate apex height of a full jump from rest (world units). Used by camera fall look-ahead.
    /// </summary>
    public virtual float GetMaxJumpHeight()
    {
        float gravity = GetWallBounceGravity();
        float maxVy = jumpForce;
        if (dashJumpForce > 0f)
            maxVy = Mathf.Max(maxVy, dashJumpForce);

        return (maxVy * maxVy) / (2f * gravity);
    }

    /// <summary>Extra camera follow softness while this character is moving fast (Harlie charge slide, etc.).</summary>
    public virtual float CameraFastSmoothMultiplier => 1f;

    public virtual bool WantsExtraCameraSmoothing =>
        IsDashing || IsDashJumping || GetCameraFollowSpeedHint() > NormalMoveSpeed + 0.35f;

    /// <summary>
    /// Active melee / grapple box stopped a projectile. Return true when the shot was handled
    /// (reflect, clash despawn, etc.) so it cannot pass through to the player body.
    /// </summary>
    public virtual bool TryHandleProjectileContact(Projectile projectile, Collider2D hitCollider)
    {
        return false;
    }

    /// <summary>Force facing (-1 left, +1 right) and refresh flip / fire-point mirror.</summary>
    public void SetFacingSign(float sign)
    {
        if (Mathf.Abs(sign) < 0.01f)
            return;

        facingSign = Mathf.Sign(sign);
        ApplyFacingVisuals();
    }

    /// <summary>
    /// When true, Kit/Malice ignore move/jump/attack/dash (used while Kaboodle's box is open).
    /// </summary>
    public void SetInputLocked(bool locked)
    {
        inputLocked = locked;
        if (!locked)
            return;

        moveInput = Vector2.zero;
        jumpRequested = false;
        dashRequested = false;
        if (rb != null)
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
    }

    /// <summary>True while sitting in a vehicle (Kit's rocket ship): no physics, damage, or edge clamps.</summary>
    public bool IsRidingVehicle { get; private set; }
    /// <summary>Cutscene protection (e.g. walking into Kit's rocket ship): no damage or shoves.</summary>
    public bool IsScriptedInvulnerable { get; private set; }

    public void SetScriptedInvulnerable(bool invulnerable)
    {
        IsScriptedInvulnerable = invulnerable;
    }
    public Collider2D BodyCollider => bodyCollider;
    /// <summary>Upward gravity acceleration (positive) used for normal jumps.</summary>
    public float JumpGravity => Mathf.Abs(Physics2D.gravity.y * (rb != null ? rb.gravityScale : 1f));

    private bool scriptedMoveActive;
    private float scriptedMoveX;

    /// <summary>Walks the character for a cutscene while input is locked (-1 left, 1 right, 0 stand).</summary>
    public void SetScriptedMove(float x)
    {
        scriptedMoveActive = true;
        scriptedMoveX = Mathf.Clamp(x, -1f, 1f);
        moveInput = new Vector2(scriptedMoveX, 0f);
    }

    public void ClearScriptedMove()
    {
        scriptedMoveActive = false;
        scriptedMoveX = 0f;
        moveInput = Vector2.zero;
    }

    /// <summary>Cutscene jump with an exact upward launch speed.</summary>
    public void PerformScriptedJump(float upwardSpeed)
    {
        if (rb == null)
            return;

        coyoteTimer = 0f;
        isGrounded = false;
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, Mathf.Max(0f, upwardSpeed));
        IsVoluntarilyAirborne = true;
        SoundManager.Instance?.PlayJump();
    }

    public void BeginVehicleRide()
    {
        ClearScriptedMove();
        jumpRequested = false;
        CancelAllDashState();

        IsRidingVehicle = true;
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.simulated = false;
        }

        isGrounded = true;
        IsVoluntarilyAirborne = false;
    }

    public void EndVehicleRide()
    {
        IsRidingVehicle = false;
        if (rb != null)
            rb.simulated = true;
    }

    /// <summary>
    /// Temporarily blocks dash (e.g. near Kaboodle so Confirm can be used without dashing).
    /// </summary>
    public void SetDashDisabled(bool disabled)
    {
        dashDisabled = disabled;
        if (disabled)
            dashRequested = false;
    }

    public event Action<int, int> OnHealthChanged;
    public event Action OnDied;

    protected Rigidbody2D rb;
    protected SpriteRenderer spriteRenderer;
    protected Collider2D bodyCollider;
    protected InputActions controls;

    private static PhysicsMaterial2D sharedFrictionlessMaterial;

    protected Vector2 moveInput;
    protected bool isGrounded;
    protected bool jumpRequested;
    protected bool dashRequested;

    protected bool isDashing;
    protected float dashTimer;
    protected float dashDuration;
    protected float dashCooldownTimer;
    protected float dashVelocityX;
    protected float dashDirSign = -1f;
    protected int airDashesRemaining;
    protected bool wasGrounded = true;
    protected float coyoteTimer;
    protected bool isDashJumping;
    protected float dashJumpMomentumTimer;
    // Characters face left by default.
    protected float facingSign = -1f;
    // Left-facing FirePoint local pos (from Animator / your keys). Mirrored in LateUpdate when facing right.
    protected Vector3 firePointLeftFacingLocal;
    protected Vector3 firePointLastWrittenLocal;
    protected bool firePointHasLastWritten;
    protected bool isStunned;
    protected bool isShooting;
    protected float shootAnimTimer;
    protected bool inputLocked;
    protected bool dashDisabled;
    protected float stunTimer;
    protected float invincibilityTimer;
    private int upgradeAirJumpsRemaining;
    private float invincibilityFlickerTimer;
    private bool invincibilityFlickerActive;
    private bool knockbackActive;
    private Vector2 knockbackStart;
    private Vector2 knockbackEnd;
    private float knockbackElapsed;
    private float knockbackDurationActive;
    private float wallBounceCooldownTimer;
    private bool wallBounceCommitActive;
    private float wallBounceCommitAwaySign;
    private float wallBounceCommitStartX;
    private float wallBounceCommitRequiredDistance;
    private float wallBounceCommitLaunchSpeedX;
    private bool wallBounceCommitIsSteep;
    private bool isWallSliding;
    private Vector2 lastWallSlideNormal;
    private float lastWallBonkFixedTime = -1f;
    private bool wasWallSlidingDuringDash;
    private bool wasWallSlidingDuringDashJump;
    private float wallJumpCoyoteTimer;
    private Vector2 wallJumpCoyoteNormal;
    private bool skipHorizontalMoveThisFixed;
    protected float defaultGravityScale = 1f;
    protected bool airHangActive;

    protected struct PoseSample
    {
        public float time;
        public Vector3 position;
        public Sprite sprite;
        public bool flipX;
    }

    readonly System.Collections.Generic.List<PoseSample> poseHistory = new System.Collections.Generic.List<PoseSample>(64);
    GameObject[] afterimageObjects;
    SpriteRenderer[] afterimageRenderers;
    protected float afterimagesVisibleUntil;
    protected bool afterimageFadeOutActive;
    protected float afterimageFadeOutStartTime;
    protected float[] afterimageFadeBaseAlpha;
    protected SortingGroup effectSortingGroup;

    protected virtual void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        bodyCollider = GetComponent<Collider2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>(true);

        if (animator == null)
            animator = GetComponent<Animator>();

        controls = new InputActions();
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);

        if (rb != null)
            defaultGravityScale = rb.gravityScale;

        EnsureFrictionlessBodyMaterial();

        // Start facing left to match left-facing art.
        facingSign = -1f;
        ApplyFacingVisuals();
        airDashesRemaining = Mathf.Max(0, maxAirDashes);
        effectSortingGroup = CharacterEffectSorting.EnsureHostSortingGroup(this, spriteRenderer);
        SetupDashAfterimages();
    }

    /// <summary>Sorting group that keeps aura / afterimages behind the body but above backgrounds.</summary>
    public SortingGroup EffectSortingGroup => effectSortingGroup;

    protected virtual void OnEnable()
    {
        Active = this;

        if (controls == null)
            controls = new InputActions();

        controls.PlayerControls.Enable();
        controls.PlayerControls.Movement.performed += OnMovement;
        controls.PlayerControls.Movement.canceled += OnMovement;
        controls.PlayerControls.Jump.performed += OnJumpPerformed;
        controls.PlayerControls.Dash.performed += OnDashPerformed;
        controls.PlayerControls.Attack.started += OnAttackStarted;
        controls.PlayerControls.Attack.canceled += OnAttackCanceled;

        OnHealthChanged?.Invoke(currentHealth, maxHealth);
        DeferredEnemyPhaseRefresh.Request();
    }

    protected virtual void OnDisable()
    {
        if (Active == this)
            Active = null;

        if (controls == null)
            return;

        controls.PlayerControls.Movement.performed -= OnMovement;
        controls.PlayerControls.Movement.canceled -= OnMovement;
        controls.PlayerControls.Jump.performed -= OnJumpPerformed;
        controls.PlayerControls.Dash.performed -= OnDashPerformed;
        controls.PlayerControls.Attack.started -= OnAttackStarted;
        controls.PlayerControls.Attack.canceled -= OnAttackCanceled;
        controls.PlayerControls.Disable();
        SetDashAfterimagesActive(false);
    }

    protected virtual void OnDestroy()
    {
        if (controls != null)
        {
            controls.Dispose();
            controls = null;
        }

        DestroyDashAfterimages();
    }

    protected virtual void Update()
    {
        if (IsRidingVehicle)
        {
            UpdateAnimator();
            return;
        }

        RefreshMoveInputFromButtons();
        UpdateFacingFromInput();
        RecordPoseHistory();
        UpdateDashAfterimages();

        if (shootAnimTimer > 0f)
        {
            shootAnimTimer -= Time.deltaTime;
            if (shootAnimTimer <= 0f)
                isShooting = false;
        }

        TickHitReaction(Time.deltaTime);

        UpdateAnimator();
        HandleCharacterUpdate();
    }

    /// <summary>
    /// After Animator applies left-facing FirePoint keys, mirror them when facing right.
    /// </summary>
    protected virtual void LateUpdate()
    {
        MirrorFirePointForFacing();
        ClampToPlayableWorld();
    }

    protected virtual void FixedUpdate()
    {
        if (IsRidingVehicle)
            return;

        if (scriptedMoveActive)
            moveInput = new Vector2(scriptedMoveX, 0f);

        UpdateGrounded();
        HandleLandingAndAirDashRefill();
        TickDashJumpMomentum(Time.fixedDeltaTime);
        TickDash(Time.fixedDeltaTime);
        TickHitKnockback(Time.fixedDeltaTime);

        if (isDashing)
        {
            if (jumpRequested && CanPerformDashJump())
            {
                wasWallSlidingDuringDash = false;
                PerformDashJump();
            }
            else
            {
                ApplyPlatformAntiStick();
                bool wallNow = isWallSliding;
                if (wallNow && (!wasWallSlidingDuringDash || IsWallContactAheadOfDash()))
                    ApplyDashHeadBonk();
                wasWallSlidingDuringDash = wallNow;
                if (wallNow)
                {
                    jumpRequested = false;
                    HandleCharacterFixedUpdate();
                    ClampToPlayableWorld();
                    return;
                }

                rb.linearVelocity = new Vector2(GetCurrentDashVelocityX(), 0f);
                jumpRequested = false;
                HandleCharacterFixedUpdate();
                ClampToPlayableWorld();
                return;
            }
        }
        else
        {
            wasWallSlidingDuringDash = false;
        }

        if (isDashJumping)
        {
            ApplyPlatformAntiStick();
            bool wallNow = isWallSliding;
            if (wallNow && (!wasWallSlidingDuringDashJump || IsWallContactAheadOfDash()))
                ApplyDashHeadBonk();
            wasWallSlidingDuringDashJump = wallNow;
            if (wallNow)
            {
                jumpRequested = false;
                ApplyFallMultiplier();
                HandleCharacterFixedUpdate();
                ClampToPlayableWorld();
                return;
            }
        }
        else
        {
            wasWallSlidingDuringDashJump = false;
        }

        if (isStunned || knockbackActive)
        {
            jumpRequested = false;
            if (isStunned)
                dashRequested = false;
            if (rb != null && !knockbackActive)
                rb.linearVelocity = new Vector2(0f, airHangActive ? 0f : rb.linearVelocity.y);
            ApplyFallMultiplier();
            HandleCharacterFixedUpdate();
            ClampToPlayableWorld();
            return;
        }

        if (wallBounceCooldownTimer > 0f)
            wallBounceCooldownTimer = Mathf.Max(0f, wallBounceCooldownTimer - Time.fixedDeltaTime);

        if (wallJumpCoyoteTimer > 0f)
            wallJumpCoyoteTimer = Mathf.Max(0f, wallJumpCoyoteTimer - Time.fixedDeltaTime);

        if (airHangActive && rb != null)
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f);

        skipHorizontalMoveThisFixed = false;
        ApplyPlatformAntiStick();
        ApplyHorizontalMove();
        ApplyJump();
        ApplyFallMultiplier();
        TickWallBounceCommit();
        HandleCharacterFixedUpdate();
        ClampToPlayableWorld();
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        ApplyPlatformAntiStick(collision);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (!IsGroundLayerCollision(collision))
            return;

        if (!TryGetNonFloorContactNormal(collision, out _))
            return;

        if (isDashing || isDashJumping)
            ApplyDashHeadBonk();
    }

    protected virtual void ApplyPlatformAntiStick(Collision2D collision = null)
    {
        if (rb == null)
            return;

        isWallSliding = false;
        lastWallSlideNormal = Vector2.zero;

        if (collision != null)
        {
            ApplyPlatformSlideFromCollision(collision);
            if (!isWallSliding)
                TryProbeWallContact();
            UpdateWallJumpCoyote();
            return;
        }

        if (bodyCollider == null)
            bodyCollider = GetComponent<Collider2D>();

        if (bodyCollider == null)
            return;

        ContactFilter2D filter = new ContactFilter2D();
        filter.useLayerMask = true;
        filter.layerMask = groundLayers;
        filter.useTriggers = false;

        ContactPoint2D[] contacts = new ContactPoint2D[8];
        int count = bodyCollider.GetContacts(filter, contacts);
        for (int i = 0; i < count; i++)
        {
            if (IsPassableFloorPlatform(contacts[i].collider) ||
                IsPassableFloorPlatform(contacts[i].otherCollider))
                continue;

            TryPlatformSlideFromNormal(contacts[i].normal);
        }

        if (!isWallSliding)
            TryProbeWallContact();

        UpdateWallJumpCoyote();
    }

    private void UpdateWallJumpCoyote()
    {
        if (isWallSliding && Mathf.Abs(lastWallSlideNormal.x) > 0.1f)
        {
            wallJumpCoyoteTimer = wallJumpCoyoteTime;
            wallJumpCoyoteNormal = lastWallSlideNormal;
        }
    }

    private void TryProbeWallContact()
    {
        if (bodyCollider == null || wallProbeDistance <= 0f)
            return;

        Vector2 origin = bodyCollider.bounds.center;
        Vector2 size = wallProbeSize;

        RaycastHit2D leftHit = Physics2D.BoxCast(
            origin, size, 0f, Vector2.left, wallProbeDistance, groundLayers);
        RaycastHit2D rightHit = Physics2D.BoxCast(
            origin, size, 0f, Vector2.right, wallProbeDistance, groundLayers);

        if (leftHit.collider != null && IsPassableFloorPlatform(leftHit.collider))
            leftHit = default;
        if (rightHit.collider != null && IsPassableFloorPlatform(rightHit.collider))
            rightHit = default;

        RaycastHit2D hit = leftHit.collider != null ? leftHit : rightHit;
        if (hit.collider == null)
            return;

        if (IsFloorNormal(hit.normal))
            return;

        TryPlatformSlideFromNormal(hit.normal);
    }

    /// <summary>
    /// Slim / moving factory platforms are floors you can jump through — never wall-bounce surfaces.
    /// </summary>
    private static bool IsPassableFloorPlatform(Collider2D col)
    {
        if (col == null)
            return false;

        return col.GetComponentInParent<MovingFactoryPlatform>() != null
               || col.GetComponentInParent<SlimFactoryPlatform>() != null;
    }

    protected bool CanWallJumpNow()
    {
        return !wallBounceCommitActive &&
               wallBounceCooldownTimer <= 0f &&
               (isWallSliding || wallJumpCoyoteTimer > 0f);
    }

    private float GetWallJumpAwaySign()
    {
        Vector2 normal = isWallSliding ? lastWallSlideNormal : wallJumpCoyoteNormal;
        if (Mathf.Abs(normal.x) > 0.1f)
            return Mathf.Sign(normal.x);

        return -facingSign;
    }

    private void ApplyPlatformSlideFromCollision(Collision2D collision)
    {
        if (!IsGroundLayerCollision(collision))
            return;

        if (collision.collider != null && IsPassableFloorPlatform(collision.collider))
            return;

        for (int i = 0; i < collision.contactCount; i++)
            TryPlatformSlideFromNormal(collision.GetContact(i).normal);
    }

    private bool IsGroundLayerCollision(Collision2D collision)
    {
        return collision != null && ((1 << collision.gameObject.layer) & groundLayers) != 0;
    }

    private bool IsFloorNormal(Vector2 normal)
    {
        return normal.y > platformTopNormalThreshold;
    }

    private bool TryGetNonFloorContactNormal(Collision2D collision, out Vector2 normal)
    {
        normal = Vector2.zero;

        for (int i = 0; i < collision.contactCount; i++)
        {
            Vector2 candidate = collision.GetContact(i).normal;
            if (!IsFloorNormal(candidate))
            {
                normal = candidate;
                return true;
            }
        }

        return false;
    }

    private void TryPlatformSlideFromNormal(Vector2 normal)
    {
        if (IsFloorNormal(normal))
            return;

        if (wallBounceCommitActive &&
            Mathf.Abs(normal.x) > 0.25f &&
            Mathf.Sign(normal.x) == wallBounceCommitAwaySign)
        {
            return;
        }

        isWallSliding = true;
        if (Mathf.Abs(normal.x) >= Mathf.Abs(lastWallSlideNormal.x))
            lastWallSlideNormal = normal;

        CancelVelocityIntoWall(normal);
        ApplySmoothWallSlide(normal);

        // Side walls only kill momentum; only ceilings get nudged off to avoid sticking.
        if (platformSeparationDistance > 0f && Mathf.Abs(normal.x) <= 0.25f)
            rb.position += normal * platformSeparationDistance;
    }

    private void CancelVelocityIntoWall(Vector2 normal)
    {
        if (rb == null)
            return;

        Vector2 vel = rb.linearVelocity;

        if (Mathf.Abs(normal.x) > 0.25f)
        {
            if (normal.x > 0f && vel.x < 0f)
                vel.x = 0f;
            else if (normal.x < 0f && vel.x > 0f)
                vel.x = 0f;
        }

        if (normal.y < -0.25f && vel.y > 0f)
            vel.y = 0f;

        rb.linearVelocity = vel;
    }

    private void ApplySmoothWallSlide(Vector2 normal)
    {
        if (rb == null || wallSlideAcceleration <= 0f)
            return;

        float dt = Time.fixedDeltaTime;
        float targetVy = -Mathf.Max(0.1f, wallSlideMaxFallSpeed);
        float newVy = Mathf.MoveTowards(rb.linearVelocity.y, targetVy, wallSlideAcceleration * dt);
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, newVy);
    }

    private void CancelAllDashState()
    {
        bool hadDash = isDashing || isDashJumping;
        isDashing = false;
        isDashJumping = false;
        dashJumpMomentumTimer = 0f;
        dashTimer = 0f;
        dashRequested = false;

        if (hadDash)
        {
            dashCooldownTimer = dashCooldown;
            OnDashEnded();
        }

        SetDashAfterimagesActive(false);
        afterimagesVisibleUntil = 0f;
    }

    private void StartWallBounce(float awayFromWallSign, float intoWallSign)
    {
        if (rb == null || wallBounceCooldownTimer > 0f)
            return;

        if (Time.fixedTime - lastWallBonkFixedTime < 0.05f)
            return;

        lastWallBonkFixedTime = Time.fixedTime;
        CancelAllDashState();

        awayFromWallSign = awayFromWallSign >= 0f ? 1f : -1f;
        bool steepJump = moveInput.y >= upAimThreshold;
        float commitDistance = steepJump ? 0f : wallBounceShallowDistance;
        Vector2 launchVelocity = ComputeWallBounceVelocity(awayFromWallSign, steepJump);
        rb.linearVelocity = launchVelocity;
        IsVoluntarilyAirborne = true;
        IsInWallBounceArc = true;
        SoundManager.Instance?.PlayJump();
        wallBounceCooldownTimer = wallBounceCooldown;
        coyoteTimer = 0f;
        wallJumpCoyoteTimer = 0f;
        isWallSliding = false;

        if (steepJump && wallBounceSteepWallSeparation > 0f)
            rb.position += new Vector2(awayFromWallSign * wallBounceSteepWallSeparation, 0f);

        wallBounceCommitActive = true;
        wallBounceCommitIsSteep = steepJump;
        wallBounceCommitAwaySign = awayFromWallSign;
        wallBounceCommitStartX = rb.position.x;
        wallBounceCommitRequiredDistance = commitDistance;
        wallBounceCommitLaunchSpeedX = Mathf.Abs(launchVelocity.x);
        skipHorizontalMoveThisFixed = true;
    }

    private void TickWallBounceCommit()
    {
        if (!wallBounceCommitActive || rb == null)
            return;

        if (isGrounded)
        {
            EndWallBounceCommit();
            return;
        }

        float traveled = (rb.position.x - wallBounceCommitStartX) * wallBounceCommitAwaySign;

        if (wallBounceCommitIsSteep)
        {
            if (Mathf.Abs(rb.linearVelocity.x) > 0.01f)
                rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);

            if (rb.linearVelocity.y <= 0f)
                EndWallBounceCommit();

            return;
        }
        else if (traveled >= wallBounceCommitRequiredDistance)
        {
            EndWallBounceCommit();
            return;
        }

        float targetVx = wallBounceCommitLaunchSpeedX * wallBounceCommitAwaySign;
        float awaySpeed = rb.linearVelocity.x * wallBounceCommitAwaySign;
        if (awaySpeed < wallBounceCommitLaunchSpeedX * 0.985f)
        {
            float restore = Mathf.Max(1f, wallBounceCommitVelocityRestore);
            float newVx = Mathf.MoveTowards(
                rb.linearVelocity.x,
                targetVx,
                restore * Time.fixedDeltaTime);
            rb.linearVelocity = new Vector2(newVx, rb.linearVelocity.y);
        }
    }

    private void EndWallBounceCommit()
    {
        wallBounceCommitActive = false;
        wallBounceCommitIsSteep = false;
    }

    private Vector2 ComputeWallBounceVelocity(float awayFromWallSign, bool steepJump)
    {
        if (steepJump)
            return ComputeMmxSteepWallClimbVelocity(awayFromWallSign);

        float angleDeg = wallBounceShallowAngleDeg;
        float speed = GetShallowWallBounceLaunchSpeed(angleDeg) * Mathf.Max(1f, wallBounceLaunchSpeedMultiplier);
        float rad = angleDeg * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(rad) * awayFromWallSign * speed, Mathf.Sin(rad) * speed);
    }

    /// <summary>
    /// Straight-up wall climb kick — no horizontal push, easier to chain and scale walls.
    /// </summary>
    private Vector2 ComputeMmxSteepWallClimbVelocity(float awayFromWallSign)
    {
        float gravity = GetWallBounceGravity();
        float vy = Mathf.Sqrt(2f * gravity * wallBounceSteepClimbHeight);
        vy *= Mathf.Max(1f, wallBounceSteepClimbSpeedMultiplier);
        return new Vector2(0f, vy);
    }

    private float GetWallBounceGravity()
    {
        float gravity = Mathf.Abs(Physics2D.gravity.y * (rb != null ? rb.gravityScale : defaultGravityScale));
        return gravity < 0.001f ? 9.81f : gravity;
    }

    private float GetShallowWallBounceLaunchSpeed(float angleDeg)
    {
        float gravity = GetWallBounceGravity();
        float rad = angleDeg * Mathf.Deg2Rad;
        float sin2 = Mathf.Sin(2f * rad);
        if (sin2 < 0.05f)
            sin2 = 0.05f;

        return Mathf.Sqrt(gravity * wallBounceShallowDistance / sin2);
    }

    /// <summary>
    /// Dash or dash-jump into a wall: MMX-style velocity rebound, dash state cleared.
    /// </summary>
    private void ApplyDashHeadBonk()
    {
        StartWallBounce(-dashDirSign, dashDirSign);

        // Bounce skipped by its cooldown: still end the dash so it can't resume once contact ends.
        if ((isDashing || isDashJumping) && IsWallContactAheadOfDash())
            CancelAllDashState();
    }

    /// <summary>Touching a wall in front of the dash, or a ceiling above it.</summary>
    private bool IsWallContactAheadOfDash()
    {
        if (!isWallSliding)
            return false;

        return lastWallSlideNormal.x * dashDirSign < -0.25f || lastWallSlideNormal.y < -0.25f;
    }

    /// <summary>
    /// Player pressed jump while sliding on a platform side.
    /// </summary>
    private void ApplyManualWallJump()
    {
        float awaySign = GetWallJumpAwaySign();
        float intoSign = -awaySign;
        StartWallBounce(awaySign, intoSign);
    }

    private void EnsureFrictionlessBodyMaterial()
    {
        if (bodyCollider == null)
            return;

        if (sharedFrictionlessMaterial == null)
        {
            sharedFrictionlessMaterial = new PhysicsMaterial2D("PlayerFrictionless")
            {
                friction = 0f,
                bounciness = 0f
            };
        }

        bodyCollider.sharedMaterial = sharedFrictionlessMaterial;
    }

    /// <summary>
    /// Stay inside the same background edges the camera cannot leave.
    /// </summary>
    protected void ClampToPlayableWorld()
    {
        if (rb == null || CameraFollow.Instance == null || IsRidingVehicle)
            return;

        if (bodyCollider == null)
            bodyCollider = GetComponent<Collider2D>();

        CameraFollow.Instance.ClampRigidbodyToPlayableBounds(rb, bodyCollider);
    }

    /// <summary>Child-specific Update logic (charge timers, etc.).</summary>
    protected virtual void HandleCharacterUpdate() { }

    /// <summary>Child-specific FixedUpdate logic.</summary>
    protected virtual void HandleCharacterFixedUpdate() { }

    /// <summary>Called when Attack starts (button pressed).</summary>
    protected virtual void OnAttackStarted(InputAction.CallbackContext context)
    {
        if (inputLocked || isStunned || BlocksActionCancel())
            return;
    }

    /// <summary>Called when Attack ends (button released).</summary>
    protected virtual void OnAttackCanceled(InputAction.CallbackContext context)
    {
        if (inputLocked || isStunned)
            return;
    }

    protected virtual void OnMovement(InputAction.CallbackContext context)
    {
        if (inputLocked || isStunned)
        {
            moveInput = Vector2.zero;
            return;
        }

        moveInput = context.ReadValue<Vector2>();
        NotifyButtonSpriteDevice(context);
    }

    protected virtual void OnJumpPerformed(InputAction.CallbackContext context)
    {
        if (!context.performed || inputLocked || isStunned || BlocksActionCancel())
            return;

        NotifyButtonSpriteDevice(context);
        jumpRequested = true;
    }

    protected virtual void OnDashPerformed(InputAction.CallbackContext context)
    {
        if (!context.performed || inputLocked || dashDisabled || isStunned || BlocksActionCancel())
            return;

        NotifyButtonSpriteDevice(context);
        dashRequested = true;
    }

    private static void NotifyButtonSpriteDevice(InputAction.CallbackContext context)
    {
        InputDevice device = context.control != null ? context.control.device : null;
        if (device == null)
            return;

        ButtonSpriteManager manager = ButtonSpriteManager.Instance;
        if (manager == null)
            return;

        manager.NotifyDevice(device);
    }

    /// <summary>
    /// When true, jump/dash/new attacks are blocked (e.g. Malice mid-grapple).
    /// </summary>
    protected virtual bool BlocksActionCancel()
    {
        return false;
    }

    /// <summary>
    /// Keyboard uses Left/Right/Up/Down actions; stick uses Movement.
    /// Merge them so both schemes work.
    /// </summary>
    protected void RefreshMoveInputFromButtons()
    {
        if (scriptedMoveActive)
        {
            moveInput = new Vector2(scriptedMoveX, 0f);
            return;
        }

        if (controls == null)
            return;

        if (inputLocked || isStunned)
        {
            moveInput = Vector2.zero;
            return;
        }

        Vector2 stick = controls.PlayerControls.Movement.ReadValue<Vector2>();
        Vector2 buttons = Vector2.zero;

        if (controls.PlayerControls.Left.IsPressed()) buttons.x -= 1f;
        if (controls.PlayerControls.Right.IsPressed()) buttons.x += 1f;
        if (controls.PlayerControls.Up.IsPressed()) buttons.y += 1f;
        if (controls.PlayerControls.Down.IsPressed()) buttons.y -= 1f;

        if (buttons.sqrMagnitude > 0.01f)
            moveInput = buttons;
        else
            moveInput = stick;
    }

    protected void UpdateGrounded()
    {
        if (groundCheck == null)
        {
            isGrounded = false;
            TickCoyoteTimer();
            return;
        }

        // OverlapCircle ignores Physics2D.IgnoreCollision — filter passable platforms manually
        // or coyote never expires while overlapping a mover you're not riding.
        Collider2D[] hits = Physics2D.OverlapCircleAll(
            groundCheck.position,
            groundCheckRadius,
            groundLayers);

        isGrounded = false;
        for (int i = 0; i < hits.Length; i++)
        {
            Collider2D hit = hits[i];
            if (hit == null)
                continue;

            if (!CountsAsGroundCollider(hit))
                continue;

            isGrounded = true;
            break;
        }

        if (isGrounded)
            coyoteTimer = coyoteTime;
        else
            TickCoyoteTimer();
    }

    private void TickCoyoteTimer()
    {
        if (coyoteTimer <= 0f)
        {
            coyoteTimer = 0f;
            return;
        }

        coyoteTimer -= Time.fixedDeltaTime;
        if (coyoteTimer < 0f)
            coyoteTimer = 0f;
    }

    /// <summary>
    /// True when this collider should count for grounded / coyote refresh.
    /// Moving platforms only count while seated; slim platforms don't count during drop-through.
    /// </summary>
    private bool CountsAsGroundCollider(Collider2D hit)
    {
        if (hit == null)
            return false;

        SlimFactoryPlatform slim = hit.GetComponentInParent<SlimFactoryPlatform>();
        if (slim != null)
        {
            if (SlimFactoryPlatform.IsDroppingThrough(this))
                return false;
            return true;
        }

        MovingFactoryPlatform mover = hit.GetComponentInParent<MovingFactoryPlatform>();
        if (mover != null)
            return mover.IsSupporting(this);

        return true;
    }

    protected void HandleLandingAndAirDashRefill()
    {
        if (isGrounded && !wasGrounded)
            OnLanded();

        wasGrounded = isGrounded;
    }

    /// <summary>Character's air dashes plus one per equipped Ariel Action level.</summary>
    protected int MaxAirDashesWithUpgrades =>
        Mathf.Max(0, maxAirDashes) + PlayerUpgrades.GetActiveLevel(this, UpgradeType.AerialAction);

    protected virtual void OnLanded()
    {
        airDashesRemaining = MaxAirDashesWithUpgrades;
        upgradeAirJumpsRemaining = Mathf.Max(0, UpgradeAirJumps);
        isDashJumping = false;
        IsVoluntarilyAirborne = false;
        IsInWallBounceArc = false;
        dashJumpMomentumTimer = 0f;
        EndWallBounceCommit();
    }

    protected virtual void ApplyHorizontalMove()
    {
        if (skipHorizontalMoveThisFixed || wallBounceCommitActive || rb == null)
            return;

        float speed = GetCurrentMoveSpeed();
        float x;
        float y = rb.linearVelocity.y;

        if (isDashJumping && (dashJumpMomentumUntilLanded || dashJumpMomentumTimer > 0f))
        {
            float boosted = dashDirSign * GetDashJumpHorizontalSpeed();
            x = boosted;

            // Light opposite-steer cancel; same-direction input keeps full dash-jump speed.
            if (Mathf.Abs(moveInput.x) > 0.01f && Mathf.Sign(moveInput.x) != dashDirSign)
                x = Mathf.Lerp(boosted, moveInput.x * speed, dashJumpSteerCancel);
        }
        else
        {
            x = moveInput.x * speed;
        }

        // Ride moving factory platforms without locking walk input (add platform velocity).
        // Only inherit vertical ride when physically grounded — never overwrite an upward jump.
        if (MovingFactoryPlatform.TryGetRideVelocity(this, out Vector2 platVel))
        {
            x += platVel.x;
            if (isGrounded && Mathf.Abs(platVel.y) > 0.01f && rb.linearVelocity.y <= platVel.y + 0.15f)
                y = platVel.y;
        }

        rb.linearVelocity = new Vector2(x, y);
    }

    /// <summary>
    /// Attack/shoot lock: no walk input, but still carried by a moving factory platform
    /// (standing in place on it, not frozen in world space).
    /// </summary>
    protected void ApplyStandInPlaceMove()
    {
        if (rb == null)
            return;

        float x = 0f;
        float y = rb.linearVelocity.y;
        if (MovingFactoryPlatform.TryGetRideVelocity(this, out Vector2 platVel))
        {
            x = platVel.x;
            if (isGrounded && Mathf.Abs(platVel.y) > 0.01f && rb.linearVelocity.y <= platVel.y + 0.15f)
                y = platVel.y;
        }

        rb.linearVelocity = new Vector2(x, y);
    }

    /// <summary>Base move speed, or a character-specific boosted speed (e.g. Kit while charged).</summary>
    protected virtual float GetCurrentMoveSpeed()
    {
        return moveSpeed;
    }

    protected virtual void ApplyJump()
    {
        if (!jumpRequested)
            return;

        if (isStunned || BlocksActionCancel())
        {
            jumpRequested = false;
            return;
        }

        if (CanWallJumpNow())
        {
            jumpRequested = false;
            ApplyManualWallJump();
            return;
        }

        jumpRequested = false;

        // Kirby-style slim platforms: Down + Jump drops through instead of jumping.
        if (moveInput.y < -0.35f && IsGrounded)
        {
            if (SlimFactoryPlatform.TryDropThrough(this))
            {
                coyoteTimer = 0f;
                isGrounded = false;
                IsVoluntarilyAirborne = true;
                return;
            }
        }

        if (!IsGrounded)
        {
            if (upgradeAirJumpsRemaining > 0)
                PerformUpgradeAirJump();
            return;
        }

        // Consume coyote / leave ground for the jump.
        coyoteTimer = 0f;
        isGrounded = false;
        upgradeAirJumpsRemaining = Mathf.Max(0, UpgradeAirJumps);

        rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
        IsVoluntarilyAirborne = true;
        SoundManager.Instance?.PlayJump();
    }

    /// <summary>
    /// Extra full-height air jumps from Scratch's Ariel Action upgrade. Characters with their
    /// own air-jump system (Kit, Malice) keep this at 0.
    /// </summary>
    protected virtual int UpgradeAirJumps => 0;

    private void PerformUpgradeAirJump()
    {
        upgradeAirJumpsRemaining--;
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
        IsVoluntarilyAirborne = true;
        SoundManager.Instance?.PlayJump();

        if (animator == null || isShooting || isDashing)
            return;

        int jumpState = Animator.StringToHash("Jump");
        if (animator.HasState(0, jumpState))
            animator.Play(jumpState, 0, 0f);
    }

    protected bool CanPerformDashJump()
    {
        return allowDashJump && isDashing && isGrounded;
    }

    protected virtual void PerformDashJump()
    {
        jumpRequested = false;
        CancelDash(keepHorizontalMomentum: true);

        float jumpUp = dashJumpForce > 0f ? dashJumpForce : jumpForce;
        float jumpX = dashDirSign * GetDashJumpHorizontalSpeed();
        rb.linearVelocity = new Vector2(jumpX, jumpUp);
        IsVoluntarilyAirborne = true;
        SoundManager.Instance?.PlayJump();

        isDashJumping = true;
        dashJumpMomentumTimer = dashJumpMomentumUntilLanded
            ? float.PositiveInfinity
            : Mathf.Max(0.01f, dashJumpMomentumDuration);

        BeginDashAfterimageTrail(Mathf.Max(dashJumpMomentumDuration, GetMaxAfterimageDelay()));

        if (animator != null)
            animator.SetTrigger("Jump");
    }

    protected virtual float GetDashJumpHorizontalSpeed()
    {
        return dashJumpHorizontalSpeed > 0f ? dashJumpHorizontalSpeed : dashSpeed;
    }

    protected void TickDashJumpMomentum(float dt)
    {
        if (!isDashJumping)
            return;

        // Carry through jump + fall until landing.
        if (dashJumpMomentumUntilLanded)
            return;

        dashJumpMomentumTimer -= dt;
        if (dashJumpMomentumTimer > 0f)
            return;

        isDashJumping = false;
        dashJumpMomentumTimer = 0f;
    }

    /// <summary>Stops dash-jump carry so horizontal control returns to normal input (e.g. Kit hover).</summary>
    protected void EndDashJumpMomentum()
    {
        if (!isDashJumping)
            return;

        isDashJumping = false;
        dashJumpMomentumTimer = 0f;
        wasWallSlidingDuringDashJump = false;
    }

    protected void CancelDash(bool keepHorizontalMomentum)
    {
        if (!isDashing)
            return;

        isDashing = false;
        dashTimer = 0f;
        dashCooldownTimer = dashCooldown;

        if (!keepHorizontalMomentum && rb != null)
        {
            float exitSpeed = moveSpeed * dashEndSpeedMultiplier;
            rb.linearVelocity = new Vector2(dashDirSign * exitSpeed, rb.linearVelocity.y);
        }

        OnDashEnded();
    }

    protected virtual void ApplyFallMultiplier()
    {
        if (airHangActive || wallBounceCommitActive)
            return;

        if (rb.linearVelocity.y < 0f)
        {
            rb.linearVelocity += Vector2.up * Physics2D.gravity.y * (fallMultiplier - 1f) * Time.fixedDeltaTime;
        }
    }

    protected virtual void TickDash(float dt)
    {
        if (dashCooldownTimer > 0f)
            dashCooldownTimer -= dt;

        if (dashRequested)
        {
            dashRequested = false;
            TryBeginDash();
        }

        if (!isDashing)
            return;

        dashTimer += dt;
        if (dashTimer >= dashDuration)
        {
            isDashing = false;
            dashCooldownTimer = dashCooldown;

            // Soft handoff into normal move instead of a hard velocity cut.
            float exitSpeed = moveSpeed * dashEndSpeedMultiplier;
            rb.linearVelocity = new Vector2(dashDirSign * exitSpeed, rb.linearVelocity.y);
            OnDashEnded();
        }
    }

    protected virtual void TryBeginDash()
    {
        if (isDashing || dashCooldownTimer > 0f || isStunned || BlocksActionCancel())
            return;

        if (!isGrounded)
        {
            if (!allowAirDash)
                return;

            if (airDashesRemaining <= 0)
                return;

            airDashesRemaining--;
        }

        float distance = Mathf.Max(0.1f, dashDistance);
        float speed = Mathf.Max(0.1f, dashSpeed);

        isDashing = true;
        isDashJumping = false;
        dashJumpMomentumTimer = 0f;
        dashTimer = 0f;
        dashDuration = distance / speed;
        dashDirSign = facingSign;
        dashVelocityX = dashDirSign * speed;

        BeginDashAfterimageTrail(dashDuration);

        if (animator != null)
            animator.SetTrigger("Dash");

        OnDashStarted();
    }

    /// <summary>Called the frame a dash successfully begins.</summary>
    protected virtual void OnDashStarted()
    {
        SoundManager.Instance?.PlayDash();
    }

    /// <summary>Called when a dash ends or is canceled.</summary>
    protected virtual void OnDashEnded() { }

    /// <summary>
    /// Characters phase through each other and through bosses (solid colliders ignored).
    /// Trigger hitboxes still work so attacks / Malice dash can deal damage.
    /// </summary>
    public void RefreshPlayerPhaseCollisions()
    {
        Collider2D[] myCols = GetComponentsInChildren<Collider2D>(true);
        PlayerController[] players = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);

        for (int p = 0; p < players.Length; p++)
        {
            PlayerController other = players[p];
            if (other == null || other == this)
                continue;

            IgnoreSolidColliders(myCols, other.GetComponentsInChildren<Collider2D>(true));
        }

        Boss[] bosses = EnemyTypeCache.Bosses;
        for (int b = 0; b < bosses.Length; b++)
        {
            Boss boss = bosses[b];
            if (boss == null)
                continue;

            IgnoreSolidColliders(myCols, boss.GetComponentsInChildren<Collider2D>(true));
        }

        CrankyClanky[] enemies = EnemyTypeCache.Crankies;
        for (int e = 0; e < enemies.Length; e++)
        {
            CrankyClanky enemy = enemies[e];
            if (enemy == null)
                continue;

            IgnoreSolidColliders(myCols, enemy.GetComponentsInChildren<Collider2D>(true));
        }

        LaserBot[] laserBots = EnemyTypeCache.Lasers;
        for (int e = 0; e < laserBots.Length; e++)
        {
            LaserBot bot = laserBots[e];
            if (bot == null)
                continue;

            IgnoreSolidColliders(myCols, bot.GetComponentsInChildren<Collider2D>(true));
        }

        ChaoticTanker[] tankers = EnemyTypeCache.Tankers;
        for (int e = 0; e < tankers.Length; e++)
        {
            ChaoticTanker tanker = tankers[e];
            if (tanker == null)
                continue;

            IgnoreSolidColliders(myCols, tanker.GetComponentsInChildren<Collider2D>(true));
        }

        ScrapNit[] scrapNits = EnemyTypeCache.Scraps;
        for (int e = 0; e < scrapNits.Length; e++)
        {
            ScrapNit scrap = scrapNits[e];
            if (scrap == null)
                continue;

            IgnoreSolidColliders(myCols, scrap.GetComponentsInChildren<Collider2D>(true));
        }

        CheckPointBot[] checkpoints = EnemyTypeCache.CheckPoints;
        for (int e = 0; e < checkpoints.Length; e++)
        {
            CheckPointBot bot = checkpoints[e];
            if (bot == null)
                continue;

            IgnoreSolidColliders(myCols, bot.GetComponentsInChildren<Collider2D>(true));
        }

        ShredderBlade[] shredderBlades = EnemyTypeCache.Shredders;
        for (int s = 0; s < shredderBlades.Length; s++)
        {
            ShredderBlade blade = shredderBlades[s];
            if (blade == null)
                continue;

            // Include parent shredder-box solids — blades alone are triggers and never blocked movement.
            IgnoreSolidColliders(myCols, blade.GetShredderHierarchyColliders());
        }

        PhaseThroughPlayers[] phaseProps = FindObjectsByType<PhaseThroughPlayers>(FindObjectsSortMode.None);
        for (int p = 0; p < phaseProps.Length; p++)
        {
            PhaseThroughPlayers prop = phaseProps[p];
            if (prop == null)
                continue;

            IgnoreSolidColliders(myCols, prop.GetComponentsInChildren<Collider2D>(true));
        }

        // Town NPCs must stay phasable on HomeTown reloads / re-entries.
        NPC[] npcs = FindObjectsByType<NPC>(FindObjectsSortMode.None);
        for (int n = 0; n < npcs.Length; n++)
        {
            NPC npc = npcs[n];
            if (npc == null)
                continue;

            IgnoreSolidColliders(myCols, npc.GetComponentsInChildren<Collider2D>(true));
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

    /// <summary>Full speed for most of the dash, then ease out near the end for a smoother finish.</summary>
    protected virtual float GetCurrentDashVelocityX()
    {
        float peak = dashDirSign * dashSpeed;
        float stopWindow = Mathf.Min(dashSmoothStopDuration, dashDuration * 0.5f);
        if (stopWindow <= 0.0001f || dashTimer < dashDuration - stopWindow)
            return peak;

        float t = Mathf.InverseLerp(dashDuration - stopWindow, dashDuration, dashTimer);
        // Smoothstep ease-out.
        t = t * t * (3f - 2f * t);
        float end = dashDirSign * dashSpeed * dashEndSpeedMultiplier;
        return Mathf.Lerp(peak, end, t);
    }

    protected virtual float AfterimageFadeStaggerSeconds => 0f;

    protected virtual float AfterimageFadeDurationSeconds => 0.2f;

    protected void UpdateFacingFromInput()
    {
        if (Mathf.Abs(moveInput.x) < 0.01f)
            return;

        facingSign = Mathf.Sign(moveInput.x);
        ApplyFacingVisuals();
    }

    /// <summary>
    /// Left-facing art: flipX only when facing right.
    /// FirePoint mirroring happens in LateUpdate so animated left-facing keys are preserved.
    /// </summary>
    protected void ApplyFacingVisuals()
    {
        if (spriteRenderer == null)
            return;

        if (spriteFacesLeft)
            spriteRenderer.flipX = facingSign > 0f;
        else
            spriteRenderer.flipX = facingSign < 0f;
    }

    /// <summary>
    /// Your FirePoint keys are authored for looking left. When looking right, reflect X across the character center
    /// (e.g. -0.2 becomes +0.2). Does not change your left-facing placements.
    /// </summary>
    protected void MirrorFirePointForFacing()
    {
        MirrorChildLocalForFacing(
            firePoint,
            ref firePointLeftFacingLocal,
            ref firePointLastWrittenLocal,
            ref firePointHasLastWritten);
    }

    /// <summary>
    /// Same Kit FirePoint flip: keep Animator/editor left-facing local keys, then mirror X when facing right.
    /// Use for FirePoint, AttackBox, or any child authored while looking left.
    /// </summary>
    protected void MirrorChildLocalForFacing(
        Transform child,
        ref Vector3 leftFacingLocal,
        ref Vector3 lastWrittenLocal,
        ref bool hasLastWritten)
    {
        if (child == null)
            return;

        Vector3 currentLocal = GetChildCharacterLocal(child);

        // If Animator (or editor) changed the transform since we last wrote, that value is the left-facing source.
        bool unchangedSinceWeWrote = hasLastWritten &&
            (currentLocal - lastWrittenLocal).sqrMagnitude < 1e-10f;

        if (!unchangedSinceWeWrote)
            leftFacingLocal = currentLocal;

        Vector3 target = leftFacingLocal;
        bool mirror = spriteFacesLeft ? facingSign > 0f : facingSign < 0f;
        if (mirror)
        {
            float centerX = firePointFlipCenterLocal.x;
            target.x = centerX - (leftFacingLocal.x - centerX);
        }

        SetChildCharacterLocal(child, target);
        lastWrittenLocal = target;
        hasLastWritten = true;
    }

    protected Vector3 GetChildCharacterLocal(Transform child)
    {
        if (child == null)
            return Vector3.zero;

        if (child.parent == transform)
            return child.localPosition;

        return transform.InverseTransformPoint(child.position);
    }

    protected void SetChildCharacterLocal(Transform child, Vector3 characterLocal)
    {
        if (child == null)
            return;

        if (child.parent == transform)
            child.localPosition = characterLocal;
        else
            child.position = transform.TransformPoint(characterLocal);
    }

    protected Vector3 GetFirePointCharacterLocal()
    {
        return GetChildCharacterLocal(firePoint);
    }

    protected void SetFirePointCharacterLocal(Vector3 characterLocal)
    {
        SetChildCharacterLocal(firePoint, characterLocal);
    }

    /// <summary>True when holding Up or aiming diagonally upward (e.g. 45° stick).</summary>
    protected bool IsAimingUp()
    {
        return moveInput.y >= upAimThreshold;
    }

    /// <summary>True when holding Down or aiming diagonally downward.</summary>
    protected bool IsAimingDown()
    {
        return moveInput.y <= -upAimThreshold;
    }

    protected virtual void UpdateAnimator()
    {
        if (animator == null)
            return;

        float verticalSpeed = rb != null ? rb.linearVelocity.y : 0f;
        bool moving = Mathf.Abs(moveInput.x) > 0.01f && !isDashing && !BlocksRunAnimationWhileShooting();
        bool groundedForAnim = IsGrounded;

        // Parameters match Kit.controller (and future characters).
        animator.SetBool("IsMoving", moving);
        animator.SetBool("IsGrounded", groundedForAnim);
        animator.SetBool("IsInAir", !groundedForAnim);
        animator.SetBool("IsDashing", isDashing);
        animator.SetBool("IsShooting", isShooting);
        animator.SetBool("AimUp", IsAimingUp());
        animator.SetBool("IsStunned", isStunned);
        animator.SetBool("IsFalling", !groundedForAnim && verticalSpeed < -0.01f);
        animator.SetBool("IsJumping", !groundedForAnim && verticalSpeed >= -0.01f);
        animator.SetFloat("VerticalSpeed", verticalSpeed);
    }

    protected void BeginShootAnimation(float duration = 0.25f)
    {
        isShooting = true;
        shootAnimTimer = Mathf.Max(0.05f, duration);
    }

    /// <summary>
    /// When true, IsMoving stays off while isShooting. Override to allow run anims during charge, etc.
    /// </summary>
    protected virtual bool BlocksRunAnimationWhileShooting()
    {
        return isShooting;
    }

    public virtual void SetHealth(int value)
    {
        int clamped = Mathf.Clamp(value, 0, maxHealth);
        if (clamped == currentHealth)
            return;

        currentHealth = clamped;
        OnHealthChanged?.Invoke(currentHealth, maxHealth);

        if (currentHealth <= 0)
        {
            VisualEffects.StopStunned(this);
            PlayDeathEnergyBallsOnDeath();
            OnDied?.Invoke();
        }
    }

    /// <summary>
    /// Mega Man death burst. Character vanishes immediately at 0 HP; balls spawn from that same moment.
    /// </summary>
    protected virtual void PlayDeathEnergyBallsOnDeath()
    {
        SetInputLocked(true);
        if (rb != null)
            rb.linearVelocity = Vector2.zero;
        knockbackActive = false;

        VisualEffects.SetHostSpritesVisible(gameObject, false);
        VisualEffects.PlayDeathEnergyBalls(deathEnergyBallPrefab, this, hideHost: true);
    }

    /// <summary>
    /// Stun without damage (copy-bot diversion clones, traps, etc.).
    /// </summary>
    public void ApplyStunOnly(float duration)
    {
        if (duration <= 0f || currentHealth <= 0)
            return;

        isStunned = true;
        stunTimer = Mathf.Max(stunTimer, duration);
        moveInput = Vector2.zero;
        jumpRequested = false;
        dashRequested = false;

        // Dash-jump momentum too, or it resumes as soon as the stun ends.
        if (isDashing || isDashJumping)
            CancelAllDashState();

        OnHitStunStarted();
    }

    public virtual void TakeDamage(int amount)
    {
        TakeDamage(amount, null, applyKnockback: true);
    }

    public virtual void TakeDamage(int amount, Transform hitSource)
    {
        TakeDamage(amount, hitSource, applyKnockback: true);
    }

    public virtual void TakeDamage(int amount, Transform hitSource, bool applyKnockback)
    {
        if (amount <= 0 || currentHealth <= 0 || IsRidingVehicle || IsScriptedInvulnerable)
            return;

        if (invincibilityTimer > 0f)
            return;

        SetHealth(currentHealth - amount);

        if (currentHealth > 0)
            BeginHitReaction(hitSource, applyKnockback);
    }

    protected virtual void TickHitReaction(float dt)
    {
        if (stunTimer > 0f)
        {
            stunTimer -= dt;
            if (stunTimer <= 0f)
            {
                stunTimer = 0f;
                if (isStunned)
                {
                    isStunned = false;
                    OnHitStunEnded();
                }
            }
        }

        if (invincibilityTimer > 0f)
        {
            invincibilityTimer = Mathf.Max(0f, invincibilityTimer - dt);
            TickInvincibilityFlicker(dt);
        }
        else if (invincibilityFlickerActive)
        {
            ClearInvincibilityFlicker();
        }
    }

    private void TickInvincibilityFlicker(float dt)
    {
        if (currentHealth <= 0)
            return;

        invincibilityFlickerActive = true;
        invincibilityFlickerTimer += dt;
        float half = Mathf.Max(0.02f, invincibilityFlickerHalfPeriod);
        bool high = (Mathf.FloorToInt(invincibilityFlickerTimer / half) % 2) == 0;
        SetBodySpriteAlpha(high ? invincibilityFlickerAlphaHigh : invincibilityFlickerAlphaLow);
    }

    private void ClearInvincibilityFlicker()
    {
        invincibilityFlickerActive = false;
        invincibilityFlickerTimer = 0f;
        if (currentHealth > 0)
            SetBodySpriteAlpha(1f);
    }

    private void SetBodySpriteAlpha(float alpha)
    {
        SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer sr = renderers[i];
            if (sr == null || IsAfterimageRenderer(sr))
                continue;

            Color c = sr.color;
            c.a = alpha;
            sr.color = c;
        }
    }

    private bool IsAfterimageRenderer(SpriteRenderer sr)
    {
        if (afterimageRenderers == null || sr == null)
            return false;

        for (int i = 0; i < afterimageRenderers.Length; i++)
        {
            if (afterimageRenderers[i] == sr)
                return true;
        }

        return sr.gameObject.name.IndexOf("Afterimage", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// Stun + invincibility after a successful hit. Shared by Kit and Malice.
    /// </summary>
    protected virtual void BeginHitReaction(Transform hitSource = null, bool applyKnockback = true)
    {
        isStunned = true;
        stunTimer = Mathf.Max(0f, hitStunDuration);
        invincibilityTimer = Mathf.Max(0f, hitInvincibilityDuration);
        invincibilityFlickerTimer = 0f;
        invincibilityFlickerActive = true;
        SetBodySpriteAlpha(invincibilityFlickerAlphaHigh);

        moveInput = Vector2.zero;
        jumpRequested = false;
        dashRequested = false;

        if (isDashing || isDashJumping)
            CancelAllDashState();

        if (applyKnockback)
            ApplyHitKnockback(hitSource);

        OnHitStunStarted();
    }

    private void ApplyHitKnockback(Transform hitSource)
    {
        if (rb == null || hitKnockbackDistance <= 0.001f)
        {
            knockbackActive = false;
            return;
        }

        BeginHorizontalKnockback(hitSource, hitKnockbackDistance, hitKnockbackDuration);
    }

    /// <summary>
    /// Soft shove away from a source (no damage / stun). Used by hazards like Laser Bot body touch.
    /// Soft bounces are hard-capped at 1 space.
    /// </summary>
    public void ApplySoftBounce(Transform source, float distance = 1f, float duration = 0.18f)
    {
        if (rb == null || IsDead || ShouldIgnoreSoftBounceFrom(source))
            return;

        float dir = -facingSign;
        if (source != null)
        {
            float dx = transform.position.x - source.position.x;
            if (Mathf.Abs(dx) > 0.01f)
                dir = Mathf.Sign(dx);
        }

        ApplySoftBounce(dir, distance, duration);
    }

    /// <summary>Lets a character shrug off a source's soft shove (e.g. Malice winning a melee trade).</summary>
    protected virtual bool ShouldIgnoreSoftBounceFrom(Transform source)
    {
        return false;
    }

    /// <summary>Soft shove in an explicit horizontal direction (Blocker Bot side hits).</summary>
    public void ApplySoftBounce(float direction, float distance = 1f, float duration = 0.18f)
    {
        if (rb == null || IsDead || IsRidingVehicle || IsScriptedInvulnerable)
            return;

        float dir = Mathf.Sign(direction);
        if (Mathf.Abs(dir) < 0.01f)
            dir = -facingSign;

        float capped = Mathf.Clamp(distance, 0.05f, 1f);
        BeginHorizontalKnockback(dir, capped, Mathf.Max(0.05f, duration));
    }

    private void BeginHorizontalKnockback(Transform hitSource, float distance, float duration)
    {
        // Away from the attacker when known; otherwise opposite of facing.
        float dir = -facingSign;
        if (hitSource != null)
        {
            float dx = transform.position.x - hitSource.position.x;
            if (Mathf.Abs(dx) > 0.01f)
                dir = Mathf.Sign(dx);
        }

        BeginHorizontalKnockback(dir, Mathf.Max(0.05f, distance), duration);
    }

    private void BeginHorizontalKnockback(float dir, float distance, float duration)
    {
        // A shove ends any dash; otherwise the dash timer keeps running and takes over after.
        if (isDashing || isDashJumping)
            CancelAllDashState();

        knockbackStart = rb.position;
        knockbackEnd = knockbackStart + new Vector2(dir * distance, 0f);
        knockbackElapsed = 0f;
        knockbackDurationActive = Mathf.Max(0.05f, duration);
        knockbackActive = true;
        rb.linearVelocity = new Vector2(0f, airHangActive ? 0f : rb.linearVelocity.y);
    }

    private void TickHitKnockback(float dt)
    {
        if (!knockbackActive || rb == null)
            return;

        float duration = Mathf.Max(0.01f, knockbackDurationActive);
        knockbackElapsed += dt;
        float t = Mathf.Clamp01(knockbackElapsed / duration);
        // Smootherstep: slow start and slow finish so the shove never snaps.
        float s = t * t * t * (t * (t * 6f - 15f) + 10f);

        // Drive by velocity (not MovePosition) so walls and platforms stop the shove instead of
        // the body being forced into them and popped out at high speed.
        Vector2 pos = Vector2.Lerp(knockbackStart, knockbackEnd, s);
        float vy = airHangActive ? 0f : rb.linearVelocity.y;
        float maxSpeed = 2f * Mathf.Abs(knockbackEnd.x - knockbackStart.x) / duration;
        float vx = (pos.x - rb.position.x) / Mathf.Max(0.0001f, Time.fixedDeltaTime);
        vx = Mathf.Clamp(vx, -maxSpeed, maxSpeed);
        vx = ClampKnockbackStepAgainstWalls(vx);
        rb.linearVelocity = new Vector2(vx, vy);

        if (CameraFollow.Instance != null)
            CameraFollow.Instance.ClampRigidbodyToPlayableBounds(rb, bodyCollider);

        if (t >= 1f)
            knockbackActive = false;
    }

    private static readonly RaycastHit2D[] KnockbackCastHits = new RaycastHit2D[8];

    /// <summary>Stops a knockback step at solid walls (players use discrete collision, so fast steps could tunnel).</summary>
    private float ClampKnockbackStepAgainstWalls(float vx)
    {
        if (bodyCollider == null || Mathf.Abs(vx) < 0.0001f)
            return vx;

        float dir = Mathf.Sign(vx);
        float step = Mathf.Abs(vx) * Time.fixedDeltaTime;
        const float skin = 0.02f;

        ContactFilter2D filter = new ContactFilter2D();
        filter.useTriggers = false;
        filter.SetLayerMask(Physics2D.GetLayerCollisionMask(gameObject.layer));

        int count = bodyCollider.Cast(new Vector2(dir, 0f), filter, KnockbackCastHits, step + skin, true);
        float allowed = step;
        for (int i = 0; i < count; i++)
        {
            RaycastHit2D hit = KnockbackCastHits[i];
            if (hit.collider == null || hit.collider.isTrigger)
                continue;
            if (hit.normal.x * dir > -0.5f)
                continue;
            if (Physics2D.GetIgnoreCollision(bodyCollider, hit.collider))
                continue;
            if (hit.collider.GetComponentInParent<PlayerController>() == this)
                continue;

            allowed = Mathf.Min(allowed, Mathf.Max(0f, hit.distance - skin));
        }

        return dir * allowed / Time.fixedDeltaTime;
    }

    protected virtual void OnHitStunStarted()
    {
        VisualEffects.PlayStunned(stunnedEffectPrefab, this);
    }

    protected virtual void OnHitStunEnded()
    {
        VisualEffects.StopStunned(this);
    }

    /// <summary>
    /// Freeze vertical motion (air dash / air grapple hang). Restores gravity when cleared.
    /// </summary>
    protected void SetAirHang(bool active)
    {
        airHangActive = active;
        if (rb == null)
            return;

        if (active)
        {
            rb.gravityScale = 0f;
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f);
        }
        else
        {
            rb.gravityScale = defaultGravityScale;
        }
    }

    public virtual void Heal(int amount)
    {
        if (amount <= 0 || currentHealth <= 0)
            return;

        int before = currentHealth;
        SetHealth(currentHealth + amount);
        if (currentHealth > before)
            VisualEffects.PlayHealed(healedEffectPrefab, this);
    }

    public virtual void SetCharacterId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return;

        characterId = id;
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }

    protected virtual Vector2 GetAimDirection()
    {
        // Hold Up OR aim ~45° upward → fire at 45° in the facing direction.
        if (IsAimingUp())
            return new Vector2(facingSign, 1f).normalized;

        return new Vector2(facingSign, 0f);
    }

    protected Vector3 GetFirePosition()
    {
        if (firePoint != null)
            return firePoint.position;

        // Fallback muzzle in front of left-facing art.
        float forward = facingSign * 0.5f;
        float up = IsAimingUp() ? 0.35f : 0.1f;
        return transform.position + new Vector3(forward, up, 0f);
    }

    protected void SetupDashAfterimages()
    {
        DestroyDashAfterimages();

        if (!enableDashAfterimages)
            return;

        int count = Mathf.Max(1, dashAfterimageCount);
        afterimageObjects = new GameObject[count];
        afterimageRenderers = new SpriteRenderer[count];

        for (int i = 0; i < count; i++)
        {
            var go = new GameObject($"{name}_DashAfterimage_{i + 1}");
            // Stay under the character Sorting Group so trails sort behind the body vs the world.
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            CharacterEffectSorting.ApplyTrailBehindBody(sr, spriteRenderer, effectSortingGroup, i);

            float t = count == 1 ? 0f : i / (float)(count - 1);
            Color c = dashAfterimageColor;
            c.a = Mathf.Lerp(dashAfterimageAlphaStart, dashAfterimageAlphaEnd, t);
            sr.color = c;

            go.SetActive(false);
            afterimageObjects[i] = go;
            afterimageRenderers[i] = sr;
        }
    }

    protected void DestroyDashAfterimages()
    {
        if (afterimageObjects == null)
            return;

        for (int i = 0; i < afterimageObjects.Length; i++)
        {
            if (afterimageObjects[i] != null)
                Destroy(afterimageObjects[i]);
        }

        afterimageObjects = null;
        afterimageRenderers = null;
    }

    protected void BeginDashAfterimageTrail(float activeDuration)
    {
        if (!enableDashAfterimages || afterimageRenderers == null)
            return;

        float keep = Mathf.Max(activeDuration, GetMaxAfterimageDelay()) + dashAfterimageLifetimePadding;
        afterimagesVisibleUntil = Time.time + keep;
        SetDashAfterimagesActive(true);
    }

    protected float GetAfterimageDelay(int index)
    {
        return Mathf.Max(0.01f, dashAfterimageSpacing) * (index + 1);
    }

    protected float GetMaxAfterimageDelay()
    {
        int count = Mathf.Max(1, dashAfterimageCount);
        return GetAfterimageDelay(count - 1);
    }

    protected void RecordPoseHistory()
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

        float keepFor = GetMaxAfterimageDelay() + 0.35f;
        float cutoff = Time.time - keepFor;
        while (poseHistory.Count > 0 && poseHistory[0].time < cutoff)
            poseHistory.RemoveAt(0);
    }

    protected bool TrySamplePose(float delay, out PoseSample sample)
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

    protected virtual void UpdateDashAfterimages()
    {
        if (!enableDashAfterimages || afterimageRenderers == null || spriteRenderer == null)
            return;

        bool trailActive = isDashing || isDashJumping || Time.time <= afterimagesVisibleUntil;
        if (!trailActive)
        {
            if (TryUpdateAfterimageFadeOut())
                return;

            SetDashAfterimagesActive(false);
            afterimageFadeOutActive = false;
            return;
        }

        afterimageFadeOutActive = false;

        // Keep trail alive while dash-jump momentum is carrying through the fall.
        if (isDashJumping || isDashing)
            afterimagesVisibleUntil = Mathf.Max(afterimagesVisibleUntil, Time.time + GetMaxAfterimageDelay() + dashAfterimageLifetimePadding);

        for (int i = 0; i < afterimageRenderers.Length; i++)
        {
            float delay = GetAfterimageDelay(i);
            if (!TrySamplePose(delay, out PoseSample sample))
            {
                afterimageObjects[i].SetActive(false);
                continue;
            }

            afterimageObjects[i].SetActive(true);
            afterimageObjects[i].transform.position = sample.position;
            afterimageRenderers[i].sprite = sample.sprite != null ? sample.sprite : spriteRenderer.sprite;
            afterimageRenderers[i].flipX = sample.flipX;
            CharacterEffectSorting.ApplyTrailBehindBody(
                afterimageRenderers[i],
                spriteRenderer,
                effectSortingGroup,
                i);

            float t = afterimageRenderers.Length == 1 ? 0f : i / (float)(afterimageRenderers.Length - 1);
            Color c = dashAfterimageColor;
            c.a = Mathf.Lerp(dashAfterimageAlphaStart, dashAfterimageAlphaEnd, t);
            afterimageRenderers[i].color = c;
        }
    }

    private bool TryUpdateAfterimageFadeOut()
    {
        float stagger = AfterimageFadeStaggerSeconds;
        if (stagger <= 0f || afterimageRenderers == null)
            return false;

        if (!afterimageFadeOutActive)
        {
            bool anyVisible = false;
            for (int i = 0; i < afterimageObjects.Length; i++)
            {
                if (afterimageObjects[i] != null && afterimageObjects[i].activeSelf)
                {
                    anyVisible = true;
                    break;
                }
            }

            if (!anyVisible)
                return false;

            afterimageFadeOutActive = true;
            afterimageFadeOutStartTime = Time.time;
            afterimageFadeBaseAlpha = new float[afterimageRenderers.Length];
            for (int i = 0; i < afterimageRenderers.Length; i++)
            {
                afterimageFadeBaseAlpha[i] = afterimageRenderers[i] != null
                    ? afterimageRenderers[i].color.a
                    : dashAfterimageAlphaEnd;
            }
        }

        float fadeDuration = Mathf.Max(0.05f, AfterimageFadeDurationSeconds);
        int count = afterimageRenderers.Length;
        bool anyStillVisible = false;

        for (int i = 0; i < count; i++)
        {
            // Farthest trail (highest index) fades out first, closest last.
            float fadeStart = afterimageFadeOutStartTime + (count - 1 - i) * stagger;
            float fadeT = Mathf.InverseLerp(fadeStart, fadeStart + fadeDuration, Time.time);

            if (fadeT >= 1f)
            {
                if (afterimageObjects[i] != null)
                    afterimageObjects[i].SetActive(false);
                continue;
            }

            anyStillVisible = true;
            if (afterimageObjects[i] == null || afterimageRenderers[i] == null)
                continue;

            afterimageObjects[i].SetActive(true);
            float baseAlpha = afterimageFadeBaseAlpha != null && i < afterimageFadeBaseAlpha.Length
                ? afterimageFadeBaseAlpha[i]
                : dashAfterimageAlphaEnd;
            Color c = afterimageRenderers[i].color;
            c.a = baseAlpha * (1f - SmoothFade(fadeT));
            afterimageRenderers[i].color = c;
        }

        if (!anyStillVisible)
            afterimageFadeOutActive = false;

        return anyStillVisible;
    }

    private static float SmoothFade(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    protected void SetDashAfterimagesActive(bool active)
    {
        if (afterimageObjects == null)
            return;

        for (int i = 0; i < afterimageObjects.Length; i++)
        {
            if (afterimageObjects[i] != null)
                afterimageObjects[i].SetActive(active);
        }
    }

#if UNITY_EDITOR
    protected virtual void OnValidate()
    {
        maxHealth = Mathf.Max(1, maxHealth);
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
        groundCheckRadius = Mathf.Max(0.01f, groundCheckRadius);
        moveSpeed = Mathf.Max(0f, moveSpeed);
        jumpForce = Mathf.Max(0f, jumpForce);
        coyoteTime = Mathf.Max(0f, coyoteTime);
        platformSeparationDistance = Mathf.Max(0f, platformSeparationDistance);
        platformTopNormalThreshold = Mathf.Clamp01(platformTopNormalThreshold);
        wallSlideAcceleration = Mathf.Max(0f, wallSlideAcceleration);
        wallSlideMaxFallSpeed = Mathf.Max(0.1f, wallSlideMaxFallSpeed);
        wallProbeDistance = Mathf.Max(0f, wallProbeDistance);
        wallProbeSize.x = Mathf.Max(0.02f, wallProbeSize.x);
        wallProbeSize.y = Mathf.Max(0.02f, wallProbeSize.y);
        wallJumpCoyoteTime = Mathf.Max(0f, wallJumpCoyoteTime);
        wallBounceShallowDistance = Mathf.Max(0.1f, wallBounceShallowDistance);
        wallBounceSteepClimbHeight = Mathf.Max(0.1f, wallBounceSteepClimbHeight);
        wallBounceSteepClimbSpeedMultiplier = Mathf.Max(1f, wallBounceSteepClimbSpeedMultiplier);
        wallBounceSteepWallSeparation = Mathf.Max(0f, wallBounceSteepWallSeparation);
        wallBounceShallowAngleDeg = Mathf.Clamp(wallBounceShallowAngleDeg, 15f, 89f);
        wallBounceCooldown = Mathf.Max(0f, wallBounceCooldown);
        wallBounceLaunchSpeedMultiplier = Mathf.Max(1f, wallBounceLaunchSpeedMultiplier);
        wallBounceCommitVelocityRestore = Mathf.Max(1f, wallBounceCommitVelocityRestore);
        dashSpeed = Mathf.Max(0.1f, dashSpeed);
        dashDistance = Mathf.Max(0.1f, dashDistance);
        dashCooldown = Mathf.Max(0f, dashCooldown);
        dashSmoothStopDuration = Mathf.Max(0f, dashSmoothStopDuration);
        maxAirDashes = Mathf.Max(0, maxAirDashes);
        dashJumpMomentumDuration = Mathf.Max(0.01f, dashJumpMomentumDuration);
        dashAfterimageCount = Mathf.Max(1, dashAfterimageCount);
        dashAfterimageSpacing = Mathf.Max(0.01f, dashAfterimageSpacing);
        dashAfterimageAlphaEnd = Mathf.Min(dashAfterimageAlphaEnd, dashAfterimageAlphaStart);
        hitStunDuration = Mathf.Max(0f, hitStunDuration);
        hitStunDuration = Mathf.Max(0f, hitStunDuration);
        hitInvincibilityDuration = Mathf.Max(0f, hitInvincibilityDuration);
        invincibilityFlickerAlphaHigh = Mathf.Clamp01(invincibilityFlickerAlphaHigh);
        invincibilityFlickerAlphaLow = Mathf.Clamp01(invincibilityFlickerAlphaLow);
        invincibilityFlickerHalfPeriod = Mathf.Max(0.02f, invincibilityFlickerHalfPeriod);
        hitKnockbackDistance = Mathf.Max(0f, hitKnockbackDistance);
        hitKnockbackDuration = Mathf.Max(0.01f, hitKnockbackDuration);
    }

    protected virtual void OnDrawGizmosSelected()
    {
        Vector3 centerWorld = transform.TransformPoint(new Vector3(firePointFlipCenterLocal.x, firePointFlipCenterLocal.y, 0f));
        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
        Gizmos.DrawLine(centerWorld + Vector3.up * 1.5f, centerWorld + Vector3.down * 1.5f);

        if (firePoint != null)
        {
            Gizmos.color = new Color(1f, 0.4f, 0.9f, 0.9f);
            Gizmos.DrawWireSphere(firePoint.position, 0.08f);
        }

        if (groundCheck == null)
            return;

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
    }
#endif
}
