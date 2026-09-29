using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Scratch's upgrade shop (HomeTown). Hidden until the owning NPC's dialogue finishes.
/// Up/Down (or stick / mouse hover) picks an upgrade, Left/Right moves between its buy button and
/// its Equip button, Confirm (or click) buys / toggles equip, Back closes.
/// Levels and equip state are per character (see <see cref="PlayerUpgrades"/>).
/// </summary>
public sealed class UpgradeShop : MonoBehaviour
{
    [Header("UI (auto-finds by scene names if empty)")]
    [SerializeField] private GameObject shopRoot;
    [SerializeField] private Button aerialActionButton;
    [SerializeField] private Button hyperAbilityButton;
    [SerializeField] private Button attackStyleButton;
    [SerializeField] private TMP_Text aerialActionLevelText;
    [SerializeField] private TMP_Text hyperAbilityLevelText;
    [SerializeField] private TMP_Text attackStyleLevelText;
    [SerializeField] private TMP_Text crystalAmountText;
    [Tooltip("Shows the price of the hovered / selected upgrade. Hidden when nothing is selected.")]
    [SerializeField] private TMP_Text crystalCostText;
    [Tooltip("Auto-found as the \"Equip Button\" child of each upgrade button if empty.")]
    [SerializeField] private Button aerialActionEquipButton;
    [SerializeField] private Button hyperAbilityEquipButton;
    [SerializeField] private Button attackStyleEquipButton;

    [Header("Equip Button Look")]
    [SerializeField] private Color equippedColor = new Color(0.22f, 1f, 0.08f, 1f);
    [SerializeField] private Color unequippedColor = new Color(0.5f, 0.5f, 0.5f, 0.45f);
    [Tooltip("Shown while the Equip button is selected (keyboard / controller) or hovered.")]
    [SerializeField] private Color equipFocusedColor = new Color(0.1f, 0.85f, 1f, 1f);
    [SerializeField] private float equipFocusedScaleBonus = 0.3f;
    [SerializeField] private string equippedText = "E";
    [SerializeField] private string unequippedText = "_";

    [Header("Selection Look")]
    [SerializeField] private Color selectedButtonColor = new Color(1f, 0.92f, 0.45f, 1f);
    [SerializeField] private float selectedScaleBonus = 0.12f;

    [Header("Input")]
    [SerializeField] [Range(0.1f, 1f)] private float stickThreshold = 0.5f;
    [SerializeField] private float menuInputCooldown = 0.18f;
    [Tooltip("Ignore input briefly after opening so the Confirm that closed the dialogue doesn't buy.")]
    [SerializeField] private float openInputDelay = 0.3f;
    [Tooltip("After closing, keep dash blocked this long so Back / Confirm does not also dash.")]
    [SerializeField] private float postCloseDashLockSeconds = 0.35f;

    private sealed class Entry
    {
        public UpgradeType Type;
        public Button Button;
        public TMP_Text LevelText;
        public Image Image;
        public Color BaseColor = Color.white;
        public Vector3 BaseScale = Vector3.one;
        public Button EquipButton;
        public Image EquipImage;
        public TMP_Text EquipText;
        public Vector3 EquipBaseScale = Vector3.one;
        public bool PointerOverEquip;
    }

    private readonly List<Entry> entries = new List<Entry>(3);
    private InputActions controls;
    private PlayerController shopper;
    private Action onClosed;
    private Coroutine dashReleaseRoutine;
    private bool isOpen;
    private int selectedIndex = -1;
    private bool equipColumnSelected;
    private bool selectionFromPointer;
    private float nextInputTime;
    private bool moveUpHeld;
    private bool moveDownHeld;
    private bool moveLeftHeld;
    private bool moveRightHeld;

    public bool IsOpen => isOpen;

