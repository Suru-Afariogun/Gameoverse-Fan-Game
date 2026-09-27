using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// In the boss scene: keep / spawn the boss matching <see cref="BossEncounter.SelectedBossId"/>.
/// Unready bosses (e.g. Kit until BossKit exists) destroy scene bosses and return to HomeTown.
/// </summary>
public class BossSpawner : MonoBehaviour
{
    [Serializable]
    public class BossEntry
    {
        [Tooltip("Matches Kaboodle boss buttons: Kit, Malice, …")]
        public string bossId = "Malice";

        [Tooltip("Optional. If null, a matching boss already placed in the scene is kept.")]
        public Boss prefab;
    }

    [Header("Bosses")]
    [SerializeField] private BossEntry[] bosses = new BossEntry[]
    {
        new BossEntry { bossId = "Malice" },
        new BossEntry { bossId = "Kit" }
    };

    [Header("Spawn")]
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private bool spawnOnAwake = true;
    [SerializeField] private bool destroyMismatchedBossesOnStart = true;
    [SerializeField] private string homeTownSceneName = "HomeTown";

    private bool pendingReturnHome;
    private bool autoSpawnDeferred;

    /// <summary>World position used for boss spawn / mid-fight retry resets.</summary>
    public Vector3 GetSpawnWorldPosition()
    {
        if (spawnPoint != null)
            return spawnPoint.position;

        Transform child = transform.Find("SpawnPoint");
        if (child != null)
            return child.position;

        return transform.position;
    }

    private void Awake()
    {
        // Level gates: wait until the player passes a BossBattleStarter.
        // Level one door encounter also defers until the room closes.
        if (FindFirstObjectByType<BossBattleStarter>() != null)
            DeferAutoSpawn();
        else if (LevelOneBossEncounter.IsCopyBotEncounterScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name))
            DeferAutoSpawn();

        if (autoSpawnDeferred || !spawnOnAwake)
            return;

