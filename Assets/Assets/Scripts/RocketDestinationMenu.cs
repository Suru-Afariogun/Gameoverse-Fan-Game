using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Hometown-only rocket travel popup. Clones Scratch's shop canvas layout, navigates like Pause A
/// (Confirm / Back / d-pad / stick + highlight), and starts the ship's normal board-and-launch
/// sequence when a destination is chosen.
/// </summary>
[DisallowMultipleComponent]
public sealed class RocketDestinationMenu : MonoBehaviour
{
    public static RocketDestinationMenu Instance { get; private set; }

    [Serializable]
    public sealed class Destination
    {
        [Tooltip("Button label shown to the player.")]
        public string displayName;
        [Tooltip("Exact scene name from Build Settings.")]
        public string sceneName;
    }

    private sealed class Entry
    {
        public Button button;
        public TMP_Text label;
        public Image image;
        public Color baseColor = Color.white;
        public Vector3 baseScale = Vector3.one;
        public Destination destination;
    }

    [Header("Destinations (playable levels)")]
    [SerializeField] private Destination[] destinations =
    {
        new Destination { displayName = "Tutorial", sceneName = "Tutorial level" },
        new Destination { displayName = "Level One", sceneName = "Level one" },
        new Destination { displayName = "The City", sceneName = "The City level" },
        new Destination { displayName = "Bonus Level 0", sceneName = "Bonus Level 0" }
    };

    [Header("Layout Source")]
    [Tooltip("Defaults to Scrath's Shop Canvas in HomeTown.")]
    [SerializeField] private string shopCanvasObjectName = "Scrath's Shop Canvas";

    [Header("Input")]
    [SerializeField] [Range(0.1f, 1f)] private float stickThreshold = 0.5f;
    [SerializeField] private float menuInputCooldown = 0.18f;
    [SerializeField] private float openInputDelay = 0.25f;

    [Header("Selection Look (Pause A)")]
    [SerializeField] private Color normalButtonColor = new Color(0.907585f, 0.93962264f, 0.515906f, 1f);
    [SerializeField] private Color selectedButtonColor = new Color(0.89433956f, 0.7035845f, 0.038810864f, 1f);
    [SerializeField] private float selectedScaleBonus = 0.15f;
    [SerializeField] private Color outlineColor = new Color(1f, 0.95f, 0.35f, 1f);
    [SerializeField] private Vector2 outlineDistance = new Vector2(2.5f, 2.5f);

    [Header("Pause")]
    [SerializeField] private bool freezeTimeWhileOpen = true;
    [SerializeField] private bool lockPlayerWhileOpen = true;

    private readonly List<Entry> entries = new List<Entry>(8);
    private readonly List<Destination> loadableDestinations = new List<Destination>(8);

    private InputActions controls;
    private GameObject menuRoot;
    private TMP_Text infoTitleText;
    private TMP_Text infoBodyText;
    private Button rowTemplate;
    private Transform rowsParent;

    private KitRocketShip ship;
    private PlayerController traveler;
    private bool isOpen;
    private bool launching;
    private int selectedIndex;
    private float nextMenuInputTime;
    private bool moveUpHeld;
    private bool moveDownHeld;
    private float savedTimeScale = 1f;

    public static bool IsOpen => Instance != null && Instance.isOpen;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureExistsInHomeTown()
    {
        if (!IsHomeTownScene())
            return;

        if (Instance != null)
            return;

        GameObject host = new GameObject("Rocket Destination Menu");
        host.AddComponent<RocketDestinationMenu>();
    }

