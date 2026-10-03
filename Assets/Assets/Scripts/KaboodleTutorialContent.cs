using System.Collections.Generic;

/// <summary>
/// Builds Kaboodle Tutorial page parts for the active character + attack style.
/// Button mentions are colored (Kit yellow / Malice purple) and match the active controller
/// via ButtonSpriteManager.
/// </summary>
public static class KaboodleTutorialContent
{
    public static List<string> BuildParts()
    {
        ButtonSpriteManager manager = ButtonSpriteManager.EnsureExists();
        manager?.SyncDeviceForPrompts();

        bool malice = PlayerAttackStyle.IsMaliceSelected();
        bool harlie = IsCharacterSelected("Harlie");
        bool hex = IsCharacterSelected("Hex");
        bool count = IsCharacterSelected("Count");
        AttackStyleId style = PlayerAttackStyle.Selected;
        var parts = new List<string>(7);

        if (count)
            BuildCountParts(parts, style);
        else if (hex)
            BuildHexParts(parts, style);
        else if (harlie)
            BuildHarlieParts(parts, style);
        else if (malice)
            BuildMaliceParts(parts, style);
        else
            BuildKitParts(parts, style);

        parts.Add(BuildItemsAndShopsPart());
        parts.Add(BuildExtraTipsPart());
        return parts;
    }

    /// <summary>Usable items, Scratch's upgrades and Blacksmith Cat's gear.</summary>
    private static string BuildItemsAndShopsPart()
    {
        ButtonSpriteManager b = Btn;
        return
            "Items and shops:\n\n" +
            $"• Usable items are found in stages. Touch one to carry it above your head, then {b.FormatPress("Use Item")} to use it. " +
            "Super Candy gives star power, Scouter maxes out an upgrade for a while, Bomb defeats every common enemy on screen, " +
            "Banana Phone calls the rocket to drop food, and Silver Swords rain down on enemies.\n\n" +
            "• You carry one item at a time. Blacksmith Cat's <b>Item Pouch</b> adds more slots: extra items follow behind you, and Use Item always uses the front one.\n\n" +
            "• Spend crystals on upgrades at <b>Scratch</b>'s shop and on gear at <b>Blacksmith Cat</b>'s forge. You can unequip anything in either shop.\n\n" +
            "• <b>Auto Use Gear</b> uses your front item for you when a hit would defeat you, and blocks that hit.";
    }

    /// <summary>Shared movement tips every character uses.</summary>
    private static string BuildExtraTipsPart()
    {
        ButtonSpriteManager b = Btn;
        return
            "Extra tips:\n\n" +
            $"• Slide down a wall and {b.FormatPress("Jump")} to kick off it. To climb instead, {b.FormatAimUp()} as you jump.\n\n" +
            $"• To drop through a thin platform, {b.FormatAimDown()} and {b.FormatPress("Jump")}.\n\n" +
            "• Kaboodle's <b>Item Shop</b> sells attack styles, and his <b>Boss Fight</b> page lets you pick a boss and fight it again.";
    }

    private static bool IsCharacterSelected(string characterId)
    {
        string id = PlayerSpawner.SelectedCharacterId;
        if (PlayerController.Active != null)
            id = PlayerController.Active.CharacterId;
        return string.Equals(id, characterId, System.StringComparison.OrdinalIgnoreCase);
    }

    private static ButtonSpriteManager Btn => ButtonSpriteManager.EnsureExists();

