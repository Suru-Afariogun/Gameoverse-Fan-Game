using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>Which catalog a shop sells.</summary>
public enum ShopCatalog
{
    ScratchUpgrades = 0,
    BlacksmithGear = 1
}

/// <summary>
/// HomeTown crystal shop (Scratch's upgrades or Blacksmith Cat's gear). Hidden until the owning NPC's
/// dialogue finishes. Up/Down (or stick / mouse hover) picks a row, Left/Right moves between its buy
/// button and its Equip button, Confirm (or click) buys / toggles equip, Back closes.
/// The authored rows are slots: when the catalog has more items than rows, the rows scroll.
/// The info box under the shop explains the selected item for the current character.
/// Levels and equip state are per character (see <see cref="PlayerUpgrades"/> / <see cref="PlayerGear"/>).
/// </summary>
public sealed class UpgradeShop : MonoBehaviour
{
    [Header("Catalog")]
    [SerializeField] private ShopCatalog catalog = ShopCatalog.ScratchUpgrades;
    [Tooltip("Info box title while nothing is selected. Empty = default for the catalog.")]
    [SerializeField] private string shopTitle = "";

    [Header("UI (auto-finds by scene names if empty)")]
    [Tooltip("Defaults to the child whose name ends with \"Shop Canvas\".")]
    [SerializeField] private GameObject shopRoot;
    [SerializeField] private TMP_Text crystalAmountText;
    [Tooltip("Shows the price of the hovered / selected item. Hidden when nothing is selected.")]
    [SerializeField] private TMP_Text crystalCostText;
    [Tooltip("Auto-found as \"Shop Info Title\" under the shop canvas.")]
    [SerializeField] private TMP_Text infoTitleText;
    [Tooltip("Auto-found as \"Shop Info Text\" under the shop canvas.")]
    [SerializeField] private TMP_Text infoBodyText;

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

    /// <summary>One authored row (button + level text + equip button). Shows item windowStart + its index.</summary>
    private sealed class Slot
    {
        public Button Button;
        public TMP_Text LabelText;
        public TMP_Text LevelText;
        public Image Image;
        public Color BaseColor = Color.white;
        public Vector3 BaseScale = Vector3.one;
        public Button EquipButton;
        public Image EquipImage;
        public TMP_Text EquipText;
        public Vector3 EquipBaseScale = Vector3.one;
        public bool PointerOverEquip;
        public UpgradeType BoundUpgrade;
    }

    private readonly List<Slot> slots = new List<Slot>(3);
    // Item codes: (int)UpgradeType for Scratch, (int)GearType for Blacksmith.
    private readonly List<int> items = new List<int>(8);
    private InputActions controls;
    private PlayerController shopper;
    private Action onClosed;
    private Coroutine dashReleaseRoutine;
    private bool isOpen;
    private int selectedItem = -1;
    private int windowStart;
    private bool equipColumnSelected;
    private bool selectionFromPointer;
    private float nextInputTime;
    private bool moveUpHeld;
    private bool moveDownHeld;
    private bool moveLeftHeld;
    private bool moveRightHeld;

    public bool IsOpen => isOpen;
    private bool IsGearShop => catalog == ShopCatalog.BlacksmithGear;

    private void Awake()
    {
        CacheUi();
        BuildSlots();
        BuildItems();
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
        PlayerGear.OnChanged += OnGearChanged;
        PlayerGear.OnEquippedChanged += OnGearEquippedChanged;
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
        PlayerGear.OnChanged -= OnGearChanged;
        PlayerGear.OnEquippedChanged -= OnGearEquippedChanged;

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
        selectedItem = -1;
        windowStart = 0;
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
        selectedItem = -1;
        equipColumnSelected = false;
        for (int i = 0; i < slots.Count; i++)
            slots[i].PointerOverEquip = false;
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

        if (selectedItem < 0)
        {
            MoveSelection(1);
            return;
        }

        if (equipColumnSelected)
            TryToggleEquip(selectedItem);
        else
            TryBuy(selectedItem);
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
        if (items.Count == 0)
            return;

        selectionFromPointer = false;
        if (selectedItem < 0)
            selectedItem = delta > 0 ? windowStart : Mathf.Min(items.Count, windowStart + slots.Count) - 1;
        else
            selectedItem = (selectedItem + delta + items.Count) % items.Count;

        ScrollToSelection();
        ConsumeInputCooldown();
        RefreshAll();
    }

