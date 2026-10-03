using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Kaboodle NPC hub: interact prompt in range, then a box with menu pages
/// (Tutorial, Item Shop, Character Select, Boss Fight).
/// Menu supports full 2D navigation (rows + columns). Selected/hovered buttons
/// scale up and show an outline.
/// </summary>
public class Kaboodle : MonoBehaviour
{
    public enum BoxPage
    {
        None = 0,
        Menu = 1,
        Tutorial = 2,
        ItemShop = 3,
        CharacterSelect = 4,
        BossFight = 5
    }

    [Header("Range")]
    [Tooltip("How close the player must be (world units) to see the interact prompt.")]
    [SerializeField] private float interactRadius = 2f;

    [Header("UI - Prompt")]
    [Tooltip("Shown only while the player is in range and the box is closed.")]
    [SerializeField] private GameObject interactPrompt;
    [Tooltip("Scale multiplier for PlayStation / Xbox / Switch Confirm glyphs (not Keyboard or Default).")]
    [SerializeField] private float controllerPromptScaleMultiplier = 2f;

    private SpriteRenderer interactPromptRenderer;
    private Sprite interactPromptFallbackSprite;
    private Vector3 interactPromptBaseScale = Vector3.one;
    private bool interactPromptBaseScaleCached;
    private bool subscribedToButtonSprites;

    [Header("UI - Kaboodle Box")]
    [Tooltip("Root of the Kaboodle dialogue/menu box. Hidden until the player interacts.")]
    [SerializeField] private GameObject kaboodleBox;

    [Tooltip("Main button list page (Tutorial / Item Shop / Character Select / Boss Fight).")]
    [SerializeField] private GameObject menuPage;

    [SerializeField] private GameObject tutorialPage;
    [Tooltip("Main tutorial body under Tutorial Page (TMP). Auto-finds Text (TMP) if empty.")]
    [SerializeField] private TMP_Text tutorialBodyText;
    [Tooltip("Bottom footer hint: Back / Confirm / Select for the active controller. Auto-finds by name if empty.")]
    [SerializeField] private TMP_Text navigationHintText;
    [SerializeField] private GameObject itemShopPage;
    [SerializeField] private GameObject characterSelectPage;
    [SerializeField] private GameObject bossFightPage;

    [Header("UI - Menu Buttons (row-major order: left→right, top→bottom)")]
    [SerializeField] private Button tutorialButton;
    [SerializeField] private Button itemShopButton;
    [SerializeField] private Button characterSelectButton;
    [SerializeField] private Button bossFightButton;
    [Tooltip("How many buttons per row (e.g. 2 = two columns).")]
    [SerializeField] private int menuColumns = 2;

    [Header("UI - Character Select")]
    [SerializeField] private Button kitCharacterButton;
    [SerializeField] private Button maliceCharacterButton;
    [SerializeField] private Button harlieCharacterButton;
    [SerializeField] private Button countCharacterButton;
    [SerializeField] private Button hexCharacterButton;
    [Tooltip("How many character buttons per row (usually 2: Kit | Malice).")]
    [SerializeField] private int characterSelectColumns = 2;
    [Tooltip("Solid blue character-shaped highlight behind the portrait when hovered/selected.")]
    [SerializeField] private Color characterOutlineColor = new Color(0f, 0.45f, 1f, 1f);
    [Tooltip("How much bigger the solid blue highlight is than the portrait (UI units).")]
    [SerializeField] private float characterOutlineThickness = 11f;
    [Tooltip("Close Kaboodle after a successful character switch.")]
    [SerializeField] private bool closeBoxAfterCharacterSwitch = true;

    [Header("UI - Boss Fight Select")]
    [Tooltip("Same layout as Character Select. Parent Kit/Malice/Harlie buttons under Boss Fight Page. " +
             "Each button picks a boss id; the Boss Spawner maps that id to the boss prefab.")]
    [SerializeField] private Button kitBossButton;
    [SerializeField] private Button maliceBossButton;
    [Tooltip("Spawns Boss Harlie (BossHarlie prefab via the Boss Spawner's \"Harlie\" entry).")]
    [SerializeField] private Button harlieBossButton;
    [Tooltip("Spawns Boss Hex (BossHex prefab via the Boss Spawner's \"Hex\" entry).")]
    [SerializeField] private Button hexBossButton;
    [Tooltip("Spawns Boss Count (BossCount prefab via the Boss Spawner's \"Count\" entry).")]
    [SerializeField] private Button countBossButton;
    [SerializeField] private int bossSelectColumns = 2;

    [Header("UI - Item Shop")]
    [SerializeField] private Button juiceShopButton;
    [SerializeField] private Button hotdogShopButton;
    [Tooltip("Temporary Kit attack-style placeholder (Spread Shot).")]
    [SerializeField] private Button spreadShotShopButton;
    [Tooltip("Temporary Kit attack-style placeholder (Machine Gun).")]
    [SerializeField] private Button machineGunShopButton;
    [Tooltip("How many shop buttons per row (usually 2).")]
    [SerializeField] private int itemShopColumns = 2;
    [Tooltip("Selected/hovered item buttons grow by this much (smaller than character select).")]
    [SerializeField] private float itemShopSelectedScaleBonus = 0.25f;
    [Tooltip("Optional. Auto-found under each item button if left empty.")]
    [SerializeField] private SpriteRenderer juiceItemTextContainer;
    [SerializeField] private SpriteRenderer hotdogItemTextContainer;
    [SerializeField] private SpriteRenderer spreadShotItemTextContainer;
    [SerializeField] private SpriteRenderer machineGunItemTextContainer;

    [Header("Selection Look (main menu)")]
    [SerializeField] private Color normalButtonColor = Color.white;
    [SerializeField] private Color selectedButtonColor = new Color(1f, 0.92f, 0.45f, 1f);
    [Tooltip("Selected/hovered menu buttons grow by this much (1 + 0.8 = scale 1.8). Character select uses this too.")]
    [SerializeField] private float selectedScaleBonus = 0.8f;
    [SerializeField] private Color outlineColor = new Color(1f, 0.95f, 0.35f, 1f);
    [SerializeField] private Vector2 outlineDistance = new Vector2(2.5f, 2.5f);

    [Header("Input")]
    [Tooltip("Stick axis past this counts as a menu / interact direction.")]
    [SerializeField] [Range(0.1f, 1f)] private float stickThreshold = 0.5f;
    [Tooltip("Small cooldown so one press does not open the box and move the menu in the same frame.")]
    [SerializeField] private float menuInputCooldown = 0.18f;

    [Header("Boss Fight (stub ready)")]
    [SerializeField] private string bossFightSceneName = "Boss Fight Mode";

    [Header("Events (optional hooks for later content)")]
    public UnityEvent onOpened;
    public UnityEvent onClosed;
    public UnityEvent onOpenedTutorial;
    public UnityEvent onOpenedItemShop;
    public UnityEvent onOpenedCharacterSelect;
    public UnityEvent onOpenedBossFight;

    private InputActions controls;
    private bool playerInRange;
    private bool boxOpen;
    private BoxPage currentPage = BoxPage.None;
    private int menuIndex;
    private int characterIndex;
    private int bossIndex;
    private int shopIndex;
    private int tutorialPartIndex;
    private readonly List<string> tutorialParts = new List<string>(5);
    private float nextMenuInputTime;
    private bool moveUpHeld;
    private bool moveDownHeld;
    private bool moveLeftHeld;
    private bool moveRightHeld;
    private PlayerController rangedPlayer;

    private readonly Dictionary<Button, Vector3> buttonBaseScales = new Dictionary<Button, Vector3>();
    private readonly Dictionary<Button, Image> characterHighlightShapes = new Dictionary<Button, Image>();
    private Material characterHighlightMaterial;
    private readonly List<Button> validMenuButtons = new List<Button>(4);
    private readonly List<Button> validCharacterButtons = new List<Button>(2);
    private readonly List<string> characterButtonIds = new List<string>(2);
    private readonly List<Button> validBossButtons = new List<Button>(2);
    private readonly List<string> bossButtonIds = new List<string>(2);
    private readonly HashSet<Button> bossHoverHooked = new HashSet<Button>();
    private readonly List<Button> validShopButtons = new List<Button>(2);
    private readonly HashSet<Button> shopHoverHooked = new HashSet<Button>();
    private readonly Dictionary<Button, SpriteRenderer> shopItemTextContainers =
        new Dictionary<Button, SpriteRenderer>();
    private readonly Dictionary<SpriteRenderer, Color> shopContainerBaseColors =
        new Dictionary<SpriteRenderer, Color>();
    private readonly Dictionary<SpriteRenderer, Vector3> shopContainerBaseLocalPositions =
        new Dictionary<SpriteRenderer, Vector3>();
    private readonly Dictionary<SpriteRenderer, int> shopContainerBaseSortingOrders =
        new Dictionary<SpriteRenderer, int>();
    private readonly Dictionary<Button, Vector3> shopIconBaseScales = new Dictionary<Button, Vector3>();

    public bool IsBoxOpen => boxOpen;
    public BoxPage CurrentPage => currentPage;
    public bool PlayerInRange => playerInRange;

    private void Awake()
    {
        controls = new InputActions();
        CacheMenuButtons();
        CacheCharacterButtons();
        CacheBossButtons();
        CacheShopButtons();
        CacheButtonVisualDefaults();
        CacheInteractPrompt();
        HookPointerHover();
        HookCharacterPointerHover();
        HookBossPointerHover();
        HookShopPointerHover();
        EnsureCharacterHighlightShapes();
        EnsureBossHighlightShapes();
        ButtonSpriteManager.EnsureExists();
        SetPromptVisible(false);
        CloseBox(unlockPlayer: false);
    }

