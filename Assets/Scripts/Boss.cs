using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Base boss / enemy fighter. Put this on a copy of a character prefab
/// (Gameoverse → Boss → Create Boss Prefab From Selected Character Prefab).
/// Players and bosses phase through each other; only AttackHitbox / projectiles deal damage.
/// Bosses are hurt on their body collider — not via their own AttackBox.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class Boss : MonoBehaviour, IDamageable
{
    [Header("Identity")]
    [SerializeField] private string bossId = "Boss";

    [Header("References")]
    [SerializeField] protected Transform groundCheck;
    [SerializeField] protected LayerMask groundLayers;
    [SerializeField] protected float groundCheckRadius = 0.12f;
    [SerializeField] protected Animator animator;
    [SerializeField] protected SpriteRenderer spriteRenderer;
    [Tooltip("Used only when the boss attacks. Not a hurtbox — the body collider takes hits.")]
    [SerializeField] protected AttackHitbox attackHitbox;
    [Tooltip("All boss art faces LEFT by default. flipX turns on when facing right.")]
    [SerializeField] protected bool spriteFacesLeft = true;

    [Header("Movement")]
    [SerializeField] protected float moveSpeed = 5f;
    [SerializeField] protected float jumpForce = 12f;
    [SerializeField] protected float fallMultiplier = 2.5f;

    [Header("Combat")]
    [SerializeField] protected float hitStunDuration = 0.35f;
    [SerializeField] protected float hitInvincibilityDuration = 0.75f;

    [Header("Combat - VFX Prefabs")]
    [Tooltip("Assign your Default Stunned prefab. Set Effect Type = Default Stunned on the prefab.")]
    [SerializeField] protected VisualEffect stunnedEffectPrefab;
    [Tooltip("Assign matching Death Energy Ball prefab (Kit balls for Boss Kit, Malice for Boss Malice).")]
    [SerializeField] protected VisualEffect deathEnergyBallPrefab;

    [Header("Health")]
    [SerializeField] protected int maxHealth = 100;
    [SerializeField] protected int currentHealth = 100;

    protected Rigidbody2D rb;
    protected float facingSign = -1f;
    protected bool isGrounded;
    protected bool isStunned;
    protected bool isAttacking;
    protected bool isDashing;
    protected bool isCharging;
    protected bool aimUp;
    protected float stunTimer;
    protected float invincibilityTimer;
    protected float defaultGravityScale = 1f;
    protected bool wantsMoveAnim;
    protected SortingGroup effectSortingGroup;
    protected Collider2D bodyCollider;

    // Crystal temporary boosts (set by Crystal.cs).
    protected float crystalMoveSpeedBonus;
    protected int crystalAttackBonus;
    private float crystalBoostEndsAt;
    private Coroutine crystalBoostRoutine;
    private GameObject crystalBoostAuraObject;
    private SpriteRenderer crystalBoostAuraRenderer;
    private Material crystalBoostAuraMaterial;
    private float crystalBoostAuraFlickerPhase;
    private Color crystalBoostAuraColor = new Color(0.2f, 0.55f, 1f, 1f);
    private Color crystalBoostAuraStrongColor = new Color(0.05f, 0.25f, 0.85f, 1f);

    public string BossId => bossId;
    public int MaxHealth => maxHealth;
    public int CurrentHealth => currentHealth;
    public bool IsGrounded => isGrounded;
    public bool IsStunned => isStunned;
    public bool IsInvincible => invincibilityTimer > 0f;
    public bool IsDead => currentHealth <= 0;
    public float FacingSign => facingSign;
    public Rigidbody2D Body => rb;
    public AttackHitbox AttackHitbox => attackHitbox;
    public SortingGroup EffectSortingGroup => effectSortingGroup;

    protected void SetBossId(string id)
    {
        if (!string.IsNullOrWhiteSpace(id))
            bossId = id;
    }

    public event Action<int, int> OnHealthChanged;
    public event Action OnDied;

    protected virtual void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        bodyCollider = GetComponent<Collider2D>();
        if (rb != null)
            defaultGravityScale = rb.gravityScale;

        if (animator == null)
            animator = GetComponent<Animator>();

        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();

        if (attackHitbox == null)
            attackHitbox = GetComponentInChildren<AttackHitbox>(true);

        if (attackHitbox != null)
            attackHitbox.SetOwner(this);

        currentHealth = Mathf.Clamp(currentHealth, 0, Mathf.Max(1, maxHealth));
        ApplyFacingVisual();
        effectSortingGroup = CharacterEffectSorting.EnsureHostSortingGroup(this, spriteRenderer);
        RefreshPhaseCollisionsWithPlayers();

        // Make sure already-spawned players also ignore this boss's solid collider.
        PlayerController[] players = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] != null)
                players[i].RefreshPlayerPhaseCollisions();
        }

        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }

    protected virtual void OnEnable()
    {
        if (attackHitbox != null)
            attackHitbox.SetOwner(this);
        RefreshPhaseCollisionsWithPlayers();
    }

    protected virtual void Update()
    {
        if (IsDead)
            return;

        TickHitReaction(Time.deltaTime);
        UpdateGrounded();
        TickPostHitVisuals(Time.deltaTime);

        if (!isStunned)
            HandleBossUpdate();

        UpdateAnimator();
        ClampToPlayableWorld();
    }

    protected virtual void FixedUpdate()
    {
        if (IsDead || rb == null)
            return;

        if (isStunned)
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            ApplyFallMultiplier();
            ClampToPlayableWorld();
            return;
        }

        HandleBossFixedUpdate();
        ApplyFallMultiplier();
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

    /// <summary>Override for AI decisions (choose attacks, chase, etc.).</summary>
    protected virtual void HandleBossUpdate() { }

    /// <summary>Override for physics movement.</summary>
    protected virtual void HandleBossFixedUpdate() { }

    /// <summary>
    /// Matches Kit/Malice animator parameter names so character controllers can be reused.
    /// </summary>
    protected virtual void UpdateAnimator()
    {
        if (animator == null)
            return;

        float verticalSpeed = rb != null ? rb.linearVelocity.y : 0f;
        bool moving = wantsMoveAnim && !isDashing && !isAttacking;

        animator.SetBool("IsMoving", moving);
        animator.SetBool("IsGrounded", isGrounded);
        animator.SetBool("IsInAir", !isGrounded);
        animator.SetBool("IsDashing", isDashing);
        animator.SetBool("IsShooting", false);
        animator.SetBool("IsAttacking", isAttacking);
        animator.SetBool("IsCharging", isCharging);
        animator.SetBool("AimUp", aimUp);
        animator.SetBool("IsStunned", isStunned);
        animator.SetBool("IsFalling", !isGrounded && verticalSpeed < -0.01f);
        animator.SetBool("IsJumping", !isGrounded && verticalSpeed >= -0.01f);
        animator.SetFloat("VerticalSpeed", verticalSpeed);
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

    protected void ApplyFallMultiplier()
    {
        if (rb == null || isGrounded)
            return;

        if (rb.linearVelocity.y < 0f)
            rb.linearVelocity += Vector2.up * Physics2D.gravity.y * (fallMultiplier - 1f) * Time.fixedDeltaTime;
    }

    /// <summary>
    /// Solid body colliders ignore player solid colliders (phase through).
    /// AttackHitbox triggers still overlap for attack damage.
    /// </summary>
    public void RefreshPhaseCollisionsWithPlayers()
    {
        Collider2D[] myCols = GetComponentsInChildren<Collider2D>(true);
        PlayerController[] players = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);

        for (int p = 0; p < players.Length; p++)
        {
            PlayerController player = players[p];
            if (player == null)
                continue;

            IgnoreSolidColliders(myCols, player.GetComponentsInChildren<Collider2D>(true));
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

    public void FaceToward(float worldX)
    {
        float delta = worldX - transform.position.x;
        if (Mathf.Abs(delta) < 0.05f)
            return;

        facingSign = Mathf.Sign(delta);
        ApplyFacingVisual();
    }

    public void FaceToward(Transform target)
    {
        if (target == null)
            return;
        FaceToward(target.position.x);
    }

    protected void ApplyFacingVisual()
    {
        if (spriteRenderer == null)
            return;

        if (spriteFacesLeft)
            spriteRenderer.flipX = facingSign > 0f;
        else
            spriteRenderer.flipX = facingSign < 0f;
    }

    protected void MoveHorizontal(float directionSign)
    {
        if (rb == null || isStunned || IsDead)
            return;

        float sign = Mathf.Sign(directionSign);
        if (Mathf.Abs(directionSign) > 0.01f)
        {
            facingSign = sign;
            ApplyFacingVisual();
            wantsMoveAnim = true;
        }

        rb.linearVelocity = new Vector2(sign * GetMoveSpeed(), rb.linearVelocity.y);
    }

    /// <summary>Base walk speed. Override for charge move bonuses (Boss Malice).</summary>
    protected virtual float GetMoveSpeed()
    {
        return moveSpeed + crystalMoveSpeedBonus;
    }

    /// <summary>Outgoing attack damage after crystal (and subclass) bonuses.</summary>
    protected int ApplyCrystalAttackBonus(int baseDamage)
    {
        return Mathf.Max(1, baseDamage + Mathf.Max(0, crystalAttackBonus));
    }

    public void BeginCrystalSpeedBoost(float bonus, float duration)
    {
        StartCrystalBoost(moveBonus: Mathf.Max(0f, bonus), attackBonus: 0, duration,
            new Color(0.15f, 0.55f, 1f, 1f),
            new Color(0.05f, 0.3f, 0.95f, 1f));
    }

    public void BeginCrystalAttackBoost(int bonus, float duration)
    {
        StartCrystalBoost(moveBonus: 0f, attackBonus: Mathf.Max(0, bonus), duration,
            new Color(1f, 0.45f, 0.1f, 1f),
            new Color(0.95f, 0.25f, 0.05f, 1f));
    }

    private void StartCrystalBoost(
        float moveBonus,
        int attackBonus,
        float duration,
        Color baseColor,
        Color strongColor)
    {
        if (!isActiveAndEnabled)
            return;

        if (crystalBoostRoutine != null)
            StopCoroutine(crystalBoostRoutine);

        crystalMoveSpeedBonus = moveBonus;
        crystalAttackBonus = attackBonus;
        crystalBoostAuraColor = baseColor;
        crystalBoostAuraStrongColor = strongColor;
        crystalBoostEndsAt = Time.time + Mathf.Max(0.05f, duration);
        EnsureCrystalBoostAura();
        SetCrystalBoostAuraVisible(true);
        crystalBoostRoutine = StartCoroutine(CrystalBoostTimeout());
    }

    private IEnumerator CrystalBoostTimeout()
    {
        while (Time.time < crystalBoostEndsAt)
        {
            TickCrystalBoostAura();
            yield return null;
        }

        ClearCrystalBoost();
    }

    private void ClearCrystalBoost()
    {
        crystalMoveSpeedBonus = 0f;
        crystalAttackBonus = 0;
        crystalBoostRoutine = null;
        SetCrystalBoostAuraVisible(false);
    }

    private void EnsureCrystalBoostAura()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
            return;

        if (crystalBoostAuraObject == null)
        {
            crystalBoostAuraObject = new GameObject($"{name}_CrystalBoostAura");
            crystalBoostAuraObject.transform.SetParent(transform, false);
            crystalBoostAuraObject.transform.localPosition = Vector3.zero;
            crystalBoostAuraObject.transform.localScale = Vector3.one * 1.18f;
            crystalBoostAuraObject.transform.SetAsFirstSibling();

            crystalBoostAuraRenderer = crystalBoostAuraObject.AddComponent<SpriteRenderer>();
            Shader solidShader = Shader.Find("Gameoverse/SpriteSolidColor");
            if (solidShader != null)
            {
                crystalBoostAuraMaterial = new Material(solidShader);
                crystalBoostAuraRenderer.sharedMaterial = crystalBoostAuraMaterial;
            }
        }

        if (crystalBoostAuraRenderer == null)
            crystalBoostAuraRenderer = crystalBoostAuraObject.GetComponent<SpriteRenderer>();

        crystalBoostAuraRenderer.sprite = spriteRenderer.sprite;
        CharacterEffectSorting.ApplyAuraBehindBody(crystalBoostAuraRenderer, spriteRenderer, EffectSortingGroup);
        crystalBoostAuraRenderer.sortingOrder = CharacterEffectSorting.AuraOrderInGroup;
    }

    private void TickCrystalBoostAura()
    {
        if (crystalBoostAuraObject == null || crystalBoostAuraRenderer == null || spriteRenderer == null)
            return;

        crystalBoostAuraObject.SetActive(true);
        crystalBoostAuraRenderer.sprite = spriteRenderer.sprite;
        crystalBoostAuraRenderer.flipX = spriteRenderer.flipX;
        CharacterEffectSorting.ApplyAuraBehindBody(crystalBoostAuraRenderer, spriteRenderer, EffectSortingGroup);

        crystalBoostAuraFlickerPhase += Time.deltaTime * 1.35f;
        float shimmer = 0.5f + 0.5f * Mathf.Sin(crystalBoostAuraFlickerPhase * Mathf.PI * 2f);
        Color c = Color.Lerp(crystalBoostAuraColor, crystalBoostAuraStrongColor, 0.45f + 0.55f * shimmer);
        c.a = 0.7f;
        crystalBoostAuraRenderer.color = c;

        float pulse = 1f + Mathf.Sin(Time.time * 6f) * 0.05f;
        crystalBoostAuraObject.transform.localScale = Vector3.one * (1.18f * pulse);
    }

    private void SetCrystalBoostAuraVisible(bool visible)
    {
        if (crystalBoostAuraObject == null)
            return;

        if (!visible)
        {
            crystalBoostAuraObject.SetActive(false);
            crystalBoostAuraFlickerPhase = 0f;
        }
    }

    /// <summary>
    /// Restore after a crystal-backed defeat: full HP, visible again, AI re-enabled.
    /// </summary>
    public virtual void ReviveFull(Vector3 worldPosition)
    {
        ClearCrystalBoost();
        VisualEffects.SetHostSpritesVisible(gameObject, true);

        transform.position = worldPosition;
        currentHealth = Mathf.Max(1, maxHealth);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);

        isStunned = false;
        stunTimer = 0f;
        invincibilityTimer = 0f;
        isAttacking = false;
        isDashing = false;
        isCharging = false;
        aimUp = false;
        wantsMoveAnim = false;

        if (attackHitbox != null)
            attackHitbox.Deactivate();

        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.gravityScale = defaultGravityScale;
        }

        if (animator != null)
        {
            animator.SetBool("IsStunned", false);
            animator.speed = 1f;
        }

        enabled = true;
        OnRevived();
    }

    /// <summary>Subclass hook to reset AI / combat state after a crystal respawn.</summary>
    protected virtual void OnRevived()
    {
    }

    protected void StopHorizontal()
    {
        wantsMoveAnim = false;
        if (rb == null)
            return;
        rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
    }

    protected void TryJump()
    {
        if (!isGrounded || rb == null || isStunned || IsDead)
            return;

        rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f);
        rb.AddForce(Vector2.up * jumpForce, ForceMode2D.Impulse);
    }

    public virtual void SetHealth(int value)
    {
        int clamped = Mathf.Clamp(value, 0, Mathf.Max(1, maxHealth));
        if (clamped == currentHealth)
            return;

        currentHealth = clamped;
        OnHealthChanged?.Invoke(currentHealth, maxHealth);

        if (currentHealth <= 0)
            Die();
    }

    public virtual void TakeDamage(int amount)
    {
        ApplyIncomingDamage(amount, stunOverride: null, fromBigShot: false);
    }

    /// <summary>
    /// Projectile hits can pass shot type so bosses (e.g. Boss Malice) can gate stun on Big shots.
    /// </summary>
    public virtual void TakeDamage(int amount, ProjectileShotType shotType)
    {
        ApplyIncomingDamage(amount, stunOverride: null, fromBigShot: shotType == ProjectileShotType.Big);
    }

    /// <summary>
    /// Shared damage path. Subclasses override <see cref="ShouldEnterHitStun"/> to customize stun rules.
    /// </summary>
    protected void ApplyIncomingDamage(int amount, bool? stunOverride, bool fromBigShot)
    {
        if (amount <= 0 || IsDead || invincibilityTimer > 0f)
            return;

        int healthBefore = currentHealth;
        SetHealth(currentHealth - amount);

        if (currentHealth <= 0)
            return;

        bool shouldStun = stunOverride ?? ShouldEnterHitStun(healthBefore, currentHealth, fromBigShot);
        OnDamageApplied(healthBefore, currentHealth, shouldStun, fromBigShot);
        if (shouldStun)
            BeginHitReaction();
    }

    /// <summary>
    /// Called after HP is reduced on a surviving hit. Default no-op; Boss Malice plays hit VFX even without stun.
    /// </summary>
    protected virtual void OnDamageApplied(int healthBefore, int healthAfter, bool enteredStun, bool fromBigShot)
    {
    }

    /// <summary>
    /// Default: any damaging hit stuns. Boss Malice overrides for Big-shot / 10% threshold rules.
    /// </summary>
    protected virtual bool ShouldEnterHitStun(int healthBefore, int healthAfter, bool fromBigShot)
    {
        return true;
    }

    public virtual void Heal(int amount)
    {
        if (amount <= 0 || IsDead)
            return;
        SetHealth(currentHealth + amount);
    }

    protected virtual void BeginHitReaction()
    {
        isStunned = true;
        stunTimer = Mathf.Max(0f, hitStunDuration);
        invincibilityTimer = Mathf.Max(0f, hitInvincibilityDuration);

        if (rb != null)
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);

        OnHitStunStarted();
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
    /// Extra per-frame visuals after hits (e.g. Boss Malice hit-flash VFX while not stunned).
    /// </summary>
    protected virtual void TickPostHitVisuals(float dt)
    {
    }

    protected virtual void OnHitStunStarted()
    {
        VisualEffects.PlayStunned(stunnedEffectPrefab, this);
    }

    protected virtual void OnHitStunEnded()
    {
        VisualEffects.StopStunned(this);
    }

    protected virtual void Die()
    {
        isStunned = false;
        stunTimer = 0f;
        isAttacking = false;
        VisualEffects.StopStunned(this);
        VisualEffects.PlayDeathEnergyBalls(deathEnergyBallPrefab, this, hideHost: true);

        if (attackHitbox != null)
            attackHitbox.Deactivate();

        if (rb != null)
            rb.linearVelocity = Vector2.zero;

        OnDied?.Invoke();
        OnBossDefeated();
    }

    protected virtual void OnBossDefeated()
    {
        if (animator != null)
            animator.SetBool("IsStunned", true);

        enabled = false;
    }

    protected PlayerController FindPlayer()
    {
        if (PlayerSpawner.Instance != null && PlayerSpawner.Instance.CurrentPlayer != null)
            return PlayerSpawner.Instance.CurrentPlayer;

        return FindFirstObjectByType<PlayerController>();
    }

#if UNITY_EDITOR
    protected virtual void OnValidate()
    {
        maxHealth = Mathf.Max(1, maxHealth);
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
        groundCheckRadius = Mathf.Max(0.01f, groundCheckRadius);
        moveSpeed = Mathf.Max(0f, moveSpeed);
        jumpForce = Mathf.Max(0f, jumpForce);
        hitStunDuration = Mathf.Max(0f, hitStunDuration);
        hitInvincibilityDuration = Mathf.Max(0f, hitInvincibilityDuration);
    }

    private void OnDrawGizmosSelected()
    {
        if (groundCheck == null)
            return;
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
    }
#endif
}
