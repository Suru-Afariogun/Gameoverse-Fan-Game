using System;

/// <summary>
/// Built-in Bass dialogue for Town NPCs when an NPC has no Inspector Bass pages filled in.
/// Each speaker has a unique thread. Townsfolk nudge Bass that Dual / Double Gear and the rest of
/// his kit can serve the Town after Malice's army falls — not only the battlefield.
/// Kit and Scratch are girls; NPCs say "Town," never "Hometown," and never "street."
/// </summary>
public static class BassNpcDialogue
{
    public static string[] PagesFor(string speakerName)
    {
        string key = string.IsNullOrWhiteSpace(speakerName) ? "" : speakerName.Trim();

        if (Contains(key, "NPC Bass") || string.Equals(key, "Bass", StringComparison.OrdinalIgnoreCase))
        {
            return new[]
            {
                "Hmph. Another Bass. Don't think sharing a face means I'll share the win.",
                "Destroy Malice's army if that's your goal — then what? Strength without a next step is just noise.",
                "Prove you're more than a walking arsenal. I'll be watching."
            };
        }

        if (Contains(key, "Scratch"))
        {
            return new[]
            {
                "Bass — my board is open. Ariel Action, Hyper Ability, Attack Style: pick what fits your run.",
                "Those upgrades aren't only for the fight against Malice's army. They keep you sharp when the shooting stops.",
                "When the Dual gear and Double Gear are quiet, the Town still needs someone who can move. Don't forget that."
            };
        }

        if (Contains(key, "Black Smith") || Contains(key, "Blacksmith"))
        {
            return new[]
            {
                "Forge is lit, Bass. Life, Armor, Booster, Jump — same shelves as everyone else.",
                "Buy what helps you tear through Malice's army. Unequip what you don't need. Tags show the crystal cost.",
                "When that war's over, that same kit can clear rubble, reach high places, and watch over this town. Tools aren't only for enemies."
            };
        }

        if (Contains(key, "Kit's mom"))
        {
            return new[]
            {
                "Bass, dear. Kit says you're the rival who always has to be strongest.",
                "Finish what you came for against Malice's army — then come home. This town could use that power for something kinder.",
                "Tea's in the kitchen when you're ready to put the buster down for a minute."
            };
        }

        if (Contains(key, "Kit's dad"))
        {
            return new[]
            {
                "Bass. Solid stance — tournament days taught me to spot a real fighter.",
                "Beat Malice's army with that Dual arsenal if you must. Afterward, put those same moves to work protecting people, not just proving a point.",
                "And if Kit's late for dinner, drag her home. Rivalry doesn't excuse an empty chair."
            };
        }

        if (Contains(key, "Cat Kid Boy"))
        {
            return new[]
            {
                "Bass!! Show me the rocket dash later — the one with the cool trail!",
                "Mom says the army fight comes first… but after you win, can you use those moves for races and rescues too?",
                "Fountain finish line. Loser buys the winner a crystal soda!"
            };
        }

        if (Contains(key, "Cat Kid Girl"))
        {
            return new[]
            {
                "Your buster has the best sound in Town — don't tell Kit I said that.",
                "Those Dual outfits look amazing. When Malice's army is gone, you should show them off at the plaza festival!",
                "Helping people with flashy gear beats just blowing stuff up. Promise you'll try?"
            };
        }

        if (Contains(key, "Blonde Cat Woman"))
        {
            return new[]
            {
                "Bass. Kaboodle's Item Shop has Dual System and Double Gear when you need a style change.",
                "Use them to cut down Malice's army — then keep them handy. Patrols, escort jobs, rooftop shortcuts… that kit travels well.",
                "The plaza stays a no-blast zone. Everywhere else, you've earned the right to train."
            };
        }

        if (Contains(key, "Blonde Cat man") || (Contains(key, "Blonde") && Contains(key, "man")))
        {
            return new[]
            {
                "Didn't expect the world's loudest rival in our quiet plaza.",
                "Wipe out Malice's army first. After that, lend that Dual bag of tricks to the rebuild — we're short on hands who can fly across rooftops.",
                "Count's a good spar if you want a challenge that isn't a war. He respects a clean fight."
            };
        }

        if (Contains(key, "Old Man Cat"))
        {
            return new[]
            {
                "Bass. I've watched robots burn out chasing the title of strongest.",
                "Tear through Malice's army if that's the job. Then ask what that arsenal is for when nobody's shooting back.",
                "The Town remembers the ones who stayed to help — not only the ones who won."
            };
        }

        if (Contains(key, "Granny Cat"))
        {
            return new[]
            {
                "Come closer, Bass. Don't loom like you're about to charge the fountain.",
                "You're built for big fights — Malice's army included. When it's done, use those gadgets to lift, reach, and guard. Quiet work still needs a strong back.",
                "And wipe your feet before you track soot through my porch."
            };
        }

        if (Contains(key, "Purple hair"))
        {
            return new[]
            {
                "Those dash afterimages still look sharp — keep the show for open fields.",
                "Finish Malice's army. After that, that same Dual kit could scout cliffs, clear paths, and keep kids out of trouble.",
                "I'll be at the plaza. Wave if you're done proving a point and ready to help."
            };
        }

        // Generic fallback — unique from shopkeepers; still on the post-war purpose theme.
        return new[]
        {
            "Bass — the Town's glad you're here, even if you arrive louder than most.",
            "Smash Malice's army with every Dual trick you've got. When the dust settles, those same abilities can rebuild and protect.",
            "We're counting on more than a victory parade. Come back useful."
        };
    }

    private static bool Contains(string haystack, string needle) =>
        !string.IsNullOrEmpty(haystack) &&
        haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
}
