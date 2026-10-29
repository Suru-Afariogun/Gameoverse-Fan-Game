using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Unique intro cutscene banter for every playable character versus every boss.
/// Same-character mirrors reuse <see cref="CopyBotIntroDialogue"/> instead.
/// Lore: a failed Gameoverse harness caused a mega-glitch; fighters fell into a mash of worlds
/// (Kit/Kaboodle's as the base). Purple crystals may open a way home — at another world's cost.
/// Heroes (Kit, Harlie, Count): somber, reluctant, prove the greater hero.
/// Villains (Malice, Hex, Bass, Deadline): selfish home agendas; willing to kill each other.
/// Speakers never mention the player-only wash/tint effects.
/// </summary>
public static class BossIntroDialogue
{
    public static IEnumerator PlayForActivePlayer(Boss preferredBoss = null)
    {
        PlayerController player = PlayerController.ResolveActive();
        if (player == null && PlayerSpawner.Instance != null)
            player = PlayerSpawner.Instance.CurrentPlayer;

        string playerId = player != null ? player.CharacterId : PlayerSpawner.SelectedCharacterId;
        Boss boss = preferredBoss != null && !preferredBoss.IsDead
            ? preferredBoss
            : FindNearestLivingBoss(player);

        if (boss == null)
            yield break;

        string bossId = ResolveBossCharacterId(boss);
        if (string.IsNullOrWhiteSpace(playerId) || string.IsNullOrWhiteSpace(bossId))
            yield break;

        // Mirror fights keep the copy-bot script.
        if (string.Equals(Normalize(playerId), Normalize(bossId), StringComparison.OrdinalIgnoreCase))
        {
            yield return CopyBotIntroDialogue.PlayForCharacter(playerId, player, boss);
            yield break;
        }

        DialogueBox.Line[] lines = LinesFor(playerId, bossId);
        if (lines == null || lines.Length == 0)
            yield break;

        yield return CopyBotIntroDialogue.ApproachNearestBossForDialogue(player, boss);

        DialogueBox box = CopyBotIntroDialogue.EnsureDialogueBox();
        if (box == null)
        {
            Debug.LogWarning("[BossIntroDialogue] No DialogueBox available — skipping banter.");
            yield break;
        }

        bool done = false;
        box.OpenConversation(lines, player, () => done = true);
        while (!done)
            yield return null;
    }

    public static DialogueBox.Line[] LinesFor(string playerId, string bossId)
    {
        string p = Normalize(playerId);
        string b = Normalize(bossId);
        if (string.IsNullOrEmpty(p) || string.IsNullOrEmpty(b))
            return null;
        if (string.Equals(p, b, StringComparison.OrdinalIgnoreCase))
            return null;

        if (Eq(p, "Kit")) return KitVs(b);
        if (Eq(p, "Malice")) return MaliceVs(b);
        if (Eq(p, "Harlie")) return HarlieVs(b);
        if (Eq(p, "Hex")) return HexVs(b);
        if (Eq(p, "Count")) return CountVs(b);
        if (Eq(p, "Bass")) return BassVs(b);
        if (Eq(p, "Deadline")) return DeadlineVs(b);
        return null;
    }

    public static string ResolveBossCharacterId(Boss boss)
    {
        if (boss == null)
            return string.Empty;

        if (boss is BossDeadline)
            return BossEncounter.BossIdDeadline;
        if (boss is BossCount)
            return BossEncounter.BossIdCount;
        if (boss is BossKit)
            return BossEncounter.BossIdKit;
        if (boss is BossMalice)
            return BossEncounter.BossIdMalice;
        if (boss is BossBass)
            return BossEncounter.BossIdBass;
        if (boss is BossHarlie)
        {
            string harlieId = Normalize(boss.BossId);
            if (harlieId.IndexOf("Hex", StringComparison.OrdinalIgnoreCase) >= 0)
                return BossEncounter.BossIdHex;
            return BossEncounter.BossIdHarlie;
        }

        string id = Normalize(boss.BossId);
        if (id.StartsWith("Boss", StringComparison.OrdinalIgnoreCase) && id.Length > 4)
            id = id.Substring(4);
        return id;
    }

    private static Boss FindNearestLivingBoss(PlayerController player)
    {
        Boss[] bosses = UnityEngine.Object.FindObjectsByType<Boss>(FindObjectsSortMode.None);
        Boss best = null;
        float bestSqr = float.MaxValue;
        Vector3 from = player != null ? player.transform.position : Vector3.zero;

        for (int i = 0; i < bosses.Length; i++)
        {
            Boss boss = bosses[i];
            if (boss == null || boss.IsDead || boss.IsCopyBotDecoy)
                continue;

            float sqr = (boss.transform.position - from).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = boss;
            }
        }