        ResolveSelectedBoss();
    }

    /// <summary>Level one: wait for the boss-room door to close before spawning the copy bot.</summary>
    public void DeferAutoSpawn()
    {
        autoSpawnDeferred = true;
        spawnOnAwake = false;
        DestroyAllSceneBosses();
    }

    /// <summary>
    /// Called by <see cref="BossBattleStarter"/> (or other level gates) to spawn the
    /// selected boss after auto-spawn was deferred.
    /// </summary>
    public void SpawnSelectedBossNow()
    {
        autoSpawnDeferred = false;
        spawnOnAwake = false;
        ResolveSelectedBoss();
    }

    /// <summary>
    /// Spawns a gray copy-bot version of the current player character (fallback: Boss Kit).
    /// </summary>
    public Boss SpawnCopyBotForPlayer(int maxHealth = 40)
    {
        string characterId = ResolvePlayerCharacterId();
        Boss prefab = FindCopyBotPrefab(characterId);
        if (prefab == null)
        {
            Debug.LogWarning($"[BossSpawner] No copy-bot prefab for '{characterId}'.");
            return null;
        }

        Vector3 pos = GetSpawnWorldPosition();
        Quaternion rot = spawnPoint != null ? spawnPoint.rotation : transform.rotation;
        Boss instance = Instantiate(prefab, pos, rot);
        instance.ConfigureAsCopyBot(maxHealth);
        return instance;
    }

    /// <summary>
    /// Spawns a 1-HP decoy copy-bot that only chases and stuns (no attacks).
    /// </summary>
    public Boss SpawnCopyBotDecoyNear(Vector3 position, int maxHealth = 1)
    {
        string characterId = ResolvePlayerCharacterId();
        Boss prefab = FindCopyBotPrefab(characterId);
        if (prefab == null)
            return null;

        Boss instance = Instantiate(prefab, position, prefab.transform.rotation);
        instance.ConfigureAsCopyBotDecoy(maxHealth);
        return instance;
    }

    private static string ResolvePlayerCharacterId()
    {
        if (PlayerSpawner.Instance != null && PlayerSpawner.Instance.CurrentPlayer != null)
            return PlayerSpawner.Instance.CurrentPlayer.CharacterId;

        if (!string.IsNullOrWhiteSpace(PlayerSpawner.SelectedCharacterId))
            return PlayerSpawner.SelectedCharacterId;

        return BossEncounter.BossIdKit;
    }

    /// <summary>
    /// Boss Fight Mode rival: Kit fights Malice; everyone else fights Kit.
    /// </summary>
    public static string GetBossFightOpponentBossId()
    {
        string playerId = ResolvePlayerCharacterId();
        if (string.Equals(playerId, BossEncounter.BossIdKit, StringComparison.OrdinalIgnoreCase))
            return BossEncounter.BossIdMalice;

        return BossEncounter.BossIdKit;
    }

    private Boss FindCopyBotPrefab(string characterId)
    {
        if (string.IsNullOrWhiteSpace(characterId))
            characterId = BossEncounter.BossIdKit;

        string id = characterId.Trim();
        if (string.Equals(id, BossEncounter.BossIdMalice, StringComparison.OrdinalIgnoreCase))
        {
            BossEntry malice = FindEntry(BossEncounter.BossIdMalice);
            if (malice != null && malice.prefab != null)
                return malice.prefab;
        }

        BossEntry kit = FindEntry(BossEncounter.BossIdKit);
        return kit != null ? kit.prefab : null;
    }

    private void Start()
    {
        if (pendingReturnHome)
            StartCoroutine(ReturnHomeWhenFadeReady());
    }

    private IEnumerator ReturnHomeWhenFadeReady()
    {
        // Wait until the enter-fade finishes so LoadScene is not dropped while busy.
        yield return null;
        while (ScreenFade.IsBusy)
            yield return null;

        string scene = string.IsNullOrWhiteSpace(homeTownSceneName) ? "HomeTown" : homeTownSceneName;
        ScreenFade.EnsureExists().LoadScene(scene);
    }

    private void ResolveSelectedBoss()
    {
        string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        string selectedId = BossEncounter.SelectedBossId;

        if (BossFightDirector.IsBossFightScene(sceneName))
        {
            if (BossEncounter.ForcePlayerRivalBoss || !BossEncounter.BossChosenFromKaboodle)
                selectedId = GetBossFightOpponentBossId();

            BossEncounter.ClearBossFightRoutingFlags();
        }

        if (!BossEncounter.IsBossReady(selectedId))
        {
            Debug.LogWarning(
                $"[BossSpawner] Boss '{selectedId}' is not ready yet. Returning to HomeTown.");
            DestroyAllSceneBosses();
            pendingReturnHome = true;
            return;
        }

        Boss matching = null;
        Boss[] sceneBosses = FindObjectsByType<Boss>(FindObjectsSortMode.None);
        for (int i = 0; i < sceneBosses.Length; i++)
        {
            Boss boss = sceneBosses[i];
            if (boss == null)
                continue;

            if (BossEncounter.MatchesBoss(boss, selectedId))
            {
                matching = boss;
                continue;
            }

            if (destroyMismatchedBossesOnStart)
                Destroy(boss.gameObject);
        }

        if (matching != null)
            return;

        BossEntry entry = FindEntry(selectedId);
        if (entry != null && entry.prefab != null)
        {
            Vector3 pos = spawnPoint != null ? spawnPoint.position : transform.position;
            Quaternion rot = spawnPoint != null ? spawnPoint.rotation : Quaternion.identity;
            Instantiate(entry.prefab, pos, rot);
            return;
        }

        Debug.LogWarning(
            $"[BossSpawner] No scene boss and no prefab for '{selectedId}'. Returning to HomeTown.");
        pendingReturnHome = true;
    }

    private BossEntry FindEntry(string bossId)
    {
        if (bosses == null || string.IsNullOrWhiteSpace(bossId))
            return null;

        for (int i = 0; i < bosses.Length; i++)
        {
            BossEntry entry = bosses[i];
            if (entry == null || string.IsNullOrWhiteSpace(entry.bossId))
                continue;

            if (string.Equals(entry.bossId.Trim(), bossId.Trim(), StringComparison.OrdinalIgnoreCase))
                return entry;
        }

        return null;
    }

    private static void DestroyAllSceneBosses()
    {
        Boss[] sceneBosses = FindObjectsByType<Boss>(FindObjectsSortMode.None);
        for (int i = 0; i < sceneBosses.Length; i++)
        {
            if (sceneBosses[i] != null)
                Destroy(sceneBosses[i].gameObject);
        }
    }
}