    /// <summary>Left = the item's buy button, Right = its Equip button.</summary>
    private void SelectColumn(bool equip)
    {
        if (items.Count == 0)
            return;

        selectionFromPointer = false;
        if (selectedItem < 0)
            selectedItem = windowStart;

        Slot slot = SlotForItem(selectedItem);
        equipColumnSelected = equip && slot != null && slot.EquipButton != null;
        ConsumeInputCooldown();
        RefreshAll();
    }

    private void ScrollToSelection()
    {
        int visible = Mathf.Max(1, slots.Count);
        if (selectedItem < windowStart)
            windowStart = selectedItem;
        else if (selectedItem >= windowStart + visible)
            windowStart = selectedItem - visible + 1;

        windowStart = Mathf.Clamp(windowStart, 0, Mathf.Max(0, items.Count - visible));

        Slot slot = SlotForItem(selectedItem);
        if (slot == null || slot.EquipButton == null)
            equipColumnSelected = false;
    }

    private void TryBuy(int item)
    {
        if (item < 0 || item >= items.Count)
            return;

        ConsumeInputCooldown();
        string id = GetShopperId();
        bool bought = IsGearShop
            ? PlayerGear.TryBuy(id, (GearType)items[item])
            : PlayerUpgrades.TryBuy(id, (UpgradeType)items[item]);

        if (bought)
            SoundManager.Instance?.PlayUiConfirm();
        else
            SoundManager.Instance?.PlayUiBack();

        RefreshAll();
    }

    private void TryToggleEquip(int item)
    {
        if (item < 0 || item >= items.Count)
            return;

        ConsumeInputCooldown();
        string id = GetShopperId();
        bool toggled = IsGearShop
            ? PlayerGear.TryToggleEquipped(id, (GearType)items[item])
            : PlayerUpgrades.TryToggleEquipped(id, (UpgradeType)items[item]);

        if (toggled)
            SoundManager.Instance?.PlayUiConfirm();
        else
            SoundManager.Instance?.PlayUiBack();

        RefreshAll();
    }

    // ---------- Pointer ----------

