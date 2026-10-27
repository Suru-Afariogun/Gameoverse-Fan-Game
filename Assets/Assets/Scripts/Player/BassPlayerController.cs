using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Bass — Mega Man-style blaster. Item Shop styles: Dual System / Double Gear.
/// Default (Normal) has neither. Double Gear style uses green shots, and Double Gear shots
/// while the power gear is active. Animator AbilityMode drives Dual outfits.
/// </summary>
public class BassPlayerController : PlayerController
{
    /// <summary>Dual System outfit index (animator AbilityMode). Default outfit = 0.</summary>
    public enum BassDualAbility : int
    {
        Default = 0,
        MetalLeaf = 1,
        CrashingHeat = 2,
        BubbleAir = 3,
        QuickTime = 4
    }

    [Header("Bass - Projectile")]
    [SerializeField] private Projectile purpleShotPrefab;
    [SerializeField] private Projectile greenShotPrefab;
    [SerializeField] private Projectile doubleGearShotPrefab;
    [Tooltip("Bass Buster rapid-fire interval (normal / Double Gear style).")]
    [SerializeField] private float shootCooldown = 0.1f;
    [Tooltip("Fire interval during forced 16s Double Gear weaken.")]
    [SerializeField] private float doubleGearWeakenShootCooldown = 0.2f;
    [SerializeField] private GameVisualEffect busterBlastPrefab;

    [Header("Bass - Animation")]
    [SerializeField] private float shootAnimDuration = 0.28f;
    [Tooltip("While grounded and firing, Bass stands in place like classic Mega Man.")]
    [SerializeField] private bool lockMovementWhileGroundShooting = true;

    [Header("Bass - Double Jump (base arsenal, like Mega Man & Bass)")]
    [Tooltip("Extra air jumps at Aerial Action level 0. Ariel Action levels add more on top.")]
    [SerializeField] private int bassBaseAirJumps = 1;

    [Header("Bass - Move / Dash (faster than Kit)")]
    [Tooltip("Kit base move speed is 6. Bass runs +3 faster.")]
    [SerializeField] private float bassMoveSpeed = 9f;
    [SerializeField] private float bassDashDistance = 6f;
    [SerializeField] private float bassDashSpeed = 19.5f;
    [SerializeField] private float bassDashSmoothStop = 0.14f;
    [SerializeField] private int bassMaxAirDashes = 2;
    [SerializeField] private bool bassAllowDashJump = true;
    [SerializeField] private float bassDashJumpMomentumDuration = 0.55f;
    [SerializeField] private bool bassDashJumpMomentumUntilLanded = true;

    [Header("Bass - Dash Afterimages")]
    [SerializeField] private bool bassEnableDashAfterimages = true;
    [SerializeField] private int bassAfterimageCount = 3;
    [SerializeField] private float bassAfterimageSpacing = 0.1f;
    [SerializeField] private Color bassAfterimageColor = Color.white;
    [SerializeField] [Range(0f, 1f)] private float bassAfterimageAlphaStart = 0.55f;
    [SerializeField] [Range(0f, 1f)] private float bassAfterimageAlphaEnd = 0.15f;

    [Header("Bass - Double Gear")]
    [SerializeField] private GameObject doubleGearVisualContainer;
    [SerializeField] private Transform blueGear;
    [SerializeField] private Transform redGear;
    [SerializeField] private Transform doubleGearDurationBar;
    [SerializeField] private SpriteRenderer doubleGearDurationFill;
    [SerializeField] private float doubleGearDuration = 10f;
    [SerializeField] private float doubleGearForcedCooldown = 16f;
    [SerializeField] private float doubleGearShakeWindow = 2f;
    [SerializeField] private float doubleGearMoveSpeedBonus = 5f;
    [SerializeField] [Range(0f, 1f)] private float doubleGearWorldBlackTint = 0.28f;
    [Tooltip("World/enemy/hazard time scale while Double Gear is active (Bass stays full speed).")]
    [SerializeField] [Range(0.05f, 1f)] private float doubleGearWorldTimeScale = 0.5f;
    [SerializeField] [Range(0.2f, 1f)] private float doubleGearAfterimageAlphaStart = 0.92f;
    [SerializeField] [Range(0.1f, 1f)] private float doubleGearAfterimageAlphaEnd = 0.55f;
    [SerializeField] private float doubleGearSpinDegPerSecStart = 120f;
    [SerializeField] private float doubleGearSpinDegPerSecEnd = 720f;
    [SerializeField] private float doubleGearShakeAmplitude = 0.06f;
    [SerializeField] private int doubleGearAfterimageCount = 4;
    [SerializeField] private Color doubleGearAfterimagePurple = new Color(0.78f, 0.55f, 1f, 1f);
    [SerializeField] private Color doubleGearAfterimageRed = new Color(1f, 0.25f, 0.25f, 1f);
    [SerializeField] private Color doubleGearAfterimageBlue = new Color(0.45f, 0.7f, 1f, 1f);
    [SerializeField] private Color doubleGearAfterimageAqua = new Color(0.45f, 1f, 0.95f, 1f);

    [Header("Bass - Metal Leaf (Dual System)")]
    [SerializeField] private BassMetalBlade metalBladePrefab;
    [SerializeField] private BassLeafShield leafShieldPrefab;
    [SerializeField] private int metalLeafMaxAmmo = 400;
    [SerializeField] private float metalLeafAmmoRecoverSeconds = 8f;
    [SerializeField] private float metalBladeShootCooldown = 0.16f;
    [SerializeField] private float metalBladeSpeed = 18f;
    [SerializeField] private float metalBladeDamage = 1f;
    [SerializeField] private int metalBladeMaxLive = 8;
    [SerializeField] private int leafShieldCount = 12;
    [SerializeField] private float leafOrbitRadius = 2.4f;
    [SerializeField] private float leafOrbitDegreesPerSecond = 200f;
    [SerializeField] private int leafShieldHealth = 15;
    [SerializeField] private int leafContactDamage = 1;
    [SerializeField] private Color dualAmmoBarColor = new Color(1f, 0.92f, 0.15f, 1f);
    [SerializeField] private int crystalsPerAmmoRestore = 8;
    [SerializeField] private float statusBarIdleFadeSeconds = 2f;
    [SerializeField] [Range(0f, 1f)] private float statusBarIdleAlpha = 0.5f;

    [Header("Bass - QuickTime (Dual System)")]
    [SerializeField] private BassQuickBoomerang quickBoomerangPrefab;
    [SerializeField] private BassFlashTimeStar flashTimeStarPrefab;
    [Tooltip("Fallback art if Quick Boomerang prefab is missing (Bass Sheet 2_95).")]
    [SerializeField] private Sprite quickBoomerangSprite;
    [Tooltip("Fallback art if Flash Time Star prefab is missing (Bass Sheet 2_96).")]
    [SerializeField] private Sprite flashTimeStarSprite;
    [SerializeField] private RuntimeAnimatorController flashTimeStarController;
    [SerializeField] private int quickTimeMaxAmmo = 500;
    [SerializeField] private float quickTimeAmmoRecoverSeconds = 8f;
    [SerializeField] private int quickTimeBoomerangVolleyCost = 1;
    [SerializeField] private int quickTimeTimeStopCost = 80;
    [SerializeField] private float quickTimeChargeSeconds = 3f;
    [SerializeField] private float quickTimeStopDuration = 6.5f;
    [SerializeField] private float quickBoomerangShootCooldown = 0.22f;
    [SerializeField] private float quickBoomerangSpeed = 14f;
    [SerializeField] private float quickBoomerangDamage = 3f;
    [SerializeField] private int quickBoomerangMaxLive = 9;
    [SerializeField] private int quickBoomerangVolleyCount = 3;
    [SerializeField] private float quickBoomerangSpreadDegrees = 22f;
    [SerializeField] private float quickBoomerangOutDistanceSpaces = 6.5f;
    [SerializeField] private float quickTimeSpaceSize = 1f;
    [SerializeField] private int flashTimeStarCount = 36;
    [Tooltip("Viewport padding outside 0–1 so Flash stars cover a wider area than the screen.")]
    [SerializeField] private float flashTimeStarViewportPad = 0.35f;
    [SerializeField] private Color quickTimeAmmoBarColor = new Color(1f, 0.72f, 0.85f, 1f);
    [SerializeField] private HexSparkEmitter quickTimeSparkEmitter;
    [Tooltip("Hex Spark 1/2/3 — used only for QuickTime Time Stop charge aura.")]
    [SerializeField] private HexSpark[] quickTimeSparkPrefabs = new HexSpark[3];
    [Tooltip("Hit-spark / charge origin (Bass child Spark Spawner).")]
    [SerializeField] private Transform sparkSpawner;
    [SerializeField] private Color quickTimeChargeAuraColor = new Color(1f, 0.75f, 0.88f, 1f);

    [Header("Bass - Bubble Air (Dual System)")]
    [SerializeField] private BassBubbleBullet bubbleBulletPrefab;
    [SerializeField] private BassAirTornado airTornadoPrefab;
    [SerializeField] private int bubbleAirMaxAmmo = 400;
    [SerializeField] private float bubbleAirAmmoRecoverSeconds = 8f;
    [SerializeField] private int bubbleAmmoCost = 4;
    [SerializeField] private float bubbleShootCooldown = 0.2f;
    [SerializeField] private float bubbleLaunchSpeed = 11f;
    [SerializeField] private float bubbleDamage = 2f;
    [SerializeField] private int bubbleMaxLive = 6;
    [SerializeField] private float bubbleSpaceSize = 1f;
    [SerializeField] private int bubbleAirCrystalsPerAmmo = 4;
    [SerializeField] private Color bubbleAirAmmoBarColor = new Color(0.35f, 0.7f, 1f, 1f);

    [Header("Bass - Crashing Heat (Dual System)")]
    [SerializeField] private BassCrashBomb crashBombPrefab;
    [SerializeField] private BassCrashBomb crashBombPlusPrefab;
    [SerializeField] private BassCrashBombExplosion crashBombExplosionPrefab;
    [SerializeField] private BassSmallHeatFlare smallHeatFlarePrefab;
    [SerializeField] private BassHeatFlareStack heatFlareStackPrefab;
    [SerializeField] private BassHeatFlareBall heatFlareBallPrefab;
    [SerializeField] private BassHeatDashFlare heatDashFlarePrefab;
    [SerializeField] private GameVisualEffect crashHeatStunnedEffectPrefab;
    [SerializeField] private int crashingHeatMaxAmmo = 600;
    [SerializeField] private float crashingHeatAmmoRecoverSeconds = 8f;
    [SerializeField] private int crashingHeatAmmoRecoverAmount = 3;
    [SerializeField] private int crashBombAmmoCost = 5;
    [SerializeField] private int heatFlareStackAmmoCost = 1;
    [SerializeField] private int heatBallAmmoCost = 3;
    [SerializeField] private float crashingHeatShootCooldown = 0.35f;
    [SerializeField] private float crashBombSpeed = 14f;
    [SerializeField] private float crashingHeatSpaceSize = 1f;
    [SerializeField] private float crashBombPlusHpRatio = 0.15f;
    [SerializeField] private int crashingHeatCrystalsPerAmmo = 2;
    [SerializeField] private Color crashingHeatAmmoBarColor = new Color(1f, 0.55f, 0.12f, 1f);
    [SerializeField] private float heatDashDistanceMultiplier = 4f;
    [SerializeField] private float heatDashSpeedMultiplier = 5f;
    [SerializeField] private int heatDashContactDamage = 6;
    [Tooltip("Heat Dash Flare local X offset from Spark Spawner (negative = toward Bass's back).")]
    [SerializeField] private float heatDashFlareOffsetX = 1.5f;
    [Tooltip("Heat Flare Stacks left along the dash path (equally spaced).")]
    [SerializeField] private int heatDashTrailStackCount = 4;
    [Tooltip("Trail spacing is clamped to this range (in Crashing Heat spaces).")]
    [SerializeField] private float heatDashTrailMinSpaces = 2f;
    [SerializeField] private float heatDashTrailMaxSpaces = 4f;