    private static void BuildCountParts(List<string> parts, AttackStyleId style)
    {
        ButtonSpriteManager b = Btn;

        parts.Add(
            "You are playing as <b>Count</b>.\n\n" +
            $"To move, {b.FormatMoveHorizontal()}.\n\n" +
            $"To jump, {b.FormatPress("Jump")}.\n\n" +
            $"To dash, {b.FormatPress("Dash")}. Count can air-dash up to twice before landing, and can dash-jump for extra mobility.");

        parts.Add(
            "Count fights with clock-hand buster shots.\n\n" +
            $"To fire a Short Hand shot, {b.FormatPress("Attack")}.\n\n" +
            $"To charge, {b.FormatHold("Attack")}, then release for a Long Hand shot. Once Count's red charge aura appears you can move while charging, and a full charge can drop a health clock when you hit enemies.\n\n" +
            $"To aim diagonally upward while shooting, {b.FormatAimUp()}.");

        parts.Add(
            "Count's special is <b>Hyper Speed</b> (Clock) — it does not use your charge.\n\n" +
            $"On the ground, hold {b.FormatAimUp()} or {b.FormatAimDown()} and hold {b.FormatButton("Dash")} on the {b.DeviceDisplayName()} briefly to toggle it on or off.\n\n" +
            "• Up + Dash: the world and Count both slow — your dashes, shots, and falls feel heavier and softer.\n" +
            "• Down + Dash: only the world slows; Count keeps full speed for movement and shots.\n\n" +
            "You can still dash while Hyper Speed is active. Cancel the same way (hold Up/Down + Dash).");

        parts.Add(
            "Hyper Speed lasts up to <b>16 seconds</b> from a shared pool. Turn it off early to save remaining time.\n\n" +
            "If you use the full 16 seconds, Count enters a <b>short cooldown</b>: fire rate drops and charge / aura can only reach halfway (no Long Hand). Early cancels skip that weakness.\n\n" +
            $"Combat tip: open with Hyper Speed, then pick enemies apart with {b.FormatButton("Attack")} on the {b.DeviceDisplayName()}.\n\n" +
            $"To open your inventory, {b.FormatOpenInventory()}. To open the system pause menu, {b.FormatOpenSystemPause()}.");

        parts.Add(
            "Count's Scratch upgrades:\n\n" +
            $"• <b>Ariel Action</b> level 2+: {b.FormatHold("Jump")} in the air to float down slowly, leaving afterimages until you land.\n\n" +
            "• <b>Hyper Ability</b> (full health): each level adds 1 second of Hyper Speed and 1 damage to every shot.\n\n" +
            $"• <b>Hyper Ability</b> level 2+: {b.FormatHold("Dash")} for 0.8 seconds to <b>Rewind</b> 2 seconds (plus 1 per level above 2). Count returns to where he was, gets back any health he lost, and the screen flashes red. Works at any health, then waits 8 seconds before it can be used again.\n\n" +
            "• <b>Attack Style</b> level 2+ (Spread Shot or Machine Gun): the last afterimage of each dash stays behind as a <b>Time Clone</b> (10 HP) that shoots the closest enemy with your style. Up to 3 clones, plus 1 per level above 2. A new clone replaces the oldest.\n\n" +
            "• <b>Attack Style</b> level 5: when Count would die, you become your newest Time Clone with 10 HP and keep fighting, but with no upgrades.");

        parts.Add(BuildCountStylePart(style));
    }

    private static string BuildCountStylePart(AttackStyleId style)
    {
        ButtonSpriteManager b = Btn;
        string styleName = PlayerAttackStyle.DisplayName(style);
        switch (style)
        {
            case AttackStyleId.SpreadShot:
                return
                    $"Current attack style: <b>{styleName}</b>.\n\n" +
                    $"Count's shots fan into a multi-way spread. Press or hold {b.FormatButton("Attack")} on the {b.DeviceDisplayName()} as usual to cover a wider area.\n\n" +
                    $"Buy or change styles at Kaboodle's Item Shop. Hover an equipped style and {b.FormatPress("Attack")} there to return to <b>Normal</b>.";

            case AttackStyleId.MachineGun:
                return
                    $"Current attack style: <b>{styleName}</b>.\n\n" +
                    $"Hold {b.FormatButton("Attack")} on the {b.DeviceDisplayName()} to spray rapid Short Hand shots. While you are not holding fire, Count auto-charges — tap {b.FormatButton("Attack")} briefly to release a Long Hand shot, then hold again to keep the stream going.\n\n" +
                    $"Buy or change styles at Kaboodle's Item Shop. Hover an equipped style and {b.FormatPress("Attack")} there to return to <b>Normal</b>.";

            default:
                return
                    $"Current attack style: <b>{styleName}</b>.\n\n" +
                    $"This is Count's default buster. {Capitalize(b.FormatPress("Attack"))} for Short Hand shots, or {b.FormatHold("Attack")} to charge Long Hand shots.\n\n" +
                    "Visit Kaboodle's Item Shop to equip <b>Spread Shot</b> or <b>Machine Gun</b> when you want a different play style.";
        }
    }

