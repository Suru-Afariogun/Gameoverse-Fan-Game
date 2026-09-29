using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Pause A system menu on Pause Canvas A (Resume / Return to Home Town / Quit Game).
/// Open with Select (Tab / gamepad Select). Pause B keeps Start / Pause for inventory.
/// Navigation matches Kaboodle / Pause B: d-pad, stick, Confirm, Quit/Back.
/// </summary>
public class PauseA : MonoBehaviour
{
    public static PauseA Instance { get; private set; }

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

        // Typo-safe fallback for the object as authored in HomeTown.
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

    [Header("Scenes")]
    [SerializeField] private string homeTownSceneName = "HomeTown";

    [Header("Input")]
    [SerializeField] [Range(0.1f, 1f)] private float stickThreshold = 0.5f;
    [SerializeField] private float menuInputCooldown = 0.18f;

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
    private readonly List<Button> menuButtons = new List<Button>(3);
    private readonly Dictionary<Button, Vector3> buttonBaseScales = new Dictionary<Button, Vector3>();

    public bool IsOpen => isOpen;

    private void Awake()
    {
        Instance = this;
        controls = new InputActions();
        AutoBindIfNeeded();
        FixPauseCanvasSetup();
        FixButtonDrawOrder();
        CacheButtons();
        WireButtonClicks();
        EnsureEventSystem();

        if (menuRoot != null)
            menuRoot.SetActive(false);

        isOpen = false;
        RefreshMenuHighlight();
    }

    private void OnEnable()
    {
        if (controls == null)
            controls = new InputActions();

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
        if (Instance == this)
            Instance = null;

        UnwireButtonClicks();

        if (controls != null)
        {
            controls.Dispose();
            controls = null;
        }
    }

    /// <summary>
    /// Canvas was Screen Space Camera with scale 0. Sprite art on Pause A box/container
    /// used sortingOrder 80–81 while the Canvas was also 80, so UI buttons drew underneath.
    /// </summary>
    private void FixPauseCanvasSetup()
    {
        Canvas canvas = GetComponent<Canvas>();
        if (canvas == null)
            canvas = GetComponentInParent<Canvas>();
        if (canvas == null)
            return;

        if (canvas.renderMode == RenderMode.ScreenSpaceCamera && canvas.worldCamera == null)
        {
            if (Camera.main != null)
                canvas.worldCamera = Camera.main;
            else
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        }

        if (canvas.sortingOrder < 100)
            canvas.sortingOrder = 100;

        Canvas.ForceUpdateCanvases();
    }

    private void FixButtonDrawOrder()
    {
        BringButtonInFront(resumeButton);
        BringButtonInFront(returnHomeTownButton);
        BringButtonInFront(quitGameButton);
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

        // We drive selection ourselves (same as Kaboodle).
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
    }

    private void CacheButtons()
    {
        menuButtons.Clear();
        buttonBaseScales.Clear();

        AddMenuButton(resumeButton);
        AddMenuButton(returnHomeTownButton);
        AddMenuButton(quitGameButton);
    }

    private void AddMenuButton(Button button)
    {
        if (button == null || menuButtons.Contains(button))
            return;

        menuButtons.Add(button);
        buttonBaseScales[button] = button.transform.localScale;
    }

    private void WireButtonClicks()
    {
        if (resumeButton != null)
            resumeButton.onClick.AddListener(OnResumeClicked);
        if (returnHomeTownButton != null)
            returnHomeTownButton.onClick.AddListener(OnReturnHomeTownClicked);
        if (quitGameButton != null)
            quitGameButton.onClick.AddListener(OnQuitGameClicked);
    }

    private void UnwireButtonClicks()
    {
        if (resumeButton != null)
            resumeButton.onClick.RemoveListener(OnResumeClicked);
        if (returnHomeTownButton != null)
            returnHomeTownButton.onClick.RemoveListener(OnReturnHomeTownClicked);
        if (quitGameButton != null)
            quitGameButton.onClick.RemoveListener(OnQuitGameClicked);
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
        if (!context.performed || loadingScene)
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

        SoundManager.Instance?.PlayUiBack();
        ConsumeMenuInputCooldown();
        Close();
    }

    private void OnConfirmPerformed(InputAction.CallbackContext context)
    {
        if (!context.performed || !isOpen || loadingScene || !CanAcceptMenuInput())
            return;

        ActivateSelectedButton();
        ConsumeMenuInputCooldown();
    }

    private void OnUpPerformed(InputAction.CallbackContext context)
    {
        if (!context.performed || !isOpen || !CanAcceptMenuInput())
            return;
        MoveSelection(0, -1);
    }

    private void OnDownPerformed(InputAction.CallbackContext context)
    {
        if (!context.performed || !isOpen || !CanAcceptMenuInput())
            return;
        MoveSelection(0, 1);
    }

    private void OnLeftPerformed(InputAction.CallbackContext context)
    {
        if (!context.performed || !isOpen || !CanAcceptMenuInput())
            return;
        MoveSelection(-1, 0);
    }

