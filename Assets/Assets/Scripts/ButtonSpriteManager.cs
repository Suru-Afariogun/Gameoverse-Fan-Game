using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.SceneManagement;

/// <summary>
/// Central map of input-action button sprites + display labels per controller family.
/// Assign PlayStation / Xbox / Switch / Keyboard sprites per action; leave a slot empty
/// to fall back to that action's Default sprite (then built-in text labels for tutorials).
/// </summary>
public class ButtonSpriteManager : MonoBehaviour
{
    public static ButtonSpriteManager Instance { get; private set; }

    private static bool createdAtRuntime;

    public enum ControlDeviceKind
    {
        Keyboard = 0,
        PlayStation = 1,
        Xbox = 2,
        Switch = 3
    }

    [Serializable]
    public class ActionButtonSprites
    {
        [Tooltip("Must match InputActions action name, e.g. Jump, Confirm, Quit/Back.")]
        public string actionName;

        [Header("Sprites (empty → Default sprite)")]
        public Sprite playStation;
        public Sprite xbox;
        public Sprite switchController;
        public Sprite keyboard;
        [Tooltip("Used when the active device slot is empty.")]
        public Sprite defaultSprite;

        [Header("Optional text labels (empty → built-in defaults)")]
        public string playStationLabel;
        public string xboxLabel;
        public string switchLabel;
        public string keyboardLabel;
        public string defaultLabel;
    }

    [Header("Action Sprite Sets")]
    [SerializeField] private List<ActionButtonSprites> actions = new List<ActionButtonSprites>();

    [Header("Character Button Colors (tutorial text)")]
    [SerializeField] private Color kitButtonColor = new Color(1f, 0.835f, 0.29f, 1f);      // yellow
    [SerializeField] private Color maliceButtonColor = new Color(0.702f, 0.533f, 1f, 1f); // purple

    [Header("Device Tracking")]
    [SerializeField] private ControlDeviceKind fallbackDevice = ControlDeviceKind.Keyboard;

    public event Action<ControlDeviceKind> DeviceChanged;

    public ControlDeviceKind CurrentDevice { get; private set; } = ControlDeviceKind.Keyboard;

    private bool hasReceivedExplicitInput;
    private IDisposable anyButtonPressSubscription;

