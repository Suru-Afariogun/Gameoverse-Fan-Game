using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// HomeTown shared dialogue UI (root object is named "Dialouge box" in the scene).
/// Types text character-by-character; Confirm skips or advances pages.
/// </summary>
public sealed class DialogueBox : MonoBehaviour
{
    public static DialogueBox Instance { get; private set; }

    [Header("UI (auto-finds by scene names if empty)")]
    [SerializeField] private GameObject dialogueRoot;
    [SerializeField] private TMP_Text nameOfSpeakerText;
    [SerializeField] private TMP_Text dialogueOfSpeakerText;

    [Header("Typing")]
    [SerializeField] private float charactersPerSecond = 42f;
    [Tooltip("After the last Confirm closes dialogue, keep dash blocked this long so Confirm does not also dash.")]
    [SerializeField] private float postDialogueDashLockSeconds = 0.45f;

    private InputActions controls;
    private Coroutine typingRoutine;
    private Coroutine dashReleaseRoutine;
    private string[] pages = Array.Empty<string>();
    private string[] pageSpeakers = Array.Empty<string>();
    private int pageIndex;
    private string currentFullPage = string.Empty;
    private bool isOpen;
    private bool isTyping;
    private PlayerController lockedPlayer;
    private Action onClosed;

    public bool IsOpen => isOpen;
    public bool IsTyping => isTyping;

    /// <summary>One spoken beat (speaker name + page text) for multi-speaker cutscenes.</summary>
    public readonly struct Line
    {
        public readonly string Speaker;
        public readonly string Text;
        public readonly bool IsCopySpeaker;

        public Line(string speaker, string text, bool isCopySpeaker = false)
        {
            Speaker = speaker ?? string.Empty;
            Text = text ?? string.Empty;
            IsCopySpeaker = isCopySpeaker;
        }
    }

    [Header("Copy Bot Speakers")]
    [Tooltip("Speaker-name color for Copy Bot / mirror doppelganger lines.")]
    [SerializeField] private Color copySpeakerNameColor = new Color(1f, 0.22f, 0.22f, 1f);

    [Header("Placement")]
    [Tooltip("World units per space when lifting the box above the player.")]
    [SerializeField] private float spaceSize = 1f;
    [Tooltip("If the box would cover the player, place it this many spaces above their head.")]
    [SerializeField] private float coverAvoidSpacesAboveHead = 3f;

