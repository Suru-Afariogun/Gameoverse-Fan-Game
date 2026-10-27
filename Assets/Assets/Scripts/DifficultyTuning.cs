using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Live combat multipliers for <see cref="GameSettings.Difficulty"/>.
/// Easy / Normal / Hard are fixed. Balanced scales with the active player's upgrades + gear,
/// never weaker than Normal.
/// </summary>
public static class DifficultyTuning
{
    public static event Action OnApplied;
    private static readonly List<PlayerController> PlayerScratch = new List<PlayerController>(4);

    // ---------- Enemy (Easy / Hard vs Normal) ----------
    private const float EasyEnemyHpMul = 0.6f;
    private const float EasyEnemyDamageMul = 0.55f;
    private const float EasyEnemySpeedMul = 0.85f;
    private const float EasyEnemyDetectMul = 0.75f;

    private const float HardEnemyHpMul = 1.6f;
    private const float HardEnemyDamageMul = 1.5f;
    private const float HardEnemySpeedMul = 1.3f;
    private const float HardEnemyDetectMul = 1.4f;

    // ---------- Boss ----------
    private const float EasyBossHpMul = 0.7f;
    private const float EasyBossDamageMul = 0.55f;
    private const float EasyBossSpeedMul = 0.8f;
    private const float EasyBossTempoMul = 0.72f;
    private const float EasyBossRecoverMul = 1.85f;
    private const float EasyBossMoveAdd = -1.5f;
    private const float EasyRewindRushSpeedMul = 0.38f;
    private const float EasyRewindRushPauseMul = 2.6f;
    private const float EasyRewindRushPlaybackMul = 1.75f;
    // Midnight world scale: higher = less drastic player crawl (authored ~0.07).
    private const float EasyMidnightSlowScale = 0.20f;
    private const float NormalMidnightSlowScale = 0.13f;
    private const float HardMidnightSlowScaleMul = 1.22f; // tiny bit less drastic than authored
    private const float EasyMidnightMoveMul = 0.82f;
    private const float NormalMidnightMoveMul = 0.9f;
    private const float EasyMidnightBurstMul = 0.2f;
    private const float NormalMidnightBurstMul = 0.45f;
    private const float HardMidnightPostWaveHold = 0.55f;

    private const float HardBossHpMul = 2f;
    private const int HardBossHpFlatAdd = 150;
    private const float HardBossHpExtraMul = 1.3f;
    private const int HardBossDamageFlat = 2;
    private const float HardBossMoveAdd = 5f;
    private const float HardBossTempoMul = 1.45f;
    private const float HardBossRecoverMul = 0.32f;
    private const float HardBossEnrageRatio = 0.8f;
    private const float HardBossIFrames = 0.8f;
    private const float HardCrystalIFrames = 1f;
    private const int HardBossSignatureCountMul = 2;
    private const float HardBossApproachSmartMul = 1.35f;
    private const float HardBossEvasionMul = 1.4f;

    /// <summary>0 = Normal-tier, 1 = full Hard-tier. Used by Balanced.</summary>
    public static float BalancedPower01()
    {
        PlayerController player = FindPrimaryPlayer();
        if (player == null || player.IsDead)
            return 0f;

        float upgrades =
            (PlayerUpgrades.GetActiveLevel(player, UpgradeType.AerialAction) +
             PlayerUpgrades.GetActiveLevel(player, UpgradeType.HyperAbility) +
             PlayerUpgrades.GetActiveLevel(player, UpgradeType.AttackStyle)) /
            (3f * Mathf.Max(1, PlayerUpgrades.MaxLevel));

        float weapon = PlayerGear.WeaponDamageBonus(player) / (float)Mathf.Max(1, PlayerGear.MaxLevel);
        float life = PlayerGear.GetActiveLevel(player, GearType.Life) / (float)Mathf.Max(1, PlayerGear.MaxLevel);
        float armor = PlayerGear.GetActiveLevel(player, GearType.Armor) / (float)Mathf.Max(1, PlayerGear.MaxLevel);
        float booster = PlayerGear.GetActiveLevel(player, GearType.Booster) / (float)Mathf.Max(1, PlayerGear.MaxLevel);

        float gear = (weapon * 0.4f) + (life * 0.2f) + (armor * 0.2f) + (booster * 0.2f);
        return Mathf.Clamp01(upgrades * 0.55f + gear * 0.45f);
    }

