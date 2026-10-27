using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Boss Bass. Wears the four Dual System outfits (animator AbilityMode) and borrows Mega Man 2 Robot Master movement,
/// firing the SAME Dual prefabs as playable Bass (their owner is this boss, so they hurt the player):
///  • Default (0)       — Mega Man 7 Bass rapid buster assault with Double Gear.
///  • Metal Leaf (1)    — Metal Man hops + apex blade throws, a leap across the player with a midair blade, and a Leaf Shield
///                        hop press (Block Man final feel).
///  • Crashing Heat (2) — Crash Bomb pacing / hop lobs, and a Heat Man comet Heat Dash with contact damage (+ flare trail).
///  • Bubble Air (3)    — Blast Man style spaced bubble lobs while hopping away.
///  • Quick Time (4)    — Quick Man run + high-jump Quick Boomerang volleys, and a short Flash Man time stop with Flash Time Stars.
/// Double Gear turns on from time to time (longest while enraged) and stacks with Dual outfits for ×2 damage / size / spawns.
/// Each distance zone runs a fixed rotation that mixes the outfits so the fight can be learned.
/// At ≤ 55% HP (Boss Fight and Copy Bot) he enrages: faster tempo, chained moves, and every Nth pattern he drops into his
/// signature Heat Dash → Flash Time Trap combo. Tempo keeps climbing toward 0 HP within a fair cap.
/// </summary>
public class BossBass : Boss
{
    /// <summary>Same outfit indexes as <c>BassPlayerController.BassDualAbility</c> (animator AbilityMode).</summary>
    public const int OutfitDefault = 0;
    public const int OutfitMetalLeaf = 1;
    public const int OutfitCrashingHeat = 2;
    public const int OutfitBubbleAir = 3;
    public const int OutfitQuickTime = 4;

    private enum Pattern
    {
        MetalBladeHop,
        MetalCrossLeap,
        MetalShieldPress,
        CrashBombPace,
        HeatDashRush,
        BubbleLobBarrage,
        QuickBoomerangVolley,
        FlashTimeTrap,
        DoubleGearBusterAssault
    }

    private enum Zone { Close, Mid, Far, AntiAir }

    private enum Motion { Free, Stop, Walk, Velocity, Hang, Run, Raw }

    // Fixed, learnable rotations. Each mixes outfits; moves that are not ready (cooldown / missing prefab) are skipped.
    private static readonly Pattern[] CloseRotation =
    {
        Pattern.MetalShieldPress, Pattern.DoubleGearBusterAssault, Pattern.HeatDashRush,
        Pattern.BubbleLobBarrage, Pattern.QuickBoomerangVolley, Pattern.FlashTimeTrap
    };
    private static readonly Pattern[] MidRotation =
    {
        Pattern.MetalBladeHop, Pattern.MetalShieldPress, Pattern.DoubleGearBusterAssault, Pattern.CrashBombPace,
        Pattern.QuickBoomerangVolley, Pattern.MetalCrossLeap, Pattern.BubbleLobBarrage, Pattern.HeatDashRush
    };
    private static readonly Pattern[] FarRotation =
    {
        Pattern.CrashBombPace, Pattern.DoubleGearBusterAssault, Pattern.HeatDashRush, Pattern.MetalBladeHop,
        Pattern.QuickBoomerangVolley, Pattern.MetalCrossLeap, Pattern.BubbleLobBarrage, Pattern.FlashTimeTrap
    };
    private static readonly Pattern[] AntiAirRotation =
    {
        Pattern.MetalBladeHop, Pattern.DoubleGearBusterAssault, Pattern.BubbleLobBarrage,
        Pattern.QuickBoomerangVolley, Pattern.CrashBombPace
    };

    [Header("Identity")]
    [Tooltip("Max HP when spawned as a Copy Bot.")]
    [SerializeField] private int copyBotMaxHealth = 120;
    [Tooltip("Scales every attack's damage.")]
    [SerializeField] private float damageMultiplier = 1f;

    [Header("Dual Prefabs - Metal Leaf (same prefabs as playable Bass)")]
    [SerializeField] private BassMetalBlade metalBladePrefab;
    [SerializeField] private BassLeafShield leafShieldPrefab;

    [Header("Dual Prefabs - Crashing Heat")]
    [SerializeField] private BassCrashBomb crashBombPrefab;
    [SerializeField] private BassCrashBomb crashBombPlusPrefab;
    [SerializeField] private BassCrashBombExplosion crashBombExplosionPrefab;
    [SerializeField] private BassSmallHeatFlare smallHeatFlarePrefab;
    [SerializeField] private BassHeatFlareStack heatFlareStackPrefab;
    [SerializeField] private BassHeatFlareBall heatFlareBallPrefab;
    [SerializeField] private BassHeatDashFlare heatDashFlarePrefab;

    [Header("Dual Prefabs - Bubble Air")]
    [SerializeField] private BassBubbleBullet bubbleBulletPrefab;
    [SerializeField] private BassAirTornado airTornadoPrefab;

    [Header("Dual Prefabs - Quick Time")]
    [SerializeField] private BassQuickBoomerang quickBoomerangPrefab;
    [SerializeField] private BassFlashTimeStar flashTimeStarPrefab;

    [Header("Other Shots / Anchors")]
    [Tooltip("Optional Bass Buster shot (Crash Pace buster pokes / fallback).")]
    [SerializeField] private Projectile purpleShotPrefab;
    [Tooltip("Default-form green buster (Mega Man 7 Bass pattern when Double Gear is off).")]
    [SerializeField] private Projectile greenShotPrefab;
    [Tooltip("Double Gear buster shot (Mega Man 7 Bass pattern / DG bursts).")]
    [SerializeField] private Projectile doubleGearShotPrefab;
    [Tooltip("Authored for facing LEFT; mirrored when he faces right. Found by name when empty.")]
    [SerializeField] private Transform firePoint;
    [Tooltip("Heat Dash flare anchor. Found by name (Spark Spawner) when empty.")]
    [SerializeField] private Transform sparkSpawner;

    [Header("Tempo")]
    [SerializeField] private int lifeBarCount = 10;
    [Tooltip("Speed-up per life-bar segment lost (waits, telegraphs, recoveries).")]
    [SerializeField] private float tempoPerBarLost = 0.035f;
    [Tooltip("Extra tempo near 0 HP (quadratic with bars lost). Kept under Max Tempo so it stays fair.")]
    [SerializeField] private float lateFightTempoBonus = 0.4f;
    [Tooltip("Hard ceiling on Tempo so the late fight stays readable.")]
    [SerializeField] private float maxTempo = 1.85f;
    [SerializeField] private float moveSpeedPerBarLost = 0.18f;
    [SerializeField] private float recoverDurationFullHp = 0.28f;
    [SerializeField] private float recoverDurationLowHp = 0.08f;
    [Tooltip("During recover he still drifts toward preferred spacing instead of standing still.")]
    [SerializeField] private float recoverApproachSpeed = 5.5f;
    [Tooltip("Flash of colour when he swaps Dual outfits (also the telegraph for the next weapon).")]
    [SerializeField] private float outfitSwapGlintSeconds = 0.1f;
    [SerializeField] [Range(0f, 1f)] private float doubleGearWorldBlackTint = 0.28f;

    [Header("Double Gear")]
    [Tooltip("How long DG stays on outside enrage (real seconds).")]
    [SerializeField] private float doubleGearDuration = 4.5f;
    [Tooltip("How long DG stays on while enraged (real seconds). Renews while he is still enraged.")]
    [SerializeField] private float doubleGearEnragedDuration = 9f;
    [Tooltip("Idle time after a non-enraged DG window before he can turn it on again.")]
    [SerializeField] private float doubleGearCooldown = 5.5f;
    [Tooltip("World time scale while Boss Bass Double Gear is on (player keeps full speed).")]
    [SerializeField] [Range(0.2f, 1f)] private float doubleGearWorldTimeScale = 0.55f;
    [SerializeField] private float doubleGearMoveSpeedBonus = 4f;
    [Tooltip("Every this many patterns (when not enraged) he turns Double Gear on for a window.")]
    [SerializeField] private int doubleGearEveryPatterns = 3;
    [SerializeField] private float doubleGearShotSpeed = 15f;
    [SerializeField] private int doubleGearShotDamage = 2;
    [SerializeField] private float doubleGearBurstGap = 0.11f;
    [SerializeField] private float doubleGearAbilityScale = 1.3f;

    [Header("Zones")]
    [SerializeField] private float closeZoneDistance = 3.5f;
    [SerializeField] private float midZoneDistance = 8f;
    [Tooltip("Player feet this far above his feet (and within Anti Air Max Distance) = anti-air rotation.")]
    [SerializeField] private float antiAirHeight = 1.6f;
    [SerializeField] private float antiAirMaxDistance = 7f;

    [Header("Metal Leaf - Blade Hops (Metal Man)")]
    [SerializeField] private int metalHopsPerPattern = 3;
    [SerializeField] private float metalLowHopForce = 13f;
    [SerializeField] private float metalHighHopForce = 17f;
    [Tooltip("He drifts toward this distance from the player while hopping.")]
    [SerializeField] private float metalPreferredDistance = 6f;
    [Tooltip("Glint at the apex before the throw.")]
    [SerializeField] private float metalApexGlintSeconds = 0.14f;
    [Tooltip("Spaces between blades thrown in the same volley.")]
    [SerializeField] private float metalBladeSpacing = 2.4f;
    [SerializeField] private float metalBladeSpeed = 15f;
    [SerializeField] private int metalBladeDamage = 2;
    [Tooltip("Enraged volleys alternate this much angle off the player.")]
    [SerializeField] private float metalBladeSpreadDegrees = 10f;
    [SerializeField] private int metalBladeMaxLive = 6;

    [Header("Metal Leaf - Cross Leap")]
    [SerializeField] private float crossLeapGlintSeconds = 0.25f;
    [SerializeField] private float crossLeapForce = 17f;
    [Tooltip("Spaces past the player he aims to land.")]
    [SerializeField] private float crossLeapOvershoot = 3f;
    [SerializeField] private float crossLeapMaxSpeed = 11f;
    [SerializeField] private int crossLeapBlades = 1;
    [SerializeField] private float crossLeapRecoverSeconds = 0.2f;

    [Header("Metal Leaf - Leaf Shield Press (Block Man)")]
    [Tooltip("Leaves in his ring (playable Bass uses 12).")]
    [SerializeField] private int leafCount = 8;
    [SerializeField] private float leafOrbitRadius = 2.4f;
    [SerializeField] private float leafOrbitDegreesPerSecond = 190f;
    [SerializeField] private int leafHealth = 12;
    [SerializeField] private int leafContactDamage = 1;
    [Tooltip("Glint while the ring forms (he stands still).")]
    [SerializeField] private float shieldSummonSeconds = 0.22f;
    [SerializeField] private int shieldPressHops = 3;
    [SerializeField] private float shieldPressHopForce = 12f;
    [Tooltip("The press stops closing in at this distance.")]
    [SerializeField] private float shieldPressStopDistance = 3f;
    [SerializeField] private float shieldPressMaxHopSpeed = 5f;
    [SerializeField] private int shieldFinalVolley = 2;
    [Tooltip("The ring lingers this long after the last hop, then fades so there is always a gap.")]
    [SerializeField] private float shieldLingerSeconds = 0.35f;

    [Header("Crashing Heat - Crash Bomb Pace")]
    [SerializeField] private float crashPaceSeconds = 0.55f;
    [SerializeField] private float crashPreferredDistance = 7f;
    [SerializeField] private float crashHopForce = 12f;
    [SerializeField] private float crashGlintSeconds = 0.2f;
    [SerializeField] private float crashBombSpeed = 13f;
    [Tooltip("Multiplier on the Crash Bomb's stick + explosion damage (before Damage Multiplier).")]
    [SerializeField] private float crashBombDamageScale = 0.75f;
    [Tooltip("At or below this HP ratio he throws Crash Bomb Plus (heat balls). 0 = never.")]
    [Range(0f, 1f)] [SerializeField] private float crashBombPlusHealthRatio = 0.3f;
    [Tooltip("Heat ball damage scale (they are 5 damage on playable Bass).")]
    [SerializeField] private float heatBallDamageScale = 0.6f;
    [SerializeField] private float heatSpaceSize = 1f;
    [Tooltip("Optional Bass Buster pokes before the first bomb.")]
    [SerializeField] private int crashPaceBusterShots = 0;
    [SerializeField] private float crashPaceBusterSpeed = 14f;
    [SerializeField] private int crashPaceBusterDamage = 1;

