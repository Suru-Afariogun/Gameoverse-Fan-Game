using System;
using UnityEngine;

/// <summary>
/// Temporary Kit attack-style pick (Item Shop placeholders). Malice ignores this.
/// Resets to Normal on first HomeTown load each session; Kaboodle picks persist until then.
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
        switch (style)
        {
            case AttackStyleId.SpreadShot:
                return "Spread Shot";
            case AttackStyleId.MachineGun:
                return "Machine Gun";
            default:
                return "Normal";
        }
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
