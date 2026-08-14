using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Mega Man 2–style Pause B inventory on Pause Canvas B.
/// Start / Pause toggles it. Navigate item boxes like Kaboodle; Confirm uses the selected item.
/// Kit and Malice swap inventory/item sprites via Inspector slots.
/// </summary>
public class PauseBInventory : MonoBehaviour
{
    public static PauseBInventory Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureExistsInScene()
    {
        if (Instance != null)
            return;

        GameObject canvas = GameObject.Find("Pause Canvas B");
        if (canvas == null)
            return;

        if (canvas.GetComponent<PauseBInventory>() == null)
            canvas.AddComponent<PauseBInventory>();
    }

    [Serializable]
    public class CharacterInventorySprites
    {
        public Sprite inventoryBox;
        public Sprite itemBox1;
        public Sprite itemBox2;
        public Sprite juiceIcon;
        public Sprite hotdogIcon;
    }

    [Header("Roots")]
    [Tooltip("Usually the Inventory Box child under Pause Canvas B. Hidden until opened.")]
    [SerializeField] private GameObject inventoryRoot;
    [SerializeField] private GameObject pauseCanvasRoot;

    [Header("Sprites (shared Image / SpriteRenderer targets)")]
    [SerializeField] private SpriteRenderer inventoryBoxSprite;
    [SerializeField] private SpriteRenderer itemBox1Sprite;
    [SerializeField] private SpriteRenderer itemBox2Sprite;
    [SerializeField] private SpriteRenderer juiceIconSprite;
    [SerializeField] private SpriteRenderer hotdogIconSprite;

    [Header("Kit Sprites")]
    [SerializeField] private CharacterInventorySprites kitSprites = new CharacterInventorySprites();

    [Header("Malice Sprites")]
    [SerializeField] private CharacterInventorySprites maliceSprites = new CharacterInventorySprites();

    [Header("Text (shared for both characters)")]
    [SerializeField] private TMP_Text juiceAmountText;
    [SerializeField] private TMP_Text hotdogAmountText;
    [SerializeField] private TMP_Text livesText;

    [Header("Selectable Item Boxes (not Buttons)")]
    [SerializeField] private Transform itemBox1Root;
    [SerializeField] private Transform itemBox2Root;
    [Tooltip("Also highlighted when Item Box 1 is selected.")]
    [SerializeField] private SpriteRenderer juiceHighlightSource;
    [Tooltip("Also highlighted when Item Box 2 is selected.")]
    [SerializeField] private SpriteRenderer hotdogHighlightSource;

    [Header("Selection Look")]
    [SerializeField] private Color highlightColor = new Color(0f, 0.45f, 1f, 1f);
    [SerializeField] private float highlightScale = 1.12f;
    [SerializeField] private int highlightSortingOffset = -1;

    [Header("Input")]
    [SerializeField] [Range(0.1f, 1f)] private float stickThreshold = 0.5f;
    [SerializeField] private float menuInputCooldown = 0.18f;

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

    private Material highlightMaterial;
    private SpriteRenderer itemBox1Highlight;
    private SpriteRenderer itemBox2Highlight;
    private SpriteRenderer juiceHighlight;
    private SpriteRenderer hotdogHighlight;

    private float lastToggleUnscaledTime = -999f;
    private bool shopPreviewVisible;

    public bool IsOpen => isOpen;
    public bool IsShopPreviewVisible => shopPreviewVisible;

    private void Awake()
    {
        Instance = this;
        controls = new InputActions();
        AutoBindIfNeeded();
        FixPauseCanvasSetup();
        EnsureHighlights();

        if (inventoryRoot != null)
            inventoryRoot.SetActive(false);

        isOpen = false;
        RefreshAllVisuals();
    }

    /// <summary>
    /// Pause Canvas B was saved as Screen Space Camera with no camera and scale 0,
    /// which makes the inventory invisible even when Inventory Box is active.
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

        // SpriteRenderer inventory art needs to draw above gameplay; keep canvas sorting high.
        if (canvas.sortingOrder < 50)
            canvas.sortingOrder = 55;

