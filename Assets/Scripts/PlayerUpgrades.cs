using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Scratch's shop upgrade tracks.</summary>
public enum UpgradeType
{
    AerialAction = 0,
    HyperAbility = 1,
    AttackStyle = 2
}

/// <summary>
/// Per-character upgrade levels bought from Scratch's shop, saved permanently in PlayerPrefs.
/// Price to reach the next level = (current level + 1) × 100 crystals.
/// </summary>
public static class PlayerUpgrades
{
    public const int MaxLevel = 5;
    public const int PricePerLevel = 100;

    private const string PrefsPrefix = "Gameoverse_Upgrade_";

    // Queried every frame by the player controllers, so lookups must not allocate.
    private static readonly Dictionary<(string, UpgradeType), int> Cache =
        new Dictionary<(string, UpgradeType), int>();

    public static event Action<string, UpgradeType, int> OnChanged;

    public static int GetLevel(string characterId, UpgradeType type)
    {
        var cacheKey = (NormalizeId(characterId), type);
        if (!Cache.TryGetValue(cacheKey, out int level))
        {
            level = Mathf.Clamp(PlayerPrefs.GetInt(Key(characterId, type), 0), 0, MaxLevel);
            Cache[cacheKey] = level;
        }

        return level;
    }

    public static int GetLevel(PlayerController player, UpgradeType type)
    {
        return player != null ? GetLevel(player.CharacterId, type) : 0;
    }

    /// <summary>Crystals needed to go from <paramref name="currentLevel"/> to the next level.</summary>
    public static int GetPrice(int currentLevel)
    {
        return (Mathf.Clamp(currentLevel, 0, MaxLevel - 1) + 1) * PricePerLevel;
    }

    /// <summary>Only Kit and Malice have Attack Style upgrades for now.</summary>
    public static bool IsAvailable(string characterId, UpgradeType type)
    {
        if (type != UpgradeType.AttackStyle)
            return true;

        return Is(characterId, "Kit") || Is(characterId, "Malice");
    }

    public static bool CanBuy(string characterId, UpgradeType type)
    {
        return IsAvailable(characterId, type) && GetLevel(characterId, type) < MaxLevel;
    }

    public static bool TryBuy(string characterId, UpgradeType type)
    {
        if (!CanBuy(characterId, type))
            return false;

        int level = GetLevel(characterId, type);
        PlayerInventory inventory = PlayerInventory.Instance;
        if (inventory == null || !inventory.TrySpendCrystals(GetPrice(level)))
            return false;

        SetLevel(characterId, type, level + 1);
        return true;
    }

    public static void SetLevel(string characterId, UpgradeType type, int level)
    {
        level = Mathf.Clamp(level, 0, MaxLevel);
        Cache[(NormalizeId(characterId), type)] = level;
        PlayerPrefs.SetInt(Key(characterId, type), level);
        PlayerPrefs.Save();
        OnChanged?.Invoke(NormalizeId(characterId), type, level);
    }

    /// <summary>Hyper Ability bonuses only apply while the character is alive at max health.</summary>
    public static bool IsHyperActive(PlayerController player)
    {
        return player != null &&
               !player.IsDead &&
               player.CurrentHealth >= player.MaxHealth &&
               GetLevel(player, UpgradeType.HyperAbility) > 0;
    }

    private static string Key(string characterId, UpgradeType type)
    {
        return PrefsPrefix + NormalizeId(characterId) + "_" + type;
    }

    private static string NormalizeId(string characterId)
    {
        return string.IsNullOrWhiteSpace(characterId) ? "Kit" : characterId.Trim();
    }

    private static bool Is(string characterId, string expected)
    {
        return string.Equals(NormalizeId(characterId), expected, StringComparison.OrdinalIgnoreCase);
    }
}