    private void HookButtons()
    {
        for (int i = 0; i < slots.Count; i++)
        {
            int index = i;
            Slot slot = slots[i];
            slot.Button.onClick.AddListener(() => OnButtonClicked(index));

            EventTrigger trigger = slot.Button.GetComponent<EventTrigger>();
            if (trigger == null)
                trigger = slot.Button.gameObject.AddComponent<EventTrigger>();

            AddPointerCallback(trigger, EventTriggerType.PointerEnter, _ => OnPointerEnter(index));
            AddPointerCallback(trigger, EventTriggerType.PointerExit, _ => OnPointerExit(index));

            if (slot.EquipButton == null)
                continue;

            // Equip colors must show exactly (green / gray), so no hover tint on top.
            slot.EquipButton.transition = Selectable.Transition.None;
            slot.EquipButton.onClick.AddListener(() => OnEquipClicked(index));

            EventTrigger equipTrigger = slot.EquipButton.GetComponent<EventTrigger>();
            if (equipTrigger == null)
                equipTrigger = slot.EquipButton.gameObject.AddComponent<EventTrigger>();

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

    private int ItemForSlot(int slotIndex)
    {
        int item = windowStart + slotIndex;
        return item >= 0 && item < items.Count ? item : -1;
    }

    private Slot SlotForItem(int item)
    {
        int slotIndex = item - windowStart;
        return item >= 0 && slotIndex >= 0 && slotIndex < slots.Count ? slots[slotIndex] : null;
    }

    private void OnButtonClicked(int slotIndex)
    {
        int item = ItemForSlot(slotIndex);
        if (!CanAcceptInput() || item < 0)
            return;

        selectedItem = item;
        TryBuy(item);
    }

    private void OnEquipClicked(int slotIndex)
    {
        int item = ItemForSlot(slotIndex);
        if (!CanAcceptInput() || item < 0)
            return;

        // Keep the UI Submit key from clicking this button again right after our own Confirm.
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);

        selectedItem = item;
        equipColumnSelected = true;
        TryToggleEquip(item);
    }

    private void OnPointerEnter(int slotIndex)
    {
        int item = ItemForSlot(slotIndex);
        if (!isOpen || item < 0)
            return;

        // The Equip button is a child, so entering it also enters the row button (child fires first).
        selectedItem = item;
        equipColumnSelected = slots[slotIndex].PointerOverEquip;
        selectionFromPointer = true;
        RefreshAll();
    }

    private void OnEquipPointerEnter(int slotIndex)
    {
        slots[slotIndex].PointerOverEquip = true;
        int item = ItemForSlot(slotIndex);
        if (!isOpen || item < 0)
            return;

        selectedItem = item;
        equipColumnSelected = true;
        selectionFromPointer = true;
        RefreshAll();
    }

    private void OnEquipPointerExit(int slotIndex)
    {
        slots[slotIndex].PointerOverEquip = false;
        if (!isOpen || !selectionFromPointer || selectedItem != ItemForSlot(slotIndex))
            return;

        equipColumnSelected = false;
        RefreshAll();
    }

    private void OnPointerExit(int slotIndex)
    {
        if (!isOpen || !selectionFromPointer || selectedItem != ItemForSlot(slotIndex))
            return;

        selectedItem = -1;
        equipColumnSelected = false;
        selectionFromPointer = false;
        RefreshAll();
    }

    // ---------- Catalog ----------

    private int GetLevel(string id, int item)
    {
        return IsGearShop ? PlayerGear.GetLevel(id, (GearType)items[item]) : PlayerUpgrades.GetLevel(id, (UpgradeType)items[item]);
    }

    private bool IsAvailable(string id, int item)
    {
        return IsGearShop || PlayerUpgrades.IsAvailable(id, (UpgradeType)items[item]);
    }

    private int MaxLevel => IsGearShop ? PlayerGear.MaxLevel : PlayerUpgrades.MaxLevel;

    private int GetPrice(int level)
    {
        return IsGearShop ? PlayerGear.GetPrice(level) : PlayerUpgrades.GetPrice(level);
    }

    /// <summary>Not bought yet (or not available for this character) always reads as unequipped.</summary>
    private bool IsShownEquipped(string id, int item)
    {
        if (IsGearShop)
        {
            GearType gear = (GearType)items[item];
            return PlayerGear.CanToggleEquipped(id, gear) && PlayerGear.IsEquipped(id, gear);
        }

        UpgradeType upgrade = (UpgradeType)items[item];
        return PlayerUpgrades.CanToggleEquipped(id, upgrade) && PlayerUpgrades.IsEquipped(id, upgrade);
    }

    private string GetItemName(int item)
    {
        return IsGearShop ? ShopDescriptions.GearName((GearType)items[item]) : ShopDescriptions.UpgradeName((UpgradeType)items[item]);
    }

    private string GetItemDescription(string id, int item)
    {
        return IsGearShop
            ? ShopDescriptions.Gear(id, (GearType)items[item])
            : ShopDescriptions.Upgrade(id, (UpgradeType)items[item]);
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

    private void OnGearChanged(string characterId, GearType type, int level)
    {
        if (isOpen)
            RefreshAll();
    }

    private void OnGearEquippedChanged(string characterId, GearType type, bool equipped)
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

        for (int i = 0; i < slots.Count; i++)
        {
            Slot slot = slots[i];
            int item = ItemForSlot(i);
            bool used = item >= 0;
            if (slot.Button.gameObject.activeSelf != used)
                slot.Button.gameObject.SetActive(used);
            if (!used)
                continue;

            if (IsGearShop && slot.LabelText != null)
                slot.LabelText.text = GetItemName(item);
            if (slot.LevelText != null)
                slot.LevelText.text = "Level: " + GetLevel(id, item);
            if (slot.EquipText != null)
                slot.EquipText.text = IsShownEquipped(id, item) ? equippedText : unequippedText;
        }

        RefreshCostText(id);
        RefreshInfoBox(id);
    }

    private void RefreshCostText(string id)
    {
        if (crystalCostText == null)
            return;

        bool show = selectedItem >= 0 && selectedItem < items.Count;
        crystalCostText.enabled = show;
        if (!show)
            return;

        int level = GetLevel(id, selectedItem);
        if (!IsAvailable(id, selectedItem))
            crystalCostText.text = ": N/A";
        else if (level >= MaxLevel)
            crystalCostText.text = ": MAX";
        else
            crystalCostText.text = ": - " + GetPrice(level);
    }

    private void RefreshInfoBox(string id)
    {
        if (infoTitleText == null && infoBodyText == null)
            return;

        string title;
        string body;
        if (selectedItem >= 0 && selectedItem < items.Count)
        {
            int level = GetLevel(id, selectedItem);
            string state = !IsAvailable(id, selectedItem) ? "Not for " + id
                : level <= 0 ? "Not bought"
                : IsShownEquipped(id, selectedItem) ? "Equipped" : "Unequipped";
            title = GetItemName(selectedItem) + "  Lv " + level + "/" + MaxLevel + "  (" + state + ")";
            if (items.Count > slots.Count)
                title += "  " + (selectedItem + 1) + "/" + items.Count;
            body = GetItemDescription(id, selectedItem);
        }
        else
        {
            title = !string.IsNullOrWhiteSpace(shopTitle) ? shopTitle
                : IsGearShop ? "Blacksmith Cat's Gear" : "Scratch's Upgrades";
            body = (IsGearShop
                       ? "Gear for " + id + ", paid in crystals. Gear is saved per character, and you can unequip it any time."
                       : "Upgrades for " + id + ", paid in crystals. Upgrades are saved per character, and you can unequip them any time.") +
                   "\nEach level costs 100 more crystals than the last (max level " + MaxLevel + ").";
        }

        if (infoTitleText != null)
            infoTitleText.text = title;
        if (infoBodyText != null)
            infoBodyText.text = body + "\n<size=80%>" + BuildNavigationHint() + "</size>";
    }

    private string BuildNavigationHint()
    {
        string browse = items.Count > slots.Count
            ? "<b>Up</b>/<b>Down</b>: browse " + items.Count + " items"
            : "<b>Up</b>/<b>Down</b>: choose";
        return browse + "   <b>Left</b>/<b>Right</b>: buy / equip   <b>Confirm</b>: buy or toggle   <b>Back</b>: leave";
    }

    private void RefreshSelectionVisuals()
    {
        string id = GetShopperId();
        float grow = 1f + Mathf.Max(0f, selectedScaleBonus);
        float equipGrow = 1f + Mathf.Max(0f, equipFocusedScaleBonus);
        for (int i = 0; i < slots.Count; i++)
        {
            Slot slot = slots[i];
            int item = ItemForSlot(i);
            bool selected = isOpen && item >= 0 && item == selectedItem;
            bool equipSelected = selected && equipColumnSelected && slot.EquipButton != null;

            // The row keeps its highlight color; only the focused button (buy or equip) grows.
            slot.Button.transform.localScale = selected && !equipSelected ? slot.BaseScale * grow : slot.BaseScale;
            if (slot.Image != null)
                slot.Image.color = selected ? selectedButtonColor : slot.BaseColor;

            if (slot.EquipButton == null)
                continue;

            slot.EquipButton.transform.localScale = equipSelected ? slot.EquipBaseScale * equipGrow : slot.EquipBaseScale;
            if (slot.EquipImage != null)
            {
                bool shownEquipped = item >= 0 && IsShownEquipped(id, item);
                slot.EquipImage.color = equipSelected
                    ? equipFocusedColor
                    : shownEquipped ? equippedColor : unequippedColor;
            }
        }
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
            Transform root = FindDeepChildEndingWith(transform, "Shop Canvas");
            if (root != null)
                shopRoot = root.gameObject;
        }

        Transform search = shopRoot != null ? shopRoot.transform : transform;
        if (crystalAmountText == null)
            crystalAmountText = FindText(search, "Crystal amount text");
        if (crystalCostText == null)
            crystalCostText = FindTextStartingWith(search, "Crystal subtract");
        if (infoTitleText == null)
            infoTitleText = FindText(search, "Shop Info Title");
        if (infoBodyText == null)
            infoBodyText = FindText(search, "Shop Info Text");
    }

