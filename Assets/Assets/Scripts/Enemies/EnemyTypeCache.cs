using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One FindObjectsByType pass per frame for common enemy/hazard types.
/// Stops N enemies each scanning the whole scene during load / enable.
/// </summary>
public static class EnemyTypeCache
{
    private static int cachedFrame = -1;
    private static int cachedSceneHandle = int.MinValue;

    private static CrankyClanky[] crankies = Array.Empty<CrankyClanky>();
    private static LaserBot[] lasers = Array.Empty<LaserBot>();
    private static BlockerBot[] blockers = Array.Empty<BlockerBot>();
    private static ChaoticTanker[] tankers = Array.Empty<ChaoticTanker>();
    private static ScrapNit[] scraps = Array.Empty<ScrapNit>();
    private static Boss[] bosses = Array.Empty<Boss>();
    private static CheckPointBot[] checkPoints = Array.Empty<CheckPointBot>();
    private static ShredderBlade[] shredders = Array.Empty<ShredderBlade>();
    private static MovingFactoryPlatform[] movers = Array.Empty<MovingFactoryPlatform>();
    private static SlimFactoryPlatform[] slims = Array.Empty<SlimFactoryPlatform>();

    public static CrankyClanky[] Crankies { get { Ensure(); return crankies; } }
    public static LaserBot[] Lasers { get { Ensure(); return lasers; } }
    public static BlockerBot[] Blockers { get { Ensure(); return blockers; } }
    public static ChaoticTanker[] Tankers { get { Ensure(); return tankers; } }
    public static ScrapNit[] Scraps { get { Ensure(); return scraps; } }
    public static Boss[] Bosses { get { Ensure(); return bosses; } }
    public static CheckPointBot[] CheckPoints { get { Ensure(); return checkPoints; } }
    public static ShredderBlade[] Shredders { get { Ensure(); return shredders; } }
    public static MovingFactoryPlatform[] Movers { get { Ensure(); return movers; } }
    public static SlimFactoryPlatform[] Slims { get { Ensure(); return slims; } }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ResetOnLoad()
    {
        Invalidate();
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Invalidate();
    }

    public static void Invalidate()
    {
        cachedFrame = -1;
        cachedSceneHandle = int.MinValue;
    }

    private static void Ensure()
    {
        int frame = Time.frameCount;
        int sceneHandle = SceneManager.GetActiveScene().handle;
        if (cachedFrame == frame && cachedSceneHandle == sceneHandle)
            return;

        cachedFrame = frame;
        cachedSceneHandle = sceneHandle;

        crankies = UnityEngine.Object.FindObjectsByType<CrankyClanky>(FindObjectsSortMode.None);
        lasers = UnityEngine.Object.FindObjectsByType<LaserBot>(FindObjectsSortMode.None);
        blockers = UnityEngine.Object.FindObjectsByType<BlockerBot>(FindObjectsSortMode.None);
        tankers = UnityEngine.Object.FindObjectsByType<ChaoticTanker>(FindObjectsSortMode.None);
        scraps = UnityEngine.Object.FindObjectsByType<ScrapNit>(FindObjectsSortMode.None);
        bosses = UnityEngine.Object.FindObjectsByType<Boss>(FindObjectsSortMode.None);
        checkPoints = UnityEngine.Object.FindObjectsByType<CheckPointBot>(FindObjectsSortMode.None);
        shredders = UnityEngine.Object.FindObjectsByType<ShredderBlade>(FindObjectsSortMode.None);
        movers = UnityEngine.Object.FindObjectsByType<MovingFactoryPlatform>(FindObjectsSortMode.None);
        slims = UnityEngine.Object.FindObjectsByType<SlimFactoryPlatform>(FindObjectsSortMode.None);
    }
}

/// <summary>
/// One Collider2D scene scan per frame for detection-zone / phase ignore setup.
/// </summary>
public static class SceneColliderCache
{
    private static int cachedFrame = -1;
    private static int cachedSceneHandle = int.MinValue;
    private static Collider2D[] colliders = Array.Empty<Collider2D>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Hook()
    {
        Invalidate();
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Invalidate();
    }

    public static void Invalidate()
    {
        cachedFrame = -1;
        cachedSceneHandle = int.MinValue;
        colliders = Array.Empty<Collider2D>();
    }

    public static Collider2D[] GetAllIncludeInactive()
    {
        int frame = Time.frameCount;
        int sceneHandle = SceneManager.GetActiveScene().handle;
        if (cachedFrame == frame && cachedSceneHandle == sceneHandle && colliders != null && colliders.Length > 0)
            return colliders;

        cachedFrame = frame;
        cachedSceneHandle = sceneHandle;
        colliders = UnityEngine.Object.FindObjectsByType<Collider2D>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        return colliders;
    }
}
