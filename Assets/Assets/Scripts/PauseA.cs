using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Pause A system menu on Pause Canvas A (Resume / Options / Return to Home Town / Quit Game).
/// Options uses Kaboodle-style pages: hub → Audio / Display / Gameplay / Controls.
/// Open with Select (Tab / gamepad Select). Pause B keeps Start / Pause for inventory.
/// Navigation matches Kaboodle / Pause B: d-pad, stick, Confirm, Quit/Back.
/// </summary>
public class PauseA : MonoBehaviour
{
    public static PauseA Instance { get; private set; }

    private enum Page
    {
        Main = 0,
        OptionsHub = 1,
        Audio = 2,
        Display = 3,
        Gameplay = 4,
        Controls = 5
    }

    private enum OptionKind
    {
        OpenPage,
        Back,
        Resume,
        ReturnHome,
        Quit,
        MusicVolume,
        MuteMusic,
        SfxVolume,
        MuteSfx,
        Fullscreen,
        Resolution,
        Difficulty,
        RebindAction,
        ResetControls
    }

    private sealed class MenuEntry
    {
        public Button button;
        public OptionKind kind;
        public Page targetPage;
        public string actionName;
        public Func<string> label;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureExistsInScene()
    {
        if (Instance != null)
            return;

        GameObject canvas = FindPauseCanvasA();
        if (canvas == null)
            return;

        if (canvas.GetComponent<PauseA>() == null)
            canvas.AddComponent<PauseA>();
    }

    private static GameObject FindPauseCanvasA()
    {
        GameObject canvas = GameObject.Find("Pause Canvas A");
        if (canvas != null)
            return canvas;

        return GameObject.Find("Paue Canvas A");
    }

    [Header("Roots")]
    [Tooltip("Usually Pause A container. Hidden until the menu is open.")]
    [SerializeField] private GameObject menuRoot;
    [SerializeField] private GameObject pauseCanvasRoot;

    [Header("Buttons")]
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button returnHomeTownButton;
    [SerializeField] private Button quitGameButton;
    [SerializeField] private Button optionsButton;

    [Header("Scenes")]
    [SerializeField] private string homeTownSceneName = "HomeTown";

    [Header("Input")]
    [SerializeField] [Range(0.1f, 1f)] private float stickThreshold = 0.5f;
    [SerializeField] private float menuInputCooldown = 0.18f;
    [SerializeField] [Range(0.01f, 0.25f)] private float volumeStep = 0.05f;

    [Header("Selection Look")]
    [SerializeField] private Color normalButtonColor = new Color(0.907585f, 0.93962264f, 0.515906f, 1f);
    [SerializeField] private Color selectedButtonColor = new Color(0.89433956f, 0.7035845f, 0.038810864f, 1f);
    [SerializeField] private float selectedScaleBonus = 0.15f;
    [SerializeField] private Color outlineColor = new Color(1f, 0.95f, 0.35f, 1f);
    [SerializeField] private Vector2 outlineDistance = new Vector2(2.5f, 2.5f);

    [Header("Pause")]
    [SerializeField] private bool freezeTimeWhileOpen = true;
    [SerializeField] private bool lockPlayerWhileOpen = true;

    private InputActions controls;
    private bool isOpen;
    private int selectedIndex;
    private float nextMenuInputTime;
    private bool moveUpHeld;
    private bool moveDownHeld;
    private bool moveLeftHeld;
    private bool moveRightHeld;
    private float savedTimeScale = 1f;
    private float lastToggleUnscaledTime = -999f;
    private bool loadingScene;
    private Page currentPage = Page.Main;
    private bool pagesBuilt;
    private bool waitingForRebind;
    private InputActionRebindingExtensions.RebindingOperation rebindOperation;
    private string rebindingActionName;
    private Resolution[] resolutionList = Array.Empty<Resolution>();
    private int resolutionIndex;
    private Transform pauseBox;
    private readonly List<MenuEntry> activeEntries = new List<MenuEntry>(16);
    private readonly List<MenuEntry> mainEntries = new List<MenuEntry>(4);
    private readonly List<MenuEntry> optionsHubEntries = new List<MenuEntry>(4);
    private readonly List<MenuEntry> audioEntries = new List<MenuEntry>(6);
    private readonly List<MenuEntry> displayEntries = new List<MenuEntry>(4);
    private readonly List<MenuEntry> gameplayEntries = new List<MenuEntry>(4);
    private readonly List<MenuEntry> controlsEntries = new List<MenuEntry>(16);
    private readonly Dictionary<Button, Vector3> buttonBaseScales = new Dictionary<Button, Vector3>();
    private readonly List<GameObject> generatedRoots = new List<GameObject>(8);

    public bool IsOpen => isOpen;

    private void Awake()
    {
        Instance = this;
        controls = GameInput.Create();
        AutoBindIfNeeded();
        FixPauseCanvasSetup();
        EnsureOptionsUi();
        FixButtonDrawOrder();
        EnsureEventSystem();

        if (menuRoot != null)
            menuRoot.SetActive(false);

        isOpen = false;
        ShowPage(Page.Main, refreshHighlight: false);
    }

    private void OnEnable()
    {
        if (controls == null)
            controls = GameInput.Create();

        controls.PlayerControls.Enable();
        controls.PlayerControls.Select.performed += OnSelectPerformed;
        controls.PlayerControls.Confirm.performed += OnConfirmPerformed;
        controls.PlayerControls.QuitBack.performed += OnQuitBackPerformed;
        controls.PlayerControls.Up.performed += OnUpPerformed;
        controls.PlayerControls.Down.performed += OnDownPerformed;
        controls.PlayerControls.Left.performed += OnLeftPerformed;
        controls.PlayerControls.Right.performed += OnRightPerformed;
        controls.PlayerControls.Movement.performed += OnMovementPerformed;
        controls.PlayerControls.Movement.canceled += OnMovementCanceled;
    }

