using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Mega Man X-style WARNING flash. Attach to your warning text / image object.
/// Flashes on/off and plays the robot warning sound every time it turns on.
/// Locks player + boss (and optionally camera) for the whole sequence — including
/// the first load before anyone has spawned.
/// </summary>
[DefaultExecutionOrder(-1000)]
public class WarningText : MonoBehaviour
{
    [Header("Display (auto-finds on this object if left empty)")]
    [SerializeField] private TextMeshProUGUI uiText;
    [SerializeField] private TextMeshPro worldText;
    [SerializeField] private Graphic uiGraphic;
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Flash (Mega Man X style)")]
    [SerializeField] private bool playOnStart = true;
    [SerializeField] private float startDelay = 0.15f;
    [Tooltip("How many times the warning turns ON (sound plays each time).")]
    [SerializeField] private int flashCount = 12;
    [SerializeField] private float flashOnSeconds = 0.12f;
    [SerializeField] private float flashOffSeconds = 0.12f;
    [SerializeField] private bool hideWhenFinished = true;
    [SerializeField] private bool disableGameObjectWhenFinished = false;

    [Header("Sound")]
    [Tooltip("Robot Warning sound for game.")]
    [SerializeField] private AudioClip robotWarningSound;
    [SerializeField] [Range(0f, 1f)] private float warningVolume = 1f;
    [SerializeField] private bool useSoundManagerIfAvailable = true;

    [Header("Gameplay Freeze (optional MMX feel)")]
    [SerializeField] private bool lockPlayerInputDuringWarning = true;
    [SerializeField] private bool pauseBossCombatDuringWarning = true;
    [SerializeField] private bool freezeCameraDuringWarning = true;

    /// <summary>True while any warning is holding gameplay (intro or replay).</summary>
    public static bool BlocksGameplay { get; private set; }

    public bool IsPlaying { get; private set; }
    public bool HasCompletedAtLeastOnce { get; private set; }
    public event System.Action OnWarningFinished;

    private AudioSource localSource;
    private Coroutine routine;
    private bool cameraFollowWasEnabled;
    private bool pendingPlayOnStart;
    private bool freezeCameraOverrideActive;
    private bool freezeCameraOverrideValue;

    private bool ShouldFreezeCamera =>
        freezeCameraOverrideActive ? freezeCameraOverrideValue : freezeCameraDuringWarning;

    private void Awake()
    {
        ResolveDisplay();
        EnsureWarningSoundAssigned();
        SetVisible(false);

        if (ShouldAutoPlayOnLoad())
        {
            pendingPlayOnStart = true;
            BlocksGameplay = true;
            IsPlaying = true;
            // First intro should restore camera follow afterward.
            cameraFollowWasEnabled = true;
            FreezeGameplay();
        }
    }

    private void Start()
    {
        if (ShouldAutoPlayOnLoad())
            Play();
    }

    private bool ShouldAutoPlayOnLoad()
    {
        return playOnStart && !HasCompletedAtLeastOnce;
    }

    private void LateUpdate()
    {
        // Re-apply every frame so late-spawned players/bosses stay locked.
        if (BlocksGameplay || IsPlaying || pendingPlayOnStart)
            FreezeGameplay();
    }

    private void OnDisable()
    {
        if (routine != null)
        {
            StopCoroutine(routine);
            routine = null;
        }

        if (IsPlaying || BlocksGameplay)
        {
            IsPlaying = false;
            pendingPlayOnStart = false;
            EndGameplayBlock();
        }
    }

    /// <summary>Disable auto-play on load; use before Start when triggering manually (Level one boss room).</summary>
    public void PrepareForManualTriggerOnly()
    {
        playOnStart = false;
        pendingPlayOnStart = false;
        HasCompletedAtLeastOnce = false;
        StopImmediate(hide: true);
    }

    /// <summary>Reset visuals/state so Play() can run again (boss-room replay after level intro).</summary>
    public void PrepareForReplay()
    {
        EnsurePlayable();

        if (routine != null)
        {
            StopCoroutine(routine);
            routine = null;
        }

        IsPlaying = false;
        pendingPlayOnStart = false;

        if (BlocksGameplay)
            EndGameplayBlock();

        SetVisible(false);
    }

