using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Boss-fight crystal: shows HP as NNN%, keeps the boss respawning while HP remains,
/// and periodically grants mutually exclusive heal / blue speed / orange power boosts.
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
    [SerializeField] private int healthLostPerBossDeath = 50;
    [SerializeField] private TextMeshProUGUI healthText;

    [Header("Boss Support Effects (one at a time)")]
    [SerializeField] private float sharedEffectCooldown = 10f;
    [SerializeField] private float boostDuration = 5f;
    [SerializeField] private int healAmount = 30;
    [SerializeField] private float blueSpeedBonus = 4f;
    [SerializeField] private int orangeAttackBonus = 2;
    [Tooltip("How often the crystal rolls for a random effect while idle.")]
    [SerializeField] private float effectRollInterval = 2.5f;
    [SerializeField] [Range(0f, 1f)] private float effectRollChance = 0.4f;

    [Header("Boss Respawn")]
    [SerializeField] private float respawnDelay = 0.75f;
    [SerializeField] private Transform bossRespawnPoint;

    public int MaxHealth => maxHealth;
    public int CurrentHealth => currentHealth;
    public bool IsDead => currentHealth <= 0;
    public bool AllowsBossRespawn => currentHealth > 0;

    private float nextEffectAllowedAt;
    private float nextEffectRollAt;
    private bool effectRunning;
    private Collider2D bodyCollider;

    private void Awake()
    {
        Instance = this;
        currentHealth = Mathf.Clamp(currentHealth, 0, Mathf.Max(1, maxHealth));
        EnsurePhaseThroughCollider();
        ResolveHealthText();
        RefreshHealthText();
    }

    private void OnEnable()
    {
        Instance = this;
        nextEffectAllowedAt = Time.time + 1f;
        nextEffectRollAt = Time.time + effectRollInterval;
    }

    private void OnDisable()
    {
        if (Instance == this)
            Instance = null;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Update()
    {
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
        if (respawnDelay > 0f)
            yield return new WaitForSecondsRealtime(respawnDelay);

        yield return VisualEffects.WaitForDeathBallsTravel(VisualEffects.ActiveDeathBurst);

        if (!AllowsBossRespawn || defeatedBoss == null)
            yield break;

        Vector3 pos = defeatedBoss.transform.position;
        if (bossRespawnPoint != null)
            pos = bossRespawnPoint.position;
        else
        {
            BossSpawner spawner = FindFirstObjectByType<BossSpawner>();
            if (spawner != null)
            {
                // Prefer an explicit spawn point child if present on the spawner.
                Transform t = spawner.transform.Find("SpawnPoint");
                if (t != null)
                    pos = t.position;
            }
        }

        defeatedBoss.ReviveFull(pos);
    }

    public void TakeDamage(int amount)
    {
        if (amount <= 0 || IsDead)
            return;

        currentHealth = Mathf.Max(0, currentHealth - amount);
        RefreshHealthText();
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
                boss.BeginCrystalSpeedBoost(blueSpeedBonus, boostDuration);
                yield return new WaitForSeconds(boostDuration);
                break;

            case CrystalEffect.OrangePower:
                boss.BeginCrystalAttackBoost(orangeAttackBonus, boostDuration);
                yield return new WaitForSeconds(boostDuration);
                break;
        }

        effectRunning = false;
        nextEffectRollAt = Time.time + Mathf.Max(0.25f, effectRollInterval);
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
        sharedEffectCooldown = Mathf.Max(0.1f, sharedEffectCooldown);
        boostDuration = Mathf.Max(0.1f, boostDuration);
        healAmount = Mathf.Max(0, healAmount);
        blueSpeedBonus = Mathf.Max(0f, blueSpeedBonus);
        orangeAttackBonus = Mathf.Max(0, orangeAttackBonus);
        effectRollInterval = Mathf.Max(0.25f, effectRollInterval);

        Collider2D col = GetComponent<Collider2D>();
        if (col != null)
            col.isTrigger = true;
    }
#endif
}
