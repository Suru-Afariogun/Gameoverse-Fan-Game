using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Remembers common stage enemies placed in the scene and reinstantiates them when the player respawns.
/// Auto-adds to the scene's <see cref="PlayerSpawner"/> (see Player Spawner prefab or sceneLoaded bootstrap).
/// </summary>
public class SceneEnemyRespawner : MonoBehaviour
{
    [Serializable]
    public class CommonEnemyPrefabEntry
    {
        [Tooltip("Matches the enemy component type name, e.g. CrankyClanky.")]
        public string enemyKey = "CrankyClanky";

        public GameObject prefab;
    }

    private static readonly (string key, string assetPath)[] DefaultPrefabPaths =
    {
        ("CrankyClanky", "Assets/Prefabs/Cranky Clanky/Cranky Clanky.prefab"),
        ("LaserBot", "Assets/Prefabs/Enemies/LaserBot/Laser Bot.prefab"),
        ("BlockerBot", "Assets/Prefabs/Enemies/BlockerBot/Blocker Bot.prefab"),
        ("ChaoticTanker", "Assets/Prefabs/Enemies/Chaotic Tanker Enemy/Chaotic Tanker enemy.prefab"),
        ("ScrapNit", "Assets/Prefabs/Enemies/ScrapNit Enemy/ScrapNit enemy.prefab"),
    };

    [SerializeField] private CommonEnemyPrefabEntry[] commonEnemyPrefabs = new CommonEnemyPrefabEntry[]
    {
        new CommonEnemyPrefabEntry { enemyKey = "CrankyClanky" },
        new CommonEnemyPrefabEntry { enemyKey = "LaserBot" },
        new CommonEnemyPrefabEntry { enemyKey = "BlockerBot" },
        new CommonEnemyPrefabEntry { enemyKey = "ChaoticTanker" },
        new CommonEnemyPrefabEntry { enemyKey = "ScrapNit" },
    };

    private readonly List<EnemySpawnRecord> spawnRecords = new List<EnemySpawnRecord>();

    private struct EnemySpawnRecord
    {
        public GameObject prefab;
        public Vector3 position;
        public Quaternion rotation;
        public Transform parent;
        public string objectName;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneHook()
    {
        SceneManager.sceneLoaded -= EnsureOnSceneLoaded;
        SceneManager.sceneLoaded += EnsureOnSceneLoaded;
    }

    private static void EnsureOnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (ShouldSkipScene(scene.name))
            return;

        PlayerSpawner spawner = FindFirstObjectByType<PlayerSpawner>();
        if (spawner == null)
            return;

        if (spawner.GetComponent<SceneEnemyRespawner>() == null)
            spawner.gameObject.AddComponent<SceneEnemyRespawner>();
    }

    private static bool ShouldSkipScene(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
            return true;

        return sceneName.IndexOf("HomeTown", StringComparison.OrdinalIgnoreCase) >= 0
            || sceneName.IndexOf("Home Town", StringComparison.OrdinalIgnoreCase) >= 0
            || sceneName.IndexOf("Start Screen", StringComparison.OrdinalIgnoreCase) >= 0
            || sceneName.IndexOf("StartScreen", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void Awake()
    {
        EnsureDefaultPrefabsAssigned();
    }

    private void OnEnable()
    {
        if (PlayerSpawner.Instance != null)
        {
            PlayerSpawner.Instance.OnPlayerRespawned -= HandlePlayerRespawned;
            PlayerSpawner.Instance.OnPlayerRespawned += HandlePlayerRespawned;
        }
    }

    private void Start()
    {
        CaptureSceneEnemies();

        if (PlayerSpawner.Instance != null)
        {
            PlayerSpawner.Instance.OnPlayerRespawned -= HandlePlayerRespawned;
            PlayerSpawner.Instance.OnPlayerRespawned += HandlePlayerRespawned;
        }
    }

    private void OnDisable()
    {
        if (PlayerSpawner.Instance != null)
            PlayerSpawner.Instance.OnPlayerRespawned -= HandlePlayerRespawned;
    }

    private void HandlePlayerRespawned(PlayerController player)
    {
        RespawnAllEnemies();
    }

    /// <summary>Destroy live common enemies and recreate every recorded spawn.</summary>
    public void RespawnAllEnemies()
    {
        if (spawnRecords.Count == 0)
            CaptureSceneEnemies();

        if (spawnRecords.Count == 0)
            return;

        ClearLiveCommonEnemies();

        for (int i = 0; i < spawnRecords.Count; i++)
        {
            EnemySpawnRecord record = spawnRecords[i];
            if (record.prefab == null)
                continue;

            GameObject instance = Instantiate(record.prefab, record.position, record.rotation, record.parent);
            if (!string.IsNullOrWhiteSpace(record.objectName))
                instance.name = record.objectName;
        }
    }

    private void CaptureSceneEnemies()
    {
        spawnRecords.Clear();
        EnsureDefaultPrefabsAssigned();

        MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null || behaviour is not ICommonEnemy)
                continue;

            GameObject prefab = ResolvePrefab(behaviour);
            if (prefab == null)
            {
                Debug.LogWarning(
                    $"[SceneEnemyRespawner] No respawn prefab for '{behaviour.GetType().Name}' on '{behaviour.name}'. Assign it on SceneEnemyRespawner.",
                    behaviour);
                continue;
            }

            Transform t = behaviour.transform;
            spawnRecords.Add(new EnemySpawnRecord
            {
                prefab = prefab,
                position = t.position,
                rotation = t.rotation,
                parent = t.parent,
                objectName = behaviour.gameObject.name
            });
        }
    }