    private readonly Dictionary<string, ActionButtonSprites> lookup =
        new Dictionary<string, ActionButtonSprites>(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] DefaultActionNames =
    {
        "Jump",
        "Attack",
        "Dash",
        "Confirm",
        "Quit/Back",
        "Pause",
        "Start",
        "Select",
        "Up",
        "Down",
        "Left",
        "Right",
        "Movement"
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureExistsAfterLoad()
    {
        EnsureExists();
    }

    public static ButtonSpriteManager EnsureExists()
    {
        if (Instance != null)
            return Instance;

        ButtonSpriteManager existing = FindFirstObjectByType<ButtonSpriteManager>();
        if (existing != null)
        {
            Instance = existing;
            return Instance;
        }

        GameObject go = new GameObject("ButtonSpriteManager");
        Instance = go.AddComponent<ButtonSpriteManager>();
        createdAtRuntime = true;
        DontDestroyOnLoad(go);
        return Instance;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // Scene-authored manager replaces a temporary runtime stub.
            if (createdAtRuntime && Instance != this)
            {
                Destroy(Instance.gameObject);
                createdAtRuntime = false;
                Instance = this;
            }
            else
            {
                Destroy(gameObject);
                return;
            }
        }
        else
        {
            Instance = this;
        }

        if (transform.parent == null)
            DontDestroyOnLoad(gameObject);

        EnsureDefaultActionRows();
        RebuildLookup();
        CurrentDevice = DetectPreferredStartupDevice();
        InputSystem.onActionChange += OnActionChange;
        anyButtonPressSubscription?.Dispose();
        anyButtonPressSubscription = InputSystem.onAnyButtonPress.Call(OnAnyButtonPress);
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        InputSystem.onActionChange -= OnActionChange;
        anyButtonPressSubscription?.Dispose();
        anyButtonPressSubscription = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;

        if (Instance == this)
            Instance = null;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        EnsureDefaultActionRows();
    }
#endif

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Keep tracking across scenes.
    }

    private void EnsureDefaultActionRows()
    {
        if (actions == null)
            actions = new List<ActionButtonSprites>();

        for (int i = 0; i < DefaultActionNames.Length; i++)
        {
            string name = DefaultActionNames[i];
            bool found = false;
            for (int a = 0; a < actions.Count; a++)
            {
                if (actions[a] != null &&
                    string.Equals(actions[a].actionName, name, StringComparison.OrdinalIgnoreCase))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                actions.Add(new ActionButtonSprites
                {
                    actionName = name
                });
            }
        }
    }

    private void RebuildLookup()
    {
        lookup.Clear();
        if (actions == null)
            return;

        for (int i = 0; i < actions.Count; i++)
        {
            ActionButtonSprites entry = actions[i];
            if (entry == null || string.IsNullOrWhiteSpace(entry.actionName))
                continue;

            lookup[entry.actionName.Trim()] = entry;
        }
    }

    private ControlDeviceKind DetectPreferredStartupDevice()
    {
        if (Gamepad.current != null)
            return ResolveDevice(Gamepad.current);

        if (Keyboard.current != null)
            return ControlDeviceKind.Keyboard;

        return fallbackDevice;
    }

    private void OnActionChange(object obj, InputActionChange change)
    {
        if (change != InputActionChange.ActionPerformed)
            return;

        if (obj is not InputAction action)
            return;

        InputControl control = action.activeControl;
        if (control == null)
            return;

        hasReceivedExplicitInput = true;
        SetCurrentDevice(ResolveDevice(control.device));
    }

    private void OnAnyButtonPress(InputControl control)
    {
        if (control?.device == null)
            return;

        hasReceivedExplicitInput = true;
        SetCurrentDevice(ResolveDevice(control.device));
    }

    /// <summary>
    /// Call before building tutorial / prompt text so labels match the active device.
    /// Uses the last pressed device when known; otherwise prefers a connected gamepad.
    /// </summary>
    public void SyncDeviceForPrompts()
    {
        if (hasReceivedExplicitInput)
        {
            // Re-assert from the live current gamepad/keyboard so labels cannot drift.
            if (CurrentDevice != ControlDeviceKind.Keyboard && Gamepad.current != null)
                SetCurrentDevice(ResolveDevice(Gamepad.current));
            else if (CurrentDevice == ControlDeviceKind.Keyboard && Keyboard.current != null)
                SetCurrentDevice(ControlDeviceKind.Keyboard);
            return;
        }

        if (Gamepad.current != null)
            SetCurrentDevice(ResolveDevice(Gamepad.current));
        else if (Keyboard.current != null)
            SetCurrentDevice(ControlDeviceKind.Keyboard);
    }

    /// <summary>Call from gameplay input callbacks when you already have the device.</summary>
    public void NotifyDevice(InputDevice device)
    {
        if (device == null)
            return;

        hasReceivedExplicitInput = true;
        SetCurrentDevice(ResolveDevice(device));
    }

    public void SetCurrentDevice(ControlDeviceKind kind)
    {
        if (CurrentDevice == kind)
            return;

        CurrentDevice = kind;
        DeviceChanged?.Invoke(kind);
    }

    public static ControlDeviceKind ResolveDevice(InputDevice device)
    {
        if (device == null)
            return ControlDeviceKind.Keyboard;

        if (device is Keyboard)
            return ControlDeviceKind.Keyboard;

        string layout = device.layout ?? string.Empty;
        if (!string.IsNullOrEmpty(layout))
        {
            if (InputSystem.IsFirstLayoutBasedOnSecond(layout, "DualShockGamepad") ||
                InputSystem.IsFirstLayoutBasedOnSecond(layout, "DualSenseGamepadHID"))
                return ControlDeviceKind.PlayStation;

            if (InputSystem.IsFirstLayoutBasedOnSecond(layout, "XInputController") ||
                InputSystem.IsFirstLayoutBasedOnSecond(layout, "XboxOneGamepad") ||
                InputSystem.IsFirstLayoutBasedOnSecond(layout, "XboxGamepadMacOS") ||
                InputSystem.IsFirstLayoutBasedOnSecond(layout, "XboxOneGampadMacOSWireless"))
                return ControlDeviceKind.Xbox;

            if (InputSystem.IsFirstLayoutBasedOnSecond(layout, "SwitchProControllerHID") ||
                InputSystem.IsFirstLayoutBasedOnSecond(layout, "JoyCon") ||
                InputSystem.IsFirstLayoutBasedOnSecond(layout, "Switch"))
                return ControlDeviceKind.Switch;

            if (InputSystem.IsFirstLayoutBasedOnSecond(layout, "Gamepad"))
            {
                // Fall through to name checks for ambiguous generic gamepads.
            }
        }

        string name = device.displayName ?? device.name ?? string.Empty;
        string hay = (layout + " " + name).ToLowerInvariant();

        if (hay.Contains("dualshock") || hay.Contains("dualsense") || hay.Contains("playstation") ||
            hay.Contains("sony") || hay.Contains("ps4") || hay.Contains("ps5") ||
            hay.Contains("wireless controller"))
            return ControlDeviceKind.PlayStation;

        if (hay.Contains("switch") || hay.Contains("joy-con") || hay.Contains("joycon") ||
            hay.Contains("nintendo"))
            return ControlDeviceKind.Switch;

        if (hay.Contains("xbox") || hay.Contains("xinput"))
            return ControlDeviceKind.Xbox;

        if (device is Gamepad)
            return ControlDeviceKind.Xbox;

        return ControlDeviceKind.Keyboard;
    }

    public Sprite GetSprite(string actionName)
    {
        return GetSprite(actionName, CurrentDevice);
    }

    public Sprite GetSprite(string actionName, ControlDeviceKind device)
    {
        GetSprite(actionName, device, out Sprite sprite, out _);
        return sprite;
    }

    /// <summary>
    /// True when the resolved glyph comes from a filled PlayStation / Xbox / Switch slot
    /// (not Keyboard, and not falling back to Default).
    /// </summary>
    public bool UsesEnlargedControllerSprite(string actionName)
    {
        GetSprite(actionName, CurrentDevice, out _, out bool enlarge);
        return enlarge;
    }

    public void GetSprite(string actionName, ControlDeviceKind device, out Sprite sprite, out bool useEnlargedScale)
    {
        sprite = null;
        useEnlargedScale = false;

        if (string.IsNullOrWhiteSpace(actionName))
            return;

        if (!TryGetEntry(actionName, out ActionButtonSprites entry))
            return;

        if (device == ControlDeviceKind.Keyboard)
        {
            sprite = entry.keyboard != null ? entry.keyboard : entry.defaultSprite;
            useEnlargedScale = false;
            return;
        }

        Sprite slot = device switch
        {
            ControlDeviceKind.PlayStation => entry.playStation,
            ControlDeviceKind.Xbox => entry.xbox,
            ControlDeviceKind.Switch => entry.switchController,
            _ => null
        };

        if (slot != null)
        {
            sprite = slot;
            useEnlargedScale = true;
            return;
        }

        // Fallback default / keyboard — keep normal size.
        sprite = entry.defaultSprite != null ? entry.defaultSprite : entry.keyboard;
        useEnlargedScale = false;
    }

    public string GetLabel(string actionName)
    {
        return GetLabel(actionName, CurrentDevice);
    }

    public string GetLabel(string actionName, ControlDeviceKind device)
    {
        if (string.IsNullOrWhiteSpace(actionName))
            return actionName ?? string.Empty;

        // Priority:
        // 1) Inspector override for THIS device only
        // 2) Built-in per-device label (Square / X / Y / M, etc.)
        // 3) defaultLabel only as last resort (must never override a known device label)
        if (TryGetEntry(actionName, out ActionButtonSprites entry))
        {
            string overrideLabel = device switch
            {
                ControlDeviceKind.PlayStation => entry.playStationLabel,
                ControlDeviceKind.Xbox => entry.xboxLabel,
                ControlDeviceKind.Switch => entry.switchLabel,
                ControlDeviceKind.Keyboard => entry.keyboardLabel,
                _ => null
            };

            if (!string.IsNullOrWhiteSpace(overrideLabel))
                return overrideLabel.Trim();
        }

        string builtIn = GetBuiltInLabel(actionName, device);
        if (!string.IsNullOrWhiteSpace(builtIn))
            return builtIn;

        if (TryGetEntry(actionName, out entry) && !string.IsNullOrWhiteSpace(entry.defaultLabel))
            return entry.defaultLabel.Trim();

        return actionName.Trim();
    }

    public string DeviceDisplayName()
    {
        return DeviceDisplayName(CurrentDevice);
    }

    public static string DeviceDisplayName(ControlDeviceKind device)
    {
        return device switch
        {
            ControlDeviceKind.PlayStation => "PlayStation Controller",
            ControlDeviceKind.Xbox => "Xbox Controller",
            ControlDeviceKind.Switch => "Switch Controller",
            _ => "Keyboard"
        };
    }

    public Color ActiveCharacterButtonColor()
    {
        return PlayerAttackStyle.IsMaliceSelected() ? maliceButtonColor : kitButtonColor;
    }

    public string ActiveCharacterButtonColorHex()
    {
        return ColorUtility.ToHtmlStringRGB(ActiveCharacterButtonColor());
    }

    /// <summary>Colored bold button name for tutorial TMP rich text.</summary>
    public string FormatButton(string actionName)
    {
        string label = GetLabel(actionName);
        string hex = ActiveCharacterButtonColorHex();
        return $"<color=#{hex}><b>{label}</b></color>";
    }

    /// <summary>"press {button} on the {device}" with colored button.</summary>
    public string FormatPress(string actionName)
    {
        return $"press {FormatButton(actionName)} on the {DeviceDisplayName()}";
    }

    public string FormatHold(string actionName)
    {
        return $"hold {FormatButton(actionName)} on the {DeviceDisplayName()}";
    }

    /// <summary>Move left/right phrasing for the active device.</summary>
    public string FormatMoveHorizontal()
    {
        string hex = ActiveCharacterButtonColorHex();
        if (CurrentDevice == ControlDeviceKind.Keyboard)
        {
            return
                $"press <color=#{hex}><b>A</b></color> / <color=#{hex}><b>D</b></color> on the Keyboard, " +
                $"or the <color=#{hex}><b>Arrow Keys</b></color>";
        }

        return $"use the {FormatButton("Movement")} on the {DeviceDisplayName()}";
    }

    public string FormatAimUp()
    {
        string hex = ActiveCharacterButtonColorHex();
        if (CurrentDevice == ControlDeviceKind.Keyboard)
        {
            return
                $"hold <color=#{hex}><b>W</b></color> on the Keyboard, or <color=#{hex}><b>Up Arrow</b></color>";
        }

        return
            $"hold {FormatButton("Up")} or tilt the {FormatButton("Movement")} up on the {DeviceDisplayName()}";
    }

    public string FormatAimDown()
    {
        string hex = ActiveCharacterButtonColorHex();
        if (CurrentDevice == ControlDeviceKind.Keyboard)
        {
            return
                $"hold <color=#{hex}><b>S</b></color> on the Keyboard, or <color=#{hex}><b>Down Arrow</b></color>";
        }

        return
            $"hold {FormatButton("Down")} or tilt the {FormatButton("Movement")} down on the {DeviceDisplayName()}";
    }

    /// <summary>Inventory open: Start and/or Pause depending on device.</summary>
    public string FormatOpenInventory()
    {
        if (CurrentDevice == ControlDeviceKind.Keyboard)
        {
            string hex = ActiveCharacterButtonColorHex();
            return
                $"press <color=#{hex}><b>Enter</b></color> or <color=#{hex}><b>Escape</b></color> on the Keyboard";
        }

        // Gamepad Start is shared by inventory Pause/Start in this project.
        return $"press {FormatButton("Start")} on the {DeviceDisplayName()}";
    }

    public string FormatOpenSystemPause()
    {
        return FormatPress("Select");
    }

    private bool TryGetEntry(string actionName, out ActionButtonSprites entry)
    {
        if (lookup.Count == 0)
            RebuildLookup();

        return lookup.TryGetValue(actionName.Trim(), out entry);
    }

    private static string GetBuiltInLabel(string actionName, ControlDeviceKind device)
    {
        string key = actionName.Trim();

        // Matches Assets/InputActions.inputactions:
        // Jump / Quit/Back = buttonSouth
        // Attack           = buttonWest
        // Dash / Confirm   = buttonEast
        // Pause / Start    = gamepad start
        // Select           = gamepad select
        //
        // Face-button names players actually say:
        //   PlayStation: South=X, East=O, West=Square, North=Triangle
        //   Xbox:        South=A, East=B, West=X, North=Y
        //   Switch:      South=B, East=A, West=Y, North=X
        switch (key)
        {
            case "Jump":
                return device switch
                {
                    ControlDeviceKind.PlayStation => "X",
                    ControlDeviceKind.Xbox => "A",
                    ControlDeviceKind.Switch => "B",
                    _ => "Space"
                };

            case "Quit/Back":
                return device switch
                {
                    ControlDeviceKind.PlayStation => "X",
                    ControlDeviceKind.Xbox => "A",
                    ControlDeviceKind.Switch => "B",
                    _ => "B"
                };

            case "Attack":
                return device switch
                {
                    ControlDeviceKind.PlayStation => "Square",
                    ControlDeviceKind.Xbox => "X",
                    ControlDeviceKind.Switch => "Y",
                    _ => "M"
                };

            case "Dash":
            case "Confirm":
                return device switch
                {
                    ControlDeviceKind.PlayStation => "O",
                    ControlDeviceKind.Xbox => "B",
                    ControlDeviceKind.Switch => "A",
                    _ => "M"
                };

            case "Pause":
                return device switch
                {
                    ControlDeviceKind.PlayStation => "Options",
                    ControlDeviceKind.Xbox => "Menu",
                    ControlDeviceKind.Switch => "+",
                    _ => "Escape"
                };

            case "Start":
                return device switch
                {
                    ControlDeviceKind.PlayStation => "Options",
                    ControlDeviceKind.Xbox => "Menu",
                    ControlDeviceKind.Switch => "+",
                    _ => "Enter"
                };

            case "Select":
                return device switch
                {
                    ControlDeviceKind.PlayStation => "Share",
                    ControlDeviceKind.Xbox => "View",
                    ControlDeviceKind.Switch => "-",
                    _ => "Tab"
                };

            case "Up":
                return device switch
                {
                    ControlDeviceKind.Keyboard => "W / Up Arrow",
                    _ => "D-pad Up"
                };

            case "Down":
                return device switch
                {
                    ControlDeviceKind.Keyboard => "S / Down Arrow",
                    _ => "D-pad Down"
                };

            case "Left":
                return device switch
                {
                    ControlDeviceKind.Keyboard => "A / Left Arrow",
                    _ => "D-pad Left"
                };

            case "Right":
                return device switch
                {
                    ControlDeviceKind.Keyboard => "D / Right Arrow",
                    _ => "D-pad Right"
                };

            case "Movement":
                return device switch
                {
                    ControlDeviceKind.Keyboard => "A / D or Arrow Keys",
                    _ => "Left Stick"
                };

            default:
                return key;
        }
    }
}
