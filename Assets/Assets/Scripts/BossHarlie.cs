using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Boss version of Harlie (Boss Hex reuses it with a red wave prefab and a damage multiplier).
/// Mirrors playable Harlie: Kick → Slash 1 → 2 → 3 with a slash wave per slash, air slash, falling
/// jump attack with pogo, invincible 7-space dash (3 air dashes), double jump, charge slash / kick
/// slides and the charged air slash. Boss-only patterns are takes on Lace / Hornet (Hollow Knight),
/// Chozo Robot Soldier (Metroid Dread) and Omega Zero (Mega Man Zero) moves that match one of Harlie's own
/// moves (lunge = kick slide, cross-up / flash dash = dash, dive / plunge = falling jump attack, flurry /
/// triple saber = slash combo, rising saber = air slash). Never flinches (sparks only).
/// Gets a little faster per life bar lost, enrages at ≤50% HP and adds the Omega moves at ≤30% HP.
/// Patterns are not random: the player's range picks a zone and each zone cycles a fixed rotation.
/// Straight air slashes only come out when the player is actually in reach.
/// Her slash waves can be destroyed by the player (they have their own HP).
/// Boss Hex turns on Use Hex Moves (more Omega Zero): a dive stab whose landing fires a 5-wave upward fan,
/// three-way charged waves, V-Bot summons, and her own rotations built around them. At ≤50% HP she also
/// gets a ring of orbiting V-Bots; each slash or kick sends one straight at where the player is.
/// Boss Harlie at ≤50% HP summons 8 Silver Swords that stay until she dies. On their own timer, alongside
/// her normal patterns, they cycle base attack, Grandmother Silk needles, Lace spiral and Moorwing blades.
/// </summary>
public class BossHarlie : Boss
{
    private enum Pattern
    {
        GroundCombo,
        DashCancelCombo,
        PogoHop,
        ChargeSlide,
        LaceCrossUp,
        HornetDive,
        LaceLunge,
        ChozoPlunge,
        FlashDash,
        RisingAirSlash,
        ChargedAirSlash,
        LaceFlurry,
        OmegaRisingSaber,
        OmegaTripleSaber,
        OmegaChargedWave,
        HexDiveSlam,
        HexVBots
    }

    private enum WaveShape
    {
        Forward,
        BothWays,
        Triple,
        Fan
    }

    private enum Zone
    {
        Close,
        Mid,
        Far,
        AntiAir
    }

    private enum Motion
    {
        Stop,
        Free,
        Walk,
        Velocity,
        Hang,
        Slide
    }

    private const int ComboKick = 0;
    private const int ComboSlash1 = 1;
    private const int ComboSlash2 = 2;
    private const int ComboSlash3 = 3;

    // Each zone cycles its rotation in order, so the player can learn what comes next.
    private static readonly Pattern[] CloseRotation =
        { Pattern.GroundCombo, Pattern.PogoHop, Pattern.LaceFlurry, Pattern.LaceCrossUp };
    private static readonly Pattern[] MidRotation = { Pattern.DashCancelCombo, Pattern.HornetDive, Pattern.ChargeSlide };
    private static readonly Pattern[] FarRotation = { Pattern.LaceLunge, Pattern.ChozoPlunge, Pattern.FlashDash };
    private static readonly Pattern[] AntiAirRotation = { Pattern.RisingAirSlash, Pattern.ChargedAirSlash };

    // ≤30% HP: the same rotations with the Omega Zero moves slotted in.
    private static readonly Pattern[] CloseRotationDesperate =
        { Pattern.GroundCombo, Pattern.OmegaRisingSaber, Pattern.LaceFlurry, Pattern.PogoHop, Pattern.LaceCrossUp };
    private static readonly Pattern[] MidRotationDesperate =
        { Pattern.DashCancelCombo, Pattern.HornetDive, Pattern.OmegaTripleSaber, Pattern.ChargeSlide };
    private static readonly Pattern[] FarRotationDesperate =
        { Pattern.LaceLunge, Pattern.ChozoPlunge, Pattern.OmegaChargedWave, Pattern.FlashDash };
    private static readonly Pattern[] AntiAirRotationDesperate =
        { Pattern.RisingAirSlash, Pattern.OmegaRisingSaber, Pattern.ChargedAirSlash };

    // Boss Hex: the dive slam (its fan doubles as anti-air) and V-Bots slotted into the same structure.
    private static readonly Pattern[] HexCloseRotation =
        { Pattern.GroundCombo, Pattern.PogoHop, Pattern.HexDiveSlam, Pattern.LaceFlurry, Pattern.LaceCrossUp };
    private static readonly Pattern[] HexMidRotation =
        { Pattern.DashCancelCombo, Pattern.HexVBots, Pattern.HornetDive, Pattern.ChargeSlide };
    private static readonly Pattern[] HexFarRotation =
        { Pattern.LaceLunge, Pattern.HexVBots, Pattern.ChozoPlunge, Pattern.FlashDash };
    private static readonly Pattern[] HexAntiAirRotation =
        { Pattern.RisingAirSlash, Pattern.HexDiveSlam, Pattern.ChargedAirSlash };

    private static readonly Pattern[] HexCloseRotationDesperate =
    {
        Pattern.GroundCombo, Pattern.OmegaRisingSaber, Pattern.LaceFlurry, Pattern.HexDiveSlam, Pattern.PogoHop,
        Pattern.LaceCrossUp
    };
    private static readonly Pattern[] HexMidRotationDesperate =
        { Pattern.DashCancelCombo, Pattern.HexVBots, Pattern.OmegaTripleSaber, Pattern.HornetDive, Pattern.ChargeSlide };
    private static readonly Pattern[] HexFarRotationDesperate =
        { Pattern.LaceLunge, Pattern.HexVBots, Pattern.OmegaChargedWave, Pattern.ChozoPlunge, Pattern.FlashDash };
    private static readonly Pattern[] HexAntiAirRotationDesperate =
        { Pattern.RisingAirSlash, Pattern.HexDiveSlam, Pattern.OmegaRisingSaber, Pattern.ChargedAirSlash };

    // Back, back-up, up, forward-up, forward (x is flipped to her facing).
    private static readonly Vector2[] HexSlamFan =
    {
        new Vector2(-1f, 0f), new Vector2(-1f, 1f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f)
    };

    [Header("Boss Harlie - Identity")]
    [Tooltip("Character this boss is a copy of (Harlie, Hex, …). Boss id becomes \"Boss\" + this.")]
    [SerializeField] private string characterId = "Harlie";
    [Tooltip("Multiplies every hit (Boss Hex = 1.5).")]
    [SerializeField] private float damageMultiplier = 1f;
    [Tooltip("HP when spawned as the Level 1 Copy Bot (0 = use the encounter's default).")]
    [SerializeField] private int copyBotMaxHealth = 60;

    [Header("Boss Harlie - Spacing / Tempo")]
    [Tooltip("Ground combo swings only continue while the player is within this X distance.")]
    [SerializeField] private float slashRange = 2.3f;
    [Tooltip("If she can't walk into slash range in this time, she dashes in instead.")]
    [SerializeField] private float slashApproachTimeout = 1.4f;
    [SerializeField] private float recoverDurationFullHp = 0.55f;
    [SerializeField] private float recoverDurationLowHp = 0.22f;
    [Tooltip("After a pattern she backdashes if the player is standing this close.")]
    [SerializeField] private float backdashIfPlayerWithin = 1.6f;
    [Tooltip("Mid-combo, if the player stepped back by up to this much past slash range, she steps in once and keeps swinging.")]
    [SerializeField] private float comboChaseMaxGap = 3f;

    [Header("Boss Harlie - Aggression Per Life Bar")]
    [Tooltip("Bars on her life bar. Each bar lost adds tempo / move speed and shortens recovery (full → low HP value).")]
    [SerializeField] private int lifeBarCount = 10;
    [SerializeField] private float tempoPerBarLost = 0.025f;
    [SerializeField] private float moveSpeedPerBarLost = 0.15f;

    [Header("Boss Harlie - Pattern Zones")]
    [Tooltip("Player within this X distance = close rotation: Combo → Pogo Hop → Lace Cross-Up.")]
    [SerializeField] private float closeZoneDistance = 3.2f;
    [Tooltip("Up to this X distance = mid rotation: Dash Combo → Hornet Dive → Charge Slide. " +
             "Farther = far rotation: Lace Lunge → Plunge → Flash Dash.")]
    [SerializeField] private float midZoneDistance = 7f;
    [Tooltip("Player's feet at least this far above hers (and within antiAirMaxDistance) = anti-air rotation: " +
             "Rising Air Slash → Charged Air Slash.")]
    [SerializeField] private float antiAirHeight = 1.4f;
    [SerializeField] private float antiAirMaxDistance = 7f;
    [Tooltip("Enraged fixed chains (no recovery between): Flash Dash → Plunge, Dash Combo → Pogo Hop, Charge Slide → Hornet Dive.")]
    [SerializeField] private bool enragedChains = true;

    [Header("Boss Harlie - Air Slash Aim")]
    [Tooltip("Straight air slashes only come out when the gap between her body and the player's is at most this (X / Y).")]
    [SerializeField] private float airSlashReachX = 2.2f;
    [SerializeField] private float airSlashReachY = 1f;
    [Tooltip("Charged air slash only releases when the player is in the wave's lane (Y gap) within this X distance.")]
    [SerializeField] private float chargedAirSlashReachX = 9f;
    [SerializeField] private float chargedAirSlashLaneY = 0.8f;

    [Header("Boss Harlie - Dash / Jumps (playable parity)")]
    [SerializeField] private float dashDistance = 7f;
    [SerializeField] private float dashSpeed = 19f;
    [SerializeField] private int maxAirDashes = 3;
    [SerializeField] private bool dashInvincible = true;
    [SerializeField] private int maxAirJumps = 1;
    [Tooltip("Same launch speed as Kit's double jump.")]
    [SerializeField] private float doubleJumpForce = 16f;
    [Tooltip("Mid-air steering speed while chasing the player.")]
    [SerializeField] private float airSteerSpeed = 7f;

    [Header("Boss Harlie - Melee (playable parity)")]
    [SerializeField] private float kickDuration = 0.38f;
    [SerializeField] private float slash1Duration = 0.38f;
    [SerializeField] private float slash2Duration = 0.38f;
    [SerializeField] private float slash3Duration = 0.38f;
    [SerializeField] private float airSlashDuration = 0.38f;
    [SerializeField] private bool syncAttackDurationToAnimation = true;
    [SerializeField] private float attackDurationPadding = 0.02f;
    [SerializeField] private float hitboxDelay = 0.05f;
    [SerializeField] private float hitboxActiveTime = 0.22f;
    [SerializeField] private float airSlashAnimHold = 0.5f;
    [SerializeField] private int kickDamage = 2;
    [SerializeField] private int slash1Damage = 2;
    [SerializeField] private int slash2Damage = 2;
    [SerializeField] private int slash3Damage = 3;
    [SerializeField] private int airSlashDamage = 2;
    [SerializeField] private int jumpAttackDamage = 2;
    [SerializeField] private float aerialBounceForce = 12f;
    [SerializeField] private float dashCancelSlashSpeedMul = 1.5f;
    [SerializeField] private float kickLungeDistance = 1f;
    [SerializeField] private float slash1LungeDistance = 0.3f;
    [SerializeField] private float slash2LungeDistance = 0.5f;
    [SerializeField] private float slash3LungeDistance = 0.7f;
    [SerializeField] private float kickSpeedVsDash = 2f;

    [Header("Boss Harlie - Charge (playable parity)")]
    [SerializeField] private float chargeMaxSeconds = 3f;
    [SerializeField] private float chargeMaxSpaces = 7f;
    [Tooltip("Hold ratio at or below this = charge slash slide (wave at the end); above = charge kick slide.")]
    [SerializeField] [Range(0.1f, 0.9f)] private float chargeSlashMaxRatio = 0.5f;
    [SerializeField] private int chargeSlashDamage = 2;
    [SerializeField] private int chargeKickDamage = 4;
    [SerializeField] private float chargeSlideSpeedVsDash = 1.12f;
    [SerializeField] private float chargeHoldAfterSlide = 0.3f;
    [Tooltip("Charged air slash needs at least this fraction of a full charge.")]
    [SerializeField] [Range(0f, 1f)] private float chargedAirSlashMinRatio = 0.5f;
    [SerializeField] private int chargedAirSlashDamageMultiplier = 3;
    [SerializeField] private float chargedAirSlashDistanceMultiplier = 3f;

    [Header("Boss Harlie - Slash Wave")]
    [SerializeField] private HarlieSlashWaveSettings slashWave = new HarlieSlashWaveSettings();
    [Tooltip("HP of each of her waves; the player's shots, slashes and waves chip it down.")]
    [SerializeField] private int waveDurability = 10;