    /// <summary>Every button with a "Level text" child is a row, ordered top to bottom on screen.</summary>
    private void BuildSlots()
    {
        slots.Clear();
        Transform search = shopRoot != null ? shopRoot.transform : transform;
        Button[] buttons = search.GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            Button button = buttons[i];
            Transform levelText = FindDeepChild(button.transform, "Level text");
            if (levelText == null || button.name.Equals("Equip Button", StringComparison.OrdinalIgnoreCase))
                continue;

            Button equipButton = FindEquipButton(button);
            Image image = GetButtonImage(button);
            Image equipImage = GetButtonImage(equipButton);

            slots.Add(new Slot
            {
                Button = button,
                LabelText = FindLabelText(button, levelText, equipButton),
                LevelText = levelText.GetComponent<TMP_Text>(),
                Image = image,
                BaseColor = image != null ? image.color : Color.white,
                BaseScale = button.transform.localScale,
                EquipButton = equipButton,
                EquipImage = equipImage,
                EquipText = equipButton != null ? equipButton.GetComponentInChildren<TMP_Text>(true) : null,
                EquipBaseScale = equipButton != null ? equipButton.transform.localScale : Vector3.one,
                BoundUpgrade = UpgradeFromName(button.name)
            });
        }

        // Layout height, not world position: the canvas may still be hidden / unscaled during Awake.
        slots.Sort((a, b) => RowHeight(b.Button).CompareTo(RowHeight(a.Button)));
    }

    private static float RowHeight(Button button)
    {
        return button.transform is RectTransform rect ? rect.anchoredPosition.y : button.transform.localPosition.y;
    }

    /// <summary>Scratch: each row keeps its authored upgrade. Blacksmith: every gear type, scrolled through the rows.</summary>
    private void BuildItems()
    {
        items.Clear();
        if (IsGearShop)
        {
            for (int i = 0; i < PlayerGear.All.Length; i++)
                items.Add((int)PlayerGear.All[i]);
            return;
        }

        for (int i = 0; i < slots.Count; i++)
            items.Add((int)slots[i].BoundUpgrade);
    }

    private static UpgradeType UpgradeFromName(string objectName)
    {
        if (objectName.IndexOf("Hyper", StringComparison.OrdinalIgnoreCase) >= 0)
            return UpgradeType.HyperAbility;
        if (objectName.IndexOf("Attack", StringComparison.OrdinalIgnoreCase) >= 0)
            return UpgradeType.AttackStyle;
        return UpgradeType.AerialAction;
    }

    /// <summary>The row's own caption: a direct text child that is not the level text or the equip button's.</summary>
    private static TMP_Text FindLabelText(Button button, Transform levelText, Button equipButton)
    {
        Transform t = button.transform;
        for (int i = 0; i < t.childCount; i++)
        {
            Transform child = t.GetChild(i);
            if (child == levelText || (equipButton != null && child == equipButton.transform))
                continue;
            if (child.TryGetComponent(out TMP_Text text))
                return text;
        }

        return null;
    }

    private static Image GetButtonImage(Button button)
    {
        if (button == null)
            return null;

        Image image = button.targetGraphic as Image;
        return image != null ? image : button.GetComponent<Image>();
    }

    private static Button FindEquipButton(Button rowButton)
    {
        if (rowButton == null)
            return null;

        Transform t = FindDeepChild(rowButton.transform, "Equip Button");
        return t != null ? t.GetComponent<Button>() : null;
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

    private static Transform FindDeepChildEndingWith(Transform root, string suffix)
    {
        if (root == null)
            return null;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child.name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return child;

            Transform found = FindDeepChildEndingWith(child, suffix);
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