    private void Awake()
    {
        CacheUi();
        BuildEntries();
        HookButtons();
        SetShopVisible(false);
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
        controls.PlayerControls.QuitBack.performed += OnBackPerformed;
        controls.PlayerControls.Select.performed += OnBackPerformed;
        controls.PlayerControls.Movement.performed += OnMovementPerformed;
        controls.PlayerControls.Movement.canceled += OnMovementCanceled;

        if (PlayerInventory.Instance != null)
            PlayerInventory.Instance.OnInventoryChanged += RefreshTexts;
        PlayerUpgrades.OnChanged += OnUpgradeChanged;
        PlayerUpgrades.OnEquippedChanged += OnUpgradeEquippedChanged;
    }

    private void OnDisable()
    {
        if (controls != null)
        {
            controls.PlayerControls.Up.performed -= OnUpPerformed;
            controls.PlayerControls.Down.performed -= OnDownPerformed;
            controls.PlayerControls.Left.performed -= OnLeftPerformed;
            controls.PlayerControls.Right.performed -= OnRightPerformed;
            controls.PlayerControls.Confirm.performed -= OnConfirmPerformed;
            controls.PlayerControls.QuitBack.performed -= OnBackPerformed;
            controls.PlayerControls.Select.performed -= OnBackPerformed;
            controls.PlayerControls.Movement.performed -= OnMovementPerformed;
            controls.PlayerControls.Movement.canceled -= OnMovementCanceled;
            controls.PlayerControls.Disable();
        }

        if (PlayerInventory.Instance != null)
            PlayerInventory.Instance.OnInventoryChanged -= RefreshTexts;
        PlayerUpgrades.OnChanged -= OnUpgradeChanged;
        PlayerUpgrades.OnEquippedChanged -= OnUpgradeEquippedChanged;

        if (isOpen)
            Close();
    }

    private void OnDestroy()
    {
        if (controls != null)
        {
            controls.Dispose();
            controls = null;
        }
    }

    public void Open(PlayerController player, Action closedCallback = null)
    {
        if (isOpen)
            return;

        isOpen = true;
        shopper = player != null ? player : PlayerController.ResolveActive();
        onClosed = closedCallback;
        selectedIndex = -1;
        equipColumnSelected = false;
        selectionFromPointer = false;
        moveUpHeld = true;
        moveDownHeld = true;
        moveLeftHeld = true;
        moveRightHeld = true;
        nextInputTime = Time.unscaledTime + Mathf.Max(0f, openInputDelay);

        if (dashReleaseRoutine != null)
        {
            StopCoroutine(dashReleaseRoutine);
            dashReleaseRoutine = null;
        }

        if (shopper != null)
        {
            shopper.SetInputLocked(true);
            shopper.SetDashDisabled(true);
        }

        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);