    private void OnEnable()
    {
        if (controls == null)
            controls = new InputActions();

        controls.PlayerControls.Enable();
        controls.PlayerControls.Up.performed += OnUpPerformed;
        controls.PlayerControls.Down.performed += OnDownPerformed;
        controls.PlayerControls.Left.performed += OnLeftPerformed;
        controls.PlayerControls.Right.performed += OnRightPerformed;
        controls.PlayerControls.Confirm.performed += OnConfirmPerformed;
        controls.PlayerControls.Attack.performed += OnAttackPerformed;
        controls.PlayerControls.Select.performed += OnSelectPerformed;
        controls.PlayerControls.QuitBack.performed += OnQuitBackPerformed;
        controls.PlayerControls.Movement.performed += OnMovementPerformed;
        controls.PlayerControls.Movement.canceled += OnMovementCanceled;

        WireMenuButtonClicks(true);
        WireBossButtonClicks(true);
        WireShopButtonClicks(true);
        SubscribeButtonSpriteEvents(true);
    }

    private void OnDisable()
    {
        WireMenuButtonClicks(false);
        WireBossButtonClicks(false);
        WireShopButtonClicks(false);
        SubscribeButtonSpriteEvents(false);

        if (controls == null)
            return;

        controls.PlayerControls.Up.performed -= OnUpPerformed;
        controls.PlayerControls.Down.performed -= OnDownPerformed;
        controls.PlayerControls.Left.performed -= OnLeftPerformed;
        controls.PlayerControls.Right.performed -= OnRightPerformed;
        controls.PlayerControls.Confirm.performed -= OnConfirmPerformed;
        controls.PlayerControls.Attack.performed -= OnAttackPerformed;
        controls.PlayerControls.Select.performed -= OnSelectPerformed;
        controls.PlayerControls.QuitBack.performed -= OnQuitBackPerformed;
        controls.PlayerControls.Movement.performed -= OnMovementPerformed;
        controls.PlayerControls.Movement.canceled -= OnMovementCanceled;
        controls.PlayerControls.Disable();

        ClearPlayerLocks();
    }

    private void OnDestroy()
    {
        ClearPlayerLocks();

        if (characterHighlightMaterial != null)
        {
            Destroy(characterHighlightMaterial);
            characterHighlightMaterial = null;
        }

        if (controls != null)
        {
            controls.Dispose();
            controls = null;
        }
    }

    private void Update()
    {
        UpdateRangeState();
    }

    private void CacheMenuButtons()
    {
        validMenuButtons.Clear();
        AddValidButton(validMenuButtons, tutorialButton);
        AddValidButton(validMenuButtons, itemShopButton);
        AddValidButton(validMenuButtons, characterSelectButton);
        AddValidButton(validMenuButtons, bossFightButton);
    }

    private void CacheCharacterButtons()
    {
        validCharacterButtons.Clear();
        characterButtonIds.Clear();

        // Auto-wire if Inspector refs were left empty (common scene setup miss).
        if (kitCharacterButton == null)
            kitCharacterButton = FindNamedButtonUnder(characterSelectPage, "Kit");
        if (maliceCharacterButton == null)
            maliceCharacterButton = FindNamedButtonUnder(characterSelectPage, "Malice");
        if (harlieCharacterButton == null)
            harlieCharacterButton = FindNamedButtonUnder(characterSelectPage, "Harlie");
        if (countCharacterButton == null)
            countCharacterButton = FindNamedButtonUnder(characterSelectPage, "Count");
        if (hexCharacterButton == null)
            hexCharacterButton = FindNamedButtonUnder(characterSelectPage, "Hex");

        AddCharacterButton(kitCharacterButton, "Kit");
        AddCharacterButton(maliceCharacterButton, "Malice");
        AddCharacterButton(harlieCharacterButton, "Harlie");
        AddCharacterButton(countCharacterButton, "Count");
        AddCharacterButton(hexCharacterButton, "Hex");

        if (validCharacterButtons.Count == 0)
            Debug.LogWarning("[Kaboodle] Character Select has no Kit/Malice/Harlie/Count/Hex buttons assigned.");
    }

    private void CacheBossButtons()
    {
        validBossButtons.Clear();
        bossButtonIds.Clear();

        if (kitBossButton == null)
            kitBossButton = FindNamedButtonUnder(bossFightPage, "Kit");
        if (maliceBossButton == null)
            maliceBossButton = FindNamedButtonUnder(bossFightPage, "Malice");
        if (harlieBossButton == null)
            harlieBossButton = FindNamedButtonUnder(bossFightPage, "Harlie");
        if (hexBossButton == null)
            hexBossButton = FindNamedButtonUnder(bossFightPage, "Hex");
        if (countBossButton == null)
            countBossButton = FindNamedButtonUnder(bossFightPage, "Count");

        AddBossButton(kitBossButton, BossEncounter.BossIdKit);
        AddBossButton(maliceBossButton, BossEncounter.BossIdMalice);
        AddBossButton(harlieBossButton, BossEncounter.BossIdHarlie);
        AddBossButton(hexBossButton, BossEncounter.BossIdHex);
        AddBossButton(countBossButton, BossEncounter.BossIdCount);

        if (validBossButtons.Count == 0)
            Debug.LogWarning(
                "[Kaboodle] Boss Fight page has no Kit/Malice/Harlie/Hex buttons. " +
                "Parent the same buttons under Boss Fight Page (or assign the Boss Button refs).");
    }

    private void CacheShopButtons()
    {
        validShopButtons.Clear();
        shopItemTextContainers.Clear();
        shopContainerBaseColors.Clear();
        shopContainerBaseLocalPositions.Clear();
        shopContainerBaseSortingOrders.Clear();

        if (juiceShopButton == null)
            juiceShopButton = FindNamedButtonUnder(itemShopPage, "Juice");
        if (spreadShotShopButton == null)
            spreadShotShopButton = FindNamedButtonUnder(itemShopPage, "Spread");
        if (hotdogShopButton == null)
            hotdogShopButton = FindNamedButtonUnder(itemShopPage, "HotDog")
                               ?? FindNamedButtonUnder(itemShopPage, "Hotdog");
        if (machineGunShopButton == null)
            machineGunShopButton = FindNamedButtonUnder(itemShopPage, "Machine Gun")
                                   ?? FindNamedButtonUnder(itemShopPage, "MachineGun");

        if (juiceItemTextContainer == null)
            juiceItemTextContainer = FindItemTextContainer(juiceShopButton);
        if (spreadShotItemTextContainer == null)
            spreadShotItemTextContainer = FindItemTextContainer(spreadShotShopButton);
        if (hotdogItemTextContainer == null)
            hotdogItemTextContainer = FindItemTextContainer(hotdogShopButton);
        if (machineGunItemTextContainer == null)
            machineGunItemTextContainer = FindItemTextContainer(machineGunShopButton);

        // Visual row-major: top-left Juice, top-right HotDog, bottom-left Spread, bottom-right Machine Gun.
        AddValidButton(validShopButtons, juiceShopButton);
        AddValidButton(validShopButtons, hotdogShopButton);
        AddValidButton(validShopButtons, spreadShotShopButton);
        AddValidButton(validShopButtons, machineGunShopButton);

        RegisterShopTextContainer(juiceShopButton, juiceItemTextContainer);
        RegisterShopTextContainer(spreadShotShopButton, spreadShotItemTextContainer);
        RegisterShopTextContainer(hotdogShopButton, hotdogItemTextContainer);
        RegisterShopTextContainer(machineGunShopButton, machineGunItemTextContainer);

        RefreshAttackStyleButtonLabels();

        if (validShopButtons.Count == 0)
            Debug.LogWarning("[Kaboodle] Item Shop has no shop buttons assigned.");
    }

    private void RegisterShopTextContainer(Button button, SpriteRenderer container)
    {
        if (button == null || container == null)
            return;

        shopItemTextContainers[button] = container;
        // Always force fully visible base color (never inherit a bad/tinted alpha).
        Color baseColor = container.color;
        baseColor.a = 1f;
        container.color = baseColor;
        container.enabled = true;
        container.gameObject.SetActive(true);
        container.allowOcclusionWhenDynamic = false;
        shopContainerBaseColors[container] = baseColor;

        if (!shopContainerBaseLocalPositions.ContainsKey(container))
            shopContainerBaseLocalPositions[container] = container.transform.localPosition;

        if (!shopContainerBaseSortingOrders.ContainsKey(container))
            shopContainerBaseSortingOrders[container] = container.sortingOrder;

        Canvas parentCanvas = button.GetComponentInParent<Canvas>();
        int canvasOrder = parentCanvas != null ? parentCanvas.sortingOrder : 10;
        container.sortingOrder = Mathf.Min(shopContainerBaseSortingOrders[container], canvasOrder - 1);
        container.transform.SetAsFirstSibling();
        BringShopForegroundUiToFront(button, container.transform);

        CacheShopIconBaseScale(button);
    }

    private void CacheShopIconBaseScale(Button button)
    {
        if (button == null || shopIconBaseScales.ContainsKey(button))
            return;

        Transform icon = GetShopIconTransform(button);
        if (icon != null)
            shopIconBaseScales[button] = icon.localScale;
    }

