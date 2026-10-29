using System;

/// <summary>
/// Built-in Deadline dialogue for Town NPCs when Inspector Deadline pages are empty.
/// Townsfolk fear Deterioration Acceleration (it can age living things to death in minutes).
/// Most plead with him to use that power for good — and not define himself only by killing Count.
/// Bass is the exception: he understands the Count obsession (same as his Mega Man drive),
/// does not pitch "use it for good," but still says a robot is more than a walking weapon
/// and that Deadline needs a goal of his own.
/// Deadline cooperates only to return home and defeat Count fairly for his creator.
/// Never mention the player-only wash/tint; NPCs cannot see it.
/// Kit and Scratch are girls; say "Town," never "Hometown" or "street."
/// </summary>
public static class DeadlineNpcDialogue
{
    public static string[] PagesFor(string speakerName)
    {
        string key = string.IsNullOrWhiteSpace(speakerName) ? "" : speakerName.Trim();

        if (Contains(key, "NPC Bass") || string.Equals(key, "Bass", StringComparison.OrdinalIgnoreCase))
        {
            return new[]
            {
                "Huh. Deadline. Yeah, I know that look. I've worn it every time Mega Man walks away without a scratch.",
                "You're stuck on Count the way I'm stuck on him. Fair fight, prove yourself, make the guy who built you proud. I get it. I'm not gonna lecture you about \"using that Acceleration for good.\"",
                "But listen — and I mean this. You're a robot, not a gun with legs. There's more to us than the one name we can't stop chasing.",
                "I aim to be the strongest. That's mine. You need something that's yours — not just \"end Count\" on a loop. Figure that out… or you'll hollow yourself out before you ever get home."
            };
        }

        if (Contains(key, "Kit's mom"))
        {
            return new[]
            {
                "Oh— Deadline. Could you… give me a little space, dear? Kit told me what that Acceleration of yours can do. Minutes. That's all it takes.",
                "I'm not trying to be unkind. I know you want to go home and face Count properly. That part, I can respect.",
                "It's just— when a mother hears a power can age someone to death that fast, her hands don't stop shaking. And I keep thinking… if it works that quickly for harm, maybe someday it could work that quickly for help.",
                "You don't have to be only the thing that ends Count. Even something built as a weapon can choose where it points."
            };
        }

        if (Contains(key, "Kit's dad"))
        {
            return new[]
            {
                "Deadline. I'll be straight with you — people around here are scared. Not of how you look. Of what that Acceleration does. Minutes, and someone's gone for good.",
                "You're holding yourself back so you can get home and settle things with Count fair and square. That takes more control than most machines ever show. I respect that.",
                "Still… a power that can rush a life to its end could rush help to someone who needs it too. This Town could use speed like that — without the cruelty.",
                "Just… don't let \"take down Count\" be the only thing written next to your name. Machine or not, you're more than the guy you're hunting."
            };
        }

        if (Contains(key, "Scratch"))
        {
            return new[]
            {
                "Deadline. Easy — my scanners jump every time you walk in. Not because I'm impressed. Because that Acceleration of yours is basically a countdown on a living body.",
                "Kit said you're cooperating to get home and beat Count clean. Okay. I can work with someone who keeps their word. I build for fighters like that.",
                "But between you and me? A power that burns through a lifetime that fast could also burn through a bad situation — a rescue, a medical emergency, buying someone a few minutes they didn't have.",
                "Bring crystals if you want upgrades. And maybe… don't make \"killing Count\" the only thing you're upgrading toward."
            };
        }

        if (Contains(key, "Cat Kid Boy"))
        {
            return new[]
            {
                "Um… Deadline? Mom said I shouldn't stand too close. She said your power can make people get really old… really, really fast.",
                "You're kinda scary. But Kit says you're trying to go home and fight Count fair, so… that part's kinda cool? In a scary way.",
                "If you can make bad robots fall apart faster, maybe you could help without hurting people? Just an idea.",
                "Please don't use the scary clock on us. Okay? Promise?"
            };
        }

        if (Contains(key, "Cat Kid Girl"))
        {
            return new[]
            {
                "Deadline… I watered my flowers farther away today. Mom said your Acceleration can make living things… go away. Like, forever. In minutes.",
                "I know you're not trying to hurt us. Kit said you want to go home and beat Count the right way. I believe her.",
                "Could you maybe… use that speed to help someday? Like fixing a bad day faster, not making someone disappear?",
                "You don't have to just be Count's enemy. You could protect things too. If you wanted."
            };
        }

        if (Contains(key, "Blonde Cat Woman"))
        {
            return new[]
            {
                "Deadline— sorry. Habit. My customers clear out when they spot you. Word travels: a few minutes under that Acceleration, and a life's just… spent.",
                "You've been careful though. Waiting for a fair fight with Count instead of tearing through us. Honestly? That's the only reason this plaza hasn't emptied for good.",
                "Please just hear me out. Something that kills that fast can also buy someone a chance — an escape, a rescue, closing a disaster before it finishes us.",
                "You're more than a weapon built to stop Count. Or… you could be. If you let yourself."
            };
        }

        if (Contains(key, "Blonde Cat man") || (Contains(key, "Blonde") && Contains(key, "man")))
        {
            return new[]
            {
                "Deadline. I'm gonna keep a few steps back — no offense. I've heard what that Acceleration does to a body.",
                "You're in our Town because you need a way home to settle Count properly. I get having a purpose. I also get why folks flinch when you walk by.",
                "Look… if that power can age someone to death, it can probably shut down a disaster before it finishes us too. Worth thinking about when the fighting cools off.",
                "Don't let \"the one who eliminates Count\" be the whole story. Even rivals grow past one name on a list."
            };
        }

        if (Contains(key, "Old Man Cat"))
        {
            return new[]
            {
                "Deadline. I've lived a long while. Your Acceleration could take that from someone in minutes. That isn't gossip. That's a fact that keeps me awake.",
                "You want a fair path home — to face Count and make your creator proud. Purpose can be honorable. Letting it eat you alive is not.",
                "A power that steals years can spare suffering too, if you aim it with a little mercy. This Town's hoping you learn the difference.",
                "You don't have to be only Count's ending. Even someone built to kill can choose what comes after."
            };
        }

        if (Contains(key, "Black Smith") || Contains(key, "Blacksmith"))
        {
            return new[]
            {
                "Deadline. I shoe horses and fit armor for a living. I don't scare easy. You? You scare me. Aging flesh in minutes is past anything my hammer can fix.",
                "You've been civil — cooperate, go home, beat Count fair, serve your creator. I can sell gear to a soldier who has rules.",
                "But hear it from a smith: anything that can destroy can defend. That kit of yours could clear a path for the living, not just erase one rival.",
                "Buy what you need. And remember — a blade made only to kill one man is still a waste of good steel."
            };
        }

        if (Contains(key, "Purple hair"))
        {
            return new[]
            {
                "Deadline. I almost asked you to sit for a sketch. Then I heard what your Acceleration does — minutes, and someone's gone. My hands kinda… quit on the idea.",
                "You're trying to get home for a clean fight with Count. That's weirdly principled for someone everyone calls evil.",
                "If you ever use that speed to save a life instead of shortening one, tell me. I'll paint that instead. Promise.",
                "You don't have to stay Count's shadow forever. Even the scary ones get a second motif."
            };
        }

        if (Contains(key, "Granny Cat"))
        {
            return new[]
            {
                "That's close enough, Deadline. I've buried enough people without watching a clock eat someone alive in minutes.",
                "Kit says you're only helping so you can go home and face Count fairly. I believe the girl. I still won't pretend I'm not afraid of you.",
                "Listen to an old woman: power that steals a lifetime can give someone a chance too. Use it for that someday. Please.",
                "Don't let \"eliminate Count\" be the only words you know how to say. Even a weapon can learn a kinder line."
            };
        }

        return new[]
        {
            "Deadline. Word's out about you. That Acceleration can age the living to death in minutes — and yes, that frightens this Town.",
            "We know why you're cooperating: get home, beat Count fair, please your creator. Purpose we understand. The fear… we can't just ignore.",
            "Please — that power could serve life as well as end it. You don't have to be only Count's executioner.",
            "Stay careful. And if you must be a weapon… aim at something worth more than revenge."
        };
    }

    private static bool Contains(string haystack, string needle) =>
        !string.IsNullOrEmpty(haystack) &&
        haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
}