    private GameObject ResolvePrefab(MonoBehaviour enemyBehaviour)
    {
        if (enemyBehaviour == null)
            return null;

        string key = enemyBehaviour.GetType().Name;

        // 1) Explicit mapping on this component (preferred for builds).
        GameObject mapped = FindMappedPrefab(key);
        if (mapped != null)
            return mapped;

#if UNITY_EDITOR
        // 2) Prefab instance in the scene → use the asset it came from (correct type).
        GameObject source = UnityEditor.PrefabUtility.GetCorrespondingObjectFromOriginalSource(enemyBehaviour.gameObject);
        if (source != null)
            return source;

        source = UnityEditor.PrefabUtility.GetCorrespondingObjectFromSource(enemyBehaviour.gameObject);
        if (source != null)
            return source;

        // 3) Known default asset paths.
        for (int i = 0; i < DefaultPrefabPaths.Length; i++)
        {
            if (!string.Equals(DefaultPrefabPaths[i].key, key, StringComparison.OrdinalIgnoreCase))
                continue;

            GameObject loaded = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(DefaultPrefabPaths[i].assetPath);
            if (loaded != null)
                return loaded;
        }
#endif

        return null;
    }

    private GameObject FindMappedPrefab(string enemyKey)
    {
        if (commonEnemyPrefabs == null || string.IsNullOrWhiteSpace(enemyKey))
            return null;

        for (int i = 0; i < commonEnemyPrefabs.Length; i++)
        {
            CommonEnemyPrefabEntry entry = commonEnemyPrefabs[i];
            if (entry == null || entry.prefab == null || string.IsNullOrWhiteSpace(entry.enemyKey))
                continue;

            if (string.Equals(entry.enemyKey.Trim(), enemyKey, StringComparison.OrdinalIgnoreCase))
                return entry.prefab;
        }

        return null;
    }

    private static void ClearLiveCommonEnemies()
    {
        MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null || behaviour is not ICommonEnemy)
                continue;

            Destroy(behaviour.gameObject);
        }
    }

    private void EnsureDefaultPrefabsAssigned()
    {
        Dictionary<string, CommonEnemyPrefabEntry> byKey =
            new Dictionary<string, CommonEnemyPrefabEntry>(StringComparer.OrdinalIgnoreCase);

        if (commonEnemyPrefabs != null)
        {
            for (int i = 0; i < commonEnemyPrefabs.Length; i++)
            {
                CommonEnemyPrefabEntry entry = commonEnemyPrefabs[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.enemyKey))
                    continue;

                string key = entry.enemyKey.Trim();
                if (!byKey.ContainsKey(key))
                    byKey[key] = entry;
            }
        }

        for (int i = 0; i < DefaultPrefabPaths.Length; i++)
        {
            string key = DefaultPrefabPaths[i].key;
            if (!byKey.TryGetValue(key, out CommonEnemyPrefabEntry entry) || entry == null)
            {
                entry = new CommonEnemyPrefabEntry { enemyKey = key };
                byKey[key] = entry;
            }

#if UNITY_EDITOR
            if (entry.prefab == null)
            {
                entry.prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                    DefaultPrefabPaths[i].assetPath);
            }
#endif
        }

        commonEnemyPrefabs = new CommonEnemyPrefabEntry[byKey.Count];
        int index = 0;
        foreach (KeyValuePair<string, CommonEnemyPrefabEntry> pair in byKey)
            commonEnemyPrefabs[index++] = pair.Value;
    }
}