    private static Transform GetShopIconTransform(Button button)
    {
        if (button == null)
            return null;

        if (button.targetGraphic != null)
            return button.targetGraphic.transform;

        Image image = button.GetComponentInChildren<Image>(true);
        return image != null ? image.transform : null;
    }

    private static SpriteRenderer FindItemTextContainer(Button button)
    {
        if (button == null)
            return null;

        Transform[] children = button.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            Transform child = children[i];
            if (child == null || child == button.transform)
                continue;

            if (child.name.IndexOf("Item text container", StringComparison.OrdinalIgnoreCase) < 0 &&
                child.name.IndexOf("Item Text Container", StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            SpriteRenderer sr = child.GetComponent<SpriteRenderer>();
            if (sr != null)
                return sr;
        }

        return null;
    }

    private Button FindNamedButtonUnder(GameObject pageRoot, string nameContains)
    {
        Transform searchRoot = pageRoot != null
            ? pageRoot.transform
            : (kaboodleBox != null ? kaboodleBox.transform : transform);

        Button[] buttons = searchRoot.GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            Button button = buttons[i];
            if (button == null)
                continue;

            if (button.name.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0)
                return button;
        }

        return null;
    }

    private Button FindCharacterButtonByName(string nameContains)
    {
        return FindNamedButtonUnder(characterSelectPage, nameContains);
    }

    private void AddCharacterButton(Button button, string characterId)
    {
        if (button == null)
            return;

        validCharacterButtons.Add(button);
        characterButtonIds.Add(characterId);
    }

    private void AddBossButton(Button button, string bossId)
    {
        if (button == null)
            return;

        // Avoid double-adding if Kit/Malice refs point at the same object as Character Select.
        if (validBossButtons.Contains(button))
            return;

        validBossButtons.Add(button);
        bossButtonIds.Add(bossId);
    }

    private static void AddValidButton(List<Button> list, Button button)
    {
        if (button != null)
            list.Add(button);
    }

    private void AddValidButton(Button button)
    {
        AddValidButton(validMenuButtons, button);
    }

    private void CacheButtonVisualDefaults()
    {
        buttonBaseScales.Clear();
        CacheScaleDefaults(validMenuButtons);
        CacheScaleDefaults(validCharacterButtons);
        CacheScaleDefaults(validBossButtons);
        CacheScaleDefaults(validShopButtons);

        for (int i = 0; i < validMenuButtons.Count; i++)
        {
            Button button = validMenuButtons[i];
            if (button == null)
                continue;

            Outline outline = button.GetComponent<Outline>();
            if (outline == null)
                outline = button.gameObject.AddComponent<Outline>();

            outline.effectColor = outlineColor;
            outline.effectDistance = outlineDistance;
            outline.useGraphicAlpha = true;
            outline.enabled = false;
        }

        // Shop buttons: no Outline — keeps Item text container SpriteRenderers visible.
        for (int i = 0; i < validShopButtons.Count; i++)
            CacheShopIconBaseScale(validShopButtons[i]);
    }

    private void CacheScaleDefaults(List<Button> buttons)
    {
        for (int i = 0; i < buttons.Count; i++)
        {
            Button button = buttons[i];
            if (button == null || buttonBaseScales.ContainsKey(button))
                continue;

            buttonBaseScales[button] = button.transform.localScale;
        }
    }

    /// <summary>
    /// One solid blue character-shaped highlight behind each portrait (same silhouette, slightly bigger).
    /// Uses a solid-color UI material so it is true blue, not a tinted copy of the art.
    /// </summary>
    private void EnsureCharacterHighlightShapes()
    {
        CleanupLegacyBordersUnder(characterSelectPage);
        EnsureCharacterHighlightMaterial();
        EnsurePortraitHighlightShapes(validCharacterButtons);
    }

    private void EnsureBossHighlightShapes()
    {
        CleanupLegacyBordersUnder(bossFightPage);
        EnsureCharacterHighlightMaterial();
        EnsurePortraitHighlightShapes(validBossButtons);
    }

    private void EnsurePortraitHighlightShapes(List<Button> buttons)
    {
        if (buttons == null)
            return;

        for (int i = 0; i < buttons.Count; i++)
        {
            Button button = buttons[i];
            if (button == null)
                continue;

            Outline legacyOutline = button.GetComponent<Outline>();
            if (legacyOutline != null)
                legacyOutline.enabled = false;

            Image portrait = GetCharacterPortraitImage(button);
            if (portrait == null)
                continue;

            const string highlightName = "HighlightShape";
            Transform existing = button.transform.Find(highlightName);
            Transform oldBorder = button.transform.Find("SpriteBorder");
            if (oldBorder != null)
                Destroy(oldBorder.gameObject);

            GameObject highlightGo;
            if (existing != null)
            {
                highlightGo = existing.gameObject;
                if (highlightGo.GetComponent<Image>() == null)
                    highlightGo.AddComponent<Image>();
            }
            else
            {
                highlightGo = new GameObject(highlightName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                highlightGo.transform.SetParent(button.transform, false);
            }

            Image highlightImage = highlightGo.GetComponent<Image>();
            ConfigureHighlightImage(highlightImage, portrait);
            RefreshHighlightShapeTransform(highlightImage.rectTransform, portrait.rectTransform);

            highlightGo.transform.SetSiblingIndex(0);
            if (portrait.transform.parent == button.transform)
                portrait.transform.SetSiblingIndex(1);

            highlightGo.SetActive(false);
            characterHighlightShapes[button] = highlightImage;
        }
    }

    private void EnsureCharacterHighlightMaterial()
    {
        if (characterHighlightMaterial != null)
            return;

        Shader shader = Shader.Find("Gameoverse/UISolidColor");
        if (shader == null)
            shader = Shader.Find("Gameoverse/SpriteSolidColor");

        if (shader != null)
            characterHighlightMaterial = new Material(shader);
    }

    private void ConfigureHighlightImage(Image highlightImage, Image portrait)
    {
        if (highlightImage == null || portrait == null)
            return;

        highlightImage.sprite = portrait.sprite;
        highlightImage.type = Image.Type.Simple;
        highlightImage.preserveAspect = portrait.preserveAspect;
        highlightImage.useSpriteMesh = true;
        highlightImage.raycastTarget = false;
        highlightImage.color = GetSolidCharacterOutlineColor();
        if (characterHighlightMaterial != null)
            highlightImage.material = characterHighlightMaterial;
    }

    private void RefreshHighlightShapeTransform(RectTransform highlightRect, RectTransform portraitRect)
    {
        if (highlightRect == null || portraitRect == null)
            return;

        SyncRectTransform(highlightRect, portraitRect);

        float minSide = Mathf.Max(1f, Mathf.Min(Mathf.Abs(portraitRect.rect.width), Mathf.Abs(portraitRect.rect.height)));
        float grow = 1f + (2f * Mathf.Max(1f, characterOutlineThickness) / minSide);
        highlightRect.localScale = portraitRect.localScale * grow;
    }

    /// <summary>Removes older rectangular / multi-outline leftovers.</summary>
    private void CleanupLegacyCharacterBorders()
    {
        CleanupLegacyBordersUnder(characterSelectPage);
    }

    private void CleanupLegacyBordersUnder(GameObject pageRoot)
    {
        Transform searchRoot = pageRoot != null
            ? pageRoot.transform
            : (kaboodleBox != null ? kaboodleBox.transform : transform);

        List<Transform> toDestroy = new List<Transform>(8);
        CollectLegacyBorders(searchRoot, toDestroy);
        for (int i = 0; i < toDestroy.Count; i++)
        {
            if (toDestroy[i] != null)
                Destroy(toDestroy[i].gameObject);
        }
    }

    private static void CollectLegacyBorders(Transform root, List<Transform> results)
    {
        if (root == null)
            return;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            string name = child.name;
            if (name.EndsWith("_BlueBorder", StringComparison.Ordinal) ||
                name == "SilhouetteOutline" ||
                name == "SpriteBorder")
            {
                results.Add(child);
            }

            CollectLegacyBorders(child, results);
        }
    }

    private static Image GetCharacterPortraitImage(Button button)
    {
        if (button == null)
            return null;

        if (button.targetGraphic is Image targetImage &&
            targetImage.sprite != null &&
            !IsHighlightGraphic(targetImage.transform))
        {
            return targetImage;
        }

        Image[] images = button.GetComponentsInChildren<Image>(true);
        for (int i = 0; i < images.Length; i++)
        {
            Image image = images[i];
            if (image == null || image.sprite == null)
                continue;
            if (IsHighlightGraphic(image.transform))
                continue;
            return image;
        }

        return null;
    }

    private static bool IsHighlightGraphic(Transform t)
    {
        if (t == null)
            return false;

        if (t.name == "HighlightShape" ||
            t.name == "SilhouetteOutline" ||
            t.name == "SpriteBorder" ||
            t.name.StartsWith("Border_", StringComparison.Ordinal))
        {
            return true;
        }

        Transform p = t.parent;
        while (p != null)
        {
            if (p.name == "SpriteBorder")
                return true;
            p = p.parent;
        }

        return false;
    }

    private static void SyncRectTransform(RectTransform target, RectTransform source)
    {
        if (target == null || source == null)
            return;

        target.anchorMin = source.anchorMin;
        target.anchorMax = source.anchorMax;
        target.pivot = source.pivot;
        target.anchoredPosition = source.anchoredPosition;
        target.sizeDelta = source.sizeDelta;
        target.localRotation = source.localRotation;
        target.localScale = source.localScale;
    }