    private static void BuildHarlieParts(List<string> parts, AttackStyleId style)
    {
        ButtonSpriteManager b = Btn;

        parts.Add(
            "You are playing as <b>Harlie</b>.\n\n" +
            $"To move, {b.FormatMoveHorizontal()}.\n\n" +
            $"To jump, {b.FormatPress("Jump")}. In the air, press it again to double jump.\n\n" +
            $"To dash, {b.FormatPress("Dash")}. Harlie can't be hurt while dashing, can air-dash up to three times before landing, and can dash-jump for extra mobility.");

        parts.Add(BuildSwordComboPart("Harlie", "yellow"));
        parts.Add(BuildSwordChargePart("Harlie", tripleWaves: false));

        parts.Add(
            "<b>Silver Swords:</b> with the Hyper Ability upgrade at level 3 or higher, 8 silver swords float above Harlie while she is at full health. They vanish if she gets hurt.\n\n" +
            $"{Capitalize(b.FormatAimUp())} and {b.FormatPress("Attack")}: the swords rise, then shoot straight down through every enemy for 3 damage each. Each one sends a silver wave left and right along the ground.\n\n" +
            $"{Capitalize(b.FormatAimDown())} and {b.FormatPress("Attack")}: <b>needle storm</b>. The swords surround the closest enemy and fire in, rain down from the top of the screen, then fly across from both sides.\n\n" +
            "The swords fly back into place after each attack, and you can use them again as soon as they're back.");

        parts.Add(BuildSwordTipsPart("Harlie"));

        parts.Add(
            "Harlie's Scratch upgrades:\n\n" +
            "• <b>Ariel Action</b>: each level adds 1 air jump and 2 air dashes. At level 5, a successful pogo keeps her invincible until she lands.\n\n" +
            "• <b>Hyper Ability</b>: each level adds 2 max health. At full health she also gets +2 damage, +0.8 move speed, and bigger, longer slash waves. Level 3 unlocks the Silver Swords.\n\n" +
            "• <b>Attack Style</b>: Speed Style adds damage, farther and faster slash waves, and a longer, faster dash each level. Heavy Style adds bigger, stronger slash waves, 2 max health, faster falls and more knockback. Enemies knocked into each other are both defeated, and her dash shoves enemies.");

        parts.Add(BuildHarlieStylePart(style, "Harlie", orbitingVBots: false));
    }

    private static void BuildHexParts(List<string> parts, AttackStyleId style)
    {
        ButtonSpriteManager b = Btn;

        parts.Add(
            "You are playing as <b>Hex</b>.\n\n" +
            "Hex moves and fights like Harlie, but every hit deals 1.5× damage and her sword waves are red.\n\n" +
            $"To move, {b.FormatMoveHorizontal()}.\n\n" +
            $"To jump, {b.FormatPress("Jump")}. In the air, press it again to double jump.\n\n" +
            $"To dash, {b.FormatPress("Dash")}. Hex can't be hurt while dashing, can air-dash up to three times before landing, and can dash-jump for extra mobility.");

        parts.Add(BuildSwordComboPart("Hex", "red"));
        parts.Add(BuildSwordChargePart("Hex", tripleWaves: true));

        parts.Add(
            "Hex has two moves of her own.\n\n" +
            $"<b>Dive stab:</b> in the air, {b.FormatAimDown()} and {b.FormatPress("Attack")}. Hex dives straight down, cutting through everything in her path. When she lands, she slams the ground and fires five waves in an upward fan.\n\n" +
            $"<b>V-Bots:</b> {b.FormatAimUp()} and {b.FormatPress("Attack")} to summon two V-Bots beside her. They back up, then rocket forward for 2 damage each. Only one pair can be out at a time, with a short cooldown.");

        parts.Add(BuildSwordTipsPart("Hex"));

        parts.Add(
            "Hex's Scratch upgrades:\n\n" +
            "• <b>Ariel Action</b>: each level adds 1 air jump and 2 air dashes. At level 5, a successful pogo keeps her invincible until she lands.\n\n" +
            "• <b>Hyper Ability</b>: each level adds 2 max health, and at full health +2 damage and +0.8 move speed. V-Bots recharge 1 second faster, deal 1 more damage and fly 1 space farther per level, and home in at level 5. Dive stab waves get faster, stronger and longer.\n\n" +
            "• <b>Attack Style</b>: Speed Style and Heavy Style improve the same way as Harlie's. Level 3 adds 8 orbiting V-Bots (see the next page).");

        parts.Add(BuildHarlieStylePart(style, "Hex", orbitingVBots: true));
    }

