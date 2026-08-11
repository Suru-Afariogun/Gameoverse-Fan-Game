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
    [SerializeField] private bool destroyMismatchedBossesOnStart = true;
    [SerializeField] private string homeTownSceneName = "HomeTown";

    private bool pendingReturnHome;

    private void Awake()
    {
        ResolveSelectedBoss();
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
        string selectedId = BossEncounter.SelectedBossId;

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
