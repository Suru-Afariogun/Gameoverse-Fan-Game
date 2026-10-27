using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Subtle black wash on world sprites/tilemaps while Bass Double Gear is active.
/// Shared across player Bass and Boss Bass so both can keep the dim while either is geared,
/// and both characters stay untinted.
/// </summary>
public class BassDoubleGearWorldTint : MonoBehaviour
{
    private Transform retainedExclude;

    public void Apply(Transform exclude, float tintAmount = -1f)
    {
        if (retainedExclude != null && retainedExclude != exclude)
            BassDoubleGearWorldDarkness.Release(retainedExclude);

        retainedExclude = exclude;
        BassDoubleGearWorldDarkness.Retain(exclude, tintAmount);
    }

    public void Release()
    {
        if (retainedExclude == null)
            return;

        BassDoubleGearWorldDarkness.Release(retainedExclude);
        retainedExclude = null;
    }

    private void OnDestroy()
    {
        Release();
    }
}

/// <summary>
/// Reference-counted world dim used by every Bass Double Gear owner.
/// </summary>
public static class BassDoubleGearWorldDarkness
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
    private static float blackTintAmount = 0.28f;

    public static void Retain(Transform exclude, float tintAmount = -1f)
    {
        if (tintAmount >= 0f)
        {
            float clamped = Mathf.Clamp01(tintAmount);
            // First retain sets the amount; later retains can only deepen the wash.
            blackTintAmount = excludes.Count == 0
                ? clamped
                : Mathf.Max(blackTintAmount, clamped);
        }

        if (exclude != null && !excludes.Contains(exclude))
            excludes.Add(exclude);

        Rebuild();
    }

    public static void Release(Transform exclude)
    {
        if (exclude != null)
            excludes.Remove(exclude);

        // Drop destroyed / null excludes so a dead Bass cannot keep the wash forever.
        for (int i = excludes.Count - 1; i >= 0; i--)
        {
            if (excludes[i] == null)
                excludes.RemoveAt(i);
        }

        if (excludes.Count == 0)
        {
            ClearTint();
            blackTintAmount = 0.28f;
            return;
        }

        Rebuild();
    }

    private static void Rebuild()
    {
        ClearTint();
        if (blackTintAmount <= 0.001f || excludes.Count == 0)
            return;

        // Lerp toward a soft charcoal so the world dims without crushing contrast.
        Color wash = Color.Lerp(Color.white, new Color(0.42f, 0.42f, 0.48f, 1f), blackTintAmount);

        SpriteRenderer[] allSprites =
            Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < allSprites.Length; i++)
        {
            SpriteRenderer sr = allSprites[i];
            if (sr == null || !sr.enabled || IsUnderAnyExclude(sr.transform))
                continue;
            // Afterimages stay bright on top of the wash even if parenting/exclude misses them.
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
