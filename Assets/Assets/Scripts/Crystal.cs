using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Boss-fight crystal: shows HP as NNN%, prevents lethal boss blows while HP remains
/// (full cinematic heal instead of death/respawn), and periodically grants mutually exclusive
/// heal / blue speed / orange power boosts.
/// Boosts telegraph on the crystal with a matching-color aura 0.5s before they apply.
/// Phases through everything (trigger collider) but still takes overlap damage from
/// player melee and shots.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class Crystal : MonoBehaviour, IDamageable
{
    public static Crystal Instance { get; private set; }

    private enum CrystalEffect
    {
        Heal = 0,
        BlueSpeed = 1,
        OrangePower = 2
    }

    [Header("Health")]
    [SerializeField] private int maxHealth = 1000;
    [SerializeField] private int currentHealth = 1000;
    [SerializeField] private int healthLostPerBossDeath = 250;
    [Tooltip("Every this much direct crystal damage also damages the boss.")]
    [SerializeField] private int directDamageChunkSize = 20;
    [Tooltip("Percent of the boss's CURRENT HP lost each chunk (30 = 30%).")]
    [SerializeField] [Range(1f, 100f)] private float linkedBossDamagePercentOfCurrent = 30f;
    [Tooltip("Every this much damage dealt to any boss applies crystalIndirectDamagePerChunk to the crystal.")]
    [SerializeField] private int bossDamageChunkSize = 20;
    [Tooltip("Crystal HP lost (indirect — does not trigger crystal→boss linked damage) per boss-damage chunk.")]
    [SerializeField] private int crystalIndirectDamagePerChunk = 60;
    [SerializeField] private TextMeshProUGUI healthText;

    [Header("Boss Support Effects (one at a time)")]
    [SerializeField] private float sharedEffectCooldown = 10f;
    [SerializeField] private float boostDuration = 5f;
    [SerializeField] private float boostWarningSeconds = 0.5f;
    [SerializeField] private int healAmount = 30;
    [SerializeField] private float blueSpeedBonus = 4f;
    [SerializeField] private int orangeAttackBonus = 2;
    [Tooltip("How often the crystal rolls for a random effect while idle.")]
    [SerializeField] private float effectRollInterval = 2.5f;
    [SerializeField] [Range(0f, 1f)] private float effectRollChance = 0.4f;

    [Header("Warning Aura (matches boss boost colors)")]
    [SerializeField] private float auraBaseScale = 1.18f;
    [SerializeField] private float auraPulseAmount = 0.05f;
    [SerializeField] private float auraPulseSpeed = 6f;
    [SerializeField] [Range(0f, 1f)] private float auraAlpha = 0.7f;
    [SerializeField] private Color blueAuraColor = new Color(0.15f, 0.55f, 1f, 1f);
    [SerializeField] private Color blueAuraStrongColor = new Color(0.05f, 0.3f, 0.95f, 1f);
    [SerializeField] private Color orangeAuraColor = new Color(1f, 0.45f, 0.1f, 1f);
    [SerializeField] private Color orangeAuraStrongColor = new Color(0.95f, 0.25f, 0.05f, 1f);

    [Header("Boss Respawn")]
    [Tooltip("Legacy delay before recall starts. Keep small — the ball flight is the real wait.")]
    [SerializeField] private float respawnDelay = 0f;
    [Tooltip("How far death balls fly out before reversing home for a crystal respawn.")]
    [SerializeField] private float crystalRecallOutboundDistance = 9f;
    [SerializeField] private Transform bossRespawnPoint;

    public int MaxHealth => maxHealth;
    public int CurrentHealth => currentHealth;
    public bool IsDead => currentHealth <= 0;
    public bool AllowsBossRespawn => currentHealth > 0;

    private float nextEffectAllowedAt;
    private float nextEffectRollAt;
    private bool effectRunning;
    private float hitInvincibilityTimer;
    private int directDamageAccumulator;
    private int bossDamageAccumulator;
    private Collider2D bodyCollider;
    private SpriteRenderer spriteRenderer;

    private GameObject warningAuraObject;
    private SpriteRenderer warningAuraRenderer;
    private Material warningAuraMaterial;
    private float warningAuraFlickerPhase;
    private Color warningAuraBase = Color.white;
    private Color warningAuraStrong = Color.white;
    private bool warningAuraActive;

    private void Awake()
    {
        Instance = this;
        currentHealth = Mathf.Clamp(currentHealth, 0, Mathf.Max(1, maxHealth));
        spriteRenderer = GetComponent<SpriteRenderer>();
        EnsurePhaseThroughCollider();
        ResolveHealthText();
        RefreshHealthText();
        SetupWarningAura();
    }

    private void OnEnable()
    {
        Instance = this;
        nextEffectAllowedAt = Time.time + 1f;
        nextEffectRollAt = Time.time + effectRollInterval;
    }

    private void OnDisable()
    {
        SetWarningAuraVisible(false);
        if (Instance == this)
            Instance = null;
    }

    private void OnDestroy()
    {
        if (warningAuraObject != null)
            Destroy(warningAuraObject);
        if (warningAuraMaterial != null)
            Destroy(warningAuraMaterial);

        if (Instance == this)
            Instance = null;
    }

    private void Update()
    {
        if (hitInvincibilityTimer > 0f)
            hitInvincibilityTimer = Mathf.Max(0f, hitInvincibilityTimer - Time.deltaTime);

        if (warningAuraActive)
            TickWarningAura();

        if (IsDead || effectRunning || Time.time < nextEffectAllowedAt)
            return;

        if (Time.time < nextEffectRollAt)
            return;

        nextEffectRollAt = Time.time + Mathf.Max(0.25f, effectRollInterval);
        if (Random.value > effectRollChance)
            return;

        Boss boss = FindLivingBoss();
        if (boss == null || boss.IsNearDeathSaving || boss.IsCinematicDying)
            return;

        CrystalEffect pick = (CrystalEffect)Random.Range(0, 3);
        // Heal is rarer: never while boss is above 50% HP, and uses a 3× cooldown.
        if (pick == CrystalEffect.Heal)
        {
            float ratio = boss.MaxHealth > 0 ? boss.CurrentHealth / (float)boss.MaxHealth : 1f;
            if (ratio > 0.5f)
                pick = Random.value < 0.5f ? CrystalEffect.BlueSpeed : CrystalEffect.OrangePower;
        }

        StartCoroutine(RunEffect(pick, boss));
    }

    /// <summary>
    /// Called when a boss would have died. Returns true if the crystal still had HP before the
    /// defeat tax (cinematic full heal). The 250 drain may empty the crystal afterward — that
    /// still counts as a successful save. Copy Bots never use this path.
    /// </summary>
    public bool TryPreventBossDeath(Boss boss)
    {
        if (boss == null || boss.IsCopyBot || IsDead)
            return false;

        // Grant the save based on HP before the tax. Paying 250 may zero the crystal.
        ApplyBossDeathPenalty();
        return true;
    }

    /// <summary>
    /// Legacy name used by BossFightDirector. Always false — near-death saves are handled inside
    /// <see cref="Boss"/> before OnDied, and final defeats do not respawn.
    /// </summary>
    public bool TryHandleBossDeath(Boss defeatedBoss) => false;

    /// <summary>Legacy respawn path — kept for safety; near-death heals replaced ball-recall respawns.</summary>
    public IEnumerator RespawnBossAfterDeath(Boss defeatedBoss)
    {
        yield break;
    }

    public void TakeDamage(int amount)
    {
        if (amount <= 0 || IsDead || hitInvincibilityTimer > 0f)
            return;

        int before = currentHealth;
        currentHealth = Mathf.Max(0, currentHealth - amount);
        int applied = before - currentHealth;
        RefreshHealthText();

        if (applied > 0)
        {
            hitInvincibilityTimer = Mathf.Max(hitInvincibilityTimer, DifficultyTuning.CrystalHitIFrames());
            if (!IsDead)
                SoundManager.Instance?.PlayCrystalHit();
            ApplyDirectDamageSideEffects(applied);
        }
    }

    /// <summary>
    /// HP loss from boss damage chunks. Does not shorten boosts or feed crystal→boss linked damage.
    /// </summary>
    public void ApplyIndirectDamageFromBoss(int amount)
    {
        if (amount <= 0 || IsDead)
            return;

        currentHealth = Mathf.Max(0, currentHealth - amount);
        RefreshHealthText();
    }

    /// <summary>
    /// Called when a boss takes real combat damage. Every <see cref="bossDamageChunkSize"/> HP
    /// deals <see cref="crystalIndirectDamagePerChunk"/> to the crystal.
    /// </summary>
    public void NotifyBossDamageDealt(int appliedToBoss)
    {
        if (appliedToBoss <= 0 || IsDead)
            return;

        int chunk = Mathf.Max(1, bossDamageChunkSize);
        int crystalHit = Mathf.Max(0, crystalIndirectDamagePerChunk);
        if (crystalHit <= 0)
            return;

        bossDamageAccumulator += appliedToBoss;
        while (bossDamageAccumulator >= chunk)
        {
            bossDamageAccumulator -= chunk;
            if (IsDead)
                break;

            ApplyIndirectDamageFromBoss(crystalHit);
        }
    }

    /// <summary>
    /// Full crystal HP + stop active telegraphs/effects. Used when the player loses a life.
    /// </summary>
    public void ResetToFull()
    {
        StopAllCoroutines();
        effectRunning = false;
        SetWarningAuraVisible(false);
        currentHealth = Mathf.Max(1, maxHealth);
        directDamageAccumulator = 0;
        bossDamageAccumulator = 0;
        nextEffectAllowedAt = Time.time + 1f;
        nextEffectRollAt = Time.time + Mathf.Max(0.25f, effectRollInterval);
        RefreshHealthText();
    }

    private void ApplyDirectDamageSideEffects(int applied)
    {
        Boss boss = FindLivingBoss();

        // While the boss is mid-boost, each 1 crystal HP removes 1 second of boost time.
        if (boss != null && boss.HasActiveCrystalBoost)
            boss.ShortenCrystalBoost(applied);

        // Every accumulative 20 direct crystal HP → 30% of the boss's current HP.
        int chunk = Mathf.Max(1, directDamageChunkSize);
        float pct = Mathf.Clamp(linkedBossDamagePercentOfCurrent, 1f, 100f) / 100f;
        directDamageAccumulator += applied;
        while (directDamageAccumulator >= chunk)
        {
            directDamageAccumulator -= chunk;
            Boss living = FindLivingBoss();
            if (living == null || living.IsDead)
                break;

            int linked = Mathf.Max(1, Mathf.CeilToInt(living.CurrentHealth * pct));
            living.ApplyLinkedCrystalDamage(linked);
        }
    }

    private void ApplyBossDeathPenalty()
    {
        if (IsDead)
            return;

        currentHealth = Mathf.Max(0, currentHealth - Mathf.Max(0, healthLostPerBossDeath));
        RefreshHealthText();
    }

    private IEnumerator RunEffect(CrystalEffect effect, Boss boss)
    {
        if (boss == null || boss.IsDead || effectRunning)
            yield break;

        effectRunning = true;
        float cooldown = Mathf.Max(0.1f, sharedEffectCooldown);
        if (effect == CrystalEffect.Heal)
            cooldown *= 3f;
        nextEffectAllowedAt = Time.time + cooldown;

        switch (effect)
        {
            case CrystalEffect.Heal:
            {
                float ratio = boss.MaxHealth > 0 ? boss.CurrentHealth / (float)boss.MaxHealth : 1f;
                if (ratio <= 0.5f + 0.0001f)
                    boss.Heal(healAmount);
                break;
            }

            case CrystalEffect.BlueSpeed:
                yield return TelegraphThenBoost(
                    boss,
                    blueAuraColor,
                    blueAuraStrongColor,
                    () => boss.BeginCrystalSpeedBoost(blueSpeedBonus, boostDuration));
                break;

            case CrystalEffect.OrangePower:
                yield return TelegraphThenBoost(
                    boss,
                    orangeAuraColor,
                    orangeAuraStrongColor,
                    () => boss.BeginCrystalAttackBoost(orangeAttackBonus, boostDuration));
                break;
        }

        SetWarningAuraVisible(false);
        effectRunning = false;
        nextEffectRollAt = Time.time + Mathf.Max(0.25f, effectRollInterval);
    }

    private IEnumerator TelegraphThenBoost(
        Boss boss,
        Color baseColor,
        Color strongColor,
        System.Action applyBoost)
    {
        if (boss == null || boss.IsDead)
            yield break;

        BeginWarningAura(baseColor, strongColor);

        float warn = Mathf.Max(0f, boostWarningSeconds);
        float elapsed = 0f;
        while (elapsed < warn)
        {
            if (boss == null || boss.IsDead)
            {
                SetWarningAuraVisible(false);
                yield break;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        // Warning done — hide crystal telegraph, then hand the matching aura to the boss.
        SetWarningAuraVisible(false);

        if (boss == null || boss.IsDead)
            yield break;

        applyBoost?.Invoke();

        // Wait until the boss boost actually ends (may be shortened by crystal damage).
        while (boss != null && !boss.IsDead && boss.HasActiveCrystalBoost)
            yield return null;
    }

    private void SetupWarningAura()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
            return;

        if (warningAuraObject != null)
            Destroy(warningAuraObject);
        if (warningAuraMaterial != null)
            Destroy(warningAuraMaterial);

        warningAuraObject = new GameObject($"{name}_WarningAura");
        warningAuraObject.transform.SetParent(transform, false);
        warningAuraObject.transform.localPosition = Vector3.zero;
        warningAuraObject.transform.localScale = Vector3.one * auraBaseScale;
        warningAuraObject.transform.SetAsFirstSibling();

        warningAuraRenderer = warningAuraObject.AddComponent<SpriteRenderer>();
        warningAuraRenderer.sprite = spriteRenderer.sprite;
        warningAuraRenderer.flipX = spriteRenderer.flipX;
        warningAuraRenderer.sortingLayerID = spriteRenderer.sortingLayerID;
        warningAuraRenderer.sortingOrder = spriteRenderer.sortingOrder - 1;

        Shader solidShader = Shader.Find("Gameoverse/SpriteSolidColor");
        if (solidShader != null)
        {
            warningAuraMaterial = new Material(solidShader);
            warningAuraRenderer.sharedMaterial = warningAuraMaterial;
        }

        Color c = blueAuraColor;
        c.a = 0f;
        warningAuraRenderer.color = c;
        warningAuraObject.SetActive(false);
    }

    private void BeginWarningAura(Color baseColor, Color strongColor)
    {
        if (warningAuraRenderer == null || warningAuraObject == null)
            SetupWarningAura();

        warningAuraBase = baseColor;
        warningAuraStrong = strongColor;
        warningAuraFlickerPhase = 0f;
        warningAuraActive = true;
        SetWarningAuraVisible(true);
        TickWarningAura();
    }

    private void TickWarningAura()
    {
        if (!warningAuraActive || warningAuraRenderer == null || warningAuraObject == null)
            return;

        if (spriteRenderer == null)
            return;

        warningAuraObject.SetActive(true);
        warningAuraRenderer.sprite = spriteRenderer.sprite;
        warningAuraRenderer.flipX = spriteRenderer.flipX;
        warningAuraRenderer.sortingLayerID = spriteRenderer.sortingLayerID;
        warningAuraRenderer.sortingOrder = spriteRenderer.sortingOrder - 1;

        warningAuraFlickerPhase += Time.deltaTime * 1.5f;
        float shimmer = 0.5f + 0.5f * Mathf.Sin(warningAuraFlickerPhase * Mathf.PI * 2f);
        Color c = Color.Lerp(warningAuraBase, warningAuraStrong, 0.45f + 0.55f * shimmer);
        c.a = auraAlpha;
        warningAuraRenderer.color = c;

        float pulse = 1f + Mathf.Sin(Time.time * auraPulseSpeed) * auraPulseAmount;
        warningAuraObject.transform.localScale = Vector3.one * (auraBaseScale * pulse);
    }

    private void SetWarningAuraVisible(bool visible)
    {
        warningAuraActive = visible;
        if (warningAuraObject == null)
            return;

        if (!visible)
        {
            warningAuraObject.SetActive(false);
            warningAuraFlickerPhase = 0f;
        }
    }

    private void EnsurePhaseThroughCollider()
    {
        bodyCollider = GetComponent<Collider2D>();
        if (bodyCollider == null)
            bodyCollider = gameObject.AddComponent<BoxCollider2D>();

        bodyCollider.isTrigger = true;

        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
        }

        rb.simulated = true;
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.useFullKinematicContacts = false;
    }

    private void ResolveHealthText()
    {
        if (healthText != null)
            return;

        healthText = GetComponentInChildren<TextMeshProUGUI>(true);
    }

    private void RefreshHealthText()
    {
        if (healthText == null)
            ResolveHealthText();

        if (healthText != null)
            healthText.text = $"{Mathf.Max(0, currentHealth)}%";
    }

    private static Boss FindLivingBoss()
    {
        Boss[] bosses = FindObjectsByType<Boss>(FindObjectsSortMode.None);
        for (int i = 0; i < bosses.Length; i++)
        {
            if (bosses[i] != null && !bosses[i].IsDead)
                return bosses[i];
        }

        return null;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        maxHealth = Mathf.Max(1, maxHealth);
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
        healthLostPerBossDeath = Mathf.Max(0, healthLostPerBossDeath);
        directDamageChunkSize = Mathf.Max(1, directDamageChunkSize);
        linkedBossDamagePercentOfCurrent = Mathf.Clamp(linkedBossDamagePercentOfCurrent, 1f, 100f);
        bossDamageChunkSize = Mathf.Max(1, bossDamageChunkSize);
        crystalIndirectDamagePerChunk = Mathf.Max(0, crystalIndirectDamagePerChunk);
        sharedEffectCooldown = Mathf.Max(0.1f, sharedEffectCooldown);
        boostDuration = Mathf.Max(0.1f, boostDuration);
        boostWarningSeconds = Mathf.Max(0f, boostWarningSeconds);
        healAmount = Mathf.Max(0, healAmount);
        blueSpeedBonus = Mathf.Max(0f, blueSpeedBonus);
        orangeAttackBonus = Mathf.Max(0, orangeAttackBonus);
        effectRollInterval = Mathf.Max(0.25f, effectRollInterval);
        auraBaseScale = Mathf.Max(0.1f, auraBaseScale);
        respawnDelay = Mathf.Max(0f, respawnDelay);
        crystalRecallOutboundDistance = Mathf.Max(0.1f, crystalRecallOutboundDistance);

        Collider2D col = GetComponent<Collider2D>();
        if (col != null)
            col.isTrigger = true;
    }
#endif
}