    [Header("Bass - Upgrades (Hyper Ability / Attack Style)")]
    [Tooltip("Max health added per equipped Hyper Ability level.")]
    [SerializeField] private int hyperMaxHealthPerLevel = 2;
    [Tooltip("Normal style + Hyper: Double Gear pool and Dual ammo scale to this once both have been used.")]
    [SerializeField] [Range(0.05f, 1f)] private float hyperNormalConcurrentFraction = 0.5f;
    [Tooltip("Hyper-granted system on a styled Bass (Dual ammo on Double Gear style, Double Gear on Dual style).")]
    [SerializeField] [Range(0.05f, 1f)] private float hyperStyleGrantedFraction = 0.3f;
    [Tooltip("Attack Style: extra Double Gear seconds per level.")]
    [SerializeField] private float styleDoubleGearSecondsPerLevel = 2f;
    [Tooltip("Attack Style: extra max ammo per level on each Dual ability.")]
    [SerializeField] private int styleDualAmmoPerLevel = 100;

    private bool attackHeld;
    private float shootCooldownTimer;
    private BassDualAbility dualAbility = BassDualAbility.Default;

    private bool doubleGearActive;
    /// <summary>Duration pool left. Depletes while active; regenerates 1:1 while inactive after early cancel.</summary>
    private float doubleGearRemaining;
    private float doubleGearCooldownRemaining;
    private bool doubleGearForcedWeaken;
    private bool doubleGearPoolInitialized;

    private BassDoubleGearWorldTint doubleGearWorldTint;
    private Vector3 blueGearRestLocal;
    private Vector3 redGearRestLocal;
    private float durationBarFullWidth = 1.98f;
    private bool gearRestsCached;
    private int savedDashAfterimageCount = 3;
    private Texture2D durationFillTexture;
    private bool ownsWorldSlow;
    private int savedSortingGroupOrder;

    private float metalLeafAmmo;
    private float metalLeafAmmoRecoverTimer;
    private readonly List<BassLeafShield> liveLeaves = new List<BassLeafShield>(12);
    private readonly List<BassMetalBlade> liveBlades = new List<BassMetalBlade>(8);
    private float quickTimeAmmo;
    private float quickTimeAmmoRecoverTimer;
    private float quickTimeChargeTimer;
    private bool quickTimeStopActive;
    private float quickTimeStopRemaining;
    private bool ownsQuickTimeWorldSlow;
    private readonly object QuickTimeSlowOwner = new object();
    private readonly List<BassQuickBoomerang> liveBoomerangs = new List<BassQuickBoomerang>(8);
    private readonly List<BassFlashTimeStar> liveFlashStars = new List<BassFlashTimeStar>(32);
    private BassQuickBoomerang runtimeBoomerangTemplate;
    private BassFlashTimeStar runtimeFlashStarTemplate;
    private float bubbleAirAmmo;
    private float bubbleAirAmmoRecoverTimer;
    private readonly List<BassBubbleBullet> liveBubbles = new List<BassBubbleBullet>(6);
    private float crashingHeatAmmo;
    private float crashingHeatAmmoRecoverTimer;
    private bool heatDashActive;
    private readonly HashSet<int> heatDashHitIds = new HashSet<int>();
    private readonly List<Collider2D> heatDashIgnored = new List<Collider2D>(64);
    private int crystalsTowardAmmo;
    private float lastCombatAttackTime = -999f;
    private float durationBarFillHeight = 0.22f;

    private int baseMaxHealth;
    /// <summary>Set once Dual + Double Gear run together under Hyper; cleared when Hyper is unequipped.</summary>
    private bool hyperCapsLocked;
    private int metalLeafMaxCached;
    private int bubbleAirMaxCached;
    private int crashingHeatMaxCached;
    private int quickTimeMaxCached;
    private float doubleGearMaxCached;

    private float heatDashTrailStartX;
    private float heatDashTrailDir = 1f;
    private float heatDashTrailSpacing;
    private int heatDashTrailSpawned;

    public BassDualAbility CurrentDualAbility => dualAbility;
    public bool UsesDualSystemStyle => PlayerAttackStyle.Is(AttackStyleId.SpreadShot);
    public bool UsesDoubleGearStyle => PlayerAttackStyle.Is(AttackStyleId.MachineGun);
    public bool UsesNormalStyle => !UsesDualSystemStyle && !UsesDoubleGearStyle;

    private int HyperLevel => PlayerUpgrades.GetActiveLevel(this, UpgradeType.HyperAbility);
    private int StyleLevel => PlayerUpgrades.GetActiveLevel(this, UpgradeType.AttackStyle);
    public bool HyperUpgradeEquipped => HyperLevel > 0;
    /// <summary>Dual System style, or any other style while Hyper is equipped.</summary>
    public bool CanUseDualAbilities =>
        UsesDualSystemStyle || (HyperUpgradeEquipped && (UsesNormalStyle || UsesDoubleGearStyle));
    /// <summary>Double Gear style, or any other style while Hyper is equipped.</summary>
    public bool CanUseDoubleGear =>
        UsesDoubleGearStyle || (HyperUpgradeEquipped && (UsesNormalStyle || UsesDualSystemStyle));
    private bool IsAtFullHealth => !IsDead && CurrentHealth >= MaxHealth;

    /// <summary>Attack Style level 5: Dual ability and Double Gear shot damage x2.</summary>
    /// <summary>True while Double Gear is on and a Dual outfit is selected.</summary>
    public bool AbilityWithDoubleGear =>
        doubleGearActive && dualAbility != BassDualAbility.Default;

    public float GetAbilityDamageMultiplier()
    {
        float m = StyleLevel >= PlayerUpgrades.MaxLevel ? 2f : 1f;
        // Double Gear + ability: ability weapons deal double damage.
        if (AbilityWithDoubleGear)
            m *= 2f;
        return m;
    }

    /// <summary>Additive launch-speed bonus while Double Gear boosts an ability.</summary>
    public float GetAbilitySpeedBonus() => AbilityWithDoubleGear ? 1f : 0f;

    /// <summary>Projectile visual scale while Double Gear boosts an ability (+30%).</summary>
    public float GetAbilityProjectileScale() => AbilityWithDoubleGear ? 1.3f : 1f;

    /// <summary>Child-spawn count multiplier (Crash Bomb flares/balls, Bubble bits) while DG + ability.</summary>
    public int GetAbilitySpawnMultiplier() => AbilityWithDoubleGear ? 2 : 1;

    /// <summary>Hyper cap on Dual ammo pools (1 = uncapped).</summary>
    private float DualAmmoCapFraction
    {
        get
        {
            if (!HyperUpgradeEquipped || UsesDualSystemStyle)
                return 1f;
            if (UsesDoubleGearStyle)
                return Mathf.Clamp(hyperStyleGrantedFraction, 0.05f, 1f);
            return hyperCapsLocked ? Mathf.Clamp(hyperNormalConcurrentFraction, 0.05f, 1f) : 1f;
        }
    }

    /// <summary>Hyper cap on the Double Gear duration pool (1 = uncapped).</summary>
    private float DoubleGearCapFraction
    {
        get
        {
            if (!HyperUpgradeEquipped || UsesDoubleGearStyle)
                return 1f;
            if (UsesDualSystemStyle)
                return Mathf.Clamp(hyperStyleGrantedFraction, 0.05f, 1f);
            return hyperCapsLocked ? Mathf.Clamp(hyperNormalConcurrentFraction, 0.05f, 1f) : 1f;
        }
    }

    /// <summary>Double Gear pool max: base + Attack Style seconds, then Hyper caps.</summary>
    public float EffectiveDoubleGearDuration
    {
        get
        {
            float boosted = Mathf.Max(0.1f, doubleGearDuration
                + Mathf.Max(0f, styleDoubleGearSecondsPerLevel) * StyleLevel);
            return Mathf.Max(0.1f, boosted * DoubleGearCapFraction);
        }
    }

    /// <summary>Dual ammo max: base + Attack Style ammo, then Hyper caps.</summary>
    public int EffectiveMaxAmmo(int baseMax)
    {
        int boosted = Mathf.Max(1, baseMax + Mathf.Max(0, styleDualAmmoPerLevel) * StyleLevel);
        float fraction = DualAmmoCapFraction;
        if (fraction >= 0.999f)
            return boosted;
        return Mathf.Max(1, Mathf.RoundToInt(boosted * fraction));
    }

    private int MetalLeafMax => EffectiveMaxAmmo(metalLeafMaxAmmo);
    private int BubbleAirMax => EffectiveMaxAmmo(bubbleAirMaxAmmo);
    private int CrashingHeatMax => EffectiveMaxAmmo(crashingHeatMaxAmmo);
    private int QuickTimeMax => EffectiveMaxAmmo(quickTimeMaxAmmo);
    public bool IsDoubleGearActive => doubleGearActive;
    public bool IsDoubleGearOnCooldown => doubleGearCooldownRemaining > 0f;
    public bool IsDoubleGearForcedWeaken =>
        UsesDoubleGearStyle && doubleGearForcedWeaken && doubleGearCooldownRemaining > 0f;

    public override bool WeaponGearBoostsShots => true;

    protected override int UpgradeAirJumps =>
        Mathf.Max(0, bassBaseAirJumps) + PlayerUpgrades.GetActiveLevel(this, UpgradeType.AerialAction);

    protected override void Awake()
    {
        base.Awake();
        SetCharacterId("Bass");
        baseMaxHealth = maxHealth;

        moveSpeed = bassMoveSpeed;
        dashDistance = bassDashDistance;
        dashSpeed = bassDashSpeed;
        dashSmoothStopDuration = bassDashSmoothStop;
        maxAirDashes = bassMaxAirDashes;
        allowDashJump = bassAllowDashJump;
        dashJumpMomentumDuration = bassDashJumpMomentumDuration;
        dashJumpMomentumUntilLanded = bassDashJumpMomentumUntilLanded;
        enableDashAfterimages = bassEnableDashAfterimages;
        dashAfterimageCount = bassAfterimageCount;
        dashAfterimageSpacing = bassAfterimageSpacing;
        dashAfterimageColor = bassAfterimageColor;
        dashAfterimageAlphaStart = bassAfterimageAlphaStart;
        dashAfterimageAlphaEnd = bassAfterimageAlphaEnd;
        airDashesRemaining = MaxAirDashesWithUpgrades;

        SetupDashAfterimages();
        ResolveDoubleGearRefs();
        SetupDoubleGearVisuals();
        EnsureQuickTimeSparkEmitter();
        metalLeafMaxCached = MetalLeafMax;
        quickTimeMaxCached = QuickTimeMax;
        bubbleAirMaxCached = BubbleAirMax;
        crashingHeatMaxCached = CrashingHeatMax;
        doubleGearMaxCached = EffectiveDoubleGearDuration;
        metalLeafAmmo = metalLeafMaxCached;
        quickTimeAmmo = quickTimeMaxCached;
        bubbleAirAmmo = bubbleAirMaxCached;
        crashingHeatAmmo = crashingHeatMaxCached;
        RefreshUpgradeStats();
        ApplyDualAbilityToAnimator();
        RefreshStatusBar();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        PlayerAttackStyle.OnChanged -= OnAttackStyleChanged;
        PlayerAttackStyle.OnChanged += OnAttackStyleChanged;
        PlayerUpgrades.OnChanged -= OnUpgradeChanged;
        PlayerUpgrades.OnChanged += OnUpgradeChanged;
        PlayerUpgrades.OnEquippedChanged -= OnUpgradeEquippedChanged;
        PlayerUpgrades.OnEquippedChanged += OnUpgradeEquippedChanged;
        RefreshUpgradeStats();
    }

    private void Start()
    {
        RefreshUpgradeStats();
        ApplyDualAbilityToAnimator();
        RefreshStatusBar();
    }

