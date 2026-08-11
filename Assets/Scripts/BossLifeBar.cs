using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Boss life bar using the same per-character HP sprites as <see cref="LifeBar"/>
/// (Kit / Malice sets). Tracks the active Boss and scales 0..MaxHealth across the
/// 11 sprite slots (bosses are often 100 HP while the art is authored for 10).
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class BossLifeBar : MonoBehaviour
{
    [Header("Per-Boss Character Sprites")]
    [Tooltip("Same Kit / Malice sprite sets as the player LifeBar.")]
    [SerializeField] private LifeBar.CharacterLifeBarSet[] characterLifeBars = new LifeBar.CharacterLifeBarSet[]
    {
        new LifeBar.CharacterLifeBarSet { characterId = "Kit", lifeBarSprites = new Sprite[LifeBar.DefaultSpriteSlotCount] },
        new LifeBar.CharacterLifeBarSet { characterId = "Malice", lifeBarSprites = new Sprite[LifeBar.DefaultSpriteSlotCount] }
    };

    [Header("Display")]
    [SerializeField] private SpriteRenderer lifeBarSpriteRenderer;
    [Tooltip("If true, finds the active Boss in the scene each frame.")]
    [SerializeField] private bool autoFindActiveBoss = true;
    [SerializeField] private Boss bossOverride;
    [Tooltip("Used if the boss id cannot be resolved. Kit or Malice.")]
    [SerializeField] private string fallbackCharacterId = "Kit";

    [Header("Hit Flicker")]
    [SerializeField] private int hitFlickerCycles = 4;
    [SerializeField] private float hitFlickerHalfPeriod = 0.1f;

    [Header("Optional Max HP Override")]
    [Tooltip("If > 0, uses this instead of the boss MaxHealth when mapping sprites.")]
    [SerializeField] private int maxHealthOverride = 0;

    private string activeCharacterId = "";
    private Sprite[] activeSprites;
    private Sprite activeDamagedSprite;
    private int lastShownHealth = int.MinValue;
    private Boss boundBoss;
    private Coroutine hitFlickerRoutine;
    private bool isFlickering;

    private void Awake()
    {
        if (lifeBarSpriteRenderer == null)
            lifeBarSpriteRenderer = GetComponent<SpriteRenderer>();

        EnsureSpriteArraySizes();
        ApplyCharacterSprites(ResolveInitialCharacterId());
    }

    private void OnEnable()
    {
        BindBoss(GetTrackedBoss());
    }

    private void OnDisable()
    {
        StopHitFlicker();
        UnbindBoss(boundBoss != null ? boundBoss : GetTrackedBoss());
    }

    private void Update()
    {
        if (!autoFindActiveBoss)
            return;

        Boss boss = GetTrackedBoss();
        if (boss == null)
            return;

        string characterId = ResolveCharacterIdForBoss(boss);
        if (!string.Equals(activeCharacterId, characterId, StringComparison.OrdinalIgnoreCase))
            ApplyCharacterSprites(characterId);

        RefreshBindingIfNeeded(boss);

        if (!isFlickering)
            RefreshFromBoss(boss);
    }

    private void RefreshBindingIfNeeded(Boss boss)
    {
        if (boundBoss == boss)
            return;

        UnbindBoss(boundBoss);
        BindBoss(boss);
    }

    private void BindBoss(Boss boss)
    {
        boundBoss = boss;
        if (boundBoss == null)
            return;

        boundBoss.OnHealthChanged += HandleHealthChanged;
        ApplyCharacterSprites(ResolveCharacterIdForBoss(boundBoss));
        HandleHealthChanged(boundBoss.CurrentHealth, boundBoss.MaxHealth);
    }

    private void UnbindBoss(Boss boss)
    {
        if (boss != null)
            boss.OnHealthChanged -= HandleHealthChanged;

        if (boundBoss == boss)
            boundBoss = null;
    }

    private Boss GetTrackedBoss()
    {
        if (bossOverride != null)
            return bossOverride;

        // Prefer a living boss; otherwise any boss in the scene.
        Boss[] bosses = FindObjectsByType<Boss>(FindObjectsSortMode.None);
        Boss fallback = null;
        for (int i = 0; i < bosses.Length; i++)
        {
            Boss boss = bosses[i];
            if (boss == null)
                continue;

            fallback = boss;
            if (!boss.IsDead)
                return boss;
        }

        return fallback;
    }

    private string ResolveInitialCharacterId()
    {
        if (!string.IsNullOrWhiteSpace(BossEncounter.SelectedBossId))
            return NormalizeBossCharacterId(BossEncounter.SelectedBossId);

        return fallbackCharacterId;
    }

    private static string ResolveCharacterIdForBoss(Boss boss)
    {
        if (boss == null)
            return NormalizeBossCharacterId(BossEncounter.SelectedBossId);

        if (boss is BossKit)
            return "Kit";
        if (boss is BossMalice)
            return "Malice";

        return NormalizeBossCharacterId(boss.BossId);
    }

    private static string NormalizeBossCharacterId(string bossId)
    {
        if (string.IsNullOrWhiteSpace(bossId))
            return "Kit";

        string id = bossId.Trim();
        if (id.StartsWith("Boss", StringComparison.OrdinalIgnoreCase) && id.Length > 4)
            id = id.Substring(4);

        if (string.Equals(id, BossEncounter.BossIdKit, StringComparison.OrdinalIgnoreCase))
            return "Kit";
        if (string.Equals(id, BossEncounter.BossIdMalice, StringComparison.OrdinalIgnoreCase))
            return "Malice";

        return id;
    }

    private void HandleHealthChanged(int current, int max)
    {
        bool tookDamage = lastShownHealth != int.MinValue && current < lastShownHealth;
        SetDisplayedHealth(current, max, tookDamage);
    }

    private void RefreshFromBoss(Boss boss)
    {
        if (boss == null)
            return;

        if (boss.CurrentHealth == lastShownHealth)
            return;

        bool tookDamage = lastShownHealth != int.MinValue && boss.CurrentHealth < lastShownHealth;
        SetDisplayedHealth(boss.CurrentHealth, boss.MaxHealth, tookDamage);
    }

    public void SetDisplayedHealth(int currentHealth, int maxHealth)
    {
        SetDisplayedHealth(currentHealth, maxHealth, playHitFlicker: false);
    }

    public void SetDisplayedHealth(int currentHealth, int maxHealth, bool playHitFlicker)
    {
        lastShownHealth = currentHealth;

        if (lifeBarSpriteRenderer == null)
            return;

        Sprite currentSprite = ResolveHealthSprite(currentHealth, maxHealth);
        if (currentSprite == null)
            return;

        if (playHitFlicker && activeDamagedSprite != null && hitFlickerCycles > 0)
        {
            StartHitFlicker(activeDamagedSprite, currentSprite);
            return;
        }

        StopHitFlicker();
        lifeBarSpriteRenderer.sprite = currentSprite;
    }

    private Sprite ResolveHealthSprite(int currentHealth, int maxHealth)
    {
        if (activeSprites == null || activeSprites.Length == 0)
            return null;

        int max = maxHealthOverride > 0 ? maxHealthOverride : Mathf.Max(1, maxHealth);
        int clamped = Mathf.Clamp(currentHealth, 0, max);
        float pct = clamped / (float)max;

        // 11 sprites (0..10): drop one sprite every 10% HP lost.
        // 100% → index 10, 90–99% → 9, …, 1–9% → 0 after floor, 0% → 0.
        // With Length != 11, still uses equal percentage bands across the array.
        int topIndex = activeSprites.Length - 1;
        int spriteIndex = Mathf.FloorToInt(pct * topIndex + 0.0001f);
        spriteIndex = Mathf.Clamp(spriteIndex, 0, topIndex);
        return activeSprites[spriteIndex];
    }

    private void StartHitFlicker(Sprite damagedSprite, Sprite currentHealthSprite)
    {
        StopHitFlicker();
        hitFlickerRoutine = StartCoroutine(HitFlickerRoutine(damagedSprite, currentHealthSprite));
    }

    private void StopHitFlicker()
    {
        if (hitFlickerRoutine != null)
        {
            StopCoroutine(hitFlickerRoutine);
            hitFlickerRoutine = null;
        }

        isFlickering = false;
    }

    private IEnumerator HitFlickerRoutine(Sprite damagedSprite, Sprite currentHealthSprite)
    {
        isFlickering = true;
        float half = Mathf.Max(0.02f, hitFlickerHalfPeriod);
        int cycles = Mathf.Max(1, hitFlickerCycles);

        for (int i = 0; i < cycles; i++)
        {
            lifeBarSpriteRenderer.sprite = damagedSprite;
            yield return new WaitForSeconds(half);

            lifeBarSpriteRenderer.sprite = currentHealthSprite;
            yield return new WaitForSeconds(half);
        }

        lifeBarSpriteRenderer.sprite = currentHealthSprite;
        hitFlickerRoutine = null;
        isFlickering = false;
    }

    public void ApplyCharacterSprites(string characterId)
    {
        activeCharacterId = characterId ?? "";
        LifeBar.CharacterLifeBarSet set = FindSetForCharacter(activeCharacterId);
        if (set == null)
            set = FindSetForCharacter(fallbackCharacterId);

        activeSprites = set != null ? set.lifeBarSprites : null;
        activeDamagedSprite = set != null ? set.damagedLifeBarSprite : null;

        Boss boss = GetTrackedBoss();
        if (boss != null)
            SetDisplayedHealth(boss.CurrentHealth, boss.MaxHealth, playHitFlicker: false);
        else if (activeSprites != null && activeSprites.Length > 0)
            SetDisplayedHealth(activeSprites.Length - 1, activeSprites.Length - 1, playHitFlicker: false);
    }

    private LifeBar.CharacterLifeBarSet FindSetForCharacter(string characterId)
    {
        if (characterLifeBars == null || string.IsNullOrWhiteSpace(characterId))
            return null;

        for (int i = 0; i < characterLifeBars.Length; i++)
        {
            LifeBar.CharacterLifeBarSet set = characterLifeBars[i];
            if (set == null || string.IsNullOrWhiteSpace(set.characterId))
                continue;

            if (string.Equals(set.characterId, characterId, StringComparison.OrdinalIgnoreCase))
                return set;
        }

        return null;
    }

    private void EnsureSpriteArraySizes()
    {
        if (characterLifeBars == null)
            return;

        for (int i = 0; i < characterLifeBars.Length; i++)
        {
            if (characterLifeBars[i] == null)
                characterLifeBars[i] = new LifeBar.CharacterLifeBarSet();

            if (characterLifeBars[i].lifeBarSprites == null || characterLifeBars[i].lifeBarSprites.Length == 0)
                characterLifeBars[i].lifeBarSprites = new Sprite[LifeBar.DefaultSpriteSlotCount];
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (lifeBarSpriteRenderer == null)
            lifeBarSpriteRenderer = GetComponent<SpriteRenderer>();

        hitFlickerCycles = Mathf.Max(1, hitFlickerCycles);
        hitFlickerHalfPeriod = Mathf.Max(0.02f, hitFlickerHalfPeriod);
        EnsureSpriteArraySizes();
    }
#endif
}