    public static bool IsHomeTownScene()
    {
        string name = SceneManager.GetActiveScene().name;
        return !string.IsNullOrEmpty(name) &&
               name.IndexOf("HomeTown", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>Open the destination list for a Hometown rocket interaction. Returns false if it cannot open.</summary>
    public static bool TryOpen(KitRocketShip rocket, PlayerController player)
    {
        if (rocket == null || player == null || !IsHomeTownScene())
            return false;

        if (Instance == null)
            EnsureExistsInHomeTown();

        if (Instance == null)
            return false;

        return Instance.Open(rocket, player);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        controls = GameInput.Create();
        BuildMenuFromShopLayout();
        EnsureEventSystem();
        SetMenuVisible(false);
    }

    private void OnEnable()
    {
        if (controls == null)
            controls = GameInput.Create();

        controls.PlayerControls.Enable();
        controls.PlayerControls.Confirm.performed += OnConfirmPerformed;
        controls.PlayerControls.QuitBack.performed += OnBackPerformed;
        controls.PlayerControls.Select.performed += OnBackPerformed;
        controls.PlayerControls.Up.performed += OnUpPerformed;
        controls.PlayerControls.Down.performed += OnDownPerformed;
        controls.PlayerControls.Movement.performed += OnMovementPerformed;
        controls.PlayerControls.Movement.canceled += OnMovementCanceled;
    }

    private void OnDisable()
    {
        if (controls != null)
        {
            controls.PlayerControls.Confirm.performed -= OnConfirmPerformed;
            controls.PlayerControls.QuitBack.performed -= OnBackPerformed;
            controls.PlayerControls.Select.performed -= OnBackPerformed;
            controls.PlayerControls.Up.performed -= OnUpPerformed;
            controls.PlayerControls.Down.performed -= OnDownPerformed;
            controls.PlayerControls.Movement.performed -= OnMovementPerformed;
            controls.PlayerControls.Movement.canceled -= OnMovementCanceled;
            controls.PlayerControls.Disable();
        }

        if (isOpen)
            ForceClose(resumePlayer: true);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        if (controls != null)
        {
            controls.Dispose();
            controls = null;
        }

        if (menuRoot != null)
            Destroy(menuRoot);
    }

    private bool Open(KitRocketShip rocket, PlayerController player)
    {
        if (isOpen || launching || rocket == null || player == null)
            return false;

        if (PauseA.Instance != null && PauseA.Instance.IsOpen)
            return false;

        if (PauseBInventory.Instance != null && PauseBInventory.Instance.IsOpen)
            return false;

        if (DialogueBox.Instance != null && DialogueBox.Instance.IsOpen)
            return false;

        if (AnyUpgradeShopOpen())
            return false;

        if (menuRoot == null)
            BuildMenuFromShopLayout();

        if (menuRoot == null || entries.Count == 0)
        {
            Debug.LogWarning("[RocketDestinationMenu] Could not build travel UI from Scratch's shop canvas.", this);
            return false;
        }

        RefreshLoadableDestinations();
        if (loadableDestinations.Count == 0)
        {
            Debug.LogWarning("[RocketDestinationMenu] No playable destinations are in Build Settings.", this);
            return false;
        }

        BindDestinationsToButtons();

        ship = rocket;
        traveler = player;
        isOpen = true;
        launching = false;
        selectedIndex = 0;
        moveUpHeld = moveDownHeld = true;
        nextMenuInputTime = Time.unscaledTime + Mathf.Max(0f, openInputDelay);

        if (lockPlayerWhileOpen)
            player.SetInputLocked(true);

        if (freezeTimeWhileOpen)
        {
            savedTimeScale = Time.timeScale;
            Time.timeScale = 0f;
        }

        SetMenuVisible(true);
        RefreshInfo();
        RefreshHighlight();
        SoundManager.Instance?.PlayUiConfirm();
        return true;
    }

    public void Close()
    {
        if (!isOpen || launching)
            return;

        SoundManager.Instance?.PlayUiBack();
        ForceClose(resumePlayer: true);
    }

    private void ForceClose(bool resumePlayer)
    {
        isOpen = false;
        SetMenuVisible(false);
        ResetButtonVisuals();

        if (freezeTimeWhileOpen)
            Time.timeScale = savedTimeScale > 0f ? savedTimeScale : 1f;

        if (resumePlayer && traveler != null && !traveler.IsDead)
            traveler.SetInputLocked(false);

        ship = null;
        traveler = null;

        EventSystem es = EventSystem.current;
        if (es != null && IsOwnedSelection(es.currentSelectedGameObject))
            es.SetSelectedGameObject(null);
    }

    #region Input (Pause A style)

    private bool CanAcceptMenuInput()
    {
        return isOpen && !launching && Time.unscaledTime >= nextMenuInputTime;
    }

    private void ConsumeMenuInputCooldown()
    {
        nextMenuInputTime = Time.unscaledTime + Mathf.Max(0.05f, menuInputCooldown);
    }

    private void OnConfirmPerformed(InputAction.CallbackContext context)
    {
        if (!context.performed || !CanAcceptMenuInput())
            return;

        ActivateSelected();
        ConsumeMenuInputCooldown();
    }

    private void OnBackPerformed(InputAction.CallbackContext context)
    {
        if (!context.performed || !CanAcceptMenuInput())
            return;

        ConsumeMenuInputCooldown();
        Close();
    }

    private void OnUpPerformed(InputAction.CallbackContext context)
    {
        if (!context.performed || !CanAcceptMenuInput())
            return;
        MoveSelection(-1);
    }

    private void OnDownPerformed(InputAction.CallbackContext context)
    {
        if (!context.performed || !CanAcceptMenuInput())
            return;
        MoveSelection(1);
    }

    private void OnMovementPerformed(InputAction.CallbackContext context)
    {
        Vector2 value = context.ReadValue<Vector2>();
        if (!isOpen || launching)
        {
            moveUpHeld = value.y >= stickThreshold;
            moveDownHeld = value.y <= -stickThreshold;
            return;
        }

        if (!CanAcceptMenuInput())
        {
            moveUpHeld = value.y >= stickThreshold;
            moveDownHeld = value.y <= -stickThreshold;
            return;
        }

        if (value.y >= stickThreshold)
        {
            if (!moveUpHeld)
            {
                moveUpHeld = true;
                MoveSelection(-1);
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
                MoveSelection(1);
            }
        }
        else
        {
            moveDownHeld = false;
        }
    }

    private void OnMovementCanceled(InputAction.CallbackContext context)
    {
        moveUpHeld = false;
        moveDownHeld = false;
    }

    private void MoveSelection(int delta)
    {
        if (entries.Count == 0 || delta == 0)
            return;

        selectedIndex = (selectedIndex + delta + entries.Count) % entries.Count;
        RefreshInfo();
        RefreshHighlight();
        ConsumeMenuInputCooldown();
    }

    private void ActivateSelected()
    {
        if (entries.Count == 0)
            return;

        selectedIndex = Mathf.Clamp(selectedIndex, 0, entries.Count - 1);
        Entry entry = entries[selectedIndex];
        if (entry?.destination == null || string.IsNullOrWhiteSpace(entry.destination.sceneName))
            return;

        string scene = entry.destination.sceneName.Trim();
        if (!Application.CanStreamedLevelBeLoaded(scene))
        {
            SoundManager.Instance?.PlayUiBack();
            Debug.LogWarning($"[RocketDestinationMenu] Scene '{scene}' is not in Build Settings.", this);
            return;
        }

        if (ship == null || traveler == null || traveler.IsDead)
        {
            SoundManager.Instance?.PlayUiBack();
            ForceClose(resumePlayer: true);
            return;
        }

        SoundManager.Instance?.PlayUiConfirm();
        launching = true;

        if (freezeTimeWhileOpen)
            Time.timeScale = savedTimeScale > 0f ? savedTimeScale : 1f;

        SetMenuVisible(false);
        ResetButtonVisuals();
        isOpen = false;

        KitRocketShip travelShip = ship;
        PlayerController travelPlayer = traveler;
        ship = null;
        traveler = null;

        // Keep the player locked; boarding owns that from here.
        if (!travelShip.TryBeginTravelFromMenu(travelPlayer, scene))
        {
            launching = false;
            if (travelPlayer != null && !travelPlayer.IsDead)
                travelPlayer.SetInputLocked(false);
        }
        else
        {
            launching = false;
        }
    }

    #endregion

    #region UI build (Scratch shop layout)

    private void BuildMenuFromShopLayout()
    {
        if (menuRoot != null)
            return;

        // UpgradeShop hides the canvas after Awake — Find must include inactive objects.
        GameObject template = FindNamedIncludingInactive(shopCanvasObjectName);
        if (template == null)
        {
            Debug.LogWarning(
                $"[RocketDestinationMenu] Could not find '{shopCanvasObjectName}' to clone for the travel popup.",
                this);
            return;
        }

        menuRoot = Instantiate(template, template.transform.parent);
        menuRoot.name = "Rocket Destination Canvas";
        menuRoot.SetActive(true);

        // Shop component must not drive this clone.
        UpgradeShop[] shops = menuRoot.GetComponentsInChildren<UpgradeShop>(true);
        for (int i = 0; i < shops.Length; i++)
        {
            if (shops[i] != null)
                Destroy(shops[i]);
        }

        HideCrystalHeader(menuRoot.transform);
        CacheInfoTexts(menuRoot.transform);
        CollectAndPrepareRowButtons(menuRoot.transform);
        BindCanvasToCamera(menuRoot);

        if (infoTitleText != null)
            infoTitleText.text = "Choose a Destination";
        if (infoBodyText != null)
            infoBodyText.text = "Pick where Kit's rocket should fly.\n<size=80%><b>Up</b>/<b>Down</b>: choose   <b>Confirm</b>: launch   <b>Back</b>: cancel</size>";
    }

    private static void HideCrystalHeader(Transform root)
    {
        Transform header = FindDeep(root, "Scratch's Shop");
        if (header != null)
            header.gameObject.SetActive(false);

        // Hide leftover crystal labels if they live elsewhere.
        Transform crystalLogo = FindDeep(root, "Crystal Logo");
        if (crystalLogo != null)
            crystalLogo.gameObject.SetActive(false);
        Transform crystalAmount = FindDeep(root, "Crystal amount text");
        if (crystalAmount != null)
            crystalAmount.gameObject.SetActive(false);
        Transform crystalCost = FindDeepStartingWith(root, "Crystal subtract");
        if (crystalCost != null)
            crystalCost.gameObject.SetActive(false);
    }

    private void CacheInfoTexts(Transform root)
    {
        infoTitleText = FindText(root, "Shop Info Title");
        infoBodyText = FindText(root, "Shop Info Text");
    }

    private void CollectAndPrepareRowButtons(Transform root)
    {
        entries.Clear();
        rowTemplate = null;
        rowsParent = null;

        Button[] buttons = root.GetComponentsInChildren<Button>(true);
        List<Button> rows = new List<Button>(4);
        for (int i = 0; i < buttons.Length; i++)
        {
            Button button = buttons[i];
            if (button == null || button.name.Equals("Equip Button", StringComparison.OrdinalIgnoreCase))
                continue;

            // Scratch rows have a Level text child; keep that pattern as the filter.
            if (FindDeep(button.transform, "Level text") == null)
                continue;

            rows.Add(button);
        }

        rows.Sort((a, b) => RowHeight(b).CompareTo(RowHeight(a)));
        if (rows.Count == 0)
            return;

        rowTemplate = rows[0];
        rowsParent = rowTemplate.transform.parent;

        for (int i = 0; i < rows.Count; i++)
            PrepareRowButton(rows[i], addToEntries: true);
    }

    private void PrepareRowButton(Button button, bool addToEntries)
    {
        if (button == null)
            return;

        button.onClick = new Button.ButtonClickedEvent();
        Navigation nav = button.navigation;
        nav.mode = Navigation.Mode.None;
        button.navigation = nav;

        Transform levelText = FindDeep(button.transform, "Level text");
        if (levelText != null)
            levelText.gameObject.SetActive(false);

        Transform equip = FindDeep(button.transform, "Equip Button");
        if (equip != null)
            equip.gameObject.SetActive(false);

        TMP_Text label = FindLabelText(button);
        Image image = button.targetGraphic as Image;
        if (image == null)
            image = button.GetComponent<Image>();

        Outline outline = button.GetComponent<Outline>();
        if (outline == null)
            outline = button.gameObject.AddComponent<Outline>();
        outline.enabled = false;

        if (!addToEntries)
            return;

        Entry entry = new Entry
        {
            button = button,
            label = label,
            image = image,
            baseColor = image != null ? image.color : Color.white,
            baseScale = button.transform.localScale
        };
        entries.Add(entry);

        int captured = entries.Count - 1;
        button.onClick.AddListener(() =>
        {
            if (!CanAcceptMenuInput())
                return;
            selectedIndex = captured;
            ActivateSelected();
            ConsumeMenuInputCooldown();
        });
    }

    private void RefreshLoadableDestinations()
    {
        loadableDestinations.Clear();
        if (destinations == null)
            return;

        for (int i = 0; i < destinations.Length; i++)
        {
            Destination dest = destinations[i];
            if (dest == null || string.IsNullOrWhiteSpace(dest.sceneName))
                continue;

            string scene = dest.sceneName.Trim();
            if (!Application.CanStreamedLevelBeLoaded(scene))
                continue;

            // Never list Hometown as a travel target from Hometown.
            if (scene.IndexOf("HomeTown", StringComparison.OrdinalIgnoreCase) >= 0)
                continue;

            loadableDestinations.Add(new Destination
            {
                displayName = string.IsNullOrWhiteSpace(dest.displayName) ? scene : dest.displayName.Trim(),
                sceneName = scene
            });
        }
    }

    private void BindDestinationsToButtons()
    {
        EnsureEnoughButtons(loadableDestinations.Count);

        for (int i = 0; i < entries.Count; i++)
        {
            Entry entry = entries[i];
            bool used = i < loadableDestinations.Count;
            if (entry.button != null)
                entry.button.gameObject.SetActive(used);

            if (!used)
            {
                entry.destination = null;
                continue;
            }

            entry.destination = loadableDestinations[i];
            if (entry.label != null)
                entry.label.text = entry.destination.displayName;
            else
                SetButtonLabelFallback(entry.button, entry.destination.displayName);
        }

        // Cull unused entry slots from active navigation.
        for (int i = entries.Count - 1; i >= loadableDestinations.Count; i--)
        {
            // Keep pooled buttons; just skip them via active state.
        }

        // Rebuild click indices after pool resize.
        RewireClicks();
        LayoutButtonsVertically(loadableDestinations.Count);
    }

    private void EnsureEnoughButtons(int needed)
    {
        if (rowTemplate == null || rowsParent == null)
            return;

        while (entries.Count < needed)
        {
            GameObject clone = Instantiate(rowTemplate.gameObject, rowsParent);
            clone.name = "Destination Button " + (entries.Count + 1);
            clone.SetActive(true);
            Button button = clone.GetComponent<Button>();
            PrepareRowButton(button, addToEntries: true);
        }
    }

    private void RewireClicks()
    {
        for (int i = 0; i < entries.Count; i++)
        {
            Entry entry = entries[i];
            if (entry.button == null)
                continue;

            entry.button.onClick.RemoveAllListeners();
            int captured = i;
            entry.button.onClick.AddListener(() =>
            {
                if (!CanAcceptMenuInput() || captured >= loadableDestinations.Count)
                    return;
                selectedIndex = captured;
                ActivateSelected();
                ConsumeMenuInputCooldown();
            });
        }
    }

    private void LayoutButtonsVertically(int count)
    {
        if (count <= 0 || entries.Count == 0)
            return;

        float top = RowHeight(entries[0].button);
        float bottom = count == 1 ? top : top - Mathf.Max(1.6f, 0.55f * (count - 1));
        if (count > 1 && entries.Count >= 2)
        {
            // Prefer authored spacing from the first two Scratch rows when available.
            float authoredStep = Mathf.Abs(RowHeight(entries[0].button) - RowHeight(entries[Mathf.Min(1, entries.Count - 1)].button));
            if (authoredStep > 0.05f)
                bottom = top - authoredStep * (count - 1);
        }

        float step = count <= 1 ? 0f : (top - bottom) / (count - 1);
        for (int i = 0; i < count; i++)
        {
            Button button = entries[i].button;
            if (button == null)
                continue;

            RectTransform rt = button.transform as RectTransform;
            if (rt == null)
                continue;

            Vector2 pos = rt.anchoredPosition;
            pos.y = top - step * i;
            rt.anchoredPosition = pos;
        }
    }

    private static float RowHeight(Button button)
    {
        if (button == null)
            return 0f;
        return button.transform is RectTransform rect ? rect.anchoredPosition.y : button.transform.localPosition.y;
    }

    private void BindCanvasToCamera(GameObject root)
    {
        Canvas canvas = root.GetComponent<Canvas>();
        if (canvas == null)
            canvas = root.GetComponentInChildren<Canvas>(true);
        if (canvas == null)
            return;

        Camera main = Camera.main;
        if (main != null)
            HudCanvasCameraSync.BindCanvasToCamera(canvas, main);
        else if (canvas.renderMode == RenderMode.ScreenSpaceCamera && canvas.worldCamera == null)
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        if (canvas.sortingOrder < 110)
            canvas.sortingOrder = 110;
    }

    private void SetMenuVisible(bool visible)
    {
        if (menuRoot != null)
            menuRoot.SetActive(visible);
    }

    #endregion

    #region Visuals

    private void RefreshInfo()
    {
        if (infoTitleText != null)
            infoTitleText.text = "Choose a Destination";

        string body;
        if (selectedIndex >= 0 && selectedIndex < loadableDestinations.Count)
        {
            Destination dest = loadableDestinations[selectedIndex];
            body = "Fly to <b>" + dest.displayName + "</b>?\nConfirm to board the rocket.";
        }
        else
        {
            body = "Pick where Kit's rocket should fly.";
        }

        body += "\n<size=80%><b>Up</b>/<b>Down</b>: choose   <b>Confirm</b>: launch   <b>Back</b>: cancel</size>";
        if (infoBodyText != null)
            infoBodyText.text = body;
    }

    private void RefreshHighlight()
    {
        int activeCount = Mathf.Min(entries.Count, loadableDestinations.Count);
        for (int i = 0; i < activeCount; i++)
            SetButtonHighlighted(entries[i], i == selectedIndex && isOpen);

        if (!isOpen || activeCount == 0)
            return;

        Button selected = entries[Mathf.Clamp(selectedIndex, 0, activeCount - 1)].button;
        EventSystem es = EventSystem.current;
        if (es != null && selected != null)
            es.SetSelectedGameObject(selected.gameObject);
    }

    private void ResetButtonVisuals()
    {
        for (int i = 0; i < entries.Count; i++)
            SetButtonHighlighted(entries[i], false);
    }

    private void SetButtonHighlighted(Entry entry, bool selected)
    {
        if (entry == null || entry.button == null)
            return;

        float scaleMul = selected ? (1f + Mathf.Max(0f, selectedScaleBonus)) : 1f;
        entry.button.transform.localScale = entry.baseScale * scaleMul;

        Color tint = selected ? selectedButtonColor : normalButtonColor;

        ColorBlock colors = entry.button.colors;
        colors.normalColor = tint;
        colors.selectedColor = tint;
        colors.highlightedColor = tint;
        entry.button.colors = colors;

        if (entry.image != null)
            entry.image.color = tint;

        Outline outline = entry.button.GetComponent<Outline>();
        if (outline == null)
            outline = entry.button.gameObject.AddComponent<Outline>();
        outline.effectColor = outlineColor;
        outline.effectDistance = outlineDistance;
        outline.enabled = selected;
    }

    private bool IsOwnedSelection(GameObject selected)
    {
        if (selected == null)
            return false;

        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].button == null)
                continue;
            if (selected == entries[i].button.gameObject ||
                selected.transform.IsChildOf(entries[i].button.transform))
                return true;
        }