    private void RefreshCharacterHighlightShape(Button button, Image portrait, Image highlight, bool selected)
    {
        if (highlight == null)
            return;

        if (portrait != null)
        {
            ConfigureHighlightImage(highlight, portrait);
            RefreshHighlightShapeTransform(highlight.rectTransform, portrait.rectTransform);
            highlight.transform.SetSiblingIndex(0);
            if (portrait.transform.parent == button.transform)
                portrait.transform.SetSiblingIndex(1);
        }

        highlight.gameObject.SetActive(selected);
    }
    private void HookPointerHover()
    {
        for (int i = 0; i < validMenuButtons.Count; i++)
        {
            Button button = validMenuButtons[i];
            if (button == null)
                continue;

            int index = i;
            EventTrigger trigger = button.GetComponent<EventTrigger>();
            if (trigger == null)
                trigger = button.gameObject.AddComponent<EventTrigger>();

            AddPointerCallback(trigger, EventTriggerType.PointerEnter, _ => OnMenuPointerEnter(index));
            AddPointerCallback(trigger, EventTriggerType.PointerExit, _ => OnMenuPointerExit(index));
        }
    }

    private void HookCharacterPointerHover()
    {
        for (int i = 0; i < validCharacterButtons.Count; i++)
        {
            Button button = validCharacterButtons[i];
            if (button == null)
                continue;

            int index = i;
            EventTrigger trigger = button.GetComponent<EventTrigger>();
            if (trigger == null)
                trigger = button.gameObject.AddComponent<EventTrigger>();

            AddPointerCallback(trigger, EventTriggerType.PointerEnter, _ => OnCharacterPointerEnter(index));
            AddPointerCallback(trigger, EventTriggerType.PointerExit, _ => OnCharacterPointerExit(index));
        }
    }

    private void HookBossPointerHover()
    {
        for (int i = 0; i < validBossButtons.Count; i++)
        {
            Button button = validBossButtons[i];
            if (button == null || bossHoverHooked.Contains(button))
                continue;

            bossHoverHooked.Add(button);
            int index = i;
            EventTrigger trigger = button.GetComponent<EventTrigger>();
            if (trigger == null)
                trigger = button.gameObject.AddComponent<EventTrigger>();

            AddPointerCallback(trigger, EventTriggerType.PointerEnter, _ => OnBossPointerEnter(index));
            AddPointerCallback(trigger, EventTriggerType.PointerExit, _ => OnBossPointerExit(index));
        }
    }

    private void HookShopPointerHover()
    {
        for (int i = 0; i < validShopButtons.Count; i++)
        {
            Button button = validShopButtons[i];
            if (button == null || shopHoverHooked.Contains(button))
                continue;

            shopHoverHooked.Add(button);
            int index = i;
            EventTrigger trigger = button.GetComponent<EventTrigger>();
            if (trigger == null)
                trigger = button.gameObject.AddComponent<EventTrigger>();

            AddPointerCallback(trigger, EventTriggerType.PointerEnter, _ => OnShopPointerEnter(index));
            AddPointerCallback(trigger, EventTriggerType.PointerExit, _ => OnShopPointerExit(index));
        }
    }

    private static void AddPointerCallback(EventTrigger trigger, EventTriggerType type, UnityAction<BaseEventData> callback)
    {
        EventTrigger.Entry entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(callback);
        trigger.triggers.Add(entry);
    }

    private void OnMenuPointerEnter(int index)
    {
        if (!boxOpen || currentPage != BoxPage.Menu)
            return;

        if (index < 0 || index >= validMenuButtons.Count)
            return;

        menuIndex = index;
        RefreshMenuHighlight();
    }

    private void OnMenuPointerExit(int index)
    {
        // Keep current keyboard/gamepad selection; visuals stay on menuIndex.
        if (!boxOpen || currentPage != BoxPage.Menu)
            return;

        RefreshMenuHighlight();
    }

    private void OnCharacterPointerEnter(int index)
    {
        if (!boxOpen || currentPage != BoxPage.CharacterSelect)
            return;

        if (index < 0 || index >= validCharacterButtons.Count)
            return;

        characterIndex = index;
        RefreshCharacterHighlight();
    }

    private void OnCharacterPointerExit(int index)
    {
        if (!boxOpen || currentPage != BoxPage.CharacterSelect)
            return;

        RefreshCharacterHighlight();
    }

    private void OnBossPointerEnter(int index)
    {
        if (!boxOpen || currentPage != BoxPage.BossFight)
            return;

        if (index < 0 || index >= validBossButtons.Count)
            return;

        bossIndex = index;
        RefreshBossHighlight();
    }

    private void OnBossPointerExit(int index)
    {
        if (!boxOpen || currentPage != BoxPage.BossFight)
            return;

        RefreshBossHighlight();
    }

    private void OnShopPointerEnter(int index)
    {
        if (!boxOpen || currentPage != BoxPage.ItemShop)
            return;

        if (index < 0 || index >= validShopButtons.Count)
            return;

        shopIndex = index;
        RefreshShopHighlight();
    }

    private void OnShopPointerExit(int index)
    {
        if (!boxOpen || currentPage != BoxPage.ItemShop)
            return;

        RefreshShopHighlight();
    }

    private void UpdateRangeState()
    {
        PlayerController player = FindNearestPlayer();
        bool inRange = player != null &&
                       Vector2.Distance(transform.position, player.transform.position) <= interactRadius;

        if (inRange == playerInRange && player == rangedPlayer)
        {
            if (playerInRange && !boxOpen && player != null)
                player.SetDashDisabled(true);
            return;
        }

        PlayerController previous = rangedPlayer;

        playerInRange = inRange;
        rangedPlayer = inRange ? player : null;

        if (!inRange && boxOpen)
            CloseBox(unlockPlayer: true);

        if (playerInRange)
        {
            if (!boxOpen)
            {
                SetPromptVisible(true);
                if (rangedPlayer != null)
                    rangedPlayer.SetDashDisabled(true);
            }
        }
        else
        {
            SetPromptVisible(false);
            if (previous != null)
                previous.SetDashDisabled(false);
        }
    }

    private PlayerController FindNearestPlayer()
    {
        if (PlayerController.Active != null)
            return PlayerController.Active;

        if (PlayerSpawner.Instance != null && PlayerSpawner.Instance.CurrentPlayer != null)
            return PlayerSpawner.Instance.CurrentPlayer;

        return null;
    }

    private void WireMenuButtonClicks(bool bind)
    {
        BindClick(tutorialButton, OpenTutorialPage, bind);
        BindClick(itemShopButton, OpenItemShopPage, bind);
        BindClick(characterSelectButton, OpenCharacterSelectPage, bind);
        BindClick(bossFightButton, OpenBossFightPage, bind);
    }

    private void WireBossButtonClicks(bool bind)
    {
        CacheBossButtons();
        BindClick(kitBossButton, OnKitBossButtonClicked, bind);
        BindClick(maliceBossButton, OnMaliceBossButtonClicked, bind);
        BindClick(harlieBossButton, OnHarlieBossButtonClicked, bind);
        BindClick(hexBossButton, OnHexBossButtonClicked, bind);
        BindClick(countBossButton, OnCountBossButtonClicked, bind);
    }

    private void WireShopButtonClicks(bool bind)
    {
        CacheShopButtons();
        BindClick(juiceShopButton, BuyJuice, bind);
        BindClick(hotdogShopButton, BuyHotdog, bind);
        BindClick(spreadShotShopButton, EquipSpreadShot, bind);
        BindClick(machineGunShopButton, EquipMachineGun, bind);
    }

    /// <summary>Inspector / UI OnClick → pick Kit boss and load Boss Fight Mode.</summary>
    public void OnKitBossButtonClicked()
    {
        SelectBossIndexById(BossEncounter.BossIdKit);
        ConfirmBossSelection();
    }

    /// <summary>Inspector / UI OnClick → pick Malice boss and load Boss Fight Mode.</summary>
    public void OnMaliceBossButtonClicked()
    {
        SelectBossIndexById(BossEncounter.BossIdMalice);
        ConfirmBossSelection();
    }

    /// <summary>Inspector / UI OnClick → pick Harlie boss and load Boss Fight Mode.</summary>
    public void OnHarlieBossButtonClicked()
    {
        SelectBossIndexById(BossEncounter.BossIdHarlie);
        ConfirmBossSelection();
    }

    /// <summary>Inspector / UI OnClick → pick Hex boss and load Boss Fight Mode.</summary>
    public void OnHexBossButtonClicked()
    {
        SelectBossIndexById(BossEncounter.BossIdHex);
        ConfirmBossSelection();
    }

    /// <summary>Inspector / UI OnClick → pick Count boss and load Boss Fight Mode.</summary>
    public void OnCountBossButtonClicked()
    {
        SelectBossIndexById(BossEncounter.BossIdCount);
        ConfirmBossSelection();
    }

    private void SelectBossIndexById(string bossId)
    {
        CacheBossButtons();
        for (int i = 0; i < bossButtonIds.Count; i++)
        {
            if (string.Equals(bossButtonIds[i], bossId, StringComparison.OrdinalIgnoreCase))
            {
                bossIndex = i;
                RefreshBossHighlight();
                return;
            }
        }
    }

    private static void BindClick(Button button, UnityAction action, bool bind)
    {
        if (button == null)
            return;

        button.onClick.RemoveListener(action);
        if (bind)
            button.onClick.AddListener(action);
    }