    private void OnDisable()
    {
        CancelRebind(silent: true);

        if (controls != null)
        {
            controls.PlayerControls.Select.performed -= OnSelectPerformed;
            controls.PlayerControls.Confirm.performed -= OnConfirmPerformed;
            controls.PlayerControls.QuitBack.performed -= OnQuitBackPerformed;
            controls.PlayerControls.Up.performed -= OnUpPerformed;
            controls.PlayerControls.Down.performed -= OnDownPerformed;
            controls.PlayerControls.Left.performed -= OnLeftPerformed;
            controls.PlayerControls.Right.performed -= OnRightPerformed;
            controls.PlayerControls.Movement.performed -= OnMovementPerformed;
            controls.PlayerControls.Movement.canceled -= OnMovementCanceled;
            controls.PlayerControls.Disable();
        }

        if (isOpen)
            ForceClose();
    }

    private void OnDestroy()
    {
        CancelRebind(silent: true);

        if (Instance == this)
            Instance = null;

        UnwireAllGeneratedClicks();

        if (controls != null)
        {
            controls.Dispose();
            controls = null;
        }
    }

    private void FixPauseCanvasSetup()
    {
        Canvas canvas = GetComponent<Canvas>();
        if (canvas == null)
            canvas = GetComponentInParent<Canvas>();
        if (canvas == null)
            return;

        // Same root cause as player/boss HUD: Screen Space - Camera tied to Main Camera
        // Size / clip planes. Rebind so pause stays visible when ortho size changes.
        Camera main = Camera.main;
        if (main != null)
            HudCanvasCameraSync.BindCanvasToCamera(canvas, main);
        else if (canvas.renderMode == RenderMode.ScreenSpaceCamera && canvas.worldCamera == null)
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        if (canvas.sortingOrder < 100)
            canvas.sortingOrder = 100;

        Canvas.ForceUpdateCanvases();
    }

    private void FixButtonDrawOrder()
    {
        for (int i = 0; i < mainEntries.Count; i++)
            BringButtonInFront(mainEntries[i].button);
        for (int i = 0; i < optionsHubEntries.Count; i++)
            BringButtonInFront(optionsHubEntries[i].button);
        for (int i = 0; i < audioEntries.Count; i++)
            BringButtonInFront(audioEntries[i].button);
        for (int i = 0; i < displayEntries.Count; i++)
            BringButtonInFront(displayEntries[i].button);
        for (int i = 0; i < gameplayEntries.Count; i++)
            BringButtonInFront(gameplayEntries[i].button);
        for (int i = 0; i < controlsEntries.Count; i++)
            BringButtonInFront(controlsEntries[i].button);
    }

    private static void BringButtonInFront(Button button)
    {
        if (button == null)
            return;

        RectTransform rt = button.transform as RectTransform;
        if (rt != null)
        {
            Vector3 pos = rt.localPosition;
            pos.z = 0f;
            rt.localPosition = pos;
        }

        button.transform.SetAsLastSibling();

        Navigation nav = button.navigation;
        nav.mode = Navigation.Mode.None;
        button.navigation = nav;

        SpriteRenderer[] sprites = button.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < sprites.Length; i++)
        {
            if (sprites[i] != null)
                sprites[i].sortingOrder = Mathf.Max(sprites[i].sortingOrder, 120);
        }
    }

    private void AutoBindIfNeeded()
    {
        if (pauseCanvasRoot == null)
            pauseCanvasRoot = gameObject;

        if (menuRoot == null)
        {
            Transform found = FindDeep(transform, "Pause A container");
            if (found != null)
                menuRoot = found.gameObject;
        }

        if (resumeButton == null)
        {
            Transform t = FindDeep(transform, "ResumeButton");
            if (t != null)
                resumeButton = t.GetComponent<Button>();
        }

        if (returnHomeTownButton == null)
        {
            Transform t = FindDeep(transform, "Return to Home Town Button");
            if (t != null)
                returnHomeTownButton = t.GetComponent<Button>();
        }

        if (quitGameButton == null)
        {
            Transform t = FindDeep(transform, "Quit Game Button");
            if (t != null)
                quitGameButton = t.GetComponent<Button>();
        }

        if (optionsButton == null)
        {
            Transform t = FindDeep(transform, "Options Button");
            if (t != null)
                optionsButton = t.GetComponent<Button>();
        }

        pauseBox = FindDeep(transform, "Pause A box");
    }

    private void EnsureOptionsUi()
    {
        if (pagesBuilt || resumeButton == null || pauseBox == null)
            return;

        pagesBuilt = true;
        LayoutMainButtons();
        BuildMainEntries();
        BuildOptionsHubPage();
        BuildAudioPage();
        BuildDisplayPage();
        BuildGameplayPage();
        BuildControlsPage();
        RefreshAllDynamicLabels();
    }

    private void LayoutMainButtons()
    {
        if (optionsButton == null)
            optionsButton = CloneButton(resumeButton, "Options Button", "Options");

        PlaceButton(resumeButton, 1.15f);
        PlaceButton(optionsButton, 0.38f);
        PlaceButton(returnHomeTownButton, -0.39f);
        PlaceButton(quitGameButton, -1.16f);
    }

