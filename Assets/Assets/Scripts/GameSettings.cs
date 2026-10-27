using System;
using UnityEngine;

/// <summary>
/// Persistent player settings (audio, display, difficulty, input rebinds) via PlayerPrefs.
/// Applied at boot and whenever Pause A Options changes a value.
/// </summary>
public static class GameSettings
{
    private const string PrefPrefix = "Gameoverse_Settings_";
    private const string PrefMusic = PrefPrefix + "MusicVolume";
    private const string PrefSfx = PrefPrefix + "SfxVolume";
    private const string PrefMusicMute = PrefPrefix + "MusicMute";
    private const string PrefSfxMute = PrefPrefix + "SfxMute";
    private const string PrefFullscreen = PrefPrefix + "Fullscreen";
    private const string PrefResWidth = PrefPrefix + "ResWidth";
    private const string PrefResHeight = PrefPrefix + "ResHeight";
    private const string PrefResRefresh = PrefPrefix + "ResRefresh";
    private const string PrefBindings = PrefPrefix + "Bindings";
    private const string PrefDifficulty = PrefPrefix + "Difficulty";

    /// <summary>Fires after difficulty is saved (mid-battle Options change).</summary>
    public static event Action OnDifficultyChanged;

    public static float MusicVolume { get; private set; } = 0.7f;
    public static float SfxVolume { get; private set; } = 1f;
    public static bool MusicMuted { get; private set; }
    public static bool SfxMuted { get; private set; }
    public static bool Fullscreen { get; private set; } = true;
    public static int ResolutionWidth { get; private set; }
    public static int ResolutionHeight { get; private set; }
    public static int ResolutionRefreshRate { get; private set; }
    public static string BindingOverridesJson { get; private set; } = string.Empty;
    public static GameDifficulty Difficulty { get; private set; } = GameDifficulty.Normal;

    public static bool SupportsResolutionList
    {
        get
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return false;
#else
            return true;
#endif
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void BootLoad()
    {
        Load();
        ApplyDisplay();
    }

    public static void Load()
    {
        MusicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(PrefMusic, 0.7f));
        SfxVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(PrefSfx, 1f));
        MusicMuted = PlayerPrefs.GetInt(PrefMusicMute, 0) != 0;
        SfxMuted = PlayerPrefs.GetInt(PrefSfxMute, 0) != 0;
        Fullscreen = PlayerPrefs.GetInt(PrefFullscreen, Screen.fullScreen ? 1 : 0) != 0;
        ResolutionWidth = PlayerPrefs.GetInt(PrefResWidth, Screen.width);
        ResolutionHeight = PlayerPrefs.GetInt(PrefResHeight, Screen.height);
        ResolutionRefreshRate = PlayerPrefs.GetInt(PrefResRefresh, 0);
        BindingOverridesJson = PlayerPrefs.GetString(PrefBindings, string.Empty);
        Difficulty = ClampDifficulty(PlayerPrefs.GetInt(PrefDifficulty, (int)GameDifficulty.Normal));
    }

    public static void Save()
    {
        PlayerPrefs.SetFloat(PrefMusic, MusicVolume);
        PlayerPrefs.SetFloat(PrefSfx, SfxVolume);
        PlayerPrefs.SetInt(PrefMusicMute, MusicMuted ? 1 : 0);
        PlayerPrefs.SetInt(PrefSfxMute, SfxMuted ? 1 : 0);
        PlayerPrefs.SetInt(PrefFullscreen, Fullscreen ? 1 : 0);
        PlayerPrefs.SetInt(PrefResWidth, ResolutionWidth);
        PlayerPrefs.SetInt(PrefResHeight, ResolutionHeight);
        PlayerPrefs.SetInt(PrefResRefresh, ResolutionRefreshRate);
        PlayerPrefs.SetString(PrefBindings, BindingOverridesJson ?? string.Empty);
        PlayerPrefs.SetInt(PrefDifficulty, (int)Difficulty);
        PlayerPrefs.Save();
    }

    public static void SetDifficulty(GameDifficulty difficulty)
    {
        GameDifficulty next = ClampDifficulty((int)difficulty);
        bool changed = Difficulty != next;
        Difficulty = next;
        if (changed)
            Save();

        // Always refresh so Balanced can retune to current player power, and mid-battle swaps apply.
        DifficultyTuning.RefreshAllCombatants();
        if (changed)
            OnDifficultyChanged?.Invoke();
    }

    public static void CycleDifficulty(int direction)
    {
        int count = 4;
        int next = ((int)Difficulty + direction) % count;
        if (next < 0)
            next += count;
        SetDifficulty((GameDifficulty)next);
    }

    public static string DifficultyDisplayName(GameDifficulty difficulty) => difficulty switch
    {
        GameDifficulty.Easy => "Easy",
        GameDifficulty.Hard => "Hard",
        GameDifficulty.Balanced => "Balanced",
        _ => "Normal"
    };

    private static GameDifficulty ClampDifficulty(int value)
    {
        if (value < (int)GameDifficulty.Easy || value > (int)GameDifficulty.Balanced)
            return GameDifficulty.Normal;
        return (GameDifficulty)value;
    }

    public static void SetMusicVolume(float value)
    {
        MusicVolume = Mathf.Clamp01(value);
        Save();
        SoundManager.Instance?.ApplyGameSettingsAudio();
    }

    public static void SetSfxVolume(float value)
    {
        SfxVolume = Mathf.Clamp01(value);
        Save();
        SoundManager.Instance?.ApplyGameSettingsAudio();
    }

    public static void SetMusicMuted(bool muted)
    {
        MusicMuted = muted;
        Save();
        SoundManager.Instance?.ApplyGameSettingsAudio();
    }

    public static void SetSfxMuted(bool muted)
    {
        SfxMuted = muted;
        Save();
        SoundManager.Instance?.ApplyGameSettingsAudio();
    }

    public static void ToggleMusicMuted() => SetMusicMuted(!MusicMuted);

    public static void ToggleSfxMuted() => SetSfxMuted(!SfxMuted);

    public static void SetFullscreen(bool fullscreen)
    {
        Fullscreen = fullscreen;
        Save();
        ApplyDisplay();
    }

    public static void ToggleFullscreen() => SetFullscreen(!Fullscreen);

    public static void SetResolution(int width, int height, int refreshRate = 0)
    {
        if (!SupportsResolutionList)
            return;

        ResolutionWidth = Mathf.Max(1, width);
        ResolutionHeight = Mathf.Max(1, height);
        ResolutionRefreshRate = Mathf.Max(0, refreshRate);
        Save();
        ApplyDisplay();
    }

    public static void ApplyDisplay()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        Screen.fullScreen = Fullscreen;
        return;
