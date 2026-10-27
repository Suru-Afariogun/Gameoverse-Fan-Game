using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Shared InputActions factory that always applies saved binding overrides.
/// Use instead of <c>new InputActions()</c> so Pause A remaps affect every consumer.
/// </summary>
public static class GameInput
{
    public static readonly string[] RemappableActionNames =
    {
        "Jump",
        "Attack",
        "Dash",
        "Use Item",
        "Confirm",
        "Quit/Back",
        "Pause",
        "Start",
        "Select"
    };

    private static readonly List<WeakReference<InputActionAsset>> LiveAssets =
        new List<WeakReference<InputActionAsset>>(16);

    public static InputActions Create()
    {
        InputActions actions = new InputActions();
        Register(actions.asset);
        ApplySavedOverrides(actions.asset);
        return actions;
    }

    public static void Register(InputActionAsset asset)
    {
        if (asset == null)
            return;

        PruneDead();
        for (int i = 0; i < LiveAssets.Count; i++)
        {
            if (LiveAssets[i].TryGetTarget(out InputActionAsset existing) && existing == asset)
                return;
        }

        LiveAssets.Add(new WeakReference<InputActionAsset>(asset));
    }

    public static void ApplySavedOverrides(InputActionAsset asset)
    {
        if (asset == null)
            return;

        asset.RemoveAllBindingOverrides();
        string json = GameSettings.BindingOverridesJson;
        if (!string.IsNullOrEmpty(json))
            asset.LoadBindingOverridesFromJson(json);
    }

    public static void ApplySavedOverridesToAll()
    {
        PruneDead();
        for (int i = 0; i < LiveAssets.Count; i++)
        {
            if (LiveAssets[i].TryGetTarget(out InputActionAsset asset) && asset != null)
                ApplySavedOverrides(asset);
        }
    }

    public static void SaveOverridesFrom(InputActionAsset asset)
    {
        if (asset == null)
            return;

        string json = asset.SaveBindingOverridesAsJson();
        GameSettings.SetBindingOverridesJson(json);
        ApplySavedOverridesToAll();
    }

    public static void ResetAllBindings(InputActionAsset asset)
    {
        if (asset != null)
            asset.RemoveAllBindingOverrides();

        GameSettings.ClearBindingOverrides();
        ApplySavedOverridesToAll();
    }

    public static string FormatBindingLabel(InputAction action)
    {
        if (action == null)
            return "—";

        string kb = FindBindingDisplay(action, "Keyboard");
        string pad = FindBindingDisplay(action, "Gamepad");
        if (string.IsNullOrEmpty(kb) && string.IsNullOrEmpty(pad))
            return "(unbound)";
        if (string.IsNullOrEmpty(kb))
            return "Pad " + pad;
        if (string.IsNullOrEmpty(pad))
            return "KB " + kb;
        return "KB " + kb + " / Pad " + pad;
    }

    public static string FindBindingDisplay(InputAction action, string deviceKeyword)
    {
        if (action == null)
            return string.Empty;

        for (int i = 0; i < action.bindings.Count; i++)
        {
            InputBinding binding = action.bindings[i];
            if (binding.isComposite || binding.isPartOfComposite)
                continue;

            string path = binding.effectivePath;
            if (string.IsNullOrEmpty(path))
                continue;
            if (path.IndexOf(deviceKeyword, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            return action.GetBindingDisplayString(i, InputBinding.DisplayStringOptions.DontIncludeInteractions);
        }

        return string.Empty;
    }

    public static int FindBindingIndex(InputAction action, string deviceKeyword)
    {
        if (action == null)
            return -1;

        for (int i = 0; i < action.bindings.Count; i++)
        {
            InputBinding binding = action.bindings[i];
            if (binding.isComposite || binding.isPartOfComposite)
                continue;

            string path = string.IsNullOrEmpty(binding.overridePath) ? binding.path : binding.overridePath;
            if (string.IsNullOrEmpty(path))
                path = binding.path;
            if (string.IsNullOrEmpty(path))
                continue;
            if (path.IndexOf(deviceKeyword, StringComparison.OrdinalIgnoreCase) >= 0)
                return i;
        }

        return -1;
    }

    private static void PruneDead()
    {
        for (int i = LiveAssets.Count - 1; i >= 0; i--)
        {
            if (!LiveAssets[i].TryGetTarget(out _))
                LiveAssets.RemoveAt(i);
        }
    }
}