    /// <summary>Start (or restart) the warning flash sequence.</summary>
    public void Play()
    {
        EnsurePlayable();

        if (!isActiveAndEnabled)
        {
            Debug.LogWarning("[WarningText] Play() skipped — object is inactive or disabled.", this);
            return;
        }

        if (routine != null)
            StopCoroutine(routine);

        pendingPlayOnStart = false;
        IsPlaying = true;
        BlocksGameplay = true;
        routine = StartCoroutine(FlashRoutine());
    }

    /// <summary>Play the warning and wait until the flash sequence finishes.</summary>
    public IEnumerator PlayAndWait(bool freezeCamera = true)
    {
        freezeCameraOverrideActive = true;
        freezeCameraOverrideValue = freezeCamera;
        try
        {
            PrepareForReplay();
            Play();
            if (routine == null)
                yield break;

            while (IsPlaying || BlocksGameplay)
                yield return null;
        }
        finally
        {
            freezeCameraOverrideActive = false;
        }
    }

    private void EnsurePlayable()
    {
        Transform node = transform;
        while (node != null)
        {
            if (!node.gameObject.activeSelf)
                node.gameObject.SetActive(true);
            node = node.parent;
        }

        Canvas canvas = GetComponentInParent<Canvas>(true);
        if (canvas != null)
        {
            if (!canvas.gameObject.activeSelf)
                canvas.gameObject.SetActive(true);

            Transform canvasTransform = canvas.transform;
            if (canvasTransform.localScale.sqrMagnitude < 0.001f)
                canvasTransform.localScale = Vector3.one;

            if (canvas.renderMode != RenderMode.WorldSpace && canvas.worldCamera == null)
            {
                Camera main = Camera.main;
                if (main != null)
                    canvas.worldCamera = main;
            }
        }

        enabled = true;
    }

    public static WarningText FindInScene(bool includeInactive = true)
    {
        return Object.FindFirstObjectByType<WarningText>(
            includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude);
    }

    public void StopImmediate(bool hide = true)
    {
        if (routine != null)
        {
            StopCoroutine(routine);
            routine = null;
        }

        bool wasBlocking = IsPlaying || BlocksGameplay;
        IsPlaying = false;
        pendingPlayOnStart = false;
        if (hide)
            SetVisible(false);

        if (wasBlocking)
            EndGameplayBlock();
    }

    private IEnumerator FlashRoutine()
    {
        IsPlaying = true;
        BlocksGameplay = true;
        pendingPlayOnStart = false;
        ResolveDisplay();
        SetVisible(false);
        FreezeGameplay();

        if (startDelay > 0f)
            yield return new WaitForSecondsRealtime(startDelay);

        int flashes = Mathf.Max(1, flashCount);
        for (int i = 0; i < flashes; i++)
        {
            SetVisible(true);
            PlayWarningSound();
            if (flashOnSeconds > 0f)
                yield return new WaitForSecondsRealtime(flashOnSeconds);
            else
                yield return null;

            SetVisible(false);
            if (i < flashes - 1)
            {
                if (flashOffSeconds > 0f)
                    yield return new WaitForSecondsRealtime(flashOffSeconds);
                else
                    yield return null;
            }
        }

        if (hideWhenFinished)
            SetVisible(false);

        HasCompletedAtLeastOnce = true;
        IsPlaying = false;
        routine = null;
        EndGameplayBlock();
        OnWarningFinished?.Invoke();

        if (disableGameObjectWhenFinished)
            gameObject.SetActive(false);
    }

    private void PlayWarningSound()
    {
        if (useSoundManagerIfAvailable && SoundManager.Instance != null)
        {
            SoundManager.Instance.PlayRobotWarning();
            return;
        }

        if (robotWarningSound == null)
            return;

        EnsureLocalSource();
        if (localSource == null)
            return;

        localSource.PlayOneShot(robotWarningSound, Mathf.Clamp01(warningVolume));
    }

