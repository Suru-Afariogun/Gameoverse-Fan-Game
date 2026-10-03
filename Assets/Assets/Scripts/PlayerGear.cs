using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Blacksmith Cat's shop gear (Mighty Gunvolt Burst-style equipment).</summary>
public enum GearType
{
    Life = 0,
    Armor = 1,
    Booster = 2,
    Jump = 3,
    Weapon = 4,
    Repair = 5,
    Magnet = 6,
    ItemPouch = 7,
    AutoUse = 8,
    ItemExtender = 9
}

/// <summary>
/// Per-character gear levels bought from Blacksmith Cat, saved permanently in PlayerPrefs.
/// Same rules as Scratch's upgrades: 5 levels, (current level + 1) × 100 crystals, equip toggle.
/// Effect numbers live here so every system reads the same values.
/// </summary>
public static class PlayerGear
{
    public const int MaxLevel = 5;
    public const int PricePerLevel = 100;

    // ---------- Effect tuning ----------
    public const int LifeMaxHealthPerLevel = 1;

    public const float ArmorInvincibilitySecondsPerLevel = 0.15f;
    public const float ArmorKnockbackCutPerLevel = 0.15f;
    public const int ArmorDamageReductionLevel = 3;
    public const int ArmorNoHitStunLevel = 5;

    public const float BoosterMoveSpeedPerLevel = 0.4f;
    public const float BoosterDashCooldownCutPerLevel = 0.1f;
    public const int BoosterExtraAirDashLevel = 5;

    public const float JumpForcePerLevel = 0.04f;

    public const float WeaponShotSpeedPerLevel = 0.1f;
    public const float WeaponWaveRangePerLevel = 0.1f;
    public const int WeaponFinisherLevel = 5;
    public const float WeaponFinisherWaveScale = 1.25f;

    public const float RepairDelaySeconds = 3f;
    public const float RepairBaseIntervalSeconds = 7f;
    public const float RepairIntervalCutPerLevel = 1f;

    public const float MagnetBaseRadius = 2f;
    public const float MagnetRadiusPerLevel = 1f;
    public const float MagnetPullSpeed = 14f;
    public const int MagnetDoubleCrystalLevel = 5;

    public const int ItemPouchSlotsPerLevel = 1;

    /// <summary>Auto Use recharge time by level (index 0 = level 1).</summary>
    private static readonly float[] AutoUseCooldownByLevel = { 20f, 15f, 12f, 9f, 6f };

    public const float ExtenderDurationPerLevel = 0.2f;
    public const int ExtenderUsesPerLevel = 1;

    private const string PrefsPrefix = "Gameoverse_Gear_";
    private const string EquippedSuffix = "_Equipped";

    public static readonly GearType[] All =
    {
        GearType.Life, GearType.Armor, GearType.Booster, GearType.Jump,
        GearType.Weapon, GearType.Repair, GearType.Magnet, GearType.ItemPouch,
        GearType.AutoUse, GearType.ItemExtender
    };

    // Queried every frame by the player controllers, so lookups must not allocate.
    private static readonly Dictionary<(string, GearType), int> Cache = new Dictionary<(string, GearType), int>();
    private static readonly Dictionary<(string, GearType), bool> EquippedCache = new Dictionary<(string, GearType), bool>();

    public static event Action<string, GearType, int> OnChanged;
    public static event Action<string, GearType, bool> OnEquippedChanged;

    /// <summary>Bumped on every level / equip change so players can refresh stats cheaply.</summary>
    public static int Version { get; private set; }

    public static int GetLevel(string characterId, GearType type)
    {
        var cacheKey = (NormalizeId(characterId), type);
        if (!Cache.TryGetValue(cacheKey, out int level))
        {
            level = Mathf.Clamp(PlayerPrefs.GetInt(Key(characterId, type), 0), 0, MaxLevel);
            Cache[cacheKey] = level;
        }

        return level;
    }

    /// <summary>Level that applies in gameplay: the bought level while equipped, otherwise 0.</summary>
    public static int GetActiveLevel(PlayerController player, GearType type)
    {
        if (player == null || player.UpgradesSuppressed)
            return 0;

        string id = player.CharacterId;
        return IsEquipped(id, type) ? GetLevel(id, type) : 0;
    }

    /// <summary>Gear starts equipped once bought.</summary>
    public static bool IsEquipped(string characterId, GearType type)
    {
        var cacheKey = (NormalizeId(characterId), type);
        if (!EquippedCache.TryGetValue(cacheKey, out bool equipped))
        {
            equipped = PlayerPrefs.GetInt(Key(characterId, type) + EquippedSuffix, 1) != 0;
            EquippedCache[cacheKey] = equipped;
        }

        return equipped;
    }

    public static bool CanToggleEquipped(string characterId, GearType type)
    {
        return GetLevel(characterId, type) > 0;
    }

    public static bool TryToggleEquipped(string characterId, GearType type)
    {
        if (!CanToggleEquipped(characterId, type))
            return false;

        SetEquipped(characterId, type, !IsEquipped(characterId, type));
        return true;
    }

    public static void SetEquipped(string characterId, GearType type, bool equipped)
    {
        EquippedCache[(NormalizeId(characterId), type)] = equipped;
        PlayerPrefs.SetInt(Key(characterId, type) + EquippedSuffix, equipped ? 1 : 0);
        PlayerPrefs.Save();
        Version++;
        OnEquippedChanged?.Invoke(NormalizeId(characterId), type, equipped);
    }

    public static int GetPrice(int currentLevel)
    {
        return (Mathf.Clamp(currentLevel, 0, MaxLevel - 1) + 1) * PricePerLevel;
    }