    private void OnUpPerformed(InputAction.CallbackContext context)
    {
        if (!context.performed || !CanAcceptMenuInput())
            return;

        if (!boxOpen)
        {
            if (playerInRange)
                OpenBox();
            return;
        }

        if (currentPage == BoxPage.Menu)
            MoveMenuSelection(0, -1);
        else if (currentPage == BoxPage.CharacterSelect)
            MoveCharacterSelection(0, -1);
        else if (currentPage == BoxPage.BossFight)
            MoveBossSelection(0, -1);
        else if (currentPage == BoxPage.ItemShop)
            MoveShopSelection(0, -1);
    }

    private void OnDownPerformed(InputAction.CallbackContext context)
    {
        if (!context.performed || !CanAcceptMenuInput())
            return;

        if (boxOpen && currentPage == BoxPage.Menu)
            MoveMenuSelection(0, 1);
        else if (boxOpen && currentPage == BoxPage.CharacterSelect)
            MoveCharacterSelection(0, 1);
        else if (boxOpen && currentPage == BoxPage.BossFight)
            MoveBossSelection(0, 1);
        else if (boxOpen && currentPage == BoxPage.ItemShop)
            MoveShopSelection(0, 1);
    }

    private void OnLeftPerformed(InputAction.CallbackContext context)
    {
        if (!context.performed || !CanAcceptMenuInput())
            return;

        if (boxOpen && currentPage == BoxPage.Menu)
            MoveMenuSelection(-1, 0);
        else if (boxOpen && currentPage == BoxPage.CharacterSelect)
            MoveCharacterSelection(-1, 0);
        else if (boxOpen && currentPage == BoxPage.BossFight)
            MoveBossSelection(-1, 0);
        else if (boxOpen && currentPage == BoxPage.ItemShop)
            MoveShopSelection(-1, 0);
    }

    private void OnRightPerformed(InputAction.CallbackContext context)
    {
        if (!context.performed || !CanAcceptMenuInput())
            return;

        if (boxOpen && currentPage == BoxPage.Menu)
            MoveMenuSelection(1, 0);
        else if (boxOpen && currentPage == BoxPage.CharacterSelect)
            MoveCharacterSelection(1, 0);
        else if (boxOpen && currentPage == BoxPage.BossFight)
            MoveBossSelection(1, 0);
        else if (boxOpen && currentPage == BoxPage.ItemShop)
            MoveShopSelection(1, 0);
    }

    private void OnConfirmPerformed(InputAction.CallbackContext context)
    {
        if (!context.performed || !CanAcceptMenuInput())
            return;

        NotifyControlDevice(context);

        if (!boxOpen)
        {
            if (playerInRange)
            {
                SoundManager.Instance?.PlayUiConfirm();
                OpenBox();
            }
            return;
        }

        SoundManager.Instance?.PlayUiConfirm();

        if (currentPage == BoxPage.Menu)
            ActivateMenuSelection();
        else if (currentPage == BoxPage.Tutorial)
            AdvanceTutorialPart();
        else if (currentPage == BoxPage.CharacterSelect)
            ConfirmCharacterSelection();
        else if (currentPage == BoxPage.BossFight)
            ConfirmBossSelection();
        else if (currentPage == BoxPage.ItemShop)
            ActivateShopSelection();
    }

    /// <summary>
    /// Item Shop only: Attack on the currently equipped attack-style button returns to default.
    /// </summary>
    private void OnAttackPerformed(InputAction.CallbackContext context)
    {
        if (!context.performed || !boxOpen || currentPage != BoxPage.ItemShop)
            return;

        if (!CanAcceptMenuInput())
            return;

        if (!TryGetHoveredAttackStyle(out AttackStyleId hoveredStyle))
            return;

        // Only unequip when hovering the style that is already equipped.
        if (!PlayerAttackStyle.Is(hoveredStyle))
            return;

        PlayerAttackStyle.Set(AttackStyleId.Normal);
        RefreshAttackStyleButtonLabels();
        SoundManager.Instance?.PlayUiConfirm();
        ConsumeMenuInputCooldown();
    }

    private bool TryGetHoveredAttackStyle(out AttackStyleId style)
    {
        style = AttackStyleId.Normal;

        if (shopIndex < 0 || shopIndex >= validShopButtons.Count)
            return false;

        Button hovered = validShopButtons[shopIndex];
        if (hovered == null)
            return false;

        if (hovered == spreadShotShopButton)
        {
            style = AttackStyleId.SpreadShot;
            return true;
        }

        if (hovered == machineGunShopButton)
        {
            style = AttackStyleId.MachineGun;
            return true;
        }

        return false;
    }

    private void OnSelectPerformed(InputAction.CallbackContext context)
    {
        if (!context.performed)
            return;

        if (boxOpen)
        {
            SoundManager.Instance?.PlayUiBack();
            CloseBox(unlockPlayer: true);
        }
    }

    private void OnQuitBackPerformed(InputAction.CallbackContext context)
    {
        if (!context.performed || !boxOpen || !CanAcceptMenuInput())
            return;

        SoundManager.Instance?.PlayUiBack();

        if (currentPage == BoxPage.Menu)
        {
            CloseBox(unlockPlayer: true);
            return;
        }

        if (currentPage == BoxPage.Tutorial)
        {
            RetreatTutorialPart();
            return;
        }

        if (currentPage != BoxPage.None)
            ShowMenuPage();
    }

    private void OnMovementPerformed(InputAction.CallbackContext context)
    {
        NotifyControlDevice(context);
        Vector2 value = context.ReadValue<Vector2>();

        if (!boxOpen && playerInRange && value.y >= stickThreshold && !moveUpHeld)
        {
            moveUpHeld = true;
            if (CanAcceptMenuInput())
                OpenBox();
            return;
        }

        if (!boxOpen)
        {
            UpdateStickHeldFlags(value);
            return;
        }

        bool onNavigablePage = currentPage == BoxPage.Menu ||
                               currentPage == BoxPage.CharacterSelect ||
                               currentPage == BoxPage.BossFight ||
                               currentPage == BoxPage.ItemShop;
        if (!onNavigablePage || !CanAcceptMenuInput())
        {
            UpdateStickHeldFlags(value);
            return;
        }

        if (value.y >= stickThreshold)
        {
            if (!moveUpHeld)
            {
                moveUpHeld = true;
                NavigateActivePage(0, -1);
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
                NavigateActivePage(0, 1);
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
                NavigateActivePage(-1, 0);
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
                NavigateActivePage(1, 0);
            }
        }
        else
        {
            moveRightHeld = false;
        }
    }

    private void NavigateActivePage(int deltaX, int deltaY)
    {
        if (currentPage == BoxPage.Menu)
            MoveMenuSelection(deltaX, deltaY);
        else if (currentPage == BoxPage.CharacterSelect)
            MoveCharacterSelection(deltaX, deltaY);
        else if (currentPage == BoxPage.BossFight)
            MoveBossSelection(deltaX, deltaY);
        else if (currentPage == BoxPage.ItemShop)
            MoveShopSelection(deltaX, deltaY);
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
        if (DialogueBox.Instance != null && DialogueBox.Instance.IsOpen)
            return false;

        return Time.unscaledTime >= nextMenuInputTime;
    }

    private void ConsumeMenuInputCooldown()
    {
        nextMenuInputTime = Time.unscaledTime + Mathf.Max(0.05f, menuInputCooldown);
    }

    public void OpenBox()
    {
        if (boxOpen)
            return;

        boxOpen = true;
        SetPromptVisible(false);

        if (kaboodleBox != null)
            kaboodleBox.SetActive(true);

        PlayerController player = rangedPlayer != null ? rangedPlayer : FindNearestPlayer();
        if (player != null)
        {
            player.SetDashDisabled(true);
            player.SetInputLocked(true);
        }

        ShowMenuPage();
        LifeBar.SetAllVisible(false);
        ConsumeMenuInputCooldown();
        onOpened?.Invoke();
    }

    public void CloseBox(bool unlockPlayer = true)
    {
        bool wasOpen = boxOpen;
        boxOpen = false;
        currentPage = BoxPage.None;

        PauseBInventory.Instance?.HideShopPreview();
        ResetAllButtonVisuals();

        if (kaboodleBox != null)
            kaboodleBox.SetActive(false);

        HideAllPages();
        LifeBar.SetAllVisible(true);

        if (unlockPlayer)
        {
            PlayerController player = rangedPlayer != null ? rangedPlayer : FindNearestPlayer();
            if (player != null)
            {
                player.SetInputLocked(false);
                player.SetDashDisabled(playerInRange);
            }
        }

        SetPromptVisible(playerInRange && !boxOpen);
        RefreshNavigationHintText();

        if (wasOpen)
            onClosed?.Invoke();
    }

    public void ShowMenuPage()
    {
        currentPage = BoxPage.Menu;
        PauseBInventory.Instance?.HideShopPreview();
        SetPageActive(menuPage, true);
        SetPageActive(tutorialPage, false);
        SetPageActive(itemShopPage, false);
        SetPageActive(characterSelectPage, false);
        SetPageActive(bossFightPage, false);

        ResetCharacterButtonVisuals();
        ResetBossButtonVisuals();
        ResetShopButtonVisuals();
        CacheMenuButtons();
        menuIndex = Mathf.Clamp(menuIndex, 0, Mathf.Max(0, validMenuButtons.Count - 1));
        RefreshMenuHighlight();
        RefreshNavigationHintText();
        ConsumeMenuInputCooldown();
    }

