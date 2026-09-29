using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A local wall formed by Blocker Bots on both sides of the player.
/// Each group of bots gets its own session: a bot only joins a session whose members
/// (or whose wall) are inside its own detection radius, so bots across the scene never
/// get pulled into someone else's wall.
/// New joiners stack into open slots; existing holders keep their slots (no leader-promotion reshuffle).
/// </summary>
public sealed class BlockerBotWallSession
{
    private static readonly List<BlockerBotWallSession> Sessions = new List<BlockerBotWallSession>(4);

    private readonly List<BlockerBot> members = new List<BlockerBot>(8);
    private readonly Dictionary<BlockerBot, Vector2> slots = new Dictionary<BlockerBot, Vector2>(8);
    private readonly HashSet<Vector2Int> occupiedCells = new HashSet<Vector2Int>();

    private float wallXLeft;
    private float wallXRight;
    private float middleY;

    private Vector2 WallCenter => new Vector2((wallXLeft + wallXRight) * 0.5f, middleY);

    public static bool Contains(BlockerBot bot)
    {
        return FindSessionOf(bot) != null;
    }

    /// <summary>
    /// Join the session of an ally / wall inside <paramref name="vision"/>, or start a new wall
    /// around the player when there is none nearby.
    /// </summary>
    public static bool TryBeginOrJoin(
        BlockerBot bot,
        PlayerController player,
        float standOffSpaces,
        Bounds vision,
        BlockerBotWallSession preferred = null)
    {
        if (bot == null || player == null || player.IsDead)
            return false;

        BlockerBotWallSession session = FindSessionOf(bot);
        if (session != null)
            return session.slots.ContainsKey(bot) || session.TryAssignOpenSlot(bot);

        session = preferred != null && Sessions.Contains(preferred)
            ? preferred
            : FindNearbySession(bot, vision);

        if (session == null)
        {
            session = new BlockerBotWallSession();
            float px = player.transform.position.x;
            float standoff = Mathf.Max(0.1f, standOffSpaces);
            session.wallXLeft = px - standoff;
            session.wallXRight = px + standoff;
            session.middleY = player.transform.position.y;
            Sessions.Add(session);
        }

        session.members.Add(bot);

        if (session.TryAssignOpenSlot(bot))
            return true;

        // In session, but nowhere to stand yet — wait where they are.
        bot.EnterWaitingForRoute();
        return false;
    }

    /// <summary>
    /// Session of a bot inside <paramref name="vision"/> that can see the player or is holding a wall.
    /// </summary>
    public static BlockerBotWallSession FindSessionOfAllyInside(BlockerBot self, Bounds vision)
    {
        for (int s = 0; s < Sessions.Count; s++)
        {
            BlockerBotWallSession session = Sessions[s];
            for (int i = 0; i < session.members.Count; i++)
            {
                BlockerBot other = session.members[i];
                if (other == null || other == self || other.IsDead)
                    continue;

                if (!other.SeesPlayerNow && !other.IsHoldingWall)
                    continue;

                if (BodyInside(other, vision))
                    return session;
            }
        }

        return null;
    }

    /// <summary>
    /// Retry placing a bot that is waiting because every route/slot was blocked.
    /// </summary>
    public static bool TryAssignWaiting(BlockerBot bot)
    {
        BlockerBotWallSession session = FindSessionOf(bot);
        if (session == null)
            return false;
        if (session.slots.ContainsKey(bot))
            return true;

        return session.TryAssignOpenSlot(bot);
    }

    public static void Leave(BlockerBot bot)
    {
        BlockerBotWallSession session = FindSessionOf(bot);
        if (session == null)
            return;

        session.members.Remove(bot);
        session.ReleaseSlot(bot);

        // Leader died / left: do NOT promote anyone or reshuffle the wall.
        if (session.members.Count == 0)
            Sessions.Remove(session);
    }

    /// <summary>
    /// When no member of this bot's wall still sees the player, send them all home and end that wall.
    /// </summary>
    public static void DisbandIfPlayerLost(BlockerBot bot)
    {
        BlockerBotWallSession session = FindSessionOf(bot);
        if (session == null || session.AnyMemberSeesPlayer())
            return;

        Sessions.Remove(session);
        List<BlockerBot> snapshot = new List<BlockerBot>(session.members);
        session.members.Clear();
        session.slots.Clear();
        session.occupiedCells.Clear();

        for (int i = 0; i < snapshot.Count; i++)
        {
            if (snapshot[i] != null && !snapshot[i].IsDead)
                snapshot[i].ForceReturnHomeFromSession();
        }
    }

    private static BlockerBotWallSession FindSessionOf(BlockerBot bot)
    {
        if (bot == null)
            return null;

        for (int i = 0; i < Sessions.Count; i++)
        {
            if (Sessions[i].members.Contains(bot))
                return Sessions[i];
        }

        return null;
    }

    private static BlockerBotWallSession FindNearbySession(BlockerBot bot, Bounds vision)
    {
        for (int s = 0; s < Sessions.Count; s++)
        {
            BlockerBotWallSession session = Sessions[s];
            if (PointInside(session.WallCenter, vision))
                return session;

            for (int i = 0; i < session.members.Count; i++)
            {
                BlockerBot other = session.members[i];
                if (other != null && other != bot && !other.IsDead && BodyInside(other, vision))
                    return session;
            }
        }

        return null;
    }