    private void BuildMainEntries()
    {
        mainEntries.Clear();
        AddEntry(mainEntries, resumeButton, OptionKind.Resume, () => "Resume");
        AddEntry(mainEntries, optionsButton, OptionKind.OpenPage, () => "Options", Page.OptionsHub);
        AddEntry(mainEntries, returnHomeTownButton, OptionKind.ReturnHome, () => "Return to Home Town");
        AddEntry(mainEntries, quitGameButton, OptionKind.Quit, () => "Quit Game");
        WireEntries(mainEntries);
    }

    private void BuildOptionsHubPage()
    {
        optionsHubEntries.Clear();
        Button audio = MakePageButton("Options Audio Button", "Audio", 1.15f, 36f);
        Button display = MakePageButton("Options Display Button", "Display", 0.45f, 36f);
        Button gameplay = MakePageButton("Options Gameplay Button", "Gameplay", -0.25f, 36f);
        Button controlsBtn = MakePageButton("Options Controls Button", "Controls", -0.95f, 36f);
        Button back = MakePageButton("Options Hub Back Button", "Back", -1.55f, 36f);

        AddEntry(optionsHubEntries, audio, OptionKind.OpenPage, () => "Audio", Page.Audio);
        AddEntry(optionsHubEntries, display, OptionKind.OpenPage, () => "Display", Page.Display);
        AddEntry(optionsHubEntries, gameplay, OptionKind.OpenPage, () => "Gameplay", Page.Gameplay);
        AddEntry(optionsHubEntries, controlsBtn, OptionKind.OpenPage, () => "Controls", Page.Controls);
        AddEntry(optionsHubEntries, back, OptionKind.Back, () => "Back", Page.Main);
        WireEntries(optionsHubEntries);
        SetEntriesActive(optionsHubEntries, false);
    }

    private void BuildGameplayPage()
    {
        gameplayEntries.Clear();
        Button difficulty = MakePageButton("Difficulty Button", "Difficulty", 0.35f, 34f);
        Button back = MakePageButton("Gameplay Back Button", "Back", -0.55f, 34f);

        AddEntry(gameplayEntries, difficulty, OptionKind.Difficulty, DifficultyLabel);
        AddEntry(gameplayEntries, back, OptionKind.Back, () => "Back", Page.OptionsHub);
        WireEntries(gameplayEntries);
        SetEntriesActive(gameplayEntries, false);
    }

    private void BuildAudioPage()
    {
        audioEntries.Clear();
        float y = 1.2f;
        float step = 0.48f;
        Button musicVol = MakePageButton("Music Volume Button", "Music Volume", y, 32f);
        y -= step;
        Button musicMute = MakePageButton("Music Mute Button", "Mute Music", y, 32f);
        y -= step;
        Button sfxVol = MakePageButton("SFX Volume Button", "SFX Volume", y, 32f);
        y -= step;
        Button sfxMute = MakePageButton("SFX Mute Button", "Mute SFX", y, 32f);
        y -= step;
        Button back = MakePageButton("Audio Back Button", "Back", y, 32f);

        AddEntry(audioEntries, musicVol, OptionKind.MusicVolume, MusicVolumeLabel);
        AddEntry(audioEntries, musicMute, OptionKind.MuteMusic, MuteMusicLabel);
        AddEntry(audioEntries, sfxVol, OptionKind.SfxVolume, SfxVolumeLabel);
        AddEntry(audioEntries, sfxMute, OptionKind.MuteSfx, MuteSfxLabel);
        AddEntry(audioEntries, back, OptionKind.Back, () => "Back", Page.OptionsHub);
        WireEntries(audioEntries);
        SetEntriesActive(audioEntries, false);
    }

    private void BuildDisplayPage()
    {
        displayEntries.Clear();
        resolutionList = GameSettings.SupportsResolutionList
            ? GameSettings.GetSortedResolutions()
            : Array.Empty<Resolution>();
        resolutionIndex = GameSettings.FindClosestResolutionIndex(resolutionList);

        float y = 0.7f;
        Button fullscreen = MakePageButton("Fullscreen Button", "Fullscreen", y, 34f);
        y -= 0.7f;
        Button resolution = null;
        if (GameSettings.SupportsResolutionList)
        {
            resolution = MakePageButton("Resolution Button", "Resolution", y, 30f);
            y -= 0.7f;
        }

        Button back = MakePageButton("Display Back Button", "Back", y, 34f);

        AddEntry(displayEntries, fullscreen, OptionKind.Fullscreen, FullscreenLabel);
        if (resolution != null)
            AddEntry(displayEntries, resolution, OptionKind.Resolution, ResolutionLabel);
        AddEntry(displayEntries, back, OptionKind.Back, () => "Back", Page.OptionsHub);
        WireEntries(displayEntries);
        SetEntriesActive(displayEntries, false);
    }

    private void BuildControlsPage()
    {
        controlsEntries.Clear();
        float top = 1.35f;
        float bottom = -1.35f;
        int rows = GameInput.RemappableActionNames.Length + 2;
        float step = (top - bottom) / Mathf.Max(1, rows - 1);

        for (int i = 0; i < GameInput.RemappableActionNames.Length; i++)
        {
            string actionName = GameInput.RemappableActionNames[i];
            float y = top - step * i;
            Button button = MakePageButton("Rebind " + actionName, actionName, y, 26f);
            string captured = actionName;
            AddEntry(controlsEntries, button, OptionKind.RebindAction,
                () => RebindLabel(captured), Page.Main, captured);
        }

        Button reset = MakePageButton("Reset Controls Button", "Reset Controls",
            top - step * GameInput.RemappableActionNames.Length, 26f);
        Button back = MakePageButton("Controls Back Button", "Back", bottom, 26f);
        AddEntry(controlsEntries, reset, OptionKind.ResetControls, () => "Reset Controls");
        AddEntry(controlsEntries, back, OptionKind.Back, () => "Back", Page.OptionsHub);
        WireEntries(controlsEntries);
        SetEntriesActive(controlsEntries, false);
    }