    public static float EnemyHpMul() => ResolveEnemyMul(EasyEnemyHpMul, 1f, HardEnemyHpMul);
    public static float EnemyDamageMul() => ResolveEnemyMul(EasyEnemyDamageMul, 1f, HardEnemyDamageMul);
    public static float EnemySpeedMul() => ResolveEnemyMul(EasyEnemySpeedMul, 1f, HardEnemySpeedMul);
    public static float EnemyDetectMul() => ResolveEnemyMul(EasyEnemyDetectMul, 1f, HardEnemyDetectMul);

    public static float BossHpMul() => ResolveBossMul(EasyBossHpMul, 1f, HardBossHpMul);
    public static float BossDamageMul() => ResolveBossMul(EasyBossDamageMul, 1f, 1f);
    public static int BossDamageFlat() =>
        GameSettings.Difficulty switch
        {
            GameDifficulty.Hard => HardBossDamageFlat,
            GameDifficulty.Balanced => Mathf.RoundToInt(HardBossDamageFlat * BalancedPower01()),
            _ => 0
        };

    public static float BossMoveSpeedAdd() =>
        GameSettings.Difficulty switch
        {
            GameDifficulty.Easy => EasyBossMoveAdd,
            GameDifficulty.Hard => HardBossMoveAdd,
            GameDifficulty.Balanced => Mathf.Lerp(0f, HardBossMoveAdd, BalancedPower01()),
            _ => 0f
        };

    public static float BossMoveSpeedMul() => ResolveBossMul(EasyBossSpeedMul, 1f, 1f);
    public static float BossTempoMul() => ResolveBossMul(EasyBossTempoMul, 1f, HardBossTempoMul);
    public static float BossRecoverMul() => ResolveBossMul(EasyBossRecoverMul, 1f, HardBossRecoverMul);

    public static float BossEnrageThreshold(float authored)
    {
        return GameSettings.Difficulty switch
        {
            GameDifficulty.Hard => HardBossEnrageRatio,
            GameDifficulty.Balanced => Mathf.Lerp(authored, HardBossEnrageRatio, BalancedPower01()),
            GameDifficulty.Easy => Mathf.Min(authored, 0.35f),
            _ => authored
        };
    }

    public static float BossHitIFrames(float authored)
    {
        return GameSettings.Difficulty switch
        {
            GameDifficulty.Easy => 0f,
            GameDifficulty.Hard => HardBossIFrames,
            GameDifficulty.Balanced => Mathf.Lerp(0f, HardBossIFrames, BalancedPower01()),
            _ => authored
        };
    }

    /// <summary>Hard: crystal briefly ignores hits after taking damage.</summary>
    public static float CrystalHitIFrames()
    {
        return GameSettings.Difficulty switch
        {
            GameDifficulty.Hard => HardCrystalIFrames,
            GameDifficulty.Balanced => HardCrystalIFrames * BalancedPower01(),
            _ => 0f
        };
    }

    public static bool BossDashInvincible(bool authored)
    {
        if (GameSettings.Difficulty == GameDifficulty.Easy)
            return false;
        return authored;
    }

    public static int BossSignatureCountMul()
    {
        return GameSettings.Difficulty switch
        {
            GameDifficulty.Hard => HardBossSignatureCountMul,
            GameDifficulty.Balanced => BalancedPower01() >= 0.55f ? HardBossSignatureCountMul : 1,
            _ => 1
        };
    }

    /// <summary>Hard/Balanced: stretch approach / keep-away so bosses pick better attack lanes.</summary>
    public static float BossApproachSmartMul() =>
        GameSettings.Difficulty switch
        {
            GameDifficulty.Hard => HardBossApproachSmartMul,
            GameDifficulty.Balanced => Mathf.Lerp(1f, HardBossApproachSmartMul, BalancedPower01()),
            GameDifficulty.Easy => 0.9f,
            _ => 1f
        };

    public static float BossEvasionMul() =>
        GameSettings.Difficulty switch
        {
            GameDifficulty.Hard => HardBossEvasionMul,
            GameDifficulty.Balanced => Mathf.Lerp(1f, HardBossEvasionMul, BalancedPower01()),
            GameDifficulty.Easy => 0.85f,
            _ => 1f
        };

    public static int ScaleEnemyHp(int authored) =>
        Mathf.Max(1, Mathf.RoundToInt(authored * EnemyHpMul()));

    public static int ScaleEnemyDamage(int authored) =>
        Mathf.Max(0, Mathf.RoundToInt(authored * EnemyDamageMul()));

    public static float ScaleEnemySpeed(float authored) =>
        Mathf.Max(0.05f, authored * EnemySpeedMul());