    protected override void OnDisable()
    {
        PlayerAttackStyle.OnChanged -= OnAttackStyleChanged;
        PlayerUpgrades.OnChanged -= OnUpgradeChanged;
        PlayerUpgrades.OnEquippedChanged -= OnUpgradeEquippedChanged;
        base.OnDisable();
        attackHeld = false;
        StopQuickTimeChargeAura(immediate: true);
        DestroyLeafShields();
        EndQuickTimeStop();
        EndDoubleGear(force: true, exhausted: false);
        doubleGearWorldTint?.Release();

        if (runtimeBoomerangTemplate != null)
        {
            Destroy(runtimeBoomerangTemplate.gameObject);
            runtimeBoomerangTemplate = null;
        }

        if (runtimeFlashStarTemplate != null)
        {
            Destroy(runtimeFlashStarTemplate.gameObject);
            runtimeFlashStarTemplate = null;
        }
    }

    private void OnAttackStyleChanged(AttackStyleId _)
    {
        RefreshUpgradeStats();
        ApplyDualAbilityToAnimator();
        RefreshStatusBar();
    }

    private void OnUpgradeChanged(string characterId, UpgradeType type, int level)
    {
        if (!IsThisCharacterId(characterId))
            return;
        RefreshUpgradeStats();
        ApplyDualAbilityToAnimator();
        RefreshStatusBar();
    }

    private void OnUpgradeEquippedChanged(string characterId, UpgradeType type, bool equipped)
    {
        if (!IsThisCharacterId(characterId))
            return;
        RefreshUpgradeStats();
        ApplyDualAbilityToAnimator();
        RefreshStatusBar();
    }

