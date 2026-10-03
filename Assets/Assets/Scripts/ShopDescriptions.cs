using System;

/// <summary>
/// Names and per-character explanations shown in the shop info box (Scratch's upgrades and
/// Blacksmith Cat's gear). Button names are bold so players can skim for them.
/// </summary>
public static class ShopDescriptions
{
    public static string UpgradeName(UpgradeType type)
    {
        switch (type)
        {
            case UpgradeType.AerialAction: return "Ariel Action";
            case UpgradeType.HyperAbility: return "Hyper Ability";
            default: return "Attack Style";
        }
    }

    public static string GearName(GearType type)
    {
        switch (type)
        {
            case GearType.Life: return "Life Gear";
            case GearType.Armor: return "Armor Gear";
            case GearType.Booster: return "Booster Gear";
            case GearType.Jump: return "Jump Gear";
            case GearType.Weapon: return "Weapon Gear";
            case GearType.Repair: return "Repair Gear";
            case GearType.Magnet: return "Magnet Gear";
            case GearType.ItemPouch: return "Item Pouch";
            case GearType.AutoUse: return "Auto Use Gear";
            default: return "Item Extender";
        }
    }

    // ---------- Scratch ----------

    public static string Upgrade(string characterId, UpgradeType type)
    {
        if (Is(characterId, "Kit")) return Kit(type);
        if (Is(characterId, "Malice")) return Malice(type);
        if (Is(characterId, "Hex")) return Hex(type);
        if (Is(characterId, "Harlie")) return Harlie(type);
        if (Is(characterId, "Count")) return Count(type);
        return Generic(type);
    }

    private static string Kit(UpgradeType type)
    {
        switch (type)
        {
            case UpgradeType.AerialAction:
                return "Each level: +2 seconds of hover and +1 air dash.\n" +
                       "Level 5: every hover starts with a free air jump, so air jumps never run out.";
            case UpgradeType.HyperAbility:
                return "Full health only: every press fires max charge shots in a row, one space apart and 2.5x faster, " +
                       "and Kit stays at max charge. Level 1 fires 2 shots; each level adds 1 more.";
            default:
                return "Machine Gun: auto charge is 0.5s faster per level; level 2+ charges while shooting.\n" +
                       "Spread Shot: the same charge perks, plus 1 more bullet per shot each level.";
        }
    }

    private static string Malice(UpgradeType type)
    {
        switch (type)
        {
            case UpgradeType.AerialAction:
                return "Each level: +1 air jump, +1 air dash, and a stronger pogo with longer invincibility.";
            case UpgradeType.HyperAbility:
                return "Full health only. Each level: +2 attack power, slash projectiles travel 2 spaces farther, " +
                       "+1 slash pair on slashes 1 and 2, and +1 slash knockback.";
            default:
                return "Life Steal: +1 health stolen per level. Level 2+: heal 1 health every 2 seconds while standing still. " +
                       "Level 5: no need to stand still.\n" +
                       "Cruel Claw: +1 speed and +2 attack power per level.";
        }
    }

    private static string Harlie(UpgradeType type)
    {
        switch (type)
        {
            case UpgradeType.AerialAction:
                return "Each level: +1 air jump and +2 air dashes.\n" +
                       "Level 5: after a successful pogo you stay invincible until you land.";
            case UpgradeType.HyperAbility:
                return "Each level: +2 max health. At full health: +2 damage, +0.8 move speed, and bigger, longer slash projectiles.\n" +
                       "Level 3+ at full health: <b>Up</b> + <b>Attack</b> calls the Silver Swords, " +
                       "<b>Down</b> + <b>Attack</b> rains them down like needles.";
            default:
                return "Speed Style: +1 damage, farther and faster slash projectiles, and a longer, faster dash each level.\n" +
                       "Heavy Style: bigger, stronger slash projectiles, +2 max health, faster falls and more knockback each level. " +
                       "Enemies knocked into each other are both defeated, and your dash shoves enemies.";
        }
    }

    private static string Hex(UpgradeType type)
    {
        switch (type)
        {
            case UpgradeType.AerialAction:
                return Harlie(type);
            case UpgradeType.HyperAbility:
                return "Each level: +2 max health; at full health +2 damage and +0.8 move speed.\n" +
                       "V-Bots recharge 1s faster, deal +1 damage and fly 1 space farther per level (level 5: they home in). " +
                       "Dive stab waves get faster, stronger and longer, and grow as they travel.";
            default:
                return "Speed and Heavy Style perks work like Harlie's.\n" +
                       "Level 3+: <b>Up</b> + <b>Attack</b> summons 8 V-Bots that orbit you; each slash or kick sends one flying ahead. " +
                       "Use them all before summoning more.";
        }
    }

