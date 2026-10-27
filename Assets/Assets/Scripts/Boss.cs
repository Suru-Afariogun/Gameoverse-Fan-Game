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
    [SerializeField] protected GameVisualEffect stunnedEffectPrefab;
    [Tooltip("Assign matching Death Energy Ball prefab (Kit balls for Boss Kit, Malice for Boss Malice).")]
    [SerializeField] protected GameVisualEffect deathEnergyBallPrefab;
    [Tooltip("Assign Healed Visual Effect prefab. Set Effect Type = Healed.")]
    [SerializeField] protected GameVisualEffect healedEffectPrefab;

    [Header("Health")]
    [SerializeField] protected int maxHealth = 100;
    [SerializeField] protected int currentHealth = 100;

    [Header("Cinematic Death Explosions")]
    [Tooltip("Cranky / Scrap / Tanker-style EnemyExplosionSmallPurple prefabs. Auto-resolved when empty.")]
    [SerializeField] protected GameVisualEffect[] cinematicDeathExplosionPrefabs;
    [Tooltip("Anchor for cinematic death explosion waves. Auto-finds Spark Spawner / Spark box when empty.")]
    [SerializeField] protected Transform sparkSpawnPoint;

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

    /// <summary>Prefab-authored max HP before difficulty scaling.</summary>
    protected int authoredMaxHealth = 100;
    /// <summary>Prefab-authored walk speed before difficulty scaling.</summary>
    protected float authoredMoveSpeed = 5f;
    /// <summary>Prefab-authored hit i-frames before difficulty scaling.</summary>
    protected float authoredHitInvincibility = 0.75f;
    private bool difficultyAuthoredCaptured;

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

    private bool combatPaused;
    private bool isCopyBot;
    private bool isMainCopyBot;
    private bool isCopyBotDecoy;
    private bool nearDeathSaveActive;
    private bool cinematicDeathActive;
    private bool enrageLatched;
    private bool signaturePhaseLatched;
    private bool desperatePhaseLatched;

    public GameVisualEffect DeathEnergyBallPrefab => deathEnergyBallPrefab;

    /// <summary>World origin for cinematic death explosion waves (Spark Spawner / Spark box when present).</summary>
    public Transform DeathExplosionAnchor => ResolveSparkSpawnPoint();

    public string BossId => bossId;
    public int MaxHealth => maxHealth;
    public int CurrentHealth => currentHealth;
    public int LifeBarCount => Mathf.Max(1, ResolveLifeBarCount());

    /// <summary>Override when the boss authors a life-bar segment count (Harlie/Bass/Count/etc.).</summary>
    protected virtual int ResolveLifeBarCount() => 10;
    public bool IsGrounded => isGrounded;
    public bool IsStunned => isStunned;
    public bool IsInvincible => invincibilityTimer > 0f || nearDeathSaveActive;
    public bool EnrageLatched => enrageLatched;
    public bool SignaturePhaseLatched => signaturePhaseLatched;
    public bool DesperatePhaseLatched => desperatePhaseLatched;
    public bool IsNearDeathSaving => nearDeathSaveActive;
    public bool IsCinematicDying => cinematicDeathActive;
    public bool HasActiveCrystalBoost =>
        crystalBoostRoutine != null &&
        (crystalMoveSpeedBonus > 0.001f || crystalAttackBonus > 0) &&
        Time.time < crystalBoostEndsAt;
    public bool CombatPaused => combatPaused;
    public bool IsCopyBot => isCopyBot;
    public bool IsMainCopyBot => isMainCopyBot;
    public bool IsCopyBotDecoy => isCopyBotDecoy;
    public bool IsDead => currentHealth <= 0 && !nearDeathSaveActive;
    public float FacingSign => facingSign;
    public Rigidbody2D Body => rb;
    public AttackHitbox AttackHitbox => attackHitbox;
    public SortingGroup EffectSortingGroup => effectSortingGroup;

    /// <summary>Latch enrage / signature / desperate so full heals cannot clear them.</summary>
    public void LatchCombatPhases(bool enraged, bool signaturePhase = false, bool desperatePhase = false)
    {
        if (enraged)
            enrageLatched = true;
        if (signaturePhase)
            signaturePhaseLatched = true;
        if (desperatePhase)
            desperatePhaseLatched = true;
    }

    /// <summary>HP write used by cinematic heal (does not trigger death).</summary>
    public void ApplyCinematicHealth(int value)
    {
        currentHealth = Mathf.Clamp(value, 0, Mathf.Max(1, maxHealth));
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }

    protected void SetBossId(string id)
    {
        if (!string.IsNullOrWhiteSpace(id))
            bossId = id;
    }

    public event Action<int, int> OnHealthChanged;
    public event Action OnDied;
    /// <summary>Any boss in the scene died (fires after the instance <see cref="OnDied"/>).</summary>
    public static event Action<Boss> AnyBossDied;

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

        CaptureAuthoredDifficultyStats();
        currentHealth = Mathf.Clamp(currentHealth, 0, Mathf.Max(1, maxHealth));
        ApplyFacingVisual();
        effectSortingGroup = CharacterEffectSorting.EnsureHostSortingGroup(this, spriteRenderer);
        DeferredEnemyPhaseRefresh.Request();

        RefreshDifficulty();
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
        ApplyBossFightSpawnFacing();
    }

    protected virtual void OnEnable()
    {
        if (attackHitbox != null)
            attackHitbox.SetOwner(this);
        DeferredEnemyPhaseRefresh.Request();
    }

    private void CaptureAuthoredDifficultyStats()
    {
        if (difficultyAuthoredCaptured)
            return;

        authoredMaxHealth = Mathf.Max(1, maxHealth);
        authoredMoveSpeed = moveSpeed;
        authoredHitInvincibility = hitInvincibilityDuration;
        difficultyAuthoredCaptured = true;
    }

    /// <summary>Re-apply Easy/Normal/Hard/Balanced stats. Safe mid-battle (preserves HP ratio).</summary>
    public virtual void RefreshDifficulty()
    {
        CaptureAuthoredDifficultyStats();

        // Copy bots keep their encounter-configured HP until next configure call.
        if (!isCopyBot)
        {
            int newMax = DifficultyTuning.ScaleBossHp(authoredMaxHealth);
            DifficultyTuning.RetuneHealth(ref currentHealth, ref maxHealth, newMax, IsDead);
            OnHealthChanged?.Invoke(currentHealth, maxHealth);
        }

        OnDifficultyRefreshed();
    }

    /// <summary>Subclass hook after HP/speed i-frame retune (tempo caches, etc.).</summary>
    protected virtual void OnDifficultyRefreshed()
    {
    }

    protected virtual void Update()
    {
        if (cinematicDeathActive || nearDeathSaveActive)
        {
            UpdateGrounded();
            ApplyCinematicStunPose();
            ClampToPlayableWorld();
            return;
        }

        if (IsDead)
            return;

        TickHitReaction(BossDeltaTime);
        UpdateGrounded();
        TickPostHitVisuals(BossDeltaTime);

        if (combatPaused)
        {
            StopHorizontal();
            UpdateAnimator();
            ClampToPlayableWorld();
            return;
        }

        if (!isStunned)
            HandleBossUpdate();

        UpdateAnimator();
        ClampToPlayableWorld();
    }

    protected virtual void FixedUpdate()
    {
        if (cinematicDeathActive)
        {
            if (rb != null && !isGrounded)
                ApplyFallMultiplier();
            ClampToPlayableWorld();
            return;
        }

        if (nearDeathSaveActive)
        {
            if (rb != null)
                rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            ClampToPlayableWorld();
            return;
        }

        if (IsDead || rb == null)
            return;

        if (combatPaused || isStunned)
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

    protected float BossDeltaTime => HyperSpeedWorldSlow.WorldDeltaTime;

    protected float BossFixedDeltaTime => HyperSpeedWorldSlow.WorldFixedDeltaTime;

    protected float BossSpeed(float speed) => HyperSpeedWorldSlow.ScaleSpeed(speed);

    /// <summary>
    /// Matches Kit/Malice animator parameter names so character controllers can be reused.
    /// </summary>
    protected virtual void UpdateAnimator()
    {
        if (animator == null)
            return;

        animator.speed = HyperSpeedWorldSlow.IsActive ? HyperSpeedWorldSlow.WorldTimeScale : 1f;

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

    protected virtual void ApplyFallMultiplier()
    {
        if (rb == null || isGrounded)
            return;

        if (rb.linearVelocity.y < 0f)
        {
            float mult = HyperSpeedWorldSlow.IsActive
                ? HyperSpeedWorldSlow.ScaleEnemyFallMultiplier(fallMultiplier)
                : fallMultiplier;
            rb.linearVelocity += Vector2.up * Physics2D.gravity.y * (mult - 1f) * Time.fixedDeltaTime;
        }
    }

    /// <summary>
    /// Solid body colliders ignore player solid colliders (phase through).
    /// AttackHitbox triggers still overlap for attack damage.
    /// </summary>
    public void RefreshPhaseCollisionsWithPlayers()
    {
        Collider2D[] myCols = GetComponentsInChildren<Collider2D>(true);
        PlayerController player = PlayerController.ResolveActive();
        if (player != null)
            IgnoreSolidColliders(myCols, player.GetComponentsInChildren<Collider2D>(true));

        CrankyClanky[] enemies = EnemyTypeCache.Crankies;
        for (int e = 0; e < enemies.Length; e++)
        {
            CrankyClanky enemy = enemies[e];
            if (enemy == null)
                continue;

            IgnoreSolidColliders(myCols, enemy.GetComponentsInChildren<Collider2D>(true));
        }
    }

    /// <summary>
    /// Copy-bot boss and decoys phase through each other so they cannot wedge or stack.
    /// </summary>
    public void RefreshPhaseCollisionsWithCopyBots()
    {
        if (!isCopyBot)
            return;

        Collider2D[] myCols = GetComponentsInChildren<Collider2D>(true);
        Boss[] bosses = EnemyTypeCache.Bosses;
        for (int i = 0; i < bosses.Length; i++)
        {
            Boss other = bosses[i];
            if (other == null || other == this || !other.isCopyBot)
                continue;

            IgnoreSolidColliders(myCols, other.GetComponentsInChildren<Collider2D>(true));
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

    public void SetFacingSign(float sign)
    {
        if (Mathf.Abs(sign) < 0.01f)
            return;

        facingSign = Mathf.Sign(sign);
        ApplyFacingVisual();
    }

    protected void ApplyBossFightSpawnFacing()
    {
        if (!BossFightDirector.IsActiveBossFightScene())
            return;

        SetFacingSign(BossFightDirector.GetDefaultBossFacingSign());
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
        float baseSpeed = DifficultyTuning.ScaleBossMoveSpeed(
            difficultyAuthoredCaptured ? authoredMoveSpeed : moveSpeed);
        return BossSpeed(baseSpeed + crystalMoveSpeedBonus);
    }

    /// <summary>Outgoing attack damage after crystal (and subclass) bonuses + difficulty.</summary>
    protected int ApplyCrystalAttackBonus(int baseDamage)
    {
        int dmg = Mathf.Max(1, baseDamage + Mathf.Max(0, crystalAttackBonus));
        return DifficultyTuning.ScaleBossDamage(dmg);
    }

    /// <summary>Copy Bot HP for this character (0 = use the encounter's default).</summary>
    public virtual int CopyBotMaxHealthOverride => 0;

    /// <summary>
    /// Level-one copy bot: set HP and mark identity without changing prefab type.
    /// </summary>
    public void ConfigureAsCopyBot(int hp)
    {
        isCopyBot = true;
        isMainCopyBot = true;
        isCopyBotDecoy = false;
        maxHealth = Mathf.Max(1, hp);
        currentHealth = maxHealth;
        OnHealthChanged?.Invoke(currentHealth, maxHealth);

        if (this is BossKit kitBoss)
            kitBoss.EnableCopyBotMode();
        else if (this is BossBass bassBoss)
            bassBoss.EnableCopyBotMode();

        RefreshPhaseCollisionsWithCopyBots();
    }

    /// <summary>
    /// Darker 1-HP copy spawned during the copy-bot fight — chase + stun only, no attacks.
    /// </summary>
    public void ConfigureAsCopyBotDecoy(int hp = 1)
    {
        isCopyBot = true;
        isMainCopyBot = false;
        isCopyBotDecoy = true;
        maxHealth = Mathf.Max(1, hp);
        currentHealth = maxHealth;
        OnHealthChanged?.Invoke(currentHealth, maxHealth);

        if (this is BossKit kitBoss)
            kitBoss.EnableCopyBotDecoyMode();
        else if (this is BossBass bassBoss)
            bassBoss.EnableCopyBotDecoyMode();

        RefreshPhaseCollisionsWithCopyBots();
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

    /// <summary>
    /// Shortens an active crystal boost. Used when the crystal takes direct damage during the boost.
    /// 1 HP of crystal damage → 1 second removed.
    /// </summary>
    public void ShortenCrystalBoost(float seconds)
    {
        if (!HasActiveCrystalBoost || seconds <= 0f)
            return;

        crystalBoostEndsAt -= seconds;
        if (Time.time < crystalBoostEndsAt)
            return;

        if (crystalBoostRoutine != null)
        {
            StopCoroutine(crystalBoostRoutine);
            crystalBoostRoutine = null;
        }

        ClearCrystalBoost();
    }

    /// <summary>
    /// HP loss from crystal being attacked (no stun / invuln). Pierces hit i-frames.
    /// Lethal amounts go through the crystal-save / cinematic-death path (never silent HP=0).
    /// </summary>
    public void ApplyLinkedCrystalDamage(int amount)
    {
        if (amount <= 0 || IsDead || nearDeathSaveActive || cinematicDeathActive)
            return;

        int healthBefore = currentHealth;
        int proposed = currentHealth - amount;
        if (proposed <= 0)
        {
            HandleLethalBlow(healthBefore, fromBigShot: false);
            return;
        }

        SetHealth(proposed);
    }

    /// <summary>
    /// Full HP + clear boosts. Used when the player loses a life mid-fight.
    /// </summary>
    public void ResetHealthAndBoostsToFull()
    {
        bool wasDead = IsDead;

        if (crystalBoostRoutine != null)
        {
            StopCoroutine(crystalBoostRoutine);
            crystalBoostRoutine = null;
        }

        ClearCrystalBoost();
        currentHealth = Mathf.Max(1, maxHealth);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
        isStunned = false;
        stunTimer = 0f;
        invincibilityTimer = 0f;
        VisualEffects.StopStunned(this);
        VisualEffects.SetHostSpritesVisible(gameObject, true);
        enabled = true;

        if (wasDead)
            OnRevived();
    }

    /// <summary>
    /// Stops attacking / AI while the player life-loss fade runs.
    /// </summary>
    public void SetCombatPaused(bool paused)
    {
        combatPaused = paused;
        if (!paused)
            return;

        OnCombatPaused();
    }

    protected virtual void OnCombatPaused()
    {
        isAttacking = false;
        isCharging = false;
        isDashing = false;
        aimUp = false;
        wantsMoveAnim = false;
        if (attackHitbox != null)
            attackHitbox.Deactivate();
        StopHorizontal();
        if (rb != null)
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
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
        crystalBoostEndsAt = 0f;
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

        crystalBoostAuraFlickerPhase += BossDeltaTime * 1.35f;
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
            rb.position = worldPosition;
            rb.linearVelocity = Vector2.zero;
            rb.gravityScale = defaultGravityScale;
        }

        if (animator != null)
        {
            animator.SetBool("IsStunned", false);
            animator.speed = 1f;
        }

        enabled = true;
        combatPaused = false;
        ApplyBossFightSpawnFacing();
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
        // Lethal hits must go through the crystal-save / cinematic-death pipeline — never silent HP=0.
        int clamped = Mathf.Clamp(value, 0, Mathf.Max(1, maxHealth));
        if (clamped <= 0 && !nearDeathSaveActive && !cinematicDeathActive)
        {
            if (currentHealth > 0)
                HandleLethalBlow(currentHealth, fromBigShot: false);
            return;
        }

        if (clamped == currentHealth)
            return;

        currentHealth = clamped;
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }

    /// <summary>
    /// Force a health UI refresh even if the value did not change (e.g. after bind).
    /// </summary>
    public void NotifyHealthChanged()
    {
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
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
    /// Lethal hits are intercepted for crystal near-death saves or the cinematic defeat sequence.
    /// </summary>
    protected void ApplyIncomingDamage(int amount, bool? stunOverride, bool fromBigShot)
    {
        if (amount <= 0 || IsDead || invincibilityTimer > 0f || nearDeathSaveActive || cinematicDeathActive)
            return;

        int healthBefore = currentHealth;
        int proposed = currentHealth - amount;

        if (proposed <= 0)
        {
            // Do not chip the crystal here — NotifyBossDamageDealt before the save check could
            // empty the crystal and falsely deny a near-death heal. The 250 defeat tax covers it.
            if (healthBefore > 0)
                SoundManager.Instance?.PlayBossHit();

            HandleLethalBlow(healthBefore, fromBigShot);
            return;
        }

        SetHealth(proposed);

        int dealt = healthBefore - currentHealth;
        if (dealt > 0 && Crystal.Instance != null)
            Crystal.Instance.NotifyBossDamageDealt(dealt);

        if (dealt > 0)
            SoundManager.Instance?.PlayBossHit();

        bool shouldStun = stunOverride ?? ShouldEnterHitStun(healthBefore, currentHealth, fromBigShot);
        OnDamageApplied(healthBefore, currentHealth, shouldStun, fromBigShot);
        if (shouldStun)
            BeginHitReaction();
    }

    /// <summary>
    /// Crystal still alive (any HP) → cinematic full heal (pays the 250 defeat tax).
    /// Otherwise → Mega Man 11-style defeat (Copy Bots always take this path).
    /// </summary>
    private void HandleLethalBlow(int healthBefore, bool fromBigShot)
    {
        if (nearDeathSaveActive || cinematicDeathActive)
            return;

        OnNearDeathPhasesSnapshot();

        bool crystalCanSave = !isCopyBot
                              && Crystal.Instance != null
                              && Crystal.Instance.TryPreventBossDeath(this);

        if (crystalCanSave)
        {
            // Latch immediately so a same-frame follow-up hit cannot skip the save / zero HP.
            nearDeathSaveActive = true;
            currentHealth = Mathf.Max(1, healthBefore > 0 ? healthBefore : 1);
            OnHealthChanged?.Invoke(currentHealth, maxHealth);
            StartCoroutine(NearDeathSaveRoutine());
            return;
        }

        cinematicDeathActive = true;
        currentHealth = 0;
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
        StartCoroutine(CinematicDeathRoutine());
    }

    /// <summary>Subclass snapshots enrage / signature / desperate so they latch through the heal.</summary>
    protected virtual void OnNearDeathPhasesSnapshot()
    {
    }

    private IEnumerator NearDeathSaveRoutine()
    {
        nearDeathSaveActive = true;
        SetCombatPaused(true);
        isStunned = true;
        stunTimer = 999f;
        invincibilityTimer = 999f;
        isAttacking = false;
        isDashing = false;
        isCharging = false;

        if (attackHitbox != null)
            attackHitbox.Deactivate();
        if (rb != null)
            rb.linearVelocity = Vector2.zero;
        if (animator != null)
            animator.speed = 1f;
        ApplyCinematicStunPose();

        VisualEffects.PlayStunned(stunnedEffectPrefab, this);
        OnNearDeathSaveStarted();

        try
        {
            yield return BossCinematics.NearDeathFullHeal(this, LifeBarCount);
        }
        finally
        {
            VisualEffects.StopStunned(this);
            isStunned = false;
            stunTimer = 0f;
            invincibilityTimer = 0.35f;
            combatPaused = false;
            nearDeathSaveActive = false;
            ClearCinematicStunPose();
        }

        OnNearDeathSaveFinished();
        OnRevived();
    }

    protected virtual void OnNearDeathSaveStarted()
    {
    }

    protected virtual void OnNearDeathSaveFinished()
    {
    }

    private IEnumerator CinematicDeathRoutine()
    {
        cinematicDeathActive = true;
        nearDeathSaveActive = false;
        SetCombatPaused(true);
        isStunned = true;
        stunTimer = 999f;
        invincibilityTimer = 999f;
        isAttacking = false;
        isDashing = false;
        isCharging = false;

        if (attackHitbox != null)
            attackHitbox.Deactivate();
        if (rb != null)
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
        if (animator != null)
            animator.speed = 1f;
        ApplyCinematicStunPose();

        VisualEffects.PlayStunned(stunnedEffectPrefab, this);
        OnCinematicDeathStarted();

        GameVisualEffect[] fx = ResolveCinematicDeathExplosions();
        yield return BossCinematics.MegaManStyleDeath(this, fx);

        VisualEffects.StopStunned(this);
        OnDied?.Invoke();
        AnyBossDied?.Invoke(this);
        OnBossDefeated();
        cinematicDeathActive = false;
    }

    /// <summary>
    /// Fires the moment defeat cinema begins: clear afterimages, bullets, screen FX, backup weapons.
    /// Subclasses should call base and then wipe their own residue.
    /// </summary>
    protected virtual void OnCinematicDeathStarted()
    {
        ClearSharedDefeatResidue();
    }

    /// <summary>Shared wipe: world-slow layers owned by this boss, all owned projectiles / slash waves / bombs.</summary>
    protected void ClearSharedDefeatResidue()
    {
        HyperSpeedWorldSlow.End(this);
        Projectile.DestroyOwnedBy(transform);
        MaliceSlashProjectile.DestroyOwnedBy(transform);
        BassCrashBomb.DestroyOwnedBy(transform);

        HexSparkEmitter emitter = GetComponent<HexSparkEmitter>();
        if (emitter != null)
            emitter.StopImmediate();
    }

    /// <summary>Default: IsStunned. Harlie/Hex override to Jump (no stun clip).</summary>
    protected virtual void ApplyCinematicStunPose()
    {
        if (animator == null)
            return;
        animator.SetBool("IsStunned", true);
    }

    protected virtual void ClearCinematicStunPose()
    {
        if (animator == null)
            return;
        animator.SetBool("IsStunned", false);
    }

    private GameVisualEffect[] ResolveCinematicDeathExplosions()
    {
        if (cinematicDeathExplosionPrefabs != null && cinematicDeathExplosionPrefabs.Length > 0)
        {
            int valid = 0;
            for (int i = 0; i < cinematicDeathExplosionPrefabs.Length; i++)
            {
                if (cinematicDeathExplosionPrefabs[i] != null)
                    valid++;
            }

            if (valid > 0)
                return cinematicDeathExplosionPrefabs;
        }

        GameVisualEffect[] resolved = VisualEffects.ResolveEnemyExplosionSmallPurplePrefabs();
        if (resolved != null && resolved.Length > 0)
        {
            cinematicDeathExplosionPrefabs = resolved;
            return resolved;
        }

        return System.Array.Empty<GameVisualEffect>();
    }

    /// <summary>
    /// Prefers the authored Spark Spawner / Spark box so death explosions erupt from the spark origin.
    /// </summary>
    public Transform ResolveSparkSpawnPoint()
    {
        if (sparkSpawnPoint != null)
            return sparkSpawnPoint;

        HexSparkEmitter emitter = GetComponent<HexSparkEmitter>();
        if (emitter != null && emitter.SparkBox != null)
        {
            sparkSpawnPoint = emitter.SparkBox;
            return sparkSpawnPoint;
        }

        sparkSpawnPoint = FindChildNamedTrimmed(transform, "Spark Spawner");
        if (sparkSpawnPoint == null)
            sparkSpawnPoint = FindChildNamedTrimmed(transform, "Spark box");
        if (sparkSpawnPoint == null)
            sparkSpawnPoint = FindChildNamedTrimmed(transform, "Spark Spawn Box");

        return sparkSpawnPoint != null ? sparkSpawnPoint : transform;
    }

    private static Transform FindChildNamedTrimmed(Transform root, string targetName)
    {
        if (root == null || string.IsNullOrEmpty(targetName))
            return null;

        string want = targetName.Trim();
        Transform direct = root.Find(targetName);
        if (direct != null)
            return direct;
        direct = root.Find(targetName + " ");
        if (direct != null)
            return direct;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child == null)
                continue;
            if (child.name != null && child.name.Trim() == want)
                return child;

            Transform nested = FindChildNamedTrimmed(child, targetName);
            if (nested != null)
                return nested;
        }

        return null;
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

        int before = currentHealth;
        SetHealth(currentHealth + amount);
        if (currentHealth > before)
            VisualEffects.PlayHealed(healedEffectPrefab, this);
    }

    protected virtual void BeginHitReaction()
    {
        isStunned = true;
        stunTimer = Mathf.Max(0f, hitStunDuration);
        float authoredIframes = difficultyAuthoredCaptured ? authoredHitInvincibility : hitInvincibilityDuration;
        invincibilityTimer = Mathf.Max(0f, DifficultyTuning.BossHitIFrames(authoredIframes));

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

    /// <summary>
    /// Legacy entry: routes through the cinematic defeat sequence (no crystal save).
    /// Prefer lethal damage via <see cref="ApplyIncomingDamage"/>.
    /// </summary>
    protected virtual void Die()
    {
        if (cinematicDeathActive || nearDeathSaveActive)
            return;

        cinematicDeathActive = true;
        currentHealth = 0;
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
        StartCoroutine(CinematicDeathRoutine());
    }

    protected virtual void OnBossDefeated()
    {
        if (animator != null)
            animator.SetBool("IsStunned", true);

        enabled = false;
    }

    protected PlayerController FindPlayer()
    {
        return PlayerController.ResolveActive();
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