    private bool[] pageCopyFlags = Array.Empty<bool>();
    private Color defaultSpeakerNameColor = Color.white;
    private bool defaultSpeakerNameColorCached;
    private RectTransform panelImageRect;
    private RectTransform nameRect;
    private RectTransform bodyRect;
    private Vector2 panelImageDefaultPos;
    private Vector2 nameDefaultPos;
    private Vector2 bodyDefaultPos;
    private bool placementDefaultsCached;
    private Coroutine placementRoutine;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        CacheUi();
        HideImmediate();
    }

    private void OnEnable()
    {
        if (controls == null)
            controls = GameInput.Create();

        controls.PlayerControls.Enable();
        controls.PlayerControls.Confirm.performed += OnConfirmPerformed;
    }

    private void OnDisable()
    {
        if (controls != null)
        {
            controls.PlayerControls.Confirm.performed -= OnConfirmPerformed;
            controls.PlayerControls.Disable();
        }

        EndTypingSound();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        EndTypingSound();
        StopPlacementRoutine();
        UnlockPlayer();

        if (controls != null)
        {
            controls.Dispose();
            controls = null;
        }
    }

    /// <summary>
    /// Opens the dialogue box for an NPC conversation.
    /// </summary>
    public void Open(string speakerName, string[] dialoguePages, PlayerController player, Action closedCallback = null)
    {
        if (dialoguePages == null || dialoguePages.Length == 0)
            return;

        string[] speakers = new string[dialoguePages.Length];
        for (int i = 0; i < speakers.Length; i++)
            speakers[i] = speakerName ?? string.Empty;

        OpenConversation(speakers, dialoguePages, player, closedCallback);
    }

    /// <summary>
    /// Opens a multi-speaker conversation (Powered Up–style banter). Confirm advances beats.
    /// </summary>
    public void OpenConversation(Line[] lines, PlayerController player, Action closedCallback = null)
    {
        if (lines == null || lines.Length == 0)
            return;

        string[] speakers = new string[lines.Length];
        string[] texts = new string[lines.Length];
        bool[] copyFlags = new bool[lines.Length];
        for (int i = 0; i < lines.Length; i++)
        {
            speakers[i] = lines[i].Speaker;
            texts[i] = lines[i].Text;
            copyFlags[i] = lines[i].IsCopySpeaker;
        }

        OpenConversation(speakers, texts, player, closedCallback, copyFlags);
    }

    public void OpenConversation(
        string[] speakers,
        string[] dialoguePages,
        PlayerController player,
        Action closedCallback = null,
        bool[] copySpeakerFlags = null)
    {
        if (dialoguePages == null || dialoguePages.Length == 0)
            return;

        CacheUi();
        CacheDefaultSpeakerNameColor();
        StopTypingRoutine();
        EndTypingSound();

        pages = dialoguePages;
        pageSpeakers = speakers != null && speakers.Length == dialoguePages.Length
            ? speakers
            : Array.Empty<string>();
        pageCopyFlags = copySpeakerFlags != null && copySpeakerFlags.Length == dialoguePages.Length
            ? copySpeakerFlags
            : Array.Empty<bool>();
        pageIndex = 0;
        onClosed = closedCallback;
        isOpen = true;

        ShowVisual(true);
        LockPlayer(player);
        BeginPlacementAdjust(player);
        ShowPage(0);
    }

    public void Close()
    {
        if (!isOpen)
            return;

        isOpen = false;
        StopTypingRoutine();
        EndTypingSound();
        StopPlacementRoutine();

        if (dialogueOfSpeakerText != null)
            dialogueOfSpeakerText.text = string.Empty;
        if (nameOfSpeakerText != null)
        {
            nameOfSpeakerText.text = string.Empty;
            if (defaultSpeakerNameColorCached)
                nameOfSpeakerText.color = defaultSpeakerNameColor;
        }

        pageSpeakers = Array.Empty<string>();
        pageCopyFlags = Array.Empty<bool>();
        RestoreDefaultPlacement();
        HideImmediate();
        UnlockPlayer();

        Action callback = onClosed;
        onClosed = null;
        callback?.Invoke();
    }

    private void OnConfirmPerformed(InputAction.CallbackContext context)
    {
        if (!context.performed || !isOpen)
            return;

        if (isTyping)
        {
            SkipTypingToEnd();
            return;
        }

        if (pageIndex < pages.Length - 1)
        {
            pageIndex++;
            ShowPage(pageIndex);
            return;
        }

        Close();
    }

    private void ShowPage(int index)
    {
        if (index < 0 || index >= pages.Length)
            return;

        if (nameOfSpeakerText != null)
        {
            string speaker = pageSpeakers != null && index < pageSpeakers.Length
                ? pageSpeakers[index]
                : string.Empty;
            nameOfSpeakerText.text = speaker ?? string.Empty;

            bool isCopy = pageCopyFlags != null && index < pageCopyFlags.Length && pageCopyFlags[index];
            nameOfSpeakerText.color = isCopy ? copySpeakerNameColor : defaultSpeakerNameColor;
        }

        currentFullPage = pages[index] ?? string.Empty;
        StopTypingRoutine();
        typingRoutine = StartCoroutine(TypePageRoutine(currentFullPage));
    }

    private void CacheDefaultSpeakerNameColor()
    {
        if (defaultSpeakerNameColorCached || nameOfSpeakerText == null)
            return;

        defaultSpeakerNameColor = nameOfSpeakerText.color;
        defaultSpeakerNameColorCached = true;
    }

    private IEnumerator TypePageRoutine(string message)
    {
        isTyping = true;
        if (dialogueOfSpeakerText != null)
            dialogueOfSpeakerText.text = string.Empty;

        float delay = 1f / Mathf.Max(1f, charactersPerSecond);
        BeginTypingSound();

        for (int i = 0; i < message.Length; i++)
        {
            if (dialogueOfSpeakerText != null)
                dialogueOfSpeakerText.text += message[i];

            yield return new WaitForSecondsRealtime(delay);
        }

        EndTypingSound();
        isTyping = false;
        typingRoutine = null;
    }

    private void SkipTypingToEnd()
    {
        StopTypingRoutine();
        EndTypingSound();
        isTyping = false;

        if (dialogueOfSpeakerText != null)
            dialogueOfSpeakerText.text = currentFullPage;
    }

    private void StopTypingRoutine()
    {
        if (typingRoutine == null)
            return;

        StopCoroutine(typingRoutine);
        typingRoutine = null;
        isTyping = false;
    }

    private void BeginTypingSound() => SoundManager.Instance?.BeginDialogueTyping();

    private void EndTypingSound() => SoundManager.Instance?.EndDialogueTyping();

    private void LockPlayer(PlayerController player)
    {
        if (dashReleaseRoutine != null)
        {
            StopCoroutine(dashReleaseRoutine);
            dashReleaseRoutine = null;
        }

        lockedPlayer = player;
        if (lockedPlayer == null)
            return;

        lockedPlayer.SetInputLocked(true);
        lockedPlayer.SetDashDisabled(true);
    }

    private void UnlockPlayer()
    {
        if (lockedPlayer == null)
            return;

        PlayerController player = lockedPlayer;
        lockedPlayer = null;

        player.SetInputLocked(false);
        player.SetDashDisabled(true);

        if (dashReleaseRoutine != null)
            StopCoroutine(dashReleaseRoutine);

        dashReleaseRoutine = StartCoroutine(ReleaseDashAfterDelay(player, postDialogueDashLockSeconds));
    }

    private IEnumerator ReleaseDashAfterDelay(PlayerController player, float delaySeconds)
    {
        float wait = Mathf.Max(0.05f, delaySeconds);
        yield return new WaitForSecondsRealtime(wait);

        if (player != null)
            player.SetDashDisabled(false);

        dashReleaseRoutine = null;
    }

    private void ShowVisual(bool visible)
    {
        CacheUi();
        if (dialogueRoot != null)
            dialogueRoot.SetActive(visible);
    }

    private void HideImmediate()
    {
        ShowVisual(false);
    }

    private void CacheUi()
    {
        // Keep this GameObject active so Confirm input still works; hide the UI canvas instead.
        if (dialogueRoot == null || dialogueRoot == gameObject)
        {
            Transform canvas = FindDeepChild(transform, "Canvas for Dialouge box");
            if (canvas != null)
                dialogueRoot = canvas.gameObject;
        }

        if (nameOfSpeakerText == null)
        {
            Transform t = FindDeepChild(transform, "Name of speaker");
            if (t != null)
                nameOfSpeakerText = t.GetComponent<TMP_Text>();
        }

        if (dialogueOfSpeakerText == null)
        {
            Transform t = FindDeepChild(transform, "Dialouge of Speaker");
            if (t == null)
                t = FindDeepChild(transform, "Dialogue of Speaker");
            if (t != null)
                dialogueOfSpeakerText = t.GetComponent<TMP_Text>();
        }

        CachePlacementTargets();
    }

    private void CachePlacementTargets()
    {
        if (panelImageRect == null)
        {
            Transform image = FindDeepChild(transform, "Image");
            if (image != null)
                panelImageRect = image as RectTransform;
        }

        if (nameRect == null && nameOfSpeakerText != null)
            nameRect = nameOfSpeakerText.rectTransform;

        if (bodyRect == null && dialogueOfSpeakerText != null)
            bodyRect = dialogueOfSpeakerText.rectTransform;

        if (placementDefaultsCached)
            return;

        if (panelImageRect != null)
            panelImageDefaultPos = panelImageRect.anchoredPosition;
        if (nameRect != null)
            nameDefaultPos = nameRect.anchoredPosition;
        if (bodyRect != null)
            bodyDefaultPos = bodyRect.anchoredPosition;

        placementDefaultsCached = panelImageRect != null || nameRect != null || bodyRect != null;
    }

    private void RestoreDefaultPlacement()
    {
        CachePlacementTargets();
        if (!placementDefaultsCached)
            return;

        if (panelImageRect != null)
            panelImageRect.anchoredPosition = panelImageDefaultPos;
        if (nameRect != null)
            nameRect.anchoredPosition = nameDefaultPos;
        if (bodyRect != null)
            bodyRect.anchoredPosition = bodyDefaultPos;
    }

    private void BeginPlacementAdjust(PlayerController player)
    {
        StopPlacementRoutine();
        RestoreDefaultPlacement();
        if (player == null || panelImageRect == null)
            return;

        placementRoutine = StartCoroutine(AdjustPlacementRoutine(player));
    }

    private void StopPlacementRoutine()
    {
        if (placementRoutine == null)
            return;

        StopCoroutine(placementRoutine);
        placementRoutine = null;
    }

    /// <summary>
    /// Screen-space check after the canvas scaler has applied. World-bounds checks fail on
    /// Screen Space - Camera dialogue UI (and on the first frame while canvas scale is still 0).
    /// </summary>
    private IEnumerator AdjustPlacementRoutine(PlayerController player)
    {
        // Wait until the activated canvas has a real scale / camera binding.
        for (int i = 0; i < 3; i++)
        {
            yield return null;
            if (!isOpen || player == null)
            {
                placementRoutine = null;
                yield break;
            }

            EnsureDialogueCanvasReady();
            Canvas.ForceUpdateCanvases();
            if (TryLiftPanelAbovePlayerIfCovering(player))
                break;
        }

        placementRoutine = null;
    }

    private void EnsureDialogueCanvasReady()
    {
        if (dialogueRoot == null)
            return;

        Canvas canvas = dialogueRoot.GetComponent<Canvas>();
        if (canvas == null)
            canvas = dialogueRoot.GetComponentInChildren<Canvas>(true);
        if (canvas == null)
            return;

        Camera main = Camera.main;
        if (main != null)
            HudCanvasCameraSync.BindCanvasToCamera(canvas, main);
    }

    private bool TryLiftPanelAbovePlayerIfCovering(PlayerController player)
    {
        RestoreDefaultPlacement();
        if (player == null || panelImageRect == null)
            return false;

        if (!TryGetPlayerWorldBounds(player, out Bounds playerBounds))
            return false;

        Camera cam = ResolveDialogueCamera(out bool isOverlay);
        if (!isOverlay && cam == null)
            return false;

        if (!TryGetScreenRect(panelImageRect, cam, out Rect panelScreen))
            return false;

        // Canvas still collapsed / not laid out.
        if (panelScreen.width < 2f || panelScreen.height < 2f)
            return false;

        Rect playerScreen = WorldBoundsToScreenRect(playerBounds, cam);
        if (playerScreen.width < 1f || playerScreen.height < 1f)
            return false;

        if (!panelScreen.Overlaps(playerScreen))
            return false;

        float headY = playerBounds.max.y;
        Vector3 aboveHeadWorld = new Vector3(
            playerBounds.center.x,
            headY + Mathf.Max(0f, coverAvoidSpacesAboveHead) * Mathf.Max(0.01f, spaceSize),
            player.transform.position.z);

        Vector3 aboveScreen = RectTransformUtility.WorldToScreenPoint(cam, aboveHeadWorld);
        if (!isOverlay && aboveScreen.z < 0f)
            return false;

        // Put the panel's bottom edge on that screen Y (lift only — never drop below default).
        float deltaScreenY = aboveScreen.y - panelScreen.yMin;
        if (deltaScreenY <= 1f)
            return true;

        if (!TryScreenDeltaToCanvasY(panelImageRect, cam, deltaScreenY, out float liftCanvasY))
            return false;

        if (liftCanvasY <= 0.5f)
            return false;

        ApplyVerticalCanvasOffset(liftCanvasY);
        return true;
    }

    private void ApplyVerticalCanvasOffset(float deltaY)
    {
        if (panelImageRect != null)
            panelImageRect.anchoredPosition = panelImageDefaultPos + new Vector2(0f, deltaY);
        if (nameRect != null)
            nameRect.anchoredPosition = nameDefaultPos + new Vector2(0f, deltaY);
        if (bodyRect != null)
            bodyRect.anchoredPosition = bodyDefaultPos + new Vector2(0f, deltaY);
    }

    private Camera ResolveDialogueCamera(out bool isOverlay)
    {
        isOverlay = false;
        if (panelImageRect != null)
        {
            Canvas canvas = panelImageRect.GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                {
                    isOverlay = true;
                    return null;
                }

                if (canvas.worldCamera != null)
                    return canvas.worldCamera;
            }
        }

        return Camera.main;
    }

    private static bool TryGetPlayerWorldBounds(PlayerController player, out Bounds bounds)
    {
        bounds = default;
        if (player == null)
            return false;

        Collider2D body = player.BodyCollider;
        if (body != null && body.enabled)
        {
            bounds = body.bounds;
            return true;
        }

        SpriteRenderer sr = player.GetComponentInChildren<SpriteRenderer>();
        if (sr != null)
        {
            bounds = sr.bounds;
            return true;
        }

        bounds = new Bounds(player.transform.position, Vector3.one);
        return true;
    }

    private static bool TryGetScreenRect(RectTransform rect, Camera cam, out Rect screenRect)
    {
        screenRect = default;
        if (rect == null)
            return false;

        Vector3[] corners = new Vector3[4];
        rect.GetWorldCorners(corners);

        Vector2 min = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
        Vector2 max = min;
        for (int i = 1; i < corners.Length; i++)
        {
            Vector2 p = RectTransformUtility.WorldToScreenPoint(cam, corners[i]);
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p);
        }

        screenRect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        return screenRect.width > 0f && screenRect.height > 0f;
    }

    private static Rect WorldBoundsToScreenRect(Bounds bounds, Camera cam)
    {
        Vector3[] points =
        {
            new Vector3(bounds.min.x, bounds.min.y, bounds.center.z),
            new Vector3(bounds.min.x, bounds.max.y, bounds.center.z),
            new Vector3(bounds.max.x, bounds.min.y, bounds.center.z),
            new Vector3(bounds.max.x, bounds.max.y, bounds.center.z)
        };

        Vector2 min = RectTransformUtility.WorldToScreenPoint(cam, points[0]);
        Vector2 max = min;
        for (int i = 1; i < points.Length; i++)
        {
            Vector2 p = RectTransformUtility.WorldToScreenPoint(cam, points[i]);
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p);
        }

        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private static bool TryScreenDeltaToCanvasY(
        RectTransform rect,
        Camera cam,
        float deltaScreenY,
        out float canvasDeltaY)
    {
        canvasDeltaY = 0f;
        if (rect == null || Mathf.Abs(deltaScreenY) < 0.01f)
            return false;

        RectTransform parent = rect.parent as RectTransform;
        if (parent == null)
            return false;

        Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, rect.position);
        Vector2 screenLifted = screen + new Vector2(0f, deltaScreenY);

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, cam, out Vector2 localFrom))
            return false;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenLifted, cam, out Vector2 localTo))
            return false;

        canvasDeltaY = localTo.y - localFrom.y;
        return !float.IsNaN(canvasDeltaY) && !float.IsInfinity(canvasDeltaY);
    }

    private static Transform FindDeepChild(Transform root, string objectName)
    {
        if (root == null)
            return null;

        if (string.Equals(root.name, objectName, StringComparison.OrdinalIgnoreCase))
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindDeepChild(root.GetChild(i), objectName);
            if (found != null)
                return found;
        }

        return null;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        charactersPerSecond = Mathf.Max(1f, charactersPerSecond);
        postDialogueDashLockSeconds = Mathf.Max(0.05f, postDialogueDashLockSeconds);
        spaceSize = Mathf.Max(0.01f, spaceSize);
        coverAvoidSpacesAboveHead = Mathf.Max(0f, coverAvoidSpacesAboveHead);
    }
#endif
}