    [Header("Boss-Only: Telegraph")]
    [SerializeField] private float telegraphSeconds = 0.4f;
    [SerializeField] private Color telegraphColor = new Color(1f, 1f, 0.85f, 1f);

    [Header("Boss-Only: Lace Lunge (long dash-stab, then Slash 2 / 3)")]
    [SerializeField] private float laceLungeMaxDistance = 9f;
    [SerializeField] private float laceLungeSpeedVsDash = 1.35f;

    [Header("Boss-Only: Lace Flurry (rapid slashes, kick finisher)")]
    [SerializeField] private int flurrySlashes = 3;
    [SerializeField] private float flurrySpeedMul = 1.7f;
    [SerializeField] private float flurryWaveDistanceMultiplier = 0.7f;

    [Header("Boss-Only: Lace Cross-Up (dash through, turn, slash / kick)")]
    [SerializeField] private int crossUpCount = 3;
    [SerializeField] private float crossUpOvershoot = 2.2f;
    [SerializeField] private float crossUpDashSpeedMul = 1.3f;
    [SerializeField] private float crossUpSlashSpeedMul = 1.4f;

    [Header("Boss-Only: Hornet Dive (diagonal air dive)")]
    [SerializeField] private float hornetDiveSpeed = 22f;
    [SerializeField] private float hornetHangSeconds = 0.15f;
    [Tooltip("Dive is at least this steep (0 = flat, 1 = straight down).")]
    [SerializeField] [Range(0.2f, 1f)] private float hornetMinDiveSteepness = 0.45f;

    [Header("Boss-Only: Flash Dash (Chozo soldier)")]
    [SerializeField] private float flashDashSpeedMul = 2f;
    [SerializeField] private float flashDashOvershoot = 3f;
    [SerializeField] private int flashDashDamage = 3;

    [Header("Boss-Only: Plunge (Chozo soldier)")]
    [SerializeField] private float plungeHangSeconds = 0.25f;
    [SerializeField] private float plungeSpeed = 26f;
    [SerializeField] private int plungeDamage = 3;
    [SerializeField] private float plungeShockwaveDistanceMultiplier = 2.5f;

    [Header("Desperation (≤30% HP): Omega Zero moves (Mega Man Zero)")]
    [SerializeField] private float desperateHealthRatio = 0.3f;
    [SerializeField] private float desperateTempoBonus = 0.05f;
    [Tooltip("Ryuenjin-style rising saber: glint, then a rising air slash straight up at the player.")]
    [SerializeField] private float risingSaberForce = 17f;
    [SerializeField] private int risingSaberDamage = 3;
    [Tooltip("Triple saber: glint, dash in, Slash 1 → 2 → 3 with long steps; the last wave travels farther.")]
    [SerializeField] private float tripleSaberSpeedMul = 1.25f;
    [SerializeField] private float tripleSaberLungeMultiplier = 3f;
    [SerializeField] private float tripleSaberFinalWaveMultiplier = 2f;
    [Tooltip("Charged saber wave: visible charge, then a Slash 3 that sends a long, strong wave along the ground.")]
    [SerializeField] private float chargedWaveChargeSeconds = 1.4f;
    [SerializeField] private int chargedWaveDamage = 4;
    [SerializeField] private float chargedWaveDistanceMultiplier = 3f;

    [Header("Boss Hex - Omega Zero Moves (off for Harlie)")]
    [Tooltip("On for Boss Hex: dive slam with a 5-wave fan, three-way charged waves, V-Bots and Hex's rotations.")]
    [SerializeField] private bool useHexMoves = false;
    [Tooltip("Brief glint in the air before the dive stab.")]
    [SerializeField] private float hexDiveGlintSeconds = 0.18f;
    [SerializeField] private float hexDiveSpeed = 26f;
    [SerializeField] private int hexDiveDamage = 3;
    [Tooltip("Landing slam fan waves: base travel distance × this.")]
    [SerializeField] private float hexSlamWaveDistanceMultiplier = 4f;
    [Tooltip("Angle of the up / down waves of the three-way charged wave.")]
    [SerializeField] [Range(5f, 85f)] private float hexTripleWaveAngle = 45f;
    [SerializeField] private HexVBot vBotPrefab;
    [Tooltip("Seconds before she can summon again (one pair at a time).")]
    [SerializeField] private float vBotCooldown = 3f;
    [Tooltip("Spawn distance to each side of her (world units, 1 = one space).")]
    [SerializeField] private float vBotSideOffset = 1f;
    [Tooltip("Sparks: burst when hurt, stream while charging. Auto-found on this object.")]
    [SerializeField] private HexSparkEmitter sparkEmitter;

    [Header("Boss Hex - Orbiting V-Bots (≤50% HP)")]
    [Tooltip("At this HP she summons a ring of V-Bots. Each slash or kick sends one straight at where the player is.")]
    [SerializeField] private float orbitVBotHealthRatio = 0.5f;
    [SerializeField] private int orbitVBotCount = 8;
    [SerializeField] private float orbitVBotRadius = 2.4f;
    [SerializeField] private float orbitVBotDegreesPerSecond = 200f;
    [Tooltip("Damage when the player touches an orbiting bot.")]
    [SerializeField] private int orbitVBotTouchDamage = 1;
    [Tooltip("Damage of a launched bot before the damage multiplier.")]
    [SerializeField] private int orbitVBotLaunchDamage = 2;
    [SerializeField] private float orbitVBotLaunchSpaces = 14f;
    [Tooltip("Seconds after the last bot is sent before a new ring appears.")]
    [SerializeField] private float orbitVBotRefillSeconds = 4f;

    [Header("Boss Harlie - Summonable Silver Swords (≤50% HP, off for Hex)")]
    [Tooltip("At this HP 8 swords appear beside her and stay until she dies. They attack on their own timer while she keeps fighting.")]
    [SerializeField] private float silverSwordHealthRatio = 0.5f;
    [SerializeField] private SilverSwordSettings silverSwords = new SilverSwordSettings();
    [SerializeField] private SilverSwordBossSettings silverSwordPatterns = new SilverSwordBossSettings();
    [Tooltip("Seconds after the swords appear before their first pattern.")]
    [SerializeField] private float swordFirstPatternDelay = 2f;
    [Tooltip("Seconds of rest between the end of one sword pattern and the start of the next.")]
    [SerializeField] private float swordPatternGap = 3f;

    [Header("Enrage (≤50% HP)")]
    [SerializeField] private float enrageHealthRatio = 0.5f;
    [Tooltip("Extra tempo at enrage, on top of the per-bar ramp (0.15 = 15% faster).")]
    [SerializeField] private float enrageTempoBonus = 0.15f;
    [SerializeField] private float enrageMoveSpeedBonus = 2f;
    [SerializeField] private int enrageBonusDamage = 1;
    [SerializeField] private bool showEnrageAura = true;
    [SerializeField] private Color enrageAuraColor = new Color(0.55f, 0.04f, 0.06f, 1f);
    [SerializeField] private Color enrageAuraFlickerColor = new Color(1f, 0.35f, 0.2f, 1f);
    [SerializeField] [Range(0f, 1f)] private float enrageAuraAlpha = 0.7f;

    [Header("Charge Aura")]
    [SerializeField] private Color chargeAuraColor = new Color(1f, 0.92f, 0.08f, 1f);
    [SerializeField] private Color chargeAuraFlickerColor = new Color(1f, 1f, 0.72f, 1f);
    [SerializeField] private float auraBaseScale = 1.18f;

    [Header("Dash Afterimages")]
    [SerializeField] private bool enableDashAfterimages = true;
    [SerializeField] private int dashAfterimageCount = 5;
    [SerializeField] private float dashAfterimageSpacing = 0.1f;
    [SerializeField] private Color dashAfterimageColor = new Color(1f, 0.92f, 0.02f, 1f);
    [SerializeField] [Range(0f, 1f)] private float dashAfterimageAlphaStart = 0.65f;
    [SerializeField] [Range(0f, 1f)] private float dashAfterimageAlphaEnd = 0.2f;
    [SerializeField] private float dashAfterimageLifetimePadding = 0.15f;

    [Header("Attack Box Facing")]
    [Tooltip("Local X the attack box mirrors around when she faces right (same as playable Harlie's flip center).")]
    [SerializeField] private float attackBoxFlipCenterX = 0f;

    private readonly Stack<IEnumerator> actionStack = new Stack<IEnumerator>();
    private readonly int[] zoneRotationIndex = new int[4];
    private bool rotationsDesperate;

    private Motion motion = Motion.Stop;
    private float walkDir;
    private Vector2 motionVelocity;
    private float slideStartX;
    private float slideEndX;
    private float slideDuration;
    private float slideTimer;
    private bool slideEaseOut;
    private bool gravityOff;

    private int airJumpsLeft;
    private int airDashesLeft;
    private bool wasGrounded;

    private bool jumpAttackActive;
    private bool attackHitboxInUse;
    private bool pogoHappened;
    private float attackAnimSpeed = 1f;
    private float airSlashHoldUntil;
    private int currentCombo;

    private float chargeTimer;
    private float hitVfxTimer;

    private float vBotReadyAt = -999f;
    private readonly List<HexVBot> liveVBots = new List<HexVBot>(2);
    private readonly List<HexVBot> orbitVBots = new List<HexVBot>(8);
    private bool orbitVBotsUnlocked;
    private float orbitVBotRefillAt = -1f;

    private SilverSwordSquad swordSquad;
    private bool swordsSummoned;
    private bool swordsWereAttacking;
    private float swordNextPatternAt;

    private Aura chargeAura;
    private Aura telegraphAura;
    private Aura enrageAura;

    private readonly HashSet<int> animatorParams = new HashSet<int>();
    private bool forceTrail;
    private GameObject[] dashAfterimageObjects;
    private SpriteRenderer[] dashAfterimageRenderers;
    private float dashAfterimagesVisibleUntil;
    private readonly List<PoseSample> poseHistory = new List<PoseSample>(64);

    private Vector3 attackBoxLeftFacingLocal;
    private Vector3 attackBoxLastWrittenLocal;
    private bool attackBoxHasLastWritten;

    private struct PoseSample
    {
        public float time;
        public Vector3 position;
        public Sprite sprite;
        public bool flipX;
    }

    /// <summary>Solid-color copy of the body sprite drawn behind her (charge / telegraph / enrage glow).</summary>
    private sealed class Aura
    {
        public GameObject go;
        public SpriteRenderer sr;
        public Material material;
        public float phase;
    }

    public string CharacterId => characterId;
    public override int CopyBotMaxHealthOverride => copyBotMaxHealth;

    private float HealthRatio => currentHealth / (float)Mathf.Max(1, maxHealth);
    private bool IsEnraged => HealthRatio <= enrageHealthRatio + 0.0001f;
    private bool IsDesperate => HealthRatio <= desperateHealthRatio + 0.0001f;

    /// <summary>Whole life-bar segments lost (0 at full HP).</summary>
    private int BarsLost
    {
        get
        {
            int bars = Mathf.Max(1, lifeBarCount);
            return Mathf.Clamp(Mathf.FloorToInt((1f - HealthRatio) * bars + 0.0001f), 0, bars);
        }
    }

    private float BarsLost01 => Mathf.Clamp01(BarsLost / (float)Mathf.Max(1, lifeBarCount - 1));

    private float Tempo =>
        1f + BarsLost * Mathf.Max(0f, tempoPerBarLost) +
        (IsEnraged ? Mathf.Max(0f, enrageTempoBonus) : 0f) +
        (IsDesperate ? Mathf.Max(0f, desperateTempoBonus) : 0f);

    private float RecoverDuration => Mathf.Max(0.05f, Mathf.Lerp(recoverDurationFullHp, recoverDurationLowHp, BarsLost01));

    // ---------- Lifecycle ----------

    protected override void Awake()
    {
        SetBossId("Boss" + (string.IsNullOrWhiteSpace(characterId) ? "Harlie" : characterId.Trim()));
        base.Awake();

        CacheAnimatorParams();
        if (sparkEmitter == null)
            sparkEmitter = GetComponent<HexSparkEmitter>();
        chargeAura = CreateAura("ChargeAura");
        telegraphAura = CreateAura("TelegraphAura");
        enrageAura = CreateAura("EnrageAura");
        SetupDashAfterimages();

        if (attackHitbox != null)
        {
            attackHitbox.Deactivate();
            attackHitbox.OnTargetAcquired += HandleAttackTargetAcquired;
        }

        RefillAirActions();
    }

    private void OnDestroy()
    {
        if (attackHitbox != null)
            attackHitbox.OnTargetAcquired -= HandleAttackTargetAcquired;

        DestroyAura(chargeAura);
        DestroyAura(telegraphAura);
        DestroyAura(enrageAura);
        DestroyDashAfterimages();
        DismissSilverSwords();
        ClearOrbitVBots();
    }