    private Button MakePageButton(string objectName, string label, float localY, float fontSize)
    {
        Button button = CloneButton(resumeButton, objectName, label);
        PlaceButton(button, localY);
        SetButtonFontSize(button, fontSize);
        button.gameObject.SetActive(false);
        generatedRoots.Add(button.gameObject);
        return button;
    }

    private Button CloneButton(Button template, string objectName, string label)
    {
        GameObject clone = Instantiate(template.gameObject, template.transform.parent);
        clone.name = objectName;
        clone.SetActive(true);

        Button button = clone.GetComponent<Button>();
        button.onClick = new Button.ButtonClickedEvent();

        Outline outline = clone.GetComponent<Outline>();
        if (outline != null)
            outline.enabled = false;

        SetButtonLabel(button, label);
        buttonBaseScales[button] = button.transform.localScale;
        BringButtonInFront(button);
        return button;
    }

    private static void PlaceButton(Button button, float localY)
    {
        if (button == null)
            return;

        RectTransform rt = button.transform as RectTransform;
        if (rt == null)
            return;

        Vector2 pos = rt.anchoredPosition;
        pos.y = localY;
        rt.anchoredPosition = pos;
    }

    private static void SetButtonFontSize(Button button, float size)
    {
        TMP_Text tmp = button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;
        if (tmp == null)
            return;

        tmp.fontSize = size;
        tmp.enableAutoSizing = size <= 28f;
        if (tmp.enableAutoSizing)
        {
            tmp.fontSizeMin = 18f;
            tmp.fontSizeMax = size;
        }
    }

    private static void SetButtonLabel(Button button, string text)
    {
        if (button == null)
            return;

        TMP_Text tmp = button.GetComponentInChildren<TMP_Text>(true);
        if (tmp != null)
        {
            tmp.text = text ?? string.Empty;
            return;
        }

        Text legacy = button.GetComponentInChildren<Text>(true);
        if (legacy != null)
            legacy.text = text ?? string.Empty;
    }

    private void AddEntry(List<MenuEntry> list, Button button, OptionKind kind, Func<string> label,
        Page targetPage = Page.Main, string actionName = null)
    {
        if (button == null || list == null)
            return;

        list.Add(new MenuEntry
        {
            button = button,
            kind = kind,
            targetPage = targetPage,
            actionName = actionName,
            label = label
        });

        if (!buttonBaseScales.ContainsKey(button))
            buttonBaseScales[button] = button.transform.localScale;
    }