    public static bool CanBuy(string characterId, GearType type)
    {
        return GetLevel(characterId, type) < MaxLevel;
    }

    public static bool TryBuy(string characterId, GearType type)
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

    public static void SetLevel(string characterId, GearType type, int level)
    {
        level = Mathf.Clamp(level, 0, MaxLevel);
        Cache[(NormalizeId(characterId), type)] = level;
        PlayerPrefs.SetInt(Key(characterId, type), level);
        PlayerPrefs.Save();
        Version++;
        OnChanged?.Invoke(NormalizeId(characterId), type, level);
    }

    // ---------- Effects ----------

    public static int MaxHealthBonus(PlayerController player)
    {
        return LifeMaxHealthPerLevel * GetActiveLevel(player, GearType.Life);
    }

    public static float InvincibilityBonusSeconds(PlayerController player)
    {
        return ArmorInvincibilitySecondsPerLevel * GetActiveLevel(player, GearType.Armor);
    }

    public static float KnockbackMultiplier(PlayerController player)
    {
        return Mathf.Max(0f, 1f - ArmorKnockbackCutPerLevel * GetActiveLevel(player, GearType.Armor));
    }

    /// <summary>Armor level 3+: hits deal 1 less damage (never below 1).</summary>
    public static int ReduceIncomingDamage(PlayerController player, int amount)
    {
        if (amount <= 1 || GetActiveLevel(player, GearType.Armor) < ArmorDamageReductionLevel)
            return amount;

        return amount - 1;
    }

    public static bool IgnoresHitStun(PlayerController player)
    {
        return GetActiveLevel(player, GearType.Armor) >= ArmorNoHitStunLevel;
    }

    public static float MoveSpeedBonus(PlayerController player)
    {
        return BoosterMoveSpeedPerLevel * GetActiveLevel(player, GearType.Booster);
    }

    public static float DashCooldownMultiplier(PlayerController player)
    {
        return Mathf.Max(0f, 1f - BoosterDashCooldownCutPerLevel * GetActiveLevel(player, GearType.Booster));
    }

    public static int ExtraAirDashes(PlayerController player)
    {
        return GetActiveLevel(player, GearType.Booster) >= BoosterExtraAirDashLevel ? 1 : 0;
    }

    public static float JumpForceMultiplier(PlayerController player)
    {
        return 1f + JumpForcePerLevel * GetActiveLevel(player, GearType.Jump);
    }

    /// <summary>+1 damage at levels 1-2, +2 at 3-4, +3 at 5.</summary>
    public static int WeaponDamageBonus(PlayerController player)
    {
        return WeaponDamageBonusForLevel(GetActiveLevel(player, GearType.Weapon));
    }

    public static int WeaponDamageBonusForLevel(int level)
    {
        return level <= 0 ? 0 : (level + 1) / 2;
    }

    public static float WeaponShotSpeedMultiplier(PlayerController player)
    {
        return 1f + WeaponShotSpeedPerLevel * GetActiveLevel(player, GearType.Weapon);
    }

    public static float WeaponWaveRangeMultiplier(PlayerController player)
    {
        return 1f + WeaponWaveRangePerLevel * GetActiveLevel(player, GearType.Weapon);
    }

    public static bool WeaponFinisher(PlayerController player)
    {
        return GetActiveLevel(player, GearType.Weapon) >= WeaponFinisherLevel;
    }

    /// <summary>Seconds between 1 HP repairs, or 0 when Repair Gear is off.</summary>
    public static float RepairIntervalSeconds(PlayerController player)
    {
        int level = GetActiveLevel(player, GearType.Repair);
        if (level <= 0)
            return 0f;

        return Mathf.Max(1f, RepairBaseIntervalSeconds - RepairIntervalCutPerLevel * level);
    }

    public static float MagnetRadius(PlayerController player)
    {
        int level = GetActiveLevel(player, GearType.Magnet);
        return level <= 0 ? 0f : MagnetBaseRadius + MagnetRadiusPerLevel * level;
    }

    public static int CrystalMultiplier(PlayerController player)
    {
        return GetActiveLevel(player, GearType.Magnet) >= MagnetDoubleCrystalLevel ? 2 : 1;
    }

    /// <summary>How many usable items the player can carry at once (1 without Item Pouch).</summary>
    public static int ItemSlots(PlayerController player)
    {
        return 1 + ItemPouchSlotsPerLevel * GetActiveLevel(player, GearType.ItemPouch);
    }

    /// <summary>Seconds between Auto Use saves, or 0 when Auto Use is off.</summary>
    public static float AutoUseCooldownSeconds(PlayerController player)
    {
        int level = GetActiveLevel(player, GearType.AutoUse);
        if (level <= 0)
            return 0f;

        return AutoUseCooldownByLevel[Mathf.Clamp(level, 1, AutoUseCooldownByLevel.Length) - 1];
    }

    /// <summary>Item Extender: Super Candy / Scouter time multiplier.</summary>
    public static float ItemDurationMultiplier(PlayerController player)
    {
        return 1f + ExtenderDurationPerLevel * GetActiveLevel(player, GearType.ItemExtender);
    }

    /// <summary>Item Extender: uses per Bomb / Banana Phone / Silver Swords before it is gone.</summary>
    public static int ItemUses(PlayerController player)
    {
        return 1 + ExtenderUsesPerLevel * GetActiveLevel(player, GearType.ItemExtender);
    }

    private static string Key(string characterId, GearType type)
    {
        return PrefsPrefix + NormalizeId(characterId) + "_" + type;
    }

    private static string NormalizeId(string characterId)
    {
        return string.IsNullOrWhiteSpace(characterId) ? "Kit" : characterId.Trim();
    }
}
