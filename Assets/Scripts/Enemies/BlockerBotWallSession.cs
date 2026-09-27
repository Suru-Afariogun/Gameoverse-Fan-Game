using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared wall session for Blocker Bots. Forms walls on both sides of the player.
/// New joiners stack into open slots; existing holders keep their slots (no leader-promotion reshuffle).
/// </summary>
public static class BlockerBotWallSession
{
    private static readonly List<BlockerBot> Members = new List<BlockerBot>(8);
    private static readonly Dictionary<BlockerBot, Vector2> Slots = new Dictionary<BlockerBot, Vector2>(8);
    private static readonly HashSet<Vector2Int> OccupiedCells = new HashSet<Vector2Int>();

    public static bool IsActive { get; private set; }
    public static BlockerBot Leader { get; private set; }
    public static float WallXLeft { get; private set; }
    public static float WallXRight { get; private set; }
    public static float MiddleY { get; private set; }

    public static float WallX => Leader != null && Leader.HomePosition.x <= (WallXLeft + WallXRight) * 0.5f
        ? WallXLeft
        : WallXRight;

    public static bool Contains(BlockerBot bot)
    {
        return bot != null && Members.Contains(bot);
    }

    public static bool TryBeginOrJoin(BlockerBot bot, PlayerController player, float standOffSpaces)
    {
        if (bot == null || player == null || player.IsDead)
            return false;

        if (!IsActive)
        {
            Leader = bot;
            float px = player.transform.position.x;
            float standoff = Mathf.Max(0.1f, standOffSpaces);
            WallXLeft = px - standoff;
            WallXRight = px + standoff;
            MiddleY = player.transform.position.y;
            IsActive = true;
            Members.Clear();
            SoftClearSlots();
        }

        if (!Members.Contains(bot))
            Members.Add(bot);

        if (Slots.ContainsKey(bot))
            return true;

        if (TryAssignOpenSlot(bot))
            return true;

        // In session, but nowhere to stand yet — wait where they are.
        bot.EnterWaitingForRoute();
        return false;
    }

    /// <summary>
    /// Retry placing a bot that is waiting because every route/slot was blocked.
    /// </summary>
    public static bool TryAssignWaiting(BlockerBot bot)
    {
        if (bot == null || !IsActive || !Members.Contains(bot))
            return false;
        if (Slots.ContainsKey(bot))
            return true;

        return TryAssignOpenSlot(bot);
    }

    public static void Leave(BlockerBot bot)
    {
        if (bot == null)
            return;

        Members.Remove(bot);
        ReleaseSlot(bot);

        if (Members.Count == 0)
        {
            Clear();
            return;
        }

        // Leader died / left: do NOT promote anyone or reshuffle the wall.
        if (Leader == bot)
            Leader = null;
    }

    public static void Clear()
    {
        IsActive = false;
        Leader = null;
        Members.Clear();
        SoftClearSlots();
        WallXLeft = 0f;
        WallXRight = 0f;
        MiddleY = 0f;
    }

    public static bool AnyMemberSeesPlayer()
    {
        for (int i = 0; i < Members.Count; i++)
        {
            BlockerBot bot = Members[i];
            if (bot != null && !bot.IsDead && bot.SeesPlayerNow)
                return true;
        }

        return false;
    }

    /// <summary>
    /// When no member still sees the player, send everyone home and clear the session.
    /// </summary>
    public static void DisbandIfPlayerLost()
    {
        if (!IsActive)
            return;

        if (AnyMemberSeesPlayer())
            return;

        List<BlockerBot> snapshot = new List<BlockerBot>(Members);
        Clear();
        for (int i = 0; i < snapshot.Count; i++)
        {
            if (snapshot[i] != null && !snapshot[i].IsDead)
                snapshot[i].ForceReturnHomeFromSession();
        }
    }

    private static void SoftClearSlots()
    {
        Slots.Clear();
        OccupiedCells.Clear();
    }

    private static void ReleaseSlot(BlockerBot bot)
    {
        if (bot == null)
            return;

        if (!Slots.TryGetValue(bot, out Vector2 world))
            return;

        Slots.Remove(bot);
        Vector2 size = bot.BodySize;
        float wallX = world.x < (WallXLeft + WallXRight) * 0.5f ? WallXLeft : WallXRight;
        OccupiedCells.Remove(WorldToCell(wallX, world, size));
    }

    private static bool TryAssignOpenSlot(BlockerBot bot)
    {
        float midX = (WallXLeft + WallXRight) * 0.5f;
        float wallX = bot.HomePosition.x <= midX ? WallXLeft : WallXRight;
        Vector2 size = bot.BodySize;

        // Prefer stacking onto the densest column already on this side.
        int preferredCol = 0;
        int bestCount = -1;
        for (int col = 0; col < 8; col++)
        {
            int count = CountOccupiedInColumn(wallX, col, size);
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
                int stackIndex = stackOrder[s];
                if (!TryBuildCandidate(bot, wallX, col, stackIndex, size, out Vector2 candidate, out Vector2Int cell))
                    continue;

                if (OccupiedCells.Contains(cell))
                    continue;
                if (bot.WouldOverlapPlatform(candidate))
                    continue;
                if (!bot.CanReachWallSlot(candidate))
                    continue;

                CommitSlot(bot, wallX, candidate, cell);
                return true;
            }
        }

        // No open route / slot — caller should wait in place.
        return false;
    }

    private static bool TryBuildCandidate(
        BlockerBot bot,
        float wallX,
        int col,
        int stackIndex,
        Vector2 size,
        out Vector2 candidate,
        out Vector2Int cell)
    {
        candidate = default;
        cell = default;

        float midX = (WallXLeft + WallXRight) * 0.5f;
        int colDir = wallX < midX ? -1 : 1;
        float x = wallX + col * colDir * size.x;
        float y = MiddleY + stackIndex * size.y;
        candidate = new Vector2(x, y);
        cell = new Vector2Int(col * colDir, stackIndex);
        return true;
    }

    private static void CommitSlot(BlockerBot bot, float wallX, Vector2 candidate, Vector2Int cell)
    {
        OccupiedCells.Add(cell);
        Slots[bot] = candidate;
        bot.AssignWallSlot(candidate, allowReshuffle: false);
    }

    private static int CountOccupiedInColumn(float wallX, int col, Vector2 size)
    {
        float midX = (WallXLeft + WallXRight) * 0.5f;
        int colDir = wallX < midX ? -1 : 1;
        int signedCol = col * colDir;
        int count = 0;
        foreach (Vector2Int cell in OccupiedCells)
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

    private static Vector2Int WorldToCell(float wallX, Vector2 world, Vector2 size)
    {
        int col = Mathf.RoundToInt((world.x - wallX) / Mathf.Max(0.01f, size.x));
        int row = Mathf.RoundToInt((world.y - MiddleY) / Mathf.Max(0.01f, size.y));
        return new Vector2Int(col, row);
    }
}
