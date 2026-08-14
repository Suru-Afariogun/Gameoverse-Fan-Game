using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Boss-fight crystal: shows HP as NNN%, keeps the boss respawning while HP remains,
/// and periodically grants mutually exclusive heal / blue speed / orange power boosts.
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
    [SerializeField] private int healthLostPerBossDeath = 100;
    [Tooltip("Every this much direct crystal damage also damages the boss.")]
    [SerializeField] private int directDamageChunkSize = 20;
    [Tooltip("Percent of the boss's CURRENT HP lost each chunk (30 = 30%).")]
    [SerializeField] [Range(1f, 100f)] private float linkedBossDamagePercentOfCurrent = 30f;
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
    private int directDamageAccumulator;
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
        if (boss == null)
            return;

        CrystalEffect pick = (CrystalEffect)Random.Range(0, 3);
        StartCoroutine(RunEffect(pick, boss));
    }

    /// <summary>
    /// Called by BossFightDirector when a boss dies. Drains crystal HP and reports whether
    /// the boss should respawn (crystal still has HP after the drain).
    /// </summary>
    public bool TryHandleBossDeath(Boss defeatedBoss)
    {
        ApplyBossDeathPenalty();
        return AllowsBossRespawn;
    }

    public IEnumerator RespawnBossAfterDeath(Boss defeatedBoss)
    {
        // Capture death center before anything else moves the boss.
        Vector3 deathPos = defeatedBoss != null
            ? defeatedBoss.transform.position
            : Vector3.zero;

        // Start recall immediately so balls don't fade out on the way to 9 spaces.
        DeathEnergyBallBurst burst = VisualEffects.ActiveDeathBurst;
        if (burst != null)
            burst.EnableCrystalRecall(crystalRecallOutboundDistance);

        if (respawnDelay > 0f)
            yield return new WaitForSecondsRealtime(respawnDelay);

        yield return VisualEffects.WaitForDeathBallsCrystalRecall(
            burst,
            crystalRecallOutboundDistance);

        if (burst != null)
            Destroy(burst.gameObject);

        if (!AllowsBossRespawn || defeatedBoss == null)
            yield break;

        // Reform at the kill spot — crystal pulls the boss back together there.
        defeatedBoss.ReviveFull(deathPos);
    }

    public void TakeDamage(int amount)
    {
        if (amount <= 0 || IsDead)
            return;

        int before = currentHealth;
        currentHealth = Mathf.Max(0, currentHealth - amount);
        int applied = before - currentHealth;
        RefreshHealthText();

        if (applied > 0)
            ApplyDirectDamageSideEffects(applied);
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
        nextEffectAllowedAt = Time.time + Mathf.Max(0.1f, sharedEffectCooldown);

        switch (effect)
        {
            case CrystalEffect.Heal:
                boss.Heal(healAmount);
                break;

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