    [Header("Crashing Heat - Heat Dash Rush (Heat Man)")]
    [Tooltip("Telegraph before the comet dash.")]
    [SerializeField] private float heatDashWindupSeconds = 0.5f;
    [SerializeField] private float heatDashSpeed = 26f;
    [Tooltip("How far past the player the dash ends.")]
    [SerializeField] private float heatDashOvershoot = 3f;
    [SerializeField] private float heatDashMinDistance = 4f;
    [SerializeField] private float heatDashMaxDistance = 16f;
    [SerializeField] private float heatDashTurnPause = 0.25f;
    [SerializeField] private int heatDashContactDamage = 4;
    [Tooltip("Heat Dash flare local X offset from the spark anchor (negative = behind him).")]
    [SerializeField] private float heatDashFlareOffsetX = 1.5f;
    [SerializeField] private bool heatDashFlareTrail = true;
    [Tooltip("Trail flares are only dropped while enraged.")]
    [SerializeField] private bool heatDashTrailOnlyEnraged = true;
    [SerializeField] private int heatDashTrailCount = 3;
    [SerializeField] private float heatDashTrailMinSpaces = 3f;
    [SerializeField] private float heatDashTrailMaxSpaces = 5f;
    [Tooltip("Damage scale of the stacks the trail flares leave.")]
    [SerializeField] private float heatDashTrailDamageScale = 0.75f;

    [Header("Bubble Air - Bubble Lob Barrage (Blast Man)")]
    [SerializeField] private int bubbleLobCount = 3;
    [SerializeField] private float bubbleHopForce = 12.5f;
    [Tooltip("Horizontal drift away from the player while he hops (negative = toward).")]
    [SerializeField] private float bubbleRetreatSpeed = 3.5f;
    [SerializeField] private float bubbleLobGlintSeconds = 0.12f;
    [Tooltip("Minimum seconds between bubbles so they stay spaced out.")]
    [SerializeField] private float bubbleLobGap = 0.65f;
    [SerializeField] private float bubbleLaunchSpeed = 10f;
    [Tooltip("Extra upward lean on the lob (0 = straight at the player).")]
    [SerializeField] private float bubbleLobUpLean = 0.25f;
    [SerializeField] private float bubbleDamage = 2f;
    [SerializeField] private float bubbleSpaceSize = 1f;
    [SerializeField] private int bubbleMaxLive = 3;

    [Header("Quick Time - Boomerang Volley (Quick Man)")]
    [Tooltip("He runs this long to reach his preferred range first.")]
    [SerializeField] private float quickRunSeconds = 0.4f;
    [SerializeField] private float quickRunSpeed = 12f;
    [SerializeField] private float quickPreferredDistance = 6.5f;
    [SerializeField] private float quickJumpForce = 18.5f;
    [SerializeField] private float quickGlintSeconds = 0.16f;
    [SerializeField] private int quickBoomerangCount = 3;
    [SerializeField] private float quickBoomerangSpreadDegrees = 22f;
    [SerializeField] private float quickBoomerangSpeed = 13f;
    [SerializeField] private float quickBoomerangOutSpaces = 6.5f;
    [SerializeField] private float quickSpaceSize = 1f;
    [SerializeField] private int quickBoomerangDamage = 2;
    [SerializeField] private int quickBoomerangMaxLive = 9;

    [Header("Default Form - Double Gear Buster Assault (Mega Man 7 Bass)")]
    [SerializeField] private float busterPreferredDistance = 5f;
    [SerializeField] private float busterApproachSpeed = 9.5f;
    [SerializeField] private int busterGroundBurstCount = 4;
    [SerializeField] private int busterAirBurstCount = 2;
    [SerializeField] private float busterHopForce = 14f;
    [SerializeField] private float busterDashSpeed = 18f;
    [SerializeField] private float busterDashDistance = 5.5f;

    [Header("Quick Time - Flash Time Trap (Flash Man)")]
    [Tooltip("Telegraph before time stops (flicker + ticks).")]
    [SerializeField] private float flashWindupSeconds = 0.7f;
    [SerializeField] private float flashCooldown = 7f;
    [Tooltip("Real seconds the world and the player stay frozen.")]
    [SerializeField] private float flashStopSeconds = 1.3f;
    [Tooltip("During the stop he closes to this distance so the boomerang follow-up is in face.")]
    [SerializeField] private float flashRepositionDistance = 2.75f;
    [SerializeField] private float flashRunSpeed = 13f;
    [SerializeField] private int flashStarCount = 36;
    [Tooltip("Viewport padding outside 0–1 so the stars cover more than the screen.")]
    [SerializeField] private float flashStarViewportPad = 0.35f;
    [Tooltip("After time resumes: fire right away when already in range (0 = instant).")]
    [SerializeField] private float flashFireDelay = 0.05f;
    [SerializeField] private int flashBoomerangCount = 5;
    [SerializeField] private float flashBoomerangSpreadDegrees = 30f;

    [Header("Enrage (HP ratio)")]
    [Range(0f, 1f)] [SerializeField] private float enrageHealthRatio = 0.55f;
    [SerializeField] private float enrageTempoBonus = 0.15f;
    [SerializeField] private float enrageMoveSpeedBonus = 1.5f;
    [SerializeField] private int enrageBonusDamage = 1;
    [Tooltip("Enraged: a few moves chain straight into another move with no recovery between.")]
    [SerializeField] private bool enragedChains = true;

    [Header("Signature Combo: Heat Dash → Flash Time Trap (HP ratio)")]
    [Tooltip("Boss Fight and Copy Bot: at this HP he drops his current move and starts the combo right away.")]
    [Range(0f, 1f)] [SerializeField] private float signatureHealthRatio = 0.55f;
    [Tooltip("After the first combo, it comes back every this many patterns (the combo counts as one).")]
    [SerializeField] private int signatureEvery = 3;

    [Header("Copy Bot")]
    [SerializeField] private float copyBotTempoBonus = 0.08f;

    [Header("Auras")]
    [SerializeField] private Color glintColor = Color.white;
    [SerializeField] private Color telegraphColor = new Color(1f, 0.55f, 0.15f, 1f);
    [SerializeField] private bool showEnrageAura = true;
    [SerializeField] private Color enrageAuraColor = new Color(0.8f, 0.1f, 0.35f, 1f);
    [SerializeField] private Color enrageAuraFlickerColor = new Color(1f, 0.45f, 0.65f, 1f);
    [Range(0f, 1f)] [SerializeField] private float enrageAuraAlpha = 0.4f;
    [SerializeField] private float auraBaseScale = 1.12f;

    [Header("Afterimages")]
    [SerializeField] private bool enableAfterimages = true;
    [SerializeField] private int afterimageCount = 5;
    [SerializeField] private float afterimageSpacing = 0.045f;
    [SerializeField] private Color afterimageColor = new Color(1f, 0.6f, 0.2f, 1f);
    [Range(0f, 1f)] [SerializeField] private float afterimageAlphaStart = 0.55f;
    [Range(0f, 1f)] [SerializeField] private float afterimageAlphaEnd = 0.12f;
    [SerializeField] private float afterimageLifetimePadding = 0.08f;

    // ---------- Runtime ----------

    private readonly Stack<IEnumerator> actionStack = new Stack<IEnumerator>();
    private readonly int[] zoneRotationIndex = new int[4];
    private Motion motion = Motion.Stop;
    private float walkDir;
    private Vector2 motionVelocity;
    private bool gravityOff;

    private int currentOutfit = OutfitMetalLeaf;
    private float flashReadyAt;
    private bool signatureStarted;
    private int signaturePatternCounter;
    private bool signatureInterruptPending;

    private int contactDamage;
    private bool contactHitDone;
    private bool forceTrail;
    private float shootPoseUntil;
    private float hitVfxTimer;

    private bool copyBotMode;
    private bool copyBotDecoyMode;

    private Vector3 firePointLeftLocal = new Vector3(-0.8f, -0.2f, 0f);

    private readonly List<BassLeafShield> liveLeaves = new List<BassLeafShield>(12);
    private readonly List<BassBubbleBullet> liveBubbles = new List<BassBubbleBullet>(6);
    private readonly List<BassMetalBlade> liveBlades = new List<BassMetalBlade>(8);
    private readonly List<BassQuickBoomerang> liveBoomerangs = new List<BassQuickBoomerang>(10);
    private readonly List<BassFlashTimeStar> liveStars = new List<BassFlashTimeStar>(40);

    private PlayerController frozenPlayer;
    private bool flashStopActive;
    private bool ownsWorldSlow;
    private bool ownsDoubleGearSlow;
    private readonly object FlashStopSlowOwner = new object();
    private bool doubleGearActive;
    private float doubleGearRemaining;
    private float doubleGearReadyAt;
    private int patternsSinceDoubleGear;
    private BassDoubleGearWorldTint doubleGearWorldTint;
    private GameObject doubleGearVisualContainer;
    private Transform blueGear;
    private Transform redGear;
    private Vector3 blueGearRest = Vector3.zero;
    private Vector3 redGearRest = Vector3.zero;
    private bool gearRestsCached;

    private Aura telegraphAura;
    private Aura enrageAura;

    /// <summary>
    /// During his own Double Gear / Flash stop he keeps full speed (like playable Bass).
    /// Other world slows still affect him via <see cref="BossDeltaTime"/> / <see cref="BossSpeed"/>.
    /// </summary>
    private bool KeepsFullSpeed => doubleGearActive || flashStopActive || ownsDoubleGearSlow;

    private float ActionDeltaTime => KeepsFullSpeed ? Time.deltaTime : BossDeltaTime;

    private float ActionSpeed(float speed) => KeepsFullSpeed ? speed : BossSpeed(speed);

    private readonly HashSet<int> animatorParams = new HashSet<int>();
    private GameObject[] afterimageObjects;
    private SpriteRenderer[] afterimageRenderers;
    private float afterimagesVisibleUntil;
    private readonly List<PoseSample> poseHistory = new List<PoseSample>(64);

    private struct PoseSample
    {
        public float time;
        public Vector3 position;
        public Sprite sprite;
        public bool flipX;
    }

    private sealed class Aura
    {
        public GameObject go;
        public SpriteRenderer sr;
        public Material material;
        public float phase;
    }

    public override int CopyBotMaxHealthOverride => copyBotMaxHealth;

    private float HealthRatio => currentHealth / (float)Mathf.Max(1, maxHealth);
    private bool IsEnraged =>
        EnrageLatched || HealthRatio <= DifficultyTuning.BossEnrageThreshold(enrageHealthRatio) + 0.0001f;
    private bool SignaturePhase => SignaturePhaseLatched || HealthRatio <= signatureHealthRatio + 0.0001f;

    protected override int ResolveLifeBarCount() => lifeBarCount;

    protected override void OnNearDeathPhasesSnapshot()
    {
        LatchCombatPhases(IsEnraged, signaturePhase: SignaturePhase, desperatePhase: false);
    }

    private int BarsLost
    {
        get
        {
            int bars = Mathf.Max(1, lifeBarCount);
            return Mathf.Clamp(Mathf.FloorToInt((1f - HealthRatio) * bars + 0.0001f), 0, bars);
        }
    }

    private float BarsLost01 => Mathf.Clamp01(BarsLost / (float)Mathf.Max(1, lifeBarCount - 1));

    /// <summary>Climbs with life bars lost and accelerates near death, capped so the fight stays fair.</summary>
    private float Tempo
    {
        get
        {
            float late = Mathf.Max(0f, lateFightTempoBonus) * BarsLost01 * BarsLost01;
            float t = 1f
                + BarsLost * Mathf.Max(0f, tempoPerBarLost)
                + late
                + (IsEnraged ? Mathf.Max(0f, enrageTempoBonus) : 0f)
                + (copyBotMode ? Mathf.Max(0f, copyBotTempoBonus) : 0f);
            return DifficultyTuning.ScaleBossTempo(Mathf.Min(Mathf.Max(1f, maxTempo), t));
        }
    }

    private float RecoverDuration
    {
        get
        {
            // Quadratic lean toward the low-HP recover so pattern switches tighten as he dies.
            float t = BarsLost01 * BarsLost01;
            return DifficultyTuning.ScaleBossRecover(
                Mathf.Max(0.05f, Mathf.Lerp(recoverDurationFullHp, recoverDurationLowHp, t)));
        }
    }

    /// <summary>Double Gear + Dual outfit: same stacking rules as playable Bass.</summary>
    private bool AbilityWithDoubleGear => doubleGearActive && currentOutfit != OutfitDefault;

    // ---------- Lifecycle ----------