        return false;
    }

    #endregion

    #region Helpers

    private static bool AnyUpgradeShopOpen()
    {
        UpgradeShop[] shops = FindObjectsByType<UpgradeShop>(FindObjectsSortMode.None);
        for (int i = 0; i < shops.Length; i++)
        {
            if (shops[i] != null && shops[i].IsOpen)
                return true;
        }

        return false;
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null)
            return;

        GameObject go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
    }

    private static TMP_Text FindLabelText(Button button)
    {
        Transform t = button.transform;
        Transform level = FindDeep(t, "Level text");
        Transform equip = FindDeep(t, "Equip Button");
        for (int i = 0; i < t.childCount; i++)
        {
            Transform child = t.GetChild(i);
            if (child == level || child == equip)
                continue;
            if (child.TryGetComponent(out TMP_Text text))
                return text;
        }

        return button.GetComponentInChildren<TMP_Text>(true);
    }

    private static void SetButtonLabelFallback(Button button, string text)
    {
        if (button == null)
            return;

        TMP_Text tmp = button.GetComponentInChildren<TMP_Text>(true);
        if (tmp != null)
            tmp.text = text ?? string.Empty;
    }

    private static TMP_Text FindText(Transform root, string objectName)
    {
        Transform t = FindDeep(root, objectName);
        return t != null ? t.GetComponent<TMP_Text>() : null;
    }

    private static Transform FindDeep(Transform root, string objectName)
    {
        if (root == null)
            return null;

        if (string.Equals(root.name, objectName, StringComparison.OrdinalIgnoreCase))
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindDeep(root.GetChild(i), objectName);
            if (found != null)
                return found;
        }

        return null;
    }

    private static Transform FindDeepStartingWith(Transform root, string prefix)
    {
        if (root == null)
            return null;

        if (root.name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindDeepStartingWith(root.GetChild(i), prefix);
            if (found != null)
                return found;
        }

        return null;
    }

    private static GameObject FindNamedIncludingInactive(string objectName)
    {
        if (string.IsNullOrEmpty(objectName))
            return null;

        GameObject[] all = FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            GameObject go = all[i];
            if (go == null || !go.scene.IsValid())
                continue;
            if (string.Equals(go.name, objectName, StringComparison.Ordinal))
                return go;
        }

        return null;
    }

    #endregion

#if UNITY_EDITOR
    private void OnValidate()
    {
        menuInputCooldown = Mathf.Max(0.05f, menuInputCooldown);
        openInputDelay = Mathf.Max(0f, openInputDelay);
        selectedScaleBonus = Mathf.Max(0f, selectedScaleBonus);
    }
#endif
}
