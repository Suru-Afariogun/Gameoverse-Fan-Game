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
        bool count = IsCharacterSelected("Count");
        AttackStyleId style = PlayerAttackStyle.Selected;
        var parts = new List<string>(5);

        if (count)
            BuildCountParts(parts, style);
        else if (harlie)
            BuildHarlieParts(parts, style);
        else if (malice)
            BuildMaliceParts(parts, style);
        else
            BuildKitParts(parts, style);

        return parts;
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
            "If you use the full 16 seconds, Count enters a <b>3-second cooldown</b>: fire rate drops and charge / aura can only reach halfway (no Long Hand). Early cancels skip that weakness.\n\n" +
            $"Combat tip: open with Hyper Speed, then pick enemies apart with {b.FormatButton("Attack")} on the {b.DeviceDisplayName()}.\n\n" +
            $"To open your inventory, {b.FormatOpenInventory()}. To open the system pause menu, {b.FormatOpenSystemPause()}.");

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
            $"To jump, {b.FormatPress("Jump")}.\n\n" +
            $"To dash, {b.FormatPress("Dash")}. Harlie can air-dash up to three times before landing, and can dash-jump for extra mobility.");

        parts.Add(
            "Harlie fights with a melee combo.\n\n" +
            $"On the ground, {b.FormatPress("Attack")} to chain Kick → Slash 1 → Slash 2 → Slash 3.\n\n" +
            $"In the air, {b.FormatPress("Attack")} for an air slash.\n\n" +
            $"On the ground, {b.FormatHold("Attack")} up to 2 seconds, then release — half charge or less is a charge slash, longer is a charge kick. Travel distance scales with how long you held (max 24 spaces).");

        parts.Add(
            "Combat tips for Harlie:\n\n" +
            "• Kick lunges farther and faster than the slash steps.\n" +
            "• Keep tapping Attack within the combo window to continue the chain.\n" +
            $"• Use dash ({b.FormatButton("Dash")} on the {b.DeviceDisplayName()}) to reposition between swings.");

        parts.Add(BuildHarlieStylePart(style));
    }

    private static string BuildHarlieStylePart(AttackStyleId style)
    {
        ButtonSpriteManager b = Btn;
        string styleName = PlayerAttackStyle.DisplayName(style);
        switch (style)
        {
            case AttackStyleId.SpreadShot:
                return
                    $"Current attack style: <b>{styleName}</b>.\n\n" +
                    "Harlie moves and attacks twice as fast, but all her hits deal half damage.\n\n" +
                    "Buy or change styles at Kaboodle's Item Shop. Hover an equipped style and press Attack there to return to <b>Normal</b>.";
            case AttackStyleId.MachineGun:
                return
                    $"Current attack style: <b>{styleName}</b>.\n\n" +
                    "Harlie moves and attacks at half speed, but every hit — including charge kick — deals +3 extra damage.\n\n" +
                    "Buy or change styles at Kaboodle's Item Shop. Hover an equipped style and press Attack there to return to <b>Normal</b>.";
            default:
                return
                    $"Current attack style: <b>{styleName}</b>.\n\n" +
                    "This is Harlie's default balance — normal speed and damage.\n\n" +
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
            $"To dash, {b.FormatPress("Dash")}. Kit can air-dash up to twice before landing, and can dash-jump for extra mobility.");

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
                    $"Every shot — including charged shots — fires in a three-way spread. Press or hold {b.FormatButton("Attack")} on the {b.DeviceDisplayName()} as usual; Kit covers a wider area, which is great for groups and aiming help.\n\n" +
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
            $"To dash, {b.FormatPress("Dash")}. Malice's dash hits enemies on contact, and she becomes invincible during the second half of the dash.");

        parts.Add(
            "Malice's basic offense is a slash combo.\n\n" +
            $"To slash, {b.FormatPress("Attack")} repeatedly to chain Slash 1 → 2 → 3. Attacking quickly raises her attack speed for a short time.\n\n" +
            $"In the air, {b.FormatPress("Attack")} to air-slash. To dive during an air slash, {b.FormatAimDown()}.\n\n" +
            $"For a faster dash-cancel slash, {b.FormatPress("Attack")} during or right after a dash ({b.FormatButton("Dash")} on the {b.DeviceDisplayName()}).");

        parts.Add(
            "Malice can also charge Grapple Arm.\n\n" +
            $"To charge Grapple Arm, {b.FormatHold("Attack")}, then release to strike. A full charge hits harder.\n\n" +
            $"While grappling, {b.FormatAimUp()}, to pull Malice toward her attack box.\n\n" +
            $"On release, {b.FormatAimDown()}, to grab and pull foes into the attack box.\n\n" +
            $"To open your inventory, {b.FormatOpenInventory()}. To open the system pause menu, {b.FormatOpenSystemPause()}.");

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