    private static string BuildSwordComboPart(string name, string waveColor)
    {
        ButtonSpriteManager b = Btn;
        return
            $"{name} fights with a sword combo.\n\n" +
            $"On the ground, {b.FormatPress("Attack")} to chain Kick → Slash 1 → Slash 2 → Slash 3. Every slash also fires a short {waveColor} sword wave, and slashes can reflect enemy shots.\n\n" +
            $"In the air, {b.FormatPress("Attack")} for an air slash.\n\n" +
            $"While falling, {name}'s sword is out on its own: land on an enemy to pogo off it. A pogo refreshes her double jump and briefly protects her.";
    }

    private static string BuildSwordChargePart(string name, bool tripleWaves)
    {
        ButtonSpriteManager b = Btn;
        string waves = tripleWaves
            ? "\n\nThe charge slash and charged air slash each fire three waves: straight ahead, angled up and angled down."
            : "";
        return
            $"To charge, {b.FormatHold("Attack")}, then release. A full charge takes 3 seconds.\n\n" +
            $"On the ground, half charge or less is a <b>charge slash</b> that slides forward and fires a wave. More than half is a <b>charge kick</b>. The longer you held, the farther {name} slides.\n\n" +
            "In the air, releasing at half charge or more does a <b>charged air slash</b>: triple damage and a much longer wave." +
            waves;
    }

    private static string BuildSwordTipsPart(string name)
    {
        ButtonSpriteManager b = Btn;
        return
            $"Combat tips for {name}:\n\n" +
            "• Tapping Attack quickly speeds up her swings for a moment.\n" +
            $"• {Capitalize(b.FormatPress("Attack"))} during or right after a dash for a faster dash-cancel slash.\n" +
            "• Enemies touching only the part of her covered by a slash can't hurt her.\n" +
            "• Kick lunges farther and faster than the slash steps.\n\n" +
            $"To open your inventory, {b.FormatOpenInventory()}. To open the system pause menu, {b.FormatOpenSystemPause()}.";
    }

    private static string BuildHarlieStylePart(AttackStyleId style, string name, bool orbitingVBots)
    {
        ButtonSpriteManager b = Btn;
        string styleName = PlayerAttackStyle.DisplayName(style);
        string orbit = orbitingVBots
            ? $"\n\nWith the Attack Style upgrade at level 3 or higher, {b.FormatAimUp()} and {b.FormatPress("Attack")} to summon 8 V-Bots that circle Hex. Each slash or kick sends one flying ahead. Use them all before summoning more."
            : "";
        switch (style)
        {
            case AttackStyleId.SpreadShot:
                return
                    $"Current attack style: <b>{styleName}</b>.\n\n" +
                    $"{name} moves, dashes and attacks twice as fast." + orbit + "\n\n" +
                    $"Buy or change styles at Kaboodle's Item Shop. Hover an equipped style and {b.FormatPress("Attack")} there to return to <b>Normal</b>.";
            case AttackStyleId.MachineGun:
                return
                    $"Current attack style: <b>{styleName}</b>.\n\n" +
                    $"{name} moves and attacks at half speed, but every hit — including the charge kick — deals +3 extra damage." + orbit + "\n\n" +
                    $"Buy or change styles at Kaboodle's Item Shop. Hover an equipped style and {b.FormatPress("Attack")} there to return to <b>Normal</b>.";
            default:
                return
                    $"Current attack style: <b>{styleName}</b>.\n\n" +
                    $"This is {name}'s default balance — normal speed and damage.\n\n" +
                    "Visit Kaboodle's Item Shop to equip <b>Speed Style</b> or <b>Heavy Style</b> when you want a different play style.";
        }
    }
    private static void BuildKitParts(List<string> parts, AttackStyleId style)
    {
        ButtonSpriteManager b = Btn;

        parts.Add(
            "You are playing as <b>Kit</b>.\n\n" +
            $"To move, {b.FormatMoveHorizontal()}.\n\n" +
            $"To jump, {b.FormatPress("Jump")}.\n\n" +
            $"To dash, {b.FormatPress("Dash")}. Kit can air-dash up to twice before landing, and can dash-jump for extra mobility.\n\n" +
            $"In the air, {b.FormatPress("Jump")} again to double jump. Keep holding {b.FormatButton("Jump")} at the top (or press it again later) to hover. Dashing at max charge or while hovering is faster and goes farther.");

        parts.Add(
            "Kit fights with buster shots.\n\n" +
            $"To fire a small shot, {b.FormatPress("Attack")}.\n\n" +
            $"To charge, {b.FormatHold("Attack")}, then release for a medium or big shot depending on how long you held. While charging on the ground you stay still at first — once Kit's pink charge aura appears, you can move again, and longer charges make you faster.\n\n" +
            $"To aim diagonally upward while shooting, {b.FormatAimUp()}.");

        parts.Add(
            "Combat tips for Kit:\n\n" +
            $"To dodge through danger, {b.FormatPress("Dash")}, then keep shooting.\n\n" +
            "Charge shots deal more damage — medium and big shots are worth the wait against tougher foes.\n\n" +
            $"To open your inventory, {b.FormatOpenInventory()}. To open the system pause menu, {b.FormatOpenSystemPause()}.");

        parts.Add(
            "Kit's Scratch upgrades:\n\n" +
            "• <b>Ariel Action</b>: each level adds 2 seconds of hover and 1 air dash. At level 5, every hover starts with a free air jump.\n\n" +
            $"• <b>Hyper Ability</b> (full health only): each {b.FormatButton("Attack")} fires max charge shots back to back, and Kit stays at max charge. Level 1 fires 2 shots, plus 1 more per level.\n\n" +
            "• <b>Attack Style</b> (Spread Shot or Machine Gun): auto charge gets 0.5 seconds faster per level, and from level 2 Kit charges while shooting. Spread Shot also adds 1 bullet per shot each level.");

        parts.Add(BuildKitStylePart(style));
    }

