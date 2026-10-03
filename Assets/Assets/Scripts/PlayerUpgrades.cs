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
/// Each bought upgrade can be equipped / unequipped; unequipped upgrades have no gameplay effect.
/// </summary>
public static class PlayerUpgrades
{
    public const int MaxLevel = 5;
    public const int PricePerLevel = 100;

    private const string PrefsPrefix = "Gameoverse_Upgrade_";
    private const string EquippedSuffix = "_Equipped";

    // Queried every frame by the player controllers, so lookups must not allocate.
    private static readonly Dictionary<(string, UpgradeType), int> Cache =
        new Dictionary<(string, UpgradeType), int>();
    private static readonly Dictionary<(string, UpgradeType), bool> EquippedCache =
        new Dictionary<(string, UpgradeType), bool>();
    // Scouter item: temporary max level, keyed to the Time.time it wears off.
    private static readonly Dictionary<(string, UpgradeType), float> MaxBoostUntil =
        new Dictionary<(string, UpgradeType), float>();

    public static event Action<string, UpgradeType, int> OnChanged;
    public static event Action<string, UpgradeType, bool> OnEquippedChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        MaxBoostUntil.Clear();
    }

    /// <summary>Bought level, whether or not it is equipped (shop display / pricing).</summary>
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

    /// <summary>
    /// Level that applies in gameplay: the bought level while equipped, otherwise 0.
    /// A Scouter boost counts as max level for its duration.
    /// </summary>
    public static int GetActiveLevel(PlayerController player, UpgradeType type)
    {
        if (player == null || player.UpgradesSuppressed)
            return 0;

        if (IsMaxBoosted(player.CharacterId, type))
            return MaxLevel;

        if (!IsEquipped(player.CharacterId, type))
            return 0;

        return GetLevel(player.CharacterId, type);
    }

    /// <summary>Scouter: <paramref name="type"/> acts as max level for <paramref name="seconds"/>.</summary>
    public static void BoostToMax(string characterId, UpgradeType type, float seconds)
    {
        MaxBoostUntil[(NormalizeId(characterId), type)] = Time.time + Mathf.Max(0f, seconds);
    }

    public static bool IsMaxBoosted(string characterId, UpgradeType type)
    {
        return MaxBoostUntil.Count > 0 &&
               MaxBoostUntil.TryGetValue((NormalizeId(characterId), type), out float until) &&
               Time.time < until;
    }

    /// <summary>Upgrades start equipped (including ones bought before equipping existed).</summary>
    public static bool IsEquipped(string characterId, UpgradeType type)
    {
        var cacheKey = (NormalizeId(characterId), type);
        if (!EquippedCache.TryGetValue(cacheKey, out bool equipped))
        {
            equipped = PlayerPrefs.GetInt(Key(characterId, type) + EquippedSuffix, 1) != 0;
            EquippedCache[cacheKey] = equipped;
        }

        return equipped;
    }

    /// <summary>Only a bought upgrade (level 1+) can be toggled.</summary>
    public static bool CanToggleEquipped(string characterId, UpgradeType type)
    {
        return IsAvailable(characterId, type) && GetLevel(characterId, type) > 0;
    }

    public static bool TryToggleEquipped(string characterId, UpgradeType type)
    {
        if (!CanToggleEquipped(characterId, type))
            return false;

        SetEquipped(characterId, type, !IsEquipped(characterId, type));
        return true;
    }

    public static void SetEquipped(string characterId, UpgradeType type, bool equipped)
    {
        EquippedCache[(NormalizeId(characterId), type)] = equipped;
        PlayerPrefs.SetInt(Key(characterId, type) + EquippedSuffix, equipped ? 1 : 0);
        PlayerPrefs.Save();
        OnEquippedChanged?.Invoke(NormalizeId(characterId), type, equipped);
    }

    /// <summary>Crystals needed to go from <paramref name="currentLevel"/> to the next level.</summary>
    public static int GetPrice(int currentLevel)
    {
        return (Mathf.Clamp(currentLevel, 0, MaxLevel - 1) + 1) * PricePerLevel;
    }

    /// <summary>Kit, Malice, Harlie, Hex (Harlie's styles) and Count have Attack Style upgrades.</summary>
    public static bool IsAvailable(string characterId, UpgradeType type)
    {
        if (type != UpgradeType.AttackStyle)
            return true;

        return Is(characterId, "Kit") || Is(characterId, "Malice") || Is(characterId, "Harlie") || Is(characterId, "Hex") ||
               Is(characterId, "Count");
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
        if (!IsEquipped(characterId, type))
            SetEquipped(characterId, type, true);
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
               GetActiveLevel(player, UpgradeType.HyperAbility) > 0;
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
