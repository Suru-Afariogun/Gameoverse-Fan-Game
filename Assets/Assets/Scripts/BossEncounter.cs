using System;
using UnityEngine;

/// <summary>
/// Remembers which boss the player picked on Kaboodle's Boss Fight page
/// so the boss scene can spawn (or keep) the matching boss.
/// </summary>
public static class BossEncounter
{
    public const string BossIdKit = "Kit";
    public const string BossIdMalice = "Malice";
    public const string BossIdHarlie = "Harlie";
    public const string BossIdHex = "Hex";
    public const string BossIdCount = "Count";

    /// <summary>Boss ids that currently have a fight ready in the project.</summary>
    static readonly string[] ReadyBossIds = { BossIdMalice, BossIdKit, BossIdHarlie, BossIdHex, BossIdCount };

    public static string SelectedBossId { get; private set; } = BossIdMalice;

    /// <summary>True when the player picked a boss on Kaboodle's Boss Fight page.</summary>
    public static bool BossChosenFromKaboodle { get; private set; }

    /// <summary>
    /// When true, the next Boss Fight load uses Kit/Malice rival rules from the active player
    /// (e.g. Spawner Crystal after Level one) instead of <see cref="SelectedBossId"/>.
    /// </summary>
    public static bool ForcePlayerRivalBoss { get; private set; }

    public static void SetSelectedBoss(string bossId)
    {
        if (string.IsNullOrWhiteSpace(bossId))
            return;

        SelectedBossId = bossId.Trim();
        BossChosenFromKaboodle = true;
        ForcePlayerRivalBoss = false;
    }

    /// <summary>Spawner Crystal → Boss Fight: rival boss from player character, not Kaboodle pick.</summary>
    public static void PrepareBossFightFromLevelProgress()
    {
        ForcePlayerRivalBoss = true;
        BossChosenFromKaboodle = false;
    }

    public static void ClearBossFightRoutingFlags()
    {
        ForcePlayerRivalBoss = false;
    }

    public static bool IsBossReady(string bossId)
    {
        if (string.IsNullOrWhiteSpace(bossId))
            return false;

        for (int i = 0; i < ReadyBossIds.Length; i++)
        {
            if (string.Equals(ReadyBossIds[i], bossId.Trim(), StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static bool MatchesBoss(Boss boss, string selectedId)
    {
        if (boss == null || string.IsNullOrWhiteSpace(selectedId))
            return false;

        string id = selectedId.Trim();
        string bossId = boss.BossId ?? string.Empty;

        if (string.Equals(bossId, id, StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.Equals(bossId, "Boss" + id, StringComparison.OrdinalIgnoreCase))
            return true;

        if (boss is BossMalice && string.Equals(id, BossIdMalice, StringComparison.OrdinalIgnoreCase))
            return true;

        if (boss is BossKit && string.Equals(id, BossIdKit, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}
