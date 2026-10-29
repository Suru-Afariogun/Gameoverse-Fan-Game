using System;

/// <summary>
/// Kaboodle lines before his menu popup.
/// Kit: talks to Kit, then the player.
/// Malice: brief annoyance, then the player.
/// Everyone else: "who are you?", then the player (hub).
/// Keep pages short so text fits the DialogueBox.
/// </summary>
public static class KaboodleGreetingDialogue
{
    private static int kitRotate;
    private static int maliceRotate;
    private static int strangerRotate;
    private static int playerHubRotate;

    public static DialogueBox.Line[] LinesFor(string characterId)
    {
        string id = string.IsNullOrWhiteSpace(characterId) ? "Kit" : characterId.Trim();

        if (Eq(id, "Kit"))
            return PickPair(KitPairs(), ref kitRotate);

        if (Eq(id, "Malice"))
            return PickPair(MalicePairs(), ref maliceRotate);

        return StrangerThenPlayer();
    }

    public static bool IsHero(string characterId)
    {
        string id = characterId ?? string.Empty;
        return Eq(id, "Kit") || Eq(id, "Harlie") || Eq(id, "Count");
    }

    private static string[][] KitPairs() => new[]
    {
        new[]
        {
            "Hey, Kit. I know what saving costs us. We already lost everyone once — but I've still got your back. Always.",
            "Alright, player — I'm the hub for now. Switch characters, pick a boss, change styles, grab heals, or open the Tutorial from here."
        },
        new[]
        {
            "You don't have to pretend you're fine, Kit. If winning means losing them again… we face that together.",
            "Player — need a rematch, a different fighter, supplies, a style swap, or a how-to? Talk to me. Menu's up."
        },
        new[]
        {
            "The Town doesn't know what we know, Kit. Let them keep that. You and me keep fighting for whoever's still stuck here.",
            "Player — Character Select, Boss Fight, Item Shop, Tutorial. Whatever you need before the next fight, it's through me."
        },
        new[]
        {
            "Chin up, partner. Purple rocks and broken worlds… messy. You're still the Kit I believe in.",
            "Player — swap who you're piloting, challenge a boss, restock, change attack style, or check controls. Hub's open."
        }
    };

    private static string[][] MalicePairs() => new[]
    {
        new[]
        {
            "…Malice. Great.",
            "Player — ignore her attitude. Hub's open: Character Select, Boss Fight, Item Shop, Tutorial."
        },
        new[]
        {
            "Ugh. You.",
            "Player — switch characters, pick a boss, change styles, grab heals, or hit Tutorial. I'm still on Kit's side."
        },
        new[]
        {
            "Don't start.",
            "Player — rematch, style swap, supplies, how-to — talk to me. Menu's waiting."
        },
        new[]
        {
            "Claws off the counter.",
            "Player — fighters, bosses, styles, heals, tutorials. Hub first. Her later."
        }
    };

    private static DialogueBox.Line[] StrangerThenPlayer()
    {
        string[] who =
        {
            "Who the hell are you?",
            "…And you are?",
            "Okay — who are you supposed to be?",
            "Never seen you before. Who the hell are you?"
        };

        string[] whatever =
        {
            "Whatever. Not my problem.",
            "Eh. Fine. Don't care.",
            "Cool story. Moving on.",
            "Sure. Anyway—"
        };

        string[] hub =
        {
            "Player — I'm the hub for now. Switch characters, pick a boss, change styles, grab heals, or open the Tutorial from here.",
            "Player — Character Select, Boss Fight, Item Shop, Tutorial. Whatever you need, it's through me.",
            "Player — rematch, different fighter, supplies, style swap, or a how-to? Menu's up.",
            "Player — swap who you're piloting, challenge a boss, restock, change attack style, or check controls. Hub's open."
        };

        int i = strangerRotate % who.Length;
        int j = playerHubRotate % hub.Length;
        strangerRotate++;
        playerHubRotate++;

        return new[]
        {
            Line(who[i]),
            Line(whatever[i % whatever.Length]),
            Line(hub[j])
        };
    }

    private static DialogueBox.Line[] PickPair(string[][] pairs, ref int rotate)
    {
        if (pairs == null || pairs.Length == 0)
            return new[] { Line("…Hub's open. Menu's waiting.") };

        string[] pair = pairs[rotate % pairs.Length];
        rotate++;

        if (pair == null || pair.Length == 0)
            return new[] { Line("…Hub's open. Menu's waiting.") };

        DialogueBox.Line[] lines = new DialogueBox.Line[pair.Length];
        for (int i = 0; i < pair.Length; i++)
            lines[i] = Line(pair[i]);
        return lines;
    }

    private static DialogueBox.Line Line(string text) =>
        new DialogueBox.Line("Kaboodle", text, isCopySpeaker: false);

    private static bool Eq(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