        SetShopVisible(true);
        RefreshAll();
    }

    public void Close()
    {
        if (!isOpen)
            return;

        isOpen = false;
        selectedIndex = -1;
        equipColumnSelected = false;
        for (int i = 0; i < entries.Count; i++)
            entries[i].PointerOverEquip = false;
        RefreshSelectionVisuals();
        SetShopVisible(false);

        PlayerController player = shopper;
        shopper = null;
        if (player != null)
        {
            player.SetInputLocked(false);
            player.SetDashDisabled(true);
            if (isActiveAndEnabled)
                dashReleaseRoutine = StartCoroutine(ReleaseDashAfterDelay(player));
            else
                player.SetDashDisabled(false);
        }

        Action callback = onClosed;
        onClosed = null;
        callback?.Invoke();
    }

    private IEnumerator ReleaseDashAfterDelay(PlayerController player)
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(0.05f, postCloseDashLockSeconds));
        if (player != null)
            player.SetDashDisabled(false);
        dashReleaseRoutine = null;
    }

    // ---------- Input ----------

    private bool CanAcceptInput()
    {
        if (!isOpen)
            return false;
        if (DialogueBox.Instance != null && DialogueBox.Instance.IsOpen)
            return false;
        return Time.unscaledTime >= nextInputTime;
    }

    private void ConsumeInputCooldown()
    {
        nextInputTime = Time.unscaledTime + Mathf.Max(0.05f, menuInputCooldown);
    }

    private void OnUpPerformed(InputAction.CallbackContext context)
    {
        if (context.performed && CanAcceptInput())
            MoveSelection(-1);
    }

    private void OnDownPerformed(InputAction.CallbackContext context)
    {
        if (context.performed && CanAcceptInput())
            MoveSelection(1);
    }

    private void OnLeftPerformed(InputAction.CallbackContext context)
    {
        if (context.performed && CanAcceptInput())
            SelectColumn(equip: false);
    }

    private void OnRightPerformed(InputAction.CallbackContext context)
    {
        if (context.performed && CanAcceptInput())
            SelectColumn(equip: true);
    }

    private void OnMovementPerformed(InputAction.CallbackContext context)
    {
        Vector2 value = context.ReadValue<Vector2>();
        bool up = value.y >= stickThreshold;
        bool down = value.y <= -stickThreshold;
        bool left = value.x <= -stickThreshold;
        bool right = value.x >= stickThreshold;

        if (CanAcceptInput())
        {
            if (up && !moveUpHeld)
                MoveSelection(-1);
            else if (down && !moveDownHeld)
                MoveSelection(1);
            else if (left && !moveLeftHeld)
                SelectColumn(equip: false);
            else if (right && !moveRightHeld)
                SelectColumn(equip: true);
        }

        moveUpHeld = up;
        moveDownHeld = down;
        moveLeftHeld = left;
        moveRightHeld = right;
    }

    private void OnMovementCanceled(InputAction.CallbackContext context)
    {
        moveUpHeld = false;
        moveDownHeld = false;
        moveLeftHeld = false;
        moveRightHeld = false;
    }

    private void OnConfirmPerformed(InputAction.CallbackContext context)
    {
        if (!context.performed || !CanAcceptInput())
            return;

        if (selectedIndex < 0)
        {
            MoveSelection(1);
            return;
        }

        if (equipColumnSelected)
            TryToggleEquip(selectedIndex);
        else
            TryBuy(selectedIndex);
    }

    private void OnBackPerformed(InputAction.CallbackContext context)
    {
        if (!context.performed || !CanAcceptInput())
            return;

        SoundManager.Instance?.PlayUiBack();
        Close();
    }

    private void MoveSelection(int delta)
    {
        if (entries.Count == 0)
            return;

        selectionFromPointer = false;
        if (selectedIndex < 0)
            selectedIndex = delta > 0 ? 0 : entries.Count - 1;
        else
            selectedIndex = (selectedIndex + delta + entries.Count) % entries.Count;

        if (entries[selectedIndex].EquipButton == null)
            equipColumnSelected = false;

        ConsumeInputCooldown();
        RefreshAll();
    }

    /// <summary>Left = the upgrade's buy button, Right = its Equip button.</summary>
    private void SelectColumn(bool equip)
    {
        if (entries.Count == 0)
            return;

        selectionFromPointer = false;
        if (selectedIndex < 0)
            selectedIndex = 0;

        equipColumnSelected = equip && entries[selectedIndex].EquipButton != null;
        ConsumeInputCooldown();
        RefreshAll();
    }

    private void TryBuy(int index)
    {
        if (index < 0 || index >= entries.Count)
            return;

        ConsumeInputCooldown();
        string id = GetShopperId();
        if (PlayerUpgrades.TryBuy(id, entries[index].Type))
            SoundManager.Instance?.PlayUiConfirm();
        else
            SoundManager.Instance?.PlayUiBack();

        RefreshAll();
    }

    private void TryToggleEquip(int index)
    {
        if (index < 0 || index >= entries.Count)
            return;

        ConsumeInputCooldown();
        if (PlayerUpgrades.TryToggleEquipped(GetShopperId(), entries[index].Type))
            SoundManager.Instance?.PlayUiConfirm();
        else
            SoundManager.Instance?.PlayUiBack();

        RefreshAll();
    }

    // ---------- Pointer ----------

    private void HookButtons()
    {
        for (int i = 0; i < entries.Count; i++)
        {
            int index = i;
            Entry entry = entries[i];
            entry.Button.onClick.AddListener(() => OnButtonClicked(index));

            EventTrigger trigger = entry.Button.GetComponent<EventTrigger>();
            if (trigger == null)
                trigger = entry.Button.gameObject.AddComponent<EventTrigger>();

            AddPointerCallback(trigger, EventTriggerType.PointerEnter, _ => OnPointerEnter(index));
            AddPointerCallback(trigger, EventTriggerType.PointerExit, _ => OnPointerExit(index));

            if (entry.EquipButton == null)
                continue;

            // Equip colors must show exactly (green / gray), so no hover tint on top.
            entry.EquipButton.transition = Selectable.Transition.None;
            entry.EquipButton.onClick.AddListener(() => OnEquipClicked(index));

            EventTrigger equipTrigger = entry.EquipButton.GetComponent<EventTrigger>();
            if (equipTrigger == null)
                equipTrigger = entry.EquipButton.gameObject.AddComponent<EventTrigger>();

            AddPointerCallback(equipTrigger, EventTriggerType.PointerEnter, _ => OnEquipPointerEnter(index));
            AddPointerCallback(equipTrigger, EventTriggerType.PointerExit, _ => OnEquipPointerExit(index));
        }
    }

    private static void AddPointerCallback(EventTrigger trigger, EventTriggerType type, UnityAction<BaseEventData> callback)
    {
        EventTrigger.Entry entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(callback);
        trigger.triggers.Add(entry);
    }

    private void OnButtonClicked(int index)
    {
        if (!CanAcceptInput())
            return;

        selectedIndex = index;
        TryBuy(index);
    }

    private void OnEquipClicked(int index)
    {
        if (!CanAcceptInput())
            return;

        // Keep the UI Submit key from clicking this button again right after our own Confirm.
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);

        selectedIndex = index;
        equipColumnSelected = true;
        TryToggleEquip(index);
    }

    private void OnPointerEnter(int index)
    {
        if (!isOpen)
            return;

        // The Equip button is a child, so entering it also enters the upgrade button (child fires first).
        selectedIndex = index;
        equipColumnSelected = entries[index].PointerOverEquip;
        selectionFromPointer = true;
        RefreshAll();
    }

    private void OnEquipPointerEnter(int index)
    {
        entries[index].PointerOverEquip = true;
        if (!isOpen)
            return;

        selectedIndex = index;
        equipColumnSelected = true;
        selectionFromPointer = true;
        RefreshAll();
    }

    private void OnEquipPointerExit(int index)
    {
        entries[index].PointerOverEquip = false;
        if (!isOpen || !selectionFromPointer || selectedIndex != index)
            return;

        equipColumnSelected = false;
        RefreshAll();
    }

    private void OnPointerExit(int index)
    {
        if (!isOpen || !selectionFromPointer || selectedIndex != index)
            return;

        selectedIndex = -1;
        equipColumnSelected = false;
        selectionFromPointer = false;
        RefreshAll();
    }

    // ---------- Display ----------

    private void OnUpgradeChanged(string characterId, UpgradeType type, int level)
    {
        if (isOpen)
            RefreshAll();
    }

    private void OnUpgradeEquippedChanged(string characterId, UpgradeType type, bool equipped)
    {
        if (isOpen)
            RefreshAll();
    }

    private void RefreshAll()
    {
        RefreshTexts();
        RefreshSelectionVisuals();
    }

    private void RefreshTexts()
    {
        if (!isOpen)
            return;

        string id = GetShopperId();
        int crystals = PlayerInventory.Instance != null ? PlayerInventory.Instance.CrystalCount : 0;
        if (crystalAmountText != null)
            crystalAmountText.text = ": " + crystals;

        for (int i = 0; i < entries.Count; i++)
        {
            Entry entry = entries[i];
            if (entry.LevelText != null)
                entry.LevelText.text = "Level: " + PlayerUpgrades.GetLevel(id, entry.Type);

            if (entry.EquipText != null)
                entry.EquipText.text = IsShownEquipped(id, entry) ? equippedText : unequippedText;
        }

        if (crystalCostText == null)
            return;

        bool show = selectedIndex >= 0 && selectedIndex < entries.Count;
        crystalCostText.enabled = show;
        if (!show)
            return;

        UpgradeType type = entries[selectedIndex].Type;
        int level = PlayerUpgrades.GetLevel(id, type);
        if (!PlayerUpgrades.IsAvailable(id, type))
            crystalCostText.text = ": N/A";
        else if (level >= PlayerUpgrades.MaxLevel)
            crystalCostText.text = ": MAX";
        else
            crystalCostText.text = ": - " + PlayerUpgrades.GetPrice(level);
    }

    private void RefreshSelectionVisuals()
    {
        string id = GetShopperId();
        float grow = 1f + Mathf.Max(0f, selectedScaleBonus);
        float equipGrow = 1f + Mathf.Max(0f, equipFocusedScaleBonus);
        for (int i = 0; i < entries.Count; i++)
        {
            Entry entry = entries[i];
            bool selected = isOpen && i == selectedIndex;
            bool equipSelected = selected && equipColumnSelected && entry.EquipButton != null;

            // The row keeps its highlight color; only the focused button (buy or equip) grows.
            entry.Button.transform.localScale = selected && !equipSelected ? entry.BaseScale * grow : entry.BaseScale;
            if (entry.Image != null)
                entry.Image.color = selected ? selectedButtonColor : entry.BaseColor;

            if (entry.EquipButton == null)
                continue;

            entry.EquipButton.transform.localScale = equipSelected ? entry.EquipBaseScale * equipGrow : entry.EquipBaseScale;
            if (entry.EquipImage != null)
            {
                entry.EquipImage.color = equipSelected
                    ? equipFocusedColor
                    : IsShownEquipped(id, entry) ? equippedColor : unequippedColor;
            }
        }
    }

    /// <summary>Not bought yet (or not available for this character) always reads as unequipped.</summary>
    private static bool IsShownEquipped(string characterId, Entry entry)
    {
        return PlayerUpgrades.CanToggleEquipped(characterId, entry.Type) &&
               PlayerUpgrades.IsEquipped(characterId, entry.Type);
    }

    private void SetShopVisible(bool visible)
    {
        if (shopRoot != null)
            shopRoot.SetActive(visible);
    }

    private string GetShopperId()
    {
        PlayerController player = shopper != null ? shopper : PlayerController.ResolveActive();
        return player != null ? player.CharacterId : "Kit";
    }

    // ---------- Setup ----------

    private void CacheUi()
    {
        if (shopRoot == null)
        {
            Transform root = FindDeepChild(transform, "Scrath's Shop Canvas") ??
                             FindDeepChild(transform, "Scratch's Shop Canvas");
            if (root != null)
                shopRoot = root.gameObject;
        }

        Transform search = shopRoot != null ? shopRoot.transform : transform;
        if (aerialActionButton == null)
            aerialActionButton = FindButton(search, "Ariel Action", "Aerial Action");
        if (hyperAbilityButton == null)
            hyperAbilityButton = FindButton(search, "Hyper Ability");
        if (attackStyleButton == null)
            attackStyleButton = FindButton(search, "Attack Style Level", "Attack Style");

        if (aerialActionLevelText == null)
            aerialActionLevelText = FindLevelText(aerialActionButton);
        if (hyperAbilityLevelText == null)
            hyperAbilityLevelText = FindLevelText(hyperAbilityButton);
        if (attackStyleLevelText == null)
            attackStyleLevelText = FindLevelText(attackStyleButton);

        if (aerialActionEquipButton == null)
            aerialActionEquipButton = FindEquipButton(aerialActionButton);
        if (hyperAbilityEquipButton == null)
            hyperAbilityEquipButton = FindEquipButton(hyperAbilityButton);
        if (attackStyleEquipButton == null)
            attackStyleEquipButton = FindEquipButton(attackStyleButton);

        if (crystalAmountText == null)
            crystalAmountText = FindText(search, "Crystal amount text");
        if (crystalCostText == null)
            crystalCostText = FindTextStartingWith(search, "Crystal subtract");
    }

    private void BuildEntries()
    {
        entries.Clear();
        AddEntry(UpgradeType.AerialAction, aerialActionButton, aerialActionLevelText, aerialActionEquipButton);
        AddEntry(UpgradeType.HyperAbility, hyperAbilityButton, hyperAbilityLevelText, hyperAbilityEquipButton);
        AddEntry(UpgradeType.AttackStyle, attackStyleButton, attackStyleLevelText, attackStyleEquipButton);
    }

    private void AddEntry(UpgradeType type, Button button, TMP_Text levelText, Button equipButton)
    {
        if (button == null)
            return;

        Image image = GetButtonImage(button);
        Image equipImage = GetButtonImage(equipButton);

        entries.Add(new Entry
        {
            Type = type,
            Button = button,
            LevelText = levelText,
            Image = image,
            BaseColor = image != null ? image.color : Color.white,
            BaseScale = button.transform.localScale,
            EquipButton = equipButton,
            EquipImage = equipImage,
            EquipText = equipButton != null ? equipButton.GetComponentInChildren<TMP_Text>(true) : null,
            EquipBaseScale = equipButton != null ? equipButton.transform.localScale : Vector3.one
        });
    }

    private static Image GetButtonImage(Button button)
    {
        if (button == null)
            return null;

        Image image = button.targetGraphic as Image;
        return image != null ? image : button.GetComponent<Image>();
    }

    private static Button FindEquipButton(Button upgradeButton)
    {
        if (upgradeButton == null)
            return null;

        Transform t = FindDeepChild(upgradeButton.transform, "Equip Button");
        return t != null ? t.GetComponent<Button>() : null;
    }

    private static Button FindButton(Transform root, params string[] names)
    {
        for (int i = 0; i < names.Length; i++)
        {
            Transform t = FindDeepChild(root, names[i]);
            if (t != null && t.TryGetComponent(out Button button))
                return button;
        }

        return null;
    }

    private static TMP_Text FindLevelText(Button button)
    {
        if (button == null)
            return null;

        Transform t = FindDeepChild(button.transform, "Level text");
        return t != null ? t.GetComponent<TMP_Text>() : null;
    }

    private static TMP_Text FindText(Transform root, string objectName)
    {
        Transform t = FindDeepChild(root, objectName);
        return t != null ? t.GetComponent<TMP_Text>() : null;
    }

    private static TMP_Text FindTextStartingWith(Transform root, string prefix)
    {
        if (root == null)
            return null;

        if (root.name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            root.TryGetComponent(out TMP_Text text))
            return text;

        for (int i = 0; i < root.childCount; i++)
        {
            TMP_Text found = FindTextStartingWith(root.GetChild(i), prefix);
            if (found != null)
                return found;
        }

        return null;
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
        selectedScaleBonus = Mathf.Max(0f, selectedScaleBonus);
        equipFocusedScaleBonus = Mathf.Max(0f, equipFocusedScaleBonus);
        menuInputCooldown = Mathf.Max(0.05f, menuInputCooldown);
        openInputDelay = Mathf.Max(0f, openInputDelay);
        postCloseDashLockSeconds = Mathf.Max(0.05f, postCloseDashLockSeconds);
    }
#endif
}