    private void LateUpdate()
    {
        SyncAttackBoxFacing();
    }

    public void SetCopyBotAfterimageStyle(float grayLevel)
    {
        grayLevel = Mathf.Clamp(grayLevel, 0.05f, 1f);
        dashAfterimageColor = new Color(grayLevel, grayLevel, grayLevel, 1f);
    }

    // ---------- Damage rules ----------

    public override void TakeDamage(int amount)
    {
        if (BlocksIncomingHit())
            return;
        base.TakeDamage(amount);
    }

    public override void TakeDamage(int amount, ProjectileShotType shotType)
    {
        if (BlocksIncomingHit())
            return;
        base.TakeDamage(amount, shotType);
    }

    private bool BlocksIncomingHit() => !IsDead && dashInvincible && isDashing;

    /// <summary>Never flinches — hits only show sparks.</summary>
    protected override bool ShouldEnterHitStun(int healthBefore, int healthAfter, bool fromBigShot) => false;

    protected override void OnDamageApplied(int healthBefore, int healthAfter, bool enteredStun, bool fromBigShot)
    {
        VisualEffects.PlayStunned(stunnedEffectPrefab, this);
        hitVfxTimer = Mathf.Max(0.05f, hitStunDuration);
    }

    protected override void OnHitStunStarted()
    {
        ResetActions();
        base.OnHitStunStarted();
    }

    protected override float GetMoveSpeed()
    {
        float speed = moveSpeed + crystalMoveSpeedBonus + BarsLost * Mathf.Max(0f, moveSpeedPerBarLost);
        if (IsEnraged)
            speed += Mathf.Max(0f, enrageMoveSpeedBonus);
        return BossSpeed(speed);
    }

    private int Damage(int baseDamage)
    {
        int dmg = Mathf.Max(0, baseDamage) + (IsEnraged ? Mathf.Max(0, enrageBonusDamage) : 0);
        dmg = Mathf.FloorToInt(dmg * Mathf.Max(0.1f, damageMultiplier) + 0.5f);
        return ApplyCrystalAttackBonus(dmg);
    }

    // ---------- Update loop ----------

    protected override void HandleBossUpdate()
    {
        if (isGrounded && !wasGrounded)
            RefillAirActions();
        wasGrounded = isGrounded;

        PlayerController player = FindPlayer();
        if (player == null || player.IsDead)
        {
            if (actionStack.Count > 0)
                ResetActions();
            motion = Motion.Stop;
            return;
        }

        TickSilverSwords();
        TickOrbitVBots();

        if (actionStack.Count == 0)
            actionStack.Push(RunNextPattern());

        TickActions();
        TickJumpAttack();
        TickChargeAura();
    }

    /// <summary>Runs the active pattern; nested IEnumerators run as sub-steps and can be dropped at any time.</summary>
    private void TickActions()
    {
        int guard = 0;
        while (actionStack.Count > 0 && guard++ < 64)
        {
            IEnumerator top = actionStack.Peek();
            if (!top.MoveNext())
            {
                actionStack.Pop();
                continue;
            }

            if (top.Current is IEnumerator child)
            {
                actionStack.Push(child);
                continue;
            }

            return;
        }
    }

    protected override void HandleBossFixedUpdate()
    {
        if (rb == null)
            return;

        switch (motion)
        {
            case Motion.Walk:
                if (Mathf.Abs(walkDir) > 0.01f)
                    MoveHorizontal(walkDir);
                else
                    StopHorizontal();
                break;
            case Motion.Velocity:
                rb.linearVelocity = new Vector2(BossSpeed(motionVelocity.x), BossSpeed(motionVelocity.y));
                break;
            case Motion.Hang:
                rb.linearVelocity = Vector2.zero;
                break;
            case Motion.Slide:
                TickSlideFixed();
                break;
            case Motion.Stop:
                if (isGrounded)
                    StopHorizontal();
                break;
        }
    }

    protected override void ApplyFallMultiplier()
    {
        if (gravityOff || motion == Motion.Velocity || motion == Motion.Hang)
            return;
        base.ApplyFallMultiplier();
    }

    protected override void TickPostHitVisuals(float dt)
    {
        if (hitVfxTimer > 0f)
        {
            hitVfxTimer -= dt;
            if (hitVfxTimer <= 0f)
                VisualEffects.StopStunned(this);
        }

        RecordPoseHistory();
        UpdateDashAfterimages();
        TickEnrageAura();
    }

    protected override void UpdateAnimator()
    {
        if (animator == null)
            return;

        float world = HyperSpeedWorldSlow.IsActive ? HyperSpeedWorldSlow.WorldTimeScale : 1f;
        animator.speed = world * (isAttacking ? Mathf.Max(0.05f, attackAnimSpeed) : 1f);

        float vy = rb != null ? rb.linearVelocity.y : 0f;
        bool moving = motion == Motion.Walk && Mathf.Abs(walkDir) > 0.01f && !isDashing && !isAttacking;
        SetAnimBool("IsMoving", moving);
        SetAnimBool("IsGrounded", isGrounded);
        SetAnimBool("IsInAir", !isGrounded);
        SetAnimBool("IsDashing", isDashing);
        SetAnimBool("IsAttacking", isAttacking || Time.time < airSlashHoldUntil);
        SetAnimBool("IsCharging", isCharging);
        SetAnimBool("IsStunned", false);
        SetAnimBool("IsFalling", !isGrounded && vy < -0.01f);
        SetAnimBool("IsJumping", !isGrounded && vy >= -0.01f);
        SetAnimFloat("VerticalSpeed", vy);
        SetAnimInt("AttackCombo", currentCombo);
    }

    // ---------- Pattern selection ----------

    private IEnumerator RunNextPattern()
    {
        PlayerController player = FindPlayer();
        if (player == null)
            yield break;

        if (!isGrounded)
            yield return WaitUntilGrounded(2f);

        Pattern pattern = PickPattern(player);
        yield return RunPattern(pattern);

        if (enragedChains && TryGetFollowUp(pattern, out Pattern followUp))
        {
            CleanupAfterPattern();
            if (!isGrounded)
                yield return WaitUntilGrounded(2f);
            yield return RunPattern(followUp);
        }

        yield return Recover();
    }

    private Pattern PickPattern(PlayerController player)
    {
        bool desperate = IsDesperate;
        if (desperate != rotationsDesperate)
        {
            // New phase: every rotation starts over from its first move.
            rotationsDesperate = desperate;
            System.Array.Clear(zoneRotationIndex, 0, zoneRotationIndex.Length);
        }

        Zone zone = ResolveZone(player);
        Pattern[] rotation = useHexMoves ? HexRotation(zone, desperate) : HarlieRotation(zone, desperate);

        // Moves that aren't ready (V-Bots on cooldown) are skipped; the rotation order stays the same.
        int z = (int)zone;
        Pattern pattern = rotation[zoneRotationIndex[z] % rotation.Length];
        for (int i = 0; i < rotation.Length; i++)
        {
            pattern = rotation[zoneRotationIndex[z] % rotation.Length];
            zoneRotationIndex[z] = (zoneRotationIndex[z] + 1) % rotation.Length;
            if (IsPatternReady(pattern))
                break;
        }

        return pattern;
    }

    private static Pattern[] HarlieRotation(Zone zone, bool desperate) => zone switch
    {
        Zone.Close => desperate ? CloseRotationDesperate : CloseRotation,
        Zone.Mid => desperate ? MidRotationDesperate : MidRotation,
        Zone.Far => desperate ? FarRotationDesperate : FarRotation,
        _ => desperate ? AntiAirRotationDesperate : AntiAirRotation
    };

    private static Pattern[] HexRotation(Zone zone, bool desperate) => zone switch
    {
        Zone.Close => desperate ? HexCloseRotationDesperate : HexCloseRotation,
        Zone.Mid => desperate ? HexMidRotationDesperate : HexMidRotation,
        Zone.Far => desperate ? HexFarRotationDesperate : HexFarRotation,
        _ => desperate ? HexAntiAirRotationDesperate : HexAntiAirRotation
    };

    private bool IsPatternReady(Pattern pattern)
    {
        if (pattern != Pattern.HexVBots)
            return true;

        liveVBots.RemoveAll(bot => bot == null);
        return vBotPrefab != null && Time.time >= vBotReadyAt && liveVBots.Count == 0;
    }

    private Zone ResolveZone(PlayerController player)
    {
        float dx = Mathf.Abs(player.transform.position.x - transform.position.x);
        if (dx <= antiAirMaxDistance && PlayerFeetAboveMine(player) >= antiAirHeight)
            return Zone.AntiAir;
        if (dx <= closeZoneDistance)
            return Zone.Close;
        return dx <= midZoneDistance ? Zone.Mid : Zone.Far;
    }

    /// <summary>
    /// Fixed chains (no recovery between). Enraged: Flash Dash → Plunge, Dash Combo → Pogo Hop,
    /// Charge Slide → Hornet Dive. Desperate adds: Ground Combo → Rising Saber, Charged Wave → Flash Dash.
    /// </summary>
    private bool TryGetFollowUp(Pattern pattern, out Pattern followUp)
    {
        followUp = pattern;
        if (IsDesperate)
        {
            switch (pattern)
            {
                case Pattern.GroundCombo: followUp = Pattern.OmegaRisingSaber; return true;
                case Pattern.OmegaChargedWave: followUp = Pattern.FlashDash; return true;
            }
        }

        if (!IsEnraged)
            return false;

        switch (pattern)
        {
            case Pattern.FlashDash: followUp = Pattern.ChozoPlunge; return true;
            case Pattern.DashCancelCombo: followUp = Pattern.PogoHop; return true;
            case Pattern.ChargeSlide: followUp = Pattern.HornetDive; return true;
            default: return false;
        }
    }

    private IEnumerator RunPattern(Pattern pattern)
    {
        switch (pattern)
        {
            case Pattern.GroundCombo: return GroundComboPattern();
            case Pattern.DashCancelCombo: return DashCancelComboPattern();
            case Pattern.PogoHop: return PogoHopPattern();
            case Pattern.ChargeSlide: return ChargeSlidePattern();
            case Pattern.LaceCrossUp: return LaceCrossUpPattern();
            case Pattern.HornetDive: return HornetDivePattern();
            case Pattern.LaceLunge: return LaceLungePattern();
            case Pattern.ChozoPlunge: return ChozoPlungePattern();
            case Pattern.FlashDash: return FlashDashPattern();
            case Pattern.RisingAirSlash: return RisingAirSlashPattern();
            case Pattern.LaceFlurry: return LaceFlurryPattern();
            case Pattern.OmegaRisingSaber: return OmegaRisingSaberPattern();
            case Pattern.OmegaTripleSaber: return OmegaTripleSaberPattern();
            case Pattern.OmegaChargedWave: return OmegaChargedWavePattern();
            case Pattern.HexDiveSlam: return HexDiveSlamPattern();
            case Pattern.HexVBots: return HexVBotsPattern();
            default: return ChargedAirSlashPattern();
        }
    }

    private IEnumerator Recover()
    {
        CleanupAfterPattern();
        if (!isGrounded)
            yield return WaitUntilGrounded(2f);

        motion = Motion.Stop;
        PlayerController player = FindPlayer();
        if (player != null && Mathf.Abs(DxToPlayer()) < backdashIfPlayerWithin)
        {
            yield return Dash(-DirToPlayer(), dashDistance * 0.6f);
            motion = Motion.Stop;
            FacePlayer();
        }

        yield return Wait(RecoverDuration);
    }

    // ---------- Mirrored playable patterns ----------

    private IEnumerator GroundComboPattern()
    {
        yield return ApproachForSlash();
        yield return Combo(1f);
    }

    private IEnumerator DashCancelComboPattern()
    {
        float dx = Mathf.Abs(DxToPlayer());
        yield return Dash(DirToPlayer(), Mathf.Clamp(dx - 1.2f, 1.5f, dashDistance));
        yield return Combo(dashCancelSlashSpeedMul);
    }

    /// <summary>Pogo hop: jump onto the player and keep bouncing off their head (falling jump attack).</summary>
    private IEnumerator PogoHopPattern()
    {
        JumpToward(1f);
        yield return WaitUntilAirborne();

        int bounces = 0;
        pogoHappened = false;
        float t = 0f;
        while (!isGrounded && t < 4f)
        {
            if (pogoHappened)
            {
                pogoHappened = false;
                bounces++;
                if (bounces >= 3)
                {
                    yield return Wait(0.12f);
                    if (PlayerInAirSlashReach())
                        yield return AirSlash(charged: false);
                    break;
                }
            }

            SteerTowardPlayerInAir();
            t += BossDeltaTime;
            yield return null;
        }

        yield return WaitUntilGrounded(2f);
    }