    public void OpenTutorialPage()
    {
        OpenContentPage(BoxPage.Tutorial, tutorialPage);
        BeginTutorial();
        onOpenedTutorial?.Invoke();
    }

    public void OpenItemShopPage()
    {
        OpenContentPage(BoxPage.ItemShop, itemShopPage);
        PrepareItemShopPage();
        onOpenedItemShop?.Invoke();
    }

    public void OpenCharacterSelectPage()
    {
        OpenContentPage(BoxPage.CharacterSelect, characterSelectPage);
        PrepareCharacterSelectPage();
        onOpenedCharacterSelect?.Invoke();
    }

    public void OpenBossFightPage()
    {
        OpenContentPage(BoxPage.BossFight, bossFightPage);
        PrepareBossSelectPage();
        onOpenedBossFight?.Invoke();
    }

    private void EnsureTutorialTextBound()
    {
        if (tutorialBodyText != null)
            return;

        if (tutorialPage == null)
            return;

        tutorialBodyText = tutorialPage.GetComponentInChildren<TMP_Text>(true);
    }

    private void BeginTutorial()
    {
        EnsureTutorialTextBound();
        tutorialParts.Clear();
        tutorialParts.AddRange(KaboodleTutorialContent.BuildParts());
        tutorialPartIndex = 0;
        RefreshTutorialText();
        ConsumeMenuInputCooldown();
    }

    private void AdvanceTutorialPart()
    {
        if (tutorialParts.Count == 0)
        {
            BeginTutorial();
            if (tutorialParts.Count == 0)
            {
                ShowMenuPage();
                return;
            }
        }

        if (tutorialPartIndex >= tutorialParts.Count - 1)
        {
            MarkCurrentCharacterTutorialComplete();
            ShowMenuPage();
            ConsumeMenuInputCooldown();
            return;
        }

        tutorialPartIndex++;
        RefreshTutorialText();
        ConsumeMenuInputCooldown();
    }

    private void MarkCurrentCharacterTutorialComplete()
    {
        string id = PlayerSpawner.SelectedCharacterId;
        if (PlayerController.Active != null)
            id = PlayerController.Active.CharacterId;

        KaboodleTutorialProgress.MarkTutorialDone(id);
        KaboodleForcedText.Instance?.NotifyTutorialProgressChanged();
    }

    private void RetreatTutorialPart()
    {
        if (tutorialPartIndex <= 0)
        {
            ShowMenuPage();
            ConsumeMenuInputCooldown();
            return;
        }

        tutorialPartIndex--;
        RefreshTutorialText();
        ConsumeMenuInputCooldown();
    }

    private void RefreshTutorialText()
    {
        EnsureTutorialTextBound();
        if (tutorialBodyText == null || tutorialParts.Count == 0)
            return;

        tutorialPartIndex = Mathf.Clamp(tutorialPartIndex, 0, tutorialParts.Count - 1);
        bool last = tutorialPartIndex >= tutorialParts.Count - 1;
        tutorialBodyText.richText = true;
        tutorialBodyText.text = KaboodleTutorialContent.AppendContinuePrompt(
            tutorialParts[tutorialPartIndex],
            last);
    }

    private void PrepareCharacterSelectPage()
    {
        CacheCharacterButtons();
        EnsureCharacterHighlightShapes();

        characterIndex = 0;
        string currentId = PlayerSpawner.SelectedCharacterId;
        if (PlayerController.Active != null)
            currentId = PlayerController.Active.CharacterId;

        for (int i = 0; i < characterButtonIds.Count; i++)
        {
            if (string.Equals(characterButtonIds[i], currentId, StringComparison.OrdinalIgnoreCase))
            {
                characterIndex = i;
                break;
            }
        }

        if (characterIndex >= validCharacterButtons.Count)
            characterIndex = Mathf.Max(0, validCharacterButtons.Count - 1);

        RefreshCharacterHighlight();
    }

    private void PrepareBossSelectPage()
    {
        CacheBossButtons();
        CacheScaleDefaults(validBossButtons);
        EnsureBossHighlightShapes();
        HookBossPointerHover();
        WireBossButtonClicks(true);

        bossIndex = 0;
        string currentId = BossEncounter.SelectedBossId;
        for (int i = 0; i < bossButtonIds.Count; i++)
        {
            if (string.Equals(bossButtonIds[i], currentId, StringComparison.OrdinalIgnoreCase))
            {
                bossIndex = i;
                break;
            }
        }

        if (bossIndex >= validBossButtons.Count)
            bossIndex = Mathf.Max(0, validBossButtons.Count - 1);

        RefreshBossHighlight();
    }

    private void PrepareItemShopPage()
    {
        CacheShopButtons();
        CacheScaleDefaults(validShopButtons);
        HookShopPointerHover();
        WireShopButtonClicks(true);

        shopIndex = Mathf.Clamp(shopIndex, 0, Mathf.Max(0, validShopButtons.Count - 1));
        RefreshShopHighlight();
        RefreshAttackStyleButtonLabels();
        PauseBInventory.Instance?.ShowShopPreview();
    }

    /// <summary>Wire Item Shop Juice button OnClick → Kaboodle.BuyJuice.</summary>
    public void BuyJuice()
    {
        if (PlayerInventory.Instance == null)
            return;
        PlayerInventory.Instance.AddJuice(1);
        PauseBInventory.Instance?.ShowShopPreview();
    }

    /// <summary>Wire Item Shop Hotdog button OnClick → Kaboodle.BuyHotdog.</summary>
    public void BuyHotdog()
    {
        if (PlayerInventory.Instance == null)
            return;
        PlayerInventory.Instance.AddHotdogs(1);
        PauseBInventory.Instance?.ShowShopPreview();
    }

    /// <summary>Item Shop slot: Kit Spread Shot / Malice Life Steal.</summary>
    public void EquipSpreadShot()
    {
        PlayerAttackStyle.Set(AttackStyleId.SpreadShot);
        RefreshAttackStyleButtonLabels();
    }

    /// <summary>Item Shop slot: Kit Machine Gun / Malice Cruel Claw.</summary>
    public void EquipMachineGun()
    {
        PlayerAttackStyle.Set(AttackStyleId.MachineGun);
        RefreshAttackStyleButtonLabels();
    }

    private void RefreshAttackStyleButtonLabels()
    {
        SetAttackStyleButtonLabel(spreadShotShopButton, AttackStyleId.SpreadShot);
        SetAttackStyleButtonLabel(machineGunShopButton, AttackStyleId.MachineGun);
    }

    private static void SetAttackStyleButtonLabel(Button button, AttackStyleId style)
    {
        if (button == null)
            return;

        string styleName = PlayerAttackStyle.DisplayName(style);
        string label = PlayerAttackStyle.Is(style)
            ? $"{styleName}: equipped"
            : styleName;

        TextMeshProUGUI ui = button.GetComponentInChildren<TextMeshProUGUI>(true);
        if (ui != null)
        {
            ui.text = label;
            return;
        }

        TextMeshPro world = button.GetComponentInChildren<TextMeshPro>(true);
        if (world != null)
            world.text = label;
    }

    /// <summary>Wire shop / continue-after-game-over → Kaboodle.BuyLife.</summary>
    public void BuyLife()
    {
        if (PlayerInventory.Instance == null)
            return;
        PlayerInventory.Instance.AddLife(1);
        PauseBInventory.Instance?.ShowShopPreview();
    }

    /// <summary>Loads Boss Fight Mode for the currently selected ready boss.</summary>
    public void LoadBossFightScene()
    {
        if (string.IsNullOrWhiteSpace(bossFightSceneName))
            return;

        CloseBox(unlockPlayer: true);
        ScreenFade.EnsureExists().LoadScene(bossFightSceneName);
    }

    /// <summary>Public stub: confirm highlighted boss (same as Confirm on Boss Fight page).</summary>
    public void ConfirmBossFightSelection()
    {
        ConfirmBossSelection();
    }

    private void OpenContentPage(BoxPage page, GameObject pageRoot)
    {
        if (!boxOpen)
            OpenBox();

        currentPage = page;
        ResetAllButtonVisuals();

        if (page != BoxPage.ItemShop)
            PauseBInventory.Instance?.HideShopPreview();

        SetPageActive(menuPage, false);
        SetPageActive(tutorialPage, page == BoxPage.Tutorial);
        SetPageActive(itemShopPage, page == BoxPage.ItemShop);
        SetPageActive(characterSelectPage, page == BoxPage.CharacterSelect);
        SetPageActive(bossFightPage, page == BoxPage.BossFight);

        if (pageRoot != null)
            pageRoot.SetActive(true);

        RefreshNavigationHintText();
        ConsumeMenuInputCooldown();
    }

    private void HideAllPages()
    {
        SetPageActive(menuPage, false);
        SetPageActive(tutorialPage, false);
        SetPageActive(itemShopPage, false);
        SetPageActive(characterSelectPage, false);
        SetPageActive(bossFightPage, false);
    }

    private static void SetPageActive(GameObject page, bool active)
    {
        if (page != null)
            page.SetActive(active);
    }

    private void EnsureNavigationHintBound()
    {
        if (navigationHintText != null)
            return;

        string[] names =
        {
            "How to navigate the Kaboodle box text",
            "How to navigate the Kaboodle box Text",
            "Kaboodle navigation hint",
            "Navigation hint"
        };

        Transform searchRoot = kaboodleBox != null ? kaboodleBox.transform : transform;
        for (int n = 0; n < names.Length; n++)
        {
            Transform found = FindChildByNameRecursive(searchRoot, names[n]);
            if (found == null)
                continue;

            navigationHintText = found.GetComponent<TMP_Text>();
            if (navigationHintText == null)
                navigationHintText = found.GetComponentInChildren<TMP_Text>(true);
            if (navigationHintText != null)
                return;
        }
    }

