using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Runs in boss scenes: return to HomeTown when the boss dies, or when the player
/// loses their last life. Mid-fight deaths with lives left respawn in the boss scene.
/// Place one in Boss Fight Mode (or it auto-adds via sceneLoaded).
/// </summary>
public class BossFightDirector : MonoBehaviour
{
    [SerializeField] private string homeTownSceneName = "HomeTown";
    [SerializeField] private float fadeOutSeconds = 0.45f;
    [SerializeField] private float fadeInSeconds = 0.45f;
    [SerializeField] private float returnDelayAfterBossDeath = 0.75f;
    [SerializeField] private float respawnDelayAfterDeath = 0.35f;

    private bool flowBusy;
    private PlayerController watchedPlayer;
    private bool bossDeathHandled;
    private bool playerDeathHandled;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneHook()
    {
        SceneManager.sceneLoaded -= EnsureOnSceneLoaded;
        SceneManager.sceneLoaded += EnsureOnSceneLoaded;
    }

    private static void EnsureOnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnsureForScene(scene.name);
    }

    private static void EnsureForScene(string sceneName)
    {
        if (!IsBossFightScene(sceneName))
            return;

        if (FindFirstObjectByType<BossSpawner>() == null)
        {
            GameObject spawnerGo = new GameObject("BossSpawner");
            spawnerGo.AddComponent<BossSpawner>();
        }

        if (FindFirstObjectByType<BossFightDirector>() == null)
        {
            GameObject go = new GameObject("BossFightDirector");
            go.AddComponent<BossFightDirector>();
        }

        EnsureCrystalComponent();
    }

    private static void EnsureCrystalComponent()
    {
        Crystal existing = FindFirstObjectByType<Crystal>();
        if (existing != null)
            return;

        GameObject crystalGo = GameObject.Find("Crystal");
        if (crystalGo != null)
            crystalGo.AddComponent<Crystal>();
    }

    private static bool IsBossFightScene(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
            return false;

        return sceneName.IndexOf("Boss Fight", StringComparison.OrdinalIgnoreCase) >= 0 ||
               sceneName.IndexOf("BossFight", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void OnEnable()
    {
        bossDeathHandled = false;
        playerDeathHandled = false;
        flowBusy = false;
        BindBosses();
        BindPlayer();
    }

    private void OnDisable()
    {
        UnbindPlayer();
        UnbindBosses();
        StopAllCoroutines();
    }

    private void Start()
    {
        StartCoroutine(RebindAfterSceneReady());
    }

    private void Update()
    {
        // Belt-and-suspenders: catch deaths even if an event subscription was missed.
        if (!flowBusy && !bossDeathHandled)
        {
            Boss[] bosses = FindObjectsByType<Boss>(FindObjectsSortMode.None);
            for (int i = 0; i < bosses.Length; i++)
            {
                if (bosses[i] != null && bosses[i].IsDead)
                {
                    HandleBossDied();
                    break;
                }
            }
        }

        if (!flowBusy && !playerDeathHandled && watchedPlayer != null && watchedPlayer.IsDead)
            HandlePlayerDied();
    }

    private IEnumerator RebindAfterSceneReady()
    {
        yield return null;
        BindBosses();
        BindPlayer();
        yield return null;
        BindBosses();
        BindPlayer();
    }

    private void BindBosses()
    {
        UnbindBosses();
        Boss[] bosses = FindObjectsByType<Boss>(FindObjectsSortMode.None);
        for (int i = 0; i < bosses.Length; i++)
        {
            if (bosses[i] != null)
                bosses[i].OnDied += HandleBossDied;
        }
    }

    private void UnbindBosses()
    {
        Boss[] bosses = FindObjectsByType<Boss>(FindObjectsSortMode.None);
        for (int i = 0; i < bosses.Length; i++)
        {
            if (bosses[i] != null)
                bosses[i].OnDied -= HandleBossDied;
        }
    }

    private void BindPlayer()
    {
        UnbindPlayer();

        if (PlayerSpawner.Instance != null)
            PlayerSpawner.Instance.OnPlayerSpawned += HandlePlayerSpawned;

        WatchPlayer(FindActivePlayer());
    }

    private void UnbindPlayer()
    {
        if (PlayerSpawner.Instance != null)
            PlayerSpawner.Instance.OnPlayerSpawned -= HandlePlayerSpawned;

        if (watchedPlayer != null)
        {
            watchedPlayer.OnDied -= HandlePlayerDied;
            watchedPlayer = null;
        }
    }

    private void HandlePlayerSpawned(PlayerController player)
    {
        playerDeathHandled = false;
        WatchPlayer(player);
    }

    private void WatchPlayer(PlayerController player)
    {
        if (watchedPlayer != null)
            watchedPlayer.OnDied -= HandlePlayerDied;

        watchedPlayer = player;
        if (watchedPlayer != null)
            watchedPlayer.OnDied += HandlePlayerDied;
    }

    private void HandleBossDied()
    {
        if (flowBusy || bossDeathHandled)
            return;

        Boss defeated = FindDefeatedBoss();

        // Crystal still has HP → drain it and respawn the boss instead of ending the fight.
        if (Crystal.Instance != null &&
            defeated != null &&
            Crystal.Instance.TryHandleBossDeath(defeated))
        {
            flowBusy = true;
            StartCoroutine(CrystalBossRespawnFlow(defeated));
            return;
        }

        bossDeathHandled = true;
        flowBusy = true;
        StartCoroutine(ReturnHomeAfterDelay(returnDelayAfterBossDeath));
    }

    private IEnumerator CrystalBossRespawnFlow(Boss defeated)
    {
        if (Crystal.Instance != null)
            yield return Crystal.Instance.RespawnBossAfterDeath(defeated);

        // If the death penalty emptied the crystal during/after the wait, go home.
        if (Crystal.Instance == null || !Crystal.Instance.AllowsBossRespawn)
        {
            bossDeathHandled = true;
            ReturnToHomeTown();
            yield break;
        }

        flowBusy = false;
        bossDeathHandled = false;
        BindBosses();
    }

    private static Boss FindDefeatedBoss()
    {
        Boss[] bosses = FindObjectsByType<Boss>(FindObjectsSortMode.None);
        for (int i = 0; i < bosses.Length; i++)
        {
            if (bosses[i] != null && bosses[i].IsDead)
                return bosses[i];
        }

        return bosses.Length > 0 ? bosses[0] : null;
    }

    private void HandlePlayerDied()
    {
        if (flowBusy || playerDeathHandled)
            return;

        playerDeathHandled = true;
        flowBusy = true;

        PlayerInventory inv = PlayerInventory.Instance;
        if (inv != null)
            inv.TryLoseLife(1);

        int livesLeft = inv != null ? inv.LivesCount : 0;
        if (livesLeft <= 0)
        {
            if (inv != null)
                inv.ResetLivesToStarting();
            StartCoroutine(ReturnHomeAfterDelay(respawnDelayAfterDeath));
            return;
        }

        StartCoroutine(RespawnAfterDelay(respawnDelayAfterDeath));
    }

    private IEnumerator ReturnHomeAfterDelay(float delay)
    {
        if (delay > 0f)
            yield return new WaitForSecondsRealtime(delay);

        // Wait until Mega Man death balls have traveled far enough from the center.
        yield return VisualEffects.WaitForDeathBallsTravel(VisualEffects.ActiveDeathBurst);

        ReturnToHomeTown();
    }

    private IEnumerator RespawnAfterDelay(float delay)
    {
        if (delay > 0f)
            yield return new WaitForSecondsRealtime(delay);

        RespawnInBossScene();
    }

    private void RespawnInBossScene()
    {
        if (PlayerSpawner.Instance != null)
            PlayerSpawner.Instance.RespawnAtSpawnPoint();
        else
        {
            PlayerController player = FindActivePlayer();
            if (player != null)
                player.SetHealth(player.MaxHealth);
        }

        playerDeathHandled = false;
        flowBusy = false;
        BindPlayer();
    }

    private void ReturnToHomeTown()
    {
        string scene = string.IsNullOrWhiteSpace(homeTownSceneName) ? "HomeTown" : homeTownSceneName;
        ScreenFade.EnsureExists().LoadScene(scene, fadeOutSeconds, fadeInSeconds);
        StartCoroutine(HardFallbackLoad(scene));
    }

    private IEnumerator HardFallbackLoad(string scene)
    {
        // If fade/queue somehow stalls, force the load so the player is never stuck.
        yield return new WaitForSecondsRealtime(Mathf.Max(1.25f, fadeOutSeconds + 1f));
        if (IsBossFightScene(SceneManager.GetActiveScene().name))
            SceneManager.LoadScene(scene);
    }

    private static PlayerController FindActivePlayer()
    {
        if (PlayerSpawner.Instance != null && PlayerSpawner.Instance.CurrentPlayer != null)
            return PlayerSpawner.Instance.CurrentPlayer;

        return FindFirstObjectByType<PlayerController>();
    }
}
