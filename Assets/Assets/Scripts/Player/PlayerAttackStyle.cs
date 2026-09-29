using System;
using UnityEngine;

/// <summary>
/// Session attack-style pick from the Item Shop.
/// Kit: Spread Shot / Machine Gun. Malice: Life Steal / Cruel Claw (same slots).
/// Resets to Normal on first HomeTown load each session.
/// </summary>
public enum AttackStyleId
{
    Normal = 0,
    SpreadShot = 1,
    MachineGun = 2
}

public static class PlayerAttackStyle
{
    public static AttackStyleId Selected { get; private set; } = AttackStyleId.Normal;

    public static event Action<AttackStyleId> OnChanged;

    /// <summary>HomeTown first load only — other scenes keep the session pick.</summary>
    public static void ResetToNormal()
    {
        Selected = AttackStyleId.Normal;
        OnChanged?.Invoke(Selected);
    }

    public static string DisplayName(AttackStyleId style)
    {
        if (IsHarlieSelected())
        {
            switch (style)
            {
                case AttackStyleId.SpreadShot:
                    return "Speed Style";
                case AttackStyleId.MachineGun:
                    return "Heavy Style";
                default:
                    return "Normal";
            }
        }

        bool malice = IsMaliceSelected();
        switch (style)
        {
            case AttackStyleId.SpreadShot:
                return malice ? "Life Steal" : "Spread Shot";
            case AttackStyleId.MachineGun:
                return malice ? "Cruel Claw" : "Machine Gun";
            default:
                return "Normal";
        }
    }

    public static bool IsHarlieSelected()
    {
        if (PlayerController.Active != null &&
            string.Equals(PlayerController.Active.CharacterId, "Harlie", StringComparison.OrdinalIgnoreCase))
            return true;

        return string.Equals(PlayerSpawner.SelectedCharacterId, "Harlie", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsMaliceSelected()
    {
        if (PlayerController.Active != null &&
            string.Equals(PlayerController.Active.CharacterId, "Malice", StringComparison.OrdinalIgnoreCase))
            return true;

        return string.Equals(PlayerSpawner.SelectedCharacterId, "Malice", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Equip a style. Use Attack on the equipped shop button (Kaboodle) to clear back to Normal.</summary>
    public static void Set(AttackStyleId style)
    {
        Selected = style;
        OnChanged?.Invoke(Selected);
    }

    public static bool Is(AttackStyleId style)
    {
        return Selected == style;
    }
}
