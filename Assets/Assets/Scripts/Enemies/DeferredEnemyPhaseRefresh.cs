using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Batches enemy phase IgnoreCollision setup across a few LateUpdate frames
/// so scene load does not hitch on one giant O(N) pass.
/// </summary>
[DefaultExecutionOrder(1000)]
public sealed class DeferredEnemyPhaseRefresh : MonoBehaviour
{
    private static DeferredEnemyPhaseRefresh instance;
    private static bool queued;
    private static bool batchRunning;
    private static int step;

    public static bool IsBatchRunning => batchRunning;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureExists()
    {
        if (instance == null)
        {
            GameObject go = new GameObject("DeferredEnemyPhaseRefresh");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<DeferredEnemyPhaseRefresh>();
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        queued = true;
        step = 0;
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        queued = true;
        step = 0;
    }

    public static void Request()
    {
        EnsureExists();
        queued = true;
        if (step > 0 && !batchRunning)
            step = 0;
    }

    private void LateUpdate()
    {
        if (!queued && step == 0)
            return;

        queued = false;
        RunStep();
    }

    private static void RunStep()
    {
        batchRunning = true;
        try
        {
            switch (step)
            {
                case 0:
                    EnemyTypeCache.Invalidate();
                    // Detection zones are bounds-only now — cheap configure pass.
                    EnemyDetectionZone[] zones = Object.FindObjectsByType<EnemyDetectionZone>(
                        FindObjectsInactive.Include,
                        FindObjectsSortMode.None);
                    for (int i = 0; i < zones.Length; i++)
                    {
                        if (zones[i] != null)
                            zones[i].RefreshPhaseCollisions();
                    }
                    step = 1;
                    queued = true;
                    break;

                case 1:
                    RefreshAll(EnemyTypeCache.Crankies);
                    RefreshAll(EnemyTypeCache.Lasers);
                    RefreshAll(EnemyTypeCache.Tankers);
                    step = 2;
                    queued = true;
                    break;

                case 2:
                    RefreshAll(EnemyTypeCache.Blockers);
                    RefreshAll(EnemyTypeCache.Scraps);
                    RefreshAll(EnemyTypeCache.CheckPoints);
                    RefreshAll(EnemyTypeCache.Shredders);
                    step = 3;
                    queued = true;
                    break;

                default:
                    Boss[] bosses = EnemyTypeCache.Bosses;
                    for (int i = 0; i < bosses.Length; i++)
                    {
                        if (bosses[i] == null)
                            continue;
                        bosses[i].RefreshPhaseCollisionsWithPlayers();
                        bosses[i].RefreshPhaseCollisionsWithCopyBots();
                    }

                    PlayerController player = PlayerController.ResolveActive();
                    if (player != null)
                        player.RefreshPlayerPhaseCollisions();

                    NPC.RefreshAllPhaseCollisions();

                    BossBattleStarter[] starters = Object.FindObjectsByType<BossBattleStarter>(
                        FindObjectsInactive.Include,
                        FindObjectsSortMode.None);
                    for (int i = 0; i < starters.Length; i++)
                    {
                        if (starters[i] != null)
                            starters[i].RefreshPhaseCollisions();
                    }

                    step = 0;
                    break;
            }
        }
        finally
        {
            batchRunning = false;
        }
    }

    private static void RefreshAll(CrankyClanky[] list)
    {
        for (int i = 0; i < list.Length; i++)
            if (list[i] != null) list[i].RefreshPhaseCollisions();
    }

    private static void RefreshAll(LaserBot[] list)
    {
        for (int i = 0; i < list.Length; i++)
            if (list[i] != null) list[i].RefreshPhaseCollisions();
    }

    private static void RefreshAll(BlockerBot[] list)
    {
        for (int i = 0; i < list.Length; i++)
            if (list[i] != null) list[i].RefreshPhaseCollisions();
    }

    private static void RefreshAll(ChaoticTanker[] list)
    {
        for (int i = 0; i < list.Length; i++)
            if (list[i] != null) list[i].RefreshPhaseCollisions();
    }

    private static void RefreshAll(ScrapNit[] list)
    {
        for (int i = 0; i < list.Length; i++)
            if (list[i] != null) list[i].RefreshPhaseCollisions();
    }

    private static void RefreshAll(CheckPointBot[] list)
    {
        for (int i = 0; i < list.Length; i++)
            if (list[i] != null) list[i].RefreshPhaseCollisions();
    }

    private static void RefreshAll(ShredderBlade[] list)
    {
        for (int i = 0; i < list.Length; i++)
            if (list[i] != null) list[i].RefreshPhaseCollisions();
    }
}