    private static string BuildKitStylePart(AttackStyleId style)
    {
        ButtonSpriteManager b = Btn;
        string styleName = PlayerAttackStyle.DisplayName(style);
        switch (style)
        {
            case AttackStyleId.SpreadShot:
                return
                    $"Current attack style: <b>{styleName}</b>.\n\n" +
                    $"Every shot — including charged shots — fires in a three-way spread (more with Scratch's Attack Style upgrade). Press or hold {b.FormatButton("Attack")} on the {b.DeviceDisplayName()} as usual; Kit covers a wider area, which is great for groups and aiming help.\n\n" +
                    $"Buy or change styles at Kaboodle's Item Shop. Hover an equipped style and {b.FormatPress("Attack")} there to return to <b>Normal</b>.";

            case AttackStyleId.MachineGun:
                return
                    $"Current attack style: <b>{styleName}</b>.\n\n" +
                    $"Hold {b.FormatButton("Attack")} on the {b.DeviceDisplayName()} to spray rapid small shots. While you are not holding fire, Kit auto-charges in the background — tap {b.FormatButton("Attack")} on the {b.DeviceDisplayName()} briefly to release that charged shot, then hold again to keep the stream going.\n\n" +
                    $"Buy or change styles at Kaboodle's Item Shop. Hover an equipped style and {b.FormatPress("Attack")} there to return to <b>Normal</b>.";

            default:
                return
                    $"Current attack style: <b>{styleName}</b>.\n\n" +
                    $"This is Kit's default buster. {Capitalize(b.FormatPress("Attack"))} for small shots, or {b.FormatHold("Attack")} to charge medium and big shots.\n\n" +
                    "Visit Kaboodle's Item Shop to equip <b>Spread Shot</b> or <b>Machine Gun</b> when you want a different play style.";
        }
    }