#else
        if (SupportsResolutionList && ResolutionWidth > 0 && ResolutionHeight > 0)
        {
            FullScreenMode mode = Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            if (ResolutionRefreshRate > 0)
            {
                var rate = new RefreshRate { numerator = (uint)ResolutionRefreshRate, denominator = 1u };
                Screen.SetResolution(ResolutionWidth, ResolutionHeight, mode, rate);
            }
            else
            {
                Screen.SetResolution(ResolutionWidth, ResolutionHeight, mode);
            }
        }
        else
        {
            Screen.fullScreen = Fullscreen;
        }
#endif
    }

    public static void SetBindingOverridesJson(string json)
    {
        BindingOverridesJson = json ?? string.Empty;
        Save();
    }

    public static void ClearBindingOverrides()
    {
        BindingOverridesJson = string.Empty;
        Save();
    }

    public static Resolution[] GetSortedResolutions()
    {
        Resolution[] raw = Screen.resolutions;
        if (raw == null || raw.Length == 0)
            return Array.Empty<Resolution>();

        Resolution[] copy = (Resolution[])raw.Clone();
        Array.Sort(copy, (a, b) =>
        {
            int w = a.width.CompareTo(b.width);
            if (w != 0)
                return w;
            int h = a.height.CompareTo(b.height);
            if (h != 0)
                return h;
            return a.refreshRateRatio.value.CompareTo(b.refreshRateRatio.value);
        });

        // Deduplicate same width/height keeping highest refresh.
        int write = 0;
        for (int i = 0; i < copy.Length; i++)
        {
            if (write > 0 &&
                copy[write - 1].width == copy[i].width &&
                copy[write - 1].height == copy[i].height)
            {
                copy[write - 1] = copy[i];
                continue;
            }

            copy[write++] = copy[i];
        }

        if (write == copy.Length)
            return copy;

        Resolution[] trimmed = new Resolution[write];
        Array.Copy(copy, trimmed, write);
        return trimmed;
    }

    public static int FindClosestResolutionIndex(Resolution[] list)
    {
        if (list == null || list.Length == 0)
            return 0;

        int best = 0;
        int bestScore = int.MaxValue;
        for (int i = 0; i < list.Length; i++)
        {
            int score = Mathf.Abs(list[i].width - ResolutionWidth) +
                        Mathf.Abs(list[i].height - ResolutionHeight) * 2;
            if (score < bestScore)
            {
                bestScore = score;
                best = i;
            }
        }

        return best;
    }
}