        // Force a canvas rebuild once the camera is valid (fixes scale staying at 0).
        Canvas.ForceUpdateCanvases();
    }

    private void OnEnable()
    {
        if (controls == null)
            controls = new InputActions();

        controls.PlayerControls.Enable();
        controls.PlayerControls.Pause.performed += OnPauseOrStart;
        controls.PlayerControls.Start.performed += OnPauseOrStart;
        controls.PlayerControls.Confirm.performed += OnConfirmPerformed;
        controls.PlayerControls.QuitBack.performed += OnQuitBackPerformed;
        controls.PlayerControls.Up.performed += OnUpPerformed;
        controls.PlayerControls.Down.performed += OnDownPerformed;
        controls.PlayerControls.Left.performed += OnLeftPerformed;
        controls.PlayerControls.Right.performed += OnRightPerformed;
        controls.PlayerControls.Movement.performed += OnMovementPerformed;
        controls.PlayerControls.Movement.canceled += OnMovementCanceled;

        if (PlayerInventory.Instance != null)
            PlayerInventory.Instance.OnInventoryChanged += RefreshCounts;

        if (PlayerSpawner.Instance != null)
        {
            PlayerSpawner.Instance.OnCharacterChanged += OnCharacterChanged;
            PlayerSpawner.Instance.OnPlayerSpawned += OnPlayerSpawned;
        }
    }

    private void OnDisable()
    {
        if (controls != null)
        {
            controls.PlayerControls.Pause.performed -= OnPauseOrStart;
            controls.PlayerControls.Start.performed -= OnPauseOrStart;
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

        if (PlayerInventory.Instance != null)
            PlayerInventory.Instance.OnInventoryChanged -= RefreshCounts;

        if (PlayerSpawner.Instance != null)
        {
            PlayerSpawner.Instance.OnCharacterChanged -= OnCharacterChanged;
            PlayerSpawner.Instance.OnPlayerSpawned -= OnPlayerSpawned;
        }

        if (isOpen)
            ForceClose();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        if (highlightMaterial != null)
        {
            Destroy(highlightMaterial);
            highlightMaterial = null;
        }

        if (controls != null)
        {
            controls.Dispose();
            controls = null;
        }
    }

    private void Start()
    {
        // Spawner may appear after us.
        if (PlayerSpawner.Instance != null)
        {
            PlayerSpawner.Instance.OnCharacterChanged -= OnCharacterChanged;
            PlayerSpawner.Instance.OnPlayerSpawned -= OnPlayerSpawned;
            PlayerSpawner.Instance.OnCharacterChanged += OnCharacterChanged;
            PlayerSpawner.Instance.OnPlayerSpawned += OnPlayerSpawned;
        }

        if (PlayerInventory.Instance != null)
        {
            PlayerInventory.Instance.OnInventoryChanged -= RefreshCounts;
            PlayerInventory.Instance.OnInventoryChanged += RefreshCounts;
        }

        RefreshAllVisuals();
    }

    private void AutoBindIfNeeded()
    {
        if (pauseCanvasRoot == null)
            pauseCanvasRoot = gameObject;

        if (inventoryRoot == null)
        {
            Transform found = transform.Find("Inventory Box");
            if (found != null)
                inventoryRoot = found.gameObject;
        }

        if (inventoryRoot == null)
            return;

        Transform root = inventoryRoot.transform;

        if (inventoryBoxSprite == null)
            inventoryBoxSprite = FindSpriteRenderer(root, "Inventory Box sprite");

        if (itemBox1Root == null)
        {
            Transform t = FindDeep(root, "Inventroy Box item 1 box");
            if (t == null)
                t = FindDeep(root, "Inventory Box item 1 box");
            itemBox1Root = t;
        }

        if (itemBox2Root == null)
        {
            Transform t = FindDeep(root, "Inventroy Box item 2 box");
            if (t == null)
                t = FindDeep(root, "Inventory Box item 2 box");
            itemBox2Root = t;
        }

        if (itemBox1Sprite == null && itemBox1Root != null)
            itemBox1Sprite = itemBox1Root.GetComponent<SpriteRenderer>();

        if (itemBox2Sprite == null && itemBox2Root != null)
            itemBox2Sprite = itemBox2Root.GetComponent<SpriteRenderer>();

        if (juiceIconSprite == null)
            juiceIconSprite = FindSpriteRenderer(root, "Juice Item Box image");

        if (hotdogIconSprite == null)
            hotdogIconSprite = FindSpriteRenderer(root, "HotDog Item box image");

        juiceHighlightSource = juiceIconSprite;
        hotdogHighlightSource = hotdogIconSprite;

        if (juiceAmountText == null)
        {
            Transform t = FindDeep(root, "Amount of item text");
            if (t != null)
                juiceAmountText = t.GetComponent<TMP_Text>();
        }

        if (hotdogAmountText == null)
        {
            Transform t = FindDeep(root, "Amount of item text 2");
            if (t != null)
                hotdogAmountText = t.GetComponent<TMP_Text>();
        }

        if (livesText == null)
        {
            Transform t = FindDeep(root, "Lives");
            if (t != null)
                livesText = t.GetComponent<TMP_Text>();
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

    private static SpriteRenderer FindSpriteRenderer(Transform parent, string name)
    {
        Transform t = FindDeep(parent, name);
        return t != null ? t.GetComponent<SpriteRenderer>() : null;
    }

    private void EnsureHighlights()
    {
        EnsureHighlightMaterial();
        itemBox1Highlight = EnsureHighlightFor(itemBox1Sprite, itemBox1Highlight);
        itemBox2Highlight = EnsureHighlightFor(itemBox2Sprite, itemBox2Highlight);
        juiceHighlight = EnsureHighlightFor(juiceHighlightSource, juiceHighlight);
        hotdogHighlight = EnsureHighlightFor(hotdogHighlightSource, hotdogHighlight);
    }

    private void EnsureHighlightMaterial()
    {
        if (highlightMaterial != null)
            return;

        Shader shader = Shader.Find("Gameoverse/SpriteSolidColor");
        if (shader != null)
            highlightMaterial = new Material(shader);
    }

    private SpriteRenderer EnsureHighlightFor(SpriteRenderer source, SpriteRenderer existing)
    {
        if (source == null)
            return existing;

        if (existing != null)
            return existing;

        const string highlightName = "HighlightShape";
        Transform child = source.transform.Find(highlightName);
        GameObject go;
        if (child != null)
        {
            go = child.gameObject;
        }
        else
        {
            go = new GameObject(highlightName);
            go.transform.SetParent(source.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * highlightScale;
        }

        SpriteRenderer sr = go.GetComponent<SpriteRenderer>();
        if (sr == null)
            sr = go.AddComponent<SpriteRenderer>();

        sr.sprite = source.sprite;
        sr.color = highlightColor;
        sr.sortingLayerID = source.sortingLayerID;
        sr.sortingOrder = source.sortingOrder + highlightSortingOffset;
        sr.flipX = source.flipX;
        sr.flipY = source.flipY;
        if (highlightMaterial != null)
            sr.sharedMaterial = highlightMaterial;

        go.SetActive(false);
        go.transform.SetAsFirstSibling();
        return sr;
    }

    private void OnPauseOrStart(InputAction.CallbackContext context)
    {
        if (!context.performed)
            return;

        // Gamepad Start is bound to BOTH Pause and Start — without this, Open+Close fire in one frame.
        if (Time.unscaledTime - lastToggleUnscaledTime < 0.12f)
            return;
        lastToggleUnscaledTime = Time.unscaledTime;

        if (isOpen)
        {
            SoundManager.Instance?.PlayUiConfirmOrBack();
            Close();
        }
        else
        {
            SoundManager.Instance?.PlayUiConfirmOrBack();
            Open();
        }
    }

    private void OnQuitBackPerformed(InputAction.CallbackContext context)
    {
        if (!context.performed || !isOpen)
            return;

        SoundManager.Instance?.PlayUiConfirmOrBack();
        Close();
    }

    private void OnConfirmPerformed(InputAction.CallbackContext context)
    {
        if (!context.performed || !isOpen)
            return;

        SoundManager.Instance?.PlayUiConfirmOrBack();
        UseSelectedItem();
    }

    private void OnUpPerformed(InputAction.CallbackContext context)
    {
        if (!context.performed || !isOpen || !CanAcceptMenuInput())
            return;
        MoveSelection(-1);
    }

    private void OnDownPerformed(InputAction.CallbackContext context)
    {
        if (!context.performed || !isOpen || !CanAcceptMenuInput())
            return;
        MoveSelection(1);
    }

    private void OnLeftPerformed(InputAction.CallbackContext context)
    {
        if (!context.performed || !isOpen || !CanAcceptMenuInput())
            return;
        MoveSelection(-1);
    }

    private void OnRightPerformed(InputAction.CallbackContext context)
    {
        if (!context.performed || !isOpen || !CanAcceptMenuInput())
            return;
        MoveSelection(1);
    }

    private void OnMovementPerformed(InputAction.CallbackContext context)
    {
        if (!isOpen)
            return;

        Vector2 v = context.ReadValue<Vector2>();
        bool up = v.y >= stickThreshold;
        bool down = v.y <= -stickThreshold;
        bool left = v.x <= -stickThreshold;
        bool right = v.x >= stickThreshold;

        if (up && !moveUpHeld && CanAcceptMenuInput())
            MoveSelection(-1);
        if (down && !moveDownHeld && CanAcceptMenuInput())
            MoveSelection(1);
        if (left && !moveLeftHeld && CanAcceptMenuInput())
            MoveSelection(-1);
        if (right && !moveRightHeld && CanAcceptMenuInput())
            MoveSelection(1);

        moveUpHeld = up;
        moveDownHeld = down;
        moveLeftHeld = left;
        moveRightHeld = right;
    }

    private void OnMovementCanceled(InputAction.CallbackContext context)
    {
        moveUpHeld = moveDownHeld = moveLeftHeld = moveRightHeld = false;
    }

    private bool CanAcceptMenuInput()
    {
        if (Time.unscaledTime < nextMenuInputTime)
            return false;
        nextMenuInputTime = Time.unscaledTime + menuInputCooldown;
        return true;
    }

    private void MoveSelection(int delta)
    {
        selectedIndex = (selectedIndex + delta + 2) % 2;
        RefreshHighlights();
    }

    public void Open()
    {
        if (isOpen)
            return;

        // Don't stack on Kaboodle's menu.
        if (KaboodleIsBlocking())
            return;

        // Real pause replaces any shop peek.
        shopPreviewVisible = false;

        isOpen = true;
        selectedIndex = 0;

        if (inventoryRoot != null)
            inventoryRoot.SetActive(true);

        if (freezeTimeWhileOpen)
        {
            savedTimeScale = Time.timeScale;
            Time.timeScale = 0f;
        }

        if (lockPlayerWhileOpen)
            SetPlayersLocked(true);

        RefreshAllVisuals();
    }

    public void Close()
    {
        if (!isOpen)
            return;

        ForceClose();
    }

    /// <summary>
    /// Show Pause B inventory visuals while shopping at Kaboodle.
    /// Does not pause time, lock the player, or take menu navigation.
    /// </summary>
    public void ShowShopPreview()
    {
        if (isOpen)
            return;

        shopPreviewVisible = true;
        if (inventoryRoot != null)
            inventoryRoot.SetActive(true);

        RefreshAllVisuals();
    }

    /// <summary>Hide the shop-only inventory peek (leaves a real Pause B open alone).</summary>
    public void HideShopPreview()
    {
        if (!shopPreviewVisible)
            return;

        shopPreviewVisible = false;
        if (!isOpen && inventoryRoot != null)
            inventoryRoot.SetActive(false);
    }

    private void ForceClose()
    {
        isOpen = false;
        shopPreviewVisible = false;

        if (inventoryRoot != null)
            inventoryRoot.SetActive(false);

        if (freezeTimeWhileOpen)
            Time.timeScale = savedTimeScale > 0f ? savedTimeScale : 1f;

        if (lockPlayerWhileOpen)
            SetPlayersLocked(false);

        RefreshHighlights();
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

    private void UseSelectedItem()
    {
        PlayerInventory inv = PlayerInventory.Instance;
        if (inv == null)
            return;

        bool used = selectedIndex == 0 ? inv.TryUseJuice() : inv.TryUseHotdog();
        if (used)
            RefreshCounts();
    }

    private void OnCharacterChanged(string characterId)
    {
        RefreshCharacterSprites();
        RefreshHighlights();
    }

    private void OnPlayerSpawned(PlayerController player)
    {
        RefreshCharacterSprites();
        RefreshHighlights();
    }

    private void RefreshAllVisuals()
    {
        RefreshCharacterSprites();
        RefreshCounts();
        RefreshHighlights();
    }

    private void RefreshCounts()
    {
        PlayerInventory inv = PlayerInventory.Instance;
        int juice = inv != null ? inv.JuiceCount : 0;
        int hotdogs = inv != null ? inv.HotdogCount : 0;
        int lives = inv != null ? inv.LivesCount : 3;

        if (juiceAmountText != null)
            juiceAmountText.text = FormatCount(juice);

        if (hotdogAmountText != null)
            hotdogAmountText.text = FormatCount(hotdogs);

        if (livesText != null)
            livesText.text = lives.ToString("00");
    }

    private static string FormatCount(int count)
    {
        return ": " + Mathf.Clamp(count, 0, 99).ToString("00");
    }

    private void RefreshCharacterSprites()
    {
        CharacterInventorySprites sprites = GetSpritesForCurrentCharacter();
        ApplySprite(inventoryBoxSprite, sprites.inventoryBox);
        ApplySprite(itemBox1Sprite, sprites.itemBox1);
        ApplySprite(itemBox2Sprite, sprites.itemBox2);
        ApplySprite(juiceIconSprite, sprites.juiceIcon);
        ApplySprite(hotdogIconSprite, sprites.hotdogIcon);

        SyncHighlightSprite(itemBox1Highlight, itemBox1Sprite);
        SyncHighlightSprite(itemBox2Highlight, itemBox2Sprite);
        SyncHighlightSprite(juiceHighlight, juiceHighlightSource);
        SyncHighlightSprite(hotdogHighlight, hotdogHighlightSource);
    }

    private CharacterInventorySprites GetSpritesForCurrentCharacter()
    {
        string id = PlayerSpawner.SelectedCharacterId;
        if (PlayerSpawner.Instance != null && PlayerSpawner.Instance.CurrentPlayer != null)
            id = PlayerSpawner.Instance.CurrentPlayer.CharacterId;

        if (!string.IsNullOrEmpty(id) &&
            id.Equals("Malice", StringComparison.OrdinalIgnoreCase))
            return maliceSprites;

        return kitSprites;
    }

    private static void ApplySprite(SpriteRenderer renderer, Sprite sprite)
    {
        if (renderer == null || sprite == null)
            return;
        renderer.sprite = sprite;
    }

    private void SyncHighlightSprite(SpriteRenderer highlight, SpriteRenderer source)
    {
        if (highlight == null || source == null)
            return;

        highlight.sprite = source.sprite;
        highlight.color = highlightColor;
        highlight.sortingLayerID = source.sortingLayerID;
        highlight.sortingOrder = source.sortingOrder + highlightSortingOffset;
        highlight.transform.localScale = Vector3.one * highlightScale;
        if (highlightMaterial != null)
            highlight.sharedMaterial = highlightMaterial;
    }

    private void RefreshHighlights()
    {
        bool show = isOpen;
        bool box1 = show && selectedIndex == 0;
        bool box2 = show && selectedIndex == 1;

        SetHighlightActive(itemBox1Highlight, box1);
        SetHighlightActive(juiceHighlight, box1);
        SetHighlightActive(itemBox2Highlight, box2);
        SetHighlightActive(hotdogHighlight, box2);
    }

    private static void SetHighlightActive(SpriteRenderer highlight, bool active)
    {
        if (highlight != null)
            highlight.gameObject.SetActive(active);
    }
}
