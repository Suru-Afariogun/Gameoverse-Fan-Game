using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Central audio hub. Create a GameObject named SoundManager, attach this, assign clips.
/// Survives scene loads. Volumes / pitches are Inspector-adjustable.
/// </summary>
public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance { get; private set; }

    [Header("Master Volumes")]
    [SerializeField] [Range(0f, 1f)] private float masterVolume = 1f;
    [SerializeField] [Range(0f, 1f)] private float musicVolume = 0.7f;
    [SerializeField] [Range(0f, 1f)] private float sfxVolume = 1f;

    [Header("Music Clips")]
    [Tooltip("Lofi — Start Screen + HomeTown (loop).")]
    [SerializeField] private AudioClip lofiLoopMusic;
    [Tooltip("Boss Fight Mode (loop).")]
    [SerializeField] private AudioClip bossFightMusic;
    [SerializeField] [Range(0f, 1f)] private float lofiMusicVolume = 1f;
    [SerializeField] [Range(0f, 1f)] private float bossMusicVolume = 1f;

    [Header("Charge Loop (any character charging)")]
    [Tooltip("Cat purr — loops while charging.")]
    [SerializeField] private AudioClip chargePurrLoop;
    [SerializeField] [Range(0f, 1f)] private float chargePurrVolume = 1f;
    [SerializeField] [Range(0.5f, 2f)] private float chargePurrPitch = 1f;

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

    [Header("Malice Slash")]
    [Tooltip("Slash 1 / 2 / Air Slash.")]
    [SerializeField] private AudioClip slashLight;
    [Tooltip("Slash 3 (retro slash).")]
    [SerializeField] private AudioClip slashHeavy;
    [SerializeField] [Range(0f, 1f)] private float slashLightVolume = 1f;
    [SerializeField] [Range(0f, 1f)] private float slashHeavyVolume = 1f;

    [Header("Malice Grapple")]
    [Tooltip("Grapple Arm — extend + reel slices of this clip.")]
    [SerializeField] private AudioClip grappleHook;
    [SerializeField] [Range(0f, 1f)] private float grappleVolume = 1f;
    [SerializeField] [Range(0.5f, 2f)] private float grappleBasePitch = 1f;
    [Tooltip("If 0, auto-detects the first loud spike (extend) in the clip.")]
    [SerializeField] private float grappleExtendSpikeTime = 0f;
    [Tooltip("If 0, auto-detects the second loud spike (reel-in) after the first.")]
    [SerializeField] private float grappleReelSpikeTime = 0f;
    [Tooltip("How long the extend slice may play before we wait for reel-in.")]
    [SerializeField] private float grappleExtendMaxDuration = 0.28f;

    [Header("UI (Kaboodle / Pause)")]
    [Tooltip("Confirm choice or go back.")]
    [SerializeField] private AudioClip uiSoftLaser;
    [SerializeField] [Range(0f, 1f)] private float uiVolume = 1f;

    private AudioSource musicSource;
    private AudioSource sfxSource;
    private AudioSource pitchedSfxSource;
    private AudioSource chargeLoopSource;
    private AudioSource grappleSource;
    private AudioSource[] kitRapidMeowPool;
    private AudioSource[] kitRapidLaserPool;
    private int kitRapidMeowPoolIndex;
    private int kitRapidLaserPoolIndex;
    private AudioClip currentMusic;
    private int chargeLoopRefCount;
    private bool grappleSpikesCached;
    private float cachedExtendSpike;
    private float cachedReelSpike;

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

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void EnsureAudioSources()
    {
        if (musicSource == null)
        {
            musicSource = gameObject.AddComponent<AudioSource>();
            musicSource.playOnAwake = false;
            musicSource.loop = true;
            musicSource.spatialBlend = 0f;
            // Highest priority so SFX spam cannot virtualize / mute music.
            musicSource.priority = 0;
        }

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

        EnsureKitRapidVoicePools();

        // Keep priorities even if sources already existed from an older session.
        musicSource.priority = 0;
        if (sfxSource != null) sfxSource.priority = 128;
        if (pitchedSfxSource != null) pitchedSfxSource.priority = 128;
        if (grappleSource != null) grappleSource.priority = 64;
    }

    private void EnsureKitRapidVoicePools()
    {
        int size = Mathf.Clamp(kitRapidVoicePoolSize, 2, 6);
        kitRapidMeowPool = BuildOrResizeVoicePool(kitRapidMeowPool, size, 40);
        kitRapidLaserPool = BuildOrResizeVoicePool(kitRapidLaserPool, size, 56);
        kitRapidMeowPoolIndex %= kitRapidMeowPool.Length;
        kitRapidLaserPoolIndex %= kitRapidLaserPool.Length;
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
            chargeLoopSource.volume = masterVolume * sfxVolume * chargePurrVolume;
        if (grappleSource != null)
            grappleSource.volume = masterVolume * sfxVolume * grappleVolume;
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        string n = scene.name ?? string.Empty;

        if (n.IndexOf("Boss Fight", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            n.IndexOf("BossFight", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            PlayMusic(bossFightMusic, bossMusicVolume);
            return;
        }

        // Start Screen + HomeTown share the lofi loop.
        if (n.IndexOf("Start", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            n.IndexOf("HomeTown", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            n.IndexOf("Home Town", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            PlayMusic(lofiLoopMusic, lofiMusicVolume);
        }
    }

    // --- Music ---

    public void PlayMusic(AudioClip clip, float trackVolumeScale = 1f, bool restartIfSame = false)
    {
        EnsureAudioSources();
        if (clip == null)
            return;

        if (!restartIfSame && currentMusic == clip && musicSource.isPlaying)
        {
            musicSource.volume = masterVolume * musicVolume * Mathf.Clamp01(trackVolumeScale);
            return;
        }

        currentMusic = clip;
        musicSource.clip = clip;
        musicSource.loop = true;
        musicSource.volume = masterVolume * musicVolume * Mathf.Clamp01(trackVolumeScale);
        musicSource.Play();
    }

    public void StopMusic()
    {
        if (musicSource != null)
            musicSource.Stop();
        currentMusic = null;
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

    public void StartChargeLoop()
    {
        EnsureAudioSources();
        chargeLoopRefCount++;
        if (chargePurrLoop == null || chargeLoopSource == null)
            return;

        if (chargeLoopSource.isPlaying && chargeLoopSource.clip == chargePurrLoop)
            return;

        chargeLoopSource.clip = chargePurrLoop;
        chargeLoopSource.loop = true;
        chargeLoopSource.pitch = chargePurrPitch;
        chargeLoopSource.volume = masterVolume * sfxVolume * chargePurrVolume;
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

    public void PlayMaliceSlashLight() => PlaySfx(slashLight, slashLightVolume);

    public void PlayMaliceSlashHeavy() => PlaySfx(slashHeavy, slashHeavyVolume);

    public void PlayMaliceGrappleExtend(float animationSpeed = 1f)
    {
        EnsureGrappleSpikes();
        PlayGrappleSlice(cachedExtendSpike, animationSpeed);
    }

    public void PlayMaliceGrappleReel(float animationSpeed = 1f)
    {
        EnsureGrappleSpikes();
        PlayGrappleSlice(cachedReelSpike, animationSpeed);
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

    private void PlayGrappleSlice(float startTime, float animationSpeed)
    {
        EnsureAudioSources();
        if (grappleHook == null || grappleSource == null)
            return;

        float pitch = grappleBasePitch * Mathf.Max(0.01f, animationSpeed);
        grappleSource.Stop();
        grappleSource.clip = grappleHook;
        grappleSource.pitch = Mathf.Clamp(pitch, 0.25f, 3f);
        grappleSource.volume = Mathf.Clamp01(masterVolume * sfxVolume * grappleVolume);

        float maxStart = Mathf.Max(0f, grappleHook.length - 0.02f);
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

    public void PlayUiConfirmOrBack() => PlaySfx(uiSoftLaser, uiVolume);

    public void SetMasterVolume(float value)
    {
        masterVolume = Mathf.Clamp01(value);
        ApplyVolumes();
        if (musicSource != null && currentMusic != null)
            musicSource.volume = masterVolume * musicVolume;
    }

    public void SetMusicVolume(float value)
    {
        musicVolume = Mathf.Clamp01(value);
        if (musicSource != null && currentMusic != null)
            musicSource.volume = masterVolume * musicVolume;
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
