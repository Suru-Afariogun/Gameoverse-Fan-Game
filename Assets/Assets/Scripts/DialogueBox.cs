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

    private bool[] pageCopyFlags = Array.Empty<bool>();
    private Color defaultSpeakerNameColor = Color.white;
    private bool defaultSpeakerNameColorCached;

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
        ShowPage(0);
    }

    public void Close()
    {
        if (!isOpen)
            return;

        isOpen = false;
        StopTypingRoutine();
        EndTypingSound();

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
    }
#endif
}