        return best;
    }

    private static string Normalize(string id) =>
        string.IsNullOrWhiteSpace(id) ? string.Empty : id.Trim();

    private static bool Eq(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static DialogueBox.Line P(string name, string text) =>
        new DialogueBox.Line(name, text, isCopySpeaker: false);

    private static DialogueBox.Line B(string name, string text) =>
        new DialogueBox.Line(name, text, isCopySpeaker: false);

    // -------------------------------------------------------------------------
    // Kit
    // -------------------------------------------------------------------------

    private static DialogueBox.Line[] KitVs(string bossId)
    {
        if (Eq(bossId, "Malice"))
        {
            // Same-world rivals: Kit beating Malice ended their world. Malice wants revenge + crystal power for herself.
            // Keep pages short so text fits the DialogueBox.
            return new[]
            {
                B("Malice", "Here, kitty, kitty… I was wondering when you'd crawl out of that little dream of yours."),
                P("Kit", "Malice. I'm not playing. Not today — not after what happened last time we finished a fight."),
                B("Malice", "Last time? You mean when you \"won\" and our whole world went up in smoke?"),
                B("Malice", "I've been chewing on that ever since."),
                B("Malice", "Those crystals are mine now — so I never lose again, and so I can build something that finally caters to me."),
                B("Malice", "And you? You're getting crushed for good."),
                P("Kit", "Kaboodle and I already paid for that win. Everyone did."),
                P("Kit", "I'm not letting you turn a second chance into another massacre just to settle a grudge."),
                B("Malice", "A grudge? Darling, this is overdue. The rest of this glitch can wait until you're scrap. Fight me."),
                P("Kit", "…Fine. Come on, then. If revenge is all you've got left, I'll put you down myself — with Kaboodle watching my back, like always.")
            };
        }

        if (Eq(bossId, "Harlie"))
        {
            return new[]
            {
                B("Harlie", "Kit… I wish the crystals hadn't made us opponents. Truly."),
                P("Kit", "Me too. I don't want to hurt you. But if one of us going home means another world pays for it… I can't pretend that doesn't matter."),
                B("Harlie", "Then prove it, love. Prove you have the strength to save all our worlds — prove you are the greater hero between us."),
                P("Kit", "…Kaboodle and I already lost everyone once. If saving means losing them again, it still has to be me who carries that. I'm sorry, Harlie.")
            };
        }

        if (Eq(bossId, "Hex"))
        {
            return new[]
            {
                B("Hex", "Look at you. A washed-up kitty still trying to play hero — after failing so hard your whole world went quiet."),
                P("Kit", "Hex— I'm not here for your insults. Stand down."),
                B("Hex", "Stand down? Please. Give up already. Let me take those crystals home to KillCam. She's the one who builds our androids — and once she has that power, she'll finally enforce true justice on our world."),
                P("Kit", "\"True justice\" paid for with someone else's home? That's not justice. That's theft with a nicer name."),
                B("Hex", "Call it whatever you want. Your death would be a worthy sacrifice for a better future — KillCam's future. Attack first. Talk never."),
                P("Kit", "Then attack. I won't be your sacrifice — and I won't let KillCam rewrite the world on everyone else's bones.")
            };
        }

        if (Eq(bossId, "Count"))
        {
            return new[]
            {
                B("Count", "Kit… I hoped we wouldn't have to do this."),
                P("Kit", "So did I. I don't want to fight you, Count. But someone has to be strong enough for everyone stuck here."),
                B("Count", "I can slow time itself… and I was still too slow to save my world. That failure doesn't leave. This glitch — these crystals — it's a second chance I can't afford to lose. Not again."),
                P("Kit", "I get it. Kaboodle and I already lost everyone once. If I win, I might lose them again… and I still have to try — for yours, for Harlie's, for whoever's left."),
                B("Count", "I respect you. More than you know. But respect won't bring either of our homes back. So prove it — prove you have the strength to save all our worlds. Prove you are the greater hero out of the two of us."),
                P("Kit", "…Okay. Then we do this honestly. No holding back — for both our sakes.")
            };
        }

        if (Eq(bossId, "Bass"))
        {
            return new[]
            {
                B("Bass", "Heh. Hand 'em over, cat. I'm collecting those purple crystals for Dr. Wily's plan — and yours are next. Or else."),
                P("Kit", "Or else? Bass, I can't trust you with these. You work for a guy who threatens the existence of your own world all the time."),
                B("Bass", "Watch your mouth—"),
                P("Kit", "If I lose to you, both our worlds end up in more danger — his schemes on yours, and everyone else's hanging on these rocks. I want to save everyone. I'm not handing that to Wily."),
                B("Bass", "Tch. You sound just like that blue idiot Mega Man. Always lecturing. Always \"saving everyone.\""),
                P("Kit", "Then maybe he's right about something."),
                B("Bass", "Shut it. If I can't fight him right now, at least I can blow off some steam destroying you.")
            };
        }

        if (Eq(bossId, "Deadline"))
        {
            return new[]
            {
                B("Deadline", "Kit. Step aside. I need those crystals, a way home, and Count — and I won't waste either of us on a speech."),
                P("Kit", "Everyone here knows what your Acceleration does. Minutes, and someone can be gone for good. I'm not in the mood for your mission list."),
                B("Deadline", "Then don't listen to it. I only cooperate long enough to leave this place, beat Count fair and square, and prove my creator was right to build me. That is the whole of it."),
                P("Kit", "Get out of my way, Deadline — before I give you a taste of your own medicine. And I'll make it quick.")
            };
        }

        return null;
    }

    // -------------------------------------------------------------------------
    // Malice
    // -------------------------------------------------------------------------

    private static DialogueBox.Line[] MaliceVs(string bossId)
    {
        if (Eq(bossId, "Kit"))
        {
            // Mirror of Kit-vs-Malice: revenge + crystals for Malice's own power/desires.
            // Keep pages short so text fits the DialogueBox.
            return new[]
            {
                P("Malice", "Here, kitty, kitty… I was wondering when you'd crawl out of that little dream of yours."),
                B("Kit", "Malice. I'm not playing. Not today — not after what happened last time we finished a fight."),
                P("Malice", "Last time? You mean when you \"won\" and our whole world went up in smoke?"),
                P("Malice", "I've been chewing on that ever since."),
                P("Malice", "Those crystals are mine now — so I never lose again, and so I can build something that finally caters to me."),
                P("Malice", "And you? You're getting crushed for good."),
                B("Kit", "Kaboodle and I already paid for that win. Everyone did."),
                B("Kit", "I'm not letting you turn a second chance into another massacre just to settle a grudge."),
                P("Malice", "A grudge? Darling, this is overdue. The rest of this glitch can wait until you're scrap. Fight me."),
                B("Kit", "…Fine. Come on, then. If revenge is all you've got left, I'll put you down myself — with Kaboodle watching my back, like always.")
            };
        }

        if (Eq(bossId, "Harlie"))
        {
            return new[]
            {
                P("Malice", "Careful, stitch-girl. Shredding through fragile material is a cat's specialty — so keep yourself and those pretty clothes away from my claws, if you know what's good for you."),
                B("Harlie", "Oh, darling… all you need to tame a cat is a nice ball of yarn. And as a weaver, I've got plenty of thread for you to bat around. Go on — play."),
                P("Malice", "…I'm going to cut you down to threads."),
                B("Harlie", "Heehee. The stage is no place for a house pet. Though if you'd like, I can file those claws down so you make a proper one~"),
                P("Malice", "You're dead, mannequin."),
                B("Harlie", "Bring it on, putty-cat~")
            };
        }

        if (Eq(bossId, "Hex"))
        {
            return new[]
            {
                P("Malice", "Look at you. A selfish brat playing soldier of justice. Leave the act to real villains — like me. At least I'm honest about what I want."),
                B("Hex", "You—! When I get those crystals, I'll make sure your world crashes and burns before I ever go back to KillCam."),
                P("Malice", "World stuff? Don't care. Power's all I'm after. And if that KillCam-brat wants my crystals so badly, she should've sent a real killer — not some doll swinging a toy knife."),
                B("Hex", "Then come test whether this blade is a toy or not."),
                P("Malice", "Sure. But I must warn you — I play rough.")
            };
        }

        if (Eq(bossId, "Count"))
        {
            return new[]
            {
                P("Malice", "Time powers? That's basically cheating… and you still ended up stuck in this mess? What a joke. You're barely even worth the kill."),
                B("Count", "I'm not in the mood, Malice. Hand over those crystals — or I take them by force. I know what kind of person you are. I won't regret putting down a bad kitty."),
                P("Malice", "Aww… is the baby mouse still squeaking? Fine. I'll bite."),
                P("Malice", "Tick-tock — you're dead on the spot. This kitty's coming for you!")
            };
        }

        if (Eq(bossId, "Bass"))
        {
            return new[]
            {
                P("Malice", "You're just some bratty bot who can't even get rid of a makeshift fighting robot. Go home and leave the villain gig to the pros."),
                B("Bass", "Says the one who got beat by a kitty who drinks apple juice like a kindergartner. You go home — and leave the battle-junkie gig to the pros."),
                P("Malice", "…Okay. That's it. We settle this right now — and see who's really full of themselves."),
                B("Bass", "Heh. I already know the winner. But it'll make good pastime.")
            };
        }

        if (Eq(bossId, "Deadline"))
        {
            return new[]
            {
                P("Malice", "Ugh. An obsessive brat who needs a hobby. Go find one that isn't \"stare at Count until something breaks.\""),
                B("Deadline", "Mission parameters unchanged: retrieve the crystals, return home, defeat Count fairly, prove my creator correct."),
                B("Deadline", "Feline entity detected. Intent: battle. Conclusion: all obstacles and those who interfere with the mission must be eradicated."),
                P("Malice", "Hue hue hue… there can only be one troublemaker."),
                B("Deadline", "Negative. After I deal with you, all problems and troubles will have been terminated.")
            };
        }

        return null;
    }

    // -------------------------------------------------------------------------
    // Harlie
    // -------------------------------------------------------------------------

    private static DialogueBox.Line[] HarlieVs(string bossId)
    {
        if (Eq(bossId, "Kit"))
        {
            return new[]
            {
                B("Kit", "Harlie… I hate that it has to be us. The crystals, the cost — all of it."),
                P("Harlie", "I know, love. Neither of us wanted a stage like this."),
                B("Kit", "If one world goes home on another's loss… someone has to be strong enough for everyone."),
                P("Harlie", "Then prove it. Prove you have the strength to save all our worlds — prove you are the greater hero between us. I'll meet you honestly.")
            };
        }

        if (Eq(bossId, "Malice"))
        {
            // Mirror of Malice-vs-Harlie yarn/claws banter.
            return new[]
            {
                B("Malice", "Careful, stitch-girl. Shredding through fragile material is a cat's specialty — so keep yourself and those pretty clothes away from my claws, if you know what's good for you."),
                P("Harlie", "Oh, darling… all you need to tame a cat is a nice ball of yarn. And as a weaver, I've got plenty of thread for you to bat around. Go on — play."),
                B("Malice", "…I'm going to cut you down to threads."),
                P("Harlie", "Heehee. The stage is no place for a house pet. Though if you'd like, I can file those claws down so you make a proper one~"),
                B("Malice", "You're dead, mannequin."),
                P("Harlie", "Bring it on, putty-cat~")
            };
        }

        if (Eq(bossId, "Hex"))
        {
            return new[]
            {
                P("Harlie", "They say if you can't beat them, join them… though in your case, darling, that seems to mean stealing someone's likeness."),
                B("Hex", "I'm not joining anything. I'm here to finally eliminate the washed-up, outdated android in front of me."),
                P("Harlie", "Experience comes with age, Hex. Simply copying my moves doesn't mean you understand them."),
                B("Hex", "I don't need to understand! Once you're gone, there'll only be one performer left on this stage — me!"),
                B("Hex", "I'll be the one setting the status quo once KillCam builds her new world!"),
                P("Harlie", "Ha… even if you've copied every scrap of my data — even if you somehow win — you'll have nothing left to copy."),
                P("Harlie", "You'll be left for the dump and replaced by another H-Model… just like you're trying to do to me."),
                B("Hex", "You know nothing! You're a mannequin that should've been recycled years ago! I'll take you down with your own moves!"),
                P("Harlie", "You may know all of mine… which means I know all of yours. So it won't help you. Strange, isn't it?"),
                B("Hex", "THERE CAN ONLY BE ONE H-MODEL — ME!"),
                P("Harlie", "FINE — TAKE CENTER STAGE AND LET'S SETTLE THIS RIGHT HERE, RIGHT NOW, YOU SCRAPPY COPY!")
            };
        }

        if (Eq(bossId, "Count"))
        {
            return new[]
            {
                B("Count", "Harlie. It's an honor. I've always admired your elegance — I only wish I'd gotten to see you perform under kinder circumstances."),
                P("Harlie", "That's very kind of you, Count. Then I'll do my best to put on a show… even here, even like this."),
                B("Count", "I'd appreciate that. Don't hold back. Show me whether I can trust you to be the hero strong enough to save everyone."),
                P("Harlie", "…Very well. Cleanly, then. No theatrics we don't mean. Let's see whose tempo holds when home is the prize.")
            };
        }

        if (Eq(bossId, "Bass"))
        {
            return new[]
            {
                P("Harlie", "You know, Bass… I'm impressed. You're not even a performer android like me, and somehow you're still this dramatic."),
                B("Bass", "I'll show you dramatic—!"),
                P("Harlie", "Then take center stage, darling. I'll be the judge of your first show."),
                B("Bass", "Tch. Wily and Mega Man can wait — I'll destroy you first!"),
                P("Harlie", "En garde, darling~")
            };
        }

        if (Eq(bossId, "Deadline"))
        {
            return new[]
            {
                B("Deadline", "Harlie. Your theatrics burn seconds. I need crystals, Count, and a creator's approval."),
                P("Harlie", "Spare me the mission briefing, Deadline. I'm really not in the mood."),
                B("Deadline", "Mood is irrelevant. Budge, or I accelerate the ending."),
                P("Harlie", "Then rewrite yourself mid-scene, love — or I'll cut that monologue short. Even a villain with one cue card can be stopped.")
            };
        }

        return null;
    }

    // -------------------------------------------------------------------------
    // Hex
    // -------------------------------------------------------------------------

    private static DialogueBox.Line[] HexVs(string bossId)
    {
        if (Eq(bossId, "Kit"))
        {
            // Mirror tone of Kit-vs-Hex: washed-up hero, KillCam justice, worthy sacrifice.
            return new[]
            {
                P("Hex", "Look at you. A washed-up kitty still trying to play hero — after failing so hard your whole world went quiet."),
                B("Kit", "Hex— I'm not here for your insults. Stand down."),
                P("Hex", "Stand down? Please. Give up already."),
                P("Hex", "Let me take those crystals home to KillCam. She builds our androids — and with that power, she'll enforce true justice."),
                B("Kit", "\"True justice\" paid for with someone else's home? That's not justice. That's theft with a nicer name."),
                P("Hex", "Call it whatever you want. Your death would be a worthy sacrifice for KillCam's future."),
                P("Hex", "Attack first. Talk never."),
                B("Kit", "Then attack. I won't be your sacrifice — and I won't let KillCam rewrite the world on everyone else's bones.")
            };
        }

        if (Eq(bossId, "Malice"))
        {
            // Mirror of Malice-vs-Hex: fake justice vs honest villain / toy knife / play rough.
            return new[]
            {
                B("Malice", "Look at you. A selfish brat playing soldier of justice. Leave the act to real villains — like me."),
                B("Malice", "At least I'm honest about what I want."),
                P("Hex", "You—! When I get those crystals, I'll make sure your world crashes and burns before I ever go back to KillCam."),
                B("Malice", "World stuff? Don't care. Power's all I'm after."),
                B("Malice", "And if that KillCam-brat wants my crystals so badly, she should've sent a real killer — not some doll swinging a toy knife."),
                P("Hex", "Then come test whether this blade is a toy or not."),
                B("Malice", "Sure. But I must warn you — I play rough.")
            };
        }

        if (Eq(bossId, "Harlie"))
        {
            // Mirror of Harlie-vs-Hex H-Model spat.
            return new[]
            {
                B("Harlie", "They say if you can't beat them, join them… though in your case, darling, that seems to mean stealing someone's likeness."),
                P("Hex", "I'm not joining anything. I'm here to finally eliminate the washed-up, outdated android in front of me."),
                B("Harlie", "Experience comes with age, Hex. Simply copying my moves doesn't mean you understand them."),
                P("Hex", "I don't need to understand! Once you're gone, there'll only be one performer left on this stage — me!"),
                P("Hex", "I'll be the one setting the status quo once KillCam builds her new world!"),
                B("Harlie", "Ha… even if you've copied every scrap of my data — even if you somehow win — you'll have nothing left to copy."),
                B("Harlie", "You'll be left for the dump and replaced by another H-Model… just like you're trying to do to me."),
                P("Hex", "You know nothing! You're a mannequin that should've been recycled years ago!"),
                P("Hex", "I'll take you down with your own moves!"),
                B("Harlie", "You may know all of mine… which means I know all of yours. So it won't help you. Strange, isn't it?"),
                P("Hex", "THERE CAN ONLY BE ONE H-MODEL — ME!"),
                B("Harlie", "FINE — TAKE CENTER STAGE AND LET'S SETTLE THIS RIGHT HERE, RIGHT NOW, YOU SCRAPPY COPY!")
            };
        }

        if (Eq(bossId, "Count"))
        {
            // Mirror of Count-vs-Hex: impatient clock hero vs KillCam justice.
            return new[]
            {
                B("Count", "I'm in no mood to banter with another villain, Hex. Hand over those crystals before you get hurt."),
                P("Hex", "Aww… cute. For a guy who's all about slowing things down, you're quite impatient."),
                P("Hex", "Just enjoy yourself while I slowly cut you down… piece by piece."),
                B("Count", "Quit yapping and bring it on already. Stop wasting precious time."),
                P("Hex", "It's about time someone brought fake heroes to justice…"),
                P("Hex", "And I'll start with you — for KillCam!")
            };
        }

        if (Eq(bossId, "Bass"))
        {
            return new[]
            {
                P("Hex", "KillCam's forces destroy and fight for absolute justice."),
                P("Hex", "Self-absorbed robots who terrorize others for fun — like you — are exactly the fiends I was created to destroy."),
                B("Bass", "Wrong. I don't terrorize the weak. There's no fun in that."),
                B("Bass", "I only go after the strong — to prove I'm the strongest."),
                B("Bass", "Robots like you and Mega Man who go on and on about justice annoy me the most…"),
                B("Bass", "But you're both strong. So at least you're fun to fight."),
                P("Hex", "If you're so battle-hungry, we settle this in a language you understand."),
                P("Hex", "Loser gives up their crystals."),
                B("Bass", "Heh. Finally — you're talking my language!"),
                B("Bass", "HERE I COME!")
            };
        }

        if (Eq(bossId, "Deadline"))
        {
            return new[]
            {
                B("Deadline", "Mission objective: destroy Count."),
                B("Deadline", "Priority directive: on hold. Must eliminate current obstacle."),
                P("Hex", "You speak like a basic android… for someone with the abilities and specs of an elite."),
                P("Hex", "Basically, you're just someone's overclocked toy. How intriguing."),
                P("Hex", "If not for that one-track mind, you would've made a good recruit for KillCam's forces."),
                B("Deadline", "I have only one master — my creator. And one main purpose — destroying Count."),
                B("Deadline", "Those who interfere with the mission will meet very quick ends."),
                P("Hex", "I don't care what you can do. All I need is a second to cut you down."),
                P("Hex", "So bring it on, you overpriced watch."),
                B("Deadline", "Sub-directive: eliminate the current disturbance."),
                B("Deadline", "Engaging battle mode. Initiating combat systems."),
                B("Deadline", "Reply imminent: it does not matter if you can slay opponents in a second…"),
                B("Deadline", "…because the very second you depend on is within my control."),
                B("Deadline", "AS LONG AS TIME IS MY WEAPON — YOU STAND NO CHANCE!")
            };
        }

        return null;
    }

    // -------------------------------------------------------------------------
    // Count
    // -------------------------------------------------------------------------

    private static DialogueBox.Line[] CountVs(string bossId)
    {
        if (Eq(bossId, "Kit"))
        {
            return new[]
            {
                B("Kit", "Count… I don't want this. Not with you. Not with what the crystals cost."),
                P("Count", "Nor I. The glitch already stole enough. Still — someone must be strong enough for every world stuck here."),
                B("Kit", "Kaboodle and I know what my world already lost. If I win, I might lose them again. I'll still try."),
                P("Count", "Then prove you have the strength to save all our worlds. Prove you are the greater hero out of the two of us. I will not hold back.")
            };
        }

        if (Eq(bossId, "Malice"))
        {
            return new[]
            {
                P("Count", "I'm tired of dealing with selfish brats, Malice. I'm in no mood for a villain's antics."),
                B("Malice", "Hue hue hue… for a guy who can slow down time, you always seem so impatient."),
                B("Malice", "If you're dead, you'll never have to wait for anything ever again~"),
                P("Count", "Even your threats are childish. Stop wasting my time. Hand over those crystals — or I take them by force."),
                B("Malice", "You're one to talk. Your threats are cuter than Kit's."),
                B("Malice", "If you truly want my crystals, you'll have to pry them out of my claws…"),
                B("Malice", "But be warned — no one who tussles with me leaves unscathed.")
            };
        }

        if (Eq(bossId, "Harlie"))
        {
            // Mirror of Harlie-vs-Count.
            return new[]
            {
                P("Count", "Harlie. It's an honor. I've always admired your elegance — I only wish I'd gotten to see you perform under kinder circumstances."),
                B("Harlie", "That's very kind of you, Count. Then I'll do my best to put on a show… even here, even like this."),
                P("Count", "I'd appreciate that. Don't hold back. Show me whether I can trust you to be the hero strong enough to save everyone."),
                B("Harlie", "…Very well. Cleanly, then. No theatrics we don't mean. Let's see whose tempo holds when home is the prize.")
            };
        }

        if (Eq(bossId, "Hex"))
        {
            return new[]
            {
                P("Count", "I'm in no mood to banter with another villain, Hex. Hand over those crystals before you get hurt."),
                B("Hex", "Aww… cute. For a guy who's all about slowing things down, you're quite impatient."),
                B("Hex", "Just enjoy yourself while I slowly cut you down… piece by piece."),
                P("Count", "Quit yapping and bring it on already. Stop wasting precious time."),
                B("Hex", "It's about time someone brought fake heroes to justice…"),
                B("Hex", "And I'll start with you — for KillCam!")
            };
        }

        if (Eq(bossId, "Bass"))
        {
            return new[]
            {
                P("Count", "I thought Deadline was the only battle junkie around."),
                P("Count", "You two should just fight each other instead of wasting my time."),
                B("Bass", "Heh. This Deadline guy must have taste — you act a lot like that defect Proto Man."),
                B("Bass", "Anyone who hates guys like that is worth my time…"),
                B("Bass", "But in the end, I'll always be the strongest. No room for an alliance at the top."),
                B("Bass", "So I'll just destroy Deadline after I destroy you."),
                P("Count", "Get out of my way — or hand over your crystals. I've wasted enough time on this useless banter."),
                B("Bass", "I need those crystals for Dr. Wily's plan. So you hand yours over."),
                P("Count", "This has gone on long enough. Let's settle it the way you know best."),
                B("Bass", "Finally — you're speaking my language!"),
                B("Bass", "Doesn't matter if you can mess with time… because I'M THE STRONGEST!")
            };
        }

        if (Eq(bossId, "Deadline"))
        {
            return new[]
            {
                P("Count", "Your existence is proof that evil is unoriginal, Deadline — it just corrupts any good thing that comes around."),
                B("Deadline", "Mission objective: destroy Count."),
                P("Count", "It doesn't matter if some copycat has my abilities, my looks, and my data."),
                P("Count", "Whatever you know about me, I already know about myself. So that data won't help you. Strange, isn't it?"),
                B("Deadline", "No matter. The mission will be achieved. An advantage is not necessary."),
                B("Deadline", "Your abilities only delay the inevitable. I was created to skip right to it."),
                B("Deadline", "I am the force you can't outrun, can't slow down, can't stop."),
                B("Deadline", "I am your DEADLINE."),
                P("Count", "You're not even a pastime. Get out of my way so I can focus on the crystals — and saving our world."),
                B("Deadline", "Negative. Destroying Count: first objective. Acquiring crystals: second."),
                B("Deadline", "Saving world: third. Reporting to creator: fourth."),
                P("Count", "Impressive. A nuisance like you manages to stay organized… you even have a schedule."),
                B("Deadline", "Enough. Conversation duration has exceeded reasonable character count."),
                B("Deadline", "Engaging battle mode. Initiating combat systems."),
                B("Deadline", "PRESENT, PAST, OR FUTURE — AS LONG AS WE ARE THE MANIPULATORS OF TIME, YOU ARE MINE!"),
                P("Count", "No matter how many times you get defeated, you always come back."),
                P("Count", "A future without you in it would be great… so I agree. We settle this now."),
                P("Count", "Save our world — then go back to it, and leave you trapped in this mess!")
            };
        }

        return null;
    }

    // -------------------------------------------------------------------------
    // Bass
    // -------------------------------------------------------------------------

    private static DialogueBox.Line[] BassVs(string bossId)
    {
        if (Eq(bossId, "Kit"))
        {
            // Mirror of Kit-vs-Bass: Wily crystals / Mega Man lecture / blow off steam.
            return new[]
            {
                P("Bass", "Heh. Hand 'em over, cat. I'm collecting those purple crystals for Dr. Wily's plan — and yours are next. Or else."),
                B("Kit", "Or else? Bass, I can't trust you with these. You work for a guy who threatens the existence of your own world all the time."),
                P("Bass", "Watch your mouth—"),
                B("Kit", "If I lose to you, both our worlds end up in more danger — his schemes on yours, and everyone else's hanging on these rocks."),
                B("Kit", "I want to save everyone. I'm not handing that to Wily."),
                P("Bass", "Tch. You sound just like that blue idiot Mega Man. Always lecturing. Always \"saving everyone.\""),
                B("Kit", "Then maybe he's right about something."),
                P("Bass", "Shut it. If I can't fight him right now, at least I can blow off some steam destroying you.")
            };
        }

        if (Eq(bossId, "Malice"))
        {
            // Mirror of Malice-vs-Bass trash talk.
            return new[]
            {
                B("Malice", "You're just some bratty bot who can't even get rid of a makeshift fighting robot."),
                B("Malice", "Go home and leave the villain gig to the pros."),
                P("Bass", "Says the one who got beat by a kitty who drinks apple juice like a kindergartner."),
                P("Bass", "You go home — and leave the battle-junkie gig to the pros."),
                B("Malice", "…Okay. That's it. We settle this right now — and see who's really full of themselves."),
                P("Bass", "Heh. I already know the winner. But it'll make good pastime.")
            };
        }

        if (Eq(bossId, "Harlie"))
        {
            // Mirror of Harlie-vs-Bass dramatic / en garde banter.
            return new[]
            {
                B("Harlie", "You know, Bass… I'm impressed. You're not even a performer android like me, and somehow you're still this dramatic."),
                P("Bass", "I'll show you dramatic—!"),
                B("Harlie", "Then take center stage, darling. I'll be the judge of your first show."),
                P("Bass", "Tch. Wily and Mega Man can wait — I'll destroy you first!"),
                B("Harlie", "En garde, darling~")
            };
        }

        if (Eq(bossId, "Hex"))
        {
            // Mirror of Hex-vs-Bass: justice fiends vs strongest / crystals bet.
            return new[]
            {
                B("Hex", "KillCam's forces destroy and fight for absolute justice."),
                B("Hex", "Self-absorbed robots who terrorize others for fun — like you — are exactly the fiends I was created to destroy."),
                P("Bass", "Wrong. I don't terrorize the weak. There's no fun in that."),
                P("Bass", "I only go after the strong — to prove I'm the strongest."),
                P("Bass", "Robots like you and Mega Man who go on and on about justice annoy me the most…"),
                P("Bass", "But you're both strong. So at least you're fun to fight."),
                B("Hex", "If you're so battle-hungry, we settle this in a language you understand."),
                B("Hex", "Loser gives up their crystals."),
                P("Bass", "Heh. Finally — you're talking my language!"),
                P("Bass", "HERE I COME!")
            };
        }

        if (Eq(bossId, "Count"))
        {
            // Mirror of Count-vs-Bass: Proto Man / Wily / I'M THE STRONGEST.
            return new[]
            {
                B("Count", "I thought Deadline was the only battle junkie around."),
                B("Count", "You two should just fight each other instead of wasting my time."),
                P("Bass", "Heh. This Deadline guy must have taste — you act a lot like that defect Proto Man."),
                P("Bass", "Anyone who hates guys like that is worth my time…"),
                P("Bass", "But in the end, I'll always be the strongest. No room for an alliance at the top."),
                P("Bass", "So I'll just destroy Deadline after I destroy you."),
                B("Count", "Get out of my way — or hand over your crystals. I've wasted enough time on this useless banter."),
                P("Bass", "I need those crystals for Dr. Wily's plan. So you hand yours over."),
                B("Count", "This has gone on long enough. Let's settle it the way you know best."),
                P("Bass", "Finally — you're speaking my language!"),
                P("Bass", "Doesn't matter if you can mess with time… because I'M THE STRONGEST!")
            };
        }

        if (Eq(bossId, "Deadline"))
        {
            // Annoyed understanding: Bass gets the Count/Mega Man parallel, hates being overlooked again.
            return new[]
            {
                B("Deadline", "Mission objective: destroy Count."),
                B("Deadline", "Priority directive: on hold. Must eliminate current obstacle."),
                P("Bass", "Tch. Of course. Another one looking straight through me…"),
                P("Bass", "I know that look. Same rivalry mess I've got with Mega Man — except yours is Count."),
                B("Deadline", "Correct. One master: my creator. One purpose: Count. Interference will be eradicated."),
                P("Bass", "Yeah, yeah. Complicated. I get it. Doesn't mean I like being treated like a speed bump."),
                B("Deadline", "Assessment irrelevant. You are not the target. You are in the way."),
                P("Bass", "That's what they always say. Fine. If you think you're stronger — prove it."),
                B("Deadline", "Engaging battle mode. Initiating combat systems."),
                B("Deadline", "PRESENT, PAST, OR FUTURE — AS LONG AS TIME IS MY WEAPON, YOU STAND NO CHANCE!"),
                P("Bass", "Heh… we'll see about that. HERE I COME!")
            };
        }

        return null;
    }

    // -------------------------------------------------------------------------
    // Deadline — ~70% Metal Sonic / ~20% Enker / ~10% Surge
    // -------------------------------------------------------------------------

    private static DialogueBox.Line[] DeadlineVs(string bossId)
    {
        if (Eq(bossId, "Kit"))
        {
            // Mirror of Kit-vs-Deadline, Deadline leads in mission-speak.
            return new[]
            {
                P("Deadline", "Step aside. Crystals required. Path home required. Count required."),
                P("Deadline", "Speech duration: unnecessary."),
                B("Kit", "Everyone here knows what your Acceleration does. Minutes, and someone can be gone for good."),
                B("Kit", "I'm not in the mood for your mission list."),
                P("Deadline", "Mood is irrelevant."),
                P("Deadline", "I cooperate only long enough to leave this place, beat Count fair and square, and prove my creator was right to build me."),
                P("Deadline", "That is the whole of it."),
                B("Kit", "Get out of my way, Deadline — before I give you a taste of your own medicine. And I'll make it quick.")
            };
        }

        if (Eq(bossId, "Malice"))
        {
            // Mirror of Malice-vs-Deadline.
            return new[]
            {
                B("Malice", "Ugh. An obsessive brat who needs a hobby. Go find one that isn't \"stare at Count until something breaks.\""),
                P("Deadline", "Mission parameters unchanged: retrieve the crystals, return home, defeat Count fairly, prove my creator correct."),
                P("Deadline", "Feline entity detected. Intent: battle."),
                P("Deadline", "Conclusion: all obstacles and those who interfere with the mission must be eradicated."),
                B("Malice", "Hue hue hue… there can only be one troublemaker."),
                P("Deadline", "Negative. After I deal with you, all problems and troubles will have been terminated.")
            };
        }

        if (Eq(bossId, "Harlie"))
        {
            // Mirror of Harlie-vs-Deadline.
            return new[]
            {
                P("Deadline", "Theatrics burn seconds. Crystals required. Count required. Creator approval required."),
                B("Harlie", "Spare me the mission briefing, Deadline. I'm really not in the mood."),
                P("Deadline", "Mood is irrelevant. Budge, or I accelerate the ending."),
                B("Harlie", "Then rewrite yourself mid-scene, love — or I'll cut that monologue short. Even a villain with one cue card can be stopped."),
                P("Deadline", "Engaging battle mode. Initiating combat systems.")
            };
        }

        if (Eq(bossId, "Hex"))
        {
            // Mirror of Hex-vs-Deadline.
            return new[]
            {
                P("Deadline", "Mission objective: destroy Count."),
                P("Deadline", "Priority directive: on hold. Must eliminate current obstacle."),
                B("Hex", "You speak like a basic android… for someone with the abilities and specs of an elite."),
                B("Hex", "Basically, you're just someone's overclocked toy. How intriguing."),
                B("Hex", "If not for that one-track mind, you would've made a good recruit for KillCam's forces."),
                P("Deadline", "I have only one master — my creator. And one main purpose — destroying Count."),
                P("Deadline", "Those who interfere with the mission will meet very quick ends."),
                B("Hex", "I don't care what you can do. All I need is a second to cut you down."),
                B("Hex", "So bring it on, you overpriced watch."),
                P("Deadline", "Sub-directive: eliminate the current disturbance."),
                P("Deadline", "Engaging battle mode. Initiating combat systems."),
                P("Deadline", "Reply imminent: it does not matter if you can slay opponents in a second…"),
                P("Deadline", "…because the very second you depend on is within my control."),
                P("Deadline", "AS LONG AS TIME IS MY WEAPON — YOU STAND NO CHANCE!")
            };
        }

        if (Eq(bossId, "Count"))
        {
            // Mirror of Count-vs-Deadline — Deadline leads as the aggressor on mission.
            return new[]
            {
                B("Count", "Your existence is proof that evil is unoriginal, Deadline — it just corrupts any good thing that comes around."),
                P("Deadline", "Mission objective: destroy Count."),
                B("Count", "It doesn't matter if some copycat has my abilities, my looks, and my data."),
                B("Count", "Whatever you know about me, I already know about myself. So that data won't help you. Strange, isn't it?"),
                P("Deadline", "No matter. The mission will be achieved. An advantage is not necessary."),
                P("Deadline", "Your abilities only delay the inevitable. I was created to skip right to it."),
                P("Deadline", "I am the force you can't outrun, can't slow down, can't stop."),
                P("Deadline", "I am your DEADLINE."),
                B("Count", "You're not even a pastime. Get out of my way so I can focus on the crystals — and saving our world."),
                P("Deadline", "Negative. Destroying Count: first objective. Acquiring crystals: second."),
                P("Deadline", "Saving world: third. Reporting to creator: fourth."),
                B("Count", "Impressive. A nuisance like you manages to stay organized… you even have a schedule."),
                P("Deadline", "Enough. Conversation duration has exceeded reasonable character count."),
                P("Deadline", "Engaging battle mode. Initiating combat systems."),
                P("Deadline", "PRESENT, PAST, OR FUTURE — AS LONG AS WE ARE THE MANIPULATORS OF TIME, YOU ARE MINE!"),
                B("Count", "No matter how many times you get defeated, you always come back."),
                B("Count", "A future without you in it would be great… so I agree. We settle this now."),
                B("Count", "Save our world — then go back to it, and leave you trapped in this mess!")
            };
        }

        if (Eq(bossId, "Bass"))
        {
            // Mirror of Bass-vs-Deadline: Deadline cold/mission; Bass annoyed but understanding.
            return new[]
            {
                P("Deadline", "Mission objective: destroy Count."),
                P("Deadline", "Priority directive: on hold. Must eliminate current obstacle."),
                B("Bass", "Tch. Of course. Another one looking straight through me…"),
                B("Bass", "I know that look. Same rivalry mess I've got with Mega Man — except yours is Count."),
                P("Deadline", "Correct. One master: my creator. One purpose: Count. Interference will be eradicated."),
                B("Bass", "Yeah, yeah. Complicated. I get it. Doesn't mean I like being treated like a speed bump."),
                P("Deadline", "Assessment irrelevant. You are not the target. You are in the way."),
                B("Bass", "That's what they always say. Fine. If you think you're stronger — prove it."),
                P("Deadline", "Engaging battle mode. Initiating combat systems."),
                P("Deadline", "PRESENT, PAST, OR FUTURE — AS LONG AS TIME IS MY WEAPON, YOU STAND NO CHANCE!"),
                B("Bass", "Heh… we'll see about that. HERE I COME!")
            };
        }

        return null;
    }
}