    private void EnsureLocalSource()
    {
        if (localSource != null)
            return;

        localSource = GetComponent<AudioSource>();
        if (localSource == null)
            localSource = gameObject.AddComponent<AudioSource>();

        localSource.playOnAwake = false;
        localSource.loop = false;
        localSource.spatialBlend = 0f;
    }

    private void FreezeGameplay()
    {
        BlocksGameplay = true;

        if (lockPlayerInputDuringWarning)
        {
            PlayerController[] players = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
            for (int i = 0; i < players.Length; i++)
            {
                if (players[i] != null)
                    players[i].SetInputLocked(true);
            }
        }

        if (pauseBossCombatDuringWarning)
        {
            Boss[] bosses = FindObjectsByType<Boss>(FindObjectsSortMode.None);
            for (int i = 0; i < bosses.Length; i++)
            {
                if (bosses[i] != null)
                    bosses[i].SetCombatPaused(true);
            }
        }

        if (ShouldFreezeCamera && CameraFollow.Instance != null)
        {
            if (CameraFollow.Instance.FollowEnabled)
                cameraFollowWasEnabled = true;

            CameraFollow.Instance.SnapToTarget();
            CameraFollow.Instance.SetFollowEnabled(false);
        }
    }

    private void EndGameplayBlock()
    {
        BlocksGameplay = false;
        RestoreGameplay();
    }

    private void RestoreGameplay()
    {
        if (lockPlayerInputDuringWarning)
        {
            PlayerController[] players = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
            for (int i = 0; i < players.Length; i++)
            {
                if (players[i] != null)
                    players[i].SetInputLocked(false);
            }
        }

        if (pauseBossCombatDuringWarning)
        {
            Boss[] bosses = FindObjectsByType<Boss>(FindObjectsSortMode.None);
            for (int i = 0; i < bosses.Length; i++)
            {
                if (bosses[i] != null)
                    bosses[i].SetCombatPaused(false);
            }
        }

        // Only restore camera if this warning was freezing it.
        if (ShouldFreezeCamera && CameraFollow.Instance != null)
            CameraFollow.Instance.SetFollowEnabled(cameraFollowWasEnabled);
    }

    private void ResolveDisplay()
    {
        if (uiText == null)
            uiText = GetComponent<TextMeshProUGUI>();
        if (uiText == null)
            uiText = GetComponentInChildren<TextMeshProUGUI>(true);

        if (worldText == null)
            worldText = GetComponent<TextMeshPro>();
        if (worldText == null)
            worldText = GetComponentInChildren<TextMeshPro>(true);

        if (uiGraphic == null)
            uiGraphic = GetComponent<Graphic>();
        if (uiGraphic == null)
            uiGraphic = GetComponentInChildren<Graphic>(true);

        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>(true);

        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = GetComponentInChildren<CanvasGroup>(true);
    }

    private void EnsureWarningSoundAssigned()
    {
        if (robotWarningSound != null)
            return;

#if UNITY_EDITOR
        robotWarningSound = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(
            "Assets/Sounds/Robot Warning sound for game.wav");
#endif
    }

    private void SetVisible(bool visible)
    {
        if (canvasGroup != null)
        {
            canvasGroup.alpha = visible ? 1f : 0f;
            return;
        }

        if (uiText != null)
        {
            uiText.enabled = visible;
            Color c = uiText.color;
            c.a = visible ? 1f : 0f;
            uiText.color = c;
        }

        if (worldText != null)
        {
            worldText.enabled = visible;
            Color c = worldText.color;
            c.a = visible ? 1f : 0f;
            worldText.color = c;
        }

        if (uiGraphic != null && uiGraphic != uiText)
            uiGraphic.enabled = visible;

        if (spriteRenderer != null)
            spriteRenderer.enabled = visible;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        flashCount = Mathf.Max(1, flashCount);
        flashOnSeconds = Mathf.Max(0f, flashOnSeconds);
        flashOffSeconds = Mathf.Max(0f, flashOffSeconds);
        startDelay = Mathf.Max(0f, startDelay);
        warningVolume = Mathf.Clamp01(warningVolume);

        ResolveDisplay();
        EnsureWarningSoundAssigned();
    }

    private void Reset()
    {
        ResolveDisplay();
        EnsureWarningSoundAssigned();
    }
#endif
}
