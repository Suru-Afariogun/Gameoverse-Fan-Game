using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Powered Up–style banter for Copy Bots and same-character Boss Select mirrors.
/// Copy speakers use a trailing "?" and render in red in the DialogueBox.
/// Before banter, the player walks to within a few spaces of the boss so both fit on camera.
/// </summary>
public static class CopyBotIntroDialogue
{
    /// <summary>World units per "space" (same convention as knockback / charge travel).</summary>
    private const float SpaceSize = 1f;
    private const float ApproachStopSpaces = 5f;
    private const float ApproachTimeoutSeconds = 10f;
    private const float ApproachArriveTolerance = 0.08f;

    /// <summary>
    /// Plays intro banter for the active player's character. No-ops if lines or UI are missing.
    /// </summary>
    public static IEnumerator PlayForActivePlayer(Boss preferredBoss = null)
    {
        PlayerController player = PlayerController.ResolveActive();
        if (player == null && PlayerSpawner.Instance != null)
            player = PlayerSpawner.Instance.CurrentPlayer;

        string id = player != null ? player.CharacterId : PlayerSpawner.SelectedCharacterId;
        yield return PlayForCharacter(id, player, preferredBoss);
    }

    public static IEnumerator PlayForCharacter(string characterId, PlayerController player, Boss preferredBoss = null)
    {
        DialogueBox.Line[] lines = LinesFor(characterId);
        if (lines == null || lines.Length == 0)
            yield break;

        yield return ApproachNearestBossForDialogue(player, preferredBoss);

        DialogueBox box = EnsureDialogueBox();
        if (box == null)
        {
            Debug.LogWarning("[CopyBotIntroDialogue] No DialogueBox available — skipping banter.");
            yield break;
        }

        bool done = false;
        box.OpenConversation(lines, player, () => done = true);
        while (!done)
            yield return null;
    }