    public static float ScaleEnemyDetect(float authored) =>
        Mathf.Max(0.5f, authored * EnemyDetectMul());

    public static int ScaleBossHp(int authored)
    {
        int baseScaled = Mathf.Max(1, Mathf.RoundToInt(authored * BossHpMul()));
        return GameSettings.Difficulty switch
        {
            // (current Hard pool + 150) then +30%.
            GameDifficulty.Hard => Mathf.Max(1, Mathf.RoundToInt((baseScaled + HardBossHpFlatAdd) * HardBossHpExtraMul)),
            GameDifficulty.Balanced => Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(
                authored,
                (Mathf.RoundToInt(authored * HardBossHpMul) + HardBossHpFlatAdd) * HardBossHpExtraMul,
                BalancedPower01()))),
            _ => baseScaled
        };
    }

    public static int ScaleBossDamage(int authored)
    {
        int dmg = Mathf.RoundToInt(authored * BossDamageMul());
        dmg += BossDamageFlat();
        return Mathf.Max(1, dmg);
    }

    public static float ScaleBossMoveSpeed(float authored) =>
        Mathf.Max(0.5f, authored * BossMoveSpeedMul() + BossMoveSpeedAdd());

    public static float ScaleBossTempo(float authored) =>
        Mathf.Max(0.2f, authored * BossTempoMul());

    public static float ScaleBossRecover(float authored) =>
        Mathf.Max(0.05f, authored * BossRecoverMul());

    public static int ScaleSignatureCount(int authored) =>
        Mathf.Max(1, authored * BossSignatureCountMul());

    /// <summary>Easy: Count Rewind Rush dashes crawl. Other difficulties unchanged.</summary>
    public static float BossRewindRushSpeedMul() =>
        GameSettings.Difficulty == GameDifficulty.Easy ? EasyRewindRushSpeedMul : 1f;

    public static float BossRewindRushPauseMul() =>
        GameSettings.Difficulty == GameDifficulty.Easy ? EasyRewindRushPauseMul : 1f;

    public static float BossRewindRushPlaybackMul() =>
        GameSettings.Difficulty == GameDifficulty.Easy ? EasyRewindRushPlaybackMul : 1f;

    /// <summary>
    /// Midnight HyperWorld scale. Higher = gentler slow.
    /// Easy/Normal are clearly milder; Hard is only a tiny nudge above authored.
    /// </summary>
    public static float BossMidnightWorldSlowScale(float authored)
    {
        authored = Mathf.Clamp(authored, 0.03f, 0.25f);
        return GameSettings.Difficulty switch
        {
            GameDifficulty.Easy => EasyMidnightSlowScale,
            GameDifficulty.Normal => NormalMidnightSlowScale,
            GameDifficulty.Hard => Mathf.Clamp(authored * HardMidnightSlowScaleMul, 0.03f, 0.12f),
            GameDifficulty.Balanced => Mathf.Lerp(
                NormalMidnightSlowScale,
                Mathf.Clamp(authored * HardMidnightSlowScaleMul, 0.03f, 0.12f),
                BalancedPower01()),
            _ => NormalMidnightSlowScale
        };
    }

    /// <summary>Count's move multiplier while he owns Midnight (1 = full authored pace).</summary>
    public static float BossMidnightMoveMul() =>
        GameSettings.Difficulty switch
        {
            GameDifficulty.Easy => EasyMidnightMoveMul,
            GameDifficulty.Normal => NormalMidnightMoveMul,
            GameDifficulty.Hard => 1f,
            GameDifficulty.Balanced => Mathf.Lerp(NormalMidnightMoveMul, 1f, BalancedPower01()),
            _ => NormalMidnightMoveMul
        };

    /// <summary>0–1 blend of authored Midnight speed-burst strength.</summary>
    public static float BossMidnightSpeedBurstBlend() =>
        GameSettings.Difficulty switch
        {
            GameDifficulty.Easy => EasyMidnightBurstMul,
            GameDifficulty.Normal => NormalMidnightBurstMul,
            GameDifficulty.Hard => 1f,
            GameDifficulty.Balanced => Mathf.Lerp(NormalMidnightBurstMul, 1f, BalancedPower01()),
            _ => NormalMidnightBurstMul
        };

    /// <summary>Extra real-time pause after each Hard Midnight wave so the player can evade.</summary>
    public static float BossMidnightPostWaveHold() =>
        GameSettings.Difficulty switch
        {
            GameDifficulty.Hard => HardMidnightPostWaveHold,
            GameDifficulty.Balanced => HardMidnightPostWaveHold * BalancedPower01(),
            _ => 0.15f
        };

    /// <summary>Preserve fill ratio when retuning max HP mid-fight. Dead stays dead.</summary>
    public static void RetuneHealth(ref int currentHealth, ref int maxHealth, int newMax, bool isDead)
    {
        newMax = Mathf.Max(1, newMax);
        if (isDead || currentHealth <= 0)
        {
            maxHealth = newMax;
            currentHealth = 0;
            return;
        }

        float ratio = maxHealth > 0 ? currentHealth / (float)maxHealth : 1f;
        maxHealth = newMax;
        currentHealth = Mathf.Clamp(Mathf.RoundToInt(newMax * ratio), 1, newMax);
    }

    public static void ApplyCircleDetection(CircleCollider2D col, float authoredRadius)
    {
        if (col == null || authoredRadius < 0.05f)
            return;
        col.radius = ScaleEnemyDetect(authoredRadius);
    }

    public static void ApplyBoxDetection(BoxCollider2D col, Vector2 authoredSize)
    {
        if (col == null || authoredSize.x < 0.05f || authoredSize.y < 0.05f)
            return;
        float mul = EnemyDetectMul();
        col.size = new Vector2(
            Mathf.Max(0.2f, authoredSize.x * mul),
            Mathf.Max(0.2f, authoredSize.y * mul));
    }

    public static void ApplyDetectionCollider(Collider2D col, float authoredCircleRadius, Vector2 authoredBoxSize)
    {
        if (col is CircleCollider2D circle)
            ApplyCircleDetection(circle, authoredCircleRadius);
        else if (col is BoxCollider2D box)
            ApplyBoxDetection(box, authoredBoxSize);
    }

    /// <summary>Harder = shorter intervals (more aggression).</summary>
    public static float ScaleEnemyInterval(float authoredInterval) =>
        Mathf.Max(0.04f, authoredInterval / Mathf.Max(0.25f, EnemySpeedMul()));

    /// <summary>Re-apply difficulty to every live enemy and boss (mid-battle Options change).</summary>
    public static void RefreshAllCombatants()
    {
        EnemyTypeCache.Invalidate();

        Boss[] bosses = EnemyTypeCache.Bosses;
        for (int i = 0; i < bosses.Length; i++)
        {
            if (bosses[i] != null)
                bosses[i].RefreshDifficulty();
        }

        CrankyClanky[] crankies = EnemyTypeCache.Crankies;
        for (int i = 0; i < crankies.Length; i++)
            crankies[i]?.RefreshDifficulty();

        LaserBot[] lasers = EnemyTypeCache.Lasers;
        for (int i = 0; i < lasers.Length; i++)
            lasers[i]?.RefreshDifficulty();

        BlockerBot[] blockers = EnemyTypeCache.Blockers;
        for (int i = 0; i < blockers.Length; i++)
            blockers[i]?.RefreshDifficulty();

        ChaoticTanker[] tankers = EnemyTypeCache.Tankers;
        for (int i = 0; i < tankers.Length; i++)
            tankers[i]?.RefreshDifficulty();

        ScrapNit[] scraps = EnemyTypeCache.Scraps;
        for (int i = 0; i < scraps.Length; i++)
            scraps[i]?.RefreshDifficulty();

        OnApplied?.Invoke();
    }

    private static float ResolveEnemyMul(float easy, float normal, float hard)
    {
        return GameSettings.Difficulty switch
        {
            GameDifficulty.Easy => easy,
            GameDifficulty.Hard => hard,
            GameDifficulty.Balanced => Mathf.Lerp(normal, hard, BalancedPower01()),
            _ => normal
        };
    }

    private static float ResolveBossMul(float easy, float normal, float hard)
    {
        return GameSettings.Difficulty switch
        {
            GameDifficulty.Easy => easy,
            GameDifficulty.Hard => hard,
            GameDifficulty.Balanced => Mathf.Lerp(normal, hard, BalancedPower01()),
            _ => normal
        };
    }

    private static PlayerController FindPrimaryPlayer()
    {
        PlayerController.CollectActivePlayers(PlayerScratch);
        for (int i = 0; i < PlayerScratch.Count; i++)
        {
            if (PlayerScratch[i] != null && !PlayerScratch[i].IsDead)
                return PlayerScratch[i];
        }

        return PlayerScratch.Count > 0 ? PlayerScratch[0] : null;
    }
}