    private bool IsThisCharacterId(string characterId)
    {
        return string.Equals(characterId, CharacterId, System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Applies upgrade-driven stats: Hyper max health, Hyper caps, Attack Style ammo / duration pools,
    /// and drops abilities the current style + Hyper no longer allow.
    /// </summary>
    private void RefreshUpgradeStats()
    {
        if (baseMaxHealth <= 0)
            baseMaxHealth = maxHealth;

        if (!HyperUpgradeEquipped)
            hyperCapsLocked = false;

        SetMaxHealthRuntime(baseMaxHealth + Mathf.Max(0, hyperMaxHealthPerLevel) * HyperLevel);

        // Double Gear / Dual outfits only exist for their shop style, or any style with Hyper.
        if (!CanUseDoubleGear && doubleGearActive)
            EndDoubleGear(force: true, exhausted: false);

        if (!CanUseDualAbilities)
        {
            if (dualAbility != BassDualAbility.Default || liveLeaves.Count > 0)
                DestroyLeafShields();
            EndQuickTimeStop();
            dualAbility = BassDualAbility.Default;
            quickTimeChargeTimer = 0f;
        }

        metalLeafMaxCached = RefreshAmmoPool(ref metalLeafAmmo, metalLeafMaxCached, MetalLeafMax);
        bubbleAirMaxCached = RefreshAmmoPool(ref bubbleAirAmmo, bubbleAirMaxCached, BubbleAirMax);
        crashingHeatMaxCached = RefreshAmmoPool(ref crashingHeatAmmo, crashingHeatMaxCached, CrashingHeatMax);
        quickTimeMaxCached = RefreshAmmoPool(ref quickTimeAmmo, quickTimeMaxCached, QuickTimeMax);

        float dgMax = EffectiveDoubleGearDuration;
        if (doubleGearPoolInitialized)
        {
            if (dgMax > doubleGearMaxCached && !IsDoubleGearForcedWeaken)
                doubleGearRemaining += dgMax - doubleGearMaxCached;
            doubleGearRemaining = Mathf.Min(doubleGearRemaining, dgMax);
        }

        doubleGearMaxCached = dgMax;
    }

    /// <summary>Growing the max adds the difference (like max HP); shrinking clamps current ammo.</summary>
    private static int RefreshAmmoPool(ref float ammo, int oldMax, int newMax)
    {
        if (newMax == oldMax)
            return oldMax;
        if (newMax > oldMax)
            ammo += newMax - oldMax;
        ammo = Mathf.Clamp(ammo, 0f, newMax);
        return newMax;
    }

    /// <summary>Dual + Double Gear running together under Hyper locks the pool caps until Hyper is unequipped.</summary>
    private void EngageConcurrentMode()
    {
        if (!HyperUpgradeEquipped || hyperCapsLocked)
            return;
        hyperCapsLocked = true;
        RefreshUpgradeStats();
    }

    protected override void ClearForVehicleRide()
    {
        attackHeld = false;
        quickTimeChargeTimer = 0f;
        StopQuickTimeChargeAura(immediate: true);
        DestroyLeafShields();
        EndQuickTimeStop();
        EndDoubleGear(force: true, exhausted: false);
        base.ClearForVehicleRide();
    }

    protected override void HandleCharacterUpdate()
    {
        // Events cover shop changes; polling also catches Scouter max-level boosts expiring.
        RefreshUpgradeStats();
        TickDoubleGear(Time.deltaTime);
        TickMetalLeafAmmo(Time.deltaTime);
        TickBubbleAirAmmo(Time.deltaTime);
        TickCrashingHeatAmmo(Time.deltaTime);
        TickHeatDashContact();
        TickHeatDashTrail();
        TickQuickTimeAmmo(Time.deltaTime);
        TickQuickTimeCharge(Time.deltaTime);
        TickQuickTimeStop(Time.deltaTime);
        RefreshStatusBar();

        if (shootCooldownTimer > 0f)
            shootCooldownTimer -= Time.deltaTime;

        bool attackPressed = IsAttackButtonHeld();
        if (attackPressed)
            attackHeld = true;

        if (InputLocked || IsDead)
        {
            attackHeld = false;
            quickTimeChargeTimer = 0f;
            return;
        }

        // Stun does not cancel QuickTime charge; still allow hold-fire when not stunned.
        if (isStunned)
            return;

        if (attackHeld && shootCooldownTimer <= 0f)
            FireCurrentAttack();
    }

    protected override void OnAttackStarted(InputAction.CallbackContext context)
    {
        if (InputLocked || IsDead)
            return;

        // Charge survives stun; still allow starting hold while stunned so release can fire Time Stop.
        attackHeld = true;
        if (isStunned)
            return;

        if (shootCooldownTimer <= 0f)
            FireCurrentAttack();
    }

    protected override void OnAttackCanceled(InputAction.CallbackContext context)
    {
        TryReleaseQuickTimeCharge();
        attackHeld = false;
        quickTimeChargeTimer = 0f;
    }

    protected override void OnHitStunStarted()
    {
        bool keepChargeHold = CanUseDualAbilities
            && dualAbility == BassDualAbility.QuickTime
            && IsAttackButtonHeld();

        // Kit-style stunned spark VFX, but Bass art — anchored at Spark Spawner when present.
        ResolveSparkSpawner();
        VisualEffects.PlayStunned(stunnedEffectPrefab, this, sparkSpawner);

        if (keepChargeHold)
            attackHeld = true;
    }

    private bool IsAttackButtonHeld()
    {
        return controls != null && controls.PlayerControls.Attack.IsPressed();
    }

    /// <summary>
    /// Down + Dash: Double Gear style toggles the power gear; Dual System cycles outfits.
    /// Hyper Ability: Normal style Down+Dash toggles Double Gear; Up+Dash cycles Dual (Normal / Double Gear style)
    /// or toggles Double Gear (Dual System style). Without Hyper, Normal style dashes as usual.
    /// </summary>
    protected override void OnDashPerformed(InputAction.CallbackContext context)
    {
        if (!context.performed || inputLocked || dashDisabled || isStunned || BlocksActionCancel() || IsDead)
            return;

        bool hyper = HyperUpgradeEquipped;

        if (IsAimingUp() && hyper)
        {
            if (UsesNormalStyle || UsesDoubleGearStyle)
                CycleDualAbility();
            else if (UsesDualSystemStyle)
                TryToggleDoubleGear();
            dashRequested = false;
            return;
        }

        if (IsAimingDown())
        {
            if (UsesDoubleGearStyle || (UsesNormalStyle && hyper))
            {
                TryToggleDoubleGear();
                dashRequested = false;
                return;
            }

            if (UsesDualSystemStyle)
            {
                CycleDualAbility();
                dashRequested = false;
                return;
            }
        }

        base.OnDashPerformed(context);
    }

    protected override void TryBeginDash()
    {
        // Dual / Double Gear Down+Dash and Hyper Up+Dash consume the dash; plain Normal can dash while holding down.
        bool hyper = HyperUpgradeEquipped;
        if ((IsAimingDown() && (UsesDualSystemStyle || UsesDoubleGearStyle || hyper))
            || (IsAimingUp() && hyper))
        {
            dashRequested = false;
            return;
        }

        if (UsesHeatDash)
        {
            // Keep these elevated for the whole dash — GetCurrentDashVelocityX reads dashSpeed/dashVelocityX each frame.
            dashDistance = GetHeatDashDistance();
            dashSpeed = GetHeatDashSpeed();
            base.TryBeginDash();
            // If the dash failed to start (cooldown / air charges), restore baselines immediately.
            if (!isDashing)
                RestoreBassDashStats();
            return;
        }

        base.TryBeginDash();
    }

    private float GetHeatDashDistance() =>
        Mathf.Max(0.1f, bassDashDistance * heatDashDistanceMultiplier - 7f);

    private float GetHeatDashSpeed() =>
        Mathf.Max(0.1f, bassDashSpeed * heatDashSpeedMultiplier + 9f);

    private void RestoreBassDashStats()
    {
        dashDistance = bassDashDistance;
        dashSpeed = bassDashSpeed;
    }

    protected override float GetCurrentMoveSpeed()
    {
        float speed = base.GetCurrentMoveSpeed();
        if (doubleGearActive)
            speed += Mathf.Max(0f, doubleGearMoveSpeedBonus);
        return speed;
    }

    protected override void UpdateDashAfterimages()
    {
        // Heat Dash never shows afterimages.
        if (heatDashActive)
        {
            SetDashAfterimagesActive(false);
            afterimagesVisibleUntil = 0f;
            afterimageFadeOutActive = false;
            return;
        }

        if (doubleGearActive && IsDoubleGearTrailMoving())
        {
            afterimagesVisibleUntil = Time.time
                + GetMaxAfterimageDelay()
                + dashAfterimageLifetimePadding
                + 0.2f;
            SetDashAfterimagesActive(true);
            UpdateColoredDoubleGearAfterimages();
            return;
        }

        base.UpdateDashAfterimages();
    }

    private void UpdateColoredDoubleGearAfterimages()
    {
        if (!enableDashAfterimages || afterimageRenderers == null || spriteRenderer == null)
            return;

        afterimageFadeOutActive = false;

        for (int i = 0; i < afterimageRenderers.Length; i++)
        {
            float delay = GetAfterimageDelay(i);
            if (!TrySamplePose(delay, out PoseSample sample))
            {
                afterimageObjects[i].SetActive(false);
                continue;
            }

            afterimageObjects[i].SetActive(true);
            afterimageObjects[i].transform.SetPositionAndRotation(sample.position, sample.rotation);
            afterimageRenderers[i].sprite = sample.sprite != null ? sample.sprite : spriteRenderer.sprite;
            afterimageRenderers[i].flipX = sample.flipX;
            // World-space trails (unparented): sit behind Bass without inheriting body spin.
            CharacterEffectSorting.ApplyDetachedEffectBehindHost(
                afterimageRenderers[i], effectSortingGroup, spriteRenderer, 1 + i);

            Color c = GetDoubleGearAfterimageColor(i);
            float t = afterimageRenderers.Length == 1 ? 0f : i / (float)(afterimageRenderers.Length - 1);
            c.a = Mathf.Lerp(doubleGearAfterimageAlphaStart, doubleGearAfterimageAlphaEnd, t);
            afterimageRenderers[i].color = c;
        }
    }

    private Color GetDoubleGearAfterimageColor(int index)
    {
        switch (index)
        {
            case 0: return doubleGearAfterimagePurple;
            case 1: return doubleGearAfterimageRed;
            case 2: return doubleGearAfterimageBlue;
            default: return doubleGearAfterimageAqua;
        }
    }

    private bool IsDoubleGearTrailMoving()
    {
        if (isDashing || isDashJumping)
            return true;
        if (Mathf.Abs(moveInput.x) > 0.01f)
            return true;
        if (rb != null && Mathf.Abs(rb.linearVelocity.x) > 0.35f)
            return true;
        return false;
    }

    private void TryToggleDoubleGear()
    {
        if (!CanUseDoubleGear)
            return;

        if (doubleGearActive)
        {
            EndDoubleGear(force: false, exhausted: false);
            return;
        }

        // Forced 16s weaken cooldown only — early cancel has no lockout.
        if (doubleGearCooldownRemaining > 0f)
            return;

        EnsureDoubleGearPool();
        if (doubleGearRemaining <= 0.05f)
            return;

        BeginDoubleGear();
    }

    private void EnsureDoubleGearPool()
    {
        if (doubleGearPoolInitialized)
            return;
        doubleGearMaxCached = EffectiveDoubleGearDuration;
        doubleGearRemaining = doubleGearMaxCached;
        doubleGearPoolInitialized = true;
    }

    private void BeginDoubleGear()
    {
        if (!CanUseDoubleGear)
            return;

        // Second system turning on while a Dual ability is running needs full health.
        bool concurrent = CanUseDualAbilities && dualAbility != BassDualAbility.Default;
        if (concurrent && !IsAtFullHealth)
            return;

        EnsureDoubleGearPool();
        if (doubleGearRemaining <= 0.05f)
            return;

        if (concurrent)
            EngageConcurrentMode();

        // Hyper-granted pool (30% on Dual style / 50% once concurrent on Normal) — never start above the cap.
        doubleGearRemaining = Mathf.Min(doubleGearRemaining, EffectiveDoubleGearDuration);
        if (doubleGearRemaining <= 0.05f)
            return;

        doubleGearActive = true;
        doubleGearForcedWeaken = false;
        NoteCombatAttack();

        if (doubleGearVisualContainer != null)
            doubleGearVisualContainer.SetActive(true);

        CacheGearRests();
        RefreshStatusBar();

        savedDashAfterimageCount = dashAfterimageCount;
        dashAfterimageCount = Mathf.Max(1, doubleGearAfterimageCount);
        SetupDashAfterimages();

        // Same world slow as Count Hyper Speed — enemies/hazards/platforms only (Bass stays full speed).
        // Owner-keyed so Boss Bass DG / Flash Stop can stack without one End wiping the other.
        HyperSpeedWorldSlow.Begin(this, doubleGearWorldTimeScale, slowPlayer: false);
        ownsWorldSlow = true;

        if (effectSortingGroup != null)
        {
            savedSortingGroupOrder = effectSortingGroup.sortingOrder;
            effectSortingGroup.sortingOrder = savedSortingGroupOrder + 2;
        }

        if (doubleGearWorldTint == null)
            doubleGearWorldTint = GetComponent<BassDoubleGearWorldTint>()
                ?? gameObject.AddComponent<BassDoubleGearWorldTint>();
        doubleGearWorldTint.Apply(transform, doubleGearWorldBlackTint);
    }

    private void TickDoubleGear(float dt)
    {
        if (doubleGearCooldownRemaining > 0f)
        {
            doubleGearCooldownRemaining = Mathf.Max(0f, doubleGearCooldownRemaining - dt);
            if (doubleGearCooldownRemaining <= 0f)
            {
                doubleGearForcedWeaken = false;
                // Full pool back after forced exhaust cooldown.
                doubleGearRemaining = EffectiveDoubleGearDuration;
                doubleGearPoolInitialized = true;
            }
        }

        if (!doubleGearActive)
        {
            // Early-cancel only: duration regenerates in real time while off (no cooldown lockout).
            float poolMax = EffectiveDoubleGearDuration;
            if (CanUseDoubleGear
                && !IsDoubleGearForcedWeaken
                && doubleGearPoolInitialized
                && doubleGearRemaining < poolMax)
            {
                doubleGearRemaining = Mathf.Min(poolMax, doubleGearRemaining + dt);
            }
            return;
        }

        doubleGearRemaining = Mathf.Max(0f, doubleGearRemaining - dt);
        UpdateGearMotion(dt);

        if (doubleGearRemaining <= 0f)
            EndDoubleGear(force: false, exhausted: true);
    }

    private float GetDoubleGearUsed01()
    {
        float poolMax = EffectiveDoubleGearDuration;
        if (poolMax <= 0.001f)
            return 1f;
        float used = poolMax - doubleGearRemaining;
        return Mathf.Clamp01(used / poolMax);
    }

    private void EndDoubleGear(bool force, bool exhausted)
    {
        if (!doubleGearActive)
        {
            if (force)
            {
                doubleGearWorldTint?.Release();
                if (ownsWorldSlow)
                {
                    HyperSpeedWorldSlow.End(this);
                    ownsWorldSlow = false;
                }
            }
            return;
        }

        // Keep remaining pool on early cancel; clear only on forced exhaust.
        float poolLeft = Mathf.Max(0f, doubleGearRemaining);
        doubleGearActive = false;
        if (exhausted)
            doubleGearRemaining = 0f;

        ResetGearTransforms();
        doubleGearWorldTint?.Release();
        RefreshStatusBar();

        if (ownsWorldSlow)
        {
            HyperSpeedWorldSlow.End(this);
            ownsWorldSlow = false;
        }

        if (effectSortingGroup != null)
            effectSortingGroup.sortingOrder = savedSortingGroupOrder;

        dashAfterimageCount = Mathf.Max(1, savedDashAfterimageCount);
        SetupDashAfterimages();
        SetDashAfterimagesActive(false);
        afterimagesVisibleUntil = 0f;

        // Dual ability is kept: under Hyper it can run alongside Double Gear.
        ApplyDualAbilityToAnimator();

        if (!UsesDoubleGearStyle)
        {
            doubleGearForcedWeaken = false;
            doubleGearCooldownRemaining = 0f;
            return;
        }

        if (exhausted)
        {
            // Ran the pool dry — forced 16s weaken. Early cancel has no cooldown.
            doubleGearForcedWeaken = true;
            doubleGearCooldownRemaining = Mathf.Max(0.1f, doubleGearForcedCooldown);
        }
        else
        {
            doubleGearForcedWeaken = false;
            doubleGearCooldownRemaining = 0f;
            doubleGearRemaining = poolLeft;
        }
    }

    private void CycleDualAbility()
    {
        if (!CanUseDualAbilities)
            return;

        BassDualAbility previous = dualAbility;
        BassDualAbility next = (BassDualAbility)(((int)dualAbility + 1) % 5);

        // Second system turning on while Double Gear runs needs full health.
        if (previous == BassDualAbility.Default && next != BassDualAbility.Default
            && doubleGearActive && !IsAtFullHealth)
            return;

        dualAbility = next;
        if (previous == BassDualAbility.MetalLeaf && dualAbility != BassDualAbility.MetalLeaf)
            DestroyLeafShields();
        if (previous == BassDualAbility.QuickTime && dualAbility != BassDualAbility.QuickTime)
        {
            EndQuickTimeStop();
            quickTimeChargeTimer = 0f;
            StopQuickTimeChargeAura(immediate: true);
        }
        if (dualAbility != BassDualAbility.Default)
            NoteCombatAttack();
        if (dualAbility != BassDualAbility.Default && doubleGearActive)
            EngageConcurrentMode();
        ApplyDualAbilityToAnimator();
        RefreshStatusBar();
    }

    private void UpdateGearMotion(float dt)
    {
        if (blueGear == null && redGear == null)
            return;

        float poolMax = EffectiveDoubleGearDuration;
        float life01 = poolMax > 0.001f
            ? Mathf.Clamp01(doubleGearRemaining / poolMax)
            : 0f;
        // Faster as duration runs out (life01 → 0).
        float spin = Mathf.Lerp(doubleGearSpinDegPerSecEnd, doubleGearSpinDegPerSecStart, life01);

        if (blueGear != null)
            blueGear.Rotate(0f, 0f, spin * dt, Space.Self);
        if (redGear != null)
            redGear.Rotate(0f, 0f, -spin * dt, Space.Self);

        bool shake = doubleGearRemaining <= doubleGearShakeWindow && doubleGearRemaining > 0f;
        if (!gearRestsCached)
            return;

        if (shake)
        {
            Vector2 jolt = Random.insideUnitCircle * doubleGearShakeAmplitude;
            if (blueGear != null)
                blueGear.localPosition = blueGearRestLocal + (Vector3)jolt;
            if (redGear != null)
            {
                Vector2 jolt2 = Random.insideUnitCircle * doubleGearShakeAmplitude;
                redGear.localPosition = redGearRestLocal + (Vector3)jolt2;
            }
        }
        else
        {
            if (blueGear != null)
                blueGear.localPosition = blueGearRestLocal;
            if (redGear != null)
                redGear.localPosition = redGearRestLocal;
        }
    }

    /// <summary>
    /// Double Gear: red fill grows left→right as duration is used.
    /// Dual ammo: yellow fill starts full and shrinks right→left (remaining from the left).
    /// Hidden on Normal / Dual Default — only real abilities show the bar.
    /// </summary>
    private void RefreshStatusBar()
    {
        bool dualAbilityBar = CanUseDualAbilities && !doubleGearActive && dualAbility != BassDualAbility.Default;
        bool gearBar = doubleGearActive;
        bool show = dualAbilityBar || gearBar;

        if (doubleGearVisualContainer != null)
            doubleGearVisualContainer.SetActive(show);

        if (!show)
            return;

        SetGearSpritesVisible(gearBar);

        float alpha = Time.time - lastCombatAttackTime >= statusBarIdleFadeSeconds
            ? statusBarIdleAlpha
            : 1f;

        if (doubleGearDurationBar != null)
        {
            SpriteRenderer barSr = doubleGearDurationBar.GetComponent<SpriteRenderer>();
            if (barSr != null)
            {
                Color bc = barSr.color;
                bc.a = alpha;
                barSr.color = bc;
            }
        }

        if (doubleGearDurationFill == null)
            return;

        if (gearBar)
        {
            Color red = new Color(1f, 0.12f, 0.12f, alpha);
            SetBarFill(GetDoubleGearUsed01(), red);
            return;
        }

        float remaining01 = 1f;
        Color fillColor = dualAmmoBarColor;
        if (dualAbility == BassDualAbility.MetalLeaf && MetalLeafMax > 0)
            remaining01 = Mathf.Clamp01(metalLeafAmmo / MetalLeafMax);
        else if (dualAbility == BassDualAbility.BubbleAir && BubbleAirMax > 0)
        {
            remaining01 = Mathf.Clamp01(bubbleAirAmmo / BubbleAirMax);
            fillColor = bubbleAirAmmoBarColor;
        }
        else if (dualAbility == BassDualAbility.CrashingHeat && CrashingHeatMax > 0)
        {
            remaining01 = Mathf.Clamp01(crashingHeatAmmo / CrashingHeatMax);
            fillColor = crashingHeatAmmoBarColor;
        }
        else if (dualAbility == BassDualAbility.QuickTime && QuickTimeMax > 0)
        {
            remaining01 = Mathf.Clamp01(quickTimeAmmo / QuickTimeMax);
            fillColor = quickTimeAmmoBarColor;
        }

        fillColor.a = alpha;
        SetBarFill(remaining01, fillColor);
    }

    private void SetGearSpritesVisible(bool visible)
    {
        if (blueGear != null)
            blueGear.gameObject.SetActive(visible);
        if (redGear != null)
            redGear.gameObject.SetActive(visible);
    }

    private void SetBarFill(float fill01, Color color)
    {
        fill01 = Mathf.Clamp01(fill01);
        if (doubleGearDurationFill == null)
            return;

        // Stay inside the duration bar collider / frame (not larger than the bar art).
        float width = Mathf.Max(0.01f, durationBarFullWidth);
        float height = Mathf.Max(0.02f, durationBarFillHeight);

        Transform fillT = doubleGearDurationFill.transform;
        float filledWidth = width * fill01;
        fillT.localScale = new Vector3(Mathf.Max(0.001f, filledWidth), height, 1f);
        // Left edge fixed → shrinks from the right as fill01 drops (ammo), or grows right (DG used).
        fillT.localPosition = new Vector3((-width * 0.5f) + (filledWidth * 0.5f), 0f, 0f);
        doubleGearDurationFill.color = color;
        doubleGearDurationFill.enabled = fill01 > 0.001f;
    }

    private void NoteCombatAttack()
    {
        lastCombatAttackTime = Time.time;
    }

    public override void NotifyCollectableCrystalCollected()
    {
        if (!CanUseDualAbilities || dualAbility == BassDualAbility.Default)
            return;

        crystalsTowardAmmo++;
        int need;
        if (dualAbility == BassDualAbility.BubbleAir)
            need = Mathf.Max(1, bubbleAirCrystalsPerAmmo);
        else if (dualAbility == BassDualAbility.CrashingHeat)
            need = Mathf.Max(1, crashingHeatCrystalsPerAmmo);
        else
            need = Mathf.Max(1, crystalsPerAmmoRestore);

        if (crystalsTowardAmmo < need)
            return;

        crystalsTowardAmmo = 0;
        GrantAmmoToEquippedAbility(1);
    }

    private void GrantAmmoToEquippedAbility(int amount)
    {
        if (amount <= 0)
            return;

        if (dualAbility == BassDualAbility.MetalLeaf)
        {
            metalLeafAmmo = Mathf.Min(MetalLeafMax, metalLeafAmmo + amount);
            RefreshStatusBar();
        }
        else if (dualAbility == BassDualAbility.BubbleAir)
        {
            bubbleAirAmmo = Mathf.Min(BubbleAirMax, bubbleAirAmmo + amount);
            RefreshStatusBar();
        }
        else if (dualAbility == BassDualAbility.CrashingHeat)
        {
            crashingHeatAmmo = Mathf.Min(CrashingHeatMax, crashingHeatAmmo + amount);
            RefreshStatusBar();
        }
        else if (dualAbility == BassDualAbility.QuickTime)
        {
            quickTimeAmmo = Mathf.Min(QuickTimeMax, quickTimeAmmo + amount);
            RefreshStatusBar();
        }
    }

    /// <summary>Spend Crashing Heat ammo for optional explosion side-effects (stacks / balls). Skip if unpaid.</summary>
    public bool TrySpendCrashingHeatAmmo(int cost)
    {
        if (cost <= 0)
            return true;
        if (crashingHeatAmmo < cost)
            return false;
        crashingHeatAmmo = Mathf.Max(0f, crashingHeatAmmo - cost);
        NoteCombatAttack();
        RefreshStatusBar();
        return true;
    }

    private void ResolveDoubleGearRefs()
    {
        if (doubleGearVisualContainer == null)
        {
            Transform found = transform.Find("Double Gear Visual Container");
            if (found != null)
                doubleGearVisualContainer = found.gameObject;
        }

        if (doubleGearVisualContainer == null)
            return;

        Transform root = doubleGearVisualContainer.transform;
        if (blueGear == null)
            blueGear = root.Find("Blue Gear");
        if (redGear == null)
            redGear = root.Find("Red Gear");
        if (doubleGearDurationBar == null)
            doubleGearDurationBar = root.Find("Double Gear duration bar");
    }

    private void SetupDoubleGearVisuals()
    {
        if (doubleGearVisualContainer != null)
            doubleGearVisualContainer.SetActive(false);

        // Gears draw above the duration bar (same sorting layer).
        EnsureGearSortAboveBar();

        if (doubleGearDurationBar != null)
        {
            BoxCollider2D barCol = doubleGearDurationBar.GetComponent<BoxCollider2D>();
            if (barCol != null)
                barCol.isTrigger = true;

            if (doubleGearDurationBar.GetComponent<PhaseThroughPlayers>() == null)
                doubleGearDurationBar.gameObject.AddComponent<PhaseThroughPlayers>();

            SpriteRenderer barSr = doubleGearDurationBar.GetComponent<SpriteRenderer>();
            // Prefer the collider / inner track so the fill never draws larger than the bar.
            if (barCol != null)
            {
                durationBarFullWidth = Mathf.Max(0.05f, barCol.size.x * 0.96f);
                durationBarFillHeight = Mathf.Max(0.04f, barCol.size.y * 0.7f);
            }
            else if (barSr != null && barSr.sprite != null)
            {
                durationBarFullWidth = barSr.sprite.bounds.size.x * 0.9f;
                durationBarFillHeight = barSr.sprite.bounds.size.y * 0.55f;
            }

            if (doubleGearDurationFill == null)
            {
                Transform existing = doubleGearDurationBar.Find("Duration Fill");
                if (existing != null)
                    doubleGearDurationFill = existing.GetComponent<SpriteRenderer>();
            }

            if (doubleGearDurationFill == null && barSr != null)
            {
                var fillGo = new GameObject("Duration Fill");
                fillGo.transform.SetParent(doubleGearDurationBar, false);
                var fillSr = fillGo.AddComponent<SpriteRenderer>();
                fillSr.sprite = CreateWhiteFillSprite();
                fillSr.color = new Color(1f, 0.12f, 0.12f, 1f);
                fillSr.sortingLayerID = barSr.sortingLayerID;
                fillSr.sortingOrder = barSr.sortingOrder + 1;
                fillGo.transform.localScale = new Vector3(durationBarFullWidth, durationBarFillHeight, 1f);
                doubleGearDurationFill = fillSr;
            }
            else if (doubleGearDurationFill != null)
            {
                doubleGearDurationFill.color = new Color(1f, 0.12f, 0.12f, 1f);
            }
        }

        CacheGearRests();
        RefreshStatusBar();
    }

    private Sprite CreateWhiteFillSprite()
    {
        if (durationFillTexture == null)
        {
            durationFillTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            durationFillTexture.SetPixel(0, 0, Color.white);
            durationFillTexture.Apply();
            durationFillTexture.hideFlags = HideFlags.HideAndDontSave;
        }

        return Sprite.Create(
            durationFillTexture,
            new Rect(0f, 0f, 1f, 1f),
            new Vector2(0.5f, 0.5f),
            1f);
    }

    private void EnsureGearSortAboveBar()
    {
        int barOrder = 3;
        if (doubleGearDurationBar != null)
        {
            SpriteRenderer barSr = doubleGearDurationBar.GetComponent<SpriteRenderer>();
            if (barSr != null)
                barOrder = barSr.sortingOrder;
        }

        int gearOrder = barOrder + 2; // leave +1 for the red fill
        SetGearSortOrder(blueGear, gearOrder);
        SetGearSortOrder(redGear, gearOrder);
    }

    private static void SetGearSortOrder(Transform gear, int order)
    {
        if (gear == null)
            return;
        SpriteRenderer sr = gear.GetComponent<SpriteRenderer>();
        if (sr != null)
            sr.sortingOrder = order;
    }

    private void CacheGearRests()
    {
        if (blueGear != null)
            blueGearRestLocal = blueGear.localPosition;
        if (redGear != null)
            redGearRestLocal = redGear.localPosition;
        gearRestsCached = blueGear != null || redGear != null;
    }

    private void ResetGearTransforms()
    {
        if (!gearRestsCached)
            return;
        if (blueGear != null)
        {
            blueGear.localPosition = blueGearRestLocal;
            blueGear.localRotation = Quaternion.identity;
        }
        if (redGear != null)
        {
            redGear.localPosition = redGearRestLocal;
            redGear.localRotation = Quaternion.identity;
        }
    }

    private void ApplyDualAbilityToAnimator()
    {
        if (animator == null)
            return;

        // Dual outfits only while Dual System shop style is equipped (and gear is off).
        // Dual outfit stays on while Double Gear runs (only possible together under Hyper).
        int mode = CanUseDualAbilities
            ? (int)dualAbility
            : (int)BassDualAbility.Default;
        animator.SetInteger("AbilityMode", mode);
    }

    private Projectile GetActiveShotPrefab()
    {
        if (UsesDoubleGearStyle)
        {
            if (IsDoubleGearForcedWeaken)
                return purpleShotPrefab;
            if (doubleGearActive)
                return doubleGearShotPrefab != null ? doubleGearShotPrefab : greenShotPrefab;
            return greenShotPrefab != null ? greenShotPrefab : purpleShotPrefab;
        }

        // Hyper-granted Double Gear on Normal / Dual System style still fires gear shots.
        if (doubleGearActive)
            return doubleGearShotPrefab != null ? doubleGearShotPrefab : greenShotPrefab;

        // Default and Dual System use purple until Dual projectiles are wired.
        return purpleShotPrefab;
    }

    private float GetActiveShootCooldown()
    {
        return IsDoubleGearForcedWeaken
            ? Mathf.Max(0.016f, doubleGearWeakenShootCooldown)
            : Mathf.Max(0.016f, shootCooldown);
    }

    private void FireCurrentAttack()
    {
        NoteCombatAttack();

        if (CanUseDualAbilities && dualAbility == BassDualAbility.MetalLeaf)
        {
            FireMetalLeafAttack();
            return;
        }

        if (CanUseDualAbilities && dualAbility == BassDualAbility.BubbleAir)
        {
            FireBubbleAirAttack();
            return;
        }

        if (CanUseDualAbilities && dualAbility == BassDualAbility.CrashingHeat)
        {
            FireCrashingHeatAttack();
            return;
        }

        if (CanUseDualAbilities && dualAbility == BassDualAbility.QuickTime)
        {
            FireQuickTimeBoomerangs();
            return;
        }

        FireBusterShot();
    }

    private void FireMetalLeafAttack()
    {
        PruneLeafLists();
        if (liveLeaves.Count == 0)
            SummonLeafShields();

        if (!TryFireMetalBlade())
            shootCooldownTimer = Mathf.Max(0.016f, metalBladeShootCooldown);
    }

    private void SummonLeafShields()
    {
        if (leafShieldPrefab == null)
            return;

        DestroyLeafShields();
        int count = Mathf.Max(1, leafShieldCount);
        float step = 360f / count;
        for (int i = 0; i < count; i++)
        {
            BassLeafShield leaf = Instantiate(leafShieldPrefab, transform.position, Quaternion.identity);
            leaf.BeginOrbit(
                transform,
                i * step,
                leafOrbitRadius,
                leafOrbitDegreesPerSecond,
                leafShieldHealth,
                Mathf.Max(1, Mathf.RoundToInt(leafContactDamage * GetAbilityDamageMultiplier())));
            liveLeaves.Add(leaf);
        }

        SoundManager.Instance?.PlayBassFireRapid();
    }

    private bool TryFireMetalBlade()
    {
        PruneBladeList();
        if (metalBladePrefab == null)
            return false;
        if (metalLeafAmmo < 1f)
            return false;
        if (liveBlades.Count >= Mathf.Max(1, metalBladeMaxLive))
            return false;

        Vector2 aim = GetAimDirection();
        BeginShootAnimation(shootAnimDuration);
        PlayShootAnimatorTriggers();

        Vector3 spawnPos = GetFirePosition();
        BassMetalBlade blade = Instantiate(metalBladePrefab, spawnPos, Quaternion.identity);
        blade.Launch(
            aim,
            metalBladeSpeed + GetAbilitySpeedBonus(),
            metalBladeDamage * GetAbilityDamageMultiplier(),
            transform);
        blade.ConfigureDoubleGearBoosts(this);
        liveBlades.Add(blade);

        metalLeafAmmo = Mathf.Max(0f, metalLeafAmmo - 1f);
        shootCooldownTimer = Mathf.Max(0.016f, metalBladeShootCooldown);
        SoundManager.Instance?.PlayBassFireRapid();

        ReportTutorialAction(TutorialAction.Attack);
        if (aim.y > 0.2f)
            ReportTutorialAction(TutorialAction.AimShot);
        return true;
    }

    private void TickMetalLeafAmmo(float dt)
    {
        if (!CanUseDualAbilities || dualAbility != BassDualAbility.MetalLeaf)
        {
            metalLeafAmmoRecoverTimer = 0f;
            return;
        }

        if (metalLeafAmmo >= MetalLeafMax)
        {
            metalLeafAmmo = MetalLeafMax;
            metalLeafAmmoRecoverTimer = 0f;
            return;
        }

        metalLeafAmmoRecoverTimer += dt;
        float interval = Mathf.Max(0.05f, metalLeafAmmoRecoverSeconds);
        while (metalLeafAmmoRecoverTimer >= interval && metalLeafAmmo < MetalLeafMax)
        {
            metalLeafAmmoRecoverTimer -= interval;
            metalLeafAmmo = Mathf.Min(MetalLeafMax, metalLeafAmmo + 1f);
        }
    }

    private void FireBubbleAirAttack()
    {
        PruneBubbleList();
        int cost = Mathf.Max(1, bubbleAmmoCost);
        if (bubbleBulletPrefab == null)
        {
            shootCooldownTimer = Mathf.Max(0.016f, bubbleShootCooldown);
            return;
        }

        if (bubbleAirAmmo < cost)
        {
            shootCooldownTimer = Mathf.Max(0.016f, bubbleShootCooldown);
            return;
        }

        if (liveBubbles.Count >= Mathf.Max(1, bubbleMaxLive))
        {
            shootCooldownTimer = Mathf.Max(0.016f, bubbleShootCooldown);
            return;
        }

        Vector2 aim = GetAimDirection();
        if (aim.sqrMagnitude < 0.001f)
            aim = new Vector2(facingSign, 0f);

        BeginShootAnimation(shootAnimDuration);
        PlayShootAnimatorTriggers();

        Vector3 spawnPos = GetFirePosition();
        BassBubbleBullet bubble = Instantiate(bubbleBulletPrefab, spawnPos, Quaternion.identity);
        bubble.Launch(
            aim,
            bubbleLaunchSpeed + GetAbilitySpeedBonus(),
            bubbleDamage * GetAbilityDamageMultiplier(),
            bubbleSpaceSize,
            airTornadoPrefab,
            transform);
        bubble.ConfigureDoubleGearBoosts(this);
        liveBubbles.Add(bubble);

        bubbleAirAmmo = Mathf.Max(0f, bubbleAirAmmo - cost);
        shootCooldownTimer = Mathf.Max(0.016f, bubbleShootCooldown);
        SoundManager.Instance?.PlayBassFireRapid();

        ReportTutorialAction(TutorialAction.Attack);
        if (aim.y > 0.2f)
            ReportTutorialAction(TutorialAction.AimShot);
    }

    private void TickBubbleAirAmmo(float dt)
    {
        if (!CanUseDualAbilities || dualAbility != BassDualAbility.BubbleAir)
        {
            bubbleAirAmmoRecoverTimer = 0f;
            return;
        }

        if (bubbleAirAmmo >= BubbleAirMax)
        {
            bubbleAirAmmo = BubbleAirMax;
            bubbleAirAmmoRecoverTimer = 0f;
            return;
        }

        bubbleAirAmmoRecoverTimer += dt;
        float interval = Mathf.Max(0.05f, bubbleAirAmmoRecoverSeconds);
        while (bubbleAirAmmoRecoverTimer >= interval && bubbleAirAmmo < BubbleAirMax)
        {
            bubbleAirAmmoRecoverTimer -= interval;
            bubbleAirAmmo = Mathf.Min(BubbleAirMax, bubbleAirAmmo + 1f);
        }
    }

    private void PruneBubbleList()
    {
        liveBubbles.RemoveAll(b => b == null || !b.IsAlive);
    }

    private void FireCrashingHeatAttack()
    {
        int cost = Mathf.Max(1, crashBombAmmoCost);
        if (crashBombPrefab == null && crashBombPlusPrefab == null)
        {
            shootCooldownTimer = Mathf.Max(0.016f, crashingHeatShootCooldown);
            return;
        }

        if (crashingHeatAmmo < cost)
        {
            shootCooldownTimer = Mathf.Max(0.016f, crashingHeatShootCooldown);
            return;
        }

        float hpRatio = MaxHealth > 0 ? CurrentHealth / (float)MaxHealth : 1f;
        bool usePlus = hpRatio <= crashBombPlusHpRatio;
        BassCrashBomb prefab = usePlus && crashBombPlusPrefab != null
            ? crashBombPlusPrefab
            : crashBombPrefab;
        if (prefab == null)
            prefab = crashBombPlusPrefab;
        if (prefab == null)
        {
            shootCooldownTimer = Mathf.Max(0.016f, crashingHeatShootCooldown);
            return;
        }

        Vector2 aim = GetAimDirection();
        if (aim.sqrMagnitude < 0.001f)
            aim = new Vector2(facingSign, 0f);

        BeginShootAnimation(shootAnimDuration);
        PlayShootAnimatorTriggers();

        Vector3 spawnPos = GetFirePosition();
        BassCrashBomb bomb = Instantiate(prefab, spawnPos, Quaternion.identity);
        bomb.Launch(
            aim,
            crashBombSpeed + GetAbilitySpeedBonus(),
            usePlus,
            crashingHeatSpaceSize,
            transform,
            TrySpendCrashingHeatAmmo,
            crashBombExplosionPrefab,
            smallHeatFlarePrefab,
            heatFlareStackPrefab,
            heatFlareBallPrefab,
            crashHeatStunnedEffectPrefab != null ? crashHeatStunnedEffectPrefab : stunnedEffectPrefab);
        bomb.SetDamageMultiplier(GetAbilityDamageMultiplier());
        bomb.ConfigureDoubleGearBoosts(this);

        crashingHeatAmmo = Mathf.Max(0f, crashingHeatAmmo - cost);
        shootCooldownTimer = Mathf.Max(0.016f, crashingHeatShootCooldown);
        SoundManager.Instance?.PlayBassFireRapid();

        ReportTutorialAction(TutorialAction.Attack);
        if (aim.y > 0.2f)
            ReportTutorialAction(TutorialAction.AimShot);
    }

    private void TickCrashingHeatAmmo(float dt)
    {
        if (!CanUseDualAbilities || dualAbility != BassDualAbility.CrashingHeat)
        {
            crashingHeatAmmoRecoverTimer = 0f;
            return;
        }

        if (crashingHeatAmmo >= CrashingHeatMax)
        {
            crashingHeatAmmo = CrashingHeatMax;
            crashingHeatAmmoRecoverTimer = 0f;
            return;
        }

        crashingHeatAmmoRecoverTimer += dt;
        float interval = Mathf.Max(0.05f, crashingHeatAmmoRecoverSeconds);
        int gain = Mathf.Max(1, crashingHeatAmmoRecoverAmount);
        while (crashingHeatAmmoRecoverTimer >= interval && crashingHeatAmmo < CrashingHeatMax)
        {
            crashingHeatAmmoRecoverTimer -= interval;
            crashingHeatAmmo = Mathf.Min(CrashingHeatMax, crashingHeatAmmo + gain);
        }
    }

    // Heat Dash stays available during Double Gear (Crashing Heat only).
    private bool UsesHeatDash =>
        CanUseDualAbilities && dualAbility == BassDualAbility.CrashingHeat;

    protected override void OnDashStarted()
    {
        base.OnDashStarted();
        if (!UsesHeatDash || !isDashing)
        {
            heatDashActive = false;
            return;
        }

        heatDashActive = true;
        heatDashHitIds.Clear();
        // Invincible via TakeDamage override — do NOT set invincibilityTimer (that flickers Bass transparent).
        // Heat Dash also has no afterimages.
        SetDashAfterimagesActive(false);
        afterimagesVisibleUntil = 0f;
        afterimageFadeOutActive = false;
        BeginHeatDashEnemyPhase();
        SpawnHeatDashFlare();
        BeginHeatDashTrail();
        TickHeatDashTrail();
    }

    /// <summary>
    /// Plans equally spaced Small Heat Flares along the dash path.
    /// Each flare falls, and on landing spawns a Heat Flare Stack (same as Crash Bomb flares).
    /// </summary>
    private void BeginHeatDashTrail()
    {
        heatDashTrailDir = dashDirSign >= 0f ? 1f : -1f;
        heatDashTrailStartX = transform.position.x;
        heatDashTrailSpawned = 0;

        float space = Mathf.Max(0.01f, crashingHeatSpaceSize);
        int count = Mathf.Max(1, heatDashTrailStackCount);
        float minSpaces = Mathf.Max(0.1f, heatDashTrailMinSpaces);
        float maxSpaces = Mathf.Max(minSpaces, heatDashTrailMaxSpaces);
        float spacingSpaces = Mathf.Clamp(GetHeatDashDistance() / (count * space), minSpaces, maxSpaces);
        heatDashTrailSpacing = spacingSpaces * space;
    }

    private void TickHeatDashTrail()
    {
        if (!heatDashActive || !isDashing || smallHeatFlarePrefab == null || heatFlareStackPrefab == null)
            return;

        int count = Mathf.Max(1, heatDashTrailStackCount);
        float traveled = Mathf.Abs(transform.position.x - heatDashTrailStartX);
        while (heatDashTrailSpawned < count && traveled + 0.001f >= heatDashTrailSpawned * heatDashTrailSpacing)
        {
            float x = heatDashTrailStartX + heatDashTrailDir * heatDashTrailSpacing * heatDashTrailSpawned;
            heatDashTrailSpawned++;
            SpawnHeatDashTrailFlare(x);
        }
    }

    private void SpawnHeatDashTrailFlare(float worldX)
    {
        float spawnY = bodyCollider != null ? bodyCollider.bounds.center.y : transform.position.y;
        Vector3 spawn = new Vector3(worldX, spawnY, 0f);
        // Drop mostly downward with a tiny bit of dash-direction drift so the trail reads clearly.
        Vector2 vel = new Vector2(heatDashTrailDir * 1.5f, 2.5f);

        BassSmallHeatFlare flare = Instantiate(smallHeatFlarePrefab, spawn, Quaternion.identity);
        flare.ConfigureHostSorting(effectSortingGroup, spriteRenderer);
        flare.SetStackDamageMultiplier(GetAbilityDamageMultiplier());
        // Ammo is spent when the flare lands and spawns its stack (same as Crash Bomb flares).
        flare.LaunchStackSpawner(
            vel,
            spawn + Vector3.down,
            heatFlareStackPrefab,
            smallHeatFlarePrefab,
            crashingHeatSpaceSize,
            TrySpendCrashingHeatAmmo,
            heatFlareStackAmmoCost,
            transform);
    }

    protected override void OnDashEnded()
    {
        base.OnDashEnded();
        EndHeatDash();
        RestoreBassDashStats();
    }

    private void SpawnHeatDashFlare()
    {
        if (heatDashFlarePrefab == null)
            return;

        ResolveSparkSpawner();
        Transform anchor = sparkSpawner != null ? sparkSpawner : transform;
        // Pull flare back from "too forward" — offset opposite Bass facing on X.
        float backSign = facingSign >= 0f ? -1f : 1f;
        Vector3 spawnPos = anchor.position + new Vector3(backSign * Mathf.Abs(heatDashFlareOffsetX), 0f, 0f);
        BassHeatDashFlare flare = Instantiate(heatDashFlarePrefab, spawnPos, Quaternion.identity);
        flare.Begin(anchor, dashDuration, spriteRenderer, effectSortingGroup, backSign * Mathf.Abs(heatDashFlareOffsetX));
    }

    private void BeginHeatDashEnemyPhase()
    {
        EndHeatDashEnemyPhase();
        if (bodyCollider == null)
            return;

        // FindObjectsByType can't query interfaces — scan MonoBehaviours that implement ICommonEnemy.
        MonoBehaviour[] all = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] is not ICommonEnemy)
                continue;
            Collider2D[] cols = all[i].GetComponentsInChildren<Collider2D>(true);
            for (int c = 0; c < cols.Length; c++)
            {
                Collider2D col = cols[c];
                if (col == null || col.isTrigger)
                    continue;
                Physics2D.IgnoreCollision(bodyCollider, col, true);
                heatDashIgnored.Add(col);
            }
        }