    /// <summary>
    /// Walks the player toward the nearest living boss until they are
    /// <see cref="ApproachStopSpaces"/> spaces away (or already closer).
    /// </summary>
    public static IEnumerator ApproachNearestBossForDialogue(PlayerController player, Boss preferredBoss = null)
    {
        if (player == null || player.IsDead)
            yield break;

        Boss boss = preferredBoss != null && !preferredBoss.IsDead
            ? preferredBoss
            : FindNearestLivingBoss(player);
        if (boss == null)
            yield break;

        float stopDistance = ApproachStopSpaces * SpaceSize;
        float dx = boss.transform.position.x - player.transform.position.x;
        if (Mathf.Abs(dx) <= stopDistance + ApproachArriveTolerance)
        {
            FaceToward(player, dx);
            yield break;
        }

        // Keep the camera on the player so the walk brings the boss into frame.
        if (CameraFollow.Instance != null)
            CameraFollow.Instance.SetFollowEnabled(true);

        player.SetInputLocked(true);
        player.SetScriptedInvulnerable(true);

        float dir = Mathf.Sign(dx);
        if (Mathf.Abs(dir) < 0.01f)
            dir = player.FacingSign >= 0f ? 1f : -1f;

        float targetX = boss.transform.position.x - dir * stopDistance;
        float elapsed = 0f;
        float lastX = player.transform.position.x;
        float stuckTimer = 0f;

        while (player != null && !player.IsDead && boss != null && !boss.IsDead)
        {
            float currentDx = boss.transform.position.x - player.transform.position.x;
            if (Mathf.Abs(currentDx) <= stopDistance + ApproachArriveTolerance)
                break;

            float toTarget = targetX - player.transform.position.x;
            if (Mathf.Abs(toTarget) <= ApproachArriveTolerance || Mathf.Sign(toTarget) != dir)
                break;

            player.SetScriptedMove(dir);
            FaceToward(player, dir);

            elapsed += Time.deltaTime;
            if (elapsed >= ApproachTimeoutSeconds)
                break;

            // Bail if a wall stops progress.
            float moved = Mathf.Abs(player.transform.position.x - lastX);
            lastX = player.transform.position.x;
            if (moved < 0.01f)
            {
                stuckTimer += Time.deltaTime;
                if (stuckTimer >= 0.75f)
                    break;
            }
            else
            {
                stuckTimer = 0f;
            }

            yield return null;
        }

        if (player != null)
        {
            player.ClearScriptedMove();
            Rigidbody2D rb = player.GetComponent<Rigidbody2D>();
            if (rb != null)
                rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);

            if (boss != null)
                FaceToward(player, boss.transform.position.x - player.transform.position.x);

            player.SetScriptedInvulnerable(false);
            player.SetInputLocked(true);
        }
    }

    private static void FaceToward(PlayerController player, float signedDeltaX)
    {
        if (player == null)
            return;

        if (Mathf.Abs(signedDeltaX) < 0.01f)
            return;

        player.SetFacingSign(Mathf.Sign(signedDeltaX));
    }

    private static Boss FindNearestLivingBoss(PlayerController player)
    {
        Boss[] bosses = UnityEngine.Object.FindObjectsByType<Boss>(FindObjectsSortMode.None);
        Boss best = null;
        float bestSqr = float.MaxValue;
        Vector3 from = player.transform.position;

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

    public static DialogueBox EnsureDialogueBox()
    {
        if (DialogueBox.Instance != null)
            return DialogueBox.Instance;

        DialogueBox existing = UnityEngine.Object.FindFirstObjectByType<DialogueBox>();
        if (existing != null)
            return existing;

        GameObject prefab = Resources.Load<GameObject>("Tutorial Dialogue Box");
        if (prefab == null)
            return null;

        GameObject instance = UnityEngine.Object.Instantiate(prefab);
        instance.name = "Dialouge box";

        DialogueBox box = instance.GetComponent<DialogueBox>();
        if (box == null)
            box = instance.AddComponent<DialogueBox>();

        Canvas canvas = instance.GetComponentInChildren<Canvas>(true);
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            canvas.worldCamera = Camera.main != null ? Camera.main : UnityEngine.Object.FindFirstObjectByType<Camera>();

        return box;
    }

    public static DialogueBox.Line[] LinesFor(string characterId)
    {
        string id = string.IsNullOrWhiteSpace(characterId) ? "Kit" : characterId.Trim();

        if (EqualsId(id, "Malice"))
            return Malice();
        if (EqualsId(id, "Harlie"))
            return Harlie();
        if (EqualsId(id, "Hex"))
            return Hex();
        if (EqualsId(id, "Count"))
            return Count();
        if (EqualsId(id, "Bass"))
            return Bass();

        return Kit();
    }

    private static bool EqualsId(string id, string other) =>
        string.Equals(id, other, StringComparison.OrdinalIgnoreCase);

    private static DialogueBox.Line Copy(string name, string text) =>
        new DialogueBox.Line(name + "?", text, isCopySpeaker: true);

    private static DialogueBox.Line Real(string name, string text) =>
        new DialogueBox.Line(name, text, isCopySpeaker: false);

    private static DialogueBox.Line[] Kit()
    {
        return new[]
        {
            Copy("Kit", "Well, well... look what the cat dragged in. Took you long enough, original."),
            Real("Kit", "Dragged in?! I'm the hero of this Town — not some stray off the street!"),
            Copy("Kit", "Hero, stray... either way, the kitty's finally come to play. I've got this whole saving-people routine down to puuuuurfection — so you and your hairball buddy Ka-uh-whodle can go find a ditch."),
            Real("Kit", "Leave Kaboodle out of this!"),
            Copy("Kit", "Then stop me. Claws out — this Town's getting a new top cat!")
        };
    }

    private static DialogueBox.Line[] Malice()
    {
        return new[]
        {
            Copy("Malice", "Long time coming, Malice... my loathsome original."),
            Real("Malice", "Loathsome? You're a cheap knockoff with my face."),
            Copy("Malice", "No. I'm what you were supposed to be — sharper, colder, and done pretending mercy is a personality."),
            Real("Malice", "Keep telling yourself that. The real Malice doesn't need a script to scare people."),
            Copy("Malice", "Then stop stalling and bow already. Show me what the \"real\" one is made of... before I carve the answer out myself.")
        };
    }

    private static DialogueBox.Line[] Harlie()
    {
        return new[]
        {
            Copy("Harlie", "Encore, darling. Same smile, same steel — though I do think I got the better lighting."),
            Real("Harlie", "Cute entrance. Shame the footwork still looks like dress rehearsal."),
            Copy("Harlie", "Dress rehearsal? I've been waiting in the wings for this duel since the curtain first went up."),
            Real("Harlie", "Then you should already know — I don't share the spotlight."),
            Copy("Harlie", "Neither do I. So draw your blade... and try to keep up with your own choreography.")
        };
    }

    private static DialogueBox.Line[] Hex()
    {
        return new[]
        {
            Copy("Hex", "How sad. My soft little prototype finally wandered in."),
            Real("Hex", "Prototype? You're the spare they bolted my moves onto."),
            Copy("Hex", "Spare? Please. I'm the finished Hex — meaner, cleaner, and not wasted on pretending I care. All that temper you hide? I wear it properly."),
            Real("Hex", "Having my dive stab doesn't make you me. It makes you desperate."),
            Copy("Hex", "Desperate? Take all the time you want, original — I've already won. Now raise your blade... and prove you still deserve the name.")
        };
    }

    private static DialogueBox.Line[] Count()
    {
        return new[]
        {
            Copy("Count", "Right on schedule. I was wondering when you'd walk in."),
            Real("Count", "I already have Deadline wasting my time. I don't need another evil twin clogging the calendar."),
            Copy("Count", "Deadline's old news. I'm the appointment you can't reschedule."),
            Real("Count", "Then make it brief. I don't double-book disasters."),
            Copy("Count", "Gladly. Let's see whose clock runs out first.")
        };
    }

    private static DialogueBox.Line[] Bass()
    {
        return new[]
        {
            Copy("Bass", "Heh. Guess Wily's copy machine finally spit out a winner."),
            Real("Bass", "You've got to be kidding me. Another Wily mess? I just finished cleaning up after the last one."),
            Copy("Bass", "Mess? Easy, original. The machine didn't slip up — it finally built you some real competition."),
            Real("Bass", "Competition for what? Mega Man won't even look my way, and now I'm stuck babysitting his leftovers."),
            Copy("Bass", "Then quit complaining and fight me. If Mega Man won't settle who's strongest... the next best opponent is myself.")
        };
    }
}
