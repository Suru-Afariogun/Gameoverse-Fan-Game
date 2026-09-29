using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Applies a subtle red wash to world sprites/tilemaps during Count hyper speed (Count excluded).
/// </summary>
public class CountHyperSpeedWorldTint : MonoBehaviour
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

    [SerializeField] [Range(0f, 1f)] private float redTintAmount = 0.28f;

    private readonly List<CachedSprite> cachedSprites = new List<CachedSprite>(256);
    private readonly List<CachedTilemap> cachedTilemaps = new List<CachedTilemap>(32);
    private bool applied;
    private Transform excludeRoot;

    public void Apply(Transform exclude, float tintAmount = -1f)
    {
        if (tintAmount >= 0f)
            redTintAmount = Mathf.Clamp01(tintAmount);

        Release();
        excludeRoot = exclude;
        if (redTintAmount <= 0.001f)
            return;

        Color wash = Color.Lerp(Color.white, new Color(1f, 0.45f, 0.45f, 1f), redTintAmount);

        SpriteRenderer[] allSprites =
            UnityEngine.Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < allSprites.Length; i++)
        {
            SpriteRenderer sr = allSprites[i];
            if (sr == null || !sr.enabled || IsUnderExclude(sr.transform))
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
            UnityEngine.Object.FindObjectsByType<TilemapRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < tilemapRenderers.Length; i++)
        {
            TilemapRenderer tr = tilemapRenderers[i];
            if (tr == null || !tr.enabled || IsUnderExclude(tr.transform))
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

        applied = true;
    }

    public void Release()
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
        excludeRoot = null;
        applied = false;
    }

    private bool IsUnderExclude(Transform t)
    {
        if (excludeRoot == null || t == null)
            return false;
        return t == excludeRoot || t.IsChildOf(excludeRoot);
    }

    private void OnDestroy()
    {
        Release();
    }
}

/// <summary>
/// Slows world actors during Count hyper speed without touching Time.timeScale.
/// Down hyper: world only. Up hyper: world and Count both use scaled deltas / speed.
///
/// Jump/fall: gravity and gravityScale are left alone (avoids lift-off). Descent is softened
/// by reducing the fall multiplier — including below 1 so fall partially counters gravity
/// while airborne only.
/// </summary>
public static class HyperSpeedWorldSlow
{
    public static bool IsActive { get; private set; }
    public static float WorldTimeScale { get; private set; } = 1f;
    public static bool SlowsPlayer { get; private set; }

    public static void Begin(float worldScale, bool slowPlayer)
    {
        RestorePhysicsGravityIfNeeded();
        FixLeftoverScaledGravityScales();

        IsActive = true;
        WorldTimeScale = Mathf.Clamp(worldScale, 0.05f, 1f);
        SlowsPlayer = slowPlayer;
    }

    public static void End()
    {
        RestorePhysicsGravityIfNeeded();
        IsActive = false;
        WorldTimeScale = 1f;
        SlowsPlayer = false;
    }

    private static void RestorePhysicsGravityIfNeeded()
    {
        Vector2 g = Physics2D.gravity;
        if (g.y > -5f || g.y < -40f)
            Physics2D.gravity = new Vector2(0f, -9.81f);
    }

    /// <summary>
    /// Older Hyper Speed builds left gravityScale reduced; snap gameplay actors back.
    /// </summary>
    private static void FixLeftoverScaledGravityScales()
    {
        Rigidbody2D[] found = UnityEngine.Object.FindObjectsByType<Rigidbody2D>(FindObjectsSortMode.None);
        for (int i = 0; i < found.Length; i++)
        {
            Rigidbody2D body = found[i];
            if (body == null || Mathf.Abs(body.gravityScale) < 0.0001f)
                continue;

            bool gameplayActor =
                body.GetComponentInParent<PlayerController>() != null
                || body.GetComponentInParent<Boss>() != null
                || body.GetComponentInParent<CrankyClanky>() != null;

            if (!gameplayActor)
                continue;

            // Authored scales are typically 1; leftover slow values sit under 1.
            if (body.gravityScale > 0.05f && body.gravityScale < 0.95f)
                body.gravityScale = 1f;
        }
    }

    public static float WorldDeltaTime =>
        IsActive ? Time.deltaTime * WorldTimeScale : Time.deltaTime;

    public static float WorldFixedDeltaTime =>
        IsActive ? Time.fixedDeltaTime * WorldTimeScale : Time.fixedDeltaTime;

    /// <summary>Ground / scripted move speeds (linear time scale).</summary>
    public static float ScaleSpeed(float speed) =>
        IsActive ? speed * WorldTimeScale : speed;

    /// <summary>Jump launch unchanged — fall softens via <see cref="ScaleFallMultiplier"/>.</summary>
    public static float ScaleJumpSpeed(float speed) => speed;

    public static float AuthoringGravityY => Mathf.Abs(Physics2D.gravity.y);

    /// <summary>Legacy alias; prefer <see cref="ScaleFallMultiplier"/>.</summary>
    public static float GravityTimeFactor => 1f;

    /// <summary>
    /// Softens Count's fall during Hyper Speed. Values well below 1 cancel a large share of
    /// gravity while falling only. Does not change WorldDeltaTime / ScaleSpeed hooks.
    /// </summary>
    public static float ScaleFallMultiplier(float authoredFallMultiplier)
    {
        if (!IsActive)
            return authoredFallMultiplier;

        // At world scale 0.5 → about -0.75: strong slow descent for Count.
        float softTarget = Mathf.Lerp(-1.75f, 0.15f, WorldTimeScale);
        return Mathf.Min(authoredFallMultiplier, softTarget);
    }

    /// <summary>
    /// Softens enemy/boss falls less than Count so hoppers (esp. Cranky Clanky) don't hover.
    /// </summary>
    public static float ScaleEnemyFallMultiplier(float authoredFallMultiplier, bool hoppingEnemy = false)
    {
        if (!IsActive)
            return authoredFallMultiplier;

        // Crankies: barely soften (still slightly floaty, not endless hang).
        // Other enemies/bosses: mild soften between player and Cranky.
        float softTarget = hoppingEnemy
            ? Mathf.Lerp(1.05f, 1.6f, WorldTimeScale)
            : Mathf.Lerp(0.45f, 1.1f, WorldTimeScale);
        return Mathf.Min(authoredFallMultiplier, softTarget);
    }

    /// <summary>True when this body should get slowed fall (not Count on down-hyper).</summary>
    public static bool ShouldSlowFall(Rigidbody2D body)
    {
        if (!IsActive || body == null)
            return false;

        if (SlowsPlayer)
            return true;

        CountPlayerController count = body.GetComponent<CountPlayerController>();
        if (count == null)
            count = body.GetComponentInParent<CountPlayerController>();
        return count == null;
    }
}
