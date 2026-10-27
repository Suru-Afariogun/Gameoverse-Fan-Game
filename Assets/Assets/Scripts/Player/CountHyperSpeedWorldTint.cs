using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Applies a subtle red wash to world sprites/tilemaps during Count hyper speed / Flash time.
/// Shared across playable Count and Boss Count (Bass Double Gear–style refcount) so both can
/// keep the wash while either is altering time, and both Counts stay untinted.
/// </summary>
public class CountHyperSpeedWorldTint : MonoBehaviour
{
    private Transform retainedExclude;

    public void Apply(Transform exclude, float tintAmount = -1f)
    {
        if (retainedExclude != null && retainedExclude != exclude)
            CountHyperSpeedWorldRedness.Release(retainedExclude);

        retainedExclude = exclude;
        CountHyperSpeedWorldRedness.Retain(exclude, tintAmount);
    }

    public void Release()
    {
        if (retainedExclude == null)
            return;

        CountHyperSpeedWorldRedness.Release(retainedExclude);
        retainedExclude = null;
    }

    private void OnDestroy()
    {
        Release();
    }
}

/// <summary>
/// Reference-counted red world wash used by every Count Hyper Speed / Flash Time owner.
/// </summary>
public static class CountHyperSpeedWorldRedness
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
    private static float redTintAmount = 0.28f;

    public static void Retain(Transform exclude, float tintAmount = -1f)
    {
        if (tintAmount >= 0f)
        {
            float clamped = Mathf.Clamp01(tintAmount);
            redTintAmount = excludes.Count == 0
                ? clamped
                : Mathf.Max(redTintAmount, clamped);
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
            redTintAmount = 0.28f;
            return;
        }

        Rebuild();
    }

    private static void Rebuild()
    {
        ClearTint();
        if (redTintAmount <= 0.001f || excludes.Count == 0)
            return;

        Color wash = Color.Lerp(Color.white, new Color(1f, 0.45f, 0.45f, 1f), redTintAmount);

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

/// <summary>
/// Slows world actors during Count hyper speed without touching Time.timeScale.
/// Down hyper: world only. Up hyper: world and Count both use scaled deltas / speed.
///
/// Multiple owners (player Bass DG, Boss Bass DG, Flash Stop, Count Hyper, Boss Count Flash, etc.) can stack.
/// Effective scale is the minimum; SlowsPlayer is true if any layer requests it.
/// Ending one owner leaves others intact.
///
/// Jump/fall: gravity and gravityScale are left alone (avoids lift-off). Descent is softened
/// by reducing the fall multiplier — including below 1 so fall partially counters gravity
/// while airborne only.
/// </summary>
public static class HyperSpeedWorldSlow
{
    private struct Layer
    {
        public object Owner;
        public float Scale;
        public bool SlowPlayer;
    }

    private static readonly List<Layer> layers = new List<Layer>(4);
    private static readonly object LegacyOwner = new object();

    public static bool IsActive { get; private set; }
    public static float WorldTimeScale { get; private set; } = 1f;
    public static bool SlowsPlayer { get; private set; }

    /// <summary>Legacy single-owner Begin. Prefer <see cref="Begin(object, float, bool)"/>.</summary>
    public static void Begin(float worldScale, bool slowPlayer) =>
        Begin(LegacyOwner, worldScale, slowPlayer);

    public static void Begin(object owner, float worldScale, bool slowPlayer)
    {
        if (owner == null)
            owner = LegacyOwner;

        RestorePhysicsGravityIfNeeded();
        FixLeftoverScaledGravityScales();

        float scale = Mathf.Clamp(worldScale, 0f, 1f);
        int idx = IndexOf(owner);
        if (idx >= 0)
        {
            Layer layer = layers[idx];
            layer.Scale = scale;
            layer.SlowPlayer = slowPlayer;
            layers[idx] = layer;
        }
        else
        {
            layers.Add(new Layer
            {
                Owner = owner,
                Scale = scale,
                SlowPlayer = slowPlayer
            });
        }

        Recompute();
    }

    /// <summary>Legacy End for anonymous Begin. Prefer <see cref="End(object)"/>.</summary>
    public static void End() => End(LegacyOwner);

    public static void End(object owner)
    {
        if (owner == null)
            owner = LegacyOwner;

        int idx = IndexOf(owner);
        if (idx < 0)
            return;

        layers.RemoveAt(idx);
        RestorePhysicsGravityIfNeeded();
        Recompute();
    }

    private static int IndexOf(object owner)
    {
        for (int i = 0; i < layers.Count; i++)
        {
            if (ReferenceEquals(layers[i].Owner, owner))
                return i;
        }

        return -1;
    }

    private static void Recompute()
    {
        if (layers.Count == 0)
        {
            IsActive = false;
            WorldTimeScale = 1f;
            SlowsPlayer = false;
            return;
        }

        IsActive = true;
        float minScale = 1f;
        bool anySlowPlayer = false;
        for (int i = 0; i < layers.Count; i++)
        {
            minScale = Mathf.Min(minScale, layers[i].Scale);
            if (layers[i].SlowPlayer)
                anySlowPlayer = true;
        }

        // 0 = full freeze (Bass QuickTime Time Stop); Double Gear / Hyper still use mid scales.
        WorldTimeScale = minScale;
        SlowsPlayer = anySlowPlayer;
    }

    private static void RestorePhysicsGravityIfNeeded()
    {
        // GravityScale is never rewritten by this system (avoids lift-off). Intentional no-op.
    }

    private static void FixLeftoverScaledGravityScales()
    {
        // Intentional no-op — see RestorePhysicsGravityIfNeeded.
    }

    public static float WorldDeltaTime =>
        IsActive ? Time.deltaTime * WorldTimeScale : Time.deltaTime;

    public static float WorldFixedDeltaTime =>
        IsActive ? Time.fixedDeltaTime * WorldTimeScale : Time.fixedDeltaTime;

    public static float ScaleSpeed(float speed) =>
        IsActive ? speed * WorldTimeScale : speed;

    /// <summary>
    /// Softens player fall while Hyper Slow is active (can partially counter gravity below 1).
    /// </summary>
    public static float ScaleFallMultiplier(float fallMultiplier)
    {
        if (!IsActive)
            return fallMultiplier;
        return Mathf.Lerp(0.25f, fallMultiplier, Mathf.Clamp01(WorldTimeScale));
    }

    /// <summary>Same fall soften for bosses / common enemies.</summary>
    public static float ScaleEnemyFallMultiplier(float fallMultiplier) =>
        ScaleFallMultiplier(fallMultiplier);
}