        Boss[] bosses = FindObjectsByType<Boss>(FindObjectsSortMode.None);
        for (int i = 0; i < bosses.Length; i++)
        {
            if (bosses[i] == null) continue;
            Collider2D[] cols = bosses[i].GetComponentsInChildren<Collider2D>(true);
            for (int c = 0; c < cols.Length; c++)
            {
                Collider2D col = cols[c];
                if (col == null || col.isTrigger)
                    continue;
                Physics2D.IgnoreCollision(bodyCollider, col, true);
                heatDashIgnored.Add(col);
            }
        }
    }

    private void EndHeatDashEnemyPhase()
    {
        if (bodyCollider != null)
        {
            for (int i = 0; i < heatDashIgnored.Count; i++)
            {
                if (heatDashIgnored[i] != null)
                    Physics2D.IgnoreCollision(bodyCollider, heatDashIgnored[i], false);
            }
        }

        heatDashIgnored.Clear();
    }

    private void EndHeatDash()
    {
        heatDashActive = false;
        heatDashHitIds.Clear();
        EndHeatDashEnemyPhase();
    }

    private void TickHeatDashContact()
    {
        if (!heatDashActive || !isDashing || bodyCollider == null || heatDashContactDamage <= 0)
            return;

        ContactFilter2D filter = new ContactFilter2D();
        filter.useTriggers = true;
        filter.NoFilter();
        var buf = new Collider2D[24];
        int count = Physics2D.OverlapCollider(bodyCollider, filter, buf);
        for (int i = 0; i < count; i++)
        {
            Collider2D other = buf[i];
            if (other == null)
                continue;

            Component key = other.GetComponentInParent<Boss>() as Component
                ?? other.GetComponentInParent<ICommonEnemy>() as Component;
            if (key == null)
                continue;
            if (!heatDashHitIds.Add(key.GetInstanceID()))
                continue;

            BassCrashHeatCombat.ApplyDamageOnly(
                other, heatDashContactDamage * GetAbilityDamageMultiplier(), guaranteeStun: false);
        }
    }

    public override void TakeDamage(int amount, Transform hitSource, bool applyKnockback)
    {
        if (heatDashActive && isDashing)
            return;
        base.TakeDamage(amount, hitSource, applyKnockback);
    }

    private void DestroyLeafShields()
    {
        for (int i = 0; i < liveLeaves.Count; i++)
        {
            if (liveLeaves[i] != null)
                liveLeaves[i].DestroyLeaf();
        }

        liveLeaves.Clear();
    }

    private void PruneLeafLists()
    {
        liveLeaves.RemoveAll(leaf => leaf == null || !leaf.IsAlive);
    }

    private void PruneBladeList()
    {
        liveBlades.RemoveAll(blade => blade == null || blade.Projectile == null || blade.Projectile.IsResolvingHit);
        // Also drop despawned blades (destroyed objects already null).
        for (int i = liveBlades.Count - 1; i >= 0; i--)
        {
            if (liveBlades[i] == null)
                liveBlades.RemoveAt(i);
        }
    }

    private void FireBusterShot()
    {
        Projectile prefab = GetActiveShotPrefab();
        if (prefab == null)
            prefab = purpleShotPrefab;
        if (prefab == null)
            return;

        Vector2 aim = GetAimDirection();
        BeginShootAnimation(shootAnimDuration);
        PlayShootAnimatorTriggers();

        Vector3 spawnPos = GetFirePosition();
        Projectile shot = Instantiate(prefab, spawnPos, Quaternion.identity);
        // Attack Style level 5: Double Gear buster shots hit for double (ability DG×2 is separate).
        float styleMult = StyleLevel >= PlayerUpgrades.MaxLevel ? 2f : 1f;
        float shotDamage = doubleGearActive ? prefab.Damage * styleMult : prefab.Damage;
        shot.Launch(aim, prefab.Speed, shotDamage, transform);

        if (busterBlastPrefab != null)
        {
            Transform muzzle = firePoint != null ? firePoint : transform;
            VisualEffects.SpawnBusterBlast(busterBlastPrefab, muzzle, shot);
        }

        shootCooldownTimer = GetActiveShootCooldown();
        SoundManager.Instance?.PlayBassFireRapid();

        ReportTutorialAction(TutorialAction.Attack);
        if (aim.y > 0.2f)
            ReportTutorialAction(TutorialAction.AimShot);
    }

    protected override Vector2 GetAimDirection()
    {
        if (IsAimingDown())
            return new Vector2(facingSign, -1f).normalized;

        return base.GetAimDirection();
    }

    protected override void ApplyHorizontalMove()
    {
        if (lockMovementWhileGroundShooting && isGrounded && isShooting)
        {
            ApplyStandInPlaceMove();
            return;
        }

        base.ApplyHorizontalMove();
    }

    protected override bool BlocksRunAnimationWhileShooting()
    {
        return isShooting;
    }

    protected override void UpdateAnimator()
    {
        base.UpdateAnimator();
        if (animator == null)
            return;

        animator.SetBool("AimDown", IsAimingDown());
        ApplyDualAbilityToAnimator();
    }

    private void PlayShootAnimatorTriggers()
    {
        if (animator == null)
            return;

        if (IsAimingUp())
            animator.SetTrigger("ShootUp");
        else if (IsAimingDown())
            animator.SetTrigger("ShootDown");
        else if (IsGrounded)
            animator.SetTrigger("Shoot");
        else if (rb != null && rb.linearVelocity.y >= 0f)
            animator.SetTrigger("JumpShot");
        else
            animator.SetTrigger("FallShot");
    }

    private void TickQuickTimeAmmo(float dt)
    {
        if (!CanUseDualAbilities || dualAbility != BassDualAbility.QuickTime)
        {
            quickTimeAmmoRecoverTimer = 0f;
            return;
        }

        if (quickTimeAmmo >= QuickTimeMax)
        {
            quickTimeAmmo = QuickTimeMax;
            quickTimeAmmoRecoverTimer = 0f;
            return;
        }

        quickTimeAmmoRecoverTimer += dt;
        float interval = Mathf.Max(0.05f, quickTimeAmmoRecoverSeconds);
        while (quickTimeAmmoRecoverTimer >= interval && quickTimeAmmo < QuickTimeMax)
        {
            quickTimeAmmoRecoverTimer -= interval;
            quickTimeAmmo = Mathf.Min(QuickTimeMax, quickTimeAmmo + 1f);
        }
    }

    private void TickQuickTimeCharge(float dt)
    {
        if (!CanUseDualAbilities || dualAbility != BassDualAbility.QuickTime)
        {
            quickTimeChargeTimer = 0f;
            StopQuickTimeChargeAura(immediate: false);
            return;
        }

        bool holding = attackHeld || IsAttackButtonHeld();
        if (!holding || quickTimeAmmo < quickTimeTimeStopCost || quickTimeStopActive)
        {
            if (!holding)
                quickTimeChargeTimer = 0f;
            else if (quickTimeAmmo < quickTimeTimeStopCost)
                quickTimeChargeTimer = 0f;
            StopQuickTimeChargeAura(immediate: false);
            return;
        }

        quickTimeChargeTimer += dt;
        UpdateQuickTimeChargeAura();
    }

    private void ResolveSparkSpawner()
    {
        if (sparkSpawner != null)
            return;

        sparkSpawner = transform.Find("Spark Spawner");
        if (sparkSpawner == null)
            sparkSpawner = transform.Find("Spark Spawner ");
        if (sparkSpawner != null)
            return;

        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (child != null && child.name != null && child.name.Trim() == "Spark Spawner")
            {
                sparkSpawner = child;
                return;
            }
        }
    }

    private bool quickTimeSparkEmitterWired;

    private void EnsureQuickTimeSparkEmitter()
    {
        if (quickTimeSparkEmitter == null)
            quickTimeSparkEmitter = GetComponent<HexSparkEmitter>();
        if (quickTimeSparkEmitter == null)
            quickTimeSparkEmitter = gameObject.AddComponent<HexSparkEmitter>();

        if (quickTimeSparkEmitterWired && quickTimeSparkEmitter != null)
            return;

        ResolveSparkSpawner();
        if (sparkSpawner != null)
            quickTimeSparkEmitter.SetSparkBox(sparkSpawner);

        // Charge aura only — hit sparks use Kit-style Bass Stunned effect, not Hex bursts.
        // Do not touch Streaming here; Update/Stop drive that each frame.
        quickTimeSparkEmitter.EnableHurtBursts = false;
        quickTimeSparkEmitter.InwardGatherUntilFull = true;
        quickTimeSparkEmitter.Tint = quickTimeChargeAuraColor;

        if (quickTimeSparkPrefabs != null && quickTimeSparkPrefabs.Length > 0)
            quickTimeSparkEmitter.SetSparkPrefabs(quickTimeSparkPrefabs);

        quickTimeSparkEmitterWired = quickTimeSparkEmitter != null;
    }

    private void UpdateQuickTimeChargeAura()
    {
        EnsureQuickTimeSparkEmitter();
        if (quickTimeSparkEmitter == null)
            return;

        float charge01 = Mathf.Clamp01(quickTimeChargeTimer / Mathf.Max(0.01f, quickTimeChargeSeconds));
        // MM7-style ring → center while charging; at max, Hex's usual rising aura.
        quickTimeSparkEmitter.Tint = quickTimeChargeAuraColor;
        quickTimeSparkEmitter.InwardGatherUntilFull = true;
        quickTimeSparkEmitter.EnableHurtBursts = false;
        quickTimeSparkEmitter.ChargeProgress = charge01;
        quickTimeSparkEmitter.FullCharge = charge01 >= 0.999f;
        quickTimeSparkEmitter.Streaming = true;
    }

    private void StopQuickTimeChargeAura(bool immediate)
    {
        if (quickTimeSparkEmitter == null)
            return;

        quickTimeSparkEmitter.Streaming = false;
        quickTimeSparkEmitter.FullCharge = false;
        quickTimeSparkEmitter.ChargeProgress = 0f;
        if (immediate)
            quickTimeSparkEmitter.StopImmediate();
    }

    private void TryReleaseQuickTimeCharge()
    {
        if (!CanUseDualAbilities || dualAbility != BassDualAbility.QuickTime)
            return;
        if (quickTimeStopActive)
            return;
        if (quickTimeChargeTimer < quickTimeChargeSeconds)
            return;
        if (quickTimeAmmo < quickTimeTimeStopCost)
            return;

        BeginQuickTimeStop();
    }

    private void FireQuickTimeBoomerangs()
    {
        PruneBoomerangList();
        int volley = Mathf.Max(1, quickBoomerangVolleyCount);
        int maxLive = Mathf.Max(volley, quickBoomerangMaxLive);
        BassQuickBoomerang prefab = ResolveQuickBoomerangPrefab();
        if (prefab == null)
        {
            Debug.LogWarning("[Bass QuickTime] Quick Boomerang prefab missing — cannot fire.");
            shootCooldownTimer = Mathf.Max(0.016f, quickBoomerangShootCooldown);
            return;
        }

        if (quickTimeAmmo < quickTimeBoomerangVolleyCost)
        {
            shootCooldownTimer = Mathf.Max(0.016f, quickBoomerangShootCooldown);
            return;
        }

        if (liveBoomerangs.Count + volley > maxLive)
        {
            shootCooldownTimer = Mathf.Max(0.016f, quickBoomerangShootCooldown);
            return;
        }

        Vector2 aim = GetAimDirection();
        if (aim.sqrMagnitude < 0.001f)
            aim = new Vector2(facingSign, 0f);

        BeginShootAnimation(shootAnimDuration);
        PlayShootAnimatorTriggers();

        Vector3 spawnPos = GetFirePosition();
        float spread = Mathf.Max(0f, quickBoomerangSpreadDegrees);
        for (int i = 0; i < volley; i++)
        {
            float t = volley <= 1 ? 0f : (i / (float)(volley - 1)) * 2f - 1f;
            float angle = t * spread;
            Vector2 dir = Quaternion.Euler(0f, 0f, angle) * aim;

            BassQuickBoomerang boomerang = Instantiate(prefab, spawnPos, Quaternion.identity);
            boomerang.gameObject.SetActive(true);
            boomerang.Launch(
                dir,
                quickBoomerangSpeed + GetAbilitySpeedBonus(),
                quickBoomerangDamage * GetAbilityDamageMultiplier(),
                quickBoomerangOutDistanceSpaces,
                quickTimeSpaceSize,
                transform);
            boomerang.ConfigureDoubleGearBoosts(this);
            liveBoomerangs.Add(boomerang);
        }

        quickTimeAmmo = Mathf.Max(0f, quickTimeAmmo - quickTimeBoomerangVolleyCost);
        shootCooldownTimer = Mathf.Max(0.016f, quickBoomerangShootCooldown);
        SoundManager.Instance?.PlayBassFireRapid();

        ReportTutorialAction(TutorialAction.Attack);
        if (aim.y > 0.2f)
            ReportTutorialAction(TutorialAction.AimShot);
    }

    private BassQuickBoomerang ResolveQuickBoomerangPrefab()
    {
        if (quickBoomerangPrefab != null)
            return quickBoomerangPrefab;
        if (runtimeBoomerangTemplate != null)
            return runtimeBoomerangTemplate;

        runtimeBoomerangTemplate = BuildRuntimeQuickBoomerangTemplate();
        return runtimeBoomerangTemplate;
    }

    private BassFlashTimeStar ResolveFlashTimeStarPrefab()
    {
        if (flashTimeStarPrefab != null)
            return flashTimeStarPrefab;
        if (runtimeFlashStarTemplate != null)
            return runtimeFlashStarTemplate;

        runtimeFlashStarTemplate = BuildRuntimeFlashTimeStarTemplate();
        return runtimeFlashStarTemplate;
    }

    private BassQuickBoomerang BuildRuntimeQuickBoomerangTemplate()
    {
        var go = new GameObject("Quick Boomerang (Runtime)");
        go.SetActive(false);
        DontDestroyOnLoad(go);

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = quickBoomerangSprite;
        if (sr.sprite == null && metalBladePrefab != null)
        {
            SpriteRenderer src = metalBladePrefab.GetComponent<SpriteRenderer>();
            if (src != null)
                sr.sprite = src.sprite;
        }

        sr.sortingOrder = 5;

        BoxCollider2D col = go.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        col.size = sr.sprite != null
            ? new Vector2(Mathf.Max(0.35f, sr.sprite.bounds.size.x * 0.85f), Mathf.Max(0.35f, sr.sprite.bounds.size.y * 0.85f))
            : new Vector2(0.5f, 1.1f);

        Rigidbody2D body = go.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Kinematic;
        body.gravityScale = 0f;
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        go.AddComponent<Projectile>();
        return go.AddComponent<BassQuickBoomerang>();
    }

    private BassFlashTimeStar BuildRuntimeFlashTimeStarTemplate()
    {
        var go = new GameObject("Flash Time Star (Runtime)");
        go.SetActive(false);
        DontDestroyOnLoad(go);

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = flashTimeStarSprite;
        sr.sortingOrder = 40;

        Animator anim = go.GetComponent<Animator>();
        if (anim == null)
            anim = go.AddComponent<Animator>();
        if (flashTimeStarController != null)
            anim.runtimeAnimatorController = flashTimeStarController;
        anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        return go.AddComponent<BassFlashTimeStar>();
    }

    private void BeginQuickTimeStop()
    {
        if (quickTimeStopActive)
            return;
        if (quickTimeAmmo < quickTimeTimeStopCost)
            return;

        quickTimeAmmo = Mathf.Max(0f, quickTimeAmmo - quickTimeTimeStopCost);
        quickTimeChargeTimer = 0f;
        StopQuickTimeChargeAura(immediate: true);
        quickTimeStopActive = true;
        quickTimeStopRemaining = Mathf.Max(0.25f, quickTimeStopDuration);
        NoteCombatAttack();

        // Dedicated owner so Time Stop can deepen freeze without stealing Double Gear's layer.
        HyperSpeedWorldSlow.Begin(QuickTimeSlowOwner, 0f, slowPlayer: false);
        ownsQuickTimeWorldSlow = true;

        SpawnFlashTimeStars();
        RefreshStatusBar();
    }

    private void TickQuickTimeStop(float dt)
    {
        if (!quickTimeStopActive)
            return;

        quickTimeStopRemaining -= dt;
        if (quickTimeStopRemaining <= 0f)
            EndQuickTimeStop();
    }

    private void EndQuickTimeStop()
    {
        if (!quickTimeStopActive && liveFlashStars.Count == 0 && !ownsQuickTimeWorldSlow)
            return;

        quickTimeStopActive = false;
        quickTimeStopRemaining = 0f;
        ClearFlashTimeStars();

        if (ownsQuickTimeWorldSlow)
        {
            HyperSpeedWorldSlow.End(QuickTimeSlowOwner);
            ownsQuickTimeWorldSlow = false;
        }
    }

    private void SpawnFlashTimeStars()
    {
        ClearFlashTimeStars();
        BassFlashTimeStar prefab = ResolveFlashTimeStarPrefab();
        if (prefab == null)
        {
            Debug.LogWarning("[Bass QuickTime] Flash Time Star prefab missing — Time Stop has no stars.");
            return;
        }

        Camera cam = Camera.main;
        if (cam == null)
            return;

        float depth = Mathf.Abs(cam.transform.position.z);
        float pad = Mathf.Max(0f, flashTimeStarViewportPad);
        int count = Mathf.Max(8, flashTimeStarCount);
        for (int i = 0; i < count; i++)
        {
            // Wide scatter: well past the screen edges so the field feels sparse / MM2-large.
            Vector2 vp = new Vector2(
                Random.Range(-pad, 1f + pad),
                Random.Range(-pad, 1f + pad));
            BassFlashTimeStar star = Instantiate(prefab);
            star.gameObject.SetActive(true);

            // Runtime templates may lack the controller — keep the twinkle anim alive.
            Animator anim = star.GetComponent<Animator>();
            if (anim != null && anim.runtimeAnimatorController == null && flashTimeStarController != null)
                anim.runtimeAnimatorController = flashTimeStarController;

            star.Place(cam, vp, depth);
            star.PlayTwinkle(Random.value);
            liveFlashStars.Add(star);
        }
    }

    private void ClearFlashTimeStars()
    {
        for (int i = 0; i < liveFlashStars.Count; i++)
        {
            if (liveFlashStars[i] != null)
                Destroy(liveFlashStars[i].gameObject);
        }

        liveFlashStars.Clear();
    }

    private void PruneBoomerangList()
    {
        liveBoomerangs.RemoveAll(b => b == null);
    }

    protected override void OnValidate()
    {
        base.OnValidate();
        shootCooldown = Mathf.Max(0.016f, shootCooldown);
        doubleGearWeakenShootCooldown = Mathf.Max(shootCooldown, doubleGearWeakenShootCooldown);
        bassBaseAirJumps = Mathf.Max(0, bassBaseAirJumps);
        bassMoveSpeed = Mathf.Max(0f, bassMoveSpeed);
        shootAnimDuration = Mathf.Max(0.05f, shootAnimDuration);
        doubleGearDuration = Mathf.Max(0.1f, doubleGearDuration);
        doubleGearForcedCooldown = Mathf.Max(0.1f, doubleGearForcedCooldown);
        doubleGearWorldTimeScale = Mathf.Clamp(doubleGearWorldTimeScale, 0.05f, 1f);
        doubleGearAfterimageAlphaEnd = Mathf.Clamp(doubleGearAfterimageAlphaEnd, 0.05f, doubleGearAfterimageAlphaStart);
        doubleGearShakeWindow = Mathf.Max(0.05f, doubleGearShakeWindow);
        doubleGearMoveSpeedBonus = Mathf.Max(0f, doubleGearMoveSpeedBonus);
        doubleGearAfterimageCount = Mathf.Max(1, doubleGearAfterimageCount);
        metalLeafMaxAmmo = Mathf.Max(1, metalLeafMaxAmmo);
        metalLeafAmmoRecoverSeconds = Mathf.Max(0.05f, metalLeafAmmoRecoverSeconds);
        metalBladeShootCooldown = Mathf.Max(0.016f, metalBladeShootCooldown);
        metalBladeMaxLive = Mathf.Max(1, metalBladeMaxLive);
        leafShieldCount = Mathf.Max(1, leafShieldCount);
        leafShieldHealth = Mathf.Max(1, leafShieldHealth);
        quickTimeMaxAmmo = Mathf.Max(1, quickTimeMaxAmmo);
        quickTimeAmmoRecoverSeconds = Mathf.Max(0.05f, quickTimeAmmoRecoverSeconds);
        quickTimeBoomerangVolleyCost = Mathf.Max(1, quickTimeBoomerangVolleyCost);
        quickTimeTimeStopCost = Mathf.Max(1, quickTimeTimeStopCost);
        quickTimeChargeSeconds = Mathf.Max(0.1f, quickTimeChargeSeconds);
        quickTimeStopDuration = Mathf.Max(0.25f, quickTimeStopDuration);
        quickBoomerangShootCooldown = Mathf.Max(0.016f, quickBoomerangShootCooldown);
        quickBoomerangMaxLive = Mathf.Max(1, quickBoomerangMaxLive);
        quickBoomerangVolleyCount = Mathf.Max(1, quickBoomerangVolleyCount);
        quickBoomerangOutDistanceSpaces = Mathf.Max(0.5f, quickBoomerangOutDistanceSpaces);
        flashTimeStarCount = Mathf.Max(1, flashTimeStarCount);
        flashTimeStarViewportPad = Mathf.Max(0f, flashTimeStarViewportPad);
        bubbleAirMaxAmmo = Mathf.Max(1, bubbleAirMaxAmmo);
        bubbleAirAmmoRecoverSeconds = Mathf.Max(0.05f, bubbleAirAmmoRecoverSeconds);
        bubbleAmmoCost = Mathf.Max(1, bubbleAmmoCost);
        bubbleShootCooldown = Mathf.Max(0.016f, bubbleShootCooldown);
        bubbleLaunchSpeed = Mathf.Max(0.5f, bubbleLaunchSpeed);
        bubbleMaxLive = Mathf.Max(1, bubbleMaxLive);
        bubbleSpaceSize = Mathf.Max(0.01f, bubbleSpaceSize);
        bubbleAirCrystalsPerAmmo = Mathf.Max(1, bubbleAirCrystalsPerAmmo);
        crashingHeatMaxAmmo = Mathf.Max(1, crashingHeatMaxAmmo);
        crashingHeatAmmoRecoverSeconds = Mathf.Max(0.05f, crashingHeatAmmoRecoverSeconds);
        crashingHeatAmmoRecoverAmount = Mathf.Max(1, crashingHeatAmmoRecoverAmount);
        crashBombAmmoCost = Mathf.Max(1, crashBombAmmoCost);
        heatFlareStackAmmoCost = Mathf.Max(0, heatFlareStackAmmoCost);
        heatBallAmmoCost = Mathf.Max(0, heatBallAmmoCost);
        crashingHeatShootCooldown = Mathf.Max(0.016f, crashingHeatShootCooldown);
        crashBombSpeed = Mathf.Max(0.5f, crashBombSpeed);
        crashingHeatCrystalsPerAmmo = Mathf.Max(1, crashingHeatCrystalsPerAmmo);
        heatDashDistanceMultiplier = Mathf.Max(1f, heatDashDistanceMultiplier);
        heatDashSpeedMultiplier = Mathf.Max(1f, heatDashSpeedMultiplier);
        heatDashContactDamage = Mathf.Max(0, heatDashContactDamage);
        heatDashTrailStackCount = Mathf.Max(1, heatDashTrailStackCount);
        heatDashTrailMinSpaces = Mathf.Max(0.1f, heatDashTrailMinSpaces);
        heatDashTrailMaxSpaces = Mathf.Max(heatDashTrailMinSpaces, heatDashTrailMaxSpaces);
        hyperMaxHealthPerLevel = Mathf.Max(0, hyperMaxHealthPerLevel);
        styleDoubleGearSecondsPerLevel = Mathf.Max(0f, styleDoubleGearSecondsPerLevel);
        styleDualAmmoPerLevel = Mathf.Max(0, styleDualAmmoPerLevel);
    }
}