    protected override void Awake()
    {
        SetBossId("BossBass");
        base.Awake();

        CacheAnimatorParams();
        if (firePoint == null)
            firePoint = transform.Find("FirePoint");
        if (firePoint != null)
            firePointLeftLocal = firePoint.localPosition;
        if (sparkSpawner == null)
            sparkSpawner = FindChildByName(transform, "Spark Spawner");

        ResolveDoubleGearVisuals();
        SetOutfitImmediate(OutfitMetalLeaf);

        telegraphAura = CreateAura("TelegraphAura");
        enrageAura = CreateAura("EnrageAura");
        SetupAfterimages();
    }

    private void OnDisable()
    {
        EndFlashStop();
        EndDoubleGear();
        DestroyLeaves();
    }

    private void OnDestroy()
    {
        EndFlashStop();
        EndDoubleGear();
        DestroyLeaves();
        DestroyAura(telegraphAura);
        DestroyAura(enrageAura);
        DestroyAfterimages();
    }

    private void LateUpdate()
    {
        SyncFirePoint();
    }

    public void SetCopyBotAfterimageStyle(float grayLevel)
    {
        grayLevel = Mathf.Clamp(grayLevel, 0.05f, 1f);
        afterimageColor = new Color(grayLevel, grayLevel, grayLevel, 1f);
    }

    /// <summary>Copy-bot boss fight: a little faster than the normal fight.</summary>
    public void EnableCopyBotMode()
    {
        copyBotMode = true;
        copyBotDecoyMode = false;
    }

    /// <summary>1-HP decoy: no attacks — he just stands and faces the player.</summary>
    public void EnableCopyBotDecoyMode()
    {
        copyBotDecoyMode = true;
        copyBotMode = false;
        ResetActions();
        isDashing = false;
        if (attackHitbox != null)
            attackHitbox.Deactivate();
    }

    private void ResolveDoubleGearVisuals()
    {
        if (doubleGearVisualContainer == null)
        {
            Transform found = FindChildByName(transform, "Double Gear Visual Container");
            if (found != null)
                doubleGearVisualContainer = found.gameObject;
        }

        if (doubleGearVisualContainer == null)
            return;

        Transform root = doubleGearVisualContainer.transform;
        if (blueGear == null)
            blueGear = FindChildByName(root, "Blue Gear");
        if (redGear == null)
            redGear = FindChildByName(root, "Red Gear");

        // Duration bar is playable-Bass UI — keep it off for the boss.
        Transform bar = FindChildByName(root, "Double Gear duration bar");
        if (bar != null)
            bar.gameObject.SetActive(false);

        if (!gearRestsCached)
        {
            if (blueGear != null)
                blueGearRest = blueGear.localPosition;
            if (redGear != null)
                redGearRest = redGear.localPosition;
            gearRestsCached = true;
        }

        doubleGearVisualContainer.SetActive(false);
    }

