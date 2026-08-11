using System;
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

    [Header("Combat - Hit Reaction")]
    [Tooltip("How long the character is stunned (no control) after taking damage.")]
    [SerializeField] protected float hitStunDuration = 0.5f;
    [Tooltip("How long the character is invincible after taking damage.")]
    [SerializeField] protected float hitInvincibilityDuration = 1.5f;

    [Header("Combat - VFX Prefabs")]
    [Tooltip("Assign your Default Stunned prefab. Set Effect Type = Default Stunned on the prefab.")]
    [SerializeField] protected VisualEffect stunnedEffectPrefab;
    [Tooltip("Assign Kit/Malice Death Energy Ball prefab. Set Effect Type = Death Energy Ball.")]
    [SerializeField] protected VisualEffect deathEnergyBallPrefab;

    [Header("Health")]
    [SerializeField] protected int maxHealth = 10;
    [SerializeField] protected int currentHealth = 10;

    public string CharacterId => characterId;
    public int MaxHealth => maxHealth;
    public int CurrentHealth => currentHealth;
    public bool IsDead => currentHealth <= 0;
    public bool IsGrounded => isGrounded;
    public bool IsDashing => isDashing;
    public bool IsDashJumping => isDashJumping;
    public bool IsStunned => isStunned;
    public bool IsInvincible => invincibilityTimer > 0f;
    public float FacingSign => facingSign;
    public float NormalMoveSpeed => moveSpeed;
    public Vector2 Velocity => rb != null ? rb.linearVelocity : Vector2.zero;
    public Vector2 MoveInput => moveInput;
    public bool InputLocked => inputLocked;
    public bool DashDisabled => dashDisabled;

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
    float afterimagesVisibleUntil;
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
        RefreshPlayerPhaseCollisions();
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
        UpdateGrounded();
        HandleLandingAndAirDashRefill();
        TickDashJumpMomentum(Time.fixedDeltaTime);
        TickDash(Time.fixedDeltaTime);

        if (isDashing)
        {
            // Mega Man Zero: jump out of a grounded dash → dash jump.
            if (jumpRequested && CanPerformDashJump())
            {
                PerformDashJump();
            }
            else
            {
                // Air dash hangs in the air (no fall) until the dash ends.
                rb.linearVelocity = new Vector2(GetCurrentDashVelocityX(), 0f);
                jumpRequested = false;
                HandleCharacterFixedUpdate();
                ClampToPlayableWorld();
                return;
            }
        }

        if (isStunned)
        {
            jumpRequested = false;
            dashRequested = false;
            if (rb != null)
                rb.linearVelocity = new Vector2(0f, airHangActive ? 0f : rb.linearVelocity.y);
            ApplyFallMultiplier();
            HandleCharacterFixedUpdate();
            ClampToPlayableWorld();
            return;
        }

        if (airHangActive && rb != null)
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f);

        ApplyHorizontalMove();
        ApplyJump();
        ApplyFallMultiplier();
        HandleCharacterFixedUpdate();
        ClampToPlayableWorld();
    }

    /// <summary>
    /// Stay inside the same background edges the camera cannot leave.
    /// </summary>
    protected void ClampToPlayableWorld()
    {
        if (rb == null || CameraFollow.Instance == null)
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
    }

    protected virtual void OnJumpPerformed(InputAction.CallbackContext context)
    {
        if (!context.performed || inputLocked || isStunned || BlocksActionCancel())
            return;

        jumpRequested = true;
    }

    protected virtual void OnDashPerformed(InputAction.CallbackContext context)
    {
        if (!context.performed || inputLocked || dashDisabled || isStunned || BlocksActionCancel())
            return;

        dashRequested = true;
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
            return;
        }

        isGrounded = Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayers);
    }

    protected void HandleLandingAndAirDashRefill()
    {
        if (isGrounded && !wasGrounded)
            OnLanded();

        wasGrounded = isGrounded;
    }

    protected virtual void OnLanded()
    {
        airDashesRemaining = Mathf.Max(0, maxAirDashes);
        isDashJumping = false;
        dashJumpMomentumTimer = 0f;
    }

    protected virtual void ApplyHorizontalMove()
    {
        float speed = GetCurrentMoveSpeed();

        if (isDashJumping && (dashJumpMomentumUntilLanded || dashJumpMomentumTimer > 0f))
        {
            float boosted = dashDirSign * GetDashJumpHorizontalSpeed();
            float x = boosted;

            // Light opposite-steer cancel; same-direction input keeps full dash-jump speed.
            if (Mathf.Abs(moveInput.x) > 0.01f && Mathf.Sign(moveInput.x) != dashDirSign)
                x = Mathf.Lerp(boosted, moveInput.x * speed, dashJumpSteerCancel);

            rb.linearVelocity = new Vector2(x, rb.linearVelocity.y);
            return;
        }

        rb.linearVelocity = new Vector2(moveInput.x * speed, rb.linearVelocity.y);
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

        jumpRequested = false;

        if (isStunned || BlocksActionCancel())
            return;

        if (!isGrounded)
            return;

        rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
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

        isDashJumping = true;
        dashJumpMomentumTimer = dashJumpMomentumUntilLanded
            ? float.PositiveInfinity
            : Mathf.Max(0.01f, dashJumpMomentumDuration);

        BeginDashAfterimageTrail(Mathf.Max(dashJumpMomentumDuration, GetMaxAfterimageDelay()));

        if (animator != null)
            animator.SetTrigger("Jump");
    }

    protected float GetDashJumpHorizontalSpeed()
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
        if (airHangActive)
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
    protected virtual void OnDashStarted() { }

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

        Boss[] bosses = FindObjectsByType<Boss>(FindObjectsSortMode.None);
        for (int b = 0; b < bosses.Length; b++)
        {
            Boss boss = bosses[b];
            if (boss == null)
                continue;

            IgnoreSolidColliders(myCols, boss.GetComponentsInChildren<Collider2D>(true));
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

    /// <summary>
    /// Full speed for most of the dash, then ease out near the end for a smoother finish.
    /// </summary>
    protected float GetCurrentDashVelocityX()
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

    protected virtual void UpdateAnimator()
    {
        if (animator == null)
            return;

        float verticalSpeed = rb != null ? rb.linearVelocity.y : 0f;
        bool moving = Mathf.Abs(moveInput.x) > 0.01f && !isDashing && !BlocksRunAnimationWhileShooting();

        // Parameters match Kit.controller (and future characters).
        animator.SetBool("IsMoving", moving);
        animator.SetBool("IsGrounded", isGrounded);
        animator.SetBool("IsInAir", !isGrounded);
        animator.SetBool("IsDashing", isDashing);
        animator.SetBool("IsShooting", isShooting);
        animator.SetBool("AimUp", IsAimingUp());
        animator.SetBool("IsStunned", isStunned);
        animator.SetBool("IsFalling", !isGrounded && verticalSpeed < -0.01f);
        animator.SetBool("IsJumping", !isGrounded && verticalSpeed >= -0.01f);
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
    /// Mega Man death burst. Hides the character only when this death consumes the last life.
    /// </summary>
    protected virtual void PlayDeathEnergyBallsOnDeath()
    {
        PlayerInventory inv = PlayerInventory.Instance;
        // Life is deducted by BossFightDirector after OnDied — hide if this is the last life.
        bool hideCharacter = inv == null || inv.LivesCount <= 1;
        VisualEffects.PlayDeathEnergyBalls(deathEnergyBallPrefab, this, hideCharacter);
    }

    public virtual void TakeDamage(int amount)
    {
        if (amount <= 0 || currentHealth <= 0)
            return;

        if (invincibilityTimer > 0f)
            return;

        SetHealth(currentHealth - amount);

        if (currentHealth > 0)
            BeginHitReaction();
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
            invincibilityTimer = Mathf.Max(0f, invincibilityTimer - dt);
    }

    /// <summary>
    /// Stun + invincibility after a successful hit. Shared by Kit and Malice.
    /// </summary>
    protected virtual void BeginHitReaction()
    {
        isStunned = true;
        stunTimer = Mathf.Max(0f, hitStunDuration);
        invincibilityTimer = Mathf.Max(0f, hitInvincibilityDuration);

        moveInput = Vector2.zero;
        jumpRequested = false;
        dashRequested = false;

        if (isDashing)
            CancelDash(keepHorizontalMomentum: false);

        if (rb != null)
            rb.linearVelocity = new Vector2(0f, airHangActive ? 0f : rb.linearVelocity.y);

        OnHitStunStarted();
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

        SetHealth(currentHealth + amount);
    }

    public virtual void SetCharacterId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return;

        characterId = id;
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }

    protected Vector2 GetAimDirection()
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

    protected void UpdateDashAfterimages()
    {
        if (!enableDashAfterimages || afterimageRenderers == null || spriteRenderer == null)
            return;

        bool trailActive = isDashing || isDashJumping || Time.time <= afterimagesVisibleUntil;
        if (!trailActive)
        {
            SetDashAfterimagesActive(false);
            return;
        }

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
        hitInvincibilityDuration = Mathf.Max(0f, hitInvincibilityDuration);
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