    private static void BuildMaliceParts(List<string> parts, AttackStyleId style)
    {
        ButtonSpriteManager b = Btn;

        parts.Add(
            "You are playing as <b>Malice</b>.\n\n" +
            $"To move, {b.FormatMoveHorizontal()}.\n\n" +
            $"To jump, {b.FormatPress("Jump")}.\n\n" +
            $"To dash, {b.FormatPress("Dash")}. Malice's dash hits enemies on contact, and she becomes invincible during the second half of the dash.\n\n" +
            $"In the air, {b.FormatPress("Jump")} again to double jump.");

        parts.Add(
            "Malice's basic offense is a slash combo.\n\n" +
            $"To slash, {b.FormatPress("Attack")} repeatedly to chain Slash 1 → 2 → 3. Attacking quickly raises her attack speed for a short time. Her slashes also fire short projectile slashes, and a slash can reflect enemy shots.\n\n" +
            $"In the air, {b.FormatPress("Attack")} to air-slash. To dive during an air slash, {b.FormatAimDown()}. A dive that hits an enemy bounces her back up.\n\n" +
            $"For a faster dash-cancel slash, {b.FormatPress("Attack")} during or right after a dash ({b.FormatButton("Dash")} on the {b.DeviceDisplayName()}).");

        parts.Add(
            "Malice can also charge Grapple Arm.\n\n" +
            $"To charge Grapple Arm, {b.FormatHold("Attack")}, then release to strike. A full charge hits harder.\n\n" +
            $"While grappling, {b.FormatAimUp()}, to pull Malice toward her attack box.\n\n" +
            $"On release, {b.FormatAimDown()}, to grab and pull foes into the attack box.\n\n" +
            $"To open your inventory, {b.FormatOpenInventory()}. To open the system pause menu, {b.FormatOpenSystemPause()}.");

        parts.Add(
            "Malice's Scratch upgrades:\n\n" +
            "• <b>Ariel Action</b>: each level adds 1 air jump, 1 air dash, and a stronger dive bounce with longer invincibility.\n\n" +
            "• <b>Hyper Ability</b> (full health only): each level adds 2 attack power, sends slash projectiles 2 spaces farther, adds 1 more slash pair to Slash 1 and 2, and adds knockback.\n\n" +
            "• <b>Attack Style</b>: Life Steal steals 1 more health per level. From level 2 Malice heals 1 health every 2 seconds while standing still, and at level 5 she heals while moving too. Cruel Claw adds 1 speed and 2 attack power per level.");

        parts.Add(BuildMaliceStylePart(style));
    }

    private static string BuildMaliceStylePart(AttackStyleId style)
    {
        ButtonSpriteManager b = Btn;
        string styleName = PlayerAttackStyle.DisplayName(style);
        switch (style)
        {
            case AttackStyleId.SpreadShot: // Life Steal
                return
                    $"Current attack style: <b>{styleName}</b>.\n\n" +
                    "Successful slashes and Grapple Arm hits restore health over time (partial heals bank until a full heart).\n\n" +
                    $"Keep landing hits with {b.FormatButton("Attack")} on the {b.DeviceDisplayName()} to stay healthy in long fights.\n\n" +
                    $"Buy or change styles at Kaboodle's Item Shop. Hover an equipped style and {b.FormatPress("Attack")} there to return to <b>Normal</b>.";

            case AttackStyleId.MachineGun: // Cruel Claw
                return
                    $"Current attack style: <b>{styleName}</b>.\n\n" +
                    "Malice hits much harder with slashes, dash contact, and Grapple Arm, but she moves slower.\n\n" +
                    $"Play carefully: make space with {b.FormatButton("Dash")} on the {b.DeviceDisplayName()}, then punish with {b.FormatButton("Attack")} on the {b.DeviceDisplayName()}.\n\n" +
                    $"Buy or change styles at Kaboodle's Item Shop. Hover an equipped style and {b.FormatPress("Attack")} there to return to <b>Normal</b>.";

            default:
                return
                    $"Current attack style: <b>{styleName}</b>.\n\n" +
                    "This is Malice's default melee kit — slash combos, dash hits, and charged Grapple Arm with no extra style bonus.\n\n" +
                    "Visit Kaboodle's Item Shop to equip <b>Life Steal</b> or <b>Cruel Claw</b> when you want a different play style.";
        }
    }

    public static string AppendContinuePrompt(string body, bool isLastPart)
    {
        ButtonSpriteManager b = Btn;
        string prompt = isLastPart
            ? $"{Capitalize(b.FormatPress("Confirm"))} to finish"
            : $"{Capitalize(b.FormatPress("Confirm"))} to continue";

        return body + "\n\n<align=\"right\"><b>" + prompt + "</b></align>";
    }

    private static string Capitalize(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        return char.ToUpperInvariant(text[0]) + text.Substring(1);
    }
}
