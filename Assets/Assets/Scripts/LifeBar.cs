using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Multi-character life bar using SpriteRenderer sprites (not UI Images).
/// Each character has 11 HP sprites (0..10) plus one damaged-hit flash sprite.
/// On damage, flickers damaged ↔ current HP sprite, then settles on current HP.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class LifeBar : MonoBehaviour
{
    public const int DefaultSpriteSlotCount = 11; // 0..10 HP

    [Serializable]
    public class CharacterLifeBarSet
    {
        [Tooltip("Must match PlayerController Character Id (e.g. Kit).")]
        public string characterId = "Kit";

        [Tooltip("11 sprites: index 0 = 0 HP, index 10 = full (10 HP).")]
        public Sprite[] lifeBarSprites = new Sprite[DefaultSpriteSlotCount];

        [Tooltip("Shown when the player gets hit — flickers with the new current-HP sprite.")]
        public Sprite damagedLifeBarSprite;

        [Tooltip("Moves this character's life bar down by this many in-game spaces (0 = default HUD position).")]
        public float positionOffsetDownSpaces;

        [Tooltip("Scales this character's life bar (player HUD and boss bar). 1 = normal size, 3 = three times bigger.")]
        public float sizeMultiplier = 1f;

        public float ResolvedSizeMultiplier => sizeMultiplier > 0f ? sizeMultiplier : 1f;
    }

    [Header("Per-Character Sprites")]
    [Tooltip("Add one entry per playable character and fill 11 life-bar sprites + damaged sprite each.")]
    [SerializeField] private CharacterLifeBarSet[] characterLifeBars = new CharacterLifeBarSet[]
    {
        new CharacterLifeBarSet { characterId = "Kit", lifeBarSprites = new Sprite[DefaultSpriteSlotCount] }
    };

    [Header("Display")]
    [SerializeField] private SpriteRenderer lifeBarSpriteRenderer;
    [Tooltip("If true, auto-finds PlayerController.Active each frame.")]
    [SerializeField] private bool autoFindActivePlayer = true;
    [SerializeField] private PlayerController playerOverride;
    [SerializeField] private string fallbackCharacterId = "Kit";

    [Header("Hit Flicker")]
    [Tooltip("How many times to alternate damaged ↔ current HP before staying on current.")]
    [SerializeField] private int hitFlickerCycles = 4;
    [Tooltip("Seconds each half-flash is shown (damaged or current). Medium ≈ 0.1s.")]
    [SerializeField] private float hitFlickerHalfPeriod = 0.1f;

    [Header("Optional Max HP Override")]
    [Tooltip("If > 0, uses this instead of the player's MaxHealth when mapping sprites.")]
    [SerializeField] private int maxHealthOverride = 0;

    private string activeCharacterId = "";
    private Sprite[] activeSprites;
    private Sprite activeDamagedSprite;
    private int lastShownHealth = int.MinValue;
    private PlayerController boundPlayer;
    private Coroutine hitFlickerRoutine;
    private bool isFlickering;
    private Vector3 defaultLocalPosition;
    private Vector3 defaultLocalScale;
    private bool defaultTransformCaptured;
    private bool hiddenByRequest;

    private void Awake()
    {
        if (lifeBarSpriteRenderer == null)
            lifeBarSpriteRenderer = GetComponent<SpriteRenderer>();

        CaptureDefaultTransform();
        EnsureSpriteArraySizes();
        ApplyCharacterSprites(fallbackCharacterId);
        RefreshRendererVisibility();
    }

    private void OnEnable()
    {
        BindPlayer(GetTrackedPlayer());
        RefreshRendererVisibility();
    }

    private void OnDisable()
    {
        StopHitFlicker();
        UnbindPlayer(boundPlayer != null ? boundPlayer : GetTrackedPlayer());
    }

    private void Update()
    {
        RefreshRendererVisibility();

        if (!autoFindActivePlayer)
            return;

        PlayerController player = GetTrackedPlayer();
        if (player == null)
            return;

        if (!string.Equals(activeCharacterId, player.CharacterId, StringComparison.OrdinalIgnoreCase))
            ApplyCharacterSprites(player.CharacterId);

        RefreshBindingIfNeeded(player);

        // Don't snap over an active hit flicker.
        if (!isFlickering)
            RefreshFromPlayer(player);
    }

    private void RefreshBindingIfNeeded(PlayerController player)
    {
        if (boundPlayer == player)
            return;

        UnbindPlayer(boundPlayer);
        BindPlayer(player);
    }

    private void BindPlayer(PlayerController player)
    {
        boundPlayer = player;
        if (boundPlayer == null)
            return;

        boundPlayer.OnHealthChanged += HandleHealthChanged;
        ApplyCharacterSprites(boundPlayer.CharacterId);
        HandleHealthChanged(boundPlayer.CurrentHealth, boundPlayer.MaxHealth);
    }

    private void UnbindPlayer(PlayerController player)
    {
        if (player != null)
            player.OnHealthChanged -= HandleHealthChanged;

        if (boundPlayer == player)
            boundPlayer = null;
    }

    private PlayerController GetTrackedPlayer()
    {
        if (playerOverride != null)
            return playerOverride;

        return PlayerController.Active;
    }

    private void HandleHealthChanged(int current, int max)
    {
        bool tookDamage = lastShownHealth != int.MinValue && current < lastShownHealth;
        SetDisplayedHealth(current, max, tookDamage);
    }

    private void RefreshFromPlayer(PlayerController player)
    {
        if (player == null)
            return;

        if (player.CurrentHealth == lastShownHealth)
            return;

        bool tookDamage = lastShownHealth != int.MinValue && player.CurrentHealth < lastShownHealth;
        SetDisplayedHealth(player.CurrentHealth, player.MaxHealth, tookDamage);
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

        int spriteIndex = clamped;
        if (max != activeSprites.Length - 1 && activeSprites.Length > 1)
        {
            float t = clamped / (float)max;
            spriteIndex = Mathf.RoundToInt(t * (activeSprites.Length - 1));
        }

        spriteIndex = Mathf.Clamp(spriteIndex, 0, activeSprites.Length - 1);
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

    /// <summary>
    /// Flickers damaged ↔ current HP for hitFlickerCycles, then stays on current HP.
    /// </summary>
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
        CharacterLifeBarSet set = FindSetForCharacter(activeCharacterId);
        if (set == null)
            set = FindSetForCharacter(fallbackCharacterId);

        activeSprites = set != null ? set.lifeBarSprites : null;
        activeDamagedSprite = set != null ? set.damagedLifeBarSprite : null;
        ApplyCharacterPositionOffset(set);

        PlayerController player = GetTrackedPlayer();
        if (player != null)
            SetDisplayedHealth(player.CurrentHealth, player.MaxHealth, playHitFlicker: false);
        else if (activeSprites != null && activeSprites.Length > 0)
            SetDisplayedHealth(activeSprites.Length - 1, activeSprites.Length - 1, playHitFlicker: false);
    }

    private CharacterLifeBarSet FindSetForCharacter(string characterId)
    {
        if (characterLifeBars == null || string.IsNullOrWhiteSpace(characterId))
            return null;

        for (int i = 0; i < characterLifeBars.Length; i++)
        {
            CharacterLifeBarSet set = characterLifeBars[i];
            if (set == null || string.IsNullOrWhiteSpace(set.characterId))
                continue;

            if (string.Equals(set.characterId, characterId, StringComparison.OrdinalIgnoreCase))
                return set;
        }

        return null;
    }

    /// <summary>Sprite set for a character from any player LifeBar in the loaded scenes (null if none).</summary>
    public static CharacterLifeBarSet FindSetInScene(string characterId)
    {
        LifeBar[] bars = FindObjectsByType<LifeBar>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < bars.Length; i++)
        {
            CharacterLifeBarSet set = bars[i] != null ? bars[i].FindSetForCharacter(characterId) : null;
            if (set != null && set.lifeBarSprites != null && set.lifeBarSprites.Length > 0)
                return set;
        }

        return null;
    }

    private void CaptureDefaultTransform()
    {
        if (defaultTransformCaptured)
            return;

        defaultTransformCaptured = true;
        defaultLocalPosition = transform.localPosition;
        defaultLocalScale = transform.localScale;
    }

    private void ApplyCharacterPositionOffset(CharacterLifeBarSet set)
    {
        CaptureDefaultTransform();
        float downSpaces = set != null ? Mathf.Max(0f, set.positionOffsetDownSpaces) : 0f;
        float localYOffset = -downSpaces * GetLifeBarSpaceUnit();
        transform.localPosition = defaultLocalPosition + new Vector3(0f, localYOffset, 0f);
        transform.localScale = defaultLocalScale * (set != null ? set.ResolvedSizeMultiplier : 1f);
    }

    private float GetLifeBarSpaceUnit()
    {
        return Mathf.Max(0.01f, Mathf.Abs(defaultLocalScale.y));
    }

    private void EnsureSpriteArraySizes()
    {
        if (characterLifeBars == null)
            return;

        for (int i = 0; i < characterLifeBars.Length; i++)
        {
            if (characterLifeBars[i] == null)
                characterLifeBars[i] = new CharacterLifeBarSet();

            if (characterLifeBars[i].lifeBarSprites == null || characterLifeBars[i].lifeBarSprites.Length == 0)
                characterLifeBars[i].lifeBarSprites = new Sprite[DefaultSpriteSlotCount];
        }
    }

    /// <summary>Hide/show this life bar without destroying bind state (e.g. while Kaboodle is open).</summary>
    public void SetVisible(bool visible)
    {
        hiddenByRequest = !visible;
        RefreshRendererVisibility();
    }

    /// <summary>Shown only once a player has spawned in, and not while hidden by <see cref="SetVisible"/>.</summary>
    private void RefreshRendererVisibility()
    {
        if (lifeBarSpriteRenderer == null)
            lifeBarSpriteRenderer = GetComponent<SpriteRenderer>();

        bool show = !hiddenByRequest && GetTrackedPlayer() != null;
        if (lifeBarSpriteRenderer != null && lifeBarSpriteRenderer.enabled != show)
            lifeBarSpriteRenderer.enabled = show;
    }

    /// <summary>Hide or show every player LifeBar in the loaded scenes.</summary>
    public static void SetAllVisible(bool visible)
    {
        LifeBar[] bars = FindObjectsByType<LifeBar>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < bars.Length; i++)
        {
            if (bars[i] != null)
                bars[i].SetVisible(visible);
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
        for (int i = 0; i < characterLifeBars.Length; i++)
        {
            if (characterLifeBars[i] == null)
                continue;

            characterLifeBars[i].positionOffsetDownSpaces = Mathf.Max(0f, characterLifeBars[i].positionOffsetDownSpaces);
            characterLifeBars[i].sizeMultiplier = characterLifeBars[i].ResolvedSizeMultiplier;
        }
    }
#endif
}
