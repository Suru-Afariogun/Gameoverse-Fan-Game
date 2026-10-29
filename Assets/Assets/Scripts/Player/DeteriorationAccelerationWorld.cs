using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Deadline Deterioration Acceleration — speeds the world (enemies/bosses/hazards) and applies a yellow wash.
/// Progressive scales are owned by <see cref="DeadlinePlayerController"/>; this type exposes the live multipliers
/// consumed by <see cref="HyperSpeedWorldSlow"/> and Deadline's own movement.
/// </summary>
public static class DeteriorationAccelerationWorld
{
    public static bool IsActive { get; private set; }
    /// <summary>Non-boss enemy / hazard scale (ramps toward 6, or 3 with Hyper Ability 3).</summary>
    public static float WorldTimeScale { get; private set; } = 1f;
    /// <summary>Boss scale (ramps toward 2 — bosses must not share the enemy ×6 cap).</summary>
    public static float BossTimeScale { get; private set; } = 1f;
    /// <summary>Deadline's personal scale (ramps toward 3, or 5 with Hyper Ability 3).</summary>
    public static float PlayerTimeScale { get; private set; } = 1f;

    public static void SetScales(float enemyScale, float bossScale, float playerScale)
    {
        WorldTimeScale = Mathf.Max(1f, enemyScale);
        BossTimeScale = Mathf.Max(1f, bossScale);
        PlayerTimeScale = Mathf.Max(1f, playerScale);
        bool was = IsActive;
        IsActive = WorldTimeScale > 1.001f || BossTimeScale > 1.001f || PlayerTimeScale > 1.001f;
        if (was != IsActive)
            HyperSpeedWorldSlow.RefreshActiveFromAcceleration();
    }

    /// <summary>Legacy 2-arg API: bosses share a soft cap of 2 while enemies use <paramref name="worldScale"/>.</summary>
    public static void SetScales(float worldScale, float playerScale) =>
        SetScales(worldScale, Mathf.Min(worldScale, 2f), playerScale);

    public static void Clear()
    {
        WorldTimeScale = 1f;
        BossTimeScale = 1f;
        PlayerTimeScale = 1f;
        if (IsActive)
        {
            IsActive = false;
            HyperSpeedWorldSlow.RefreshActiveFromAcceleration();
        }
    }
}

/// <summary>Yellow world wash while Deadline's Deterioration Acceleration is active.</summary>
public class DeadlineDeteriorationWorldTint : MonoBehaviour
{
    private Transform retainedExclude;

    public void Apply(Transform exclude, float tintAmount = -1f)
    {
        if (retainedExclude != null && retainedExclude != exclude)
            DeadlineDeteriorationWorldYellowness.Release(retainedExclude);

        retainedExclude = exclude;
        DeadlineDeteriorationWorldYellowness.Retain(exclude, tintAmount);
    }

    public void Release()
    {
        if (retainedExclude == null)
            return;

        DeadlineDeteriorationWorldYellowness.Release(retainedExclude);
        retainedExclude = null;
    }

    private void OnDestroy()
    {
        Release();
    }
}

public static class DeadlineDeteriorationWorldYellowness
{
    private struct CachedSprite
    {
        public SpriteRenderer Renderer;
        public Color Original;
    }

    private struct CachedTilemap
    {
        public Tilemap Tilemap;
        public Color Original;
    }

    private static readonly List<Transform> excludes = new List<Transform>(4);
    private static readonly List<CachedSprite> cachedSprites = new List<CachedSprite>(256);
    private static readonly List<CachedTilemap> cachedTilemaps = new List<CachedTilemap>(32);
    private static float yellowTintAmount = 0.28f;

    public static void Retain(Transform exclude, float tintAmount = -1f)
    {
        if (tintAmount >= 0f)
        {
            float clamped = Mathf.Clamp01(tintAmount);
            yellowTintAmount = excludes.Count == 0
                ? clamped
                : Mathf.Max(yellowTintAmount, clamped);
        }

        if (exclude != null && !excludes.Contains(exclude))
            excludes.Add(exclude);

        Rebuild();
    }

    public static void Release(Transform exclude)
    {
        if (exclude != null)
            excludes.Remove(exclude);

        for (int i = excludes.Count - 1; i >= 0; i--)
        {
            if (excludes[i] == null)
                excludes.RemoveAt(i);
        }

        if (excludes.Count == 0)
        {
            ClearTint();
            yellowTintAmount = 0.28f;
            return;
        }

        Rebuild();
    }

    private static void Rebuild()
    {
        ClearTint();
        if (yellowTintAmount <= 0.001f || excludes.Count == 0)
            return;

        Color wash = Color.Lerp(Color.white, new Color(1f, 0.92f, 0.35f, 1f), yellowTintAmount);

        SpriteRenderer[] allSprites =
            Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < allSprites.Length; i++)
        {
            SpriteRenderer sr = allSprites[i];
            if (sr == null || !sr.enabled || IsUnderAnyExclude(sr.transform))
                continue;
            if (sr.gameObject.name.IndexOf("Afterimage", System.StringComparison.OrdinalIgnoreCase) >= 0)
                continue;

            cachedSprites.Add(new CachedSprite { Renderer = sr, Original = sr.color });
            Color c = sr.color;
            sr.color = new Color(
                Mathf.Min(1f, c.r * wash.r),
                Mathf.Min(1f, c.g * wash.g),
                Mathf.Min(1f, c.b * wash.b),
                c.a);
        }

        TilemapRenderer[] tilemapRenderers =
            Object.FindObjectsByType<TilemapRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < tilemapRenderers.Length; i++)
        {
            TilemapRenderer tr = tilemapRenderers[i];
            if (tr == null || !tr.enabled || IsUnderAnyExclude(tr.transform))
                continue;

            Tilemap tm = tr.GetComponent<Tilemap>();
            if (tm == null)
                continue;

            cachedTilemaps.Add(new CachedTilemap { Tilemap = tm, Original = tm.color });
            Color c = tm.color;
            tm.color = new Color(
                Mathf.Min(1f, c.r * wash.r),
                Mathf.Min(1f, c.g * wash.g),
                Mathf.Min(1f, c.b * wash.b),
                c.a);
        }
    }

    private static void ClearTint()
    {
        for (int i = 0; i < cachedSprites.Count; i++)
        {
            if (cachedSprites[i].Renderer != null)
                cachedSprites[i].Renderer.color = cachedSprites[i].Original;
        }

        for (int i = 0; i < cachedTilemaps.Count; i++)
        {
            if (cachedTilemaps[i].Tilemap != null)
                cachedTilemaps[i].Tilemap.color = cachedTilemaps[i].Original;
        }

        cachedSprites.Clear();
        cachedTilemaps.Clear();
    }

    private static bool IsUnderAnyExclude(Transform t)
    {
        if (t == null)
            return false;

        for (int i = 0; i < excludes.Count; i++)
        {
            Transform root = excludes[i];
            if (root == null)
                continue;
            if (t == root || t.IsChildOf(root))
                return true;
        }

        return false;
    }
}