    private static Transform FindChildByName(Transform parent, string childName)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name.Trim() == childName)
                return child;

            Transform found = FindChildByName(child, childName);
            if (found != null)
                return found;
        }

        return null;
    }

    // ---------- Damage rules ----------

    /// <summary>Never flinches — hits only show sparks.</summary>
    protected override bool ShouldEnterHitStun(int healthBefore, int healthAfter, bool fromBigShot) => false;

    protected override void OnDamageApplied(int healthBefore, int healthAfter, bool enteredStun, bool fromBigShot)
    {
        VisualEffects.PlayStunned(stunnedEffectPrefab, this);
        hitVfxTimer = Mathf.Max(0.05f, hitStunDuration);

        // First drop into the signature phase: drop the current move so the combo starts right away.
        if (!signatureStarted && SignaturePhase &&
            IsPatternReady(Pattern.HeatDashRush) && IsPatternReady(Pattern.FlashTimeTrap, ignoreCooldown: true))
            signatureInterruptPending = true;
    }

    protected override void OnHitStunStarted()
    {
        ResetActions();
        base.OnHitStunStarted();
    }

    protected override float GetMoveSpeed()
    {
        // Raw authored speed — FixedUpdate applies ActionSpeed so DG doesn't double-scale.
        float speed = DifficultyTuning.ScaleBossMoveSpeed(authoredMoveSpeed)
                      + crystalMoveSpeedBonus + BarsLost * Mathf.Max(0f, moveSpeedPerBarLost);
        if (IsEnraged)
            speed += Mathf.Max(0f, enrageMoveSpeedBonus);
        if (doubleGearActive)
            speed += Mathf.Max(0f, doubleGearMoveSpeedBonus);
        return speed;
    }

    private int Damage(int baseDamage)
    {
        int dmg = Mathf.Max(0, baseDamage) + (IsEnraged ? Mathf.Max(0, enrageBonusDamage) : 0);
        if (AbilityWithDoubleGear)
            dmg *= 2;
        dmg = Mathf.FloorToInt(dmg * Mathf.Max(0.1f, damageMultiplier) + 0.5f);
        return ApplyCrystalAttackBonus(dmg);
    }

    // ---------- Update loop ----------

    protected override void HandleBossUpdate()
    {
        PlayerController player = FindPlayer();
        if (player == null || player.IsDead)
        {
            if (actionStack.Count > 0 || flashStopActive || liveLeaves.Count > 0)
                ResetActions();
            motion = Motion.Stop;
            return;
        }

        if (copyBotDecoyMode)
        {
            motion = Motion.Stop;
            FacePlayer();
            return;
        }

        if (signatureInterruptPending)
        {
            signatureInterruptPending = false;
            if (!signatureStarted)
                ResetActions();
        }

        TickDoubleGear(Time.deltaTime);

        if (actionStack.Count == 0)
            actionStack.Push(RunNextPattern());

        TickActions();
        TickContactDamage();
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
                {
                    // MoveHorizontal uses BossSpeed; during own DG keep full walk speed.
                    float speed = ActionSpeed(GetMoveSpeed());
                    float dir = Mathf.Sign(walkDir);
                    SetFacing(dir);
                    rb.linearVelocity = new Vector2(dir * speed, rb.linearVelocity.y);
                }
                else
                    StopHorizontal();
                break;
            case Motion.Run:
                rb.linearVelocity = new Vector2(ActionSpeed(motionVelocity.x), rb.linearVelocity.y);
                break;
            case Motion.Raw:
                // Not scaled by the world slow: he keeps moving while time is stopped.
                rb.linearVelocity = new Vector2(motionVelocity.x, rb.linearVelocity.y);
                break;
            case Motion.Velocity:
                rb.linearVelocity = new Vector2(ActionSpeed(motionVelocity.x), ActionSpeed(motionVelocity.y));
                break;
            case Motion.Hang:
                rb.linearVelocity = Vector2.zero;
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
        UpdateAfterimages();
        TickEnrageAura();
    }

    protected override void UpdateAnimator()
    {
        if (animator == null)
            return;

        // Flash / DG world slow is for everyone else — he keeps full animation speed.
        animator.speed = 1f;

        float vy = rb != null ? rb.linearVelocity.y : 0f;
        bool moving = (motion == Motion.Walk && Mathf.Abs(walkDir) > 0.01f
                       || motion == Motion.Run || motion == Motion.Raw && Mathf.Abs(motionVelocity.x) > 0.01f)
                      && !isDashing;
        SetAnimBool("IsMoving", moving);
        SetAnimBool("IsGrounded", isGrounded);
        SetAnimBool("IsInAir", !isGrounded);
        SetAnimBool("IsDashing", isDashing);
        SetAnimBool("IsShooting", Time.time < shootPoseUntil);
        SetAnimBool("AimUp", false);
        SetAnimBool("AimDown", false);
        SetAnimBool("IsStunned", false);
        SetAnimBool("IsFalling", !isGrounded && vy < -0.01f);
        SetAnimBool("IsJumping", !isGrounded && vy >= -0.01f);
        SetAnimFloat("VerticalSpeed", vy);
        if (HasParam("AbilityMode"))
            animator.SetInteger("AbilityMode", currentOutfit);
    }

    // ---------- Pattern selection ----------

    private IEnumerator RunNextPattern()
    {
        PlayerController player = FindPlayer();
        if (player == null)
            yield break;

        if (!isGrounded)
            yield return WaitUntilGrounded(2f);

        ConsiderDoubleGearForNextPattern();

        if (TryStartSignature())
        {
            EnsureDoubleGearForAbility();
            yield return RunPattern(Pattern.HeatDashRush);
            CleanupAfterPattern();
            if (!isGrounded)
                yield return WaitUntilGrounded(2f);
            yield return RunPattern(Pattern.FlashTimeTrap);

            yield return Recover();
            yield break;
        }

        Pattern pattern = PickPattern(player);
        if (pattern != Pattern.DoubleGearBusterAssault && IsEnraged)
            EnsureDoubleGearForAbility();

        yield return RunPattern(pattern);

        if (enragedChains && IsEnraged && TryGetFollowUp(pattern, out Pattern followUp) && IsPatternReady(followUp))
        {
            CleanupAfterPattern();
            if (!isGrounded)
                yield return WaitUntilGrounded(2f);
            EnsureDoubleGearForAbility();
            yield return RunPattern(followUp);
        }

        yield return Recover();
    }

    /// <summary>
    /// Enraged: the Heat Dash → Flash Time Trap combo right away, then every <see cref="signatureEvery"/> patterns.
    /// </summary>
    private bool TryStartSignature()
    {
        if (!SignaturePhase)
            return false;
        if (!IsPatternReady(Pattern.FlashTimeTrap, ignoreCooldown: true) || !IsPatternReady(Pattern.HeatDashRush))
            return false;

        int every = Mathf.Max(1, signatureEvery);
        if (!signatureStarted)
        {
            signatureStarted = true;
            signaturePatternCounter = every;
        }

        if (signaturePatternCounter < every)
        {
            signaturePatternCounter++;
            return false;
        }

        signaturePatternCounter = 1;
        return true;
    }

    private Pattern PickPattern(PlayerController player)
    {
        Zone zone = ResolveZone(player);
        Pattern[] rotation = zone switch
        {
            Zone.Close => CloseRotation,
            Zone.Mid => MidRotation,
            Zone.Far => FarRotation,
            _ => AntiAirRotation
        };

        // Moves that aren't ready (Flash on cooldown, missing prefab) are skipped; the rotation order stays the same.
        int z = (int)zone;
        for (int i = 0; i < rotation.Length; i++)
        {
            Pattern pattern = rotation[zoneRotationIndex[z] % rotation.Length];
            zoneRotationIndex[z] = (zoneRotationIndex[z] + 1) % rotation.Length;
            if (IsPatternReady(pattern))
                return pattern;
        }

        return Pattern.HeatDashRush;
    }

    private bool IsPatternReady(Pattern pattern, bool ignoreCooldown = false)
    {
        switch (pattern)
        {
            case Pattern.MetalBladeHop:
            case Pattern.MetalCrossLeap:
                return metalBladePrefab != null;
            case Pattern.MetalShieldPress:
                return leafShieldPrefab != null;
            case Pattern.CrashBombPace:
                return crashBombPrefab != null || crashBombPlusPrefab != null;
            case Pattern.BubbleLobBarrage:
                return bubbleBulletPrefab != null;
            case Pattern.QuickBoomerangVolley:
                return quickBoomerangPrefab != null;
            case Pattern.FlashTimeTrap:
                return quickBoomerangPrefab != null && (ignoreCooldown || Time.time >= flashReadyAt);
            case Pattern.DoubleGearBusterAssault:
                return doubleGearShotPrefab != null || greenShotPrefab != null || purpleShotPrefab != null;
            default:
                return true;
        }
    }

    private Zone ResolveZone(PlayerController player)
    {
        float dx = Mathf.Abs(player.transform.position.x - transform.position.x);
        if (dx <= antiAirMaxDistance && PlayerBounds(player).min.y - MyBounds().min.y >= antiAirHeight)
            return Zone.AntiAir;
        if (dx <= closeZoneDistance)
            return Zone.Close;
        return dx <= midZoneDistance ? Zone.Mid : Zone.Far;
    }

    private static bool TryGetFollowUp(Pattern pattern, out Pattern followUp)
    {
        switch (pattern)
        {
            case Pattern.MetalBladeHop:
                followUp = Pattern.MetalCrossLeap;
                return true;
            case Pattern.BubbleLobBarrage:
                followUp = Pattern.QuickBoomerangVolley;
                return true;
            case Pattern.QuickBoomerangVolley:
                followUp = Pattern.HeatDashRush;
                return true;
            default:
                followUp = pattern;
                return false;
        }
    }

    private IEnumerator RunPattern(Pattern pattern)
    {
        switch (pattern)
        {
            case Pattern.MetalCrossLeap: return MetalCrossLeapPattern();
            case Pattern.MetalShieldPress: return MetalShieldPressPattern();
            case Pattern.CrashBombPace: return CrashBombPacePattern();
            case Pattern.HeatDashRush: return HeatDashRushPattern();
            case Pattern.BubbleLobBarrage: return BubbleLobBarragePattern();
            case Pattern.QuickBoomerangVolley: return QuickBoomerangVolleyPattern();
            case Pattern.FlashTimeTrap: return FlashTimeTrapPattern();
            case Pattern.DoubleGearBusterAssault: return DoubleGearBusterAssaultPattern();
            default: return MetalBladeHopPattern();
        }
    }

    private IEnumerator Recover()
    {
        CleanupAfterPattern();
        if (!isGrounded)
            yield return WaitUntilGrounded(2f);

        // Short spacing drift — never a long idle stand.
        float duration = RecoverDuration;
        float t = 0f;
        float preferred = Mathf.Lerp(closeZoneDistance, midZoneDistance, 0.45f);
        while (t < duration)
        {
            FacePlayer();
            float dx = DxToPlayer();
            float dist = Mathf.Abs(dx);
            if (dist > preferred + 0.75f)
            {
                motion = Motion.Walk;
                walkDir = Mathf.Sign(dx);
            }
            else if (dist < preferred - 1.1f)
            {
                motion = Motion.Walk;
                walkDir = -Mathf.Sign(dx);
            }
            else
            {
                motion = Motion.Stop;
                walkDir = 0f;
            }

            t += ActionDeltaTime * Tempo;
            yield return null;
        }

        motion = Motion.Stop;
        walkDir = 0f;
    }

    // ---------- Outfits ----------

    private void SetOutfitImmediate(int mode)
    {
        currentOutfit = Mathf.Clamp(mode, OutfitDefault, OutfitQuickTime);
        if (animator != null && HasParam("AbilityMode"))
            animator.SetInteger("AbilityMode", currentOutfit);
    }

    private static Color OutfitColor(int mode)
    {
        switch (mode)
        {
            case OutfitDefault: return new Color(0.7f, 0.45f, 1f, 1f);
            case OutfitMetalLeaf: return new Color(0.75f, 1f, 0.25f, 1f);
            case OutfitCrashingHeat: return new Color(1f, 0.5f, 0.1f, 1f);
            case OutfitBubbleAir: return new Color(0.4f, 0.75f, 1f, 1f);
            default: return new Color(1f, 0.7f, 0.9f, 1f);
        }
    }

    /// <summary>Swaps the Dual outfit with a short colour glint (so each weapon change is readable).</summary>
    private IEnumerator SwitchOutfit(int mode)
    {
        if (currentOutfit == mode)
            yield break;

        motion = Motion.Stop;
        walkDir = 0f;
        SetOutfitImmediate(mode);

        float duration = Mathf.Max(0f, outfitSwapGlintSeconds);
        float t = 0f;
        while (t < duration)
        {
            t += ActionDeltaTime * Tempo;
            FacePlayer();
            ShowAura(telegraphAura, OutfitColor(mode), glintColor, 0.5f + 0.4f * Mathf.PingPong(t * 10f, 1f), auraBaseScale * 1.08f);
            yield return null;
        }

        HideAura(telegraphAura);
    }

    // ---------- Double Gear ----------

    private void ConsiderDoubleGearForNextPattern()
    {
        patternsSinceDoubleGear++;
        if (doubleGearActive)
            return;

        if (IsEnraged)
        {
            EnsureDoubleGearForAbility();
            return;
        }

        int every = Mathf.Max(1, doubleGearEveryPatterns);
        if (patternsSinceDoubleGear < every || Time.time < doubleGearReadyAt)
            return;

        BeginDoubleGear(Mathf.Max(0.5f, doubleGearDuration));
        patternsSinceDoubleGear = 0;
    }

    private void EnsureDoubleGearForAbility()
    {
        float duration = IsEnraged
            ? Mathf.Max(0.5f, doubleGearEnragedDuration)
            : Mathf.Max(0.5f, doubleGearDuration);
        if (doubleGearActive)
        {
            // Keep the window topped up while Dual + enrage so gear stays on through the combo.
            doubleGearRemaining = Mathf.Max(doubleGearRemaining, duration * 0.55f);
            return;
        }

        if (!IsEnraged && Time.time < doubleGearReadyAt)
            return;

        BeginDoubleGear(duration);
        patternsSinceDoubleGear = 0;
    }

    private void BeginDoubleGear(float duration)
    {
        doubleGearActive = true;
        doubleGearRemaining = Mathf.Max(0.5f, duration);
        forceTrail = true;
        ShowDoubleGearVisuals(true);

        // Owner-keyed stack: player Bass DG can run at the same time without one End wiping both.
        // Safe to push even during Flash Stop — freeze layer keeps the effective scale at 0.
        HyperSpeedWorldSlow.Begin(this, Mathf.Clamp(doubleGearWorldTimeScale, 0.2f, 1f), slowPlayer: false);
        ownsDoubleGearSlow = true;

        if (doubleGearWorldTint == null)
            doubleGearWorldTint = GetComponent<BassDoubleGearWorldTint>()
                ?? gameObject.AddComponent<BassDoubleGearWorldTint>();
        doubleGearWorldTint.Apply(transform, doubleGearWorldBlackTint);
    }

    private void TickDoubleGear(float dt)
    {
        if (!doubleGearActive)
        {
            // Enraged: renew DG as soon as the short gap ends so Dual patterns keep the stack.
            if (IsEnraged && Time.time >= doubleGearReadyAt && !flashStopActive)
                BeginDoubleGear(Mathf.Max(0.5f, doubleGearEnragedDuration));
            return;
        }

        TickDoubleGearVisualSpin(dt);
        doubleGearRemaining -= Mathf.Max(0f, dt);
        if (doubleGearRemaining > 0f)
            return;

        EndDoubleGear();
    }

    private void EndDoubleGear()
    {
        if (!doubleGearActive && !ownsDoubleGearSlow)
            return;

        doubleGearActive = false;
        doubleGearRemaining = 0f;
        if (!isDashing)
            forceTrail = false;
        ShowDoubleGearVisuals(false);
        doubleGearWorldTint?.Release();

        float gap = IsEnraged ? 0.35f : Mathf.Max(0.5f, doubleGearCooldown);
        doubleGearReadyAt = Time.time + gap;

        if (ownsDoubleGearSlow)
        {
            HyperSpeedWorldSlow.End(this);
            ownsDoubleGearSlow = false;
        }
    }

    private void ShowDoubleGearVisuals(bool on)
    {
        if (doubleGearVisualContainer == null)
            ResolveDoubleGearVisuals();
        if (doubleGearVisualContainer == null)
            return;

        doubleGearVisualContainer.SetActive(on);
        if (on && gearRestsCached)
        {
            if (blueGear != null)
                blueGear.localPosition = blueGearRest;
            if (redGear != null)
                redGear.localPosition = redGearRest;
        }
    }

    private void TickDoubleGearVisualSpin(float dt)
    {
        if (!doubleGearActive || doubleGearVisualContainer == null || !doubleGearVisualContainer.activeSelf)
            return;

        float spin = Mathf.Lerp(720f, 180f, Mathf.Clamp01(doubleGearRemaining / Mathf.Max(0.5f, doubleGearDuration)));
        if (blueGear != null)
            blueGear.Rotate(0f, 0f, spin * dt, Space.Self);
        if (redGear != null)
            redGear.Rotate(0f, 0f, -spin * dt, Space.Self);
    }

    private void ApplyBossAbilityBoosts(BassMetalBlade blade)
    {
        if (blade != null && AbilityWithDoubleGear)
            blade.ConfigureBossAbilityBoosts(doubleGearAbilityScale);
    }

    private void ApplyBossAbilityBoosts(BassQuickBoomerang boomerang)
    {
        if (boomerang != null && AbilityWithDoubleGear)
            boomerang.ConfigureBossAbilityBoosts(doubleGearAbilityScale);
    }

    private void ApplyBossAbilityBoosts(BassCrashBomb bomb)
    {
        if (bomb != null && AbilityWithDoubleGear)
            bomb.ConfigureBossAbilityBoosts(doubleGearAbilityScale, 2);
    }

    private void ApplyBossAbilityBoosts(BassBubbleBullet bubble)
    {
        if (bubble != null && AbilityWithDoubleGear)
            bubble.ConfigureBossAbilityBoosts(doubleGearAbilityScale, 2);
    }

    // ---------- Metal Leaf ----------

    /// <summary>Hops alternate low / high; the apex volley throws 2, 1, then 3 blades (3, 2, 3 when enraged).</summary>
    private IEnumerator MetalBladeHopPattern()
    {
        yield return SwitchOutfit(OutfitMetalLeaf);

        int hops = Mathf.Max(1, metalHopsPerPattern);
        for (int h = 0; h < hops; h++)
        {
            FacePlayer();
            bool high = h % 2 == 1 || PlayerFeetAboveMine() >= antiAirHeight;
            float vy = high ? metalHighHopForce : metalLowHopForce;
            float dx = DxToPlayer();
            float drift = Mathf.Clamp((Mathf.Abs(dx) - metalPreferredDistance) * Mathf.Sign(dx), -3f, 3f);
            LaunchJump(drift / EstimateAirTime(vy), vy);
            yield return WaitUntilAirborne();
            yield return WaitUntilFalling(1.5f);

            yield return ApexThrow(MetalVolleySize(h), metalApexGlintSeconds);
            yield return WaitUntilGrounded(2f);
            motion = Motion.Stop;
            yield return Wait(0.08f);
        }
    }

    private int MetalVolleySize(int hop)
    {
        int[] volleys = IsEnraged ? new[] { 3, 2, 3 } : new[] { 2, 1, 3 };
        for (int v = 0; v < volleys.Length; v++)
            volleys[v] = DifficultyTuning.ScaleSignatureCount(volleys[v]);
        return volleys[hop % volleys.Length];
    }

    /// <summary>Stalls at the apex, glints, then throws blades at the player at least Metal Blade Spacing apart.</summary>
    private IEnumerator ApexThrow(int shots, float glintSeconds)
    {
        motion = Motion.Hang;
        SetGravity(false);
        FacePlayer();

        float t = 0f;
        while (t < glintSeconds)
        {
            t += ActionDeltaTime * Tempo;
            FacePlayer();
            ShowAura(telegraphAura, OutfitColor(OutfitMetalLeaf), glintColor, 0.5f + 0.4f * Mathf.PingPong(t * 10f, 1f), auraBaseScale);
            yield return null;
        }

        HideAura(telegraphAura);
        float gap = Mathf.Max(0.05f, metalBladeSpacing) / Mathf.Max(1f, metalBladeSpeed);
        for (int s = 0; s < shots; s++)
        {
            FacePlayer();
            float offset = IsEnraged && shots > 1 ? (s % 2 == 0 ? -1f : 1f) * Mathf.Abs(metalBladeSpreadDegrees) * 0.5f : 0f;
            FireBlade(offset);
            if (s < shots - 1)
                yield return WaitRaw(gap);
        }

        SetGravity(true);
        motion = Motion.Free;
    }

    /// <summary>Metal Man crouch glint, then a leap clean over the player; a blade flies back at the apex.</summary>
    private IEnumerator MetalCrossLeapPattern()
    {
        yield return SwitchOutfit(OutfitMetalLeaf);

        motion = Motion.Stop;
        yield return Glint(crossLeapGlintSeconds, OutfitColor(OutfitMetalLeaf));

        FacePlayer();
        float dx = DxToPlayer();
        float dir = Mathf.Abs(dx) > 0.05f ? Mathf.Sign(dx) : facingSign;
        float vy = Mathf.Max(1f, crossLeapForce);
        float vx = Mathf.Clamp((dx + dir * crossLeapOvershoot) / EstimateAirTime(vy), -crossLeapMaxSpeed, crossLeapMaxSpeed);
        LaunchJump(vx, vy);
        yield return WaitUntilAirborne();
        yield return WaitUntilFalling(1.6f);

        // Apex, passing over the player: the blade(s) go back at them while he keeps his momentum.
        int blades = DifficultyTuning.ScaleSignatureCount(
            Mathf.Max(1, crossLeapBlades) + (IsEnraged ? 1 : 0));
        for (int i = 0; i < blades; i++)
        {
            FacePlayer();
            FireBlade(0f);
            if (i < blades - 1)
                yield return WaitRaw(0.18f);
        }

        yield return WaitUntilGrounded(2f);
        motion = Motion.Stop;
        yield return Wait(crossLeapRecoverSeconds);
    }

    /// <summary>
    /// Block Man final feel: the Leaf Shield ring forms (glint, he stands still), then he hops closer behind it and
    /// finishes with blades. The ring fades after a short linger so there is always a gap.
    /// </summary>
    private IEnumerator MetalShieldPressPattern()
    {
        yield return SwitchOutfit(OutfitMetalLeaf);

        motion = Motion.Stop;
        walkDir = 0f;
        StopHorizontal();

        // Form the ring first so a short glint can't be interrupted into a leaf-less press.
        SummonLeaves();
        if (liveLeaves.Count == 0)
        {
            Debug.LogWarning("[BossBass] Leaf Shield prefab missing or failed to spawn — skipping shield press.");
            yield break;
        }

        yield return Glint(shieldSummonSeconds, OutfitColor(OutfitMetalLeaf));

        int hops = Mathf.Max(1, shieldPressHops);
        for (int h = 0; h < hops; h++)
        {
            // Keep the ring alive across hops (cleanup must not wipe it mid-pattern).
            PruneLeaves();
            if (liveLeaves.Count == 0)
                SummonLeaves();

            FacePlayer();
            float vy = shieldPressHopForce + (h == hops - 1 ? 2.5f : 0f);
            float dx = DxToPlayer();
            float gapToStop = Mathf.Abs(dx) - Mathf.Max(0.5f, shieldPressStopDistance);
            float vx = Mathf.Clamp(gapToStop * Mathf.Sign(dx) / EstimateAirTime(vy), -shieldPressMaxHopSpeed, shieldPressMaxHopSpeed);
            LaunchJump(vx, vy);
            yield return WaitUntilAirborne();

            if (h == hops - 1 && metalBladePrefab != null)
            {
                yield return WaitUntilFalling(1.5f);
                yield return ApexThrow(Mathf.Max(1, shieldFinalVolley), metalApexGlintSeconds);
            }

            yield return WaitUntilGrounded(2f);
            motion = Motion.Stop;
            yield return Wait(0.1f);
        }

        yield return Wait(shieldLingerSeconds);
        DestroyLeaves();
    }

    private void SummonLeaves()
    {
        // Always rebuild the ring so a prior cleanup / interrupt can't leave a stuck "already up" state.
        DestroyLeaves();
        if (leafShieldPrefab == null)
        {
            Debug.LogWarning("[BossBass] leafShieldPrefab is not assigned on the BossBass prefab.");
            return;
        }

        int count = Mathf.Max(1, leafCount);
        float step = 360f / count;
        for (int i = 0; i < count; i++)
        {
            BassLeafShield leaf = Instantiate(leafShieldPrefab, transform.position, Quaternion.identity);
            if (leaf == null)
                continue;
            leaf.gameObject.SetActive(true);
            leaf.BeginOrbit(
                transform,
                i * step,
                leafOrbitRadius,
                leafOrbitDegreesPerSecond,
                Mathf.Max(1, leafHealth),
                Mathf.Max(0, Damage(leafContactDamage)));
            if (AbilityWithDoubleGear)
                leaf.transform.localScale *= Mathf.Max(1f, doubleGearAbilityScale);
            liveLeaves.Add(leaf);
        }

        if (liveLeaves.Count > 0)
            SoundManager.Instance?.PlayBassFireRapid();
    }

    private void DestroyLeaves()
    {
        for (int i = 0; i < liveLeaves.Count; i++)
        {
            if (liveLeaves[i] != null)
                liveLeaves[i].DestroyLeaf();
        }

        liveLeaves.Clear();
    }

    private void PruneLeaves()
    {
        liveLeaves.RemoveAll(l => l == null || !l.IsAlive);
    }

    private BassMetalBlade FireBlade(float angleOffset)
    {
        liveBlades.RemoveAll(b => b == null);
        if (metalBladePrefab == null || liveBlades.Count >= Mathf.Max(1, metalBladeMaxLive))
            return null;

        Vector2 origin = OriginPoint();
        Vector2 dir = AimDirToPlayer(origin, angleOffset);
        BassMetalBlade blade = Instantiate(metalBladePrefab, origin, Quaternion.identity);
        blade.gameObject.SetActive(true);
        ApplyBossAbilityBoosts(blade);
        blade.Launch(dir, metalBladeSpeed, Damage(metalBladeDamage), transform);
        liveBlades.Add(blade);
        SoundManager.Instance?.PlayBassFireRapid();
        PlayShootAnim();
        return blade;
    }

    // ---------- Crashing Heat ----------

    /// <summary>Paces to his range, hops, and lobs a Crash Bomb at the apex (two when enraged).</summary>
    private IEnumerator CrashBombPacePattern()
    {
        yield return SwitchOutfit(OutfitCrashingHeat);

        int bombs = DifficultyTuning.ScaleSignatureCount(IsEnraged ? 2 : 1);
        for (int i = 0; i < bombs; i++)
        {
            yield return Pace(crashPaceSeconds, i);

            // Optional Buster pokes (off by default).
            for (int b = 0; b < crashPaceBusterShots; b++)
            {
                FireBuster();
                yield return WaitRaw(0.15f);
            }

            FacePlayer();
            float dx = DxToPlayer();
            float vy = Mathf.Max(1f, crashHopForce);
            float drift = Mathf.Clamp((Mathf.Abs(dx) - crashPreferredDistance) * Mathf.Sign(dx), -2.5f, 2.5f);
            LaunchJump(drift / EstimateAirTime(vy), vy);
            yield return WaitUntilAirborne();
            yield return WaitUntilFalling(1.5f);

            motion = Motion.Hang;
            SetGravity(false);
            float t = 0f;
            while (t < crashGlintSeconds)
            {
                t += ActionDeltaTime * Tempo;
                FacePlayer();
                ShowAura(telegraphAura, OutfitColor(OutfitCrashingHeat), glintColor, 0.5f + 0.4f * Mathf.PingPong(t * 10f, 1f), auraBaseScale);
                yield return null;
            }

            HideAura(telegraphAura);
            FireCrashBomb();
            yield return WaitRaw(0.1f);
            SetGravity(true);
            motion = Motion.Free;
            yield return WaitUntilGrounded(2f);
            motion = Motion.Stop;
            yield return Wait(0.3f);
        }
    }

    /// <summary>Walks toward / away from the player to hold his preferred range.</summary>
    private IEnumerator Pace(float seconds, int index)
    {
        float t = 0f;
        while (t < seconds)
        {
            t += ActionDeltaTime * Tempo;
            FacePlayer();
            float dx = DxToPlayer();
            float dist = Mathf.Abs(dx);
            float dir;
            if (dist < crashPreferredDistance - 1f)
                dir = -Mathf.Sign(dx);
            else if (dist > crashPreferredDistance + 1f)
                dir = Mathf.Sign(dx);
            else
                dir = index % 2 == 0 ? -Mathf.Sign(dx) : Mathf.Sign(dx);

            motion = Motion.Walk;
            walkDir = dir;
            yield return null;
        }

        motion = Motion.Stop;
        walkDir = 0f;
        yield return null;
    }

    private void FireBuster()
    {
        FireBusterShot(0f, crashPaceBusterSpeed, crashPaceBusterDamage, preferPurple: true);
    }

    private void FireBusterShot(float angleOffset, float speed = -1f, int damage = -1, bool preferPurple = false)
    {
        Projectile prefab = ResolveBusterPrefab(preferPurple);
        if (prefab == null)
            return;

        Vector2 origin = OriginPoint();
        Vector2 dir = AimDirToPlayer(origin, angleOffset);
        float shotSpeed = speed > 0f ? speed : Mathf.Max(1f, doubleGearShotSpeed);
        int shotDamage = damage >= 0 ? damage : doubleGearShotDamage;
        // Default-form DG shots already use the DG prefab; don't double-count AbilityWithDoubleGear.
        int applied = currentOutfit == OutfitDefault
            ? ApplyCrystalAttackBonus(Mathf.Max(0, shotDamage) + (IsEnraged ? Mathf.Max(0, enrageBonusDamage) : 0))
            : Damage(shotDamage);

        Projectile shot = Instantiate(prefab, origin, Quaternion.identity);
        shot.Launch(dir, shotSpeed, applied, transform);
        SoundManager.Instance?.PlayBassFireRapid();
        PlayShootAnim();
    }

    private Projectile ResolveBusterPrefab(bool preferPurple)
    {
        if (preferPurple && purpleShotPrefab != null)
            return purpleShotPrefab;
        if (doubleGearActive && doubleGearShotPrefab != null)
            return doubleGearShotPrefab;
        if (greenShotPrefab != null)
            return greenShotPrefab;
        return purpleShotPrefab;
    }

    private void FireCrashBomb()
    {
        bool plus = crashBombPlusHealthRatio > 0f && HealthRatio <= crashBombPlusHealthRatio + 0.0001f;
        BassCrashBomb prefab = plus && crashBombPlusPrefab != null ? crashBombPlusPrefab : crashBombPrefab;
        if (prefab == null)
            prefab = crashBombPlusPrefab;
        if (prefab == null)
            return;
        plus = prefab == crashBombPlusPrefab;

        Vector2 origin = OriginPoint();
        Vector2 dir = AimDirToPlayer(origin, 0f);
        BassCrashBomb bomb = Instantiate(prefab, origin, Quaternion.identity);
        ApplyBossAbilityBoosts(bomb);
        bomb.Launch(
            dir,
            crashBombSpeed,
            plus,
            Mathf.Max(0.01f, heatSpaceSize),
            transform,
            AlwaysSpend,
            crashBombExplosionPrefab,
            smallHeatFlarePrefab,
            heatFlareStackPrefab,
            heatFlareBallPrefab,
            null);
        float dmgScale = Mathf.Max(0.1f, damageMultiplier * crashBombDamageScale);
        if (AbilityWithDoubleGear)
            dmgScale *= 2f;
        bomb.SetDamageMultiplier(dmgScale);
        bomb.SetHostileHeatBallScale(heatBallDamageScale);
        SoundManager.Instance?.PlayBassFireRapid();
        PlayShootAnim();
    }

    private static bool AlwaysSpend(int cost) => true;

    /// <summary>Heat Man comet dash: telegraph, a fast straight dash through the player with contact damage.</summary>
    private IEnumerator HeatDashRushPattern()
    {
        yield return SwitchOutfit(OutfitCrashingHeat);
        yield return DashTelegraph(heatDashWindupSeconds, OutfitColor(OutfitCrashingHeat));

        int dashes = IsEnraged ? 2 : 1;
        for (int i = 0; i < dashes; i++)
        {
            float dx = DxToPlayer();
            float dir = Mathf.Abs(dx) > 0.05f ? Mathf.Sign(dx) : facingSign;
            float distance = Mathf.Clamp(Mathf.Abs(dx) + heatDashOvershoot, heatDashMinDistance, heatDashMaxDistance);
            // Heat Dash flare VFX always; ground stacks only when trail is enabled (enraged by default).
            bool trail = heatDashFlareTrail && (!heatDashTrailOnlyEnraged || IsEnraged);
            yield return Rush(dir, distance, heatDashSpeed, Damage(heatDashContactDamage), true, trail);

            if (i < dashes - 1)
                yield return DashTelegraph(heatDashTurnPause, OutfitColor(OutfitCrashingHeat));
        }

        yield return Wait(0.15f);
    }

    private IEnumerator DashTelegraph(float seconds, Color color)
    {
        motion = Motion.Stop;
        SoundManager.Instance?.PlayClockworkTic();
        float t = 0f;
        while (t < seconds)
        {
            t += ActionDeltaTime * Tempo;
            FacePlayer();
            ShowAura(telegraphAura, color, Color.white, 0.45f + 0.45f * Mathf.PingPong(t * 8f, 1f), auraBaseScale * 1.1f);
            yield return null;
        }

        HideAura(telegraphAura);
    }

    /// <summary>Straight dash along the ground with contact damage, optional heat flare VFX and flare trail.</summary>
    private IEnumerator Rush(float dir, float distance, float speed, int damage, bool heat, bool trail)
    {
        if (rb == null)
            yield break;

        SetFacing(dir);
        isDashing = true;
        forceTrail = true;
        contactDamage = damage;
        contactHitDone = false;
        SetGravity(false);
        motion = Motion.Velocity;
        motionVelocity = new Vector2(dir * Mathf.Max(1f, speed), 0f);
        if (HasParam("Dash"))
            animator.SetTrigger("Dash");
        SoundManager.Instance?.PlayDash();

        float runTime = distance / Mathf.Max(1f, speed);
        if (heat)
            SpawnHeatDashFlare(Mathf.Max(0.28f, runTime));

        float startX = rb.position.x;
        float lastX = startX;
        float stuck = 0f;
        float t = 0f;
        float timeout = runTime + 0.45f;
        // During DG he keeps full speed; allow tiny FixedUpdate steps without false "stuck".
        float stuckThreshold = KeepsFullSpeed ? 0.00005f : 0.001f;
        float stuckLimit = KeepsFullSpeed ? 0.22f : 0.12f;
        int trailSpawned = 0;
        int trailCount = Mathf.Max(1, heatDashTrailCount);
        float space = Mathf.Max(0.01f, heatSpaceSize);
        float trailSpacing = Mathf.Clamp(
            distance / (trailCount * space),
            Mathf.Max(0.1f, heatDashTrailMinSpaces),
            Mathf.Max(heatDashTrailMinSpaces, heatDashTrailMaxSpaces)) * space;
        while (Mathf.Abs(rb.position.x - startX) < distance && t < timeout)
        {
            t += ActionDeltaTime;
            float traveled = Mathf.Abs(rb.position.x - startX);
            while (trail && trailSpawned < trailCount && traveled + 0.001f >= trailSpawned * trailSpacing)
            {
                SpawnHeatTrailFlare(startX + dir * trailSpacing * trailSpawned, dir);
                trailSpawned++;
            }

            if (Mathf.Abs(rb.position.x - lastX) < stuckThreshold)
            {
                stuck += ActionDeltaTime;
                if (stuck > stuckLimit)
                    break;
            }
            else
            {
                stuck = 0f;
            }

            lastX = rb.position.x;
            yield return null;
        }

        EndRush();
    }

    private void EndRush()
    {
        isDashing = false;
        // Keep DG afterimages alive if gear is still running.
        if (!doubleGearActive)
            forceTrail = false;
        contactDamage = 0;
        SetGravity(true);
        motion = Motion.Stop;
        if (rb != null)
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
    }

    private void TickContactDamage()
    {
        if (contactDamage <= 0 || contactHitDone)
            return;

        PlayerController player = FindPlayer();
        if (player == null || player.IsDead)
            return;

        if (MyBounds().Intersects(PlayerBounds(player)))
        {
            contactHitDone = true;
            player.TakeDamage(contactDamage, transform);
        }
    }

    private void SpawnHeatDashFlare(float duration)
    {
        if (heatDashFlarePrefab == null)
            return;

        Transform anchor = sparkSpawner != null ? sparkSpawner : transform;
        float backSign = facingSign >= 0f ? -1f : 1f;
        Vector3 spawnPos = anchor.position + new Vector3(backSign * Mathf.Abs(heatDashFlareOffsetX), 0f, 0f);
        BassHeatDashFlare flare = Instantiate(heatDashFlarePrefab, spawnPos, Quaternion.identity);
        flare.Begin(anchor, Mathf.Max(0.1f, duration), spriteRenderer, EffectSortingGroup, backSign * Mathf.Abs(heatDashFlareOffsetX));
    }

    /// <summary>Small Heat Flare dropped along the dash: it falls, lands, and leaves a Heat Flare Stack.</summary>
    private void SpawnHeatTrailFlare(float worldX, float dirSign)
    {
        if (smallHeatFlarePrefab == null || heatFlareStackPrefab == null)
            return;

        float spawnY = MyBounds().center.y;
        Vector3 spawn = new Vector3(worldX, spawnY, 0f);
        Vector2 vel = new Vector2(dirSign * 1.5f, 2.5f);

        BassSmallHeatFlare flare = Instantiate(smallHeatFlarePrefab, spawn, Quaternion.identity);
        flare.ConfigureHostSorting(EffectSortingGroup, spriteRenderer);
        flare.SetHostile(true);
        flare.SetStackDamageMultiplier(Mathf.Max(0.1f, damageMultiplier * heatDashTrailDamageScale));
        flare.LaunchStackSpawner(
            vel,
            spawn + Vector3.down,
            heatFlareStackPrefab,
            smallHeatFlarePrefab,
            Mathf.Max(0.01f, heatSpaceSize),
            AlwaysSpend,
            0,
            transform);
    }

    // ---------- Bubble Air ----------

    /// <summary>Blast Man style: hops away from the player and lobs spaced bubbles from each apex.</summary>
    private IEnumerator BubbleLobBarragePattern()
    {
        yield return SwitchOutfit(OutfitBubbleAir);

        int lobs = DifficultyTuning.ScaleSignatureCount(
            Mathf.Max(1, bubbleLobCount) + (IsEnraged ? 1 : 0));
        for (int i = 0; i < lobs; i++)
        {
            FacePlayer();
            float away = -DirToPlayer();
            float vy = Mathf.Max(1f, bubbleHopForce);
            float startTime = Time.time;
            LaunchJump(away * bubbleRetreatSpeed, vy);
            yield return WaitUntilAirborne();
            yield return WaitUntilFalling(1.5f);

            yield return Glint(bubbleLobGlintSeconds, OutfitColor(OutfitBubbleAir), keepMotion: true);
            LobBubble();

            yield return WaitUntilGrounded(2f);
            motion = Motion.Stop;

            // Keep the lobs spaced even when the hop was short.
            float spaced = Mathf.Max(0f, bubbleLobGap - (Time.time - startTime));
            yield return Wait(Mathf.Max(0.12f, spaced));
        }
    }

    private void LobBubble()
    {
        liveBubbles.RemoveAll(b => b == null || !b.IsAlive);
        if (bubbleBulletPrefab == null || liveBubbles.Count >= Mathf.Max(1, bubbleMaxLive))
            return;

        FacePlayer();
        Vector2 origin = OriginPoint();
        Vector2 dir = AimDirToPlayer(origin, 0f);
        dir = (dir + Vector2.up * Mathf.Max(0f, bubbleLobUpLean)).normalized;

        BassBubbleBullet bubble = Instantiate(bubbleBulletPrefab, origin, Quaternion.identity);
        bubble.gameObject.SetActive(true);
        ApplyBossAbilityBoosts(bubble);
        bubble.Launch(
            dir,
            bubbleLaunchSpeed,
            Damage(Mathf.RoundToInt(bubbleDamage)),
            Mathf.Max(0.01f, bubbleSpaceSize),
            airTornadoPrefab,
            transform);
        liveBubbles.Add(bubble);
        SoundManager.Instance?.PlayBassFireRapid();
        PlayShootAnim();
    }

    // ---------- Quick Time ----------

    /// <summary>Quick Man: a short run to range, then a high jump that throws a fan of Quick Boomerangs at the apex.</summary>
    private IEnumerator QuickBoomerangVolleyPattern()
    {
        yield return SwitchOutfit(OutfitQuickTime);

        // Quick run toward / away from the player to reach his range.
        float dx0 = DxToPlayer();
        float runDir = Mathf.Abs(dx0) > quickPreferredDistance ? Mathf.Sign(dx0) : -Mathf.Sign(dx0);
        if (Mathf.Abs(Mathf.Abs(dx0) - quickPreferredDistance) > 1.25f)
        {
            motion = Motion.Run;
            motionVelocity = new Vector2(runDir * Mathf.Max(1f, quickRunSpeed), 0f);
            SetFacing(runDir);
            float t = 0f;
            float runSeconds = Mathf.Max(0f, quickRunSeconds);
            while (t < runSeconds)
            {
                t += ActionDeltaTime * Tempo;
                float dist = Mathf.Abs(DxToPlayer());
                if (Mathf.Abs(dist - quickPreferredDistance) < 0.6f)
                    break;
                yield return null;
            }

            motion = Motion.Stop;
            walkDir = 0f;
            FacePlayer();
        }

        FacePlayer();
        float vy = Mathf.Max(1f, quickJumpForce);
        float vx = Mathf.Clamp(DxToPlayer() * 0.3f / EstimateAirTime(vy), -6f, 6f);
        LaunchJump(vx, vy);
        yield return WaitUntilAirborne();
        yield return WaitUntilFalling(1.6f);

        motion = Motion.Hang;
        SetGravity(false);
        float g = 0f;
        while (g < quickGlintSeconds)
        {
            g += ActionDeltaTime * Tempo;
            FacePlayer();
            ShowAura(telegraphAura, OutfitColor(OutfitQuickTime), glintColor, 0.5f + 0.4f * Mathf.PingPong(g * 10f, 1f), auraBaseScale);
            yield return null;
        }

        HideAura(telegraphAura);
        FireBoomerangFan(
            DifficultyTuning.ScaleSignatureCount(
                Mathf.Max(1, quickBoomerangCount) + (IsEnraged ? 2 : 0)),
            quickBoomerangSpreadDegrees);

        yield return WaitRaw(0.08f);
        SetGravity(true);
        motion = Motion.Free;
        yield return WaitUntilGrounded(2f);
        motion = Motion.Stop;
    }

    private void FireBoomerangFan(int count, float spreadDegrees)
    {
        liveBoomerangs.RemoveAll(b => b == null);
        if (quickBoomerangPrefab == null)
            return;
        if (liveBoomerangs.Count + count > Mathf.Max(count, quickBoomerangMaxLive))
            return;

        Vector2 origin = OriginPoint();
        float spread = Mathf.Max(0f, spreadDegrees);
        for (int i = 0; i < count; i++)
        {
            float offset = count == 1 ? 0f : Mathf.Lerp(-spread, spread, i / (float)(count - 1));
            Vector2 dir = AimDirToPlayer(origin, offset);

            BassQuickBoomerang boomerang = Instantiate(quickBoomerangPrefab, origin, Quaternion.identity);
            boomerang.gameObject.SetActive(true);
            ApplyBossAbilityBoosts(boomerang);
            boomerang.Launch(
                dir,
                quickBoomerangSpeed,
                Damage(quickBoomerangDamage),
                Mathf.Max(0.5f, quickBoomerangOutSpaces),
                Mathf.Max(0.01f, quickSpaceSize),
                transform);
            liveBoomerangs.Add(boomerang);
        }

        SoundManager.Instance?.PlayBassFireRapid();
        PlayShootAnim();
    }

    /// <summary>
    /// Mega Man 7 Bass: default outfit + Double Gear — approach with rapid buster bursts, hop-shot, then a short dash past.
    /// </summary>
    private IEnumerator DoubleGearBusterAssaultPattern()
    {
        yield return SwitchOutfit(OutfitDefault);
        BeginDoubleGear(IsEnraged
            ? Mathf.Max(0.5f, doubleGearEnragedDuration)
            : Mathf.Max(0.5f, doubleGearDuration));
        patternsSinceDoubleGear = 0;

        // Mega Man / Bass: walk into range, then stand still and shoot on the ground (never run-and-gun).
        float preferred = Mathf.Max(1.5f, busterPreferredDistance);
        float approachTimeout = 1.1f;
        float approachT = 0f;
        while (approachT < approachTimeout && Mathf.Abs(DxToPlayer()) > preferred + 0.35f)
        {
            approachT += ActionDeltaTime * Tempo;
            FacePlayer();
            motion = Motion.Walk;
            walkDir = DirToPlayer();
            yield return null;
        }

        motion = Motion.Stop;
        walkDir = 0f;
        StopHorizontal();
        FacePlayer();

        int groundCount = Mathf.Max(2, busterGroundBurstCount) + (IsEnraged ? 2 : 0);
        for (int i = 0; i < groundCount; i++)
        {
            FacePlayer();
            motion = Motion.Stop;
            walkDir = 0f;
            StopHorizontal();
            FireBusterShot(0f);
            if (i < groundCount - 1)
                yield return WaitRaw(Mathf.Max(0.05f, doubleGearBurstGap));
        }

        FacePlayer();

        // Hop toward / over the player with air shots.
        float vy = Mathf.Max(1f, busterHopForce);
        float vx = Mathf.Clamp(DxToPlayer() * 0.55f / EstimateAirTime(vy), -7f, 7f);
        LaunchJump(vx, vy);
        yield return WaitUntilAirborne();
        yield return WaitUntilFalling(1.5f);

        int air = Mathf.Max(1, busterAirBurstCount) + (IsEnraged ? 1 : 0);
        for (int i = 0; i < air; i++)
        {
            FacePlayer();
            float lean = i == air - 1 ? -18f : 0f;
            FireBusterShot(lean);
            if (i < air - 1)
                yield return WaitRaw(Mathf.Max(0.05f, doubleGearBurstGap));
        }

        yield return WaitUntilGrounded(2f);
        motion = Motion.Stop;
        yield return Wait(0.08f);

        // Short dash past the player, then a finishing burst facing them.
        float dashDir = DirToPlayer();
        float dashDist = Mathf.Max(2f, busterDashDistance) + (IsEnraged ? 1.5f : 0f);
        yield return Rush(dashDir, dashDist, Mathf.Max(1f, busterDashSpeed), 0, false, false);
        FacePlayer();
        int finish = IsEnraged ? 4 : 3;
        for (int i = 0; i < finish; i++)
        {
            FireBusterShot(0f);
            yield return WaitRaw(Mathf.Max(0.05f, doubleGearBurstGap));
        }

        yield return Wait(0.12f);
    }

    /// <summary>
    /// Flash Man: telegraph, time stops, he closes into boomerang range, then time resumes and the fan fires.
    /// He never fires while time is stopped.
    /// </summary>
    private IEnumerator FlashTimeTrapPattern()
    {
        flashReadyAt = Time.time + Mathf.Max(0f, flashCooldown);
        yield return SwitchOutfit(OutfitQuickTime);

        motion = Motion.Stop;
        walkDir = 0f;
        StopHorizontal();
        yield return FlashWindup(flashWindupSeconds);

        PlayerController player = FindPlayer();
        if (player == null || player.IsDead)
            yield break;

        BeginFlashStop(player);

        // Real time: close hard on the frozen player so the follow-up is point-blank.
        float elapsed = 0f;
        float stopSeconds = Mathf.Max(0.1f, flashStopSeconds);
        float closeRange = Mathf.Max(1f, flashRepositionDistance);
        bool reachedClose = false;
        while (elapsed < stopSeconds)
        {
            elapsed += Time.deltaTime;
            FacePlayer();

            float dx = DxToPlayer();
            float dist = Mathf.Abs(dx);
            if (dist <= closeRange)
                reachedClose = true;

            if (isGrounded && dist > closeRange)
            {
                float dir = Mathf.Abs(dx) > 0.05f ? Mathf.Sign(dx) : facingSign;
                motion = Motion.Raw;
                motionVelocity = new Vector2(dir * Mathf.Max(1f, flashRunSpeed), 0f);
            }
            else
            {
                motion = Motion.Raw;
                motionVelocity = Vector2.zero;
                // Arrived early — end the freeze so the boomerangs come out immediately.
                if (reachedClose && elapsed >= 0.35f)
                    break;
            }

            yield return null;
        }

        motion = Motion.Stop;
        walkDir = 0f;
        FacePlayer();
        EndFlashStop();

        // Fire immediately once time resumes (tiny optional telegraph only if somehow still far).
        float delay = reachedClose || Mathf.Abs(DxToPlayer()) <= closeRange + 0.5f
            ? 0f
            : Mathf.Max(0f, flashFireDelay);
        float t = 0f;
        while (t < delay)
        {
            t += ActionDeltaTime;
            FacePlayer();
            ShowAura(telegraphAura, OutfitColor(OutfitQuickTime), glintColor, 0.5f + 0.4f * Mathf.PingPong(t * 10f, 1f), auraBaseScale);
            yield return null;
        }

        HideAura(telegraphAura);
        FireBoomerangFan(Mathf.Max(1, flashBoomerangCount), flashBoomerangSpreadDegrees);
        yield return Wait(0.12f);
    }

    /// <summary>Flicker + clock ticks before time stops (the "he's about to do it" tell).</summary>
    private IEnumerator FlashWindup(float seconds)
    {
        float duration = Mathf.Max(0.05f, seconds);
        float t = 0f;
        int ticks = 0;
        while (t < duration)
        {
            float progress = Mathf.Clamp01(t / duration);
            while (ticks < 4 && progress >= ticks / 4f)
            {
                if (ticks % 2 == 0)
                    SoundManager.Instance?.PlayClockworkTic();
                else
                    SoundManager.Instance?.PlayClockworkToc();
                ticks++;
            }

            FacePlayer();
            ShowAura(
                telegraphAura,
                OutfitColor(OutfitQuickTime),
                Color.white,
                0.3f + 0.55f * progress,
                auraBaseScale * (1f + 0.12f * progress));
            t += ActionDeltaTime * Tempo;
            yield return null;
        }

        HideAura(telegraphAura);
    }

    private void BeginFlashStop(PlayerController player)
    {
        if (flashStopActive)
            return;

        flashStopActive = true;

        // Stack a full freeze on top of Double Gear — do not End other owners.
        HyperSpeedWorldSlow.Begin(FlashStopSlowOwner, 0f, slowPlayer: true);
        ownsWorldSlow = true;

        if (player != null && !player.IsDead)
        {
            player.SetTimeFrozen(true);
            frozenPlayer = player;
        }

        SpawnFlashStars();
    }

    private void EndFlashStop()
    {
        if (!flashStopActive && liveStars.Count == 0 && frozenPlayer == null && !ownsWorldSlow)
            return;

        flashStopActive = false;
        ClearFlashStars();

        if (frozenPlayer != null)
            frozenPlayer.SetTimeFrozen(false);
        frozenPlayer = null;

        if (ownsWorldSlow)
        {
            HyperSpeedWorldSlow.End(FlashStopSlowOwner);
            ownsWorldSlow = false;
        }
        // Double Gear layer (if still active) remains on the stack — no re-Begin needed.
    }

    private void SpawnFlashStars()
    {
        ClearFlashStars();
        if (flashTimeStarPrefab == null)
            return;

        Camera cam = Camera.main;
        if (cam == null)
            return;

        float depth = Mathf.Abs(cam.transform.position.z);
        float pad = Mathf.Max(0f, flashStarViewportPad);
        int count = Mathf.Max(8, flashStarCount);
        for (int i = 0; i < count; i++)
        {
            Vector2 vp = new Vector2(Random.Range(-pad, 1f + pad), Random.Range(-pad, 1f + pad));
            BassFlashTimeStar star = Instantiate(flashTimeStarPrefab);
            star.gameObject.SetActive(true);
            star.Place(cam, vp, depth);
            star.PlayTwinkle(Random.value);
            liveStars.Add(star);
        }
    }

    private void ClearFlashStars()
    {
        for (int i = 0; i < liveStars.Count; i++)
        {
            if (liveStars[i] != null)
                Destroy(liveStars[i].gameObject);
        }

        liveStars.Clear();
    }

    // ---------- Aiming / firing helpers ----------

    private Vector2 OriginPoint()
    {
        return firePoint != null
            ? (Vector2)firePoint.position
            : (Vector2)MyBounds().center + new Vector2(facingSign * 0.8f, -0.2f);
    }

    private Vector2 AimDirToPlayer(Vector2 origin, float angleOffset)
    {
        PlayerController player = FindPlayer();
        Vector2 dir = player != null
            ? (Vector2)PlayerBounds(player).center - origin
            : new Vector2(facingSign, 0f);
        if (dir.sqrMagnitude < 0.0001f)
            dir = new Vector2(facingSign, 0f);
        dir.Normalize();
        if (Mathf.Abs(angleOffset) > 0.01f)
            dir = Quaternion.Euler(0f, 0f, angleOffset) * dir;
        return dir;
    }

    private void PlayShootAnim()
    {
        shootPoseUntil = Time.time + 0.25f;
        if (animator == null)
            return;

        float vy = rb != null ? rb.linearVelocity.y : 0f;
        string trigger = isGrounded ? "Shoot" : vy < -0.01f ? "FallShot" : "JumpShot";
        if (HasParam(trigger))
            animator.SetTrigger(trigger);
    }

    /// <summary>FirePoint is authored facing left; mirror its X when facing right.</summary>
    private void SyncFirePoint()
    {
        if (firePoint == null || firePoint.parent != transform)
            return;

        Vector3 local = firePointLeftLocal;
        if (facingSign > 0f)
            local.x = -local.x;
        firePoint.localPosition = local;
    }

    // ---------- Building blocks ----------

    private IEnumerator Wait(float seconds)
    {
        float t = 0f;
        while (t < seconds)
        {
            t += ActionDeltaTime * Tempo;
            yield return null;
        }
    }

    /// <summary>Exact wait, not sped up by Tempo (shot spacing, fire delays).</summary>
    private IEnumerator WaitRaw(float seconds)
    {
        float t = 0f;
        while (t < seconds)
        {
            t += ActionDeltaTime;
            yield return null;
        }
    }

    /// <summary>Coloured glint telegraph. Stands still unless <paramref name="keepMotion"/> (e.g. at an apex).</summary>
    private IEnumerator Glint(float seconds, Color color, bool keepMotion = false)
    {
        if (!keepMotion)
            motion = Motion.Stop;

        float t = 0f;
        while (t < seconds)
        {
            t += ActionDeltaTime * Tempo;
            FacePlayer();
            ShowAura(telegraphAura, color, glintColor, 0.45f + 0.45f * Mathf.PingPong(t * 9f, 1f), auraBaseScale * 1.06f);
            yield return null;
        }

        HideAura(telegraphAura);
    }

    private IEnumerator WaitUntilAirborne(float timeout = 0.25f)
    {
        float t = 0f;
        while (isGrounded && t < timeout)
        {
            t += ActionDeltaTime;
            yield return null;
        }
    }

    private IEnumerator WaitUntilGrounded(float timeout)
    {
        float t = 0f;
        while (!isGrounded && t < timeout)
        {
            t += ActionDeltaTime;
            yield return null;
        }
    }

    private IEnumerator WaitUntilFalling(float timeout)
    {
        float t = 0f;
        while (!isGrounded && rb != null && rb.linearVelocity.y > 0f && t < timeout)
        {
            t += ActionDeltaTime;
            yield return null;
        }
    }

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

    private float PlayerFeetAboveMine()
    {
        PlayerController player = FindPlayer();
        return player != null ? PlayerBounds(player).min.y - MyBounds().min.y : 0f;
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

    private void FacePlayer()
    {
        PlayerController player = FindPlayer();
        if (player != null)
            FaceToward(player.transform);
    }

    private void SetFacing(float dir)
    {
        SetFacingSign(dir);
    }

    private void SetGravity(bool on)
    {
        gravityOff = !on;
        if (rb != null)
            rb.gravityScale = on ? defaultGravityScale : 0f;
    }

    private float EstimateAirTime(float launchVy)
    {
        float g = Mathf.Abs(Physics2D.gravity.y) * Mathf.Max(0.01f, defaultGravityScale);
        float up = launchVy / g;
        float peak = launchVy * launchVy / (2f * g);
        float down = Mathf.Sqrt(2f * peak / (g * Mathf.Max(1f, fallMultiplier)));
        return Mathf.Max(0.2f, up + down);
    }

    private void LaunchJump(float vx, float vy)
    {
        if (rb == null)
            return;

        motion = Motion.Free;
        SetGravity(true);
        rb.linearVelocity = new Vector2(ActionSpeed(vx), vy);
        isGrounded = false;
        FacePlayer();
        SoundManager.Instance?.PlayJump();
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

        aura.phase += Time.deltaTime * 1.5f;
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

    private void TickEnrageAura()
    {
        if (!showEnrageAura || !IsEnraged || IsDead)
        {
            HideAura(enrageAura);
            return;
        }

        ShowAura(enrageAura, enrageAuraColor, enrageAuraFlickerColor, enrageAuraAlpha, auraBaseScale * 1.08f);
    }

    // ---------- Afterimages ----------

    private void SetupAfterimages()
    {
        DestroyAfterimages();
        if (!enableAfterimages || spriteRenderer == null)
            return;

        int count = Mathf.Max(1, afterimageCount);
        afterimageObjects = new GameObject[count];
        afterimageRenderers = new SpriteRenderer[count];
        for (int i = 0; i < count; i++)
        {
            var go = new GameObject($"{name}_Afterimage_{i + 1}");
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            CharacterEffectSorting.ApplyTrailBehindBody(sr, spriteRenderer, EffectSortingGroup, i);
            go.SetActive(false);
            afterimageObjects[i] = go;
            afterimageRenderers[i] = sr;
        }
    }

    private void DestroyAfterimages()
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

    private float AfterimageDelay(int index) => Mathf.Max(0.01f, afterimageSpacing) * (index + 1);

    private float MaxAfterimageDelay => AfterimageDelay(Mathf.Max(1, afterimageCount) - 1);

    private void RecordPoseHistory()
    {
        if (!enableAfterimages || spriteRenderer == null)
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

    private void UpdateAfterimages()
    {
        if (afterimageRenderers == null || spriteRenderer == null)
            return;

        bool moving = isDashing || forceTrail;
        if (moving)
            afterimagesVisibleUntil = Mathf.Max(afterimagesVisibleUntil,
                Time.time + MaxAfterimageDelay + afterimageLifetimePadding);

        bool active = moving || Time.time <= afterimagesVisibleUntil;
        for (int i = 0; i < afterimageRenderers.Length; i++)
        {
            GameObject go = afterimageObjects[i];
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
            SpriteRenderer sr = afterimageRenderers[i];
            sr.sprite = sample.sprite != null ? sample.sprite : spriteRenderer.sprite;
            sr.flipX = sample.flipX;
            CharacterEffectSorting.ApplyTrailBehindBody(sr, spriteRenderer, EffectSortingGroup, i);

            float t = afterimageRenderers.Length == 1 ? 0f : i / (float)(afterimageRenderers.Length - 1);
            Color c = afterimageColor;
            c.a = Mathf.Lerp(afterimageAlphaStart, afterimageAlphaEnd, t);
            sr.color = c;
        }
    }

    private void HideAfterimages()
    {
        if (afterimageObjects == null)
            return;

        for (int i = 0; i < afterimageObjects.Length; i++)
        {
            if (afterimageObjects[i] != null)
                afterimageObjects[i].SetActive(false);
        }
    }

    // ---------- Reset ----------

    private void CleanupAfterPattern()
    {
        EndFlashStop();
        // Leaf ring is owned by MetalShieldPressPattern — only wipe it on hard resets (ResetActions).
        HideAura(telegraphAura);
        isDashing = false;
        if (!doubleGearActive)
            forceTrail = false;
        contactDamage = 0;
        SetGravity(true);
        if (motion != Motion.Free)
            motion = Motion.Stop;
        walkDir = 0f;
    }

    private void ResetActions()
    {
        actionStack.Clear();
        CleanupAfterPattern();
        DestroyLeaves();
        motion = Motion.Stop;
    }

    protected override void OnCombatPaused()
    {
        ResetActions();
        base.OnCombatPaused();
    }

    protected override void OnRevived()
    {
        ResetActions();
        hitVfxTimer = 0f;
        System.Array.Clear(zoneRotationIndex, 0, zoneRotationIndex.Length);
        flashReadyAt = 0f;
        signatureStarted = false;
        signaturePatternCounter = 0;
        signatureInterruptPending = false;
        patternsSinceDoubleGear = 0;
        doubleGearReadyAt = 0f;
        EndDoubleGear();
        SetOutfitImmediate(OutfitMetalLeaf);
    }

    protected override void OnCinematicDeathStarted()
    {
        base.OnCinematicDeathStarted();
        ResetActions();
        EndDoubleGear();
        EndFlashStop();
        DestroyLeaves();
        BassAirTornado.DestroyOwnedBy(transform);
        HideAura(enrageAura);
        HideAura(telegraphAura);
        HideAfterimages();
        hitVfxTimer = 0f;
        // Flash-stop / DG use dedicated slow owners — ensure they are released.
        HyperSpeedWorldSlow.End(FlashStopSlowOwner);
        HyperSpeedWorldSlow.End(this);
    }

    protected override void OnBossDefeated()
    {
        ResetActions();
        EndDoubleGear();
        HideAura(enrageAura);
        HideAfterimages();
        hitVfxTimer = 0f;
        VisualEffects.StopStunned(this);
        if (animator != null)
            animator.speed = 1f;

        enabled = false;
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        copyBotMaxHealth = Mathf.Max(0, copyBotMaxHealth);
        damageMultiplier = Mathf.Max(0.1f, damageMultiplier);
        lifeBarCount = Mathf.Max(1, lifeBarCount);
        tempoPerBarLost = Mathf.Max(0f, tempoPerBarLost);
        lateFightTempoBonus = Mathf.Max(0f, lateFightTempoBonus);
        maxTempo = Mathf.Max(1f, maxTempo);
        moveSpeedPerBarLost = Mathf.Max(0f, moveSpeedPerBarLost);
        recoverDurationFullHp = Mathf.Max(0.05f, recoverDurationFullHp);
        recoverDurationLowHp = Mathf.Max(0.05f, recoverDurationLowHp);
        recoverApproachSpeed = Mathf.Max(0f, recoverApproachSpeed);
        doubleGearWorldBlackTint = Mathf.Clamp01(doubleGearWorldBlackTint);
        doubleGearDuration = Mathf.Max(0.5f, doubleGearDuration);
        doubleGearEnragedDuration = Mathf.Max(doubleGearDuration, doubleGearEnragedDuration);
        doubleGearCooldown = Mathf.Max(0.5f, doubleGearCooldown);
        doubleGearEveryPatterns = Mathf.Max(1, doubleGearEveryPatterns);
        doubleGearShotSpeed = Mathf.Max(1f, doubleGearShotSpeed);
        doubleGearShotDamage = Mathf.Max(0, doubleGearShotDamage);
        doubleGearBurstGap = Mathf.Max(0.04f, doubleGearBurstGap);
        doubleGearAbilityScale = Mathf.Max(1f, doubleGearAbilityScale);
        busterPreferredDistance = Mathf.Max(1f, busterPreferredDistance);
        busterApproachSpeed = Mathf.Max(1f, busterApproachSpeed);
        busterGroundBurstCount = Mathf.Max(1, busterGroundBurstCount);
        busterAirBurstCount = Mathf.Max(1, busterAirBurstCount);
        busterHopForce = Mathf.Max(1f, busterHopForce);
        busterDashSpeed = Mathf.Max(1f, busterDashSpeed);
        busterDashDistance = Mathf.Max(1f, busterDashDistance);
        outfitSwapGlintSeconds = Mathf.Max(0f, outfitSwapGlintSeconds);
        closeZoneDistance = Mathf.Max(0.5f, closeZoneDistance);
        midZoneDistance = Mathf.Max(closeZoneDistance, midZoneDistance);
        antiAirHeight = Mathf.Max(0.1f, antiAirHeight);
        antiAirMaxDistance = Mathf.Max(0f, antiAirMaxDistance);
        metalHopsPerPattern = Mathf.Max(1, metalHopsPerPattern);
        metalLowHopForce = Mathf.Max(1f, metalLowHopForce);
        metalHighHopForce = Mathf.Max(metalLowHopForce, metalHighHopForce);
        metalPreferredDistance = Mathf.Max(0f, metalPreferredDistance);
        metalApexGlintSeconds = Mathf.Max(0f, metalApexGlintSeconds);
        metalBladeSpacing = Mathf.Max(2f, metalBladeSpacing);
        metalBladeSpeed = Mathf.Max(1f, metalBladeSpeed);
        metalBladeDamage = Mathf.Max(0, metalBladeDamage);
        metalBladeSpreadDegrees = Mathf.Max(0f, metalBladeSpreadDegrees);
        metalBladeMaxLive = Mathf.Max(1, metalBladeMaxLive);
        crossLeapGlintSeconds = Mathf.Max(0f, crossLeapGlintSeconds);
        crossLeapForce = Mathf.Max(1f, crossLeapForce);
        crossLeapOvershoot = Mathf.Max(0f, crossLeapOvershoot);
        crossLeapMaxSpeed = Mathf.Max(1f, crossLeapMaxSpeed);
        crossLeapBlades = Mathf.Max(1, crossLeapBlades);
        crossLeapRecoverSeconds = Mathf.Max(0f, crossLeapRecoverSeconds);
        leafCount = Mathf.Max(1, leafCount);
        leafOrbitRadius = Mathf.Max(0.5f, leafOrbitRadius);
        leafHealth = Mathf.Max(1, leafHealth);
        leafContactDamage = Mathf.Max(0, leafContactDamage);
        shieldSummonSeconds = Mathf.Max(0f, shieldSummonSeconds);
        shieldPressHops = Mathf.Max(1, shieldPressHops);
        shieldPressHopForce = Mathf.Max(1f, shieldPressHopForce);
        shieldPressStopDistance = Mathf.Max(0.5f, shieldPressStopDistance);
        shieldPressMaxHopSpeed = Mathf.Max(0.5f, shieldPressMaxHopSpeed);
        shieldFinalVolley = Mathf.Max(1, shieldFinalVolley);
        shieldLingerSeconds = Mathf.Max(0f, shieldLingerSeconds);
        crashPaceSeconds = Mathf.Max(0.05f, crashPaceSeconds);
        crashPreferredDistance = Mathf.Max(1f, crashPreferredDistance);
        crashHopForce = Mathf.Max(1f, crashHopForce);
        crashGlintSeconds = Mathf.Max(0f, crashGlintSeconds);
        crashBombSpeed = Mathf.Max(0.5f, crashBombSpeed);
        crashBombDamageScale = Mathf.Max(0.1f, crashBombDamageScale);
        heatBallDamageScale = Mathf.Max(0f, heatBallDamageScale);
        heatSpaceSize = Mathf.Max(0.01f, heatSpaceSize);
        crashPaceBusterShots = Mathf.Max(0, crashPaceBusterShots);
        crashPaceBusterSpeed = Mathf.Max(1f, crashPaceBusterSpeed);
        crashPaceBusterDamage = Mathf.Max(0, crashPaceBusterDamage);
        heatDashWindupSeconds = Mathf.Max(0.05f, heatDashWindupSeconds);
        heatDashSpeed = Mathf.Max(1f, heatDashSpeed);
        heatDashOvershoot = Mathf.Max(0f, heatDashOvershoot);
        heatDashMinDistance = Mathf.Max(0.5f, heatDashMinDistance);
        heatDashMaxDistance = Mathf.Max(heatDashMinDistance, heatDashMaxDistance);
        heatDashTurnPause = Mathf.Max(0f, heatDashTurnPause);
        heatDashContactDamage = Mathf.Max(0, heatDashContactDamage);
        heatDashTrailCount = Mathf.Max(1, heatDashTrailCount);
        heatDashTrailMinSpaces = Mathf.Max(0.1f, heatDashTrailMinSpaces);
        heatDashTrailMaxSpaces = Mathf.Max(heatDashTrailMinSpaces, heatDashTrailMaxSpaces);
        heatDashTrailDamageScale = Mathf.Max(0.1f, heatDashTrailDamageScale);
        bubbleLobCount = Mathf.Max(1, bubbleLobCount);
        bubbleHopForce = Mathf.Max(1f, bubbleHopForce);
        bubbleLobGlintSeconds = Mathf.Max(0f, bubbleLobGlintSeconds);
        bubbleLobGap = Mathf.Max(0.1f, bubbleLobGap);
        bubbleLaunchSpeed = Mathf.Max(0.5f, bubbleLaunchSpeed);
        bubbleLobUpLean = Mathf.Max(0f, bubbleLobUpLean);
        bubbleDamage = Mathf.Max(0f, bubbleDamage);
        bubbleSpaceSize = Mathf.Max(0.01f, bubbleSpaceSize);
        bubbleMaxLive = Mathf.Max(1, bubbleMaxLive);
        quickRunSeconds = Mathf.Max(0f, quickRunSeconds);
        quickRunSpeed = Mathf.Max(1f, quickRunSpeed);
        quickPreferredDistance = Mathf.Max(1f, quickPreferredDistance);
        quickJumpForce = Mathf.Max(1f, quickJumpForce);
        quickGlintSeconds = Mathf.Max(0f, quickGlintSeconds);
        quickBoomerangCount = Mathf.Max(1, quickBoomerangCount);
        quickBoomerangSpreadDegrees = Mathf.Max(0f, quickBoomerangSpreadDegrees);
        quickBoomerangSpeed = Mathf.Max(1f, quickBoomerangSpeed);
        quickBoomerangOutSpaces = Mathf.Max(0.5f, quickBoomerangOutSpaces);
        quickSpaceSize = Mathf.Max(0.01f, quickSpaceSize);
        quickBoomerangDamage = Mathf.Max(0, quickBoomerangDamage);
        quickBoomerangMaxLive = Mathf.Max(1, quickBoomerangMaxLive);
        flashWindupSeconds = Mathf.Max(0.05f, flashWindupSeconds);
        flashCooldown = Mathf.Max(0f, flashCooldown);
        flashStopSeconds = Mathf.Max(0.1f, flashStopSeconds);
        flashRepositionDistance = Mathf.Max(1f, flashRepositionDistance);
        flashRunSpeed = Mathf.Max(1f, flashRunSpeed);
        flashStarCount = Mathf.Max(8, flashStarCount);
        flashStarViewportPad = Mathf.Max(0f, flashStarViewportPad);
        flashFireDelay = Mathf.Max(0f, flashFireDelay);
        flashBoomerangCount = Mathf.Max(1, flashBoomerangCount);
        flashBoomerangSpreadDegrees = Mathf.Max(0f, flashBoomerangSpreadDegrees);
        enrageTempoBonus = Mathf.Max(0f, enrageTempoBonus);
        enrageMoveSpeedBonus = Mathf.Max(0f, enrageMoveSpeedBonus);
        enrageBonusDamage = Mathf.Max(0, enrageBonusDamage);
        signatureEvery = Mathf.Max(1, signatureEvery);
        copyBotTempoBonus = Mathf.Max(0f, copyBotTempoBonus);
        auraBaseScale = Mathf.Max(1f, auraBaseScale);
        afterimageCount = Mathf.Max(1, afterimageCount);
        afterimageSpacing = Mathf.Max(0.01f, afterimageSpacing);
        afterimageLifetimePadding = Mathf.Max(0f, afterimageLifetimePadding);
    }
#endif
}