    private void WireEntries(List<MenuEntry> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            MenuEntry entry = list[i];
            if (entry.button == null)
                continue;

            entry.button.onClick.RemoveAllListeners();
            MenuEntry captured = entry;
            entry.button.onClick.AddListener(() => ActivateEntry(captured));
        }
    }

    private void UnwireAllGeneratedClicks()
    {
        UnwireList(mainEntries);
        UnwireList(optionsHubEntries);
        UnwireList(audioEntries);
        UnwireList(displayEntries);
        UnwireList(gameplayEntries);
        UnwireList(controlsEntries);
    }

    private static void UnwireList(List<MenuEntry> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].button != null)
                list[i].button.onClick.RemoveAllListeners();
        }
    }

    private static void SetEntriesActive(List<MenuEntry> list, bool active)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].button != null)
                list[i].button.gameObject.SetActive(active);
        }
    }

    private void ShowPage(Page page, bool refreshHighlight = true)
    {
        CancelRebind(silent: true);
        currentPage = page;

        SetEntriesActive(mainEntries, page == Page.Main);
        SetEntriesActive(optionsHubEntries, page == Page.OptionsHub);
        SetEntriesActive(audioEntries, page == Page.Audio);
        SetEntriesActive(displayEntries, page == Page.Display);
        SetEntriesActive(gameplayEntries, page == Page.Gameplay);
        SetEntriesActive(controlsEntries, page == Page.Controls);

        activeEntries.Clear();
        List<MenuEntry> source = page switch
        {
            Page.OptionsHub => optionsHubEntries,
            Page.Audio => audioEntries,
            Page.Display => displayEntries,
            Page.Gameplay => gameplayEntries,
            Page.Controls => controlsEntries,
            _ => mainEntries
        };

        for (int i = 0; i < source.Count; i++)
        {
            if (source[i].button != null && source[i].button.gameObject.activeInHierarchy)
                activeEntries.Add(source[i]);
        }

        selectedIndex = 0;
        RefreshAllDynamicLabels();
        if (refreshHighlight)
            RefreshMenuHighlight();
    }

    private string MusicVolumeLabel() =>
        "Music Volume: " + Mathf.RoundToInt(GameSettings.MusicVolume * 100f) + "%";

    private string SfxVolumeLabel() =>
        "SFX Volume: " + Mathf.RoundToInt(GameSettings.SfxVolume * 100f) + "%";

    private string MuteMusicLabel() =>
        "Mute Music: " + (GameSettings.MusicMuted ? "On" : "Off");

    private string MuteSfxLabel() =>
        "Mute SFX: " + (GameSettings.SfxMuted ? "On" : "Off");

    private string FullscreenLabel() =>
        "Fullscreen: " + (GameSettings.Fullscreen ? "On" : "Off");

    private string DifficultyLabel() =>
        "Difficulty: " + GameSettings.DifficultyDisplayName(GameSettings.Difficulty);

    private string ResolutionLabel()
    {
        if (resolutionList == null || resolutionList.Length == 0)
            return "Resolution: —";

        resolutionIndex = Mathf.Clamp(resolutionIndex, 0, resolutionList.Length - 1);
        Resolution r = resolutionList[resolutionIndex];
        int hz = Mathf.RoundToInt((float)r.refreshRateRatio.value);
        return hz > 0
            ? $"Resolution: {r.width}x{r.height} @{hz}Hz"
            : $"Resolution: {r.width}x{r.height}";
    }

    private string RebindLabel(string actionName)
    {
        if (waitingForRebind && rebindingActionName == actionName)
            return actionName + ": press a key / button...";

        return actionName + ": " + GameInput.FormatBindingLabel(FindRemapAction(actionName));
    }

    private InputAction FindRemapAction(string actionName)
    {
        if (controls == null || string.IsNullOrEmpty(actionName))
            return null;

        InputActionMap map = controls.asset.FindActionMap("PlayerControls", throwIfNotFound: false);
        if (map != null)
        {
            InputAction fromMap = map.FindAction(actionName, throwIfNotFound: false);
            if (fromMap != null)
                return fromMap;
        }

        return controls.asset.FindAction(actionName, throwIfNotFound: false);
    }

    private void RefreshAllDynamicLabels()
    {
        RefreshListLabels(mainEntries);
        RefreshListLabels(optionsHubEntries);
        RefreshListLabels(audioEntries);
        RefreshListLabels(displayEntries);
        RefreshListLabels(gameplayEntries);
        RefreshListLabels(controlsEntries);
    }

    private static void RefreshListLabels(List<MenuEntry> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            MenuEntry entry = list[i];
            if (entry.button == null || entry.label == null)
                continue;
            SetButtonLabel(entry.button, entry.label());
        }
    }

    private static Transform FindDeep(Transform parent, string name)
    {
        if (parent == null)
            return null;

        if (parent.name == name)
            return parent;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform found = FindDeep(parent.GetChild(i), name);
            if (found != null)
                return found;
        }

        return null;
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null)
            return;

        GameObject go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
    }

    private void OnSelectPerformed(InputAction.CallbackContext context)
    {
        if (!context.performed || loadingScene || waitingForRebind)
            return;

        if (Time.unscaledTime - lastToggleUnscaledTime < 0.12f)
            return;
        lastToggleUnscaledTime = Time.unscaledTime;

        if (isOpen)
        {
            SoundManager.Instance?.PlayUiBack();
            Close();
        }
        else
        {
            SoundManager.Instance?.PlayUiConfirm();
            Open();
        }
    }

    private void OnQuitBackPerformed(InputAction.CallbackContext context)
    {
        if (!context.performed || !isOpen || loadingScene || !CanAcceptMenuInput())
            return;

        if (waitingForRebind)
        {
            CancelRebind(silent: false);
            return;
        }

        SoundManager.Instance?.PlayUiBack();
        ConsumeMenuInputCooldown();
        HandleBack();
    }

    private void OnConfirmPerformed(InputAction.CallbackContext context)
    {
        if (!context.performed || !isOpen || loadingScene || waitingForRebind || !CanAcceptMenuInput())
            return;

        ActivateSelectedEntry();
        ConsumeMenuInputCooldown();
    }

    private void OnUpPerformed(InputAction.CallbackContext context)
    {
        if (!context.performed || !isOpen || waitingForRebind || !CanAcceptMenuInput())
            return;
        MoveSelection(0, -1);
    }

    private void OnDownPerformed(InputAction.CallbackContext context)
    {
        if (!context.performed || !isOpen || waitingForRebind || !CanAcceptMenuInput())
            return;
        MoveSelection(0, 1);
    }

    private void OnLeftPerformed(InputAction.CallbackContext context)
    {
        if (!context.performed || !isOpen || waitingForRebind || !CanAcceptMenuInput())
            return;
        HandleHorizontal(-1);
    }

    private void OnRightPerformed(InputAction.CallbackContext context)
    {
        if (!context.performed || !isOpen || waitingForRebind || !CanAcceptMenuInput())
            return;
        HandleHorizontal(1);
    }

    private void OnMovementPerformed(InputAction.CallbackContext context)
    {
        Vector2 value = context.ReadValue<Vector2>();

        if (!isOpen || waitingForRebind)
        {
            UpdateStickHeldFlags(value);
            return;
        }

        if (!CanAcceptMenuInput())
        {
            UpdateStickHeldFlags(value);
            return;
        }

        if (value.y >= stickThreshold)
        {
            if (!moveUpHeld)
            {
                moveUpHeld = true;
                MoveSelection(0, -1);
            }
        }
        else
        {
            moveUpHeld = false;
        }

        if (value.y <= -stickThreshold)
        {
            if (!moveDownHeld)
            {
                moveDownHeld = true;
                MoveSelection(0, 1);
            }
        }
        else
        {
            moveDownHeld = false;
        }

        if (value.x <= -stickThreshold)
        {
            if (!moveLeftHeld)
            {
                moveLeftHeld = true;
                HandleHorizontal(-1);
            }
        }
        else
        {
            moveLeftHeld = false;
        }

        if (value.x >= stickThreshold)
        {
            if (!moveRightHeld)
            {
                moveRightHeld = true;
                HandleHorizontal(1);
            }
        }
        else
        {
            moveRightHeld = false;
        }
    }

    private void OnMovementCanceled(InputAction.CallbackContext context)
    {
        moveUpHeld = false;
        moveDownHeld = false;
        moveLeftHeld = false;
        moveRightHeld = false;
    }

    private void UpdateStickHeldFlags(Vector2 value)
    {
        moveUpHeld = value.y >= stickThreshold;
        moveDownHeld = value.y <= -stickThreshold;
        moveLeftHeld = value.x <= -stickThreshold;
        moveRightHeld = value.x >= stickThreshold;
    }

    private bool CanAcceptMenuInput()
    {
        return Time.unscaledTime >= nextMenuInputTime;
    }

    private void ConsumeMenuInputCooldown()
    {
        nextMenuInputTime = Time.unscaledTime + Mathf.Max(0.05f, menuInputCooldown);
    }

    private void HandleHorizontal(int dir)
    {
        if (activeEntries.Count == 0)
            return;

        selectedIndex = Mathf.Clamp(selectedIndex, 0, activeEntries.Count - 1);
        MenuEntry entry = activeEntries[selectedIndex];
        switch (entry.kind)
        {
            case OptionKind.MusicVolume:
                AdjustMusicVolume(dir);
                ConsumeMenuInputCooldown();
                return;
            case OptionKind.SfxVolume:
                AdjustSfxVolume(dir);
                ConsumeMenuInputCooldown();
                return;
            case OptionKind.Resolution:
                CycleResolution(dir);
                ConsumeMenuInputCooldown();
                return;
            case OptionKind.Difficulty:
                CycleDifficulty(dir);
                ConsumeMenuInputCooldown();
                return;
            default:
                MoveSelection(dir, 0);
                return;
        }
    }

    private void CycleDifficulty(int dir)
    {
        GameSettings.CycleDifficulty(dir >= 0 ? 1 : -1);
        SoundManager.Instance?.PlayUiConfirm();
        RefreshAllDynamicLabels();
        RefreshMenuHighlight();
    }

    private void MoveSelection(int deltaX, int deltaY)
    {
        if (activeEntries.Count == 0 || (deltaX == 0 && deltaY == 0))
            return;

        List<Button> buttons = new List<Button>(activeEntries.Count);
        for (int i = 0; i < activeEntries.Count; i++)
            buttons.Add(activeEntries[i].button);

        if (MoveSpatialSelection(buttons, ref selectedIndex, deltaX, deltaY))
        {
            RefreshMenuHighlight();
            ConsumeMenuInputCooldown();
            return;
        }

        int step = deltaY != 0 ? deltaY : deltaX;
        if (step == 0)
            return;

        selectedIndex = (selectedIndex + step + activeEntries.Count) % activeEntries.Count;
        RefreshMenuHighlight();
        ConsumeMenuInputCooldown();
    }

    private static bool MoveSpatialSelection(List<Button> buttons, ref int currentIndex, int deltaX, int deltaY)
    {
        if (buttons == null || buttons.Count <= 1)
            return false;

        if (deltaX == 0 && deltaY == 0)
            return false;

        currentIndex = Mathf.Clamp(currentIndex, 0, buttons.Count - 1);
        if (!TryGetButtonWorldPosition(buttons[currentIndex], out Vector2 from))
            return false;

        Vector2 dir = new Vector2(deltaX, -deltaY);
        if (dir.sqrMagnitude < 0.0001f)
            return false;

        dir.Normalize();

        int bestIndex = -1;
        float bestPrimary = float.MaxValue;
        float bestSecondary = float.MaxValue;
        const float minAlignment = 0.25f;

        for (int i = 0; i < buttons.Count; i++)
        {
            if (i == currentIndex)
                continue;

            Button candidate = buttons[i];
            if (candidate == null || !candidate.isActiveAndEnabled || !candidate.gameObject.activeInHierarchy)
                continue;

            if (!TryGetButtonWorldPosition(candidate, out Vector2 to))
                continue;

            Vector2 delta = to - from;
            if (delta.sqrMagnitude < 1f)
                continue;

            float alignment = Vector2.Dot(delta.normalized, dir);
            if (alignment < minAlignment)
                continue;

            float primary = Vector2.Dot(delta, dir);
            if (primary <= 0f)
                continue;

            float secondary = Mathf.Abs(Vector2.Dot(delta, new Vector2(-dir.y, dir.x)));

            if (primary < bestPrimary - 0.5f ||
                (Mathf.Abs(primary - bestPrimary) <= 0.5f && secondary < bestSecondary))
            {
                bestPrimary = primary;
                bestSecondary = secondary;
                bestIndex = i;
            }
        }

        if (bestIndex < 0)
            return false;

        currentIndex = bestIndex;
        return true;
    }

    private static bool TryGetButtonWorldPosition(Button button, out Vector2 position)
    {
        position = default;
        if (button == null)
            return false;

        RectTransform rt = button.transform as RectTransform;
        position = rt != null ? (Vector2)rt.position : (Vector2)button.transform.position;
        return true;
    }

    private void ActivateSelectedEntry()
    {
        if (activeEntries.Count == 0)
            return;

        selectedIndex = Mathf.Clamp(selectedIndex, 0, activeEntries.Count - 1);
        ActivateEntry(activeEntries[selectedIndex]);
    }

    private void ActivateEntry(MenuEntry entry)
    {
        if (entry == null || entry.button == null || loadingScene || waitingForRebind)
            return;

        int index = activeEntries.IndexOf(entry);
        if (index >= 0)
            selectedIndex = index;

        switch (entry.kind)
        {
            case OptionKind.Resume:
                SoundManager.Instance?.PlayUiConfirm();
                Close();
                break;
            case OptionKind.ReturnHome:
                OnReturnHomeTownClicked();
                break;
            case OptionKind.Quit:
                OnQuitGameClicked();
                break;
            case OptionKind.OpenPage:
                SoundManager.Instance?.PlayUiConfirm();
                ShowPage(entry.targetPage);
                break;
            case OptionKind.Back:
                SoundManager.Instance?.PlayUiBack();
                ShowPage(entry.targetPage);
                break;
            case OptionKind.MusicVolume:
                AdjustMusicVolume(1);
                break;
            case OptionKind.SfxVolume:
                AdjustSfxVolume(1);
                break;
            case OptionKind.MuteMusic:
                GameSettings.ToggleMusicMuted();
                SoundManager.Instance?.PlayUiConfirm();
                RefreshAllDynamicLabels();
                RefreshMenuHighlight();
                break;
            case OptionKind.MuteSfx:
                GameSettings.ToggleSfxMuted();
                SoundManager.Instance?.PlayUiConfirm();
                RefreshAllDynamicLabels();
                RefreshMenuHighlight();
                break;
            case OptionKind.Fullscreen:
                GameSettings.ToggleFullscreen();
                SoundManager.Instance?.PlayUiConfirm();
                RefreshAllDynamicLabels();
                RefreshMenuHighlight();
                break;
            case OptionKind.Resolution:
                CycleResolution(1);
                break;
            case OptionKind.Difficulty:
                CycleDifficulty(1);
                break;
            case OptionKind.RebindAction:
                BeginRebind(entry.actionName);
                break;
            case OptionKind.ResetControls:
                GameInput.ResetAllBindings(controls != null ? controls.asset : null);
                SoundManager.Instance?.PlayUiConfirm();
                RefreshAllDynamicLabels();
                RefreshMenuHighlight();
                break;
        }
    }

    private void HandleBack()
    {
        switch (currentPage)
        {
            case Page.Main:
                Close();
                break;
            case Page.Audio:
            case Page.Display:
            case Page.Gameplay:
            case Page.Controls:
                ShowPage(Page.OptionsHub);
                break;
            default:
                ShowPage(Page.Main);
                break;
        }
    }

    private void AdjustMusicVolume(int dir)
    {
        float next = Mathf.Clamp01(GameSettings.MusicVolume + dir * volumeStep);
        GameSettings.SetMusicVolume(next);
        SoundManager.Instance?.PlayUiConfirm();
        RefreshAllDynamicLabels();
        RefreshMenuHighlight();
    }

    private void AdjustSfxVolume(int dir)
    {
        float next = Mathf.Clamp01(GameSettings.SfxVolume + dir * volumeStep);
        GameSettings.SetSfxVolume(next);
        SoundManager.Instance?.PlayUiConfirm();
        RefreshAllDynamicLabels();
        RefreshMenuHighlight();
    }

    private void CycleResolution(int dir)
    {
        if (!GameSettings.SupportsResolutionList || resolutionList == null || resolutionList.Length == 0)
            return;

        resolutionIndex = (resolutionIndex + dir + resolutionList.Length) % resolutionList.Length;
        Resolution r = resolutionList[resolutionIndex];
        int hz = Mathf.RoundToInt((float)r.refreshRateRatio.value);
        GameSettings.SetResolution(r.width, r.height, hz);
        SoundManager.Instance?.PlayUiConfirm();
        RefreshAllDynamicLabels();
        RefreshMenuHighlight();
    }

    private void BeginRebind(string actionName)
    {
        if (controls == null || string.IsNullOrEmpty(actionName) || waitingForRebind)
            return;

        InputAction action = FindRemapAction(actionName);
        if (action == null)
            return;

        CancelRebind(silent: true);
        waitingForRebind = true;
        rebindingActionName = actionName;
        RefreshAllDynamicLabels();
        RefreshMenuHighlight();
        SoundManager.Instance?.PlayUiConfirm();

        action.Disable();
        rebindOperation = action.PerformInteractiveRebinding()
            .WithControlsExcluding("<Mouse>")
            .WithControlsExcluding("<Pointer>")
            .WithCancelingThrough("<Keyboard>/escape")
            .OnMatchWaitForAnother(0.15f)
            .OnComplete(op =>
            {
                action.Enable();
                GameInput.SaveOverridesFrom(controls.asset);
                FinishRebind();
            })
            .OnCancel(op =>
            {
                action.Enable();
                FinishRebind();
            })
            .Start();
    }

    private void FinishRebind()
    {
        if (rebindOperation != null)
        {
            rebindOperation.Dispose();
            rebindOperation = null;
        }

        waitingForRebind = false;
        rebindingActionName = null;
        RefreshAllDynamicLabels();
        RefreshMenuHighlight();
        ConsumeMenuInputCooldown();
    }

    private void CancelRebind(bool silent)
    {
        if (!waitingForRebind && rebindOperation == null)
            return;

        if (rebindOperation != null)
        {
            try
            {
                rebindOperation.Cancel();
            }
            catch
            {
                // ignored — operation may already be disposed
            }

            rebindOperation.Dispose();
            rebindOperation = null;
        }

        waitingForRebind = false;
        rebindingActionName = null;
        if (!silent)
            SoundManager.Instance?.PlayUiBack();
        RefreshAllDynamicLabels();
        RefreshMenuHighlight();
        ConsumeMenuInputCooldown();
    }

    private void RefreshMenuHighlight()
    {
        for (int i = 0; i < activeEntries.Count; i++)
            SetButtonHighlighted(activeEntries[i].button, i == selectedIndex && isOpen);

        if (!isOpen || activeEntries.Count == 0)
            return;

        Button selected = activeEntries[Mathf.Clamp(selectedIndex, 0, activeEntries.Count - 1)].button;
        EventSystem es = EventSystem.current;
        if (es != null && selected != null)
            es.SetSelectedGameObject(selected.gameObject);
    }

    private void SetButtonHighlighted(Button button, bool selected)
    {
        if (button == null)
            return;

        if (!buttonBaseScales.TryGetValue(button, out Vector3 baseScale))
        {
            baseScale = button.transform.localScale;
            buttonBaseScales[button] = baseScale;
        }

        float scaleMul = selected ? (1f + Mathf.Max(0f, selectedScaleBonus)) : 1f;
        button.transform.localScale = baseScale * scaleMul;

        Color tint = selected ? selectedButtonColor : normalButtonColor;
        ColorBlock colors = button.colors;
        colors.normalColor = tint;
        colors.selectedColor = tint;
        colors.highlightedColor = tint;
        button.colors = colors;

        if (button.targetGraphic != null)
            button.targetGraphic.color = tint;

        Outline outline = button.GetComponent<Outline>();
        if (outline == null)
            outline = button.gameObject.AddComponent<Outline>();

        outline.effectColor = outlineColor;
        outline.effectDistance = outlineDistance;
        outline.enabled = selected;
    }

    private void ResetAllButtonVisuals()
    {
        ResetListVisuals(mainEntries);
        ResetListVisuals(optionsHubEntries);
        ResetListVisuals(audioEntries);
        ResetListVisuals(displayEntries);
        ResetListVisuals(gameplayEntries);
        ResetListVisuals(controlsEntries);
    }

    private void ResetListVisuals(List<MenuEntry> list)
    {
        for (int i = 0; i < list.Count; i++)
            SetButtonHighlighted(list[i].button, false);
    }

    public void Open()
    {
        if (isOpen || loadingScene)
            return;

        if (KaboodleIsBlocking())
            return;

        if (PauseBInventory.Instance != null && PauseBInventory.Instance.IsOpen)
            return;

        if (RocketDestinationMenu.IsOpen)
            return;

        EnsureOptionsUi();
        isOpen = true;

        if (menuRoot != null)
            menuRoot.SetActive(true);

        FixButtonDrawOrder();
        ShowPage(Page.Main);

        if (freezeTimeWhileOpen)
        {
            savedTimeScale = Time.timeScale;
            Time.timeScale = 0f;
        }

        if (lockPlayerWhileOpen)
            SetPlayersLocked(true);

        moveUpHeld = moveDownHeld = moveLeftHeld = moveRightHeld = false;
        ConsumeMenuInputCooldown();
        RefreshMenuHighlight();
    }

    public void Close()
    {
        if (!isOpen)
            return;

        ForceClose();
    }

    private void ForceClose()
    {
        CancelRebind(silent: true);
        isOpen = false;
        ShowPage(Page.Main, refreshHighlight: false);

        if (menuRoot != null)
            menuRoot.SetActive(false);

        ResetAllButtonVisuals();

        if (freezeTimeWhileOpen)
            Time.timeScale = savedTimeScale > 0f ? savedTimeScale : 1f;

        if (lockPlayerWhileOpen)
            SetPlayersLocked(false);

        EventSystem es = EventSystem.current;
        if (es != null && IsOwnedSelection(es.currentSelectedGameObject))
            es.SetSelectedGameObject(null);
    }

    private bool IsOwnedSelection(GameObject selected)
    {
        if (selected == null)
            return false;

        return IsInList(mainEntries, selected) ||
               IsInList(optionsHubEntries, selected) ||
               IsInList(audioEntries, selected) ||
               IsInList(displayEntries, selected) ||
               IsInList(gameplayEntries, selected) ||
               IsInList(controlsEntries, selected);
    }

    private static bool IsInList(List<MenuEntry> list, GameObject selected)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].button != null &&
                (selected == list[i].button.gameObject ||
                 selected.transform.IsChildOf(list[i].button.transform)))
                return true;
        }

        return false;
    }

    private void OnReturnHomeTownClicked()
    {
        if (loadingScene)
            return;

        SoundManager.Instance?.PlayUiConfirm();
        loadingScene = true;

        if (freezeTimeWhileOpen)
            Time.timeScale = savedTimeScale > 0f ? savedTimeScale : 1f;

        isOpen = false;
        ResetAllButtonVisuals();
        if (menuRoot != null)
            menuRoot.SetActive(false);

        string scene = string.IsNullOrWhiteSpace(homeTownSceneName) ? "HomeTown" : homeTownSceneName;

        if (KitRocketShip.TryPickUpPlayer(scene))
            return;

        SetPlayersLocked(false);
        ScreenFade.EnsureExists().LoadScene(scene);
    }

    private void OnQuitGameClicked()
    {
        if (loadingScene)
            return;

        SoundManager.Instance?.PlayUiConfirm();

        if (freezeTimeWhileOpen)
            Time.timeScale = savedTimeScale > 0f ? savedTimeScale : 1f;

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private static bool KaboodleIsBlocking()
    {
        Kaboodle kaboodle = FindFirstObjectByType<Kaboodle>();
        return kaboodle != null && kaboodle.IsBoxOpen;
    }

    private static void SetPlayersLocked(bool locked)
    {
        PlayerController[] players = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] != null)
                players[i].SetInputLocked(locked);
        }
    }
}