    /// <summary>Anti-air: jump up at the player and only swing once they're actually in reach (double jump to follow).</summary>
    private IEnumerator RisingAirSlashPattern()
    {
        JumpToward(0.7f);
        yield return WaitUntilAirborne();

        int swings = 0;
        float t = 0f;
        while (!isGrounded && t < 2.2f && swings < 2)
        {
            SteerTowardPlayerInAir();
            if (PlayerInAirSlashReach())
            {
                yield return AirSlash(charged: false);
                swings++;
                continue;
            }

            if (rb != null && rb.linearVelocity.y < 0f && PlayerHeightAboveMe() > 0.5f)
                TryDoubleJump(DirToPlayer() * airSteerSpeed * 0.6f);

            t += BossDeltaTime;
            yield return null;
        }

        yield return WaitUntilGrounded(2f);
    }

    /// <summary>
    /// Anti-air: charge on the ground, jump and release the big wave only once the player is in its lane.
    /// If they never line up, the charge is spent as a kick slide on landing.
    /// </summary>
    private IEnumerator ChargedAirSlashPattern()
    {
        float hold = chargeMaxSeconds * Mathf.Max(chargedAirSlashMinRatio, 0.55f);
        yield return ChargeUp(hold, keepDistance: 3f);

        JumpToward(0.35f);
        yield return WaitUntilAirborne();

        float t = 0f;
        while (!isGrounded && t < 2.2f)
        {
            FacePlayer();
            if (PlayerInChargedWaveLane())
            {
                EndCharge();
                yield return AirSlash(charged: true);
                yield return WaitUntilGrounded(2f);
                yield break;
            }

            if (rb != null && rb.linearVelocity.y < 0f && PlayerHeightAboveMe() > chargedAirSlashLaneY)
                TryDoubleJump(0f);

            t += BossDeltaTime;
            yield return null;
        }

        yield return WaitUntilGrounded(2f);
        EndCharge();
        float dx = Mathf.Abs(DxToPlayer());
        yield return SlideAttack(Mathf.Clamp(dx + 1.5f, 2f, chargeMaxSpaces), chargeSlideSpeedVsDash,
            Damage(chargeKickDamage), ComboKick, slashAtEnd: false);
    }

    /// <summary>Close enough for a short slide = charge slash slide (wave at the end); farther = charge kick slide.</summary>
    private IEnumerator ChargeSlidePattern()
    {
        float dx = Mathf.Abs(DxToPlayer());
        float spaces = Mathf.Max(0.1f, chargeMaxSpaces);
        bool slashSlide = dx <= chargeSlashMaxRatio * spaces + 1f;
        float ratio = slashSlide
            ? Mathf.Clamp((dx - 0.5f) / spaces, 0.3f, chargeSlashMaxRatio)
            : Mathf.Clamp((dx + 1.5f) / spaces, chargeSlashMaxRatio + 0.05f, 1f);

        yield return ChargeUp(ratio * chargeMaxSeconds, keepDistance: 0f);
        EndCharge();

        bool kick = ratio > chargeSlashMaxRatio;
        yield return SlideAttack(
            ratio * chargeMaxSpaces,
            chargeSlideSpeedVsDash,
            Damage(kick ? chargeKickDamage : chargeSlashDamage),
            kick ? ComboKick : ComboSlash1,
            slashAtEnd: !kick);
        yield return Wait(chargeHoldAfterSlide);
    }

    // ---------- Boss-only patterns ----------

    /// <summary>Lace / Hornet lunge: a glint, a long dash-stab, then Slash 2 → 3 if still close.</summary>
    private IEnumerator LaceLungePattern()
    {
        FacePlayer();
        yield return Telegraph(telegraphSeconds);

        float distance = Mathf.Clamp(Mathf.Abs(DxToPlayer()) + 2.5f, 4f, laceLungeMaxDistance);
        yield return SlideAttack(distance, laceLungeSpeedVsDash, Damage(chargeKickDamage), ComboKick, slashAtEnd: false);

        if (PlayerInRange(slashRange * 1.25f))
        {
            yield return GroundAttack(ComboSlash2, 1.3f);
            yield return GroundAttack(ComboSlash3, 1.3f);
        }
    }

    /// <summary>Lace cross-ups: a quick glint, then dash through the player, turn, slash — several times.</summary>
    private IEnumerator LaceCrossUpPattern()
    {
        yield return Telegraph(telegraphSeconds * 0.6f);

        int count = Mathf.Max(1, crossUpCount) + (IsEnraged ? 1 : 0);
        for (int i = 0; i < count; i++)
        {
            float dx = Mathf.Abs(DxToPlayer());
            yield return Dash(DirToPlayer(), Mathf.Clamp(dx + crossUpOvershoot, 2f, dashDistance + 2f), crossUpDashSpeedMul);
            motion = Motion.Stop;
            FacePlayer();
            yield return GroundAttack(CrossUpAttacks[i % CrossUpAttacks.Length], crossUpSlashSpeedMul);
            yield return Wait(0.08f);
        }
    }

    private static readonly int[] CrossUpAttacks = { ComboSlash1, ComboKick, ComboSlash2, ComboSlash3 };

    /// <summary>Lace flurry: a short glint, rapid alternating slashes, then a kick to finish.</summary>
    private IEnumerator LaceFlurryPattern()
    {
        FacePlayer();
        yield return Telegraph(telegraphSeconds * 0.5f);
        yield return ApproachForSlash();

        bool stepped = false;
        int slashes = Mathf.Max(1, flurrySlashes) + (IsEnraged ? 1 : 0);
        for (int i = 0; i < slashes; i++)
        {
            if (i > 0 && !PlayerInRange(slashRange * 1.25f))
            {
                if (stepped || !TryBeginChaseStep())
                    yield break;
                stepped = true;
                yield return ChaseStep();
            }

            yield return GroundAttack(i % 2 == 0 ? ComboSlash1 : ComboSlash2, flurrySpeedMul,
                waveDistanceMul: flurryWaveDistanceMultiplier);
        }

        if (PlayerInRange(slashRange * 1.4f))
            yield return GroundAttack(ComboKick, flurrySpeedMul * 0.85f);
    }

    /// <summary>Hornet dive: rise, hang for a beat, then dive diagonally at the player.</summary>
    private IEnumerator HornetDivePattern()
    {
        JumpToward(0.5f);
        yield return WaitUntilAirborne();
        yield return WaitUntilFalling(1.2f);

        if (TryDoubleJump(0f))
            yield return WaitUntilFalling(1f);

        yield return Hang(hornetHangSeconds, telegraph: true);

        PlayerController player = FindPlayer();
        Vector2 aim = player != null
            ? (Vector2)(player.transform.position - transform.position)
            : new Vector2(facingSign, -1f);
        if (aim.sqrMagnitude < 0.01f)
            aim = new Vector2(facingSign, -1f);
        aim.Normalize();
        if (aim.y > -hornetMinDiveSteepness)
        {
            float x = Mathf.Sign(Mathf.Abs(aim.x) > 0.01f ? aim.x : facingSign) *
                      Mathf.Sqrt(1f - hornetMinDiveSteepness * hornetMinDiveSteepness);
            aim = new Vector2(x, -hornetMinDiveSteepness);
        }

        SetFacing(aim.x);
        yield return Dive(aim * hornetDiveSpeed, Damage(airSlashDamage), 1.2f);

        if (isGrounded && PlayerInRange(slashRange * 1.2f))
        {
            yield return GroundAttack(ComboKick, 1.2f);
            if (PlayerInRange(slashRange * 1.25f))
                yield return GroundAttack(ComboSlash2, 1.2f);
        }
    }

    /// <summary>Chozo soldier flash dash: glint, a blinding dash through the player, then a long charged wave.</summary>
    private IEnumerator FlashDashPattern()
    {
        FacePlayer();
        yield return Telegraph(telegraphSeconds * 1.2f);

        float dx = Mathf.Abs(DxToPlayer());
        yield return Dash(DirToPlayer(), Mathf.Clamp(dx + flashDashOvershoot, 3f, dashDistance * 2f),
            flashDashSpeedMul, Damage(flashDashDamage));
        motion = Motion.Stop;
        FacePlayer();
        yield return Wait(0.12f);
        yield return GroundAttack(ComboSlash3, 1f, waveDistanceMul: chargedAirSlashDistanceMultiplier);
    }

    /// <summary>Chozo soldier plunge: leap above the player, hang, slam straight down, shockwaves both ways.</summary>
    private IEnumerator ChozoPlungePattern()
    {
        JumpAbovePlayer();
        yield return WaitUntilAirborne();
        yield return WaitUntilFalling(1.2f);

        if (airJumpsLeft > 0)
        {
            TryDoubleJump(VelocityToReachPlayerAtApex(doubleJumpForce));
            yield return WaitUntilFalling(1f);
        }

        yield return Hang(plungeHangSeconds, telegraph: true);
        yield return Dive(new Vector2(0f, -plungeSpeed), Damage(plungeDamage), 1.5f, stabPose: useHexMoves);
        if (!isGrounded)
            yield break;

        // Landing slam: a real Slash 3 swing sends the shockwaves out both ways (Hex: the 5-wave fan).
        SoundManager.Instance?.PlayHarlieBigSwordClash();
        if (useHexMoves)
        {
            BurstSparksAtFeet();
            yield return GroundAttack(ComboSlash3, 1.3f, waveDistanceMul: hexSlamWaveDistanceMultiplier,
                lungeMul: 0f, waveShape: WaveShape.Fan);
        }
        else
        {
            yield return GroundAttack(ComboSlash3, 1.3f, waveDistanceMul: plungeShockwaveDistanceMultiplier,
                lungeMul: 0f, waveShape: WaveShape.BothWays);
        }

        yield return Wait(0.2f);
    }

    // ---------- Desperation (≤30% HP): Omega Zero ----------

    /// <summary>Omega Zero's Ryuenjin: a glint, then a rising air slash launched straight up at the player.</summary>
    private IEnumerator OmegaRisingSaberPattern()
    {
        FacePlayer();
        yield return Telegraph(telegraphSeconds * 0.8f);

        float vy = Mathf.Max(1f, risingSaberForce);
        float riseTime = Mathf.Max(0.15f, vy / Gravity);
        float vx = Mathf.Clamp(DxToPlayer() * 0.8f / riseTime, -airSteerSpeed, airSteerSpeed);
        LaunchJump(vx, vy);
        FacePlayer();

        isAttacking = true;
        attackAnimSpeed = 1f;
        forceTrail = true;
        BeginDashAfterimageTrail(riseTime);
        int dmg = Damage(risingSaberDamage);

        // Two swings on the way up (each re-arms the hitbox), then she falls with her jump attack.
        for (int swing = 0; swing < 2; swing++)
        {
            PlayAttackAnim(ComboKick, air: true);
            PlaySwing();
            ActivateHitbox(dmg);

            float t = 0f;
            float swingTime = Mathf.Max(0.1f, ResolveDuration("air slash", airSlashDuration));
            while (t < swingTime && (t < 0.08f || rb == null || rb.linearVelocity.y > 0f))
            {
                t += BossDeltaTime;
                yield return null;
            }

            DeactivateHitbox();
            if (rb != null && rb.linearVelocity.y <= 0f)
                break;
        }

        forceTrail = false;
        isAttacking = false;
        airSlashHoldUntil = Time.time + airSlashAnimHold * 0.5f;

        if (!isGrounded && PlayerInAirSlashReach())
            yield return AirSlash(charged: false);

        yield return WaitUntilGrounded(2f);
    }

    /// <summary>Omega Zero's triple saber: a glint, dash in, then Slash 1 → 2 → 3 with long steps.</summary>
    private IEnumerator OmegaTripleSaberPattern()
    {
        FacePlayer();
        yield return Telegraph(telegraphSeconds * 0.7f);

        float dx = Mathf.Abs(DxToPlayer());
        if (dx > slashRange)
            yield return Dash(DirToPlayer(), Mathf.Clamp(dx - 1.6f, 1.5f, dashDistance), 1.2f);

        motion = Motion.Stop;
        yield return GroundAttack(ComboSlash1, tripleSaberSpeedMul, lungeMul: tripleSaberLungeMultiplier);
        yield return GroundAttack(ComboSlash2, tripleSaberSpeedMul, lungeMul: tripleSaberLungeMultiplier);
        yield return GroundAttack(ComboSlash3, tripleSaberSpeedMul * 0.9f,
            waveDistanceMul: tripleSaberFinalWaveMultiplier, lungeMul: tripleSaberLungeMultiplier * 0.8f);
    }