    private static string Count(UpgradeType type)
    {
        switch (type)
        {
            case UpgradeType.AerialAction:
                return "Each level: +1 air jump and +1 air dash.\n" +
                       "Level 2+: hold <b>Jump</b> in the air to float down slowly.";
            case UpgradeType.HyperAbility:
                return "Full health: +1 second of Hyper Speed and +1 shot damage per level.\n" +
                       "Level 2+ (any health): hold <b>Dash</b> without <b>Up</b>/<b>Down</b> to Rewind your position and health " +
                       "(8s cooldown). Higher levels rewind further.";
            default:
                return "Spread Shot or Machine Gun only. Level 2+: dashing leaves a Time Clone that shoots the nearest enemy " +
                       "(3 clones, +1 per level).\n" +
                       "Level 5: instead of falling, you take over your newest clone.";
        }
    }

    private static string Generic(UpgradeType type)
    {
        switch (type)
        {
            case UpgradeType.AerialAction:
                return "Each level: +1 air jump and +1 air dash.";
            case UpgradeType.HyperAbility:
                return "Full health only: use your max charge attack back to back.";
            default:
                return "Boosts your chosen attack style.";
        }
    }

    // ---------- Blacksmith Cat ----------

    public static string Gear(string characterId, GearType type)
    {
        bool harlieLike = Is(characterId, "Harlie") || Is(characterId, "Hex");
        switch (type)
        {
            case GearType.Life:
                return "+1 max health per level (+5 at level 5). Stacks with Scratch's upgrades.";
            case GearType.Armor:
                return harlieLike
                    ? "Each level: +0.15s of invincibility after a hit.\n" +
                      "Level 3+: hits deal 1 less damage (never below 1). She already shrugs off knockback and stun."
                    : "Each level: +0.15s of invincibility after a hit and 15% less knockback.\n" +
                      "Level 3+: hits deal 1 less damage (never below 1). Level 5: hits no longer stun you.";
            case GearType.Booster:
                return "Each level: faster running and a 10% shorter dash cooldown.\n" +
                       "Level 5: +1 air dash.";
            case GearType.Jump:
                return "Each level: your jump is 4% stronger, so you jump higher (about 44% higher at level 5).";
            case GearType.Weapon:
                return WeaponGear(characterId);
            case GearType.Repair:
                return "After 3 seconds without getting hit, recover 1 health every few seconds: " +
                       "every 6s at level 1, down to every 2s at level 5.";
            case GearType.Magnet:
                return "Pulls crystals and pickups to you from 3 spaces away at level 1, up to 7 at level 5 " +
                       "(usable items stay put).\nLevel 5: every crystal counts double.";
            case GearType.ItemPouch:
                return "Carry 1 more usable item per level (6 at level 5). Extra items trail behind you in a line; " +
                       "<b>Use Item</b> uses the front one and the rest move up. When every slot is full, a new item swaps out the front one.";
            case GearType.AutoUse:
                return "When a hit would defeat you, your front item is used automatically and the hit is blocked. " +
                       "Recharges in 20s at level 1, then 15s, 12s, 9s, and 6s at level 5.";
            default:
                return "Super Candy and Scouter last 20% longer per level (twice as long at level 5).\n" +
                       "Bomb, Banana Phone and Silver Swords get +1 use per level before they're gone (6 uses at level 5).";
        }
    }

    private static string WeaponGear(string characterId)
    {
        const string damage = "+1 damage at levels 1-2, +2 at 3-4, +3 at 5";
        if (Is(characterId, "Kit") || Is(characterId, "Count"))
        {
            string shots = Is(characterId, "Count") ? "Short and Long Hand shots (Time Clones too)" : "Buster shots";
            return shots + ": " + damage + ", and 10% faster, farther shots each level.\n" +
                   "Level 5: a shot that defeats a common enemy keeps flying.";
        }

        string attacks = Is(characterId, "Malice") ? "Slashes and grapples" :
                         Is(characterId, "Hex") ? "Kicks, slashes and dive stabs" : "Kicks and slashes";
        return attacks + ": " + damage + ". Slash projectiles fly 10% farther each level.\n" +
               "Level 5: slash projectiles are 25% bigger.";
    }

    private static bool Is(string characterId, string expected)
    {
        string id = string.IsNullOrWhiteSpace(characterId) ? "Kit" : characterId.Trim();
        return string.Equals(id, expected, StringComparison.OrdinalIgnoreCase);
    }
}