    private static bool BodyInside(BlockerBot bot, Bounds vision)
    {
        Collider2D body = bot.GetComponent<Collider2D>();
        if (body == null)
            body = bot.GetComponentInChildren<Collider2D>();

        return body != null
            ? vision.Intersects(body.bounds)
            : PointInside(bot.transform.position, vision);
    }

    private static bool PointInside(Vector2 point, Bounds vision)
    {
        return point.x >= vision.min.x && point.x <= vision.max.x &&
               point.y >= vision.min.y && point.y <= vision.max.y;
    }

    private bool AnyMemberSeesPlayer()
    {
        for (int i = 0; i < members.Count; i++)
        {
            BlockerBot bot = members[i];
            if (bot != null && !bot.IsDead && bot.SeesPlayerNow)
                return true;
        }

        return false;
    }

    private void ReleaseSlot(BlockerBot bot)
    {
        if (bot == null || !slots.TryGetValue(bot, out Vector2 world))
            return;

        slots.Remove(bot);
        Vector2 size = bot.BodySize;
        float wallX = world.x < (wallXLeft + wallXRight) * 0.5f ? wallXLeft : wallXRight;
        occupiedCells.Remove(WorldToCell(wallX, world, size));
    }

    private bool TryAssignOpenSlot(BlockerBot bot)
    {
        float midX = (wallXLeft + wallXRight) * 0.5f;
        float wallX = bot.HomePosition.x <= midX ? wallXLeft : wallXRight;
        Vector2 size = bot.BodySize;

        // Prefer stacking onto the densest column already on this side.
        int preferredCol = 0;
        int bestCount = -1;
        for (int col = 0; col < 8; col++)
        {
            int count = CountOccupiedInColumn(wallX, col);
            if (count > bestCount)
            {
                bestCount = count;
                preferredCol = col;
            }
        }

        int[] stackOrder = BuildStackOrder(16);
        int[] colOrder = BuildColumnOrder(preferredCol, 8);

        for (int c = 0; c < colOrder.Length; c++)
        {
            int col = colOrder[c];
            for (int s = 0; s < stackOrder.Length; s++)
            {
                BuildCandidate(wallX, col, stackOrder[s], size, out Vector2 candidate, out Vector2Int cell);

                if (occupiedCells.Contains(cell))
                    continue;
                if (!bot.IsWithinWallTravelRange(candidate))
                    continue;
                if (bot.WouldOverlapPlatform(candidate))
                    continue;
                if (!bot.CanReachWallSlot(candidate))
                    continue;

                occupiedCells.Add(cell);
                slots[bot] = candidate;
                bot.AssignWallSlot(candidate, allowReshuffle: false);
                return true;
            }
        }

        // No open route / slot — caller should wait in place.
        return false;
    }

    private void BuildCandidate(
        float wallX,
        int col,
        int stackIndex,
        Vector2 size,
        out Vector2 candidate,
        out Vector2Int cell)
    {
        float midX = (wallXLeft + wallXRight) * 0.5f;
        int colDir = wallX < midX ? -1 : 1;
        float x = wallX + col * colDir * size.x;
        float y = middleY + stackIndex * size.y;
        candidate = new Vector2(x, y);
        cell = new Vector2Int(col * colDir, stackIndex);
    }

    private int CountOccupiedInColumn(float wallX, int col)
    {
        float midX = (wallXLeft + wallXRight) * 0.5f;
        int colDir = wallX < midX ? -1 : 1;
        int signedCol = col * colDir;
        int count = 0;
        foreach (Vector2Int cell in occupiedCells)
        {
            if (cell.x == signedCol)
                count++;
        }

        return count;
    }

    private static int[] BuildStackOrder(int count)
    {
        // 0 (middle), +1, -1, +2, -2...
        int[] order = new int[count];
        order[0] = 0;
        int write = 1;
        int n = 1;
        while (write < count)
        {
            order[write++] = n;
            if (write >= count)
                break;
            order[write++] = -n;
            n++;
        }

        return order;
    }

    private static int[] BuildColumnOrder(int preferred, int maxCols)
    {
        int[] order = new int[maxCols];
        order[0] = Mathf.Clamp(preferred, 0, maxCols - 1);
        int write = 1;
        for (int dist = 1; write < maxCols; dist++)
        {
            int a = preferred + dist;
            int b = preferred - dist;
            if (a >= 0 && a < maxCols)
                order[write++] = a;
            if (write >= maxCols)
                break;
            if (b >= 0 && b < maxCols)
                order[write++] = b;
        }

        // Fill any gaps
        for (int i = 0; i < maxCols && write < maxCols; i++)
        {
            bool found = false;
            for (int j = 0; j < write; j++)
            {
                if (order[j] == i)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
                order[write++] = i;
        }

        return order;
    }

    private Vector2Int WorldToCell(float wallX, Vector2 world, Vector2 size)
    {
        int col = Mathf.RoundToInt((world.x - wallX) / Mathf.Max(0.01f, size.x));
        int row = Mathf.RoundToInt((world.y - middleY) / Mathf.Max(0.01f, size.y));
        return new Vector2Int(col, row);
    }
}