    /// <summary>Omega Zero's charged saber: a visible charge, then a Slash 3 that sends a long, strong ground wave.</summary>
    private IEnumerator OmegaChargedWavePattern()
    {
        yield return ChargeUp(chargedWaveChargeSeconds, keepDistance: 0f);
        EndCharge();
        yield return GroundAttack(ComboSlash3, 1f, damageOverride: Damage(chargedWaveDamage),
            waveDistanceMul: chargedWaveDistanceMultiplier, lungeMul: 0.5f, waveShape: ChargedWaveShape);
        yield return Wait(0.15f);
    }

    // ---------- Boss Hex: Omega Zero ----------

    /// <summary>
    /// Omega Zero's dive: jump at the player, a glint at the top, then a dive stab straight down that pierces.
    /// Landing punches the ground with a Slash 3 that fires 5 waves in an upward fan.
    /// </summary>
    private IEnumerator HexDiveSlamPattern()
    {
        JumpToward(0.9f);
        yield return WaitUntilAirborne();

        float t = 0f;
        while (!isGrounded && t < 1.2f)
        {
            SteerTowardPlayerInAir();
            if (rb != null && rb.linearVelocity.y <= 0f)
                break;
            if (Mathf.Abs(DxToPlayer()) < 0.6f && t > 0.15f)
                break;
            t += BossDeltaTime;
            yield return null;
        }

        if (isGrounded)
            yield break;

        yield return Hang(hexDiveGlintSeconds, telegraph: true);
        yield return Dive(new Vector2(0f, -hexDiveSpeed), Damage(hexDiveDamage), 1.5f, stabPose: true);
        if (!isGrounded)
            yield break;

        SoundManager.Instance?.PlayHarlieBigSwordClash();
        BurstSparksAtFeet();
        yield return GroundAttack(ComboSlash3, 1.3f, waveDistanceMul: hexSlamWaveDistanceMultiplier,
            lungeMul: 0f, waveShape: WaveShape.Fan);
        yield return Wait(0.15f);
    }

    /// <summary>
    /// A glint, then two V-Bots appear one space to either side, back up and rocket toward the player.
    /// If the player is right next to her she kicks them into the bots' path.
    /// </summary>
    private IEnumerator HexVBotsPattern()
    {
        FacePlayer();
        yield return Telegraph(telegraphSeconds * 0.6f);
        FacePlayer();
        SummonVBots();
        yield return Wait(0.25f);

        if (PlayerInRange(slashRange))
            yield return GroundAttack(ComboKick, 1.1f);
    }

    // ---------- Silver Swords (Boss Harlie) ----------

    /// <summary>Summons the swords once, then runs their patterns on their own timer alongside hers.</summary>
    private void TickSilverSwords()
    {
        if (swordSquad != null)
        {
            bool attacking = swordSquad.IsAttacking;
            if (swordsWereAttacking && !attacking)
                swordNextPatternAt = Time.time + Mathf.Max(0f, swordPatternGap);
            swordsWereAttacking = attacking;

            if (!attacking && Time.time >= swordNextPatternAt && swordSquad.TryStartBossPattern(SwordTargetBounds))
                swordsWereAttacking = true;
            return;
        }

        if (swordsSummoned || useHexMoves || IsCopyBotDecoy || silverSwords.swordPrefab == null ||
            HealthRatio > silverSwordHealthRatio + 0.0001f)
            return;

        swordsSummoned = true;
        swordsWereAttacking = false;
        swordNextPatternAt = Time.time + Mathf.Max(0f, swordFirstPatternDelay);
        swordSquad = SilverSwordSquad.Create(transform, silverSwords, groundLayers, silverSwordPatterns);
    }

    private Bounds SwordTargetBounds()
    {
        PlayerController player = FindPlayer();
        return player != null
            ? PlayerBounds(player)
            : new Bounds(MyBounds().center + Vector3.right * (facingSign * 4f), Vector3.one);
    }

    private void DismissSilverSwords()
    {
        if (swordSquad != null)
            swordSquad.Dismiss();
        swordSquad = null;
    }

    // ---------- Orbiting V-Bots (Boss Hex) ----------

    private void TickOrbitVBots()
    {
        if (!useHexMoves || vBotPrefab == null || IsCopyBotDecoy)
            return;

        if (!orbitVBotsUnlocked)
        {
            if (HealthRatio > orbitVBotHealthRatio + 0.0001f)
                return;

            orbitVBotsUnlocked = true;
            SummonOrbitVBots();
            return;
        }

        orbitVBots.RemoveAll(bot => bot == null || !bot.IsOrbiting);
        if (orbitVBots.Count > 0)
            return;

        if (orbitVBotRefillAt < 0f)
            orbitVBotRefillAt = Time.time + Mathf.Max(0f, orbitVBotRefillSeconds);
        else if (Time.time >= orbitVBotRefillAt)
            SummonOrbitVBots();
    }

    private void SummonOrbitVBots()
    {
        orbitVBotRefillAt = -1f;
        Vector3 center = MyBounds().center;
        int count = Mathf.Max(1, orbitVBotCount);
        for (int i = 0; i < count; i++)
        {
            HexVBot bot = Instantiate(vBotPrefab, center, Quaternion.identity);
            bot.BeginOrbit(transform, i * 360f / count, orbitVBotRadius, orbitVBotDegreesPerSecond, orbitVBotTouchDamage);
            orbitVBots.Add(bot);
        }

        if (sparkEmitter != null)
            sparkEmitter.Burst(center);
    }

    /// <summary>Each slash / kick sends the orbiting bot closest to the player straight at where the player is now.</summary>
    private void LaunchOrbitVBot()
    {
        if (!useHexMoves)
            return;

        orbitVBots.RemoveAll(bot => bot == null || !bot.IsOrbiting);
        PlayerController player = FindPlayer();
        if (orbitVBots.Count == 0 || player == null || player.IsDead)
            return;

        Vector2 target = PlayerBounds(player).center;
        HexVBot best = orbitVBots[0];
        float bestSqr = float.MaxValue;
        for (int i = 0; i < orbitVBots.Count; i++)
        {
            float sqr = ((Vector2)orbitVBots[i].transform.position - target).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = orbitVBots[i];
            }
        }