    private void OnRightPerformed(InputAction.CallbackContext context)
    {
        if (!context.performed || !isOpen || !CanAcceptMenuInput())
            return;
        MoveSelection(1, 0);
    }

    private void OnMovementPerformed(InputAction.CallbackContext context)
    {
        Vector2 value = context.ReadValue<Vector2>();

        if (!isOpen)
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
                MoveSelection(-1, 0);
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
                MoveSelection(1, 0);
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

    /// <summary>
    /// Same spatial nav as Kaboodle. Falls back to wrap cycling so a vertical button
    /// column still responds to Left/Right (Pause B style free movement).
    /// </summary>
    private void MoveSelection(int deltaX, int deltaY)
    {
        if (menuButtons.Count == 0 || (deltaX == 0 && deltaY == 0))
            return;

        if (MoveSpatialSelection(menuButtons, ref selectedIndex, deltaX, deltaY))
        {
            RefreshMenuHighlight();
            ConsumeMenuInputCooldown();
            return;
        }

        int step = 0;
        if (deltaY != 0)
            step = deltaY;
        else if (deltaX != 0)
            step = deltaX;

        if (step == 0)
            return;

        selectedIndex = (selectedIndex + step + menuButtons.Count) % menuButtons.Count;
        RefreshMenuHighlight();
        ConsumeMenuInputCooldown();
    }

    /// <summary>
    /// Move selection to the nearest button in the input direction using on-screen positions.
    /// Copied pattern from Kaboodle so Pause A feels identical.
    /// </summary>
    private static bool MoveSpatialSelection(List<Button> buttons, ref int currentIndex, int deltaX, int deltaY)
    {
        if (buttons == null || buttons.Count <= 1)
            return false;

        if (deltaX == 0 && deltaY == 0)
            return false;

        currentIndex = Mathf.Clamp(currentIndex, 0, buttons.Count - 1);
        if (!TryGetButtonWorldPosition(buttons[currentIndex], out Vector2 from))
            return false;

        // Up on stick maps to deltaY = -1; screen Y increases upward in UI world space.
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

    private void ActivateSelectedButton()
    {
        if (menuButtons.Count == 0)
            return;

        selectedIndex = Mathf.Clamp(selectedIndex, 0, menuButtons.Count - 1);
        Button button = menuButtons[selectedIndex];
        if (button != null)
            button.onClick.Invoke();
    }

    private void RefreshMenuHighlight()
    {
        for (int i = 0; i < menuButtons.Count; i++)
            SetButtonHighlighted(menuButtons[i], i == selectedIndex && isOpen);

        if (!isOpen || menuButtons.Count == 0)
            return;

        Button selected = menuButtons[Mathf.Clamp(selectedIndex, 0, menuButtons.Count - 1)];
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
        for (int i = 0; i < menuButtons.Count; i++)
            SetButtonHighlighted(menuButtons[i], false);
    }

    private void SelectButtonByReference(Button button)
    {
        int index = menuButtons.IndexOf(button);
        if (index < 0)
            return;

        selectedIndex = index;
        RefreshMenuHighlight();
    }

    public void Open()
    {
        if (isOpen || loadingScene)
            return;

        if (KaboodleIsBlocking())
            return;

        if (PauseBInventory.Instance != null && PauseBInventory.Instance.IsOpen)
            return;

        isOpen = true;
        selectedIndex = 0;

        if (menuRoot != null)
            menuRoot.SetActive(true);

        FixButtonDrawOrder();
        CacheButtons();

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
        isOpen = false;

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

        for (int i = 0; i < menuButtons.Count; i++)
        {
            if (menuButtons[i] != null &&
                (selected == menuButtons[i].gameObject ||
                 selected.transform.IsChildOf(menuButtons[i].transform)))
                return true;
        }

        return false;
    }

    private void OnResumeClicked()
    {
        if (loadingScene)
            return;

        SelectButtonByReference(resumeButton);
        SoundManager.Instance?.PlayUiConfirm();
        Close();
    }

    private void OnReturnHomeTownClicked()
    {
        if (loadingScene)
            return;

        SelectButtonByReference(returnHomeTownButton);
        SoundManager.Instance?.PlayUiConfirm();
        loadingScene = true;

        if (freezeTimeWhileOpen)
            Time.timeScale = savedTimeScale > 0f ? savedTimeScale : 1f;

        isOpen = false;
        ResetAllButtonVisuals();
        if (menuRoot != null)
            menuRoot.SetActive(false);

        string scene = string.IsNullOrWhiteSpace(homeTownSceneName) ? "HomeTown" : homeTownSceneName;

        // Kit's rocket ship swoops in, the player boards, and the fade starts once it is off-screen.
        if (KitRocketShip.TryPickUpPlayer(scene))
            return;

        SetPlayersLocked(false);
        ScreenFade.EnsureExists().LoadScene(scene);
    }

    private void OnQuitGameClicked()
    {
        if (loadingScene)
            return;

        SelectButtonByReference(quitGameButton);
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