    private static Transform FindChildByNameRecursive(Transform root, string name)
    {
        if (root == null || string.IsNullOrEmpty(name))
            return null;

        if (root.name == name)
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindChildByNameRecursive(root.GetChild(i), name);
            if (found != null)
                return found;
        }

        return null;
    }

    private void RefreshNavigationHintText()
    {
        EnsureNavigationHintBound();
        if (navigationHintText == null)
            return;

        // Tutorial pages already teach Confirm / Back in their body text.
        if (!boxOpen || currentPage == BoxPage.None || currentPage == BoxPage.Tutorial)
        {
            navigationHintText.gameObject.SetActive(false);
            return;
        }

        ButtonSpriteManager manager = ButtonSpriteManager.EnsureExists();
        manager?.SyncDeviceForPrompts();

        string back = manager != null ? manager.FormatButton("Quit/Back") : "B";
        string confirm = manager != null ? manager.FormatButton("Confirm") : "M";
        string select = manager != null ? manager.FormatButton("Select") : "Tab";
        string attack = manager != null ? manager.FormatButton("Attack") : "M";

        navigationHintText.richText = true;
        navigationHintText.text = currentPage switch
        {
            BoxPage.ItemShop =>
                $"Back: {back}   Confirm: {confirm}   Unequip style: {attack}   Select: {select}",
            BoxPage.Menu or BoxPage.CharacterSelect or BoxPage.BossFight =>
                $"Back: {back}   Confirm: {confirm}   Select: {select}",
            _ => $"Back: {back}   Confirm: {confirm}   Select: {select}"
        };
        navigationHintText.gameObject.SetActive(true);
    }

    private void SetPromptVisible(bool visible)
    {
        if (visible)
            RefreshInteractPromptSprite();

        if (interactPrompt != null)
            interactPrompt.SetActive(visible);
    }

    private void CacheInteractPrompt()
    {
        if (interactPrompt == null)
            return;

        interactPromptRenderer = interactPrompt.GetComponent<SpriteRenderer>();
        if (interactPromptRenderer == null)
            interactPromptRenderer = interactPrompt.GetComponentInChildren<SpriteRenderer>(true);

        if (interactPromptRenderer != null)
            interactPromptFallbackSprite = interactPromptRenderer.sprite;

        if (!interactPromptBaseScaleCached)
        {
            interactPromptBaseScale = interactPrompt.transform.localScale;
            interactPromptBaseScaleCached = true;
        }
    }

    private static void NotifyControlDevice(InputAction.CallbackContext context)
    {
        InputDevice device = context.control != null ? context.control.device : null;
        if (device == null)
            return;

        ButtonSpriteManager manager = ButtonSpriteManager.Instance ?? ButtonSpriteManager.EnsureExists();
        manager?.NotifyDevice(device);
    }

    private void SubscribeButtonSpriteEvents(bool bind)
    {
        ButtonSpriteManager manager = ButtonSpriteManager.EnsureExists();
        if (manager == null)
            return;

        if (bind)
        {
            if (subscribedToButtonSprites)
                return;
            manager.DeviceChanged += OnControlDeviceChanged;
            subscribedToButtonSprites = true;
            RefreshInteractPromptSprite();
        }
        else
        {
            if (!subscribedToButtonSprites)
                return;
            manager.DeviceChanged -= OnControlDeviceChanged;
            subscribedToButtonSprites = false;
        }
    }

    private void OnControlDeviceChanged(ButtonSpriteManager.ControlDeviceKind device)
    {
        RefreshInteractPromptSprite();
        RefreshNavigationHintText();

        if (boxOpen && currentPage == BoxPage.Tutorial)
            RebuildTutorialKeepingPage();
    }

    private void RefreshInteractPromptSprite()
    {
        if (interactPromptRenderer == null)
            CacheInteractPrompt();
        if (interactPromptRenderer == null)
            return;

        if (!interactPromptBaseScaleCached && interactPrompt != null)
        {
            interactPromptBaseScale = interactPrompt.transform.localScale;
            interactPromptBaseScaleCached = true;
        }

        ButtonSpriteManager manager = ButtonSpriteManager.Instance ?? ButtonSpriteManager.EnsureExists();
        bool enlarge = false;
        Sprite sprite = null;
        if (manager != null)
            manager.GetSprite("Confirm", manager.CurrentDevice, out sprite, out enlarge);

        interactPromptRenderer.sprite = sprite != null ? sprite : interactPromptFallbackSprite;

        float mult = enlarge ? Mathf.Max(1f, controllerPromptScaleMultiplier) : 1f;
        if (interactPrompt != null)
            interactPrompt.transform.localScale = interactPromptBaseScale * mult;
    }

    private void RebuildTutorialKeepingPage()
    {
        int page = tutorialPartIndex;
        tutorialParts.Clear();
        tutorialParts.AddRange(KaboodleTutorialContent.BuildParts());
        if (tutorialParts.Count == 0)
            return;

        tutorialPartIndex = Mathf.Clamp(page, 0, tutorialParts.Count - 1);
        RefreshTutorialText();
    }

    /// <summary>
    /// Move selection to the nearest button in the input direction using on-screen positions.
    /// Works for any layout — no rigid row/column list order required.
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

    private void MoveMenuSelection(int deltaX, int deltaY)
    {
        if (!MoveSpatialSelection(validMenuButtons, ref menuIndex, deltaX, deltaY))
            return;

        RefreshMenuHighlight();
        ConsumeMenuInputCooldown();
    }

    private void MoveCharacterSelection(int deltaX, int deltaY)
    {
        if (!MoveSpatialSelection(validCharacterButtons, ref characterIndex, deltaX, deltaY))
            return;

        RefreshCharacterHighlight();
        ConsumeMenuInputCooldown();
    }

    private void MoveBossSelection(int deltaX, int deltaY)
    {
        if (!MoveSpatialSelection(validBossButtons, ref bossIndex, deltaX, deltaY))
            return;

        RefreshBossHighlight();
        ConsumeMenuInputCooldown();
    }

    private void MoveShopSelection(int deltaX, int deltaY)
    {
        if (!MoveSpatialSelection(validShopButtons, ref shopIndex, deltaX, deltaY))
            return;

        RefreshShopHighlight();
        ConsumeMenuInputCooldown();
    }

    private void ConfirmBossSelection()
    {
        CacheBossButtons();

        if (bossIndex < 0 || bossIndex >= bossButtonIds.Count)
        {
            Debug.LogWarning("[Kaboodle] ConfirmBossSelection: no boss buttons available on Boss Fight page.");
            ConsumeMenuInputCooldown();
            return;
        }

        string bossId = bossButtonIds[bossIndex];
        if (string.IsNullOrWhiteSpace(bossId))
            return;

        BossEncounter.SetSelectedBoss(bossId);

        if (!BossEncounter.IsBossReady(bossId))
        {
            Debug.LogWarning(
                $"[Kaboodle] Boss '{bossId}' is not ready yet.");
            ConsumeMenuInputCooldown();
            RefreshBossHighlight();
            return;
        }

        ConsumeMenuInputCooldown();
        LoadBossFightScene();
    }

    private void ConfirmCharacterSelection()
    {
        CacheCharacterButtons();

        if (characterIndex < 0 || characterIndex >= characterButtonIds.Count)
        {
            Debug.LogWarning("[Kaboodle] ConfirmCharacterSelection: no character buttons available.");
            ConsumeMenuInputCooldown();
            return;
        }

        string characterId = characterButtonIds[characterIndex];
        if (string.IsNullOrWhiteSpace(characterId))
            return;

        if (PlayerSpawner.Instance == null)
        {
            Debug.LogWarning("[Kaboodle] No PlayerSpawner in the scene — cannot switch characters.");
            ConsumeMenuInputCooldown();
            return;
        }

        if (!PlayerSpawner.Instance.HasPrefab(characterId))
        {
            Debug.LogWarning(
                $"[Kaboodle] PlayerSpawner has no prefab for '{characterId}'. " +
                "On PlayerSpawner, add a Characters entry for Harlie and assign Playable Harlie prefab.");
            ConsumeMenuInputCooldown();
            return;
        }

        // Unlock briefly so the new player does not inherit a locked input state.
        PlayerController previous = rangedPlayer != null ? rangedPlayer : FindNearestPlayer();
        if (previous != null)
        {
            previous.SetInputLocked(false);
            previous.SetDashDisabled(false);
        }

        PlayerController spawned = PlayerSpawner.Instance.SwitchCharacter(characterId);
        if (spawned == null)
        {
            Debug.LogWarning($"[Kaboodle] SwitchCharacter('{characterId}') failed.");
            ConsumeMenuInputCooldown();
            return;
        }

        rangedPlayer = spawned;
        RefreshAttackStyleButtonLabels();
        ConsumeMenuInputCooldown();

        if (closeBoxAfterCharacterSwitch)
            CloseBox(unlockPlayer: true);
        else
        {
            spawned.SetInputLocked(true);
            RefreshCharacterHighlight();
        }
    }

    private void ActivateMenuSelection()
    {
        if (menuIndex < 0 || menuIndex >= validMenuButtons.Count)
            return;

        Button button = validMenuButtons[menuIndex];
        if (button == null)
            return;

        button.onClick.Invoke();
        ConsumeMenuInputCooldown();
    }

    private void ActivateShopSelection()
    {
        if (shopIndex < 0 || shopIndex >= validShopButtons.Count)
            return;

        Button button = validShopButtons[shopIndex];
        if (button == null)
            return;

        button.onClick.Invoke();
        ConsumeMenuInputCooldown();
    }

    private void RefreshMenuHighlight()
    {
        for (int i = 0; i < validMenuButtons.Count; i++)
            SetButtonHighlighted(validMenuButtons[i], i == menuIndex);
    }

    private void RefreshCharacterHighlight()
    {
        for (int i = 0; i < validCharacterButtons.Count; i++)
            SetCharacterButtonHighlighted(validCharacterButtons[i], i == characterIndex);
    }

    private void RefreshBossHighlight()
    {
        for (int i = 0; i < validBossButtons.Count; i++)
            SetCharacterButtonHighlighted(validBossButtons[i], i == bossIndex);
    }

    private void RefreshShopHighlight()
    {
        for (int i = 0; i < validShopButtons.Count; i++)
            SetShopButtonHighlighted(validShopButtons[i], i == shopIndex);
    }

    private void ResetAllButtonVisuals()
    {
        for (int i = 0; i < validMenuButtons.Count; i++)
            SetButtonHighlighted(validMenuButtons[i], false);

        ResetCharacterButtonVisuals();
        ResetBossButtonVisuals();
        ResetShopButtonVisuals();
    }

    private void ResetCharacterButtonVisuals()
    {
        for (int i = 0; i < validCharacterButtons.Count; i++)
            SetCharacterButtonHighlighted(validCharacterButtons[i], false);
    }

    private void ResetBossButtonVisuals()
    {
        for (int i = 0; i < validBossButtons.Count; i++)
            SetCharacterButtonHighlighted(validBossButtons[i], false);
    }

    private void ResetShopButtonVisuals()
    {
        for (int i = 0; i < validShopButtons.Count; i++)
            SetShopButtonHighlighted(validShopButtons[i], false);
    }

    private void SetButtonHighlighted(Button button, bool selected)
    {
        if (button == null)
            return;

        ApplyButtonHighlight(button, selected, selectedScaleBonus, tintTargetGraphic: true);
    }

    private void SetShopButtonHighlighted(Button button, bool selected)
    {
        if (button == null)
            return;

        if (!buttonBaseScales.TryGetValue(button, out Vector3 buttonBase))
        {
            buttonBaseScales[button] = button.transform.localScale;
            buttonBase = buttonBaseScales[button];
        }

        float scaleMul = selected ? (1f + Mathf.Max(0f, itemShopSelectedScaleBonus)) : 1f;
        button.transform.localScale = buttonBase * scaleMul;

        // Reset icon to its own base so we don't stack icon-only scaling from older logic.
        Transform icon = GetShopIconTransform(button);
        if (icon != null && shopIconBaseScales.TryGetValue(button, out Vector3 iconBase))
            icon.localScale = iconBase;

        Color tint = selected ? selectedButtonColor : normalButtonColor;
        ColorBlock colors = button.colors;
        colors.normalColor = normalButtonColor;
        colors.selectedColor = normalButtonColor;
        colors.highlightedColor = normalButtonColor;
        button.colors = colors;

        if (button.targetGraphic != null)
            button.targetGraphic.color = tint;

        Outline outline = button.GetComponent<Outline>();
        if (outline != null)
            outline.enabled = false;

        // SpriteRenderer children use huge local Z to sit on the World Space canvas.
        // Parent scale multiplies that Z and clips/hides them — compensate so they still grow with the button.
        EnsureShopTextContainerVisible(button, scaleMul);
    }

    private void EnsureShopTextContainerVisible(Button button, float buttonScaleMul)
    {
        if (button == null)
            return;

        if (!shopItemTextContainers.TryGetValue(button, out SpriteRenderer container) || container == null)
        {
            container = FindItemTextContainer(button);
            if (container != null)
                RegisterShopTextContainer(button, container);
        }

        if (container == null)
            return;

        if (!shopContainerBaseColors.TryGetValue(container, out Color baseColor))
        {
            baseColor = container.color;
            baseColor.a = 1f;
            shopContainerBaseColors[container] = baseColor;
        }

        if (!shopContainerBaseLocalPositions.TryGetValue(container, out Vector3 baseLocalPos))
        {
            baseLocalPos = container.transform.localPosition;
            shopContainerBaseLocalPositions[container] = baseLocalPos;
        }

        float safeMul = Mathf.Max(0.01f, buttonScaleMul);
        // Keep world Z stable while XY still scales with the button pivot.
        Vector3 compensatedLocalPos = baseLocalPos;
        compensatedLocalPos.z = baseLocalPos.z / safeMul;
        container.transform.localPosition = compensatedLocalPos;

        if (!shopContainerBaseSortingOrders.TryGetValue(container, out int baseOrder))
        {
            baseOrder = container.sortingOrder;
            shopContainerBaseSortingOrders[container] = baseOrder;
        }

        // Stay behind Kaboodle Canvas UI (sorting order 10) so item image + text sit on top.
        Canvas parentCanvas = button.GetComponentInParent<Canvas>();
        int canvasOrder = parentCanvas != null ? parentCanvas.sortingOrder : 10;
        container.sortingOrder = Mathf.Min(baseOrder, canvasOrder - 1);

        // Hierarchy: container first, then image/text so UI children draw after it.
        container.transform.SetAsFirstSibling();
        BringShopForegroundUiToFront(button, container.transform);

        container.gameObject.SetActive(true);
        container.enabled = true;
        container.allowOcclusionWhenDynamic = false;
        container.color = baseColor;
    }

    private static void BringShopForegroundUiToFront(Button button, Transform container)
    {
        if (button == null)
            return;

        Transform icon = GetShopIconTransform(button);
        Transform text = null;

        for (int i = 0; i < button.transform.childCount; i++)
        {
            Transform child = button.transform.GetChild(i);
            if (child == null || child == container || child == icon)
                continue;

            if (child.name.IndexOf("Text", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                text = child;
                break;
            }
        }

        // Draw order among button children: container (back) → icon → text (front).
        if (icon != null && icon != container && icon.parent == button.transform)
            icon.SetAsLastSibling();
        if (text != null && text.parent == button.transform)
            text.SetAsLastSibling();
    }

    private void ApplyButtonHighlight(
        Button button,
        bool selected,
        float scaleBonus,
        bool tintTargetGraphic)
    {
        if (!buttonBaseScales.TryGetValue(button, out Vector3 baseScale))
        {
            buttonBaseScales[button] = button.transform.localScale;
            baseScale = buttonBaseScales[button];
        }

        float scaleMul = selected ? (1f + Mathf.Max(0f, scaleBonus)) : 1f;
        button.transform.localScale = baseScale * scaleMul;

        Color tint = selected ? selectedButtonColor : normalButtonColor;
        ColorBlock colors = button.colors;
        colors.normalColor = tint;
        colors.selectedColor = tint;
        colors.highlightedColor = tint;
        button.colors = colors;

        if (tintTargetGraphic && button.targetGraphic != null)
            button.targetGraphic.color = tint;

        Outline outline = button.GetComponent<Outline>();
        if (outline == null)
            outline = button.gameObject.AddComponent<Outline>();

        outline.effectColor = outlineColor;
        outline.effectDistance = outlineDistance;
        outline.enabled = selected;
    }

    private void SetCharacterButtonHighlighted(Button button, bool selected)
    {
        if (button == null)
            return;

        if (!buttonBaseScales.TryGetValue(button, out Vector3 baseScale))
        {
            buttonBaseScales[button] = button.transform.localScale;
            baseScale = buttonBaseScales[button];
        }

        float scaleMul = selected ? (1f + Mathf.Max(0f, selectedScaleBonus)) : 1f;
        button.transform.localScale = baseScale * scaleMul;

        // Keep portrait colors — selection is one solid blue character-shaped highlight behind the art.
        Outline legacyOutline = button.GetComponent<Outline>();
        if (legacyOutline != null)
            legacyOutline.enabled = false;

        Image portrait = GetCharacterPortraitImage(button);
        if (characterHighlightShapes.TryGetValue(button, out Image highlight) && highlight != null)
            RefreshCharacterHighlightShape(button, portrait, highlight, selected);
    }

    private Color GetSolidCharacterOutlineColor()
    {
        Color c = characterOutlineColor;
        c.a = 1f;
        return c;
    }

    private void ClearPlayerLocks()
    {
        PlayerController player = rangedPlayer != null ? rangedPlayer : PlayerController.Active;
        if (player == null)
            return;

        player.SetInputLocked(false);
        player.SetDashDisabled(false);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        interactRadius = Mathf.Max(0.1f, interactRadius);
        menuInputCooldown = Mathf.Max(0.05f, menuInputCooldown);
        stickThreshold = Mathf.Clamp(stickThreshold, 0.1f, 1f);
        menuColumns = Mathf.Max(1, menuColumns);
        characterSelectColumns = Mathf.Max(1, characterSelectColumns);
        selectedScaleBonus = Mathf.Max(0f, selectedScaleBonus);
        itemShopSelectedScaleBonus = Mathf.Max(0f, itemShopSelectedScaleBonus);
        characterOutlineThickness = Mathf.Max(1f, characterOutlineThickness);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.75f, 0.2f, 0.85f);
        Gizmos.DrawWireSphere(transform.position, interactRadius);
    }
#endif
}