        int damage = Mathf.FloorToInt(Mathf.Max(0, orbitVBotLaunchDamage) * Mathf.Max(0.1f, damageMultiplier) + 0.5f);
        best.LaunchFromOrbitToward(target - (Vector2)best.transform.position, damage, orbitVBotLaunchSpaces);
        orbitVBots.Remove(best);
    }

    private void ClearOrbitVBots()
    {
        for (int i = 0; i < orbitVBots.Count; i++)
        {
            if (orbitVBots[i] != null)
                Destroy(orbitVBots[i].gameObject);
        }

        orbitVBots.Clear();
        orbitVBotRefillAt = -1f;
    }

    // ---------- Building blocks ----------

    private IEnumerator Wait(float seconds)
    {
        float t = 0f;
        while (t < seconds)
        {
            t += BossDeltaTime * Tempo;
            yield return null;
        }
    }

    private IEnumerator WaitUntilAirborne(float timeout = 0.25f)
    {
        float t = 0f;
        while (isGrounded && t < timeout)
        {
            t += BossDeltaTime;
            yield return null;
        }
    }

    private IEnumerator WaitUntilGrounded(float timeout)
    {
        float t = 0f;
        while (!isGrounded && t < timeout)
        {
            t += BossDeltaTime;
            yield return null;
        }
    }

    private IEnumerator WaitUntilFalling(float timeout)
    {
        float t = 0f;
        while (!isGrounded && rb != null && rb.linearVelocity.y > 0f && t < timeout)
        {
            t += BossDeltaTime;
            yield return null;
        }
    }

    private IEnumerator ApproachForSlash()
    {
        float t = 0f;
        while (true)
        {
            float dx = DxToPlayer();
            if (Mathf.Abs(dx) <= slashRange * 0.85f)
                break;

            if (t >= slashApproachTimeout)
            {
                yield return Dash(Mathf.Sign(dx), Mathf.Clamp(Mathf.Abs(dx) - 1.2f, 1.5f, dashDistance));
                break;
            }

            motion = Motion.Walk;
            walkDir = Mathf.Sign(dx);
            t += BossDeltaTime;
            yield return null;
        }

        motion = Motion.Stop;
        walkDir = 0f;
    }

    /// <summary>Kick → Slash 1 → 2 → 3. If the player backs off mid-combo she steps in once and keeps going.</summary>
    private IEnumerator Combo(float speedMul)
    {
        bool stepped = false;
        for (int combo = ComboKick; combo <= ComboSlash3; combo++)
        {
            if (combo > ComboKick && !PlayerInRange(slashRange * 1.25f))
            {
                if (stepped || !TryBeginChaseStep())
                    break;
                stepped = true;
                yield return ChaseStep();
            }

            yield return GroundAttack(combo, speedMul);
        }
    }

    private bool TryBeginChaseStep()
    {
        float gap = Mathf.Abs(DxToPlayer());
        return isGrounded && gap <= slashRange + Mathf.Max(0f, comboChaseMaxGap) &&
               Mathf.Abs(PlayerHeightAboveMe()) <= 1.5f;
    }

    /// <summary>Quick dash-speed step back into slash range (with afterimages so it reads as a dash).</summary>
    private IEnumerator ChaseStep()
    {
        FacePlayer();
        float distance = Mathf.Max(0.4f, Mathf.Abs(DxToPlayer()) - slashRange * 0.6f);
        float duration = distance / Mathf.Max(0.1f, dashSpeed * 0.8f * Tempo);
        StartSlide(facingSign * distance, duration, easeOut: true);
        forceTrail = true;
        BeginDashAfterimageTrail(duration);

        float t = 0f;
        while (motion == Motion.Slide && t < duration + 0.2f)
        {
            t += BossDeltaTime;
            yield return null;
        }

        forceTrail = false;
        motion = Motion.Stop;
    }

    private IEnumerator GroundAttack(int combo, float speedMul, int damageOverride = -1, float waveDistanceMul = 1f,
        float lungeMul = 1f, WaveShape waveShape = WaveShape.Forward)
    {
        float speed = Mathf.Max(0.1f, speedMul * Tempo);
        int dmg = damageOverride >= 0 ? damageOverride : Damage(ComboDamage(combo));
        float duration = ResolveDuration(ComboStateName(combo), ComboDuration(combo)) / speed;

        motion = Motion.Stop;
        FacePlayer();
        isAttacking = true;
        LaunchOrbitVBot();
        attackAnimSpeed = speed;
        PlayAttackAnim(combo, air: false);
        if (combo != ComboKick)
            PlaySwing();
        StartLunge(combo, duration, speed, lungeMul);

        yield return AttackWindow(duration, dmg, speed, fireWave: combo != ComboKick, waveDistanceMul, waveShape);
        isAttacking = false;
    }

    private IEnumerator AirSlash(bool charged)
    {
        float speed = Mathf.Max(0.1f, Tempo);
        int multiplier = charged ? Mathf.Max(1, chargedAirSlashDamageMultiplier) : 1;
        int dmg = Damage(airSlashDamage) * multiplier;
        float duration = ResolveDuration("air slash", airSlashDuration) / speed;

        FacePlayer();
        motion = Motion.Free;
        isAttacking = true;
        LaunchOrbitVBot();
        attackAnimSpeed = speed;
        PlayAttackAnim(ComboKick, air: true);
        PlaySwing();

        yield return AttackWindow(duration, dmg, speed, fireWave: true,
            charged ? Mathf.Max(1f, chargedAirSlashDistanceMultiplier) : 1f,
            charged ? ChargedWaveShape : WaveShape.Forward);
        isAttacking = false;
        airSlashHoldUntil = Time.time + airSlashAnimHold / speed;
    }

    private IEnumerator AttackWindow(float duration, int damage, float speed, bool fireWave, float waveDistanceMul,
        WaveShape waveShape = WaveShape.Forward)
    {
        float open = Mathf.Max(0f, hitboxDelay) / speed;
        float close = open + Mathf.Max(0.01f, hitboxActiveTime) / speed;
        bool opened = false;
        bool closed = false;
        float t = 0f;
        while (t < duration)
        {
            t += BossDeltaTime;
            if (!opened && t >= open)
            {
                opened = true;
                ActivateHitbox(damage);
                if (fireWave)
                    FireWaveShape(waveShape, damage, waveDistanceMul);
            }

            if (opened && !closed && t >= close)
            {
                closed = true;
                DeactivateHitbox();
            }

            yield return null;
        }

        if (!closed)
            DeactivateHitbox();
    }

    /// <summary>
    /// Charge slash / kick slide and Lace lunge: eased dash-speed slide with the attack box out.
    /// slashAtEnd finishes with a real slash swing (that's where the wave comes from).
    /// </summary>
    private IEnumerator SlideAttack(float distance, float speedVsDash, int damage, int combo, bool slashAtEnd)
    {
        FacePlayer();
        isAttacking = true;
        attackAnimSpeed = 1f;
        PlayAttackAnim(combo, air: false);
        if (combo != ComboKick)
            PlaySwing();
        ActivateHitbox(damage);

        float speed = Mathf.Max(0.1f, dashSpeed * Mathf.Max(0.1f, speedVsDash));
        float duration = Mathf.Max(0.06f, Mathf.Max(0f, distance) / speed);
        StartSlide(facingSign * Mathf.Max(0f, distance), duration, easeOut: true);
        forceTrail = true;
        BeginDashAfterimageTrail(duration);

        float t = 0f;
        while (motion == Motion.Slide && t < duration + 0.3f)
        {
            t += BossDeltaTime;
            yield return null;
        }

        forceTrail = false;
        DeactivateHitbox();
        motion = Motion.Stop;
        isAttacking = false;
        if (slashAtEnd)
            yield return GroundAttack(ComboSlash2, 1.2f, damageOverride: damage, lungeMul: 0f, waveShape: ChargedWaveShape);
    }

    private IEnumerator Dash(float dir, float distance, float speedMul = 1f, int contactDamage = 0)
    {
        bool airborne = !isGrounded;
        if (airborne)
        {
            if (airDashesLeft <= 0)
                yield break;
            airDashesLeft--;
        }

        dir = Mathf.Abs(dir) > 0.01f ? Mathf.Sign(dir) : facingSign;
        float speed = Mathf.Max(0.1f, dashSpeed * Mathf.Max(0.1f, speedMul));
        float duration = Mathf.Max(0.05f, Mathf.Max(0f, distance) / speed);

        SetFacing(dir);
        isDashing = true;
        SetGravity(false);
        motion = Motion.Velocity;
        motionVelocity = new Vector2(dir * speed, 0f);
        if (rb != null)
            rb.linearVelocity = new Vector2(BossSpeed(motionVelocity.x), 0f);
        BeginDashAfterimageTrail(duration);
        PlayAnimState("Dash");
        SoundManager.Instance?.PlayDash();
        if (contactDamage > 0)
            ActivateHitbox(contactDamage);

        float t = 0f;
        while (t < duration)
        {
            t += BossDeltaTime;
            yield return null;
        }

        if (contactDamage > 0)
            DeactivateHitbox();
        EndDash();
    }

    /// <summary>stabPose = Hex's dive stab (her pogo / fall pose) instead of the air slash.</summary>
    private IEnumerator Dive(Vector2 velocity, int damage, float timeout, bool stabPose = false)
    {
        isAttacking = true;
        attackAnimSpeed = 1f;
        if (stabPose)
        {
            if (HasParam("Attack"))
                animator.ResetTrigger("Attack");
            PlayAnimState("fall");
        }
        else
        {
            PlayAttackAnim(ComboKick, air: true);
        }
        PlaySwing();
        ActivateHitbox(damage);
        SetGravity(false);
        motion = Motion.Velocity;
        motionVelocity = velocity;
        forceTrail = true;
        BeginDashAfterimageTrail(0.3f);

        float t = 0f;
        while (!isGrounded && t < timeout)
        {
            t += BossDeltaTime;
            yield return null;
        }

        forceTrail = false;
        DeactivateHitbox();
        SetGravity(true);
        motion = isGrounded ? Motion.Stop : Motion.Free;
        if (rb != null)
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
        isAttacking = false;
    }

    private IEnumerator Hang(float seconds, bool telegraph)
    {
        motion = Motion.Hang;
        SetGravity(false);
        FacePlayer();

        float t = 0f;
        while (t < seconds)
        {
            t += BossDeltaTime * Tempo;
            if (telegraph)
                ShowAura(telegraphAura, telegraphColor, Color.white, 0.45f + 0.45f * Mathf.PingPong(t * 8f, 1f), 1.25f);
            yield return null;
        }

        HideAura(telegraphAura);
        SetGravity(true);
        motion = Motion.Free;
    }

    private IEnumerator Telegraph(float seconds)
    {
        motion = Motion.Stop;
        float t = 0f;
        while (t < seconds)
        {
            t += BossDeltaTime * Tempo;
            FacePlayer();
            ShowAura(telegraphAura, telegraphColor, Color.white, 0.45f + 0.45f * Mathf.PingPong(t * 8f, 1f), 1.25f);
            yield return null;
        }

        HideAura(telegraphAura);
    }

    /// <summary>Holds a charge (aura grows). keepDistance &gt; 0 backs off to that range while charging.</summary>
    private IEnumerator ChargeUp(float seconds, float keepDistance)
    {
        isCharging = true;
        chargeTimer = 0f;
        while (chargeTimer < seconds)
        {
            chargeTimer += BossDeltaTime * Tempo;
            float dx = DxToPlayer();
            if (keepDistance > 0f && Mathf.Abs(dx) < keepDistance && isGrounded)
            {
                motion = Motion.Walk;
                walkDir = -Mathf.Sign(dx);
            }
            else
            {
                motion = Motion.Stop;
                walkDir = 0f;
                FacePlayer();
            }

            yield return null;
        }

        motion = Motion.Stop;
        walkDir = 0f;
        FacePlayer();
    }

    // ---------- Movement helpers ----------

    private float DxToPlayer()
    {
        PlayerController player = FindPlayer();
        return player != null ? player.transform.position.x - transform.position.x : 0f;
    }

    private float DirToPlayer()
    {
        float dx = DxToPlayer();
        return Mathf.Abs(dx) > 0.05f ? Mathf.Sign(dx) : facingSign;
    }

    private bool PlayerInRange(float range)
    {
        PlayerController player = FindPlayer();
        return player != null && !player.IsDead && Mathf.Abs(DxToPlayer()) <= range;
    }

    private static Bounds PlayerBounds(PlayerController player)
    {
        return player.BodyCollider != null
            ? player.BodyCollider.bounds
            : new Bounds(player.transform.position, Vector3.one);
    }

    private Bounds MyBounds()
    {
        return bodyCollider != null ? bodyCollider.bounds : new Bounds(transform.position, Vector3.one);
    }

    private float PlayerFeetAboveMine(PlayerController player)
    {
        return PlayerBounds(player).min.y - MyBounds().min.y;
    }

    private float PlayerHeightAboveMe()
    {
        PlayerController player = FindPlayer();
        return player != null ? PlayerBounds(player).center.y - MyBounds().center.y : 0f;
    }

    /// <summary>Gap between her body and the player's body is within (reachX, reachY).</summary>
    private bool PlayerBodyWithin(float reachX, float reachY)
    {
        PlayerController player = FindPlayer();
        if (player == null || player.IsDead)
            return false;

        Bounds pb = PlayerBounds(player);
        Bounds mb = MyBounds();
        float gapX = Mathf.Abs(pb.center.x - mb.center.x) - pb.extents.x - mb.extents.x;
        float gapY = Mathf.Abs(pb.center.y - mb.center.y) - pb.extents.y - mb.extents.y;
        return gapX <= reachX && gapY <= reachY;
    }

    private bool PlayerInAirSlashReach() => PlayerBodyWithin(airSlashReachX, airSlashReachY);

    /// <summary>Player overlaps the charged wave's lane (her center height ± lane) within its travel range.</summary>
    private bool PlayerInChargedWaveLane()
    {
        PlayerController player = FindPlayer();
        if (player == null || player.IsDead)
            return false;

        Bounds pb = PlayerBounds(player);
        Vector3 c = MyBounds().center;
        float gapX = Mathf.Abs(pb.center.x - c.x) - pb.extents.x;
        float gapY = Mathf.Abs(pb.center.y - c.y) - pb.extents.y;
        return gapX <= chargedAirSlashReachX && gapY <= chargedAirSlashLaneY;
    }

    private void FacePlayer()
    {
        PlayerController player = FindPlayer();
        if (player != null)
            FaceToward(player.transform);
        SyncAttackBoxFacing();
    }

    private void SetFacing(float dir)
    {
        SetFacingSign(dir);
        SyncAttackBoxFacing();
    }

    private void SetGravity(bool on)
    {
        gravityOff = !on;
        if (rb != null)
            rb.gravityScale = on ? defaultGravityScale : 0f;
    }

    private float Gravity => Mathf.Abs(Physics2D.gravity.y) * Mathf.Max(0.01f, defaultGravityScale);

    private float EstimateAirTime(float launchVy)
    {
        float g = Gravity;
        float up = launchVy / g;
        float peak = launchVy * launchVy / (2f * g);
        float down = Mathf.Sqrt(2f * peak / (g * Mathf.Max(1f, fallMultiplier)));
        return Mathf.Max(0.2f, up + down);
    }

    /// <summary>Jump at the player; travelFraction 1 = land on them.</summary>
    private void JumpToward(float travelFraction)
    {
        float vy = Mathf.Max(0.1f, jumpForce);
        float vx = DxToPlayer() * Mathf.Clamp01(travelFraction) / EstimateAirTime(vy);
        vx = Mathf.Clamp(vx, -dashSpeed, dashSpeed);
        LaunchJump(vx, vy);
    }

    private void JumpAbovePlayer()
    {
        float vy = Mathf.Max(0.1f, jumpForce);
        LaunchJump(VelocityToReachPlayerAtApex(vy), vy);
    }

    private float VelocityToReachPlayerAtApex(float vy)
    {
        float timeToApex = Mathf.Max(0.15f, vy / Gravity);
        return Mathf.Clamp(DxToPlayer() / timeToApex, -dashSpeed, dashSpeed);
    }

    private void LaunchJump(float vx, float vy)
    {
        if (rb == null)
            return;

        if (Mathf.Abs(vx) > 0.05f)
            SetFacing(vx);
        motion = Motion.Free;
        SetGravity(true);
        rb.linearVelocity = new Vector2(BossSpeed(vx), vy);
        isGrounded = false;
        PlayAnimState("Jump");
        SoundManager.Instance?.PlayJump();
    }

    private bool TryDoubleJump(float vx)
    {
        if (isGrounded || airJumpsLeft <= 0 || rb == null)
            return false;

        airJumpsLeft--;
        motion = Motion.Free;
        SetGravity(true);
        if (Mathf.Abs(vx) > 0.05f)
            SetFacing(vx);
        rb.linearVelocity = new Vector2(BossSpeed(vx), Mathf.Max(0.1f, doubleJumpForce));
        PlayAnimState("Jump");
        SoundManager.Instance?.PlayJump();
        return true;
    }

    private void SteerTowardPlayerInAir()
    {
        if (rb == null || isGrounded)
            return;

        float dx = DxToPlayer();
        float target = Mathf.Abs(dx) < 0.2f ? 0f : Mathf.Sign(dx) * airSteerSpeed;
        float vx = Mathf.MoveTowards(rb.linearVelocity.x, BossSpeed(target), BossSpeed(airSteerSpeed) * 4f * BossDeltaTime);
        rb.linearVelocity = new Vector2(vx, rb.linearVelocity.y);
        if (Mathf.Abs(dx) > 0.2f)
            SetFacing(dx);
    }

    private void StartLunge(int combo, float attackDuration, float speed, float lungeMul = 1f)
    {
        float baseDistance = combo switch
        {
            ComboSlash1 => slash1LungeDistance,
            ComboSlash2 => slash2LungeDistance,
            ComboSlash3 => slash3LungeDistance,
            _ => kickLungeDistance
        };
        float distance = baseDistance * Mathf.Max(0f, lungeMul);
        if (distance <= 0f || !isGrounded)
            return;

        float duration = combo == ComboKick
            ? distance / Mathf.Max(0.01f, dashSpeed * Mathf.Max(1.01f, kickSpeedVsDash) * speed)
            : Mathf.Min(attackDuration * 0.45f, Mathf.Max(0.03f, distance / Mathf.Max(0.01f, moveSpeed * 4f * speed)));
        StartSlide(facingSign * distance, duration, easeOut: false);
    }

    private void StartSlide(float deltaX, float duration, bool easeOut)
    {
        if (rb == null)
            return;

        slideStartX = rb.position.x;
        slideEndX = slideStartX + deltaX;
        slideDuration = Mathf.Max(Time.fixedDeltaTime * 2f, duration);
        slideTimer = 0f;
        slideEaseOut = easeOut;
        motion = Motion.Slide;
    }

    private void TickSlideFixed()
    {
        slideTimer += BossFixedDeltaTime;
        float t = Mathf.Clamp01(slideTimer / slideDuration);
        float eased = slideEaseOut ? 1f - Mathf.Pow(1f - t, 3f) : t * t * t * (t * (t * 6f - 15f) + 10f);

        Vector2 pos = rb.position;
        float prevX = pos.x;
        pos.x = Mathf.Lerp(slideStartX, slideEndX, eased);
        rb.MovePosition(pos);
        float vx = (pos.x - prevX) / Mathf.Max(BossFixedDeltaTime, 0.0001f);
        rb.linearVelocity = new Vector2(vx, isGrounded ? 0f : rb.linearVelocity.y);

        if (t < 1f)
            return;

        rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
        motion = Motion.Stop;
    }

    private void EndDash()
    {
        if (!isDashing)
            return;

        isDashing = false;
        SetGravity(true);
        if (motion == Motion.Velocity)
            motion = isGrounded ? Motion.Stop : Motion.Free;
        if (rb != null)
            rb.linearVelocity = new Vector2(rb.linearVelocity.x * 0.25f, 0f);
    }

    private void RefillAirActions()
    {
        airJumpsLeft = Mathf.Max(0, maxAirJumps);
        airDashesLeft = Mathf.Max(0, maxAirDashes);
    }

    // ---------- Hitbox / jump attack / pogo ----------

    private void ActivateHitbox(int damage)
    {
        if (attackHitbox == null)
            return;

        jumpAttackActive = false;
        attackHitboxInUse = true;
        SyncAttackBoxFacing();
        attackHitbox.Activate(Mathf.Max(0, damage), applyDamage: true);
    }

    private void DeactivateHitbox()
    {
        attackHitboxInUse = false;
        if (attackHitbox != null && !jumpAttackActive)
            attackHitbox.Deactivate();
    }

    /// <summary>Playable Harlie's automatic falling attack: any idle airborne moment is dangerous.</summary>
    private void TickJumpAttack()
    {
        if (attackHitbox == null)
            return;

        bool want = !isGrounded && !isAttacking && !isDashing && !attackHitboxInUse &&
                    motion != Motion.Hang && Time.time >= airSlashHoldUntil;
        if (want && !jumpAttackActive)
        {
            jumpAttackActive = true;
            attackHitbox.Activate(Damage(jumpAttackDamage), applyDamage: true);
        }
        else if (!want && jumpAttackActive)
        {
            jumpAttackActive = false;
            if (!attackHitboxInUse)
                attackHitbox.Deactivate();
        }
    }

    private void HandleAttackTargetAcquired(Collider2D other, PlayerController player)
    {
        if (!jumpAttackActive || player == null || rb == null || isGrounded)
            return;

        if (rb.linearVelocity.y > 0.05f || transform.position.y < player.transform.position.y + 0.3f)
            return;

        airJumpsLeft = Mathf.Max(0, maxAirJumps);
        rb.linearVelocity = new Vector2(0f, Mathf.Max(0.1f, aerialBounceForce));
        pogoHappened = true;
        attackHitbox.ForgetHitInstance(player.GetInstanceID());
    }

    private MaliceSlashProjectile FireWave(int damage, float distanceMul, float dir, bool fromAttackBox,
        Vector2? direction = null, MaliceSlashVolley volley = null, Vector3? spawn = null)
    {
        // Waves only ever come out of a swing the player can see.
        if (slashWave == null || !isAttacking)
            return null;

        SyncAttackBoxFacing();
        Collider2D box = fromAttackBox && attackHitbox != null ? attackHitbox.GetComponent<Collider2D>() : null;
        MaliceSlashProjectile wave = slashWave.Fire(transform, box, dir, damage, spriteRenderer,
            distanceMul, 0f, 0f, 1f, 0f, false, direction, volley, spawn);
        if (wave != null)
            wave.SetDurability(waveDurability);
        return wave;
    }

    /// <summary>Charged slashes: one wave for Harlie, three (straight / up / down) for Hex.</summary>
    private WaveShape ChargedWaveShape => useHexMoves ? WaveShape.Triple : WaveShape.Forward;

    private void FireWaveShape(WaveShape shape, int damage, float distanceMul)
    {
        switch (shape)
        {
            case WaveShape.BothWays:
                FireWave(damage, distanceMul, facingSign, fromAttackBox: true);
                FireWave(damage, distanceMul, -facingSign, fromAttackBox: false);
                break;
            case WaveShape.Triple:
            {
                MaliceSlashVolley volley = new MaliceSlashVolley();
                float rad = hexTripleWaveAngle * Mathf.Deg2Rad;
                Vector2 up = new Vector2(facingSign * Mathf.Cos(rad), Mathf.Sin(rad));
                FireWave(damage, distanceMul, facingSign, true, null, volley);
                FireWave(damage, distanceMul, facingSign, true, up, volley);
                FireWave(damage, distanceMul, facingSign, true, new Vector2(up.x, -up.y), volley);
                break;
            }
            case WaveShape.Fan:
            {
                MaliceSlashVolley volley = new MaliceSlashVolley();
                Vector3 origin = MyBounds().center;
                for (int i = 0; i < HexSlamFan.Length; i++)
                {
                    Vector2 dir = new Vector2(HexSlamFan[i].x * facingSign, HexSlamFan[i].y).normalized;
                    FireWave(damage, distanceMul, facingSign, false, dir, volley, origin);
                }
                break;
            }
            default:
                FireWave(damage, distanceMul, facingSign, fromAttackBox: true);
                break;
        }
    }

    private void SummonVBots()
    {
        if (vBotPrefab == null)
            return;

        vBotReadyAt = Time.time + Mathf.Max(0f, vBotCooldown);
        liveVBots.RemoveAll(bot => bot == null);
        Vector3 center = MyBounds().center;
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 pos = new Vector3(center.x + side * vBotSideOffset, center.y, 0f);
            HexVBot bot = Instantiate(vBotPrefab, pos, Quaternion.identity);
            bot.Launch(transform, facingSign);
            liveVBots.Add(bot);
            if (sparkEmitter != null)
                sparkEmitter.Burst(pos);
        }
    }

    private void BurstSparksAtFeet()
    {
        if (sparkEmitter == null)
            return;

        Bounds b = MyBounds();
        sparkEmitter.Burst(new Vector3(b.center.x, b.min.y, 0f));
    }

    private void SyncAttackBoxFacing()
    {
        if (attackHitbox == null)
            return;

        Transform box = attackHitbox.transform;
        Vector3 current = box.parent == transform ? box.localPosition : transform.InverseTransformPoint(box.position);
        bool unchangedSinceWeWrote = attackBoxHasLastWritten &&
                                     (current - attackBoxLastWrittenLocal).sqrMagnitude < 1e-10f;
        if (!unchangedSinceWeWrote)
            attackBoxLeftFacingLocal = current;

        Vector3 target = attackBoxLeftFacingLocal;
        bool mirror = spriteFacesLeft ? facingSign > 0f : facingSign < 0f;
        if (mirror)
            target.x = attackBoxFlipCenterX - (attackBoxLeftFacingLocal.x - attackBoxFlipCenterX);

        if (box.parent == transform)
            box.localPosition = target;
        else
            box.position = transform.TransformPoint(target);

        attackBoxLastWrittenLocal = target;
        attackBoxHasLastWritten = true;
    }

    // ---------- Combo data ----------

    private int ComboDamage(int combo) => combo switch
    {
        ComboSlash1 => slash1Damage,
        ComboSlash2 => slash2Damage,
        ComboSlash3 => slash3Damage,
        _ => kickDamage
    };

    private float ComboDuration(int combo) => combo switch
    {
        ComboSlash1 => slash1Duration,
        ComboSlash2 => slash2Duration,
        ComboSlash3 => slash3Duration,
        _ => kickDuration
    };

    private static string ComboStateName(int combo) => combo switch
    {
        ComboSlash1 => "slash 1",
        ComboSlash2 => "slash 2",
        ComboSlash3 => "slash 3",
        _ => "kick"
    };

    private float ResolveDuration(string clipName, float fallback)
    {
        if (!syncAttackDurationToAnimation || animator == null || animator.runtimeAnimatorController == null)
            return Mathf.Max(0.05f, fallback);

        AnimationClip[] clips = animator.runtimeAnimatorController.animationClips;
        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i] != null && string.Equals(clips[i].name, clipName, System.StringComparison.OrdinalIgnoreCase))
                return Mathf.Max(0.05f, clips[i].length + Mathf.Max(0f, attackDurationPadding));
        }

        return Mathf.Max(0.05f, fallback);
    }

    private static void PlaySwing()
    {
        SoundManager.Instance?.PlayHarlieSwordSwing();
    }

    // ---------- Animator ----------

    private void CacheAnimatorParams()
    {
        animatorParams.Clear();
        if (animator == null)
            return;

        foreach (AnimatorControllerParameter param in animator.parameters)
            animatorParams.Add(param.nameHash);
    }

    private bool HasParam(string paramName) => animatorParams.Contains(Animator.StringToHash(paramName));

    private void SetAnimBool(string paramName, bool value)
    {
        if (HasParam(paramName))
            animator.SetBool(paramName, value);
    }

    private void SetAnimFloat(string paramName, float value)
    {
        if (HasParam(paramName))
            animator.SetFloat(paramName, value);
    }

    private void SetAnimInt(string paramName, int value)
    {
        if (HasParam(paramName))
            animator.SetInteger(paramName, value);
    }

    private void PlayAnimState(string stateName)
    {
        if (animator == null)
            return;

        int hash = Animator.StringToHash(stateName);
        if (animator.HasState(0, hash))
            animator.Play(hash, 0, 0f);
    }

    private void PlayAttackAnim(int combo, bool air)
    {
        currentCombo = combo;
        if (animator == null)
            return;

        SetAnimInt("AttackCombo", combo);
        SetAnimBool("IsAttacking", true);
        SetAnimBool("IsGrounded", !air);
        SetAnimBool("IsInAir", air);
        // States are played directly. A leftover Attack trigger would let the controller's Any State
        // transitions swap in a different attack clip (or the air slash) later.
        if (HasParam("Attack"))
            animator.ResetTrigger("Attack");

        PlayAnimState(air ? "air slash" : ComboStateName(combo));
    }

    // ---------- Auras ----------

    private Aura CreateAura(string label)
    {
        if (spriteRenderer == null)
            return null;

        Aura aura = new Aura { go = new GameObject($"{name}_{label}") };
        aura.go.transform.SetParent(transform, false);
        aura.go.transform.localPosition = Vector3.zero;
        aura.go.transform.localScale = Vector3.one * auraBaseScale;
        aura.go.transform.SetAsFirstSibling();
        aura.sr = aura.go.AddComponent<SpriteRenderer>();
        aura.sr.sprite = spriteRenderer.sprite;
        CharacterEffectSorting.ApplyAuraBehindBody(aura.sr, spriteRenderer, EffectSortingGroup);

        Shader solid = Shader.Find("Gameoverse/SpriteSolidColor");
        if (solid != null)
        {
            aura.material = new Material(solid);
            aura.sr.sharedMaterial = aura.material;
        }

        aura.go.SetActive(false);
        return aura;
    }

    private void ShowAura(Aura aura, Color baseColor, Color flickerColor, float alpha, float scale)
    {
        if (aura == null || aura.go == null || spriteRenderer == null)
            return;

        aura.go.SetActive(true);
        aura.sr.sprite = spriteRenderer.sprite;
        aura.sr.flipX = spriteRenderer.flipX;
        CharacterEffectSorting.ApplyAuraBehindBody(aura.sr, spriteRenderer, EffectSortingGroup);

        aura.phase += BossDeltaTime * 1.5f;
        float shimmer = 0.5f + 0.5f * Mathf.Sin(aura.phase * Mathf.PI * 2f);
        Color c = Color.Lerp(baseColor, flickerColor, shimmer * 0.3f);
        c.a = Mathf.Clamp01(alpha);
        aura.sr.color = c;
        aura.go.transform.localScale = Vector3.one * scale;
    }

    private static void HideAura(Aura aura)
    {
        if (aura == null || aura.go == null || !aura.go.activeSelf)
            return;

        aura.go.SetActive(false);
        aura.phase = 0f;
    }

    private void DestroyAura(Aura aura)
    {
        if (aura == null)
            return;
        if (aura.go != null)
            Destroy(aura.go);
        if (aura.material != null)
            Destroy(aura.material);
    }

    private void TickChargeAura()
    {
        float charge01 = isCharging ? Mathf.Clamp01(chargeTimer / Mathf.Max(0.05f, chargeMaxSeconds)) : 0f;
        if (sparkEmitter != null)
        {
            sparkEmitter.Streaming = isCharging;
            sparkEmitter.ChargeProgress = charge01;
            sparkEmitter.FullCharge = charge01 >= 0.999f;
        }

        if (!isCharging)
        {
            HideAura(chargeAura);
            return;
        }

        float pulse = 1f + Mathf.Sin(Time.time * 6f) * 0.05f * charge01;
        ShowAura(chargeAura, chargeAuraColor, chargeAuraFlickerColor, Mathf.Lerp(0.25f, 0.95f, charge01), auraBaseScale * pulse);
    }

    private void TickEnrageAura()
    {
        if (!showEnrageAura || !IsEnraged || IsDead)
        {
            HideAura(enrageAura);
            return;
        }

        bool desperate = IsDesperate;
        float alpha = desperate ? Mathf.Min(1f, enrageAuraAlpha + 0.15f) : enrageAuraAlpha;
        ShowAura(enrageAura, enrageAuraColor, enrageAuraFlickerColor, alpha, auraBaseScale * (desperate ? 1.13f : 1.08f));
    }

    private void EndCharge()
    {
        isCharging = false;
        chargeTimer = 0f;
        HideAura(chargeAura);
        if (sparkEmitter != null)
            sparkEmitter.Streaming = false;
    }

    // ---------- Dash afterimages ----------

    private void SetupDashAfterimages()
    {
        DestroyDashAfterimages();
        if (!enableDashAfterimages || spriteRenderer == null)
            return;

        int count = Mathf.Max(1, dashAfterimageCount);
        dashAfterimageObjects = new GameObject[count];
        dashAfterimageRenderers = new SpriteRenderer[count];
        for (int i = 0; i < count; i++)
        {
            var go = new GameObject($"{name}_DashAfterimage_{i + 1}");
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            CharacterEffectSorting.ApplyTrailBehindBody(sr, spriteRenderer, EffectSortingGroup, i);
            go.SetActive(false);
            dashAfterimageObjects[i] = go;
            dashAfterimageRenderers[i] = sr;
        }
    }

    private void DestroyDashAfterimages()
    {
        if (dashAfterimageObjects == null)
            return;

        for (int i = 0; i < dashAfterimageObjects.Length; i++)
        {
            if (dashAfterimageObjects[i] != null)
                Destroy(dashAfterimageObjects[i]);
        }

        dashAfterimageObjects = null;
        dashAfterimageRenderers = null;
    }

    private float AfterimageDelay(int index) => Mathf.Max(0.01f, dashAfterimageSpacing) * (index + 1);

    private float MaxAfterimageDelay => AfterimageDelay(Mathf.Max(1, dashAfterimageCount) - 1);

    private void BeginDashAfterimageTrail(float activeDuration)
    {
        if (!enableDashAfterimages || dashAfterimageRenderers == null)
            return;

        dashAfterimagesVisibleUntil = Time.time + Mathf.Max(activeDuration, MaxAfterimageDelay) + dashAfterimageLifetimePadding;
    }

    private void RecordPoseHistory()
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

        float cutoff = Time.time - (MaxAfterimageDelay + 0.35f);
        while (poseHistory.Count > 0 && poseHistory[0].time < cutoff)
            poseHistory.RemoveAt(0);
    }

    private bool TrySamplePose(float delay, out PoseSample sample)
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

    private void UpdateDashAfterimages()
    {
        if (dashAfterimageRenderers == null || spriteRenderer == null)
            return;

        bool moving = isDashing || forceTrail;
        if (moving)
            dashAfterimagesVisibleUntil = Mathf.Max(dashAfterimagesVisibleUntil,
                Time.time + MaxAfterimageDelay + dashAfterimageLifetimePadding);

        bool active = moving || Time.time <= dashAfterimagesVisibleUntil;
        for (int i = 0; i < dashAfterimageRenderers.Length; i++)
        {
            GameObject go = dashAfterimageObjects[i];
            if (go == null)
                continue;

            if (!active || !TrySamplePose(AfterimageDelay(i), out PoseSample sample))
            {
                if (go.activeSelf)
                    go.SetActive(false);
                continue;
            }

            go.SetActive(true);
            go.transform.position = sample.position;
            SpriteRenderer sr = dashAfterimageRenderers[i];
            sr.sprite = sample.sprite != null ? sample.sprite : spriteRenderer.sprite;
            sr.flipX = sample.flipX;
            CharacterEffectSorting.ApplyTrailBehindBody(sr, spriteRenderer, EffectSortingGroup, i);

            float t = dashAfterimageRenderers.Length == 1 ? 0f : i / (float)(dashAfterimageRenderers.Length - 1);
            Color c = dashAfterimageColor;
            c.a = Mathf.Lerp(dashAfterimageAlphaStart, dashAfterimageAlphaEnd, t);
            sr.color = c;
        }
    }

    // ---------- Reset ----------

    private void CleanupAfterPattern()
    {
        EndDash();
        EndCharge();
        HideAura(telegraphAura);
        isAttacking = false;
        forceTrail = false;
        attackAnimSpeed = 1f;
        attackHitboxInUse = false;
        if (attackHitbox != null && !jumpAttackActive)
            attackHitbox.Deactivate();
        SetGravity(true);
        if (motion != Motion.Free)
            motion = Motion.Stop;
        walkDir = 0f;
    }

    private void ResetActions()
    {
        actionStack.Clear();
        CleanupAfterPattern();
        jumpAttackActive = false;
        if (attackHitbox != null)
            attackHitbox.Deactivate();
        motion = Motion.Stop;
        airSlashHoldUntil = 0f;
        if (swordSquad != null)
            swordSquad.CancelAttack();
    }

    protected override void OnCombatPaused()
    {
        ResetActions();
        base.OnCombatPaused();
    }

    protected override void OnRevived()
    {
        ResetActions();
        RefillAirActions();
        hitVfxTimer = 0f;
        System.Array.Clear(zoneRotationIndex, 0, zoneRotationIndex.Length);
        rotationsDesperate = false;
        vBotReadyAt = -999f;
        DismissSilverSwords();
        swordsSummoned = false;
        swordsWereAttacking = false;
        ClearOrbitVBots();
        orbitVBotsUnlocked = false;
    }

    protected override void OnBossDefeated()
    {
        ResetActions();
        DismissSilverSwords();
        ClearOrbitVBots();
        HideAura(enrageAura);
        SetDashAfterimagesHidden();
        hitVfxTimer = 0f;
        VisualEffects.StopStunned(this);
        if (animator != null)
        {
            animator.speed = 1f;
            SetAnimBool("IsDefeated", true);
        }

        enabled = false;
    }

    private void SetDashAfterimagesHidden()
    {
        if (dashAfterimageObjects == null)
            return;

        for (int i = 0; i < dashAfterimageObjects.Length; i++)
        {
            if (dashAfterimageObjects[i] != null)
                dashAfterimageObjects[i].SetActive(false);
        }
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        damageMultiplier = Mathf.Max(0.1f, damageMultiplier);
        copyBotMaxHealth = Mathf.Max(0, copyBotMaxHealth);
        backdashIfPlayerWithin = Mathf.Max(0f, backdashIfPlayerWithin);
        comboChaseMaxGap = Mathf.Max(0f, comboChaseMaxGap);
        lifeBarCount = Mathf.Max(1, lifeBarCount);
        tempoPerBarLost = Mathf.Max(0f, tempoPerBarLost);
        moveSpeedPerBarLost = Mathf.Max(0f, moveSpeedPerBarLost);
        flurrySlashes = Mathf.Max(1, flurrySlashes);
        flurrySpeedMul = Mathf.Max(0.1f, flurrySpeedMul);
        flurryWaveDistanceMultiplier = Mathf.Max(0.1f, flurryWaveDistanceMultiplier);
        desperateHealthRatio = Mathf.Clamp01(desperateHealthRatio);
        desperateTempoBonus = Mathf.Max(0f, desperateTempoBonus);
        risingSaberForce = Mathf.Max(1f, risingSaberForce);
        tripleSaberSpeedMul = Mathf.Max(0.1f, tripleSaberSpeedMul);
        tripleSaberLungeMultiplier = Mathf.Max(0f, tripleSaberLungeMultiplier);
        tripleSaberFinalWaveMultiplier = Mathf.Max(0.1f, tripleSaberFinalWaveMultiplier);
        chargedWaveChargeSeconds = Mathf.Max(0.1f, chargedWaveChargeSeconds);
        chargedWaveDistanceMultiplier = Mathf.Max(0.1f, chargedWaveDistanceMultiplier);
        closeZoneDistance = Mathf.Max(0.5f, closeZoneDistance);
        midZoneDistance = Mathf.Max(closeZoneDistance, midZoneDistance);
        antiAirHeight = Mathf.Max(0.1f, antiAirHeight);
        antiAirMaxDistance = Mathf.Max(0f, antiAirMaxDistance);
        airSlashReachX = Mathf.Max(0f, airSlashReachX);
        airSlashReachY = Mathf.Max(0f, airSlashReachY);
        chargedAirSlashReachX = Mathf.Max(0f, chargedAirSlashReachX);
        chargedAirSlashLaneY = Mathf.Max(0f, chargedAirSlashLaneY);
        slashRange = Mathf.Max(0.5f, slashRange);
        slashApproachTimeout = Mathf.Max(0.1f, slashApproachTimeout);
        recoverDurationFullHp = Mathf.Max(0.05f, recoverDurationFullHp);
        recoverDurationLowHp = Mathf.Max(0.05f, recoverDurationLowHp);
        dashDistance = Mathf.Max(0.1f, dashDistance);
        dashSpeed = Mathf.Max(0.1f, dashSpeed);
        maxAirDashes = Mathf.Max(0, maxAirDashes);
        maxAirJumps = Mathf.Max(0, maxAirJumps);
        doubleJumpForce = Mathf.Max(0.1f, doubleJumpForce);
        airSteerSpeed = Mathf.Max(0f, airSteerSpeed);
        hitboxActiveTime = Mathf.Max(0.01f, hitboxActiveTime);
        aerialBounceForce = Mathf.Max(0.1f, aerialBounceForce);
        dashCancelSlashSpeedMul = Mathf.Max(1f, dashCancelSlashSpeedMul);
        kickSpeedVsDash = Mathf.Max(1.01f, kickSpeedVsDash);
        chargeMaxSeconds = Mathf.Max(0.05f, chargeMaxSeconds);
        chargeMaxSpaces = Mathf.Max(0f, chargeMaxSpaces);
        chargeSlideSpeedVsDash = Mathf.Max(0.1f, chargeSlideSpeedVsDash);
        chargedAirSlashDamageMultiplier = Mathf.Max(1, chargedAirSlashDamageMultiplier);
        chargedAirSlashDistanceMultiplier = Mathf.Max(1f, chargedAirSlashDistanceMultiplier);
        waveDurability = Mathf.Max(0, waveDurability);
        telegraphSeconds = Mathf.Max(0.05f, telegraphSeconds);
        laceLungeMaxDistance = Mathf.Max(1f, laceLungeMaxDistance);
        laceLungeSpeedVsDash = Mathf.Max(0.1f, laceLungeSpeedVsDash);
        crossUpCount = Mathf.Max(1, crossUpCount);
        crossUpOvershoot = Mathf.Max(0f, crossUpOvershoot);
        hornetDiveSpeed = Mathf.Max(1f, hornetDiveSpeed);
        flashDashSpeedMul = Mathf.Max(1f, flashDashSpeedMul);
        plungeSpeed = Mathf.Max(1f, plungeSpeed);
        hexDiveGlintSeconds = Mathf.Max(0f, hexDiveGlintSeconds);
        hexDiveSpeed = Mathf.Max(1f, hexDiveSpeed);
        hexDiveDamage = Mathf.Max(0, hexDiveDamage);
        hexSlamWaveDistanceMultiplier = Mathf.Max(0.1f, hexSlamWaveDistanceMultiplier);
        vBotCooldown = Mathf.Max(0f, vBotCooldown);
        vBotSideOffset = Mathf.Max(0f, vBotSideOffset);
        orbitVBotHealthRatio = Mathf.Clamp01(orbitVBotHealthRatio);
        orbitVBotCount = Mathf.Clamp(orbitVBotCount, 1, 16);
        orbitVBotRadius = Mathf.Max(0.5f, orbitVBotRadius);
        orbitVBotTouchDamage = Mathf.Max(0, orbitVBotTouchDamage);
        orbitVBotLaunchDamage = Mathf.Max(0, orbitVBotLaunchDamage);
        orbitVBotLaunchSpaces = Mathf.Max(1f, orbitVBotLaunchSpaces);
        orbitVBotRefillSeconds = Mathf.Max(0f, orbitVBotRefillSeconds);
        silverSwordHealthRatio = Mathf.Clamp01(silverSwordHealthRatio);
        swordFirstPatternDelay = Mathf.Max(0f, swordFirstPatternDelay);
        swordPatternGap = Mathf.Max(0f, swordPatternGap);
        silverSwords?.Validate();
        silverSwordPatterns?.Validate();
        enrageHealthRatio = Mathf.Clamp01(enrageHealthRatio);
        enrageTempoBonus = Mathf.Max(0f, enrageTempoBonus);
        auraBaseScale = Mathf.Max(1f, auraBaseScale);
        dashAfterimageCount = Mathf.Max(1, dashAfterimageCount);
        slashWave?.Validate();
    }
#endif
}
