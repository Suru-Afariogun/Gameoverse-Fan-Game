using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.SceneManagement;

/// <summary>
/// Central audio hub. Create a GameObject named SoundManager, attach this, assign clips.
/// Survives scene loads. Volumes / pitches are Inspector-adjustable.
/// WebGL: resumes music after the first user gesture (browser autoplay policy).
/// </summary>
public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance { get; private set; }

    [Header("Master Volumes")]
    [SerializeField] [Range(0f, 1f)] private float masterVolume = 1f;
    [SerializeField] [Range(0f, 1f)] private float musicVolume = 0.7f;
    [SerializeField] [Range(0f, 1f)] private float sfxVolume = 1f;

    [Header("Scene Music (loop)")]
    [Tooltip("Start Screen scene — drag your song here.")]
    [SerializeField] private AudioClip startScreenMusic;
    [SerializeField] [Range(0f, 1f)] private float startScreenMusicVolume = 1f;

    [Tooltip("Home Town scene — drag your song here.")]
    [SerializeField] private AudioClip homeTownMusic;
    [SerializeField] [Range(0f, 1f)] private float homeTownMusicVolume = 1f;

    [Tooltip("Level one scene — drag your song here.")]
    [SerializeField] private AudioClip levelOneMusic;
    [SerializeField] [Range(0f, 1f)] private float levelOneMusicVolume = 1f;

    [Tooltip("Level one Copy Bot boss fight — replaces level music while the fight is active.")]
    [SerializeField] private AudioClip levelOneCopyBotBossMusic;
    [SerializeField] [Range(0f, 1f)] private float levelOneCopyBotBossMusicVolume = 1f;

    [Tooltip("Boss Fight Mode scene — drag your song here.")]
    [SerializeField] private AudioClip bossFightMusic;
    [SerializeField] [Range(0f, 1f)] private float bossFightMusicVolume = 1f;

    [Tooltip("More scenes: use the list below or drag .unity files onto Sound Manager in the Inspector.")]
    [SerializeField] private SceneMusicEntry[] extraSceneMusic = Array.Empty<SceneMusicEntry>();

    [Header("Music Playback")]
    [SerializeField] [Min(0f)] private float musicCrossfadeSeconds = 1.25f;
    [Tooltip("Seconds trimmed from the end when a song has no custom loop end.")]
    [SerializeField] [Min(0f)] private float musicAutoLoopEndTrimSeconds = 6f;
    [SerializeField] private MusicLoopProfile[] musicLoopProfiles = Array.Empty<MusicLoopProfile>();

    [Header("Charge Loops (one slot per character)")]
    [Tooltip("Legacy shared charge purr — used only when a character's own charge slot is empty.")]
    [SerializeField] private AudioClip chargePurrLoop;
    [SerializeField] [Range(0f, 1f)] private float chargePurrVolume = 1f;
    [SerializeField] [Range(0.5f, 2f)] private float chargePurrPitch = 1f;
    [SerializeField] private AudioClip kitChargeLoop;
    [SerializeField] [Range(0f, 1f)] private float kitChargeVolume = 1f;
    [SerializeField] [Range(0.5f, 2f)] private float kitChargePitch = 1f;
    [SerializeField] private AudioClip countChargeLoop;
    [SerializeField] [Range(0f, 1f)] private float countChargeVolume = 1f;
    [SerializeField] [Range(0.5f, 2f)] private float countChargePitch = 1f;
    [SerializeField] private AudioClip maliceChargeLoop;
    [SerializeField] [Range(0f, 1f)] private float maliceChargeVolume = 1f;
    [SerializeField] [Range(0.5f, 2f)] private float maliceChargePitch = 1f;
    [SerializeField] private AudioClip bossKitChargeLoop;
    [SerializeField] [Range(0f, 1f)] private float bossKitChargeVolume = 1f;
    [SerializeField] [Range(0.5f, 2f)] private float bossKitChargePitch = 1f;
    [SerializeField] private AudioClip bossMaliceChargeLoop;
    [SerializeField] [Range(0f, 1f)] private float bossMaliceChargeVolume = 1f;
    [SerializeField] [Range(0.5f, 2f)] private float bossMaliceChargePitch = 1f;
    [SerializeField] private AudioClip chaoticTankerChargeLoop;
    [SerializeField] [Range(0f, 1f)] private float chaoticTankerChargeVolume = 1f;
    [SerializeField] [Range(0.5f, 2f)] private float chaoticTankerChargePitch = 1f;

    [Header("Kit Shot")]
    [SerializeField] private AudioClip kitMeow;
    [Tooltip("Small + medium shots.")]
    [SerializeField] private AudioClip kitSoftLaser;
    [Tooltip("Big shot.")]
    [SerializeField] private AudioClip kitWeirdLaser;
    [SerializeField] [Range(0f, 1f)] private float kitMeowVolume = 1f;
    [SerializeField] [Range(0f, 1f)] private float kitLaserVolume = 1f;
    [Tooltip("Machine Gun meow volume (plays on every pellet via a capped voice pool).")]
    [SerializeField] [Range(0f, 1f)] private float kitRapidFireMeowVolume = 1f;
    [SerializeField] [Range(0f, 1f)] private float kitRapidFireLaserVolume = 0.8f;
    [Tooltip("How many overlapping meow/laser voices Machine Gun may keep (hard cap for performance).")]
    [SerializeField] [Range(2, 6)] private int kitRapidVoicePoolSize = 4;

    [Header("Count Shot")]
    [Tooltip("Leave empty to reuse Kit meow until you assign Count's own clip.")]
    [SerializeField] private AudioClip countMeow;
    [Tooltip("Short Hand / rapid fire laser.")]
    [SerializeField] private AudioClip countSoftLaser;
    [Tooltip("Long Hand laser.")]
    [SerializeField] private AudioClip countWeirdLaser;
    [SerializeField] [Range(0f, 1f)] private float countMeowVolume = 1f;
    [SerializeField] [Range(0f, 1f)] private float countLaserVolume = 1f;
    [SerializeField] [Range(0f, 1f)] private float countRapidFireMeowVolume = 1f;
    [SerializeField] [Range(0f, 1f)] private float countRapidFireLaserVolume = 0.8f;
    [SerializeField] [Range(2, 6)] private int countRapidVoicePoolSize = 4;

    [Header("Boss Kit Shot")]
    [Tooltip("Leave empty to reuse Kit clips until assigned.")]
    [SerializeField] private AudioClip bossKitMeow;
    [SerializeField] private AudioClip bossKitSoftLaser;
    [SerializeField] private AudioClip bossKitWeirdLaser;
    [SerializeField] [Range(0f, 1f)] private float bossKitMeowVolume = 1f;
    [SerializeField] [Range(0f, 1f)] private float bossKitLaserVolume = 1f;
    [SerializeField] [Range(0f, 1f)] private float bossKitRapidFireMeowVolume = 1f;
    [SerializeField] [Range(0f, 1f)] private float bossKitRapidFireLaserVolume = 0.8f;
    [SerializeField] [Range(2, 6)] private int bossKitRapidVoicePoolSize = 4;

    [Header("Malice Slash")]
    [Tooltip("Slash 1 / 2 / Air Slash.")]
    [SerializeField] private AudioClip slashLight;
    [Tooltip("Slash 3 (retro slash).")]
    [SerializeField] private AudioClip slashHeavy;
    [SerializeField] [Range(0f, 1f)] private float slashLightVolume = 1f;
    [SerializeField] [Range(0f, 1f)] private float slashHeavyVolume = 1f;

    [Header("Boss Malice Slash")]
    [Tooltip("Leave empty to reuse player Malice slash clips until assigned.")]
    [SerializeField] private AudioClip bossMaliceSlashLight;
    [SerializeField] private AudioClip bossMaliceSlashHeavy;
    [SerializeField] [Range(0f, 1f)] private float bossMaliceSlashLightVolume = 1f;
    [SerializeField] [Range(0f, 1f)] private float bossMaliceSlashHeavyVolume = 1f;

    [Header("Malice Grapple")]
    [Tooltip("Shared grapple clip (used when extend/reel slots are empty). Extend + reel can slice this clip.")]
    [SerializeField] private AudioClip grappleHook;
    [Tooltip("Optional full clip for extend only. Empty = slice grappleHook.")]
    [SerializeField] private AudioClip grappleExtendSfx;
    [Tooltip("Optional full clip for reel-in only. Empty = slice grappleHook.")]
    [SerializeField] private AudioClip grappleReelSfx;
    [SerializeField] [Range(0f, 1f)] private float grappleVolume = 1f;
    [SerializeField] [Range(0.5f, 2f)] private float grappleBasePitch = 1f;
    [Tooltip("If 0, auto-detects the first loud spike (extend) in grappleHook.")]
    [SerializeField] private float grappleExtendSpikeTime = 0f;
    [Tooltip("If 0, auto-detects the second loud spike (reel-in) after the first.")]
    [SerializeField] private float grappleReelSpikeTime = 0f;
    [Tooltip("How long the extend slice may play before we wait for reel-in.")]
    [SerializeField] private float grappleExtendMaxDuration = 0.28f;

    [Header("Boss Malice Grapple")]
    [Tooltip("Leave empty to reuse player Malice grapple until assigned.")]
    [SerializeField] private AudioClip bossMaliceGrappleHook;
    [SerializeField] private AudioClip bossMaliceGrappleExtendSfx;
    [SerializeField] private AudioClip bossMaliceGrappleReelSfx;
    [SerializeField] [Range(0f, 1f)] private float bossMaliceGrappleVolume = 1f;
    [SerializeField] [Range(0.5f, 2f)] private float bossMaliceGrappleBasePitch = 1f;

    [Header("Harlie Melee")]
    [Tooltip("Slash 1 / 2 / 3 and air slash (from Little Program Harlie). Kick is silent.")]
    [SerializeField] private AudioClip harlieSwordSwing;
    [Tooltip("Big clash when a slash connects (from Little Program Harlie).")]
    [SerializeField] private AudioClip harlieBigSwordClash;
    [Tooltip("Loops while Harlie holds Attack for a charge kick.")]
    [SerializeField] private AudioClip harlieChargeKickLoop;
    [SerializeField] [Range(0f, 1f)] private float harlieSwingVolume = 1f;
    [SerializeField] [Range(0f, 1f)] private float harlieClashVolume = 1f;
    [SerializeField] [Range(0f, 1f)] private float harlieChargeKickVolume = 1f;
    [SerializeField] [Range(0.5f, 2f)] private float harlieChargeKickPitch = 1f;
    [Tooltip("Overlapping slash voices when Harlie chains quickly (Speed Style).")]
    [SerializeField] [Range(2, 8)] private int harlieSwingVoicePoolSize = 5;

    [Header("UI (Kaboodle / Pause)")]
    [Tooltip("Legacy shared UI beep — used when Confirm/Back slots are empty.")]
    [SerializeField] private AudioClip uiSoftLaser;
    [SerializeField] private AudioClip uiConfirmSfx;
    [SerializeField] private AudioClip uiBackSfx;
    [SerializeField] [Range(0f, 1f)] private float uiVolume = 1f;
    [Tooltip("Loops while NPC dialogue text is typing (Undertale-style). Same clip as Serious Business text boxes.")]
    [SerializeField] private AudioClip dialogueTypingLoop;
    [SerializeField] [Range(0f, 1f)] private float dialogueTypingVolume = 1f;

    [Header("Gameplay SFX — Movement")]
    [SerializeField] private AudioClip jumpSfx;
    [Tooltip("Count dash-jump. Empty = reuse Jump.")]
    [SerializeField] private AudioClip countJumpSfx;
    [SerializeField] private AudioClip dashSfx;

    [Header("Gameplay SFX — Items")]
    [SerializeField] private AudioClip collectItemSfx;
    [Tooltip("Item pops out of an enemy (spawn).")]
    [SerializeField] private AudioClip itemDropSfx;
    [Tooltip("Pickup lands on the ground after burst.")]
    [SerializeField] private AudioClip itemHitGroundSfx;

    [Header("Gameplay SFX — Clockwork Platforms")]
    [Tooltip("Played every second while green/red platforms are counting down. Empty = Count's Tick shot sound.")]
    [SerializeField] private AudioClip clockworkTicSfx;
    [Tooltip("Played right as the platforms switch.")]
    [SerializeField] private AudioClip clockworkTocSfx;
    [Tooltip("Where in the Toc clip playback starts (seconds).")]
    [SerializeField] private float clockworkTocStartTime = 0f;
    [Tooltip("The Toc clip is cut off after this many seconds (the source clip is long).")]
    [SerializeField] private float clockworkTocMaxSeconds = 0.6f;
    [SerializeField] [Range(0f, 1f)] private float clockworkVolume = 1f;

    [Header("Gameplay SFX — Crystals")]
    [SerializeField] private AudioClip crystalHitSfx;
    [Tooltip("Crystal shattering.")]
    [SerializeField] private AudioClip crystalShatterSfx;

    [Header("Gameplay SFX — Boss")]
    [Tooltip("Boss takes damage. Empty = Crystal Hit until assigned.")]
    [SerializeField] private AudioClip bossHitSfx;

    [Header("Gameplay SFX — Laser Bot")]
    [Tooltip("Empty hit/death = Boss Hit / Enemy Explosion until assigned.")]
    [SerializeField] private AudioClip laserBotHitSfx;
    [SerializeField] private AudioClip laserBotShotSfx;
    [SerializeField] private AudioClip laserBotDeathSfx;

    [Header("Gameplay SFX — Blocker Bot")]
    [SerializeField] private AudioClip blockerBotHitSfx;
    [Tooltip("Empty = player Dash until assigned.")]
    [SerializeField] private AudioClip blockerBotDashSfx;
    [SerializeField] private AudioClip blockerBotDeathSfx;

    [Header("Gameplay SFX — Scrap Nit")]
    [SerializeField] private AudioClip scrapNitHitSfx;
    [SerializeField] private AudioClip scrapNitDeathSfx;

    [Header("Gameplay SFX — Cranky Clanky")]
    [SerializeField] private AudioClip crankyClankyHitSfx;
    [SerializeField] private AudioClip crankyClankyHopSfx;
    [SerializeField] private AudioClip crankyClankyDeathSfx;

    [Header("Gameplay SFX — Chaotic Tanker")]
    [SerializeField] private AudioClip chaoticTankerHitSfx;
    [SerializeField] private AudioClip chaoticTankerVoltSfx;
    [SerializeField] private AudioClip chaoticTankerDeathSfx;

    [Header("Gameplay SFX — Enemies (legacy fallback)")]
    [Tooltip("Used when a specific enemy death slot is empty.")]
    [SerializeField] private AudioClip enemyExplosionSfx;

    [Header("Gameplay SFX — Game Flow")]
    [SerializeField] private AudioClip gameOverSfx;
    [SerializeField] private AudioClip gameRestartSfx;
    [Tooltip("WARNING text + robot alert.")]
    [SerializeField] private AudioClip robotWarningSfx;
    [SerializeField] [Range(0f, 1f)] private float gameplaySfxVolume = 1f;

    [Header("Projectile Hit (voice pool)")]
    [SerializeField] private AudioClip projectileHitSmall;
    [SerializeField] private AudioClip projectileHitMedium;
    [SerializeField] private AudioClip projectileHitBig;
    [SerializeField] [Range(0f, 1f)] private float projectileHitVolume = 0.85f;
    [SerializeField] [Range(2, 8)] private int projectileHitVoicePoolSize = 4;

    private AudioSource musicSource;
    private AudioSource musicSourceAlt;
    private AudioSource activeMusicSource;
    private Coroutine musicCrossfadeRoutine;
    private Coroutine musicFadeOutRoutine;
    private AudioSource sfxSource;
    private AudioSource pitchedSfxSource;
    private AudioSource chargeLoopSource;
    private AudioSource clockworkTocSource;
    private AudioSource dialogueTypingSource;
    private AudioSource grappleSource;
    private AudioSource[] harlieSwingPool;
    private int harlieSwingPoolIndex;
    private AudioSource harlieChargeKickSource;
    private int harlieChargeKickLoopRefCount;
    private AudioSource[] kitRapidMeowPool;
    private AudioSource[] kitRapidLaserPool;
    private int kitRapidMeowPoolIndex;
    private int kitRapidLaserPoolIndex;
    private AudioSource[] countRapidMeowPool;
    private AudioSource[] countRapidLaserPool;
    private int countRapidMeowPoolIndex;
    private int countRapidLaserPoolIndex;
    private AudioSource[] bossKitRapidMeowPool;
    private AudioSource[] bossKitRapidLaserPool;
    private int bossKitRapidMeowPoolIndex;
    private int bossKitRapidLaserPoolIndex;
    private AudioSource[] projectileHitPool;
    private int projectileHitPoolIndex;
    private AudioClip currentMusic;
    private float currentMusicTrackVolume = 1f;
    private bool levelOneCopyBotMusicActive;
    private int chargeLoopRefCount;
    private float activeChargeLoopVolume = 1f;
    private bool grappleSpikesCached;
    private float cachedExtendSpike;
    private float cachedReelSpike;
    private bool bossGrappleSpikesCached;
    private float cachedBossExtendSpike;
    private float cachedBossReelSpike;

    public enum ChargeLoopId
    {
        Kit,
        Count,
        Malice,
        BossKit,
        BossMalice,
        ChaoticTanker
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    private bool webAudioUnlocked;
    private IDisposable webAudioUnlockSubscription;
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureExists()
    {
        if (Instance != null)
            return;

        if (FindFirstObjectByType<SoundManager>() != null)
            return;

        GameObject go = new GameObject("SoundManager");
        go.AddComponent<SoundManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        EnsureAudioSources();
        ApplyVolumes();
        BeginWebAudioUnlockWatch();
    }

    private void Start()
    {
        // Cover the case where we spawn after the first scene already loaded.
        HandleSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    private void Update()
    {
        TickCustomMusicLoops();
    }

    private void OnDestroy()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        webAudioUnlockSubscription?.Dispose();
        webAudioUnlockSubscription = null;
#endif
        if (Instance == this)
            Instance = null;
    }

    private void BeginWebAudioUnlockWatch()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (webAudioUnlocked || webAudioUnlockSubscription != null)
            return;

        // One-shot listener: no per-frame polling. Browsers require a user gesture first.
        webAudioUnlockSubscription = InputSystem.onAnyButtonPress.Call(OnWebUserGesture);
#endif
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    private void OnWebUserGesture(InputControl control)
    {
        if (webAudioUnlocked)
            return;

        webAudioUnlocked = true;
        AudioListener.pause = false;

        EnsureAudioSources();
        if (activeMusicSource != null && currentMusic != null && !activeMusicSource.isPlaying)
        {
            activeMusicSource.clip = currentMusic;
            activeMusicSource.loop = true;
            activeMusicSource.time = GetMusicLoopStart(currentMusic);
            activeMusicSource.volume = masterVolume * musicVolume * Mathf.Clamp01(currentMusicTrackVolume);
            activeMusicSource.Play();
        }

        webAudioUnlockSubscription?.Dispose();
        webAudioUnlockSubscription = null;
    }
#endif

    private void EnsureAudioSources()
    {
        if (musicSource == null)
        {
            musicSource = gameObject.AddComponent<AudioSource>();
            musicSource.playOnAwake = false;
            musicSource.loop = false;
            musicSource.spatialBlend = 0f;
            // Highest priority so SFX spam cannot virtualize / mute music.
            musicSource.priority = 0;
        }

        if (musicSourceAlt == null)
        {
            musicSourceAlt = gameObject.AddComponent<AudioSource>();
            musicSourceAlt.playOnAwake = false;
            musicSourceAlt.loop = false;
            musicSourceAlt.spatialBlend = 0f;
            musicSourceAlt.priority = 0;
        }

        if (activeMusicSource == null)
            activeMusicSource = musicSource;

        if (sfxSource == null)
        {
            sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;
            sfxSource.loop = false;
            sfxSource.spatialBlend = 0f;
            sfxSource.pitch = 1f;
            sfxSource.priority = 128;
        }

        if (pitchedSfxSource == null)
        {
            pitchedSfxSource = gameObject.AddComponent<AudioSource>();
            pitchedSfxSource.playOnAwake = false;
            pitchedSfxSource.loop = false;
            pitchedSfxSource.spatialBlend = 0f;
            pitchedSfxSource.priority = 128;
        }

        if (grappleSource == null)
        {
            grappleSource = gameObject.AddComponent<AudioSource>();
            grappleSource.playOnAwake = false;
            grappleSource.loop = false;
            grappleSource.spatialBlend = 0f;
            grappleSource.priority = 64;
        }

        if (chargeLoopSource == null)
        {
            chargeLoopSource = gameObject.AddComponent<AudioSource>();
            chargeLoopSource.playOnAwake = false;
            chargeLoopSource.loop = true;
            chargeLoopSource.spatialBlend = 0f;
            chargeLoopSource.priority = 80;
        }

        if (dialogueTypingSource == null)
        {
            dialogueTypingSource = gameObject.AddComponent<AudioSource>();
            dialogueTypingSource.playOnAwake = false;
            dialogueTypingSource.loop = true;
            dialogueTypingSource.spatialBlend = 0f;
            dialogueTypingSource.priority = 90;
        }

        if (harlieChargeKickSource == null)
        {
            harlieChargeKickSource = gameObject.AddComponent<AudioSource>();
            harlieChargeKickSource.playOnAwake = false;
            harlieChargeKickSource.loop = true;
            harlieChargeKickSource.spatialBlend = 0f;
            harlieChargeKickSource.priority = 80;
        }

        EnsureHarlieSwingPool();
        EnsureKitRapidVoicePools();
        EnsureCountRapidVoicePools();
        EnsureBossKitRapidVoicePools();
        EnsureProjectileHitPool();

        // Keep priorities even if sources already existed from an older session.
        musicSource.priority = 0;
        if (musicSourceAlt != null) musicSourceAlt.priority = 0;
        if (sfxSource != null) sfxSource.priority = 128;
        if (pitchedSfxSource != null) pitchedSfxSource.priority = 128;
        if (grappleSource != null) grappleSource.priority = 64;
        if (chargeLoopSource != null) chargeLoopSource.priority = 80;
        if (harlieChargeKickSource != null) harlieChargeKickSource.priority = 80;
    }

    private void EnsureHarlieSwingPool()
    {
        int size = Mathf.Clamp(harlieSwingVoicePoolSize, 2, 8);
        harlieSwingPool = BuildOrResizeVoicePool(harlieSwingPool, size, 96);
        harlieSwingPoolIndex %= harlieSwingPool.Length;
    }

    private void EnsureKitRapidVoicePools()
    {
        int size = Mathf.Clamp(kitRapidVoicePoolSize, 2, 6);
        kitRapidMeowPool = BuildOrResizeVoicePool(kitRapidMeowPool, size, 40);
        kitRapidLaserPool = BuildOrResizeVoicePool(kitRapidLaserPool, size, 56);
        kitRapidMeowPoolIndex %= kitRapidMeowPool.Length;
        kitRapidLaserPoolIndex %= kitRapidLaserPool.Length;
    }

    private void EnsureCountRapidVoicePools()
    {
        int size = Mathf.Clamp(countRapidVoicePoolSize, 2, 6);
        countRapidMeowPool = BuildOrResizeVoicePool(countRapidMeowPool, size, 40);
        countRapidLaserPool = BuildOrResizeVoicePool(countRapidLaserPool, size, 56);
        countRapidMeowPoolIndex %= countRapidMeowPool.Length;
        countRapidLaserPoolIndex %= countRapidLaserPool.Length;
    }

    private void EnsureBossKitRapidVoicePools()
    {
        int size = Mathf.Clamp(bossKitRapidVoicePoolSize, 2, 6);
        bossKitRapidMeowPool = BuildOrResizeVoicePool(bossKitRapidMeowPool, size, 40);
        bossKitRapidLaserPool = BuildOrResizeVoicePool(bossKitRapidLaserPool, size, 56);
        bossKitRapidMeowPoolIndex %= bossKitRapidMeowPool.Length;
        bossKitRapidLaserPoolIndex %= bossKitRapidLaserPool.Length;
    }

    private void EnsureProjectileHitPool()
    {
        int size = Mathf.Clamp(projectileHitVoicePoolSize, 2, 8);
        projectileHitPool = BuildOrResizeVoicePool(projectileHitPool, size, 112);
        projectileHitPoolIndex %= projectileHitPool.Length;
    }

    private AudioSource[] BuildOrResizeVoicePool(AudioSource[] existing, int size, int priority)
    {
        if (existing != null && existing.Length == size)
        {
            for (int i = 0; i < existing.Length; i++)
            {
                if (existing[i] != null)
                    existing[i].priority = priority;
            }

            return existing;
        }

        if (existing != null)
        {
            for (int i = 0; i < existing.Length; i++)
            {
                if (existing[i] != null)
                    Destroy(existing[i]);
            }
        }

        AudioSource[] pool = new AudioSource[size];
        for (int i = 0; i < size; i++)
        {
            AudioSource src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = false;
            src.spatialBlend = 0f;
            src.pitch = 1f;
            src.volume = 1f;
            src.priority = priority;
            pool[i] = src;
        }

        return pool;
    }

    private void ApplyVolumes()
    {
        // Music volume is set per-track when playing.
        if (sfxSource != null)
            sfxSource.volume = masterVolume * sfxVolume;
        if (pitchedSfxSource != null)
            pitchedSfxSource.volume = masterVolume * sfxVolume;
        if (chargeLoopSource != null)
            chargeLoopSource.volume = masterVolume * sfxVolume * activeChargeLoopVolume;
        if (dialogueTypingSource != null)
            dialogueTypingSource.volume = masterVolume * sfxVolume * dialogueTypingVolume;
        if (harlieChargeKickSource != null)
            harlieChargeKickSource.volume = masterVolume * sfxVolume * harlieChargeKickVolume;
        if (grappleSource != null)
            grappleSource.volume = masterVolume * sfxVolume * grappleVolume;
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        levelOneCopyBotMusicActive = false;
        string sceneName = scene.name ?? string.Empty;

        if (TryPlayDedicatedSceneMusic(sceneName))
            return;

        if (TryPlayExtraSceneMusic(sceneName))
            return;

        // No track assigned for this scene — silence (no shared fallback loop).
        StopMusic();
    }

    private bool TryPlayDedicatedSceneMusic(string sceneName)
    {
        if (IsBossFightScene(sceneName))
            return TryPlaySceneMusic(bossFightMusic, bossFightMusicVolume);

        if (IsStartScreenScene(sceneName))
            return TryPlaySceneMusic(startScreenMusic, startScreenMusicVolume);

        if (IsHomeTownScene(sceneName))
            return TryPlaySceneMusic(homeTownMusic, homeTownMusicVolume);

        if (LevelOneBossEncounter.IsLevelOneScene(sceneName))
            return TryPlaySceneMusic(levelOneMusic, levelOneMusicVolume);

        return false;
    }

    private bool TryPlayExtraSceneMusic(string sceneName)
    {
        if (extraSceneMusic == null || extraSceneMusic.Length == 0)
            return false;

        for (int i = 0; i < extraSceneMusic.Length; i++)
        {
            SceneMusicEntry entry = extraSceneMusic[i];
            if (entry == null || entry.Music == null)
                continue;

            if (!string.Equals(sceneName, entry.SceneName, StringComparison.OrdinalIgnoreCase))
                continue;

            return TryPlaySceneMusic(entry.Music, entry.TrackVolume);
        }

        return false;
    }

    private static bool IsBossFightScene(string sceneName)
    {
        return sceneName.IndexOf("Boss Fight", StringComparison.OrdinalIgnoreCase) >= 0 ||
               sceneName.IndexOf("BossFight", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsStartScreenScene(string sceneName)
    {
        return string.Equals(sceneName, "Start Screen", StringComparison.OrdinalIgnoreCase) ||
               sceneName.IndexOf("Start Screen", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsHomeTownScene(string sceneName)
    {
        return string.Equals(sceneName, "HomeTown", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(sceneName, "Home Town", StringComparison.OrdinalIgnoreCase);
    }

    private bool TryPlaySceneMusic(AudioClip clip, float trackVolumeScale)
    {
        if (clip == null)
        {
            StopMusic();
            return true;
        }

        PlayMusic(clip, trackVolumeScale);
        return true;
    }

    /// <summary>
    /// Level one Copy Bot fight: stops level music and plays the boss track.
    /// </summary>
    public void PlayLevelOneCopyBotBossMusic()
    {
        levelOneCopyBotMusicActive = true;

        if (musicFadeOutRoutine != null)
        {
            StopCoroutine(musicFadeOutRoutine);
            musicFadeOutRoutine = null;
        }

        if (levelOneCopyBotBossMusic == null)
        {
            StopMusic();
            return;
        }

        PlayMusic(levelOneCopyBotBossMusic, levelOneCopyBotBossMusicVolume, restartIfSame: true);
    }

    /// <summary>
    /// Copy Bot defeated or fight ended — resume Level one scene music if still in that scene.
    /// </summary>
    public void StopLevelOneCopyBotBossMusic()
    {
        if (!levelOneCopyBotMusicActive)
            return;

        levelOneCopyBotMusicActive = false;

        if (!LevelOneBossEncounter.IsLevelOneScene(SceneManager.GetActiveScene().name))
        {
            // Resume this scene's track (Tutorial extra-scene music, etc.) instead of silence.
            levelOneCopyBotMusicActive = false;
            HandleSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
            return;
        }

        TryPlaySceneMusic(levelOneMusic, levelOneMusicVolume);
    }

    public bool IsLevelOneCopyBotMusicActive => levelOneCopyBotMusicActive;

    /// <summary>
    /// Boss room door opening — fade out Level one music before the door closes.
    /// </summary>
    public void FadeOutLevelOneMusic(float fadeSeconds = -1f)
    {
        if (!LevelOneBossEncounter.IsLevelOneScene(SceneManager.GetActiveScene().name))
            return;

        if (levelOneCopyBotMusicActive)
            return;

        if (activeMusicSource == null || !activeMusicSource.isPlaying || currentMusic != levelOneMusic)
            return;

        if (musicFadeOutRoutine != null)
            StopCoroutine(musicFadeOutRoutine);

        float duration = fadeSeconds > 0f ? fadeSeconds : musicCrossfadeSeconds;
        musicFadeOutRoutine = StartCoroutine(FadeOutActiveMusic(duration));
    }

    // --- Music ---

    public void PlayMusic(AudioClip clip, float trackVolumeScale = 1f, bool restartIfSame = false)
    {
        EnsureAudioSources();
        if (clip == null)
            return;

        currentMusicTrackVolume = trackVolumeScale;
        float targetVolume = masterVolume * musicVolume * Mathf.Clamp01(trackVolumeScale);

        if (!restartIfSame &&
            currentMusic == clip &&
            activeMusicSource != null &&
            activeMusicSource.isPlaying &&
            activeMusicSource.clip == clip)
        {
            activeMusicSource.volume = targetVolume;
            return;
        }

        if (activeMusicSource != null &&
            activeMusicSource.isPlaying &&
            currentMusic != null &&
            currentMusic != clip &&
            musicCrossfadeSeconds > 0.01f)
        {
            if (musicCrossfadeRoutine != null)
                StopCoroutine(musicCrossfadeRoutine);

            musicCrossfadeRoutine = StartCoroutine(CrossfadeMusic(clip, targetVolume));
            return;
        }

        StopMusicCrossfadeImmediate();
        PlayMusicImmediate(clip, targetVolume);
    }

    public void StopMusic()
    {
        StopMusicCrossfadeImmediate();

        if (musicSource != null)
            musicSource.Stop();
        if (musicSourceAlt != null)
            musicSourceAlt.Stop();

        activeMusicSource = musicSource;
        currentMusic = null;
    }

    private void PlayMusicImmediate(AudioClip clip, float targetVolume)
    {
        EnsureAudioSources();
        AudioSource idle = activeMusicSource == musicSource ? musicSourceAlt : musicSource;
        if (idle != null && idle.isPlaying)
            idle.Stop();

        activeMusicSource = musicSource;
        activeMusicSource.Stop();
        activeMusicSource.clip = clip;
        // Unity loop is the safety net; TickCustomMusicLoops still handles early loop ends.
        activeMusicSource.loop = true;
        activeMusicSource.volume = targetVolume;
        activeMusicSource.time = GetMusicLoopStart(clip);
        activeMusicSource.Play();

        currentMusic = clip;
    }

    private IEnumerator CrossfadeMusic(AudioClip clip, float targetVolume)
    {
        EnsureAudioSources();

        AudioSource fadeOut = activeMusicSource;
        AudioSource fadeIn = fadeOut == musicSource ? musicSourceAlt : musicSource;

        fadeIn.Stop();
        fadeIn.clip = clip;
        fadeIn.loop = true;
        fadeIn.time = GetMusicLoopStart(clip);
        fadeIn.volume = 0f;
        fadeIn.Play();

        float startOutVolume = fadeOut != null && fadeOut.isPlaying ? fadeOut.volume : 0f;
        float duration = Mathf.Max(0.01f, musicCrossfadeSeconds);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            if (fadeOut != null && fadeOut.isPlaying)
                fadeOut.volume = Mathf.Lerp(startOutVolume, 0f, t);

            fadeIn.volume = Mathf.Lerp(0f, targetVolume, t);
            yield return null;
        }

        if (fadeOut != null)
            fadeOut.Stop();

        fadeIn.volume = targetVolume;
        activeMusicSource = fadeIn;
        currentMusic = clip;
        musicCrossfadeRoutine = null;
    }

    private void StopMusicCrossfadeImmediate()
    {
        if (musicCrossfadeRoutine != null)
        {
            StopCoroutine(musicCrossfadeRoutine);
            musicCrossfadeRoutine = null;
        }

        if (musicFadeOutRoutine != null)
        {
            StopCoroutine(musicFadeOutRoutine);
            musicFadeOutRoutine = null;
        }
    }

    private IEnumerator FadeOutActiveMusic(float duration)
    {
        EnsureAudioSources();

        AudioSource source = activeMusicSource;
        if (source == null || !source.isPlaying)
        {
            musicFadeOutRoutine = null;
            yield break;
        }

        float startVolume = source.volume;
        float elapsed = 0f;
        duration = Mathf.Max(0.01f, duration);

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            if (source.isPlaying)
                source.volume = Mathf.Lerp(startVolume, 0f, t);

            yield return null;
        }

        if (source.isPlaying)
            source.Stop();

        source.volume = startVolume;
        currentMusic = null;
        musicFadeOutRoutine = null;
    }

    private void TickCustomMusicLoops()
    {
        if (musicSource != null && musicSource.isPlaying && musicSource.clip != null)
            TickMusicSourceLoop(musicSource);

        if (musicSourceAlt != null && musicSourceAlt.isPlaying && musicSourceAlt.clip != null)
            TickMusicSourceLoop(musicSourceAlt);
    }

    private void TickMusicSourceLoop(AudioSource source)
    {
        AudioClip clip = source.clip;
        if (clip == null)
            return;

        ResolveMusicLoopWindow(clip, out float loopStart, out float loopEnd);
        if (loopEnd <= loopStart + 0.05f)
            return;

        if (source.time >= loopEnd - 0.02f)
            source.time = loopStart;
    }

    private void ResolveMusicLoopWindow(AudioClip clip, out float loopStart, out float loopEnd)
    {
        loopStart = 0f;
        loopEnd = clip.length;

        if (musicLoopProfiles != null)
        {
            for (int i = 0; i < musicLoopProfiles.Length; i++)
            {
                MusicLoopProfile profile = musicLoopProfiles[i];
                if (profile == null || profile.clip != clip)
                    continue;

                loopStart = Mathf.Max(0f, profile.loopStart);
                loopEnd = profile.loopEnd > 0.01f
                    ? profile.loopEnd
                    : Mathf.Max(loopStart + 0.5f, clip.length - SafeMusicEndTrim(clip));
                loopEnd = Mathf.Min(loopEnd, clip.length - 0.02f);
                return;
            }
        }

        // Short tracks (e.g. Tutorial): do not trim so far that the loop window collapses.
        loopEnd = Mathf.Max(loopStart + 0.5f, clip.length - SafeMusicEndTrim(clip));
        loopEnd = Mathf.Min(loopEnd, clip.length - 0.02f);
        if (loopEnd <= loopStart + 0.05f)
            loopEnd = Mathf.Max(0.05f, clip.length - 0.02f);
    }

    /// <summary>
    /// End trim for seamless loops — never more than 15% of the clip, and never enough
    /// to leave a tiny / zero window on short songs.
    /// </summary>
    private float SafeMusicEndTrim(AudioClip clip)
    {
        if (clip == null)
            return 0f;

        float requested = Mathf.Max(0f, musicAutoLoopEndTrimSeconds);
        float maxByLength = Mathf.Max(0f, clip.length * 0.15f);
        float leaveAtLeast = Mathf.Min(clip.length * 0.5f, 2f);
        float maxTrim = Mathf.Max(0f, clip.length - leaveAtLeast);
        return Mathf.Min(requested, maxByLength, maxTrim);
    }

    private float GetMusicLoopStart(AudioClip clip)
    {
        if (clip == null)
            return 0f;

        ResolveMusicLoopWindow(clip, out float loopStart, out _);
        return loopStart;
    }

    // --- Generic one-shots ---

    public void PlaySfx(AudioClip clip, float volumeScale = 1f)
    {
        EnsureAudioSources();
        if (clip == null || sfxSource == null)
            return;

        sfxSource.pitch = 1f;
        sfxSource.PlayOneShot(clip, Mathf.Clamp01(masterVolume * sfxVolume * volumeScale));
    }

    public void PlaySfxPitched(AudioClip clip, float pitch, float volumeScale = 1f)
    {
        EnsureAudioSources();
        if (clip == null || pitchedSfxSource == null)
            return;

        pitchedSfxSource.pitch = Mathf.Clamp(pitch, 0.25f, 3f);
        pitchedSfxSource.PlayOneShot(clip, Mathf.Clamp01(masterVolume * sfxVolume * volumeScale));
    }

    // --- Named gameplay helpers ---

    public void StartChargeLoop() => StartChargeLoop(ChargeLoopId.Kit);

    public void StartChargeLoop(ChargeLoopId id)
    {
        EnsureAudioSources();
        ResolveChargeLoop(id, out AudioClip clip, out float volume, out float pitch);
        chargeLoopRefCount++;
        activeChargeLoopVolume = volume;

        if (clip == null || chargeLoopSource == null)
            return;

        if (chargeLoopSource.isPlaying && chargeLoopSource.clip == clip)
            return;

        chargeLoopSource.clip = clip;
        chargeLoopSource.loop = true;
        chargeLoopSource.pitch = pitch;
        chargeLoopSource.volume = masterVolume * sfxVolume * volume;
        chargeLoopSource.Play();
    }

    public void StopChargeLoop()
    {
        chargeLoopRefCount = Mathf.Max(0, chargeLoopRefCount - 1);
        if (chargeLoopRefCount > 0)
            return;

        if (chargeLoopSource != null && chargeLoopSource.isPlaying)
            chargeLoopSource.Stop();
    }

    public void StopChargeLoopImmediate()
    {
        chargeLoopRefCount = 0;
        if (chargeLoopSource != null && chargeLoopSource.isPlaying)
            chargeLoopSource.Stop();
    }

    private void ResolveChargeLoop(ChargeLoopId id, out AudioClip clip, out float volume, out float pitch)
    {
        switch (id)
        {
            case ChargeLoopId.Count:
                clip = countChargeLoop != null ? countChargeLoop : chargePurrLoop;
                volume = countChargeLoop != null ? countChargeVolume : chargePurrVolume;
                pitch = countChargeLoop != null ? countChargePitch : chargePurrPitch;
                break;
            case ChargeLoopId.Malice:
                clip = maliceChargeLoop != null ? maliceChargeLoop : chargePurrLoop;
                volume = maliceChargeLoop != null ? maliceChargeVolume : chargePurrVolume;
                pitch = maliceChargeLoop != null ? maliceChargePitch : chargePurrPitch;
                break;
            case ChargeLoopId.BossKit:
                clip = bossKitChargeLoop != null ? bossKitChargeLoop : chargePurrLoop;
                volume = bossKitChargeLoop != null ? bossKitChargeVolume : chargePurrVolume;
                pitch = bossKitChargeLoop != null ? bossKitChargePitch : chargePurrPitch;
                break;
            case ChargeLoopId.BossMalice:
                clip = bossMaliceChargeLoop != null ? bossMaliceChargeLoop : chargePurrLoop;
                volume = bossMaliceChargeLoop != null ? bossMaliceChargeVolume : chargePurrVolume;
                pitch = bossMaliceChargeLoop != null ? bossMaliceChargePitch : chargePurrPitch;
                break;
            case ChargeLoopId.ChaoticTanker:
                clip = chaoticTankerChargeLoop != null ? chaoticTankerChargeLoop : chargePurrLoop;
                volume = chaoticTankerChargeLoop != null ? chaoticTankerChargeVolume : chargePurrVolume;
                pitch = chaoticTankerChargeLoop != null ? chaoticTankerChargePitch : chargePurrPitch;
                break;
            default:
                clip = kitChargeLoop != null ? kitChargeLoop : chargePurrLoop;
                volume = kitChargeLoop != null ? kitChargeVolume : chargePurrVolume;
                pitch = kitChargeLoop != null ? kitChargePitch : chargePurrPitch;
                break;
        }
    }

    public void PlaySfxPitchedFromTime(AudioClip clip, float pitch, float startTime, float volumeScale = 1f)
    {
        EnsureAudioSources();
        if (clip == null || pitchedSfxSource == null)
            return;

        pitchedSfxSource.Stop();
        pitchedSfxSource.clip = clip;
        pitchedSfxSource.pitch = Mathf.Clamp(pitch, 0.25f, 3f);
        pitchedSfxSource.volume = Mathf.Clamp01(masterVolume * sfxVolume * volumeScale);

        float maxStart = Mathf.Max(0f, clip.length - 0.02f);
        pitchedSfxSource.time = Mathf.Clamp(startTime, 0f, maxStart);
        pitchedSfxSource.Play();
    }

    public void PlayKitFire(ProjectileShotType shotType = ProjectileShotType.Small)
    {
        PlaySfx(kitMeow, kitMeowVolume);

        AudioClip laser = shotType == ProjectileShotType.Big
            ? kitWeirdLaser
            : (kitSoftLaser != null ? kitSoftLaser : kitWeirdLaser);
        PlaySfx(laser, kitLaserVolume);
    }

    public void PlayBossKitFire(ProjectileShotType shotType = ProjectileShotType.Small)
    {
        AudioClip meow = bossKitMeow != null ? bossKitMeow : kitMeow;
        PlaySfx(meow, bossKitMeow != null ? bossKitMeowVolume : kitMeowVolume);

        AudioClip soft = bossKitSoftLaser != null ? bossKitSoftLaser : kitSoftLaser;
        AudioClip weird = bossKitWeirdLaser != null ? bossKitWeirdLaser : kitWeirdLaser;
        AudioClip laser = shotType == ProjectileShotType.Big
            ? weird
            : (soft != null ? soft : weird);
        PlaySfx(laser, bossKitSoftLaser != null || bossKitWeirdLaser != null ? bossKitLaserVolume : kitLaserVolume);
    }

    /// <summary>Impact when a boss is damaged.</summary>
    public void PlayBossHit()
    {
        AudioClip clip = bossHitSfx != null ? bossHitSfx : crystalHitSfx;
        if (clip != null)
            PlaySfx(clip, gameplaySfxVolume);
        else
            PlayProjectileHit(ProjectileShotType.Medium);
    }

    public void PlayLaserBotHit() => PlayEnemyHit(laserBotHitSfx);
    public void PlayLaserBotShot()
    {
        AudioClip clip = laserBotShotSfx != null ? laserBotShotSfx : kitSoftLaser;
        PlaySfx(clip, clip == kitSoftLaser ? kitLaserVolume : gameplaySfxVolume);
    }
    public void PlayLaserBotDeath() => PlayEnemyDeath(laserBotDeathSfx);

    public void PlayBlockerBotHit() => PlayEnemyHit(blockerBotHitSfx);
    public void PlayBlockerBotDash()
    {
        AudioClip clip = blockerBotDashSfx != null ? blockerBotDashSfx : dashSfx;
        PlaySfx(clip, gameplaySfxVolume);
    }
    public void PlayBlockerBotDeath() => PlayEnemyDeath(blockerBotDeathSfx);

    public void PlayScrapNitHit() => PlayEnemyHit(scrapNitHitSfx);
    public void PlayScrapNitDeath() => PlayEnemyDeath(scrapNitDeathSfx);

    public void PlayCrankyClankyHit() => PlayEnemyHit(crankyClankyHitSfx);
    public void PlayCrankyClankyDeath() => PlayEnemyDeath(crankyClankyDeathSfx);

    public void PlayChaoticTankerHit() => PlayEnemyHit(chaoticTankerHitSfx);
    public void PlayChaoticTankerVolt()
    {
        AudioClip clip = chaoticTankerVoltSfx != null ? chaoticTankerVoltSfx : kitWeirdLaser;
        PlaySfx(clip, clip == kitWeirdLaser ? kitLaserVolume : gameplaySfxVolume);
    }
    public void PlayChaoticTankerDeath() => PlayEnemyDeath(chaoticTankerDeathSfx);

    private void PlayEnemyHit(AudioClip dedicated)
    {
        AudioClip clip = dedicated != null ? dedicated : (bossHitSfx != null ? bossHitSfx : crystalHitSfx);
        if (clip != null)
            PlaySfx(clip, gameplaySfxVolume);
        else
            PlayProjectileHit(ProjectileShotType.Medium);
    }

    private void PlayEnemyDeath(AudioClip dedicated)
    {
        AudioClip clip = dedicated != null ? dedicated : enemyExplosionSfx;
        PlaySfx(clip, gameplaySfxVolume);
    }

    /// <summary>
    /// Machine Gun pellet SFX: meow + soft laser on every call.
    /// Round-robin pools hard-cap concurrent voices (no skip gate, no unbounded PlayOneShot spam).
    /// </summary>
    public void PlayKitFireRapid()
    {
        EnsureAudioSources();
        EnsureKitRapidVoicePools();

        float meowScale = Mathf.Clamp01(masterVolume * sfxVolume * kitRapidFireMeowVolume);
        float laserScale = Mathf.Clamp01(masterVolume * sfxVolume * kitRapidFireLaserVolume);

        if (kitMeow != null && kitRapidMeowPool != null && kitRapidMeowPool.Length > 0)
        {
            AudioSource meowSrc = kitRapidMeowPool[kitRapidMeowPoolIndex];
            kitRapidMeowPoolIndex = (kitRapidMeowPoolIndex + 1) % kitRapidMeowPool.Length;
            if (meowSrc != null)
            {
                meowSrc.Stop();
                meowSrc.pitch = 1f;
                meowSrc.volume = 1f;
                meowSrc.PlayOneShot(kitMeow, meowScale);
            }
        }

        AudioClip laser = kitSoftLaser != null ? kitSoftLaser : kitWeirdLaser;
        if (laser != null && kitRapidLaserPool != null && kitRapidLaserPool.Length > 0)
        {
            AudioSource laserSrc = kitRapidLaserPool[kitRapidLaserPoolIndex];
            kitRapidLaserPoolIndex = (kitRapidLaserPoolIndex + 1) % kitRapidLaserPool.Length;
            if (laserSrc != null)
            {
                laserSrc.Stop();
                laserSrc.pitch = 1f;
                laserSrc.volume = 1f;
                laserSrc.PlayOneShot(laser, laserScale);
            }
        }
    }

    public void PlayBossKitFireRapid()
    {
        EnsureAudioSources();
        EnsureBossKitRapidVoicePools();

        float meowScale = Mathf.Clamp01(masterVolume * sfxVolume *
            (bossKitMeow != null ? bossKitRapidFireMeowVolume : kitRapidFireMeowVolume));
        float laserScale = Mathf.Clamp01(masterVolume * sfxVolume *
            (bossKitSoftLaser != null || bossKitWeirdLaser != null ? bossKitRapidFireLaserVolume : kitRapidFireLaserVolume));

        AudioClip meow = bossKitMeow != null ? bossKitMeow : kitMeow;
        if (meow != null && bossKitRapidMeowPool != null && bossKitRapidMeowPool.Length > 0)
        {
            AudioSource meowSrc = bossKitRapidMeowPool[bossKitRapidMeowPoolIndex];
            bossKitRapidMeowPoolIndex = (bossKitRapidMeowPoolIndex + 1) % bossKitRapidMeowPool.Length;
            if (meowSrc != null)
            {
                meowSrc.Stop();
                meowSrc.pitch = 1f;
                meowSrc.volume = 1f;
                meowSrc.PlayOneShot(meow, meowScale);
            }
        }

        AudioClip soft = bossKitSoftLaser != null ? bossKitSoftLaser : kitSoftLaser;
        AudioClip laser = soft != null ? soft : (bossKitWeirdLaser != null ? bossKitWeirdLaser : kitWeirdLaser);
        if (laser != null && bossKitRapidLaserPool != null && bossKitRapidLaserPool.Length > 0)
        {
            AudioSource laserSrc = bossKitRapidLaserPool[bossKitRapidLaserPoolIndex];
            bossKitRapidLaserPoolIndex = (bossKitRapidLaserPoolIndex + 1) % bossKitRapidLaserPool.Length;
            if (laserSrc != null)
            {
                laserSrc.Stop();
                laserSrc.pitch = 1f;
                laserSrc.volume = 1f;
                laserSrc.PlayOneShot(laser, laserScale);
            }
        }
    }

    public void PlayCountFire(ProjectileShotType shotType = ProjectileShotType.Small)
    {
        AudioClip meow = countMeow != null ? countMeow : kitMeow;
        PlaySfx(meow, countMeowVolume > 0f ? countMeowVolume : kitMeowVolume);

        AudioClip soft = countSoftLaser != null ? countSoftLaser : kitSoftLaser;
        AudioClip big = countWeirdLaser != null ? countWeirdLaser : kitWeirdLaser;
        AudioClip laser = shotType == ProjectileShotType.Big
            ? big
            : (soft != null ? soft : big);
        float laserVol = countLaserVolume > 0f ? countLaserVolume : kitLaserVolume;
        PlaySfx(laser, laserVol);
    }

    /// <summary>Count Machine Gun — separate clip slots; slower cadence is handled in gameplay.</summary>
    public void PlayCountFireRapid()
    {
        EnsureAudioSources();
        EnsureCountRapidVoicePools();

        float meowScale = Mathf.Clamp01(masterVolume * sfxVolume * (countRapidFireMeowVolume > 0f ? countRapidFireMeowVolume : kitRapidFireMeowVolume));
        float laserScale = Mathf.Clamp01(masterVolume * sfxVolume * (countRapidFireLaserVolume > 0f ? countRapidFireLaserVolume : kitRapidFireLaserVolume));

        AudioClip meow = countMeow != null ? countMeow : kitMeow;
        if (meow != null && countRapidMeowPool != null && countRapidMeowPool.Length > 0)
        {
            AudioSource meowSrc = countRapidMeowPool[countRapidMeowPoolIndex];
            countRapidMeowPoolIndex = (countRapidMeowPoolIndex + 1) % countRapidMeowPool.Length;
            if (meowSrc != null)
            {
                meowSrc.Stop();
                meowSrc.pitch = 1f;
                meowSrc.volume = 1f;
                meowSrc.PlayOneShot(meow, meowScale);
            }
        }

        AudioClip soft = countSoftLaser != null ? countSoftLaser : kitSoftLaser;
        AudioClip laser = soft != null ? soft : (countWeirdLaser != null ? countWeirdLaser : kitWeirdLaser);
        if (laser != null && countRapidLaserPool != null && countRapidLaserPool.Length > 0)
        {
            AudioSource laserSrc = countRapidLaserPool[countRapidLaserPoolIndex];
            countRapidLaserPoolIndex = (countRapidLaserPoolIndex + 1) % countRapidLaserPool.Length;
            if (laserSrc != null)
            {
                laserSrc.Stop();
                laserSrc.pitch = 1f;
                laserSrc.volume = 1f;
                laserSrc.PlayOneShot(laser, laserScale);
            }
        }
    }

    public void PlayMaliceSlashLight() => PlaySfx(slashLight, slashLightVolume);

    public void PlayMaliceSlashHeavy() => PlaySfx(slashHeavy, slashHeavyVolume);

    public void PlayBossMaliceSlashLight()
    {
        AudioClip clip = bossMaliceSlashLight != null ? bossMaliceSlashLight : slashLight;
        PlaySfx(clip, bossMaliceSlashLight != null ? bossMaliceSlashLightVolume : slashLightVolume);
    }

    public void PlayBossMaliceSlashHeavy()
    {
        AudioClip clip = bossMaliceSlashHeavy != null ? bossMaliceSlashHeavy : slashHeavy;
        PlaySfx(clip, bossMaliceSlashHeavy != null ? bossMaliceSlashHeavyVolume : slashHeavyVolume);
    }

    /// <summary>
    /// Harlie slash / air slash. Uses a voice pool so fast combos overlap instead of cutting short.
    /// </summary>
    public void PlayHarlieSwordSwing()
    {
        EnsureAudioSources();
        EnsureHarlieSwingPool();
        if (harlieSwordSwing == null || harlieSwingPool == null || harlieSwingPool.Length == 0)
            return;

        AudioSource src = harlieSwingPool[harlieSwingPoolIndex];
        harlieSwingPoolIndex = (harlieSwingPoolIndex + 1) % harlieSwingPool.Length;
        if (src == null)
            return;

        src.Stop();
        src.clip = harlieSwordSwing;
        src.pitch = 1f;
        src.volume = Mathf.Clamp01(masterVolume * sfxVolume * harlieSwingVolume);
        src.Play();
    }

    /// <summary>No-op during gameplay — swings are allowed to overlap and finish naturally.</summary>
    public void StopHarlieSwordSwing()
    {
    }

    public void StopHarlieSwordSwingImmediate()
    {
        EnsureHarlieSwingPool();
        if (harlieSwingPool == null)
            return;

        for (int i = 0; i < harlieSwingPool.Length; i++)
        {
            AudioSource src = harlieSwingPool[i];
            if (src != null && src.isPlaying)
                src.Stop();
        }
    }

    public void PlayHarlieBigSwordClash() => PlaySfx(harlieBigSwordClash, harlieClashVolume);

    public void StartHarlieChargeKickLoop()
    {
        EnsureAudioSources();
        harlieChargeKickLoopRefCount++;
        if (harlieChargeKickLoop == null || harlieChargeKickSource == null)
            return;

        if (harlieChargeKickSource.isPlaying && harlieChargeKickSource.clip == harlieChargeKickLoop)
            return;

        harlieChargeKickSource.clip = harlieChargeKickLoop;
        harlieChargeKickSource.loop = true;
        harlieChargeKickSource.pitch = harlieChargeKickPitch;
        harlieChargeKickSource.volume = masterVolume * sfxVolume * harlieChargeKickVolume;
        harlieChargeKickSource.Play();
    }

    public void StopHarlieChargeKickLoop()
    {
        harlieChargeKickLoopRefCount = Mathf.Max(0, harlieChargeKickLoopRefCount - 1);
        if (harlieChargeKickLoopRefCount > 0)
            return;

        if (harlieChargeKickSource != null && harlieChargeKickSource.isPlaying)
            harlieChargeKickSource.Stop();
    }

    public void StopHarlieChargeKickLoopImmediate()
    {
        harlieChargeKickLoopRefCount = 0;
        if (harlieChargeKickSource != null && harlieChargeKickSource.isPlaying)
            harlieChargeKickSource.Stop();
    }

    public void PlayMaliceGrappleExtend(float animationSpeed = 1f)
    {
        if (grappleExtendSfx != null)
        {
            PlaySfxPitched(grappleExtendSfx, grappleBasePitch * Mathf.Max(0.01f, animationSpeed), grappleVolume);
            return;
        }

        EnsureGrappleSpikes();
        PlayGrappleSlice(grappleHook, cachedExtendSpike, animationSpeed, grappleVolume, grappleBasePitch);
    }

    public void PlayMaliceGrappleReel(float animationSpeed = 1f)
    {
        if (grappleReelSfx != null)
        {
            PlaySfxPitched(grappleReelSfx, grappleBasePitch * Mathf.Max(0.01f, animationSpeed), grappleVolume);
            return;
        }

        EnsureGrappleSpikes();
        PlayGrappleSlice(grappleHook, cachedReelSpike, animationSpeed, grappleVolume, grappleBasePitch);
    }

    public void PlayBossMaliceGrappleExtend(float animationSpeed = 1f)
    {
        AudioClip dedicated = bossMaliceGrappleExtendSfx != null ? bossMaliceGrappleExtendSfx : grappleExtendSfx;
        if (dedicated != null)
        {
            float pitchBase = bossMaliceGrappleExtendSfx != null ? bossMaliceGrappleBasePitch : grappleBasePitch;
            float vol = bossMaliceGrappleExtendSfx != null ? bossMaliceGrappleVolume : grappleVolume;
            PlaySfxPitched(dedicated, pitchBase * Mathf.Max(0.01f, animationSpeed), vol);
            return;
        }

        AudioClip hook = bossMaliceGrappleHook != null ? bossMaliceGrappleHook : grappleHook;
        EnsureBossGrappleSpikes(hook);
        float sliceVol = bossMaliceGrappleHook != null ? bossMaliceGrappleVolume : grappleVolume;
        float slicePitch = bossMaliceGrappleHook != null ? bossMaliceGrappleBasePitch : grappleBasePitch;
        PlayGrappleSlice(hook, cachedBossExtendSpike, animationSpeed, sliceVol, slicePitch);
    }

    public void PlayBossMaliceGrappleReel(float animationSpeed = 1f)
    {
        AudioClip dedicated = bossMaliceGrappleReelSfx != null ? bossMaliceGrappleReelSfx : grappleReelSfx;
        if (dedicated != null)
        {
            float pitchBase = bossMaliceGrappleReelSfx != null ? bossMaliceGrappleBasePitch : grappleBasePitch;
            float vol = bossMaliceGrappleReelSfx != null ? bossMaliceGrappleVolume : grappleVolume;
            PlaySfxPitched(dedicated, pitchBase * Mathf.Max(0.01f, animationSpeed), vol);
            return;
        }

        AudioClip hook = bossMaliceGrappleHook != null ? bossMaliceGrappleHook : grappleHook;
        EnsureBossGrappleSpikes(hook);
        float sliceVol = bossMaliceGrappleHook != null ? bossMaliceGrappleVolume : grappleVolume;
        float slicePitch = bossMaliceGrappleHook != null ? bossMaliceGrappleBasePitch : grappleBasePitch;
        PlayGrappleSlice(hook, cachedBossReelSpike, animationSpeed, sliceVol, slicePitch);
    }

    public void StopMaliceGrapple()
    {
        if (grappleSource != null && grappleSource.isPlaying)
            grappleSource.Stop();
    }

    public void PlayMaliceGrapple(float animationSpeed = 1f)
    {
        PlayMaliceGrappleExtend(animationSpeed);
    }

    private void PlayGrappleSlice(AudioClip clip, float startTime, float animationSpeed, float volumeScale, float basePitch)
    {
        EnsureAudioSources();
        if (clip == null || grappleSource == null)
            return;

        float pitch = basePitch * Mathf.Max(0.01f, animationSpeed);
        grappleSource.Stop();
        grappleSource.clip = clip;
        grappleSource.pitch = Mathf.Clamp(pitch, 0.25f, 3f);
        grappleSource.volume = Mathf.Clamp01(masterVolume * sfxVolume * volumeScale);

        float maxStart = Mathf.Max(0f, clip.length - 0.02f);
        // Play() resets time — seek after so we actually skip the silent lead-in.
        grappleSource.Play();
        grappleSource.time = Mathf.Clamp(startTime, 0f, maxStart);
    }

    private void EnsureGrappleSpikes()
    {
        if (grappleSpikesCached)
            return;

        cachedExtendSpike = grappleExtendSpikeTime > 0.001f ? grappleExtendSpikeTime : 0.713f;
        cachedReelSpike = grappleReelSpikeTime > 0.001f ? grappleReelSpikeTime : 1.038f;

        if (grappleHook != null &&
            (grappleExtendSpikeTime <= 0.001f || grappleReelSpikeTime <= 0.001f) &&
            DetectGrappleSpikes(grappleHook, out float autoExtend, out float autoReel))
        {
            if (grappleExtendSpikeTime <= 0.001f)
                cachedExtendSpike = autoExtend;
            if (grappleReelSpikeTime <= 0.001f)
                cachedReelSpike = autoReel;
        }

        grappleSpikesCached = true;
    }

    private void EnsureBossGrappleSpikes(AudioClip hook)
    {
        if (bossGrappleSpikesCached)
            return;

        if (hook == grappleHook)
        {
            EnsureGrappleSpikes();
            cachedBossExtendSpike = cachedExtendSpike;
            cachedBossReelSpike = cachedReelSpike;
            bossGrappleSpikesCached = true;
            return;
        }

        cachedBossExtendSpike = 0.713f;
        cachedBossReelSpike = 1.038f;
        if (hook != null && DetectGrappleSpikes(hook, out float autoExtend, out float autoReel))
        {
            cachedBossExtendSpike = autoExtend;
            cachedBossReelSpike = autoReel;
        }

        bossGrappleSpikesCached = true;
    }

    /// <summary>
    /// Finds the first loud spike (extend) and the next spike after a dip (reel-in).
    /// </summary>
    private static bool DetectGrappleSpikes(AudioClip clip, out float extendTime, out float reelTime)
    {
        extendTime = 0.713f;
        reelTime = 1.038f;
        if (clip == null)
            return false;

        int samples = clip.samples;
        int channels = Mathf.Max(1, clip.channels);
        if (samples <= 16)
            return false;

        float[] data = new float[samples * channels];
        if (!clip.GetData(data, 0))
            return false;

        int window = Mathf.Max(32, clip.frequency / 200);
        int windows = Mathf.Max(1, samples / window);
        float[] rms = new float[windows];
        float peak = 0.0001f;

        for (int w = 0; w < windows; w++)
        {
            double sum = 0d;
            int start = w * window;
            int count = 0;
            for (int i = 0; i < window && start + i < samples; i++)
            {
                float v = 0f;
                int sampleIndex = start + i;
                for (int c = 0; c < channels; c++)
                    v += data[sampleIndex * channels + c];
                v /= channels;
                sum += v * v;
                count++;
            }

            rms[w] = count > 0 ? Mathf.Sqrt((float)(sum / count)) : 0f;
            if (rms[w] > peak)
                peak = rms[w];
        }

        float thresh = peak * 0.18f;
        int first = -1;
        for (int w = 0; w < rms.Length; w++)
        {
            if (rms[w] >= thresh)
            {
                first = w;
                break;
            }
        }

        if (first < 0)
            return false;

        extendTime = (first * window) / (float)clip.frequency;

        float quiet = peak * 0.08f;
        bool sawQuiet = false;
        int second = -1;
        int after = first + Mathf.Max(2, clip.frequency / window / 20);
        for (int w = after; w < rms.Length; w++)
        {
            if (!sawQuiet && rms[w] < quiet)
                sawQuiet = true;
            else if (sawQuiet && rms[w] >= thresh)
            {
                second = w;
                break;
            }
        }

        reelTime = second >= 0
            ? (second * window) / (float)clip.frequency
            : Mathf.Min(clip.length - 0.05f, extendTime + 0.32f);

        return true;
    }

    public void PlayUiConfirmOrBack() => PlayUiConfirm();

    public void PlayUiConfirm()
    {
        AudioClip clip = uiConfirmSfx != null ? uiConfirmSfx : uiSoftLaser;
        PlaySfx(clip, uiVolume);
    }

    public void PlayUiBack()
    {
        AudioClip clip = uiBackSfx != null ? uiBackSfx : uiSoftLaser;
        PlaySfx(clip, uiVolume);
    }

    /// <summary>Start the looping dialogue typewriter beep (Serious Business text-box style).</summary>
    public void BeginDialogueTyping()
    {
        EnsureAudioSources();
        if (dialogueTypingLoop == null || dialogueTypingSource == null)
            return;

        if (dialogueTypingSource.isPlaying && dialogueTypingSource.clip == dialogueTypingLoop)
            return;

        dialogueTypingSource.clip = dialogueTypingLoop;
        dialogueTypingSource.loop = true;
        dialogueTypingSource.pitch = 1f;
        dialogueTypingSource.volume = masterVolume * sfxVolume * dialogueTypingVolume;
        dialogueTypingSource.Play();
    }

    /// <summary>Stop the looping dialogue typewriter beep.</summary>
    public void EndDialogueTyping()
    {
        if (dialogueTypingSource != null && dialogueTypingSource.isPlaying)
            dialogueTypingSource.Stop();
    }

    public void PlayJump() => PlaySfx(jumpSfx, gameplaySfxVolume);

    public void PlayCountJump()
    {
        AudioClip clip = countJumpSfx != null ? countJumpSfx : jumpSfx;
        PlaySfx(clip, gameplaySfxVolume);
    }

    public void PlayDash() => PlaySfx(dashSfx, gameplaySfxVolume);

    public void PlayCollectItem() => PlaySfx(collectItemSfx, gameplaySfxVolume);

    public void PlayItemDrop() => PlaySfx(itemDropSfx, gameplaySfxVolume);

    public void PlayItemHitGround() => PlaySfx(itemHitGroundSfx, gameplaySfxVolume);

    public void PlayClockworkTic() => PlaySfx(clockworkTicSfx != null ? clockworkTicSfx : countMeow, clockworkVolume);

    public void PlayClockworkToc()
    {
        if (clockworkTocSfx == null)
        {
            PlaySfxPitched(clockworkTicSfx != null ? clockworkTicSfx : countMeow, 0.7f, clockworkVolume);
            return;
        }

        if (clockworkTocSource == null)
        {
            clockworkTocSource = gameObject.AddComponent<AudioSource>();
            clockworkTocSource.playOnAwake = false;
            clockworkTocSource.loop = false;
            clockworkTocSource.spatialBlend = 0f;
            clockworkTocSource.priority = 100;
        }

        clockworkTocSource.Stop();
        clockworkTocSource.clip = clockworkTocSfx;
        clockworkTocSource.volume = Mathf.Clamp01(masterVolume * sfxVolume * clockworkVolume);
        clockworkTocSource.time = Mathf.Clamp(clockworkTocStartTime, 0f, Mathf.Max(0f, clockworkTocSfx.length - 0.02f));

        double now = AudioSettings.dspTime;
        clockworkTocSource.PlayScheduled(now);
        clockworkTocSource.SetScheduledEndTime(now + Mathf.Max(0.05f, clockworkTocMaxSeconds));
    }

    public void PlayCrystalHit() => PlaySfx(crystalHitSfx, gameplaySfxVolume);

    public void PlayCrystalShatter() => PlaySfx(crystalShatterSfx, gameplaySfxVolume);

    public void PlayEnemyExplosion() => PlaySfx(enemyExplosionSfx, gameplaySfxVolume);

    public void PlayCrankyClankyHop() => PlaySfx(crankyClankyHopSfx, gameplaySfxVolume);

    /// <summary>Alias for <see cref="PlayEnemyExplosion"/>.</summary>
    public void PlayExplosion() => PlayEnemyExplosion();

    public void PlayGameOver() => PlaySfx(gameOverSfx, gameplaySfxVolume);

    public void PlayGameRestart() => PlaySfx(gameRestartSfx, gameplaySfxVolume);

    public void PlayRobotWarning() => PlaySfx(robotWarningSfx, gameplaySfxVolume);

    public void PlayProjectileHit(ProjectileShotType shotType)
    {
        EnsureAudioSources();
        EnsureProjectileHitPool();

        AudioClip clip = projectileHitSmall;
        if (shotType == ProjectileShotType.Medium)
            clip = projectileHitMedium != null ? projectileHitMedium : projectileHitSmall;
        else if (shotType == ProjectileShotType.Big)
            clip = projectileHitBig != null ? projectileHitBig : projectileHitMedium;

        PlayVoicePoolOneShot(
            projectileHitPool,
            ref projectileHitPoolIndex,
            clip,
            projectileHitVolume);
    }

    private void PlayVoicePoolOneShot(
        AudioSource[] pool,
        ref int poolIndex,
        AudioClip clip,
        float volumeScale)
    {
        if (clip == null || pool == null || pool.Length == 0)
            return;

        AudioSource src = pool[poolIndex];
        poolIndex = (poolIndex + 1) % pool.Length;
        if (src == null)
            return;

        src.Stop();
        src.pitch = 1f;
        src.volume = 1f;
        src.PlayOneShot(clip, Mathf.Clamp01(masterVolume * sfxVolume * volumeScale));
    }

    public void SetMasterVolume(float value)
    {
        masterVolume = Mathf.Clamp01(value);
        ApplyVolumes();
        RefreshActiveMusicVolume();
    }

    public void SetMusicVolume(float value)
    {
        musicVolume = Mathf.Clamp01(value);
        RefreshActiveMusicVolume();
    }

    private void RefreshActiveMusicVolume()
    {
        if (activeMusicSource != null && currentMusic != null)
            activeMusicSource.volume = masterVolume * musicVolume * Mathf.Clamp01(currentMusicTrackVolume);
    }

    public void SetSfxVolume(float value)
    {
        sfxVolume = Mathf.Clamp01(value);
        ApplyVolumes();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        masterVolume = Mathf.Clamp01(masterVolume);
        musicVolume = Mathf.Clamp01(musicVolume);
        sfxVolume = Mathf.Clamp01(sfxVolume);
        if (Application.isPlaying)
            ApplyVolumes();
    }
#endif
}
